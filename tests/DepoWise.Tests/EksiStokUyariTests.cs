using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ K-05 (kullanıcı kararı) — BİLİNÇLİ NEGATİF STOK İSTİSNALARI UYARIYLA ═══
/// Bakım malzemesi ve açılış stoğu eksiye düşebilir (CLAUDE.md §3), ama kayıt anında kullanıcı UYARILIR.
///
///  EKS1 — Açılış stoğu eksi girilince masaüstü ve web AYNI uyarı metniyle ayrıca onay ister (yalnız yeni kayıtta)
///  EKS2 — Masaüstü ekipman bakımı, araç bakımı gibi seçilen deponun stoğuna bakar; yetersizse kayıttan ÖNCE uyarır
///  EKS3 — Kural metni CLAUDE.md §3'te (istisna yazılı; sessiz davranış değil)
/// </summary>
public class EksiStokUyariTests
{
    private static string Kok()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        return dir;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel.Replace('/', Path.DirectorySeparatorChar)));

    private const string AcilisMetni = "Açılış stoğu EKSİ girildi (";

    [Fact]
    public void EKS1_Eksi_Acilis_Stogu_Iki_Platformda_Ayni_Uyari()
    {
        var masa = Oku("src/DepoWise.Desktop/ViewModels/MaterialsViewModel.cs");
        var web = Oku("src/DepoWise.Web/Components/Pages/Materials.razor");
        Assert.Contains(AcilisMetni, masa);
        Assert.Contains(AcilisMetni, web);
        Assert.Contains("!editing && NewOpeningStock < 0", masa);
        Assert.Contains("_editId is not null ? \"Malzeme güncellensin mi?\"", web);   // düzenlemede açılış stoğu yok
        // Uyarı kayıttan ÖNCE: masaüstünde Create çağrısından önce gelir.
        Assert.True(masa.IndexOf(AcilisMetni, StringComparison.Ordinal) < masa.IndexOf("DesktopServices.Materials.Create(", StringComparison.Ordinal));
        Assert.Contains("danger: _editId is null && _opening < 0", web);   // web de tehlike görünümünde (iki ortam denetimi)
    }

    [Fact]
    public void EKS2_Ekipman_Bakimi_Yetersiz_Stokta_Once_Uyarir()
    {
        var src = Oku("src/DepoWise.Desktop/ViewModels/MaintenanceViewModel.cs");
        var bas = src.IndexOf("private async Task SaveEquipmentMaintenance()", StringComparison.Ordinal);
        Assert.True(bas > 0);
        var govde = src[bas..src.IndexOf("DesktopServices.EquipmentMaintenance.Save(", bas, StringComparison.Ordinal)];
        Assert.Contains("GetBalanceAt(_session, l.MaterialId, eqmKonum)", govde);
        Assert.Contains("if (l.FromTeamStock) continue;", govde);
        Assert.Contains("\"Yetersiz Stok\"", govde);
    }

    /// <summary>EKS4 — M-16/K-06: depo bazlı eksi stok ana ekran uyarısında görünür (malzeme + depo + miktar); firma toplamı
    /// artıda olsa da; şube kapsamı dışındaki deponun uyarısı o kullanıcıya görünmez; eksi tutar değişince uyarı yeniden açılır.</summary>
    [Fact]
    public void EKS4_Eksi_Stok_Uyarisi_Depo_Bazli_Ve_Kapsamli()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_eks_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new DepoWise.Infrastructure.Database.SqliteConnectionFactory(yol);
            new DepoWise.Infrastructure.Database.Migrations.MigrationRunner(f).Run();
            void Sql(string q) { using var c = f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = q; cmd.ExecuteNonQuery(); }
            Sql("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('EKS','EKS',1,1,1,0);" +
                "INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) VALUES('B1','EKS','Şantiye 1','branch',1,1,1,0),('B2','EKS','Şantiye 2','branch',1,1,1,0);" +
                "INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES('M1','EKS','CMT-1','Çimento',1,1,1,0);" +
                "INSERT INTO stock_balances(company_id, material_id, location_id, quantity, updated_at) VALUES('EKS','M1','B1','-3',1),('EKS','M1','B2','10',1);");
            var uid = new DepoWise.Infrastructure.Security.UserService(f).EnsureInitialAdmin("EKS", "admin_eks", "admin123", DepoWise.Application.Security.RoleKeys.CompanyAdmin);
            DepoWise.Application.Security.SessionContext Oturum(string? sube) =>
                new(uid, "EKS", new[] { DepoWise.Application.Security.RoleKeys.CompanyAdmin }, DepoWise.Application.Security.PermissionSet.Empty) { OperatingBranchId = sube };
            var dash = new DepoWise.Infrastructure.Reporting.DashboardService(f, new DepoWise.Infrastructure.Maintenance.MaintenanceService(f),
                new DepoWise.Infrastructure.Maintenance.InspectionService(f));

            var u = Assert.Single(dash.GetSummary(Oturum(null)).Alerts, a => a.Title.StartsWith("Eksi stok", StringComparison.Ordinal));
            Assert.Equal("Eksi stok: Çimento (Şantiye 1)", u.Title);   // depo başlıkta → uyarı anahtarı depo başına ayrı
            Assert.Equal("CMT-1 · Şantiye 1 · stok -3", u.Detail);
            Assert.True(u.IsCritical);

            Assert.DoesNotContain(dash.GetSummary(Oturum("B2")).Alerts, a => a.Title.StartsWith("Eksi stok", StringComparison.Ordinal));   // kapsam dışı depo
            Assert.Contains(dash.GetSummary(Oturum("B1")).Alerts, a => a.Title.StartsWith("Eksi stok", StringComparison.Ordinal));

            Sql("UPDATE stock_balances SET quantity='-5' WHERE location_id='B1';");
            var u2 = Assert.Single(dash.GetSummary(Oturum(null)).Alerts, a => a.Title.StartsWith("Eksi stok", StringComparison.Ordinal));
            Assert.NotEqual(u.SignatureOverride, u2.SignatureOverride);   // tutar değişti → okunmuş uyarı yeniden açılır
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { File.Delete(yol); } catch { } }
    }

    [Fact]
    public void EKS3_Istisna_Kurali_Yazili()
    {
        var kural = Oku("CLAUDE.md");
        Assert.Contains("bakım malzemesi tüketimi ve açılış stoğu eksiye", kural);
        Assert.Contains("UYARI", kural);
    }
}
