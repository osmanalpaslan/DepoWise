using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using System.Data.Common;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Kasa/banka hesap türü kataloğu — tek doğru kaynak (web + masaüstü aynı etiketi gösterir).</summary>
public static class FinanceAccountKinds
{
    public const string Cash = Migration068_Finance.KindCash;
    public const string Bank = Migration068_Finance.KindBank;

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Cash, "Kasa"),
        (Bank, "Banka"),
    };

    public static string Label(string? key) => All.FirstOrDefault(x => x.Key == key).Label ?? (key ?? "—");
    public static bool IsValid(string? key) => All.Any(x => x.Key == key);
}

/// <summary>
/// Para hareketi türü kataloğu.
///
/// <b>⚠️ CARİ ETKİSİ OLAN YALNIZ İKİSİ VARDIR:</b> <see cref="Receipt"/> ve <see cref="Payment"/>.
/// Transfer/açılış/düzeltme cariye DOKUNMAZ — iç transferde kimseye borç doğmaz veya kapanmaz.
/// </summary>
public static class FinanceTxnTypes
{
    /// <summary>TAHSİLAT — müşteriden para ALINDI: hesap +, cari alacağı AZALIR.</summary>
    public const string Receipt = Migration068_Finance.TxnReceipt;

    /// <summary>ÖDEME — tedarikçiye para VERİLDİ: hesap −, cari borcumuz AZALIR.</summary>
    public const string Payment = Migration068_Finance.TxnPayment;

    public const string TransferOut = Migration068_Finance.TxnTransferOut;
    public const string TransferIn = Migration068_Finance.TxnTransferIn;
    public const string Opening = Migration068_Finance.TxnOpening;
    public const string Adjustment = Migration068_Finance.TxnAdjustment;

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Receipt, "Tahsilat"),
        (Payment, "Ödeme"),
        (TransferOut, "Transfer (Çıkış)"),
        (TransferIn, "Transfer (Giriş)"),
        (Opening, "Açılış"),
        (Adjustment, "Düzeltme"),
    };

    public static string Label(string? key) => All.FirstOrDefault(x => x.Key == key).Label ?? (key ?? "—");
    public static bool IsValid(string? key) => All.Any(x => x.Key == key);

    /// <summary>Cari hareketi ÜRETEN türler — yalnız bunlarda cari zorunludur.</summary>
    public static readonly IReadOnlyList<string> PartyAffecting = new[] { Receipt, Payment };

    /// <summary>Kullanıcının hesap ekranından ELLE girebileceği türler.
    /// Tahsilat/ödeme kendi ekranından, transfer kendi işleminden gelir — elle girilip iki gerçeklik oluşmasın.</summary>
    public static readonly IReadOnlyList<string> ManualEntry = new[] { Opening, Adjustment };
}

/// <summary>Yeni kasa/banka hesabı.</summary>
public sealed record NewFinanceAccount(
    string Code, string Name, string AccountKind, string Currency = "TRY", string? BranchId = null,
    string? BankName = null, string? BankBranch = null, string? AccountNo = null, string? Iban = null,
    string? Note = null, bool IsDefault = false);

/// <summary>Hesap güncelleme (kod/tür değişebilir; sürüm ile düzenleme kilidi).</summary>
public sealed record UpdateFinanceAccount(
    string Code, string Name, string AccountKind, string Currency = "TRY", string? BranchId = null,
    string? BankName = null, string? BankBranch = null, string? AccountNo = null, string? Iban = null,
    string? Note = null, bool IsDefault = false, bool IsActive = true, long? Version = null);

/// <summary>Bir faturaya yapılacak tahsis: hangi fatura, ne kadar.</summary>
public sealed record InvoiceAllocationInput(string InvoiceId, decimal Amount);

/// <summary>
/// Tahsilat / ödeme girdisi.
/// </summary>
/// <param name="Allocations">
/// Faturaya kapama listesi. BOŞ bırakılırsa işlem "bağımsız cari tahsilatı/ödemesi" olur:
/// cari bakiyesini etkiler ama hiçbir faturayı kapatmaz.
/// </param>
/// <param name="OperationId">Idempotency anahtarı — aynı değerle ikinci çağrı ikinci hareket üretmez.</param>
public sealed record NewFinanceEntry(
    string AccountId,
    string TxnType,
    decimal Amount,
    string OperationId,
    string? PartyId = null,
    long? TxnDate = null,
    string? BranchId = null,
    string? Description = null,
    string? DocNo = null,
    string? PaymentMethod = null,
    string? ReferenceNo = null,
    string Currency = "TRY",
    IReadOnlyList<InvoiceAllocationInput>? Allocations = null);

/// <summary>İç transfer girdisi — kasa→banka, banka→kasa, kasa→kasa.</summary>
public sealed record NewFinanceTransfer(
    string FromAccountId, string ToAccountId, decimal Amount, string OperationId,
    long? TxnDate = null, string? Description = null, string Currency = "TRY");

/// <summary>Para hareketi yazma sonucu.</summary>
/// <param name="AlreadyExisted">true ise aynı operation_id daha önce işlenmişti; YENİ kayıt oluşmadı.</param>
public sealed record FinanceEntryResult(
    string TransactionId, string? LedgerEntryId, IReadOnlyList<string> AllocationIds, bool AlreadyExisted);

