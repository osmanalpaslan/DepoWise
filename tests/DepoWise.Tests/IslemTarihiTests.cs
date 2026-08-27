using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TRH-01 — İŞLEM TARİHİ ile KAYIT ANI AYRIMI ═══ (kullanıcı isteği 2026-08-27)
///
/// <b>Kullanıcının istediği kural.</b> <i>"Log tarihi ve kayıt tarihi ayrı olmalı. Log üzerinden
/// gerçekten kaydı ne zaman eklediğini görebilmeliyiz. Ama tarih iş gereği ileri veya geri tarihli
/// olabilir."</i>
///
/// Buradan iki DEĞİŞMEZ çıkar ve bu sınıf ikisini de kilitler:
/// <list type="number">
///   <item><b>İşlem tarihi</b> (<c>doc_date</c> / <c>entry_date</c> / <c>distribution_date</c>) kullanıcının
///   seçtiği İŞ GÜNÜDÜR; geçmiş ya da gelecek olabilir. <b>Raporlar buna göre süzer.</b></item>
///   <item><b>Kayıt anı</b> (<c>created_at</c>) kullanıcının seçiminden ETKİLENMEZ; daima gerçek saattir.
///   Geçmişe kayıt girilse bile "ne zaman girildi" izlenebilir kalır.</item>
/// </list>
///
/// Ayrıca geri/ileri tarih bir <b>YETKİDİR</b> (<see cref="SpecialButtons.BackDate"/>): yetkisiz kullanıcının
/// gönderdiği tarih sunucuda yok sayılır — arayüz kilidi güvenlik sayılmaz.
///
/// 🔒 Tamamen yerel SQLite; canlı veriye dokunmaz.
/// </summary>
public class IslemTarihiTests : IDisposable
{
    private const string Co = "TRH";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();
    private readonly StockService _stock;
    private readonly FuelService _fuel;
    private readonly ReportService _reports;
    private readonly SessionContext _yetkili, _yetkisiz;
    private readonly string _depo, _depo2, _mat, _arac;

