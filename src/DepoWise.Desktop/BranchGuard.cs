using System.Threading.Tasks;
using DepoWise.Application.Security;

namespace DepoWise.Desktop;

/// <summary>
/// "Tüm Şubeler" modu koruması (kullanıcı kuralı, 2026-07-16) — web'deki DepoWise.Web.Services.BranchGuard'ın eşi.
///
/// Girişte şube yerine "Tüm Şubeler" seçildiğinde oturumun ÇALIŞMA şubesi yoktur (OperatingBranchId = null).
/// Bu modda malzeme/araç/stok gibi ŞUBE BAZLI ekranlarda kayıt açılırsa hareket şubesiz (branch_id NULL) düşer
/// ve hangi şantiyeye ait olduğu kaybolur. Bu yüzden YAZMA engellenir; kullanıcıdan çıkış yapıp ilgili şubeyi
/// seçerek girmesi istenir. OKUMA serbesttir (tüm şubeleri görmek bu modun amacıdır).
///
/// Not: Yetki sınırı DEĞİL, veri doğruluğu korumasıdır — kullanıcının o şubelere erişimi zaten var.
/// Yetki/tenant sınırları serviste (AccessControl + TenantAccessGuard) fail-closed uygulanır.
/// </summary>
public static class BranchGuard
{
    /// <summary>Oturum "Tüm Şubeler" modunda mı? (çalışma şubesi seçilmemiş)</summary>
    public static bool IsAllBranches(SessionContext session) => string.IsNullOrEmpty(session.OperatingBranchId);

    /// <summary>Şube bazlı ekranlarda gösterilen sabit uyarı metni (ekran üstü şerit).</summary>
    public const string Banner =
        "Şu an \"Tüm Şubeler\" modundasınız — kayıtları görebilirsiniz ama işlem yapamazsınız. " +
        "İşlem yapmak için çıkış yapıp ilgili şubeyi seçerek tekrar giriş yapın.";

    /// <summary>
    /// Yazma işleminden ÖNCE çağrılır. "Tüm Şubeler" modundaysa uyarı penceresi gösterir ve <c>false</c> döner
    /// (çağıran işlemi yapmadan çıkar). Şube seçiliyse <c>true</c> döner.
    /// </summary>
    public static async Task<bool> RequireBranchAsync(SessionContext session, string screenName)
    {
        if (!IsAllBranches(session)) return true;
        await ConfirmService.AskAsync(
            $"Şu an \"Tüm Şubeler\" modundasınız.\n\n" +
            $"{screenName} ekranındaki kayıtlar bir şubeye/şantiyeye ait olmalıdır. Hangi şubeye yazılacağı " +
            $"belli olmadığı için bu işleme izin verilmiyor.\n\n" +
            $"Lütfen çıkış yapıp giriş ekranından ilgili şubeyi seçerek tekrar giriş yapın.",
            "Şube Seçilmedi", "Tamam", "");
        return false;
    }
}
