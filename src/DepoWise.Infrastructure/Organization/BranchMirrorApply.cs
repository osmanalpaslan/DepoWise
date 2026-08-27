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
/// • Gelen şubeler upsert edilir (ad/kod/tür/üst şube güncellenir; sunucuda yeniden açıldıysa <c>is_deleted=0</c> olur).
/// • Sunucunun listesinde ARTIK OLMAYAN yerel şubeler <c>is_deleted=1</c> yapılır — <b>fiziksel silme YOK</b>:
///   stok hareketleri o kimliğe bağlıdır, silmek geçmişi kopartırdı (CLAUDE.md §4).
/// • Kapsam YALNIZ verilen firmadır → başka firmanın şubelerine dokunulmaz (tenant izolasyonu).
/// • Çevrimdışı davranışı çağıranın sorumluluğundadır: sunucuya ulaşılamazsa bu metot HİÇ çağrılmaz,
///   yerel liste olduğu gibi kalır ve daha önce inmiş depolarla çevrimdışı çalışma sürer.
///
/// <b>ŞB-01 (2026-08-18) DÜZELTMESİ.</b> Bu ayna eskiden yalnız (Id, Name, Code) taşıyordu:
/// INSERT'te <c>kind</c> sabit <c>'branch'</c> yazılıyor, <c>parent_id</c> hiç yazılmıyordu ve
/// ON CONFLICT güncellemesi de bu iki kolona dokunmuyordu. Masaüstünde üst şube seçilip kaydedildiğinde
/// sunucu doğru kaydediyor, hemen ardından çalışan bu ayna yerel kopyayı üst şubesiz tazeliyor, ekran da
/// yerelden okuduğu için değer <b>"tanımlanmamış" gibi geri dönüyordu</b>. Artık ikisi de taşınır.
///
/// <b>İKİ GEÇİŞ, neden:</b> <c>branches.parent_id</c> kendi tablosuna yabancı anahtardır ve masaüstünde
/// yabancı anahtarlar AÇIKTIR (<c>foreign_keys=ON</c>). Alt şube, üst şubesinden ÖNCE gelirse tek geçişli
/// yazma FK hatası verirdi. Bu yüzden önce tüm satırlar <c>parent_id</c>'siz upsert edilir, sonra ikinci
/// geçişte üst şube bağlanır. Üst şube sunucunun listesinde YOKSA bağ kurulmaz (NULL kalır) — kopuk
/// referans üretmemek için.
/// </summary>
public static class BranchMirrorApply
{
    /// <summary>Sunucudan gelen şube satırı. <paramref name="Kind"/> boşsa <c>branch</c> kabul edilir.</summary>
    public readonly record struct Row(string Id, string Name, string? Code, string? Kind, string? ParentId);

    public static void Run(IDbConnectionFactory factory, string companyId, IReadOnlyList<Row> rows)
    {
        var now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var conn = factory.Create();
        var serverIds = new HashSet<string>(System.StringComparer.Ordinal);

        // 1. GEÇİŞ — satırları üst şube BAĞLAMADAN upsert et (FK sırası derdi olmasın).
        foreach (var b in rows)
        {
            if (string.IsNullOrEmpty(b.Id)) continue;
            serverIds.Add(b.Id);
            using var c = conn.CreateCommand();
            c.CommandText = "INSERT INTO branches(id,company_id,name,kind,code,created_at,updated_at,version,is_deleted) " +
                            "VALUES(@id,@c,@n,@k,@code,@now,@now,1,0) " +
                            "ON CONFLICT(id) DO UPDATE SET company_id=@c, name=@n, kind=@k, code=@code, is_deleted=0, updated_at=@now;";
            c.AddWithValue("@id", b.Id);
            c.AddWithValue("@c", companyId);
            c.AddWithValue("@n", b.Name);
            c.AddWithValue("@k", b.Kind is "site" or "field" ? b.Kind : "branch");
            c.AddWithValue("@code", (object?)b.Code ?? System.DBNull.Value);
            c.AddWithValue("@now", now);
            c.ExecuteNonQuery();
        }

        // 2. GEÇİŞ — üst şubeleri bağla. Artık tüm şubeler yereldedir; FK güvenlidir.
        // Kendi kendine referans (id == parentId) sunucuda zaten engellidir, burada da kabul edilmez.
        foreach (var b in rows)
        {
            if (string.IsNullOrEmpty(b.Id)) continue;
            var parent = b.ParentId;
            if (!string.IsNullOrEmpty(parent) && (!serverIds.Contains(parent!) || parent == b.Id)) parent = null;
            using var c = conn.CreateCommand();
            c.CommandText = "UPDATE branches SET parent_id=@p, updated_at=@now WHERE id=@id AND company_id=@c;";
            c.AddWithValue("@p", (object?)parent ?? System.DBNull.Value);
            c.AddWithValue("@id", b.Id);
            c.AddWithValue("@c", companyId);
            c.AddWithValue("@now", now);
            c.ExecuteNonQuery();
        }

        using (var del = conn.CreateCommand())
        {
            var names = new List<string>();
            int i = 0;
            foreach (var id in serverIds)
            {
                var p = "@k" + i++;
                names.Add(p);
                del.AddWithValue(p, id);
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
