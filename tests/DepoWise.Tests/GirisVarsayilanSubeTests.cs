using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ GİRİŞTE VARSAYILAN ŞUBE — SUNUCU OTORİTEDİR (kullanıcı bildirimi 2026-09-06) ═══
///
/// <b>Kullanıcının bildirdiği hata.</b> <i>"Babamın kullanıcısını ve makinesini Düzce olarak atamıştım
/// ama hâlâ eski şubesi Karaman'ı login olurken getiriyor. Şube ve kullanıcı ataması hangi şubeye
/// yapılmışsa login olurken ilk onu getirmeli."</i>
///
/// <b>Ölçülen gerçek (canlı, salt-okunur).</b> Sunucu ATAMAYI DOĞRU TUTUYORDU: kullanıcının
/// <c>branchId</c>'si DÜZCE, makinenin şubesi DÜZCE, şube kapsamında DÜZCE var. Hata sunucuda değil,
/// masaüstünün varsayılanı YEREL AYNADAN okumasındaydı.
///
/// <b>Kök neden sınıfı.</b> Çevrimiçi girişte sunucu paketi yerele üç adımda yazılır
/// (<c>ImportRemoteUser</c> → <c>BranchMirror.RefreshAsync</c> → <c>ImportUserScopes</c>) ve bu üç adım
/// TEK bir <c>try/catch</c> içindedir — biri sessizce düşerse oturum yine kurulur, ama giriş ekranı
/// ESKİ şubeyi önerir. Tek bir dalı düzeltmek yerine sınıfın tamamı kapatıldı: çevrimiçi girişte
/// otorite doğrudan sunucunun yanıtıdır.
///
/// <b>İkinci kilit.</b> Kapsam kırpması kullanıcının KENDİ şubesini listeden atmamalı; atarsa kullanıcı
/// kendi şubesini seçemez ve yine makinenin eski şubesine düşer. Bu bir yetki gevşetmesi değildir —
/// asıl kapı serviste (<c>BranchAccess</c>) ve sunucudadır.
/// </summary>
public class GirisVarsayilanSubeTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    /// <summary>VS1 — Sunucu yanıtı kullanıcının şubesini TAŞIR (taşımazsa otorite yerelde kalırdı).</summary>
    [Fact]
    public void VS1_Sunucu_Yaniti_Kullanicinin_Subesini_Tasir()
    {
        var istemci = Oku("src", "DepoWise.Desktop", "ServerAuthClient.cs");

        Assert.Contains("public readonly record struct AuthResult(AuthState State, string? CompanyId,", istemci);
        Assert.Contains("string? BranchId = null", istemci);
        // Başarılı girişte paketin şubesi GERÇEKTEN döndürülmeli — alan eklenip boş bırakılırsa hata sürerdi.
        Assert.Contains("return new(AuthState.Ok, bundle.CompanyId, bundle.BranchId, bundle.ScopeBranchIds);", istemci);
    }

    /// <summary>VS2 — 🔴 Çevrimiçi girişte varsayılan şube SUNUCUDAN, çevrimdışında yerel aynadan.</summary>
    [Fact]
    public void VS2_Cevrimici_Otorite_Sunucu_Cevrimdisi_Yerel()
    {
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "LoginViewModel.cs");

        Assert.Contains(
            "var userBranchId = _online && !string.IsNullOrEmpty(srv.BranchId) ? srv.BranchId : yerelSube;",
            vm);

        // Varsayılan seçim hâlâ "önce kullanıcının şubesi, sonra makine şubesi" sırasında olmalı.
        var i = vm.IndexOf("SelectedBranch = Branches.FirstOrDefault(b => b.Id == userBranchId)", StringComparison.Ordinal);
        Assert.True(i > 0, "Varsayılan seçim kullanıcının şubesinden başlamıyor.");
        var devam = vm.Substring(i, Math.Min(220, vm.Length - i));
        Assert.Contains("DesktopServices.MachineBranchId", devam);   // yedek yol duruyor
    }

    /// <summary>VS3 — Kullanıcının kendi şubesi kapsam kırpmasına TAKILMAZ (yoksa seçilemezdi).</summary>
    [Fact]
    public void VS3_Kendi_Subesi_Kapsam_Kirpmasina_Takilmaz()
    {
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "LoginViewModel.cs");

        Assert.Contains("private string? _evSubesiId;", vm);
        Assert.Contains("_evSubesiId = userBranchId;", vm);
        Assert.Contains("|| (evSubesi is not null && b.Id == evSubesi))", vm);
    }

    /// <summary>
    /// VS4 — Kullanıcı seçimi DEĞİŞTİREBİLMELİ. Kullanıcının açık isteği: <i>"bu aşamada kendim istersem
    /// değiştirebilmeliyim."</i> Şube adımı atlanmamalı ve liste tek seçeneğe kilitlenmemeli.
    /// </summary>
    [Fact]
    public void VS4_Kullanici_Subeyi_Degistirebilir()
    {
        var view = Oku("src", "DepoWise.Desktop", "Views", "LoginWindow.axaml");

        // Şube adımında GERÇEK bir açılır liste var ve kullanıcı seçimi iki yönlü bağlı.
        Assert.Contains("ItemsSource=\"{Binding Branches}\"", view);
        Assert.Contains("SelectedItem=\"{Binding SelectedBranch", view);
    }
}