/// <summary>İç transfer sonucu — iki bacak tek grupta.</summary>
public sealed record FinanceTransferResult(
    string GroupId, string OutTransactionId, string InTransactionId, bool AlreadyExisted);

/// <summary>
/// G4-3 — KASA / BANKA SERVİSİ (kullanıcı isteği 2026-08-12).
///
/// <b>⚠️ PARALEL DEFTER YOK.</b> Bu servis:
/// <list type="bullet">
/// <item>cari defterine DOĞRUDAN YAZMAZ — <see cref="PartyLedgerService.AddFromDocumentTx"/> çağırır;</item>
/// <item>stok defterine HİÇ dokunmaz — para hareketi stok hareketi değildir;</item>
/// <item>fatura tablosunu GÜNCELLEMEZ — kapama <c>invoice_allocations</c>'a yazılır, faturanın
///       kalanı oradan HESAPLANIR (<c>invoices</c>'ta "ödenen" kolonu yoktur);</item>
/// <item>hiçbir bakiye SAKLAMAZ — ne hesap bakiyesi ne fatura kalanı.</item>
/// </list>
///
/// <b>TEK TRANSACTION:</b> para hareketi + cari hareketi + fatura tahsisleri + audit AYNI
/// transaction'da yazılır. Herhangi biri hata verirse HİÇBİRİ yazılmaz (G4-2'deki ambient
/// transaction deseninin aynısı).
///
/// <b>IDEMPOTENCY:</b> tek <c>operation_id</c> dallara ayrılır — para <c>op</c>, cari
/// <c>op:ledger</c>, tahsisler <c>op:alloc:{i}</c>. Aynı istek iki kez gelirse kasa iki kez hareket
/// etmez, cari iki kez etkilenmez, fatura iki kez kapanmaz.
///
/// <b>SİLME YOK, TERS KAYIT VAR:</b> <see cref="Reverse"/> karşı yönde YENİ hareket yazar, orijinali
/// <c>is_reversed=1</c> işaretler, cari karşılığını ve fatura tahsislerini de geri alır.
/// Çift iptal engellenir. Fiziksel silme yolu YOKTUR.
/// </summary>
public sealed class FinanceService
{
    public const string Module = "finance";

    /// <summary>Cari defterinde bu işlemin kaynak türü.</summary>
    public const string LedgerSourceType = "finance";

    private const int MoneyScale = 2;

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly PartyLedgerService _ledger;

    public FinanceService(IDbConnectionFactory factory, PartyLedgerService ledger, IClock? clock = null)
    {
        _factory = factory;
        _ledger = ledger;
        _clock = clock ?? new SystemClock();
    }

    private static decimal R(decimal v) => Math.Round(v, MoneyScale, MidpointRounding.AwayFromZero);

