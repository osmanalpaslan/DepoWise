using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ADR-182 · S4 (PK-D1=A) — "GÜNLÜK FAALİYET — DETAY" RAPORU ═══ (ARA İŞ 2, 2026-08-29)
///
/// Kullanıcı isteği: Günlük Faaliyet kayıtlarının gün gün dökümü; tarih aralığı ZORUNLU; kayıt tipi
/// ÇOKLU seçilebilsin; <b>hiçbir tip seçilmezse TÜM tipler</b> listelensin; yeni ekran/menü açılmasın.
///
/// Kayıt tipi veritabanında İKİ sütunla kodlanır (<c>activity_type</c> + <c>movement_kind</c>):
/// Bakım · İlave Yağ · İlave Filtre · Tamir · Hareket (movement, kind≠transfer) · Transfer.
/// Bu sınıf hem eşlemeyi hem güvenlik kapılarını (üst kapı + yeni kategori kapısı) kilitler.
/// </summary>
public class GunlukFaaliyetRaporuTests : IDisposable
{
    private const long Gun = 86_400_000L;
    private static readonly long G1 = new DateTimeOffset(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    private static readonly long G2 = G1 + Gun;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    public GunlukFaaliyetRaporuTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_gunfaal_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var users = new UserService(_factory, new SabitSaat());
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Seed();
    }

