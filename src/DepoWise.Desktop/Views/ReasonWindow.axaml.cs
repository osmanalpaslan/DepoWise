using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

/// <summary>B-4: Gerekçe soran modal pencere. Onay → girilen metin (boş olamaz), Vazgeç/Esc → null.
/// G6-04 (2026-08-11): aynı pencere PAROLA sorma kipini de destekler (<paramref name="isPassword"/>) —
/// Çöp Kutusu'nun ikinci doğrulama kapısı için. Yeni parametreler SONA eklendi → mevcut çağrılar (yakıt /
/// talep / muayene iptali) değişmeden çalışır.</summary>
public partial class ReasonWindow : Window
{
    public ReasonWindow() => InitializeComponent();

    public ReasonWindow(string title, string message, string label, string okText, string cancelText,
        bool isPassword = false, string? errorText = null, string? helperText = null)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBlock>("MsgText")!.Text = message;
        this.FindControl<TextBlock>("LabelText")!.Text = label;
        var box = this.FindControl<TextBox>("ReasonBox")!;
        var err = this.FindControl<TextBlock>("ErrText")!;
        var ok = this.FindControl<Button>("OkBtn")!;
        var cancel = this.FindControl<Button>("CancelBtn")!;
        if (errorText is not null) err.Text = errorText;
        if (helperText is not null) this.FindControl<TextBlock>("HelperText")!.Text = helperText;
        if (isPassword)
        {
            // Parola kipi: karakterler maskelenir, tek satır, ekranda yer kaplamayan kısa kutu.
            // Buton "tehlike" değil onay rengindedir (silme değil, kapı açma işlemi).
            box.PasswordChar = '●';
            box.Height = 40;
            box.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
            box.PlaceholderText = "Parolanız";
            ok.Classes.Remove("Danger");
            ok.Classes.Add("Primary");
        }
        ok.Content = okText;
        cancel.Content = cancelText;
        ok.Click += (_, _) =>
        {
            // Boş/yalnız boşluk kabul edilmez — servis katmanı da aynı kuralı uygular.
            if (string.IsNullOrWhiteSpace(box.Text)) { err.IsVisible = true; box.Focus(); return; }
            // Parola KIRPILMAZ: baştaki/sondaki boşluk parolanın parçası olabilir (gerekçe metni kırpılır).
            Close(isPassword ? box.Text : box.Text.Trim());
        };
        cancel.Click += (_, _) => Close(null);
        Opened += (_, _) => box.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
