using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Kalıcı silme sonucu — kaç tabloda kaç satır silindi (kullanıcıya rapor edilir).</summary>
public sealed record PurgeResult(string CompanyId, string CompanyName, int TablesTouched, int RowsDeleted);

/// <summary>Silme künyesi — masaüstü bunu görüp yerel veriyi siler.</summary>
public sealed record CompanyPurgeRow(string CompanyId, string CompanyName, long PurgedAt, string PurgedBy);

/// <summary>
/// FİRMA KALICI SİLME (ADR-083) — geri alınamaz. Firma Tanım'ın soft-delete'inden (pasife alma) FARKLIDIR:
/// burada firma satırı ve o firmaya ait TÜM kayıtlar veritabanından fiziksel olarak silinir.
///
/// ⚠️ CLAUDE.md §4 "operasyonel kaydı fiziksel silme" kuralının BİLİNÇLİ istisnasıdır (kullanıcı talebi
/// 2026-07-16): platform sahibinin test/offboarding aracı. Kapsam yalnız FİRMA bazlıdır; normal iş
/// akışlarında silme yine yasak (iptal/ters kayıt + audit).
///
/// Korumalar (fail-closed):
/// - Yalnız süper admin.
/// - AKTÖRÜN KENDİ FİRMASI ASLA silinemez. Geçmişte kendi firmasını silen süper admin sistemden kilitlendi
///   (ADR-064) ve oturumu 401'e düştü (ADR-068); kalıcı silmede bunun telafisi YOKTUR.
/// - schema_migrations / sqlite_sequence / company_purges ASLA silinmez; sistem rolleri (company_id NULL) korunur.
/// - Tek transaction: kısmen silinmiş firma kalmaz.
/// - Silme künyesi (company_purges) AYNI transaction'da yazılır → çevrimdışı makineler silmeyi öğrenir;
///   künye olmasaydı makine kendi yerel kopyasını sunucuya geri push edip veriyi diriltirdi.
/// </summary>
public sealed class CompanyPurgeService
{
    /// <summary>Purge sırasında ASLA dokunulmayan tablolar.</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "schema_migrations", "sqlite_sequence", "company_purges",
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CompanyPurgeService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Firmanın adı (pasife alınmış olsa da bulur). Yoksa null.</summary>
    public string? FindName(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM companies WHERE id=$c;";
        cmd.Parameters.AddWithValue("$c", companyId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Firmayı ve TÜM verisini kalıcı siler. Geri alınamaz.</summary>
    public PurgeResult Purge(SessionContext actor, string companyId)
    {
        if (!actor.IsSuperAdmin) throw new ForbiddenException("Kalıcı silme yalnız süper admin.");
        if (string.IsNullOrWhiteSpace(companyId)) throw new ArgumentException("Firma seçilmedi.");
        // KRİTİK: kendi firmanı silemezsin (kilitlenme + oturum ölümü; bkz. ADR-064/068).
        if (string.Equals(companyId, actor.CompanyId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Kendi bağlı olduğunuz firmayı kalıcı silemezsiniz. Önce başka bir firmaya geçin ya da bu firmayı Firma Tanım'dan pasife alın.");

        var name = FindName(companyId) ?? throw new InvalidOperationException("Firma bulunamadı.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        var tables = ListTables(conn);

        // FK'ler kapalı: silme sırası önemsiz hale gelir (aksi halde ebeveyn/çocuk sırası tutturulamaz).
        using (var off = conn.CreateCommand()) { off.CommandText = "PRAGMA foreign_keys=OFF;"; off.ExecuteNonQuery(); }
        int rows = 0, touched = 0;
        try
        {
            using var tx = conn.BeginTransaction();

            // 1) user_roles'ta company_id YOK → firmanın kullanıcıları ÜZERİNDEN silinir. Users'tan ÖNCE olmalı.
            rows += Exec(conn, tx,
                "DELETE FROM user_roles WHERE user_id IN (SELECT id FROM users WHERE company_id=$c);",
                ("$c", companyId));

            // 2) company_id sütunu OLAN her tablodan bu firmanın satırlarını sil.
            //    roles'ta company_id NULL = sistem rolü → filtre onları korur (tüm firmalar kullanır).
            foreach (var t in tables)
            {
                if (Protected.Contains(t)) continue;
                if (string.Equals(t, "companies", StringComparison.OrdinalIgnoreCase)) continue; // en sonda
                if (!HasColumn(conn, t, "company_id")) continue;
                var n = Exec(conn, tx, $"DELETE FROM \"{t}\" WHERE company_id=$c;", ("$c", companyId));
                if (n > 0) touched++;
                rows += n;
            }

            // 3) Firmanın kendisi.
            rows += Exec(conn, tx, "DELETE FROM companies WHERE id=$c;", ("$c", companyId));
            touched++;

            // 4) KÜNYE — aynı transaction. Bu satır olmadan çevrimdışı makine silmeyi asla öğrenemez.
            Exec(conn, tx,
                "INSERT OR REPLACE INTO company_purges(company_id, company_name, purged_at, purged_by) VALUES($c,$n,$at,$by);",
                ("$c", companyId), ("$n", name), ("$at", now), ("$by", actor.UserId));

            // 5) Audit: aktörün KENDİ firmasında iz kalır (silinen firmada iz bırakmanın anlamı yok — o satırlar gitti).
            AuditWriter.Write(conn, tx, new AuditEntry(actor.CompanyId, "company_purge", companyId, AuditActions.Delete, actor.UserId), _clock);

            tx.Commit();
        }
        finally
        {
            using var on = conn.CreateCommand(); on.CommandText = "PRAGMA foreign_keys=ON;"; on.ExecuteNonQuery();
        }
        return new PurgeResult(companyId, name, touched, rows);
    }

    /// <summary>İŞ VERİSİ SIFIRLAMA (kullanıcı talebi 2026-07-19): firma + şubeler + KULLANICILAR KORUNUR;
    /// yalnız o firmanın İŞ verisi (malzeme/araç/stok/bakım/yakıt/talep + ilgili tanımlar =
    /// <see cref="DepoWise.Infrastructure.Sync.BusinessSyncService.Tables"/>) fiziksel silinir. Kalıcı Silme'den
    /// (ADR-083) FARKI: firma kabuğu (companies/branches/users/roles) DOKUNULMAZ → kullanıcı giriş yapmaya
    /// devam eder, temiz bir iş verisiyle sıfırdan başlar.
    ///
    /// ⚠️ Geri alınamaz. Çağıran uç (endpoint) parola + özel kod + firma-adı teyidini ZORUNLU kılar.
    /// Makine koordinasyonu: bu tek başına yeterli DEĞİL — endpoint ayrıca CompanyLocalReset.RequestReset
    /// çağırır ki firmanın makineleri bir sonraki girişte yerel iş verisini temizleyip boş sunucudan çeksin
    /// (aksi halde çevrimiçi bir makine yerel kopyasını geri push edip veriyi diriltir).</summary>
    public PurgeResult ResetBusinessData(SessionContext actor, string companyId)
    {
        if (!actor.IsSuperAdmin) throw new ForbiddenException("İş verisi sıfırlama yalnız süper admin.");
        if (string.IsNullOrWhiteSpace(companyId)) throw new ArgumentException("Firma seçilmedi.");
        var name = FindName(companyId) ?? throw new InvalidOperationException("Firma bulunamadı.");

        using var conn = _factory.Create();
        using (var off = conn.CreateCommand()) { off.CommandText = "PRAGMA foreign_keys=OFF;"; off.ExecuteNonQuery(); }
        int rows = 0, touched = 0;
        try
        {
            using var tx = conn.BeginTransaction();
            // YALNIZ iş verisi tabloları — firma/şube/kullanıcı/rol/yetki KORUNUR.
            foreach (var t in DepoWise.Infrastructure.Sync.BusinessSyncService.Tables)
            {
                if (!TableExists(conn, t) || !HasColumn(conn, t, "company_id")) continue;
                var n = Exec(conn, tx, $"DELETE FROM \"{t}\" WHERE company_id=$c;", ("$c", companyId));
                if (n > 0) touched++;
                rows += n;
            }
            AuditWriter.Write(conn, tx, new AuditEntry(actor.CompanyId, "company_business_reset", companyId, AuditActions.Delete, actor.UserId), _clock);
            tx.Commit();
        }
        finally
        {
            using var on = conn.CreateCommand(); on.CommandText = "PRAGMA foreign_keys=ON;"; on.ExecuteNonQuery();
        }
        return new PurgeResult(companyId, name, touched, rows);
    }

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>Bu firma kalıcı silindi mi? (masaüstü eşitleme adımı sorar)</summary>
    public CompanyPurgeRow? GetPurge(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT company_id, company_name, purged_at, purged_by FROM company_purges WHERE company_id=$c;";
        cmd.Parameters.AddWithValue("$c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new CompanyPurgeRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3)) : null;
    }

    /// <summary>Silinmiş firmaların künyeleri (süper admin görebilsin — ne zaman ne silindi).</summary>
    public IReadOnlyList<CompanyPurgeRow> ListPurges(SessionContext actor)
    {
        if (!actor.IsSuperAdmin) throw new ForbiddenException("Yalnız süper admin.");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT company_id, company_name, purged_at, purged_by FROM company_purges ORDER BY purged_at DESC;";
        var list = new List<CompanyPurgeRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new CompanyPurgeRow(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3)));
        return list;
    }

    private static List<string> ListTables(SqliteConnection conn)
    {
        var tables = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));
        return tables;
    }

    private static bool HasColumn(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static int Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteNonQuery();
    }
}