    private sealed class SabitSaat : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.FromUnixTimeMilliseconds(G1);
    }

    private void Seed()
    {
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
             "VALUES('B','Baska',@n,@n,1,0,5,5,2);", ("@n", G1));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", G1));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B2','A','Sahra',@n,@n);", ("@n", G1));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('BB','B','Baska',@n,@n);", ("@n", G1));
        Exec(@"INSERT INTO vehicles(id,company_id,internal_code,plate,meter_unit,branch_id,current_meter,created_at,updated_at,version,is_deleted)
               VALUES('va','A','VA','34ABC01','km','B1','0',@n,@n,1,0);", ("@n", G1));
        Exec("INSERT INTO personnel(id,company_id,full_name,created_at,updated_at,version,is_deleted) " +
             "VALUES('p1','A','Ali Usta',@n,@n,1,0);", ("@n", G1));

        // 1 Ağustos — dört "doğrudan" tip
        Faaliyet("a1", "A", "maintenance", null, G1, "B1", "Bakım yapıldı", 2);
        Faaliyet("a2", "A", "extra_oil", null, G1, "B1", "Yağ ilavesi", null);
        Faaliyet("a3", "A", "extra_filter", null, G1, "B2", "Filtre", null);
        Faaliyet("a4", "A", "repair", null, G1, "B1", "Tamir", 1);
        // 2 Ağustos — movement ailesi (kind ile ayrışır)
        Faaliyet("a5", "A", "movement", "movement", G2, "B1", "Sahaya gitti", null);
        Faaliyet("a6", "A", "movement", "transfer", G2, "B1", "Transfer edildi", 3);
        // Silinmiş kayıt — GÖRÜNMEMELİ
        Faaliyet("a7", "A", "maintenance", null, G1, "B1", "İPTAL", null, silinmis: true);
        // Aralık dışı
        Faaliyet("a8", "A", "repair", null, G1 + 10 * Gun, "B1", "Sonraki hafta", null);
        // Başka firma
        Faaliyet("bx", "B", "maintenance", null, G1, "BB", "Baska firma", null);
    }

    // ══════════════ Katalog + yapı ══════════════

    [Fact]
    public void GFR1_Katalog_Tanimi_Dogru()
    {
        var d = ReportCatalog.ByKey("daily-activity");
        Assert.NotNull(d);
        Assert.Equal("Günlük Faaliyet — Detay", d!.Name);
        Assert.Equal(ReportCategory.DailyActivity, d.Category);
        Assert.Equal("Günlük Faaliyet", ReportCatalog.CategoryLabel(d.Category));
        Assert.Equal("report_daily_activity", ReportCatalog.CategoryModule(d.Category));
        Assert.True(d.RequiresDate);                  // tarih ZORUNLU (kullanıcı şartı)
        Assert.True(d.UsesActivityType);              // kayıt tipi çoklu seçimi
        Assert.True(d.UsesDate && d.UsesBranch && d.UsesVehicle);
        Assert.Equal("daily_activity", d.DataModule); // ekran kapalıysa rapor da kapalı (RPR-15)
    }

    [Fact]
    public void GFR2_Yeni_Yetki_Anahtari_Katalogda_Var()
        => Assert.Contains(AppModules.All, m => m.Key == "report_daily_activity");

    [Fact]
    public void GFR3_Rapor_Yapisi_ve_Sutunlar()
    {
        var t = Rapor();
        Assert.Equal("Günlük Faaliyet — Detay", t.Title);
        Assert.Equal(new[] { "Tarih", "Kayıt Tipi", "Şube", "Araç", "Nereden → Nereye", "Operatör", "Süre (gün)", "Açıklama" }, t.Headers);
    }

    // ══════════════ Kayıt tipi filtresi ══════════════

    [Fact]
    public void GFR4_Hicbir_Tip_Secilmezse_TUM_Tipler_Gelir()
    {
        var t = Rapor();                       // ActivityTypes = null
        Assert.Equal(6, t.Rows.Count);         // a1..a6 (a7 silinmiş, a8 aralık dışı, bx başka firma)
        var tipler = t.Rows.Select(r => (string)r[1]!).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Bakım", "Hareket", "Tamir", "Transfer", "İlave Filtre", "İlave Yağ" }, tipler);
    }

    [Fact]
    public void GFR5_Bos_Liste_de_TUM_Tipler_Demektir()
        => Assert.Equal(6, Rapor(Array.Empty<string>()).Rows.Count);   // boş liste = filtre yok

    [Fact]
    public void GFR6_Tek_Tip_Secimi()
    {
        var t = Rapor(new[] { DailyActivityTypeOptions.Maintenance });
        Assert.Single(t.Rows);
        Assert.Equal("Bakım", (string)t.Rows[0][1]!);
    }

    [Fact]
    public void GFR7_Coklu_Tip_Secimi()
    {
        var t = Rapor(new[] { DailyActivityTypeOptions.ExtraOil, DailyActivityTypeOptions.ExtraFilter });
        Assert.Equal(2, t.Rows.Count);
        Assert.All(t.Rows, r => Assert.Contains((string)r[1]!, new[] { "İlave Yağ", "İlave Filtre" }));
    }

    /// <summary>⭐ "Hareket" ve "Transfer" aynı <c>activity_type='movement'</c> satırlarının
    /// <c>movement_kind</c> ile ayrılmış hâlleridir — filtre ikisini KARIŞTIRMAMALIDIR.</summary>
    [Fact]
    public void GFR8_Hareket_ve_Transfer_Ayri_Suzulur()
    {
        var hareket = Rapor(new[] { DailyActivityTypeOptions.Movement });
        Assert.Single(hareket.Rows);
        Assert.Equal("Hareket", (string)hareket.Rows[0][1]!);
        Assert.Equal("Sahaya gitti", (string)hareket.Rows[0][7]!);

        var transfer = Rapor(new[] { DailyActivityTypeOptions.Transfer });
        Assert.Single(transfer.Rows);
        Assert.Equal("Transfer", (string)transfer.Rows[0][1]!);

        // İkisi birlikte seçilirse movement ailesinin TAMAMI gelir.
        Assert.Equal(2, Rapor(new[] { DailyActivityTypeOptions.Movement, DailyActivityTypeOptions.Transfer }).Rows.Count);
    }

    [Fact]
    public void GFR9_Bilinmeyen_Tip_Anahtari_Hicbir_Sey_Getirmez_FailClosed()
        => Assert.Empty(Rapor(new[] { "boyle-bir-tip-yok'; DROP TABLE daily_activities; --" }).Rows);

    // ══════════════ Kapsam / tarih / güvenlik ══════════════

    [Fact]
    public void GFR10_Silinmis_Kayit_Gorunmez()
        => Assert.DoesNotContain(Rapor().Rows, r => (string)r[7]! == "İPTAL");

    [Fact]
    public void GFR11_Tarih_Araligi_Disi_Gorunmez()
        => Assert.DoesNotContain(Rapor().Rows, r => (string)r[7]! == "Sonraki hafta");

    [Fact]
    public void GFR12_Gun_Sinirlari_ve_Siralama_Yeni_Gun_Ustte()
    {
        var t = Rapor();
        Assert.Equal("02.08.2026", (string)t.Rows[0][0]!);   // en yeni gün üstte
        Assert.Equal("01.08.2026", (string)t.Rows[^1][0]!);

        // Yalnız 1 Ağustos aralığı → 2 Ağustos kayıtları düşer
        var birAgustos = _reports.Run(_admin, "daily-activity", new ReportRequest(true, G1, G1 + Gun - 1));
        Assert.Equal(4, birAgustos.Rows.Count);
        Assert.All(birAgustos.Rows, r => Assert.Equal("01.08.2026", (string)r[0]!));
    }

    [Fact]
    public void GFR13_Tenant_Baska_Firma_Gorunmez()
        => Assert.DoesNotContain(Rapor().Rows, r => (string)r[7]! == "Baska firma");

    [Fact]
    public void GFR14_BranchAccess_Kapsam_Disi_Sube_Gelmez()
    {
        var izin = new PermissionSet(new[]
        {
            new ModulePermission("reports", true, false, false, false),
            new ModulePermission("report_daily_activity", true, false, false, false),
            new ModulePermission("daily_activity", true, false, false, false),
        }, Array.Empty<string>());
        var kapsamli = new SessionContext("u-b1", "A", new[] { RoleKeys.Staff }, izin) { ScopeBranchIds = new[] { "B1" } };
        var t = _reports.Run(kapsamli, "daily-activity", new ReportRequest(true, G1, G2 + Gun - 1));
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[2]!));
        Assert.DoesNotContain(t.Rows, r => (string)r[7]! == "Filtre");   // B2 kaydı kapsam dışı
    }

    [Fact]
    public void GFR15_Arac_Filtresi_Calisir()
    {
        var t = _reports.Run(_admin, "daily-activity",
            new ReportRequest(true, G1, G2 + Gun - 1, VehicleIds: new[] { "va" }));
        Assert.All(t.Rows, r => Assert.Equal("VA - 34ABC01", (string)r[3]!));
    }

    /// <summary>⭐ ÇİFT KAPI: `reports` üst kapısı + yeni `report_daily_activity` kategori kapısı.
    /// Rapor türü adı değiştirilerek atlatılamaz — ikisi de ortak `Run` üzerinden uygulanır.</summary>
    [Fact]
    public void GFR16_Yetki_Reports_Arti_Kategori_Gerekir()
    {
        Assert.Throws<ForbiddenException>(() => _reports.Run(Personel("daily_activity"), "daily-activity", Istek()));                      // reports yok
        Assert.Throws<ForbiddenException>(() => _reports.Run(Personel("reports", "daily_activity"), "daily-activity", Istek()));            // kategori yok
        Assert.Throws<ForbiddenException>(() => _reports.Run(Personel("reports", "report_vehicle", "daily_activity"), "daily-activity", Istek()));  // YANLIŞ kategori
        Assert.NotEmpty(_reports.Run(Personel("reports", "report_daily_activity", "daily_activity"), "daily-activity", Istek()).Rows);      // doğru ikili
    }

    /// <summary>Yeni kategori yetkisi BAŞKA raporları açmaz (çapraz sızma yok).</summary>
    [Fact]
    public void GFR17_Yeni_Kategori_Baska_Raporu_Acmaz()
    {
        var s = Personel("reports", "report_daily_activity", "daily_activity");
        Assert.Throws<ForbiddenException>(() => _reports.Run(s, "vehicle", Istek()));
        Assert.Throws<ForbiddenException>(() => _reports.Run(s, "stock-movements", Istek()));
    }

    [Fact]
    public void GFR18_Toplam_Satiri_Kayit_Sayisi_ve_Sure()
    {
        var t = Rapor();
        Assert.NotNull(t.TotalRow);
        Assert.Equal("TOPLAM", (string)t.TotalRow![0]!);
        Assert.Equal("6 kayıt", (string)t.TotalRow[1]!);
        Assert.Equal(6.0, D(t.TotalRow[6]), 3);   // 2 + 1 + 3 gün
    }

    [Fact]
    public void GFR19_Tip_Katalogu_Alti_Tip_Tek_Kaynak()
    {
        Assert.Equal(6, DailyActivityTypeOptions.All.Count);
        Assert.Equal("Bakım", DailyActivityTypeOptions.Label(DailyActivityTypeOptions.Maintenance));
        Assert.Equal("Transfer", DailyActivityTypeOptions.Label(DailyActivityTypeOptions.Transfer));
        Assert.Equal("bilinmeyen", DailyActivityTypeOptions.Label("bilinmeyen"));   // sessizce kaybolmaz
    }

    // ══════════════ Yardımcılar ══════════════

    private TableModel Rapor(string[]? tipler = null)
        => _reports.Run(_admin, "daily-activity", new ReportRequest(true, G1, G2 + Gun - 1, ActivityTypes: tipler));

    private static ReportRequest Istek() => new(true, G1, G2 + Gun - 1);

    private static SessionContext Personel(params string[] moduller)
        => new("u-p", "A", new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, false, false, false)).ToArray(), Array.Empty<string>()));

    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => Convert.ToDouble(v),
    };

    private void Faaliyet(string id, string firma, string tip, string? kind, long tarih, string sube,
                          string aciklama, int? gun, bool silinmis = false)
        => Exec(@"INSERT INTO daily_activities(id,company_id,activity_type,movement_kind,vehicle_id,from_location_id,to_location_id,
                      operator_id,duration_days,description,source_module,stock_processed,activity_date,operation_id,op_branch_id,
                      created_at,updated_at,version,is_deleted)
                  VALUES(@id,@f,@t,@k,@v,@sube,NULL,@op,@gun,@a,'daily_activity',0,@d,@oid,@sube,@n,@n,1,@sil);",
            ("@id", id), ("@f", firma), ("@t", tip), ("@k", (object?)kind), ("@v", firma == "A" ? "va" : null),
            ("@sube", sube), ("@op", firma == "A" ? "p1" : null), ("@gun", (object?)gun), ("@a", aciklama),
            ("@d", tarih), ("@oid", "op-" + id), ("@n", G1), ("@sil", silinmis ? 1 : 0));

    private void Exec(string sql, params (string, object?)[] ps)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
