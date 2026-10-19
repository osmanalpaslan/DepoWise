using System.Data.Common;
using DepoWise.Application.Approvals;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Approvals;

/// <summary>⭐ M-33: onay ekranında belge kalemi önizlemesi (ekran değiştirmeden karar).</summary>
public sealed record OnayKalemi(string MaterialName, decimal Quantity, decimal? UnitPrice, string? Currency)
{
    public decimal? Tutar => UnitPrice is { } p ? Math.Round(p * Quantity, 2) : null;
    public string Satir => $"{MaterialName} · {Quantity:0.####}" + (Tutar is { } t ? $" × {UnitPrice:0.##} = {t:N2} {Currency ?? "TRY"}" : "");
}

/// <summary>⭐ M-33: toplu onay/ret sonucunun bir satırı (kısmi başarı açıkça raporlanır; sessiz atlama yok).</summary>
public sealed record TopluOnaySonucu(string StepId, bool Basarili, string? Hata);

/// <summary>
/// ═══ M-33 (AR-GE 2026-10-18, 05 F-115 / F-092) — TEK ONAY MERKEZİ: KALEM ÖNİZLEME + TOPLU KARAR ═══
/// Onaylayan kişi "Onaylamalarım" ekranında belgenin kalemlerini ve (varsa) tutarını görür, birden çok adımı tek seferde
/// onaylar ya da tek gerekçeyle reddeder. Her adım KENDİ işleminde ve mevcut beş kapıdan geçer (<see cref="Approve"/> /
/// <see cref="Reject"/>) → toplu karar yeni bir yetki yolu AÇMAZ; biri başarısız olsa diğerleri etkilenmez ve sonuç
/// satır satır döner.
/// </summary>
public sealed partial class ApprovalService
{
    /// <summary>Kullanıcının bekleyen adımına ait belgenin kalemleri. Adım bu kullanıcıya atanmış olmalı (veri sızıntısı yok).</summary>
    public IReadOnlyList<OnayKalemi> KalemOnizleme(SessionContext s, string stepId)
    {
        using var conn = _factory.Create();
        string instanceId, approver;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT instance_id, approver_user_id FROM approval_step WHERE id=@i AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@i", stepId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Onay adımı bulunamadı veya başka firmaya ait.");
            instanceId = r.GetString(0);
            approver = r.IsDBNull(1) ? "" : r.GetString(1);
        }
        if (!string.Equals(approver, s.UserId, StringComparison.Ordinal)) throw new ForbiddenException("Bu onay adımı size atanmamış.");
        var inst = Instance(conn, null, s.CompanyId, instanceId) ?? throw new ForbiddenException("Onay süreci bulunamadı.");
        var list = new List<OnayKalemi>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = inst.EntityType == ApprovalEntityTypes.MaterialRequest
                ? "SELECT m.name, i.quantity, m.unit_price, NULL FROM material_request_items i " +
                  "JOIN materials m ON m.id=i.material_id AND m.company_id=@c WHERE i.request_id=@e ORDER BY m.name;"
                : "SELECT m.name, l.quantity, l.unit_price, l.currency_code FROM purchase_order_lines l " +
                  "JOIN materials m ON m.id=l.material_id AND m.company_id=@c WHERE l.order_id=@e AND l.company_id=@c AND l.is_deleted=0 ORDER BY m.name;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@e", inst.EntityId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                decimal? fiyat = r.IsDBNull(2) || r.GetString(2).Length == 0 ? null : Money.Parse(r.GetString(2));
                if (fiyat == 0m) fiyat = null;   // fiyatsız malzeme kartı → tutar gösterilmez
                list.Add(new OnayKalemi(r.GetString(0), Money.Parse(r.GetString(1)), fiyat, r.IsDBNull(3) ? null : r.GetString(3)));
            }
        }
        // Fiyatı göremeyen kullanıcıya tutar gösterilmez (alan koruması — FAZ 3c ile aynı kural).
        return DepoWise.Infrastructure.Materials.MaterialService.FiyatGorunur(s)
            ? list : list.Select(k => k with { UnitPrice = null }).ToList();
    }

    /// <summary>Seçilen adımları TOPLU onaylar (her biri ayrı işlem, mevcut kapılar). Sonuç satır satır.</summary>
    public IReadOnlyList<TopluOnaySonucu> TopluOnayla(SessionContext s, IReadOnlyList<string> stepIds)
        => Toplu(stepIds, id => Approve(s, id));

    /// <summary>Seçilen adımları TEK gerekçeyle reddeder (gerekçe zorunlu). Sonuç satır satır.</summary>
    public IReadOnlyList<TopluOnaySonucu> TopluReddet(SessionContext s, IReadOnlyList<string> stepIds, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Ret gerekçesi zorunlu.");
        return Toplu(stepIds, id => Reject(s, id, reason));
    }

    private static IReadOnlyList<TopluOnaySonucu> Toplu(IReadOnlyList<string> stepIds, Action<string> islem)
    {
        if (stepIds.Count == 0) throw new ArgumentException("En az bir onay adımı seçin.");
        if (stepIds.Count > 200) throw new ArgumentException("Tek seferde en fazla 200 onay adımı işlenebilir.");
        var sonuc = new List<TopluOnaySonucu>();
        foreach (var id in stepIds.Distinct(StringComparer.Ordinal))
        {
            try { islem(id); sonuc.Add(new TopluOnaySonucu(id, true, null)); }
            catch (Exception ex) when (ex is ForbiddenException or InvalidOperationException or ArgumentException)
            { sonuc.Add(new TopluOnaySonucu(id, false, ex.Message)); }
        }
        return sonuc;
    }
}
