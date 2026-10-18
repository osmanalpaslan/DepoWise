using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-29 (AR-GE 2026-10-18, F-073) — SİPARİŞ VE İŞ EMRİ NUMARASI OTOMATİK ═══
///  OBN1 — Sipariş no boşsa SIP-yıl-0001, sonraki 0002; elle yazılan no aynen kalır
///  OBN2 — Bilgisayar kodlu oturumda SIP-yıl-A3-0001 (çevrimdışı iki bilgisayar çakışmaz)
///  OBN3 — İş emri no boşsa IE-yıl-0001; DÜZENLEMEDE no yine zorunlu
///  OBN4 — Ekranlar yeni kayıtta numarayı zorunlu tutmuyor (iki platform; kaynak kilidi)
/// </summary>
public class OtomatikBelgeNoTests : IDisposable
{
    private const string Co = "OBN";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_obn_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly string _mat;
    private readonly int _yil = DateTimeOffset.UtcNow.Year;

    public OtomatikBelgeNoTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        using (var c = _f.Create()) using (var cmd = c.CreateCommand())
        { cmd.CommandText = $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);"; cmd.ExecuteNonQuery(); }
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "obn", "admin123", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = new MaterialService(_f).Create(_s, new NewMaterial("OBN-M", "Malzeme"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private string SiparisNo(PurchaseOrderService svc, string id) => svc.List(_s).Single(o => o.Id == id).OrderNo;
    private NewPurchaseOrder Siparis(string no) => new(no, Lines: new[] { new NewPurchaseOrderLine(_mat, 1m) });

    [Fact]
    public void OBN1_Siparis_No_Otomatik()
    {
        var svc = new PurchaseOrderService(_f);
        var a = svc.Create(_s, Siparis(""));
        var b = svc.Create(_s, Siparis("  "));
        var c = svc.Create(_s, Siparis("ELLE-7"));
        Assert.Equal($"SIP-{_yil}-0001", SiparisNo(svc, a));
        Assert.Equal($"SIP-{_yil}-0002", SiparisNo(svc, b));
        Assert.Equal("ELLE-7", SiparisNo(svc, c));
    }

    [Fact]
    public void OBN2_Bilgisayar_Kodlu_Numara()
    {
        var svc = new PurchaseOrderService(_f);
        var kodlu = new SessionContext(_s.UserId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { MachineCode = "A3" };
        var id = svc.Create(kodlu, Siparis(""));
        Assert.Matches(new Regex($"^SIP-{_yil}-A3-0001$"), SiparisNo(svc, id));
    }

    [Fact]
    public void OBN3_Is_Emri_No_Otomatik_Duzenlemede_Zorunlu()
    {
        var svc = new WorkOrderService(_f);
        var id = svc.Create(_s, new NewWorkOrder("", "Pompa değişimi"));
        var id2 = svc.Create(_s, new NewWorkOrder("", "Yağ kaçağı"));
        Assert.Equal($"IE-{_yil}-0001", svc.List(_s).Single(w => w.Id == id).WoNo);
        Assert.Equal($"IE-{_yil}-0002", svc.List(_s).Single(w => w.Id == id2).WoNo);
        var ex = Assert.Throws<ArgumentException>(() => svc.UpdateMeta(_s, id, new NewWorkOrder("", "Pompa değişimi")));
        Assert.Contains("İş emri no zorunlu", ex.Message);
    }

    [Fact]
    public void OBN4_Ekranlar_Yeni_Kayitta_Numara_Istemez()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir }.Concat(p).ToArray()));
        Assert.DoesNotContain("Sipariş no zorunlu", Oku("src", "DepoWise.Desktop", "ViewModels", "PurchasingViewModel.cs"));
        Assert.DoesNotContain("Sipariş no zorunlu", Oku("src", "DepoWise.Web", "Components", "Pages", "Purchasing.razor"));
        Assert.Contains("(editing && string.IsNullOrWhiteSpace(FormWoNo))", Oku("src", "DepoWise.Desktop", "ViewModels", "WorkOrdersViewModel.cs"));
        Assert.Contains("(_editId is not null && string.IsNullOrWhiteSpace(_woNo))", Oku("src", "DepoWise.Web", "Components", "Pages", "WorkOrders.razor"));
    }
}
