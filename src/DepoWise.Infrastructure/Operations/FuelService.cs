using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Operations;

public sealed record NewDepotEntry(decimal Liters, decimal UnitPrice, string Currency = "TRY",
    string? SupplierId = null, string? InvoiceNo = null, string? Note = null, long? EntryDate = null, decimal? FxRate = null);

public sealed record NewDistribution(string VehicleId, decimal Liters, decimal CurrentMeter,
    decimal? UnitPrice = null, string Currency = "TRY", string? PersonnelId = null,
    long? DistributionDate = null, string? Note = null, decimal? FxRate = null,
    string? RecipientPersonnelId = null,   // "Yakıtı Alan" (kullanıcı isteği 2026-07-19) — PersonnelId="Yakıtı Veren"den ayrı
    // DÜZELTME AKIŞI (kullanıcı kararı Y2, 2026-08-09): "İptal Et ve Yeniden Gir"de, iptal edilen kaydın
    // BAŞLANGIÇ SAYACI yeni kayda taşınır; yoksa yeni kayıt aracın GÜNCEL sayacını başlangıç sanar ve
    // ortadaki bir kayıt düzeltildiğinde rapor km'si bozulur. Boş bırakılırsa eski davranış (araçtan okunur).
    // ⚠️ Aracın current_meter'ını ilerletme kararı DAİMA aracın GERÇEK sayacına bakar; bu alan onu etkilemez.
    decimal? PrevMeter = null);

public sealed record FuelDistributionRow(string Id, string VehicleId, string? VehicleCode,
    decimal PrevMeter, decimal CurrentMeter, decimal Liters, decimal UnitPrice, string Currency, long DistributionDate,
    bool IsCancelled = false)
{
    public string StatusText => IsCancelled ? "İptal edildi" : "";
}

