using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

/// <summary>⭐ M-27: yoldaki transferin bir kalemi (gönderilen / teslim alınan / kayıp / hâlâ yolda).</summary>
public sealed record YoldaKalem(string MaterialId, string MaterialCode, string MaterialName, decimal Gonderilen, decimal TeslimAlinan,
    decimal Kayip, decimal Yolda);

/// <summary>⭐ M-27: teslim bekleyen (ya da eksik teslim alınmış) iki adımlı transfer.</summary>
public sealed record YoldaTransfer(string DocumentId, string DocNo, long DocDate, string? FromBranchId, string? FromBranchName,
    string? ToBranchId, string? ToBranchName, string Durum, IReadOnlyList<YoldaKalem> Kalemler)
{
    public string DurumMetni => Durum switch { "yolda" => "Yolda", "eksik" => "Eksik teslim", "teslim" => "Teslim alındı", "kapandi" => "Kayıpla kapandı", _ => Durum };
    public string Ozet => $"{DocNo} · {DateTimeOffset.FromUnixTimeMilliseconds(DocDate).ToLocalTime():dd.MM.yyyy} · " +
                          $"{FromBranchName ?? "—"} → {ToBranchName ?? "—"} · {DurumMetni} · {Kalemler.Count} kalem, yolda {Kalemler.Sum(k => k.Yolda):0.####}";
}

/// <summary>
/// ═══ M-27 (AR-GE 2026-10-18, 05 F-070) — İKİ ADIMLI TRANSFER: GÖNDER → TESLİM AL ("YOLDA") ═══
///
/// Tek adımlı transferde mal kaynaktan çıktığı anda hedefte görünürdü; nakliyede kaybolan mal fark edilmezdi. İki adımlı
/// transferde gönderimde mal kaynaktan <b>yolda</b> lokasyonuna geçer (<see cref="StockBalanceWriter.Transit"/>); hedef depo
/// teslim aldığı miktarı girer ve mal yoldan hedefe geçer. Eksik gelen miktar yolda kalır (raporda görünür) ve gerekçeyle
/// <b>kayıp olarak kapatılabilir</b> (yoldan çıkış). Her adım stok defterine hareket olarak yazılır (silme/üzerine yazma yok),
/// firma toplamı kayıp dışında değişmez. Tek adımlı transfer aynen kalır (seçim kullanıcıda).
/// </summary>
public sealed partial class StockService
{
    private static readonly string[] AcikYolda = { "yolda", "eksik" };

    private static bool YoldaDestekli(DbConnection conn, DbTransaction? tx)
        => DbIntrospect.ColumnExists(conn, tx, "stock_documents", "transit_status");

