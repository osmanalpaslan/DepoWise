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

    /// <summary>2026-10-29 ("Görüldü"): karşı tarafın BENİM mesajlarımı en son okuduğu an — kişi listesi yoklamasıyla tazelenir.</summary>
    public long? KarsiOkuduAt { get; set; }
    /// <summary>Konuşmanın altındaki durum: son mesaj benimse "Görüldü 14:32" / "İletildi"; değilse boş.</summary>
    [ObservableProperty] private string _gorulduMetni = "";
    public bool GorulduVar => !string.IsNullOrEmpty(GorulduMetni);
    partial void OnGorulduMetniChanged(string value) => OnPropertyChanged(nameof(GorulduVar));

    public void GorulduHesapla()
    {
        var son = Mesajlar.Count > 0 ? Mesajlar[^1] : null;
        var okundu = son?.ReadAt is { } r && (KarsiOkuduAt is null || r > KarsiOkuduAt) ? r : KarsiOkuduAt;
        GorulduMetni = ChatKisi.GorulduDurumu(son?.CreatedAt, son?.Mine == true, okundu);
    }

    /// <summary>En son alınan mesajın zamanı — yoklama bundan sonrasını ister (tüm geçmiş taşınmaz).</summary>
    public long? SonZaman { get; set; }

    public KonusmaVm(string userId, string display, bool online)
    {
        UserId = userId;
        Display = display;
        _online = online;
        Mesajlar.CollectionChanged += (_, _) => GorulduHesapla();
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
/// <para><b>Yetki.</b> KULLANICI KARARI 2026-10-29: sohbet bir işlem değil → yetkiye BAĞLI DEĞİL, giriş yapan her kullanıcıda açık. Firma sınırı servistedir (yalnız aynı firmadaki kişiler). <see cref="Kullanilabilir"/> yalnız oturum yoksa false'tur.</para>
/// </summary>
public sealed partial class ChatViewModel : ObservableObject, IDisposable
{
    // ⭐ 2026-10-30 — ANLIK SOHBET (kullanıcı isteği "anlık olsun"): yeni mesaj / okundu olayı sunucunun /api/chat/wait ucundan ANINDA
    // gelir (<see cref="AnlikDinle"/>; masaüstünün anlık eşitlemesiyle aynı uzun-bekleme deseni). Aşağıdaki aralıklı yoklama artık
    // YEDEKTİR (bekleme ucuna ulaşılamazsa, ör. ağ uzun bağlantıyı keser ya da eski sunucu) — eskiden açıkken 3 sn / kapalıyken 20 sn idi.

    /// <summary>Sohbet AÇIKKEN yedek yoklama aralığı (saniye).</summary>
    public const int AcikYoklamaSaniye = 15;

    /// <summary>Sohbet KAPALIYKEN yedek yoklama aralığı (saniye) — yalnız okunmamış rozeti içindir.</summary>
    public const int KapaliYoklamaSaniye = 60;

    /// <summary>Bekleme ucuna ulaşılamayınca yeniden deneme aralığı (saniye).</summary>
    public const int AnlikYenidenDeneSaniye = 10;

    private readonly CancellationTokenSource _anlikIptal = new();
    private long _anlikSurum;
    private bool _bekleyenYoklama;

    /// <summary>Son bekleme sunucuya ulaştı mı (anlık kanal çalışıyor mu).</summary>
    public bool AnlikBagli { get; private set; }

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
    /// <summary>Konuşma önbelleği (bu oturumda açılanlar). 2026-10-29: alt bar sekmesi / ayrı pencere YOK — tek panelde
    /// yalnız <see cref="AktifKonusma"/> görünür ve sık yoklanır.</summary>
    public ObservableCollection<KonusmaVm> Konusmalar { get; } = new();

    // ═══ 2. tur madde 6 (2026-10-29, kullanıcı isteği: "telefonda kullanıyormuş gibi") ═══
    // Tek panel, üç görünüm: "sohbetler" (mesajlaştığım herkes, son mesaj + saat + okunmamış) → "kisiler" (yeni mesaj: tüm
    // kişiler) → "konusma" (balonlar). Geri düğmesi bir önceki görünüme döner; kişi seçince konuşma AYNI panelde açılır.
    [ObservableProperty] private string _gorunum = "sohbetler";
    [ObservableProperty] private KonusmaVm? _aktifKonusma;
    public bool SohbetlerGorunur => Gorunum == "sohbetler";
    public bool KisilerGorunur => Gorunum == "kisiler";
    public bool KonusmaGorunur => Gorunum == "konusma" && AktifKonusma is not null;
    partial void OnGorunumChanged(string value)
    {
        OnPropertyChanged(nameof(SohbetlerGorunur)); OnPropertyChanged(nameof(KisilerGorunur)); OnPropertyChanged(nameof(KonusmaGorunur));
    }
    partial void OnAktifKonusmaChanged(KonusmaVm? value) => OnPropertyChanged(nameof(KonusmaGorunur));

    /// <summary>Sohbetler listesi: mesajlaşılmış (ya da okunmamış mesajı olan / bu oturumda açılmış) kişiler, EN YENİ üstte.</summary>
    public ObservableCollection<ChatKisi> Sohbetler { get; } = new();
    public bool SohbetYok => Sohbetler.Count == 0;

    [RelayCommand]
    private void YeniMesaj() { Arama = ""; Gorunum = "kisiler"; }

    [RelayCommand]
    private void Geri()
    {
        if (Gorunum == "konusma" && AktifKonusma is not null) { AktifKonusma.Acik = false; AktifKonusma = null; }
        Arama = "";
        Gorunum = "sohbetler";
        AraligiAyarla();
        _ = Yokla();   // listedeki son mesaj/okunmamış hemen tazelensin
    }

    public ChatViewModel(SessionContext session)
    {
        _session = session;
        // KULLANICI KARARI 2026-10-29: sohbet bir işlem değil → yetkiye BAĞLI DEĞİL, giriş yapan her kullanıcıda açık. Firma sınırı servistedir (yalnız aynı firmadaki kişiler).
        Kullanilabilir = !string.IsNullOrEmpty(session.UserId);

        // Zamanlayıcı oturum yoksa hiç kurulmaz.
        _zamanlayici = new Timer(_ => _ = Yokla(), null,
            Kullanilabilir ? TimeSpan.FromSeconds(2) : Timeout.InfiniteTimeSpan,
            TimeSpan.FromSeconds(KapaliYoklamaSaniye));
        if (Kullanilabilir) _ = Task.Run(AnlikDinle);
    }

    /// <summary>
    /// Anlık dinleyici: sunucu "bu kullanıcı için yeni olay var" deyince HEMEN bir yoklama turu atar (kişiler + açık konuşmalar).
    /// Ulaşılamazsa <see cref="AnlikYenidenDeneSaniye"/> sonra yeniden dener; o arada yedek yoklama işini görür.
    /// </summary>
    private async Task AnlikDinle()
    {
        var ct = _anlikIptal.Token;
        while (!ct.IsCancellationRequested && !_birakildi)
        {
            var r = await OrgServerClient.ChatBekleAsync(_anlikSurum, ct);
            if (ct.IsCancellationRequested || _birakildi) return;
            if (r is null)
            {
                AnlikBagli = false;
                try { await Task.Delay(TimeSpan.FromSeconds(AnlikYenidenDeneSaniye), ct); } catch { return; }
                continue;
            }
            AnlikBagli = true;
            _anlikSurum = r.Value.Surum;
            if (r.Value.Degisti) await Yokla();
        }
    }

    partial void OnPanelAcikChanged(bool value)
    {
        if (!value && AktifKonusma is not null) AktifKonusma.Acik = false;            // kapalı panel = yavaş yoklama
        if (value && AktifKonusma is not null && Gorunum == "konusma") AktifKonusma.Acik = true;
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
        if (_birakildi || !Kullanilabilir) return;
        // Tur sürerken gelen anlık işaret KAYBOLMASIN: tur bitince bir tur daha atılır (sürmekte olan tur olaydan önce başlamış olabilir).
        if (_yoklamaCalisiyor) { _bekleyenYoklama = true; return; }
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
                    kon.KarsiOkuduAt = k.KarsiOkuduAt;
                    kon.GorulduHesapla();
                }
            });

            // Açık konuşmalara yeni mesajları çek (kapalı olanlar için trafik harcanmaz).
            foreach (var kon in Konusmalar.Where(k => k.Acik).ToList())
                await KonusmayiTazele(kon);
        }
        catch { /* yoklama sessiz olmalı: ağ dalgalanması ekranda hata kutusu açmaz */ }
        finally
        {
            _yoklamaCalisiyor = false;
            if (_bekleyenYoklama && !_birakildi) { _bekleyenYoklama = false; _ = Yokla(); }
        }
    }

    private async Task KonusmayiTazele(KonusmaVm kon)
    {
        var yeni = await OrgServerClient.ChatKonusmaAsync(kon.UserId, kon.SonZaman);
        if (yeni is null || yeni.Count == 0) return;

        var gercektenEklendi = false;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var m in yeni)
            {
                if (kon.Mesajlar.Any(x => x.Id == m.Id)) continue;   // mükerrer eklemeyi önle
                kon.Mesajlar.Add(m);
                if (!m.Mine) gercektenEklendi = true;
                if (m.CreatedAt > (kon.SonZaman ?? 0)) kon.SonZaman = m.CreatedAt;
            }
        });

        // ⭐ SES: YENİ gelen mesaj için — turda kaç mesaj gelirse gelsin TEK ses. Kullanıcı
        // "mevcut olanlar için değil" dedi: geçmişi yükleyen KisiyiAc bu yoldan geçmez, bu
        // yüzden konuşmayı açmak ses çıkarmaz. Kendi mesajım (Mine) da burada ses çıkarmaz —
        // onun sesi gönderim anında çalar.
        if (gercektenEklendi) SesServisi.Cal(DepoWise.Application.Notifications.SesTuru.MesajGelen);

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
        Sohbetler.Clear();
        var q = (Arama ?? "").Trim();
        bool Uyar(ChatKisi k) => q.Length == 0 ||
            k.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (k.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (k.SonMesaj ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        foreach (var k in Kisiler.Where(Uyar)) GorunenKisiler.Add(k);
        foreach (var k in Kisiler.Where(k => (k.MesajVar || k.Unread > 0 || Konusmalar.Any(c => c.UserId == k.UserId)) && Uyar(k))
                                 .OrderByDescending(k => k.SonMesajAt ?? 0).ThenByDescending(k => k.Unread))
            Sohbetler.Add(k);
        OnPropertyChanged(nameof(SohbetYok));
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
        foreach (var k in Konusmalar) k.Acik = false;   // yalnız aktif konuşma yoklanır
        mevcut.Acik = true;
        AktifKonusma = mevcut;
        Gorunum = "konusma";    // 2026-10-29: konuşma AYNI panelde açılır (telefon gibi; ayrı pencere yok)
        PanelAcik = true;
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
            // ⭐ SES: gönderim ONAYLANDIKTAN sonra çalar — "gitti" geri bildirimi. Hata dönerse
            // ses çıkmaz (yukarıdaki iki koşul zaten çıkmış olurdu).
            SesServisi.Cal(DepoWise.Application.Notifications.SesTuru.MesajGiden);
            await KonusmayiTazele(kon);   // kendi mesajımız da sunucudan gelsin (tek doğruluk kaynağı)
        }
        catch { kon.Hata = ErisimHatasi; }
    }

    public void Dispose()
    {
        _birakildi = true;
        try { _anlikIptal.Cancel(); _anlikIptal.Dispose(); } catch { }
        try { _zamanlayici.Dispose(); } catch { }
    }
}
