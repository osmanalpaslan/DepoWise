using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DepoWise.Application.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Bir yazdırma işinin başlık bilgileri. Hepsi opsiyoneldir; verilmeyen satır çizilmez.
/// </summary>
/// <param name="CompanyName">Firma adı — kâğıt elden ele dolaştığı için sayfada görünmesi gerekir.</param>
/// <param name="BranchName">Şube / şantiye.</param>
/// <param name="UserName">Çıktıyı alan kullanıcı — "bu listeyi kim, ne zaman almış" sorusunun cevabı.</param>
/// <param name="Filters">Uygulanan süzgeçler (etiket → değer). Kâğıda YAZILIR: aksi hâlde eksik bir liste
/// "tam liste" sanılır. Bu, yazdırmada en sık yapılan hatadır.</param>
/// <param name="LogoPath">Firma logosu (varsa).</param>
public sealed record PdfBaslik(
    string? CompanyName = null,
    string? BranchName = null,
    string? UserName = null,
    IReadOnlyList<(string Etiket, string Deger)>? Filters = null,
    string? LogoPath = null);

/// <summary>
/// ═══ LİSTE / TABLO YAZDIRMA (PDF) ═══ (kullanıcı isteği 2026-09-06 — "A grubu")
///
/// <para><b>Neden var.</b> Projede PDF üretimi yalnız <b>Talep Formu</b> için vardı
/// (<c>RequestPdfService</c>). Zimmet tutanağı, malzeme çıkış listesi, stok hareketleri, iş emri,
/// bakım listesi, fatura ve tahsilat dökümü <b>yazdırılamıyordu</b>; kullanıcı Excel'e aktarıp elle
/// biçimlendirmek zorunda kalıyordu. Şantiyede ıslak imzalı kâğıt hâlâ gerekiyor.</para>
///
/// <para><b>Tasarım kararı — neden ekran başına değil, ORTAK.</b> Ekranların hepsi Excel çıktısını
/// zaten ortak <see cref="TableModel"/> ile üretiyor (<c>ToTableModel</c>). Bu servis AYNI modeli
/// alır; böylece Excel'i olan her ekran <b>dört satırlık</b> bir ekleme ile yazdırılabilir hâle gelir
/// ve iki çıktı asla ayrışmaz (aynı kolonlar, aynı satırlar, aynı sıra).</para>
///
/// <para><b>Toplam satırı.</b> <see cref="TableModel.TotalRow"/> verilmişse o kullanılır. Verilmemişse
/// ve sayısal kolon varsa <b>toplam kendiliğinden hesaplanır</b> — kullanıcının ikinci isteği
/// ("listelerde toplam yok") bu sayede yazdırmada da karşılanır. Toplam satırı görsel olarak ayrıdır
/// (kalın, üstten çizgili) ve normal satır sanılmaz.</para>
///
/// <para><b>Sayfa yönü.</b> 6'dan çok kolon varsa YATAY (landscape) — dikeye sıkıştırılan geniş tablo
/// okunmaz hâle gelir. Uzun metin kırpılmaz, satır sarar: kâğıtta "…" işe yaramaz.</para>
/// </summary>
public sealed class TablePdfService
{
    private const string Navy = "#1F2A44";
    private const string Zebra = "#F2F5FA";
    private const string Cizgi = "#C9D2E3";

