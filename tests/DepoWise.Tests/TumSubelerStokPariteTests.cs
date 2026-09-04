using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ STK-12 — "TÜM ŞUBELER" MODUNDA STOK İŞLEMİ: WEB ↔ MASAÜSTÜ PARİTE NÖBETİ (2026-09-04) ═══
///
/// <b>Kapatılan fark:</b> aynı iş iki platformda farklı davranıyordu. Web (STK-04) "Tüm Şubeler"
/// ile giren kullanıcının stok işlemi yapmasına — <i>depoyu açıkça seçmesi şartıyla</i> — izin
/// veriyordu; masaüstünde <c>BranchGuard.RequireBranchAsync</c> Kaydet'in tamamını kapatıyordu.
/// Çok depolu firmada yönetici masaüstünde hiç stok işlemi yapamıyor, çıkıp tek şube seçerek
/// yeniden girmek zorunda kalıyordu.
///
/// <b>Koruma KALDIRILMADI, YERİ DEĞİŞTİ:</b>
/// <c>"şube seçmeden hiçbir şey yapamazsın"</c> → <c>"işlemin yazılacağı depoyu açıkça seç"</c>.
/// Sonuç aynı: şubesiz (belirsiz) stok hareketi <b>oluşamaz</b>. Bu testler tam olarak bunu kilitler
/// — kapı gevşetilirse (ör. lokasyon boşken kayıt yolu açılırsa) burada patlar.
///
/// <b>Neden metin üzerinden test:</b> ekranlar Avalonia XAML ve Razor'dur, bu ortamda render
/// edilemez (bkz. <c>MasaustuTasarimPaketiTests</c>) — bu yüzden iki ekranın <b>sözleşmesi</b>
/// kilitlenir. Servis davranışı (branchId parametresi, negatif stok, transaction) zaten kendi
/// testlerinde kapsanıyor; burada tekrarlanmaz.
///
///  TSB1 — Giriş-Çıkış: lokasyon oturumdan DEĞİL etkin lokasyondan gelir (iki platformda da)
///  TSB2 — Giriş-Çıkış: lokasyon boşken kayıt YAPILMAZ (eski tümden-engel geri gelmemeli)
///  TSB3 — Sayım: aynı kapı, aynı gerekçe
///  TSB4 — Depo seçilmeden BAKİYE bile okunmaz (yanlış sayı göstermektense boş)
///  TSB5 — "Tüm Şubeler" modunda kullanıcı YÖNLENDİRİLİR (bant + seçici), sessizce kilitlenmez
///  TSB6 — Sayımda depo değişince sepet TEMİZLENİR (sistem stokları eski depoya aitti)
/// </summary>
public class TumSubelerStokPariteTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string WebGiris()  => Oku("src", "DepoWise.Web", "Components", "Pages", "Stock.razor");
    private static string WebSayim()  => Oku("src", "DepoWise.Web", "Components", "Pages", "StockCount.razor");
    private static string GirisVm()   => Oku("src", "DepoWise.Desktop", "ViewModels", "StockEntryViewModel.cs");
    private static string GirisView() => Oku("src", "DepoWise.Desktop", "Views", "StockEntryView.axaml");
    private static string SayimVm()   => Oku("src", "DepoWise.Desktop", "ViewModels", "StockCountViewModel.cs");
    private static string SayimView() => Oku("src", "DepoWise.Desktop", "Views", "StockCountView.axaml");

    /// <summary>Yorum satırlarını atar — açıklama metni testi yanlışlıkla doğrulamasın.</summary>
    private static string KodSadece(string s)
    {
        var blok = System.Text.RegularExpressions.Regex.Replace(s, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        var satirlar = blok.Split('\n').Where(l => !l.TrimStart().StartsWith("//"));
        return string.Join("\n", satirlar);
    }

    [Fact]
    public void TSB1_Lokasyon_Oturumdan_Degil_Etkin_Lokasyondan_Gelir()
    {
        // Web: şubeli kullanıcıda oturum şubesi (değiştirilemez), "Tüm Şubeler"de seçilen depo.
        Assert.Contains("Auth.IsAllBranches() ? NullIfEmpty(_workLocation) : Auth.BranchId", WebGiris());

        // Masaüstü: birebir aynı ifade. Kritik nokta boş metne ("Atanmamış") DÜŞMEMESİDİR —
        // yeni kayıt belirsiz olamaz, bu yüzden tip nullable.
        var vm = KodSadece(GirisVm());
        Assert.Contains("public string? EtkinLokasyon => IsAllBranches ? CalismaDeposu?.Id : _session.OperatingBranchId;", vm);

        // ...ve üç işlem yolunun HEPSİ bu lokasyonu kullanır (biri unutulursa hareket yanlış depoya yazılır).
        Assert.Contains("branchId: EtkinLokasyon", vm);
        Assert.Contains("var from = EtkinLokasyon;", vm);
    }

    [Fact]
    public void TSB2_Giris_Cikis_Lokasyon_Bosken_Kayit_Yapilmaz()
    {
        // Web'deki kapı
        Assert.Contains("if (EffectiveLocation is null)", WebGiris());

        // Masaüstündeki AYNI kapı — eski tümden-engelin yerine geçti.
        var vm = KodSadece(GirisVm());
        Assert.Contains("if (EtkinLokasyon is null)", vm);
        Assert.Contains("Önce işlemin yapılacağı depoyu/şantiyeyi seçin.", vm);

        // 🔴 GERİLEME KİLİDİ: eski tümden-engel geri gelmemeli. Geri gelirse depo seçici anlamsızlaşır
        // ve kullanıcı yine çıkıp yeniden giriş yapmak zorunda kalır.
        Assert.DoesNotContain("BranchGuard.RequireBranchAsync(_session, \"Stok", vm);
    }

    [Fact]
    public void TSB3_Sayimda_Ayni_Kapi()
    {
        Assert.Contains("if (EffectiveLocation is null)", WebSayim());

        var vm = KodSadece(SayimVm());
        Assert.Contains("public string? CountLocationId => IsAllBranches ? CalismaDeposu?.Id : _session.OperatingBranchId;", vm);
        Assert.Contains("Önce sayımın yapılacağı depoyu/şantiyeyi seçin.", vm);
        Assert.Contains("branchId: sayimDeposu", vm);
        Assert.DoesNotContain("BranchGuard.RequireBranchAsync(_session, \"Stok Sayım\")", vm);
    }

    [Fact]
    public void TSB4_Depo_Secilmeden_Bakiye_Okunmaz()
    {
        // Depo seçilmeden firma geneli toplamı göstermek kullanıcıyı YANILTIR: 10'luk depoyu sayarken
        // ekranda 15 görür, farkı yanlış hesaplar. Web bunu zaten yapmıyordu.
        Assert.Contains("Bakiye için önce depo seçin.", WebGiris());

        var girisVm = KodSadece(GirisVm());
        Assert.Contains("var loc = EtkinLokasyon;", girisVm);
        Assert.Contains("Bakiye için önce depo seçin.", girisVm);

        var sayimVm = KodSadece(SayimVm());
        Assert.Contains("var loc = CountLocationId;", sayimVm);
        Assert.Contains("if (string.IsNullOrEmpty(loc)) { SystemBalance = 0; }", sayimVm);
    }

    [Fact]
    public void TSB5_Kullanici_Yonlendirilir_Sessizce_Kilitlenmez()
    {
        // Kapıyı koymak yetmez: kullanıcı NE yapması gerektiğini görmeli. Aksi hâlde "kaydet çalışmıyor"
        // şikayetine döner. Web'de bant + zorunlu seçici var; masaüstünde de aynısı olmalı.
        foreach (var view in new[] { GirisView(), SayimView() })
        {
            Assert.Contains("IsVisible=\"{Binding IsAllBranches}\"", view);
            Assert.Contains("{Binding TumSubelerUyarisi}", view);
            Assert.Contains("SelectedItem=\"{Binding CalismaDeposu}\"", view);
        }

        // Şubeye bağlı kullanıcıda depo DEĞİŞTİRİLEMEZ — seçici yalnız "Tüm Şubeler" modunda çıkar.
        Assert.Contains("IsVisible=\"{Binding !IsAllBranches}\"", GirisView());
        Assert.Contains("IsVisible=\"{Binding !IsAllBranches}\"", SayimView());
    }

    [Fact]
    public void TSB6_Sayimda_Depo_Degisince_Sepet_Temizlenir()
    {
        // Sepetteki "sistem stoğu" değerleri EKLENDİĞİ deponun bakiyesiydi. Depo değişince o sayılar
        // yanlıştır; ekranda bırakmak sessizce hatalı fark hesabı demektir (STK-05'te düzeltilen kusurun
        // yeni bir biçimde geri gelmesi). Bu yüzden liste temizlenir ve kullanıcı bilgilendirilir.
        var vm = KodSadece(SayimVm());
        Assert.Contains("partial void OnCalismaDeposuChanged(BranchRow? value)", vm);
        Assert.Contains("CountLines.Clear();", vm);
        Assert.Contains("Depo değişti — sayım listesi temizlendi", vm);
    }
}
