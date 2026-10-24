using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ Kullanıcı isteği 2026-10-24 (yayın öncesi 2/2) — GİRİŞ-ÇIKIŞ "YENİ KAYIT"TA BİRDEN FAZLA MALZEME ═══
///  YK1 — Satır kuralı (tek yer): aynı malzeme toplanır, fiyat miktarla ağırlıklı ortalanır, fiyatsız satır ortalamaya girmez;
///        miktar ≤ 0 / negatif fiyat / boş malzeme / 200'den fazla satır reddedilir
///  YK2 — API: sepet (mevcut malzemeler) + formdaki YENİ kart TEK giriş belgesinde; bakiyeler doğru; yalnız sepet (form boş) çalışır
///  YK3 — Tekrar gönderim (aynı işlem kimliği) ikinci belge/hareket üretmez; sepet yoksa eski tek malzeme davranışı aynen;
///        geçersiz sepet satırında hiçbir şey yazılmaz (yeni kart da açılmaz); başka firmanın malzemesi kabul edilmez
///  YK4 — İki platform kaynak kilidi (sepet Yeni Kayıt'ta görünür, tür değişince boşalır, aynı kural)
/// </summary>
[Collection("PostgresSchema")]
public class YeniKayitCokluMalzemeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "YKC-A", Admin = "ykc_admin", Pass = "Test!2026";
    private HttpClient _client = null!;
    private SessionContext _s = null!;
    private string _sube = "", _personel = "", _m1 = "", _m2 = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        Firma(Company);
        var uid = Svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = Svc.Branches.Create(_s, new DepoWise.Infrastructure.Organization.NewBranch("YKC Depo"));
        _personel = Svc.Personnel.Create(_s, new DepoWise.Infrastructure.Org.NewPersonnel("Depocu", null, null, _sube, true, false));
        _m1 = Svc.Materials.Create(_s, new NewMaterial("YK-1", "Çimento"));
        _m2 = Svc.Materials.Create(_s, new NewMaterial("YK-2", "Kum"));
        _client = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();
    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    private void Firma(string id)
    {
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                          "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private long Say(string sql)
    {
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private decimal Bakiye(string mat) => Svc.Stock.GetBalanceAt(_s, mat, _sube);

    [Fact]
    public void YK1_Satir_Kurali()
    {
        var r = StockService.GirisSatirlari(new[]
        {
            new StockLine("a", 10m, 100m), new StockLine("b", 1m), new StockLine("a", 30m, 200m), new StockLine("a", 5m),
        });
        Assert.Equal(2, r.Count);
        var a = r.Single(x => x.MaterialId == "a");
        Assert.Equal(45m, a.Quantity);
        Assert.Equal(175m, a.UnitPrice);   // (10·100 + 30·200) / 40 — fiyatsız 5 ortalamaya girmez
        Assert.Null(r.Single(x => x.MaterialId == "b").UnitPrice);
        Assert.Throws<ArgumentException>(() => StockService.GirisSatirlari(new[] { new StockLine("a", 0m) }));
        Assert.Throws<ArgumentException>(() => StockService.GirisSatirlari(new[] { new StockLine("a", 1m, -1m) }));
        Assert.Throws<ArgumentException>(() => StockService.GirisSatirlari(new[] { new StockLine(" ", 1m) }));
        Assert.Throws<ArgumentException>(() => StockService.GirisSatirlari(Enumerable.Range(0, 201).Select(i => new StockLine("m" + i, 1m))));
    }

    private Task<HttpResponseMessage> Giris(object govde) => _client.PostAsJsonAsync("/api/stock/receive", govde);

    [Fact]
    public async Task YK2_Sepet_Ve_Yeni_Kart_Tek_Belge()
    {
        var belgeOnce = Say($"SELECT COUNT(*) FROM stock_documents WHERE company_id='{Company}' AND doc_type='in';");
        var r = await Giris(new
        {
            operationId = Guid.NewGuid().ToString("N"), code = "YK-YENI", name = "Yeni Boya", quantity = 4m, unitPrice = 50m,
            branchId = _sube, personnelId = _personel,
            extraLines = new[] { new { materialId = _m1, quantity = 10m, unitPrice = (decimal?)120m }, new { materialId = _m2, quantity = 3m, unitPrice = (decimal?)null } },
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(belgeOnce + 1, Say($"SELECT COUNT(*) FROM stock_documents WHERE company_id='{Company}' AND doc_type='in';"));   // TEK belge
        var yeni = Svc.Materials.List(_s, new DepoWise.Application.Common.PageRequest(), "YK-YENI").Items.Single(x => x.Code == "YK-YENI").Id;
        Assert.Equal(10m, Bakiye(_m1));
        Assert.Equal(3m, Bakiye(_m2));
        Assert.Equal(4m, Bakiye(yeni));

        // Yalnız sepet: form boş
        r = await Giris(new
        {
            operationId = Guid.NewGuid().ToString("N"), code = "", name = "", quantity = 0m, unitPrice = 0m, branchId = _sube, personnelId = _personel,
            extraLines = new[] { new { materialId = _m1, quantity = 2m, unitPrice = (decimal?)null } },
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(12m, Bakiye(_m1));
    }

    [Fact]
    public async Task YK3_Tekrar_Eski_Davranis_Gecersiz_Ve_Firma()
    {
        var op = Guid.NewGuid().ToString("N");
        object Govde() => new
        {
            operationId = op, materialId = _m1, code = "", name = "", quantity = 5m, unitPrice = 0m, branchId = _sube, personnelId = _personel,
            extraLines = new[] { new { materialId = _m2, quantity = 7m, unitPrice = (decimal?)null } },
        };
        Assert.Equal(HttpStatusCode.OK, (await Giris(Govde())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Giris(Govde())).StatusCode);   // tekrar gönderim
        Assert.Equal(5m, Bakiye(_m1));
        Assert.Equal(7m, Bakiye(_m2));

        // sepetsiz: eski tek malzeme davranışı aynen
        Assert.Equal(HttpStatusCode.OK, (await Giris(new { operationId = Guid.NewGuid().ToString("N"), materialId = _m2, code = "", name = "", quantity = 1m, unitPrice = 0m, branchId = _sube, personnelId = _personel })).StatusCode);
        Assert.Equal(8m, Bakiye(_m2));

        // geçersiz sepet satırı → hiçbir şey yazılmaz, yeni kart da açılmaz
        var kartOnce = Say($"SELECT COUNT(*) FROM materials WHERE company_id='{Company}';");
        var hata = await Giris(new
        {
            operationId = Guid.NewGuid().ToString("N"), code = "YK-BOS", name = "Açılmamalı", quantity = 1m, unitPrice = 0m, branchId = _sube, personnelId = _personel,
            extraLines = new[] { new { materialId = _m1, quantity = -3m, unitPrice = (decimal?)null } },
        });
        Assert.NotEqual(HttpStatusCode.OK, hata.StatusCode);
        Assert.Equal(kartOnce, Say($"SELECT COUNT(*) FROM materials WHERE company_id='{Company}';"));
        Assert.Equal(5m, Bakiye(_m1));

        // başka firmanın malzemesi kabul edilmez (stok değişmez)
        Firma("YKC-B");
        var uidB = Svc.Users.EnsureInitialAdmin("YKC-B", "ykc_b", Pass, RoleKeys.CompanyAdmin);
        var b = new SessionContext(uidB, "YKC-B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yabanci = Svc.Materials.Create(b, new NewMaterial("B-1", "Başka firma malzemesi"));
        var red = await Giris(new
        {
            operationId = Guid.NewGuid().ToString("N"), code = "", name = "", quantity = 0m, unitPrice = 0m, branchId = _sube, personnelId = _personel,
            extraLines = new[] { new { materialId = yabanci, quantity = 9m, unitPrice = (decimal?)null } },
        });
        Assert.NotEqual(HttpStatusCode.OK, red.StatusCode);
        Assert.Equal(0L, Say($"SELECT COUNT(*) FROM stock_movements WHERE material_id='{yabanci}';"));
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void YK4_Iki_Platform()
    {
        var vm = Oku("src/DepoWise.Desktop/ViewModels/StockEntryViewModel.cs");
        Assert.Contains("public bool ShowExitLines => IsExit || IsNew;", vm);
        Assert.Contains("DepoWise.Infrastructure.Materials.StockService.GirisSatirlari(girisSatirlari), op,", vm);
        Assert.Contains("partial void OnSelectedKindChanged(string value) { if (ExitLines.Count > 0) { ExitLines.Clear(); NotifyExitLines(); } }", vm);
        var web = Oku("src/DepoWise.Web/Components/Pages/Stock.razor");
        Assert.Contains("private bool ShowExitLines => IsExit || (IsNew && _editId is null);", web);
        Assert.Contains("extraLines = _exitLines.Select(l => new { materialId = l.MaterialId, quantity = l.Quantity, unitPrice = l.Price }).ToList(),", web);
        Assert.Contains("@bind-Value:after=\"() => { _exitLines.Clear(); }\"", web);
        Assert.Contains("DepoWise.Infrastructure.Materials.StockService.GirisSatirlari(girisSatirlari),", Oku("src/DepoWise.Api/Program.cs"));
    }
}
