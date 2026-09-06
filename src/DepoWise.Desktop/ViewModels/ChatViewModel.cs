using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Chat;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Açık bir konuşma penceresi. Alt barda bir sekmesi, ekranda (açıksa) bir penceresi vardır.
/// </summary>
public sealed partial class KonusmaVm : ObservableObject
{
    public string UserId { get; }
    public string Display { get; }
    public string Initial => string.IsNullOrWhiteSpace(Display) ? "?" : Display.Trim()[..1].ToUpperInvariant();

    /// <summary>Pencere açık mı? Alt bardaki sekmeye tıklamak bunu aç/kapat yapar.</summary>
    [ObservableProperty] private bool _acik = true;
    [ObservableProperty] private bool _online;
    [ObservableProperty] private string _taslak = "";
    [ObservableProperty] private int _okunmamis;
    [ObservableProperty] private string? _hata;

    public bool OkunmamisVar => Okunmamis > 0;
    partial void OnOkunmamisChanged(int value) => OnPropertyChanged(nameof(OkunmamisVar));

    public ObservableCollection<ChatMesaj> Mesajlar { get; } = new();

    /// <summary>En son alınan mesajın zamanı — yoklama bundan sonrasını ister (tüm geçmiş taşınmaz).</summary>
    public long? SonZaman { get; set; }

    public KonusmaVm(string userId, string display, bool online)
    {
        UserId = userId;
        Display = display;
        _online = online;
    }
}

/// <summary>
/// ═══ UYGULAMA İÇİ SOHBET — masaüstü ═══ (kullanıcı isteği 2026-09-06)
///
/// <para><b>Kullanıcının tasarımı:</b> ana sohbet düğmesi alt barın EN SAĞINDA sabit durur; kişi
/// listesi ondan açılır. Bir kişiye tıklanınca konuşma AYRI bir pencere olarak açılır ve alt barda
/// kendi sekmesini alır. Sekmeye tıklamak pencereyi açar/kapatır; ✕ sekmeyi kaldırır. Pencereler
/// üst katmanda çizilir → <b>ekranda fazladan yer işgal etmezler</b>.</para>
///
/// <para><b>Yoklama (kullanıcı kuralı).</b> Sohbet normal eşitlemenin DIŞINDADIR ve yalnız makine
/// çevrimiçiyken çalışır. Aralık:
/// <list type="bullet">
///   <item><b>Açıkken 3 sn</b> — kullanıcının açık kararı. GEÇİCİDİR: gerçek sunucuya geçilince
///         yeniden ele alınacak (bkz. kalıcı not). Bu yüzden değer koda gömülü değil, aşağıdaki
///         tek sabittedir.</item>
///   <item><b>Kapalıyken 20 sn</b> — yalnız okunmamış rozetini tazelemek için; kapalı pencere
///         için 3 saniyede bir sunucuya gitmek boşuna maliyettir.</item>
///   <item><b>Çevrimdışıyken hiç</b> — çağrı bile yapılmaz.</item>
/// </list></para>
///
/// <para><b>Yetki.</b> Sohbet "chat" modülüne bağlıdır ve deny-by-default'tur: yetkisi olmayan
/// kullanıcıda <see cref="Kullanilabilir"/> false olur, alt bardaki düğme görünmez ve hiçbir çağrı
/// yapılmaz. Sunucu uçları da aynı anahtarı arar (UI ile API aynı kapı).</para>
/// </summary>
public sealed partial class ChatViewModel : ObservableObject, IDisposable
{
    /// <summary>Sohbet penceresi AÇIKKEN yoklama aralığı (saniye). Kullanıcı kararı; geçici.</summary>
    public const int AcikYoklamaSaniye = 3;

    /// <summary>Sohbet KAPALIYKEN yoklama aralığı (saniye) — yalnız okunmamış rozeti içindir.</summary>
    public const int KapaliYoklamaSaniye = 20;

    private readonly SessionContext _session;
    private readonly Timer _zamanlayici;
    private bool _yoklamaCalisiyor;
    private bool _birakildi;

    /// <summary>Kullanıcının sohbet yetkisi var mı? Yoksa hiçbir şey çizilmez ve çağrı yapılmaz.</summary>
    public bool Kullanilabilir { get; }

    [ObservableProperty] private bool _panelAcik;
    [ObservableProperty] private int _toplamOkunmamis;
    [ObservableProperty] private bool _cevrimdisi;
    [ObservableProperty] private string _arama = "";

    public bool ToplamOkunmamisVar => ToplamOkunmamis > 0;
    partial void OnToplamOkunmamisChanged(int value) => OnPropertyChanged(nameof(ToplamOkunmamisVar));

