using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-09 (AR-GE 2026-10-18, F-040) — ŞUBE YETKİSİ DARALINCA YEREL VERİ TEMİZLİĞİ ═══
///  YKT1 — Sınırsız → [A]: B şubesinin araçları/talepleri (ve kalemleri) silinir; A ve şubesiz kalır; tam çekim gerekmez
///  YKT2 — Gönderilmemiş yerel kayıt SİLİNMEZ; bekleyen kalemi olan talep de korunur
///  YKT3 — Genişleme ([A] → sınırsız / [A,B]) silmez ama tam çekim ister; aynı kapsam hiçbir şey yapmaz
///  YKT4 — Gönderim sayacı bilinmiyorsa ertelenir (hiçbir şey silinmez, imza değişmez)
///  YKT5 — İmza ve kapsama ilişkisi; masaüstü girişte gönderimden sonra çağırır (kaynak kilidi)
/// </summary>
public class YerelKapsamTemizligiTests : IDisposable
{
    private const string Co = "YKT";
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_ykt_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly string _a, _b, _mat;
    private readonly SessionContext _s;

    public YerelKapsamTemizligiTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();   // masaüstü rolü: yerel değişiklik sayacı tetikleyicileri etkin
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "ykt", "admin123", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var br = new BranchService(_f);
        _a = br.Create(_s, new NewBranch("Şube A"));
        _b = br.Create(_s, new NewBranch("Şube B"));
        _mat = new MaterialService(_f).Create(_s, new NewMaterial("YKT-M", "Malzeme"));
        var veh = new VehicleService(_f);
        veh.Create(_s, new NewVehicle("A-1", BranchId: _a));
        veh.Create(_s, new NewVehicle("B-1", BranchId: _b));
        veh.Create(_s, new NewVehicle("B-2", BranchId: _b));
        veh.Create(_s, new NewVehicle("SUBESIZ"));
        Talep("T-A", _a, "K-A");
        Talep("T-B", _b, "K-B");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _yol, _yol + "-wal", _yol + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private void Talep(string id, string? sube, string kalem)
    {
        Exec($"INSERT INTO material_requests(id,company_id,doc_no,request_date,branch_id,status,created_at,updated_at) " +
             $"VALUES('{id}','{Co}','{id}',1,{(sube is null ? "NULL" : $"'{sube}'")},'draft',1,1);");
        Exec($"INSERT INTO material_request_items(id,company_id,request_id,material_id,quantity) VALUES('{kalem}','{Co}','{id}','{_mat}','1');");
    }

    private long Sayac()
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT next - 1 FROM sync_local_counter WHERE id=1;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private List<string> Kodlar(string sql)
    {
        using var c = _f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        using var r = cmd.ExecuteReader(); var l = new List<string>(); while (r.Read()) l.Add(r.GetString(0));
        l.Sort(StringComparer.Ordinal); return l;
    }

    private List<string> Araclar() => Kodlar($"SELECT internal_code FROM vehicles WHERE company_id='{Co}';");
    private List<string> Talepler() => Kodlar($"SELECT id FROM material_requests WHERE company_id='{Co}';");
    private List<string> Kalemler() => Kodlar("SELECT id FROM material_request_items;");

    [Fact]
    public void YKT1_Darilinca_Kapsam_Disi_Silinir()
    {
        var gonderilen = Sayac();   // her şey gönderilmiş
        var r = YerelKapsamTemizligi.Uygula(_f, Co, null, new[] { _a }, gonderilen);
        Assert.False(r.Ertelendi);
        Assert.False(r.TamCekimGerekli);
        Assert.Equal(YerelKapsamTemizligi.Imza(new[] { _a }), r.YeniImza);
        Assert.Equal(new[] { "A-1", "SUBESIZ" }, Araclar());
        Assert.Equal(new[] { "T-A" }, Talepler());
        Assert.Equal(new[] { "K-A" }, Kalemler());
        Assert.Equal(4, r.Silinen);                  // B-1, B-2, T-B, K-B
        Assert.Contains("YKT-M", Kodlar($"SELECT code FROM materials WHERE company_id='{Co}';"));   // malzeme kartı firma geneli
    }

