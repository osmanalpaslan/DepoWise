using DepoWise.Application.Common;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ BAG-01 · "SUNUCUYA ULAŞILAMIYOR" DURUMU ═══ (denetim 2026-08-26, dördüncü tur)
///
/// <b>Bulunan durum (gerçek tarayıcıda gözlendi).</b> API kapalıyken web <b>oturumu düşürmüyordu</b>
/// (doğru davranış) ama ekran neredeyse boş kalıyor, menü varsayılana düşüyor ve kullanıcıya <b>sebep
/// söylenmiyordu</b>. Yazılım bilgisi olmayan kullanıcı "uygulama bozuldu" sanıyor.
///
/// <b>Bu sınıf neyi test eder:</b> işin RİSKLİ kısmını — <b>ağ hatası ile uygulama/yetki hatasının
/// ayrımını</b>. Karar mantığı bilerek <see cref="BaglantiIzleyici"/> içindedir; web projesi ortak
/// dosyaların aynasını derlediği için test projesine referans verilemez (verilirse mevcut testlerde
/// tür çakışması olur — denendi ve geri alındı). Arayüz tarafı (uyarı şeridi + "Tekrar Dene")
/// <b>gerçek tarayıcıda</b> doğrulanmıştır.
///
/// <b>Kural (dar kapsam):</b> yalnız taşıma katmanı hatası "ulaşılamıyor"dur; sunucudan gelen
/// 401/403/404/500 <b>bağlantı sorunu sayılmaz</b> ve oturuma dokunulmaz.
/// </summary>
public class WebBaglantiDurumuTests
{
    /// <summary>⭐ BAG-01a — bağlantı kurulamazsa bayrak YANAR.</summary>
    [Fact]
    public async Task BAG01a_Baglanti_Kurulamazsa_Bayrak_Yanar()
    {
        var izleyici = new BaglantiIzleyici();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            izleyici.Calistir<int>(() => throw new HttpRequestException("bağlanılamadı")));

