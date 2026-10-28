using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// 2026-10-29 (kullanıcı isteği): Giriş-Çıkış "Son hareketler" sütun filtreleri İKİ ORTAMDA aynı (platform kuralı: web eksik kalmaz).
///  HFP1 — web sayfası aynı uca aynı parametreleri gönderir (kod, malzeme, miktar, aciklama, yon, type, from/to); filtre satırı
///         tablodan bağımsız (sonuç boşken de görünür, "Filtreye uyan hareket yok").
///  HFP2 — API ucu parametreleri servise HareketSutunFiltresi olarak geçirir; masaüstü aynı servisi aynı kayıtla çağırır.
/// </summary>
public class HareketFiltresiPariteTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(params string[] yol) => File.ReadAllText(Path.Combine(new[] { Kok() }.Concat(yol).ToArray()));

    [Fact]
    public void HFP1_Web_Ayni_Filtreler()
    {
        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "Stock.razor");
        Assert.Contains("/api/stock/movements/grid?page={_hrkPage}&pageSize={_hrkPageSize}{HrkFiltreSorgusu()}", web);
        foreach (var p in new[] { "\"type\"", "\"kod\"", "\"malzeme\"", "\"yon\"", "\"miktar\"", "\"aciklama\"", "&from=", "&to=" })
            Assert.Contains(p, web);
        Assert.Contains("Filtreye uyan hareket yok.", web);
        // filtre satırı yükleme/boş durumundan ÖNCE (tablonun içinde değil) → sonuç boşken de temizlenebilir
        Assert.True(web.IndexOf("@bind-Value=\"_hfKod\"", StringComparison.Ordinal) < web.IndexOf("@if (_rows is null) { <MudProgressCircular", StringComparison.Ordinal));
    }

    [Fact]
    public void HFP2_Api_Ve_Masaustu_Ayni_Servis()
    {
        var api = Oku("src", "DepoWise.Api", "Program.cs");
        Assert.Contains("new DepoWise.Infrastructure.Materials.HareketSutunFiltresi(kod, malzeme, miktar, aciklama, yon)", api);
        var vm = Oku("src", "DepoWise.Desktop", "ViewModels", "StockEntryViewModel.cs");
        Assert.Contains("HareketSayfa, HareketSayfaBoyutu, hfSutun);", vm);
        Assert.Contains("new HareketSutunFiltresi(V(HfKod), V(HfMalzeme), V(HfMiktar), V(HfAciklama), yon)", vm);
        var gorunum = Oku("src", "DepoWise.Desktop", "Views", "StockEntryView.axaml");
        foreach (var k in new[] { "HfTip", "HfKod", "HfMalzeme", "HfYon", "HfMiktar", "HfAciklama" })
            Assert.Contains($"DataContext=\"{{Binding {k}}}\"", gorunum);
    }
}
