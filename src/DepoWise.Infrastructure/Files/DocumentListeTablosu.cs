using DepoWise.Application.Reports;

namespace DepoWise.Infrastructure.Files;

/// <summary>
/// ⭐ M-54 (AR-GE 2026-10-24, F-057): belge (evrak) listesinin Excel/Yazdır tablosu — ekrandaki bilgilerle aynı. Dosya içeriği
/// DEĞİL yalnız listenin kendisi aktarılır. Masaüstü belgeleri sunucudan okuduğu için dışa aktarımı da sunucunun aynı ucundan indirir.
/// </summary>
public static class DocumentListeTablosu
{
    private static string Gun(long? ms) => ms is { } v ? DateTimeOffset.FromUnixTimeMilliseconds(v).UtcDateTime.ToString("dd.MM.yyyy") : "";

    public static TableModel ToTableModel(IReadOnlyList<DocumentRow> rows)
        => new("Belgeler", new[] { "Başlık", "Bağlı Kayıt Türü", "Bağlı Kayıt", "Belge Türü", "Geçerlilik Başlangıcı", "Geçerlilik Sonu", "Dosya", "Boyut (KB)", "Yüklenme", "Açıklama" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.Title, r.EntityTypeDisplay, r.EntityLabel, r.DocType ?? "", Gun(r.ValidFrom), Gun(r.ValidUntil), r.FileName,
                r.SizeBytes is { } b ? Math.Round(b / 1024m, 1) : null, Gun(r.CreatedAt), r.Description ?? "",
            }).ToList(),
            new[] { false, false, false, false, false, false, false, true, false, false });
}
