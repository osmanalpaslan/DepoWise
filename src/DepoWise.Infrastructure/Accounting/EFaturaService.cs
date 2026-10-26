using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>e-Fatura satırı (UBL-TR InvoiceLine).</summary>
public sealed record EFaturaSatiri(string Aciklama, string? Birim, decimal Miktar, decimal BirimFiyat, decimal IskontoOrani,
    decimal KdvOrani, decimal TevkifatOrani)
{
    public decimal Matrah => Math.Round(Miktar * BirimFiyat * (1 - IskontoOrani / 100m), 2);
}

/// <summary>e-Fatura içe alma ÖNİZLEMESİ. <see cref="CariId"/> null → satıcı için yeni tedarikçi carisi açılacak.</summary>
public sealed record EFaturaTaslak(
    string FaturaNo, string Uuid, long Tarih, string ParaBirimi,
    string SaticiVkn, string SaticiUnvan, string? SaticiVergiDairesi, bool SaticiGercekKisi,
    string? CariId, string? CariUnvan,
    IReadOnlyList<EFaturaSatiri> Satirlar, decimal DosyaToplami, decimal HesaplananToplam,
    IReadOnlyList<string> Uyarilar, bool DahaOnceAlindi)
{
    /// <summary>Onay penceresi metni — masaüstü ve web AYNI özeti gösterir.</summary>
    public string Ozet =>
        $"Satıcı: {SaticiUnvan} ({(SaticiGercekKisi ? "TCKN" : "VKN")} {SaticiVkn})\n" +
        $"Fatura no: {FaturaNo} · Tarih: {DateTimeOffset.FromUnixTimeMilliseconds(Tarih).UtcDateTime:dd.MM.yyyy}\n" +
        $"{Satirlar.Count} satır · Toplam: {HesaplananToplam:#,0.00} {ParaBirimi}\n" +
        (CariId is null ? "Cari: yeni tedarikçi carisi açılacak" : $"Cari: {CariUnvan}") +
        (Uyarilar.Count == 0 ? "" : "\n\nUyarılar:\n" + string.Join("\n", Uyarilar.Select(u => "• " + u))) +
        "\n\nAlış faturası olarak kaydedilsin mi? (stok etkilenmez)";
}

/// <summary>
/// ⭐ M-52 (AR-GE 2026-10-27, F-141, K-02=a) — e-FATURA XML İÇE ALMA (UBL-TR 1.2, Türkiye e-Fatura/e-Arşiv standardı).
/// Tedarikçiden gelen fatura dosyası okunur → önizleme (satıcı, no, tarih, satırlar, toplam, uyarılar) → kullanıcı onayıyla ALIŞ
/// faturası olarak kaydedilir. Satıcı vergi no / TCKN ile cariye eşlenir; yoksa tedarikçi carisi açılır (M-53 köprüsü tanımı da
/// açar). Satırlar malzemeye BAĞLANMAZ (stok etkilemez — mal kabul ayrı akıştır); satır açıklaması/birim/fiyat/KDV/tevkifat/iskonto
/// dosyadan gelir. Aynı belge (UUID) ikinci kez alınamaz — işlem kimliği <c>efatura:{UUID}</c> (tekrar gönderim zararsız).
/// Hesaplanan toplam dosyadaki ödenecek tutardan 0,05'ten fazla saparsa UYARI (kayıt engellenmez; kullanıcı görür).
/// </summary>
public sealed class EFaturaService
{
    private const string Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private const string Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace B = Cbc, A = Cac;
    public const int MaxBoyut = 5 * 1024 * 1024;

    private readonly IDbConnectionFactory _factory;
    private readonly PartyService _cariler;
    private readonly InvoiceService _faturalar;

    public EFaturaService(IDbConnectionFactory factory, PartyService cariler, InvoiceService faturalar)
    {
        _factory = factory;
        _cariler = cariler;
        _faturalar = faturalar;
    }

