using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>Bir aracın son yakıt dolumunda tüketimi kendi ortalamasının belirgin üstünde.</summary>
/// <param name="Last">Son geçerli dolumun tüketimi (L/km ya da L/saat).</param>
/// <param name="Baseline">Önceki dolumların toplam litre / toplam sayaç farkı (ağırlıklı ortalama).</param>
public sealed record FuelAnomaly(string VehicleId, string DistributionId, decimal Last, decimal Baseline,
    string MeterUnit, long Date, int Samples)
{
    public decimal ExcessPercent => Baseline <= 0 ? 0 : (Last / Baseline - 1m) * 100m;
    public string UnitText => MeterUnit == DepoWise.Application.Ui.MeterUnitOptions.Hour ? "L/saat" : "L/km";
}

/// <summary>Kira bitişi yaklaşan ya da geçmiş ama hâlâ aktif kiralık araç.</summary>
public sealed record RentalExpiry(string VehicleId, string? Company, long End, int DaysLeft)
{
    public bool Expired => DaysLeft < 0;
}

/// <summary>
/// ⭐ 2026-10-10 (kullanıcının seçtiği öneriler 1 ve 2) — FİLO UYARILARI. Ana ekran / Uyarılar
/// (<see cref="DashboardService"/>) bunları masaüstü ve web için AYNI hesapla üretir (tek kaynak).
///
/// <b>Yakıt tüketim sapması:</b> her dolumun tüketimi = litre ÷ (güncel sayaç − önceki sayaç). Aracın son
/// <see cref="PencereGun"/> gündeki ÖNCEKİ dolumlarının ağırlıklı ortalaması (Σlitre ÷ Σfark) taban kabul edilir;
/// son dolum tabandan <see cref="EsikOran"/> kat fazlaysa uyarı (≥ <see cref="KritikOran"/> → kritik).
/// Kalkanlar (yanlış alarm üretmesin): en az <see cref="MinOrnek"/> geçerli dolum; son dolumun sayaç farkı
/// km'de ≥ 20, saatte ≥ 2 (çok kısa aralıkta tek dolum oranı şişirir); iptal edilen kayıt (is_deleted=1) sayılmaz.
///
/// <b>Kira bitişi:</b> kiralık (is_rental=1), pasif olmayan, bitişi bugünden en çok <see cref="KiraUyariGun"/> gün
/// sonra olan ya da GEÇMİŞ olan araç. Bitişi geçtiği hâlde aktif duran araç kritik (iade/uzatma unutulmuş).
/// Şube kapsamı araç şubesi üzerinden uygulanır (Araç Listesi ile aynı kural).
/// </summary>
public static class FleetAlerts
{
    public const decimal EsikOran = 1.30m;
    public const decimal KritikOran = 1.60m;
    public const int MinOrnek = 5;
    public const int PencereGun = 180;
    public const int KiraUyariGun = 7;
    private const long Gun = 86_400_000L;

    public static List<FuelAnomaly> FuelAnomalies(DbConnection conn, SessionContext s, long nowMs)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT fd.id, fd.vehicle_id, fd.prev_meter, fd.current_meter, fd.liters, fd.distribution_date, v.meter_unit
FROM fuel_distributions fd
JOIN vehicles v ON v.id = fd.vehicle_id AND v.company_id = fd.company_id
WHERE fd.company_id=@c AND fd.is_deleted=0 AND v.is_deleted=0 AND fd.distribution_date >= @since"
            + BranchScope.Sql(s, "v.branch_id") + @"
ORDER BY fd.vehicle_id, fd.distribution_date, fd.created_at;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@since", nowMs - PencereGun * Gun);
        if (BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);

        var araclar = new Dictionary<string, List<(string Id, decimal Liters, decimal Diff, long Date, string Unit)>>(StringComparer.Ordinal);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                if (r.IsDBNull(2) || r.IsDBNull(3)) continue;
                var fark = Money.Parse(Convert.ToString(r.GetValue(3))) - Money.Parse(Convert.ToString(r.GetValue(2)));
                var litre = Money.Parse(Convert.ToString(r.GetValue(4)));
                if (fark <= 0 || litre <= 0) continue;   // sayaç girilmemiş / geri gitmiş → ölçülemez
                var vid = r.GetString(1);
                if (!araclar.TryGetValue(vid, out var l)) araclar[vid] = l = new();
                l.Add((r.GetString(0), litre, fark, Convert.ToInt64(r.GetValue(5)), r.GetString(6)));
            }

        var sonuc = new List<FuelAnomaly>();
        foreach (var (vid, l) in araclar)
        {
            if (l.Count < MinOrnek) continue;
            var son = l[^1];
            var asgariFark = son.Unit == DepoWise.Application.Ui.MeterUnitOptions.Hour ? 2m : 20m;
            if (son.Diff < asgariFark) continue;
            var onceki = l.Take(l.Count - 1).ToList();
            var taban = onceki.Sum(x => x.Liters) / onceki.Sum(x => x.Diff);
            var sonTuketim = son.Liters / son.Diff;
            if (taban > 0 && sonTuketim >= taban * EsikOran)
                sonuc.Add(new FuelAnomaly(vid, son.Id, sonTuketim, taban, son.Unit, son.Date, l.Count));
        }
        return sonuc;
    }

    public static List<RentalExpiry> RentalExpiries(DbConnection conn, SessionContext s, long nowMs)
    {
        var bugun = nowMs - nowMs % Gun;   // UTC gün başı (kira tarihleri de UTC gün başı tutulur)
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT v.id, v.rental_company, v.rental_end FROM vehicles v
WHERE v.company_id=@c AND v.is_deleted=0 AND v.is_rental=1 AND v.status<>@p
  AND v.rental_end IS NOT NULL AND v.rental_end <= @sinir" + BranchScope.Sql(s, "v.branch_id") + @"
ORDER BY v.rental_end;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@p", DepoWise.Application.Ui.VehicleStatus.Passive);
        cmd.AddWithValue("@sinir", bugun + KiraUyariGun * Gun);
        if (BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        var list = new List<RentalExpiry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var bitis = Convert.ToInt64(r.GetValue(2));
            list.Add(new RentalExpiry(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), bitis,
                (int)Math.Floor((bitis - bugun) / (double)Gun)));
        }
        return list;
    }
}
