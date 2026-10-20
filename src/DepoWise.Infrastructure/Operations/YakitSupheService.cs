using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Operations;

/// <summary>⭐ M-44: şüpheli yakıt dağıtımı ve nedenleri.</summary>
public sealed record SupheliYakit(string DistributionId, string VehicleId, string AracKodu, long Tarih, decimal Litre,
    decimal OncekiSayac, decimal Sayac, IReadOnlyList<string> Nedenler)
{
    public string Ozet => $"{DateTimeOffset.FromUnixTimeMilliseconds(Tarih).UtcDateTime:dd.MM.yyyy} · {AracKodu} · {Litre:0.##} L · " +
                          $"sayaç {OncekiSayac:N0} → {Sayac:N0} · " + string.Join("; ", Nedenler);
}

/// <summary>
/// ═══ M-44 (AR-GE 2026-10-19, 05 F-117) — YAKITTA ŞÜPHELİ GİRİŞLERİN OTOMATİK İŞARETLENMESİ ═══
/// Kayıt engellenmez ve DEĞİŞTİRİLMEZ; geçerli (iptal edilmemiş) dağıtımlar şu kurallarla taranır ve liste olarak gösterilir:
/// <list type="bullet">
///   <item>sayaç geri (güncel &lt; önceki) · şüpheli sayaç sıçraması (<see cref="MeterRule.SuspiciousJump"/>) · çok büyük litre;</item>
///   <item>tüketim sapması: litre / sayaç farkı, aracın kendi ortanca tüketiminin <see cref="SapmaKat"/> katından fazla ya da
///   1/<see cref="SapmaKat"/>'ından az (en az <see cref="AsgariOrnek"/> karşılaştırılabilir kayıt varsa);</item>
///   <item>aynı araca aynı gün birden fazla dağıtım.</item>
/// </list>
/// Hatalı sayaç "geçersiz say" (M-36) ile, hatalı kayıt düzeltme/iptal ile giderilir — bu servis yalnız işaretler.
/// </summary>
public sealed class YakitSupheService
{
    public const decimal SapmaKat = 2.5m;
    public const int AsgariOrnek = 4;
    private readonly IDbConnectionFactory _factory;

    public YakitSupheService(IDbConnectionFactory factory) => _factory = factory;

    private sealed record Kayit(string Id, string Arac, string Kod, long Tarih, decimal Litre, decimal Onceki, decimal Sayac, bool SayacGecersiz);

    /// <summary>Dönemdeki şüpheli dağıtımlar (UTC ms aralığı; null = tümü), en yeni üstte.</summary>
    public IReadOnlyList<SupheliYakit> Supheliler(SessionContext s, long? bas = null, long? bit = null)
    {
        AccessControl.Require(s, "fuel", PermissionAction.View);
        using var conn = _factory.Create();
        var gv = DbIntrospect.ColumnExists(conn, null, "fuel_distributions", "meter_void") ? "f.meter_void" : "0";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT f.id, f.vehicle_id, v.internal_code, f.distribution_date, f.liters, f.prev_meter, f.current_meter, {gv} " +
                          "FROM fuel_distributions f JOIN vehicles v ON v.id=f.vehicle_id AND v.company_id=f.company_id " +
                          "WHERE f.company_id=@c AND f.is_deleted=0 ORDER BY f.vehicle_id, f.distribution_date, f.created_at;";
        cmd.AddWithValue("@c", s.CompanyId);
        var tum = new List<Kayit>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                tum.Add(new Kayit(r.GetString(0), r.GetString(1), r.GetString(2), Convert.ToInt64(r.GetValue(3)),
                    Money.Parse(r.GetString(4)), r.IsDBNull(5) ? 0m : Money.Parse(r.GetString(5)),
                    r.IsDBNull(6) ? 0m : Money.Parse(r.GetString(6)), Convert.ToInt64(r.GetValue(7)) != 0));

        var sonuc = new List<SupheliYakit>();
        foreach (var arac in tum.GroupBy(x => x.Arac))
        {
            // Aracın ortanca tüketimi (litre / sayaç farkı) — yalnız geçerli sayaçlı, farkı pozitif kayıtlar.
            var oranlar = arac.Where(k => !k.SayacGecersiz && k.Sayac > k.Onceki && k.Onceki > 0)
                .Select(k => k.Litre / (k.Sayac - k.Onceki)).OrderBy(x => x).ToList();
            decimal? ortanca = oranlar.Count >= AsgariOrnek ? oranlar[oranlar.Count / 2] : null;
            var gunler = arac.GroupBy(k => k.Tarih - k.Tarih % 86_400_000L).Where(g => g.Count() > 1).SelectMany(g => g).Select(k => k.Id).ToHashSet();
            foreach (var k in arac)
            {
                if (bas is { } b && k.Tarih < b) continue;
                if (bit is { } e && k.Tarih > e) continue;
                var n = new List<string>();
                if (!k.SayacGecersiz)
                {
                    if (k.Sayac < k.Onceki) n.Add("sayaç geri gitmiş");
                    else if (MeterRule.SuspiciousJump(k.Onceki, k.Sayac)) n.Add("şüpheli sayaç sıçraması");
                    if (ortanca is { } o && o > 0 && k.Sayac > k.Onceki && k.Onceki > 0)
                    {
                        var oran = k.Litre / (k.Sayac - k.Onceki);
                        if (oran > o * SapmaKat) n.Add($"tüketim aracın ortalamasının {oran / o:0.#} katı (fazla yakıt)");
                        else if (oran < o / SapmaKat) n.Add("tüketim ortalamanın çok altında (sayaç ya da litre hatalı olabilir)");
                    }
                }
                if (DepoWise.Application.Ui.FieldChecks.IsSuspiciouslyLarge(k.Litre)) n.Add("çok büyük litre");
                if (gunler.Contains(k.Id)) n.Add("aynı gün aynı araca birden fazla dağıtım");
                if (n.Count > 0) sonuc.Add(new SupheliYakit(k.Id, k.Arac, k.Kod, k.Tarih, k.Litre, k.Onceki, k.Sayac, n));
            }
        }
        return sonuc.OrderByDescending(x => x.Tarih).ToList();
    }
}
