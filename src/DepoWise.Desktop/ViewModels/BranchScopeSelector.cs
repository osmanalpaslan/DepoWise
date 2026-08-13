using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// G4-3d — ORTAK ŞUBE SEÇİCİ (masaüstü). Web'deki <c>BranchPicker.razor</c>'ın karşılığıdır;
/// Cari / Fatura / Kasa-Banka / Tahsilat-Ödeme ekranları AYNI nesneyi kullanır — şube seçme
/// mantığı her ViewModel'e kopyalanmaz.
///
/// <b>⚠️ GÜVENLİK KONTROLÜ DEĞİLDİR.</b> Yalnız kullanıcıya YETKİLİ olduğu şubeleri gösterir ve
/// seçimi servise taşır. Asıl kapı <see cref="BranchAccess"/>'tedir: seçim atlansa bile servis
/// kesişimi alır (okuma) veya işlemi reddeder (yazma).
///
/// <b>ÇEVRİMDIŞI GÜVENLİ:</b> kapsam SUNUCUDAN sorulmaz — oturumun kendi
/// <see cref="SessionContext.ScopeBranchIds"/> / <see cref="SessionContext.HomeBranchId"/>
/// bilgisinden türetilir. Ağ yokken kapsam genişlemez.
///
/// <b>OKUMA ≠ YAZMA:</b> çoklu seçim yalnız listeleme/filtreleme içindir. Yazmada tekil
/// <see cref="ActiveWriteBranchId"/> kullanılır — birden çok şube seçiliyken yeni kaydın hangi
/// şubeye yazılacağı belirsiz olurdu.
/// </summary>
public sealed partial class BranchScopeSelector : ObservableObject
{
    private readonly SessionContext _session;
    private readonly Action _changed;

    /// <summary>Açılır liste öğesi (Avalonia ValueTuple bağlayamadığı için gerçek tip).</summary>
    public sealed record Option(string Key, string Label);

    /// <summary>Kullanıcının erişebildiği şubeler. Yetkisiz şube BU LİSTEDE YOKTUR.</summary>
    public ObservableCollection<Option> Branches { get; } = new();

    /// <summary>Kullanıcı kapsamı sınırsız mı (admin / tüm şube yetkisi)?</summary>
    public bool Unrestricted { get; }

    /// <summary>Seçici gösterilsin mi? Tek şubeli kullanıcıya seçenek sunmanın anlamı yok.</summary>
    public bool IsVisible => Branches.Count > 1;

    /// <summary>Seçili şubeler. BOŞ = "tüm yetkili şubeler" (kullanıcının erişebildikleri).</summary>
    public ObservableCollection<string> Selected { get; } = new();

    /// <summary>
    /// Serviste kullanılacak OKUMA kapsamı.
    ///
    /// ⭐ GUI-03 (2026-08-13, gerçek masaüstü GUI testinde bulundu): seçim boşken <c>null</c> dönülüyordu.
    /// <c>null</c> "istenen yok" demektir ve <see cref="BranchAccess.Effective"/> formülü
    /// (<c>İZİNLİ ∩ (İSTENEN ?? OTURUM ?? İZİNLİ)</c>) bu durumda <b>oturumun çalışma şubesine</b> düşer.
    /// Oysa ekran boş seçim için "Tüm yetkili şubeler" YAZIYORDU → etiket A+B vaat ederken veri yalnız
    /// çalışma şubesinden geliyordu (Şube B'de 700 görünüyor, 2200 bekleniyordu).
    ///
    /// Artık boş seçimde YETKİLİ ŞUBELERİN TAMAMI açıkça istenir; etiket ile veri aynı şeyi söyler.
    /// Kapsamı GENİŞLETMEZ: servis yine <c>İZİNLİ</c> ile kesiştirir, yetkisiz şube giremez.
    /// Gerçekten kısıtsız kullanıcıda (kapsam yok) <c>null</c> korunur — firma geneli anlamını taşır.
    /// </summary>
    public IReadOnlyList<string>? Filter =>
        Selected.Count > 0 ? Selected.ToList()
        : Unrestricted ? null
        : Branches.Select(b => b.Key).ToList();

    /// <summary>Tekil seçim bağlaması (basit ekranlar için). Boş dize = tüm yetkili şubeler.</summary>
    [ObservableProperty] private string _single = "";

    public string SummaryText => Selected.Count switch
    {
        0 => Unrestricted ? "Tüm yetkili şubeler" : $"{Branches.Count} şube (yetkili)",
        1 => Branches.FirstOrDefault(b => b.Key == Selected[0])?.Label ?? "1 şube",
        _ => $"{Selected.Count} şube seçili",
    };

    public BranchScopeSelector(SessionContext session, Action changed)
    {
        _session = session;
        _changed = changed;

        var allowed = BranchAccess.Allowed(session);
        Unrestricted = allowed is null;

        try
        {
            foreach (var b in DesktopServices.Branches.List(session))
                if (allowed is null || allowed.Contains(b.Id, StringComparer.Ordinal))
                    Branches.Add(new Option(b.Id, b.Name));
        }
        catch { /* şube listesi alınamazsa seçici gizlenir; servis filtresi yine çalışır */ }

        // VARSAYILAN: kullanıcının çalışma şubesi (yoksa ana şubesi). Normal kullanıcı ekranı
        // açtığında KENDİ ŞUBESİNİ görür — "firma geneli" varsayılan DEĞİLDİR.
        var varsayilan = session.OperatingBranchId ?? session.HomeBranchId;
        if (varsayilan is not null && Branches.Any(b => b.Key == varsayilan))
        {
            Selected.Add(varsayilan);
            Single = varsayilan;
        }
        else if (!Unrestricted && Branches.Count == 1)
        {
            Selected.Add(Branches[0].Key);
            Single = Branches[0].Key;
        }

        BuildPicks();   // çoklu seçim listesi (web BranchPicker ile aynı semantik)
    }

