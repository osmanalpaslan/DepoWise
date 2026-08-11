using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DepoWise.Desktop.Views;

namespace DepoWise.Desktop;

/// <summary>Türkçe modal onay penceresi yardımcısı. Owner = aktif MainWindow.</summary>
public static class ConfirmService
{
    public static async Task<bool> AskAsync(string message, string title = "Onay",
        string okText = "Evet", string cancelText = "Vazgeç", bool danger = false)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return false;

        return await AskAsync(desktop.MainWindow, message, title, okText, cancelText, danger);
    }

    /// <summary>Belirtilen pencerenin ÜZERİNDE onay sorar (owner). Bir modal pencerenin içinden çağrılırken
    /// gereklidir: MainWindow o an devre dışı olduğundan onay ona sahiplendirilirse arkada kalır (kullanıcı
    /// isteği 2026-07-19: çift-tık hızlı düzenle penceresinde Kaydet/Sil onayı).</summary>
    public static async Task<bool> AskAsync(Window owner, string message, string title = "Onay",
        string okText = "Evet", string cancelText = "Vazgeç", bool danger = false)
    {
        var win = new ConfirmWindow(title, message, okText, cancelText, danger);
        return await win.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// B-4: İptal işlemleri için onay + GEREKÇE penceresi. Uyarıyı gösterir ve gerekçeyi tek adımda alır.
    /// Dönüş: kullanıcı vazgeçtiyse <c>null</c>, onayladıysa boş olmayan gerekçe metni.
    /// </summary>
    public static async Task<string?> AskReasonAsync(string message, string title = "İptal",
        string label = "İptal gerekçesi", string okText = "Evet, İptal Et", string cancelText = "Vazgeç")
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return null;

        var win = new ReasonWindow(title, message, label, okText, cancelText);
        return await win.ShowDialog<string?>(desktop.MainWindow);
    }

    /// <summary>
    /// G6-04: PAROLA soran pencere (yeniden kimlik doğrulama). Çöp Kutusu gibi ikinci bir kapı isteyen
    /// ekranlar kullanır — web'deki "Parolanız → Çöp Kutusunu Aç" adımının masaüstü karşılığıdır.
    /// Dönüş: vazgeçildiyse <c>null</c>, aksi halde girilen parola (KIRPILMAZ).
    /// Parola burada yalnız taşınır; doğrulama çağıran taraftadır (<c>AuthService.VerifyUserPassword</c>).
    /// </summary>
    public static async Task<string?> AskPasswordAsync(string message, string title = "Parola Doğrulama",
        string label = "Parolanız", string okText = "Devam", string cancelText = "Vazgeç")
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return null;

        var win = new ReasonWindow(title, message, label, okText, cancelText,
            isPassword: true, errorText: "Parola boş olamaz.",
            helperText: "Parolanız yalnız bu işlemi doğrulamak için kullanılır, hiçbir yere kaydedilmez.");
        return await win.ShowDialog<string?>(desktop.MainWindow);
    }
}
