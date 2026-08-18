using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Cari hareket belge türü kataloğu — tek doğru kaynak (iki platform aynı etiketi gösterir).
/// G4-2/G4-3 kendi türlerini (fatura/tahsilat/ödeme) BURAYA ekler; yeni bir katalog açılmaz.</summary>
public static class PartyDocTypes
{
    public const string Opening = "opening";        // açılış bakiyesi
    public const string Invoice = "invoice";        // G4-2
    public const string Payment = "payment";        // biz ödedik (G4-3)
    public const string Receipt = "receipt";        // biz tahsil ettik (G4-3)
    public const string Adjustment = "adjustment";  // düzeltme (gerekçeli)

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Opening, "Açılış"),
        (Invoice, "Fatura"),
        (Payment, "Ödeme"),
        (Receipt, "Tahsilat"),
        (Adjustment, "Düzeltme"),
    };

    public static string Label(string? key) => All.FirstOrDefault(x => x.Key == key).Label ?? (key ?? "—");
    public static bool IsValid(string? key) => All.Any(x => x.Key == key);

    /// <summary>G4-1'de kullanıcının ELLE girebileceği türler. Fatura/tahsilat/ödeme kendi
    /// modüllerinden (G4-2/G4-3) üretilecek — elle girilip iki gerçeklik oluşmasın.</summary>
    public static readonly IReadOnlyList<string> ManualEntry = new[] { Opening, Adjustment };
}

