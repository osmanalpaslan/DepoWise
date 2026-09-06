using CommunityToolkit.Mvvm.ComponentModel;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    // ═══ FAZ 4.6 (kullanıcı isteği 2026-09-06) — SATIR İÇİ "+" FİRMA AYARI ══════════════════════
    // Kullanıcı hangi sabit tanımın yanında "+" çıkacağını seçebilir (Alan Ayarları ekranı).
    // İki kademe: (1) btn-add-lookup YETKİSİ — değişmedi, (2) FİRMA ayarı — yeni.
    // Kayıt yoksa AÇIK → hiçbir firmada bugünkü davranış değişmez.
    //
    // XAML kullanımı: IsVisible="{Binding [units]}" — tablo adı butonun yanında YAZILI olur,
    // böylece hangi düğmenin hangi tanımı eklediği kodu okumadan görülür.

    /// <summary>Bu tanım için "+" düğmesi çizilsin mi (yetki VE firma ayarı).</summary>
    public bool this[string tablo]
    {
        get
        {
            if (!CanAddLookup) return false;
            try { return DesktopServices.Session is { } s && DesktopServices.Lookups.QuickAddEnabled(s, tablo); }
            catch { return CanAddLookup; }   // ayar okunamadı → yetkiye göre davran
        }
    }


    /// <summary>
    /// ⭐ FAZ 4.14 (kullanıcı isteği 2026-09-06) — KOLON TERCİHİNİ SUNUCUDAN AYNALA.
    ///
    /// Kolon tercihi eskiden YALNIZ o makinenin yerel veritabanındaydı; kullanıcı iki bilgisayar
    /// kullandığı için diğer makinede hep varsayılan kolonlar geliyordu ("kaydettiğim seçim
    /// kayboluyor"). Artık çevrimiçiyken SUNUCUDAKİ tercih otoritedir ve yerele aynalanır.
    ///
    /// ⚠️ Çevrimdışıysa ya da sunucu erişilemezse HİÇBİR ŞEY yapılmaz — ekran yerel değerle çalışır
    /// (çalışma durmaz, kullanıcı bir hata görmez).
    /// </summary>
    /// <param name="listKey">Liste anahtarı (ör. "vehicles").</param>
    /// <param name="uygula">Sunucudan gelen kolonları ekrana uygulayan işlem (UI iş parçacığında çağrılır).</param>
    protected async System.Threading.Tasks.Task SunucudanKolonAynalaAsync(
        string listKey, System.Action<System.Collections.Generic.IReadOnlyList<string>> uygula)
    {
        try
        {
            var sunucu = await ServerListPrefsClient.GetColumnsAsync(listKey);
            if (sunucu is not { Count: > 0 }) return;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => uygula(sunucu));
        }
        catch { /* çevrimdışı / erişilemez → yerel değer geçerli kalır */ }
    }

    /// <summary>
    /// Satır içi "+" (tanım ekleme) butonlarının görünürlüğü. Admin bypass + açık "+" izni; aksi halde gizli
    /// (deny-by-default). Tüm view'ler ortak bu özelliğe bağlanır. Oturum login'de yüklenir → view kurulurken sabit.
    /// </summary>
    public bool CanAddLookup =>
        DesktopServices.Session is { } s && AccessControl.CanUseButton(s, SpecialButtons.AddLookup);
}
