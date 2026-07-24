using System.Data.Common;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// PostgreSQL geçişi — Faz 3 (2026-07-23): firma bazlı TOPLU SİLME'nin PostgreSQL yolu.
///
/// SQLite'ta silme, bağlantıda <c>PRAGMA foreign_keys=OFF</c> ile FK kapatılıp herhangi bir sırada
/// yapılır (bkz. <see cref="Organization.CompanyPurgeService"/>). PostgreSQL'de bu MÜMKÜN DEĞİL: Neon'un
/// tablo-sahibi rolü (neondb_owner) ne <c>session_replication_role=replica</c> ne de
/// <c>ALTER TABLE ... DISABLE TRIGGER ALL</c> yapabilir (ikisi de 42501 izin reddi — gerçek testle doğrulandı).
///
/// Bu yüzden PG'de FK'ye SAYGI göstererek silinir:
///   1) <b>Kapanış (closure):</b> hedef tabloları (company_id'li + <paramref name="includeCompanyTable"/>)
///      GEÇİŞLİ olarak REFERANS EDEN tüm tablolar da silme kümesine alınır. Böylece hedefin company_id'SİZ
///      çocukları (junction/satır tabloları) VE hedefe bağlı ama hedef-olmayan company_id'li tablolar
///      (ör. Reset'te vehicle_meter_logs) da silinir — hiçbiri yetim/engel kalmaz.
///   2) <b>Silme ifadeleri:</b> company_id'li tablo → <c>WHERE company_id=@c</c>; company_id'siz tablo →
///      company_id'li ebeveynine JOIN (<c>WHERE fk IN (SELECT pk FROM parent WHERE company_id=@c)</c>).
///   3) <b>Retry-fixpoint (savepoint):</b> tablolar birbirine FK'li olduğundan doğru sıra önceden bilinemez;
///      her DELETE bir SAVEPOINT içinde denenir, FK ihlali (23503) verirse geri alınıp sonraki geçişe bırakılır,
///      ilerleme durana dek tekrarlanır (topolojik sıra kendiliğinden oluşur). İlerleme yoksa gerçek hata
///      yükselir (tek transaction → güvenli biçimde geri sarılır, kısmi silme kalmaz).
///   4) İstenirse <c>companies</c> satırı da kümeye eklenir (retry onu doğal olarak en sona bırakır).
///
/// Tümü ÇAĞIRANIN transaction'ı içinde çalışır (tek transaction; kısmi silme yok).
/// </summary>
public static class DialectPurge
{
    private readonly record struct Fk(string Child, string ChildCol, string Parent, string ParentCol);