    /// <summary>GÖNDER: kaynaktan yolda lokasyonuna. Kurallar tek adımlı transferle aynı (kaynak = kullanıcının şubesi,
    /// hedef zorunlu, eksi stok kalkanı, tekrar gönderim zararsız).</summary>
    public StockDocResult YoldaGonder(SessionContext s, IReadOnlyList<StockLine> lines, string fromBranchId, string toBranchId,
        string operationId, string? note = null, long? docDate = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (lines.Count == 0) throw new ArgumentException("En az bir malzeme seçin.");
        if (lines.Any(l => l.Quantity <= 0)) throw new ArgumentException("Transfer miktarı pozitif olmalı.");
        fromBranchId = EnforceOwnBranch(s, fromBranchId, "transfer") ?? fromBranchId;
        if (string.IsNullOrEmpty(fromBranchId)) throw new ArgumentException("Kaynak şube belirlenemedi.");
        if (string.IsNullOrEmpty(toBranchId)) throw new ArgumentException("Hedef depo/şantiye seçin. \"Atanmamış\" hedef olarak seçilemez.");
        if (fromBranchId == toBranchId) throw new ArgumentException("Kaynak ve hedef şube aynı olamaz.");
        using (var kontrol = _factory.Create())
            if (!YoldaDestekli(kontrol, null))
                throw new InvalidOperationException("Veritabanı iki adımlı transferi henüz desteklemiyor (güncelleme bekleniyor).");
        var groupId = Guid.NewGuid().ToString("N");
        return RunDocument(s, "transfer", operationId, toBranchId, fromBranchId, toBranchId, null, null, note, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    ApplyLine(conn, tx, s, docId, lines[i], -1, $"{operationId}:{i}:out", "transfer", fromBranchId, fromBranchId, groupId);
                    ApplyLine(conn, tx, s, docId, lines[i], +1, $"{operationId}:{i}:yol", "transfer", StockBalanceWriter.Transit, fromBranchId, groupId);
                }
                using var u = conn.CreateCommand();
                u.Transaction = tx;
                u.CommandText = "UPDATE stock_documents SET transit_status='yolda' WHERE id=@d;";
                u.AddWithValue("@d", docId);
                u.ExecuteNonQuery();
            }, groupId);
    }

    private sealed record YoldaBaslik(string Id, string DocNo, long DocDate, string? From, string? To, string? Durum);

    private static YoldaBaslik YoldaOku(DbConnection conn, DbTransaction? tx, string companyId, string documentId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, doc_no, doc_date, from_branch_id, to_branch_id, transit_status FROM stock_documents " +
                          "WHERE id=@d AND company_id=@c AND doc_type='transfer';";
        cmd.AddWithValue("@d", documentId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Transfer belgesi bulunamadı veya başka firmaya ait.");
        return new YoldaBaslik(r.GetString(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5));
    }

    /// <summary>Belgenin malzeme başına gönderilen / teslim alınan / kayıp / yolda miktarları (defterden).</summary>
    private static List<YoldaKalem> YoldaKalemleri(DbConnection conn, DbTransaction? tx, string companyId, string documentId, string? hedef)
    {
        var sira = new List<string>();
        var d = new Dictionary<string, (string Kod, string Ad, decimal Gon, decimal Tes, decimal Kay, decimal Yol)>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT sm.material_id, m.code, m.name, sm.branch_id, sm.direction, sm.quantity, sm.movement_type FROM stock_movements sm " +
                          "JOIN materials m ON m.id=sm.material_id AND m.company_id=sm.company_id " +
                          "WHERE sm.document_id=@d AND sm.company_id=@c ORDER BY sm.created_at, sm.id;";
        cmd.AddWithValue("@d", documentId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            if (!d.TryGetValue(id, out var v)) { sira.Add(id); v = (r.GetString(1), r.GetString(2), 0m, 0m, 0m, 0m); }
            var sube = r.IsDBNull(3) ? null : r.GetString(3);
            var yon = r.GetInt64(4);
            var q = Money.Parse(r.GetString(5));
            var tur = r.GetString(6);
            if (sube == StockBalanceWriter.Transit)
            {
                v.Yol += yon * q;
                if (yon > 0) v.Gon += q;
                else if (tur == "out") v.Kay += q;   // kayıp kapatma = yoldan çıkış
            }
            else if (sube == hedef && yon > 0) v.Tes += q;
            d[id] = v;
        }
        return sira.Select(id => { var v = d[id]; return new YoldaKalem(id, v.Kod, v.Ad, v.Gon, v.Tes, v.Kay, v.Yol); }).ToList();
    }

    /// <summary>TESLİM AL: hedef depo gelen miktarı girer (gönderilenden fazla olamaz; eksik gelirse fark yolda kalır).
    /// Yetki: stok oluşturma + hedef şube kullanıcının kapsamında. Tekrar gönderim (aynı işlem kimliği) zararsız.</summary>
    public void TeslimAl(SessionContext s, string documentId, IReadOnlyList<StockLine> alinan, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        StockBalanceWriter.Run(() => TeslimAlOnce(s, documentId, alinan, operationId), $"transit-receive:{documentId} op={operationId}");
    }

    private void TeslimAlOnce(SessionContext s, string documentId, IReadOnlyList<StockLine> alinan, string operationId)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        if (FindDocumentByOperation(conn, tx, s.CompanyId, operationId) is not null) { tx.Commit(); return; }   // tekrar gönderim
        if (!YoldaDestekli(conn, tx)) throw new InvalidOperationException("Veritabanı iki adımlı transferi henüz desteklemiyor (güncelleme bekleniyor).");
        var b = YoldaOku(conn, tx, s.CompanyId, documentId);
        if (b.Durum is null || !AcikYolda.Contains(b.Durum)) throw new InvalidOperationException("Bu transfer teslim beklemiyor.");
        BranchAccess.Require(s, b.To, "transfer teslim alma");
        var kalemler = YoldaKalemleri(conn, tx, s.CompanyId, documentId, b.To).ToDictionary(k => k.MaterialId, StringComparer.Ordinal);
        var satirlar = alinan.Where(x => x.Quantity > 0).ToList();
        if (satirlar.Count == 0) throw new ArgumentException("Teslim alınan miktarı girin.");
        foreach (var g in satirlar.GroupBy(x => x.MaterialId))
        {
            if (!kalemler.TryGetValue(g.Key, out var k)) throw new ArgumentException("Bu transferde olmayan malzeme teslim alınamaz.");
            if (g.Sum(x => x.Quantity) > k.Yolda)
                throw new ArgumentException($"{k.MaterialName}: teslim alınan ({g.Sum(x => x.Quantity):0.####}) yoldaki miktardan ({k.Yolda:0.####}) fazla.");
        }

        var groupId = Guid.NewGuid().ToString("N");
        for (int i = 0; i < satirlar.Count; i++)
        {
            ApplyLine(conn, tx, s, documentId, satirlar[i], -1, $"{operationId}:{i}:yol", "transfer", StockBalanceWriter.Transit, b.From, groupId);
            ApplyLine(conn, tx, s, documentId, satirlar[i], +1, $"{operationId}:{i}:in", "transfer", b.To, StockBalanceWriter.Transit, groupId);
        }
        var kalan = YoldaKalemleri(conn, tx, s.CompanyId, documentId, b.To).Sum(k => k.Yolda);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using (var u = conn.CreateCommand())
        {
            u.Transaction = tx;
            u.CommandText = "UPDATE stock_documents SET transit_status=@st, received_at=@n, updated_at=@n WHERE id=@d AND company_id=@c;";
            u.AddWithValue("@st", kalan > 0 ? "eksik" : "teslim");
            u.AddWithValue("@n", now);
            u.AddWithValue("@d", documentId);
            u.AddWithValue("@c", s.CompanyId);
            u.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "stock_document", documentId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"teslimAlindi\":true,\"yoldaKalan\":\"{Money.Serialize(kalan)}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>KAYIP KAPAT: eksik teslimde yolda kalan miktar gerekçeyle stoktan düşülür (yoldan çıkış) ve transfer kapanır.
    /// Yetki: stok düzenleme + ters kayıt düğmesi (iptalle aynı ağırlık).</summary>
    public void YoldaKayipKapat(SessionContext s, string documentId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        AccessControl.RequireButton(s, SpecialButtons.Reverse);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Kayıp gerekçesi zorunlu.");
        StockBalanceWriter.Run(() =>
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginImmediate();
            if (!YoldaDestekli(conn, tx)) throw new InvalidOperationException("Veritabanı iki adımlı transferi henüz desteklemiyor (güncelleme bekleniyor).");
            var b = YoldaOku(conn, tx, s.CompanyId, documentId);
            if (b.Durum is "kapandi" or "teslim") { tx.Commit(); return; }   // zaten kapalı — zararsız
            if (b.Durum is null) throw new InvalidOperationException("Bu transfer tek adımlıdır; yolda miktarı yok.");
            BranchAccess.Require(s, b.To, "yolda kayıp kapatma");
            var satirlar = YoldaKalemleri(conn, tx, s.CompanyId, documentId, b.To).Where(k => k.Yolda > 0).ToList();
            for (int i = 0; i < satirlar.Count; i++)
                ApplyLine(conn, tx, s, documentId, new StockLine(satirlar[i].MaterialId, satirlar[i].Yolda), -1,
                    $"transit-loss:{documentId}:{i}", "out", StockBalanceWriter.Transit, StockBalanceWriter.Transit);
            var now = _clock.UtcNow.ToUnixTimeMilliseconds();
            using (var u = conn.CreateCommand())
            {
                u.Transaction = tx;
                u.CommandText = "UPDATE stock_documents SET transit_status='kapandi', updated_at=@n WHERE id=@d AND company_id=@c;";
                u.AddWithValue("@n", now);
                u.AddWithValue("@d", documentId);
                u.AddWithValue("@c", s.CompanyId);
                u.ExecuteNonQuery();
            }
            AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "stock_document", documentId, AuditActions.Update, s.UserId,
                AfterJson: $"{{\"yoldaKayip\":\"{Money.Serialize(satirlar.Sum(k => k.Yolda))}\",\"reason\":\"{reason.Trim().Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}"), _clock);
            tx.Commit();
        }, $"transit-loss:{documentId}");
    }

    /// <summary>Teslim bekleyen / eksik teslim alınmış transferler (kullanıcının kapsamındaki kaynak ya da hedef).</summary>
    public IReadOnlyList<YoldaTransfer> Yoldakiler(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        if (!YoldaDestekli(conn, null)) return Array.Empty<YoldaTransfer>();
        var basliklar = new List<(YoldaBaslik B, string? FromAd, string? ToAd)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT d.id, d.doc_no, d.doc_date, d.from_branch_id, d.to_branch_id, d.transit_status, bf.name, bt.name " +
                              "FROM stock_documents d LEFT JOIN branches bf ON bf.id=d.from_branch_id AND bf.company_id=d.company_id " +
                              "LEFT JOIN branches bt ON bt.id=d.to_branch_id AND bt.company_id=d.company_id " +
                              "WHERE d.company_id=@c AND d.doc_type='transfer' AND d.transit_status IN ('yolda','eksik') ORDER BY d.doc_date, d.doc_no;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                basliklar.Add((new YoldaBaslik(r.GetString(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5)), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return basliklar
            .Where(x => BranchAccess.CanAccess(s, x.B.From) || BranchAccess.CanAccess(s, x.B.To))
            .Select(x => new YoldaTransfer(x.B.Id, x.B.DocNo, x.B.DocDate, x.B.From, x.FromAd, x.B.To, x.ToAd, x.B.Durum!,
                YoldaKalemleri(conn, null, s.CompanyId, x.B.Id, x.B.To)))
            .ToList();
    }
}