    /// <summary>Kayıt anı olarak kullanılacak "şimdi" — 15.11.2023.</summary>
    private const long Simdi = 1_700_000_000_000;
    /// <summary>İşlem tarihi olarak seçilen GEÇMİŞ gün — 60 gün önce.</summary>
    private static readonly long Gecmis = Simdi - 60L * 86_400_000;
    /// <summary>İşlem tarihi olarak seçilen GELECEK gün — 10 gün sonra.</summary>
    private static readonly long Gelecek = Simdi + 10L * 86_400_000;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Simdi);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    public IslemTarihiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_trh_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", Co);
            cmd.ExecuteNonQuery();
        }

        var users = new UserService(_f, _clock);
        var uid = users.EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);

        // ADMİN bypass'ı testi anlamsız kılardı (admin her butonu geçer) → iki kullanıcı da PERSONEL rolünde,
        // farkları YALNIZCA btn-backdate yetkisi. Böylece kapı gerçekten sınanır.
        _yetkili = Oturum(uid, SpecialButtons.BackDate);
        _yetkisiz = Oturum(uid);

        var yonetici = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _depo = new BranchService(_f, _clock).Create(yonetici, new NewBranch("Depo A"));
        _depo2 = new BranchService(_f, _clock).Create(yonetici, new NewBranch("Depo B"));
        _mat = new MaterialService(_f, _clock).Create(yonetici, new NewMaterial("M-1", "Çimento"));
        _arac = new VehicleService(_f, _clock).Create(yonetici, new NewVehicle("ARC-1", "06AA001", 2020, 100m, "km", _depo));

        _stock = new StockService(_f, _clock);
        _fuel = new FuelService(_f, _clock);
        _reports = new ReportService(_f, _clock);
    }

    /// <summary>Personel rolünde oturum; verilen özel butonlar AÇIK, diğer her şey kapalı değil —
    /// modül izinleri tam verilir ki test yalnız TARİH kapısını ölçsün.</summary>
    private static SessionContext Oturum(string uid, params string[] butonlar)
    {
        var izin = new PermissionSet(
            new[]
            {
                new ModulePermission("stock", true, true, true, false),
                new ModulePermission("fuel", true, true, true, false),
                new ModulePermission("materials", true, false, false, false),
                new ModulePermission("reports", true, false, false, false),
            },
            butonlar);
        return new SessionContext(uid, Co, new[] { RoleKeys.Staff }, izin);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private (long DocDate, long CreatedAt) BelgeTarihleri()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT doc_date, created_at FROM stock_documents ORDER BY rowid DESC LIMIT 1;";
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read(), "Belge oluşmamış.");
        return (r.GetInt64(0), r.GetInt64(1));
    }

    // ══════════════ 1) TEMEL AYRIM: iş günü ile kayıt anı ══════════════

    /// <summary>⭐ ASIL KURAL: seçilen işlem tarihi belgeye yazılır, kayıt anı GERÇEK saat kalır.
    /// Yani geçmişe kayıt girilse bile logdan "ne zaman girildiği" okunabilir.</summary>
    [Theory]
    [InlineData("giris")]
    [InlineData("cikis")]
    [InlineData("sayim")]
    [InlineData("dagitim")]
    public void TRH1_Islem_Tarihi_ile_Kayit_Ani_Ayri_Yazilir(string islem)
    {
        // Önce stok koy (çıkış/dağıtım için gerekli) — bu hazırlık kaydı ölçüme girmesin diye önce yapılır.
        _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 100m) }, Op(), branchId: _depo);

        switch (islem)
        {
            case "giris":
                _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depo, docDate: Gecmis);
                break;
            case "cikis":
                _stock.IssueOut(_yetkili, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depo, docDate: Gecmis);
                break;
            case "sayim":
                _stock.Count(_yetkili, new[] { new CountLine(_mat, 90m) }, "sayım", Op(), branchId: _depo, docDate: Gecmis);
                break;
            case "dagitim":
                _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 10m) }, Op());   // ATANMAMIŞ kovasına
                _stock.DistributeUnassigned(_yetkili, new[] { new StockLine(_mat, 3m) }, _depo2, Op(), docDate: Gecmis);
                break;
        }

        var (docDate, createdAt) = BelgeTarihleri();
        Assert.Equal(Gecmis, docDate);      // iş günü = kullanıcının seçtiği
        Assert.Equal(Simdi, createdAt);     // kayıt anı = gerçek saat (DEĞİŞMEDİ)
        Assert.NotEqual(docDate, createdAt);
    }

    /// <summary>İleri tarih de aynı kuralla çalışır — iş gereği gelecek tarihli işlem meşrudur.</summary>
    [Fact]
    public void TRH2_Ileri_Tarihli_Islem_Kabul_Edilir()
    {
        _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 7m) }, Op(), branchId: _depo, docDate: Gelecek);
        var (docDate, createdAt) = BelgeTarihleri();
        Assert.Equal(Gelecek, docDate);
        Assert.Equal(Simdi, createdAt);
    }

    /// <summary>Tarih verilmezse "şimdi" kullanılır — eski davranış korunur.</summary>
    [Fact]
    public void TRH3_Tarih_Verilmezse_Simdi()
    {
        _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depo);
        var (docDate, createdAt) = BelgeTarihleri();
        Assert.Equal(Simdi, docDate);
        Assert.Equal(Simdi, createdAt);
    }

    // ══════════════ 2) YETKİ KAPISI ══════════════

    /// <summary>⭐ Yetkisiz kullanıcının gönderdiği farklı iş günü SUNUCUDA yok sayılır. Arayüz alanı
    /// kilitler ama kilit güvenlik değildir — API'ye doğrudan istek atan da geçememelidir.</summary>
    [Fact]
    public void TRH4_Yetkisiz_Kullanici_Gecmise_Kayit_Acamaz()
    {
        _stock.ReceiveIn(_yetkisiz, new[] { new StockLine(_mat, 6m) }, Op(), branchId: _depo, docDate: Gecmis);

        var (docDate, createdAt) = BelgeTarihleri();
        Assert.Equal(Simdi, docDate);       // istenen GEÇMİŞ tarih uygulanmadı
        Assert.Equal(Simdi, createdAt);
    }

    /// <summary>Yetkili kullanıcı aynı isteği yaptığında tarih UYGULANIR — kapı gerçekten yetkiye bakıyor.</summary>
    [Fact]
    public void TRH5_Yetkili_Kullanici_Gecmise_Kayit_Acabilir()
    {
        _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 6m) }, Op(), branchId: _depo, docDate: Gecmis);
        Assert.Equal(Gecmis, BelgeTarihleri().DocDate);
    }

    /// <summary>Kapı politikası tek yerden okunur; görünüm bayrağı ile sunucu kararı ÇELİŞMEZ.</summary>
    [Fact]
    public void TRH6_Politika_Gorunum_ile_Sunucu_Ayni_Karari_Verir()
    {
        Assert.True(DateEntryPolicy.Serbest(_yetkili));
        Assert.False(DateEntryPolicy.Serbest(_yetkisiz));
        Assert.Equal(Gecmis, DateEntryPolicy.Uygula(_yetkili, Gecmis));
        Assert.Null(DateEntryPolicy.Uygula(_yetkisiz, Gecmis));
        Assert.Null(DateEntryPolicy.Uygula(_yetkili, null));   // "şimdi" her zaman serbest
    }

    // ══════════════ 3) YAKIT ══════════════

    /// <summary>Yakıt depo girişi ve dağıtımı da aynı ayrımı uygular.</summary>
    [Fact]
    public void TRH7_Yakit_Islem_Tarihi_ile_Kayit_Ani_Ayri()
    {
        _fuel.AddDepotEntry(_yetkili, new NewDepotEntry(1000m, 40m, EntryDate: Gecmis), Op());
        _fuel.Distribute(_yetkili, new NewDistribution(_arac, 50m, 200m, 40m, DistributionDate: Gecmis), Op());

        using var conn = _f.Create();
        foreach (var (tablo, kolon) in new[] { ("fuel_depot_entries", "entry_date"), ("fuel_distributions", "distribution_date") })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {kolon}, created_at FROM {tablo} ORDER BY rowid DESC LIMIT 1;";
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read(), tablo + ": kayıt yok.");
            Assert.Equal(Gecmis, r.GetInt64(0));   // iş günü
            Assert.Equal(Simdi, r.GetInt64(1));    // kayıt anı
        }
    }

    /// <summary>Yakıtta da yetkisiz kullanıcı geçmişe kayıt açamaz.</summary>
    [Fact]
    public void TRH8_Yakit_Yetkisiz_Gecmise_Kayit_Acamaz()
    {
        _fuel.AddDepotEntry(_yetkisiz, new NewDepotEntry(500m, 40m, EntryDate: Gecmis), Op());

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT entry_date FROM fuel_depot_entries ORDER BY rowid DESC LIMIT 1;";
        Assert.Equal(Simdi, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ══════════════ 4) RAPORLAR — kullanıcının vurguladığı nokta ══════════════

    /// <summary>
    /// ⭐⭐ EN KRİTİK: rapor İŞ GÜNÜNE göre süzer, kayıt anına göre DEĞİL.
    ///
    /// Kullanıcının cümlesi: <i>"raporlarda tarih alanlarının doğru çalışması çok önemli, raporların
    /// bel kemiği."</i> Geçmişe girilen bir hareket, GEÇMİŞİ kapsayan aralıkta görünmeli; yalnız
    /// BUGÜNÜ kapsayan aralıkta görünmemelidir. Aksi halde geriye dönük kayıt raporu bozar.
    /// </summary>
    [Fact]
    public void TRH9_Rapor_Is_Gunune_Gore_Suzer_Kayit_Anina_Gore_Degil()
    {
        _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 9m) }, Op(), branchId: _depo, docDate: Gecmis);

        // (a) GEÇMİŞİ kapsayan aralık → hareket GÖRÜNMELİ
        var gecmisAralik = _reports.Run(_yetkili, "stock-movements",
            new ReportRequest(Executed: true, FromDate: Gecmis - 86_400_000, ToDate: Gecmis + 86_400_000));
        Assert.NotEmpty(gecmisAralik.Rows);

        // (b) YALNIZ BUGÜNÜ kapsayan aralık → hareket GÖRÜNMEMELİ (kayıt anı bugün olsa bile)
        var bugunAralik = _reports.Run(_yetkili, "stock-movements",
            new ReportRequest(Executed: true, FromDate: Simdi - 3_600_000, ToDate: Simdi + 3_600_000));
        Assert.Empty(bugunAralik.Rows);
    }

    /// <summary>Yakıt raporu da iş gününe göre süzer (depo girişi).</summary>
    [Fact]
    public void TRH10_Yakit_Raporu_Is_Gunune_Gore_Suzer()
    {
        _fuel.AddDepotEntry(_yetkili, new NewDepotEntry(300m, 40m, EntryDate: Gecmis), Op());

        var gecmis = _reports.Run(_yetkili, "fuel-depot",
            new ReportRequest(Executed: true, FromDate: Gecmis - 86_400_000, ToDate: Gecmis + 86_400_000));
        Assert.NotEmpty(gecmis.Rows);

        var bugun = _reports.Run(_yetkili, "fuel-depot",
            new ReportRequest(Executed: true, FromDate: Simdi - 3_600_000, ToDate: Simdi + 3_600_000));
        Assert.Empty(bugun.Rows);
    }

    /// <summary>⭐ Kayıt anı (log) geçmişe kayıtta bile GERÇEK saati gösterir — denetim izi bozulmaz.
    /// Bu, kullanıcının "log üzerinden gerçekten ne zaman eklendiğini görebilmeliyiz" isteğidir.</summary>
    [Fact]
    public void TRH11_Log_Gercek_Kayit_Anini_Gosterir()
    {
        _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 2m) }, Op(), branchId: _depo, docDate: Gecmis);

        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        // Hareket satırının created_at'i de iş gününden BAĞIMSIZ olmalı (yalnız belge değil).
        cmd.CommandText = "SELECT created_at FROM stock_movements ORDER BY rowid DESC LIMIT 1;";
        Assert.Equal(Simdi, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    /// <summary>Saat ilerlese bile iş günü sabit kalır; ikisi bağımsız değişkendir.</summary>
    [Fact]
    public void TRH12_Saat_Ilerleyince_Is_Gunu_Degismez()
    {
        _clock.Advance(5 * 3_600_000);   // 5 saat sonra kaydediliyor
        _stock.ReceiveIn(_yetkili, new[] { new StockLine(_mat, 1m) }, Op(), branchId: _depo, docDate: Gecmis);

        var (docDate, createdAt) = BelgeTarihleri();
        Assert.Equal(Gecmis, docDate);
        Assert.Equal(Simdi + 5 * 3_600_000, createdAt);
    }
}
