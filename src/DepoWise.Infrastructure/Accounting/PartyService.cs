using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Cari tipi kataloğu — tek doğru kaynak (web ve masaüstü AYNI etiketleri gösterir).</summary>
public static class PartyTypes
{
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string Both = "both";

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Customer, "Müşteri"),
        (Supplier, "Tedarikçi"),
        (Both, "Müşteri + Tedarikçi"),
    };

    public static string Label(string? key) => All.FirstOrDefault(x => x.Key == key).Label ?? "—";
    public static bool IsValid(string? key) => All.Any(x => x.Key == key);
}

/// <summary>Yeni cari (oluşturma girdisi).</summary>
public sealed record NewParty(
    string Code, string Title, string PartyType,
    bool IsPerson = false, string? TaxOffice = null, string? TaxNo = null, string? NationalId = null,
    string? Phone = null, string? Email = null, string? Address = null, string? City = null,
    string? District = null, string Currency = "TRY", string? Note = null, string? SupplierId = null);

/// <summary>Cari güncelleme girdisi. <c>Version</c> düzenleme kilidi jetonudur.</summary>
public sealed record UpdateParty(
    string Code, string Title, string PartyType,
    bool IsPerson = false, string? TaxOffice = null, string? TaxNo = null, string? NationalId = null,
    string? Phone = null, string? Email = null, string? Address = null, string? City = null,
    string? District = null, string Currency = "TRY", string? Note = null, bool IsActive = true,
    long Version = 0);

/// <summary>Cari kartı (detay + liste ortak gövdesi).</summary>
public sealed record PartyRecord(
    string Id, string CompanyId, string Code, string Title, string PartyType,
    bool IsPerson, string? TaxOffice, string? TaxNo, string? NationalId,
    string? Phone, string? Email, string? Address, string? City, string? District,
    string Currency, string? Note, bool IsActive, string? SupplierId,
    long CreatedAt, long UpdatedAt, long Version)
{
    public string TypeText => PartyTypes.Label(PartyType);
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    /// <summary>Vergi kimliği — gerçek kişide TCKN, tüzel kişide VKN (ekranlarda tek kolon).</summary>
    public string TaxIdText => IsPerson ? (NationalId ?? "—") : (TaxNo ?? "—");
}

/// <summary>Liste satırı: kart + bakiye (tek sorguda, N+1 YOK).</summary>
public sealed record PartyListRow(PartyRecord Party, decimal Debit, decimal Credit)
{
    /// <summary>Bakiye = borç − alacak. Pozitif: cari BİZE borçlu. Negatif: biz cariye borçluyuz.</summary>
    public decimal Balance => Debit - Credit;
    public string BalanceText => Balance == 0 ? "0"
        : Balance > 0 ? $"{Balance:0.##} (B)" : $"{-Balance:0.##} (A)";
}

/// <summary>
/// ═══ G4-1 — CARİ KARTI SERVİSİ (2026-08-12) ═══
///
/// Ön muhasebenin temel varlığı. <b>Stok defterine DOKUNMAZ</b> — cari kaydı hiçbir koşulda stok
/// hareketi üretmez; stok yazımının tek yolu <c>StockService</c> olmaya devam eder (G4-2'de fatura
/// da oradan geçecek).
///
/// Mevcut desenlere birebir uyar: <c>AccessControl.Require</c> → tek transaction →
/// <c>EditLockGuard</c> (düzenleme kilidi) → <c>AuditWriter</c> → commit. Firma izolasyonu her
/// sorguda <c>company_id</c> ile zorlanır ve <b>oturumdan</b> alınır (istemciden gelen firma reddedilir).
/// </summary>
public sealed class PartyService
{
    /// <summary>Yetki modülü. Tek modül + dört aksiyon (View/Create/Edit/Delete) — ayrı ayrı
    /// "party_view/party_create/…" anahtarları AÇILMADI: mevcut modül modeli zaten aksiyon taşıyor.</summary>
    public const string Module = "parties";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public PartyService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    // ── Doğrulama (web ve masaüstü AYNI kuralları kullanır — servis katmanı tek kaynak) ──────────

