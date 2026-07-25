using System.Threading.Tasks;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;   // AddWithValue (DbCommand extension)

namespace DepoWise.Desktop;

/// <summary>
/// Şubeler SUNUCU-OTORİTELİ: yerel kopyayı sunucununkiyle aynalar (sunucudakileri upsert, sunucuda ARTIK
/// OLMAYAN yerel şubeleri is_deleted=1). Her girişte (LoginViewModel) ve masaüstü çevrimiçi şube işlemi
/// sonrası (BranchesViewModel) çağrılır — böylece masaüstünde sunucuya yazılan şube anında yerelde görünür.
/// Çevrimdışıysa (sunucu null) DOKUNMAZ (yerelde olanla devam).
/// </summary>
public static class BranchMirror
{
    public static async Task RefreshAsync(string companyId)
    {
        var now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            var online = await ServerAuthClient.GetLoginBranchesAsync(companyId);
            if (online is null) return; // çevrimdışı → yerelde zaten olanla devam
            using var conn = DesktopServices.Factory.Create();
            var serverIds = new System.Collections.Generic.List<string>();
            foreach (var b in online)
            {
                if (b.Id == BranchConstants.AllBranchesId) continue;
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

            // Sunucunun listesinde ARTIK OLMAYAN yerel şubeler SİLİNMİŞ demektir → yerelde de pasife al.
            using (var del = conn.CreateCommand())
            {
                var names = new System.Collections.Generic.List<string>();
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
        catch { }
    }
}