    /// <summary>Tüm kişiler (çevrimiçi olanlar üstte).</summary>
    public ObservableCollection<ChatKisi> Kisiler { get; } = new();
    /// <summary>Aramaya uyan kişiler — listede bunlar gösterilir.</summary>
    public ObservableCollection<ChatKisi> GorunenKisiler { get; } = new();
    /// <summary>Açık konuşmalar — alt barda birer sekme, ekranda birer pencere.</summary>
    public ObservableCollection<KonusmaVm> Konusmalar { get; } = new();

    public ChatViewModel(SessionContext session)
    {
        _session = session;
        Kullanilabilir = AccessControl.Can(session, "chat", PermissionAction.View);

        // Zamanlayıcı yetki yoksa hiç kurulmaz: yetkisiz kullanıcı sunucuya tek istek bile atmaz.
        _zamanlayici = new Timer(_ => _ = Yokla(), null,
            Kullanilabilir ? TimeSpan.FromSeconds(2) : Timeout.InfiniteTimeSpan,
            TimeSpan.FromSeconds(KapaliYoklamaSaniye));
    }

    partial void OnPanelAcikChanged(bool value)
    {
        AraligiAyarla();
        if (value) _ = Yokla();   // açılışta bekletme: liste hemen gelsin
    }

    partial void OnAramaChanged(string value) => SuzgeciUygula();

    /// <summary>Açık pencere varsa hızlı, yoksa yavaş yokla.</summary>
    private void AraligiAyarla()
    {
        if (_birakildi || !Kullanilabilir) return;
        bool hizli = PanelAcik || Konusmalar.Any(k => k.Acik);
        var saniye = hizli ? AcikYoklamaSaniye : KapaliYoklamaSaniye;
        try { _zamanlayici.Change(TimeSpan.FromSeconds(saniye), TimeSpan.FromSeconds(saniye)); } catch { }
    }

    /// <summary>
    /// Tek yoklama turu: kişiler + açık konuşmaların yeni mesajları. Çakışan turlar engellenir
    /// (yavaş bir yanıt, bir sonraki turu tetiklemesin).
    /// </summary>
    private async Task Yokla()
    {
        if (_birakildi || !Kullanilabilir || _yoklamaCalisiyor) return;
        _yoklamaCalisiyor = true;
        try
        {
            var kisiler = await OrgServerClient.ChatKisilerAsync();
            if (kisiler is null)
            {
                // Sunucuya ulaşılamadı = çevrimdışı. Sohbet sessizce kapanır; sahte veri gösterilmez.
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Cevrimdisi = true);
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Cevrimdisi = false;
                Kisiler.Clear();
                foreach (var k in kisiler.OrderByDescending(x => x.Online).ThenBy(x => x.Display))
                    Kisiler.Add(k);
                SuzgeciUygula();
                ToplamOkunmamis = kisiler.Sum(x => x.Unread);

                // Açık konuşmaların çevrimiçi göstergesi ve rozeti tazelensin.
                foreach (var kon in Konusmalar)
                {
                    var k = kisiler.FirstOrDefault(x => x.UserId == kon.UserId);
                    if (k is null) continue;
                    kon.Online = k.Online;
                    if (!kon.Acik) kon.Okunmamis = k.Unread;
                }
            });

