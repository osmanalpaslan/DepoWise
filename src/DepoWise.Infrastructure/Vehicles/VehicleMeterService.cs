using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Vehicles;

/// <summary>
/// ═══ FAZ 4.1 (2026-09-06) — ARAÇ SAYACININ GERÇEK KAYITLARDAN YENİDEN HESAPLANMASI ═══
///
/// <b>ÇÖZÜLEN GERÇEK OLAY.</b> Kullanıcı (mustafa.alpaslan) bir araca yakıt dağıtımından YANLIŞ ve
/// çok yüksek bir sayaç girdi, sonra o kaydı DÜZELTTİ. Buna rağmen araç hâlâ hatalı sayacı
/// gösterdi ve yeni yakıt fişinde başlangıç sayacı kilitli geldiği için kullanıcı revize edemedi.
///
/// <b>KÖK NEDEN.</b> <c>vehicles.current_meter</c> "yalnız ileri giden" bir SAKLI değerdi
/// (<see cref="MeterRule.ShouldAdvance"/> + iptal yolunda "sayaç geri alınmaz" kuralı Y2).
/// Yani hatalı-yüksek değer bir kez yazıldıktan sonra:
/// <list type="bullet">
///   <item>kaydı düzeltmek onu geri almıyordu (düzeltme = iptal + yeni kayıt; iptal sayaca dokunmuyor),</item>
///   <item>kaydı iptal etmek de geri almıyordu,</item>
///   <item>araç formundan elle düşürmek <c>MeterBackwardException</c> ile engelleniyordu.</item>
/// </list>
/// Değer kalıcı olarak "zehirleniyordu"; bakım hedefi, uyarılar ve yeni fişin başlangıç sayacı
/// bu yanlış değerden besleniyordu.
///
/// <b>DÜZELTME.</b> Sayaç artık <b>türetilmiş</b> bir değerdir: aracın GEÇERLİ (iptal/silinmemiş)
/// kayıtlarındaki EN YÜKSEK sayaç. Kullanıcının en baştaki kurgusu da buydu:
/// <i>"projeyi en yüksek sayaç bilgisi hangi kayıtta ise ondan al"</i>.
/// Kaynak kayıt iptal edilince veya düzeltilince değer <b>aşağı da inebilir</b> — çünkü artık
/// gerçeği yansıtan bir özet olur, ayrı bir "en yüksek gördüğüm değer" hafızası değil.
///
/// <b>KAYBOLMAYAN ŞEY.</b> Elle beyan edilen sayaç (araç kartı açılışı / araç formu) taban olarak
/// korunur: <c>vehicle_meter_logs</c> içinde <c>vehicle_create</c> / <c>vehicle_form</c> kaynaklı
/// satırların en büyüğü hesaba KATILIR. Böylece hiç yakıt/bakım kaydı olmayan bir araçta sayaç
/// sıfıra düşmez. Hesap yalnız <b>operasyonel</b> kaynakları (iptal edilebilen kayıtları) geri alır.
///
/// <b>SENKRON.</b> Bu sınıf yeni bir senkron kuralı GETİRMEZ; yalnız <c>vehicles</c> satırını
/// günceller ve normal sürüm/senkron yoluyla taşınır. <c>BusinessSyncService</c>'teki
/// "gelen sayaç geriye götürmesin" koruması aynen durur (uzak makinenin bayat değeri hâlâ
/// yereli düşüremez); buradaki düşüş <b>yerel gerçeğin</b> yeniden hesabıdır.
/// </summary>
internal static class VehicleMeterService
{
    /// <summary>Elle beyan sayılan (operasyonel olmayan, iptal edilemeyen) sayaç kaynakları.</summary>
    private static readonly string[] ElleBeyan = { "vehicle_create", "vehicle_form" };

    /// <summary>
    /// Aracın GERÇEK sayacını hesaplar: geçerli yakıt dağıtımları · geçerli bakımlar · elle beyan
    /// tabanı — hepsinin en büyüğü.
    ///
    /// ⚠️ Sayaç birimi araca özeldir (<c>km</c> / <c>hour</c>); bakım tarafında km ve saat AYRI
    /// kolonlardır, bu yüzden aracın birimine uyan kolon okunur. Yakıt fişi tek kolon kullanır.
    /// </summary>
    internal static decimal Hesapla(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        var birim = Birim(conn, tx, companyId, vehicleId);
        var bakimKolonu = birim == "hour" ? "performed_hour" : "performed_km";

        decimal enBuyuk = 0m;

        // 1) Geçerli yakıt dağıtımları (iptal edilen kayıt SAYILMAZ — düzeltmenin işe yaramasının nedeni).
        enBuyuk = Max(enBuyuk, TekDeger(conn, tx,
            "SELECT current_meter FROM fuel_distributions " +
            "WHERE company_id=@c AND vehicle_id=@v AND is_deleted=0 AND current_meter IS NOT NULL;",
            companyId, vehicleId));

        // 2) Geçerli bakımlar (iptal edilmiş/silinmiş bakım sayaca katkı vermez).
        enBuyuk = Max(enBuyuk, TekDeger(conn, tx,
            $"SELECT {bakimKolonu} FROM vehicle_maintenances " +
            $"WHERE company_id=@c AND vehicle_id=@v AND is_deleted=0 AND is_cancelled=0 AND {bakimKolonu} IS NOT NULL;",
            companyId, vehicleId));

        // 3) ELLE BEYAN TABANI — araç kartı açılışı ve araç formundan girilen sayaç. Bu satırlar
        //    iptal edilemez; hesaba katılmazsa kayıtsız bir araçta sayaç sıfıra düşerdi (veri kaybı).
        var inList = string.Join(",", ElleBeyan.Select((_, i) => "@s" + i));
        enBuyuk = Max(enBuyuk, TekDeger(conn, tx,
            $"SELECT new_value FROM vehicle_meter_logs " +
            $"WHERE company_id=@c AND vehicle_id=@v AND source IN ({inList});",
            companyId, vehicleId, ElleBeyan));

        return enBuyuk;
    }

