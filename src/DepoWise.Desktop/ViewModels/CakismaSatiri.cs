using System.Collections.Generic;
using System.Windows.Input;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ⭐ FAZ 4.4 (kullanıcı isteği 2026-09-06) — SENKRON ÇAKIŞMA EKRANI SATIRI.
///
/// Sunucudan gelen çakışma kaydının gösterim modeli: <b>kim kazandı, kim kaybetti</b>, iki sürüm
/// arasındaki alan farkları ve çözüm komutları. Karar (kazanan/kaybeden, fark listesi) SUNUCUDA
/// üretilir; burada yalnız gösterilir — iki tarafta ayrı hesap yapılsaydı ekranlar çelişirdi.
/// </summary>
public sealed class CakismaSatiri
{
    public string Id { get; init; } = "";
    public string Baslik { get; init; } = "";
    public string Tarih { get; init; } = "";
    public string KazananMetni { get; init; } = "";
    public string KaybedenMetni { get; init; } = "";
    public List<string> Farklar { get; init; } = new();
    public bool FarkVar => Farklar.Count > 0;
    public string Not { get; init; } = "";
    public bool NotVar => Not.Length > 0;

    /// <summary>Kaybeden sürüm kayıtlı VE kullanıcının çözme yetkisi var mı.</summary>
    public bool CozebilirMi { get; init; }

    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public string? AuditEntityType { get; init; }
    public bool GecmisVar => !string.IsNullOrWhiteSpace(AuditEntityType) && !string.IsNullOrWhiteSpace(EntityId);

    public ICommand? KazananYapCommand { get; init; }
    public ICommand? GizleCommand { get; init; }
    public ICommand? GecmisCommand { get; init; }
}
