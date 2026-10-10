namespace DepoWise.Application.Requests;

/// <summary>PDF'teki bir kalem. <paramref name="Vehicles"/> (2026-10-10): kalemin TÜM araçları — doluysa
/// "Talep Edilen Araç" hücresi bunları alt alta yazar; boşsa eski tek araç alanları kullanılır.</summary>
public sealed record RequestPdfItem(
    string MaterialCode, string MaterialName, string Unit, decimal Quantity,
    string? VehicleCode, string? VehicleChassis, IReadOnlyList<RequestPdfVehicle>? Vehicles = null);

/// <summary>PDF'te bir aracın gösterimi: iç kod + (varsa) şase no.</summary>
public sealed record RequestPdfVehicle(string Code, string? Chassis);

/// <summary>Talep PDF için tenant-bağımsız veri modeli (web ve masaüstü aynı modeli kullanır).</summary>
public sealed record RequestPdfModel(
    string CompanyName,
    string DocNo,
    string RequestDate,
    string Status,
    string? BranchName,
    string? RequesterName,
    string? WarehouseName,
    string? ApproverName,
    string? Description,
    IReadOnlyList<RequestPdfItem> Items,
    string? LogoPath = null);

/// <summary>Belge dışa aktarımı. Türkçe karakterler korunur.</summary>
public interface IRequestPdfService
{
    /// <param name="economic">Ekonomik (sade, gri dolgusuz, toner tasarruflu) çıktı.</param>
    byte[] Generate(RequestPdfModel model, bool economic = false);
}
