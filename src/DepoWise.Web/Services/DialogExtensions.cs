using DepoWise.Web.Components;
using MudBlazor;

namespace DepoWise.Web.Services;

/// <summary>Her ekle/düzelt/sil/kaydet öncesi ortak onay penceresi.</summary>
public static class DialogExtensions
{
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
}
