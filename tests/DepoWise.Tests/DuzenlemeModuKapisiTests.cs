using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ DÜZENLEME MODU KAPISI — ŞUBE KAPSAMI + KORUMALI ALANLAR (kullanıcı bildirimi 2026-09-06) ═══
///
/// <b>Kullanıcının bildirdiği hata.</b> <i>"Şube kapsamı ve korumalı alanlar gibi bölümlerde düzenle
/// butonuna tıklamadan aktif veya pasif yapabiliyorum… butona tıklanmamış ise yetkilerinin hiçbiri
/// değişmemeli."</i>
///
/// <b>Neden ciddiydi.</b> Korumalı alan kutusu <b>anında kaydediyordu</b> (kaydet düğmesi yok): yanlışlıkla
/// atılan tek tık, FİRMA genelinde bir alanı herkesten gizleyebiliyordu. Şube kapsamında ise kutular
/// düzenleme modu dışında da tıklanabiliyor, sonra "Kaydet" ile yazılabiliyordu — ekranın geri kalanı
/// (yetki ağacı, rol, şablon) düzenleme modu isterken bu iki bölüm istemiyordu.
///
/// <b>Bu test neyi kilitler.</b> İki platformda da bu bölümlerin düzenleme moduna bağlı olduğunu ve
/// arayüz kilidi atlansa bile yazma yolunun kapalı kaldığını (ikinci kapı) doğrular. Kaynak metni
/// üzerinden çalışır: davranış GUI'de yaşadığı için birim testiyle koşturulamaz, ama sözleşmenin
/// sessizce geri alınmasını engeller.
/// </summary>
public class DuzenlemeModuKapisiTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    // ══════════════════ MASAÜSTÜ ══════════════════

    /// <summary>DK1 — Şube kapsamı düzenleme moduna bağlı; bölüm yine de GÖRÜNÜR (salt-okunur).</summary>
    [Fact]
    public void DK1_Masaustu_Sube_Kapsami_Duzenleme_Moduna_Bagli()
    {
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "PermissionsViewModel.cs");

        // Düzenlenebilirlik IsEditing ister…
        Assert.Matches(new Regex(@"public bool CanEditScope => IsEditing\b"), vm);
        // …ama GÖRÜNÜRLÜK ayrı bir alandır (bölüm kaybolmamalı, mevcut kapsam okunabilmeli).
        Assert.Contains("public bool ScopeGorunur =>", vm);
        Assert.DoesNotContain("public bool ScopeGorunur => IsEditing", vm);

        var view = Oku("src", "DepoWise.Desktop", "Views", "PermissionsView.axaml");
        Assert.Contains("IsVisible=\"{Binding ScopeGorunur}\" IsEnabled=\"{Binding CanEditScope}\"", view);
    }

    /// <summary>DK2 — 🔴 Korumalı alanlar: düzenleme modu ŞART + servise giden yolda İKİNCİ KAPI.</summary>
    [Fact]
    public void DK2_Masaustu_Korumali_Alanlar_Duzenleme_Moduna_Bagli()
    {
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "PermissionsViewModel.cs");

        Assert.Contains("public bool KorumaDuzenlenebilir => KorumaYonetebilir && IsEditing;", vm);

        // İkinci kapı: yazma metodunun İLK işi düzenleme modunu doğrulamak olmalı.
        var i = vm.IndexOf("private void KorumaDegistir(", StringComparison.Ordinal);
        Assert.True(i > 0, "KorumaDegistir bulunamadı.");
        var govde = vm.Substring(i, Math.Min(400, vm.Length - i));
        Assert.Contains("if (!KorumaDuzenlenebilir) return;", govde);
        // Kapı, servise gitmeden ÖNCE olmalı.
        Assert.True(govde.IndexOf("if (!KorumaDuzenlenebilir) return;", StringComparison.Ordinal)
                    < govde.IndexOf("FieldProtections.Set", StringComparison.Ordinal),
            "Düzenleme modu kontrolü servis çağrısından SONRA — kapı işe yaramaz.");

        var view = Oku("src", "DepoWise.Desktop", "Views", "PermissionsView.axaml");
        Assert.Contains("IsEnabled=\"{Binding KorumaDuzenlenebilir}\"", view);
    }

    /// <summary>
    /// DK3 — Düzenleme moduna GİRİŞ yolu kapanmamalı. Korumalı alanlar FİRMA ayarıdır ve kullanıcı
    /// seçilmeden de yönetilir; "Düzenle" yalnız "kullanıcı seçili" koşuluna bağlı kalsaydı bu bölüm
    /// düzenleme moduna alınınca HİÇ düzenlenemez hâle gelirdi (çözümsüzlük).
    /// </summary>
    [Fact]
    public void DK3_Masaustu_Duzenle_Dugmesi_Korumali_Alanlar_Icin_De_Acilir()
    {
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "PermissionsViewModel.cs");
        var i = vm.IndexOf("public bool CanBeginEdit =>", StringComparison.Ordinal);
        Assert.True(i > 0);
        var ifade = vm.Substring(i, Math.Min(220, vm.Length - i));
        Assert.Contains("KorumaYonetebilir", ifade);
    }

    // ══════════════════ WEB (aynı kural) ══════════════════

    /// <summary>DK4 — Web'de de aynı kapı: iki bölüm de düzenleme moduna bağlı.</summary>
    [Fact]
    public void DK4_Webde_De_Ayni_Kapi_Var()
    {
        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "Permissions.razor");

        // Şube kapsamı: seçim kutusu ve kaydetme düğmesi düzenleme modu ister.
        Assert.Contains("SelectedValues=\"_scopeSelected\" Disabled=\"@(!_edit)\"", web);
        Assert.Contains("OnClick=\"SaveScope\" Disabled=\"@(_busy || !_edit)\"", web);

        // Korumalı alanlar: kutu düzenleme modu ister.
        Assert.Contains("Disabled=\"@(_korumaBusy || !_edit)\"", web);

        // İkinci kapı: yazma metodunun ilk işi.
        var i = web.IndexOf("private async Task KorumaDegistir(", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = web.Substring(i, Math.Min(400, web.Length - i));
        Assert.Contains("if (!_edit) return;", govde);
        Assert.True(govde.IndexOf("if (!_edit) return;", StringComparison.Ordinal)
                    < govde.IndexOf("field-protections", StringComparison.Ordinal),
            "Düzenleme modu kontrolü istek gönderildikten SONRA — kapı işe yaramaz.");
    }
}
