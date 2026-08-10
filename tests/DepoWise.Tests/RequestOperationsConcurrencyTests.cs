using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// KLT-01a — Talep operasyonlarında düzenleme kilidi (2026-08-10).
///
/// KAPSAM AYRIMI (kullanıcı kararı):
/// • <see cref="RequestOperationsService.UpdateShipmentInfo"/> → üç alanı KÖRLEMESİNE yazıyordu
///   (karşılaştırma yok) → iki kullanıcı birbirini SESSİZCE eziyordu. expectedVersion EKLENDİ.
/// • <see cref="RequestOperationsService.ChangeStatus"/> durum geçişi → sürüm kontrolü EKLENMEDİ.
///   Durum zaten korumalı: BeginImmediate + durumu transaction İÇİNDE okuma +
///   RequestOperationStateMachine.CanTransition (from == to → false).
///   Bu dosyadaki ilgili testler o mevcut korumanın REGRESYON testleridir (yeni mekanizma değil).
/// • ChangeStatus(updateBranches: true) → aynı UPDATE'te gönderim alanları da yazıldığı için
///   sürüm kontrolü orada devrededir; çakışmada çağrının tamamı reddedilir (tek transaction atomik).
/// </summary>
public class RequestOperationsConcurrencyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly RequestService _requests;
    private readonly RequestOperationsService _ops;
    private readonly SessionContext _admin;
    private readonly string _branchA;
    private readonly string _branchB;

    public RequestOperationsConcurrencyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_klt01a_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin_A", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _branchA = branches.Create(_admin, new NewBranch("Şube A"));
        _branchB = branches.Create(_admin, new NewBranch("Şube B"));

        var stock = new StockService(_factory, _clock);
        _requests = new RequestService(_factory, stock, _clock);
        _ops = new RequestOperationsService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    /// <summary>Onaylanmış (operasyon sürecine girmiş) bir talep üretir — testlerin ön koşulu.</summary>
    private string ApprovedRequest(string materialCode = "M-1")
    {
        var materials = new MaterialService(_factory, _clock);
        var m = materials.Create(_admin, new NewMaterial(materialCode, "Test Malzeme"));
        var h = _requests.Create(_admin, new NewRequest(new[] { new RequestItemInput(m, 5m) }, SubmitImmediately: true));
        _requests.Approve(_admin, h.Id);
        return h.Id;
    }

    private RequestOperationRow Row(string requestId)
        => _ops.List(_admin).First(r => r.Id == requestId);

    // ───────────── UpdateShipmentInfo — asıl KLT-01a hedefi ─────────────

    [Fact]
    public void UpdateShipmentInfo_AyniSurumle_IkinciIslemReddedilir()
    {
        var id = ApprovedRequest();
        var v = Row(id).Version;

        // Birinci kullanıcı kaydeder.
        _ops.UpdateShipmentInfo(_admin, id, _branchA, _branchB, "birinci not", expectedVersion: v);

        // İkinci kullanıcı ESKİ sürümle kaydetmeye çalışır → reddedilir.
        Assert.Throws<ConcurrencyException>(() =>
            _ops.UpdateShipmentInfo(_admin, id, _branchB, _branchA, "ikinci not", expectedVersion: v));
    }

    [Fact]
    public void UpdateShipmentInfo_CakismaSonrasi_BirincininVerisiKorunur()
    {
        var id = ApprovedRequest();
        var v = Row(id).Version;

        _ops.UpdateShipmentInfo(_admin, id, _branchA, _branchB, "birinci not", expectedVersion: v);
        Assert.Throws<ConcurrencyException>(() =>
            _ops.UpdateShipmentInfo(_admin, id, _branchB, _branchA, "ikinci not", expectedVersion: v));

        // Birincinin yazdıkları AYNEN durur; ikincinin hiçbir alanı yazılmamıştır (kısmi yazma yok).
        var after = Row(id);
        Assert.Equal(_branchA, after.FromBranchId);
        Assert.Equal(_branchB, after.ToBranchId);
        Assert.Equal("birinci not", after.OpsNote);
    }

    [Fact]
    public void UpdateShipmentInfo_GuncelSurumleTekrarDenemeBasarili()
    {
        var id = ApprovedRequest();
        var v0 = Row(id).Version;

        _ops.UpdateShipmentInfo(_admin, id, _branchA, _branchB, "birinci", expectedVersion: v0);
        Assert.Throws<ConcurrencyException>(() =>
            _ops.UpdateShipmentInfo(_admin, id, _branchB, _branchA, "ikinci", expectedVersion: v0));

        // Ekran tazelenip GÜNCEL sürümle tekrar denenince geçmeli (kullanıcı kilitlenip kalmaz).
        var fresh = Row(id).Version;
        Assert.NotEqual(v0, fresh);
        _ops.UpdateShipmentInfo(_admin, id, _branchB, _branchA, "ikinci", expectedVersion: fresh);

        var after = Row(id);
        Assert.Equal(_branchB, after.FromBranchId);
        Assert.Equal("ikinci", after.OpsNote);
    }

    [Fact]
    public void UpdateShipmentInfo_FarkliTalepler_BirbiriniEngellemez()
    {
        var id1 = ApprovedRequest("M-A");
        var id2 = ApprovedRequest("M-B");
        var v1 = Row(id1).Version;
        var v2 = Row(id2).Version;

        _ops.UpdateShipmentInfo(_admin, id1, _branchA, _branchB, "t1", expectedVersion: v1);
        // id1'e yazmak id2'nin sürümünü ETKİLEMEZ.
        _ops.UpdateShipmentInfo(_admin, id2, _branchB, _branchA, "t2", expectedVersion: v2);

        Assert.Equal("t1", Row(id1).OpsNote);
        Assert.Equal("t2", Row(id2).OpsNote);
    }

    [Fact]
    public void UpdateShipmentInfo_SurumVerilmezse_KontrolYapilmaz()
    {
        var id = ApprovedRequest();
        // Geriye uyumluluk: eski çağrılar (sürümsüz) bozulmamalı.
        _ops.UpdateShipmentInfo(_admin, id, _branchA, _branchB, "eski istemci");
        _ops.UpdateShipmentInfo(_admin, id, _branchB, _branchA, "yine eski istemci");
        Assert.Equal("yine eski istemci", Row(id).OpsNote);
    }

    // ───────────── ChangeStatus(updateBranches: true) — gönderim alanları korunur ─────────────

    [Fact]
    public void ChangeStatus_UpdateBranches_AyniSurumle_IkinciReddedilir()
    {
        var id = ApprovedRequest();
        var v = Row(id).Version;

        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse, "not1",
            _branchA, _branchB, updateBranches: true, expectedVersion: v);

        // Aynı (artık eski) sürümle ikinci çağrı → gönderim alanları ezilmemeli.
        Assert.ThrowsAny<Exception>(() =>
            _ops.ChangeStatus(_admin, id, RequestOperationStatus.Shipped, "not2",
                _branchB, _branchA, updateBranches: true, expectedVersion: v));

        var after = Row(id);
        Assert.Equal(_branchA, after.FromBranchId);
        Assert.Equal(_branchB, after.ToBranchId);
    }

    [Fact]
    public void ChangeStatus_UpdateBranches_GuncelSurumle_Basarili()
    {
        var id = ApprovedRequest();
        var v0 = Row(id).Version;

        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse, "n1",
            _branchA, _branchB, updateBranches: true, expectedVersion: v0);

        var v1 = Row(id).Version;
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Shipped, "n2",
            _branchA, _branchB, updateBranches: true, expectedVersion: v1);

        Assert.Equal(RequestOperationStatusInfo.ToDb(RequestOperationStatus.Shipped), Row(id).OperationStatusDb);
    }

    // ───────────── ChangeStatus — MEVCUT korumanın REGRESYON testleri ─────────────

    [Fact]
    public void REGRESYON_AyniGecisIkinciKez_DurumMakinesiTarafindanReddedilir()
    {
        // Bu, YENİ bir concurrency mekanizması DEĞİLDİR: RequestOperationStateMachine.CanTransition
        // içindeki "from == to → false" kuralının ileride bozulmasını engelleyen regresyon testidir.
        var id = ApprovedRequest();

        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse));

        Assert.Contains("Geçersiz operasyon geçişi", ex.Message);
    }

    [Fact]
    public void REGRESYON_SurumVermeden_NormalDurumGecisleri_Calismaya_Devam_Eder()
    {
        // Durum geçişine sürüm kontrolü EKLENMEDİ → sürümsüz çağrılar aynen çalışmalı.
        var id = ApprovedRequest();

        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.Shipped);
        _ops.ChangeStatus(_admin, id, RequestOperationStatus.ArrivedAtBranch);

        Assert.Equal(RequestOperationStatusInfo.ToDb(RequestOperationStatus.ArrivedAtBranch), Row(id).OperationStatusDb);
    }

    [Fact]
    public void REGRESYON_EskiSurumle_Bile_DurumGecisi_UpdateBranchesFalse_Ise_Engellenmez()
    {
        // Kullanıcı kararı: durum geçişinin KENDİSİNE sürüm kilidi konmayacak.
        // updateBranches=false iken sürüm gönderilse bile kontrol UYGULANMAZ.
        var id = ApprovedRequest();
        var eskiSurum = Row(id).Version;

        _ops.UpdateShipmentInfo(_admin, id, _branchA, _branchB, "araya girdi");   // sürüm artar

        _ops.ChangeStatus(_admin, id, RequestOperationStatus.FromWarehouse, "not",
            updateBranches: false, expectedVersion: eskiSurum);

        Assert.Equal(RequestOperationStatusInfo.ToDb(RequestOperationStatus.FromWarehouse), Row(id).OperationStatusDb);
    }

    // ───────────── Yetki ve izolasyon bozulmadı ─────────────

    [Fact]
    public void YetkiKontrolu_SurumKontrolunden_ONCE_Calisir()
    {
        var id = ApprovedRequest();
        var v = Row(id).Version;
        var yetkisiz = new SessionContext("u-yetkisiz", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);

        // Yetkisiz kullanıcı, ESKİ sürümle bile çakışma hatası değil YETKİ hatası almalı
        // (aksi hâlde kaydın değişip değişmediğini öğrenebilirdi).
        Assert.Throws<ForbiddenException>(() =>
            _ops.UpdateShipmentInfo(yetkisiz, id, _branchA, _branchB, "x", expectedVersion: v - 99));

        Assert.Null(Row(id).OpsNote);
    }

    [Fact]
    public void BaskaFirmanin_Talebi_Duzenlenemez()
    {
        var id = ApprovedRequest();
        var v = Row(id).Version;
        var users = new UserService(_factory, _clock);
        var bid = users.EnsureInitialAdmin("B", "admin_B", "admin123", RoleKeys.CompanyAdmin);
        var adminB = new SessionContext(bid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Assert.ThrowsAny<Exception>(() =>
            _ops.UpdateShipmentInfo(adminB, id, _branchA, _branchB, "sızıntı", expectedVersion: v));

        Assert.Null(Row(id).OpsNote);
    }

    [Fact]
    public void BaskaFirmanin_Subesi_GonderimBilgisine_Yazilamaz()
    {
        // Mevcut EnsureBranchOwned korumasının regresyonu — sürüm eklenmesi bunu bozmamalı.
        var id = ApprovedRequest();
        var v = Row(id).Version;

        var users = new UserService(_factory, _clock);
        var bid = users.EnsureInitialAdmin("B", "admin_B", "admin123", RoleKeys.CompanyAdmin);
        var adminB = new SessionContext(bid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branchesB = new BranchService(_factory, _clock);
        var yabanciSube = branchesB.Create(adminB, new NewBranch("B Şubesi"));

        Assert.ThrowsAny<Exception>(() =>
            _ops.UpdateShipmentInfo(_admin, id, yabanciSube, _branchB, "x", expectedVersion: v));

        Assert.Null(Row(id).FromBranchId);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
