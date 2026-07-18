using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Operations;

public sealed record NewDepotEntry(decimal Liters, decimal UnitPrice, string Currency = "TRY",
    string? SupplierId = null, string? InvoiceNo = null, string? Note = null, long? EntryDate = null, decimal? FxRate = null);

public sealed record NewDistribution(string VehicleId, decimal Liters, decimal CurrentMeter,
    decimal? UnitPrice = null, string Currency = "TRY", string? PersonnelId = null,
    long? DistributionDate = null, string? Note = null, decimal? FxRate = null,
    string? RecipientPersonnelId = null);   // "Yakıtı Alan" (kullanıcı isteği 2026-07-19) — PersonnelId="Yakıtı Veren"den ayrı

public sealed record FuelDistributionRow(string Id, string VehicleId, string? VehicleCode,
    decimal PrevMeter, decimal CurrentMeter, decimal Liters, decimal UnitPrice, string Currency, long DistributionDate);

public sealed record FuelDepotRow(string Id, decimal Liters, decimal UnitPrice, string Currency, long EntryDate, string? InvoiceNo)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(EntryDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string InvoiceDisplay => string.IsNullOrEmpty(InvoiceNo) ? "—" : InvoiceNo!;
    public string LitersText => $"{Liters:0.##}";
    public string PriceText => $"{UnitPrice:0.##} {Currency}";
}

