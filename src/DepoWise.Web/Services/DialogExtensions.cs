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

    // ⭐ M-69 (AR-GE 2026-10-26, yerel tarayıcı testi bulgusu): "Kaydet"e ÇİFT tıklanınca iki onay penceresi üst üste açılıyordu
    // (ilk tıklama onayı beklerken ikinci tıklama da işleyiciye giriyordu); ikisi de onaylanırsa kayıt İKİ KEZ yazılabilirdi.
    // Kural: aynı oturumda (IDialogService devre başınadır) bir onay/gerekçe penceresi AÇIKKEN yeni onay istenirse açılmaz ve
    // "hayır" sayılır → ikinci tıklama sessizce yok sayılır. Sıralı onaylar (biri kapanınca diğeri) etkilenmez.
    private sealed class AcikPencere { public bool Var; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IDialogService, AcikPencere> _acik = new();

    private static bool Kilitle(IDialogService dialog)
    {
        var d = _acik.GetValue(dialog, _ => new AcikPencere());
        lock (d) { if (d.Var) return false; d.Var = true; return true; }
    }
    private static void Birak(IDialogService dialog)
    {
        if (_acik.TryGetValue(dialog, out var d)) lock (d) d.Var = false;
    }

    public static async Task<bool> Confirm(this IDialogService dialog, string message,
        string okText = "Evet", string cancelText = "Vazgeç", bool danger = false)
    {
        if (!Kilitle(dialog)) return false;   // zaten açık bir onay var (çift tıklama) → yok say
        try { return await ConfirmCore(dialog, message, okText, cancelText, danger); }
        finally { Birak(dialog); }
    }

    private static async Task<bool> ConfirmCore(IDialogService dialog, string message, string okText, string cancelText, bool danger)
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
        if (!Kilitle(dialog)) return null;   // zaten açık bir pencere var (çift tıklama) → yok say
        try { return await AskReasonCore(dialog, message, title, label, okText, cancelText); }
        finally { Birak(dialog); }
    }

    private static async Task<string?> AskReasonCore(IDialogService dialog, string message, string title, string label,
        string okText, string cancelText)
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
