using DepoWise.Application.Requests;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DepoWise.Infrastructure.Requests;

/// <summary>
/// QuestPDF ile talep PDF'i (Community lisansı). Türkçe karakterler korunur (UTF-8 + uygun font).
/// Düzen: başlık (şirket + belge no + durum), şantiye/personel şeridi, kalem tablosu, 3 imza alanı.
/// </summary>
public sealed class RequestPdfService : IRequestPdfService
{
    static RequestPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Generate(RequestPdfModel m, bool economic = false)
    {
        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Text(m.CompanyName).FontSize(16).Bold();
                    col.Item().Text("MALZEME TALEP FORMU").FontSize(13).SemiBold();
                    col.Item().Text($"Belge No: {m.DocNo}    Tarih: {m.RequestDate}    Durum: {m.Status}");
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text($"Şantiye: {m.BranchName ?? "-"}");
                    col.Item().Text($"Talep Eden: {m.RequesterName ?? "-"}    Depo Sorumlusu: {m.WarehouseName ?? "-"}");
                    if (!string.IsNullOrWhiteSpace(m.Description))
                        col.Item().Text($"Açıklama: {m.Description}");

                    col.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(80);
                            c.RelativeColumn();
                            c.ConstantColumn(70);
                            c.ConstantColumn(110);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderCell).Text("Kod");
                            h.Cell().Element(HeaderCell).Text("Malzeme");
                            h.Cell().Element(HeaderCell).Text("Miktar");
                            h.Cell().Element(HeaderCell).Text("Araç");
                        });
                        foreach (var it in m.Items)
                        {
                            table.Cell().Element(BodyCell).Text(it.MaterialCode);
                            table.Cell().Element(BodyCell).Text(it.MaterialName);
                            table.Cell().Element(BodyCell).Text(it.Quantity.ToString("0.##"));
                            table.Cell().Element(BodyCell).Text(it.VehicleLabel ?? "-");
                        }
                    });
                });

                page.Footer().PaddingTop(20).Row(row =>
                {
                    row.RelativeItem().AlignCenter().Text($"Talep Eden\n{m.RequesterName ?? "____"}");
                    row.RelativeItem().AlignCenter().Text($"Depo Sorumlusu\n{m.WarehouseName ?? "____"}");
                    row.RelativeItem().AlignCenter().Text($"Onaylayan\n{m.ApproverName ?? "____"}");
                });
            });
        }).GeneratePdf();

        IContainer HeaderCell(IContainer c) => economic
            ? c.Padding(4).BorderBottom(1)
            : c.Background(Colors.Grey.Lighten2).Padding(4).BorderBottom(1);
        IContainer BodyCell(IContainer c) => economic
            ? c.Padding(4).BorderBottom(1)
            : c.Padding(4).BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
    }
}
