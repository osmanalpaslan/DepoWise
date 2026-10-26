using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ 2026-10-26 — İKİ ORTAM DENETİMİ KÜÇÜK BULGULARI (kullanıcı istekleri 1 ve 4) ═══
///  DP1 — şube şifresi: web de "hatalı" ile "doğrulanamadı (ulaşılamadı / çok deneme)" mesajlarını ayırır; ikisinde de giriş YOK
///  DP2 — Günlük Faaliyet bakım düzeltme onay metni ve başarı mesajı iki ortamda birebir aynı
///  DP3 — masaüstü Depo Çıkışı "stok" göstergesi çıkışın düştüğü çalışma şubesinin deposu; yeni kayıt eski "düzenlenemez" bandını temizler
/// </summary>
public class DenetimParitesi1026Tests
{
    private static string Oku(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, rel)).Replace("\r\n", "\n");
    }

    private const string Ulasilamadi =
        "Şube şifresi doğrulanamadı (sunucuya ulaşılamadı ya da çok fazla deneme yapıldı). Biraz sonra tekrar deneyin.";

    [Fact]
    public void DP1_Sube_Sifresi_Mesajlari_Iki_Ortamda_Ayni_Ve_Kapali()
    {
        var mas = Oku("src/DepoWise.Desktop/ViewModels/LoginViewModel.cs");
        var web = Oku("src/DepoWise.Web/Components/Pages/Login.razor");
        Assert.Contains("if (ok == false) { Error = \"Şube şifresi hatalı.\"; return; }", mas);
        Assert.Contains("if (ok != true) { Error = \"" + Ulasilamadi + "\"; return; }", mas);
        Assert.Contains("if (ok == false) { _error = \"Şube şifresi hatalı.\"; return; }", web);
        Assert.Contains("if (ok != true) { _error = \"" + Ulasilamadi + "\"; return; }", web);   // null (ulaşılamadı) da girişi durdurur
        var istemci = Oku("src/DepoWise.Web/Services/ApiClient.cs");
        Assert.Contains("public async Task<bool?> VerifyBranchAsync(", istemci);
    }

    [Fact]
    public void DP2_Duzeltme_Onay_Ve_Basari_Metni_Ayni()
    {
        var mas = Oku("src/DepoWise.Desktop/ViewModels/DailyActivityViewModel.cs");
        var web = Oku("src/DepoWise.Web/Components/Pages/Daily.razor");
        foreach (var parca in new[]
                 {
                     "\"Kayıt düzeltilsin mi?\\n\\nEski kaydın malzeme çıkışı stoğa geri alınır, yeni değerlerle yeniden yazılır\" +",
                     " (malzemeler şu depodan düşülecek: {",
                     "\". Araç sayacı geçerli kayıtlardan yeniden hesaplanır. İşlem tek adımda yapılır; bir sorun olursa hiçbir şey değişmez.\";",
                     "\"Kayıt düzeltildi (eski malzeme çıkışı geri alındı, yeni değerler yazıldı).\"",
                 })
        {
            Assert.Contains(parca, mas);
            Assert.Contains(parca, web);
        }
        Assert.Equal(2, web.Split("_duzeltId is not null ? DuzeltmeOnayMetni()").Length - 1);   // bakım + ilave yağ/filtre/tamir
    }

    [Fact]
    public void DP3_Masaustu_Cikis_Stogu_Calisma_Subesi_Ve_Bant_Temizligi()
    {
        var mas = Oku("src/DepoWise.Desktop/ViewModels/DailyActivityViewModel.cs");
        Assert.Contains("DesktopServices.Stock.GetBalanceAt(_session, m.Id, _session.OperatingBranchId)", mas);
        Assert.Contains("branchId: _session.OperatingBranchId", mas);    // çıkış da aynı depodan düşer
        var yeni = mas[mas.IndexOf("private void NewRecord()", StringComparison.Ordinal)..];
        yeni = yeni[..yeni.IndexOf("ShowForm = true;", StringComparison.Ordinal)];
        Assert.Contains("DuzenlemeUyarisi = null;", yeni);
    }
}
