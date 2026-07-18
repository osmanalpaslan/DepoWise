using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Settings;

/// <summary>
/// Liste ekranı kolon tercihi — KİŞİSEL (kullanıcı isteği 2026-07-17). Anahtar (user_id, list_key);
/// firma/yetki kontrolü GEREKMEZ — herkes yalnız KENDİ tercihini okur/yazar (session'dan gelen user_id,
/// dışarıdan asla). Değer yoksa (ilk açılış) çağıran ekranın kendi varsayılan kolon listesini kullanır.
/// </summary>
public sealed class UserListPreferenceService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public UserListPreferenceService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Kullanıcının bu liste için kayıtlı kolon anahtarları — hiç kaydetmediyse null (varsayılana düş).</summary>
    public IReadOnlyList<string>? GetColumns(SessionContext s, string listKey)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT columns_json FROM user_list_preferences WHERE user_id=$u AND list_key=$k;";
        cmd.Parameters.AddWithValue("$u", s.UserId);
        cmd.Parameters.AddWithValue("$k", listKey);
        var json = cmd.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(json);
            // BOŞ dizi "ayar yok" sayılır (varsayılana düş) — kullanıcı yalnız sayfa boyutu/genişlik kaydettiyse
            // columns_json '[]' olarak açılmış olabilir; bu, "hiç kolon" demek DEĞİLDİR.
            return arr is { Length: > 0 } ? arr : null;
        }
        catch { return null; }   // bozuk kayıt → varsayılana düş (kullanıcıyı kilitlemez)
    }

    public void SaveColumns(SessionContext s, string listKey, IReadOnlyList<string> columns)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(columns);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO user_list_preferences(user_id, list_key, columns_json, updated_at) VALUES($u,$k,$j,$now)
ON CONFLICT(user_id, list_key) DO UPDATE SET columns_json=$j, updated_at=$now;";
        cmd.Parameters.AddWithValue("$u", s.UserId);
        cmd.Parameters.AddWithValue("$k", listKey);
        cmd.Parameters.AddWithValue("$j", json);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    // ── Sayfa boyutu (kullanıcı isteği 2026-07-18: değiştirilmemişse null → ekran 25 kullanır) ──
    public int? GetPageSize(SessionContext s, string listKey)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT page_size FROM user_list_preferences WHERE user_id=$u AND list_key=$k;";
        cmd.Parameters.AddWithValue("$u", s.UserId);
        cmd.Parameters.AddWithValue("$k", listKey);
        var v = cmd.ExecuteScalar();
        return v is null or System.DBNull ? null : System.Convert.ToInt32(v);
    }

    public void SavePageSize(SessionContext s, string listKey, int pageSize)
        => UpsertField(s, listKey, "page_size", pageSize);

    // ── Kolon genişlikleri (kişiye özel; kolon anahtarı → piksel). Yoksa null → otomatik genişlik. ──
    public IReadOnlyDictionary<string, int>? GetWidths(SessionContext s, string listKey)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT widths_json FROM user_list_preferences WHERE user_id=$u AND list_key=$k;";
        cmd.Parameters.AddWithValue("$u", s.UserId);
        cmd.Parameters.AddWithValue("$k", listKey);
        var json = cmd.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            return map is { Count: > 0 } ? map : null;
        }
        catch { return null; }
    }

    public void SaveWidths(SessionContext s, string listKey, IReadOnlyDictionary<string, int> widths)
        => UpsertField(s, listKey, "widths_json", JsonSerializer.Serialize(widths));

    /// <summary>Tek alanı upsert eder; ilk kez ekleniyorsa columns_json '[]' (yani "ayar yok") ile açılır,
    /// böylece bu alan kolon seçimini EZMEZ (GetColumns boş diziyi null sayar).</summary>
    private void UpsertField(SessionContext s, string listKey, string field, object value)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
INSERT INTO user_list_preferences(user_id, list_key, columns_json, {field}, updated_at) VALUES($u,$k,'[]',$v,$now)
ON CONFLICT(user_id, list_key) DO UPDATE SET {field}=$v, updated_at=$now;";
        cmd.Parameters.AddWithValue("$u", s.UserId);
        cmd.Parameters.AddWithValue("$k", listKey);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }
}
