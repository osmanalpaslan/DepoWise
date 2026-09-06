using System.Collections.Generic;
using System.Linq;

namespace DepoWise.Application.Security;

/// <summary>
/// ═══ KORUNABİLİR ALANLAR — TEK DOĞRU KAYNAK (FAZ 3b, ADR-223 · D4) ═══
///
/// Bir firmanın "korumalı" işaretleyebileceği alanların listesi. <see cref="FieldAccess"/> kararı
/// bu katalogla değil <c>field_protections</c> tablosuyla verir; katalog yalnızca <b>yönetim
/// ekranına ne gösterileceğini</b> ve hangi anahtarların geçerli sayılacağını belirler.
///
/// <b>🔴 EN ÖNEMLİ KURAL — buraya yalnız GERÇEKTEN UYGULANAN alan yazılır.</b>
/// Serviste süzülmeyen bir alanı buraya eklemek, yöneticiye "korudum" dedirtip aslında yalnız
/// arayüzde gizlemek olurdu. Kullanıcının açık şartı: <i>"sadece UI'da buton gizleyerek 'yetki
/// sistemi yaptım' deme."</i> Kapsam büyüdükçe (3b-5+) satır eklenir — önce servis, sonra katalog.
///
/// <b>Neden <see cref="Ui.FieldCatalog"/> genişletilmedi:</b> o katalog "bu alan doldurulmak ZORUNDA
/// mı" sorusunun (Alan Ayarları ekranı) kaynağıdır. Ön Muhasebe alanlarını oraya eklemek, hiç
/// istenmemiş bir işi — ön muhasebe formlarında zorunluluk doğrulaması — sessizce devreye sokardı.
/// İki soru ayrı kalır; anahtar biçimi (<c>ekran</c> + <c>alan</c>) ortaktır.
///
/// <b>Ekran anahtarları</b> mevcut sözlükten alınır — <c>materials</c> <see cref="Ui.FieldCatalog"/>
/// ile, <c>accounting.*</c> ise <see cref="AppScreens"/> ekran anahtarlarıyla birebir aynıdır.
/// Anahtar tek yerde (burada) sabitlendiği için <b>web ve masaüstü kaçınılmaz olarak aynı izin
/// anahtarını üretir</b> (kullanıcı şartı §6: platforma göre farklı anahtar YASAK).
/// </summary>
public static class FieldProtectionCatalog
{
    /// <param name="ScreenKey">Ekran anahtarı — <c>fld_&lt;ScreenKey&gt;_&lt;FieldKey&gt;</c> anahtarının parçası.</param>
    /// <param name="ScreenLabel">Yönetim ekranında gösterilen ekran adı.</param>
    /// <param name="FieldKey">Alan anahtarı (DB kolon adı değil, <b>kanonik alan kimliği</b>).</param>
    /// <param name="Label">Yönetim ekranında gösterilen alan adı.</param>
    /// <param name="Note">Korunduğunda ne olacağının tek cümlelik açıklaması (yöneticiye gösterilir).</param>
    /// <param name="ModuleKey">Alanın ait olduğu EKRAN MODÜLÜ (<c>materials</c>, <c>parties</c>…).
    ///   Yetki ağacında alan satırı bu modülün hemen ardına yerleşir → yönetici alanı ait olduğu
    ///   ekranın altında bulur, ayrı bir listede aramak zorunda kalmaz (FAZ 3b-5).</param>
    public sealed record ProtectableField(string ScreenKey, string ScreenLabel, string FieldKey,
        string Label, string Note, string ModuleKey);

    /// <summary>Ekran anahtarları — yazım hatası riskini kaldırmak için sabit.</summary>
    public const string Materials = "materials";
    public const string Parties = "accounting.parties";
    public const string Invoices = "accounting.invoices";
    public const string Finance = "accounting.finance";

