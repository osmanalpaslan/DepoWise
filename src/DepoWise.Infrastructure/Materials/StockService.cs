using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;   // STK-B1: MovementTypeOptions (hareket türü etiketi tek kaynak)
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

public sealed class NegativeStockException : Exception
{
    public NegativeStockException(string message) : base(message) { }
}

public sealed record StockLine(string MaterialId, decimal Quantity, decimal? UnitPrice = null, string Currency = "TRY");
public sealed record CountLine(string MaterialId, decimal CountedQuantity);

public sealed record StockDocResult(string DocumentId, string DocNo);

/// <summary>A3 (Aurora): malzeme kartı "Son Hareketler"/İşlem Geçmişi satırı. Quantity İŞARETLİ (+giriş/−çıkış).</summary>
public sealed record MaterialMovementRow(long Date, string Kind, decimal Quantity, string Label, string? Reference)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(Date).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string QtyText => (Quantity >= 0 ? "+" : "") + Quantity.ToString("0.##");
}

/// <summary>STK-03 — bir malzemenin TEK lokasyondaki bakiyesi (API kırılım sözleşmesi).
/// <paramref name="LocationId"/> boş metin = ATANMAMIŞ (lokasyonu bilinmeyen geçmiş stok).</summary>
public sealed record StockLocationBalance(string LocationId, string LocationName, decimal Quantity);

public sealed record StockMovementRow(long CreatedAt, string MovementType, string Code, string Name, string Unit,
    int Direction, decimal Quantity, decimal? UnitPrice, string? Note,
    string? InvoiceNo = null, string? OrderSlipNo = null, string? CreditSlipNo = null,
    string? DocumentId = null, bool IsReversed = false,
    // STK-03: hareketin LOKASYONU. Depo bazlı stokta "hangi depodan/depoya" bilgisi olmadan hareket
    // defteri okunamaz. Alanlar SONA eklendi (opsiyonel) → mevcut çağıranlar etkilenmez.
    // FromLocation yalnız TRANSFER'de doludur (kaynak depo); diğer türlerde null.
    string? LocationId = null, string? LocationName = null,
    string? FromLocationId = null, string? FromLocationName = null)
{
    /// <summary>Ekranda gösterilecek lokasyon adı. Lokasyon yoksa (geçmiş kayıt) "Atanmamış".</summary>
    public string LocationText => string.IsNullOrEmpty(LocationId) ? "Atanmamış" : (LocationName ?? LocationId!);
    /// <summary>Transferde kaynak depo; diğer hareketlerde boş (tire).</summary>
    public string FromLocationText => string.IsNullOrEmpty(FromLocationId) ? "—" : (FromLocationName ?? FromLocationId!);

    /// <summary>STK-05 — ekranda tek hücrede gösterilecek lokasyon akışı: transferde
    /// <c>Kaynak → Hedef</c>, diğer hareketlerde tek depo adı. Web ve masaüstü AYNI metni gösterir.</summary>
    public string LocationFlowText
        => string.IsNullOrEmpty(FromLocationId) || FromLocationId == LocationId
            ? LocationText
            : $"{FromLocationText} → {LocationText}";

    public string InvoiceText => string.IsNullOrWhiteSpace(InvoiceNo) ? "—" : InvoiceNo!;
    // Transfer geri ALINAMAZ (kullanıcı isteği 2026-08-06): iki şubenin stoğunu etkiler; doğrusu hedeften
    // kaynağa yeni bir ters transfer. Açılış da geri alınmaz. Sunucu ReverseDocument da ayrıca reddeder.
    public bool CanReverse => !IsReversed && DocumentId != null && MovementType != "opening" && MovementType != "transfer";
    public string StatusText => IsReversed ? "İptal edildi" : "";
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string DirectionText => Direction > 0 ? "Giriş" : "Çıkış";
    /// <summary>STK-B1: hareket türü etiketi TEK KAYNAKTAN gelir (<see cref="MovementTypeOptions"/>).
    /// Eskiden burada 5 türlük ayrı bir switch vardı → <c>usage</c>/<c>usage_reverse</c>/<c>reverse</c>
    /// kullanıcıya HAM İNGİLİZCE görünüyor, <c>adjustment</c> ise web'den farklı adlanıyordu.</summary>
    public string TypeText => MovementTypeOptions.Label(MovementType);
    public string QtyText => $"{Quantity:0.##} {Unit}".Trim();
    public string PriceText => UnitPrice is null ? "—" : $"{UnitPrice:0.##}";
    public string NoteText => string.IsNullOrWhiteSpace(Note) ? "—" : Note!;
}