/// <summary>Cari hesap hareketi (okuma).</summary>
public sealed record PartyLedgerEntry(
    string Id, string PartyId, long EntryDate, string DocType, string? DocNo, string? Description,
    int Direction, decimal Amount, string Currency, long? DueDate,
    string? SourceType, string? SourceId, string? BranchId, bool IsReversed, long CreatedAt)
{
    public decimal Debit => Direction > 0 ? Amount : 0m;    // borç (cari bize borçlandı)
    public decimal Credit => Direction < 0 ? Amount : 0m;   // alacak (cariye borçlandık)
    public string TypeText => PartyDocTypes.Label(DocType);
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(EntryDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string DueText => DueDate is null ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(DueDate.Value).LocalDateTime.ToString("dd.MM.yyyy");
}

/// <summary>Ekstre satırı: hareket + YÜRÜYEN BAKİYE (tarih sırasına göre hesaplanır, saklanmaz).</summary>
public sealed record PartyStatementRow(PartyLedgerEntry Entry, decimal RunningBalance);

/// <summary>Cari finansal özet — hepsi hareketlerden TÜRETİLİR, hiçbiri saklanmaz.</summary>
public sealed record PartyBalance(decimal Debit, decimal Credit, int EntryCount, long? LastEntryDate)
{
    /// <summary>Borç − Alacak. Pozitif: cari BİZE borçlu. Negatif: biz cariye borçluyuz.</summary>
    public decimal Balance => Debit - Credit;
    public string BalanceText => Balance == 0 ? "0"
        : Balance > 0 ? $"{Balance:0.##} (Borç)" : $"{-Balance:0.##} (Alacak)";
    public string LastEntryText => LastEntryDate is null ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(LastEntryDate.Value).LocalDateTime.ToString("dd.MM.yyyy");
}

/// <summary>Yeni cari hareketi.</summary>
public sealed record NewLedgerEntry(
    string PartyId, string DocType, decimal Amount, bool IsDebit,
    long? EntryDate = null, string? DocNo = null, string? Description = null,
    long? DueDate = null, string Currency = "TRY", string? BranchId = null,
    string? SourceType = null, string? SourceId = null, string? OperationId = null);

/// <summary>
/// ═══ G4-1 — CARİ HESAP HAREKETİ (DEFTER) SERVİSİ (2026-08-12) ═══
///
/// <b>DEFTER ANA KAYNAKTIR, BAKİYE TÜRETİLİR.</b> <c>parties</c> tablosunda bakiye kolonu YOKTUR;
/// bakiye her zaman <c>Σ(direction × amount)</c> ile hesaplanır. Bu, stok tarafındaki
/// "hareket defteri ana kaynak" kuralının cari karşılığıdır — elle yazılan ve defterle uyuşmayabilecek
/// bir bakiye alanı bilinçli olarak YOKTUR.
///
/// <b>⚠️ STOKLA SINIR:</b> bu servis <c>stock_movements</c> / <c>stock_balances</c> tablolarına ASLA
/// yazmaz ve okumaz. G4-2'de fatura geldiğinde stok etkisi YALNIZ <c>StockService.ReceiveIn/IssueOut</c>
/// üzerinden yürüyecek; cari borcu ise buraya yazılacak. İki defter AYRI kalır, biri diğerinin
/// alternatifi değildir.
///
/// <b>IDEMPOTENCY:</b> <c>operation_id</c> verildiyse aynı işlem ikinci kez hareket ÜRETMEZ
/// (benzersiz indeks + önden kontrol). G4-2/G4-3 ağ tekrarlarında cariyi ikinci kez borçlandırmaz.
///
/// <b>DÜZELTME SİLMEYLE DEĞİL TERS KAYITLA:</b> operasyonel kayıt fiziksel silinmez (CLAUDE.md §4).
/// </summary>
public sealed class PartyLedgerService
{
    private const string Module = PartyService.Module;   // cari yetkisiyle aynı düğüm

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public PartyLedgerService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>
    /// Hareket ekler. <paramref name="dto"/>.IsDebit true → BORÇ (+1), false → ALACAK (−1).
    /// Tutar POZİTİF olmalıdır; yön ayrı alandır (negatif tutarla ters yön yazılamaz — çift anlam olmasın).
    /// </summary>
    public string Add(SessionContext s, NewLedgerEntry dto)
    {
        // ⭐ GUI-02 (2026-08-13, gerçek masaüstü GUI testinde bulundu) — ELLE GİRİLEN HAREKET ŞUBESİZ KALIYORDU.
        // Ne masaüstü ne web bu yolda BranchId gönderiyordu; Require(null) serbest olduğu için satır
        // branch_id = NULL yazılıyordu. Şubesiz satır "her şubeye ait" sayıldığından (okuma filtresinde
        // `OR branch_id IS NULL` vardır) Şube A'da girilen açılış bakiyesi Şube B'nin ekstresinde,
        // bakiyesinde ve raporlarında da görünüyordu → şube bazlı ön muhasebe fiilen delinmişti.
        // Fatura/tahsilat/ödeme zaten BranchAccess.Resolve kullanıyordu; elle hareket artık AYNI kapıdan
        // geçer: verilmediyse oturumun ÇALIŞMA şubesine (yoksa tek izinli şubeye) yazılır, verildiyse
        // kapsam içinde olduğu doğrulanır. İkinci bir kapsam mantığı EKLENMEZ.
        dto = dto with { BranchId = BranchAccess.Resolve(s, dto.BranchId, "cari hareketi") };
        // ⭐ G4-1b (2026-08-12) — KULLANICI YOLU YALNIZ ELLE GİRİLEBİLİR TÜRLERİ KABUL EDER.
        // Eskiden her tür kabul ediliyordu: kullanıcı UI'yi atlayıp doğrudan "invoice" türünde hareket
        // yazabilir, G4-2 aynı faturayı işlediğinde cari İKİ KEZ borçlanırdı (sahte belge + mükerrer borç).
        // Belge kaynaklı türler (fatura/tahsilat/ödeme) yalnız <see cref="AddFromDocument"/> ile yazılır ve
        // orada kaynak belge (source_type + source_id) ZORUNLUDUR → her hareketin bir belgesi olur.
        if (!PartyDocTypes.ManualEntry.Contains(dto.DocType))
            throw new ArgumentException(
                $"'{PartyDocTypes.Label(dto.DocType)}' hareketi elle girilemez; ilgili belge ekranından oluşturulur. " +
                "Elle yalnız açılış ve düzeltme hareketi girilebilir.");
        return Write(s, dto);
    }

    /// <summary>
    /// BELGE KAYNAKLI hareket (G4-2 fatura, G4-3 tahsilat/ödeme buradan yazacak). Kullanıcı yolundan
    /// çağrılamaz: <paramref name="dto"/> içinde <c>SourceType</c> ve <c>SourceId</c> ZORUNLUDUR —
    /// böylece her sistem hareketi bir belgeye bağlıdır ve izlenebilir kalır.
    /// <b>Bu metot da stok tablolarına DOKUNMAZ</b>; stok etkisi çağıranın <c>StockService</c> ile
    /// yürüttüğü ayrı bir iştir (iki defter ayrı kalır).
    /// </summary>
    public string AddFromDocument(SessionContext s, NewLedgerEntry dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SourceType) || string.IsNullOrWhiteSpace(dto.SourceId))
            throw new ArgumentException("Belge kaynaklı harekette kaynak belge zorunludur.");
        return Write(s, dto);
    }

    /// <summary>
    /// G4-2 — AMBIENT TRANSACTION: belge kaynaklı cari hareketini ÇAĞIRANIN transaction'ı içinde yazar
    /// (açmaz, commit etmez). Fatura, cari + stok + belgeyi TEK transaction'da yazabilsin diye vardır.
    /// Doğrulama, yetki, firma sahipliği ve idempotency AYNEN uygulanır — bkz. <see cref="Write"/>.
    /// ⚠️ Paralel defter DEĞİLDİR: <see cref="Write"/> de aynı gövdeye delege eder.
    /// </summary>
    public string AddFromDocumentTx(DbConnection conn, DbTransaction tx, SessionContext s, NewLedgerEntry dto)
    {
        if (string.IsNullOrWhiteSpace(dto.SourceType) || string.IsNullOrWhiteSpace(dto.SourceId))
            throw new ArgumentException("Belge kaynaklı harekette kaynak belge zorunludur.");
        BranchAccess.Require(s, dto.BranchId, "cari hareketi");   // ⭐ G4-3b kapsam kapısı
        return WriteInTx(conn, tx, s, dto);
    }

    private string Write(SessionContext s, NewLedgerEntry dto)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var id = WriteInTx(conn, tx, s, dto);
        tx.Commit();
        return id;
    }

    private string WriteInTx(DbConnection conn, DbTransaction tx, SessionContext s, NewLedgerEntry dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.Amount <= 0) throw new ArgumentException("Tutar sıfırdan büyük olmalıdır.");
        if (!PartyDocTypes.IsValid(dto.DocType)) throw new ArgumentException("Belge türü geçersiz.");
        if (!Money.IsSupported(dto.Currency)) throw new ArgumentException("Para birimi geçersiz.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        EnsurePartyOwned(conn, tx, s.CompanyId, dto.PartyId);

        // IDEMPOTENCY: aynı operation_id daha önce işlendiyse mevcut hareketi döndür (çift yazma yok).
        if (!string.IsNullOrWhiteSpace(dto.OperationId))
        {
            using var chk = conn.CreateCommand();
            chk.Transaction = tx;
            chk.CommandText = "SELECT id FROM party_ledger WHERE company_id=@c AND operation_id=@op;";
            chk.AddWithValue("@c", s.CompanyId); chk.AddWithValue("@op", dto.OperationId!);
            if (chk.ExecuteScalar() is string existing) return existing;   // commit ÇAĞIRANIN işi
        }

        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO party_ledger(id, company_id, party_id, branch_id, entry_date, doc_type, doc_no, description,
                         direction, amount, currency_code, fx_rate, due_date, source_type, source_id,
                         operation_id, is_reversed, created_at, created_by, updated_at)
VALUES(@id,@c,@p,@br,@date,@type,@no,@desc,@dir,@amt,@cur,NULL,@due,@stype,@sid,@op,0,@now,@by,@now);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@p", dto.PartyId);
            cmd.AddWithValue("@br", (object?)dto.BranchId ?? DBNull.Value);
            cmd.AddWithValue("@date", dto.EntryDate ?? now);
            cmd.AddWithValue("@type", dto.DocType);
            cmd.AddWithValue("@no", (object?)dto.DocNo ?? DBNull.Value);
            cmd.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.AddWithValue("@dir", dto.IsDebit ? 1 : -1);
            cmd.AddWithValue("@amt", Money.Serialize(dto.Amount));   // decimal ölçeği korunur
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@due", (object?)dto.DueDate ?? DBNull.Value);
            cmd.AddWithValue("@stype", (object?)dto.SourceType ?? DBNull.Value);
            cmd.AddWithValue("@sid", (object?)dto.SourceId ?? DBNull.Value);
            cmd.AddWithValue("@op", (object?)dto.OperationId ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@by", s.UserId);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "party_ledger", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"party\":\"{dto.PartyId}\",\"type\":\"{dto.DocType}\",\"dir\":{(dto.IsDebit ? 1 : -1)}}}"), _clock);
        return id;
    }

    /// <summary>
    /// Ters kayıt — hareketi SİLMEZ, karşı yönde YENİ hareket yazar ve orijinali işaretler
    /// (operasyonel kayıt fiziksel silinmez, CLAUDE.md §4). Çift iptal engellenir.
    /// </summary>
    public string Reverse(SessionContext s, string entryId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        string partyId, docType, currency; long dir; decimal amount; bool reversed; string? branchId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT party_id, doc_type, direction, amount, currency_code, is_reversed, branch_id FROM party_ledger WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@id", entryId); cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Hareket bulunamadı veya başka firmaya ait.");
            partyId = r.GetString(0); docType = r.GetString(1); dir = r.GetInt64(2);
            amount = Money.Parse(r.GetString(3)); currency = r.GetString(4); reversed = r.GetInt64(5) == 1;
            branchId = r.IsDBNull(6) ? null : r.GetString(6);
        }
        if (reversed) throw new InvalidOperationException("Bu hareket zaten iptal edilmiş.");
        // ⭐ GUI-02 — kapsam kapısı: kullanıcının YETKİSİ OLMAYAN şubenin hareketi ters kaydedilemez
        // (fatura iptali/finans ters kaydı bu kapıdan zaten geçiyordu; cari defterinde eksikti).
        BranchAccess.Require(s, branchId, "cari hareketi iptali");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE party_ledger SET is_reversed=1, updated_at=@now WHERE id=@id AND company_id=@c AND is_reversed=0;";
            cmd.AddWithValue("@now", now);   // SNK-A1: damga tazelenmezse iptal senkrona HİÇ girmez
            cmd.AddWithValue("@id", entryId); cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException("Bu hareket zaten iptal edilmiş.");
        }

        // Karşı kayıt da is_reversed=1 ile yazılır: ikisi de bakiyeye GİRMEZ, ama defterde İZ kalır.
        var newId = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO party_ledger(id, company_id, party_id, branch_id, entry_date, doc_type, doc_no, description,
                         direction, amount, currency_code, due_date, source_type, source_id,
                         operation_id, is_reversed, created_at, created_by, updated_at)