/// <summary>
/// Yakıt depo girişi + araç dağıtımı. Dağıtım atomik (IMMEDIATE): depo bakiye kontrolü + fiyat snapshot +
/// araç sayacı ileri + meter log + audit; operation_id idempotent. Fiyat geçmişte değişmez (snapshot).
/// </summary>
public sealed class FuelService
{
    private const string Module = "fuel";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public FuelService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string AddDepotEntry(SessionContext s, NewDepotEntry dto, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.Liters <= 0) throw new ArgumentException("Litre pozitif olmalı.");
        if (!Money.IsSupported(dto.Currency)) throw new ArgumentException("Desteklenmeyen para birimi.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction(deferred: false);
        if (OperationExists(conn, tx, "fuel_depot_entries", operationId)) { tx.Commit(); return ""; }

        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO fuel_depot_entries(id, company_id, supplier_id, liters, unit_price, currency_code, fx_rate,
    invoice_no, note, entry_date, operation_id, op_branch_id, created_at, updated_at, version, is_deleted)
VALUES($id,$c,$sup,$lt,$pr,$cur,$fx,$inv,$note,$dt,$op,$opb,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$opb", (object?)s.OperatingBranchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lt", Money.Serialize(dto.Liters));
            cmd.Parameters.AddWithValue("$pr", Money.Serialize(dto.UnitPrice));
            cmd.Parameters.AddWithValue("$cur", dto.Currency);
            cmd.Parameters.AddWithValue("$fx", dto.FxRate is null ? DBNull.Value : Money.Serialize(dto.FxRate.Value));
            cmd.Parameters.AddWithValue("$inv", (object?)dto.InvoiceNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$note", (object?)dto.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dt", dto.EntryDate ?? now);
            cmd.Parameters.AddWithValue("$op", operationId);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "fuel_depot_entry", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    public string Distribute(SessionContext s, NewDistribution dto, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.Liters <= 0) throw new ArgumentException("Litre pozitif olmalı.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction(deferred: false);
        var existing = FindDistribution(conn, tx, operationId);
        if (existing is not null) { tx.Commit(); return existing; }

        // Depo bakiyesi (tüm zamanlar) yeterli mi
        var depot = DepotBalance(conn, tx, s.CompanyId);
        if (dto.Liters > depot)
            throw new InvalidOperationException($"Depo yakıtı yetersiz: mevcut {depot} L, talep {dto.Liters} L.");

        // Araç + önceki sayaç
        var prev = ReadMeter(conn, tx, s.CompanyId, dto.VehicleId);
        var price = dto.UnitPrice ?? CurrentFuelPrice(conn, tx, s.CompanyId);

        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO fuel_distributions(id, company_id, vehicle_id, prev_meter, current_meter, liters, unit_price,
    currency_code, fx_rate, personnel_id, recipient_personnel_id, distribution_date, note, operation_id, op_branch_id, created_at, updated_at, version, is_deleted)
VALUES($id,$c,$v,$prev,$cur,$lt,$pr,$ccur,$fx,$pers,$rec,$dt,$note,$op,$opb,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$opb", (object?)s.OperatingBranchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$v", dto.VehicleId);
            cmd.Parameters.AddWithValue("$prev", Money.Serialize(prev));
            cmd.Parameters.AddWithValue("$cur", Money.Serialize(dto.CurrentMeter));
            cmd.Parameters.AddWithValue("$lt", Money.Serialize(dto.Liters));
            cmd.Parameters.AddWithValue("$pr", Money.Serialize(price));
            cmd.Parameters.AddWithValue("$ccur", dto.Currency);
            cmd.Parameters.AddWithValue("$fx", dto.FxRate is null ? DBNull.Value : Money.Serialize(dto.FxRate.Value));
            cmd.Parameters.AddWithValue("$pers", (object?)dto.PersonnelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rec", (object?)dto.RecipientPersonnelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dt", dto.DistributionDate ?? now);
            cmd.Parameters.AddWithValue("$note", (object?)dto.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$op", operationId);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        // Araç sayacı ileri (geçmiş kaydı engellemez)
        if (MeterRule.ShouldAdvance(prev, dto.CurrentMeter))
        {
            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE vehicles SET current_meter=$m, version=version+1, updated_at=$now WHERE id=$id;";
                upd.Parameters.AddWithValue("$m", Money.Serialize(dto.CurrentMeter));
                upd.Parameters.AddWithValue("$now", now);
                upd.Parameters.AddWithValue("$id", dto.VehicleId);
                upd.ExecuteNonQuery();
            }
            WriteMeterLog(conn, tx, s.CompanyId, dto.VehicleId, prev, dto.CurrentMeter, now);
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "fuel_distribution", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Bu operation_id daha önce işlendi mi? (İÇE AKTARIM için salt-okunur kontrol.)
    /// Excel içe aktarımı satır başına DETERMİNİSTİK bir operation_id üretir; aynı dosya ikinci kez
    /// aktarılırsa servis zaten idempotent davranır (yeni kayıt oluşmaz) — bu metot, içe aktarım
    /// ekranının "eklendi" yerine "zaten vardı, atlandı" diyebilmesi için o durumu ÖNCEDEN görür.</summary>
    public bool OperationApplied(SessionContext s, string operationId, bool depotEntry)
    {
        if (string.IsNullOrWhiteSpace(operationId)) return false;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = depotEntry
            ? "SELECT COUNT(*) FROM fuel_depot_entries WHERE operation_id=$op AND company_id=$c;"
            : "SELECT COUNT(*) FROM fuel_distributions WHERE operation_id=$op AND company_id=$c;";
        cmd.Parameters.AddWithValue("$op", operationId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Depo bakiyesi = tüm girişler − tüm dağıtımlar (tüm zamanlar, is_deleted=0).</summary>
    public decimal GetDepotBalance(SessionContext s)
    {
        using var conn = _factory.Create();
        return DepotBalance(conn, null, s.CompanyId);
    }

    /// <summary>Güncel yakıt fiyatı = en son depo girişi birim fiyatı.</summary>
    public decimal GetCurrentFuelPrice(SessionContext s)
    {
        using var conn = _factory.Create();
        return CurrentFuelPrice(conn, null, s.CompanyId);
    }

    /// <summary>Yakıt dağıtımları (salt okuma) — en yeni önce.</summary>
    public IReadOnlyList<FuelDistributionRow> ListDistributions(SessionContext s, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT fd.id, fd.vehicle_id, v.internal_code, fd.prev_meter, fd.current_meter, fd.liters,
       fd.unit_price, fd.currency_code, fd.distribution_date
FROM fuel_distributions fd
LEFT JOIN vehicles v ON v.id = fd.vehicle_id
WHERE fd.company_id=$c AND fd.is_deleted=0
ORDER BY fd.distribution_date DESC, fd.created_at DESC LIMIT $lim;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<FuelDistributionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FuelDistributionRow(
                r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                Money.Parse(r.GetString(3)), Money.Parse(r.GetString(4)), Money.Parse(r.GetString(5)),
                Money.Parse(r.GetString(6)), r.GetString(7), r.GetInt64(8)));
        return list;
    }

    /// <summary>Depo girişleri (salt okuma) — en yeni önce.</summary>
    public IReadOnlyList<FuelDepotRow> ListDepotEntries(SessionContext s, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, liters, unit_price, currency_code, entry_date, invoice_no
FROM fuel_depot_entries WHERE company_id=$c AND is_deleted=0
ORDER BY entry_date DESC, created_at DESC LIMIT $lim;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$lim", limit);
        var list = new List<FuelDepotRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FuelDepotRow(
                r.GetString(0), Money.Parse(r.GetString(1)), Money.Parse(r.GetString(2)),
                r.GetString(3), r.GetInt64(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    // ---- yardımcılar ----
    private static decimal DepotBalance(SqliteConnection conn, SqliteTransaction? tx, string companyId)
    {
        decimal Sum(string table, string col)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT COALESCE(SUM(CAST({col} AS REAL)),0) FROM {table} WHERE company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$c", companyId);
            return Convert.ToDecimal(cmd.ExecuteScalar());
        }
        return Sum("fuel_depot_entries", "liters") - Sum("fuel_distributions", "liters");
    }

    private static decimal CurrentFuelPrice(SqliteConnection conn, SqliteTransaction? tx, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT unit_price FROM fuel_depot_entries WHERE company_id=$c AND is_deleted=0 " +
            "ORDER BY entry_date DESC, created_at DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$c", companyId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private static decimal ReadMeter(SqliteConnection conn, SqliteTransaction tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT current_meter FROM vehicles WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", vehicleId);
        cmd.Parameters.AddWithValue("$c", companyId);
        var v = cmd.ExecuteScalar();
        if (v is null) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        return Money.Parse(v as string);
    }

    private static void WriteMeterLog(SqliteConnection conn, SqliteTransaction tx, string companyId, string vehicleId,
        decimal oldVal, decimal newVal, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES($id,$c,$v,$o,$n,'fuel_distribution',$now);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$v", vehicleId);
        cmd.Parameters.AddWithValue("$o", Money.Serialize(oldVal));
        cmd.Parameters.AddWithValue("$n", Money.Serialize(newVal));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static bool OperationExists(SqliteConnection conn, SqliteTransaction tx, string table, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE operation_id=$op;";
        cmd.Parameters.AddWithValue("$op", operationId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string? FindDistribution(SqliteConnection conn, SqliteTransaction tx, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM fuel_distributions WHERE operation_id=$op;";
        cmd.Parameters.AddWithValue("$op", operationId);
        return cmd.ExecuteScalar() as string;
    }
}
