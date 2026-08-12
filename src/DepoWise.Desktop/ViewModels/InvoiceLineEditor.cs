using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// G4-2 — Fatura formundaki TEK SATIR.
///
/// Ayrı bir üst düzey tip olarak durur (iç sınıf DEĞİL): Avalonia bağlama yolundaki tür
/// dönüşümü <c>Tip+IcTip</c> yazımını ayrıştıramıyor, bu yüzden satır şablonundan komut
/// çağırabilmek için dışarı alındı.
///
/// <b>Toplam hesabı SERVİSTEKİ fonksiyondur</b> (<see cref="InvoiceService.LineAmounts"/>) —
/// ekranda görünen tutar ile kaydedilen tutar AYNI koddan gelir, ayrışamaz.
/// </summary>
public sealed partial class InvoiceLineEditor : ObservableObject
{
    private readonly Action _changed;
    private readonly SessionContext _session;

    public InvoiceLineEditor(SessionContext session, Action changed)
    {
        _session = session;
        _changed = changed;
    }

    // ── Malzeme seçimi: ARANABİLİR (tüm malzemeler açılır listeye DOLDURULMAZ) ──
    // NEDEN: MaterialService sayfalıdır (PageRequest.MaxLimit = 200). Sabit bir açılır liste
    // 200'de SESSİZCE kesilirdi ve kullanıcı malzemesini bulamazdı. Bu yüzden aynı desen
    // kullanılır: yaz → sunucu tarafı arama → ilk 30 sonuç → seç.
    public ObservableCollection<InvoicesViewModel.Option> Results { get; } = new();
    [ObservableProperty] private string _materialSearch = "";
    [ObservableProperty] private string _materialText = "";

    [ObservableProperty] private string? _materialId;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _unit = "";
    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private decimal _discountRate;
    [ObservableProperty] private decimal _vatRate = 20m;
    [ObservableProperty] private decimal _withholdingRate;

    public string LineTotalText => $"Satır toplamı: {InvoiceService.LineAmounts(ToDto()).Total:0.00}";

    public NewInvoiceLine ToDto() => new(
        string.IsNullOrWhiteSpace(MaterialId) ? null : MaterialId,
        string.IsNullOrWhiteSpace(Description) ? null : Description,
        string.IsNullOrWhiteSpace(Unit) ? null : Unit,
        Quantity, UnitPrice, DiscountRate, VatRate, WithholdingRate);

    partial void OnMaterialSearchChanged(string value)
    {
        Results.Clear();
        var term = value?.Trim();
        if (string.IsNullOrEmpty(term)) return;
        try
        {
            var page = DesktopServices.Materials.List(_session,
                new DepoWise.Application.Common.PageRequest { Limit = 30 }, term);
            foreach (var m in page.Items) Results.Add(new InvoicesViewModel.Option(m.Id, $"{m.Code} — {m.Name}"));
        }
        catch { /* arama hatası satırı bozmaz; kullanıcı açıklama yazarak devam edebilir */ }
    }

    [RelayCommand]
    private void PickMaterial(InvoicesViewModel.Option? o)
    {
        if (o is null) return;
        MaterialId = o.Key;
        MaterialText = o.Label;
        MaterialSearch = "";
        Results.Clear();
    }

    /// <summary>Malzemeyi kaldırır — satır hizmet/masraf satırına döner (açıklama zorunlu olur).</summary>
    [RelayCommand]
    private void ClearMaterial()
    {
        MaterialId = null;
        MaterialText = "";
    }

    partial void OnQuantityChanged(decimal v) => Recalc();
    partial void OnUnitPriceChanged(decimal v) => Recalc();
    partial void OnDiscountRateChanged(decimal v) => Recalc();
    partial void OnVatRateChanged(decimal v) => Recalc();
    partial void OnWithholdingRateChanged(decimal v) => Recalc();

    private void Recalc() { OnPropertyChanged(nameof(LineTotalText)); _changed(); }
}
