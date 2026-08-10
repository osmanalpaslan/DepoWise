using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

/// <summary>B-4: Gerekçe soran modal pencere. Onay → girilen gerekçe (boş olamaz), Vazgeç/Esc → null.</summary>
public partial class ReasonWindow : Window
{
    public ReasonWindow() => InitializeComponent();

    public ReasonWindow(string title, string message, string label, string okText, string cancelText)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MsgText")!.Text = message;
        this.FindControl<TextBlock>("LabelText")!.Text = label;
        var box = this.FindControl<TextBox>("ReasonBox")!;
        var err = this.FindControl<TextBlock>("ErrText")!;
        var ok = this.FindControl<Button>("OkBtn")!;
        var cancel = this.FindControl<Button>("CancelBtn")!;
        ok.Content = okText;
        cancel.Content = cancelText;
        ok.Click += (_, _) =>
        {
            // Boş/yalnız boşluk kabul edilmez — servis katmanı da aynı kuralı uygular.
            if (string.IsNullOrWhiteSpace(box.Text)) { err.IsVisible = true; box.Focus(); return; }
            Close(box.Text.Trim());
        };
        cancel.Click += (_, _) => Close(null);
        Opened += (_, _) => box.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
