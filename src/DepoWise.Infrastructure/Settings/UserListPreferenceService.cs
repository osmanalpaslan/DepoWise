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
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<string[]>(json); }
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
}