        Assert.True(izleyici.Ulasilamiyor);
    }

    /// <summary>⭐ BAG-01b — zaman aşımı da "ulaşılamıyor" sayılır.</summary>
    [Fact]
    public async Task BAG01b_Zaman_Asimi_Da_Bayragi_Yakar()
    {
        var izleyici = new BaglantiIzleyici();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            izleyici.Calistir<int>(() => throw new TaskCanceledException("zaman aşımı")));

        Assert.True(izleyici.Ulasilamiyor);
    }

    /// <summary>
    /// ⭐ BAG-01c — <b>EN KRİTİK AYRIM</b>: sunucudan YANIT geldiyse (401/403/500 dahil) bağlantı vardır.
    /// Bu testte istek başarıyla döner (yanıt nesnesi taşınır) → bayrak yanmamalı.
    /// </summary>
    [Fact]
    public async Task BAG01c_Sunucu_Yaniti_Baglanti_Sorunu_Sayilmaz()
    {
        var izleyici = new BaglantiIzleyici();

        var kod = await izleyici.Calistir(() => Task.FromResult(401));   // 401 = sunucu yanıtı

        Assert.Equal(401, kod);
        Assert.False(izleyici.Ulasilamiyor);
    }

    /// <summary>
    /// ⭐ BAG-01d — sunucu hatası TÜRÜNDEN gelen istisnalar (JSON, iş kuralı, doğrulama) bağlantı
    /// sorunu sayılmaz; bayrak YANMAZ ve istisna aynen geçer.
    /// </summary>
    [Fact]
    public async Task BAG01d_Uygulama_Istisnasi_Baglanti_Sorunu_Sayilmaz()
    {
        var izleyici = new BaglantiIzleyici();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            izleyici.Calistir<int>(() => throw new InvalidOperationException("iş kuralı")));

        Assert.False(izleyici.Ulasilamiyor);
    }

    /// <summary>⭐ BAG-01e — bağlantı geri gelince bayrak SÖNER (kullanıcı takılı kalmaz).</summary>
    [Fact]
    public async Task BAG01e_Baglanti_Gelince_Bayrak_Soner()
    {
        var izleyici = new BaglantiIzleyici();
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            izleyici.Calistir<int>(() => throw new HttpRequestException("yok")));
        Assert.True(izleyici.Ulasilamiyor);

        await izleyici.Calistir(() => Task.FromResult(200));

        Assert.False(izleyici.Ulasilamiyor);
    }

    /// <summary>BAG-01f — olay yalnız DEĞİŞİMDE tetiklenir (her istekte gereksiz arayüz çizimi olmaz).</summary>
    [Fact]
    public async Task BAG01f_Olay_Yalniz_Degisimde_Tetiklenir()
    {
        var izleyici = new BaglantiIzleyici();
        int sayac = 0;
        izleyici.Degisti += () => sayac++;

        for (int i = 0; i < 3; i++)
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                izleyici.Calistir<int>(() => throw new HttpRequestException("yok")));

        Assert.Equal(1, sayac);                       // üç kopuk istek → TEK olay

        await izleyici.Calistir(() => Task.FromResult(1));
        await izleyici.Calistir(() => Task.FromResult(1));

        Assert.Equal(2, sayac);                       // geri gelince bir olay daha
    }

    /// <summary>Sınıflandırma kuralının kendisi (tek tek).</summary>
    [Theory]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(System.Text.Json.JsonException), false)]
    public void BAG01g_Tasima_Hatasi_Siniflandirmasi(Type tur, bool beklenen)
    {
        var ex = (Exception)Activator.CreateInstance(tur)!;
        Assert.Equal(beklenen, BaglantiIzleyici.TasimaHatasi(ex));
    }

    /// <summary>
    /// ⭐ Arayüz kaynak kilidi: uyarı şeridi ve "Tekrar Dene" MainLayout'ta durmalı, abonelik
    /// <c>Dispose</c>'da çözülmeli (MAS-01 dersi — statik/uzun ömürlü olay birikmesin).
    /// </summary>
    [Fact]
    public void BAG01h_Arayuz_Serit_Ve_Abonelik_Yerinde()
    {
        var src = KaynakOku("src", "DepoWise.Web", "Components", "Layout", "MainLayout.razor");

        Assert.Contains("Api.SunucuyaUlasilamiyor", src, StringComparison.Ordinal);
        Assert.Contains("Sunucuya ulaşılamıyor", src, StringComparison.Ordinal);
        Assert.Contains("TEKRAR DENE", src, StringComparison.Ordinal);
        Assert.Contains("Api.BaglantiDurumuDegisti +=", src, StringComparison.Ordinal);
        Assert.Contains("Api.BaglantiDurumuDegisti -=", src, StringComparison.Ordinal);
    }

    /// <summary>Kaynak kilidi: web istemcisinde TÜM istekler tek geçiş noktasından geçmeli.</summary>
    [Fact]
    public void BAG01i_Tum_Istekler_Tek_Noktadan_Geciyor()
    {
        var src = KaynakOku("src", "DepoWise.Web", "Services", "ApiClient.cs");
        var dogrudan = System.Text.RegularExpressions.Regex.Matches(src, @"_http\.(SendAsync|GetAsync|PostAsync|PutAsync|DeleteAsync)\(").Count;

        // Tek istisna: sarmalayıcının KENDİ içindeki gerçek çağrı.
        Assert.Equal(1, dogrudan);
    }

    private static string KaynakOku(params string[] parcalar)
    {
        var kok = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && kok is not null; i++)
        {
            var aday = Path.Combine(new[] { kok }.Concat(parcalar).ToArray());
            if (File.Exists(aday)) return File.ReadAllText(aday);
            kok = Path.GetDirectoryName(kok!);
        }
        throw new FileNotFoundException("Kaynak bulunamadı: " + string.Join("/", parcalar));
    }
}