    /// <summary>PG'de firma verisini FK-güvenli siler. <paramref name="includeCompanyTable"/> hangi company_id
    /// tablolarının hedef olduğunu seçer (Purge: hepsi; Reset: yalnız iş tabloları). Döner: (silinen satır, dokunulan tablo).</summary>
    public static (int Rows, int Touched) DeleteCompanyData(
        DbConnection conn, DbTransaction tx, string companyId,
        Func<string, bool> includeCompanyTable, bool deleteCompaniesRow,
        ISet<string> protectedTables)
    {
        var companyCols = CompanyIdTables(conn, tx);                 // company_id sütunu olan tüm tablolar
        var fks = ForeignKeys(conn, tx);
        bool NotProtected(string t) => !protectedTables.Contains(t)
            && !string.Equals(t, "companies", StringComparison.OrdinalIgnoreCase);

        // 1) KAPANIŞ: hedefler + onları (geçişli) referans eden tüm tablolar.
        var closure = new HashSet<string>(
            companyCols.Where(t => NotProtected(t) && includeCompanyTable(t)),
            StringComparer.OrdinalIgnoreCase);
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var fk in fks)
                if (closure.Contains(fk.Parent) && !closure.Contains(fk.Child)
                    && !string.Equals(fk.Child, fk.Parent, StringComparison.OrdinalIgnoreCase)
                    && NotProtected(fk.Child))
                    grew |= closure.Add(fk.Child);
        }

        // 2) Silme ifadeleri: company_id varsa doğrudan; yoksa company_id'li ebeveyne JOIN.
        var pending = new List<string>();
        foreach (var t in closure)
        {
            if (companyCols.Contains(t))
                pending.Add($"DELETE FROM \"{t}\" WHERE company_id=@c;");
            else
                foreach (var fk in fks)
                    if (string.Equals(fk.Child, t, StringComparison.OrdinalIgnoreCase) && companyCols.Contains(fk.Parent))
                        pending.Add($"DELETE FROM \"{t}\" WHERE \"{fk.ChildCol}\" IN " +
                                    $"(SELECT \"{fk.ParentCol}\" FROM \"{fk.Parent}\" WHERE company_id=@c);");
        }
        if (deleteCompaniesRow) pending.Add("DELETE FROM companies WHERE id=@c;");

        // 3) Retry-fixpoint: FK sırası savepoint+geri-al ile kendiliğinden çözülür.
        int rows = 0, touched = 0, sp = 0;
        while (pending.Count > 0)
        {
            int before = pending.Count;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var spName = $"dwp{sp++}";
                Exec(conn, tx, $"SAVEPOINT {spName};");
                try
                {
                    var n = ExecSavepointless(conn, tx, pending[i], companyId);
                    Exec(conn, tx, $"RELEASE SAVEPOINT {spName};");
                    if (n > 0) touched++;
                    rows += n;
                    pending.RemoveAt(i);
                }
                catch (DbException)   // büyük olasılıkla FK sırası (23503) → geri al, sonraki geçişe bırak.
                {                     // Gerçekten çözülemeyen hata ise aşağıdaki "ilerleme yok" dalında yükselir.
                    Exec(conn, tx, $"ROLLBACK TO SAVEPOINT {spName};");
                }
            }
            if (pending.Count == before)
            {
                // İlerleme yok → çözülemeyen FK/hata. İlk bekleyeni savepoint'siz çalıştır ki gerçek hata
                // yükselsin (tek transaction → tümü geri sarılır, kısmi silme kalmaz).
                ExecSavepointless(conn, tx, pending[0], companyId);
            }
        }
        return (rows, touched);
    }

    /// <summary>Verilen DELETE ifadelerini PostgreSQL'de FK-GÜVENLİ sırada çalıştırır (FK kapatmadan):
    /// her ifade bir SAVEPOINT içinde denenir, FK ihlali verirse geri alınıp sonraki geçişe bırakılır,
    /// ilerleme durana dek tekrarlanır (topolojik sıra kendiliğinden). Gerçekten çözülemeyen hata yükselir.
    /// <paramref name="bind"/> her komuta ortak parametreleri (ör. @me/@co) bağlar. Döner: (ifade, silinen satır).
    /// ⚠️ Yalnız var olan/geçerli tablolar verilmeli (eksik tablo hatası FK değildir → ilerleme-yok'ta yükselir).</summary>
    public static List<(string Sql, int Rows)> RunFkSafe(
        DbConnection conn, DbTransaction tx, IEnumerable<string> statements, Action<DbCommand>? bind = null)
    {
        var pending = statements.ToList();
        var done = new List<(string, int)>();
        int sp = 0;
        while (pending.Count > 0)
        {
            int before = pending.Count;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var spName = $"dwr{sp++}";
                Exec(conn, tx, $"SAVEPOINT {spName};");
                try
                {
                    int n = RunOne(conn, tx, pending[i], bind);
                    Exec(conn, tx, $"RELEASE SAVEPOINT {spName};");
                    done.Add((pending[i], n));
                    pending.RemoveAt(i);
                }
                catch (DbException)   // büyük olasılıkla FK sırası → geri al, sonraki geçişe bırak.
                {
                    Exec(conn, tx, $"ROLLBACK TO SAVEPOINT {spName};");
                }
            }
            if (pending.Count == before)
                done.Add((pending[0], RunOne(conn, tx, pending[0], bind)));   // gerçek hata yükselsin
        }
        return done;
    }

    private static int RunOne(DbConnection conn, DbTransaction tx, string sql, Action<DbCommand>? bind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind?.Invoke(cmd);
        return cmd.ExecuteNonQuery();
    }

    private static HashSet<string> CompanyIdTables(DbConnection conn, DbTransaction tx)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT table_name FROM information_schema.columns " +
            "WHERE table_schema='public' AND column_name='company_id';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    private static List<Fk> ForeignKeys(DbConnection conn, DbTransaction tx)
    {
        var list = new List<Fk>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Tek sütunlu FK'ler (şemamızda hepsi öyle): conkey[1]/confkey[1].
        cmd.CommandText = @"
SELECT con.conrelid::regclass::text, ac.attname, con.confrelid::regclass::text, ap.attname
FROM pg_constraint con
JOIN pg_attribute ac ON ac.attrelid=con.conrelid AND ac.attnum=con.conkey[1]
JOIN pg_attribute ap ON ap.attrelid=con.confrelid AND ap.attnum=con.confkey[1]
WHERE con.contype='f' AND con.connamespace='public'::regnamespace;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Fk(Strip(r.GetString(0)), r.GetString(1), Strip(r.GetString(2)), r.GetString(3)));
        return list;
    }

    // regclass::text bazen "public.tbl" ya da tırnaklı gelebilir → sade tablo adına indir.
    private static string Strip(string name)
    {
        var s = name;
        var dot = s.LastIndexOf('.');
        if (dot >= 0) s = s[(dot + 1)..];
        return s.Trim('"');
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int ExecSavepointless(DbConnection conn, DbTransaction tx, string sql, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteNonQuery();
    }
}
