using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Requests;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Durum seçim öğesi (yalnız izin verilen sonraki durumlar listelenir).</summary>
public sealed record OpsStatusPick(RequestOperationStatus Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Ortak renk anahtarı (neutral/info/warning/primary/success/danger) → masaüstü rozet türü.
/// Web aynı anahtarı MudBlazor rengine eşler → iki platformda AYNI renk mantığı (şartname madde 16).</summary>
public sealed class OpsColorConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly OpsColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value as string switch
        {
            "success" => BadgeKind.Success,
            "warning" => BadgeKind.Warning,
            "danger" => BadgeKind.Danger,
            "info" or "primary" => BadgeKind.Info,
            _ => BadgeKind.Neutral,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// TALEP OPERASYONLARI ekranı (Faz 2, kullanıcı isteği 2026-08-08). Ana Depo ve Satın Alma birimleri kullanır.
/// Onaylanmış taleplerin operasyon sürecini yönetir: durum değiştirme (onaylı geçiş matrisi + yetki),
/// gönderim bilgileri (gönderen/gönderilecek şube, operasyon notu) ve işlem geçmişi.
///
/// FAZ 2 SINIRI: kısmi karşılama miktarları, alternatif malzeme, talebin bölünmesi, satın alma sipariş
/// detayları, teslim alan/şekli, dosya eki, bildirim ve otomatik stok hareketleri BU FAZDA YOK.
/// İş kuralları servistedir (RequestOperationsService); burada yalnız sunum.
/// </summary>
/// <summary>⭐ M-26: karşılama tablosunun düzenlenebilir satırı (miktar kullanıcıda; varsayılan = hemen gönderilebilir).</summary>
public sealed partial class KarsilamaSatiriVm : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public DepoWise.Infrastructure.Requests.TalepKalemDurumu Durum { get; }
    public string Ozet => Durum.Ozet;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private decimal _miktar;
    public KarsilamaSatiriVm(DepoWise.Infrastructure.Requests.TalepKalemDurumu d) { Durum = d; _miktar = d.Gonderilebilir; }
}

public sealed partial class RequestOperationsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    // ═══ M-26 (AR-GE 2026-10-18, F-071) — TALEPTEN TEK TIKLA KARŞILAMA ════════════════════════════════
    // Seçili onaylı talebin kalemleri: istenen / gönderilen / siparişte / kalan / kaynak depo stoğu. "Stoktan Gönder" gönderen
    // şubeden gidecek şubeye gerçek transfer yapar; "Sipariş Aç" kalan için talebe bağlı sipariş açar. Kurallar serviste.
    public ObservableCollection<KarsilamaSatiriVm> Karsilama { get; } = new();
    public ObservableCollection<ProjectPick> SupplierOptions { get; } = new();
    [ObservableProperty] private ProjectPick? _karsilamaTedarikci;
    [ObservableProperty] private string? _karsilamaOzeti;
    public bool HasKarsilama => Karsilama.Count > 0;
    /// <summary>Denetim B5+B6: karşılama düğmeleri yalnız düzenleme yetkisiyle görünür (web _canEdit ile aynı; asıl kapı serviste).</summary>
    public bool KarsilamaYapabilir => HasKarsilama && CanEdit;
    private string _gonderimIslemi = Guid.NewGuid().ToString("N");

    private void KarsilamaYukle()
    {
        Karsilama.Clear();
        KarsilamaOzeti = null;
        if (Selected is null) { OnPropertyChanged(nameof(HasKarsilama)); OnPropertyChanged(nameof(KarsilamaYapabilir)); return; }
        try
        {
            var d = DesktopServices.TalepKarsilama.Durum(_session, Selected.Id, FromBranch?.Id);
            foreach (var k in d.Kalemler) Karsilama.Add(new KarsilamaSatiriVm(k));
            KarsilamaOzeti = $"Karşılanma: %{d.Yuzde:0}" + (d.TamamiGonderildi ? " — tüm kalemler gönderildi." :
                string.IsNullOrEmpty(d.FromBranchId) ? " — kaynak depo stoğunu görmek için «Gönderen Şube» seçin." : "");
        }
        catch (Exception ex) { KarsilamaOzeti = "Karşılama bilgisi alınamadı: " + ex.Message; }
        OnPropertyChanged(nameof(HasKarsilama)); OnPropertyChanged(nameof(KarsilamaYapabilir));
    }

    partial void OnFromBranchChanged(BranchRow? value) => KarsilamaYukle();

    [RelayCommand]
    private async Task StoktanGonder()
    {
        if (Selected is null) return;
        var satirlar = Karsilama.Where(x => x.Miktar > 0)
            .Select(x => new DepoWise.Infrastructure.Requests.TalepKarsilamaSatiri(x.Durum.MaterialId, x.Miktar)).ToList();
        if (FromBranch is null || ToBranch is null) { Status = "Gönderen ve gidecek şubeyi seçin."; return; }
        if (satirlar.Count == 0) { Status = "Gönderilecek miktar girin."; return; }
        if (!await ConfirmService.AskAsync(
                $"{satirlar.Count} kalem {FromBranch.Name} → {ToBranch.Name} transfer edilsin mi? Stok kaynaktan düşer, hedefe girer.",
                "Stoktan Gönder", "Gönder", "Vazgeç")) return;
        try
        {
            var r = DesktopServices.TalepKarsilama.StoktanGonder(_session, Selected.Id, FromBranch.Id, ToBranch.Id, satirlar, _gonderimIslemi);
            _gonderimIslemi = Guid.NewGuid().ToString("N");   // başarılı gönderimden sonra yeni işlem kimliği
            Status = $"Transfer belgesi oluşturuldu: {r.DocNo}";
            KarsilamaYukle();
        }
        catch (Exception ex) { Status = "Gönderilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task SiparisAc()
    {
        if (Selected is null) return;
        // Siparişe yalnız stoktan karşılanamayan kalan gider: kullanıcı miktarı düzenleyebilir, boşsa kalanın tamamı.
        var satirlar = Karsilama.Where(x => x.Durum.Kalan > 0)
            .Select(x => new DepoWise.Infrastructure.Requests.TalepKarsilamaSatiri(x.Durum.MaterialId,
                x.Miktar > 0 && x.Miktar <= x.Durum.Kalan ? x.Miktar : x.Durum.Kalan)).ToList();
        if (satirlar.Count == 0) { Status = "Sipariş edilecek kalan yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"{satirlar.Count} kalem için satın alma siparişi açılsın mı?" + (KarsilamaTedarikci is null ? " (Tedarikçi seçilmedi; siparişte sonradan seçilebilir.)" : ""),
                "Sipariş Aç", "Sipariş Aç", "Vazgeç")) return;
        try
        {
            DesktopServices.TalepKarsilama.SiparisAc(_session, Selected.Id, KarsilamaTedarikci?.Id, satirlar, ToBranch?.Id);
            Status = "Satın alma siparişi açıldı (Satın Alma ekranında).";
            KarsilamaYukle();
        }
        catch (Exception ex) { Status = "Sipariş açılamadı: " + ex.Message; }
    }

    public ObservableCollection<RequestOperationRow> Items { get; } = new();
    public ObservableCollection<RequestItemRow> DetailItems { get; } = new();
    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<OpsStatusPick> NextStates { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();

    /// <summary>Durum filtresi — "Tümü" + 13 operasyon durumu (şartname sırası).</summary>
    public ObservableCollection<string> StatusFilters { get; } = new(
        new[] { "Tümü" }.Concat(RequestOperationStatusInfo.All.Select(RequestOperationStatusInfo.Label)));

    [ObservableProperty] private string _selectedStatusFilter = "Tümü";
    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanChangeStatus))]
    private RequestOperationRow? _selected;
    public bool HasSelection => Selected != null;

    // Form alanları (gönderim bilgileri — şartname §6)
    [ObservableProperty] private OpsStatusPick? _newStatus;
    [ObservableProperty] private BranchRow? _fromBranch;
    [ObservableProperty] private BranchRow? _toBranch;
    [ObservableProperty] private string _opsNote = "";

    public bool CanView => AccessControl.Can(_session, RequestOperationStateMachine.ModuleOps, PermissionAction.View);
    public bool CanEdit => AccessControl.Can(_session, RequestOperationStateMachine.ModuleOps, PermissionAction.Edit)
                           || AccessControl.IsAdmin(_session);
    public bool CanChangeStatus => HasSelection && CanEdit && NextStates.Count > 0;

    public RequestOperationsViewModel(SessionContext session)
    {
        _session = session;
        LoadBranches();
        try { foreach (var x in DesktopServices.Lookups.List(_session, "suppliers")) SupplierOptions.Add(new ProjectPick(x.Id, x.Name)); }
        catch { }
        Load();
    }

    partial void OnSelectedStatusFilterChanged(string value) => Load();

    private string? FilterDb => SelectedStatusFilter == "Tümü"
        ? null
        : RequestOperationStatusInfo.All
            .Where(x => RequestOperationStatusInfo.Label(x) == SelectedStatusFilter)
            .Select(RequestOperationStatusInfo.ToDb).FirstOrDefault();

    private void LoadBranches()
    {
        try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); }
        catch { }
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var r in DesktopServices.RequestOps.List(_session, FilterDb)) Items.Add(r);
            Status = $"{Items.Count} operasyon kaydı";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    partial void OnSelectedChanged(RequestOperationRow? value)
    {
        DetailItems.Clear();
        History.Clear();
        NextStates.Clear();
        NewStatus = null;
        if (value is null) { OnPropertyChanged(nameof(CanChangeStatus)); return; }
        try
        {
            foreach (var it in DesktopServices.Requests.GetItems(_session, value.Id)) DetailItems.Add(it);
            foreach (var h in DesktopServices.RequestOps.GetHistory(_session, value.Id)) History.Add(h.Line);
            foreach (var st in DesktopServices.RequestOps.AllowedNextStates(_session, value.Id))
                NextStates.Add(new OpsStatusPick(st, RequestOperationStatusInfo.Label(st)));
            // Mevcut gönderim bilgilerini forma yükle
            FromBranch = Branches.FirstOrDefault(b => b.Id == value.FromBranchId);
            ToBranch = Branches.FirstOrDefault(b => b.Id == value.ToBranchId);
            OpsNote = value.OpsNote ?? "";
        }
        catch (Exception ex) { Status = "Detay yüklenemedi: " + ex.Message; }
        KarsilamaYukle();   // ⭐ M-26
        OnPropertyChanged(nameof(CanChangeStatus));
    }

    /// <summary>Durumu değiştirir (gönderim bilgileriyle birlikte). İş kuralı/yetki servistedir.</summary>
    [RelayCommand]
    private async Task ApplyStatus()
    {
        if (Selected is null || NewStatus is null) { Status = "Önce kayıt ve yeni durum seçin."; return; }
        var target = NewStatus.Label;
        if (!await ConfirmService.AskAsync(
                $"'{Selected.DocNo}' talebinin operasyon durumu \"{target}\" olarak güncellensin mi?", "Durum Değiştir"))
            return;
        try
        {
            // KLT-01a: gönderim alanları da yazıldığı için (updateBranches:true) sürüm jetonu gönderilir.
            // Durum geçişinin kendisi zaten durum makinesiyle korunuyor.
            DesktopServices.RequestOps.ChangeStatus(_session, Selected.Id, NewStatus.Value,
                string.IsNullOrWhiteSpace(OpsNote) ? null : OpsNote.Trim(),
                FromBranch?.Id, ToBranch?.Id, updateBranches: true, expectedVersion: Selected.Version);
            Status = $"Durum güncellendi: {target}";
            var keepId = Selected.Id;
            Load();
            Selected = Items.FirstOrDefault(x => x.Id == keepId);   // seçim korunur (ekran atlamasın)
        }
        catch (ConcurrencyException) { HandleOpsConflict(); }
        catch (Exception ex) { Status = "Güncellenemedi: " + ex.Message; }
    }

    /// <summary>Yalnız gönderim bilgisi/notu kaydeder (durum değişmez).</summary>
    [RelayCommand]
    private void SaveShipment()
    {
        if (Selected is null) return;
        try
        {
            // KLT-01a: düzenleme kilidi — ekran açıldığındaki sürüm gönderilir.
            DesktopServices.RequestOps.UpdateShipmentInfo(_session, Selected.Id,
                FromBranch?.Id, ToBranch?.Id, string.IsNullOrWhiteSpace(OpsNote) ? null : OpsNote.Trim(),
                expectedVersion: Selected.Version);
            Status = "Gönderim bilgileri kaydedildi.";
            var keepId = Selected.Id;
            Load();
            Selected = Items.FirstOrDefault(x => x.Id == keepId);
        }
        catch (ConcurrencyException) { HandleOpsConflict(); }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    /// <summary>
    /// KLT-01a — DÜZENLEME KİLİDİ çakışması: talep, ekran açıldıktan sonra başkası tarafından
    /// değiştirilmiş. Değişiklik YAZILMADI. Liste tazelenir ki kullanıcı güncel hâli görsün ve
    /// eski veriyle tekrar denemesin (sürüm de böylece yenilenir).
    /// </summary>
    private void HandleOpsConflict()
    {
        var keepId = Selected?.Id;
        Load();
        Selected = Items.FirstOrDefault(x => x.Id == keepId);
        Status = "Bu talep siz ekranı açtıktan sonra başka bir kullanıcı tarafından değiştirildi. "
               + "Değişikliğiniz kaydedilmedi. Güncel bilgiler yeniden yüklendi; kontrol edip tekrar deneyin.";
    }
}
