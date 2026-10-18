using System.Runtime.CompilerServices;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ AR-GE 2026-10-18 (B3, test süresi) — İŞ PARÇACIĞI HAVUZU ALT SINIRI (yalnız test süreci).
///
/// <b>Belirti:</b> tam koşu ~1 sa → ~2 sa'e çıktı; <c>PaketSaklamaPolitikasiTests.PKT3</c> tek başına saniyeler sürerken tam koşuda
/// ~20 dk sürdü. <b>Neden (olası):</b> paralel test sınıflarının bir kısmı eşzamansız işi bloklayarak bekler; havuzdaki iş
/// parçacıkları dolunca <c>await</c> devamları kuyrukta bekler ve .NET havuzu yeni iş parçacığını yavaş ekler (aç kalma).
/// <b>Çözüm:</b> havuzun ALT sınırı yükseltilir — yalnız ne kadar iş parçacığının BEKLEMEDEN açılabileceğini belirler; hiçbir
/// testin doğrulamasını, sırasını ya da paralellik ayarını değiştirmez. Etki tam koşu süresiyle ölçülür (docs/arge/10).
/// </summary>
internal static class TestIsParcacigiHavuzu
{
    [ModuleInitializer]
    internal static void Ayarla()
    {
        ThreadPool.GetMinThreads(out var isci, out var gc);
        var hedef = Math.Max(isci, Math.Max(64, Environment.ProcessorCount * 8));
        ThreadPool.SetMinThreads(hedef, Math.Max(gc, hedef));
    }
}