    static TablePdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Uret(TableModel tablo, PdfBaslik? baslik = null)
    {
        baslik ??= new PdfBaslik();
        var sayisal = SayisalBayraklar(tablo);
        var toplam = tablo.TotalRow ?? ToplamHesapla(tablo, sayisal);
        bool yatay = tablo.Headers.Count > 6;

        return Document.Create(doc => doc.Page(sayfa =>
        {
            sayfa.Size(yatay ? PageSizes.A4.Landscape() : PageSizes.A4);
            sayfa.Margin(1.2f, Unit.Centimetre);
            sayfa.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

            sayfa.Header().Element(e => BasligiCiz(e, tablo, baslik));
            sayfa.Content().PaddingVertical(8).Element(e => TabloyuCiz(e, tablo, sayisal, toplam));
            sayfa.Footer().Row(r =>
            {
                r.RelativeItem().Text(t =>
                {
                    t.Span("Alpnex · ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    t.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm", TrKultur)).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
                r.ConstantItem(120).AlignRight().Text(t =>
                {
                    t.Span("Sayfa ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                    t.Span(" / ").FontSize(8).FontColor(Colors.Grey.Darken1);
                    t.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        })).GeneratePdf();
    }

    private static readonly CultureInfo TrKultur = CultureInfo.GetCultureInfo("tr-TR");

    private static void BasligiCiz(IContainer alan, TableModel tablo, PdfBaslik b)
    {
        alan.Column(kok =>
        {
            kok.Item().Background(Navy).Padding(10).Row(satir =>
            {
                if (!string.IsNullOrWhiteSpace(b.LogoPath) && System.IO.File.Exists(b.LogoPath))
                    satir.ConstantItem(120).Height(44).AlignMiddle().Image(b.LogoPath).FitHeight();

                satir.RelativeItem().AlignMiddle().PaddingLeft(10).Column(c =>
                {
                    c.Item().Text(tablo.Title).FontColor(Colors.White).FontSize(14).Bold();
                    var alt = new List<string>();
                    if (!string.IsNullOrWhiteSpace(b.CompanyName)) alt.Add(b.CompanyName!);
                    if (!string.IsNullOrWhiteSpace(b.BranchName)) alt.Add(b.BranchName!);
                    if (alt.Count > 0)
                        c.Item().Text(string.Join(" · ", alt)).FontColor(Colors.White).FontSize(9);
                });

                satir.ConstantItem(160).AlignMiddle().Column(c =>
                {
                    c.Item().AlignRight().Text(DateTime.Now.ToString("dd.MM.yyyy HH:mm", TrKultur))
                        .FontColor(Colors.White).FontSize(9);
                    if (!string.IsNullOrWhiteSpace(b.UserName))
                        c.Item().AlignRight().Text(b.UserName!).FontColor(Colors.White).FontSize(8);
                    c.Item().AlignRight().Text($"{tablo.Rows.Count} kayıt").FontColor(Colors.White).FontSize(8);
                });
            });

            // Süzgeçler — kâğıtta MUTLAKA görünmeli: aksi hâlde filtrelenmiş bir çıktı "tam liste" sanılır.
            if (b.Filters is { Count: > 0 })
            {
                var metin = string.Join("   ·   ", b.Filters.Select(f => $"{f.Etiket}: {f.Deger}"));
                kok.Item().PaddingTop(4).Text(t =>
                {
                    t.Span("Uygulanan süzgeçler — ").FontSize(8).Bold().FontColor(Colors.Grey.Darken2);
                    t.Span(metin).FontSize(8).FontColor(Colors.Grey.Darken2);
                });
            }
        });
    }

    private static void TabloyuCiz(IContainer alan, TableModel tablo, IReadOnlyList<bool> sayisal,
        IReadOnlyList<object?>? toplam)
    {
        if (tablo.Rows.Count == 0)
        {
            alan.PaddingTop(24).AlignCenter()
                .Text("Bu süzgeçlerle gösterilecek kayıt yok.").FontSize(10).FontColor(Colors.Grey.Darken1);
            return;
        }

        alan.Table(t =>
        {
            t.ColumnsDefinition(k =>
            {
                for (int i = 0; i < tablo.Headers.Count; i++)
                {
                    // Sayısal kolonlar dar, metin kolonları geniş: sayı sütunu boş yer kaplamasın.
                    if (i < sayisal.Count && sayisal[i]) k.RelativeColumn(1f);
                    else k.RelativeColumn(2f);
                }
            });

            t.Header(h =>
            {
                for (int i = 0; i < tablo.Headers.Count; i++)
                {
                    var hucre = h.Cell().Background(Navy).Padding(5);
                    var yazi = hucre.Text(tablo.Headers[i]).FontColor(Colors.White).FontSize(9).Bold();
                    if (i < sayisal.Count && sayisal[i]) yazi.AlignRight();
                }
            });

            for (int r = 0; r < tablo.Rows.Count; r++)
            {
                var satir = tablo.Rows[r];
                bool cift = r % 2 == 1;
                for (int i = 0; i < tablo.Headers.Count; i++)
                {
                    var deger = i < satir.Count ? satir[i] : null;
                    var hucre = t.Cell()
                        .Background(cift ? Zebra : Colors.White)
                        .BorderBottom(0.5f).BorderColor(Cizgi)
                        .Padding(4);
                    var yazi = hucre.Text(Bicimle(deger)).FontSize(8.5f);
                    if (i < sayisal.Count && sayisal[i]) yazi.AlignRight();
                }
            }

            if (toplam is not null)
            {
                for (int i = 0; i < tablo.Headers.Count; i++)
                {
                    var deger = i < toplam.Count ? toplam[i] : null;
                    var hucre = t.Cell().BorderTop(1.4f).BorderColor(Navy).PaddingVertical(5).PaddingHorizontal(4);
                    var metin = i == 0 && deger is null ? "TOPLAM" : Bicimle(deger);
                    var yazi = hucre.Text(metin).FontSize(9).Bold();
                    if (i < sayisal.Count && sayisal[i]) yazi.AlignRight();
                }
            }
        });
    }

    /// <summary>
    /// Kolon "sayısal mı" bayrakları. Model belirtmişse o kullanılır; belirtmemişse ilk dolu değerin
    /// TÜRÜNE bakılır — metin biçiminde gelen sayılar (ör. "1.234,56 TRY") sayısal SAYILMAZ, çünkü
    /// güvenilir toplanamaz; yanlış bir toplam, toplam olmamasından kötüdür.
    /// </summary>
    private static IReadOnlyList<bool> SayisalBayraklar(TableModel tablo)
    {
        if (tablo.Numeric is { Count: > 0 }) return tablo.Numeric;

        var bayrak = new bool[tablo.Headers.Count];
        for (int i = 0; i < bayrak.Length; i++)
        {
            foreach (var satir in tablo.Rows)
            {
                var d = i < satir.Count ? satir[i] : null;
                if (d is null) continue;
                bayrak[i] = d is decimal or double or float or int or long or short;
                break;
            }
        }
        return bayrak;
    }

    /// <summary>
    /// Sayısal kolonların toplamı. Hiç sayısal kolon yoksa <c>null</c> döner (toplam satırı çizilmez).
    /// Yalnız GERÇEK sayı tipleri toplanır (bkz. <see cref="SayisalBayraklar"/> gerekçesi).
    /// </summary>
    private static IReadOnlyList<object?>? ToplamHesapla(TableModel tablo, IReadOnlyList<bool> sayisal)
    {
        if (!sayisal.Any(x => x)) return null;

        var toplam = new object?[tablo.Headers.Count];
        for (int i = 0; i < tablo.Headers.Count; i++)
        {
            if (i >= sayisal.Count || !sayisal[i]) continue;
            decimal t = 0;
            bool bulundu = false;
            foreach (var satir in tablo.Rows)
            {
                var d = i < satir.Count ? satir[i] : null;
                switch (d)
                {
                    case decimal m: t += m; bulundu = true; break;
                    case double db: t += (decimal)db; bulundu = true; break;
                    case float f: t += (decimal)f; bulundu = true; break;
                    case int n: t += n; bulundu = true; break;
                    case long l: t += l; bulundu = true; break;
                    case short sh: t += sh; bulundu = true; break;
                }
            }
            if (bulundu) toplam[i] = t;
        }
        return toplam.Any(x => x is not null) ? toplam : null;
    }

    /// <summary>Hücre metni. Sayılar Türkçe biçimde (binlik ayracı + en çok 2 ondalık), tarihler GG.AA.YYYY.</summary>
    private static string Bicimle(object? d) => d switch
    {
        null => "",
        decimal m => m == Math.Truncate(m) ? m.ToString("#,##0", TrKultur) : m.ToString("#,##0.##", TrKultur),
        double db => db.ToString("#,##0.##", TrKultur),
        float f => f.ToString("#,##0.##", TrKultur),
        int or long or short => Convert.ToDecimal(d).ToString("#,##0", TrKultur),
        DateTime dt => dt.ToString("dd.MM.yyyy", TrKultur),
        DateTimeOffset dto => dto.ToString("dd.MM.yyyy", TrKultur),
        bool b => b ? "Evet" : "Hayır",
        _ => d.ToString() ?? "",
    };
}
