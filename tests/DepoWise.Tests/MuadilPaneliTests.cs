using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-04 (kullanıcı isteği) — muadil malzemeler:
/// MDP1 sağ bilgi panelinde muadiller KOD + ad ile görünür (MaterialRefRow.Display).
/// MDP2 çift tık penceresinde muadiller listelenir; Düzelt modunda ekle/çıkar, Kaydet'te Add/RemoveEquivalent.
/// MDP3 web çift tık penceresi: kod + ad listesi, düzenleme ve "değişmediyse null gönder" kuralı.
/// MDP4 servis: ekleme/çıkarma iki yönlü çalışır (pencerenin kullandığı uzlaştırma).
/// </summary>
public class MuadilPaneliTests
{
    private static string Kaynak(params string[] parcalar)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DepoWise.sln"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!, "src" }.Concat(parcalar).ToArray()));
    }

    [Fact]
    public void MDP1_Bilgi_Paneli_Muadilleri_Kod_Ve_Adla_Gosterir()
    {
        var x = Kaynak("DepoWise.Desktop", "Views", "MaterialsView.axaml");
        Assert.Contains("Content=\"{Binding Display, StringFormat='• {0}'}\"", x);
    }

    [Fact]
    public void MDP2_Cift_Tik_Penceresi_Muadilleri_Listeler_Ve_Duzenler()
    {
        var x = Kaynak("DepoWise.Desktop", "Views", "MaterialQuickEditWindow.axaml");
        Assert.Contains("x:Name=\"EquivPanel\"", x);
        Assert.Contains("x:Name=\"EquivSearch\"", x);
        var cs = Kaynak("DepoWise.Desktop", "Views", "MaterialQuickEditWindow.axaml.cs");
        Assert.Contains("Text = e.Display", cs);
        Assert.Contains("DesktopServices.Materials.AddEquivalent(session, materialId, ekle)", cs);
        Assert.Contains("DesktopServices.Materials.RemoveEquivalent(session, materialId, cikar)", cs);
        Assert.Contains("if (EquivDirty()) n++;", cs);   // değişiklik sayacına dahil
    }

    [Fact]
    public void MDP3_Web_Cift_Tik_Penceresi_Muadil_Duzenler()
    {
        var r = Kaynak("DepoWise.Web", "Components", "MaterialEditDialog.razor");
        Assert.Contains("Muadil Malzemeler", r);
        Assert.Contains("$\"{ec} — {en}\"", r);
        Assert.Contains("SetEquals(_origEquiv)", r);   // değişmediyse null → sunucu korur
        Assert.Contains("equivalentIds =", r);
    }

    [Fact]
    public void MDP4_Ekle_Cikar_Iki_Yonlu()
    {
        var db = Path.Combine(Path.GetTempPath(), "depowise_mdp_" + Guid.NewGuid().ToString("N") + ".db");
        var f = new DepoWise.Infrastructure.Database.SqliteConnectionFactory(db);
        new DepoWise.Infrastructure.Database.Migrations.MigrationRunner(f).Run();
        var clock = new DepoWise.Application.Common.SystemClock();
        var users = new DepoWise.Infrastructure.Security.UserService(f, clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", DepoWise.Application.Security.RoleKeys.CompanyAdmin);
        var s = new DepoWise.Application.Security.SessionContext(uid, "A",
            new[] { DepoWise.Application.Security.RoleKeys.CompanyAdmin }, DepoWise.Application.Security.PermissionSet.Empty);
        var m = new DepoWise.Infrastructure.Materials.MaterialService(f, clock);
        var a = m.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial("M-A", "Filtre A"));
        var b = m.Create(s, new DepoWise.Infrastructure.Materials.NewMaterial("M-B", "Filtre B"));

        m.AddEquivalent(s, a, b);
        var dA = m.GetDetail(s, a)!;
        Assert.Contains(dA.Equivalents, e => e.Id == b && e.Display == "M-B — Filtre B");
        Assert.Contains(m.GetDetail(s, b)!.Equivalents, e => e.Id == a);   // iki yönlü

        m.RemoveEquivalent(s, a, b);
        Assert.Empty(m.GetDetail(s, a)!.Equivalents);
        Assert.Empty(m.GetDetail(s, b)!.Equivalents);
    }
}
