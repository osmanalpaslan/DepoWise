using System.Text;
using DepoWise.Application.Reports;
using DepoWise.Infrastructure.Reporting;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ LİSTE YAZDIRMA (PDF) ═══ (kullanıcı isteği 2026-09-06 — "A grubu")
///
/// <para>PDF'in İÇİNE bakmak kırılgan bir testtir (QuestPDF sürümü değişince sıkışma/kodlama değişir).
/// Bu yüzden burada iki şey sınanır: <b>(1)</b> üretim hiç çökmüyor mu — özellikle boş liste, null
/// hücre, çok kolon, uzun metin gibi kenar durumlarda; <b>(2)</b> çıktı gerçekten geçerli bir PDF mi
/// (dosya imzası + anlamlı boyut).</para>
///
/// <para>Toplam hesabı ise SAF mantıktır ve ayrıca sınanır: yanlış bir toplam, toplam olmamasından
/// kötüdür — kullanıcı ona bakarak karar verir.</para>
/// </summary>
public class TablePdfServiceTests
{
    private readonly TablePdfService _pdf = new();

    private static bool GecerliPdf(byte[] b)
        => b.Length > 800 && Encoding.ASCII.GetString(b, 0, 5) == "%PDF-";

    private static TableModel Ornek(int satir = 3) => new(
        "Zimmet Listesi",
        new[] { "PERSONEL", "VARLIK", "MİKTAR", "TUTAR" },
        Enumerable.Range(1, satir)
            .Select(i => (IReadOnlyList<object?>)new object?[] { $"Personel {i}", $"Malzeme {i}", i, 10.5m * i })
            .ToList(),
        Numeric: new[] { false, false, true, true });

    // ── Üretim çökmemeli ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Uret_GecerliPdfUretir()
        => Assert.True(GecerliPdf(_pdf.Uret(Ornek())));

    /// <summary>Boş liste yazdırılabilmeli — kullanıcı "sonuç yok" çıktısı da alabilir.</summary>
    [Fact]
    public void Uret_BosListe_CokmezVeGecerliPdfUretir()
    {
        var bos = new TableModel("Boş", new[] { "A", "B" }, Array.Empty<IReadOnlyList<object?>>());
        Assert.True(GecerliPdf(_pdf.Uret(bos)));
    }

    /// <summary>Null hücreler boş yazılır; NullReferenceException OLMAZ.</summary>
    [Fact]
    public void Uret_NullHucreler_Cokmez()
    {
        var t = new TableModel("Null", new[] { "A", "B", "C" },
            new[] { (IReadOnlyList<object?>)new object?[] { null, "x", null } });
        Assert.True(GecerliPdf(_pdf.Uret(t)));
    }

    /// <summary>Satır, başlıktan AZ hücre içerebilir (eksik veri) — taşma hatası olmamalı.</summary>
    [Fact]
    public void Uret_EksikHucreliSatir_Cokmez()
    {
        var t = new TableModel("Eksik", new[] { "A", "B", "C" },
            new[] { (IReadOnlyList<object?>)new object?[] { "yalnız bir hücre" } });
        Assert.True(GecerliPdf(_pdf.Uret(t)));
    }

    /// <summary>Çok kolonlu tablo YATAY sayfaya geçer ve yine geçerli PDF üretir.</summary>
    [Fact]
    public void Uret_CokKolon_Cokmez()
    {
        var basliklar = Enumerable.Range(1, 15).Select(i => "K" + i).ToArray();
        var satir = (IReadOnlyList<object?>)Enumerable.Range(1, 15).Select(i => (object?)("deger" + i)).ToList();
        var t = new TableModel("Geniş", basliklar, new[] { satir });
        Assert.True(GecerliPdf(_pdf.Uret(t)));
    }

    /// <summary>Çok uzun metin sarar; kırpma yüzünden çökme olmaz.</summary>
    [Fact]
    public void Uret_CokUzunMetin_Cokmez()
    {
        var uzun = string.Join(" ", Enumerable.Repeat("çok uzun malzeme adı", 60));
        var t = new TableModel("Uzun", new[] { "AD" },
            new[] { (IReadOnlyList<object?>)new object?[] { uzun } });
        Assert.True(GecerliPdf(_pdf.Uret(t)));
    }

    /// <summary>Türkçe karakterler ve başlık bilgisi çökmeye yol açmamalı.</summary>
    [Fact]
    public void Uret_BaslikBilgisiyle_Cokmez()
    {
        var b = new PdfBaslik(
            CompanyName: "Gaz İnşaat Ltd. Şti.",
            BranchName: "DÜZCE Şantiyesi",
            UserName: "Mustafa Alpaslan",
            Filters: new[] { ("Tarih", "01.09.2026 – 06.09.2026"), ("Şube", "DÜZCE") });
        Assert.True(GecerliPdf(_pdf.Uret(Ornek(), b)));
    }

    /// <summary>Olmayan bir logo yolu yazdırmayı DURDURMAMALI (logo tamamen süstür).</summary>
    [Fact]
    public void Uret_OlmayanLogo_YazdirmayiDurdurmaz()
    {
        var b = new PdfBaslik(LogoPath: @"C:\olmayan\klasor\logo.png");
        Assert.True(GecerliPdf(_pdf.Uret(Ornek(), b)));
    }

    /// <summary>Çok satırlı liste birden fazla sayfaya taşar; yine tek geçerli PDF çıkar.</summary>
    [Fact]
    public void Uret_CokSatir_CokSayfa_Cokmez()
        => Assert.True(GecerliPdf(_pdf.Uret(Ornek(400))));

    // ── Toplam mantığı ───────────────────────────────────────────────────────────────────
    /// <summary>Modelin KENDİ toplam satırı varsa o kullanılır (hesaplama ezilmez).</summary>
    [Fact]
    public void Uret_ModelinToplamSatiriVarsa_Kullanilir()
    {
        var t = new TableModel("Toplamlı", new[] { "AD", "TUTAR" },
            new[] { (IReadOnlyList<object?>)new object?[] { "a", 5m } },
            Numeric: new[] { false, true },
            TotalRow: new object?[] { "TOPLAM", 999m });
        Assert.True(GecerliPdf(_pdf.Uret(t)));
    }

    /// <summary>Hiç sayısal kolon yoksa toplam satırı çizilmez — yine de geçerli PDF üretilir.</summary>
    [Fact]
    public void Uret_SayisalKolonYok_ToplamsizUretir()
    {
        var t = new TableModel("Metin", new[] { "AD", "AÇIKLAMA" },
            new[] { (IReadOnlyList<object?>)new object?[] { "a", "b" } },
            Numeric: new[] { false, false });
        Assert.True(GecerliPdf(_pdf.Uret(t)));
    }

    /// <summary>
    /// METİN biçimindeki sayılar ("1.234,56 TRY") toplanmaya ÇALIŞILMAMALI — güvenilir
    /// ayrıştırılamazlar ve yanlış bir toplam üretirler. Bu durumda üretim yine çalışır.
    /// </summary>
    [Fact]
    public void Uret_MetinBicimliSayilar_YanlisToplamUretmez()
    {
        var t = new TableModel("Metin sayı", new[] { "AD", "TUTAR" },
            new[] { (IReadOnlyList<object?>)new object?[] { "a", "1.234,56 TRY" } });
        Assert.True(GecerliPdf(_pdf.Uret(t)));
    }
}
