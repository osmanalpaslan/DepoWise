using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Infrastructure.Assignments;

/// <summary>"Kimde ne var" satırı — defterden TÜRETİLİR (Σ yön×miktar &gt; 0 olanlar).</summary>
public sealed record HoldingRow(string PersonnelId, string PersonnelName, string AssetType, string AssetId,
    string AssetLabel, decimal Quantity, string? BranchId, string? BranchName, long LastDocDate)
{
    public string AssetTypeDisplay => AssetType == "equipment" ? "Ekipman" : "Malzeme";
    public string BranchDisplay => string.IsNullOrEmpty(BranchName) ? "—" : BranchName!;
    public string QuantityDisplay => Quantity.ToString("0.####", System.Globalization.CultureInfo.CurrentCulture);
    public string LastDocDateDisplay => DateTimeOffset.FromUnixTimeMilliseconds(LastDocDate).UtcDateTime.ToString("dd.MM.yyyy");
}

/// <summary>Zimmet geçmişi satırı (defter — değişmez).</summary>
public sealed record AssignmentMovementRow(string Id, string AssetType, string AssetId, string AssetLabel,
    string PersonnelId, string PersonnelName, string MovementType, long Direction, decimal Quantity,
    string? BranchId, string? BranchName, string? GroupId, long DocDate, string? Note, long CreatedAt)
{
    public string MovementDisplay => AssignmentService.MovementLabel(MovementType);
    public string AssetTypeDisplay => AssetType == "equipment" ? "Ekipman" : "Malzeme";
    public string DocDateDisplay => DateTimeOffset.FromUnixTimeMilliseconds(DocDate).UtcDateTime.ToString("dd.MM.yyyy");
}

/// <summary>
/// ═══ ZMT-01 (ADR-167, 2026-08-28) — ZİMMET YÖNETİMİ ═══
///
/// <b>DEFTER (durum değil):</b> her işlem değişmez satır; "kimde ne var" Σ ile türetilir. Sahip
/// değiştirirken UPDATE yok → geçmiş silinemez (kullanıcı §11). <b>İDEMPOTENT:</b> operation_id tekil;
/// aynı işlem ikinci kez SESSİZCE atlanır (retry ikinci hareket ve İKİNCİ STOK DÜŞÜMÜ üretmez).
///
/// <b>PK-B1 — STOKLU HİBRİT:</b> malzeme teslimi mevcut <see cref="StockService.IssueOutTx"/>'i,
/// iade <see cref="StockService.ReceiveInTx"/>'i AYNI transaction'da çağırır (fatura servisi emsali) —
/// stok defteri değiştirilmedi, yalnız ÇAĞRILIYOR. Negatif stok kalkanı/şube bakiyesi aynen çalışır.
/// Ekipman stok dışıdır ve TEK kişide olabilir. <b>PK-B3:</b> kayıp stoğa dönmez; hasarlı iade döner.
/// <b>PK-B2:</b> devir tek işlem — defterde transfer_out + transfer_in çifti (stok depoya uğramaz).
///
/// <b>YETKİ:</b> yeni <c>assignments</c> modülü (deny-by-default). Malzeme teslim/iadesinde stok kapısı
/// (stock.Create) DA gerekir — fatura ile aynı kural; stok yetkisi olmayan zimmet yoluyla stok oynatamaz.
/// <b>KAPSAM:</b> işlem şubesi üzerinden <see cref="BranchAccess"/> (fail-closed).
/// </summary>
public sealed class AssignmentService
{
    public const string Module = "assignments";

    private readonly IDbConnectionFactory _factory;
    private readonly StockService _stock;
    private readonly IClock _clock;

