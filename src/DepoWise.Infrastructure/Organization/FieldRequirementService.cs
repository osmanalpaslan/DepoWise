using System.Collections.Concurrent;
using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Organization;

/// <summary>"Alan Ayarları" ekranının bir satırı: alan + sistem kilidi + firmanın seçimi.</summary>
public sealed record FieldRequirementRow(
    string ScreenKey, string ScreenLabel, string FieldKey, string Label,
    bool SystemRequired, bool Required)
{
    /// <summary>Kullanıcıya gösterilecek kısa durum (teknik terim yok).</summary>
    public string StatusText => SystemRequired ? "Sistem zorunlusu (değiştirilemez)"
        : Required ? "Zorunlu (firma ayarı)" : "Opsiyonel";
}

/// <summary>
/// ═══ ALAN ZORUNLULUĞU SERVİSİ (kullanıcı isteği 2026-09-03) ═══
///
/// Firma bazında "bu form alanı zorunlu mu" kaydını okur/yazar. Kayıt YOKSA katalog varsayılanı
/// geçerlidir (<see cref="FieldCatalog"/>: sistem zorunluları hariç her alan OPSİYONEL) → migration
/// sonrası hiçbir formun davranışı değişmez.
///
/// <b>Yalnız SIKILAŞTIRIR:</b> sistem zorunlusu (<c>SystemRequired</c>) buradan GEVŞETİLEMEZ; firma
/// yalnız opsiyonel alanları zorunlu yapabilir/geri alabilir. <b>Firma bazlıdır:</b> A firmasının
/// ayarı B'yi etkilemez. Desen <see cref="ScreenVisibilityService"/> ile BİREBİR aynıdır (önbellek +
/// yazmada anında düşürme + tablo-yoksa-varsayılan); yeni mimari icat edilmedi.
/// </summary>
public sealed class FieldRequirementService
{
    /// <summary>Yönetim ekranının yetki modülü (yetki ağacına eklendi — kalıcı kural).</summary>
    public const string Module = "field_settings";

    public const int CacheTtlSeconds = 60;

    private sealed record Entry(IReadOnlySet<string> Required, DateTimeOffset Expires);

    /// <summary>company → o firmada ZORUNLU işaretlenmiş "screen/field" anahtarları.</summary>
    private static readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public FieldRequirementService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public static void Invalidate(string companyId) => _cache.TryRemove(companyId, out _);
    public static void InvalidateAll() => _cache.Clear();

    private static string Anahtar(string screenKey, string fieldKey) => screenKey + "/" + fieldKey;

