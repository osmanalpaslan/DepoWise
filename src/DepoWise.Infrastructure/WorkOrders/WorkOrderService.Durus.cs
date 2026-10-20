using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.WorkOrders;

/// <summary>⭐ M-38: bir aracın dönemdeki arıza duruşu.</summary>
public sealed record DurusSatiri(string VehicleId, string AracKodu, int ArizaSayisi, int AcikAriza, decimal ToplamSaat)
{
    public string Satir => $"{AracKodu} · {ArizaSayisi} arıza · {ToplamSaat:0.#} saat duruş" + (AcikAriza > 0 ? $" · {AcikAriza} arıza hâlâ açık" : "");
}

/// <summary>
/// ═══ M-38 (AR-GE 2026-10-19, 05 F-080) — DURUŞ SÜRESİ RAPORU ═══
/// Arıza akışı (M-35) zaten "arızalı → iş emri (taslak/devam) → tamamlandı" durumlarını taşır; bu rapor dönem içindeki arıza
/// iş emirlerinden araç başına duruş süresini toplar. Süre = bildirim → tamamlanma; açık arıza "şimdi"ye kadar sayılır;
/// döneme taşan kısım kesilir (yalnız [başlangıç, bitiş] içindeki saatler). İptal edilen iş emri sayılmaz. Salt okuma.
/// </summary>
public sealed partial class WorkOrderService
{
    public IReadOnlyList<DurusSatiri> DurusRaporu(SessionContext s, long bas, long bit)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (bit < bas) throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!ArizaDestekli(conn, null)) return Array.Empty<DurusSatiri>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT w.fault_vehicle_id, v.internal_code, w.created_at, w.actual_end, w.status FROM work_orders w " +
                          "JOIN vehicles v ON v.id=w.fault_vehicle_id AND v.company_id=w.company_id " +
                          "WHERE w.company_id=@c AND w.fault_vehicle_id IS NOT NULL AND w.is_deleted=0 AND w.status<>'cancelled' " +
                          "AND w.created_at<=@bit;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@bit", bit);
        var ham = new List<(string V, string Kod, long Bas, long Son, bool Acik)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var acik = r.GetString(4) != "completed";
                var son = acik || r.IsDBNull(3) ? now : Convert.ToInt64(r.GetValue(3));
                ham.Add((r.GetString(0), r.GetString(1), Convert.ToInt64(r.GetValue(2)), Math.Max(son, Convert.ToInt64(r.GetValue(2))), acik));
            }
        return ham.Where(x => x.Son >= bas)
            .GroupBy(x => (x.V, x.Kod))
            .Select(g => new DurusSatiri(g.Key.V, g.Key.Kod, g.Count(), g.Count(x => x.Acik),
                Math.Round(g.Sum(x => Math.Max(0L, Math.Min(x.Son, bit) - Math.Max(x.Bas, bas))) / 3_600_000m, 1)))
            .OrderByDescending(x => x.ToplamSaat).ThenBy(x => x.AracKodu).ToList();
    }
}
