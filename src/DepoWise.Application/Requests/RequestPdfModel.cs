namespace DepoWise.Application.Requests;

public sealed record RequestPdfItem(string MaterialCode, string MaterialName, decimal Quantity, string? VehicleLabel);

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
    IReadOnlyList<RequestPdfItem> Items);

/// <summary>Belge dışa aktarımı. Türkçe karakterler korunur.</summary>
public interface IRequestPdfService
{
    byte[] Generate(RequestPdfModel model);
}
