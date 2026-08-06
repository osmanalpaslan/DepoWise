using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

/// <summary>
/// İşlem Geçmişi kaydı detayı (madde 5, kullanıcı isteği 2026-08-06): çift-tıkla açılan, TAMAMEN SALT-OKUNUR
/// pencere. "Kaydı Görüntüle" verilmişse tıklanınca <paramref name="onOpenRecord"/> çalışır ve bu pencere
/// kapanır (çağıran taraf gerçek ekrana yönlendirir + kendi penceresini de kapatabilir).
/// </summary>
public partial class HistoryDetailWindow : Window
{
    public HistoryDetailWindow() => InitializeComponent();

    public HistoryDetailWindow(string title, string dateText, string kindText, string? detailText,
        Action? onOpenRecord = null, string openRecordLabel = "Kaydı Görüntüle")
    {
        InitializeComponent();
        this.FindControl<SelectableTextBlock>("TitleText")!.Text = title;
        this.FindControl<SelectableTextBlock>("DateText")!.Text = dateText;
        this.FindControl<SelectableTextBlock>("KindText")!.Text = kindText;

        if (!string.IsNullOrWhiteSpace(detailText))
        {
            this.FindControl<SelectableTextBlock>("DetailText")!.Text = detailText;
            this.FindControl<StackPanel>("DetailPanel")!.IsVisible = true;
        }

        if (onOpenRecord is not null)
        {
            var openBtn = this.FindControl<Button>("OpenBtn")!;
            openBtn.Content = openRecordLabel;
            openBtn.IsVisible = true;
            openBtn.Click += (_, _) => { Close(); onOpenRecord(); };
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