    private static void Validate(string code, string title, string partyType, bool isPerson,
        string? taxNo, string? nationalId, string currency)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Cari kodu zorunlu.");
        if (code.Trim().Length > 40) throw new ArgumentException("Cari kodu en fazla 40 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Ünvan / ad soyad zorunlu.");
        if (title.Trim().Length > 200) throw new ArgumentException("Ünvan en fazla 200 karakter olabilir.");
        if (!PartyTypes.IsValid(partyType)) throw new ArgumentException("Cari tipi geçersiz.");
        if (!Money.IsSupported(currency)) throw new ArgumentException("Para birimi geçersiz.");

        // Türkiye kuralları: VKN 10 hane, TCKN 11 hane. BOŞ BIRAKILABİLİR (perakende/serbest cari) —
        // zorunlu tutmak gerçek kullanımda veri girişini kilitlerdi. Girildiyse biçim doğrulanır.
        var vkn = (taxNo ?? "").Trim();
        if (vkn.Length > 0 && (vkn.Length != 10 || !vkn.All(char.IsDigit)))
            throw new ArgumentException("Vergi numarası 10 haneli olmalıdır.");
        var tckn = (nationalId ?? "").Trim();
        if (tckn.Length > 0 && (tckn.Length != 11 || !tckn.All(char.IsDigit)))
            throw new ArgumentException("T.C. kimlik numarası 11 haneli olmalıdır.");
        if (isPerson && vkn.Length > 0 && tckn.Length > 0)
            throw new ArgumentException("Gerçek kişide hem vergi no hem T.C. kimlik no girilemez.");
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ── Oluştur ─────────────────────────────────────────────────────────────────────────────────

    public string Create(SessionContext s, NewParty dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        Validate(dto.Code, dto.Title, dto.PartyType, dto.IsPerson, dto.TaxNo, dto.NationalId, dto.Currency);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        EnsureCodeFree(conn, tx, s.CompanyId, dto.Code.Trim(), null);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO parties(id, company_id, code, title, party_type, is_person, tax_office, tax_no, national_id,
                    phone, email, address, city, district, currency_code, note, is_active, supplier_id,
                    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@code,@title,@type,@person,@tofc,@tno,@nid,@phone,@mail,@addr,@city,@dist,@cur,@note,1,@sup,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@code", dto.Code.Trim());
            cmd.AddWithValue("@title", dto.Title.Trim());
            cmd.AddWithValue("@type", dto.PartyType);
            cmd.AddWithValue("@person", dto.IsPerson ? 1 : 0);
            cmd.AddWithValue("@tofc", (object?)Trim(dto.TaxOffice) ?? DBNull.Value);
            cmd.AddWithValue("@tno", (object?)Trim(dto.TaxNo) ?? DBNull.Value);
            cmd.AddWithValue("@nid", (object?)Trim(dto.NationalId) ?? DBNull.Value);
            cmd.AddWithValue("@phone", (object?)Trim(dto.Phone) ?? DBNull.Value);
            cmd.AddWithValue("@mail", (object?)Trim(dto.Email) ?? DBNull.Value);
            cmd.AddWithValue("@addr", (object?)Trim(dto.Address) ?? DBNull.Value);
            cmd.AddWithValue("@city", (object?)Trim(dto.City) ?? DBNull.Value);
            cmd.AddWithValue("@dist", (object?)Trim(dto.District) ?? DBNull.Value);
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@note", (object?)Trim(dto.Note) ?? DBNull.Value);
            cmd.AddWithValue("@sup", (object?)Trim(dto.SupplierId) ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "party", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"code\":\"{dto.Code.Trim()}\"}}"), _clock);
        tx.Commit();
        return id;
    }

    // ── Güncelle ────────────────────────────────────────────────────────────────────────────────