    // ═══════════════════════════════════════════════════════════════════════
    //  HESAP TANIMI
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Kasa/banka hesabı oluşturur.</summary>
    public string CreateAccount(SessionContext s, NewFinanceAccount dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        ValidateAccount(dto.Code, dto.Name, dto.AccountKind, dto.Currency, dto.Iban);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        // Şube kapsamı: belirli şubeyle çalışan kullanıcı BAŞKA şubeye hesap açamaz.
        var branchId = EnforceOwnBranch(s, dto.BranchId, "hesap tanımı");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        EnsureCodeFree(conn, tx, s.CompanyId, dto.Code, null);
        if (dto.IsDefault) ClearDefault(conn, tx, s.CompanyId, dto.AccountKind, now);

        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO finance_accounts(id, company_id, code, name, account_kind, currency_code, branch_id,
                             bank_name, bank_branch, account_no, iban, note, is_default, is_active,
                             created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@code,@name,@kind,@cur,@br,@bn,@bb,@an,@iban,@note,@def,1,@n,@n,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@code", dto.Code.Trim());
            cmd.AddWithValue("@name", dto.Name.Trim());
            cmd.AddWithValue("@kind", dto.AccountKind);
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
            cmd.AddWithValue("@bn", (object?)dto.BankName ?? DBNull.Value);
            cmd.AddWithValue("@bb", (object?)dto.BankBranch ?? DBNull.Value);
            cmd.AddWithValue("@an", (object?)dto.AccountNo ?? DBNull.Value);
            cmd.AddWithValue("@iban", (object?)Normalize(dto.Iban) ?? DBNull.Value);
            cmd.AddWithValue("@note", (object?)dto.Note ?? DBNull.Value);
            cmd.AddWithValue("@def", dto.IsDefault ? 1L : 0L);
            cmd.AddWithValue("@n", now);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "finance_accounts", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"code\":\"{dto.Code}\",\"kind\":\"{dto.AccountKind}\"}}"), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Hesap günceller. Düzenleme kilidi (sürüm) uygulanır.</summary>
    public void UpdateAccount(SessionContext s, string id, UpdateFinanceAccount dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        ValidateAccount(dto.Code, dto.Name, dto.AccountKind, dto.Currency, dto.Iban);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var branchId = EnforceOwnBranch(s, dto.BranchId, "hesap tanımı");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        EnsureAccountOwned(conn, tx, s.CompanyId, id);
        EnsureCodeFree(conn, tx, s.CompanyId, dto.Code, id);
        if (dto.IsDefault) ClearDefault(conn, tx, s.CompanyId, dto.AccountKind, now);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE finance_accounts SET code=@code, name=@name, account_kind=@kind, currency_code=@cur,
       branch_id=@br, bank_name=@bn, bank_branch=@bb, account_no=@an, iban=@iban, note=@note,
       is_default=@def, is_active=@act, updated_at=@n, version=version+1
WHERE id=@id AND company_id=@c AND is_deleted=0" + EditLockGuard.Clause(dto.Version) + ";";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@code", dto.Code.Trim());
            cmd.AddWithValue("@name", dto.Name.Trim());
            cmd.AddWithValue("@kind", dto.AccountKind);
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
            cmd.AddWithValue("@bn", (object?)dto.BankName ?? DBNull.Value);
            cmd.AddWithValue("@bb", (object?)dto.BankBranch ?? DBNull.Value);
            cmd.AddWithValue("@an", (object?)dto.AccountNo ?? DBNull.Value);
            cmd.AddWithValue("@iban", (object?)Normalize(dto.Iban) ?? DBNull.Value);
            cmd.AddWithValue("@note", (object?)dto.Note ?? DBNull.Value);
            cmd.AddWithValue("@def", dto.IsDefault ? 1L : 0L);
            cmd.AddWithValue("@act", dto.IsActive ? 1L : 0L);
            cmd.AddWithValue("@n", now);
            EditLockGuard.Bind(cmd, dto.Version);
            if (cmd.ExecuteNonQuery() == 0)
                EditLockGuard.ThrowIfStale(conn, tx, "finance_accounts", id, s.CompanyId, dto.Version);
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "finance_accounts", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>
    /// Hesabı siler (soft delete). <b>HAREKETİ OLAN HESAP SİLİNEMEZ</b> — geçmiş para hareketi
    /// sahipsiz kalmasın. Bu durumda doğru yol hesabı PASİF yapmaktır.
    /// </summary>
    public void DeleteAccount(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureAccountOwned(conn, tx, s.CompanyId, id);

        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(*) FROM finance_transactions WHERE company_id=@c AND account_id=@id;";
            chk.AddWithValue("@c", s.CompanyId); chk.AddWithValue("@id", id);
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0)
                throw new InvalidOperationException(
                    "Hareketi olan hesap silinemez. Kullanımdan kaldırmak için hesabı PASİF yapın " +
                    "(geçmiş hareketleri ve bakiyesi korunur).");
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE finance_accounts SET is_deleted=1, is_active=0, updated_at=@n, version=version+1 " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@n", now);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "finance_accounts", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Aktif/pasif — SİLME değil. Pasif hesap yeni işlemde seçilemez; geçmişi korunur.</summary>
    public void SetAccountActive(SessionContext s, string id, bool active)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureAccountOwned(conn, tx, s.CompanyId, id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE finance_accounts SET is_active=@a, updated_at=@n, version=version+1 " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@a", active ? 1L : 0L); cmd.AddWithValue("@n", now);
            cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "finance_accounts", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TAHSİLAT / ÖDEME
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tahsilat, ödeme, açılış veya düzeltme hareketi yazar; gerekiyorsa cari hareketini ve fatura
    /// tahsislerini AYNI transaction'da oluşturur.
    ///
    /// <b>YÖN KURALI (repodan doğrulandı — cari bakiyesi = Borç − Alacak, pozitif = cari BİZE borçlu):</b>
    /// <list type="bullet">
    /// <item><b>Tahsilat</b>: hesap <b>+</b> (para geldi), cari <b>alacak</b> (−1) → müşterinin borcu azalır.</item>
    /// <item><b>Ödeme</b>: hesap <b>−</b> (para gitti), cari <b>borç</b> (+1) → bizim borcumuz azalır.</item>
    /// <item><b>Açılış / Düzeltme</b>: yalnız hesap; cari ETKİLENMEZ.</item>
    /// </list>
    /// </summary>
    public FinanceEntryResult Add(SessionContext s, NewFinanceEntry dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        Validate(dto);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var date = dto.TxnDate ?? now;

        using var conn = _factory.Create();
        // IMMEDIATE: eş zamanlı tahsilatlar seri hale gelsin (fatura fazla kapanmasın).
        using var tx = conn.BeginImmediate();

        // ── IDEMPOTENCY: aynı operation_id daha önce işlendiyse MEVCUT sonucu döndür ──
        var existing = FindByOperation(conn, tx, s.CompanyId, dto.OperationId);
        if (existing is not null) return existing;

        var account = ReadAccount(conn, tx, s.CompanyId, dto.AccountId);
        if (!account.IsActive)
            throw new InvalidOperationException($"'{account.Name}' hesabı pasif; yeni işlem yapılamaz.");
        if (account.Currency != dto.Currency)
            throw new ArgumentException(
                $"Hesap para birimi ({account.Currency}) ile işlem para birimi ({dto.Currency}) aynı olmalıdır.");

        // Şube: işlem verilmediyse hesabın şubesine düşer; kısıtlı kullanıcı başka şubeye yazamaz.
        var branchId = EnforceOwnBranch(s, dto.BranchId ?? account.BranchId, "işlem");

        var direction = DirectionOf(dto.TxnType);
        var amount = R(dto.Amount);
        var txnId = Guid.NewGuid().ToString("N");

        // ── 1) CARİ: yalnız PartyLedgerService üzerinden (ikinci cari defteri YOK) ──
        string? ledgerId = null;
        if (FinanceTxnTypes.PartyAffecting.Contains(dto.TxnType))
        {
            EnsurePartyOwned(conn, tx, s.CompanyId, dto.PartyId!);
            ledgerId = _ledger.AddFromDocumentTx(conn, tx, s, new NewLedgerEntry(
                PartyId: dto.PartyId!,
                DocType: dto.TxnType == FinanceTxnTypes.Receipt ? PartyDocTypes.Receipt : PartyDocTypes.Payment,
                Amount: amount,
                // Tahsilat → ALACAK (IsDebit=false): müşterinin bize borcu azalır.
                // Ödeme    → BORÇ   (IsDebit=true):  bizim ona borcumuz azalır.
                IsDebit: dto.TxnType == FinanceTxnTypes.Payment,
                EntryDate: date,
                DocNo: dto.DocNo,
                Description: dto.Description,
                DueDate: null,
                Currency: dto.Currency,
                BranchId: branchId,
                SourceType: LedgerSourceType,
                SourceId: txnId,
                OperationId: dto.OperationId + ":ledger"));
        }

        // ── 2) PARA HAREKETİ ──
        InsertTransaction(conn, tx, txnId, s, dto.AccountId, branchId, date, dto.TxnType, direction, amount,
            dto.Currency, dto.PartyId, dto.Description, dto.DocNo, dto.PaymentMethod, dto.ReferenceNo,
            null, null, null, null, ledgerId, dto.OperationId, now);

        // ── 3) FATURA KAPAMA (tahsis) — faturaya "ödenen" yazılmaz, tahsis kaydı açılır ──
        var allocIds = new List<string>();
        var allocations = dto.Allocations ?? Array.Empty<InvoiceAllocationInput>();
        if (allocations.Count > 0)
        {
            decimal allocTotal = 0m;
            for (int i = 0; i < allocations.Count; i++)
            {
                var a = allocations[i];
                var allocAmount = R(a.Amount);
                if (allocAmount <= 0) throw new ArgumentException("Fatura kapama tutarı sıfırdan büyük olmalıdır.");
                allocTotal += allocAmount;

                var inv = ReadInvoiceForAllocation(conn, tx, s.CompanyId, a.InvoiceId);
                if (inv.Status == InvoiceStatuses.Cancelled)
                    throw new InvalidOperationException($"'{inv.InvoiceNo}' faturası iptal edilmiş; kapatılamaz.");
                if (inv.PartyId != dto.PartyId)
                    throw new ArgumentException($"'{inv.InvoiceNo}' faturası seçilen cariye ait değil.");
                // Alış faturası ÖDEME ile, satış faturası TAHSİLAT ile kapanır — ters eşleşme engellenir.
                var expected = inv.Direction == InvoiceDirections.Sales ? FinanceTxnTypes.Receipt : FinanceTxnTypes.Payment;
                if (dto.TxnType != expected)
                    throw new ArgumentException(
                        $"'{inv.InvoiceNo}' ({InvoiceDirections.Label(inv.Direction)}) yalnız " +
                        $"{FinanceTxnTypes.Label(expected).ToLowerInvariant()} ile kapatılabilir.");

                var remaining = RemainingOf(conn, tx, s.CompanyId, a.InvoiceId, inv.GrandTotal);
                if (allocAmount > remaining)
                    throw new InvalidOperationException(
                        $"'{inv.InvoiceNo}' faturasının kalanı {remaining:0.00} {inv.Currency}; " +
                        $"{allocAmount:0.00} kapatılamaz (fazla kapama engellendi).");

                allocIds.Add(InsertAllocation(conn, tx, s, a.InvoiceId, txnId, allocAmount, date,
                    dto.OperationId + ":alloc:" + i, now));
            }

            if (allocTotal > amount)
                throw new ArgumentException(
                    $"Faturalara dağıtılan tutar ({allocTotal:0.00}) işlem tutarını ({amount:0.00}) aşamaz.");
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "finance_transactions", txnId, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"type\":\"{dto.TxnType}\",\"dir\":{direction},\"amount\":\"{Money.Serialize(amount)}\"}}"), _clock);

