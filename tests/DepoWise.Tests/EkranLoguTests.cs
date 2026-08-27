using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ LOG-01 — EKRANA ÖZEL KAYIT GEÇMİŞİ ═══ (kullanıcı isteği 2026-08-27)
///
/// <i>"Her ekrana özel log butonu olmalı… log yetkisini yetki ağacına eklemeyi unutma."</i>
///
/// Kilitlenen kurallar:
/// <list type="number">
///   <item>Log YETKİYE bağlıdır (<see cref="SpecialButtons.ScreenLog"/>) — deny-by-default.</item>
///   <item>Ekranın KENDİ modülünde okuma izni de gerekir; aksi halde log düğmesi yetki sisteminde
///   yan kapı olurdu (göremediğin ekranın geçmişini de göremezsin).</item>
///   <item>Bir ekranın logu YALNIZ kendi varlık tiplerini gösterir — başka ekranın verisi sızmaz.</item>
///   <item>Eşlemesi olmayan modül BOŞ döner; sessizce TÜM loga düşmez.</item>
///   <item>Gösterilen zaman KAYIT ANIdır (created_at) — işlem tarihi geri alınsa bile gerçek saat.</item>
/// </list>
/// </summary>
public class EkranLoguTests : IDisposable
{
    private const string Co = "LOG";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();
    private readonly AuditLogService _audit;
    private readonly StockService _stock;
    private readonly string _depo, _mat;
    private readonly string _uid;

    private const long Simdi = 1_700_000_000_000;
    private static readonly long Gecmis = Simdi - 60L * 86_400_000;

