using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Org;

public sealed record PersonnelTitle(string Id, string Name);

/// <summary>
/// Unvan SABİT TANIM listesi (firma bazlı). Personel formunda unvan artık serbest metin değil, bu listeden
/// seçilir; yanındaki "+" butonu ile yeni tanım eklenir. Yetki: "personnel" modülü (görme=liste, ekleme=create).
/// Aynı firmada aynı isimli unvan iki kez eklenemez (büyük/küçük harf duyarsız, kırpılmış).
/// </summary>
public sealed class PersonnelTitleService
{
    private const string Module = "personnel";
    /// <summary>Türkçe karşılaştırma (Ş/İ/Ğ doğru eşleşsin) — SQLite LOWER() yalnız ASCII küçültür.</summary>
    private static readonly System.Globalization.CompareInfo Tr =
        System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public PersonnelTitleService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Firmanın unvan tanımları (alfabetik).</summary>
    public IReadOnlyList<PersonnelTitle> List(SessionContext session)
    {
        AccessControl.Require(session, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM personnel_titles WHERE company_id=$c AND is_deleted=0 ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", session.CompanyId);
        var list = new List<PersonnelTitle>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new PersonnelTitle(r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>Yeni unvan tanımı ekler ("+" butonu). Aynı isim varsa MEVCUDUNU döner (sessiz tekrar yok).</summary>
    public PersonnelTitle Create(SessionContext session, string name)
    {
        AccessControl.Require(session, Module, PermissionAction.Create);
        var clean = (name ?? "").Trim();
        if (clean.Length == 0) throw new InvalidOperationException("Unvan adı boş olamaz.");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Zaten var mı? Karşılaştırma TÜRKÇE duyarlı yapılır ("Şoför" == "şoför"). SQLite'ın LOWER()'ı yalnız
        // ASCII küçültür (Ş/İ/Ğ'yi çevirmez), o yüzden eşleştirme C# tarafında tr-TR ile yapılır.
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT id, name FROM personnel_titles WHERE company_id=$c AND is_deleted=0;";
            q.Parameters.AddWithValue("$c", session.CompanyId);
            using var r = q.ExecuteReader();
            while (r.Read())
                if (Tr.Compare(r.GetString(1), clean, System.Globalization.CompareOptions.IgnoreCase) == 0)
                    return new PersonnelTitle(r.GetString(0), r.GetString(1));
        }

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO personnel_titles(id, company_id, name, created_at, updated_at, version, is_deleted) " +
                "VALUES($id,$c,$n,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", session.CompanyId);
            cmd.Parameters.AddWithValue("$n", clean);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(session.CompanyId, "personnel_title", id, AuditActions.Create, session.UserId), _clock);
        tx.Commit();
        return new PersonnelTitle(id, clean);
    }

    /// <summary>Unvan tanımını kaldırır (soft delete). Personeldeki mevcut unvan metni korunur.</summary>
    public void Delete(SessionContext session, string id)
    {
        AccessControl.Require(session, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        int affected;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE personnel_titles SET is_deleted=1, version=version+1, updated_at=$now WHERE id=$id AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", session.CompanyId);
            cmd.Parameters.AddWithValue("$now", now);
            affected = cmd.ExecuteNonQuery();
        }
        if (affected > 0)
            AuditWriter.Write(conn, tx, new AuditEntry(session.CompanyId, "personnel_title", id, AuditActions.Delete, session.UserId), _clock);
        tx.Commit();
    }
}
