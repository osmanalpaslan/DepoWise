using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-53 (AR-GE 2026-10-26, F-059) — TEDARİKÇİ TEK YERDE (Tanımlar ↔ Cari köprüsü kendiliğinden) ═══
///  TK1 — yeni tedarikçi tanımı → bağlı tedarikçi carisi (TDR- kodlu) aynı işlemde
///  TK2 — aynı adlı (boşluk / büyük-küçük harf farkı önemsiz) serbest tedarikçi carisi varsa YENİSİ açılmaz, ona bağlanır
///  TK3 — tedarikçi/karma cari açılınca tanım açılır ve bağlanır; müşteri carisinde tanım AÇILMAZ
///  TK4 — müşteri carisi tedarikçiye çevrilince bağlanır
///  TK5 — eski veri: eşlenmemiş raporu doğru; yönetici toplu eşlemesi aynı adlıları birbirine bağlar (kopya yok), kalanlara karşılık
///        açar, sonra rapor BOŞ; yönetici olmayan toplu eşleyemez
///  TK6 — var olan tedarikçi adı yeniden eklenince (hızlı ekleme) carisi yoksa bağlanır, ikinci cari açılmaz
/// </summary>
public class TedarikciKopruTests : IDisposable
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_tdr_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly LookupService _tanim;
    private readonly PartyService _cari;
    private readonly TedarikciKopruService _kopru;
    private readonly SessionContext _y;

    public TedarikciKopruTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('TK','TK',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin("TK", "tk_admin", "Test!2026", RoleKeys.CompanyAdmin);
        _y = new SessionContext(uid, "TK", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _tanim = new LookupService(_f);
        _cari = new PartyService(_f);
        _kopru = new TedarikciKopruService(_f);
    }

    public void Dispose() { try { File.Delete(_yol); } catch { } }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private (string? Kod, string? Tur, string? Sup) Cari(string where)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT code, party_type, supplier_id FROM parties WHERE company_id='TK' AND " + where + ";";
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)) : (null, null, null);
    }

    private void HamEkle(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void TK1_Yeni_Tedarikci_Cari_Acar()
    {
        var sup = _tanim.AddSupplier(_y, "Akın Hırdavat");
        var c = Cari($"supplier_id='{sup}'");
        Assert.Equal("supplier", c.Tur);
        Assert.StartsWith("TDR-", c.Kod);
        Assert.Empty(_kopru.Eslesmemis(_y));
    }

    [Fact]
    public void TK2_Ayni_Adli_Serbest_Cari_Varsa_Ona_Baglanir()
    {
        var cariId = _cari.Create(_y, new NewParty("C-1", "AKIN   hırdavat", PartyTypes.Supplier));   // tanım da açar (TK3) → önce tanımı sil
        HamEkle($"UPDATE parties SET supplier_id=NULL WHERE id='{cariId}'; DELETE FROM suppliers;");
        var sup = _tanim.AddSupplier(_y, "Akın Hırdavat");
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM parties WHERE company_id='TK' AND is_deleted=0"));   // yeni cari açılmadı
        Assert.Equal(sup, Cari($"id='{cariId}'").Sup);
    }

    [Fact]
    public void TK3_Tedarikci_Cari_Tanim_Acar_Musteri_Acmaz()
    {
        var t = _cari.Create(_y, new NewParty("C-2", "Demir Ltd", PartyTypes.Supplier));
        var k = _cari.Create(_y, new NewParty("C-3", "Karma AŞ", PartyTypes.Both));
        var m = _cari.Create(_y, new NewParty("C-4", "Müşteri AŞ", PartyTypes.Customer));
        Assert.NotNull(Cari($"id='{t}'").Sup);
        Assert.NotNull(Cari($"id='{k}'").Sup);
        Assert.Null(Cari($"id='{m}'").Sup);
        Assert.Equal(2L, Say("SELECT COUNT(*) FROM suppliers WHERE company_id='TK' AND is_deleted=0"));
    }

    [Fact]
    public void TK4_Musteri_Tedarikciye_Cevrilince_Baglanir()
    {
        var m = _cari.Create(_y, new NewParty("C-5", "Çevrik AŞ", PartyTypes.Customer));
        Assert.Null(Cari($"id='{m}'").Sup);
        var v = _cari.Get(_y, m).Version;
        _cari.Update(_y, m, new UpdateParty("C-5", "Çevrik AŞ", PartyTypes.Supplier, Version: v));
        Assert.NotNull(Cari($"id='{m}'").Sup);
    }

    [Fact]
    public void TK5_Eski_Veri_Raporu_Ve_Toplu_Esleme()
    {
        // Eski veri (köprüsüz): aynı adlı bir çift + yalnız tanım + yalnız cari.
        HamEkle("INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('s1','TK','Ortak Firma',1,1,1,0),('s2','TK','Yalnız Tanım',1,1,1,0);" +
                "INSERT INTO parties(id,company_id,code,title,party_type,is_person,currency_code,is_active,created_at,updated_at,version,is_deleted) " +
                "VALUES('p1','TK','E-1','ortak  FİRMA','supplier',0,'TRY',1,1,1,1,0),('p2','TK','E-2','Yalnız Cari','both',0,'TRY',1,1,1,1,0);");
        var rapor = _kopru.Eslesmemis(_y);
        Assert.Equal(4, rapor.Count);
        var personel = new SessionContext(_y.UserId, "TK", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("parties", true, true, true, false) }));
        Assert.Throws<ForbiddenException>(() => _kopru.TopluEsle(personel));

        Assert.Equal(3, _kopru.TopluEsle(_y));                                    // p1↔s1 (aynı ad), p2→yeni tanım, s2→yeni cari
        Assert.Empty(_kopru.Eslesmemis(_y));
        Assert.Equal("s1", Cari("id='p1'").Sup);
        Assert.Equal(3L, Say("SELECT COUNT(*) FROM suppliers WHERE company_id='TK' AND is_deleted=0"));   // s1, s2, p2'nin tanımı
        Assert.Equal(3L, Say("SELECT COUNT(*) FROM parties WHERE company_id='TK' AND is_deleted=0"));     // p1, p2, s2'nin carisi
    }

    /// <summary>TK7 — aynı adlı İKİNCİ tedarikçi carisi: tanım adı tekil olduğundan yeni tanım açılmaz, cari bağsız kalır ve raporda
    /// görünür; cari açma HATA VERMEZ (paket koşusunda benzersizlik hatasıyla duruyordu). Silinmiş aynı adlı tanımda da aynı.</summary>
    [Fact]
    public void TK7_Ayni_Adli_Ikinci_Cari_Hata_Vermez_Raporda_Kalir()
    {
        var a = _cari.Create(_y, new NewParty("C-7", "Aynı Ad Ltd", PartyTypes.Supplier));
        var b = _cari.Create(_y, new NewParty("C-8", "Aynı Ad Ltd", PartyTypes.Supplier));   // eskiden: UNIQUE constraint hatası
        Assert.NotNull(Cari($"id='{a}'").Sup);
        Assert.Null(Cari($"id='{b}'").Sup);
        Assert.Contains(_kopru.Eslesmemis(_y), k => k.Id == b);
        Assert.Equal(0, _kopru.TopluEsle(_y));                                    // insan kararı gerekir — zorla bağlamaz
        HamEkle("INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('sd','TK','Silinmiş Tedarikçi',1,1,1,1);");
        var c = _cari.Create(_y, new NewParty("C-9", "Silinmiş Tedarikçi", PartyTypes.Supplier));
        Assert.Null(Cari($"id='{c}'").Sup);
    }

    /// <summary>TK8 — iki ortam: cari ekranında eşlenmemiş uyarısı yalnız yöneticiye, aynı metin ve aynı onayla; aynı servis yolu.</summary>
    [Fact]
    public void TK8_Iki_Ortam_Ayni_Uyari_Ve_Onay()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        string Oku(string rel) => File.ReadAllText(Path.Combine(d!.FullName, rel));
        var mas = Oku("src/DepoWise.Desktop/ViewModels/PartiesViewModel.cs");
        var web = Oku("src/DepoWise.Web/Components/Pages/Parties.razor");
        var api = Oku("src/DepoWise.Api/Program.cs");
        foreach (var metin in new[] { "tedarikçi kaydı Tanımlar ile Cari arasında eşlenmemiş (aynı firma iki yerde ayrı görünebilir).",
                                      "Eşlenmemiş tedarikçiler eşleştirilsin mi?", "\"Evet, Eşleştir\"" })
        {
            Assert.Contains(metin, mas);
            Assert.Contains(metin, web);
        }
        Assert.Contains("if (!AccessControl.IsAdmin(_session)) { TedarikciEslemeUyarisi = null; return; }", mas);
        Assert.Contains("if (!Auth.IsAdmin) { _tedarikciEsleme = 0; return; }", web);
        Assert.Contains("DesktopServices.TedarikciKopru.TopluEsle(_session)", mas);
        Assert.Contains("svc.TedarikciKopru.TopluEsle(s)", api);
    }

    [Fact]
    public void TK6_Var_Olan_Ad_Yeniden_Eklenince_Tek_Cari()
    {
        HamEkle("INSERT INTO suppliers(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('s9','TK','Eski Tedarikçi',1,1,1,0);");
        Assert.Equal("s9", _tanim.AddSupplier(_y, "Eski Tedarikçi", quick: true));
        Assert.Equal("s9", _tanim.AddSupplier(_y, "Eski Tedarikçi", quick: true));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM parties WHERE company_id='TK' AND supplier_id='s9'"));
    }
}
