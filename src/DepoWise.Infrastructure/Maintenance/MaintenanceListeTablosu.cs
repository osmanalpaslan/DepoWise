using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Maintenance;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): bakım kayıtları listesinin Excel/Yazdır tablosu — ekrandaki kolonlarla aynı (iki ortam aynı).
/// </summary>
public static class MaintenanceListeTablosu
{
    public static TableModel ToTableModel(IReadOnlyList<MaintenanceRow> rows)
        => new("Bakım Kayıtları",
            new[] { "Araç", "Plaka", "Bakım", "Alt Bakım", "Yapıldığı", "Sonraki", "Fatura / Fiş No", "Açıklama", "Durum" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.VehicleCode, r.PlateDisplay, r.DefinitionName, r.SubDisplay, r.PerformedDisplay, r.NextDueDisplay,
                r.InvoiceDisplay, r.Description ?? "", r.StatusText,
            }).ToList());
}
