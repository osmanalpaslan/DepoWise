using System.Net;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-56 (AR-GE 2026-10-24, 05 F-090) — KAYIT KARTINDA "BELGELER" ═══
///  KB1 — Kartın kullandığı uç (/api/documents?entityType=&amp;entityId=) YALNIZ o kaydın belgelerini döndürür (aynı türdeki başka kayıt,
///        firma evrakı karışmaz); başka firma göremez
///  KB2 — İki platform kaynak kilidi: 4 ekranda (malzeme, araç, ekipman, personel) panel + aynı uç; geçerlilik metni iki ortamda aynı
/// </summary>
[Collection("PostgresSchema")]
public class KayitBelgeleriTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "KBL-A", Admin = "kbl_admin", Pass = "Test!2026";
    private HttpClient _client = null!;
    private SessionContext _s = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        Firma(Company);
        var uid = Svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
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

    [Fact]
    public async Task KB1_Yalniz_O_Kaydin_Belgeleri()
    {
        var pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\ntest\n%%EOF");
        var docs = new DocumentService(Svc.Factory, new LocalFileStorageProvider(Path.Combine(Path.GetTempPath(), "depowise_kbl_" + Guid.NewGuid().ToString("N"))));
        var m1 = Svc.Materials.Create(_s, new DepoWise.Infrastructure.Materials.NewMaterial("KB-1", "Çimento"));
        var m2 = Svc.Materials.Create(_s, new DepoWise.Infrastructure.Materials.NewMaterial("KB-2", "Kum"));
        docs.Save(_s, "material", m1, new DocumentMeta("Çimento TSE belgesi", ValidUntil: 1), "a.pdf", "application/pdf", pdf);
        docs.Save(_s, "material", m1, new DocumentMeta("Çimento teknik föy"), "b.pdf", "application/pdf", pdf);
        docs.Save(_s, "material", m2, new DocumentMeta("Kum analiz raporu"), "c.pdf", "application/pdf", pdf);
        docs.Save(_s, "company", null, new DocumentMeta("Vergi levhası"), "d.pdf", "application/pdf", pdf);

        async Task<List<string>> Basliklar(HttpClient c, string id)
        {
            var r = await c.GetAsync($"/api/documents?entityType=material&entityId={id}");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var j = await ApiTestHost.JsonAsync(r);
            return j.EnumerateArray().Select(x => x.GetProperty("title").GetString()!).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        Assert.Equal(new[] { "Çimento TSE belgesi", "Çimento teknik föy" }.OrderBy(x => x, StringComparer.Ordinal), await Basliklar(_client, m1));
        Assert.Equal(new[] { "Kum analiz raporu" }, await Basliklar(_client, m2));

        Firma("KBL-B");
        Svc.Users.EnsureInitialAdmin("KBL-B", "kbl_b", Pass, RoleKeys.CompanyAdmin);
        var b = await _host.LoginAsync("kbl_b", Pass, "KBL-B");
        var rb = await b.GetAsync($"/api/documents?entityType=material&entityId={m1}");
        Assert.True(rb.StatusCode != HttpStatusCode.OK || (await ApiTestHost.JsonAsync(rb)).GetArrayLength() == 0, "başka firma belgeleri göremez");
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Theory]
    [InlineData("Materials", "material")]
    [InlineData("Vehicles", "vehicle")]
    [InlineData("Equipment", "equipment")]
    [InlineData("Personnel", "personnel")]
    public void KB2_Iki_Platform(string ekran, string tur)
    {
        Assert.Contains($"_ = Belgeler.YukleAsync(\"{tur}\",", Oku($"src/DepoWise.Desktop/ViewModels/{ekran}ViewModel.cs"));
        Assert.Matches(@"<ctrl:KayitBelgeleri DataContext=""\{Binding [^""]*Belgeler\}""", Oku($"src/DepoWise.Desktop/Views/{ekran}View.axaml"));
        Assert.Contains($"<KayitBelgeleri EntityType=\"{tur}\" EntityId=\"@_editId\" />", Oku($"src/DepoWise.Web/Components/Pages/{ekran}.razor"));
    }

    [Fact]
    public void KB2b_Ortak_Uc_Ve_Ayni_Gecerlilik_Metni()
    {
        // Kayıt seçimi sunucuya GİTMEZ (çevrimdışı/arayüz testinde bekleme olmasın); belgeler "Belgeleri Göster" ile istenir.
        var panel = Oku("src/DepoWise.Desktop/ViewModels/KayitBelgeleriPaneli.cs");
        Assert.Contains("return Task.CompletedTask;", panel);
        Assert.Contains("Command=\"{Binding GosterCommand}\"", Oku("src/DepoWise.Desktop/Controls/KayitBelgeleri.axaml"));
        var osc = Oku("src/DepoWise.Desktop/OrgServerClient.cs");
        Assert.Contains("q.Add(\"entityId=\" + Uri.EscapeDataString(entityId!));", osc);
        var web = Oku("src/DepoWise.Web/Components/KayitBelgeleri.razor");
        Assert.Contains("/api/documents?entityType={Uri.EscapeDataString(EntityType)}&entityId={Uri.EscapeDataString(EntityId)}", web);
        var masa = Oku("src/DepoWise.Desktop/ViewModels/KayitBelgeleriPaneli.cs");
        foreach (var metin in new[] { "$\"SÜRESİ DOLDU ({gun})\"", "$\"geçerli: {gun}\"", "Bu kayda bağlı belge yok — Belgeler ekranından eklenebilir." })
        {
            Assert.Contains(metin, masa);
            Assert.Contains(metin, web);
        }
    }
}