VALUES(@id,@c,@p,@br,@now,@type,NULL,@desc,@dir,@amt,@cur,NULL,'reversal',@src,NULL,1,@now,@by,@now);";
            // GUI-02: karşı kayıt ASLIN ŞUBESİNİ taşır. NULL bırakılırsa "şubesiz" olur ve her şubenin
            // ekstresinde görünür (bakiyeye girmese de defterde yanlış şubede listelenirdi).
            cmd.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
            cmd.AddWithValue("@id", newId); cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@p", partyId);
            cmd.AddWithValue("@now", now); cmd.AddWithValue("@type", docType);
            cmd.AddWithValue("@desc", "İPTAL: " + reason.Trim());
            cmd.AddWithValue("@dir", -dir); cmd.AddWithValue("@amt", Money.Serialize(amount));
            cmd.AddWithValue("@cur", currency); cmd.AddWithValue("@src", entryId);
            cmd.AddWithValue("@by", s.UserId);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "party_ledger", entryId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"reversed\":true,\"reason\":\"{reason.Trim().Replace("\"", "'")}\"}}"), _clock);
        tx.Commit();
        return newId;
    }

    /// <summary>Cari finansal özeti — tamamı defterden hesaplanır (iptal edilenler HARİÇ).</summary>
    public PartyBalance Balance(SessionContext s, string partyId, IReadOnlyList<string>? branchIds = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        EnsurePartyOwned(conn, null, s.CompanyId, partyId);

        // ⭐ G4-3b: cari KARTI firma genelinde tekildir (şubeye kopyalanmaz) ama HAREKETİ şubelidir.
        // Bakiye bu yüzden kullanıcının izinli şubeleriyle sınırlanır → "firma toplamı = yetkili
        // şube toplamları" kuralı bozulmaz, şubesiz (eski) hareketler gizlenmez.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT direction, amount, entry_date FROM party_ledger WHERE company_id=@c AND party_id=@p AND is_reversed=0"
                          + BranchAccess.Sql(s, "branch_id", branchIds) + ";";
        cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@p", partyId);
        BranchAccess.Bind(cmd, s, branchIds);
        decimal debit = 0m, credit = 0m; int n = 0; long? last = null;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var amt = Money.Parse(r.GetString(1));
            if (r.GetInt64(0) > 0) debit += amt; else credit += amt;
            n++;
            var d = r.GetInt64(2);
            if (last is null || d > last) last = d;
        }
        return new PartyBalance(debit, credit, n, last);
    }

    /// <summary>
    /// Cari EKSTRESİ — tarih sırasına göre hareketler + YÜRÜYEN BAKİYE. İptal edilen hareketler
    /// gösterilir (izlenebilirlik) ama bakiyeye GİRMEZ. Sayfalama: en yeni hareketler önce istenirse
    /// <paramref name="newestFirst"/>; yürüyen bakiye her zaman kronolojik hesaplanır.
    /// </summary>
    public IReadOnlyList<PartyStatementRow> Statement(SessionContext s, string partyId,
        long? fromDate = null, long? toDate = null, int limit = 500, bool newestFirst = true,
        IReadOnlyList<string>? branchIds = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (limit < 1) limit = 1; if (limit > 2000) limit = 2000;

        using var conn = _factory.Create();
        EnsurePartyOwned(conn, null, s.CompanyId, partyId);

        using var cmd = conn.CreateCommand();
        var sql = @"
SELECT id, party_id, entry_date, doc_type, doc_no, description, direction, amount, currency_code,
       due_date, source_type, source_id, branch_id, is_reversed, created_at
FROM party_ledger WHERE company_id=@c AND party_id=@p";
        if (fromDate is not null) sql += " AND entry_date >= @from";
        if (toDate is not null) sql += " AND entry_date <= @to";
        sql += BranchAccess.Sql(s, "branch_id", branchIds);   // ⭐ G4-3b şube kapsamı
        // Kararlı sıralama: aynı tarihli hareketler kayıt sırasına göre (created_at) ayrışır.
        sql += " ORDER BY entry_date, created_at LIMIT @lim;";
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@p", partyId);
        if (fromDate is not null) cmd.AddWithValue("@from", fromDate.Value);
        if (toDate is not null) cmd.AddWithValue("@to", toDate.Value);
        BranchAccess.Bind(cmd, s, branchIds);
        cmd.AddWithValue("@lim", limit);

        var list = new List<PartyStatementRow>();
        decimal running = 0m;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var e = new PartyLedgerEntry(
                    r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                    (int)r.GetInt64(6), Money.Parse(r.GetString(7)), r.GetString(8),
                    r.IsDBNull(9) ? null : r.GetInt64(9),
                    r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
                    r.IsDBNull(12) ? null : r.GetString(12), r.GetInt64(13) == 1, r.GetInt64(14));
                if (!e.IsReversed) running += e.Direction * e.Amount;   // iptal bakiyeye girmez
                list.Add(new PartyStatementRow(e, running));
            }

        if (newestFirst) list.Reverse();
        return list;
    }

    private static void EnsurePartyOwned(DbConnection conn, DbTransaction? tx, string companyId, string partyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM parties WHERE id=@p AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@p", partyId); cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
    }
}
