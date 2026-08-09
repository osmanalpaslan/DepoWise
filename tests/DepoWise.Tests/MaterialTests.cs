using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

public class MaterialTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;

    public MaterialTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mat_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private SessionContext Admin(string company)
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(company, "admin_" + company, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    // ---- Kod benzersizliği + tenant ----
    [Fact]
    public void Kod_AyniFirmada_Benzersiz()
    {
        var a = Admin("A");
        _materials.Create(a, new NewMaterial("M-001", "Filtre"));
        Assert.Throws<InvalidOperationException>(() => _materials.Create(a, new NewMaterial("M-001", "Başka")));
        Assert.False(_materials.IsCodeUnique(a, "M-001"));
        Assert.True(_materials.IsCodeUnique(a, "M-002"));
    }

    [Fact]
    public void Kod_FarkliFirmada_CakismdazVeTenantIzole()
    {
        var a = Admin("A"); var b = Admin("B");
        _materials.Create(a, new NewMaterial("M-001", "A-Filtre"));
        // Aynı kod B firmasında serbest
        Assert.False(string.IsNullOrEmpty(_materials.Create(b, new NewMaterial("M-001", "B-Filtre"))));
        // Tenant izolasyonu: A listesinde B yok
        var listA = _materials.List(a, new PageRequest { Limit = 50 });
        Assert.Single(listA.Items);
        Assert.All(listA.Items, m => Assert.Equal("A", m.CompanyId));
    }

    [Fact]
    public void Para_GecersizCurrency_Reddedilir()
    {
        var a = Admin("A");
        Assert.Throws<ArgumentException>(() => _materials.Create(a, new NewMaterial("X", "X", Currency: "GBP")));
        var id = _materials.Create(a, new NewMaterial("Y", "Y", UnitPrice: 12.34m, Currency: "USD"));
        var rec = _materials.List(a, new PageRequest { Limit = 50 }).Items.First(m => m.Id == id);
        Assert.Equal(12.34m, rec.UnitPrice);
        Assert.Equal("USD", rec.Currency);
    }

    // ---- ADR-086: min stok / birim fiyat NEGATİF olamaz (yalnız AÇILIŞ stoğu negatif olabilir) ----
    [Fact]
    public void NegatifFiyatVeMinStok_Reddedilir_AcilisStogu_Serbest()
    {
        var a = Admin("A");
        // Create: negatif birim fiyat / min stok → ArgumentException (API'de 400)
        Assert.Throws<ArgumentException>(() => _materials.Create(a, new NewMaterial("N-1", "Neg", UnitPrice: -5m)));
        Assert.Throws<ArgumentException>(() => _materials.Create(a, new NewMaterial("N-2", "Neg", MinStock: -1m)));
        // Geçerli (0 ve pozitif) → kabul
        var id = _materials.Create(a, new NewMaterial("N-3", "OK", MinStock: 0m, UnitPrice: 10m));
        Assert.False(string.IsNullOrEmpty(id));
        // Update: negatif → red
        Assert.Throws<ArgumentException>(() => _materials.Update(a, id, new UpdateMaterial("N-3", "OK", UnitPrice: -1m)));
        Assert.Throws<ArgumentException>(() => _materials.Update(a, id, new UpdateMaterial("N-3", "OK", MinStock: -1m)));
        // ADR-086 İSTİSNASI: AÇILIŞ stoğu NEGATİF olabilir (devralınan eksik stok) → hata FIRLATMAZ
        _opening.RecordOpening(a, id, -50m, Guid.NewGuid().ToString("N"));
    }

    // ---- Muadil (çift yönlü, döngü güvenli) ----
    [Fact]
    public void Muadil_CiftYonlu()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Bir"));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "İki"));
        _materials.AddEquivalent(a, m1, m2);

        Assert.Contains(m2, _materials.GetEquivalentGroup(m1));
        Assert.Contains(m1, _materials.GetEquivalentGroup(m2)); // ters yön de görünür
    }

    [Fact]
    public void Muadil_Kendine_Reddedilir()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Bir"));
        Assert.Throws<InvalidOperationException>(() => _materials.AddEquivalent(a, m1, m1));
    }

    [Fact]
    public void Muadil_Dongu_Guvenli_BFS_Sonlanir()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Bir"));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "İki"));
        var m3 = _materials.Create(a, new NewMaterial("M-3", "Üç"));
        // Döngü: 1-2, 2-3, 3-1 (her biri simetrik)
        _materials.AddEquivalent(a, m1, m2);
        _materials.AddEquivalent(a, m2, m3);
        _materials.AddEquivalent(a, m3, m1);

        var group = _materials.GetEquivalentGroup(m1);
        Assert.Equal(2, group.Count); // m2, m3 (kendisi hariç) — sonsuz döngü yok
        Assert.Contains(m2, group);
        Assert.Contains(m3, group);
    }

    [Fact]
    public void Muadil_BaskaFirmaMalzemesi_Reddedilir()
    {
        var a = Admin("A"); var b = Admin("B");
        var ma = _materials.Create(a, new NewMaterial("MA", "A"));
        var mb = _materials.Create(b, new NewMaterial("MB", "B"));
        Assert.Throws<ForbiddenException>(() => _materials.AddEquivalent(a, ma, mb));
    }

    // ---- Uyumlu araç + stok gösterimi ----
    [Fact]
    public void UyumluArac_DetayiMalzemeStogunuGosterir()
    {
        var a = Admin("A");
        var m1 = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "Yağ"));

        // İŞ C (2026-08-09): araç artık GERÇEK ve firmaya ait olmalı. Bu test eskiden uydurma bir
        // "VH-1" metni kullanıyordu — Migration005'teki "vehicle_id şimdilik serbest metin referans"
        // döneminden kalma. Artık SetCompatibleVehicles araç sahipliğini doğruluyor; testin İDDİASI
        // (uyumlu araç → malzeme stoğu gösterimi) değişmedi, yalnız seed gerçek veriye çekildi.
        var vehicleId = new DepoWise.Infrastructure.Vehicles.VehicleService(_factory, _clock)
            .Create(a, new DepoWise.Infrastructure.Vehicles.NewVehicle("VH-1"));
        _materials.SetCompatibleVehicles(a, m1, new[] { vehicleId });
        _materials.SetCompatibleVehicles(a, m2, new[] { vehicleId });

        // Açılış stoğu (ledger üzerinden)
        _opening.RecordOpening(a, m1, 10m, "op-1");
        _opening.RecordOpening(a, m2, 5m, "op-2");

        var forVehicle = _materials.MaterialsForVehicle(a, vehicleId);
        Assert.Equal(2, forVehicle.Count);
        Assert.Equal(10m, forVehicle.First(x => x.MaterialId == m1).Quantity);
        Assert.Equal(5m, forVehicle.First(x => x.MaterialId == m2).Quantity);
    }

    // ---- Açılış stoğu ledger'da ----
    [Fact]
    public void AcilisStogu_HareketDefterindeGorunur_VeBakiyeyiGunceller()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(a, m, 25m, "op-100", unitPrice: 3.5m, currency: "TRY");

        // Hareket defterinde 'opening' kaydı var
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT movement_type, direction, quantity FROM stock_movements WHERE material_id=@m;";
        cmd.AddWithValue("@m", m);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal("opening", r.GetString(0));
        Assert.Equal(1L, r.GetInt64(1));
        Assert.Equal(25m, Money.Parse(r.GetString(2)));

        Assert.Equal(25m, _opening.GetBalance(a, m));
    }

    [Fact]
    public void AcilisStogu_Idempotent_TekrarGonderim_CiftYazmaz()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(a, m, 25m, "op-100");
        _opening.RecordOpening(a, m, 25m, "op-100"); // aynı operation_id → no-op

        Assert.Equal(25m, _opening.GetBalance(a, m)); // 50 değil
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE material_id=@m;";
        cmd.AddWithValue("@m", m);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void AcilisStogu_DenyByDefault()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        var noPerm = new SessionContext("u-noperm", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _opening.RecordOpening(noPerm, m, 5m, "op-x"));
    }

    // ---- ADR-089: "Tür" harf duyarsız kanonik biçime çevrilir (içe aktarım "YEDEK PARÇA" bug'ı) ----
    [Theory]
    [InlineData("YEDEK PARÇA", "Yedek Parça")]
    [InlineData("yedek parça", "Yedek Parça")]
    [InlineData("SARF MALZEME", "Sarf Malzeme")]
    [InlineData("lastik", "Lastik")]
    public void Tur_HarfDuyarsizKanonikBicimeCevrilir(string input, string expected)
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Malzeme", Type: input));
        Assert.Equal(expected, _materials.GetDetail(a, m).Type);
    }

    [Fact]
    public void Tur_BilinmeyenOzelTur_OlduguGibiKalir()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Malzeme", Type: "Özel Kategori"));
        Assert.Equal("Özel Kategori", _materials.GetDetail(a, m).Type);   // serbest metin — kısıtlanmaz
    }

    [Fact]
    public void Tur_Duzenlemede_DeNormalizeEdilir()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Malzeme", Type: "Yedek Parça"));
        _materials.Update(a, m, new UpdateMaterial("M-1", "Malzeme", Type: "HAMMADDE"));
        Assert.Equal("Hammadde", _materials.GetDetail(a, m).Type);
    }

    /// <summary>Migration048: ZATEN kaydedilmiş yanlış-harfli tür değerlerini bir kez düzeltir (mevcut veri).</summary>
    [Fact]
    public void Migration048_MevcutYanlisHarfliTur_Duzeltir()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Malzeme", Type: "Yedek Parça"));
        // Servis normalize ettiği için ham UPDATE ile "bozuk" eski durumu taklit et.
        using (var conn = _factory.Create())
        using (var raw = conn.CreateCommand())
        {
            raw.CommandText = "UPDATE materials SET type='YEDEK PARÇA' WHERE id=@id;";
            raw.AddWithValue("@id", m);
            raw.ExecuteNonQuery();
        }

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            new DepoWise.Infrastructure.Database.Migrations.Migration048_NormalizeMaterialType().Up(conn, tx);
            tx.Commit();
        }

        Assert.Equal("Yedek Parça", _materials.GetDetail(a, m).Type);
    }

    // ---- ADR-086: NEGATİF açılış stoğu (firma devralırken mevcut/eksik stoğunu girer) ----
    [Fact]
    public void AcilisStogu_Negatif_YonEksi_MiktarPozitifSaklanir_BakiyeNegatif()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(a, m, -9m, "op-neg");

        // LEDGER SÖZLEŞMESİ: quantity DAİMA pozitif; işaret direction'da (senkron negatif-değer kalkanı geçilsin).
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT direction, quantity FROM stock_movements WHERE material_id=@m;";
        cmd.AddWithValue("@m", m);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(-1L, r.GetInt64(0));
        Assert.Equal(9m, Money.Parse(r.GetString(1)));   // pozitif saklandı
        Assert.Equal(-9m, _opening.GetBalance(a, m));     // türetilmiş bakiye negatif
    }

    [Fact]
    public void AcilisStogu_Sifir_Reddedilir()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        Assert.Throws<ArgumentException>(() => _opening.RecordOpening(a, m, 0m, "op-zero"));
    }

    /// <summary>Çok makineli senkron tutarlılığı: RecomputeBalances (Σ yön×miktar) negatif açılışta da
    /// bakiyeyi DOĞRU üretir — push sonrası sunucu-otoriteli yeniden hesap negatif değeri korur.</summary>
    [Fact]
    public void AcilisStogu_Negatif_RecomputeBalances_AyniNegatifDeger()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(a, m, -9m, "op-neg");

        new StockService(_factory, _clock).RecomputeBalances("A");   // hareketlerden yeniden hesapla

        Assert.Equal(-9m, _opening.GetBalance(a, m));
    }

    // ---- Tanımlar ----
    [Fact]
    public void Tanimlar_Ekle_VeTenantListe()
    {
        var a = Admin("A");
        var look = new LookupService(_factory, _clock);
        look.AddUnit(a, "Adet");
        look.AddBrand(a, "Bosch");
        var cat = look.AddCategory(a, "Filtreler");
        look.AddCategory(a, "Yağ Filtresi", parentId: cat);

        Assert.Single(look.List(a, "units"));
        Assert.Equal(2, look.List(a, "material_categories").Count);
        Assert.Empty(look.List(Admin("B"), "units")); // tenant izole

        // ListCategories: üst seviye vs alt kategori (parent filtresi)
        var tops = look.ListCategories(a);
        Assert.Single(tops);                 // yalnız "Filtreler" (üst)
        Assert.Equal("Filtreler", tops[0].Name);
        var subs = look.ListCategories(a, cat);
        Assert.Single(subs);                 // "Yağ Filtresi" (alt)
        Assert.Equal("Yağ Filtresi", subs[0].Name);

        // ListBrands: material türü (brand_type null/material) gelir
        Assert.Single(look.ListBrands(a, "material"));
    }

    // ---- Detay (GetDetail) ----
    [Fact]
    public void Detay_AlanlarVeMuadiller_Doner()
    {
        var a = Admin("A");
        var look = new LookupService(_factory, _clock);
        var unit = look.AddUnit(a, "Adet");
        var cat = look.AddCategory(a, "Filtreler");

        var m1 = _materials.Create(a, new NewMaterial("M-1", "Yağ Filtresi", CategoryId: cat, UnitId: unit, MinStock: 5m, UnitPrice: 100m));
        var m2 = _materials.Create(a, new NewMaterial("M-2", "Muadil Filtre"));
        _materials.AddEquivalent(a, m1, m2);

        var d = _materials.GetDetail(a, m1);
        Assert.Equal("Yağ Filtresi", d.Name);
        Assert.Equal("Filtreler", d.CategoryName);
        Assert.Equal("Adet", d.UnitName);
        Assert.Equal(5m, d.MinStock);
        Assert.Contains(d.Equivalents, e => e.Code == "M-2");
    }

    // ---- Güncelle / Sil ----
    [Fact]
    public void Guncelle_VeSil_Calisir()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-9", "Eski Ad", MinStock: 1m, UnitPrice: 10m));

        _materials.Update(a, m, new UpdateMaterial(Code: "M-9", Name: "Yeni Ad", MinStock: 7m, UnitPrice: 25m));
        var d = _materials.GetDetail(a, m);
        Assert.Equal("Yeni Ad", d.Name);
        Assert.Equal(7m, d.MinStock);
        Assert.Equal(25m, d.UnitPrice);

        _materials.Delete(a, m);
        Assert.Throws<ForbiddenException>(() => _materials.GetDetail(a, m));
    }

    // ---- DÜZENLEME KİLİDİ (2026-07-22): sessiz üzerine yazma engellenir ----

    private long Version(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM materials WHERE id=@i;";
        cmd.AddWithValue("@i", materialId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void DuzenlemeKilidi_EskiSurumle_Kaydetmek_UzerineYazmaz()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-100", "Filtre"));
        var acilistakiSurum = Version(m); // Kullanıcı-1 formu açtı

        // Kullanıcı-2 (ya da eşitlemeyle gelen başka makine) arada kaydı değiştirdi.
        _materials.Update(a, m, new UpdateMaterial("M-100", "Kullanici2 Adi"), expectedVersion: acilistakiSurum);

        // Kullanıcı-1 hâlâ ESKİ sürümü tutuyor → kaydetmesi ENGELLENMELİ.
        var ex = Assert.Throws<ConcurrencyException>(() =>
            _materials.Update(a, m, new UpdateMaterial("M-100", "Kullanici1 Adi"), expectedVersion: acilistakiSurum));
        Assert.Equal(acilistakiSurum, ex.ExpectedVersion);
        Assert.True(ex.ActualVersion > ex.ExpectedVersion);

        // Kullanıcı-2'nin verisi KORUNDU (sessizce ezilmedi).
        Assert.Equal("Kullanici2 Adi", _materials.GetDetail(a, m).Name);
    }

    [Fact]
    public void DuzenlemeKilidi_GuncelSurumle_Kaydetmek_Calisir()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-101", "Filtre"));

        _materials.Update(a, m, new UpdateMaterial("M-101", "Yeni Ad"), expectedVersion: Version(m));
        Assert.Equal("Yeni Ad", _materials.GetDetail(a, m).Name);

        // Sürüm verilmezse eski davranış korunur (geriye uyumluluk — çalışan çağrılar bozulmaz).
        _materials.Update(a, m, new UpdateMaterial("M-101", "Surumsuz Ad"));
        Assert.Equal("Surumsuz Ad", _materials.GetDetail(a, m).Name);
    }

    [Fact]
    public void DuzenlemeKilidi_OlmayanKayit_Surumle_YineForbidden()
    {
        var a = Admin("A");
        // Kayıt yok → sürüm hatası değil, yetki/bulunamadı hatası dönmeli (mesajlar karışmasın).
        Assert.Throws<ForbiddenException>(() =>
            _materials.Update(a, "yok-boyle-id", new UpdateMaterial("M-999", "Yok"), expectedVersion: 1));
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
