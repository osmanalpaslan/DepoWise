using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Equipment;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ AR-GE 2026-10-26 (Migration121) — B6 KAYITLARI İKİ BİLGİSAYAR + SUNUCU ARASINDA EŞİTLENİR ═══
/// B6'da eklenen tablolar eşitleme listesinde YOKTU: masaüstünde girilen günlük kontrol, ekipman taşıma geçmişi, zimmet iade tarihi,
/// tutanak ve kit yalnız o bilgisayarda kalıyordu (B6 testleri tek veritabanında koştuğu için görülmedi). Gerçek senkron uçlarıyla
/// (business-push / business-pull) iki yerel veritabanı + sunucu:
///  BE1 — bilgisayar 1'in çevrimdışı B6 kayıtları sunucuda ve bilgisayar 2'de aynen görünür (servisler üzerinden okunur)
///  BE2 — taşıma geçmişi ve günlük kontrol DEĞİŞTİRİLEMEZ: yerelde oynanıp gönderilse de sunucudaki kayıt aynı kalır
///  BE3 — günlük kontrol madde listesini eşitlemeyle de YALNIZ YÖNETİCİ değiştirir (modül yetkisi yetmez — servis kuralıyla aynı)
///  BE5 — malzeme kartından doğrudan stok değişikliği uyarı kaydı (Migration122) sunucuya ve bilgisayar 2'ye ulaşır
///  BE4 — liste kilidi: Migration121'in açtığı tablolar eşitleme listesinde, sayım taslağı bilinçli olarak dışarıda
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class B6EsitlemeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "B6-ES";
    private const string User = "b6_es";
    private const string Pass = "Test!2026";

    private Pc _pc1 = null!, _pc2 = null!;
    private string _uid = "", _subeA = "", _subeB = "", _ekipman = "", _arac = "", _personel = "", _malzeme = "";

    private sealed class Saat : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_760_000_000_000); }

    private sealed class Pc : IDisposable
    {
        public readonly string Yol;
        public readonly SqliteConnectionFactory Db;
        public readonly Saat Saat = new();
        public HttpClient Baglanti = null!;
        public SessionContext Oturum = null!;
        public Pc(string ad)
        {
            Yol = Path.Combine(Path.GetTempPath(), $"dw_b6es_{ad}_" + Guid.NewGuid().ToString("N") + ".db");
            Db = new SqliteConnectionFactory(Yol);
            new MigrationRunner(Db).Run();
        }
        public AssignmentService Zimmet => new(Db, Saat);
        public async Task GonderAsync()
        {
            var paket = new BusinessSyncService(Db, Saat).BuildSnapshot(Company);
            var r = await Baglanti.PostAsync("/api/sync/business-push", new StringContent(paket, System.Text.Encoding.UTF8, "application/json"));
            r.EnsureSuccessStatusCode();
        }
        public async Task CekAsync()
        {
            var r = await Baglanti.GetAsync("/api/sync/business-pull");
            r.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            new BusinessSyncService(Db, Saat).ApplyPull(Company, doc.RootElement, new HashSet<string>(StringComparer.Ordinal) { "stock_balances" });
        }
        public void Dispose() { try { File.Delete(Yol); } catch { } }
    }

    private ServerServices Sunucu => _host.Services.GetRequiredService<ServerServices>();
    private SessionContext Yonetici => new(_uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        using (var conn = Sunucu.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        _uid = Sunucu.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _subeA = Sunucu.Branches.Create(Yonetici, new NewBranch("Depo A"));
        _subeB = Sunucu.Branches.Create(Yonetici, new NewBranch("Şantiye B"));
        _ekipman = Sunucu.Equipment.Create(Yonetici, new NewEquipment("EKP-1", "Kırıcı", BranchId: _subeA));
        _arac = Sunucu.Vehicles.Create(Yonetici, new NewVehicle("ARC-1", "34B601", 2020, 100, "km", _subeA));
        _personel = Sunucu.Personnel.Create(Yonetici, new DepoWise.Infrastructure.Org.NewPersonnel("Veli Usta", null, null, _subeA));
        _malzeme = Sunucu.Materials.Create(Yonetici, new DepoWise.Infrastructure.Materials.NewMaterial("B6-M1", "Filtre"));
        _pc1 = await KurAsync("1");
        _pc2 = await KurAsync("2");
    }

    private async Task<Pc> KurAsync(string ad)
    {
        var pc = new Pc(ad);
        using (var conn = pc.Db.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,@c,1,1,1,0);" +
                "INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) VALUES(@a,@c,'Depo A','branch',1,1,1,0);" +
                "INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) VALUES(@b,@c,'Şantiye B','branch',1,1,1,0);";
            cmd.AddWithValue("@c", Company); cmd.AddWithValue("@a", _subeA); cmd.AddWithValue("@b", _subeB);
            cmd.ExecuteNonQuery();
        }
        pc.Oturum = new SessionContext(_uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);   // Tüm Şubeler (şubeler arası taşıma için)
        pc.Baglanti = await _host.LoginAsync(User, Pass, Company);
        await pc.CekAsync();
        return pc;
    }

    public async Task DisposeAsync()
    {
        _pc1?.Dispose(); _pc2?.Dispose();
        await ((IAsyncLifetime)_host).DisposeAsync();
    }

    private static string Op() => Guid.NewGuid().ToString("N");

    /// <summary>Bilgisayar 1: çevrimdışı tüm B6 kayıtları.</summary>
    private void Pc1CevrimdisiKayitlar()
    {
        var s = _pc1.Oturum;
        new EkipmanTasimaService(_pc1.Db, _pc1.Saat).Tasi(s, _ekipman, _subeB, "Kule vinç yanı", null, "şantiyeye gitti", Op());
        new GunlukKontrolService(_pc1.Db, _pc1.Saat).Kaydet(s, _arac, null, new[] { new KontrolSonucu("Yağ seviyesi", true), new KontrolSonucu("Fren", true) },
            "vardiya başı", arizaAc: false, Op());
        new GunlukKontrolService(_pc1.Db, _pc1.Saat).MaddeleriKaydet(s, new[] { "Yağ seviyesi", "Fren", "Lastik" });
        var z = _pc1.Zimmet;
        z.Issue(s, "equipment", _ekipman, _personel, 1m, _subeA, null, null, Op());
        z.IadeTarihiAyarla(s, _personel, "equipment", _ekipman, 1_762_000_000_000);
        new ZimmetTutanakService(_pc1.Db, z, _pc1.Saat).Olustur(s, _personel);
        new ZimmetKitService(_pc1.Db, _pc1.Saat).Kaydet(s, null, "Yeni işçi seti", new[] { new AssignmentLine("equipment", _ekipman, 1m) });
    }

    [Fact]
    public async Task BE1_Bilgisayar1_B6_Kayitlari_Sunucuya_Ve_Bilgisayar2ye_Ulasir()
    {
        Pc1CevrimdisiKayitlar();
        await _pc1.GonderAsync();
        await _pc2.CekAsync();

        foreach (var (ad, db) in new[] { ("sunucu", Sunucu.Factory), ("bilgisayar 2", (IDbConnectionFactory)_pc2.Db) })
        {
            var y = Yonetici;
            var gecmis = new EkipmanTasimaService(db).Gecmis(y, _ekipman);
            Assert.True(gecmis.Count == 1 && gecmis[0].HedefKonum == "Kule vinç yanı", ad + ": taşıma geçmişi");
            var kontrol = new GunlukKontrolService(db).Liste(y, _arac);
            Assert.True(kontrol.Count == 1, ad + ": günlük kontrol");
            Assert.Equal(new[] { "Yağ seviyesi", "Fren", "Lastik" }, new GunlukKontrolService(db).Maddeler(y));
            var zimmet = new AssignmentService(db);
            Assert.Contains(zimmet.IadeDurumu(y, _personel), k => k.BeklenenIade == 1_762_000_000_000);
            Assert.Single(new ZimmetTutanakService(db, zimmet).Liste(y, _personel));
            Assert.Contains(new ZimmetKitService(db).Liste(y), k => k.Ad == "Yeni işçi seti");
        }
    }

    [Fact]
    public async Task BE2_Tasima_Ve_Gunluk_Kontrol_Degistirilemez()
    {
        Pc1CevrimdisiKayitlar();
        await _pc1.GonderAsync();
        using (var conn = _pc1.Db.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE equipment_moves SET to_location='SAHTE', note='SAHTE'; UPDATE daily_checks SET note='SAHTE', failed_count=5;";
            cmd.ExecuteNonQuery();
        }
        await _pc1.GonderAsync();
        var y = Yonetici;
        Assert.Equal("Kule vinç yanı", new EkipmanTasimaService(Sunucu.Factory).Gecmis(y, _ekipman).Single().HedefKonum);
        Assert.Equal("vardiya başı", new GunlukKontrolService(Sunucu.Factory).Liste(y, _arac).Single().Not);
    }

    [Fact]
    public void BE3_Madde_Listesini_Eslemeyle_Yalniz_Yonetici_Degistirir()
    {
        new GunlukKontrolService(Sunucu.Factory).MaddeleriKaydet(Yonetici, new[] { "Asıl liste" });
        _pc1.Saat.UtcNow = DateTimeOffset.UtcNow.AddDays(1);                  // gelen sürüm sunucudakinden YENİ → "son yazan kazanır" kabul ederdi;
        new GunlukKontrolService(_pc1.Db, _pc1.Saat).MaddeleriKaydet(_pc1.Oturum, new[] { "Yetkisiz değişiklik" });   // reddi yalnız yönetici kuralı yapabilir
        var paket = JsonDocument.Parse(new BusinessSyncService(_pc1.Db, _pc1.Saat).BuildSnapshot(Company)).RootElement;
        // Araç modülünde düzenleme yetkisi olan ama YÖNETİCİ olmayan kullanıcı:
        var personel = new SessionContext(_uid, Company, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("vehicles", true, true, true, false) }));
        new BusinessSyncService(Sunucu.Factory).Apply(personel, paket);
        Assert.Equal(new[] { "Asıl liste" }, new GunlukKontrolService(Sunucu.Factory).Maddeler(Yonetici));
        new BusinessSyncService(Sunucu.Factory).Apply(Yonetici, paket);           // yönetici gönderirse uygulanır
        Assert.Equal(new[] { "Yetkisiz değişiklik" }, new GunlukKontrolService(Sunucu.Factory).Maddeler(Yonetici));
    }

    [Fact]
    public async Task BE5_Stok_Degisiklik_Kaydi_Eslenir()
    {
        var s = _pc1.Oturum;
        var stok = new DepoWise.Infrastructure.Materials.StockService(_pc1.Db, _pc1.Saat);
        new DepoWise.Infrastructure.Materials.StockChangeLogService(_pc1.Db, stok, _pc1.Saat).Record(s, _malzeme, 7m, continued: false, "uyarı metni");
        await _pc1.GonderAsync();
        await _pc2.CekAsync();
        foreach (var (ad, db) in new[] { ("sunucu", Sunucu.Factory), ("bilgisayar 2", (IDbConnectionFactory)_pc2.Db) })
        {
            var liste = new DepoWise.Infrastructure.Materials.StockChangeLogService(db, new DepoWise.Infrastructure.Materials.StockService(db)).List(Yonetici);
            Assert.True(liste.Count == 1, ad + ": stok değişiklik kaydı");
        }
    }

    [Fact]
    public void BE4_Migration121_Tablolari_Eslemede_Sayim_Taslagi_Disarida()
    {
        foreach (var t in Migration121_SyncB6Tables.Tablolar)
        {
            Assert.Contains(t, BusinessSyncService.Tables);
            Assert.NotNull(BusinessSyncService.ModuleOf(t));
        }
        Assert.DoesNotContain("stock_count_drafts", BusinessSyncService.Tables);
    }
}
