using DepoWise.Web.Components;
using MudBlazor;

namespace DepoWise.Web.Services;

/// <summary>Her ekle/düzelt/sil/kaydet öncesi ortak onay penceresi.</summary>
public static class DialogExtensions
{
    // ═══ FAZ 4.2 (kullanıcı isteği 2026-09-06) — STANDART İŞLEM ONAYLARI ════════════════════════
    // Masaüstündeki ConfirmService.ConfirmEdit/Delete/CancelAsync ile AYNI metinler. İki platform
    // aynı cümleyi göstermeli (CLAUDE.md §4: işlevsel eşitlik).

    /// <summary>"Kaydı düzenlemek istediğinize emin misiniz?"</summary>
    public static Task<bool> ConfirmEdit(this IDialogService dialog, string? ek = null)
        => dialog.Confirm(Birlestir("Kaydı düzenlemek istediğinize emin misiniz?", ek), "Evet, Düzenle");

    /// <summary>"Kaydı silmek istediğinize emin misiniz?"</summary>
    public static Task<bool> ConfirmDelete(this IDialogService dialog, string? ek = null)
        => dialog.Confirm(Birlestir("Kaydı silmek istediğinize emin misiniz?", ek), "Evet, Sil", "Vazgeç", danger: true);

    /// <summary>"Kaydı iptal etmek istediğinize emin misiniz?"</summary>
    public static Task<bool> ConfirmCancelRecord(this IDialogService dialog, string? ek = null)
        => dialog.Confirm(Birlestir("Kaydı iptal etmek istediğinize emin misiniz?", ek), "Evet, İptal Et", "Vazgeç", danger: true);

    /// <summary>Önce standart cümle, sonra ekrana özel açıklama (kullanıcının istediği sıra).</summary>
    private static string Birlestir(string standart, string? ek)
        => string.IsNullOrWhiteSpace(ek) ? standart : standart + "\n\n" + ek!.Trim();

    public static async Task<bool> Confirm(this IDialogService dialog, string message,
        string okText = "Evet", string cancelText = "Vazgeç", bool danger = false)
    {
        var p = new DialogParameters
        {
            ["Message"] = message,
            ["OkText"] = okText,
            ["CancelText"] = cancelText,
            ["Color"] = danger ? Color.Error : Color.Primary,
        };
        var dlg = await dialog.ShowAsync<ConfirmDialog>("Onay", p,
            new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true });
        var res = await dlg.Result;
        return res is not null && !res.Canceled && res.Data is true;
    }

    /// <summary>
    /// B-4: İptal işlemleri için onay + GEREKÇE penceresi. Uyarı metnini gösterir ve gerekçeyi tek adımda alır.
    /// Dönüş: kullanıcı vazgeçtiyse <c>null</c>, onayladıysa boş olmayan gerekçe metni.
    /// </summary>
    public static async Task<string?> AskReason(this IDialogService dialog, string message,
        string title = "İptal", string label = "İptal gerekçesi",
        string okText = "İptal Et", string cancelText = "Vazgeç")
    {
        var p = new DialogParameters
        {
            ["Message"] = message,
            ["Label"] = label,
            ["OkText"] = okText,
            ["CancelText"] = cancelText,
        };
        var dlg = await dialog.ShowAsync<ReasonInputDialog>(title, p,
            new DialogOptions { MaxWidth = MaxWidth.ExtraSmall, FullWidth = true });
        var res = await dlg.Result;
        return res is not null && !res.Canceled && res.Data is string r && !string.IsNullOrWhiteSpace(r) ? r : null;
    }
}
