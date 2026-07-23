using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// İÇE AKTARIM — TAM ALAN kapsamı + HACİM (kullanıcı kuralı 2026-07-16).
///
/// Kullanıcı şartları:
///  • "içeri alma şablonlarında yeni kayıt formunda bulunan her alan olmalı (fotoğraf hariç)"
///  • "hiçbir alana tanım ekleme, ben içeri aldığımda tanımlar oluşacak" → otomatik tanım oluşturma
///  • "babamın bir dosyası 2600 civarında, bu tutarın altında kayıt ekleme testi yapma"
///    → hacim testleri 2600'ÜN ÜSTÜNDE (3000) satırla çalışır; amaç hem doğruluk hem SÜRE.
/// </summary>
public class ImportFullFieldsTests : IDisposable
{
    /// <summary>Kullanıcının gerçek dosyası ~2600 satır → testler bunun ÜSTÜNDE olmalı.</summary>
    private const int VolumeRows = 3000;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly VehicleService _vehicles;
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly LookupService _lookups;
    private readonly MaintenanceService _maint;
    private readonly MaintenanceDefinitionService _defs;
    private readonly InspectionService _inspections;
    private readonly VehicleImportService _vimp;
    private readonly MaterialImportService _mimp;
    private readonly MaintenanceImportService _mtimp;
    private readonly InspectionImportService _iimp;
    private readonly SessionContext _admin;

    public ImportFullFieldsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_impfull_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _vehicles = new VehicleService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _lookups = new LookupService(_factory, _clock);
        _maint = new MaintenanceService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _inspections = new InspectionService(_factory, _clock);
        _vimp = new VehicleImportService(_vehicles, _lookups);
        _mimp = new MaterialImportService(_materials, _lookups, _opening, _vehicles);
        _mtimp = new MaintenanceImportService(_maint, _defs, _vehicles, _lookups);
        _iimp = new InspectionImportService(_inspections, _vehicles);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static ImportRow Row(int n, params (string Col, string? Val)[] cells)
        => new(n, cells.ToDictionary(c => c.Col, c => c.Val));

    // ══════════════ ŞABLON = FORM (kullanıcı kuralı: her form alanı şablonda olmalı) ══════════════

    /// <summary>Araç şablonu, YENİ KAYIT FORMUNDAKİ her alanı içermeli (fotoğraf + şablon hariç).</summary>
    [Fact]
    public void AracSablonu_FormdakiTumAlanlariIcerir()
    {
        var h = _vimp.SampleHeaders();
        foreach (var expected in new[]
        {
            "İç Kod", "Plaka", "Üretim Yılı", "Durum", "Durum Açıklaması", "Sayaç", "Birim",
            "Makine Tipi", "Kategori", "Marka", "Model", "Şantiye / Şube", "Sürücü", "Şasi No", "Motor No",
        })
            Assert.Contains(expected, h);
    }

    /// <summary>Malzeme şablonu, YENİ KAYIT FORMUNDAKİ her alanı içermeli (fotoğraf + şablon hariç).</summary>
    [Fact]
    public void MalzemeSablonu_FormdakiTumAlanlariIcerir()
    {
        var h = _mimp.SampleHeaders();
        foreach (var expected in new[]
        {
            "Kod", "Ad", "Tür", "Kategori", "Alt Kategori", "Birim", "Marka", "Tedarikçi",
            "Uyumlu Araçlar", "Muadil Malzeme", "Birim Fiyat", "Min Stok", "Açılış Stok", "Açıklama",
        })
            Assert.Contains(expected, h);
    }

    [Fact]
    public void BakimSablonu_AltBakimVeTeknisyenIcerir()
    {
        var h = _mtimp.SampleHeaders();
        Assert.Contains("Alt Bakım", h);
        Assert.Contains("Teknisyen", h);
    }

    [Fact]
    public void MuayeneSablonu_ErtelemeVeAciklamaIcerir()
    {
        var h = _iimp.SampleHeaders();
        Assert.Contains("Erteleme Tarihi", h);
        Assert.Contains("Açıklama", h);
    }

    // ══════════════ ARAÇ — tüm alanlar + otomatik tanım oluşturma ══════════════

