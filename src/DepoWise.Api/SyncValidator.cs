using System.Text.Json;
using DepoWise.Application.Sync;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Api;

/// <summary>
/// Kritik senkron işlemleri için SUNUCU-OTORİTELİ doğrulayıcı (LWW yok). İstemciye güvenmez:
/// - payload geçerli JSON nesnesi mi,
/// - tenant ihlali yok mu (payload'daki firma = cihazın firması),
/// - id tutarlı mı (payload id = op.EntityId),
/// - referans var mı (stok→malzeme, bakım/sayaç/yakıt→araç), onay durumu geçerli mi.
/// Alan adları hem snake_case hem camelCase tolere edilir; şema kesinleşince kural derinleştirilir.
/// </summary>
public sealed class SyncValidator
{
    private readonly IDbConnectionFactory _factory;
    public SyncValidator(IDbConnectionFactory factory) => _factory = factory;

    public (bool Ok, string? Reason) Validate(string companyId, SyncOperation op)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(op.PayloadJson) ? "{}" : op.PayloadJson);
            root = doc.RootElement.Clone();
        }
        catch { return (false, "Geçersiz payload (JSON değil)."); }
        if (root.ValueKind != JsonValueKind.Object) return (false, "Geçersiz payload (nesne değil).");

        // Tenant ihlali: payload firma alanı cihazın firmasıyla eşleşmeli
        var payloadCompany = Str(root, "company_id", "companyId");
        if (payloadCompany is not null && !string.Equals(payloadCompany, companyId, StringComparison.Ordinal))
            return (false, "Tenant ihlali: farklı firma.");

        // id tutarlılığı
        var id = Str(root, "id");
        if (id is not null && !string.Equals(id, op.EntityId, StringComparison.Ordinal))
            return (false, "Kayıt id'si op.EntityId ile tutarsız.");

        switch (op.EntityType)
        {
            case "stock_movement":
                var matId = Str(root, "material_id", "materialId");
                if (matId is not null && !Exists("materials", matId, companyId))
                    return (false, "Malzeme bulunamadı (referans).");
                break;
            case "vehicle_maintenance":
            case "vehicle_meter":
            case "fuel_distribution":
                var vehId = Str(root, "vehicle_id", "vehicleId");
                if (vehId is not null && !Exists("vehicles", vehId, companyId))
                    return (false, "Araç bulunamadı (referans).");
                break;
            case "material_request_approval":
                var status = Str(root, "status", "approval_status", "approvalStatus");
                if (status is not null && status is not ("approved" or "rejected" or "pending"))
                    return (false, "Geçersiz onay durumu.");
                break;
        }
        return (true, null);
    }

    private static string? Str(JsonElement o, params string[] names)
    {
        foreach (var n in names)
            if (o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private bool Exists(string table, string id, string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM {table} WHERE id=@id AND company_id=@c AND is_deleted=0 LIMIT 1;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() is not null;
    }
}