    /// <summary>
    /// Aracın sayacını gerçek kayıtlardan yeniden hesaplar ve gerekiyorsa günceller.
    /// Değer AŞAĞI da inebilir — hatalı kaydın düzeltilmesinin/iptalinin karşılığı budur.
    /// Değişiklik <c>vehicle_meter_logs</c>'a <c>recalc:&lt;kaynak&gt;</c> olarak yazılır
    /// (iz kaybolmaz; kullanıcı neyin neden değiştiğini görebilir).
    /// </summary>
    /// <returns>Sayaç değiştiyse true.</returns>
    internal static bool Tazele(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId,
        string kaynak, long now)
    {
        if (string.IsNullOrWhiteSpace(vehicleId)) return false;

        var mevcut = Oku(conn, tx, companyId, vehicleId);
        if (mevcut is null) return false;                    // araç yok / başka firma → dokunma
        var gercek = Hesapla(conn, tx, companyId, vehicleId);
        if (gercek == mevcut.Value) return false;

        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE vehicles SET current_meter=@m, version=version+1, updated_at=@now " +
                              "WHERE id=@id AND company_id=@c;";
            upd.AddWithValue("@m", Money.Serialize(gercek));
            upd.AddWithValue("@now", now);
            upd.AddWithValue("@id", vehicleId);
            upd.AddWithValue("@c", companyId);
            upd.ExecuteNonQuery();
        }

        using var log = conn.CreateCommand();
        log.Transaction = tx;
        log.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES(@id,@c,@v,@o,@n,@src,@now);";
        log.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        log.AddWithValue("@c", companyId);
        log.AddWithValue("@v", vehicleId);
        log.AddWithValue("@o", Money.Serialize(mevcut.Value));
        log.AddWithValue("@n", Money.Serialize(gercek));
        log.AddWithValue("@src", "recalc:" + kaynak);
        log.AddWithValue("@now", now);
        log.ExecuteNonQuery();
        return true;
    }

    /// <summary>Kendi bağlantısını açan kolaylık sarmalayıcısı (transaction dışındaki çağrılar için).</summary>
    internal static bool Tazele(IDbConnectionFactory factory, string companyId, string vehicleId, string kaynak, long now)
    {
        using var conn = factory.Create();
        using var tx = conn.BeginImmediate();
        var degisti = Tazele(conn, tx, companyId, vehicleId, kaynak, now);
        tx.Commit();
        return degisti;
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────────────────────────

    private static decimal Max(decimal a, decimal b) => b > a ? b : a;

    private static string Birim(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT meter_unit FROM vehicles WHERE id=@v AND company_id=@c;";
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() as string ?? "km";
    }

    private static decimal? Oku(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT current_meter FROM vehicles WHERE id=@v AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() is string v ? Money.Parse(v) : null;
    }

    /// <summary>
    /// TEXT sayaç kolonlarının en büyüğü. ⚠️ SQL <c>MAX()</c> KULLANILMAZ: kolonlar metin olduğu için
    /// SQLite'ta metin sıralaması yapar ve "9.000" &gt; "10.000" gibi YANLIŞ sonuç verir. Değerler
    /// C# tarafında <see cref="Money"/> ile decimal'e çevrilip karşılaştırılır (iki lehçede de aynı).
    /// </summary>
    private static decimal TekDeger(DbConnection conn, DbTransaction? tx, string sql,
        string companyId, string vehicleId, string[]? kaynaklar = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", vehicleId);
        if (kaynaklar is not null)
            for (int i = 0; i < kaynaklar.Length; i++) cmd.AddWithValue("@s" + i, kaynaklar[i]);

        decimal enBuyuk = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (r.IsDBNull(0)) continue;
            var d = Money.Parse(r.GetString(0));
            if (d > enBuyuk) enBuyuk = d;
        }
        return enBuyuk;
    }
}
