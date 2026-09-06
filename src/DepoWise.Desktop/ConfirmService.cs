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

    // ═══ FAZ 4.2 (kullanıcı isteği 2026-09-06) — STANDART İŞLEM ONAYLARI ════════════════════════
    //
    // Kullanıcı: "bütün düzenleme ve silme butonlarında işlem yapılmadan önce ... 'Kaydı düzenlemek
    // istediğinize emin misiniz?', silme ise 'Kaydı silmek istediğinize emin misiniz?' tarzında
    // uyarılar vermeli. ... eğer farklı koşullar sebebiyle eklenen mesajlar var ise ÖNCE belirttiğim
    // mesajı verip SONRA diğer koşulların mesajı çıkmalı."
    //
    // Metin TEK YERDE tutulur ki 30+ ekranda birbirinden farklı cümleler oluşmasın. Zaten kendi
    // onayı olan butonlara DOKUNULMAZ (mükerrer uyarı kullanıcıyı yorar — kullanıcının şartı).

    /// <summary>"Kaydı düzenlemek istediğinize emin misiniz?" — düzenleme formunu açmadan önce.</summary>
    public static Task<bool> ConfirmEditAsync(string? ek = null)
        => AskAsync(Birlestir("Kaydı düzenlemek istediğinize emin misiniz?", ek), "Düzenle", "Evet, Düzenle", "Vazgeç");

    /// <summary>"Kaydı silmek istediğinize emin misiniz?" — geri alınamaz işlem (kırmızı).</summary>
    public static Task<bool> ConfirmDeleteAsync(string? ek = null)
        => AskAsync(Birlestir("Kaydı silmek istediğinize emin misiniz?", ek), "Sil", "Evet, Sil", "Vazgeç", danger: true);

    /// <summary>"Kaydı iptal etmek istediğinize emin misiniz?" — iptal/ters kayıt öncesi.</summary>
    public static Task<bool> ConfirmCancelAsync(string? ek = null)
        => AskAsync(Birlestir("Kaydı iptal etmek istediğinize emin misiniz?", ek), "İptal", "Evet, İptal Et", "Vazgeç", danger: true);

    /// <summary>Standart cümle + (varsa) ekrana özel açıklama — kullanıcının istediği sıra: önce genel, sonra özel.</summary>
    private static string Birlestir(string standart, string? ek)
        => string.IsNullOrWhiteSpace(ek) ? standart : standart + "\n\n" + ek!.Trim();

    /// <summary>
    /// ⭐ FAZ 4.13 (kullanıcı bulgusu 2026-09-06) — BİLGİ PENCERESİ (TEK BUTON).
    ///
    /// Bulunan hata: bilgi amaçlı pencereler <c>AskAsync(..., "Tamam", "Tamam")</c> ile açılıyordu;
    /// ok ve cancel metni aynı olduğu için ekranda <b>iki adet "Tamam"</b> görünüyordu (kullanıcı
    /// manuel senkron penceresinde bildirdi). Beş ayrı çağrıda aynı hata vardı.
    ///
    /// Bu metot cancel butonunu HİÇ ÇİZDİRMEZ (<c>cancelText: ""</c>), böylece aynı hata bir daha
    /// yazılamaz. Soru soran pencereler <see cref="AskAsync(string,string,string,string,bool)"/>
    /// kullanmaya devam eder.
    /// </summary>
    public static Task InfoAsync(string message, string title = "Bilgi", string okText = "Tamam", bool danger = false)
        => AskAsync(message, title, okText, "", danger);

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
