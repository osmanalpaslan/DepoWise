namespace DepoWise.Application.Teams;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — EKİP ═══
///
/// Ekip bir <b>organizasyonel gruplamadır</b>. ADR-187 §3/§5 gereği ekip, onay zincirinin kaynağı
/// DEĞİLDİR: zincir <b>kullanıcı hiyerarşisinden</b> çözülür ve <b>ekip lideri otomatik onaycı
/// değildir</b>. Bu yüzden bu modelde onay/approver kavramı bilinçli olarak YOKTUR.
///
/// Kapsam FİRMA bazlıdır (İK-8) — <c>branch_id</c> taşımaz, <c>BranchAccess</c> genişletilmez.
/// </summary>
/// <param name="LeadUserId">Ekip yöneticisi. Atanmışsa o ekipte AKTİF ÜYE olmak zorundadır
/// (serviste doğrulanır). İK-6: üye ekler/çıkarır — ama onay yetkisi ekipten değil, kendisine
/// düşen onay adımından gelir.</param>
public sealed record Team(
    string Id,
    string CompanyId,
    string Name,
    string? LeadUserId,
    bool IsActive,
    long CreatedAt,
    long UpdatedAt);

/// <summary>Ekip üyeliği. İK-1: bir kullanıcı BİRDEN FAZLA ekipte olabilir (çoka-çok);
/// ancak AYNI ekibe aktif olarak iki kez eklenemez.</summary>
public sealed record TeamMember(
    string Id,
    string CompanyId,
    string TeamId,
    string UserId,
    bool IsLead,
    long CreatedAt,
    long UpdatedAt);

/// <summary>Ekip adı doğrulaması. İstisna ATMAZ — sonuç döner (istisna üzerinden kapı atlatılamaz).</summary>
public static class TeamRules
{
    public const int MaxNameLength = 100;

    public static (bool Ok, string? Error) ValidateName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return (false, "Ekip adı zorunludur.");
        if (n.Length > MaxNameLength) return (false, $"Ekip adı en fazla {MaxNameLength} karakter olabilir.");
        return (true, null);
    }
}
