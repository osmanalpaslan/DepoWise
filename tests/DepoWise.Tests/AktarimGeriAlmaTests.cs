using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-14 (AR-GE 2026-10-18, 05 F-063) — EXCEL İÇE AKTARIMINI GERİ ALMA ═══
///  AGA1 — Malzeme aktarımı geri alınır; açılış stoklu malzeme (defter değişmez) BIRAKILIR + nedeni; tanımlar bırakılır;
///         ikinci geri alma "zaten" sayar
///  AGA2 — Parti damgası yalnız aktarım süresince: önce/sonra yazılan işlem geçmişi damgasız; iç içe parti eskisine döner
///  AGA3 — Araç ve yakıt depo girişi aktarımları geri alınır (iptal kuralıyla)
///  AGA4 — Başka firmanın partisi geri alınamaz; geçersiz kimlik reddedilir; önizleme parti üretmez
///  AGA5 — Masaüstü ve web bağlantıları yerinde (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class AktarimGeriAlmaTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "AGA-A", Admin = "aga_admin", Pass = "Test!2026";
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

    /// <summary>Şablon başlıklarıyla gerçek .xlsx (verilmeyen sütun boş).</summary>
    private byte[] Dosya(IReadOnlyList<string> basliklar, params Dictionary<string, string>[] satirlar)
        => Svc.Excel.Export(new TableModel("Aktarım", basliklar, satirlar
            .Select(r => (IReadOnlyList<object?>)basliklar.Select(h => r.TryGetValue(h, out var v) ? (object?)v : null).ToList()).ToList()));

    private async Task<JsonElement> Aktar(string tur, byte[] dosya, string adim = "commit")
    {
        var form = new MultipartFormDataContent();
        var c = new ByteArrayContent(dosya);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(c, "file", "aktarim.xlsx");
        form.Add(new StringContent("__all__"), "branchId");
        var r = await _client.PostAsync($"/api/import/{tur}/{adim}", form);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await ApiTestHost.JsonAsync(r);
    }

    private async Task<JsonElement> GeriAl(HttpClient c, string parti)
    {
        var r = await c.PostAsJsonAsync("/api/import/undo", new { partiKimligi = parti });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await ApiTestHost.JsonAsync(r);
    }

    private long Sayi(string sql)
    {
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", Company);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task AGA1_Malzeme_Aktarimi_Geri_Alinir_Acilis_Stoklu_Birakilir()
    {
        var h = Svc.MaterialImport.SampleHeaders();
        var j = await Aktar("materials", Dosya(h,
            new() { [MaterialImportService.ColCode] = "AGA-1", [MaterialImportService.ColName] = "Filtre", [MaterialImportService.ColUnit] = "AgaBirim" },
            new() { [MaterialImportService.ColCode] = "AGA-2", [MaterialImportService.ColName] = "Kayış" },
            new() { [MaterialImportService.ColCode] = "AGA-3", [MaterialImportService.ColName] = "Yağ", [MaterialImportService.ColOpening] = "5" }));
        Assert.Equal(3, j.GetProperty("added").GetInt32());
        var parti = j.GetProperty("partiKimligi").GetString()!;
        Assert.StartsWith("import:materials:", parti);
        Assert.Equal(3, Sayi("SELECT COUNT(*) FROM materials WHERE company_id=@c AND is_deleted=0 AND code LIKE 'AGA-%';"));

        var g = await GeriAl(_client, parti);
        Assert.Equal(2, g.GetProperty("geriAlinan").GetInt32());
        var birakilan = g.GetProperty("birakilanlar").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Single(birakilan);
        Assert.Contains("AGA-3", birakilan[0]);                                    // açılış stoklu malzeme kaldı
        Assert.True(g.GetProperty("tanimBirakilan").GetInt32() >= 1);              // "AgaBirim" birimi bırakıldı
        Assert.Equal(1, Sayi("SELECT COUNT(*) FROM materials WHERE company_id=@c AND is_deleted=0 AND code LIKE 'AGA-%';"));
        Assert.Equal(1, Sayi("SELECT COUNT(*) FROM stock_movements WHERE company_id=@c AND movement_type='opening';"));   // defter aynı
        Assert.Equal(1, Sayi("SELECT COUNT(*) FROM units WHERE company_id=@c AND name='AgaBirim';"));

        var g2 = await GeriAl(_client, parti);                                     // tekrar: kalanlar "zaten"
        Assert.Equal(0, g2.GetProperty("geriAlinan").GetInt32());
        Assert.Equal(2, g2.GetProperty("zatenGeriAlinmis").GetInt32());
        Assert.Single(g2.GetProperty("birakilanlar").EnumerateArray());
    }

    [Fact]
    public void AGA2_Damga_Yalniz_Aktarim_Suresince()
    {
        Assert.Null(AktarimPartisi.Gecerli);
        string disId, icId;
        using (var dis = AktarimPartisi.Baslat("materials"))
        {
            disId = dis.Kimlik;
            Assert.Equal(disId, AktarimPartisi.Gecerli);
            using (var ic = AktarimPartisi.Baslat("vehicles"))
            {
                icId = ic.Kimlik;
                Assert.Equal(icId, AktarimPartisi.Gecerli);
            }
            Assert.Equal(disId, AktarimPartisi.Gecerli);                             // iç parti kapanınca dıştakine döner
            Svc.Materials.Create(_s, new DepoWise.Infrastructure.Materials.NewMaterial("AGA-D1", "İçeride"));
        }
        Assert.Null(AktarimPartisi.Gecerli);
        Svc.Materials.Create(_s, new DepoWise.Infrastructure.Materials.NewMaterial("AGA-D2", "Dışarıda"));

        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT m.code, a.correlation_id FROM audit_logs a JOIN materials m ON m.id=a.entity_id " +
                          "WHERE a.company_id=@c AND a.entity_type='material' AND a.action='create' AND m.code IN ('AGA-D1','AGA-D2');";
        cmd.AddWithValue("@c", Company);
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, string?>();
        while (r.Read()) d[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
        Assert.Equal(disId, d["AGA-D1"]);
        Assert.Null(d["AGA-D2"]);
    }

    [Fact]
    public async Task AGA3_Arac_Ve_Yakit_Depo_Girisi_Geri_Alinir()
    {
        var av = await Aktar("vehicles", Dosya(Svc.VehicleImport.SampleHeaders(),
            new() { [VehicleImportService.ColCode] = "AGA-V1", [VehicleImportService.ColPlate] = "34 AGA 01" },
            new() { [VehicleImportService.ColCode] = "AGA-V2" }));
        Assert.Equal(2, av.GetProperty("added").GetInt32());
        var gv = await GeriAl(_client, av.GetProperty("partiKimligi").GetString()!);
        Assert.Equal(2, gv.GetProperty("geriAlinan").GetInt32());
        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM vehicles WHERE company_id=@c AND is_deleted=0 AND internal_code LIKE 'AGA-%';"));

        var ay = await Aktar("fuel-depot", Dosya(Svc.FuelDepotImport.SampleHeaders(),
            new() { [FuelDepotImportService.ColLiters] = "100", [FuelDepotImportService.ColPrice] = "40" },
            new() { [FuelDepotImportService.ColLiters] = "50", [FuelDepotImportService.ColPrice] = "41" }));
        Assert.Equal(2, ay.GetProperty("added").GetInt32());
        var gy = await GeriAl(_client, ay.GetProperty("partiKimligi").GetString()!);
        Assert.Equal(2, gy.GetProperty("geriAlinan").GetInt32());
        Assert.Equal(0, Sayi("SELECT COUNT(*) FROM fuel_depot_entries WHERE company_id=@c AND is_deleted=0;"));
    }

    [Fact]
    public async Task AGA4_Baska_Firma_Gecersiz_Kimlik_Onizleme()
    {
        var h = Svc.MaterialImport.SampleHeaders();
        var dosya = Dosya(h, new Dictionary<string, string> { [MaterialImportService.ColCode] = "AGA-X1", [MaterialImportService.ColName] = "X" });
        var on = await Aktar("materials", dosya, "preview");
        Assert.True(on.GetProperty("partiKimligi").ValueKind == JsonValueKind.Null);   // önizleme yazmaz → parti yok
        var j = await Aktar("materials", dosya);
        var parti = j.GetProperty("partiKimligi").GetString()!;

        Firma("AGA-B");
        Svc.Users.EnsureInitialAdmin("AGA-B", "aga_b", Pass, RoleKeys.CompanyAdmin);
        var b = await _host.LoginAsync("aga_b", Pass, "AGA-B");
        var rb = await b.PostAsJsonAsync("/api/import/undo", new { partiKimligi = parti });
        Assert.Equal(HttpStatusCode.Forbidden, rb.StatusCode);                        // B, A'nın partisini göremez
        Assert.Equal(1, Sayi("SELECT COUNT(*) FROM materials WHERE company_id=@c AND is_deleted=0 AND code='AGA-X1';"));

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/import/undo", new { partiKimligi = "rastgele" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _host.CreateClient().PostAsJsonAsync("/api/import/undo", new { partiKimligi = parti })).StatusCode);

        // Yalnız "atlandı" (zaten var) olan tekrar aktarımda kayıt eklenmez → geri alınacak parti verilmez
        var tekrar = await Aktar("materials", dosya);
        Assert.Equal(0, tekrar.GetProperty("added").GetInt32());
        Assert.True(tekrar.GetProperty("partiKimligi").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void AGA5_Baglantilar_Yerinde()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir }.Concat(p).ToArray()));

        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "ImportExportViewModel.cs");
        Assert.Contains("AktarimPartisi.Baslat(ImportKey(SelectedImport))", vm);
        Assert.Contains("DesktopServices.AktarimGeriAlma.GeriAl(_session, parti)", vm);
        Assert.Contains("UndoImportCommand", Oku("src", "DepoWise.Desktop", "Views", "ImportExportView.axaml"));
        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "ImportExcel.razor");
        Assert.Contains("Str(data.Value, \"partiKimligi\")", web);
        Assert.Contains("Api.PostJsonAsync(\"/api/import/undo\"", web);
        Assert.Contains("AktarimPartisi.Baslat(entity)", Oku("src", "DepoWise.Api", "Program.cs"));
        Assert.Contains("AktarimPartisi.Gecerli", Oku("src", "DepoWise.Infrastructure", "Database", "AuditWriter.cs"));
    }
}
