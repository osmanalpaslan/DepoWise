using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TRF-01 — DEPO→DEPO TRANSFER: WEB ↔ MASAÜSTÜ PARİTE NÖBETİ (2026-09-04) ═══
///
/// FAZ C'nin (depo bazlı stok) kalan son işi. Transfer <b>servis katmanı zaten olgundu</b>
/// (tek transaction · idempotent · çift katmanlı negatif stok koruması · bakiye ortak yazıcıdan),
/// eksik olan <b>arayüz paritesiydi</b>: aynı iş iki ekranda farklı davranıyordu.
///
/// <b>Neden metin üzerinden test:</b> transfer ekranları Avalonia XAML ve Razor'dur; bu ortamda
/// render edilemezler (bkz. <c>MasaustuTasarimPaketiTests</c>). Bu yüzden görüntü değil, iki ekranın
/// <b>sözleşmesi</b> kilitlenir — birinde değişip diğerinde unutulan bir kural burada patlar.
/// Servis davranışı ayrıca 21 dosyada 68 <c>Transfer(</c> senaryosuyla zaten kapsanıyor; burada
/// onlar tekrarlanmaz.
///
///  TRP1 — Maliyet merkezi TRANSFERDE GİZLİ (iki platformda da) — sessizce yutulan girdi kusuru
///  TRP2 — Hedef listesinden KAYNAK depo dışlanır (iki platformda da)
///  TRP3 — Onay metni HEDEFİN ADINI yazar (iki platformda da)
///  TRP4 — Kaydet'teki kaynak==hedef kontrolü KALDIRILMADI (liste kolaylıktır, kural değil)
/// </summary>
public class TransferPariteTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string Web()      => Oku("src", "DepoWise.Web", "Components", "Pages", "Stock.razor");
    private static string MasaVm()   => Oku("src", "DepoWise.Desktop", "ViewModels", "StockEntryViewModel.cs");
    private static string MasaView() => Oku("src", "DepoWise.Desktop", "Views", "StockEntryView.axaml");

    [Fact]
    public void TRP1_Maliyet_Merkezi_Transferde_Gizli()
    {
        // 🔴 BULUNAN KUSUR (2026-09-04): alan işlem türünden bağımsız görünüyordu; kullanıcı
        // transfer yaparken de doldurabiliyordu ama değer HİÇBİR YERE yazılmıyordu ve uyarı da
        // verilmiyordu. Sessizce yutulan bir giriş, hiç olmayan bir alandan daha kötüdür.
        //
        // Depo→depo transfer bir MALİYET OLAYI DEĞİLDİR (malzeme tüketilmez, yer değiştirir);
        // maliyet çıkışta doğar ve orada zaten çalışır. Transferi maliyetlendirmek gerekirse
        // doğru yer yol haritasındaki MUH-04'tür.

        // Web: alan `!IsOutBranch` koşuluna bağlı
        Assert.Contains("!IsOutBranch && Auth.CanEdit(\"cost_centers\")", Web());

        // Masaüstü: görünürlük `!IsOutBranchExit` koşuluna bağlı
        Assert.Contains("!IsOutBranchExit && AccessControl.Can(_session, \"cost_centers\"", MasaVm());
        // ...ve işlem türü değişince görünürlük TAZELENİR (aksi hâlde alan ekranda asılı kalırdı)
        Assert.Contains("[NotifyPropertyChangedFor(nameof(CanPickCostCenter))]", MasaVm());
    }

    [Fact]
    public void TRP2_Hedef_Listesinden_Kaynak_Depo_Dislanir()
    {
        // Hatayı mesajla bildirmek yerine MÜMKÜN KILMAMAK doğrusudur: kullanıcı kendi şubesini
        // seçip Kaydet'e basana kadar hatayı görmüyordu. Web bunu zaten yapıyordu.
        Assert.Contains("_branches.Where(b => b.Id != EffectiveLocation)", Web());

        // Masaüstü artık ayrı bir HEDEF listesi kullanır (tüm şubeler DEĞİL)
        Assert.Contains("ItemsSource=\"{Binding HedefSubeler}\"", MasaView());
        Assert.Contains("b.Id != _session.OperatingBranchId", MasaVm());
        Assert.DoesNotContain("ItemsSource=\"{Binding Branches}\" SelectedItem=\"{Binding ToBranch}\"", MasaView());
    }

    [Fact]
    public void TRP3_Onay_Metni_Hedefin_Adini_Yazar()
    {
        // Transfer GERİ ALINAMAZ (StockService.CanReverse transferi dışlar). Onay ekranında hangi
        // depoya gittiğinin yazmaması bu yüzden ciddi bir eksikti. Masaüstü doğru yapıyordu →
        // parite masaüstü lehine kapatıldı.
        Assert.Contains("{KaynakDepoAdi} → {HedefDepoAdi}", Web());
        Assert.Contains("{LoginBranchName} → {ToBranch.Name}", MasaVm());
    }

    [Fact]
    public void TRP4_Kaynak_Hedef_Ayni_Olamaz_Kurali_Duruyor()
    {
        // Listeden dışlamak bir KOLAYLIKTIR, kuralın kendisi değildir. Kontrol kaldırılırsa
        // (ör. liste ileride değişirse) aynı depoya transfer sızabilir.
        Assert.Contains("EffectiveLocation == _toBranch", Web());
        Assert.Contains("ToBranch.Id == from", MasaVm());
    }
}
