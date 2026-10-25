using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using System.Data.Common;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// ═══ M-57 (AR-GE 2026-10-25, 05 F-091) — YÖNETİCİ KARAR RAPORLARI ═══
///  • stock-negative      Eksi Stok İstisna Raporu — K-05/K-06 istisnalarıyla eksiye düşen (malzeme × depo) bakiyeleri
///  • stock-value-month   Ay Sonu Stok Değeri — seçilen bitiş anındaki miktar (defterden) × kart birim fiyatı
///  • machine-utilization Makine Kullanım Oranı — dönemde çalışılan gün / dönem günü + arıza duruşu
/// "Birim maliyet" Araç Raporu'nda zaten vardır (Birim Başına Maliyet) — yeni rapor açılmadı.
/// Üçü de salt okumadır; yeni tablo/migration YOK. Depo kapsamı Stok Durumu ile AYNI kuraldır (BranchAccess.Allowed).
/// </summary>
public sealed partial class ReportService
{
    /// <summary>Stok raporlarının DEPO kapsamı — <see cref="StockStatus"/> ile aynı kural: kullanıcı yetkisi olmayan
    /// depoyu isteyemez (fail-closed), filtre boşken izinli depolar + «Atanmamış». <c>null</c> = sınırsız.
    /// <c>Bos</c> = yalnız kapsam dışı depo istendi → boş sonuç.</summary>
    private static (IReadOnlyList<string>? Konumlar, bool Bos) StokKonumKapsami(SessionContext s, ReportRequest req)
    {
        var izinli = BranchAccess.Allowed(s);
        var secilen = NormalizeLocations(req.LocationIds);
        if (izinli is null) return (secilen.Count == 0 ? null : secilen, false);
        var izinliSet = new HashSet<string>(izinli, StringComparer.Ordinal);
        if (secilen.Count == 0) return (izinli.Append(StockBalanceWriter.Unassigned).Distinct().ToList(), false);
        var suzulen = secilen.Where(x => x.Length == 0 || izinliSet.Contains(x)).ToList();
        return (suzulen, suzulen.Count == 0);
    }

    private static string KonumSql(IReadOnlyList<string>? konumlar, string col, DbCommand cmd)
    {
        if (konumlar is null) return "";
        var ps = new List<string>();
        for (int i = 0; i < konumlar.Count; i++) { var p = "@kl" + i; ps.Add(p); cmd.AddWithValue(p, konumlar[i]); }
        var bosVar = konumlar.Any(x => x.Length == 0);
        var parca = ps.Count > 0 ? $"COALESCE({col},'') IN ({string.Join(",", ps)})" : "1=0";
        return " AND (" + parca + (bosVar ? $" OR {col} IS NULL" : "") + ")";
    }

    private static string KonumAdi(string ad) => ad.Length > 0 ? ad : "📦 Atanmamış";

