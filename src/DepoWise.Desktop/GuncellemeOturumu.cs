using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop;

/// <summary>
/// ⭐ 2026-10-30 — GÜNCELLEME SONRASI OTOMATİK GİRİŞ (kullanıcı isteği: "ilk login olurken güncelleme yüklendikten sonra uygulama kendini
/// yeniden başlatıyor ve tekrar login olmamı istiyor … güncelleme sonrası otomatik giriş yapmalı").
///
/// <para>Güncelleme kurulmadan HEMEN önce açık oturumun bileti bırakılır; yeni sürüm açılınca bilet okunur ve kullanıcı aynı firma,
/// aynı çalışma şubesiyle doğrudan içeri alınır. Güvenlik:</para>
/// <list type="bullet">
///   <item><b>Bu bilgisayar + bu Windows hesabı:</b> Windows DPAPI (CurrentUser) + uygulamaya özel ek anahtar ile şifreli.</item>
///   <item><b>Tek kullanımlık:</b> okunur okunmaz silinir (geçersiz çıksa da).</item>
///   <item><b>Kısa ömürlü:</b> <see cref="Omur"/> (10 dk).</item>
///   <item><b>Yalnız hedef sürüm:</b> bilette kurulan sürüm yazılır; güncelleme kurulamazsa eski sürüm bileti KULLANAMAZ (kurulum
///         başarısızsa bilet ayrıca silinir).</item>
///   <item><b>Parola tutulmaz:</b> yalnız sunucunun girişte verdiği oturum jetonu (zaten bellekte duran) taşınır; eşitleme parolasız
///         bu jetonla sürer.</item>
///   <item>Kullanıcı bu arada pasifleştirildi / silindiyse oturum kurulmaz (AuthService.CreateSessionForUser null döner).</item>
/// </list>
/// </summary>
public static class GuncellemeOturumu
{
    public static readonly TimeSpan Omur = TimeSpan.FromMinutes(10);
    private static readonly byte[] EkAnahtar = Encoding.UTF8.GetBytes("DepoWise-guncelleme-sonrasi-oturum-v1");

    private sealed record Bilet(string CompanyId, string UserId, string Kullanici, string? SubeId, string? SubeAdi, bool TumSubeler,
        string? Jeton, string? SunucuAdresi, string HedefSurum, long SonGecerlilikMs);

    /// <summary>Kurtarılan oturum: kurulan <see cref="SessionContext"/> + çalışma şubesi.</summary>
    public sealed record Sonuc(SessionContext Oturum, string Kullanici, string? SubeId, string? SubeAdi, bool TumSubeler);

    private static string Yol
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppPaths.AppFolderName);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "guncelleme_oturumu.bin");
        }
    }

    /// <summary>Güncelleme kurulmadan hemen önce: açık oturum varsa bileti bırakır (yoksa hiçbir şey yapmaz).</summary>
    public static void Birak(string hedefSurum, TimeSpan? omur = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        var s = DesktopServices.Session;
        if (s is null || string.IsNullOrWhiteSpace(hedefSurum)) return;
        try
        {
            var b = new Bilet(s.CompanyId, s.UserId, RememberMeService.GetLastUsername(), DesktopServices.CurrentBranchId,
                DesktopServices.CurrentBranchName, DesktopServices.CurrentAllBranches, ServerAuthClient.Token, ServerAuthClient.BaseUrl,
                hedefSurum, DateTimeOffset.UtcNow.Add(omur ?? Omur).ToUnixTimeMilliseconds());
            var veri = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(b), EkAnahtar, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Yol, veri);
        }
        catch { /* bilet bırakılamazsa yalnız otomatik giriş olmaz; güncelleme etkilenmez */ }
    }

    /// <summary>Kurulum başlatılamadıysa bilet geçersizdir — hemen silinir.</summary>
    public static void Sil()
    {
        try { if (File.Exists(Yol)) File.Delete(Yol); } catch { }
    }

    /// <summary>
    /// Açılışta: bilet varsa okur ve HEMEN siler (tek kullanım). Geçerliyse (süresi dolmamış, bu sürüm için, kullanıcı hâlâ etkin)
    /// oturumu kurar ve sunucu jetonunu geri yükler; değilse null → normal giriş ekranı.
    /// </summary>
    public static Sonuc? Al(string calisanSurum)
    {
        if (!OperatingSystem.IsWindows()) return null;
        byte[] veri;
        try
        {
            if (!File.Exists(Yol)) return null;
            veri = File.ReadAllBytes(Yol);
        }
        catch { return null; }
        finally { Sil(); }
        try
        {
            var b = JsonSerializer.Deserialize<Bilet>(ProtectedData.Unprotect(veri, EkAnahtar, DataProtectionScope.CurrentUser));
            if (b is null) return null;
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > b.SonGecerlilikMs) return null;
            if (!string.Equals(b.HedefSurum, calisanSurum, StringComparison.Ordinal)) return null;
            var oturum = DesktopServices.Auth.CreateSessionForUser(b.CompanyId, b.UserId);
            if (oturum is null) return null;
            oturum.OperatingBranchId = b.TumSubeler ? null : b.SubeId;
            if (!string.IsNullOrWhiteSpace(b.Jeton) && !string.IsNullOrWhiteSpace(b.SunucuAdresi))
                ServerAuthClient.OturumuGeriYukle(b.Jeton!, b.SunucuAdresi!);
            return new Sonuc(oturum, b.Kullanici, b.SubeId, b.SubeAdi, b.TumSubeler);
        }
        catch { return null; }
    }
}
