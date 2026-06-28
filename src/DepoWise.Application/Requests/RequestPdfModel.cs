namespace DepoWise.Application.Requests;

public sealed record RequestPdfItem(
    string MaterialCode, string MaterialName, string Unit, decimal Quantity,
    string? VehicleCode, string? VehicleChassis);

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
