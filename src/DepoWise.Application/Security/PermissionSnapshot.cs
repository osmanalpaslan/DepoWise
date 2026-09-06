namespace DepoWise.Application.Security;

/// <summary>
/// F0 (YET-01, 2026-08-10) — Bir kullanıcının YETKİ FOTOĞRAFI: oturum kurmak için gereken, veritabanından
/// okunan tüm yetki verisi tek değişmez (immutable) nesnede.
///
/// <b>Neden var:</b> <c>AuthService.CreateSessionForUser</c> HER API isteğinde çalışıyor ve istek başına
/// yedi ayrı sorgu açıyordu (kullanıcı, roller, firma, modül izinleri, buton izinleri, tüm-şube bayrağı,
/// rol kısıtları). Sunucu PostgreSQL'e (ağ üzerinden) bağlandığı için bunların her biri bir gidiş-dönüştür.
/// Snapshot bu okumayı bir kez yapıp <see cref="PermissionSnapshotCache"/> üzerinden yeniden kullanır.
///
/// <b>Bu GEÇİCİ bir çözüm DEĞİLDİR</b> — kalıcı yetki önbelleği mimarisidir. Gelecekteki katmanlar
/// (F2 rol izinleri, F4 şube/birim kapsamı, F5 kayıt tipi) aynı nesneye eklenecek şekilde alan ayrıldı;
/// bugün bu alanlar <c>null</c>'dır ve <b>hiçbir yerde okunmaz</b> (F0 davranış değiştirmez).
///
/// <b>Değişmezlik şart:</b> Snapshot birden çok istek arasında paylaşılır. Bu yüzden isteğe özel,
/// DEĞİŞEBİLİR durum (örn. <see cref="SessionContext.OperatingBranchId"/>) burada TUTULMAZ — her istek
/// snapshot'tan kendi <see cref="SessionContext"/>'ini kurar.
/// </summary>
/// <param name="CompanyId">Oturumun ÇÖZÜLMÜŞ firması (süper adminin çapraz-firma oturumu dahil).</param>
/// <param name="UserId">Kullanıcı.</param>
/// <param name="RoleKeys">Kullanıcının rol anahtarları.</param>
/// <param name="Permissions">Modül izinleri + özel buton izinleri (bugün YALNIZ kullanıcı seviyesi).</param>
/// <param name="CanViewAllBranches">Tüm şube verisini görme bayrağı.</param>
/// <param name="BlockedModules">Rol Yetki Kontrol ile kullanıcının ROLÜNE kapatılmış modüller.</param>
/// <param name="ScopeBranchIds">G4-3b ile KULLANIMA ALINDI: user_scopes satırları. null/boş = açık kapsam yok.</param>
/// <param name="HomeBranchId">G4-3b ile eklendi: users.branch_id (kullanıcının ana şubesi).</param>
/// <param name="ProtectedFields">FAZ 3b (ADR-223): firmada korumalı işaretlenmiş alanlar (<c>ekran|alan</c>).
///   <b>BOŞ = bugünkü davranış</b> — hiçbir alan korunmaz. Firma bazlıdır; snapshot ile önbelleklenir.</param>
/// <param name="ScopeUnitIds">F4 (BRM-01) için AYRILMIŞ — bugün daima null, okuyucusu yok.</param>
/// <param name="AllowedRecordTypes">F5 (GNL-03) için AYRILMIŞ — bugün daima null, okuyucusu yok.</param>
public sealed record PermissionSnapshot(
    string CompanyId,
    string UserId,
    IReadOnlyList<string> RoleKeys,
    PermissionSet Permissions,
    bool CanViewAllBranches,
    IReadOnlySet<string> BlockedModules,
    IReadOnlyList<string>? ScopeBranchIds = null,
    string? HomeBranchId = null,
    IReadOnlyList<string>? ScopeUnitIds = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? AllowedRecordTypes = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? BranchDescendants = null,
    IReadOnlySet<string>? ProtectedFields = null)
{
    /// <summary>Bu fotoğraftan İSTEĞE ÖZEL yeni bir oturum kurar. Her çağrıda YENİ nesne döner:
    /// <see cref="SessionContext.OperatingBranchId"/> isteğe göre değiştiği için paylaşılamaz.</summary>
    public SessionContext ToSession() =>
        new(UserId, CompanyId, RoleKeys, Permissions, CanViewAllBranches)
        {
            BlockedModules = BlockedModules,
            ScopeBranchIds = ScopeBranchIds,
            HomeBranchId = HomeBranchId,
            BranchDescendants = BranchDescendants,   // ŞB-04: şube ağacı (üst şube → alt şubeleri)
            // FAZ 3b: null gelirse BOŞ küme → korumasız (bugünkü) davranış. Fail-safe taraf budur.
            ProtectedFields = ProtectedFields ?? new HashSet<string>(StringComparer.Ordinal),
        };
}
