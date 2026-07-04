using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Desktop;

/// <summary>
/// "Beni Hatırla" — güvenli otomatik giriş. Parola DÜZ SAKLANMAZ:
/// rastgele token üretilir, DB'de yalnız SHA-256 hash'i tutulur; düz token cihazda **DPAPI** (CurrentUser)
/// ile şifrelenip dosyaya yazılır. Açılışta dosya çözülür → DB'de hash + süre doğrulanır → oturum kurulur.
/// </summary>
public static class RememberMeService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private static string DirPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(root, AppPaths.AppFolderName);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string FilePath => Path.Combine(DirPath, "remember.bin");

    /// <summary>Son giren kullanıcı adı (hassas değil) — çıkış sonrası login ekranını doldurmak için.
    /// Beni Hatırla token'ı silinse bile bu ad korunur.</summary>
    private static string LastUserFile => Path.Combine(DirPath, "lastuser.txt");

    public static void SaveLastUsername(string username)
    {
        try { File.WriteAllText(LastUserFile, username ?? ""); } catch { }
    }

    public static string GetLastUsername()
    {
        try { return File.Exists(LastUserFile) ? File.ReadAllText(LastUserFile).Trim() : ""; } catch { return ""; }
    }

    /// <summary>Giriş sonrası çağrılır: token üret, hash'i DB'ye yaz, düz token'ı DPAPI ile dosyaya kaydet.</summary>
    public static void Save(SessionContext session)
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI yalnız Windows
        try
        {
            var token = SyncCrypto.NewKey();
            var now = DateTimeOffset.UtcNow;
            var expires = now.Add(Ttl).ToUnixTimeMilliseconds();

            using (var conn = DesktopServices.Factory.Create())
            {
                using var del = conn.CreateCommand();
                del.CommandText = "DELETE FROM remember_tokens WHERE user_id=$u;";
                del.Parameters.AddWithValue("$u", session.UserId);
                del.ExecuteNonQuery();

                using var ins = conn.CreateCommand();
                ins.CommandText =
                    "INSERT INTO remember_tokens(id, user_id, company_id, token_hash, expires_at, created_at) " +
                    "VALUES($id,$u,$c,$h,$e,$n);";
                ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                ins.Parameters.AddWithValue("$u", session.UserId);
                ins.Parameters.AddWithValue("$c", session.CompanyId);
                ins.Parameters.AddWithValue("$h", SyncCrypto.Sha256Hex(token));
                ins.Parameters.AddWithValue("$e", expires);
                ins.Parameters.AddWithValue("$n", now.ToUnixTimeMilliseconds());
                ins.ExecuteNonQuery();
            }

            var payload = $"{session.CompanyId}|{session.UserId}|{token}";
            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(payload), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, protectedBytes);
        }
        catch { /* hatırlama başarısızsa normal login akışı bozulmaz */ }
    }

    /// <summary>Açılışta: korunan dosyayı çöz, token'ı DB'de doğrula, oturum kur. Geçersizse null.</summary>
    public static SessionContext? TryAutoLogin()
    {
        if (!OperatingSystem.IsWindows()) return null; // DPAPI yalnız Windows
        try
        {
            if (!File.Exists(FilePath)) return null;
            var raw = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            var parts = Encoding.UTF8.GetString(raw).Split('|');
            if (parts.Length != 3) { Clear(); return null; }
            var (companyId, userId, token) = (parts[0], parts[1], parts[2]);

            bool valid;
            using (var conn = DesktopServices.Factory.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM remember_tokens WHERE user_id=$u AND company_id=$c " +
                    "AND token_hash=$h AND expires_at >= $now;";
                cmd.Parameters.AddWithValue("$u", userId);
                cmd.Parameters.AddWithValue("$c", companyId);
                cmd.Parameters.AddWithValue("$h", SyncCrypto.Sha256Hex(token));
                cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                valid = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
            if (!valid) { Clear(); return null; }

            return DesktopServices.Auth.CreateSessionForUser(companyId, userId);
        }
        catch { Clear(); return null; }
    }

    /// <summary>Hatırlamayı kaldırır (logout / iptal): dosya + DB token'ı silinir.</summary>
    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
