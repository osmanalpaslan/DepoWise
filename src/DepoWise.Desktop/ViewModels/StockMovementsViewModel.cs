using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Stok Hareketleri ekranındaki lokasyon seçeneği (tek seçim). Web'deki seçeneklerle AYNI üç anlam.</summary>
public sealed record MovementLocationPick(string Id, string Name);

/// <summary>
/// Stok Hareketleri — ayrı ekran (kullanıcı isteği 2026-08-05): tüm giriş/çıkış/transfer/sayım hareketleri,
/// tarih aralığı + metin araması. Salt-okunur (iptal Giriş-Çıkış ekranında kalır). Şube kapsamı ve yetki API/servis
/// katmanında (StockService.SearchMovements). Kod-arkası yok, MVVM.
///
/// <b>STK-10b-4:</b> ekran, Stok Hareketleri RAPORU ile AYNI filtre üretecini kullanır
/// (<c>StockMovementFilterSql</c>) → ekran ve rapor aynı satır kümesini verir. Bu artımda ayrıca
/// <b>lokasyon filtresi</b> eklendi: web'de vardı, masaüstünde YOKTU (parite eksiği, STK-10 envanteri).
/// Filtre SUNUCU/SQL tarafındadır — istemcide süzme YOK (B-1'in masaüstünde tekrarlanmaması için).
/// </summary>
public sealed partial class StockMovementsViewModel : ViewModelBase, IDeepLinkTarget
{
    private readonly SessionContext _session;

    /// <summary>"Tüm Şubeler" sanal seçeneği — servise GÖNDERİLMEZ, filtre yokluğu demektir.
    /// ⚠️ "📦 Atanmamış" (boş kimlik) ile AYNI ŞEY DEĞİLDİR (K-2).</summary>
    public const string AllLocationsId = "__all__";

    public ObservableCollection<StockMovementRow> Movements { get; } = new();

    /// <summary>Lokasyon seçenekleri: 🌐 Tüm Şubeler + firmanın depoları + 📦 Atanmamış.
    /// Kaynak YEREL veritabanıdır (çevrimdışı çalışır; ağ çağrısı yok).</summary>
    public ObservableCollection<MovementLocationPick> Locations { get; } = new();

    [ObservableProperty] private DateTimeOffset? _fromDate;
    [ObservableProperty] private DateTimeOffset? _toDate;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _status = "";
    /// <summary>Seçili lokasyon. null / "__all__" → filtre yok. Boş kimlik ("") → 📦 Atanmamış.</summary>
    [ObservableProperty] private MovementLocationPick? _selectedLocation;

    public bool HasRows => Movements.Count > 0;

    public StockMovementsViewModel(SessionContext session)
    {
        _session = session;
        LoadLocations();
        Load();
    }

    /// <summary>Lokasyon listesi YEREL veritabanından (internet gerekmez). Sonda ATANMAMIŞ kovası —
    /// Raporlar ekranındaki listeyle aynı sıra ve aynı etiketler.</summary>
    private void LoadLocations()
    {
        Locations.Add(new MovementLocationPick(AllLocationsId, "🌐 Tüm Şubeler"));
        try
        {
            foreach (var b in DesktopServices.Branches.List(_session))
                Locations.Add(new MovementLocationPick(b.Id, b.Name));
        }
        catch { }
        Locations.Add(new MovementLocationPick("", "📦 Atanmamış"));
        SelectedLocation = Locations[0];
    }

    [RelayCommand]
    private void Load()
    {
        Movements.Clear();
        long? from = FromDate is { } f ? new DateTimeOffset(f.Date, TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
        long? to = ToDate is { } t ? new DateTimeOffset(t.Date.AddDays(1).AddMilliseconds(-1), TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
        // 🔴 B-1: lokasyon SERVİSE (SQL'e) gider — bellekte süzülmez, LIMIT'ten ÖNCE uygulanır.
        IReadOnlyList<string>? locations =
            SelectedLocation is null || SelectedLocation.Id == AllLocationsId ? null : new[] { SelectedLocation.Id };
        try
        {
            foreach (var m in DesktopServices.Stock.SearchMovements(
                         _session, from, to, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
                         locations, null, null, 1000))
                Movements.Add(m);
            Status = Movements.Count == 0 ? "Seçilen ölçütlerde hareket yok." : $"{Movements.Count} hareket";
        }
        catch (Exception ex) { Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand]
    private void Clear()
    {
        FromDate = null; ToDate = null; Search = "";
        SelectedLocation = Locations.Count > 0 ? Locations[0] : null;   // 🌐 Tüm Şubeler
        Load();
    }

    /// <summary>Köprü (madde 5, 2026-08-06): İşlem Geçmişi "Kaydı Görüntüle" → bu ekrana gelip malzeme
    /// kodu/adıyla arama yapar (belge bazlı derin bağlantı yok; malzeme bağlamı yeterli).</summary>
    public void OpenEntity(string entityId)
    {
        Search = entityId;
        Load();
    }
}
