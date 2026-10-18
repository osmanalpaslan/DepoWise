using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Files;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Yedek Yönetimi — yedek al (VACUUM INTO), listele, geri yükle (yeniden başlat gerekir), klasörü aç.
/// Otomatik günlük yedek ShellViewModel.MaybeDailyBackupAsync ile alınır; 30 gün rotasyon BackupService'te.
/// </summary>
public sealed partial class BackupViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "backup", PermissionAction.Create);

    public ObservableCollection<BackupRowVm> Items { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    [ObservableProperty] private BackupRowVm? _selected;

    // ═══ FOTOĞRAFLARI SUNUCUYA TAŞI (kullanıcı isteği 2026-09-02) ════════════════════════════════
    //
    // Kullanıcı: "bir makinede yüklenen fotoğrafı her makine hem masaüstünde hem webte görebilmeli."
    // Kök neden: ADR-182 ÖNCESİ eklenen fotoğraflar YALNIZ ekleyen makinenin diskinde duruyor. ADR-182
    // sonrası taşıma yalnız İLGİLİ KAYIT O MAKİNEDE AÇILDIĞINDA çalışıyor. Onlarca aracı tek tek açmak
    // pratik değil (canlı ölçüm: sunucuda yalnız 8 araç fotoğrafı vardı).
    //
    // Bu düğme, BU MAKİNEDEKİ tüm yerel fotoğrafları sunucuya taşır. YALNIZ EKLEME yapar; hiçbir yerel
    // dosya veya kayıt silinmez. Zaten sunucuda olan (aynı içerik özetli) dosya atlanır → tekrar
    // çalıştırmak zararsızdır. Bu yüzden fotoğrafı OLAN makinede çalıştırılmalıdır.

    [ObservableProperty] private bool _photoBusy;
    [ObservableProperty] private string? _photoStatus;
    public bool HasPhotoStatus => !string.IsNullOrEmpty(PhotoStatus);

    partial void OnPhotoStatusChanged(string? value) => OnPropertyChanged(nameof(HasPhotoStatus));

    [RelayCommand]
    private async Task UploadPhotosToServer()
    {
        if (PhotoBusy) return;
        if (!await ConfirmService.AskAsync(
                "Bu bilgisayardaki tüm araç ve malzeme fotoğrafları sunucuya yüklenecek.\n\n" +
                "• Hiçbir fotoğraf SİLİNMEZ; yalnız sunucuda olmayanlar eklenir.\n" +
                "• Zaten sunucuda olanlar atlanır (tekrar çalıştırmak zararsızdır).\n" +
                "• İnternet bağlantısı gerekir ve fotoğraf sayısına göre sürebilir.\n\n" +
                "Devam edilsin mi?",
                "Fotoğrafları Sunucuya Yükle", "Evet, Yükle", "Vazgeç"))
            return;

        PhotoBusy = true;
        PhotoStatus = "Fotoğraflar taranıyor…";
        try
        {
            var sonuc = await DesktopPhotos.TumunuSunucuyaTasiAsync(_session,
                (islenen, toplam) => Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => PhotoStatus = $"Yükleniyor… {islenen}/{toplam}"));

            PhotoStatus = sonuc switch
            {
                { Hata: not null } => "Fotoğraflar okunamadı: " + sonuc.Hata,
                { Cevrimdisi: true } => $"Bağlantı yok — işlem yarıda kaldı ({sonuc.Yuklenen} yüklendi). " +
                                        "Çevrimiçi olduğunuzda tekrar çalıştırın; kaldığı yerden devam eder.",
                { Toplam: 0 } => "Bu bilgisayarda sunucuya taşınacak fotoğraf bulunamadı.",
                _ => $"Tamamlandı: {sonuc.Yuklenen} fotoğraf yüklendi · {sonuc.Atlanan} zaten sunucudaydı" +
                     (sonuc.Basarisiz > 0 ? $" · {sonuc.Basarisiz} dosya okunamadı" : "") +
                     $" (toplam {sonuc.Toplam}).",
            };
        }
        catch (Exception ex) { PhotoStatus = "Yüklenemedi: " + ex.Message; }
        finally { PhotoBusy = false; }
    }

    /// <summary>Bu makinenin adı (sunucuda klasör/etiket olur).</summary>
    public string MachineName => Environment.MachineName;
    private string? ServerUrl => DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerUrl);
    private string? ServerToken => DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerToken);
    public bool ServerConfigured => !string.IsNullOrWhiteSpace(ServerUrl);

    public BackupViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var b in DesktopServices.Backup.ListBackups()) Items.Add(new BackupRowVm(b));
            Status = $"{Items.Count} yedek — klasör: {DesktopServices.Backup.GetBackupFolder()}";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private async Task Backup()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Şimdi yedek alınsın mı?", "Yedek Al")) return;
        try
        {
            var path = DesktopServices.Backup.Backup();
            Load();
            Status = "Yedek alındı: " + path;
            // Sunucu tanımlıysa hemen yükle (sunucu hiç silmez, tüm makinelerin yedeğini saklar)
            if (ServerConfigured)
            {
                var r = await DesktopServices.BackupUpload.UploadAsync(
                    ServerUrl!, ServerToken, _session.CompanyId, MachineName, path);
                Status = "Yedek alındı. Sunucu: " + r.Message;
            }
        }
        catch (Exception ex) { Status = "Yedek alınamadı: " + ex.Message; }
    }

    /// <summary>Seçili (veya en güncel) yedeği bulut sunucusuna yükler.</summary>
    [RelayCommand]
    private async Task UploadToServer(BackupRowVm? row)
    {
        row ??= Selected ?? (Items.Count > 0 ? Items[0] : null);
        if (row is null) { Status = "Yüklenecek yedek seçin."; return; }
        if (!ServerConfigured) { Status = "Önce Ayarlar'dan sunucu adresini tanımlayın."; return; }
        Status = "Sunucuya yükleniyor…";
        var r = await DesktopServices.BackupUpload.UploadAsync(
            ServerUrl!, ServerToken, _session.CompanyId, MachineName, row.Path);
        Status = r.Message;
    }

    [RelayCommand]
    private async Task Restore(BackupRowVm? row)
    {
        row ??= Selected;
        if (row is null) { Status = "Yedek seçin."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{row.FileName}' yedeği GERİ YÜKLENSİN mi?\nMevcut veriler bu yedekle değiştirilir; uygulama yeniden başlatılmalı.\n\n" +
                "Önce bu bilgisayarda bekleyen değişiklikler sunucuya gönderilir ve mevcut verinin güvenlik kopyası alınır.",
                "Yedek Geri Yükle", "Evet, Geri Yükle", "Vazgeç", danger: true)) return;

        // ⭐ M-19 (AR-GE 2026-10-18, 05 F-043): geri yükleme sırasında arka plan eşitlemesi veritabanına YAZMAMALI →
        // eşitleme kapısı tutulur; başarılı geri yüklemeden sonra BIRAKILMAZ (yeniden başlatana kadar eşitleme durur,
        // eski oturum yeni dosyaya yazmaz).
        if (!await SyncGate.EnterAsync())
        { Status = "Eşitleme sürüyor; birazdan tekrar deneyin."; return; }
        var basarili = false;
        try
        {
            // 1) Yedekten sonra yapılıp henüz gönderilmemiş işler: önce sunucuya gönder (orada kalırlar; geri yüklemeden
            //    sonraki ilk eşitleme onları bu bilgisayara geri getirir).
            if (BusinessSyncPushService.YerelBekleyenVar() || CompanySyncService.PendingCount() > 0)
            {
                Status = "Bekleyen değişiklikler sunucuya gönderiliyor…";
                await CompanySyncService.TryFlushAsync();
                await BusinessSyncPushService.PushAsync();
            }
            // 2) Hâlâ gönderilemeyen varsa (çevrimdışı vb.) kullanıcı AÇIKÇA onaylamadan devam edilmez.
            if (BusinessSyncPushService.YerelBekleyenVar() || CompanySyncService.PendingCount() > 0)
            {
                if (!await ConfirmService.AskAsync(
                        "⚠️ Bu bilgisayarda sunucuya GÖNDERİLEMEYEN değişiklikler var (internet yok ya da sunucu reddetti).\n\n" +
                        "Geri yüklerseniz bu değişiklikler bu bilgisayardaki veriden çıkar. Kaybolmazlar: mevcut veri " +
                        "\"…_geri_yukleme_oncesi\" adlı güvenlik kopyasına kaydedilir — ama sunucuya ulaşmazlar.\n\n" +
                        "Önerilen: internete bağlanıp \"Eşitle\"ye basın, sonra geri yükleyin. Yine de devam edilsin mi?",
                        "Gönderilmemiş Değişiklikler", "Yine de Geri Yükle", "Vazgeç", danger: true))
                { Status = "Geri yükleme iptal edildi (gönderilmemiş değişiklikler korundu)."; return; }
            }
            // 3) Geri yükle (servis önce güvenlik kopyası alır; alamazsa geri yüklemez).
            var oncesi = DesktopServices.Backup.Restore(_session, row.Path, reauthenticated: true);
            basarili = true;
            Status = "Geri yüklendi. Lütfen uygulamayı kapatıp yeniden açın. Önceki veri kopyası: " + System.IO.Path.GetFileName(oncesi);
        }
        catch (Exception ex) { Status = "Geri yüklenemedi: " + ex.Message; }
        finally { if (!basarili) SyncGate.Exit(); }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try { FilePickerService.OpenFile(DesktopServices.Backup.GetBackupFolder()); }
        catch (Exception ex) { Status = "Klasör açılamadı: " + ex.Message; }
    }
}

public sealed class BackupRowVm
{
    public string Path { get; }
    public string FileName { get; }
    public string SizeText { get; }
    public string DateText { get; }

    public BackupRowVm(BackupInfo b)
    {
        Path = b.Path;
        FileName = System.IO.Path.GetFileName(b.Path);
        SizeText = $"{b.SizeBytes / 1024.0 / 1024.0:0.##} MB";
        DateText = DateTimeOffset.FromUnixTimeMilliseconds(b.CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    }
}