    /// <summary>
    /// EKSİ STOK İSTİSNA RAPORU. Negatif stok yasaktır; bilinçli istisnalar (K-05: bakım tüketimi, açılış stoğu) ve
    /// çevrimdışı iki bilgisayarın aynı stoğu düşmesi (K-06) eksi bakiye bırakabilir. Bu rapor TÜM eksi
    /// (malzeme × depo) bakiyelerini (ana sayfadaki 20 satırlık kutudan farklı olarak sınırsız) ve eksiye
    /// düşüren son çıkışı gösterir. Bakiye METİN saklanır → karşılaştırma C#'ta decimal ile yapılır.
    /// </summary>
    public TableModel NegativeStock(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        string[] basliklar = { "Kod", "Malzeme", "Depo / Şantiye", "Stok", "Son Çıkış Tarihi", "Son Çıkış Türü", "Son Çıkış Notu" };
        var numeric = new[] { false, false, false, true, false, false, false };
        var (konumlar, bos) = StokKonumKapsami(s, req);
        if (bos) return new TableModel("Eksi Stok İstisna Raporu", basliklar, Array.Empty<IReadOnlyList<object?>>(), numeric);

        using var conn = _factory.Create();
        var eksi = new List<(string Mat, string Kod, string Ad, string Loc, string Depo, decimal Q)>();
        using (var cmd = conn.CreateCommand())
        {
            var kSql = KonumSql(konumlar, "b.location_id", cmd);
            cmd.CommandText = @"
SELECT b.material_id, m.code, m.name, COALESCE(b.location_id,''), COALESCE(br.name,''), b.quantity
FROM stock_balances b
JOIN materials m ON m.id = b.material_id AND m.company_id = b.company_id AND m.is_deleted = 0
LEFT JOIN branches br ON br.id = b.location_id AND br.company_id = b.company_id
WHERE b.company_id = @c AND b.quantity LIKE '-%'" + kSql + ";";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var q = Money.Parse(r.GetString(5));
                if (q < 0m) eksi.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), q));
            }
        }

        // Son çıkış: eksi bakiyeli malzemelerin çıkış yönlü hareketleri TEK sorguda (N+1 yok); en yenisi C#'ta seçilir.
        var sonCikis = new Dictionary<(string, string), (long T, string Tur, string Not)>();
        if (eksi.Count > 0)
        {
            using var cmd = conn.CreateCommand();
            var ids = eksi.Select(x => x.Mat).Distinct().ToList();
            cmd.CommandText = "SELECT sm.material_id, COALESCE(sm.branch_id,''), " + StockMovementFilterSql.IslemTarihiSql +
                              ", sm.movement_type, COALESCE(sm.note,'') FROM stock_movements sm " +
                              "LEFT JOIN stock_documents d ON d.id = sm.document_id AND d.company_id = sm.company_id " +
                              "WHERE sm.company_id=@c AND sm.direction < 0" + InList("sm.material_id", "@nm", ids) + ";";
            cmd.AddWithValue("@c", companyId);
            BindList(cmd, "@nm", ids);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = (r.GetString(0), r.GetString(1));
                var t = Convert.ToInt64(r.GetValue(2));
                if (!sonCikis.TryGetValue(key, out var e) || t > e.T) sonCikis[key] = (t, r.GetString(3), r.GetString(4));
            }
        }

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var x in eksi.OrderBy(x => x.Q).ThenBy(x => x.Kod, StringComparer.Ordinal))
        {
            sonCikis.TryGetValue((x.Mat, x.Loc), out var c);
            rows.Add(new object?[]
            {
                x.Kod, x.Ad, KonumAdi(x.Depo), new NumCell((double)x.Q, x.Q.ToString("#,##0.##", Tr)),
                c.T > 0 ? D(c.T) : "", c.T > 0 ? DepoWise.Application.Ui.MovementTypeOptions.Label(c.Tur) : "", c.Not,
            });
        }
        return new TableModel("Eksi Stok İstisna Raporu", basliklar, rows, numeric);
    }

    /// <summary>
    /// AY SONU STOK DEĞERİ. Miktar, seçilen aralığın BİTİŞ anına kadar olan TÜM stok hareketlerinin toplamıdır
    /// (Σ yön × miktar; tarih = Stok Hareketleri raporuyla aynı işlem tarihi). Bu, stock_balances'ın defterden
    /// yeniden hesaplanmasıyla (StockService.Recompute) AYNI formüldür → bitiş = bugün iken Stok Durumu ile aynı
    /// miktar çıkar (testle kilitli). Değer = miktar × malzeme kartındaki GÜNCEL birim fiyat (geçmiş fiyat
    /// saklanmaz; InfoNote'ta söylenir). Birim fiyat gizliyse fiyat ve değer kolonları tabloya HİÇ konmaz
    /// (MalzemeMaliyetiniSuz). Para birimleri dönüştürülmez; toplam yalnız tek para biriminde verilir.
    /// </summary>
    public TableModel StockValueAtMonthEnd(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        string[] basliklar = { "Kod", "Malzeme", "Stok", "Birim Fiyat", "Para Birimi", "Stok Değeri" };
        var numeric = new[] { false, false, true, true, false, true };
        var (konumlar, bos) = StokKonumKapsami(s, req);
        var son = req.ToDate ?? _clock.UtcNow.ToUnixTimeMilliseconds();
        var baslik = "Ay Sonu Stok Değeri — " + D(son);
        if (bos) return new TableModel(baslik, basliklar, Array.Empty<IReadOnlyList<object?>>(), numeric);

        using var conn = _factory.Create();
        var miktar = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            var kSql = KonumSql(konumlar, "sm.branch_id", cmd);
            cmd.CommandText = "SELECT sm.material_id, sm.direction, sm.quantity FROM stock_movements sm " +
                              "LEFT JOIN stock_documents d ON d.id = sm.document_id AND d.company_id = sm.company_id " +
                              "WHERE sm.company_id=@c AND " + StockMovementFilterSql.IslemTarihiSql + " <= @son" + kSql + ";";
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@son", son);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var q = Money.Parse(r.IsDBNull(2) ? null : r.GetString(2));
                miktar.TryGetValue(r.GetString(0), out var cur);
                miktar[r.GetString(0)] = cur + Convert.ToInt64(r.GetValue(1)) * q;
            }
        }

        var rows = new List<IReadOnlyList<object?>>();
        decimal toplam = 0;
        var birimler = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, code, name, unit_price, COALESCE(currency_code,'TRY') FROM materials " +
                              "WHERE company_id=@c AND is_deleted=0 ORDER BY code;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!miktar.TryGetValue(r.GetString(0), out var q) || q == 0m) continue;
                var fiyat = Money.Parse(r.IsDBNull(3) ? null : r.GetString(3));
                var deger = Math.Round(q * fiyat, 2);
                var pb = r.GetString(4);
                rows.Add(new object?[]
                {
                    r.GetString(1), r.GetString(2), new NumCell((double)q, q.ToString("#,##0.##", Tr)),
                    new NumCell((double)fiyat, fiyat.ToString("#,##0.00", Tr)), pb,
                    new NumCell((double)deger, deger.ToString("#,##0.00", Tr)),
                });
                toplam += deger;
                birimler.Add(pb);
            }
        }
        IReadOnlyList<object?>? totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM", "", "", "", birimler.Count == 1 ? birimler.First() : "",
            birimler.Count == 1 ? new NumCell((double)toplam, toplam.ToString("#,##0.00", Tr)) : (object?)"",
        };
        return new TableModel(baslik, basliklar, rows, numeric, totalRow);
    }

    /// <summary>
    /// MAKİNE KULLANIM ORANI. Araç başına: dönem günü (başlangıç → min(bitiş, bugün), UTC gün), ÇALIŞILAN gün
    /// (o gün yakıt fişi VEYA sayaç artışı kaydı olan gün — ikisi aynı gün sayılır, çift sayılmaz), kullanım
    /// oranı = çalışılan ÷ dönem günü, dönem sayaç artışı ve arıza duruşu (M-38 DurusRaporu — TEK kaynak; iş emri
    /// görme yetkisi yoksa arıza kolonları tabloya konmaz). Tam filo listelenir: hiç çalışmayan makine %0 ile
    /// görünür (atıl makineyi bulmak raporun amacıdır). Şube/araç/araç türü kapsamı Araç Raporu ile aynıdır.
    /// </summary>
    public TableModel MachineUtilization(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        const long GunMs = 86_400_000L;
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var bas = req.FromDate ?? 0;
        var son = Math.Min(req.ToDate ?? now, now);
        var basGun = bas / GunMs;
        var sonGun = son / GunMs;
        var donemGun = sonGun >= basGun ? (int)(sonGun - basGun) + 1 : 0;

        using var conn = _factory.Create();
        var araclar = new List<(string Id, string Sube, string Kod, string Plaka, string Tur, string Birim)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT v.id, COALESCE(bch.name,''), v.internal_code, COALESCE(v.plate,''), COALESCE(vt.name,''), v.meter_unit
FROM vehicles v
LEFT JOIN vehicle_types vt ON vt.id=v.vehicle_type_id
LEFT JOIN branches bch ON bch.id=v.branch_id
WHERE v.company_id=@c AND v.is_deleted=0" + ReportScope.BranchSql(s, req, "v.branch_id")
                + InList("v.id", "@rv", req.VehicleIds) + InList("v.vehicle_type_id", "@rt", req.VehicleTypeIds) + @"
ORDER BY COALESCE(bch.name,''), v.internal_code;";
            cmd.AddWithValue("@c", companyId);
            ReportScope.BindBranch(cmd, s, req);
            BindList(cmd, "@rv", req.VehicleIds);
            BindList(cmd, "@rt", req.VehicleTypeIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                araclar.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        }

        // Çalışılan günler + sayaç artışı: firma geneli iki sorgu (araç başına sorgu YOK), araç kapsamı C#'ta.
        var gunler = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        var artis = new Dictionary<string, decimal>(StringComparer.Ordinal);
        void GunEkle(string v, long ms) { if (!gunler.TryGetValue(v, out var h)) gunler[v] = h = new HashSet<long>(); h.Add(ms / GunMs); }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT vehicle_id, distribution_date FROM fuel_distributions " +
                              "WHERE company_id=@c AND is_deleted=0 AND vehicle_id IS NOT NULL AND distribution_date BETWEEN @b AND @s;";
            cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@b", bas); cmd.AddWithValue("@s", son);
            using var r = cmd.ExecuteReader();
            while (r.Read()) GunEkle(r.GetString(0), Convert.ToInt64(r.GetValue(1)));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT vehicle_id, old_value, new_value, created_at FROM vehicle_meter_logs " +
                              "WHERE company_id=@c AND created_at BETWEEN @b AND @s" +
                              // ilk kayıt (0 → başlangıç sayacı), iptal ve yeniden hesaplama ÇALIŞMA değildir
                              " AND source <> 'vehicle_create' AND source <> 'meter_void' AND source NOT LIKE 'recalc:%';";
            cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@b", bas); cmd.AddWithValue("@s", son);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var fark = Money.Parse(r.GetString(2)) - Money.Parse(r.GetString(1));
                if (fark <= 0m) continue;   // düzeltme/geri alma çalışma sayılmaz
                var v = r.GetString(0);
                GunEkle(v, Convert.ToInt64(r.GetValue(3)));
                artis.TryGetValue(v, out var cur);
                artis[v] = cur + fark;
            }
        }

        var arizaGorunur = AccessControl.Can(s, "work_orders", PermissionAction.View);
        var durus = arizaGorunur && son >= bas
            ? new DepoWise.Infrastructure.WorkOrders.WorkOrderService(_factory, _clock).DurusRaporu(s, bas, son)
                .ToDictionary(x => x.VehicleId, StringComparer.Ordinal)
            : new Dictionary<string, DepoWise.Infrastructure.WorkOrders.DurusSatiri>(StringComparer.Ordinal);

        var rows = new List<IReadOnlyList<object?>>();
        int tCalisan = 0, tAriza = 0;
        decimal tDurus = 0;
        foreach (var a in araclar)
        {
            var calisan = gunler.TryGetValue(a.Id, out var h) ? h.Count(g => g >= basGun && g <= sonGun) : 0;
            var oran = donemGun > 0 ? Math.Round(100m * calisan / donemGun, 1) : 0m;
            artis.TryGetValue(a.Id, out var sayac);
            durus.TryGetValue(a.Id, out var d);
            rows.Add(new object?[]
            {
                a.Sube, a.Kod, a.Plaka, a.Tur, a.Birim == "hour" ? "Saat" : "KM",
                new NumCell(donemGun, donemGun.ToString("#,##0", Tr)),
                new NumCell(calisan, calisan.ToString("#,##0", Tr)),
                new NumCell((double)oran, "%" + oran.ToString("0.0", Tr)),
                Num((double)sayac, x => FmtDistance(x, a.Birim)),
                new NumCell(d?.ArizaSayisi ?? 0, (d?.ArizaSayisi ?? 0).ToString("#,##0", Tr)),
                new NumCell((double)(d?.ToplamSaat ?? 0m), (d?.ToplamSaat ?? 0m).ToString("#,##0.0", Tr)),
            });
            tCalisan += calisan; tAriza += d?.ArizaSayisi ?? 0; tDurus += d?.ToplamSaat ?? 0m;
        }
        IReadOnlyList<object?>? totalRow = null;
        if (rows.Count > 0)
        {
            var tOran = donemGun > 0 ? Math.Round(100m * tCalisan / (donemGun * (decimal)rows.Count), 1) : 0m;
            totalRow = new object?[]
            {
                "TOPLAM", "", "", "", "", "",
                new NumCell(tCalisan, tCalisan.ToString("#,##0", Tr)),
                new NumCell((double)tOran, "%" + tOran.ToString("0.0", Tr)),
                "",
                new NumCell(tAriza, tAriza.ToString("#,##0", Tr)),
                new NumCell((double)tDurus, tDurus.ToString("#,##0.0", Tr)),
            };
        }
        var table = new TableModel("Makine Kullanım Oranı", new[]
        {
            "Şube", "Araç İç Kod", "Plaka", "Araç Türü", "Sayaç Birimi", "Dönem Günü", "Çalışılan Gün",
            "Kullanım Oranı", "Sayaç Artışı", "Arıza Sayısı", "Arıza Duruşu (Saat)",
        }, rows, new[] { false, false, false, false, false, true, true, true, true, true, true }, totalRow);
        return arizaGorunur ? table : KolonlariCikar(table, new[] { "Arıza Sayısı", "Arıza Duruşu (Saat)" });
    }
}