    /// <summary>
    /// Formlar için: bu ekranda FİRMANIN zorunlu yaptığı alan anahtarları. Yetki GEREKTİRMEZ —
    /// her kayıt formunda çağrılır ve bilgi yetki taşımaz. Sistem zorunluları LİSTEYE GİRMEZ
    /// (onları formların mevcut doğrulaması zaten uygular; ikinci kaynak üretilmez).
    /// Tablo yoksa (eski şema) veya okuma hata verirse BOŞ döner → mevcut davranış aynen sürer.
    /// </summary>
    public IReadOnlySet<string> RequiredFieldsFor(string companyId, string screenKey)
    {
        var tumu = TumZorunlular(companyId);
        var sonuc = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in tumu)
            if (k.StartsWith(screenKey + "/", StringComparison.Ordinal))
                sonuc.Add(k[(screenKey.Length + 1)..]);
        return sonuc;
    }

    private IReadOnlySet<string> TumZorunlular(string companyId)
    {
        if (_cache.TryGetValue(companyId, out var hit) && hit.Expires > _clock.UtcNow) return hit.Required;

        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var conn = _factory.Create();
            if (!DbIntrospect.TableExists(conn, null, "field_requirements")) return Store(companyId, set);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT screen_key, field_key, required FROM field_requirements WHERE company_id=@c;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (Convert.ToInt64(r.GetValue(2)) == 1)
                    set.Add(Anahtar(r.GetString(0), r.GetString(1)));
        }
        catch { /* okuma hatası formu çökertmez → katalog varsayılanı (opsiyonel) geçerli kalır */ }
        return Store(companyId, set);
    }

    private IReadOnlySet<string> Store(string companyId, HashSet<string> set)
    {
        _cache[companyId] = new Entry(set, _clock.UtcNow.AddSeconds(CacheTtlSeconds));
        return set;
    }

    /// <summary>Yönetim ekranının listesi: katalogdaki her alan + firmanın etkin seçimi (ekran sıralı).</summary>
    public IReadOnlyList<FieldRequirementRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var zorunlular = TumZorunlular(s.CompanyId);
        return FieldCatalog.All
            .Select(f => new FieldRequirementRow(f.ScreenKey, f.ScreenLabel, f.FieldKey, f.Label,
                f.SystemRequired, f.SystemRequired || zorunlular.Contains(Anahtar(f.ScreenKey, f.FieldKey))))
            .ToList();
    }

    /// <summary>Alanı zorunlu yapar / opsiyonele döndürür (yalnız katalogda olan, sistem-zorunlusu
    /// OLMAYAN alanlar). Fail-closed: bilinmeyen alan ve sistem zorunlusu REDDEDİLİR.</summary>
    public void Set(SessionContext s, string screenKey, string fieldKey, bool required)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var def = FieldCatalog.Find(screenKey, fieldKey)
            ?? throw new ArgumentException($"Bilinmeyen alan: {screenKey}/{fieldKey}");
        if (def.SystemRequired)
            throw new InvalidOperationException(
                $"«{def.Label}» sistem zorunlusudur; iş kuralları bu alana dayanır ve buradan değiştirilemez.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Önce güncelle; satır yoksa ekle (UNIQUE kısıtı mükerrer kaydı zaten engeller) — 065 deseni.
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE field_requirements SET required=@r, updated_at=@now " +
                              "WHERE company_id=@c AND screen_key=@s AND field_key=@f;";
            upd.AddWithValue("@r", required ? 1 : 0); upd.AddWithValue("@now", now);
            upd.AddWithValue("@c", s.CompanyId); upd.AddWithValue("@s", screenKey); upd.AddWithValue("@f", fieldKey);
            if (upd.ExecuteNonQuery() == 0)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO field_requirements(id, company_id, screen_key, field_key, required, created_at, updated_at) " +
                                  "VALUES(@id,@c,@s,@f,@r,@now,@now);";
                ins.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                ins.AddWithValue("@c", s.CompanyId); ins.AddWithValue("@s", screenKey); ins.AddWithValue("@f", fieldKey);
                ins.AddWithValue("@r", required ? 1 : 0); ins.AddWithValue("@now", now);
                ins.ExecuteNonQuery();
            }
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "field_requirements", screenKey + "/" + fieldKey,
            AuditActions.Update, s.UserId, AfterJson: $"{{\"required\":{(required ? "true" : "false")}}}"), _clock);

        tx.Commit();
        Invalidate(s.CompanyId);   // yönetici değiştirdiği anda etkili olsun (bayat veri kalmaz)
    }

    /// <summary>Form doğrulaması için yardımcı: firmanın zorunlu yaptığı alanlardan DOLU OLMAYANLARIN
    /// etiketlerini döndürür. Boş liste = sorun yok. Formlar tek cümlelik hata üretmek için kullanır.</summary>
    public IReadOnlyList<string> EksikAlanlar(string companyId, string screenKey,
        IReadOnlyDictionary<string, bool> aluDolu)
    {
        var zorunlu = RequiredFieldsFor(companyId, screenKey);
        var eksik = new List<string>();
        foreach (var f in zorunlu)
            if (aluDolu.TryGetValue(f, out var dolu) && !dolu)
                eksik.Add(FieldCatalog.Find(screenKey, f)?.Label ?? f);
        return eksik;
    }
}
