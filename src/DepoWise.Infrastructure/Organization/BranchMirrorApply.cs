using System.Collections.Generic;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Organization;

/// <summary>
/// SNK-12 (2026-08-11) — ŞUBE AYNALAMASININ SAF (ağdan bağımsız) ÇEKİRDEĞİ.
///
/// Şubeler <b>web-otoriteli</b>dir ve iş-senkronunda (business-push/pull) TAŞINMAZ; masaüstü onları
/// ayrı bir uçtan alıp yerel SQLite'a aynalar. Ağ tarafı masaüstündedir (<c>BranchMirror</c>);
/// VERİ davranışı burada durur — böylece Avalonia'ya bağımlı olmadan doğrudan test edilebilir.
///
/// Kurallar:
/// • Gelen şubeler upsert edilir (ad/kod güncellenir; sunucuda yeniden açıldıysa <c>is_deleted=0</c> olur).
/// • Sunucunun listesinde ARTIK OLMAYAN yerel şubeler <c>is_deleted=1</c> yapılır — <b>fiziksel silme YOK</b>:
///   stok hareketleri o kimliğe bağlıdır, silmek geçmişi kopartırdı (CLAUDE.md §4).
/// • Kapsam YALNIZ verilen firmadır → başka firmanın şubelerine dokunulmaz (tenant izolasyonu).
/// • Çevrimdışı davranışı çağıranın sorumluluğundadır: sunucuya ulaşılamazsa bu metot HİÇ çağrılmaz,
///   yerel liste olduğu gibi kalır ve daha önce inmiş depolarla çevrimdışı çalışma sürer.
/// </summary>
public static class BranchMirrorApply
{
    public static void Run(IDbConnectionFactory factory, string companyId,
        IReadOnlyList<(string Id, string Name, string? Code)> rows)
    {
        var now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var conn = factory.Create();
        var serverIds = new List<string>();
        foreach (var b in rows)
        {
            if (string.IsNullOrEmpty(b.Id)) continue;
            serverIds.Add(b.Id);
            using var c = conn.CreateCommand();
            c.CommandText = "INSERT INTO branches(id,company_id,name,kind,code,created_at,updated_at,version,is_deleted) " +
                            "VALUES(@id,@c,@n,'branch',@code,@now,@now,1,0) " +
                            "ON CONFLICT(id) DO UPDATE SET company_id=@c, name=@n, code=@code, is_deleted=0, updated_at=@now;";
            c.AddWithValue("@id", b.Id);
            c.AddWithValue("@c", companyId);
            c.AddWithValue("@n", b.Name);
            c.AddWithValue("@code", (object?)b.Code ?? System.DBNull.Value);
            c.AddWithValue("@now", now);
            c.ExecuteNonQuery();
        }

        using (var del = conn.CreateCommand())
        {
            var names = new List<string>();
            for (int i = 0; i < serverIds.Count; i++)
            {
                var p = "@k" + i;
                names.Add(p);
                del.AddWithValue(p, serverIds[i]);
            }
            del.CommandText =
                "UPDATE branches SET is_deleted=1, updated_at=@now WHERE company_id=@c AND is_deleted=0" +
                (names.Count > 0 ? " AND id NOT IN (" + string.Join(",", names) + ")" : "") + ";";
            del.AddWithValue("@c", companyId);
            del.AddWithValue("@now", now);
            del.ExecuteNonQuery();
        }
    }
}
