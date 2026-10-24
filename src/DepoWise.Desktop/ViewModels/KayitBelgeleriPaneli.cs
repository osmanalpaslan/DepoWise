using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ⭐ M-56 (AR-GE 2026-10-24, F-090): KAYIT KARTINDA "BELGELER" — seçili malzeme/araç/ekipman/personele bağlı evraklar kartın içinde
/// görünür ve indirilir (önceden yalnız ayrı Belgeler ekranındaydı). Belgeler SUNUCU-OTORİTELİ (senkron paketinde taşınmaz):
/// liste ve dosya web ile aynı uçlardan, oturum jetonuyla okunur. Yetki sunucuda (belgenin bağlı olduğu kaydın ekran yetkisi).
/// Ekranlar yalnız <see cref="YukleAsync"/> çağırır; görünüm ortak <c>Controls/KayitBelgeleri.axaml</c>.
/// </summary>
public sealed partial class KayitBelgeleriPaneli : ObservableObject
{
    public ObservableCollection<BelgeSatiri> Items { get; } = new();
    [ObservableProperty] private string? _durum;
    public bool HasItems => Items.Count > 0;
    public string Baslik => $"BELGELER ({Items.Count})";
    private int _istek;

    private string _tur = "";
    private string? _id;
    public bool KayitVar => !string.IsNullOrEmpty(_id);

    /// <summary>Kayıt seçilince çağrılır: yalnız HEDEFİ ayarlar ve paneli temizler — sunucuya GİTMEZ. Belgeler kullanıcı
    /// "Belgeleri Göster" deyince istenir (her tıklamada ağ isteği olmasın; çevrimdışı kullanımda ve arayüz testlerinde
    /// kayıt seçimi beklemesin — 2026-10-25 UIT10 kilitlenmesinin kök nedeni buydu).</summary>
    public Task YukleAsync(string entityType, string? entityId)
    {
        ++_istek;
        _tur = entityType; _id = entityId;
        Items.Clear(); Durum = null; Bildir(); OnPropertyChanged(nameof(KayitVar));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Goster()
    {
        if (string.IsNullOrEmpty(_id)) return;
        var no = ++_istek;
        Durum = "Belgeler sunucudan alınıyor…";
        var list = await OrgServerClient.ListDocumentsAsync(_tur, null, _id);
        if (no != _istek) return;   // kullanıcı bu arada başka kayda geçti
        Items.Clear();
        if (list is null) { Durum = "Belgeler sunucudan okunur; şu anda sunucuya ulaşılamıyor."; Bildir(); return; }
        foreach (var d in list) Items.Add(new BelgeSatiri(d, GecerlilikMetni(d)));
        Durum = list.Count == 0 ? "Bu kayda bağlı belge yok — Belgeler ekranından eklenebilir." : null;
        Bildir();
    }

    private void Bildir() { OnPropertyChanged(nameof(HasItems)); OnPropertyChanged(nameof(Baslik)); }

    /// <summary>Belgenin geçerlilik bilgisi (süresi dolan evrak kartta fark edilsin).</summary>
    public static string GecerlilikMetni(OrgServerClient.DocumentItem d)
    {
        if (d.ValidUntil is not { } bit) return "";
        var gun = DateTimeOffset.FromUnixTimeMilliseconds(bit).UtcDateTime.ToString("dd.MM.yyyy");
        return bit < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ? $"SÜRESİ DOLDU ({gun})" : $"geçerli: {gun}";
    }

    [RelayCommand]
    private async Task Indir(BelgeSatiri? satir)
    {
        if (satir is null) return;
        var d = satir.Belge;
        var hedef = await FilePickerService.SaveAnyAsync(d.FileName);
        if (hedef is null) return;
        var bytes = await OrgServerClient.DownloadDocumentAsync(d.Id);
        if (bytes is null) { Durum = "Dosya indirilemedi (sunucuya ulaşılamıyor veya yetki yok)."; return; }
        try { await System.IO.File.WriteAllBytesAsync(hedef, bytes); Durum = $"İndirildi: {hedef}"; }
        catch (Exception ex) { Durum = "Kaydedilemedi: " + ex.Message; }
    }
}

/// <summary>⭐ M-56: panel satırı (belge + geçerlilik metni).</summary>
public sealed record BelgeSatiri(OrgServerClient.DocumentItem Belge, string Gecerlilik)
{
    public string Title => Belge.Title;
    public string FileName => Belge.FileName;
    public string Alt => string.Join(" · ", new[] { Belge.DocType, Gecerlilik }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