/// <summary>
/// Stok giriş/çıkış/transfer/sayım — hareket defteri ANA KAYNAK; bakiye yalnız hareketle değişir.
/// Negatif stok engeli + idempotency (operation_id) + IMMEDIATE transaction (eş zamanlı çıkış güvenli).
/// İptal = ters hareket (fiziksel silme yok). Tüm akışlar tek transaction; hata → rollback.
/// </summary>
public sealed class StockService
{
    private const string Module = "stock";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public StockService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    // ---- Giriş ----
    public StockDocResult ReceiveIn(SessionContext s, IReadOnlyList<StockLine> lines, string operationId,
        string? branchId = null, string? personnelId = null, string? vehicleId = null, string? note = null, long? docDate = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        return RunDocument(s, "in", operationId, branchId, null, branchId, personnelId, vehicleId, note, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                    ApplyLine(conn, tx, s, docId, lines[i], +1, $"{operationId}:{i}", "in", branchId, null);
            }, invoiceNo: invoiceNo, orderSlipNo: orderSlipNo, creditSlipNo: creditSlipNo);
    }

    // ---- Çıkış (negatif stok engeli) ----
    public StockDocResult IssueOut(SessionContext s, IReadOnlyList<StockLine> lines, string operationId,
        string? branchId = null, string? personnelId = null, string? vehicleId = null, string? note = null, long? docDate = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        branchId = EnforceOwnBranch(s, branchId, "çıkış");   // şubeye bağlı kullanıcı yalnız kendi şubesinden
        return RunDocument(s, "out", operationId, branchId, branchId, null, personnelId, vehicleId, note, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                    ApplyLine(conn, tx, s, docId, lines[i], -1, $"{operationId}:{i}", "out", branchId, branchId);
            }, invoiceNo: invoiceNo, orderSlipNo: orderSlipNo, creditSlipNo: creditSlipNo);
    }

    // ---- Transfer (kaynak çıkış + hedef giriş atomik, aynı grup) ----
    /// <summary>Tek malzemeli transfer — çok malzemeli sürüme yönlendirir (geriye uyumluluk).</summary>
    public StockDocResult Transfer(SessionContext s, string materialId, decimal quantity,
        string fromBranchId, string toBranchId, string operationId, string? note = null, long? docDate = null,
        string? personnelId = null, string? vehicleId = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
        => Transfer(s, new[] { new StockLine(materialId, quantity) }, fromBranchId, toBranchId, operationId,
            note, docDate, personnelId, vehicleId, invoiceNo, orderSlipNo, creditSlipNo);

    /// <summary>
    /// ÇOK MALZEMELİ transfer (İş #8, 2026-08-09) — tek belgede N malzeme, tek transaction.
    /// <see cref="ReceiveIn"/> ve <see cref="IssueOut"/> zaten çok satırlıydı; transfer tek malzemeydi.
    /// Bir satır bile başarısız olursa (ör. negatif stok) TAMAMI geri alınır — yarım transfer olmaz.
    /// </summary>
    public StockDocResult Transfer(SessionContext s, IReadOnlyList<StockLine> lines,
        string fromBranchId, string toBranchId, string operationId, string? note = null, long? docDate = null,
        string? personnelId = null, string? vehicleId = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (lines.Count == 0) throw new ArgumentException("En az bir malzeme seçin.");
        if (lines.Any(l => l.Quantity <= 0)) throw new ArgumentException("Transfer miktarı pozitif olmalı.");
        // Kaynak şube = KULLANICININ ŞUBESİ (login şube). Şubeye bağlı kullanıcıda boş gelse bile kendi şubesine
        // atanır; farklı şube gönderilirse reddedilir. Dönüş çözülmüş kaynak şubedir (eski kod dönüşü atıyordu →
        // istemci boş fromBranchId gönderirse kaynak hareketi şubesiz kalıyordu). Şube kapsamı NULL ise (web/admin)
        // istemcinin gönderdiği kaynak korunur.
        fromBranchId = EnforceOwnBranch(s, fromBranchId, "transfer") ?? fromBranchId;
        if (string.IsNullOrEmpty(fromBranchId)) throw new ArgumentException("Kaynak şube belirlenemedi.");
        if (fromBranchId == toBranchId) throw new ArgumentException("Kaynak ve hedef şube aynı olamaz.");
        var groupId = Guid.NewGuid().ToString("N");
        return RunDocument(s, "transfer", operationId, toBranchId, fromBranchId, toBranchId, personnelId, vehicleId, note, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    // Idempotency anahtarı: TEK malzemede eski biçim ("op:out") AYNEN korunur — bekleyen
                    // bir tekrar denemesi (retry) sürüm değişikliği yüzünden kopya hareket üretmesin.
                    // Çok malzemede satır numarası eklenir (satırlar birbirinden ayrışsın).
                    var suffix = lines.Count == 1 ? "" : $":{i}";
                    // Kaynak çıkış (negatif guard) + hedef giriş — net bakiye değişmez ama hareketler kayıtlı
                    ApplyLine(conn, tx, s, docId, lines[i], -1, $"{operationId}{suffix}:out", "transfer", fromBranchId, fromBranchId, groupId);
                    ApplyLine(conn, tx, s, docId, lines[i], +1, $"{operationId}{suffix}:in", "transfer", toBranchId, fromBranchId, groupId);
                }
            }, groupId, invoiceNo, orderSlipNo, creditSlipNo);
    }

    // ---- STK-08: ATANMAMIŞ stoğun kullanıcı tarafından depolara dağıtımı ----

    /// <summary>STK-08 — dağıtım belgesinin varsayılan notu. Kullanıcı hareket listesinde nedeni görsün.</summary>
    public const string DistributeNote = "Atanmamış stok dağıtımı";

    /// <summary>
    /// STK-08 — ATANMAMIŞ stoğu GERÇEK TRANSFER hareketiyle seçilen depoya aktarır.
    ///
    /// <b>NEDEN AYRI GİRİŞ NOKTASI (KARAR T-1):</b> <see cref="Transfer"/> boş kaynağı bilinçli olarak
    /// REDDEDER ve şubeye bağlı kullanıcıda boş kaynağı <see cref="EnforceOwnBranch"/> ile SESSİZCE
    /// kullanıcının şubesine çevirir. O davranış doğrudur (kazara lokasyonsuz transfer üretilmesin diye),
    /// ama dağıtımda YANLIŞ DEPODAN düşerdi — sessiz veri bozulması. Bu yüzden <see cref="Transfer"/>
    /// GEVŞETİLMEDİ; dağıtım kendi DAR kapısından geçer:
    ///   • kaynak DAİMA ATANMAMIŞ'tır (istemci kaynak gönderemez),
    ///   • <see cref="EnforceOwnBranch"/> ÇAĞRILMAZ,
    ///   • hedef boş olamaz ("Atanmamış"a dağıtım anlamsızdır).
    ///
    /// Hareket türü <b>"transfer"</b> KALIR (yeni tür açılmadı) → rapor, ters kayıt, audit, senkron ve
    /// idempotency mekanizmaları kendiliğinden çalışır. Belge/hareket makinesi de aynıdır
    /// (<see cref="RunDocument"/>) → yeni paralel stok mantığı YOKTUR.
    ///
    /// <b>ATOMİK:</b> tek belge + tek transaction. Bir satır bile yetersizse TAMAMI geri alınır
    /// (kısmi dağıtım kalmaz). Miktarlar <see cref="Money"/>/<c>decimal</c>'dir; float kullanılmaz.
    /// </summary>
    public StockDocResult DistributeUnassigned(SessionContext s, IReadOnlyList<StockLine> lines,
        string toLocationId, string operationId, string? note = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (lines.Count == 0) throw new ArgumentException("En az bir malzeme seçin.");
        if (lines.Any(l => l.Quantity <= 0)) throw new ArgumentException("Dağıtılacak miktar sıfırdan büyük olmalı.");
        // Hedef ATANMAMIŞ OLAMAZ: "atanmamıştan atanmamışa" dağıtım anlamsızdır ve yeni belirsizlik üretir.
        if (string.IsNullOrWhiteSpace(toLocationId))
            throw new ArgumentException("Hedef depo/şantiye seçin. \"Atanmamış\" hedef olarak seçilemez.");
        // Aynı malzeme iki satırda geldiyse TEK satırda toplanır — yeterlilik kontrolü toplam üzerinden
        // yapılmalı (aksi halde 6+6 ile 10 birimlik stoktan 12 dağıtılabilirdi).
        var merged = lines.GroupBy(l => l.MaterialId, StringComparer.Ordinal)
            .Select(g => new StockLine(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        var groupId = Guid.NewGuid().ToString("N");
        // fromBranch = null → belge/hareket ATANMAMIŞ kaynağı taşır (NULL = lokasyon bilinmiyor).
        return RunDocument(s, "transfer", operationId, toLocationId, null, toLocationId, null, null,
            string.IsNullOrWhiteSpace(note) ? DistributeNote : note, null,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < merged.Count; i++)
                {
                    var line = merged[i];
                    // YETERLİLİK: ATANMAMIŞ kovasında gerçekten var mı? StockBalanceWriter'ın kendi
                    // negatif kalkanı da var (allowNegative:false) ama mesajı geneldir — kullanıcı hangi
                    // malzemede ne kadar olduğunu görmeli. Kontrol transaction İÇİNDE, aynı okumayla.
                    var mevcut = StockBalanceWriter.ReadBalance(conn, tx, s.CompanyId, line.MaterialId,
                        StockBalanceWriter.Unassigned);
                    if (mevcut < line.Quantity)
                        throw new NegativeStockException(
                            $"Atanmamış stok yetersiz. Mevcut: {mevcut:0.##}, dağıtılmak istenen: {line.Quantity:0.##}.");

                    var suffix = merged.Count == 1 ? "" : $":{i}";
                    // Kaynak çıkışı: branchId = null → ATANMAMIŞ kovasından düşer.
                    ApplyLine(conn, tx, s, docId, line, -1, $"{operationId}{suffix}:out", "transfer", null, null, groupId);
                    // Hedef girişi: seçilen gerçek depoya.
                    ApplyLine(conn, tx, s, docId, line, +1, $"{operationId}{suffix}:in", "transfer", toLocationId, null, groupId);
                }
            }, groupId);
    }

    /// <summary>
    /// STK-08 — ATANMAMIŞ stoğu olan malzemeler (dağıtım ekranının listesi). TEK sorgu; malzeme başına
    /// ayrı okuma YAPILMAZ. Miktarı sıfır olan satırlar gösterilmez (dağıtacak bir şey yok).
    /// Negatif kalanlar GÖSTERİLİR (ADR-086 devralınan eksik stok) ama dağıtılamaz — kullanıcı görmeli.
    /// </summary>
    public IReadOnlyList<MaterialStock> ListUnassigned(SessionContext s, string? search = null, int limit = 500)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (limit < 1) limit = 1; if (limit > 2000) limit = 2000;

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sql = @"
SELECT m.id, m.code, m.name, sb.quantity
FROM stock_balances sb
JOIN materials m ON m.id = sb.material_id AND m.company_id = sb.company_id AND m.is_deleted = 0
WHERE sb.company_id=@c AND sb.location_id=''";
        if (!string.IsNullOrWhiteSpace(search))
            sql += $" AND ({SqlDialect.LikeTr(conn, "m.code", "@q")} OR {SqlDialect.LikeTr(conn, "m.name", "@q")})";
        sql += " ORDER BY m.code LIMIT @lim;";
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", s.CompanyId);
        if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
        cmd.AddWithValue("@lim", limit);

        var list = new List<MaterialStock>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var qty = Money.Parse(r.GetString(3));
            if (qty == 0m) continue;   // dağıtacak bir şey yok
            list.Add(new MaterialStock(r.GetString(0), r.GetString(1), r.GetString(2), qty));
        }
        return list;
    }

    // Şube-yetki (kullanıcı isteği 2026-08-05): şubeye bağlı kullanıcı (BranchScope.Active != null) yalnız
    // KENDİ şubesinden çıkış/transfer başlatabilir; "Tüm Şubeler"/admin (null) her şubeden. Yalnız interaktif
    // create yolunda çağrılır — sync ve idari ters kayıt bu kontrole girmez (offline/onarım bozulmaz).
    private static string? EnforceOwnBranch(SessionContext s, string? branchId, string op)
    {
        var scope = BranchScope.Active(s);
        if (scope is null) return branchId;                   // Tüm Şubeler / admin → serbest
        if (string.IsNullOrEmpty(branchId)) return scope;     // belirtilmemişse kendi şubesine ata
        if (branchId != scope)
            throw new ForbiddenException($"Yalnız kendi şubenizden {op} yapabilirsiniz.");
        return branchId;
    }

    // Şube bazlı stok bakiyesi (madde 8b) — o (malzeme, şube) için hareket defteri toplamı (giriş +, çıkış −).
    // Money.Parse ile C#'ta toplanır (SQL CAST hassasiyeti bozmasın). Aynı tx içindeki eklenmiş hareketleri de görür.
    private static decimal BranchBalance(DbConnection conn, DbTransaction tx, string companyId, string materialId, string branchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT direction, quantity FROM stock_movements WHERE company_id=@c AND material_id=@m AND branch_id=@b;";
        cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@m", materialId); cmd.AddWithValue("@b", branchId);
        decimal total = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long dir = r.GetInt64(0);
            var qty = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
            total += dir * qty;
        }
        return total;
    }

    // ---- Sayım (gerekçeli fark hareketi) ----
    public StockDocResult Count(SessionContext s, IReadOnlyList<CountLine> lines, string reason, string operationId,
        string? branchId = null, long? docDate = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Sayım fark gerekçesi zorunlu.");
        return RunDocument(s, "count", operationId, branchId, branchId, branchId, null, null, reason, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = lines[i];
                    // STK-02: sayım, SAYILAN LOKASYONUN bakiyesiyle karşılaştırılır. Fark hareketi zaten
                    // aşağıda branchId'ye yazılıyor; sistem miktarını firma geneli okumak "genelden oku,
                    // lokasyona yaz" tutarsızlığı üretirdi. branchId yoksa ATANMAMIŞ kovası okunur/yazılır.
                    var system = StockBalanceWriter.ReadBalance(conn, tx, s.CompanyId, ln.MaterialId,
                        branchId ?? StockBalanceWriter.Unassigned);
                    var diff = ln.CountedQuantity - system;
                    InsertCountLine(conn, tx, docId, ln.MaterialId, system, ln.CountedQuantity, diff, reason);
                    if (diff != 0)
                    {
                        var dir = diff > 0 ? +1 : -1;
                        ApplyLine(conn, tx, s, docId, new StockLine(ln.MaterialId, Math.Abs(diff)),
                            dir, $"{operationId}:{i}", "adjustment", branchId, branchId);
                    }
                }
            });
    }

    // ---- İptal = ters hareket ----
    public void ReverseDocument(SessionContext s, string documentId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        AccessControl.RequireButton(s, SpecialButtons.Reverse);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");
        // Faz 3-Ön: bakiye yarışında tüm iptal işlemi geri alınıp baştan denenir (kısmi tekrar yok).
        StockBalanceWriter.Run(() => ReverseDocumentOnce(s, documentId, reason), $"reverse:{documentId}");
    }

    private void ReverseDocumentOnce(SessionContext s, string documentId, string reason)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var doc = LoadDocument(conn, tx, s.CompanyId, documentId)
            ?? throw new ForbiddenException("Belge bulunamadı veya başka firmaya ait.");
        // Transfer geri ALINAMAZ (kullanıcı isteği 2026-08-06): iki şubenin stoğunu etkiler. Doğrusu hedeften
        // kaynağa yeni bir ters transfer yapmaktır. Otoriter engel — API/masaüstü/web hepsi buradan geçer.
        if (doc.DocType == "transfer")
            throw new ForbiddenException("Transfer geri alınamaz. Hedef şubeden kaynağa yeni bir ters transfer yapın.");
        if (doc.Status == "cancelled") { tx.Commit(); return; } // idempotent

        foreach (var mv in ActiveMovements(conn, tx, documentId))
        {
            // Ters yön uygula (negatif guard ters kayıtta da geçerli).
            // STK-02: ters kayıt, ORİJİNAL hareketin lokasyonuna uygulanır — başka bir depoyu etkilemez.
            ApplyDelta(conn, tx, s.CompanyId, mv.MaterialId, mv.BranchId ?? StockBalanceWriter.Unassigned,
                -mv.Direction * mv.Quantity, now, allowNegative: false);
            var revId = InsertMovement(conn, tx, s.CompanyId, mv.MaterialId, documentId, "reverse",
                -mv.Direction, mv.Quantity, null, null, null, $"{mv.OperationId}:rev", reason, now, mv.BranchId, mv.BranchFromId, mv.GroupId, reversesId: mv.Id, opBranchId: s.OperatingBranchId);
            MarkReversed(conn, tx, mv.Id);
        }
        SetDocumentStatus(conn, tx, documentId, "cancelled", now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "stock_document", documentId, AuditActions.Reverse, s.UserId,
            AfterJson: $"{{\"reason\":\"{reason}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>
    /// Malzemenin güncel stok bakiyesi — YALNIZ oturumun firmasına ait malzemeler için (T-1, 2026-08-09).
    ///
    /// Firma kontrolü AYRI bir sahiplik sorgusuyla (EnsureMaterialOwned) değil, AYNI sorgudaki
    /// <c>company_id=@c</c> filtresiyle yapılır: bu metot malzeme listesinde satır başına çağrıldığı için
    /// (bkz. <c>/api/materials</c>) ek sorgu, mevcut N+1 yükünü İKİYE KATLARDI.
    /// Başka firmanın malzemesi için 0 döner (kayıt yokmuş gibi) — bilgi sızdırmaz.
    /// </summary>
    public decimal GetBalance(SessionContext s, string materialId)
    {
        // STK-02: FİRMA GENELİ toplam = malzemenin TÜM lokasyon bakiyelerinin toplamı.
        // Toplama C#'ta decimal ile (SQL SUM'ı SQLite'ta float'a düşerdi — Money kuralı: float yasak).
        using var conn = _factory.Create();
        return StockBalanceWriter.ReadTotal(conn, null, s.CompanyId, materialId);
    }

    /// <summary>
    /// STK-02 — TEK LOKASYONUN bakiyesi (<c>branches.id</c>; bilinmiyorsa
    /// <see cref="StockBalanceWriter.Unassigned"/>). <see cref="GetBalance"/> firma genelini döndürür;
    /// bu ikisi bilinçli olarak AYRI metotlardır (aynı ad altında iki farklı anlam bırakılmadı).
    /// </summary>
    public decimal GetBalanceAt(SessionContext s, string materialId, string locationId)
    {
        using var conn = _factory.Create();
        return StockBalanceWriter.ReadBalance(conn, null, s.CompanyId, materialId, locationId);
    }

    /// <summary>
    /// STK-03 — malzemenin lokasyon kırılımı, LOKASYON ADLARIYLA (API/ekran sözleşmesi).
    ///
    /// <see cref="GetBalancesByLocation"/> yalnız kimlik→miktar döndürür; ekran ad göstermek zorunda.
    /// Adları çağıranın satır satır sorması N+1 üretirdi (100 malzeme × 5 depo = 500 sorgu) → ad
    /// AYNI sorguda <c>JOIN branches</c> ile gelir. Şube JOIN'i firmaya bağlıdır (çapraz-tenant ad sızmaz).
    ///
    /// Sıra: adı olan lokasyonlar ada göre, ATANMAMIŞ ('') en SONA (kullanıcı önce gerçek depolarını görür).
    /// Yetki: stok OKUMA — hareket listesiyle aynı kapı (deny-by-default).
    /// </summary>
    public IReadOnlyList<StockLocationBalance> GetLocationBalances(SessionContext s, string materialId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var list = new List<StockLocationBalance>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT sb.location_id, b.name, sb.quantity
FROM stock_balances sb
LEFT JOIN branches b ON b.id = sb.location_id AND b.company_id = sb.company_id
WHERE sb.company_id=@c AND sb.material_id=@m;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@m", materialId);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var loc = r.GetString(0);
                list.Add(new StockLocationBalance(
                    loc,
                    r.IsDBNull(1) ? (loc.Length == 0 ? "Atanmamış" : loc) : r.GetString(1),
                    Money.Parse(r.IsDBNull(2) ? null : r.GetString(2))));
            }
        // Sıralama C#'ta: lehçe farkı (Türkçe collation) sonucu değiştirmesin.
        list.Sort((x, y) => x.LocationId.Length == 0 ? 1
                          : y.LocationId.Length == 0 ? -1
                          : string.Compare(x.LocationName, y.LocationName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>
    /// STK-03 — TEK lokasyonun bakiyesi + adı (API sözleşmesi). <see cref="GetBalanceAt"/> yalnız sayı
    /// döndürür; bu, ekranın ihtiyaç duyduğu adı da verir ve lokasyonu DOĞRULAR.
    ///
    /// Boş <paramref name="locationId"/> = ATANMAMIŞ → doğrulanacak kimlik yoktur (uydurma yapılmaz).
    /// Dolu ama firmaya ait değilse <see cref="ForbiddenException"/> (403) — yazma yolundaki
    /// <c>EnsureLocationOwned</c> ile AYNI kural; okuma yolu daha gevşek bırakılmadı.
    /// </summary>
    public StockLocationBalance GetLocationBalance(SessionContext s, string materialId, string locationId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        locationId ??= StockBalanceWriter.Unassigned;
        using var conn = _factory.Create();
        var name = "Atanmamış";
        if (locationId.Length > 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM branches WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", locationId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteScalar() is not string found)
                throw new ForbiddenException("Şube bulunamadı veya başka firmaya ait.");
            name = found;
        }
        return new StockLocationBalance(locationId, name,
            StockBalanceWriter.ReadBalance(conn, null, s.CompanyId, materialId, locationId));
    }

    /// <summary>
    /// STK-04 — SAYIM LİSTESİ: malzemeler + <b>SAYILAN LOKASYONUN</b> sistem miktarı, TEK sorguda.
    ///
    /// ⚠️ NEDEN AYRI METOT: sayım ekranı eskiden malzeme listesinden gelen <b>firma geneli</b> toplamı
    /// "sistem stoğu" diye gösteriyordu. Depo bazlı stokta bu YANLIŞTIR — kullanıcı 10 birimlik depoyu
    /// sayarken ekranda firma toplamı 15 görünür, farkı −3 sanır. Sunucu (STK-02) zaten sayılan lokasyonla
    /// karşılaştırıyor; ekranın de aynı sayıyı göstermesi gerekir, yoksa kullanıcı yanlış rakama bakar.
    ///
    /// Satır başına ayrı sorgu (N+1) YOKTUR: bakiye tek <c>LEFT JOIN</c> ile lokasyona bağlı gelir.
    /// </summary>
    public IReadOnlyList<MaterialStock> GetCountSheet(SessionContext s, string locationId, string? search = null, int limit = 500)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (limit < 1) limit = 1; if (limit > 2000) limit = 2000;
        locationId ??= StockBalanceWriter.Unassigned;

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sql = @"
SELECT m.id, m.code, m.name, COALESCE(sb.quantity,'0')
FROM materials m
LEFT JOIN stock_balances sb ON sb.material_id = m.id AND sb.company_id = m.company_id AND sb.location_id = @loc
WHERE m.company_id = @c AND m.is_deleted = 0";
        if (!string.IsNullOrWhiteSpace(search))
            sql += $" AND ({SqlDialect.LikeTr(conn, "m.code", "@q")} OR {SqlDialect.LikeTr(conn, "m.name", "@q")})";
        sql += " ORDER BY m.code LIMIT @lim;";
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@loc", locationId);
        if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
        cmd.AddWithValue("@lim", limit);

        var list = new List<MaterialStock>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaterialStock(r.GetString(0), r.GetString(1), r.GetString(2), Money.Parse(r.GetString(3))));
        return list;
    }

    /// <summary>STK-02 — malzemenin lokasyon kırılımı (rapor/ekran için; toplama YAPILMAZ).</summary>
    public IReadOnlyDictionary<string, decimal> GetBalancesByLocation(SessionContext s, string materialId)
    {
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT location_id, quantity FROM stock_balances WHERE company_id=@c AND material_id=@m;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@m", materialId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
        return map;
    }

    /// <summary>
    /// ÇOK MALZEMELİ bakiye okuma — TEK sorgu (Faz S / İş #11, 2026-08-09).
    ///
    /// Sebep: <c>/api/materials</c> her satır için ayrı <see cref="GetBalance"/> çağırıyordu → bir sayfada
    /// 200 malzeme = 200 ayrı sorgu (N+1). Sunucu veritabanı artık PostgreSQL'de (ağ üzerinden) olduğu için
    /// her sorgu bir gidiş-dönüş demek; bu uç ayrıca Stok/Talep/Bakım ekranlarının hızlı-arama seçicisidir
    /// (sık çağrılır). Tek sorguya indirildi.
    ///
    /// Bakiyesi olmayan malzeme sözlükte YER ALMAZ → çağıran 0 varsayar (GetBalance ile aynı sonuç).
    /// </summary>
    public IReadOnlyDictionary<string, decimal> GetBalances(SessionContext s, IReadOnlyCollection<string> materialIds)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (materialIds.Count == 0) return result;

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // IN listesi PARAMETRELİ kurulur (id'ler SQL metnine gömülmez).
        var names = new List<string>(materialIds.Count);
        var i = 0;
        foreach (var id in materialIds)
        {
            var p = "@m" + i++;
            names.Add(p);
            cmd.AddWithValue(p, id);
        }
        cmd.CommandText =
            $"SELECT material_id, quantity FROM stock_balances WHERE company_id=@c AND material_id IN ({string.Join(",", names)});";
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        // STK-02: artık malzeme başına LOKASYON SAYISI KADAR satır gelir → C#'ta decimal ile TOPLANIR.
        // (Tek sorgu korunur; N+1 üretilmez. SQL SUM kullanılmaz — SQLite'ta float hatası verirdi.)
        while (r.Read())
        {
            var mat = r.GetString(0);
            result.TryGetValue(mat, out var cur);
            result[mat] = cur + Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
        }
        return result;
    }

    /// <summary>Son stok hareketleri (salt okuma) — malzeme kod/ad + tür/yön/miktar/fiyat/not.</summary>
    public IReadOnlyList<StockMovementRow> RecentMovements(SessionContext s, int limit = 200)
        => SearchMovements(s, null, null, null, limit);

    /// <summary>Stok Hareketleri ekranı (kullanıcı isteği 2026-08-05): tarih aralığı (fromMs/toMs, Unix ms) +
    /// metin araması (malzeme kodu/adı, not, belge/fatura no). Şube kapsamı ve yetki RecentMovements ile aynı.</summary>
    public IReadOnlyList<StockMovementRow> SearchMovements(SessionContext s, long? fromMs, long? toMs, string? search, int limit = 500)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (limit < 1) limit = 1; if (limit > 5000) limit = 5000;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // STK-03: lokasyon ADLARI aynı sorguda JOIN ile gelir — satır başına ayrı sorgu (N+1) YASAK.
        // Şube filtresi JOIN koşulunda: başka firmanın şubesi adıyla sızmasın (savunma derinliği).
        var sb = new System.Text.StringBuilder(@"
SELECT sm.created_at, sm.movement_type, m.code, m.name, COALESCE(u.name,''),
       sm.direction, sm.quantity, sm.unit_price, sm.note,
       d.invoice_no, d.order_slip_no, d.credit_slip_no, sm.document_id, sm.is_reversed,
       sm.branch_id, bl.name, sm.branch_from_id, bf.name
FROM stock_movements sm
JOIN materials m ON m.id = sm.material_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN stock_documents d ON d.id = sm.document_id
LEFT JOIN branches bl ON bl.id = sm.branch_id      AND bl.company_id = sm.company_id
LEFT JOIN branches bf ON bf.id = sm.branch_from_id AND bf.company_id = sm.company_id
WHERE sm.company_id = @c");
        sb.Append(DepoWise.Application.Security.BranchScope.Sql(s, "sm.branch_id"));
        if (fromMs is not null) sb.Append(" AND sm.created_at >= @from");
        if (toMs is not null) sb.Append(" AND sm.created_at <= @to");
        if (!string.IsNullOrWhiteSpace(search))
            sb.Append(" AND (m.code LIKE @q OR m.name LIKE @q OR sm.note LIKE @q OR d.invoice_no LIKE @q OR d.doc_no LIKE @q)");
        // KD-1: rowid SQLite'a özeldir, PostgreSQL'de 42703 verir → lehçeye göre kararlı anahtar.
        sb.Append($" ORDER BY sm.created_at DESC, {SqlDialect.RowTieBreaker(conn, "sm")} DESC LIMIT @lim;");
        cmd.CommandText = sb.ToString();
        cmd.AddWithValue("@c", s.CompanyId);
        if (DepoWise.Application.Security.BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        if (fromMs is not null) cmd.AddWithValue("@from", fromMs.Value);
        if (toMs is not null) cmd.AddWithValue("@to", toMs.Value);
        if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
        cmd.AddWithValue("@lim", limit);
        string? S(DbDataReader rr, int i) => rr.IsDBNull(i) ? null : rr.GetString(i);
        var list = new List<StockMovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StockMovementRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetInt32(5), Money.Parse(r.GetString(6)),
                r.IsDBNull(7) ? (decimal?)null : Money.Parse(r.GetString(7)),
                S(r, 8), S(r, 9), S(r, 10), S(r, 11), S(r, 12), r.GetInt32(13) == 1,
                S(r, 14), S(r, 15), S(r, 16), S(r, 17)));
        return list;
    }

    /// <summary>A3 (Aurora): TEK malzemenin son N hareketi (giriş/çıkış/transfer/sayım). Malzeme kartı sağ
    /// sütunundaki "Son Hareketler" paneli için. Yetki: malzeme OKUMA + firma kapsamı (malzeme detay ucuyla aynı).
    /// Tarihe göre azalan. Miktar İŞARETLİ (giriş +, çıkış −). Boşsa panel gösterilmez.</summary>
    public IReadOnlyList<MaterialMovementRow> RecentForMaterial(SessionContext s, string materialId, int take = 10)
    {
        AccessControl.Require(s, "materials", PermissionAction.View);   // malzeme detay ucuyla aynı kontrol
        if (take < 1) take = 1; if (take > 100) take = 100;
        using var conn = _factory.Create();
        // Firma kapsamı: yalnız oturumun firmasına ait + bu malzemenin hareketleri (çapraz-tenant sızıntısı yok).
        EnsureMaterialOwned(conn, null, s.CompanyId, materialId);
        using var cmd = conn.CreateCommand();
        // KD-1: ikincil sıralama anahtarı lehçeye göre (SQLite rowid · PostgreSQL id) — bkz. SqlDialect.
        cmd.CommandText = $@"
SELECT sm.created_at, sm.movement_type, sm.direction, sm.quantity,
       COALESCE(br.name,''), COALESCE(d.doc_no,'')
FROM stock_movements sm
LEFT JOIN stock_documents d ON d.id = sm.document_id
LEFT JOIN branches br ON br.id = sm.branch_id
WHERE sm.company_id=@c AND sm.material_id=@m
ORDER BY sm.created_at DESC, {SqlDialect.RowTieBreaker(conn, "sm")} DESC
LIMIT @take;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@take", take);
        var list = new List<MaterialMovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var type = r.GetString(1);
            int dir = r.GetInt32(2);
            var qty = Money.Parse(r.GetString(3));
            var branch = r.GetString(4);
            var doc = r.GetString(5);
            // STK-B1: etiket TEK KAYNAKTAN (MovementTypeOptions). Eskiden burada 6 türlük AYRI bir
            // switch vardı; `reverse` burada "İptal", web'de "İptal (ters)", masaüstünde HAM görünüyordu.
            var typeText = MovementTypeOptions.Label(type);
            // `kind` kullanıcıya dönük DEĞİLDİR — ikon/renk grubudur (giriş mi çıkış mı düzeltme mi).
            // Etiket kataloğundan ayrı tutulur; bakım hareketleri de akış yönüne göre gruplanır.
            var kind = type switch
            {
                MovementTypeOptions.In or MovementTypeOptions.Opening or MovementTypeOptions.UsageReverse => "in",
                MovementTypeOptions.Out or MovementTypeOptions.Usage => "out",
                MovementTypeOptions.Transfer => "transfer",
                MovementTypeOptions.Adjustment or MovementTypeOptions.Reverse => "adjust",
                _ => type
            };
            var label = string.IsNullOrEmpty(branch) ? typeText : $"{typeText} · {branch}";
            list.Add(new MaterialMovementRow(r.GetInt64(0), kind, dir * qty, label,
                string.IsNullOrEmpty(doc) ? null : doc));
        }
        return list;
    }

    // ================= çekirdek =================

    /// <summary>Belge motoru + TEKRAR SARMALAYICISI (Faz 3-Ön). Bakiye yarışında (yalnız o durumda) transaction
    /// tamamen geri alınır ve belge BAŞTAN üretilir — kısmi tekrar yoktur. Gövde (<paramref name="body"/>)
    /// yalnız veritabanı işlemi yapar, geri alınamaz yan etkisi yoktur → yeniden çalıştırmak güvenlidir.
    /// Aynı operationId ile yeniden denenir; başarısız deneme geri alındığı için idempotency bozulmaz.</summary>
    private StockDocResult RunDocument(SessionContext s, string docType, string operationId,
        string? toBranch, string? fromBranch, string? primaryBranch, string? personnelId, string? vehicleId,
        string? note, long? docDate, Action<DbConnection, DbTransaction, string> body, string? groupId = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
        => StockBalanceWriter.Run(() => RunDocumentOnce(s, docType, operationId, toBranch, fromBranch, primaryBranch,
            personnelId, vehicleId, note, docDate, body, groupId, invoiceNo, orderSlipNo, creditSlipNo),
            $"document:{docType} op={operationId}");

    private StockDocResult RunDocumentOnce(SessionContext s, string docType, string operationId,
        string? toBranch, string? fromBranch, string? primaryBranch, string? personnelId, string? vehicleId,
        string? note, long? docDate, Action<DbConnection, DbTransaction, string> body, string? groupId = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var date = docDate ?? now;

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate(); // IMMEDIATE → eş zamanlı çıkış serialize

        // Idempotency: bu operationId daha önce işlendiyse mevcut belgeyi döndür (çift yazma yok)
        var existing = FindDocumentByOperation(conn, tx, operationId);
        if (existing is not null) { tx.Commit(); return existing; }

        // STK-03: LOKASYON SAHİPLİĞİ — belgeye giren her şube oturumun FİRMASINA ait olmalı.
        // Burası tüm yazma yollarının (giriş/çıkış/transfer/sayım) tek geçiş noktasıdır → kontrol
        // bir kez yazılır, dördü birden korunur. Idempotency'den SONRA: zaten işlenmiş bir işlemi
        // yeniden doğrulayıp reddetmek, tekrar denemede sonucu değiştirirdi.
        EnsureLocationOwned(conn, tx, s.CompanyId, toBranch);
        EnsureLocationOwned(conn, tx, s.CompanyId, fromBranch);
        EnsureLocationOwned(conn, tx, s.CompanyId, primaryBranch);

        var docId = Guid.NewGuid().ToString("N");
        var docNo = NextDocNo(conn, tx, s.CompanyId, docType, date);
        InsertDocument(conn, tx, docId, s.CompanyId, docType, docNo, date, fromBranch, toBranch, personnelId, vehicleId, note, groupId, now,
            invoiceNo, orderSlipNo, creditSlipNo);

        body(conn, tx, docId);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "stock_document", docId, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"type\":\"{docType}\",\"no\":\"{docNo}\"}}"), _clock);
        tx.Commit();
        return new StockDocResult(docId, docNo);
    }

    private void ApplyLine(DbConnection conn, DbTransaction tx, SessionContext s, string docId,
        StockLine line, int direction, string operationId, string movementType, string? branchId, string? branchFromId, string? groupId = null)
    {
        if (line.Quantity <= 0) throw new ArgumentException("Miktar pozitif olmalı.");
        EnsureMaterialOwned(conn, tx, s.CompanyId, line.MaterialId);

        // Per-branch stok (madde 8b, kullanıcı isteği 2026-08-05): çıkış/transfer-çıkışta O ŞUBENİN defter
        // bakiyesi yeterli mi? Firma-geneli kalkan (ApplyDelta) zaten var; bu ondan KATI — şubede stok yoksa
        // çıkışı/transferi engeller. Sayım/ters kayıt HARİÇ (gerçeği düzeltir). NULL şube (Tüm Şubeler/idari) →
        // firma-geneli kalkanla yetinilir. Şema DEĞİŞMEZ; şube bakiyesi hareket defterinden anlık hesaplanır.
        if (direction < 0 && !string.IsNullOrEmpty(branchId) && (movementType == "out" || movementType == "transfer"))
        {
            var branchBal = BranchBalance(conn, tx, s.CompanyId, line.MaterialId, branchId!);
            if (branchBal < line.Quantity)
                throw new NegativeStockException($"Bu şubede yeterli stok yok. Mevcut: {branchBal:0.##}, çıkış istenen: {line.Quantity:0.##}.");
        }

        // STK-02: bakiye ARTIK LOKASYON BAZLI. Hareketin lokasyonu (branchId) bakiyeye de yazılır;
        // bilinmiyorsa ATANMAMIŞ kovası kullanılır — asla rastgele şube seçilmez.
        ApplyDelta(conn, tx, s.CompanyId, line.MaterialId, branchId ?? StockBalanceWriter.Unassigned,
            direction * line.Quantity, _clock.UtcNow.ToUnixTimeMilliseconds(), allowNegative: false);
        InsertMovement(conn, tx, s.CompanyId, line.MaterialId, docId, movementType, direction, line.Quantity,
            line.UnitPrice, line.Currency, null, operationId, null, _clock.UtcNow.ToUnixTimeMilliseconds(), branchId, branchFromId, groupId, null, s.OperatingBranchId);
    }

    /// <summary>Bakiyeye işaretli miktarı uygular; düşüşte negatif olursa fail-closed.
    /// Faz 3-Ön: gerçek yazma TEK ORTAK yazıcıdadır (<see cref="StockBalanceWriter"/>) — bakım tarafı da
    /// aynı sınıfı kullanır, böylece aynı stok için iki farklı güvenlik mantığı kalmaz (kullanıcı kararı 3).</summary>
    private static void ApplyDelta(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        string locationId, decimal signedQty, long now, bool allowNegative)
        => StockBalanceWriter.ApplyDelta(conn, tx, companyId, materialId, locationId, signedQty, now, allowNegative);

    /// <summary>Firma geneli toplam (tüm lokasyonlar) — ters kayıt/sayım gibi genel kontroller için.</summary>
    private static decimal ReadBalance(DbConnection conn, DbTransaction? tx, string companyId, string materialId)
        => StockBalanceWriter.ReadTotal(conn, tx, companyId, materialId);

    /// <summary>
    /// SUNUCU-OTORİTELİ bakiye (Senkron 2b): firmanın TÜM stok bakiyelerini hareket defterinden yeniden hesaplar.
    /// balance(malzeme) = Σ(direction × quantity) tüm hareketler (ters hareket ayrı satır olarak toplama girer).
    /// Money ile decimal-kesin (quantity TEXT). Çok makineli senkronda push sonrası çağrılır → makinelerin
    /// birleşik hareketlerinden DOĞRU tek bakiye üretir (istemci snapshot'ı birbirini ezmez).
    /// </summary>
    /// <remarks>
    /// ⚠️ Bu metot BİLEREK CAS kullanmaz (kullanıcı kararı K-3): defterden MUTLAK doğruyu yeniden kurar,
    /// yani üzerine yazması gerekir. Ancak PostgreSQL'de bir yarış penceresi vardır: hareketler okunduktan
    /// SONRA eşzamanlı bir çıkış commit ederse, yazılan mutlak değer o çıkışın etkisini siler (bakiye fazla
    /// görünür; defter yine doğrudur ve bir sonraki çağrı düzeltir).
    ///
    /// İYİMSER KORUMA (kullanıcı kararı N-2): hesaplamadan ÖNCE ve yazmadan ÖNCE hareket defterinin özeti
    /// (satır sayısı + en büyük created_at) alınır. Değiştiyse hesaplanan bakiye YAZILMAZ ve hesaplama baştan
    /// yapılır — en fazla 2 yeniden hesaplama. Sonsuz döngü yoktur; hakkı biterse yazma atlanır (bir sonraki
    /// çağrı zaten düzeltir) ve loga AYRI bir etiketle yazılır (yarış ≠ sistem hatası).
    /// SQLite'ta aynı anda tek yazar olduğu için özet hiç değişmez → davranış değişmez.
    /// </remarks>
    public void RecomputeBalances(string companyId)
    {
        const int maxRecomputes = 2;   // ilk deneme + en fazla 2 yeniden hesaplama
        for (int attempt = 0; attempt <= maxRecomputes; attempt++)
        {
            using var conn = _factory.Create();
            using var tx = conn.BeginImmediate();

            var before = LedgerSignature(conn, tx, companyId);

            // STK-02: artık (malzeme + LOKASYON) bazında hesaplanır. Toplama C#'ta decimal ile yapılır —
            // quantity TEXT içinde decimal olduğu için SQL SUM'ı SQLite'ta float hatası üretirdi.
            var totals = new Dictionary<(string Material, string Location), decimal>();
            using (var read = conn.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText =
                    "SELECT material_id, branch_id, direction, quantity FROM stock_movements WHERE company_id=@c;";
                read.AddWithValue("@c", companyId);
                using var r = read.ExecuteReader();
                while (r.Read())
                {
                    var key = (r.GetString(0), r.IsDBNull(1) ? StockBalanceWriter.Unassigned : r.GetString(1));
                    long dir = r.GetInt64(2);
                    var qty = Money.Parse(r.IsDBNull(3) ? null : r.GetString(3));
                    totals.TryGetValue(key, out var cur);
                    totals[key] = cur + dir * qty;
                }
            }

            // Yazmadan hemen önce defter yeniden mühürlenir: araya yeni hareket girdiyse yazma yapılmaz.
            if (LedgerSignature(conn, tx, companyId) != before)
            {
                tx.Rollback();
                StockBalanceWriter.Log($"[stock-recompute] race company={companyId} attempt={attempt + 1}/{maxRecomputes + 1} — yazma atlandı, yeniden hesaplanıyor");
                if (attempt < maxRecomputes) continue;
                StockBalanceWriter.Log($"[stock-recompute] give-up company={companyId} — bu turda bakiye yazılmadı (defter doğru; bir sonraki eşitleme düzeltir)");
                return;
            }

            var now = _clock.UtcNow.ToUnixTimeMilliseconds();

            // Defterde artık bulunmayan (malzeme+lokasyon) bakiye satırları KALMAMALI — aksi halde
            // hayalet bakiye oluşur (ör. tüm hareketleri ters kaydedilmiş bir lokasyon).
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM stock_balances WHERE company_id=@c;";
                del.AddWithValue("@c", companyId);
                del.ExecuteNonQuery();
            }

            foreach (var ((mat, loc), total) in totals)
            {
                using var up = conn.CreateCommand();
                up.Transaction = tx;
                up.CommandText =
                    "INSERT INTO stock_balances(company_id, material_id, location_id, quantity, updated_at) VALUES(@c,@m,@l,@q,@now) " +
                    "ON CONFLICT(company_id, material_id, location_id) DO UPDATE SET quantity=excluded.quantity, updated_at=excluded.updated_at;";
                up.AddWithValue("@c", companyId);
                up.AddWithValue("@m", mat);
                up.AddWithValue("@l", loc);
                up.AddWithValue("@q", Money.Serialize(total));
                up.AddWithValue("@now", now);
                up.ExecuteNonQuery();
            }
            tx.Commit();
            return;
        }
    }

    /// <summary>Hareket defterinin ucuz "mührü": (satır sayısı, en büyük created_at). Defter append-only
    /// olduğu için bu ikili değiştiyse araya yeni hareket girmiş demektir.</summary>
    private static (long Count, long MaxCreated) LedgerSignature(DbConnection conn, DbTransaction tx, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*), COALESCE(MAX(created_at),0) FROM stock_movements WHERE company_id=@c;";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (Convert.ToInt64(r.GetValue(0)), Convert.ToInt64(r.GetValue(1))) : (0L, 0L);
    }

    private static string InsertMovement(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        string documentId, string movementType, int direction, decimal quantity, decimal? unitPrice, string? currency,
        decimal? fxRate, string operationId, string? note, long now, string? branchId, string? branchFromId, string? groupId, string? reversesId,
        string? opBranchId = null)
    {
        var id = Guid.NewGuid().ToString("N");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, branch_from_id, movement_type, direction,
    quantity, unit_price, currency_code, fx_rate, operation_id, note, created_at, document_id, is_reversed, reverses_movement_id, op_branch_id)