        tx.Commit();
        return new FinanceEntryResult(txnId, ledgerId, allocIds, AlreadyExisted: false);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  İÇ TRANSFER
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hesaplar arası transfer (kasa→banka, banka→kasa, kasa→kasa).
    ///
    /// <b>İKİ BACAK, TEK GRUP:</b> kaynak hesaba −X, hedef hesaba +X yazılır ve ikisi
    /// <c>transfer_group_id</c> ile bağlanır → toplam para <b>değişmez</b> (net 0).
    ///
    /// <b>CARİ ETKİLENMEZ:</b> iç transferde kimseye borç doğmaz veya kapanmaz; bu yüzden
    /// <c>party_id</c> NULL'dır ve cari defterine hiçbir şey yazılmaz.
    /// </summary>
    public FinanceTransferResult Transfer(SessionContext s, NewFinanceTransfer dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.OperationId)) throw new ArgumentException("operation_id zorunlu.");
        if (dto.Amount <= 0) throw new ArgumentException("Tutar sıfırdan büyük olmalıdır.");
        if (!Money.IsSupported(dto.Currency)) throw new ArgumentException("Para birimi geçersiz.");
        if (string.IsNullOrWhiteSpace(dto.FromAccountId) || string.IsNullOrWhiteSpace(dto.ToAccountId))
            throw new ArgumentException("Kaynak ve hedef hesap seçilmelidir.");
        if (dto.FromAccountId == dto.ToAccountId)
            throw new ArgumentException("Kaynak ve hedef hesap aynı olamaz.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var date = dto.TxnDate ?? now;
        var amount = R(dto.Amount);

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        // IDEMPOTENCY: çıkış bacağının operation_id'si grubun anahtarıdır.
        var outOp = dto.OperationId + ":out";
        var existing = FindByOperation(conn, tx, s.CompanyId, outOp);
        if (existing is not null)
        {
            var pair = ReadTransferPair(conn, tx, s.CompanyId, existing.TransactionId);
            return new FinanceTransferResult(pair.GroupId, pair.OutId, pair.InId, AlreadyExisted: true);
        }

        var from = ReadAccount(conn, tx, s.CompanyId, dto.FromAccountId);
        var to = ReadAccount(conn, tx, s.CompanyId, dto.ToAccountId);
        if (!from.IsActive || !to.IsActive)
            throw new InvalidOperationException("Pasif hesapla transfer yapılamaz.");
        if (from.Currency != to.Currency || from.Currency != dto.Currency)
            throw new ArgumentException("Transferde iki hesabın ve işlemin para birimi aynı olmalıdır.");

        var fromBranch = EnforceOwnBranch(s, from.BranchId, "transfer");
        var toBranch = EnforceOwnBranch(s, to.BranchId, "transfer");

        var groupId = Guid.NewGuid().ToString("N");
        var outId = Guid.NewGuid().ToString("N");
        var inId = Guid.NewGuid().ToString("N");
        var desc = string.IsNullOrWhiteSpace(dto.Description)
            ? $"Transfer: {from.Name} → {to.Name}" : dto.Description;

        // party_id NULL — iç transfer cariye DOKUNMAZ.
        InsertTransaction(conn, tx, outId, s, from.Id, fromBranch, date, FinanceTxnTypes.TransferOut, -1, amount,
            dto.Currency, null, desc, null, null, null, null, null, groupId, to.Id, null, outOp, now);
        InsertTransaction(conn, tx, inId, s, to.Id, toBranch, date, FinanceTxnTypes.TransferIn, +1, amount,
            dto.Currency, null, desc, null, null, null, null, null, groupId, from.Id, null, dto.OperationId + ":in", now);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "finance_transactions", groupId, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"type\":\"transfer\",\"amount\":\"{Money.Serialize(amount)}\"}}"), _clock);

        tx.Commit();
        return new FinanceTransferResult(groupId, outId, inId, AlreadyExisted: false);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TERS KAYIT (SİLME DEĞİL)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gerekçeli ters kayıt. Hareketi SİLMEZ:
    /// <list type="number">
    /// <item>orijinal <c>is_reversed=1</c> işaretlenir (bakiyeye girmez, defterde iz kalır);</item>
    /// <item>karşı yönde yeni hareket yazılır (<c>reversal_of</c> orijinali gösterir);</item>
    /// <item>cari karşılığı varsa ters yönde cari hareketi yazılır;</item>
    /// <item>fatura tahsisleri <c>is_reversed=1</c> yapılır → faturanın kalanı geri artar.</item>
    /// </list>
    /// İç transferde İKİ BACAK BİRLİKTE geri alınır (yarım transfer kalmaz).
    /// Çift ters kayıt engellenir.
    /// </summary>
    public string Reverse(SessionContext s, string transactionId, string reason)
    {
        // Silme değil DÜZELTME işlemidir → Edit yetkisi (Delete aksiyonu bilinçli olarak kullanılmaz).
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var head = ReadTransaction(conn, tx, s.CompanyId, transactionId);
        if (head.IsReversed) throw new InvalidOperationException("Bu hareket zaten iptal edilmiş; ikinci kez iptal edilemez.");
        if (head.ReversalOf is not null) throw new InvalidOperationException("Ters kayıt hareketi ayrıca iptal edilemez.");

        // Transferde iki bacak birlikte geri alınır — yarım transfer para yaratır/yok eder.
        var legs = head.TransferGroupId is null
            ? new List<TxnHead> { head }
            : ReadGroupLegs(conn, tx, s.CompanyId, head.TransferGroupId);

        string firstNewId = "";
        foreach (var leg in legs)
        {
            if (leg.IsReversed) throw new InvalidOperationException("Transferin bir bacağı zaten iptal edilmiş.");

            MarkReversed(conn, tx, s.CompanyId, leg.Id, reason, now);

            // Fatura tahsisleri geri alınır → faturanın kalanı ARTAR.
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE invoice_allocations SET is_reversed=1, updated_at=@n " +
                                  "WHERE company_id=@c AND transaction_id=@t AND is_reversed=0;";
                cmd.AddWithValue("@n", now); cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@t", leg.Id);
                cmd.ExecuteNonQuery();
            }

            // Cari karşılığı ters yönde yazılır (yalnız tahsilat/ödemede vardır).
            string? counterLedger = null;
            if (leg.PartyId is not null && FinanceTxnTypes.PartyAffecting.Contains(leg.TxnType))
            {
                counterLedger = _ledger.AddFromDocumentTx(conn, tx, s, new NewLedgerEntry(
                    PartyId: leg.PartyId,
                    DocType: leg.TxnType == FinanceTxnTypes.Receipt ? PartyDocTypes.Receipt : PartyDocTypes.Payment,
                    Amount: leg.Amount,
                    IsDebit: leg.TxnType != FinanceTxnTypes.Payment,   // ters yön
                    EntryDate: now,
                    DocNo: leg.DocNo,
                    Description: "İPTAL: " + reason.Trim(),
                    DueDate: null,
                    Currency: leg.Currency,
                    BranchId: leg.BranchId,
                    SourceType: LedgerSourceType,
                    SourceId: leg.Id,
                    OperationId: $"finance:{leg.Id}:reverse:ledger"));
            }

            var newId = Guid.NewGuid().ToString("N");
            InsertTransaction(conn, tx, newId, s, leg.AccountId, leg.BranchId, now, leg.TxnType, -leg.Direction,
                leg.Amount, leg.Currency, leg.PartyId, "İPTAL: " + reason.Trim(), leg.DocNo, null, null,
                "reversal", leg.Id, leg.TransferGroupId, leg.CounterAccountId, counterLedger,
                $"finance:{leg.Id}:reverse", now, reversalOf: leg.Id, isReversed: true);

            if (firstNewId.Length == 0) firstNewId = newId;
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "finance_transactions", transactionId, AuditActions.Reverse, s.UserId,
            AfterJson: $"{{\"reversed\":true,\"reason\":\"{reason.Trim().Replace("\"", "'")}\"}}"), _clock);

        tx.Commit();
        return firstNewId;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ═══════════════════════════════════════════════════════════════════════

    private static void ValidateAccount(string code, string name, string kind, string currency, string? iban)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Hesap kodu zorunlu.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Hesap adı zorunlu.");
        if (!FinanceAccountKinds.IsValid(kind)) throw new ArgumentException("Hesap türü geçersiz.");
        if (!Money.IsSupported(currency)) throw new ArgumentException("Para birimi geçersiz.");

        // IBAN zorunlu DEĞİLDİR (kasa hesabında yoktur); yazıldıysa TR formatı beklenir.
        var n = Normalize(iban);
        if (n is not null)
        {
            if (n.Length != 26 || !n.StartsWith("TR", StringComparison.Ordinal) || !n.Skip(2).All(char.IsDigit))
                throw new ArgumentException("IBAN geçersiz. Türkiye IBAN'ı 'TR' ile başlar ve 26 karakterdir.");
        }
    }

    private static void Validate(NewFinanceEntry dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OperationId)) throw new ArgumentException("operation_id zorunlu.");
        if (string.IsNullOrWhiteSpace(dto.AccountId)) throw new ArgumentException("Kasa/banka hesabı seçilmelidir.");
        if (!FinanceTxnTypes.IsValid(dto.TxnType)) throw new ArgumentException("İşlem türü geçersiz.");
        if (dto.TxnType is FinanceTxnTypes.TransferIn or FinanceTxnTypes.TransferOut)
            throw new ArgumentException("Transfer bacakları elle yazılamaz; transfer işlemini kullanın.");
        if (dto.Amount <= 0) throw new ArgumentException("Tutar sıfırdan büyük olmalıdır.");
        if (!Money.IsSupported(dto.Currency)) throw new ArgumentException("Para birimi geçersiz.");

        var affectsParty = FinanceTxnTypes.PartyAffecting.Contains(dto.TxnType);
        if (affectsParty && string.IsNullOrWhiteSpace(dto.PartyId))
            throw new ArgumentException("Tahsilat ve ödemede cari seçilmelidir.");
        if (!affectsParty && !string.IsNullOrWhiteSpace(dto.PartyId))
            throw new ArgumentException("Açılış ve düzeltme hareketi cariye bağlanamaz.");
        if (!affectsParty && dto.Allocations is { Count: > 0 })
            throw new ArgumentException("Fatura kapama yalnız tahsilat ve ödemede yapılabilir.");
    }

    /// <summary>Hareket yönü: para GİREN türler +1, ÇIKAN türler −1.
    /// Düzeltme ve açılış +1'dir; negatif düzeltme gerekiyorsa ters kayıt kullanılır.</summary>
    private static int DirectionOf(string txnType) => txnType switch
    {
        FinanceTxnTypes.Receipt => +1,
        FinanceTxnTypes.Payment => -1,
        FinanceTxnTypes.TransferIn => +1,
        FinanceTxnTypes.TransferOut => -1,
        FinanceTxnTypes.Opening => +1,
        FinanceTxnTypes.Adjustment => +1,
        _ => throw new ArgumentException("İşlem türü geçersiz."),
    };

    // ═══════════════════════════════════════════════════════════════════════
    //  YARDIMCILAR
    // ═══════════════════════════════════════════════════════════════════════

    private static string? Normalize(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return null;
        return iban.Replace(" ", "").Replace("-", "").ToUpperInvariant();
    }

    /// <summary>
    /// Şube kapsamı — G4-3b ile TEK OTORİTEYE bağlandı: <see cref="BranchAccess"/>.
    /// Önceki hâli yalnız oturumun ÇALIŞMA şubesine bakıyordu; bu bir görünüm tercihidir ve
    /// web/API tarafında hiç dolmadığı için gerçek bir kapı değildi. Artık kullanıcının
    /// YETKİLİ olduğu şubeler (user_scopes / ana şube) denetleniyor — API atlanarak da geçilemez.
    /// </summary>
    private static string? EnforceOwnBranch(SessionContext s, string? branchId, string op)
        => BranchAccess.Resolve(s, branchId, op);

    private sealed record AccountHead(string Id, string Name, string Kind, string Currency, string? BranchId, bool IsActive);

    private static AccountHead ReadAccount(DbConnection conn, DbTransaction tx, string companyId, string accountId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, name, account_kind, currency_code, branch_id, is_active " +
                          "FROM finance_accounts WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", accountId); cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Kasa/banka hesabı bulunamadı veya başka firmaya ait.");
        return new AccountHead(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4), Convert.ToInt64(r.GetValue(5)) != 0);
    }

    private static void EnsureAccountOwned(DbConnection conn, DbTransaction tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM finance_accounts WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Kasa/banka hesabı bulunamadı veya başka firmaya ait.");
    }

    private static void EnsureCodeFree(DbConnection conn, DbTransaction tx, string companyId, string code, string? exceptId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM finance_accounts WHERE company_id=@c AND code=@code AND is_deleted=0" +
                          (exceptId is null ? "" : " AND id<>@id") + ";";
        cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@code", code.Trim());
        if (exceptId is not null) cmd.AddWithValue("@id", exceptId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
            throw new ArgumentException($"'{code.Trim()}' hesap kodu zaten kullanılıyor.");
    }

    private static void ClearDefault(DbConnection conn, DbTransaction tx, string companyId, string kind, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE finance_accounts SET is_default=0, updated_at=@n WHERE company_id=@c AND account_kind=@k;";
        cmd.AddWithValue("@n", now); cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@k", kind);
        cmd.ExecuteNonQuery();
    }

    private static void EnsurePartyOwned(DbConnection conn, DbTransaction tx, string companyId, string partyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM parties WHERE id=@p AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@p", partyId); cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
    }

    private sealed record InvoiceHead(string InvoiceNo, string Direction, string PartyId, string Currency,
        decimal GrandTotal, string Status);

    private static InvoiceHead ReadInvoiceForAllocation(DbConnection conn, DbTransaction tx, string companyId, string invoiceId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT invoice_no, direction, party_id, currency_code, grand_total, status " +
                          "FROM invoices WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", invoiceId); cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Fatura bulunamadı veya başka firmaya ait.");
        return new InvoiceHead(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            Money.Parse(r.GetString(4)), r.GetString(5));
    }

    /// <summary>
    /// Faturanın KALAN tutarı — <c>grand_total − Σ(iptal edilmemiş tahsisler)</c>.
    /// Saklanmaz; her seferinde hesaplanır. Toplama C#'ta decimal ile yapılır (amount TEXT'tir).
    /// </summary>
    internal static decimal RemainingOf(DbConnection conn, DbTransaction? tx, string companyId, string invoiceId, decimal grandTotal)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT amount FROM invoice_allocations WHERE company_id=@c AND invoice_id=@i AND is_reversed=0;";
        cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@i", invoiceId);
        decimal paid = 0m;
        using (var r = cmd.ExecuteReader())
            while (r.Read()) paid += Money.Parse(r.GetString(0));
        return grandTotal - paid;
    }

    private static string InsertAllocation(DbConnection conn, DbTransaction tx, SessionContext s,
        string invoiceId, string txnId, decimal amount, long date, string operationId, long now)
    {
        var id = Guid.NewGuid().ToString("N");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO invoice_allocations(id, company_id, invoice_id, transaction_id, amount, allocated_at,
                                operation_id, is_reversed, created_at, created_by, updated_at)
VALUES(@id,@c,@inv,@txn,@amt,@date,@op,0,@n,@by,@n);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@inv", invoiceId);
        cmd.AddWithValue("@txn", txnId);
        cmd.AddWithValue("@amt", Money.Serialize(amount));
        cmd.AddWithValue("@date", date);
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@by", s.UserId);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void InsertTransaction(DbConnection conn, DbTransaction tx, string id, SessionContext s,
        string accountId, string? branchId, long date, string txnType, int direction, decimal amount,
        string currency, string? partyId, string? description, string? docNo, string? paymentMethod,
        string? referenceNo, string? sourceType, string? sourceId, string? transferGroupId,
        string? counterAccountId, string? ledgerEntryId, string? operationId, long now,
        string? reversalOf = null, bool isReversed = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO finance_transactions(id, company_id, account_id, branch_id, txn_date, txn_type, direction,
                                 amount, currency_code, party_id, description, doc_no, payment_method,
                                 reference_no, source_type, source_id, transfer_group_id, counter_account_id,
                                 ledger_entry_id, operation_id, is_reversed, reversal_of, created_at,
                                 created_by, updated_at)
VALUES(@id,@c,@acc,@br,@date,@type,@dir,@amt,@cur,@p,@desc,@doc,@pm,@ref,@stype,@sid,@grp,@cacc,@led,@op,
       @rev,@revof,@n,@by,@n);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@acc", accountId);
        cmd.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
        cmd.AddWithValue("@date", date);
        cmd.AddWithValue("@type", txnType);
        cmd.AddWithValue("@dir", (long)direction);
        cmd.AddWithValue("@amt", Money.Serialize(amount));   // decimal ölçeği korunur
        cmd.AddWithValue("@cur", currency);
        cmd.AddWithValue("@p", (object?)partyId ?? DBNull.Value);
        cmd.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.AddWithValue("@doc", (object?)docNo ?? DBNull.Value);
        cmd.AddWithValue("@pm", (object?)paymentMethod ?? DBNull.Value);
        cmd.AddWithValue("@ref", (object?)referenceNo ?? DBNull.Value);
        cmd.AddWithValue("@stype", (object?)sourceType ?? DBNull.Value);
        cmd.AddWithValue("@sid", (object?)sourceId ?? DBNull.Value);
        cmd.AddWithValue("@grp", (object?)transferGroupId ?? DBNull.Value);
        cmd.AddWithValue("@cacc", (object?)counterAccountId ?? DBNull.Value);
        cmd.AddWithValue("@led", (object?)ledgerEntryId ?? DBNull.Value);
        cmd.AddWithValue("@op", (object?)operationId ?? DBNull.Value);
        cmd.AddWithValue("@rev", isReversed ? 1L : 0L);
        cmd.AddWithValue("@revof", (object?)reversalOf ?? DBNull.Value);
        cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@by", s.UserId);
        cmd.ExecuteNonQuery();
    }

    private static FinanceEntryResult? FindByOperation(DbConnection conn, DbTransaction tx, string companyId, string operationId)
    {
        string txnId; string? ledgerId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, ledger_entry_id FROM finance_transactions WHERE company_id=@c AND operation_id=@op;";
            cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@op", operationId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            txnId = r.GetString(0);
            ledgerId = r.IsDBNull(1) ? null : r.GetString(1);
        }

        var allocs = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM invoice_allocations WHERE company_id=@c AND transaction_id=@t ORDER BY created_at;";
            cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@t", txnId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) allocs.Add(r.GetString(0));
        }
        return new FinanceEntryResult(txnId, ledgerId, allocs, AlreadyExisted: true);
    }

    private sealed record TxnHead(string Id, string AccountId, string? BranchId, string TxnType, int Direction,
        decimal Amount, string Currency, string? PartyId, string? DocNo, string? TransferGroupId,
        string? CounterAccountId, bool IsReversed, string? ReversalOf);

    private const string TxnSelect =
        "SELECT id, account_id, branch_id, txn_type, direction, amount, currency_code, party_id, doc_no, " +
        "transfer_group_id, counter_account_id, is_reversed, reversal_of FROM finance_transactions";

    private static TxnHead ReadTxn(DbDataReader r) => new(
        r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3),
        Convert.ToInt32(r.GetValue(4)), Money.Parse(r.GetString(5)), r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10),
        Convert.ToInt64(r.GetValue(11)) != 0, r.IsDBNull(12) ? null : r.GetString(12));

    private static TxnHead ReadTransaction(DbConnection conn, DbTransaction tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = TxnSelect + " WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Hareket bulunamadı veya başka firmaya ait.");
        return ReadTxn(r);
    }

    /// <summary>Transferin İKİ bacağı — asıl hareketler (ters kayıtlar hariç).</summary>
    private static List<TxnHead> ReadGroupLegs(DbConnection conn, DbTransaction tx, string companyId, string groupId)
    {
        var list = new List<TxnHead>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = TxnSelect + " WHERE company_id=@c AND transfer_group_id=@g AND reversal_of IS NULL ORDER BY direction;";
        cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@g", groupId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadTxn(r));
        return list;
    }

    private sealed record TransferPair(string GroupId, string OutId, string InId);

    private static TransferPair ReadTransferPair(DbConnection conn, DbTransaction tx, string companyId, string anyLegId)
    {
        var leg = ReadTransaction(conn, tx, companyId, anyLegId);
        var group = leg.TransferGroupId ?? throw new InvalidOperationException("Transfer grubu bulunamadı.");
        var legs = ReadGroupLegs(conn, tx, companyId, group);
        var outId = legs.FirstOrDefault(x => x.Direction < 0)?.Id ?? anyLegId;
        var inId = legs.FirstOrDefault(x => x.Direction > 0)?.Id ?? anyLegId;
        return new TransferPair(group, outId, inId);
    }

    private static void MarkReversed(DbConnection conn, DbTransaction tx, string companyId, string id, string reason, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE finance_transactions SET is_reversed=1, reversal_reason=@r, updated_at=@n " +
                          "WHERE id=@id AND company_id=@c AND is_reversed=0;";
        cmd.AddWithValue("@r", reason.Trim()); cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", companyId);
        // 0 satır = araya başka bir işlem girip iptal etmiş → yarış durumu, transaction geri alınır.
        if (cmd.ExecuteNonQuery() == 0)
            throw new InvalidOperationException("Bu hareket zaten iptal edilmiş; ikinci kez iptal edilemez.");
    }
}