    [Fact]
    public void YKT2_Gonderilmemis_Kayit_Ve_Ebeveyni_Korunur()
    {
        var gonderilen = Sayac();
        Exec("UPDATE vehicles SET plate='34 YKT 2' WHERE internal_code='B-2';");                      // gönderilmemiş düzenleme
        Exec($"INSERT INTO material_request_items(id,company_id,request_id,material_id,quantity) VALUES('K-B2','{Co}','T-B','{_mat}','2');");   // bekleyen kalem
        var r = YerelKapsamTemizligi.Uygula(_f, Co, YerelKapsamTemizligi.Sinirsiz, new[] { _a }, gonderilen);
        Assert.Equal(new[] { "A-1", "B-2", "SUBESIZ" }, Araclar());     // B-1 silindi, bekleyen B-2 duruyor
        Assert.Equal(new[] { "T-A", "T-B" }, Talepler());               // bekleyen kalemi olan talep korunur
        Assert.Equal(new[] { "K-A", "K-B2" }, Kalemler());              // gönderilmiş eski kalem gitti, bekleyen kaldı
        Assert.Equal(2, r.BekleyenKorunan);
    }

    [Fact]
    public void YKT3_Genisleme_Silmez_Tam_Cekim_Ister()
    {
        var once = Araclar();
        var r1 = YerelKapsamTemizligi.Uygula(_f, Co, YerelKapsamTemizligi.Imza(new[] { _a }), null, Sayac());
        Assert.True(r1.TamCekimGerekli);
        Assert.Equal(0, r1.Silinen);
        var r2 = YerelKapsamTemizligi.Uygula(_f, Co, YerelKapsamTemizligi.Imza(new[] { _a }), new[] { _b, _a }, Sayac());
        Assert.True(r2.TamCekimGerekli);
        Assert.Equal(0, r2.Silinen);
        var r3 = YerelKapsamTemizligi.Uygula(_f, Co, YerelKapsamTemizligi.Imza(new[] { _a, _b }), new[] { _b, _a }, Sayac());
        Assert.False(r3.TamCekimGerekli);
        Assert.Equal(0, r3.Silinen);
        Assert.Equal(once, Araclar());
        // Örtüşen değişim [A] → [B]: A'nınkiler silinir VE tam çekim gerekir
        var r4 = YerelKapsamTemizligi.Uygula(_f, Co, YerelKapsamTemizligi.Imza(new[] { _a }), new[] { _b }, Sayac());
        Assert.True(r4.TamCekimGerekli);
        Assert.Equal(new[] { "B-1", "B-2", "SUBESIZ" }, Araclar());
    }

    [Fact]
    public void YKT4_Sayac_Bilinmiyorsa_Ertelenir()
    {
        var r = YerelKapsamTemizligi.Uygula(_f, Co, null, new[] { _a }, gonderilenSeq: -1);
        Assert.True(r.Ertelendi);
        Assert.Equal(YerelKapsamTemizligi.Sinirsiz, r.YeniImza);   // imza değişmez → sonraki girişte yeniden denenir
        Assert.Equal(4, Araclar().Count);
        Assert.Equal(2, Talepler().Count);
    }

    [Fact]
    public void YKT5_Imza_Kapsama_Ve_Baglanti()
    {
        Assert.Equal("*", YerelKapsamTemizligi.Imza(null));
        Assert.Equal("[a,b]", YerelKapsamTemizligi.Imza(new[] { "b", "a", "a" }));
        Assert.Equal("[]", YerelKapsamTemizligi.Imza(Array.Empty<string>()));
        Assert.True(YerelKapsamTemizligi.Kapsar("*", "[a]"));
        Assert.False(YerelKapsamTemizligi.Kapsar("[a]", "*"));
        Assert.True(YerelKapsamTemizligi.Kapsar("[a,b]", "[a]"));
        Assert.False(YerelKapsamTemizligi.Kapsar("[a]", "[a,b]"));
        Assert.True(YerelKapsamTemizligi.Kapsar("[a]", "[]"));

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        var login = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "ViewModels", "LoginViewModel.cs"));
        var push = login.IndexOf("await BusinessSyncPushService.PushAsync();", StringComparison.Ordinal);
        var kapsam = login.IndexOf("BusinessSyncPullService.YerelKapsamiUygula();", StringComparison.Ordinal);
        var pull = login.IndexOf("await BusinessSyncPullService.PullAsync();", kapsam > 0 ? kapsam : 0, StringComparison.Ordinal);
        Assert.True(push > 0 && kapsam > push && pull > kapsam, "sıra: gönder → kapsam temizliği → çek");
        var pullSvc = File.ReadAllText(Path.Combine(dir, "src", "DepoWise.Desktop", "BusinessSyncPullService.cs"));
        Assert.Contains("DepoWise.Application.Security.BranchAccess.Allowed(s)", pullSvc);   // sunucu çekmesiyle aynı kapsam
        Assert.Contains("if (r.TamCekimGerekli)", pullSvc);
    }
}