    [Fact]
    public void Arac_TumAlanlar_DoluAktarilir_TanimlarOtomatikOlusur()
    {
        var (res, created) = _vimp.CommitWithLookups(_admin, new[]
        {
            Row(2,
                ("İç Kod", "AR-1"), ("Plaka", "34 ABC 123"), ("Üretim Yılı", "2020"),
                ("Durum", "Arızalı"), ("Durum Açıklaması", "Motor arızası"),
                ("Sayaç", "12500,5"), ("Birim", "km"),
                ("Makine Tipi", "Kamyon"), ("Kategori", "Ağır Vasıta"),
                ("Marka", "Mercedes"), ("Model", "Actros"),
                ("Şantiye / Şube", "Merkez Şantiye"), ("Sürücü", "Ahmet Yılmaz"),
                ("Şasi No", "SASI-1"), ("Motor No", "MOTOR-1")),
        });

        Assert.Equal(1, res.Added);
        Assert.Equal(0, res.Failed);

        var v = _vehicles.List(_admin).Single();
        var d = _vehicles.Get(_admin, v.Id);
        Assert.Equal("AR-1", d.InternalCode);
        Assert.Equal("34 ABC 123", d.Plate);
        Assert.Equal(2020, d.ProductionYear);
        Assert.Equal(VehicleStatus.Faulty, d.Status);
        Assert.Equal("Motor arızası", d.StatusNote);      // Arızalı'da not SAKLANIR
        Assert.Equal(12500.5m, d.CurrentMeter);           // virgüllü ondalık bozulmadı
        Assert.Equal("km", d.MeterUnit);
        Assert.Equal("Kamyon", d.VehicleTypeName);
        Assert.Equal("Ağır Vasıta", d.CategoryName);
        Assert.Equal("Mercedes", d.BrandName);
        Assert.Equal("Actros", d.VehicleModelName);
        Assert.Equal("Merkez Şantiye", d.BranchName);
        Assert.Equal("Ahmet Yılmaz", d.DriverName);
        Assert.Equal("SASI-1", d.ChassisNo);
        Assert.Equal("MOTOR-1", d.EngineNo);

        // Tanımlar OTOMATİK oluştu ve raporlandı (kullanıcı yazım hatalarını görebilsin).
        Assert.Contains(created, x => x.Contains("Kamyon"));
        Assert.Contains(created, x => x.Contains("Mercedes"));
        Assert.Contains(created, x => x.Contains("Actros"));
        Assert.Contains(created, x => x.Contains("Merkez Şantiye"));
        Assert.Contains(created, x => x.Contains("Ahmet Yılmaz"));
    }

