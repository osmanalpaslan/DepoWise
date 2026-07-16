using MudBlazor;

namespace DepoWise.Web.Services;

/// <summary>
/// "Tüm Şubeler" modu koruması (kullanıcı kuralı, 2026-07-16).
///
/// Girişte şube yerine "🌐 Tüm Şubeler" seçildiğinde oturumun çalışma şubesi YOKTUR (BranchId = null).
/// Bu modda malzeme/araç/stok gibi ŞUBE BAZLI ekranlarda kayıt açılırsa hareket şubesiz (branch_id NULL)
/// düşer ve hangi şantiyeye ait olduğu kaybolur. Bu yüzden bu modda YAZMA işlemleri engellenir; kullanıcıdan
/// çıkış yapıp ilgili şubeyi seçerek girmesi istenir. OKUMA serbesttir (tüm şubeleri görmek bu modun amacı).
///
/// Not: Bu bir yetki sınırı DEĞİL, veri doğruluğu korumasıdır — kullanıcının o şubelere erişimi zaten var.
/// Yetki/tenant sınırları sunucuda (AccessControl + TenantAccessGuard) fail-closed uygulanır.
/// </summary>
public static class BranchGuard
{
    /// <summary>Oturum "Tüm Şubeler" modunda mı? (çalışma şubesi seçilmemiş)</summary>
    public static bool IsAllBranches(this AuthState auth) => string.IsNullOrEmpty(auth.BranchId);

    /// <summary>Şube bazlı ekranlarda gösterilen sabit uyarı metni (ekran üstü şerit).</summary>
    public const string Banner =
        "Şu an \"Tüm Şubeler\" modundasınız — kayıtları görebilirsiniz ama işlem yapamazsınız. " +
        "İşlem yapmak için çıkış yapıp ilgili şubeyi seçerek tekrar giriş yapın.";

    /// <summary>
    /// Yazma işleminden ÖNCE çağrılır. "Tüm Şubeler" modundaysa uyarı penceresi gösterir ve <c>false</c> döner
    /// (çağıran işlemi yapmadan çıkar). Şube seçiliyse <c>true</c> döner.
    /// </summary>
    public static async Task<bool> RequireBranchAsync(this IDialogService dialog, AuthState auth, string screenName)
    {
        if (!auth.IsAllBranches()) return true;
        await dialog.Confirm(
            $"Şu an \"Tüm Şubeler\" modundasınız.\n\n" +
            $"{screenName} ekranındaki kayıtlar bir şubeye/şantiyeye ait olmalıdır. Hangi şubeye yazılacağı " +
            $"belli olmadığı için bu işleme izin verilmiyor.\n\n" +
            $"Lütfen çıkış yapıp giriş ekranından ilgili şubeyi seçerek tekrar giriş yapın.",
            okText: "Tamam", cancelText: "");
        return false;
    }
}
