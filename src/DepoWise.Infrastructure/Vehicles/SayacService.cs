using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Vehicles;

/// <summary>⭐ M-36: aracın bir sayaç okuması (yakıt fişi, bakım ya da elle beyan).</summary>
public sealed record SayacOkumasi(string Kaynak, string KaynakId, long Tarih, decimal Deger, bool Gecersiz)
{
    public string KaynakMetni => Kaynak switch { "yakit" => "Yakıt", "bakim" => "Bakım", _ => "Araç kartı" };
    public string Satir => $"{DateTimeOffset.FromUnixTimeMilliseconds(Tarih).UtcDateTime:dd.MM.yyyy} · {KaynakMetni} · {Deger:N0}" +
                           (Gecersiz ? " · GEÇERSİZ (hesaba girmez)" : "");
}

/// <summary>
/// ═══ M-36 (AR-GE 2026-10-19, 05 F-078) — SAYAÇ KURALLARI: GEÇMİŞ · "GEÇERSİZ SAY" · GERİYE TARİHLİ OKUMA · ESKİ SAYAÇ ═══
/// <list type="bullet">
///   <item><b>Geçmiş:</b> yakıt, bakım ve araç kartı okumaları tarih sırasıyla tek listede.</item>
///   <item><b>Geçersiz say:</b> yanlış okunan sayaç, kayıt (litre/bakım) iptal edilmeden sayaç hesabından çıkarılır; geri alınabilir.
///   Araç sayacı <see cref="VehicleMeterService.Tazele(DbConnection, DbTransaction?, string, string, string, long)"/> ile hemen yeniden
///   hesaplanır. Yetki: araç düzenleme + ters kayıt düğmesi; gerekçe zorunlu, işlem geçmişine yazılır.</item>
///   <item><b>Geriye tarihli okuma:</b> girilen sayaç, o tarihten önceki okumadan küçük ya da sonrakinden büyükse UYARI (engel değil
///   — çevrimdışı geç gelen okuma düzgün sıraya otursun, hata fark edilsin).</item>
///   <item><b>Eski sayaç:</b> son geçerli okuma <see cref="EskiGun"/> günden eskiyse uyarı (bakım hatırlatması yanlış kalmasın).</item>
/// </list>
/// </summary>
public sealed class SayacService
{
    public const int EskiGun = 30;
    private const string Modul = "vehicles";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public SayacService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static List<SayacOkumasi> Okumalar(DbConnection conn, string companyId, string vehicleId)
    {
        var birim = "km";
        using (var b = conn.CreateCommand())
        {
            b.CommandText = "SELECT meter_unit FROM vehicles WHERE id=@v AND company_id=@c AND is_deleted=0;";
            b.AddWithValue("@v", vehicleId);
            b.AddWithValue("@c", companyId);
            birim = b.ExecuteScalar() as string ?? throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        }
        var kolon = birim == "hour" ? "performed_hour" : "performed_km";
        var yv = DbIntrospect.ColumnExists(conn, null, "fuel_distributions", "meter_void") ? "meter_void" : "0";
        var bv = DbIntrospect.ColumnExists(conn, null, "vehicle_maintenances", "meter_void") ? "meter_void" : "0";
        var lv = DbIntrospect.ColumnExists(conn, null, "vehicle_meter_logs", "is_void") ? "is_void" : "0";
        var list = new List<SayacOkumasi>();
        void Topla(string kaynak, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@v", vehicleId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (!r.IsDBNull(2) && r.GetString(2).Length > 0)
                    list.Add(new SayacOkumasi(kaynak, r.GetString(0), Convert.ToInt64(r.GetValue(1)), Money.Parse(r.GetString(2)), Convert.ToInt64(r.GetValue(3)) != 0));
        }
        Topla("yakit", $"SELECT id, distribution_date, current_meter, {yv} FROM fuel_distributions WHERE company_id=@c AND vehicle_id=@v AND is_deleted=0;");
        Topla("bakim", $"SELECT id, COALESCE(performed_date, created_at), {kolon}, {bv} FROM vehicle_maintenances WHERE company_id=@c AND vehicle_id=@v AND is_deleted=0 AND is_cancelled=0;");
        Topla("elle", $"SELECT id, created_at, new_value, {lv} FROM vehicle_meter_logs WHERE company_id=@c AND vehicle_id=@v AND source IN ('vehicle_create','vehicle_form');");
        return list.OrderByDescending(x => x.Tarih).ThenByDescending(x => x.Deger).ToList();
    }