public sealed record FuelDepotRow(string Id, decimal Liters, decimal UnitPrice, string Currency, long EntryDate,
    string? InvoiceNo, bool IsCancelled = false)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(EntryDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string InvoiceDisplay => string.IsNullOrEmpty(InvoiceNo) ? "—" : InvoiceNo!;
    public string LitersText => $"{Liters:0.##}";
    public string PriceText => $"{UnitPrice:0.##} {Currency}";
    public string StatusText => IsCancelled ? "İptal edildi" : "";
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
        using var tx = conn.BeginImmediate();
        if (OperationExists(conn, tx, "fuel_depot_entries", s.CompanyId, operationId)) { tx.Commit(); return ""; }

        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO fuel_depot_entries(id, company_id, supplier_id, liters, unit_price, currency_code, fx_rate,
    invoice_no, note, entry_date, operation_id, op_branch_id, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@sup,@lt,@pr,@cur,@fx,@inv,@note,@dt,@op,@opb,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@opb", (object?)s.OperatingBranchId ?? DBNull.Value);
            cmd.AddWithValue("@sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.AddWithValue("@lt", Money.Serialize(dto.Liters));
            cmd.AddWithValue("@pr", Money.Serialize(dto.UnitPrice));
            cmd.AddWithValue("@cur", dto.Currency);
            cmd.AddWithValue("@fx", dto.FxRate is null ? DBNull.Value : Money.Serialize(dto.FxRate.Value));
            cmd.AddWithValue("@inv", (object?)dto.InvoiceNo ?? DBNull.Value);
            cmd.AddWithValue("@note", (object?)dto.Note ?? DBNull.Value);
            // ⭐ TRH-01: farklı bir iş gününe kayıt YETKİYE bağlı; yetkisizde "şimdi"ye çekilir.
            cmd.AddWithValue("@dt", DateEntryPolicy.Uygula(s, dto.EntryDate) ?? now);
            cmd.AddWithValue("@op", operationId);
            cmd.AddWithValue("@now", now);
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
        using var tx = conn.BeginImmediate();
        var existing = FindDistribution(conn, tx, s.CompanyId, operationId);
        if (existing is not null) { tx.Commit(); return existing; }

        // Depo bakiyesi (tüm zamanlar) yeterli mi
        var depot = DepotBalance(conn, tx, s.CompanyId);
        if (dto.Liters > depot)
            throw new InvalidOperationException($"Depo yakıtı yetersiz: mevcut {depot} L, talep {dto.Liters} L.");

        // Araç + önceki sayaç.
        // vehicleMeter = aracın GERÇEK sayacı → sayacı ilerletme kararı DAİMA buna bakar (Y2).
        // prev        = kayda YAZILACAK başlangıç sayacı; düzeltme akışında iptal edilen kayıttan taşınır.
        var vehicleMeter = ReadMeter(conn, tx, s.CompanyId, dto.VehicleId);
        var prev = dto.PrevMeter ?? vehicleMeter;
        if (prev < 0) throw new ArgumentException("Başlangıç sayacı negatif olamaz.");
        var price = dto.UnitPrice ?? CurrentFuelPrice(conn, tx, s.CompanyId);

        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO fuel_distributions(id, company_id, vehicle_id, prev_meter, current_meter, liters, unit_price,
    currency_code, fx_rate, personnel_id, recipient_personnel_id, distribution_date, note, operation_id, op_branch_id, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@v,@prev,@cur,@lt,@pr,@ccur,@fx,@pers,@rec,@dt,@note,@op,@opb,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@opb", (object?)s.OperatingBranchId ?? DBNull.Value);
            cmd.AddWithValue("@v", dto.VehicleId);
            cmd.AddWithValue("@prev", Money.Serialize(prev));
            cmd.AddWithValue("@cur", Money.Serialize(dto.CurrentMeter));
            cmd.AddWithValue("@lt", Money.Serialize(dto.Liters));
            cmd.AddWithValue("@pr", Money.Serialize(price));
            cmd.AddWithValue("@ccur", dto.Currency);
            cmd.AddWithValue("@fx", dto.FxRate is null ? DBNull.Value : Money.Serialize(dto.FxRate.Value));
            cmd.AddWithValue("@pers", (object?)dto.PersonnelId ?? DBNull.Value);
            cmd.AddWithValue("@rec", (object?)dto.RecipientPersonnelId ?? DBNull.Value);
            // ⭐ TRH-01: farklı bir iş gününe kayıt YETKİYE bağlı; yetkisizde "şimdi"ye çekilir.
            cmd.AddWithValue("@dt", DateEntryPolicy.Uygula(s, dto.DistributionDate) ?? now);
            cmd.AddWithValue("@note", (object?)dto.Note ?? DBNull.Value);
            cmd.AddWithValue("@op", operationId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }

        // Araç sayacı ileri (geçmiş kaydı engellemez). Karşılaştırma ARACIN GERÇEK sayacıyla yapılır —
        // düzeltme akışında taşınan başlangıç sayacı (prev) sayacı GERİ ALDIRMAZ (kullanıcı kararı Y2).
        if (MeterRule.ShouldAdvance(vehicleMeter, dto.CurrentMeter))
        {
            using (var upd = conn.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE vehicles SET current_meter=@m, version=version+1, updated_at=@now WHERE id=@id;";
                upd.AddWithValue("@m", Money.Serialize(dto.CurrentMeter));
                upd.AddWithValue("@now", now);
                upd.AddWithValue("@id", dto.VehicleId);
                upd.ExecuteNonQuery();
            }
            WriteMeterLog(conn, tx, s.CompanyId, dto.VehicleId, vehicleMeter, dto.CurrentMeter, now);
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
            ? "SELECT COUNT(*) FROM fuel_depot_entries WHERE operation_id=@op AND company_id=@c;"
            : "SELECT COUNT(*) FROM fuel_distributions WHERE operation_id=@op AND company_id=@c;";
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@c", s.CompanyId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // KAYIT İPTALİ (kullanıcı kararları Y1–Y5, 2026-08-09)
    //
    // İptal = kaydın geçerliliğini kaldırmak (is_deleted=1). Fiziksel silme YOK, üzerine yazma YOK.
    // Bakiye ve raporlar zaten "is_deleted=0" filtreli olduğu için KENDİLİĞİNDEN düzelir.
    //
    // ⚠️ ARAÇ SAYACI GERİ ALINMAZ (Y2): ne vehicles.current_meter ne vehicle_meter_logs değişir.
    //    Sayaç yalnız ileri gider — bu proje kuralıdır (yanlış bakım/uyarı hesabı doğmasın).
    // ⚠️ İptal GERİ ALINAMAZ (Y4): "iptali geri al" yoktur; doğrusu yeni kayıt olarak girilir.
    // ⚠️ Yetki (Y5): fuel/Edit + mevcut "Ters Kayıt" özel butonu — yakıta özel yeni yetki YOK.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Yakıt DEPO GİRİŞİ iptali. Bakiyeyi eksiye düşürecekse REDDEDİLİR (Y1).
    ///
    /// ⚠️ VERİ MODELİ GERÇEĞİ: <c>fuel_distributions</c> tablosunda depo girişine işaret eden bir alan
    /// YOKTUR (tek yabancı anahtarı araçtır). Depo tek havuzdur; bakiye = Σgiriş − Σdağıtım. Bu yüzden
    /// "bu girişe bağlı dağıtımlar" diye bir bağ kurulamaz — uydurma ilişki KURULMADI, migration YAPILMADI.
    /// Kullanıcının asıl amacı ("bakiye hiçbir koşulda eksiye düşmesin") BAKİYE ÜZERİNDEN korunur.
    /// </summary>
    public void CancelDepotEntry(SessionContext s, string id, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        AccessControl.RequireButton(s, SpecialButtons.Reverse);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var (liters, cancelled) = LoadForCancel(conn, tx, "fuel_depot_entries", "liters", s.CompanyId, id);
        if (cancelled) throw new InvalidOperationException("Bu yakıt kaydı zaten iptal edilmiş.");

        // Y1 — bakiye eksiye düşecek mi?
        var balance = DepotBalance(conn, tx, s.CompanyId);
        if (balance - liters < 0)
            throw new InvalidOperationException(
                $"Bu depo girişi iptal edilemez: depo bakiyesi eksiye düşer (mevcut {balance:0.##} L, " +
                $"iptal edilecek {liters:0.##} L). Önce ilgili yakıt dağıtımlarını iptal etmeniz gerekir.");

        MarkCancelled(conn, tx, "fuel_depot_entries", s.CompanyId, id, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "fuel_depot_entry", id, AuditActions.Reverse,
            s.UserId, AfterJson: $"{{\"reason\":\"{Escape(reason)}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>
    /// Yakıt DAĞITIMI iptali. Depo bakiyesi ARTAR (çıkış geri sayılmaz) → bakiye kontrolü gerekmez.
    /// Araç sayacına ve sayaç iz kayıtlarına DOKUNULMAZ (Y2).
    /// </summary>
    public void CancelDistribution(SessionContext s, string id, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        AccessControl.RequireButton(s, SpecialButtons.Reverse);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var (_, cancelled) = LoadForCancel(conn, tx, "fuel_distributions", "liters", s.CompanyId, id);
        if (cancelled) throw new InvalidOperationException("Bu yakıt kaydı zaten iptal edilmiş.");

        MarkCancelled(conn, tx, "fuel_distributions", s.CompanyId, id, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "fuel_distribution", id, AuditActions.Reverse,
            s.UserId, AfterJson: $"{{\"reason\":\"{Escape(reason)}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>İptal edilen dağıtımın BAŞLANGIÇ SAYACI — düzeltme kaydına taşınır (Y2).
    /// Kayıt yoksa ya da iptal edilmemişse null döner (düzeltme akışı yalnız iptal sonrası çalışır).</summary>
    public decimal? GetCancelledPrevMeter(SessionContext s, string distributionId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT prev_meter FROM fuel_distributions WHERE id=@id AND company_id=@c AND is_deleted=1;";
        cmd.AddWithValue("@id", distributionId);
        cmd.AddWithValue("@c", s.CompanyId);
        return cmd.ExecuteScalar() is string v ? Money.Parse(v) : null;
    }

    /// <summary>Kaydı iptal için okur: (litre, zaten iptal mi). Bulunamazsa fail-closed.</summary>
    private static (decimal Liters, bool Cancelled) LoadForCancel(DbConnection conn, DbTransaction tx,
        string table, string litersCol, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {litersCol}, is_deleted FROM {table} WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Yakıt kaydı bulunamadı veya başka firmaya ait.");
        return (Money.Parse(r.GetString(0)), r.GetInt64(1) == 1);
    }

    private static void MarkCancelled(DbConnection conn, DbTransaction tx, string table,
        string companyId, string id, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"UPDATE {table} SET is_deleted=1, version=version+1, updated_at=@now " +
                          "WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Yakıt kaydı iptal edilemedi.");
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
    /// <param name="includeCancelled">Y3: varsayılan GİZLİ; true ise iptal edilenler de gelir
    /// (ekranda üstü çizili/pasif gösterilir).</param>
    public IReadOnlyList<FuelDistributionRow> ListDistributions(SessionContext s, int limit = 200,
        bool includeCancelled = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT fd.id, fd.vehicle_id, v.internal_code, fd.prev_meter, fd.current_meter, fd.liters,
       fd.unit_price, fd.currency_code, fd.distribution_date, fd.is_deleted
FROM fuel_distributions fd
LEFT JOIN vehicles v ON v.id = fd.vehicle_id
WHERE fd.company_id=@c" + (includeCancelled ? "" : " AND fd.is_deleted=0") + BranchScope.Sql(s, "fd.op_branch_id") + @"
ORDER BY fd.distribution_date DESC, fd.created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        if (BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        cmd.AddWithValue("@lim", limit);
        var list = new List<FuelDistributionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FuelDistributionRow(
                r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                Money.Parse(r.GetString(3)), Money.Parse(r.GetString(4)), Money.Parse(r.GetString(5)),
                Money.Parse(r.GetString(6)), r.GetString(7), r.GetInt64(8), r.GetInt64(9) == 1));
        return list;
    }

    /// <summary>Depo girişleri (salt okuma) — en yeni önce.</summary>
    /// <param name="includeCancelled">Y3: varsayılan GİZLİ; true ise iptal edilenler de gelir.</param>
    public IReadOnlyList<FuelDepotRow> ListDepotEntries(SessionContext s, int limit = 200,
        bool includeCancelled = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, liters, unit_price, currency_code, entry_date, invoice_no, is_deleted
FROM fuel_depot_entries WHERE company_id=@c" + (includeCancelled ? "" : " AND is_deleted=0") + BranchScope.Sql(s, "op_branch_id") + @"
ORDER BY entry_date DESC, created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        if (BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        cmd.AddWithValue("@lim", limit);
        var list = new List<FuelDepotRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new FuelDepotRow(
                r.GetString(0), Money.Parse(r.GetString(1)), Money.Parse(r.GetString(2)),
                r.GetString(3), r.GetInt64(4), r.IsDBNull(5) ? null : r.GetString(5), r.GetInt64(6) == 1));
        return list;
    }

    // ---- yardımcılar ----
    /// <summary>
    /// DEN-D1 (denetim 2026-08-18) — <b>BU DEĞER BİR İŞ KURALI KAPISIDIR, KAYAN NOKTAYLA HESAPLANMAMALI.</b>
    ///
    /// Eskiden <c>SUM(CAST(liters AS REAL))</c> ile hesaplanıyordu. Sonuç iki yerde <b>karar</b> veriyor:
    /// "Depo yakıtı yetersiz" (dağıtım reddi) ve "bakiye eksiye düşer" (iptal reddi). Projenin kendi kuralı
    /// bunu yasaklıyor (<c>StockBalanceWriter</c>: *"SQLite'ta SUM(CAST(... AS REAL)) kayan nokta hatası
    /// üretir (Money kuralı: float yasak)"*).
    ///
    /// Somut hata: çok sayıda ondalıklı giriş biriktiğinde toplam 999,9999999999999 çıkabilir → tam 1000 L'lik
    /// dağıtım <b>haksız yere reddedilir</b>; ters yönde bakiye kıl payı eksiye düşebilir.
    ///
    /// Artık değerler <c>decimal</c> olarak okunup C#'ta toplanır — stok tarafındaki
    /// <c>RecomputeBalances</c> ile AYNI desen (SQL SUM kullanılmaz).
    /// </summary>
    private static decimal DepotBalance(DbConnection conn, DbTransaction? tx, string companyId)
    {
        decimal Sum(string table, string col)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // Tablo/kolon adları YALNIZ aşağıdaki iki sabit çağrıdan gelir — dışarıdan girdi DEĞİLDİR.
            cmd.CommandText = $"SELECT {col} FROM {table} WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", companyId);
            decimal total = 0m;
            using var r = cmd.ExecuteReader();
            while (r.Read()) total += Money.Parse(r.IsDBNull(0) ? null : r.GetString(0));
            return total;
        }
        return Sum("fuel_depot_entries", "liters") - Sum("fuel_distributions", "liters");
    }

    private static decimal CurrentFuelPrice(DbConnection conn, DbTransaction? tx, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT unit_price FROM fuel_depot_entries WHERE company_id=@c AND is_deleted=0 " +
            "ORDER BY entry_date DESC, created_at DESC LIMIT 1;";
        cmd.AddWithValue("@c", companyId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private static decimal ReadMeter(DbConnection conn, DbTransaction tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT current_meter FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        if (v is null) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        return Money.Parse(v as string);
    }

    private static void WriteMeterLog(DbConnection conn, DbTransaction tx, string companyId, string vehicleId,
        decimal oldVal, decimal newVal, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES(@id,@c,@v,@o,@n,'fuel_distribution',@now);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@o", Money.Serialize(oldVal));
        cmd.AddWithValue("@n", Money.Serialize(newVal));
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    // ⭐ FIN-B1 (ADR-179, Migration082 ile birlikte): iki yardımcı da FİRMA KAPSAMLI — başka firmanın
    // aynı operation_id'si bu firmanın yakıt işlemini atlatamaz / yabancı kayıt id'si döndüremez.
    private static bool OperationExists(DbConnection conn, DbTransaction tx, string table, string companyId, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE company_id=@c AND operation_id=@op;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", operationId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string? FindDistribution(DbConnection conn, DbTransaction tx, string companyId, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM fuel_distributions WHERE company_id=@c AND operation_id=@op;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", operationId);
        return cmd.ExecuteScalar() as string;
    }
}