            // Açık konuşmalara yeni mesajları çek (kapalı olanlar için trafik harcanmaz).
            foreach (var kon in Konusmalar.Where(k => k.Acik).ToList())
                await KonusmayiTazele(kon);
        }
        catch { /* yoklama sessiz olmalı: ağ dalgalanması ekranda hata kutusu açmaz */ }
        finally { _yoklamaCalisiyor = false; }
    }

    private async Task KonusmayiTazele(KonusmaVm kon)
    {
        var yeni = await OrgServerClient.ChatKonusmaAsync(kon.UserId, kon.SonZaman);
        if (yeni is null || yeni.Count == 0) return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var m in yeni)
            {
                if (kon.Mesajlar.Any(x => x.Id == m.Id)) continue;   // mükerrer eklemeyi önle
                kon.Mesajlar.Add(m);
                if (m.CreatedAt > (kon.SonZaman ?? 0)) kon.SonZaman = m.CreatedAt;
            }
        });

        // Pencere açıkken gelen mesaj OKUNMUŞ sayılır — kullanıcı ekrana bakıyor.
        if (yeni.Any(m => !m.Mine))
        {
            await OrgServerClient.ChatOkunduAsync(kon.UserId);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => kon.Okunmamis = 0);
        }
    }

    private void SuzgeciUygula()
    {
        GorunenKisiler.Clear();
        var q = (Arama ?? "").Trim();
        foreach (var k in Kisiler)
        {
            if (q.Length > 0 &&
                k.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                (k.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            GorunenKisiler.Add(k);
        }
    }

    /// <summary>Alt bardaki ana sohbet düğmesi: kişi listesini aç/kapat.</summary>
    [RelayCommand]
    private void PaneliAcKapa() => PanelAcik = !PanelAcik;

    /// <summary>Bir kişiye tıklandı: konuşmayı AYRI pencerede aç (zaten açıksa öne getir).</summary>
    [RelayCommand]
    private async Task KisiyiAc(ChatKisi? kisi)
    {
        if (kisi is null) return;
        var mevcut = Konusmalar.FirstOrDefault(k => k.UserId == kisi.UserId);
        if (mevcut is null)
        {
            mevcut = new KonusmaVm(kisi.UserId, kisi.Display, kisi.Online);
            Konusmalar.Add(mevcut);
        }
        mevcut.Acik = true;
        PanelAcik = false;      // kişi listesi kapanır, konuşma penceresi açılır
        AraligiAyarla();

        // ⭐ 2026-09-07 — AĞ HATASI SESSİZCE YUTULMAZ, KULLANICIYA SÖYLENİR.
        // Eskiden bu blok korumasızdı: sunucuya ulaşılamadığında komut ortada kesiliyor, konuşma
        // penceresi BOŞ açılıyordu ve kullanıcı "gelen mesajlar görünmüyor" diyordu — üstelik
        // sebebini gösteren hiçbir şey yoktu.
        try
        {
            var gecmis = await OrgServerClient.ChatKonusmaAsync(mevcut.UserId);
            if (gecmis is not null)
            {
                mevcut.Mesajlar.Clear();
                foreach (var m in gecmis) mevcut.Mesajlar.Add(m);
                mevcut.SonZaman = gecmis.Count > 0 ? gecmis[^1].CreatedAt : null;
                mevcut.Hata = null;
            }
            else mevcut.Hata = ErisimHatasi;

            await OrgServerClient.ChatOkunduAsync(mevcut.UserId);
            mevcut.Okunmamis = 0;
            ToplamOkunmamis = Kisiler.Sum(x => x.Unread) - (Kisiler.FirstOrDefault(x => x.UserId == mevcut.UserId)?.Unread ?? 0);
        }
        catch { mevcut.Hata = ErisimHatasi; }
    }

    /// <summary>Sunucuya ulaşılamadığında konuşma penceresinde gösterilen tek metin.</summary>
    private const string ErisimHatasi =
        "Sohbet sunucuya ulaşamadı. Bağlantı gelince mesajlar kendiliğinden yenilenir.";

    /// <summary>Konuşmayı yeniler; hata DIŞARI SIZMAZ, pencerede gösterilir.</summary>
    private async Task TazeleGuvenli(KonusmaVm kon)
    {
        try { await KonusmayiTazele(kon); kon.Hata = null; }
        catch { kon.Hata = ErisimHatasi; }
    }

    /// <summary>Alt bardaki konuşma sekmesi: pencereyi aç/kapat (sekme kalır).</summary>
    [RelayCommand]
    private void KonusmaAcKapa(KonusmaVm? kon)
    {
        if (kon is null) return;
        // Kapatma HER ZAMAN çalışır: durum önce değiştirilir, ağ işi sonra ve hatasız yapılır.
        kon.Acik = !kon.Acik;
        if (kon.Acik) { kon.Okunmamis = 0; _ = TazeleGuvenli(kon); }
        AraligiAyarla();
    }

    /// <summary>Sekmedeki ✕: konuşmayı tamamen kapat (mesajlar sunucuda kalır).</summary>
    [RelayCommand]
    private void KonusmayiKapat(KonusmaVm? kon)
    {
        if (kon is null) return;
        Konusmalar.Remove(kon);
        AraligiAyarla();
    }

    /// <summary>Mesaj gönder (Enter ya da düğme).</summary>
    [RelayCommand]
    private async Task Gonder(KonusmaVm? kon)
    {
        if (kon is null) return;
        var metin = (kon.Taslak ?? "").Trim();
        if (metin.Length == 0) return;

        kon.Hata = null;
        try
        {
            var res = await OrgServerClient.ChatGonderAsync(kon.UserId, metin);
            if (res.Offline) { kon.Hata = "Mesaj gönderilemedi: bağlantı yok. Sohbet yalnız çevrimiçiyken çalışır."; return; }
            if (!res.Ok) { kon.Hata = res.Error ?? "Mesaj gönderilemedi."; return; }

            kon.Taslak = "";
            await KonusmayiTazele(kon);   // kendi mesajımız da sunucudan gelsin (tek doğruluk kaynağı)
        }
        catch { kon.Hata = ErisimHatasi; }
    }

    public void Dispose()
    {
        _birakildi = true;
        try { _zamanlayici.Dispose(); } catch { }
    }
}
