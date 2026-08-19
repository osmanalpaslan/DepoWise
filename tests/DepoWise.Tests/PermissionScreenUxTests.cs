using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YET-C — YETKİ EKRANLARININ ARAYÜZ KİLİTLERİ (kullanıcı isteği 2026-08-19) ═══
///
/// Bu tur üç somut şikâyeti düzeltti; buradaki testler <b>geri gelmelerini engeller</b>:
/// <list type="number">
///   <item><b>Sonsuz yükleme:</b> Rol/Firma Yetki Kontrol ekranları yükleme hatasında dönen
///   tekerlekte kalıyor ve hatayı hiç göstermiyordu.</item>
///   <item><b>Düzenleme adımı yok:</b> Yetkiler ekranında ağaç daima açıktı; yanlış tıklanan kutu
///   doğrudan değişiyordu.</item>
///   <item><b>İkon rayı:</b> masaüstünde soldaki dikey simge şeridi yer kaplıyordu.</item>
/// </list>
///
/// <b>Kaynak metnine bakılır</b> (davranış testi değil): bunlar arayüz kuralları ve tek doğrulama
/// yolu ilgili dosyanın gerçekten o kuralı taşıdığını görmektir. Aynı desen
/// <see cref="AppScreensParityTests"/> S10/S11'de de kullanılır.
/// </summary>
public class PermissionScreenUxTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Depo kökü bulunamadı.");
    }

    private static string Read(string rel)
        => File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 1 · SONSUZ YÜKLEME BİR DAHA OLMASIN
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// U1 — Yükleme başarısız olduğunda ekran <b>hatayı göstermeli</b>, sonsuza kadar dönen
    /// tekerlekte kalmamalı. İki koşul birlikte aranır: hata metni ayrı bir alanda tutulur
    /// (<c>_loadError</c>) ve <c>catch</c> içinde satır listesi <b>doldurulur</b> — aksi hâlde
    /// sayfa "yükleniyor" dalında takılı kalır.
    /// </summary>
    [Theory]
    [InlineData("src/DepoWise.Web/Components/Pages/RolePermissions.razor")]
    [InlineData("src/DepoWise.Web/Components/Pages/CompanyPermissions.razor")]
    public void U1_Yukleme_Hatasi_Ekranda_Gorunur(string dosya)
    {
        var src = Read(dosya);
        Assert.Contains("_loadError", src);                       // hata için ayrı alan var
        Assert.Contains("Yeniden dene", src);                     // kullanıcı kurtulabiliyor
        Assert.Contains("_rows = new();", src);                   // catch listeyi dolduruyor
        // Hata mesajı, tablo çizilmeden ÖNCE gelmeli (spinner dalının dışında).
        var hataIndex = src.IndexOf("@if (_loadError is not null)", StringComparison.Ordinal);
        // Dal işaretçileri: arama kutusundaki "Disabled=(_rows is null)" DEĞİL, gerçek @if dalları.
        var spinnerIndex = src.IndexOf("@if (_rows is null)", StringComparison.Ordinal);
        var tabloIndex = src.IndexOf("@if (_rows is not null)", StringComparison.Ordinal);
        var sonra = spinnerIndex >= 0 ? spinnerIndex : tabloIndex;
        Assert.True(hataIndex > 0 && hataIndex < sonra,
            "Yükleme hatası tablo/spinner dalından ÖNCE gösterilmeli, yoksa kullanıcı hatayı hiç göremez.");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 2 · YETKİLER EKRANI — DÜZENLE → KAYDET AKIŞI (İKİ ORTAM)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>U2 — WEB: matris varsayılan olarak KİLİTLİ, Düzenle/Kaydet/Vazgeç üçlüsü var.</summary>
    [Fact]
    public void U2_Web_Yetkiler_Duzenle_Kaydet_Akisi()
    {
        var src = Read("src/DepoWise.Web/Components/Pages/Permissions.razor");
        Assert.Contains("Locked=\"@(!_edit)\"", src);   // ⭐ ekran salt-okunur açılır
        Assert.Contains("OnClick=\"BeginEdit\"", src);
        Assert.Contains("OnClick=\"CancelEdit\"", src);
        Assert.Contains("private bool _edit;", src);
        // Kaydet YALNIZ düzenleme modunda görünür.
        Assert.Contains("!_targetIsAdmin && _edit", src);
    }

    /// <summary>U3 — MASAÜSTÜ: ağaç düzenleme modu olmadan açılmaz; Düzenle/Vazgeç komutları var.</summary>
    [Fact]
    public void U3_Masaustu_Yetkiler_Duzenle_Kaydet_Akisi()
    {
        var vm = Read("src/DepoWise.Desktop/ViewModels/PermissionsViewModel.cs");
        Assert.Contains("public bool TreeEnabled => HasUser && !IsTargetAdmin && IsEditing;", vm);
        Assert.Contains("private void BeginEdit()", vm);
        Assert.Contains("private async Task CancelEdit()", vm);

        var view = Read("src/DepoWise.Desktop/Views/PermissionsView.axaml");
        Assert.Contains("BeginEditCommand", view);
        Assert.Contains("CancelEditCommand", view);
    }

    /// <summary>
    /// U4 — Düzenleme moduna geçmek yetkileri SIFIRLAMAZ. Kullanıcının açık şikâyeti buydu:
    /// "düzenle dediğimde verilmiş olan yetkiler kaybolmamalı". <c>BeginEdit</c> yalnız bayrağı
    /// çevirir; ağaca/matrise dokunan hiçbir çağrı içermez.
    /// </summary>
    [Fact]
    public void U4_Duzenlemeye_Gecmek_Yetkileri_Silmez()
    {
        var vm = Read("src/DepoWise.Desktop/ViewModels/PermissionsViewModel.cs");
        var i = vm.IndexOf("private void BeginEdit()", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = vm.Substring(i, Math.Min(320, vm.Length - i));
        Assert.DoesNotContain("ResetTree", govde);
        Assert.DoesNotContain("BuildTree", govde);

        var web = Read("src/DepoWise.Web/Components/Pages/Permissions.razor");
        var j = web.IndexOf("private void BeginEdit()", StringComparison.Ordinal);
        Assert.True(j > 0);
        var wgovde = web.Substring(j, Math.Min(200, web.Length - j));
        Assert.DoesNotContain("LoadUserPerms", wgovde);
        Assert.DoesNotContain("LoadModules", wgovde);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 3 · ROL DEĞİŞİMİ AYNI EKRANDA
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// U5 — Rol seçimi Yetkiler ekranında; <b>kendi rolünü</b> değiştirmek engellidir (kilitlenme
    /// koruması) ve rol değişikliği yetki kaydından ÖNCE uygulanır (rol, yetki tavanını belirler).
    /// </summary>
    [Fact]
    public void U5_Rol_Degisimi_Yetkiler_Ekraninda()
    {
        var vm = Read("src/DepoWise.Desktop/ViewModels/PermissionsViewModel.cs");
        Assert.Contains("public ObservableCollection<RoleOption> Roles", vm);
        Assert.Contains("SelectedUser?.Id != _session.UserId", vm);      // kendi rolü kilitli
        Assert.Contains("SetRolesAsync(SelectedUser.Id, new[] { SelectedRole.Key })", vm);

        var web = Read("src/DepoWise.Web/Components/Pages/Permissions.razor");
        Assert.Contains("Label=\"Rol\"", web);
        Assert.Contains("Disabled=\"@(!_edit || _isSelf)\"", web);       // kendi rolü kilitli
        Assert.Contains("roles = new[] { _roleKey }", web);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 3b · İLK YÜKLEME DEVREYİ DÜŞÜRMESİN (gerçek arayüz turunda bulundu)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// U7 — Yetki ekranlarının <c>OnInitializedAsync</c> gövdesindeki sunucu çağrıları KORUMASIZ
    /// olmamalı. Korumasız bırakılırsa 401/500 yanıtı render sırasında patlar, <b>Blazor devresi
    /// tamamen düşer</b> ("bağlantı kesildi", bembeyaz ekran) ve kullanıcı hiçbir hata göremez.
    /// Bu, gerçek arayüz turunda <c>/permissions</c> adresinde yaşandı ve düzeltildi.
    /// </summary>
    [Theory]
    [InlineData("src/DepoWise.Web/Components/Pages/Permissions.razor")]
    [InlineData("src/DepoWise.Web/Components/Pages/PermissionTemplates.razor")]
    [InlineData("src/DepoWise.Web/Components/Pages/Users.razor")]
    public void U7_Ilk_Yukleme_Devreyi_Dusurmez(string dosya)
    {
        var src = Read(dosya);
        var i = src.IndexOf("protected override async Task OnInitializedAsync()", StringComparison.Ordinal);
        Assert.True(i > 0, "OnInitializedAsync bulunamadı: " + dosya);

        // Gövdeyi kabaca al (metot sonuna kadar) ve ilk sunucu çağrısının bir try korumasının
        // İÇİNDE kaldığını doğrula. Gövdede hiç try yoksa koruma da yoktur.
        var son = src.IndexOf("\n    }", i, StringComparison.Ordinal);
        var govde = src.Substring(i, Math.Max(0, son - i));
        var ilkTry = govde.IndexOf("try", StringComparison.Ordinal);
        var ilkAwait = govde.IndexOf("await ", StringComparison.Ordinal);
        Assert.True(ilkTry >= 0 && ilkAwait >= 0 && ilkTry < ilkAwait,
            "İlk sunucu çağrısı try korumasının dışında kalmış: " + dosya);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 4 · İKON RAYI KALDIRILDI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// U6 — Masaüstü kabuğunda dikey ikon rayı YOK; menü paneli ilk sütunda. Menüyü gizleyip geri
    /// açan üst bar düğmesi YERİNDE olmalı — aksi hâlde panel kapatıldığında menüsüz kalınırdı.
    /// </summary>
    [Fact]
    public void U6_Masaustunde_Ikon_Rayi_Yok()
    {
        var xaml = Read("src/DepoWise.Desktop/Views/MainWindow.axaml");
        Assert.DoesNotContain("Classes=\"NavRail\"", xaml);
        Assert.DoesNotContain("ColumnDefinitions=\"56,Auto,*\"", xaml);
        Assert.Contains("ColumnDefinitions=\"Auto,*\"", xaml);
        Assert.Contains("ToggleNavPanelCommand", xaml);                  // menüye geri dönüş yolu duruyor

        // Ölü kod da kalmamalı: ray gidince SelectGroup komutunun tek çağıranı kalmıyordu.
        var shell = Read("src/DepoWise.Desktop/ViewModels/ShellViewModel.cs");
        Assert.DoesNotContain("private void SelectGroup(", shell);
    }
}