    public AssignmentService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _stock = new StockService(factory, _clock);
    }

    public static string MovementLabel(string movementType) => movementType switch
    {
        "issue" => "Teslim",
        "return" => "İade",
        "transfer_out" => "Devir (veren)",
        "transfer_in" => "Devir (alan)",
        "lost" => "Kayıp",
        "damaged_return" => "Hasarlı İade",
        _ => movementType,
    };

    // ══════════════ İŞLEMLER ══════════════

    /// <summary>TESLİM. Malzemede stok deposundan çıkış da yapılır (aynı transaction, idempotent).</summary>
    public string Issue(SessionContext s, string assetType, string assetId, string personnelId,
        decimal quantity, string? branchId, long? docDate, string? note, string operationId)
        => Islem(s, "issue", +1, assetType, assetId, personnelId, quantity, branchId, docDate, note, operationId,
            stok: assetType == "material" ? StokYonu.Cikis : StokYonu.Yok);

    /// <summary>İADE. Malzemede stok deposuna giriş yapılır; <paramref name="damaged"/>=true → "Hasarlı İade"
    /// olarak izlenir ama stoğa YİNE döner (PK-B3).</summary>
    public string Return(SessionContext s, string assetType, string assetId, string personnelId,
        decimal quantity, string? branchId, long? docDate, string? note, string operationId, bool damaged = false)
        => Islem(s, damaged ? "damaged_return" : "return", -1, assetType, assetId, personnelId, quantity,
            branchId, docDate, note, operationId,
            stok: assetType == "material" ? StokYonu.Giris : StokYonu.Yok);

    /// <summary>KAYIP. Zimmet kapanır; stok GERİ GELMEZ (PK-B3 — malzeme fiilen yok).</summary>
    public string Lost(SessionContext s, string assetType, string assetId, string personnelId,
        decimal quantity, string? branchId, long? docDate, string? note, string operationId)
        => Islem(s, "lost", -1, assetType, assetId, personnelId, quantity, branchId, docDate, note, operationId,
            stok: StokYonu.Yok);

    /// <summary>DEVİR (PK-B2): TEK işlem, defterde ÇİFT kayıt (verenden çıkış + alana giriş, aynı grup).
    /// Stok depoya uğramaz. Geçmiş zinciri (Osman → Ahmet → Mehmet) sonsuza dek okunur.</summary>
    public string Transfer(SessionContext s, string assetType, string assetId, string fromPersonnelId,
        string toPersonnelId, decimal quantity, string? branchId, long? docDate, string? note, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (fromPersonnelId == toPersonnelId) throw new ArgumentException("Devir aynı kişiye yapılamaz.");
        DogrulaOrtak(assetType, quantity);
        var isGunu = IsGunu(s, docDate);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        if (Idempotent(conn, tx, operationId)) { tx.Commit(); return operationId; }
        KapsamVeSahiplik(s, conn, tx, assetType, assetId, branchId, fromPersonnelId);
        EnsurePersonnel(conn, tx, s.CompanyId, toPersonnelId);
        KisideYeterli(s, conn, tx, assetType, assetId, fromPersonnelId, quantity);
        var grup = Guid.NewGuid().ToString("N");
        Satir(conn, tx, s, assetType, assetId, fromPersonnelId, branchId, "transfer_out", -1, quantity,
            grup, null, isGunu, note, operationId + ":out", now);
        Satir(conn, tx, s, assetType, assetId, toPersonnelId, branchId, "transfer_in", +1, quantity,
            grup, null, isGunu, note, operationId + ":in", now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "assignment_movement", grup, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"type\":\"transfer\",\"from\":\"{fromPersonnelId}\",\"to\":\"{toPersonnelId}\"}}"), _clock);
        tx.Commit();
        return grup;
    }

    private enum StokYonu { Yok, Cikis, Giris }

    private string Islem(SessionContext s, string movementType, int direction, string assetType, string assetId,
        string personnelId, decimal quantity, string? branchId, long? docDate, string? note, string operationId,
        StokYonu stok)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        DogrulaOrtak(assetType, quantity);
        if (stok != StokYonu.Yok && string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Malzeme zimmetinde depo (şube) seçimi zorunludur — stok oradan işler.");
        var isGunu = IsGunu(s, docDate);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        if (Idempotent(conn, tx, operationId)) { tx.Commit(); return operationId; }   // retry: İKİNCİ stok düşümü de OLMAZ
        KapsamVeSahiplik(s, conn, tx, assetType, assetId, branchId, personnelId);
        if (direction < 0) KisideYeterli(s, conn, tx, assetType, assetId, personnelId, quantity);
        if (assetType == "equipment" && direction > 0) EkipmanBoşta(conn, tx, s.CompanyId, assetId);

        // PK-B1: stok etkisi MEVCUT stok kapılarıyla, AYNI transaction'da (yetki+negatif stok kalkanı aynen).
        string? stockOp = null;
        if (stok != StokYonu.Yok)
        {
            stockOp = "assign:" + operationId;
            var lines = new[] { new StockLine(assetId, quantity) };
            if (stok == StokYonu.Cikis)
                _stock.IssueOutTx(conn, tx, s, lines, stockOp, branchId, personnelId,
                    note: "Zimmet teslimi", docDate: isGunu);
            else
                _stock.ReceiveInTx(conn, tx, s, lines, stockOp, branchId, personnelId,
                    note: movementType == "damaged_return" ? "Zimmet iadesi (hasarlı)" : "Zimmet iadesi", docDate: isGunu);
        }

        Satir(conn, tx, s, assetType, assetId, personnelId, branchId, movementType, direction, quantity,
            null, stockOp, isGunu, note, operationId, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "assignment_movement", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"type\":\"{movementType}\",\"asset\":\"{assetType}\"}}"), _clock);
        tx.Commit();
        return id;
    }

    // ══════════════ SORGULAR ══════════════

    /// <summary>KİMDE NE VAR — defterden türetilir (Σ &gt; 0). Tek sorgu + bellek içi etiket eşleme (N+1 yok).
    /// BranchAccess: kapsam dışı şubenin hareketleri hesaba KATILMAZ (görünmez).</summary>
    public IReadOnlyList<HoldingRow> Holdings(SessionContext s, string? search = null, string? assetType = null,
        string? personnelId = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var hareketler = Hareketler(s, assetType, personnelId, assetId: null);
        var gruplu = hareketler
            .GroupBy(m => (m.PersonnelId, m.AssetType, m.AssetId))
            .Select(g =>
            {
                var son = g.OrderByDescending(x => x.DocDate).ThenByDescending(x => x.CreatedAt).First();
                return new HoldingRow(g.Key.PersonnelId, son.PersonnelName, g.Key.AssetType, g.Key.AssetId,
                    son.AssetLabel, g.Sum(x => x.Direction * x.Quantity), son.BranchId, son.BranchName, son.DocDate);
            })
            .Where(h => h.Quantity > 0)
            .OrderBy(h => h.PersonnelName, StringComparer.CurrentCulture).ThenBy(h => h.AssetLabel).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            gruplu = gruplu.Where(h =>
                h.PersonnelName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || h.AssetLabel.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (h.BranchName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return gruplu;
    }

    /// <summary>GEÇMİŞ (defter satırları) — en yeni üstte. Kişi/varlık filtrelenebilir.</summary>
    public IReadOnlyList<AssignmentMovementRow> History(SessionContext s, string? assetType = null,
        string? assetId = null, string? personnelId = null, int limit = 300)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        return Hareketler(s, assetType, personnelId, assetId)
            .OrderByDescending(m => m.DocDate).ThenByDescending(m => m.CreatedAt)
            .Take(limit is > 0 and <= 1000 ? limit : 300).ToList();
    }

    /// <summary>Excel (liste kuralı 2): filtrelenmiş TÜM "kimde ne var" kümesi.</summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<HoldingRow> rows)
        => new("Zimmet", new[] { "Personel", "Tür", "Varlık", "Miktar", "Şube/Şantiye", "Son İşlem" },
            rows.Select(h => (IReadOnlyList<object?>)new object?[]
                { h.PersonnelName, h.AssetTypeDisplay, h.AssetLabel, h.Quantity, h.BranchDisplay, h.LastDocDateDisplay }).ToList());

    // ══════════════ yardımcılar ══════════════

    private long IsGunu(SessionContext s, long? istenen)
        => DateEntryPolicy.Uygula(s, istenen) ?? _clock.UtcNow.ToUnixTimeMilliseconds();

    private static void DogrulaOrtak(string assetType, decimal quantity)
    {
        if (assetType is not ("material" or "equipment"))
            throw new ArgumentException("Zimmet yalnız malzeme veya ekipman için yapılabilir.");
        if (quantity <= 0) throw new ArgumentException("Miktar pozitif olmalı.");
        if (assetType == "equipment" && quantity != 1)
            throw new ArgumentException("Ekipman zimmeti adet adet yapılır (miktar 1).");
    }

    /// <summary>Retry kalkanı: aynı operation_id (veya devir çifti) zaten uygulandıysa true.</summary>
    private static bool Idempotent(DbConnection conn, DbTransaction tx, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM assignment_movements WHERE operation_id IN (@o, @o2, @o3);";
        cmd.AddWithValue("@o", operationId);
        cmd.AddWithValue("@o2", operationId + ":out");
        cmd.AddWithValue("@o3", operationId + ":in");
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Tenant + kapsam: varlık ve personel bu firmanın; işlem şubesi kullanıcının kapsamında.</summary>
    private static void KapsamVeSahiplik(SessionContext s, DbConnection conn, DbTransaction tx,
        string assetType, string assetId, string? branchId, string personnelId)
    {
        var tablo = assetType == "equipment" ? "equipment" : "materials";
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT COUNT(*) FROM {tablo} WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", assetId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                throw new ArgumentException("Varlık bulunamadı veya bu firmaya ait değil.");
        }
        EnsurePersonnel(conn, tx, s.CompanyId, personnelId);
        if (!string.IsNullOrWhiteSpace(branchId)) BranchAccess.Require(s, branchId, "zimmet");
    }

    private static void EnsurePersonnel(DbConnection conn, DbTransaction tx, string companyId, string personnelId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM personnel WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", personnelId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ArgumentException("Personel bulunamadı veya bu firmaya ait değil.");
    }

    /// <summary>Kişide yeterli miktar var mı (iade/kayıp/devir kişideki bakiyeyi aşamaz).</summary>
    private static void KisideYeterli(SessionContext s, DbConnection conn, DbTransaction tx,
        string assetType, string assetId, string personnelId, decimal quantity)
    {
        decimal bakiye = 0m;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT direction, quantity FROM assignment_movements " +
                          "WHERE company_id=@c AND personnel_id=@p AND asset_type=@t AND asset_id=@a AND is_deleted=0;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@p", personnelId);
        cmd.AddWithValue("@t", assetType);
        cmd.AddWithValue("@a", assetId);
        using (var r = cmd.ExecuteReader())
            while (r.Read()) bakiye += r.GetInt64(0) * decimal.Parse(r.GetString(1), System.Globalization.CultureInfo.InvariantCulture);
        if (bakiye < quantity)
            throw new ArgumentException($"Kişinin zimmetinde yeterli miktar yok (zimmetteki: {bakiye:0.####}).");
    }

    /// <summary>Ekipman TEK kişide olabilir: firmada bu ekipman için açık zimmet varsa yeni teslim engellenir.</summary>
    private static void EkipmanBoşta(DbConnection conn, DbTransaction tx, string companyId, string assetId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(SUM(direction), 0) FROM assignment_movements " +
                          "WHERE company_id=@c AND asset_type='equipment' AND asset_id=@a AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@a", assetId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
            throw new ArgumentException("Bu ekipman zaten bir personelin zimmetinde. Önce iade veya devir yapın.");
    }

    private static void Satir(DbConnection conn, DbTransaction tx, SessionContext s, string assetType, string assetId,
        string personnelId, string? branchId, string movementType, int direction, decimal quantity,
        string? groupId, string? stockOperationId, long docDate, string? note, string operationId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO assignment_movements(id, company_id, asset_type, asset_id, personnel_id, branch_id,
    movement_type, direction, quantity, group_id, stock_operation_id, doc_date, note, operation_id,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@t,@a,@p,@b,@mt,@dir,@q,@g,@so,@dd,@n,@op,@now,@now,1,0);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@t", assetType);
        cmd.AddWithValue("@a", assetId);
        cmd.AddWithValue("@p", personnelId);
        cmd.AddWithValue("@b", string.IsNullOrWhiteSpace(branchId) ? DBNull.Value : branchId!);
        cmd.AddWithValue("@mt", movementType);
        cmd.AddWithValue("@dir", direction);
        cmd.AddWithValue("@q", quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
        cmd.AddWithValue("@g", (object?)groupId ?? DBNull.Value);
        cmd.AddWithValue("@so", (object?)stockOperationId ?? DBNull.Value);
        cmd.AddWithValue("@dd", docDate);
        cmd.AddWithValue("@n", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note!.Trim());
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Defter satırları + etiketler (personel/malzeme/ekipman/şube adları) — tek geçiş, N+1 yok.
    /// KAPSAM: BranchAccess dışındaki şubelerin satırları ELENİR (şubesiz satır gizlenmez).</summary>
    private List<AssignmentMovementRow> Hareketler(SessionContext s, string? assetType, string? personnelId, string? assetId)
    {
        using var conn = _factory.Create();
        var list = new List<AssignmentMovementRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT m.id, m.asset_type, m.asset_id, m.personnel_id, per.full_name, " +
                "m.movement_type, m.direction, m.quantity, m.branch_id, b.name, m.group_id, m.doc_date, m.note, m.created_at, " +
                "COALESCE(mat.name, eq.name, '—') " +
                "FROM assignment_movements m " +
                "JOIN personnel per ON per.id = m.personnel_id " +
                "LEFT JOIN branches b ON b.id = m.branch_id " +
                "LEFT JOIN materials mat ON m.asset_type='material' AND mat.id = m.asset_id " +
                "LEFT JOIN equipment eq ON m.asset_type='equipment' AND eq.id = m.asset_id " +
                "WHERE m.company_id=@c AND m.is_deleted=0" +
                (string.IsNullOrWhiteSpace(assetType) ? "" : " AND m.asset_type=@t") +
                (string.IsNullOrWhiteSpace(personnelId) ? "" : " AND m.personnel_id=@p") +
                (string.IsNullOrWhiteSpace(assetId) ? "" : " AND m.asset_id=@a") + ";";
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(assetType)) cmd.AddWithValue("@t", assetType);
            if (!string.IsNullOrWhiteSpace(personnelId)) cmd.AddWithValue("@p", personnelId);
            if (!string.IsNullOrWhiteSpace(assetId)) cmd.AddWithValue("@a", assetId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AssignmentMovementRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(14),
                    r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6),
                    decimal.Parse(r.GetString(7), System.Globalization.CultureInfo.InvariantCulture),
                    r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
                    r.IsDBNull(10) ? null : r.GetString(10), r.GetInt64(11),
                    r.IsDBNull(12) ? null : r.GetString(12), r.GetInt64(13)));
        }
        var izinli = BranchAccess.Allowed(s);
        if (izinli is not null)
        {
            var set = izinli.ToHashSet(StringComparer.Ordinal);
            list = list.Where(m => m.BranchId is null || set.Contains(m.BranchId)).ToList();
        }
        return list;
    }
}