    /// <summary>Sayaç geçmişi (en yeni üstte).</summary>
    public IReadOnlyList<SayacOkumasi> Gecmis(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Modul, PermissionAction.View);
        using var conn = _factory.Create();
        return Okumalar(conn, s.CompanyId, vehicleId);
    }

    /// <summary>Son GEÇERLİ okumanın tarihi (yoksa null).</summary>
    public long? SonOkuma(SessionContext s, string vehicleId)
        => Gecmis(s, vehicleId).Where(x => !x.Gecersiz).Select(x => (long?)x.Tarih).Max();

    /// <summary>Eski sayaç uyarısı: son geçerli okuma <see cref="EskiGun"/> günden eskiyse metin (yoksa null).</summary>
    public string? EskiSayacUyarisi(SessionContext s, string vehicleId)
    {
        var son = SonOkuma(s, vehicleId);
        if (son is null) return "Bu aracın hiç sayaç okuması yok.";
        var gun = (int)((_clock.UtcNow.ToUnixTimeMilliseconds() - son.Value) / 86_400_000L);
        return gun > EskiGun ? $"Sayaç {gun} gündür güncellenmedi — bakım uyarıları eski sayaca göre hesaplanıyor olabilir." : null;
    }

    /// <summary>Geriye tarihli okuma tutarlılığı: uyumsuzsa uyarı metni, uyumluysa null (kayıt ENGELLENMEZ).</summary>
    public string? Tutarlilik(SessionContext s, string vehicleId, long tarih, decimal sayac)
    {
        var gecerli = Gecmis(s, vehicleId).Where(x => !x.Gecersiz).ToList();
        var once = gecerli.Where(x => x.Tarih < tarih).OrderByDescending(x => x.Tarih).ThenByDescending(x => x.Deger).FirstOrDefault();
        var sonra = gecerli.Where(x => x.Tarih > tarih).OrderBy(x => x.Tarih).ThenBy(x => x.Deger).FirstOrDefault();
        if (once is not null && sayac < once.Deger)
            return $"Girilen sayaç ({sayac:N0}), bu tarihten ÖNCEKİ okumadan ({once.Satir}) küçük. Tarih ya da sayaç yanlış olabilir.";
        if (sonra is not null && sayac > sonra.Deger)
            return $"Girilen sayaç ({sayac:N0}), bu tarihten SONRAKİ okumadan ({sonra.Satir}) büyük. Tarih ya da sayaç yanlış olabilir.";
        return null;
    }

    /// <summary>Okumayı geçersiz sayar (<paramref name="gecersiz"/>=false: geri alır). Araç sayacı yeniden hesaplanır.</summary>
    public void GecersizSay(SessionContext s, string vehicleId, string kaynak, string kaynakId, string gerekce, bool gecersiz = true)
    {
        AccessControl.Require(s, Modul, PermissionAction.Edit);
        AccessControl.RequireButton(s, SpecialButtons.Reverse);
        if (string.IsNullOrWhiteSpace(gerekce)) throw new ArgumentException("Gerekçe zorunlu.");
        var (tablo, kolon) = kaynak switch
        {
            "yakit" => ("fuel_distributions", "meter_void"),
            "bakim" => ("vehicle_maintenances", "meter_void"),
            "elle" => ("vehicle_meter_logs", "is_void"),
            _ => throw new ArgumentException("Bilinmeyen sayaç kaynağı."),
        };
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!DbIntrospect.ColumnExists(conn, null, tablo, kolon))
            throw new InvalidOperationException("Veritabanı sayaç okumasını geçersiz saymayı henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {tablo} SET {kolon}=@g WHERE id=@id AND company_id=@c AND vehicle_id=@v;";
            cmd.AddWithValue("@g", gecersiz ? 1L : 0L);
            cmd.AddWithValue("@id", kaynakId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@v", vehicleId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Sayaç okuması bulunamadı veya başka firmaya ait.");
        }
        VehicleMeterService.Tazele(conn, tx, s.CompanyId, vehicleId, gecersiz ? "meter_void" : "meter_unvoid", now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"sayacGecersiz\":{(gecersiz ? "true" : "false")},\"kaynak\":\"{kaynak}\",\"kayit\":\"{kaynakId}\",\"gerekce\":\"{gerekce.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}"), _clock);
        tx.Commit();
    }
}
