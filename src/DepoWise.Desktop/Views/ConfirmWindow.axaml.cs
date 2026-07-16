using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

/// <summary>Basit Türkçe onay penceresi (modal). Evet → true, Vazgeç → false.</summary>
public partial class ConfirmWindow : Window
{
    public ConfirmWindow() => InitializeComponent();

    public ConfirmWindow(string title, string message, string okText, string cancelText, bool danger)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MsgText")!.Text = message;
        var ok = this.FindControl<Button>("OkBtn")!;
        var cancel = this.FindControl<Button>("CancelBtn")!;
        ok.Content = okText;
        cancel.Content = cancelText;
        // cancelText boş → yalnız "Tamam" olan BİLGİ penceresi (boş buton gösterme).
        cancel.IsVisible = !string.IsNullOrEmpty(cancelText);
        if (danger) { ok.Classes.Remove("Primary"); ok.Classes.Add("Danger"); }
        ok.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
    }

    /// <summary>Açık pencerenin mesajını canlı günceller (ör. güncelleme paketi değişince yeni pencere açmadan).</summary>
    public void SetMessage(string message)
    {
        var t = this.FindControl<TextBlock>("MsgText");
        if (t is not null) t.Text = message;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
