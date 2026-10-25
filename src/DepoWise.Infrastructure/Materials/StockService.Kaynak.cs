using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

/// <summary>
/// ⭐ M-56 2/2 (AR-GE 2026-10-25, F-090): stok hareketinin KAYNAĞI — hareketi doğuran kayıt.
/// <paramref name="Tur"/>: siparis · talep · is_emri · fatura · arac_bakimi · ekipman_bakimi.
/// <paramref name="EkranKey"/> + <paramref name="HedefId"/> masaüstü <c>ShellViewModel.NavigateTo</c> içindir;
/// <paramref name="WebYolu"/> web sayfasının derin bağlantısıdır. <paramref name="Erisim"/> false ise kullanıcı hedef ekranı
/// göremez: etiket gösterilir, gitme kapalıdır (yetkisiz ekrana yönlendirme yok).
/// </summary>
public sealed record HareketKaynagi(string Tur, string Etiket, string EkranKey, string HedefId, string WebYolu, bool Erisim);

public sealed partial class StockService
{
    /// <summary>
    /// Hareketin kaynağını bulur (yoksa null). Sıra: satın alma siparişi → talep → iş emri → fatura (belgeye bağlılar),
    /// belgesiz bakım tüketiminde (<c>usage</c>/<c>usage_reverse</c>, not = bakım kimliği) araç ya da ekipman bakımı.
    /// Her sorgu oturumun firmasıyla süzülür (başka firmanın kaydı hiç bulunmaz); stok görüntüleme yetkisi şart.
    /// </summary>
    public HareketKaynagi? KaynakBul(SessionContext s, string? documentId, string? movementType, string? note)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        HareketKaynagi K(string tur, string etiket, string modul, string ekran, string hedef, string web) =>
            new(tur, etiket, ekran, hedef, web, AccessControl.Can(s, modul, PermissionAction.View));

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            if (DbIntrospect.ColumnExists(conn, null, "stock_documents", "purchase_order_id") &&
                Tek(conn, "SELECT o.id, o.order_no FROM stock_documents d JOIN purchase_orders o ON o.id=d.purchase_order_id AND o.company_id=d.company_id " +
                          "WHERE d.id=@d AND d.company_id=@c;", s.CompanyId, documentId) is { } po)
                return K("siparis", "Satın alma siparişi " + po.No, "purchasing", "purchasing", po.Id, "/purchasing?ac=" + po.Id);
            if (DbIntrospect.ColumnExists(conn, null, "stock_documents", "request_id") &&
                Tek(conn, "SELECT r.id, r.doc_no FROM stock_documents d JOIN material_requests r ON r.id=d.request_id AND r.company_id=d.company_id " +
                          "WHERE d.id=@d AND d.company_id=@c;", s.CompanyId, documentId) is { } rq)
                return K("talep", "Talep " + rq.No, "requests", "requests:form", rq.Id, "/requests?ac=" + rq.Id);
            if (Tek(conn, "SELECT w.id, w.wo_no FROM work_order_links l JOIN work_orders w ON w.id=l.work_order_id AND w.company_id=l.company_id " +
                          "WHERE l.entity_type='stock_document' AND l.entity_id=@d AND l.company_id=@c AND l.is_deleted=0 ORDER BY l.created_at;",
                    s.CompanyId, documentId) is { } wo)
                return K("is_emri", "İş emri " + wo.No, "work_orders", "work_orders", wo.Id, "/work-orders?ac=" + wo.Id);
            if (Tek(conn, "SELECT id, invoice_no FROM invoices WHERE stock_document_id=@d AND company_id=@c ORDER BY created_at;",
                    s.CompanyId, documentId) is { } fa)
                return K("fatura", "Fatura " + fa.No, InvoiceModul, "invoices", "fatura:" + fa.Id, "/invoices?ac=" + fa.Id);
            return null;
        }

        if (movementType is "usage" or "usage_reverse" && !string.IsNullOrWhiteSpace(note))
        {
            if (Tek(conn, "SELECT m.id, v.internal_code, m.vehicle_id FROM vehicle_maintenances m JOIN vehicles v ON v.id=m.vehicle_id AND v.company_id=m.company_id " +
                          "WHERE m.id=@d AND m.company_id=@c;", s.CompanyId, note) is { } vm)
                return K("arac_bakimi", "Araç bakımı — " + vm.No, "maintenance", "maintenance:records", "bakim:" + vm.Id + ":" + vm.Ust, "/maintenance?view=" + vm.Id);
            if (Tek(conn, "SELECT m.id, e.code, m.equipment_id FROM equipment_maintenances m JOIN equipment e ON e.id=m.equipment_id AND e.company_id=m.company_id " +
                          "WHERE m.id=@d AND m.company_id=@c;", s.CompanyId, note) is { } em)
                return K("ekipman_bakimi", "Ekipman bakımı — " + em.No, "maintenance", "maintenance:records", "ekbakim:" + em.Id + ":" + em.Ust, "/maintenance?ekview=" + em.Id);
        }
        return null;
    }

    private const string InvoiceModul = DepoWise.Infrastructure.Accounting.InvoiceService.Module;

    /// <summary>Tek satır: kimlik, numara/kod ve (varsa) üst kayıt — bakımın aracı/ekipmanı (masaüstü listeyi ona daraltır).</summary>
    private static (string Id, string No, string Ust)? Tek(DbConnection conn, string sql, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddWithValue("@d", id);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.FieldCount > 2 && !r.IsDBNull(2) ? r.GetString(2) : "") : null;
    }
}
