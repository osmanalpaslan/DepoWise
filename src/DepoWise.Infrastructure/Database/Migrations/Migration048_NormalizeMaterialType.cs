using System;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Mevcut malzeme "Tür" değerlerini KANONİK biçime çevirir (kullanıcı isteği 2026-07-18, ADR-089):
/// Excel içe aktarımında yalnız BÜYÜK harfle yazılan "YEDEK PARÇA" gibi değerler kanonik "Yedek Parça"
/// ile eşleşmiyor, listede/filtrede ayrı bir değer gibi görünüyordu. Bundan sonra <c>MaterialService</c>
/// yazarken normalize eder; bu migration da ZATEN kaydedilmiş eski değerleri bir kez düzeltir.
///
/// Neden C# (saf SQL değil): SQLite'ın <c>upper()/lower()</c> yalnız ASCII harfleri çevirir — Türkçe
/// ç/ş/ı/ğ/ö/ü'yü ele almaz, bu yüzden SQL ile harf-duyarsız eşleme güvenilmez. Kanonik liste burada
/// SABİTtir (tarihî migration; sonradan uygulama sabiti değişse bile bu davranış değişmemeli).
///
/// Yalnız GÖRÜNEN tür etiketini düzeltir — başka hiçbir alana dokunmaz. Idempotent (tekrar çalışsa da
/// kanonik değerler zaten eşit, UPDATE etkisiz).
/// </summary>
public sealed class Migration048_NormalizeMaterialType : IMigration
{
    public int Version => 48;
    public string Name => "normalize_material_type";

    private static readonly string[] Canonical = { "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer" };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // 1) Mevcut farklı tür değerlerini oku.
        var distinct = new System.Collections.Generic.List<string>();
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT DISTINCT type FROM materials WHERE type IS NOT NULL AND TRIM(type) <> '';";
            using var r = read.ExecuteReader();
            while (r.Read()) if (!r.IsDBNull(0)) distinct.Add(r.GetString(0));
        }

        // 2) Kanonik bir türe harf duyarsız uyan AMA biçimi farklı olanları düzelt.
        foreach (var current in distinct)
        {
            var trimmed = current.Trim();
            foreach (var canon in Canonical)
            {
                if (string.Equals(canon, trimmed, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(canon, current, StringComparison.Ordinal))
                {
                    using var up = conn.CreateCommand();
                    up.Transaction = tx;
                    up.CommandText = "UPDATE materials SET type=$canon WHERE type=$cur;";
                    up.AddWithValue("$canon", canon);
                    up.AddWithValue("$cur", current);
                    up.ExecuteNonQuery();
                    break;
                }
            }
        }
    }
}
