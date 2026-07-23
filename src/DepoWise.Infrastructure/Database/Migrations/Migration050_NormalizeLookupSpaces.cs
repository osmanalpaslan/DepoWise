using System.Text.RegularExpressions;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Mevcut tanım adlarındaki (kategori/marka/birim/tedarikçi/araç tipi-kategori-model/şube) ARDIŞIK 2+
/// boşluğu bir kez TEK boşluğa indirir (kullanıcı isteği 2026-07-19: "Malzeme kategorileri alanında
/// bulunan tanımların arasında 2 adet boşluktan fazla olan boşlukları 1 adet boşluk varmış gibi güncelle
/// boşluklar yüzünden satırlar çok fazla uzuyor"). Bundan sonra <c>LookupService.Insert/Rename</c> zaten
/// normalize eder (ADR-090); bu migration yalnız GEÇMİŞTE (elle/kopyala-yapıştırla) girilmiş bozuk veriyi
/// bir kez düzeltir. Idempotent (normal ad üzerinde tekrar çalışsa etkisiz).
/// </summary>
public sealed class Migration050_NormalizeLookupSpaces : IMigration
{
    public int Version => 50;
    public string Name => "normalize_lookup_spaces";

    private static readonly string[] Tables =
    {
        "material_categories", "brands", "units", "suppliers",
        "vehicle_types", "vehicle_categories", "vehicle_models", "branches",
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var table in Tables)
        {
            if (!TableExists(conn, tx, table)) continue;

            var updates = new System.Collections.Generic.List<(string Id, string Fixed)>();
            using (var read = conn.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = $"SELECT id, name FROM {table} WHERE name LIKE '%  %';";   // en az çift boşluk
                using var r = read.ExecuteReader();
                while (r.Read())
                {
                    var id = r.GetString(0);
                    var name = r.GetString(1);
                    var fixedName = Regex.Replace(name.Trim(), @"\s+", " ");
                    if (fixedName != name) updates.Add((id, fixedName));
                }
            }

            foreach (var (id, fixedName) in updates)
            {
                using var up = conn.CreateCommand();
                up.Transaction = tx;
                up.CommandText = $"UPDATE {table} SET name=@n WHERE id=@id;";
                up.AddWithValue("@n", fixedName);
                up.AddWithValue("@id", id);
                up.ExecuteNonQuery();
            }
        }
    }

    private static bool TableExists(DbConnection conn, DbTransaction tx, string table)
        => DbIntrospect.TableExists(conn, tx, table);
}
