namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ FAZ 4.6 (kullanıcı isteği 2026-09-06) — "+" (HIZLI TANIM EKLEME) YÖNETİMİ ═══
///
/// <b>Kullanıcının kalan isteği.</b> <i>"Sadece sabit tanımlı olan alanların yanına '+' butonu ekleme
/// yapabileceğim bir ekran tasarlarız. Veya uygun olan bir ekranda konumlandırırız."</i>
/// (Serbest metni sabit tanımlıya çevirme kısmı kullanıcı tarafından İPTAL edildi.)
///
/// <b>Ne yapar.</b> Firma, hangi sabit tanım alanlarının yanında "+" (satır içi hızlı ekleme)
/// çıkacağını seçer. Kapatılan tanımda "+" hiç çizilmez ve hızlı ekleme SERVİSTE de reddedilir
/// (arayüze güvenilmez). Amaç: tanım düzenini korumak — herkes formdan yeni kategori/marka
/// açarsa aynı şey farklı adlarla çoğalır.
///
/// <b>Yeni yetki motoru YOK.</b> Mevcut <c>btn-add-lookup</c> YETKİSİ aynen durur; bu, onun ÜSTÜNE
/// eklenen bir FİRMA AYARIDIR (iki kademe: yetki + firma tercihi). Kademe düzeni FAZ 3b'deki
/// alan koruması modeliyle aynıdır.
///
/// <b>Migration YOK.</b> Değer mevcut firma ayarları (settings) tablosunda
/// <c>lookup_plus:&lt;tablo&gt;</c> anahtarıyla saklanır. Kayıt yoksa varsayılan <b>AÇIK</b> →
/// hiçbir firma için bugünkü davranış değişmez (geri uyumluluk).
/// </summary>
public static class LookupPlusCatalog
{
    /// <summary>Firma ayarı anahtarı.</summary>
    public static string Key(string table) => "lookup_plus:" + table;

    /// <summary>Kapalı değerinin metin karşılığı (açık = "1" ya da kayıt yok).</summary>
    public const string Kapali = "0";

    /// <summary>
    /// "+" ile hızlı eklenebilen SABİT TANIM alanları — ekranda gösterilen ad ve tablo.
    ///
    /// ⚠️ Personel · Şube/Şantiye · Bakım Tanımları bilerek YOKTUR: bunlar bir "tanım listesi" değil,
    /// kendi ekranı ve kendi yetkisi olan MODÜLLERDİR (Tanımlar ekranında da bu gerekçeyle yer almazlar).
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string Label, string Screen)> All = new[]
    {
        ("units", "Birim", "Malzemeler"),
        ("material_categories", "Malzeme Kategorisi", "Malzemeler"),
        ("brands", "Malzeme Markası", "Malzemeler"),
        ("suppliers", "Tedarikçi", "Malzemeler / Yakıt"),
        ("vehicle_types", "Makine Tipi", "Araçlar"),
        ("vehicle_categories", "Araç Kategorisi", "Araçlar"),
        ("vehicle_brands", "Araç Markası", "Araçlar"),
        ("vehicle_models", "Araç Modeli", "Araçlar"),
    };

    /// <summary>Bilinen tablo mu? (Bilinmeyen anahtar sessizce "açık" sayılır — ekran kilitlenmez.)</summary>
    public static bool Bilinen(string table)
    {
        foreach (var x in All) if (x.Table == table) return true;
        return false;
    }

    /// <summary>Ekranda gösterilecek ad; bilinmeyen tabloda tablonun kendi adı.</summary>
    public static string Label(string table)
    {
        foreach (var x in All) if (x.Table == table) return x.Label;
        return table;
    }
}
