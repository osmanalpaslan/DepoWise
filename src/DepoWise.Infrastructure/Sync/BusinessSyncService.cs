using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Sync;

/// <summary>
/// İş verisi SNAPSHOT senkronu (Faz 2 — güvenli "web görünürlüğü" yolu). Masaüstü kendi firmasının iş
/// tablolarını snapshot olarak sunucuya gönderir; sunucu entity-aware generic upsert eder → web adminleri
/// tüm şube verisini (salt-okunur) görür. DepoWise FTS kullanmaz; türetilmiş stock_balances de client-otoriteli
/// snapshot olarak taşınır. Generic upsert: satırın verdiği kolonlar ∩ tablo kolonları; company_id sunucuda
/// zorlanır; updated_at varsa LWW (yalnız daha yeni/eşit yazma uygulanır). FK sırası: ebeveyn tablolar önce.
/// </summary>
public sealed class BusinessSyncService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public BusinessSyncService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Ebeveyn → çocuk sırası (FK güvenliği). Sadece bu tablolar snapshot'a girer / uygulanır.
    /// Önce masaüstünde oluşturulabilen lookup/tanım ebeveynleri (materials/vehicles/maintenance FK'leri çözülsün);
    /// sonra iş kayıtları. NOT: branches PUSH'a dahil DEĞİL (web-otoriteli; kod/şifre taşır) — sunucuda zaten var.</summary>
    public static readonly string[] Tables =
    {
        // ebeveyn lookup/tanımlar (LWW: web daha yeni düzenlediyse ezilmez)
        "units",
        "suppliers",
        "brands",
        "material_categories",
        "vehicle_types",
        "vehicle_categories",
        "vehicle_models",
        "maintenance_definitions",
        // iş kayıtları
        "personnel",
        "materials",
        "stock_balances",
        "vehicles",
        "vehicle_maintenances",
        "maintenance_materials",
        "fuel_depot_entries",
        "fuel_distributions",
        "daily_activities",
        "stock_movements",
        "stock_documents",
        "material_requests",
        "material_request_items",
    };

    /// <summary>Yerel DB'den firmanın iş tablolarını JSON snapshot olarak üretir (masaüstü push için).
    /// machineId: bu cihazın adı (çakışma baseline'ı için sunucuda kullanılır).</summary>
    public string BuildSnapshot(string companyId, string? machineId = null)
    {
        using var conn = _factory.Create();
        var tables = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var table in Tables)
        {
            if (!TableExists(conn, table)) continue;
            var rows = new List<Dictionary<string, object?>>();
            using var cmd = conn.CreateCommand();
            var hasCompany = ColumnNames(conn, table).Contains("company_id");
            cmd.CommandText = hasCompany
                ? $"SELECT * FROM {table} WHERE company_id=$c;"
                : $"SELECT * FROM {table};";
            if (hasCompany) cmd.Parameters.AddWithValue("$c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (int i = 0; i < r.FieldCount; i++)
                    row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(row);
            }
            tables[table] = rows;
        }
        return JsonSerializer.Serialize(new { companyId, machineId, tables });
    }

    public sealed record ApplyResult(int Upserted, int Skipped, IReadOnlyList<string> Errors);

    public sealed record ConflictRow(string Id, string EntityType, string EntityId, string Winner,
        string? AdminName, long ServerUpdatedAt, long DeviceUpdatedAt, bool PersonnelSeen, long CreatedAt)
    {
        public string WinnerText => Winner == "device" ? "Personel (masaüstü) kazandı" : "Admin (web) kazandı";
        public string EntityLabel => EntityType switch
        {
            "materials" => "Malzeme", "vehicles" => "Araç", "personnel" => "Personel",
            "material_requests" => "Talep", "vehicle_maintenances" => "Bakım", _ => EntityType,
        };
    }

    /// <summary>Firmanın açık çakışmaları (admin ana ekran listesi için).</summary>
    public IReadOnlyList<ConflictRow> ListConflicts(string companyId, bool onlyOpen = true)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, entity_type, entity_id, winner, admin_name, server_updated_at, device_updated_at, personnel_seen, created_at " +
            "FROM data_conflicts WHERE company_id=$c " + (onlyOpen ? "AND status='open' " : "") +
            "ORDER BY created_at DESC LIMIT 200;";
        cmd.Parameters.AddWithValue("$c", companyId);
        var list = new List<ConflictRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ConflictRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7) == 1, r.GetInt64(8)));
        return list;
    }

    /// <summary>Personelin (masaüstü) HENÜZ görmediği açık çakışmalar — şube kapsamında.</summary>
    public IReadOnlyList<ConflictRow> ListUnseen(string companyId, string? branchId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, entity_type, entity_id, winner, admin_name, server_updated_at, device_updated_at, personnel_seen, created_at " +
            "FROM data_conflicts WHERE company_id=$c AND status='open' AND personnel_seen=0 " +
            (branchId is null ? "" : "AND (branch_id=$b OR branch_id IS NULL) ") +
            "ORDER BY created_at DESC LIMIT 100;";
        cmd.Parameters.AddWithValue("$c", companyId);
        if (branchId is not null) cmd.Parameters.AddWithValue("$b", branchId);
        var list = new List<ConflictRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ConflictRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5), r.GetInt64(6), r.GetInt64(7) == 1, r.GetInt64(8)));
        return list;
    }

    /// <summary>Personel uyarıları gösterildi → görüldü işaretle (şube kapsamında).</summary>
    public int MarkSeen(string companyId, string? branchId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE data_conflicts SET personnel_seen=1, updated_at=$n WHERE company_id=$c AND status='open' AND personnel_seen=0 " +
            (branchId is null ? "" : "AND (branch_id=$b OR branch_id IS NULL)") + ";";
        cmd.Parameters.AddWithValue("$n", now);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (branchId is not null) cmd.Parameters.AddWithValue("$b", branchId);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Admin çakışmayı çözümledi (listeden kaldırır).</summary>
    public void ResolveConflict(string companyId, string conflictId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE data_conflicts SET status='resolved', updated_at=$n WHERE company_id=$c AND id=$id;";
        cmd.Parameters.AddWithValue("$n", now);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$id", conflictId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Sunucu: gelen snapshot'ı firmaya uygular. company_id oturumdan zorlanır (tenant güvenliği).
    /// Her satır kendi try/catch'inde (bir satır hatası diğerlerini bozmaz); FK sırası korunur.</summary>
    /// <summary>Çakışma izlenen (admin+personel ikisinin de düzenleyebildiği) kart/kayıt tabloları.
    /// Sadece bunlarda eşzamanlı düzenleme çakışması aranır (append-only hareketlerde gürültü olmasın).</summary>
    private static readonly HashSet<string> ConflictTracked = new(StringComparer.Ordinal)
    {
        "materials", "vehicles", "personnel", "material_requests", "vehicle_maintenances",
    };

    public ApplyResult Apply(string companyId, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("tables", out var tablesEl) ||
            tablesEl.ValueKind != JsonValueKind.Object)
            return new ApplyResult(0, 0, new[] { "Geçersiz snapshot (tables yok)." });

        int upserted = 0, skipped = 0;
        var errors = new List<string>();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();

        // Cihaz baseline'ı: bu cihazın son iş-verisi push zamanı (çakışma penceresi). machineId payload'da.
        var machineId = payload.TryGetProperty("machineId", out var mEl) && mEl.ValueKind == JsonValueKind.String
            ? mEl.GetString() : null;
        var (deviceId, deviceBranchId, lastPush) = ResolveDevice(conn, companyId, machineId);

        foreach (var table in Tables) // FK-güvenli sıra
        {
            if (!tablesEl.TryGetProperty(table, out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array) continue;
            if (!TableExists(conn, table)) continue;
            var cols = ColumnNames(conn, table);
            var pk = PrimaryKey(conn, table);
            if (pk.Count == 0) continue; // PK yoksa güvenli upsert yapılamaz
            bool hasCompany = cols.Contains("company_id");
            bool hasUpdated = cols.Contains("updated_at");
            bool trackConflict = hasUpdated && ConflictTracked.Contains(table) && pk.Count == 1 && pk[0] == "id";

            foreach (var rowEl in rowsEl.EnumerateArray())
            {
                if (rowEl.ValueKind != JsonValueKind.Object) { skipped++; continue; }
                try
                {
                    // Çakışma tespiti (upsert ÖNCESİ sunucu durumu okunur)
                    if (trackConflict) DetectConflict(conn, table, companyId, deviceBranchId, lastPush, rowEl, now);
                    if (UpsertRow(conn, table, cols, pk, hasCompany, hasUpdated, companyId, rowEl, now)) upserted++;
                    else skipped++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    if (errors.Count < 20) errors.Add($"{table}: {ex.Message}");
                }
            }
        }

        // Cihazın son push zamanını ilerlet (bir sonraki çakışma penceresinin başlangıcı)
        if (deviceId is not null) SetLastPush(conn, deviceId, now);

        return new ApplyResult(upserted, skipped, errors);
    }

    private static (string? DeviceId, string? BranchId, long LastPush) ResolveDevice(SqliteConnection conn, string companyId, string? machineId)
    {
        if (string.IsNullOrWhiteSpace(machineId)) return (null, null, 0);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, branch_id, COALESCE(last_business_push_at,0) FROM sync_devices WHERE company_id=$c AND device_name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$n", machineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null, 0);
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetInt64(2));
    }

    private static void SetLastPush(SqliteConnection conn, string deviceId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_devices SET last_business_push_at=$n WHERE id=$id;";
        cmd.Parameters.AddWithValue("$n", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Sunucudaki kayıt VE gelen kayıt SON push'tan sonra değişmiş + içerik farklıysa → çakışma.
    /// LWW kazananı (device/admin) ve admin kimliği (audit_logs'tan) ile data_conflicts'e yazılır (open, tek kayıt).</summary>
    private void DetectConflict(SqliteConnection conn, string table, string companyId, string? deviceBranchId,
        long lastPush, JsonElement row, long now)
    {
        if (!row.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String) return;
        var id = idEl.GetString()!;
        long incomingUpdated = ReadLong(row, "updated_at");

        // Sunucudaki mevcut kayıt
        long serverUpdated;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT updated_at FROM {table} WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", id);
            var v = cmd.ExecuteScalar();
            if (v is null || v is DBNull) return; // sunucuda yok → yeni kayıt, çakışma değil
            serverUpdated = Convert.ToInt64(v);
        }

        // İkisi de son push'tan sonra değiştiyse ve zaman damgaları farklıysa → eşzamanlı düzenleme
        bool serverChanged = serverUpdated > lastPush;
        bool deviceChanged = incomingUpdated > lastPush;
        if (!serverChanged || !deviceChanged || serverUpdated == incomingUpdated) return;

        var winner = incomingUpdated >= serverUpdated ? "device" : "admin";
        var (adminUserId, adminName) = LastServerEditor(conn, companyId, id);

        // Aynı kayıt için açık çakışma varsa güncelle; yoksa ekle (unique index: company+entity WHERE open)
        using var up = conn.CreateCommand();
        up.CommandText = @"
INSERT INTO data_conflicts(id, company_id, branch_id, entity_type, entity_id, winner, admin_user_id, admin_name,
    server_updated_at, device_updated_at, personnel_seen, status, created_at, updated_at)
VALUES($id,$c,$b,$et,$eid,$w,$au,$an,$su,$du,0,'open',$now,$now)
ON CONFLICT(company_id, entity_id) WHERE status='open' DO UPDATE SET
    winner=excluded.winner, admin_user_id=excluded.admin_user_id, admin_name=excluded.admin_name,
    server_updated_at=excluded.server_updated_at, device_updated_at=excluded.device_updated_at,
    personnel_seen=0, updated_at=excluded.updated_at;";
        up.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        up.Parameters.AddWithValue("$c", companyId);
        up.Parameters.AddWithValue("$b", (object?)deviceBranchId ?? DBNull.Value);
        up.Parameters.AddWithValue("$et", table);
        up.Parameters.AddWithValue("$eid", id);
        up.Parameters.AddWithValue("$w", winner);
        up.Parameters.AddWithValue("$au", (object?)adminUserId ?? DBNull.Value);
        up.Parameters.AddWithValue("$an", (object?)adminName ?? DBNull.Value);
        up.Parameters.AddWithValue("$su", serverUpdated);
        up.Parameters.AddWithValue("$du", incomingUpdated);
        up.Parameters.AddWithValue("$now", now);
        up.ExecuteNonQuery();
    }

    private static (string? UserId, string? Name) LastServerEditor(SqliteConnection conn, string companyId, string entityId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT a.user_id, u.full_name, u.username FROM audit_logs a
LEFT JOIN users u ON u.id = a.user_id
WHERE a.company_id=$c AND a.entity_id=$e ORDER BY a.created_at DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$e", entityId);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0)) return (null, null);
        var uid = r.GetString(0);
        var name = !r.IsDBNull(1) ? r.GetString(1) : (!r.IsDBNull(2) ? r.GetString(2) : null);
        return (uid, name);
    }

    private static long ReadLong(JsonElement row, string name)
    {
        if (row.TryGetProperty(name, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
            if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        }
        return 0;
    }

    private bool UpsertRow(SqliteConnection conn, string table, HashSet<string> tableCols, List<string> pk, bool hasCompany,
        bool hasUpdated, string companyId, JsonElement row, long now)
    {
        // Satırın verdiği kolonlar ∩ gerçek tablo kolonları
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in row.EnumerateObject())
            if (tableCols.Contains(prop.Name))
                values[prop.Name] = JsonToDb(prop.Value);

        // PK kolonlarının hepsi gelmeli (aksi halde çakışma hedefi belirsiz)
        foreach (var k in pk)
            if (!values.TryGetValue(k, out var v) || v is null) return false;
        if (hasCompany) values["company_id"] = companyId; // tenant zorla

        var colList = values.Keys.ToList();
        var insertCols = string.Join(", ", colList);
        var insertVals = string.Join(", ", colList.Select(c => "$" + c));
        var conflictTarget = string.Join(", ", pk);
        var updateSet = string.Join(", ", colList.Where(c => !pk.Contains(c)).Select(c => $"{c}=excluded.{c}"));

        // LWW: updated_at varsa yalnız gelen >= mevcut ise güncelle
        var whereLww = hasUpdated ? $" WHERE excluded.updated_at >= {table}.updated_at" : "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = updateSet.Length == 0
            ? $"INSERT INTO {table} ({insertCols}) VALUES ({insertVals}) ON CONFLICT({conflictTarget}) DO NOTHING;"
            : $"INSERT INTO {table} ({insertCols}) VALUES ({insertVals}) ON CONFLICT({conflictTarget}) DO UPDATE SET {updateSet}{whereLww};";
        foreach (var kv in values)
            cmd.Parameters.AddWithValue("$" + kv.Key, kv.Value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return true;
    }

    private static List<string> PrimaryKey(SqliteConnection conn, string table)
    {
        var pk = new List<(int Order, string Name)>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var pkIndex = r.GetInt32(5); // pk: 0 = değil, >0 = PK sırası
            if (pkIndex > 0) pk.Add((pkIndex, r.GetString(1)));
        }
        return pk.OrderBy(p => p.Order).Select(p => p.Name).ToList();
    }

    private static object? JsonToDb(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => v.GetString(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDouble(),
        _ => v.ToString(),
    };

    private static bool TableExists(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$n LIMIT 1;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static HashSet<string> ColumnNames(SqliteConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(1)); // name kolonu index 1
        return set;
    }
}
