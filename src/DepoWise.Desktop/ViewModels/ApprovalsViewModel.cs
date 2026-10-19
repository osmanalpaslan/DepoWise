using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>"Onaylamalarım" satırı — sunucudan gelen projeksiyon (yerelde onay verisi YOKTUR).</summary>
/// ⭐ M-33: <see cref="Talep"/> = zincirsiz bekleyen talep (StepId = talep kimliği; karar Talep Onaylama kapısından geçer).
public sealed partial class OnayIsiSatiri : ObservableObject
{
    public OnayIsiSatiri(string stepId, string entityLabel, string docNo, string stepLabel, string tarih, string entityType, bool talep = false)
    { StepId = stepId; EntityLabel = entityLabel; DocNo = docNo; StepLabel = stepLabel; Tarih = tarih; EntityType = entityType; Talep = talep; }
    public string StepId { get; }
    public string EntityLabel { get; }
    public string DocNo { get; }
    public string StepLabel { get; }
    public string Tarih { get; }
    public string EntityType { get; }
    public bool Talep { get; }
    /// <summary>Toplu karar için işaret.</summary>
    [ObservableProperty] private bool _secili;
}

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 3 (ADR-187/188/189) — ONAYLAMALARIM (masaüstü) ═══
///
/// <b>Bu ekran bir onay MOTORU DEĞİLDİR</b> — mevcut sunucu motorunun (ALT FAZ 2) üstünde çalışan
/// ince bir arayüzdür. Yerel <c>approval_instance</c>/<c>approval_step</c> tablolarına HİÇ dokunmaz;
/// bu tablolar masaüstüne zaten senkronlanmaz (PK-EK-05 / İK-9).
///
/// <b>ÇEVRİMDIŞI (İK-9):</b> onay verisi sunucudadır → çevrimdışıyken liste de gelmez, onay/ret de
/// yapılamaz. Her iki durumda kullanıcıya <b>açık uyarı</b> gösterilir ve <b>hiçbir yerel kayıt
/// yazılmaz</b>, <c>sync_outbox</c>'a hiçbir şey düşmez. Çevrimdışı onay kuyruğu YOKTUR.
///
/// <b>EŞZAMANLILIK (§9):</b> UI tarafında kilit/ön-kontrol YAPILMAZ. Aynı adımı iki kişi onaylarsa
/// kararı sunucudaki atomik geçiş verir; kaybeden tarafa sunucunun mesajı gösterilir ve liste tazelenir.
///
/// <b>Yetki:</b> ekran mevcut <c>request_approval</c> modülüne bağlıdır (yeni modül YOK). Listede
/// satır görünmesi onaylama yetkisi anlamına GELMEZ — gerçek kapı sunucudadır.
/// </summary>
public sealed partial class ApprovalsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ApprovalsViewModel(SessionContext session)
    {
        _session = session;
        Items = new ObservableCollection<OnayIsiSatiri>();
        _ = YenileAsync();
    }

    public ObservableCollection<OnayIsiSatiri> Items { get; }
    /// <summary>⭐ M-33: seçili belgenin kalemleri (ekran değiştirmeden karar).</summary>
    public ObservableCollection<string> Kalemler { get; } = new();
    [ObservableProperty] private string _kalemOzeti = "";

    async partial void OnSelectedChanged(OnayIsiSatiri? value)
    {
        Kalemler.Clear();
        KalemOzeti = "";
        if (value is null) return;
        var (ok, mesaj, rows) = value.Talep
            ? await OnlineApprovalClient.RequestItemsAsync(value.StepId)
            : await OnlineApprovalClient.StepItemsAsync(value.StepId);
        if (!ok) { KalemOzeti = mesaj; return; }
        decimal toplam = 0; var tutarVar = false;
        foreach (var r in rows)
        {
            if (value.Talep)
                Kalemler.Add($"{Metin(r, "materialName")} · {(r.TryGetProperty("quantity", out var q) ? q.ToString() : "")}");
            else
            {
                Kalemler.Add(Metin(r, "line"));
                if (r.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number) { toplam += a.GetDecimal(); tutarVar = true; }
            }
        }
        KalemOzeti = $"{Kalemler.Count} kalem" + (tutarVar ? $" · tahmini toplam {toplam:N2}" : "");
    }

    /// <summary>Seçilenleri (işaretli yoksa seçili satırı) toplu onaylar / reddeder. Sonuç satır satır raporlanır.</summary>
    [RelayCommand]
    private async Task TopluOnaylaAsync() => await TopluAsync(true);

    [RelayCommand]
    private async Task TopluReddetAsync()
    {
        if (string.IsNullOrWhiteSpace(RejectReason)) { Status = "Ret gerekçesi zorunlu."; return; }
        await TopluAsync(false);
    }

    private async Task TopluAsync(bool onay)
    {
        var secilen = Items.Where(x => x.Secili).ToList();
        if (secilen.Count == 0) { Status = "Toplu işlem için listeden kutucuk işaretleyin."; return; }
        if (!await ConfirmService.AskAsync($"{secilen.Count} kayıt {(onay ? "ONAYLANSIN" : "REDDEDİLSİN")} mı?", "Toplu Karar",
                onay ? "Onayla" : "Reddet", "Vazgeç", danger: !onay)) return;
        Busy = true;
        int basarili = 0; var hatalar = new System.Collections.Generic.List<string>();
        try
        {
            foreach (var grup in secilen.GroupBy(x => x.Talep))
            {
                var (ok, mesaj, rows) = await OnlineApprovalClient.BulkAsync(grup.Key, grup.Select(x => x.StepId).ToArray(), onay,
                    onay ? null : RejectReason.Trim());
                if (!ok) { hatalar.Add(mesaj); continue; }
                foreach (var r in rows)
                {
                    if (r.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True) basarili++;
                    else hatalar.Add($"{grup.First(x => x.StepId == Metin(r, "id")).DocNo}: {Metin(r, "error")}");
                }
            }
            if (!onay && basarili > 0) RejectReason = "";
            Status = $"{basarili} kayıt {(onay ? "onaylandı" : "reddedildi")}." + (hatalar.Count > 0 ? " İşlenemeyenler: " + string.Join(" · ", hatalar) : "");
        }
        finally { Busy = false; }
        await YenileAsync();
    }

    [ObservableProperty] private OnayIsiSatiri? _selected;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _rejectReason = "";
    [ObservableProperty] private bool _busy;

    /// <summary>Ekranı görüntüleme yetkisi (asıl kapılar sunucuda).</summary>
    public bool CanView => AccessControl.Can(_session, "request_approval", PermissionAction.View);

    [RelayCommand]
    public async Task YenileAsync()
    {
        if (!CanView) { Status = "Onay ekranını görüntüleme yetkiniz bulunmuyor."; return; }
        Busy = true;
        try
        {
            var (ok, mesaj, rows) = await OnlineApprovalClient.MineAsync();
            Items.Clear();
            if (!ok) { Status = mesaj; return; }

            foreach (var r in rows) Items.Add(Satir(r));
            // ⭐ M-33: zincirsiz bekleyen talepler de buraya gelir (tek onay merkezi). Yetki yoksa liste sessizce boş kalır.
            if (AccessControl.Can(_session, "request_approval", PermissionAction.Edit) && AccessControl.Can(_session, "requests", PermissionAction.View))
            {
                var (tOk, _, talepler) = await OnlineApprovalClient.PendingRequestsAsync();
                if (tOk)
                    foreach (var t in talepler)
                        Items.Add(new OnayIsiSatiri(Metin(t, "id"), "Malzeme Talebi", Metin(t, "docNo"), "Tek adım",
                            TarihMetni(t, "requestDate"), "material_request", talep: true));
            }
            Selected = Items.Count > 0 ? Items[0] : null;
            Status = Items.Count == 0 ? "Onayınızı bekleyen kayıt yok." : "";
        }
        finally { Busy = false; }
    }

    private static OnayIsiSatiri Satir(JsonElement r) => new(
        Metin(r, "stepId"),
        Metin(r, "entityLabel"),
        Metin(r, "docNo"),
        Metin(r, "stepLabel"),
        TarihMetni(r, "entityDate"),
        Metin(r, "entityType"));

    private static string Metin(JsonElement r, string alan)
        => r.TryGetProperty(alan, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>İŞ GÜNÜ tarihi (ADR-184: kayıt zaman damgası değil) — GG/AA/YYYY.</summary>
    private static string TarihMetni(JsonElement r, string alan)
    {
        if (!r.TryGetProperty(alan, out var v) || v.ValueKind != JsonValueKind.Number) return "";
        if (!v.TryGetInt64(out var ms)) return "";
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("dd/MM/yyyy");
    }

    [RelayCommand]
    private async Task ApproveAsync()
        => await KararAsync(id => Selected!.Talep ? OnlineApprovalClient.ApproveAsync(id) : OnlineApprovalClient.ApproveStepAsync(id), "Onaylandı.",
            gerekceGerekli: false);

    [RelayCommand]
    private async Task RejectAsync()
    {
        if (string.IsNullOrWhiteSpace(RejectReason)) { Status = "Ret gerekçesi zorunlu."; return; }
        var gerekce = RejectReason.Trim();
        await KararAsync(id => Selected!.Talep ? OnlineApprovalClient.RejectAsync(id, gerekce) : OnlineApprovalClient.RejectStepAsync(id, gerekce), "Reddedildi.",
            gerekceGerekli: true);
    }

    /// <summary>Karar akışı — SUNUCU otoritesinde. Başarısızlıkta yerelde hiçbir değişiklik olmaz.</summary>
    private async Task KararAsync(Func<string, Task<(bool Ok, string Message)>> islem, string ok, bool gerekceGerekli)
    {
        if (Selected is null) { Status = "Bir kayıt seçin."; return; }
        if (!await ConfirmService.AskAsync(
                $"\"{Selected.DocNo}\" ({Selected.EntityLabel}) için işlemi onaylıyor musunuz?", "Onay")) return;

        Busy = true;
        try
        {
            var (basarili, mesaj) = await islem(Selected.StepId);
            if (!basarili) { Status = mesaj; return; }   // çevrimdışı/sunucu reddi → yerelde HİÇBİR yazım yok
            if (gerekceGerekli) RejectReason = "";
            Status = ok;
        }
        finally { Busy = false; }

        await YenileAsync();   // karar sonrası liste tazelenir (adım listeden düşer)
    }
}