VALUES(@id,@c,@m,@b,@bf,@type,@dir,@q,@price,@cur,@fx,@op,@note,@now,@doc,0,@rev,@opb);";
        cmd.AddWithValue("@opb", (object?)opBranchId ?? DBNull.Value);
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.AddWithValue("@bf", (object?)branchFromId ?? DBNull.Value);
        cmd.AddWithValue("@type", movementType);
        cmd.AddWithValue("@dir", direction);
        cmd.AddWithValue("@q", Money.Serialize(quantity));
        cmd.AddWithValue("@price", unitPrice is null ? DBNull.Value : Money.Serialize(unitPrice.Value));
        cmd.AddWithValue("@cur", (object?)currency ?? DBNull.Value);
        cmd.AddWithValue("@fx", fxRate is null ? DBNull.Value : Money.Serialize(fxRate.Value));
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@doc", documentId);
        cmd.AddWithValue("@rev", (object?)reversesId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void InsertDocument(DbConnection conn, DbTransaction tx, string id, string companyId,
        string docType, string docNo, long docDate, string? fromBranch, string? toBranch, string? personnelId,
        string? vehicleId, string? note, string? groupId, long now,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_documents(id, company_id, doc_type, doc_no, doc_date, from_branch_id, to_branch_id,
    personnel_id, vehicle_id, note, status, group_id, invoice_no, order_slip_no, credit_slip_no, created_at, version, is_deleted)
VALUES(@id,@c,@type,@no,@date,@from,@to,@pers,@veh,@note,'active',@grp,@inv,@ord,@crd,@now,1,0);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@type", docType);
        cmd.AddWithValue("@no", docNo);
        cmd.AddWithValue("@date", docDate);
        cmd.AddWithValue("@from", (object?)fromBranch ?? DBNull.Value);
        cmd.AddWithValue("@to", (object?)toBranch ?? DBNull.Value);
        cmd.AddWithValue("@pers", (object?)personnelId ?? DBNull.Value);
        cmd.AddWithValue("@veh", (object?)vehicleId ?? DBNull.Value);
        cmd.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.AddWithValue("@grp", (object?)groupId ?? DBNull.Value);
        cmd.AddWithValue("@inv", (object?)invoiceNo ?? DBNull.Value);
        cmd.AddWithValue("@ord", (object?)orderSlipNo ?? DBNull.Value);
        cmd.AddWithValue("@crd", (object?)creditSlipNo ?? DBNull.Value);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertCountLine(DbConnection conn, DbTransaction tx, string docId, string materialId,
        decimal system, decimal counted, decimal diff, string reason)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_count_lines(id, document_id, material_id, system_qty, counted_qty, diff_qty, reason)
VALUES(@id,@doc,@m,@s,@c,@d,@r);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@doc", docId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@s", Money.Serialize(system));
        cmd.AddWithValue("@c", Money.Serialize(counted));
        cmd.AddWithValue("@d", Money.Serialize(diff));
        cmd.AddWithValue("@r", reason);
        cmd.ExecuteNonQuery();
    }

    private static string NextDocNo(DbConnection conn, DbTransaction tx, string companyId, string docType, long docDateMs)
    {
        var year = DateTimeOffset.FromUnixTimeMilliseconds(docDateMs).Year;
        var prefix = docType switch { "in" => "GIR", "out" => "CIK", "transfer" => "TRF", "count" => "SAY", _ => "DOC" };
        var like = $"{prefix}-{year}-%";
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COALESCE(MAX(CAST(substr(doc_no, length(@p)+1) AS INTEGER)),0) FROM stock_documents " +
            "WHERE company_id=@c AND doc_type=@t AND doc_no LIKE @like;";
        cmd.AddWithValue("@p", $"{prefix}-{year}-");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@t", docType);
        cmd.AddWithValue("@like", like);
        var next = Convert.ToInt64(cmd.ExecuteScalar()) + 1;
        return $"{prefix}-{year}-{next:0000}";
    }

    private static StockDocResult? FindDocumentByOperation(DbConnection conn, DbTransaction tx, string baseOperationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT d.id, d.doc_no FROM stock_movements mv JOIN stock_documents d ON d.id = mv.document_id " +
            "WHERE mv.operation_id LIKE @op LIMIT 1;";
        cmd.AddWithValue("@op", baseOperationId + ":%");
        using var r = cmd.ExecuteReader();
        return r.Read() ? new StockDocResult(r.GetString(0), r.GetString(1)) : null;
    }

    private sealed record MovementRow(string Id, string MaterialId, int Direction, decimal Quantity,
        string OperationId, string? BranchId, string? BranchFromId, string? GroupId);

    private static IReadOnlyList<MovementRow> ActiveMovements(DbConnection conn, DbTransaction tx, string documentId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, material_id, direction, quantity, operation_id, branch_id, branch_from_id, " +
            "(SELECT group_id FROM stock_documents d WHERE d.id=@doc) FROM stock_movements " +
            "WHERE document_id=@doc AND is_reversed=0;";
        cmd.AddWithValue("@doc", documentId);
        var list = new List<MovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MovementRow(r.GetString(0), r.GetString(1), r.GetInt32(2), Money.Parse(r.GetString(3)),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        return list;
    }

    private sealed record DocRow(string Id, string Status, string DocType);

    private static DocRow? LoadDocument(DbConnection conn, DbTransaction tx, string companyId, string documentId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, status, doc_type FROM stock_documents WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", documentId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DocRow(r.GetString(0), r.GetString(1), r.GetString(2)) : null;
    }

    private static void MarkReversed(DbConnection conn, DbTransaction tx, string movementId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE stock_movements SET is_reversed=1 WHERE id=@id;";
        cmd.AddWithValue("@id", movementId);
        cmd.ExecuteNonQuery();
    }

    private static void SetDocumentStatus(DbConnection conn, DbTransaction tx, string documentId, string status, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE stock_documents SET status=@s, version=version+1 WHERE id=@id;";
        cmd.AddWithValue("@s", status);
        cmd.AddWithValue("@id", documentId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// STK-03 — Stok lokasyonunun (şube/şantiye) oturumun FİRMASINA ait olduğunu doğrular.
    ///
    /// NEDEN GEREKLİ: STK-02'den beri lokasyon <c>stock_balances</c>'ın BİRİNCİL ANAHTAR kolonudur.
    /// Doğrulanmazsa başka firmanın şube kimliği hem hareket defterine hem de bakiye anahtarına yazılır
    /// ve o satır hiçbir firmanın ekranında düzeltilemez. Kontrol <see cref="BranchService"/>'teki
    /// <c>EnsureBranchOwned</c> ile BİREBİR aynı desendir (yeni yetki mimarisi kurulmadı).
    ///
    /// NEDEN SERVİSTE (API'de değil): masaüstü bu servisi ÇEVRİMDIŞI, API'ye uğramadan çağırır.
    /// API katmanına konsaydı çevrimdışı yol korumasız kalırdı.
    ///
    /// null/boş = ATANMAMIŞ (lokasyon bilinmiyor) → doğrulanacak bir şey yoktur, geçerlidir.
    /// </summary>
    private static void EnsureLocationOwned(DbConnection conn, DbTransaction tx, string companyId, string? locationId)
    {
        if (string.IsNullOrEmpty(locationId)) return;   // ATANMAMIŞ — uydurma yok, kontrol edilecek kimlik yok
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", locationId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Şube bulunamadı veya başka firmaya ait.");
    }

    private static void EnsureMaterialOwned(DbConnection conn, DbTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", materialId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }
}