    private static decimal Sayi(string? v) =>
        decimal.TryParse((v ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    /// <summary>XML'i güvenli okur (dış varlık/DTD kapalı) ve önizleme üretir. Hatalı dosyada anlaşılır <see cref="ArgumentException"/>.</summary>
    public EFaturaTaslak Onizle(SessionContext s, string xml)
    {
        AccessControl.Require(s, InvoiceService.Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(xml)) throw new ArgumentException("Dosya boş.");
        if (xml.Length > MaxBoyut) throw new ArgumentException("Dosya çok büyük (en fazla 5 MB).");
        XDocument doc;
        try
        {
            using var sr = new StringReader(xml);
            using var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            doc = XDocument.Load(xr);
        }
        catch (XmlException) { throw new ArgumentException("Dosya okunamadı: geçerli bir e-Fatura (UBL-TR XML) değil."); }

        var kok = doc.Root;
        if (kok is null || kok.Name.LocalName != "Invoice") throw new ArgumentException("Dosya bir e-Fatura (Invoice) belgesi değil.");
        string? Deger(XElement? e, params XName[] yol)
        {
            foreach (var ad in yol) { e = e?.Element(ad); if (e is null) return null; }
            return e?.Value.Trim();
        }

        var no = Deger(kok, B + "ID") ?? throw new ArgumentException("Fatura numarası (ID) yok.");
        var uuid = Deger(kok, B + "UUID") ?? throw new ArgumentException("Belge kimliği (UUID) yok.");
        var tarihMetni = Deger(kok, B + "IssueDate") ?? throw new ArgumentException("Fatura tarihi (IssueDate) yok.");
        if (!DateTime.TryParseExact(tarihMetni, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var tarih))
            throw new ArgumentException("Fatura tarihi okunamadı: " + tarihMetni);
        var para = (Deger(kok, B + "DocumentCurrencyCode") ?? "TRY").ToUpperInvariant();
        var uyarilar = new List<string>();
        if (!Money.IsSupported(para)) { uyarilar.Add($"Para birimi {para} desteklenmiyor; TRY olarak alınacak."); para = "TRY"; }

        var taraf = kok.Element(A + "AccountingSupplierParty")?.Element(A + "Party") ?? throw new ArgumentException("Satıcı bilgisi yok.");
        string? vkn = null; var gercek = false;
        foreach (var pid in taraf.Elements(A + "PartyIdentification").Select(x => x.Element(B + "ID")).Where(x => x is not null))
        {
            var sema = pid!.Attribute("schemeID")?.Value?.ToUpperInvariant();
            if (sema is "VKN" or "TCKN") { vkn = pid.Value.Trim(); gercek = sema == "TCKN"; break; }
        }
        if (string.IsNullOrEmpty(vkn)) throw new ArgumentException("Satıcının vergi no / T.C. kimlik no'su yok.");
        var unvan = Deger(taraf, A + "PartyName", B + "Name");
        if (string.IsNullOrWhiteSpace(unvan))
        {
            var kisi = taraf.Element(A + "Person");
            unvan = $"{Deger(kisi, B + "FirstName")} {Deger(kisi, B + "FamilyName")}".Trim();
        }
        if (string.IsNullOrWhiteSpace(unvan)) unvan = "e-Fatura satıcısı " + vkn;
        var vd = Deger(taraf, A + "PartyTaxScheme", A + "TaxScheme", B + "Name");

        var satirlar = new List<EFaturaSatiri>();
        foreach (var l in kok.Elements(A + "InvoiceLine"))
        {
            var miktarE = l.Element(B + "InvoicedQuantity");
            var miktar = Sayi(miktarE?.Value);
            var fiyat = Sayi(Deger(l, A + "Price", B + "PriceAmount"));
            var ad = Deger(l, A + "Item", B + "Name") ?? Deger(l, B + "Note") ?? "e-Fatura satırı";
            var kdv = l.Element(A + "TaxTotal")?.Elements(A + "TaxSubtotal").Select(x => Sayi(Deger(x, B + "Percent"))).DefaultIfEmpty(0m).Max() ?? 0m;
            var tevkifat = Sayi(Deger(l, A + "WithholdingTaxTotal", A + "TaxSubtotal", B + "Percent"));
            var iskonto = l.Elements(A + "AllowanceCharge")
                .Where(x => Deger(x, B + "ChargeIndicator") == "false")
                .Select(x => Sayi(Deger(x, B + "MultiplierFactorNumeric")) * 100m).DefaultIfEmpty(0m).Sum();
            if (miktar <= 0) { uyarilar.Add($"\"{ad}\" satırında miktar yok; 1 alındı."); miktar = 1m; }
            satirlar.Add(new EFaturaSatiri(ad, miktarE?.Attribute("unitCode")?.Value, miktar, fiyat, Math.Min(iskonto, 100m), kdv, tevkifat));
        }
        if (satirlar.Count == 0) throw new ArgumentException("Faturada satır (InvoiceLine) yok.");

        var dosyaToplami = Sayi(Deger(kok, A + "LegalMonetaryTotal", B + "PayableAmount"));
        var hesap = satirlar.Sum(x => x.Matrah + Math.Round(x.Matrah * x.KdvOrani / 100m, 2) - Math.Round(x.Matrah * x.KdvOrani / 100m * x.TevkifatOrani / 100m, 2));
        if (dosyaToplami > 0 && Math.Abs(dosyaToplami - hesap) > 0.05m)
            uyarilar.Add($"Hesaplanan toplam ({hesap:0.00}) dosyadaki ödenecek tutardan ({dosyaToplami:0.00}) farklı — satırları kontrol edin.");

        var (cariId, cariUnvan) = CariBul(s.CompanyId, vkn, gercek);
        var once = DahaOnceAlindi(s.CompanyId, uuid);
        if (once) uyarilar.Add("Bu e-Fatura daha önce içe alınmış; yeniden kaydedilmez.");
        if (cariId is null) uyarilar.Add($"Vergi no {vkn} ile cari bulunamadı; \"{unvan}\" adına tedarikçi carisi açılacak.");

        return new EFaturaTaslak(no, uuid, new DateTimeOffset(tarih, TimeSpan.Zero).ToUnixTimeMilliseconds(), para, vkn, unvan, vd, gercek,
            cariId, cariUnvan, satirlar, dosyaToplami, Math.Round(hesap, 2), uyarilar, once);
    }

    /// <summary>Önizlemeyi ALIŞ faturası olarak kaydeder (cari yoksa açar). Aynı UUID ikinci kez kaydedilmez (mevcut sonucu döndürür).</summary>
    public InvoiceResult IceAl(SessionContext s, string xml, string? branchId = null)
    {
        var t = Onizle(s, xml);
        var cariId = t.CariId;
        if (cariId is null)
        {
            var kod = "EF-" + t.SaticiVkn;
            cariId = _cariler.Create(s, new NewParty(kod.Length > 40 ? kod[..40] : kod, t.SaticiUnvan.Length > 200 ? t.SaticiUnvan[..200] : t.SaticiUnvan,
                PartyTypes.Supplier, IsPerson: t.SaticiGercekKisi, TaxOffice: t.SaticiVergiDairesi,
                TaxNo: t.SaticiGercekKisi ? null : t.SaticiVkn, NationalId: t.SaticiGercekKisi ? t.SaticiVkn : null,
                Currency: t.ParaBirimi, Note: "e-Fatura içe alımıyla açıldı"));
        }
        return _faturalar.Create(s, new NewInvoice(
            InvoiceDirections.Purchase, cariId,
            t.Satirlar.Select(x => new NewInvoiceLine(null, x.Aciklama, x.Birim, x.Miktar, x.BirimFiyat, x.IskontoOrani, x.KdvOrani, x.TevkifatOrani)).ToList(),
            OperationId: "efatura:" + t.Uuid, ExternalNo: t.FaturaNo, BranchId: branchId, InvoiceDate: t.Tarih, Currency: t.ParaBirimi,
            Note: $"e-Fatura {t.FaturaNo}", AffectsStock: false));
    }

    private (string? Id, string? Unvan) CariBul(string companyId, string vkn, bool gercek)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title FROM parties WHERE company_id=@c AND is_deleted=0 AND " +
                          (gercek ? "national_id=@v" : "tax_no=@v") + " ORDER BY is_active DESC, created_at LIMIT 1;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", vkn);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.GetString(1)) : (null, null);
    }

    private bool DahaOnceAlindi(string companyId, string uuid)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM invoices WHERE company_id=@c AND operation_id=@o;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@o", "efatura:" + uuid);
        return cmd.ExecuteScalar() is not null;
    }
}