    /// <summary>Aynı tanım adı farklı satırlarda/yazımlarda geçerse TEK tanım olur (harf/boşluk duyarsız).</summary>
    [Fact]
    public void Arac_AyniTanimFarkliYazim_TekTanimOlur()
    {
        var (_, created) = _vimp.CommitWithLookups(_admin, new[]
        {
            Row(2, ("İç Kod", "A1"), ("Marka", "Caterpillar")),
            Row(3, ("İç Kod", "A2"), ("Marka", "CATERPILLAR")),
            Row(4, ("İç Kod", "A3"), ("Marka", " caterpillar ")),
        });

        Assert.Equal(1, created.Count(x => x.Contains("Marka", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(_lookups.ListBrands(_admin, "vehicle"));
    }

    /// <summary>GERÇEK yazım hatası ayrı tanım olur — bu KAÇINILMAZ; test bunu belgeliyor ki
    /// kullanıcı "oluşan tanımlar" raporuna bakmanın neden gerekli olduğunu bilsin.</summary>
    [Fact]
    public void Arac_YazimHatasi_AyriTanimOlur_RaporlanirKiGorulsun()
    {
        var (_, created) = _vimp.CommitWithLookups(_admin, new[]
        {
            Row(2, ("İç Kod", "A1"), ("Marka", "Caterpillar")),
            Row(3, ("İç Kod", "A2"), ("Marka", "Caterpiller")),   // yazım hatası
        });

        Assert.Equal(2, _lookups.ListBrands(_admin, "vehicle").Count);
        Assert.Equal(2, created.Count(x => x.Contains("Marka", StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("Aktif", "active")]
    [InlineData("aktif", "active")]
    [InlineData("Pasif", "passive")]
    [InlineData("Bakımda", "maintenance")]
    [InlineData("bakimda", "maintenance")]
    [InlineData("Arızalı", "faulty")]
    [InlineData("arizali", "faulty")]
    [InlineData("Arıza", "faulty")]
    [InlineData("Bozuk", "faulty")]
    [InlineData("", "active")]        // boş → aktif (araç varsayılan çalışır)
    public void Arac_DurumMetinleri_DogruKodaCevrilir(string text, string expected)
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Durum", text)) });
        Assert.Equal(expected, _vehicles.List(_admin).Single().Status);
    }

    /// <summary>Tanınmayan durum SESSİZCE "aktif" yazılmamalı — satır reddedilmeli (yanlış durum = yanlış veri).</summary>
    [Fact]
    public void Arac_TaninmayanDurum_SatirReddedilir()
    {
        var dry = _vimp.DryRun(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Durum", "zımbırtı")) });
        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("Geçersiz Durum"));
    }

    /// <summary>Aktif durumda "Durum Açıklaması" saklanmaz (form ve servis kuralı aynı).</summary>
    [Fact]
    public void Arac_AktifDurumda_DurumAciklamasiSaklanmaz()
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Durum", "Aktif"), ("Durum Açıklaması", "yazılmamalı")) });
        var v = _vehicles.List(_admin).Single();
        Assert.Null(_vehicles.Get(_admin, v.Id).StatusNote);
    }

    [Theory]
    [InlineData("1949")]   // MinVehicleYear altı
    [InlineData("3000")]   // gelecek
    [InlineData("abc")]
    public void Arac_GecersizUretimYili_SatirReddedilir(string year)
    {
        var dry = _vimp.DryRun(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Üretim Yılı", year)) });
        Assert.Equal(0, dry.Valid);
        Assert.Equal(1, dry.Failed);
    }

    [Fact]
    public void Arac_NegatifSayac_SatirReddedilir()
    {
        var dry = _vimp.DryRun(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Sayaç", "-5")) });
        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("negatif"));
    }

    [Theory]
    [InlineData("km", "km")]
    [InlineData("saat", "hour")]
    [InlineData("Saat", "hour")]
    [InlineData("", "km")]
    public void Arac_SayacBirimi_DogruCevrilir(string text, string expected)
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Birim", text)) });
        Assert.Equal(expected, _vehicles.List(_admin).Single().MeterUnit);
    }

    [Fact]
    public void Arac_GecersizBirim_SatirReddedilir()
    {
        var dry = _vimp.DryRun(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Birim", "mil")) });
        Assert.Equal(0, dry.Valid);
    }

    /// <summary>Aynı iç kod DOSYA İÇİNDE iki kez → ikincisi uyarılır (DB'de yok ama dosyada tekrar var).</summary>
    [Fact]
    public void Arac_DosyaIcindeTekrarEdenKod_DryRunYakalar()
    {
        var dry = _vimp.DryRun(_admin, new[]
        {
            Row(2, ("İç Kod", "A1")),
            Row(3, ("İç Kod", "A1")),
        });
        Assert.Equal(1, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("birden çok kez"));
    }

    /// <summary>Aynı dosya iki kez aktarılırsa araç TEKRARLANMAZ (iç kod benzersiz → atlanır).</summary>
    [Fact]
    public void Arac_AyniDosyaIkiKez_TekrarlanmazAtlanir()
    {
        var rows = new[] { Row(2, ("İç Kod", "A1"), ("Marka", "Ford")) };
        var first = _vimp.Commit(_admin, rows);
        var second = _vimp.Commit(_admin, rows);

        Assert.Equal(1, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Updated);              // "zaten vardı, atlandı"
        Assert.Single(_vehicles.List(_admin));
        Assert.Single(_lookups.ListBrands(_admin, "vehicle"));   // marka da tekrar oluşmadı
    }

    /// <summary>Markasız model oluşturulamaz (modelin ebeveyni zorunlu) — satır modelsiz geçer, PATLAMAZ.</summary>
    [Fact]
    public void Arac_MarkasizModel_SatirGecerModelsiz()
    {
        var res = _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "A1"), ("Model", "Actros")) });
        Assert.Equal(1, res.Added);
        var v = _vehicles.List(_admin).Single();
        Assert.Null(_vehicles.Get(_admin, v.Id).VehicleModelName);
    }

    // ══════════════ MALZEME — tüm alanlar ══════════════

    [Fact]
    public void Malzeme_TumAlanlar_DoluAktarilir()
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "AR-1"), ("Plaka", "34 XYZ 99")) });

        var (res, created) = _mimp.CommitWithLookups(_admin, new[]
        {
            Row(2,
                ("Kod", "M-1"), ("Ad", "Yağ Filtresi"), ("Tür", "Yedek Parça"),
                ("Kategori", "Filtreler"), ("Alt Kategori", "Yağ Filtreleri"),
                ("Birim", "Adet"), ("Marka", "Bosch"), ("Tedarikçi", "ABC Ltd"),
                ("Uyumlu Araçlar", "AR-1"), ("Birim Fiyat", "125,75"), ("Min Stok", "5"),
                ("Açılış Stok", "20"), ("Açıklama", "Test açıklaması")),
        });

        Assert.Equal(1, res.Added);
        Assert.Equal(0, res.Failed);

        var m = _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single();
        Assert.Equal("M-1", m.Code);
        Assert.Equal(125.75m, m.UnitPrice);     // virgüllü ondalık bozulmadı
        Assert.Equal(5m, m.MinStock);

        Assert.Contains(created, x => x.Contains("Filtreler"));
        Assert.Contains(created, x => x.Contains("Yağ Filtreleri"));
        Assert.Contains(created, x => x.Contains("Adet"));
        Assert.Contains(created, x => x.Contains("Bosch"));
        Assert.Contains(created, x => x.Contains("ABC Ltd"));
    }

    /// <summary>Uyumlu araç PLAKA ile de eşlenmeli (Excel'de plaka yazar).</summary>
    [Fact]
    public void Malzeme_UyumluArac_PlakaIleEslesir()
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "AR-1"), ("Plaka", "34 XYZ 99")) });
        var vid = _vehicles.List(_admin).Single().Id;

        _mimp.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Uyumlu Araçlar", "34xyz99")) });

        var mid = _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single().Id;
        Assert.Contains(vid, new StockServiceProbe(_factory).CompatibleVehicleIds(mid));
    }

    /// <summary>Uyumlu araç bulunamazsa malzeme yine de EKLENİR (araç sonra aktarılacak olabilir).</summary>
    [Fact]
    public void Malzeme_BulunamayanUyumluArac_MalzemeYineEklenir()
    {
        var res = _mimp.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Uyumlu Araçlar", "YOK-ARAC")) });
        Assert.Equal(1, res.Added);
        Assert.Equal(0, res.Failed);
    }

    /// <summary>Muadil İLERİ REFERANS: A'nın muadili B ama B dosyada A'dan SONRA geliyor → 2. turda bağlanır.</summary>
    [Fact]
    public void Malzeme_MuadilIleriReferans_BaglanabilirOlmali()
    {
        _mimp.Commit(_admin, new[]
        {
            Row(2, ("Kod", "M-1"), ("Ad", "Filtre A"), ("Muadil Malzeme", "M-2")),   // M-2 henüz yok
            Row(3, ("Kod", "M-2"), ("Ad", "Filtre B")),
        });

        var items = _materials.List(_admin, new PageRequest { Limit = 10 }).Items;
        var m1 = items.Single(x => x.Code == "M-1");
        var m2 = items.Single(x => x.Code == "M-2");
        Assert.Contains(m2.Id, _materials.GetEquivalentGroup(m1.Id));
    }

    [Fact]
    public void Malzeme_AcilisStogu_StokHareketiOlusturur()
    {
        _mimp.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Açılış Stok", "20"), ("Birim Fiyat", "10")) });
        var mid = _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single().Id;
        Assert.Equal(20m, new StockServiceProbe(_factory).Balance(mid));
    }

    // ADR-086: negatif açılış stoğu (devralınan eksik stok) İÇE AKTARIMDA da kabul edilir.
    [Fact]
    public void Malzeme_NegatifAcilisStogu_KabulEdilir_BakiyeNegatif()
    {
        var dry = _mimp.DryRun(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Açılış Stok", "-9")) });
        Assert.Equal(1, dry.Valid);   // negatif açılış artık HATA değil
        Assert.Equal(0, dry.Failed);

        _mimp.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Açılış Stok", "-9")) });
        var mid = _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single().Id;

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=@m;";
        cmd.AddWithValue("@m", mid);
        Assert.Equal(-9m, Money.Parse(cmd.ExecuteScalar() as string));
    }

    // Fiyat/Min Stok negatif OLAMAZ (yalnız "stok" negatif olabilir — eşik/tutar değil).
    [Fact]
    public void Malzeme_NegatifBirimFiyat_SatirReddedilir()
    {
        var dry = _mimp.DryRun(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Birim Fiyat", "-5")) });
        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("negatif"));
    }

    [Fact]
    public void Malzeme_AltKategori_KategoriYoksaOlusmaz()
    {
        _mimp.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Alt Kategori", "Yağ Filtreleri")) });
        // Üst kategori verilmediği için alt kategori oluşturulamaz → malzeme kategorisiz eklenir.
        Assert.Equal(1, _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Count);
        Assert.Empty(_lookups.ListCategories(_admin, null));
    }

    [Fact]
    public void Malzeme_DosyaIcindeTekrarEdenKod_DryRunYakalar()
    {
        var dry = _mimp.DryRun(_admin, new[]
        {
            Row(2, ("Kod", "M-1"), ("Ad", "A")),
            Row(3, ("Kod", "M-1"), ("Ad", "B")),
        });
        Assert.Equal(1, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("birden çok kez"));
    }

    // ══════════════ BAKIM + MUAYENE — yeni alanlar ══════════════

    [Fact]
    public void Bakim_AltBakimVeTeknisyen_OtomatikOlusur()
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "AR-1"), ("Plaka", "34 AAA 11")) });

        var (res, created) = _mtimp.CommitWithLookups(_admin, new[]
        {
            Row(2, ("Araç", "34 AAA 11"), ("Bakım Tanımı", "Periyodik Bakım"),
                   ("Alt Bakım", "Yağ Değişimi"), ("Teknisyen", "Mehmet Usta"),
                   ("Yapılma KM", "15000"), ("Tarih", "01.05.2026"), ("Açıklama", "not")),
        });

        Assert.Equal(1, res.Added);
        Assert.Contains(created, x => x.Contains("Periyodik Bakım"));
        Assert.Contains(created, x => x.Contains("Yağ Değişimi"));
        Assert.Contains(created, x => x.Contains("Mehmet Usta"));
    }

    [Fact]
    public void Muayene_Ertelendi_ErtelemeTarihiSonrakiTariheYazilir()
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "AR-1")) });

        _iimp.Commit(_admin, new[]
        {
            Row(2, ("Araç", "AR-1"), ("Belge Tipi", "Muayene"), ("Sonraki Tarih", "01.01.2030"),
                   ("Sonuç", "Ertelendi"), ("Erteleme Tarihi", "15.06.2030"), ("Açıklama", "ertelendi notu")),
        });

        var row = _inspections.List(_admin).Single();
        // Ertelendi → sonraki tarih ERTELEME tarihidir (form ile aynı kural).
        // Tarihler YEREL saatle ayrıştırılır (mevcut tüm import'larla tutarlı) → beklenti de yerel kurulur.
        Assert.Equal(new DateTimeOffset(new DateTime(2030, 6, 15), TimeSpan.Zero).ToUnixTimeMilliseconds()
                     - (long)TimeZoneInfo.Local.GetUtcOffset(new DateTime(2030, 6, 15)).TotalMilliseconds,
                     row.NextDate);
    }

    [Fact]
    public void Muayene_ErtelendiAmaTarihYok_SatirReddedilir()
    {
        _vimp.Commit(_admin, new[] { Row(2, ("İç Kod", "AR-1")) });

        var dry = _iimp.DryRun(_admin, new[]
        {
            Row(2, ("Araç", "AR-1"), ("Belge Tipi", "Muayene"), ("Sonuç", "Ertelendi")),
        });
        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("Erteleme Tarihi zorunlu"));
    }

    // ══════════════ HACİM — kullanıcının dosyası ~2600 satır ══════════════

    /// <summary>
    /// 3000 ARAÇ (kullanıcının ~2600'lük dosyasının ÜSTÜ). Doğruluk + SÜRE ölçülür.
    /// Tanım çözücü önbelleklidir → satır başına DB sorgusu YOK; aksi halde bu test dakikalarca sürerdi.
    /// </summary>
    [Fact]
    public void Hacim_3000Arac_TamAlanlarla_MakulSuredeAktarilir()
    {
        var rows = new List<ImportRow>(VolumeRows);
        for (int i = 0; i < VolumeRows; i++)
        {
            rows.Add(Row(i + 2,
                ("İç Kod", $"V-{i:D5}"), ("Plaka", $"34 AB {i:D4}"), ("Üretim Yılı", (2000 + i % 25).ToString()),
                ("Durum", i % 4 == 0 ? "Arızalı" : "Aktif"), ("Durum Açıklaması", i % 4 == 0 ? "arıza" : ""),
                ("Sayaç", $"{1000 + i},5"), ("Birim", i % 2 == 0 ? "km" : "saat"),
                // 20 marka / 10 tip → çözücü aynı tanımı TEKRAR TEKRAR oluşturmamalı
                ("Makine Tipi", $"Tip-{i % 10}"), ("Kategori", $"Kat-{i % 5}"),
                ("Marka", $"Marka-{i % 20}"), ("Model", $"Model-{i % 30}"),
                ("Şantiye / Şube", $"Şantiye-{i % 8}"), ("Sürücü", $"Sürücü-{i % 50}"),
                ("Şasi No", $"S{i}"), ("Motor No", $"M{i}")));
        }

        var sw = Stopwatch.StartNew();
        var (res, created) = _vimp.CommitWithLookups(_admin, rows);
        sw.Stop();

        Assert.Equal(VolumeRows, res.Added);
        Assert.Equal(0, res.Failed);
        Assert.Equal(VolumeRows, _vehicles.List(_admin, null, int.MaxValue).Count);

        // Tanımlar TEKİL oluşmalı: 20 marka, 10 tip, 5 kategori, 8 şantiye, 50 sürücü.
        Assert.Equal(20, _lookups.ListBrands(_admin, "vehicle").Count);
        Assert.Equal(10, _lookups.List(_admin, "vehicle_types").Count);
        Assert.Equal(5, _lookups.List(_admin, "vehicle_categories").Count);
        Assert.Equal(50, _lookups.ListPersonnel(_admin).Count);
        // Model markaya bağlı: 30 model adı × 20 marka kombinasyonu → benzersiz (marka,model) çiftleri.
        Assert.Equal(20 + 10 + 5 + 8 + 50 + ExpectedModelCount(), created.Count);

        // Süre koruması: önbellek çalışmazsa (satır başına sorgu) bu eşik AŞILIR → regresyon yakalanır.
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(3), $"3000 araç {sw.Elapsed.TotalSeconds:0} sn sürdü — çok yavaş (önbellek bozulmuş olabilir).");
    }

    /// <summary>i%20 marka ve i%30 model → benzersiz (marka,model) çifti sayısı.</summary>
    private static int ExpectedModelCount()
        => Enumerable.Range(0, VolumeRows).Select(i => (i % 20, i % 30)).Distinct().Count();

    /// <summary>3000 MALZEME — tam alanlarla; kod benzersizliği + tanım tekilliği + süre.</summary>
    [Fact]
    public void Hacim_3000Malzeme_TamAlanlarla_MakulSuredeAktarilir()
    {
        var rows = new List<ImportRow>(VolumeRows);
        for (int i = 0; i < VolumeRows; i++)
        {
            rows.Add(Row(i + 2,
                ("Kod", $"M-{i:D5}"), ("Ad", $"Malzeme {i}"), ("Tür", "Yedek Parça"),
                ("Kategori", $"Kat-{i % 10}"), ("Alt Kategori", $"Alt-{i % 20}"),
                ("Birim", i % 2 == 0 ? "Adet" : "Kg"), ("Marka", $"MMarka-{i % 15}"),
                ("Tedarikçi", $"Tedarikçi-{i % 12}"),
                ("Birim Fiyat", $"{100 + i % 50},25"), ("Min Stok", (i % 10).ToString()),
                ("Açıklama", $"açıklama {i}")));
        }

        var sw = Stopwatch.StartNew();
        var (res, _) = _mimp.CommitWithLookups(_admin, rows);
        sw.Stop();

        Assert.Equal(VolumeRows, res.Added);
        Assert.Equal(0, res.Failed);
        Assert.Equal(2, _lookups.List(_admin, "units").Count);
        Assert.Equal(15, _lookups.ListBrands(_admin, "material").Count);
        Assert.Equal(12, _lookups.List(_admin, "suppliers").Count);
        Assert.Equal(10, _lookups.ListCategories(_admin, null).Count);
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(3), $"3000 malzeme {sw.Elapsed.TotalSeconds:0} sn sürdü — çok yavaş.");
    }

    /// <summary>3000 satırlık dosyada BOZUK satırlar varsa: sağlamlar girer, bozuklar atlanır,
    /// hata listesi şişmez (MaxReportedErrors). Kullanıcının dosyası kısmen bozuk olabilir.</summary>
    [Fact]
    public void Hacim_3000Arac_BozukSatirlarKarisik_SaglamlarGirer()
    {
        var rows = new List<ImportRow>(VolumeRows);
        int expectedBad = 0;
        for (int i = 0; i < VolumeRows; i++)
        {
            if (i % 10 == 0) { rows.Add(Row(i + 2, ("İç Kod", ""), ("Marka", "X"))); expectedBad++; }        // kod yok
            else if (i % 10 == 1) { rows.Add(Row(i + 2, ("İç Kod", $"B-{i}"), ("Durum", "zımbırtı"))); expectedBad++; }  // geçersiz durum
            else rows.Add(Row(i + 2, ("İç Kod", $"V-{i:D5}"), ("Marka", "Ford")));
        }

        var res = _vimp.Commit(_admin, rows);

        Assert.Equal(VolumeRows - expectedBad, res.Added);
        Assert.Equal(expectedBad, res.Failed);
        Assert.Equal(VolumeRows - expectedBad, _vehicles.List(_admin, null, int.MaxValue).Count);
        // Hata listesi sınırlıdır (3000 hatalı satır ekranı kilitlemesin).
        Assert.True(res.Errors.Count <= ImportResult.MaxReportedErrors);
    }

    /// <summary>3000 satırlık dosya İKİ KEZ aktarılırsa hiçbir kayıt tekrarlanmaz.</summary>
    [Fact]
    public void Hacim_3000Arac_AyniDosyaIkiKez_Tekrarlanmaz()
    {
        var rows = Enumerable.Range(0, VolumeRows)
            .Select(i => Row(i + 2, ("İç Kod", $"V-{i:D5}"), ("Marka", $"Marka-{i % 20}")))
            .ToList();

        var first = _vimp.Commit(_admin, rows);
        var second = _vimp.Commit(_admin, rows);

        Assert.Equal(VolumeRows, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(VolumeRows, second.Updated);                 // hepsi "zaten vardı"
        Assert.Equal(VolumeRows, _vehicles.List(_admin, null, int.MaxValue).Count);
        Assert.Equal(20, _lookups.ListBrands(_admin, "vehicle").Count);   // tanımlar da tekrar oluşmadı
    }

    // ══════════════ REGRESYON: 200 SATIR SINIRI (hacim testinin YAKALADIĞI gerçek kusur) ══════════════

    /// <summary>
    /// ⚠️ REGRESYON — bu kusuru 3000 satırlık hacim testi ortaya çıkardı:
    /// <c>VehicleService.List</c> varsayılanı <b>200</b>, <c>PageRequest.MaxLimit</c> de <b>200</b>'dür.
    /// İçe aktarıcılar bunlara dayandığı için 200'den fazla aracı olan firmada:
    ///   • bakım/muayene/yakıt aktarımı 201. araçtan sonrasını "Araç bulunamadı" diye REDDEDİYORDU,
    ///   • araç/malzeme aktarımı mükerrer kontrolünü kaçırıp KOPYA oluşturuyordu.
    /// Kullanıcının dosyası ~2600 satır → bu kusur onun verisini bozardı.
    /// </summary>
    [Fact]
    public void Regresyon_250Arac_BakimAktarimi_200SonrasiniDaBulur()
    {
        // 250 araç (200 sınırının ÜSTÜ). İç kodlar sıralı → 201+ olanlar eski kodda "yok" sayılırdı.
        var vrows = Enumerable.Range(0, 250)
            .Select(i => Row(i + 2, ("İç Kod", $"V-{i:D4}"), ("Plaka", $"34 ZZ {i:D4}")))
            .ToList();
        Assert.Equal(250, _vimp.Commit(_admin, vrows).Added);

        // 201., 230. ve 249. araçlara bakım — hepsi bulunmalı.
        var res = _mtimp.Commit(_admin, new[]
        {
            Row(2, ("Araç", "V-0200"), ("Bakım Tanımı", "Yağ")),
            Row(3, ("Araç", "V-0230"), ("Bakım Tanımı", "Yağ")),
            Row(4, ("Araç", "34 ZZ 0249"), ("Bakım Tanımı", "Yağ")),   // plaka ile
        });

        Assert.Equal(3, res.Added);
        Assert.Equal(0, res.Failed);
    }

    /// <summary>250 aracın hepsi mükerrer kontrolünde görülmeli → 2. aktarımda HİÇBİRİ kopyalanmamalı.</summary>
    [Fact]
    public void Regresyon_250Arac_IkinciAktarimda_200SonrasiKopyalanmaz()
    {
        var rows = Enumerable.Range(0, 250)
            .Select(i => Row(i + 2, ("İç Kod", $"V-{i:D4}")))
            .ToList();

        _vimp.Commit(_admin, rows);
        var second = _vimp.Commit(_admin, rows);

        Assert.Equal(0, second.Added);
        Assert.Equal(250, second.Updated);   // hepsi "zaten vardı"
        Assert.Equal(250, _vehicles.List(_admin, null, int.MaxValue).Count);
    }

    /// <summary>250 malzemenin hepsi mükerrer kontrolünde görülmeli (PageRequest.MaxLimit=200 tuzağı).</summary>
    [Fact]
    public void Regresyon_250Malzeme_IkinciAktarimda_200SonrasiKopyalanmaz()
    {
        var rows = Enumerable.Range(0, 250)
            .Select(i => Row(i + 2, ("Kod", $"M-{i:D4}"), ("Ad", $"Malzeme {i}")))
            .ToList();

        _mimp.Commit(_admin, rows);
        var second = _mimp.Commit(_admin, rows);

        Assert.Equal(0, second.Added);
        Assert.Equal(250, second.Updated);
        Assert.Equal(250, _materials.AllCodeToId(_admin).Count);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}

/// <summary>Açılış stoğu doğrulaması için küçük yardımcı (stok bakiyesini doğrudan okur).</summary>
internal sealed class StockServiceProbe
{
    private readonly SqliteConnectionFactory _factory;
    public StockServiceProbe(SqliteConnectionFactory factory) => _factory = factory;

    public decimal Balance(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // stock_movements'ta is_deleted YOKTUR (hareket defteri silinmez; iptal ters kayıtla yapılır).
        cmd.CommandText = "SELECT COALESCE(SUM(CAST(quantity AS REAL)),0) FROM stock_movements WHERE material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    /// <summary>Malzemeye bağlı uyumlu araç id'leri (servis salt-okuma metodu sunmuyor → doğrudan sorgu).</summary>
    public List<string> CompatibleVehicleIds(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT vehicle_id FROM material_compatible_vehicles WHERE material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
