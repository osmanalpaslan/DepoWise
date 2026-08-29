using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 3 / ADR-184 — TAKVİM TARİHİ KAYMASI ═══ (2026-08-29)
///
/// <b>Kapatılan hata.</b> Kullanıcının seçtiği takvim/iş günü, yerel saat dilimi (TR = UTC+3) uygulayan
/// bir dönüşümle unix ms'e çevrildiğinde <c>2 Ağustos 00:00</c> → <c>1 Ağustos 21:00 UTC</c> oluyor ve
/// kayıt tarih filtreli her raporda <b>BİR GÜN ERKEN</b> görünüyordu. ARA İŞ 3'te masaüstünde 19,
/// web'de 1 yazım noktasında kanıtlandı ve hepsi tek kaynağa (<see cref="IsGunuTarihi"/>) bağlandı.
///
/// <b>Bu sınıf neyi kilitler.</b> (1) Kuralın kendisini: seçilen gün, makinenin saat diliminden
/// BAĞIMSIZ olarak UTC gün başına yazılır. (2) Eski hatalı dönüşümün gerçekten bir gün erkene
/// düştüğünü (hatanın tanımı — regresyon nöbetçisi). (3) Rapor OKUMA sınırlarının aynı kaynağı
/// kullandığını (yazma ↔ okuma tutarlılığı). (4) Kaynak düzeyinde ham dönüşümün geri gelemeyeceğini.
/// </summary>
public class TarihKaymasiTests
{
    private const long Gun = 86_400_000L;

    private static long UtcMs(int yil, int ay, int gun)
        => new DateTimeOffset(new DateTime(yil, ay, gun, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    // ══════════════ 1) KURALIN KENDİSİ ══════════════

    /// <summary>⭐ Seçilen gün, makinenin saat diliminden BAĞIMSIZ olarak aynı UTC gününe yazılır.
    /// TR (+3) dahil uç ofsetlerde bile "2 Ağustos seç → 2 Ağustos kalsın" kuralı bozulmaz.</summary>
    [Theory]
    [InlineData(3)]      // TR — kullanıcının makinesi
    [InlineData(0)]      // UTC
    [InlineData(-5)]     // batı yarım küre
    [InlineData(-11)]    // uç batı
    [InlineData(13)]     // uç doğu
    [InlineData(14)]     // en uç doğu (Kiritimati)
    public void TAR1_SecilenGun_SaatDiliminden_Bagimsiz(int ofsetSaat)
    {
        var secim = new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.FromHours(ofsetSaat));
        Assert.Equal(UtcMs(2026, 8, 2), IsGunuTarihi.Ms(secim));
    }

    /// <summary>DateTime aşırı yüklemesi (web'in kullandığı tip) da aynı sonucu verir.</summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void TAR2_DateTime_Kind_Fark_Etmez(DateTimeKind kind)
        => Assert.Equal(UtcMs(2026, 8, 2), IsGunuTarihi.Ms(DateTime.SpecifyKind(new DateTime(2026, 8, 2), kind)));

    /// <summary>Saat bileşeni TAŞINMAZ: gün içinde hangi saat seçilirse seçilsin gün başına yazılır
    /// (00:00–03:00 aralığı dahil — eski hatada bu saatlerde "bugün" bile geriye düşüyordu).</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 30)]
    [InlineData(2, 59)]
    [InlineData(3, 0)]
    [InlineData(12, 0)]
    [InlineData(23, 59)]
    public void TAR3_Gun_Ici_Saat_Kaymaya_Yol_Acmaz(int saat, int dakika)
    {
        var anTr = new DateTimeOffset(new DateTime(2026, 8, 2, saat, dakika, 0), TimeSpan.FromHours(3));
        Assert.Equal(UtcMs(2026, 8, 2), IsGunuTarihi.Ms(anTr));
    }

    /// <summary>Ay/yıl sınırları: ayın ilk/son günü ve yılın ilk/son günü kaymaz.</summary>
    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]   // artık yıl
    [InlineData(2026, 8, 1)]
    [InlineData(2026, 8, 31)]
    [InlineData(2026, 12, 31)]
    public void TAR4_Ay_ve_Yil_Sinirlari(int y, int a, int g)
    {
        var secimTr = new DateTimeOffset(new DateTime(y, a, g), TimeSpan.FromHours(3));
        Assert.Equal(UtcMs(y, a, g), IsGunuTarihi.Ms(secimTr));
    }