    /// <summary>
    /// YAZMA için AKTİF ÇALIŞMA ŞUBESİ — <b>tekil</b>.
    /// Sıra: tek seçim → oturumun çalışma şubesi → ana şube → tek izinli şube → null.
    /// null dönerse servis <see cref="BranchAccess.Resolve"/> ile kendisi karar verir (ve doğrular).
    /// </summary>
    public string? ActiveWriteBranchId
    {
        get
        {
            if (Selected.Count == 1) return Selected[0];
            if (!string.IsNullOrEmpty(_session.OperatingBranchId)) return _session.OperatingBranchId;
            if (!string.IsNullOrEmpty(_session.HomeBranchId)) return _session.HomeBranchId;
            if (Branches.Count == 1) return Branches[0].Key;
            return null;
        }
    }

    /// <summary>
    /// Tekil seçim değişti → kapsamı güncelle ve ekranı yenile.
    /// ⚠️ <c>_suppress</c> koruması ŞART: çoklu seçim de <c>Single</c>'ı günceller; koruma olmadan
    /// buradaki temizleme İKİ ŞUBELİ seçimi siliyordu (geri besleme döngüsü).
    /// </summary>
    partial void OnSingleChanged(string value)
    {
        if (_suppress) return;
        Selected.Clear();
        if (!string.IsNullOrEmpty(value) && Branches.Any(b => b.Key == value)) Selected.Add(value);
        SyncPicks();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SelectionText));
        _changed();
    }

    /// <summary>Picks işaretlerini Selected ile eşitler (tetikleme yapmadan).</summary>
    private void SyncPicks()
    {
        _suppress = true;
        foreach (var p in Picks) p.Selected = Selected.Contains(p.Key);
        _suppress = false;
    }

    /// <summary>Çoklu seçimi dışarıdan uygular (yetkisiz şubeler SESSİZCE DÜŞÜRÜLÜR — fail-closed).</summary>
    public void SetSelection(IEnumerable<string> ids)
    {
        var gecerli = Branches.Select(b => b.Key).ToHashSet(StringComparer.Ordinal);
        Selected.Clear();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
            if (gecerli.Contains(id)) Selected.Add(id);
        SyncPicks();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SelectionText));
        _changed();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  ÇOKLU ŞUBE SEÇİMİ (G4-3e) — web'deki BranchPicker ile AYNI semantik
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seçilebilir şube satırı — işaretlenebilir. Avalonia'da <c>ObservableCollection</c> içindeki
    /// öğe kendi işaretini taşımalı (CheckBox bağlaması için).
    /// </summary>
    public sealed partial class Pick : ObservableObject
    {
        private readonly Action _changed;
        public Pick(string key, string label, bool selected, Action changed)
        {
            Key = key; Label = label; _selected = selected; _changed = changed;
        }

        public string Key { get; }
        public string Label { get; }

        [ObservableProperty] private bool _selected;
        partial void OnSelectedChanged(bool value) => _changed();
    }

    /// <summary>İşaretlenebilir şube listesi (çoklu seçim UI'ı bunu bağlar).</summary>
    public ObservableCollection<Pick> Picks { get; } = new();

    /// <summary>
    /// Çoklu seçim açık mı? Tek yetkili şubesi olan kullanıcıya çoklu seçim karmaşası GÖSTERİLMEZ.
    /// </summary>
    public bool MultiEnabled => Branches.Count > 1;

    private bool _suppress;   // toplu değişimde tek yenileme

    /// <summary>İşaret değişince kapsamı güncelle (tek noktadan).</summary>
    private void OnPickChanged()
    {
        if (_suppress) return;
        Selected.Clear();
        foreach (var p in Picks) if (p.Selected) Selected.Add(p.Key);
        _suppress = true;                                       // Single değişimi Selected'ı EZMESİN
        Single = Selected.Count == 1 ? Selected[0] : "";
        _suppress = false;
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SelectionText));
        _changed();
    }

    /// <summary>Kullanıcıya gösterilen özet: "Tüm yetkili şubeler" / "DÜZCE" / "2 şube seçili".</summary>
    public string SelectionText => Selected.Count switch
    {
        0 => "Tüm yetkili şubeler",
        1 => Branches.FirstOrDefault(b => b.Key == Selected[0])?.Label ?? "1 şube",
        _ => $"{Selected.Count} şube seçili",
    };

    /// <summary>
    /// TÜM yetkili şubeleri seçer. ⚠️ "Tümü" = kullanıcının ERİŞEBİLDİĞİ şubeler;
    /// firmanın tüm şubeleri DEĞİL.
    /// </summary>
    public void SelectAll()
    {
        _suppress = true;
        foreach (var p in Picks) p.Selected = true;
        _suppress = false;
        OnPickChanged();
    }

    /// <summary>Seçimi temizler → "tüm yetkili şubeler" (filtre yok) anlamına gelir.</summary>
    public void ClearSelection()
    {
        _suppress = true;
        foreach (var p in Picks) p.Selected = false;
        _suppress = false;
        OnPickChanged();
    }

    /// <summary>Picks listesini Branches + Selected'tan kurar (yapıcıda çağrılır).</summary>
    private void BuildPicks()
    {
        Picks.Clear();
        foreach (var b in Branches)
            Picks.Add(new Pick(b.Key, b.Label, Selected.Contains(b.Key), OnPickChanged));
    }
}
