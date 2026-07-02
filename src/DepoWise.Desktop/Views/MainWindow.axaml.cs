using Avalonia.Controls;

namespace DepoWise.Desktop.Views;

public partial class MainWindow : Window
{
    private bool _confirmedClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Uygulama kapatılırken onay iste (kazara kapatmayı engeller).</summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_confirmedClose) { base.OnClosing(e); return; }
        e.Cancel = true;
        var ok = await ConfirmService.AskAsync(
            "Uygulamayı kapatmak istediğinize emin misiniz?", "Uygulamadan Çık",
            "Evet, Kapat", "Vazgeç");
        if (ok)
        {
            _confirmedClose = true;
            Close();
        }
    }
}
