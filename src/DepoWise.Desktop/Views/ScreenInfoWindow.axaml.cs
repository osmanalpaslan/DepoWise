using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;

namespace DepoWise.Desktop.Views;

public partial class ScreenInfoWindow : Window
{
    private readonly string _body = "";

    public ScreenInfoWindow() => AvaloniaXamlLoader.Load(this);

    public ScreenInfoWindow(string title, string body) : this()
    {
        _body = body;
        this.FindControl<TextBlock>("TitleText")!.Text = title;
        this.FindControl<TextBox>("InfoBox")!.Text = body;
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();
        this.FindControl<Button>("CopyBtn")!.Click += async (_, _) =>
        {
            var clip = GetTopLevel(this)?.Clipboard;
            if (clip is not null)
            {
                await clip.SetTextAsync(_body);
                this.FindControl<TextBlock>("CopiedText")!.IsVisible = true;
            }
        };
    }
}
