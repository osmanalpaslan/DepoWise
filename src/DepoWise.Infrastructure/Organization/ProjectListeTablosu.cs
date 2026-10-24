using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Organization;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): proje listesinin Excel/Yazdır tablosu — ekrandaki bilgilerle aynı (iki ortam aynı dosya:
/// masaüstü de projeleri sunucudan okuduğu için dışa aktarımı sunucunun aynı ucundan indirir).
/// </summary>
public static class ProjectListeTablosu
{
    private static string Gun(long? ms) => ms is { } v ? DateTimeOffset.FromUnixTimeMilliseconds(v).UtcDateTime.ToString("dd.MM.yyyy") : "";

    public static TableModel ToTableModel(IReadOnlyList<ProjectRow> rows)
        => new("Projeler", new[] { "Proje", "Durum", "Başlangıç", "Bitiş", "Yönetici", "Konum", "Şubeler / Şantiyeler", "Açıklama" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Name, r.StatusDisplay, Gun(r.StartDate), Gun(r.EndDate), r.ManagerName ?? "", r.Location ?? "",
                string.Join(", ", r.BranchNames), r.Description ?? "",
            }).ToList());
}