    public void Update(SessionContext s, string id, UpdateParty dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        Validate(dto.Code, dto.Title, dto.PartyType, dto.IsPerson, dto.TaxNo, dto.NationalId, dto.Currency);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        EnsureOwned(conn, tx, s.CompanyId, id);
        EnsureCodeFree(conn, tx, s.CompanyId, dto.Code.Trim(), id);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE parties SET code=@code, title=@title, party_type=@type, is_person=@person, tax_office=@tofc,
       tax_no=@tno, national_id=@nid, phone=@phone, email=@mail, address=@addr, city=@city,
       district=@dist, currency_code=@cur, note=@note, is_active=@active,
       version=version+1, updated_at=@now
WHERE id=@id AND company_id=@c AND is_deleted=0" + EditLockGuard.Clause(dto.Version > 0 ? dto.Version : null) + ";";
            cmd.AddWithValue("@code", dto.Code.Trim());
            cmd.AddWithValue("@title", dto.Title.Trim());
            cmd.AddWithValue("@type", dto.PartyType);
            cmd.AddWithValue("@person", dto.IsPerson ? 1 : 0);
            cmd.AddWithValue("@tofc", (object?)Trim(dto.TaxOffice) ?? DBNull.Value);
            cmd.AddWithValue("@tno", (object?)Trim(dto.TaxNo) ?? DBNull.Value);
            cmd.AddWithValue("@nid", (object?)Trim(dto.NationalId) ?? DBNull.Value);
            cmd.AddWithValue("@phone", (object?)Trim(dto.Phone) ?? DBNull.Value);
            cmd.AddWithValue("@mail", (object?)Trim(dto.Email) ?? DBNull.Value);
            cmd.AddWithValue("@addr", (object?)Trim(dto.Address) ?? DBNull.Value);
            cmd.AddWithValue("@city", (object?)Trim(dto.City) ?? DBNull.Value);
            cmd.AddWithValue("@dist", (object?)Trim(dto.District) ?? DBNull.Value);
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@note", (object?)Trim(dto.Note) ?? DBNull.Value);
            cmd.AddWithValue("@active", dto.IsActive ? 1 : 0);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            EditLockGuard.Bind(cmd, dto.Version > 0 ? dto.Version : null);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "parties", id, s.CompanyId, dto.Version > 0 ? dto.Version : null);
                throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
            }
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "party", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Aktif/pasif — silme DEĞİL. Pasif cari yeni işlemlerde seçilmez ama geçmişi korunur.</summary>
    public void SetActive(SessionContext s, string id, bool active)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE parties SET is_active=@a, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@a", active ? 1 : 0);
            cmd.AddWithValue("@now", now); cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "party", id, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"isActive\":{(active ? "true" : "false")}}}"), _clock);
        tx.Commit();
    }

    /// <summary>
    /// Soft delete. <b>HAREKETİ OLAN CARİ SİLİNEMEZ</b> — muhasebe geçmişi bozulmamalı (stok tarafındaki
    /// MLZ-01 kuralının cari karşılığı). Kullanıcı bunun yerine PASİFE alır.
    /// </summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, id);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM party_ledger WHERE company_id=@c AND party_id=@p;";
            cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@p", id);
            var n = Convert.ToInt64(cmd.ExecuteScalar());
            if (n > 0)
                throw new InvalidOperationException(
                    $"Bu cariye ait {n} hesap hareketi var; silinemez. Kullanmayacaksanız PASİF yapın.");
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE parties SET is_deleted=1, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now); cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "party", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ── Okuma ───────────────────────────────────────────────────────────────────────────────────

    public PartyRecord Get(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectSql + " WHERE p.id=@id AND p.company_id=@c AND p.is_deleted=0;";
        cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
        return Read(r);
    }

    private const string SelectSql = @"
SELECT p.id, p.company_id, p.code, p.title, p.party_type, p.is_person, p.tax_office, p.tax_no,
       p.national_id, p.phone, p.email, p.address, p.city, p.district, p.currency_code, p.note,
       p.is_active, p.supplier_id, p.created_at, p.updated_at, p.version
