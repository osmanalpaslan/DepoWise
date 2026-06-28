using DepoWise.Application.Requests;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DepoWise.Infrastructure.Requests;

/// <summary>
/// QuestPDF ile talep PDF'i (Community lisansı). İki düzen:
/// <b>Standart</b> = lacivert başlık bandı + (varsa) firma logosu + mavi başlıklı zebra tablo + çerçeveli imza kutuları.
/// <b>Ekonomik</b> = sade siyah-beyaz başlık + kod sütunlu tablo + çizgili imza alanları (toner tasarruflu).
/// İmza adları seçilen personellerden gelir (RequesterName/WarehouseName/ApproverName).
/// </summary>
public sealed class RequestPdfService : IRequestPdfService
{
    private const string Navy = "#1F2A44";
    private const string NavyDark = "#16213A";
    private const string Blue = "#3B6FF6";
    private const string Zebra = "#EEF3FF";

    static RequestPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(RequestPdfModel m, bool economic = false)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

            if (economic) BuildEconomic(page, m);
            else BuildStandard(page, m);
        })).GeneratePdf();
    }

    // ═══════════ STANDART (renkli) ═══════════
    private static void BuildStandard(PageDescriptor page, RequestPdfModel m)
    {
        page.Content().Column(root =>
        {
            // Başlık bandı (lacivert): logo + başlık + belge no/tarih
            root.Item().Background(Navy).Padding(12).Row(row =>
            {
                if (!string.IsNullOrWhiteSpace(m.LogoPath) && System.IO.File.Exists(m.LogoPath))
                    row.ConstantItem(110).Height(48).AlignMiddle().Image(m.LogoPath).FitHeight();

                row.RelativeItem().AlignMiddle().PaddingLeft(14)
                   .Text("MALZEME TALEP FORMU").FontColor(Colors.White).FontSize(16).Bold();

                row.ConstantItem(150).AlignMiddle().Column(c =>
                {
                    c.Item().AlignRight().Text(t => { t.Span("Belge No: ").FontColor(Colors.White); t.Span(m.DocNo).FontColor(Colors.White).Bold(); });
                    c.Item().AlignRight().Text($"Tarih: {m.RequestDate}").FontColor(Colors.White).FontSize(9);
                });
            });

            // Şantiye şeridi
            root.Item().Background(NavyDark).PaddingVertical(7).PaddingHorizontal(12).Text(t =>
            {
                t.Span("Talep Eden Şantiye: ").FontColor(Colors.White);
                t.Span(m.BranchName ?? "-").FontColor(Colors.White).Bold();
            });

            // Tablo
            root.Item().PaddingTop(14).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(36);   // #
                    c.RelativeColumn();     // Malzeme Adı
                    c.ConstantColumn(80);   // Birimi
                    c.ConstantColumn(70);   // Adet
                    c.ConstantColumn(150);  // Talep Edilen Araç
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeadBlue).Text("#");
                    h.Cell().Element(HeadBlue).Text("Malzeme Adı");
                    h.Cell().Element(HeadBlue).Text("Birimi");
                    h.Cell().Element(HeadBlue).Text("Adet");
                    h.Cell().Element(HeadBlue).Text("Talep Edilen Araç");
                });
                int i = 0;
                foreach (var it in m.Items)
                {
                    i++;
                    string bg = i % 2 == 0 ? Zebra : "#FFFFFF";
                    IContainer Cell(IContainer c) => c.Background(bg).PaddingVertical(7).PaddingHorizontal(8);
                    table.Cell().Element(Cell).Text(i.ToString());
                    table.Cell().Element(Cell).Text(it.MaterialName);
                    table.Cell().Element(Cell).Text(it.Unit);
                    table.Cell().Element(Cell).Text(it.Quantity.ToString("0.##"));
                    table.Cell().Element(Cell).Element(c => VehicleCell(c, it));
                }
            });

            if (!string.IsNullOrWhiteSpace(m.Description))
                root.Item().PaddingTop(12).Text(t => { t.Span("Açıklama: ").Bold(); t.Span(m.Description); });

            // İmza kutuları (çerçeveli)
            root.Item().PaddingTop(28).Row(row =>
            {
                row.RelativeItem().Element(c => SignBox(c, "Talep Eden", m.RequesterName));
                row.ConstantItem(14);
                row.RelativeItem().Element(c => SignBox(c, "Depo Sorumlusu", m.WarehouseName));
                row.ConstantItem(14);
                row.RelativeItem().Element(c => SignBox(c, "Onaylayan", m.ApproverName));
            });
        });

        static IContainer HeadBlue(IContainer c) => c.Background(Blue).PaddingVertical(8).PaddingHorizontal(8)
            .DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold());
    }

    private static void SignBox(IContainer c, string title, string? name)
    {
        c.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Item().Height(46);                          // imza boşluğu
            col.Item().Text(title).Bold();
            col.Item().Text(name ?? "____").FontColor(Colors.Grey.Darken1);
            col.Item().PaddingTop(6).Text("Tarih: ____________").FontColor(Colors.Grey.Medium).FontSize(8);
        });
    }

    private static void VehicleCell(IContainer c, RequestPdfItem it)
    {
        if (string.IsNullOrWhiteSpace(it.VehicleCode)) { c.Text("-"); return; }
        c.Column(col =>
        {
            col.Item().Text(it.VehicleCode);
            if (!string.IsNullOrWhiteSpace(it.VehicleChassis))
                col.Item().Text($"Şase: {it.VehicleChassis}").FontColor(Colors.Grey.Medium).FontSize(8);
        });
    }

    // ═══════════ EKONOMİK (sade) ═══════════
    private static void BuildEconomic(PageDescriptor page, RequestPdfModel m)
    {
        page.Content().Column(root =>
        {
            root.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("MALZEME TALEP FORMU").FontSize(19).Bold();
                    c.Item().Text(t => { t.Span("Şantiye: "); t.Span(m.BranchName ?? "-").Bold(); });
                });
                row.ConstantItem(170).AlignMiddle().Column(c =>
                {
                    c.Item().AlignRight().Text(t => { t.Span("Belge No: "); t.Span(m.DocNo).Bold(); });
                    c.Item().AlignRight().Text($"Tarih: {m.RequestDate}").FontSize(9);
                });
            });

            root.Item().PaddingTop(8).BorderBottom(1.5f).BorderColor(Colors.Black);

            root.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(30);   // #
                    c.ConstantColumn(95);   // Malzeme Kodu
                    c.RelativeColumn();     // Malzeme Adı
                    c.ConstantColumn(70);   // Birimi
                    c.ConstantColumn(55);   // Adet
                    c.ConstantColumn(150);  // Talep Edilen Araç
                });
                table.Header(h =>
                {
                    h.Cell().Element(HeadPlain).Text("#");
                    h.Cell().Element(HeadPlain).Text("Malzeme Kodu");
                    h.Cell().Element(HeadPlain).Text("Malzeme Adı");
                    h.Cell().Element(HeadPlain).Text("Birimi");
                    h.Cell().Element(HeadPlain).Text("Adet");
                    h.Cell().Element(HeadPlain).Text("Talep Edilen Araç");
                });
                int i = 0;
                foreach (var it in m.Items)
                {
                    i++;
                    table.Cell().Element(BodyPlain).Text(i.ToString());
                    table.Cell().Element(BodyPlain).Text(it.MaterialCode);
                    table.Cell().Element(BodyPlain).Text(it.MaterialName);
                    table.Cell().Element(BodyPlain).Text(it.Unit);
                    table.Cell().Element(BodyPlain).Text(it.Quantity.ToString("0.##"));
                    table.Cell().Element(BodyPlain).Element(c => VehicleCell(c, it));
                }
            });

            if (!string.IsNullOrWhiteSpace(m.Description))
                root.Item().PaddingTop(10).Text(t => { t.Span("Açıklama: ").Bold(); t.Span(m.Description); });

            // İmza: çizgi + başlık + ad (ortalı)
            root.Item().PaddingTop(40).Row(row =>
            {
                row.RelativeItem().Element(c => SignLine(c, "Talep Eden", m.RequesterName));
                row.ConstantItem(20);
                row.RelativeItem().Element(c => SignLine(c, "Depo Sorumlusu", m.WarehouseName));
                row.ConstantItem(20);
                row.RelativeItem().Element(c => SignLine(c, "Onaylayan", m.ApproverName));
            });
        });

        static IContainer HeadPlain(IContainer c) => c.PaddingVertical(6).PaddingHorizontal(6)
            .BorderBottom(1).BorderColor(Colors.Black).DefaultTextStyle(x => x.Bold());
        static IContainer BodyPlain(IContainer c) => c.PaddingVertical(7).PaddingHorizontal(6)
            .BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
    }

    private static void SignLine(IContainer c, string title, string? name)
    {
        c.Column(col =>
        {
            col.Item().PaddingHorizontal(20).BorderTop(1).BorderColor(Colors.Black);
            col.Item().PaddingTop(4).AlignCenter().Text(title).Bold();
            col.Item().AlignCenter().Text(name ?? "____");
        });
    }
}