    private sealed class TestClock : IClock
    { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Simdi); }

    public EkranLoguTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_log_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", Co);
            cmd.ExecuteNonQuery();
        }
        _uid = new UserService(_f, _clock).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        var yonetici = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _depo = new BranchService(_f, _clock).Create(yonetici, new NewBranch("Depo"));
        _mat = new MaterialService(_f, _clock).Create(yonetici, new NewMaterial("M-1", "Çimento"));
        _stock = new StockService(_f, _clock);
        _stock.ReceiveIn(yonetici, new[] { new StockLine(_mat, 10m) }, "op-1", branchId: _depo, docDate: Gecmis);

        _audit = new AuditLogService(_f);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    /// <summary>Personel oturumu: modül izinleri + istenen özel butonlar (admin bypass'ı YOK).</summary>
    private SessionContext Oturum(string[] moduller, params string[] butonlar)
        => new(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, true, true, false)), butonlar));

    // ══════════════ YETKİ ══════════════

    /// <summary>⭐ Log yetkisi YOKSA erişim reddedilir — düğmenin gizli olması yeterli değildir.</summary>
    [Fact]
    public void LOG1_Yetkisiz_Kullanici_Ekran_Logunu_Okuyamaz()
    {
        var s = Oturum(new[] { "stock" });   // btn-screen-log YOK
        Assert.Throws<ForbiddenException>(() => _audit.ForModule(s, "stock"));
    }

    /// <summary>⭐ Log yetkisi VAR ama EKRANI göremiyorsa yine reddedilir — yan kapı kapalı.</summary>
    [Fact]
    public void LOG2_Ekrani_Goremeyen_Kullanici_Logunu_Da_Goremez()
    {
        var s = Oturum(new[] { "materials" }, SpecialButtons.ScreenLog);   // stock izni YOK
        Assert.Throws<ForbiddenException>(() => _audit.ForModule(s, "stock"));
    }

    /// <summary>Her iki kapı da açıkken geçmiş okunur.</summary>
    [Fact]
    public void LOG3_Yetkili_Kullanici_Okuyabilir()
    {
        var s = Oturum(new[] { "stock" }, SpecialButtons.ScreenLog);
        Assert.NotEmpty(_audit.ForModule(s, "stock"));
    }

    // ══════════════ KAPSAM ══════════════

    /// <summary>⭐ Bir ekranın logu YALNIZ kendi varlık tiplerini gösterir — başka ekranın verisi sızmaz.</summary>
    [Fact]
    public void LOG4_Ekran_Logu_Yalniz_Kendi_Varliklarini_Gosterir()
    {
        var s = Oturum(new[] { "stock", "materials" }, SpecialButtons.ScreenLog);

        var stok = _audit.ForModule(s, "stock");
        Assert.NotEmpty(stok);
        Assert.All(stok, r => Assert.Contains(r.EntityType, ScreenAuditMap.EntityTypes("stock")));

        var malzeme = _audit.ForModule(s, "materials");
        Assert.All(malzeme, r => Assert.Contains(r.EntityType, ScreenAuditMap.EntityTypes("materials")));

        // İki küme KESİŞMEZ — aynı satır iki ekranda birden çıkmamalı.
        Assert.Empty(stok.Select(x => x.EntityType).Intersect(malzeme.Select(x => x.EntityType)));
    }

    /// <summary>⭐ Eşlemesi olmayan modül BOŞ döner — sessizce tüm loga DÜŞMEZ.</summary>
    [Fact]
    public void LOG5_Eslemesiz_Modul_Bos_Doner_Tum_Loga_Dusmez()
    {
        var s = Oturum(new[] { "backup" }, SpecialButtons.ScreenLog);
        Assert.Empty(_audit.ForModule(s, "backup"));
        Assert.False(ScreenAuditMap.Has("backup"));
    }

    /// <summary>Başka FİRMANIN kaydı hiçbir ekranın logunda görünmez (tenant izolasyonu).</summary>
    [Fact]
    public void LOG6_Baska_Firmanin_Kaydi_Gorunmez()
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO audit_logs(id,company_id,user_id,entity_type,entity_id,action,created_at) " +
                              "VALUES('x','BASKA','u','stock_document','d','create',@t);";
            cmd.AddWithValue("@t", Simdi);
            cmd.ExecuteNonQuery();
        }
        var s = Oturum(new[] { "stock" }, SpecialButtons.ScreenLog);
        Assert.All(_audit.ForModule(s, "stock"), r => Assert.NotEqual("d", r.EntityId));
    }

    // ══════════════ TRH-01 İLE BİRLİKTE ══════════════

    /// <summary>
    /// ⭐⭐ Kullanıcının asıl istediği: <i>"log üzerinden gerçekten kaydı ne zaman eklediğini
    /// görebilmeliyiz, ama tarih iş gereği ileri/geri olabilir."</i>
    ///
    /// Hazırlıkta belge GEÇMİŞ iş günüyle açıldı; log yine de KAYIT ANINI (şimdi) göstermeli.
    /// </summary>
    [Fact]
    public void LOG7_Log_Islem_Tarihini_Degil_Kayit_Anini_Gosterir()
    {
        var s = Oturum(new[] { "stock" }, SpecialButtons.ScreenLog);
        var satirlar = _audit.ForModule(s, "stock");

        Assert.NotEmpty(satirlar);
        Assert.All(satirlar, r => Assert.Equal(Simdi, r.CreatedAt));   // gerçek saat
        Assert.All(satirlar, r => Assert.NotEqual(Gecmis, r.CreatedAt)); // iş günü DEĞİL
    }

    // ══════════════ KATALOG BÜTÜNLÜĞÜ ══════════════

    /// <summary>⭐ Eşlemedeki her modül gerçek bir yetki modülü olmalı; uydurma anahtar, sessizce
    /// hiçbir zaman eşleşmeyen (dolayısıyla daima boş) bir log düğmesi demektir.</summary>
    [Fact]
    public void LOG8_Eslemedeki_Moduller_Gercek()
    {
        var gercek = AppScreens.All.Select(x => x.ModuleKey).ToHashSet(StringComparer.Ordinal);
        var uydurma = ScreenAuditMap.Modules.Where(m => !gercek.Contains(m)).ToList();
        Assert.True(uydurma.Count == 0, "Katalogda olmayan modül(ler): " + string.Join(", ", uydurma));
    }

    /// <summary>Eşlemedeki varlık tipleri kodda GERÇEKTEN yazılıyor olmalı — yazılmayan bir tip,
    /// hiç dolmayacak bir log demektir (kullanıcı "boş" görür ve nedenini anlamaz).</summary>
    [Fact]
    public void LOG9_Eslemedeki_Varlik_Tipleri_Kodda_Yaziliyor()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);

        var kaynak = string.Join("\n", Directory.EnumerateFiles(Path.Combine(kok!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.EndsWith("ScreenAuditMap.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        var eksik = ScreenAuditMap.Modules
            .SelectMany(ScreenAuditMap.EntityTypes)
            .Distinct(StringComparer.Ordinal)
            .Where(t => !kaynak.Contains($"\"{t}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(eksik.Count == 0,
            "Eşlemede olup kodda HİÇ yazılmayan varlık tipi(leri) → o ekranın logu daima boş kalır:\n  " +
            string.Join("\n  ", eksik));
    }
}