FROM parties p";

    private static PartyRecord Read(DbDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
        r.GetInt64(5) == 1,
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7),
        r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
        r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
        r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13),
        r.GetString(14), r.IsDBNull(15) ? null : r.GetString(15),
        r.GetInt64(16) == 1, r.IsDBNull(17) ? null : r.GetString(17),
        r.GetInt64(18), r.GetInt64(19), r.GetInt64(20));

    /// <summary>
    /// Cari listesi — arama + tip/durum filtresi + SAYFALAMA (tüm kayıtlar RAM'e ÇEKİLMEZ).
    /// Bakiyeler TEK sorguda toplanır (satır başına ayrı sorgu = N+1 YOK).
    /// </summary>
    public GridResult<PartyListRow> List(SessionContext s, string? search = null, string? partyType = null,
        bool? onlyActive = null, int page = 1, int pageSize = 50, IReadOnlyList<string>? branchIds = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        using var conn = _factory.Create();
        var where = " WHERE p.company_id=@c AND p.is_deleted=0";
        if (!string.IsNullOrWhiteSpace(search))
            where += $" AND ({SqlDialect.LikeTr(conn, "p.code", "@q")} OR {SqlDialect.LikeTr(conn, "p.title", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "COALESCE(p.tax_no,'')", "@q")} OR {SqlDialect.LikeTr(conn, "COALESCE(p.phone,'')", "@q")})";
        if (PartyTypes.IsValid(partyType))
            where += partyType == PartyTypes.Both
                ? " AND p.party_type='both'"
                : " AND (p.party_type=@t OR p.party_type='both')";
        if (onlyActive is not null) where += " AND p.is_active=@act";

        void Bind(DbCommand cmd)
        {
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
            if (PartyTypes.IsValid(partyType) && partyType != PartyTypes.Both) cmd.AddWithValue("@t", partyType!);
            if (onlyActive is not null) cmd.AddWithValue("@act", onlyActive.Value ? 1 : 0);
        }

        int total;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM parties p" + where + ";";
            Bind(cmd);
            total = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var records = new List<PartyRecord>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = SelectSql + where + " ORDER BY p.code LIMIT @lim OFFSET @off;";
            Bind(cmd);
            cmd.AddWithValue("@lim", pageSize);
            cmd.AddWithValue("@off", (page - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read()) records.Add(Read(r));
        }
        if (records.Count == 0) return new GridResult<PartyListRow>(Array.Empty<PartyListRow>(), total, page, pageSize);

        // Bakiyeler: YALNIZ bu sayfadaki cariler için TEK sorgu. Toplama C#'ta decimal ile yapılır
        // (amount TEXT'tir; SQL SUM'ı SQLite'ta kayan noktaya düşer — Money kuralı).
        var totals = LedgerTotals(conn, s.CompanyId, records.Select(x => x.Id).ToList(), s, branchIds);
        var rows = records.Select(p =>
        {
            totals.TryGetValue(p.Id, out var t);
            return new PartyListRow(p, t.Debit, t.Credit);
        }).ToList();
        return new GridResult<PartyListRow>(rows, total, page, pageSize);
    }

    /// <summary>Verilen carilerin borç/alacak toplamları — TEK sorgu, C#'ta decimal toplama.</summary>
    internal static Dictionary<string, (decimal Debit, decimal Credit)> LedgerTotals(
        DbConnection conn, string companyId, IReadOnlyList<string> partyIds,
        SessionContext? session = null, IReadOnlyList<string>? branchIds = null)
    {
        var map = new Dictionary<string, (decimal, decimal)>(StringComparer.Ordinal);
        if (partyIds.Count == 0) return map;

        using var cmd = conn.CreateCommand();
        var names = new List<string>(partyIds.Count);
        for (int i = 0; i < partyIds.Count; i++) { var p = "@p" + i; names.Add(p); cmd.AddWithValue(p, partyIds[i]); }
        // ⭐ G4-3d: liste bakiyeleri de ŞUBE KAPSAMINDA hesaplanır — "firma toplamı = yetkili şube
        // toplamları" kuralı listede de geçerli olsun (kart ekranıyla sessiz fark oluşmasın).
        var branchSql = session is null ? "" : BranchAccess.Sql(session, "branch_id", branchIds);
        cmd.CommandText =
            $"SELECT party_id, direction, amount FROM party_ledger WHERE company_id=@c AND is_reversed=0 AND party_id IN ({string.Join(",", names)}){branchSql};";
        cmd.AddWithValue("@c", companyId);
        if (session is not null) BranchAccess.Bind(cmd, session, branchIds);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var pid = r.GetString(0);
            var dir = r.GetInt64(1);
            var amt = Money.Parse(r.GetString(2));
            map.TryGetValue(pid, out var cur);
            map[pid] = dir > 0 ? (cur.Item1 + amt, cur.Item2) : (cur.Item1, cur.Item2 + amt);
        }
        return map;
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────────────────────────

    private static void EnsureOwned(DbConnection conn, DbTransaction tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM parties WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
    }

    private static void EnsureCodeFree(DbConnection conn, DbTransaction tx, string companyId, string code, string? exceptId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM parties WHERE company_id=@c AND code=@code AND is_deleted=0"
                          + (exceptId is null ? "" : " AND id<>@id") + ";";
        cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@code", code);
        if (exceptId is not null) cmd.AddWithValue("@id", exceptId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
            throw new InvalidOperationException($"'{code}' cari kodu zaten kullanılıyor.");
    }
}
