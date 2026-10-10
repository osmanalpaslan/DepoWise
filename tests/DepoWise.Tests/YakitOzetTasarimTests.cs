using DepoWise.Infrastructure.Operations;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-10 (kullanıcı isteği) — Yakıt Özeti yeni tasarımı: haftalar ORAN ÇUBUĞU, günler TAKVİM KUTUCUĞU.
/// YOZ1: hafta etiketleri sıralı ("1. Hafta"…), en yoğun hafta %100, sıfır olmayan en küçük hafta görünür (≥ %4).
/// YOZ2: gün kutucuğu yoğunluk kademesi 0–3 (en yoğun gün 3), gün no + kısa gün adı Türkçe.
/// YOZ3: Ay adı/yıl Türkçe; boş ay çökmez.
/// </summary>
public class YakitOzetTasarimTests
{
    private static FuelMonthSummary Ay(params (int Gun, decimal L)[] gunler)
    {
        var days = gunler.Select(g => new FuelDaySummary(new DateTime(2026, 9, g.Gun), g.L, 1)).ToList();
        var weeks = new List<FuelWeekSummary>
        {
            new(new DateTime(2026, 9, 1), new DateTime(2026, 9, 7), 200m, 2),
            new(new DateTime(2026, 9, 8), new DateTime(2026, 9, 14), 2m, 1),
            new(new DateTime(2026, 9, 15), new DateTime(2026, 9, 21), 0m, 0),
        };
        return new FuelMonthSummary(2026, 9, days.Sum(d => d.Liters), days.Count, 1m, 1m, weeks, days);
    }

    [Fact]
    public void YOZ1_Hafta_Cubuklari()
    {
        var w = Ay((1, 10m)).WeekBars;
        Assert.Equal(new[] { "1. Hafta", "2. Hafta", "3. Hafta" }, w.Select(x => x.Label));
        Assert.Equal(100d, w[0].Percent);
        Assert.Equal(4d, w[1].Percent);   // 2/200 = %1 → en az %4 (görünür kalır)
        Assert.Equal(0d, w[2].Percent);   // sıfır hafta → çubuk yok
    }

    [Fact]
    public void YOZ2_Gun_Kutucuklari_Yogunluk()
    {
        var t = Ay((1, 100m), (2, 50m), (3, 25m), (4, 5m)).DayTiles;
        Assert.Equal(new[] { 3, 2, 1, 0 }, t.Select(x => x.Level));
        Assert.Equal("01", t[0].DayNumber);
        Assert.Equal("Sal", t[0].Weekday);          // 01.09.2026 Salı
        Assert.True(t[0].IsStrong);
        Assert.False(t[3].IsStrong);
    }

    [Fact]
    public void YOZ3_Ay_Adi_ve_Bos_Ay()
    {
        var bos = new FuelMonthSummary(2026, 10, 0m, 0, 0m, 0m, Array.Empty<FuelWeekSummary>(), Array.Empty<FuelDaySummary>());
        Assert.Equal("Ekim", bos.MonthName);
        Assert.Equal("2026", bos.YearText);
        Assert.Empty(bos.WeekBars);
        Assert.Empty(bos.DayTiles);
    }
}