    /// <summary>Alan anahtarları.</summary>
    public const string UnitPrice = "unit_price";
    public const string Balance = "balance";
    public const string GrandTotal = "grand_total";
    public const string Amount = "amount";

    /// <summary>
    /// FAZ 3b-4 kapsamı: <b>Malzemeler + Ön Muhasebe</b> (D4). Bu satırların HER BİRİ servis
    /// katmanında uygulanır ve testle kanıtlanmıştır; kapsanmayan her alan bugünkü gibi davranır.
    /// </summary>
    public static readonly IReadOnlyList<ProtectableField> All = new[]
    {
        new ProtectableField(Materials, "Malzemeler", UnitPrice, "Birim Fiyat",
            "Malzeme kartında, listede, dışa aktarımda ve raporda birim fiyat gizlenir; kaydederken korunur.",
            ModuleKey: "materials"),

        // NOT: borç, alacak ve bakiye TEK kalemdir. Bakiye = Borç − Alacak olduğu için ikisini ayrı
        // yetkilere bağlamak SAHTE bir incelik olurdu: borcu ve bakiyeyi gören alacağı hesaplar.
        new ProtectableField(Parties, "Cari Kartlar", Balance, "Bakiye (Borç / Alacak)",
            "Cari listesinde ve ekstrede borç, alacak, bakiye ve yürüyen bakiye gizlenir.",
            ModuleKey: "parties"),

        new ProtectableField(Invoices, "Faturalar", GrandTotal, "Fatura Tutarı",
            "Fatura listesinde ve dışa aktarımda tutar gizlenir; fatura DETAYI ve tahsilat/ödeme ekranı açılmaz.",
            ModuleKey: "invoices"),

        new ProtectableField(Finance, "Kasa / Banka", Amount, "Hareket Tutarı",
            "Kasa-banka hareket listesinde ve ekstrede tutar ile yürüyen bakiye gizlenir.",
            ModuleKey: "finance"),

        new ProtectableField(Finance, "Kasa / Banka", Balance, "Hesap Bakiyesi",
            "Kasa-banka hesap listesinde ve hesap kartında giriş, çıkış ve bakiye gizlenir.",
            ModuleKey: "finance"),
    };

    /// <summary>Yetki ağacında gösterilecek etiket: <c>Alan › Ekran › Alan Adı</c>.
    /// Yönetici <c>fld_accounting.finance_amount</c> gibi teknik bir anahtar GÖRMEZ
    /// (rapor kalemlerindeki "Rapor › …" deseniyle aynı).</summary>
    public static string TreeLabel(ProtectableField f) => "Alan › " + f.ScreenLabel + " › " + f.Label;

    /// <summary>Yönetim ekranının listesi (ekran bazında gruplu).</summary>
    public static IEnumerable<IGrouping<(string ScreenKey, string ScreenLabel), ProtectableField>> ByScreen()
        => All.GroupBy(f => (f.ScreenKey, f.ScreenLabel));

    /// <summary>Bu (ekran, alan) çifti korunabilir mi? Katalog dışı anahtar KABUL EDİLMEZ —
    /// aksi hâlde yazım hatası, hiçbir zaman uygulanmayan sahte bir koruma satırı üretirdi.</summary>
    public static ProtectableField? Find(string screenKey, string fieldKey)
        => All.FirstOrDefault(f => f.ScreenKey == screenKey && f.FieldKey == fieldKey);

    // NOT (FAZ 3b-5): "PermissionKeys()" yardimcisi KALDIRILDI. Bu dosya WEB projesine de link ile
    // derleniyor; FieldAccess ise oturum/AccessControl bagimliligi tasidigi icin webde YOK. Katalog
    // yalnizca SOZLUK (ekran/alan/etiket) tasir, KARAR tasimaz - bagimlilik yonu boylece tek yonlu
    // kalir: FieldAccess -> FieldProtectionCatalog. Anahtar uretimi FieldAccess.Key ile yapilir.
}
