using System.Globalization;

namespace DepoWise.Application.Common;

/// <summary>
/// Yakıt dağıtımında SAYAÇ FARKI ve ORTALAMA TÜKETİM (kullanıcı isteği 2026-09-30). Yakıt formu, yakıt
/// listesi (masaüstü + web) bu tek kaynağı kullanır.
///
/// Tüketim formülü Yakıt Tüketim raporuyla AYNIDIR (<c>ReportService.FuelConsumption</c>: litre / sayaç
/// mesafesi, birim L/km ya da L/Saat) — liste ile rapor farklı sayı göstermesin diye ikinci bir tanım
/// üretilmedi. Satır bazında: bu dağıtımın litresi / (güncel sayaç − önceki sayaç).
/// </summary>
public static class FuelMath
{
    private static readonly CultureInfo Tr = new("tr-TR");

    /// <summary>Güncel − önceki. Güncel sayaç girilmemişse null.</summary>
    public static decimal? MeterDiff(decimal prevMeter, decimal? currentMeter)
        => currentMeter is { } c ? c - prevMeter : null;

    /// <summary>Litre / sayaç farkı. Fark sıfır ya da eksiyse tüketim tanımsızdır → null.</summary>
    public static decimal? Consumption(decimal liters, decimal? meterDiff)
        => meterDiff is > 0 && liters > 0 ? liters / meterDiff.Value : null;

    /// <summary>Sayaç farkı metni ("125 km" / "8,5 saat"); tanımsızsa tire.</summary>
    public static string DiffText(decimal? meterDiff, string? meterUnit)
        => meterDiff is { } d ? $"{d.ToString("#,##0.##", Tr)} {Ui.MeterUnitOptions.Label(meterUnit)}" : "—";

    /// <summary>Tüketim metni — raporla aynı biçim ("0,35 L/km" / "4,20 L/Saat"); tanımsızsa tire.</summary>
    public static string ConsumptionText(decimal liters, decimal? meterDiff, string? meterUnit)
        => Consumption(liters, meterDiff) is { } c
            ? c.ToString("#,##0.00", Tr) + (meterUnit == Ui.MeterUnitOptions.Hour ? " L/Saat" : " L/km")
            : "—";
}