    /// <summary>Gün SONU sınırı: 23:59:59.999 — bitiş sınırı kapsayıcıdır ve bir sonraki güne taşmaz.</summary>
    [Fact]
    public void TAR5_GunSonu_Sinirlari()
    {
        var gunSonu = IsGunuTarihi.GunSonuMs(new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.FromHours(3)));
        Assert.Equal(UtcMs(2026, 8, 2) + Gun - 1, gunSonu);
        Assert.True(gunSonu < UtcMs(2026, 8, 3));
    }

    [Fact]
    public void TAR6_Null_Null_Doner()
    {
        Assert.Null(IsGunuTarihi.Ms((DateTimeOffset?)null));
        Assert.Null(IsGunuTarihi.Ms((DateTime?)null));
        Assert.Null(IsGunuTarihi.GunSonuMs((DateTimeOffset?)null));
    }

    // ══════════════ 2) HATANIN TANIMI (regresyon nöbetçisi) ══════════════

    /// <summary>⭐ ESKİ hatalı dönüşümün BİR GÜN ERKENE düştüğünü belgeler. Bu test, düzeltmenin
    /// neyi çözdüğünü kanıtlar; hata geri gelirse yeni kural ile eski kural eşitlenir ve test düşer.</summary>
    [Fact]
    public void TAR7_Eski_Ham_Donusum_Bir_Gun_Erkene_Dusuyordu()
    {
        var secimTr = new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.FromHours(3));
        var eski = secimTr.ToUnixTimeMilliseconds();          // ESKİ (hatalı) yol
        var yeni = IsGunuTarihi.Ms(secimTr)!.Value;           // YENİ (doğru) yol

        Assert.True(eski < yeni, "Ham dönüşüm daha erken bir ana düşmeliydi (hatanın tanımı).");
        Assert.InRange(eski, UtcMs(2026, 8, 1), UtcMs(2026, 8, 2) - 1);   // 1 Ağustos penceresi
        Assert.Equal(UtcMs(2026, 8, 2), yeni);                            // 2 Ağustos'ta kalır
        Assert.Equal(3 * 3_600_000L, yeni - eski);                        // tam ofset kadar fark
    }

    // ══════════════ 3) YAZMA ↔ OKUMA TUTARLILIĞI ══════════════

    /// <summary>⭐ Rapor gün sınırları (RPR-06) artık AYNI kaynaktan gelir: yazılan gün ile süzülen gün
    /// aynı tanımı paylaşır → "kaydettiğim gün raporda o gün" güvencesi yapısal hâle gelir.</summary>
    [Fact]
    public void TAR8_Rapor_Sinirlari_Ayni_Kaynaktan()
    {
        var secim = new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.FromHours(3));
        Assert.Equal(IsGunuTarihi.Ms(secim), ReportDateRange.StartMs(secim));
        Assert.Equal(IsGunuTarihi.GunSonuMs(secim), ReportDateRange.EndMs(secim));

        // Yazılan değer, o günün rapor aralığının İÇİNDEDİR (uçlar dahil).
        var yazilan = IsGunuTarihi.Ms(secim)!.Value;
        Assert.InRange(yazilan, ReportDateRange.StartMs(secim)!.Value, ReportDateRange.EndMs(secim)!.Value);
    }

    // ══════════════ 4) KAYNAK-DÜZEYİ KİLİTLER ══════════════

    /// <summary>⭐ Düzeltilen 19 masaüstü noktası ham dönüşüme GERİ DÖNEMEZ ve hepsi tek kaynağı kullanır.
    /// (ARA İŞ 3 kapsamındaki ekranlar — ADR-184 / PK-TAR-01=A.)</summary>
    [Theory]
    [InlineData("StockEntryViewModel.cs", 3)]
    [InlineData("StockCountViewModel.cs", 1)]
    [InlineData("StockDistributeViewModel.cs", 1)]
    [InlineData("InvoicesViewModel.cs", 2)]
    [InlineData("FinanceViewModel.cs", 2)]
    [InlineData("InspectionViewModel.cs", 2)]
    [InlineData("MaintenanceViewModel.cs", 1)]
    [InlineData("DailyActivityViewModel.cs", 3)]
    [InlineData("PartiesViewModel.cs", 2)]
    [InlineData("PaymentsViewModel.cs", 1)]
    [InlineData("RequestsViewModel.cs", 1)]
    public void TAR9_Masaustu_Ekranlari_Tek_Kaynagi_Kullanir(string dosya, int beklenenCagri)
    {
        var metin = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ViewModels", dosya));

        // Ham dönüşüm KALMADI (bu ekranların tarih alanlarında).
        Assert.DoesNotContain("?.ToUnixTimeMilliseconds()", metin, StringComparison.Ordinal);

        // Tek kaynağa bağlı ve beklenen sayıda yazım noktası var.
        var sayi = System.Text.RegularExpressions.Regex.Matches(metin, @"IsGunuTarihi\.Ms\(").Count;
        Assert.True(sayi >= beklenenCagri,
            $"{dosya}: en az {beklenenCagri} IsGunuTarihi.Ms çağrısı beklenirdi, {sayi} bulundu.");
    }

    /// <summary>⭐ WEB: `Stock.razor` artık web'in tek doğru kaynağını (FieldChecks.ToUnixMs) kullanır;
    /// yerel ofset uygulayan ham dönüşüm geri gelemez. (S1d yalnız masaüstünü taramıştı; bu nokta
    /// ARA İŞ 3'te bulundu.)</summary>
    [Fact]
    public void TAR10_Web_Stock_Razor_Tek_Kaynagi_Kullanir()
    {
        var metin = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Web", "Components", "Pages", "Stock.razor"));
        Assert.Contains("FieldChecks.ToUnixMs(_docDate)", metin, StringComparison.Ordinal);
        Assert.DoesNotContain("new DateTimeOffset(_docDate.Value.Date).ToUnixTimeMilliseconds()", metin, StringComparison.Ordinal);
    }

    /// <summary>⭐ WEB REGRESYONU: doğru çalışan 10 tarih noktasına DOKUNULMADI (PK-TAR-01=A sınırı).
    /// Hepsi hâlâ web'in tek kaynağını kullanıyor.</summary>
    [Theory]
    [InlineData("Inspection.razor")]
    [InlineData("Daily.razor")]
    [InlineData("Invoices.razor")]
    [InlineData("Parties.razor")]
    [InlineData("Maintenance.razor")]
    [InlineData("Finance.razor")]
    [InlineData("Payments.razor")]
    [InlineData("Requests.razor")]
    public void TAR11_Web_Dogru_Noktalar_Korundu(string sayfa)
    {
        var metin = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Web", "Components", "Pages", sayfa));
        Assert.Contains("FieldChecks.ToUnixMs", metin, StringComparison.Ordinal);
    }

    /// <summary>Web'in StockCount/StockDistribute sayfaları zaten doğruydu ve satır-içi UTC deseniyle kaldı.</summary>
    [Theory]
    [InlineData("StockCount.razor")]
    [InlineData("StockDistribute.razor")]
    public void TAR12_Web_Stok_Sayfalari_UTC_Deseniyle_Kaldi(string sayfa)
    {
        var metin = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Web", "Components", "Pages", sayfa));
        Assert.Contains("DateTimeKind.Utc", metin, StringComparison.Ordinal);
    }

    /// <summary>⭐ PK-TAR-04: gerçek zaman damgalarına DOKUNULMADI — <c>DateEntryPolicy</c>'nin iş günü ↔
    /// kayıt anı ayrımı ve <c>btn-backdate</c> kapısı aynen duruyor.</summary>
    [Fact]
    public void TAR13_Zaman_Damgasi_Ayrimi_Korundu()
    {
        var metin = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Application", "Security", "DateEntryPolicy.cs"));
        Assert.Contains("SpecialButtons.BackDate", metin, StringComparison.Ordinal);
        Assert.Contains("created_at", metin, StringComparison.Ordinal);
    }

    /// <summary>⭐ PK-TAR-03: kural TEK gövdededir — rapor sınıfı kendi kopyasını tutmaz, ortak kaynağa
    /// yönlendirir. Bu kilit düşerse kural yeniden ikiye bölünmüş demektir.</summary>
    [Fact]
    public void TAR14_Kural_Tek_Govdede()
    {
        var rapor = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Application", "Reports", "ReportDateRange.cs"));
        Assert.Contains("IsGunuTarihi", rapor, StringComparison.Ordinal);
        Assert.DoesNotContain("AddMilliseconds(-1)", rapor, StringComparison.Ordinal);   // hesap gövdesi burada DEĞİL
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }
}
