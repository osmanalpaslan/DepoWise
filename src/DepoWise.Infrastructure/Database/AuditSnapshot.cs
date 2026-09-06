using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DepoWise.Application.Common;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// ═══ FAZ 4.3 — KAYIT ANLIK GÖRÜNTÜSÜ (kullanıcı isteği 2026-09-06) ═══
///
/// Bir kaydın o andaki hâlini JSON'a çevirir; <see cref="AuditWriter"/> bunu <c>after_json</c>
/// olarak yazar. Böylece art arda iki log satırının görüntüleri karşılaştırılınca <b>hangi alanda
/// neyin neye döndüğü</b> ortaya çıkar (<see cref="AuditDiff"/>).
///
/// <b>Neden bu yol seçildi.</b> Projede 162 <c>AuditEntry</c> çağrısı var. Hepsine tek tek "önce/sonra"
/// eklemek 59 dosyada, çalışan iş mantığının içinde değişiklik demekti — canlı veri varken kabul
/// edilemez bir risk. Bunun yerine tek nokta (<c>AuditWriter</c>) zenginleştirildi: iş mantığı
/// DEĞİŞMEDİ, log kendiliğinden anlamlı hâle geldi.
///
/// <b>Güvenlik.</b> Tablo adı <see cref="AuditFields.Tablo"/> beyaz listesinden gelir — dışarıdan
/// gelen metin sorguya ASLA girmez. Hassas sütunlar (<see cref="AuditFields.Gizli"/>) görüntüye
/// hiç alınmaz.
///
/// <b>Dayanıklılık.</b> Görüntü alınamazsa (kayıt bulunamadı, sütun okunamadı) <c>null</c> döner ve
/// log satırı eskisi gibi yazılır. Log zenginleştirme, iş kaydını ASLA başarısız edemez.
/// </summary>
public static class AuditSnapshot
{
    /// <summary>Aynı bağlantı/transaction içinde kaydın güncel hâlini JSON olarak döndürür.
    /// Tip beyaz listede değilse veya kayıt yoksa <c>null</c>.</summary>
    public static string? Al(DbConnection conn, DbTransaction? tx, string entityType, string entityId)
        => AlTablodan(conn, tx, AuditFields.Tablo(entityType), entityId);

    /// <summary>
    /// ⭐ FAZ 4.4 — tablo adı ÇAĞIRANDAN gelen sürüm (senkron çakışmasında kullanılır).
    ///
    /// <b>Güvenlik:</b> yalnız İÇERİDEN, senkron kataloğundaki sabit tablo adlarıyla çağrılır;
    /// kullanıcı girdisi buraya ASLA ulaşmaz. Hassas sütunlar yine dışarıda bırakılır.
    /// </summary>
    public static string? AlTablodan(DbConnection conn, DbTransaction? tx, string? tablo, string entityId)
    {
        if (tablo is null || string.IsNullOrWhiteSpace(entityId)) return null;

        // ⚠️ PostgreSQL'de transaction içinde HATA VEREN bir ifade, transaction'ın TAMAMINI iptal
        // eder — yani başarısız bir log sorgusu, asıl iş kaydını da düşürürdü. Bu yüzden sorgu bir
        // SAVEPOINT içinde çalışır: hata olursa yalnız oraya geri dönülür, iş kaydı sağ kalır.
        var sp = tx is null ? null : "audsnap_" + Guid.NewGuid().ToString("N")[..8];
        if (sp is not null && !Calistir(conn, tx, $"SAVEPOINT {sp};")) return null;

        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // Tablo adı beyaz listeden; kimlik parametreyle bağlanır.
            cmd.CommandText = $"SELECT * FROM {tablo} WHERE id = @sid;";
            cmd.AddWithValue("@sid", entityId);

            string? sonuc = null;
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    var sb = new StringBuilder("{");
                    bool ilk = true;
                    for (int i = 0; i < r.FieldCount; i++)
                    {
                        var sutun = r.GetName(i);
                        if (AuditFields.Gizli(sutun)) continue;
                        var deger = r.IsDBNull(i) ? null : Metin(r.GetValue(i));
                        if (!ilk) sb.Append(',');
                        ilk = false;
                        sb.Append(JsonSerializer.Serialize(sutun)).Append(':')
                          .Append(deger is null ? "null" : JsonSerializer.Serialize(deger));
                    }
                    sb.Append('}');
                    sonuc = sb.ToString();
                }
            }
            if (sp is not null) Calistir(conn, tx, $"RELEASE SAVEPOINT {sp};");
            return sonuc;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            // Log zenginleştirme iş kaydını ASLA bozamaz.
            if (sp is not null) Calistir(conn, tx, $"ROLLBACK TO SAVEPOINT {sp};");
            return null;
        }
    }

    private static bool Calistir(DbConnection conn, DbTransaction? tx, string sql)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException) { return false; }
    }

    /// <summary>Ham sütun değerini kültürden bağımsız metne çevirir (virgül/nokta karışması olmasın).</summary>
    private static string Metin(object v) => v switch
    {
        string s => s,
        bool b => b ? "1" : "0",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}
