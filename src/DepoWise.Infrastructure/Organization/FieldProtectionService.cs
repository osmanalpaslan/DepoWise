using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Organization;

/// <summary>"Alan Koruma" yönetim ekranının bir satırı: korunabilir alan + firmanın seçimi.</summary>
public sealed record FieldProtectionRow(
    string ScreenKey, string ScreenLabel, string FieldKey, string Label, string Note, bool Protected)
{
    /// <summary>Kullanıcıya gösterilecek kısa durum (teknik terim yok).</summary>
    public string StatusText => Protected ? "Korumalı — yalnız yetkisi olan görür" : "Herkese açık (varsayılan)";
}

/// <summary>
/// ═══ ALAN KORUMA SERVİSİ (FAZ 3b, ADR-223 · D2 · 2026-09-05) ═══
///
/// Firma bazında "bu alan korumalı mı" kaydını okur/yazar. <b>Kararı bu servis VERMEZ</b> —
/// kararın tek yeri <see cref="FieldAccess"/>'tir; burası yalnız firmanın seçimini saklar.
///
/// <b>Varsayılan = bugünkü davranış:</b> satır yoksa alan korumasızdır ve herkes görür/düzenler.
/// Tablo boş doğduğu için yayın günü hiçbir kullanıcının ekranı değişmez.
///
/// <b>Yalnız katalogdaki alan korunabilir</b> (<see cref="FieldProtectionCatalog"/>): serviste
/// gerçekten süzülmeyen bir alanı korumalı yapmak, yöneticiye korunduğunu sandırıp aslında hiçbir
/// şey yapmamak olurdu. Bilinmeyen anahtar fail-closed REDDEDİLİR.
///
/// <b>Etki ne zaman görünür:</b> web/API'de anında (<see cref="PermissionSnapshotCache.InvalidateAll"/>).
/// Masaüstünde oturum açılışında okunduğu için <b>bir sonraki girişte</b> — bu, <c>BlockedModules</c>
/// ve şube kapsamının bugünkü davranışıyla aynıdır, yeni bir sınır değildir.
/// </summary>
public sealed class FieldProtectionService
{
    /// <summary>Yönetim ekranının yetki modülü. Alan koruması bir YETKİ ayarıdır; bu yüzden
    /// mevcut yetki yönetimi modülüne bağlanır — yeni bir yetki adası açılmaz.</summary>
    public const string Module = "permissions";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly PermissionSnapshotCache? _snapshots;

    public FieldProtectionService(IDbConnectionFactory factory, IClock? clock = null,
        PermissionSnapshotCache? snapshots = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _snapshots = snapshots;
    }

    /// <summary>
    /// Firmanın korumalı alan kümesi (<c>ekran|alan</c>). Oturum kurulurken AuthService da aynı
    /// veriyi okur; bu metot yönetim ekranı ve testler içindir.
    /// Tablo yoksa/okunamazsa BOŞ döner → koruma yok, bugünkü davranış (fail-safe taraf).
    /// </summary>
    public IReadOnlySet<string> ProtectedKeys(string companyId)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var conn = _factory.Create();
            if (!DbIntrospect.TableExists(conn, null, "field_protections")) return set;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT screen_key, field_key FROM field_protections WHERE company_id=@c;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(FieldAccess.ProtectionKey(r.GetString(0), r.GetString(1)));
        }
        catch { /* okuma hatası ekranı çökertmez → koruma yok kabul edilir (mevcut davranış) */ }
        return set;
    }

    /// <summary>Yönetim ekranının listesi: korunabilir her alan + firmanın etkin seçimi.</summary>
    public IReadOnlyList<FieldProtectionRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var korumalilar = ProtectedKeys(s.CompanyId);
        return FieldProtectionCatalog.All
            .Select(f => new FieldProtectionRow(f.ScreenKey, f.ScreenLabel, f.FieldKey, f.Label, f.Note,
                korumalilar.Contains(FieldAccess.ProtectionKey(f.ScreenKey, f.FieldKey))))
            .ToList();
    }

    /// <summary>
    /// Alanı korumalı yapar / korumayı kaldırır. Yalnız katalogdaki alanlar kabul edilir.
    ///
    /// ⚠️ Koruma açmak <b>kısıtlayıcı</b> bir işlemdir: o andan sonra alanı yalnız
    /// <c>fld_&lt;ekran&gt;_&lt;alan&gt;</c> iznine sahip kullanıcılar (ve adminler) görür.
    /// Bu yüzden yalnız yetki yönetimi yetkisi olan kullanıcı yapabilir ve işlem denetim
    /// kaydına (audit) yazılır.
    /// </summary>
    public void Set(SessionContext s, string screenKey, string fieldKey, bool isProtected)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var def = FieldProtectionCatalog.Find(screenKey, fieldKey)
            ?? throw new ArgumentException($"Bilinmeyen ya da korunamayan alan: {screenKey}/{fieldKey}");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (isProtected) Ekle(conn, tx, s.CompanyId, screenKey, fieldKey, now);
        else Sil(conn, tx, s.CompanyId, screenKey, fieldKey);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "field_protections", screenKey + "/" + fieldKey,
            AuditActions.Update, s.UserId,
            AfterJson: $"{{\"protected\":{(isProtected ? "true" : "false")},\"label\":\"{def.Label}\"}}"), _clock);

        tx.Commit();

        // Koruma FİRMA geneli olduğu için o firmanın herkesini etkiler → tüm fotoğraflar düşürülür.
        // (Rol Yetki Kontrol'ün 2026-08 tarihli deseniyle aynı: kimin etkilendiğini ayrıca sorgulamak
        //  yerine tamamı düşürülür — güvenli taraf, yetki kaybı gecikmez.)
        _snapshots?.InvalidateAll();
    }

    private static void Ekle(DbConnection conn, DbTransaction tx, string companyId,
        string screenKey, string fieldKey, long now)
    {
        // Varsa dokunma (UNIQUE zaten mükerreri engeller) — 065/087 deseni; iki lehçede de çalışır.
        using var chk = conn.CreateCommand();
        chk.Transaction = tx;
        chk.CommandText = "SELECT COUNT(*) FROM field_protections WHERE company_id=@c AND screen_key=@s AND field_key=@f;";
        chk.AddWithValue("@c", companyId); chk.AddWithValue("@s", screenKey); chk.AddWithValue("@f", fieldKey);
        if (Convert.ToInt64(chk.ExecuteScalar() ?? 0L) > 0) return;

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO field_protections(id, company_id, screen_key, field_key, created_at) " +
                          "VALUES(@id,@c,@s,@f,@now);";
        ins.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        ins.AddWithValue("@c", companyId); ins.AddWithValue("@s", screenKey); ins.AddWithValue("@f", fieldKey);
        ins.AddWithValue("@now", now);
        ins.ExecuteNonQuery();
    }

    private static void Sil(DbConnection conn, DbTransaction tx, string companyId,
        string screenKey, string fieldKey)
    {
        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM field_protections WHERE company_id=@c AND screen_key=@s AND field_key=@f;";
        del.AddWithValue("@c", companyId); del.AddWithValue("@s", screenKey); del.AddWithValue("@f", fieldKey);
        del.ExecuteNonQuery();
    }
}
