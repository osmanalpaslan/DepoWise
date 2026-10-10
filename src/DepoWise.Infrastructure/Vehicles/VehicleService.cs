using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;
using VL = DepoWise.Application.Ui.VehicleListColumns;

namespace DepoWise.Infrastructure.Vehicles;

/// <summary><paramref name="Rental"/> (2026-10-10): doluysa araç KİRALIK olarak açılır (Kiralık Araçlar ekranı).
/// null = şirket aracı (mevcut davranış, tüm eski çağrılar).</summary>
public sealed record NewVehicle(
    string InternalCode, string? Plate = null, int? ProductionYear = null,
    decimal CurrentMeter = 0m, string MeterUnit = "km", string? BranchId = null, string? DriverPersonnelId = null,
    string? ChassisNo = null, string? EngineNo = null, string Status = "active", string? StatusNote = null,
    string? VehicleTypeId = null, string? CategoryId = null, string? BrandId = null, string? VehicleModelId = null,
    string? TemplateId = null, RentalInfo? Rental = null);

/// <summary>
/// ⭐ KİRALIK ARAÇ BİLGİSİ (kullanıcı isteği 2026-10-10). <paramref name="ReplacedVehicleId"/> = bu aracın
/// YERİNE GELDİĞİ kiralık araç (arızalanıp iade edilen) — yalnız YENİ kayıtta işlenir: o araç aynı
/// transaction'da otomatik PASİFE alınır ve kira bitişi yeni aracın başlangıcı olur.
/// Tarihler UTC gün başı (IsGunuTarihi.Ms). <paramref name="PriceUnit"/>: hour/day/month (RentalPriceUnits).
/// </summary>
public sealed record RentalInfo(
    string? Company, long? Start, long? End = null, decimal? Price = null, string? PriceUnit = null,
    string? ReplacedVehicleId = null, string? ReplacementReason = null);

public sealed record VehicleRecord(
    string Id, string CompanyId, string InternalCode, string? Plate, decimal CurrentMeter, string MeterUnit,
    string Status, string? BrandId, string? VehicleModelId, int? ProductionYear, long CreatedAt);

public sealed record VehicleListRow(
    string Id, string InternalCode, string? Plate, string Status, decimal CurrentMeter, string MeterUnit, int? ProductionYear,
    bool IsRental = false)
{
    /// <summary>Araç seçimi gösterimi: "İç Kod - Plaka" (plaka boşsa yalnız iç kod). 2026-10-10: kiralık araç
    /// TÜM seçicilerde (yakıt, bakım, stok çıkışı, talep, günlük faaliyet…) "(Kiralık)" etiketiyle görünür.</summary>
    public string Display => (string.IsNullOrWhiteSpace(Plate) ? InternalCode : $"{InternalCode} - {Plate}") + (IsRental ? RentalTag : "");
    public override string ToString() => Display;
    public const string RentalTag = " (Kiralık)";
}

/// <summary>Araç listesi (kolon-bazlı filtre + sayfalama) satırı — <see cref="DepoWise.Application.Ui.VehicleListColumns"/>'taki
/// HER kolonun görüntü değerini taşır; "Bakım/Muayene" uyarısı BURADA yoktur (ekran kendi hesaplar).</summary>
public sealed record VehicleGridRow(
    string Id, string InternalCode, string? Plate, int? ProductionYear, decimal Meter, string MeterUnit,
    string Status, string StatusLabel, string? StatusNote, string? VehicleType, string? Category, string? Brand,
    string? Model, string? Branch, string? Driver, string? ChassisNo, string? EngineNo,
    // 2026-10-10 kiralık araç kolonları (şirket aracında boş):
    string? RentalCompany = null, string? RentalStart = null, string? RentalEnd = null, string? ReplacedVehicle = null);

/// <summary>Her alan için kullanıcının o kolona yazdığı filtre metni. Sıra
/// <see cref="DepoWise.Application.Ui.VehicleListColumns.All"/> ile AYNIDIR.</summary>
public sealed record VehicleGridFilter(
    string? InternalCode = null, string? Plate = null, string? ProductionYear = null, string? Meter = null,
    string? Status = null, string? StatusNote = null, string? VehicleType = null, string? Category = null,
    string? Brand = null, string? Model = null, string? Branch = null, string? Driver = null,
    string? ChassisNo = null, string? EngineNo = null,
    string? RentalCompany = null, string? RentalStart = null, string? RentalEnd = null, string? ReplacedVehicle = null);

public sealed record VehicleDetail(
    string Id, string InternalCode, string? Plate, int? ProductionYear, decimal CurrentMeter, string MeterUnit,
    string Status, string? StatusNote, string? ChassisNo, string? EngineNo,
    string? VehicleTypeId, string? CategoryId, string? BrandId, string? VehicleModelId, string? BranchId, string? DriverPersonnelId,
    string? VehicleTypeName, string? CategoryName, string? BrandName, string? VehicleModelName, string? BranchName, string? DriverName,
    // DÜZENLEME KİLİDİ: formun açıldığı andaki sürüm (bkz. EditLockGuard).
    long Version = 0,
    string? TemplateId = null,   // bağlı olduğu araç şablonu (düzenlemede korunur/değiştirilebilir)
    // ⭐ 2026-10-10 KİRALIK ARAÇ (şirket aracında IsRental=false, diğerleri boş):
    bool IsRental = false, string? RentalCompany = null, long? RentalStart = null, long? RentalEnd = null,
    decimal? RentalPrice = null, string? RentalPriceUnit = null,
    string? ReplacedVehicleId = null, string? ReplacedVehicleCode = null, string? ReplacementReason = null,
    string? ReplacedByCode = null)   // bu aracın YERİNE gelen araç (varsa) — değişim zincirinin ileri yönü
{
    public string MeterDisplay => $"{CurrentMeter:0.##} {DepoWise.Application.Ui.MeterUnitOptions.Label(MeterUnit)}";   // 2026-09-03: "hour" ekranda "saat"
    public string RentalStartText => RentalDay(RentalStart);
    public string RentalEndText => RentalDay(RentalEnd);
    public string RentalPriceText => RentalPrice is null ? "—"
        : $"{RentalPrice:0.##} TL / {DepoWise.Application.Ui.RentalPriceUnits.Label(RentalPriceUnit)}";
    public static string RentalDay(long? ms) => ms is null ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime.ToString("dd.MM.yyyy");
}

/// <summary><paramref name="Rental"/> (2026-10-10): null = kiralık alanlarına DOKUNULMAZ (bakım/hızlı düzenleme gibi
/// eski çağrılar kira bilgisini sıfırlamasın). Doluysa yalnız KİRALIK araçta yazılır; değişim bağı
/// (<c>replaced_vehicle_id</c>) düzenlemede değişmez.</summary>
public sealed record UpdateVehicle(string? Plate, int? ProductionYear, string Status, string? StatusNote,
    string? ChassisNo = null, string? EngineNo = null,
    string? VehicleTypeId = null, string? CategoryId = null, string? BrandId = null, string? VehicleModelId = null,
    string? BranchId = null, string? DriverPersonnelId = null, string? TemplateId = null, RentalInfo? Rental = null);

/// <summary>İşlem Geçmişi (madde 4, 2026-08-06): araç için tek satır — oluşturma/şube transferi/genel güncelleme
/// (audit_logs) + sayaç değişimi (vehicle_meter_logs, kaynağı yakıt/bakım/manuel) birleştirilip tarihe göre azalan
/// sıralanır. Salt-okunur; hiçbir alan bu listeden düzenlenemez.</summary>
public sealed record VehicleHistoryRow(long Date, string Label, string? Detail)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(Date).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
}

/// <summary>
/// Araç kartı — iç kod benzersiz; şablondan doldurma + şablon malzemelerini araca kopyalama (aynı transaction);
/// sayaç geriye gidemez (MeterRule) ve tüm değişimler vehicle_meter_logs'a yazılır.
/// </summary>
public sealed class VehicleService
{
    private const string Module = "vehicles";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public VehicleService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext s, NewVehicle dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        // ⭐ FAZ 4.10: şablon SEÇİLMEDEN kayıt açmak ayrı bir yetkidir (malzemeyle AYNI kural/metin).
        // 2026-10-10: KİRALIK araç bu kapıdan MUAF — şablonlar şirketin KENDİ filosunun standardıdır; dışarıdan
        // kiralanan makine için şablon tanımlanmaz ve kapı kiralık kaydını gereksiz yere kilitlerdi.
        if (dto.Rental is null)
            DepoWise.Infrastructure.Materials.MaterialService.SablonKapisi(s, dto.TemplateId, "araç", _factory, "vehicle_templates");
        if (string.IsNullOrWhiteSpace(dto.InternalCode)) throw new ArgumentException("İç kod zorunlu.");
        // ⭐ FAZ 4 FINAL QA (2026-09-06) — NEGATİF SAYAÇ REDDEDİLİR.
        // QA sırasında ölçüldü: kayıt açarken sayaç eksi verilebiliyordu (ör. −5000) ve sessizce
        // yazılıyordu. Sayaç yakıt tüketimi ve bakım periyodu hesaplarının GİRDİSİDİR; eksi bir
        // başlangıç bu hesapları ve raporları bozar. Doğrudan sayaç değiştirme yolunda (SetMeter)
        // aynı koruma zaten "geriye gitmez" kuralıyla vardı; kayıt AÇILIŞINDA yoktu.
        if (dto.CurrentMeter < 0) throw new ArgumentException("Sayaç eksi olamaz.");

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (CodeExists(conn, tx, s.CompanyId, dto.InternalCode))
            throw new InvalidOperationException($"İç kod zaten kullanılıyor: {dto.InternalCode}");

        // Şablon seçildiyse boş alanları doldur (kullanıcı değeri öncelikli)
        var applied = ApplyTemplate(conn, tx, s.CompanyId, dto);

        // Plaka benzersiz (kullanıcı isteği 2026-08-05): aynı firmada bir plaka tek araçta olabilir.
        // YEREL kontrol — sync ayrı upsert yolundan gider, offline çakışmada patlamaz.
        if (!string.IsNullOrWhiteSpace(applied.Plate) && PlateExists(conn, tx, s.CompanyId, applied.Plate!, excludeId: null))
            throw new InvalidOperationException($"Bu plaka zaten kayıtlı: {applied.Plate}");

        // B-7 (PRT-01 Grup 5, 2026-08-11): şube ve sürücü id'leri İSTEMCİDEN gelir → firmaya ait oldukları
        // doğrulanır. Aksi halde başka firmanın şubesine/personeline bağlı araç oluşturulabiliyordu ve
        // liste JOIN'leri o kaydın ADINI gösteriyordu. Emsal: PersonnelService (ScopeResolver) ve
        // B-2/B-3'teki EnsureVehicleOwned deseni.
        EnsureBranchOwned(conn, tx, s.CompanyId, applied.BranchId);
        EnsurePersonnelOwned(conn, tx, s.CompanyId, applied.DriverPersonnelId);
        var rental = applied.Rental is null ? null : ValidateRental(applied.Rental);
        if (rental?.ReplacedVehicleId is { } eskiId) EnsureReplaceable(conn, tx, s.CompanyId, eskiId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO vehicles(id, company_id, internal_code, plate, production_year, current_meter, meter_unit,
    branch_id, driver_personnel_id, chassis_no, engine_no, status, status_note,
    vehicle_type_id, category_id, brand_id, vehicle_model_id, template_id,
    is_rental, rental_company, rental_start, rental_end, rental_price, rental_price_unit, replaced_vehicle_id, replacement_reason,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@ic,@plate,@yr,@meter,@mu,@br,@drv,@ch,@en,@st,@note,@vt,@cat,@brand,@vm,@tpl,
    @rent,@rco,@rst,@ren,@rpr,@rpu,@rrv,@rrs,@now,@now,1,0);";
            BindRental(cmd, rental);
            cmd.AddWithValue("@rent", rental is null ? 0 : 1);
            cmd.AddWithValue("@rrv", (object?)rental?.ReplacedVehicleId ?? DBNull.Value);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@ic", applied.InternalCode.Trim());
            cmd.AddWithValue("@plate", (object?)applied.Plate ?? DBNull.Value);
            cmd.AddWithValue("@yr", (object?)applied.ProductionYear ?? DBNull.Value);
            cmd.AddWithValue("@meter", Money.Serialize(applied.CurrentMeter));
            cmd.AddWithValue("@mu", applied.MeterUnit);
            cmd.AddWithValue("@br", (object?)applied.BranchId ?? DBNull.Value);
            cmd.AddWithValue("@drv", (object?)applied.DriverPersonnelId ?? DBNull.Value);
            cmd.AddWithValue("@ch", (object?)applied.ChassisNo ?? DBNull.Value);
            cmd.AddWithValue("@en", (object?)applied.EngineNo ?? DBNull.Value);
            cmd.AddWithValue("@st", applied.Status);
            // Durum açıklaması yalnız "çalışmıyor" durumlarında saklanır (Bakımda + Arızalı — ortak kural).
            cmd.AddWithValue("@note", DepoWise.Application.Ui.VehicleStatus.NeedsNote(applied.Status) ? (object?)applied.StatusNote ?? DBNull.Value : DBNull.Value);
            cmd.AddWithValue("@vt", (object?)applied.VehicleTypeId ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)applied.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@brand", (object?)applied.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@vm", (object?)applied.VehicleModelId ?? DBNull.Value);
            cmd.AddWithValue("@tpl", (object?)applied.TemplateId ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }

        // Şablonun uyumlu malzemeleri yeni aracın material_compatible_vehicles kayıtlarına kopyalanır
        if (applied.TemplateId is not null)
            CopyTemplateMaterials(conn, tx, applied.TemplateId, id);

        // Açılış sayacı > 0 ise log
        if (applied.CurrentMeter > 0)
            WriteMeterLog(conn, tx, s.CompanyId, id, 0m, applied.CurrentMeter, "vehicle_create", now);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", id, AuditActions.Create, s.UserId), _clock);

        // ⭐ ARAÇ DEĞİŞİMİ (kullanıcı isteği 2026-10-10): yerine gelinen kiralık araç AYNI transaction'da
        // pasife alınır ve kira bitişi yeni aracın başlangıç günü olur → yarım durum (yeni araç açıldı
        // ama eskisi hâlâ aktif) oluşamaz.
        if (rental?.ReplacedVehicleId is { } replacedId)
            RetireReplaced(conn, tx, s, replacedId, applied.InternalCode.Trim(), rental.Start ?? now, rental.ReplacementReason, now);

        tx.Commit();
        return id;
    }

    // ═══════════════════════ KİRALIK ARAÇLAR (kullanıcı isteği 2026-10-10) ═══════════════════════

    /// <summary>Kira bilgisini doğrular ve temizler. Kiralayan firma + başlangıç ZORUNLU.</summary>
    private static RentalInfo ValidateRental(RentalInfo r)
    {
        var company = string.IsNullOrWhiteSpace(r.Company) ? null : r.Company.Trim();
        if (company is null) throw new ArgumentException("Kiralık araçta kiralayan firma zorunludur.");
        if (company.Length > 200) throw new ArgumentException("Kiralayan firma adı en fazla 200 karakter olabilir.");
        if (r.Start is null) throw new ArgumentException("Kiralık araçta kira başlangıç tarihi zorunludur.");
        if (r.End is not null && r.End < r.Start) throw new ArgumentException("Kira bitişi başlangıçtan önce olamaz.");
        if (r.Price is < 0) throw new ArgumentException("Kira bedeli eksi olamaz.");
        var unit = r.Price is null ? null : DepoWise.Application.Ui.RentalPriceUnits.Normalize(r.PriceUnit);
        var reason = string.IsNullOrWhiteSpace(r.ReplacementReason) ? null : r.ReplacementReason.Trim();
        var replaced = string.IsNullOrWhiteSpace(r.ReplacedVehicleId) ? null : r.ReplacedVehicleId.Trim();
        return r with { Company = company, PriceUnit = unit, ReplacementReason = reason, ReplacedVehicleId = replaced };
    }

    private static void BindRental(DbCommand cmd, RentalInfo? r)
    {
        cmd.AddWithValue("@rco", (object?)r?.Company ?? DBNull.Value);
        cmd.AddWithValue("@rst", (object?)r?.Start ?? DBNull.Value);
        cmd.AddWithValue("@ren", (object?)r?.End ?? DBNull.Value);
        cmd.AddWithValue("@rpr", r?.Price is { } p ? Money.Serialize(p) : DBNull.Value);
        cmd.AddWithValue("@rpu", (object?)r?.PriceUnit ?? DBNull.Value);
        cmd.AddWithValue("@rrs", (object?)r?.ReplacementReason ?? DBNull.Value);
    }

    /// <summary>Değiştirilecek araç: bu firmanın, silinmemiş, KİRALIK ve henüz pasif olmayan aracı olmalı.</summary>
    private static void EnsureReplaceable(DbConnection conn, DbTransaction tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT is_rental, status FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Değiştirilecek araç bulunamadı veya başka firmaya ait.");
        if (Convert.ToInt64(r.GetValue(0)) != 1) throw new InvalidOperationException("Yalnız kiralık bir aracın yerine değişim kaydı girilebilir.");
        if (r.GetString(1) == DepoWise.Application.Ui.VehicleStatus.Passive)
            throw new InvalidOperationException("Seçilen kiralık araç zaten pasif (iade edilmiş).");
    }

    /// <summary>Giden aracı pasife alır, kira bitişini yazar ve geçmişine "yerine X geldi" notu düşer.</summary>
    private void RetireReplaced(DbConnection conn, DbTransaction tx, SessionContext s, string vehicleId,
        string newCode, long endDay, string? reason, long now)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE vehicles SET status=@st, status_note=NULL, rental_end=COALESCE(rental_end, @end), " +
                "version=version+1, updated_at=@now WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@st", DepoWise.Application.Ui.VehicleStatus.Passive);
            cmd.AddWithValue("@end", endDay);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        var after = $"{{\"replacedBy\":{JsonStr(newCode)},\"reason\":{JsonStr(reason)}}}";
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId,
            AfterJson: after), _clock);
    }

    /// <summary>
    /// Kiralamayı bitirir (araç kiralayan firmaya İADE edildi): durum PASİF, kira bitişi = verilen gün.
    /// Kayıt SİLİNMEZ — yakıt/bakım geçmişi raporlarda kalır.
    /// </summary>
    public void EndRental(SessionContext s, string vehicleId, long endDay)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        long? start;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT is_rental, rental_start FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
            read.AddWithValue("@id", vehicleId);
            read.AddWithValue("@c", s.CompanyId);
            using var r = read.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
            if (Convert.ToInt64(r.GetValue(0)) != 1) throw new InvalidOperationException("Yalnız kiralık aracın kiralaması bitirilebilir.");
            start = r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1));
        }
        if (start is not null && endDay < start) throw new ArgumentException("Kira bitişi başlangıçtan önce olamaz.");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vehicles SET status=@st, status_note=NULL, rental_end=@end, version=version+1, updated_at=@now " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@st", DepoWise.Application.Ui.VehicleStatus.Passive);
            cmd.AddWithValue("@end", endDay);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"rentalEnded\":{endDay}}}"), _clock);
        tx.Commit();
    }

    /// <summary>Değişim seçicisi için: bu firmanın AKTİF (pasif olmayan) kiralık araçları.</summary>
    public IReadOnlyList<VehicleListRow> ListActiveRentals(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, internal_code, plate, status, current_meter, meter_unit, production_year, is_rental FROM vehicles " +
            "WHERE company_id=@c AND is_deleted=0 AND is_rental=1 AND status<>@p ORDER BY internal_code;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@p", DepoWise.Application.Ui.VehicleStatus.Passive);
        return ReadListRows(cmd);
    }

    private static List<VehicleListRow> ReadListRows(DbCommand cmd)
    {
        var list = new List<VehicleListRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VehicleListRow(
                r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3), Money.Parse(r.GetString(4)), r.GetString(5),
                r.IsDBNull(6) ? (int?)null : r.GetInt32(6),
                !r.IsDBNull(7) && Convert.ToInt64(r.GetValue(7)) == 1));
        return list;
    }

    /// <summary>İleri-yön sayaç: yeni &gt; mevcut ise ilerletir + loglar (true). Aksi halde no-op (false).
    /// Bakım/yakıt geçmiş kayıtlarını ENGELLEMEZ. Tüm ilerlemeler loglanır.</summary>
    public bool AdvanceMeter(SessionContext s, string vehicleId, decimal value, string source)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var current = ReadMeter(conn, tx, s.CompanyId, vehicleId);
        if (!MeterRule.ShouldAdvance(current, value)) { tx.Commit(); return false; }
        UpdateMeter(conn, tx, vehicleId, value, now);
        WriteMeterLog(conn, tx, s.CompanyId, vehicleId, current, value, source, now);
        tx.Commit();
        return true;
    }

    /// <summary>Doğrudan sayaç düzenleme (araç formu). Geriye gitme YASAK → MeterBackwardException.</summary>
    public void SetMeter(SessionContext s, string vehicleId, decimal value, string source = "vehicle_form")
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (value < 0) throw new ArgumentException("Sayaç eksi olamaz.");   // FAZ 4 FINAL QA (2026-09-06)
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var current = ReadMeter(conn, tx, s.CompanyId, vehicleId);
        if (!MeterRule.IsValidDirectSet(current, value))
            throw new MeterBackwardException($"Sayaç geriye alınamaz: mevcut {current}, girilen {value}.");
        if (value != current)
        {
            UpdateMeter(conn, tx, vehicleId, value, now);
            WriteMeterLog(conn, tx, s.CompanyId, vehicleId, current, value, source, now);
        }
        tx.Commit();
    }

    public decimal GetMeter(SessionContext s, string vehicleId)
    {
        using var conn = _factory.Create();
        return ReadMeter(conn, null, s.CompanyId, vehicleId);
    }

    // ═══ FAZ 4.1 (2026-09-06) — SAYACI GERÇEK KAYITLARDAN YENİDEN HESAPLA ═══════════════════════
    // Gerçek olay: yanlış-yüksek sayaç girilen kayıt düzeltildi ama araçta ESKİ (hatalı) değer kaldı;
    // kullanıcı hiçbir ekrandan düzeltemedi (sayaç geriye alınamıyordu). Artık sayaç, GEÇERLİ
    // kayıtlardan türetilir; iptal/düzeltme onu aşağı da çekebilir. Ayrıntı: VehicleMeterService.

    /// <summary>
    /// TEK aracın sayacını yeniden hesaplar. Değişiklik <c>vehicle_meter_logs</c>'a
    /// <c>recalc:*</c> kaynağıyla yazılır — iz kaybolmaz.
    /// </summary>
    /// <returns>Sayaç değiştiyse true.</returns>
    public bool RecalculateMeter(SessionContext s, string vehicleId, string kaynak = "manual")
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        return VehicleMeterService.Tazele(_factory, s.CompanyId, vehicleId, kaynak, _clock.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// TÜM araçların sayacını yeniden hesaplar (geçmişte zehirlenmiş kayıtların toplu onarımı).
    /// Kullanıcı bildirdi: <i>"bu sorun başka araçlarda da bulunmakta."</i>
    ///
    /// ⚠️ Yalnız <c>vehicles.current_meter</c> özetini düzeltir; hiçbir yakıt/bakım kaydına
    /// dokunmaz, hiçbir kayıt silmez. Elle beyan edilen taban korunur.
    /// </summary>
    /// <returns>Sayacı düzelen araç sayısı.</returns>
    public int RecalculateAllMeters(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var idler = new List<string>();
        using (var conn = _factory.Create())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id FROM vehicles WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) idler.Add(r.GetString(0));
        }

        int duzelen = 0;
        foreach (var id in idler)
            if (VehicleMeterService.Tazele(_factory, s.CompanyId, id, "bulk", now)) duzelen++;
        return duzelen;
    }

    /// <summary>
    /// Araç sayaç geçmişi. ⭐ SEC-02 (denetim 2026-08-25): metot FİRMA FİLTRESİ OLMADAN ve oturum
    /// almadan yazılmıştı. Bugün hiçbir yerden çağrılmıyor (ölü kod) ama bir ekrana bağlandığı anda
    /// başka firmanın sayaç geçmişini döndürürdü — sessiz bir tuzak. Kapı şimdi kapalı: oturum zorunlu,
    /// firma filtresi sorguda.
    /// </summary>
    public IReadOnlyList<(decimal Old, decimal New, string Source)> MeterHistory(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT old_value, new_value, source FROM vehicle_meter_logs " +
                          "WHERE vehicle_id=@v AND company_id=@c ORDER BY created_at;";
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<(decimal, decimal, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((Money.Parse(r.GetString(0)), Money.Parse(r.GetString(1)), r.GetString(2)));
        return list;
    }

    /// <summary>Araç listesi (salt okuma) — iç kod/plaka araması; firma kapsamı + is_deleted=0.</summary>
    public IReadOnlyList<VehicleListRow> List(SessionContext s, string? search = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT id, internal_code, plate, status, current_meter, meter_unit, production_year, is_rental
FROM vehicles
WHERE company_id=@c AND is_deleted=0
  AND (CAST(@s AS TEXT) IS NULL OR {SqlDialect.LikeTr(conn, "internal_code", "@like")} OR {SqlDialect.LikeTr(conn, "COALESCE(plate,'')", "@like")})
ORDER BY internal_code LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.AddWithValue("@s", (object?)term ?? DBNull.Value);
        cmd.AddWithValue("@like", term is null ? "%" : "%" + term + "%");
        cmd.AddWithValue("@lim", limit);
        return ReadListRows(cmd);
    }

    /// <summary>
    /// ⭐ RPR-04 (denetim 2026-08-25) — RAPOR FİLTRESİ için ŞUBE KAPSAMLI araç listesi.
    ///
    /// <b>Neden ayrı metot:</b> <see cref="List"/> BİLİNÇLİ olarak firma genelidir ve 20'den fazla yerden
    /// çağrılır — özellikle içe aktarma servisleri kod/ad çözerken TÜM araçlara ihtiyaç duyar. Onu
    /// daraltmak çalışan akışları kırardı. Bu metot yalnız rapor filtresinin (açılır liste) kaynağıdır.
    ///
    /// Kural tek otoriteden gelir (<see cref="BranchAccess.AllowedSql"/>): kullanıcının izinli şubeleri
    /// + ŞUBESİZ araçlar. Kapsamsız kullanıcıda (admin) sonuç <see cref="List"/> ile aynıdır.
    /// Web ve masaüstü AYNI metodu kullanır → iki platform ayrışamaz.
    /// </summary>
    public IReadOnlyList<VehicleListRow> ListForReportFilter(SessionContext s, int limit = 5000)
    {
        // ⚠️ Kapı RAPOR yetkisidir, "vehicles" DEĞİL: bu liste yalnız rapor filtresini besler ve raporu
        // açabilen kullanıcının araç modülü yetkisi olmak zorunda değildir (eski satır içi sorgu da
        // böyleydi). Bu metot mevcut ERİŞİM davranışını değiştirmez; yalnız ŞUBE KAPSAMINI ekler.
        AccessControl.Require(s, "reports", PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, internal_code, plate, status, current_meter, meter_unit, production_year, is_rental " +
            "FROM vehicles WHERE company_id=@c AND is_deleted=0" +
            BranchAccess.AllowedSql(s, "branch_id", "@rvb") +
            " ORDER BY internal_code LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@lim", limit);
        BranchAccess.BindAllowed(cmd, s, "@rvb");
        return ReadListRows(cmd);
    }

    private const string GridInnerSql = @"
SELECT v.id AS id, v.internal_code AS internal_code, v.plate AS plate, v.production_year AS production_year,
       v.current_meter AS meter_raw, v.meter_unit AS meter_unit,
       printf('%.2f', CAST(v.current_meter AS REAL)) || ' ' || v.meter_unit AS meter_text,
       v.status AS status,
       CASE v.status WHEN 'active' THEN 'Aktif' WHEN 'passive' THEN 'Pasif' WHEN 'maintenance' THEN 'Bakımda'
            WHEN 'faulty' THEN 'Arızalı' ELSE v.status END AS status_label,
       COALESCE(v.status_note,'') AS status_note,
       COALESCE(vt.name,'') AS vehicle_type, COALESCE(vc.name,'') AS category, COALESCE(b.name,'') AS brand,
       COALESCE(vm.name,'') AS model, COALESCE(br.name,'') AS branch, COALESCE(p.full_name,'') AS driver,
       COALESCE(v.chassis_no,'') AS chassis_no, COALESCE(v.engine_no,'') AS engine_no,
       COALESCE(v.rental_company,'') AS rental_company,
       v.rental_start AS rental_start_raw, {DAY:v.rental_start} AS rental_start,
       v.rental_end AS rental_end_raw, {DAY:v.rental_end} AS rental_end,
       COALESCE(rv.internal_code,'') AS replaced_vehicle
FROM vehicles v
LEFT JOIN vehicles rv ON rv.id = v.replaced_vehicle_id AND rv.company_id = v.company_id
LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id AND vt.company_id = v.company_id
LEFT JOIN vehicle_categories vc ON vc.id = v.category_id AND vc.company_id = v.company_id
LEFT JOIN brands b ON b.id = v.brand_id
LEFT JOIN vehicle_models vm ON vm.id = v.vehicle_model_id
LEFT JOIN branches br ON br.id = v.branch_id AND br.company_id = v.company_id
LEFT JOIN personnel p ON p.id = v.driver_personnel_id AND p.company_id = v.company_id
WHERE v.company_id = @c AND v.is_deleted = 0 AND v.is_rental = @rent";

    /// <summary>Kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17) — bkz.
    /// <see cref="Materials.MaterialService.SearchGrid"/> (aynı desen, <c>GridQuery</c> paylaşılır).
    /// "Durum" filtresi Türkçe ETİKETE göre arar (status_label, ör. "Aktif") — ekran zaten yalnız etiketi
    /// gösterir, kullanıcı ham koda ("active") hiç erişmez.</summary>
    /// <param name="rental">2026-10-10: false = Araç Listesi (yalnız şirket araçları — kiralıklar burada GÖRÜNMEZ,
    /// kullanıcı isteği); true = Kiralık Araçlar ekranı.</param>
    public GridResult<VehicleGridRow> SearchGrid(SessionContext s, VehicleGridFilter filter, int page, int pageSize,
        string? sortColumn = null, bool sortDesc = false, bool rental = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        var byKey = new (string Key, GridQuery.ColumnFilter Col)[]
        {
            (VL.InternalCode, new GridQuery.ColumnFilter("t.internal_code", filter.InternalCode)),
            (VL.Plate, new GridQuery.ColumnFilter("t.plate", filter.Plate)),
            (VL.ProductionYear, new GridQuery.ColumnFilter("t.production_year", filter.ProductionYear, GridQuery.ColumnKind.Numeric)),
            (VL.Meter, new GridQuery.ColumnFilter("t.meter_text", filter.Meter, GridQuery.ColumnKind.Numeric, "t.meter_raw")),
            (VL.Status, new GridQuery.ColumnFilter("t.status_label", filter.Status)),
            (VL.StatusNote, new GridQuery.ColumnFilter("t.status_note", filter.StatusNote)),
            (VL.VehicleType, new GridQuery.ColumnFilter("t.vehicle_type", filter.VehicleType)),
            (VL.Category, new GridQuery.ColumnFilter("t.category", filter.Category)),
            (VL.Brand, new GridQuery.ColumnFilter("t.brand", filter.Brand)),
            (VL.Model, new GridQuery.ColumnFilter("t.model", filter.Model)),
            (VL.Branch, new GridQuery.ColumnFilter("t.branch", filter.Branch)),
            (VL.Driver, new GridQuery.ColumnFilter("t.driver", filter.Driver)),
            (VL.ChassisNo, new GridQuery.ColumnFilter("t.chassis_no", filter.ChassisNo)),
            (VL.EngineNo, new GridQuery.ColumnFilter("t.engine_no", filter.EngineNo)),
            (VL.RentalCompany, new GridQuery.ColumnFilter("t.rental_company", filter.RentalCompany)),
            (VL.RentalStart, new GridQuery.ColumnFilter("t.rental_start", filter.RentalStart, GridQuery.ColumnKind.Text, "t.rental_start_raw")),
            (VL.RentalEnd, new GridQuery.ColumnFilter("t.rental_end", filter.RentalEnd, GridQuery.ColumnKind.Text, "t.rental_end_raw")),
            (VL.ReplacedVehicle, new GridQuery.ColumnFilter("t.replaced_vehicle", filter.ReplacedVehicle)),
        };
        var cols = System.Array.ConvertAll(byKey, x => x.Col);
        GridQuery.ColumnFilter? sort = null;
        if (sortColumn is not null)
            foreach (var x in byKey) if (x.Key == sortColumn) { sort = x.Col; break; }
        // Kira tarihleri ekranda "gg.aa.yyyy" metnidir; metne göre sıralama kronolojik olmaz → HAM tarihe göre sırala
        // (filtre yine metin üzerinden "içerir" çalışır).
        if (sortColumn is VL.RentalStart or VL.RentalEnd && sort is { } ts)
            sort = new GridQuery.ColumnFilter(ts.Alias, null, GridQuery.ColumnKind.Numeric, ts.RawAlias);
        using var conn = _factory.Create();
        var (whereSql, orderSql, ps) = GridQuery.Build(cols, "t.internal_code", sort, sortDesc, SqlDialect.IsSqlite(conn));
        // ŞUBE KAPSAMI: belirli şubeyle girişte yalnız o şubenin (+ şubesiz eski kayıtların) araçları; "Tüm Şubeler" → hepsi.
        var inner = SqlDialect.PortableSql(conn, GridInnerSql)
            .Replace("{DAY:v.rental_start}", SqlDialect.DayText(conn, "v.rental_start"))
            .Replace("{DAY:v.rental_end}", SqlDialect.DayText(conn, "v.rental_end"))
            + BranchScope.Sql(s, "v.branch_id");

        int total;
        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM ({inner}) t {whereSql};";
            cnt.AddWithValue("@c", s.CompanyId);
            cnt.AddWithValue("@rent", rental ? 1 : 0);
            if (BranchScope.Active(s) is { } b0) cnt.AddWithValue("@opb", b0);
            GridQuery.AddParams(cnt, ps);
            total = Convert.ToInt32(cnt.ExecuteScalar());
        }

        var items = new List<VehicleGridRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT * FROM ({inner}) t {whereSql}{orderSql}LIMIT @lim OFFSET @off;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@rent", rental ? 1 : 0);
            if (BranchScope.Active(s) is { } b1) cmd.AddWithValue("@opb", b1);
            GridQuery.AddParams(cmd, ps);
            cmd.AddWithValue("@lim", pageSize);
            cmd.AddWithValue("@off", (page - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                items.Add(new VehicleGridRow(
                    r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? (int?)null : r.GetInt32(3), Money.Parse(r.GetString(4)), r.GetString(5),
                    r.GetString(7), r.GetString(8), r.GetString(9),
                    r.GetString(10), r.GetString(11), r.GetString(12), r.GetString(13), r.GetString(14),
                    r.GetString(15), r.GetString(16), r.GetString(17),
                    r.GetString(18), r.IsDBNull(20) ? null : r.GetString(20), r.IsDBNull(22) ? null : r.GetString(22), r.GetString(23)));
        }
        return new GridResult<VehicleGridRow>(items, total, page, pageSize);
    }

    /// <summary>Filtrelenmiş/sıralanmış TÜM sonuçları (sayfalama sınırı YOK) döner — "Excel'e Aktar" butonu
    /// için (bkz. MaterialService.SearchGridAll — aynı desen). SearchGrid'i 500'lük sayfalarla gezer.</summary>
    public IReadOnlyList<VehicleGridRow> SearchGridAll(SessionContext s, VehicleGridFilter filter, string? sortColumn = null, bool sortDesc = false,
        bool rental = false)
    {
        var all = new System.Collections.Generic.List<VehicleGridRow>();
        int page = 1;
        while (true)
        {
            var res = SearchGrid(s, filter, page, 500, sortColumn, sortDesc, rental);
            all.AddRange(res.Items);
            if (page >= res.TotalPages || res.Items.Count == 0) break;
            page++;
        }
        return all;
    }

    /// <summary>Grid satırlarını Excel tablosuna çevirir — kolon sırası <see cref="VL"/>.All ile AYNIDIR
    /// (bkz. MaterialService.ToTableModel — aynı desen). Sayaç "değer birim" olarak tek metne birleşir.</summary>
    /// <param name="rental">true → Kiralık Araçlar çıktısı: kira kolonları da eklenir, başlık "Kiralık Araçlar".</param>
    public static Application.Reports.TableModel ToTableModel(System.Collections.Generic.IReadOnlyList<VehicleGridRow> rows, bool rental = false)
    {
        var cols = rental ? VL.RentalAll : VL.All;
        var headers = cols.Select(c => c.Label).ToList();
        var body = rows.Select(r =>
        {
            var hucre = new List<object?>
            {
                r.InternalCode, r.Plate, r.ProductionYear, $"{r.Meter} {DepoWise.Application.Ui.MeterUnitOptions.Label(r.MeterUnit)}".Trim(), r.StatusLabel, r.StatusNote,
                r.VehicleType, r.Category, r.Brand, r.Model, r.Branch, r.Driver, r.ChassisNo, r.EngineNo,
            };
            if (rental) hucre.AddRange(new object?[] { r.RentalCompany, r.RentalStart, r.RentalEnd, r.ReplacedVehicle });
            return (System.Collections.Generic.IReadOnlyList<object?>)hucre;
        }).ToList();
        return new Application.Reports.TableModel(rental ? "Kiralık Araçlar" : "Araçlar", headers, body);
    }

    /// <summary>Tek araç detayı (salt okuma) — düzenleme formu için.</summary>
    public VehicleDetail Get(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT v.id, v.internal_code, v.plate, v.production_year, v.current_meter, v.meter_unit, v.status, v.status_note,
       v.chassis_no, v.engine_no,
       v.vehicle_type_id, v.category_id, v.brand_id, v.vehicle_model_id, v.branch_id, v.driver_personnel_id,
       vt.name, vc.name, b.name, vm.name, br.name, p.full_name, v.version, v.template_id,
       v.is_rental, v.rental_company, v.rental_start, v.rental_end, v.rental_price, v.rental_price_unit,
       v.replaced_vehicle_id, rv.internal_code, v.replacement_reason,
       (SELECT nv.internal_code FROM vehicles nv WHERE nv.replaced_vehicle_id = v.id AND nv.company_id = v.company_id AND nv.is_deleted = 0
        ORDER BY nv.created_at DESC LIMIT 1)
FROM vehicles v
LEFT JOIN vehicles rv ON rv.id = v.replaced_vehicle_id AND rv.company_id = v.company_id
LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id AND vt.company_id = v.company_id
LEFT JOIN vehicle_categories vc ON vc.id = v.category_id AND vc.company_id = v.company_id
LEFT JOIN brands b ON b.id = v.brand_id
LEFT JOIN vehicle_models vm ON vm.id = v.vehicle_model_id
LEFT JOIN branches br ON br.id = v.branch_id AND br.company_id = v.company_id
LEFT JOIN personnel p ON p.id = v.driver_personnel_id AND p.company_id = v.company_id
WHERE v.id=@id AND v.company_id=@c AND v.is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        long? L(int i) => r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i));
        return new VehicleDetail(
            r.GetString(0), r.GetString(1), S(2),
            r.IsDBNull(3) ? (int?)null : r.GetInt32(3), Money.Parse(r.GetString(4)), r.GetString(5),
            r.GetString(6), S(7), S(8), S(9),
            S(10), S(11), S(12), S(13), S(14), S(15),
            S(16), S(17), S(18), S(19), S(20), S(21), r.GetInt64(22), S(23),
            IsRental: L(24) == 1, RentalCompany: S(25), RentalStart: L(26), RentalEnd: L(27),
            RentalPrice: S(28) is { } fiyat ? Money.Parse(fiyat) : null, RentalPriceUnit: S(29),
            ReplacedVehicleId: S(30), ReplacedVehicleCode: S(31), ReplacementReason: S(32), ReplacedByCode: S(33));
    }

    /// <summary>Araç alanlarını günceller (plaka/yıl/durum/durum notu). Sayaç burada DEĞİL (SetMeter ile, geriye gitmez).
    /// Durum notu yalnız 'Bakımda' / 'Arızalı' durumunda saklanır (Create ile aynı kural).</summary>
    /// <param name="expectedVersion">DÜZENLEME KİLİDİ: formun açıldığı andaki <c>version</c>. Verilirse ve kayıt
    /// o andan beri değiştiyse <see cref="ConcurrencyException"/> atılır. null = kontrol yok (geriye uyumlu).</param>
    public void Update(SessionContext s, string vehicleId, UpdateVehicle dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Plaka benzersiz (kullanıcı isteği 2026-08-05) — kendi kaydı hariç aynı firmada başka araçta olamaz.
        if (!string.IsNullOrWhiteSpace(dto.Plate) && PlateExists(conn, tx, s.CompanyId, dto.Plate!, excludeId: vehicleId))
            throw new InvalidOperationException($"Bu plaka zaten kayıtlı: {dto.Plate}");

        // İşlem Geçmişi (madde 4/1, 2026-08-06): şube DEĞİŞİYORSA (transfer) audit kaydına isim bilgisiyle
        // yazılır → geçmiş listesinde "X Şubesinden Y Şubesine transfer edildi." Değişmiyorsa normal güncelleme.
        // B-7: düzenlemede de şube/sürücü firmaya ait olmalı (bkz. Create).
        EnsureBranchOwned(conn, tx, s.CompanyId, dto.BranchId);
        EnsurePersonnelOwned(conn, tx, s.CompanyId, dto.DriverPersonnelId);
        // Kira alanları yalnız Rental verildiyse yazılır (null → dokunulmaz); değişim bağı düzenlemede DEĞİŞMEZ.
        var rental = dto.Rental is null ? null : ValidateRental(dto.Rental with { ReplacedVehicleId = null });

        string? oldBranchId;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT branch_id FROM vehicles WHERE id=@id AND company_id=@c;";
            read.AddWithValue("@id", vehicleId);
            read.AddWithValue("@c", s.CompanyId);
            oldBranchId = read.ExecuteScalar() as string;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE vehicles SET plate=@p, production_year=@y, status=@st, status_note=@note,
    chassis_no=@ch, engine_no=@en, vehicle_type_id=@vt, category_id=@cat,
    brand_id=@brand, vehicle_model_id=@vm, branch_id=@br, driver_personnel_id=@drv, template_id=@tpl,"
    + (rental is null ? "" : @"
    rental_company=@rco, rental_start=@rst, rental_end=@ren, rental_price=@rpr, rental_price_unit=@rpu, replacement_reason=@rrs,") + @"
    version=version+1, updated_at=@now
WHERE id=@id AND company_id=@c AND is_deleted=0" + (rental is null ? "" : " AND is_rental=1") + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            if (rental is not null) BindRental(cmd, rental);
            cmd.AddWithValue("@p", (object?)dto.Plate ?? DBNull.Value);
            cmd.AddWithValue("@y", (object?)dto.ProductionYear ?? DBNull.Value);
            cmd.AddWithValue("@st", dto.Status);
            cmd.AddWithValue("@note", DepoWise.Application.Ui.VehicleStatus.NeedsNote(dto.Status) ? (object?)dto.StatusNote ?? DBNull.Value : DBNull.Value);
            cmd.AddWithValue("@ch", (object?)dto.ChassisNo ?? DBNull.Value);
            cmd.AddWithValue("@en", (object?)dto.EngineNo ?? DBNull.Value);
            cmd.AddWithValue("@vt", (object?)dto.VehicleTypeId ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@brand", (object?)dto.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@vm", (object?)dto.VehicleModelId ?? DBNull.Value);
            cmd.AddWithValue("@br", (object?)dto.BranchId ?? DBNull.Value);
            cmd.AddWithValue("@drv", (object?)dto.DriverPersonnelId ?? DBNull.Value);
            cmd.AddWithValue("@tpl", (object?)dto.TemplateId ?? DBNull.Value);   // düzenlemede şablona bağla/koru
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "vehicles", vehicleId, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
            }
        }
        string? afterJson = null;
        if (!string.Equals(oldBranchId, dto.BranchId, StringComparison.Ordinal))
        {
            var fromName = BranchName(conn, tx, s.CompanyId, oldBranchId);
            var toName = BranchName(conn, tx, s.CompanyId, dto.BranchId);
            afterJson = $"{{\"branchFromId\":{JsonStr(oldBranchId)},\"branchFromName\":{JsonStr(fromName)}," +
                        $"\"branchToId\":{JsonStr(dto.BranchId)},\"branchToName\":{JsonStr(toName)}}}";
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId,
            AfterJson: afterJson), _clock);
        tx.Commit();
    }

    /// <summary>
    /// YALNIZ durum + durum notunu günceller (bakım ekranı: "bu araç arızalı" işaretlemek için).
    /// Update() ile karıştırılmamalı: Update TÜM alanları yazar → bakım ekranından çağrılsa marka/model/şube
    /// gibi doldurulmamış alanları NULL'a çekerdi. Bu metot araç kartının geri kalanına DOKUNMAZ.
    /// Not, yalnız "çalışmıyor" durumlarında (Bakımda/Arızalı) saklanır — diğer durumlarda temizlenir.
    /// </summary>
    public void SetStatus(SessionContext s, string vehicleId, string status, string? statusNote = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Durum zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE vehicles SET status=@st, status_note=@note, version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@st", status);
            cmd.AddWithValue("@note",
                DepoWise.Application.Ui.VehicleStatus.NeedsNote(status) ? (object?)statusNote ?? DBNull.Value : DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Araç soft-delete (is_deleted=1). Geçmiş kayıtlar korunur.</summary>
    public void Delete(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vehicles SET is_deleted=1, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>İşlem Geçmişi (madde 4, 2026-08-06): audit_logs (oluşturma/şube transferi/genel güncelleme/silme)
    /// + vehicle_meter_logs (sayaç değişimi; kaynağa göre yakıt/bakım/manuel etiketlenir) birleştirilip tarihe
    /// göre azalan sıralanır. Salt-okunur; malzeme kartındaki "Son Hareketler"in araç karşılığı.</summary>
    public IReadOnlyList<VehicleHistoryRow> RecentHistory(SessionContext s, string vehicleId, int take = 100)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (take < 1) take = 1; if (take > 300) take = 300;
        using var conn = _factory.Create();
        EnsureVehicleOwned(conn, s.CompanyId, vehicleId);

        var rows = new List<VehicleHistoryRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT a.created_at, a.action, a.after_json
FROM audit_logs a
WHERE a.company_id=@c AND a.entity_type='vehicle' AND a.entity_id=@v
ORDER BY a.created_at DESC LIMIT @lim;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@v", vehicleId);
            cmd.AddWithValue("@lim", take);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var action = r.GetString(1);
                var afterJson = r.IsDBNull(2) ? null : r.GetString(2);
                rows.Add(new VehicleHistoryRow(r.GetInt64(0), AuditLabel(action, afterJson), null));
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT created_at, old_value, new_value, source
FROM vehicle_meter_logs
WHERE company_id=@c AND vehicle_id=@v AND source <> 'vehicle_create'
ORDER BY created_at DESC LIMIT @lim;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@v", vehicleId);
            cmd.AddWithValue("@lim", take);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var oldV = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
                var newV = Money.Parse(r.IsDBNull(2) ? null : r.GetString(2));
                var source = r.GetString(3);
                rows.Add(new VehicleHistoryRow(r.GetInt64(0), MeterLabel(source), $"{oldV:0.##} → {newV:0.##}"));
            }
        }
        return rows.OrderByDescending(x => x.Date).Take(take).ToList();
    }

    private static string AuditLabel(string action, string? afterJson)
    {
        if (action == AuditActions.Create) return "Araç oluşturuldu.";
        if (action == AuditActions.Delete) return "Araç silindi (çöp kutusuna alındı).";
        if (action == AuditActions.Update)
        {
            if (!string.IsNullOrEmpty(afterJson))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(afterJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("branchToId", out _))
                    {
                        var from = root.TryGetProperty("branchFromName", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String ? f.GetString() : null;
                        var to = root.TryGetProperty("branchToName", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString() : null;
                        return (string.IsNullOrEmpty(from) ? "Şube atanmamış durumdan" : $"{from} Şubesinden") +
                               " " + (string.IsNullOrEmpty(to) ? "şube ataması kaldırıldı." : $"{to} Şubesine transfer edildi.");
                    }
                }
                catch { /* json bozuksa genel metne düş */ }
            }
            return "Araç bilgileri güncellendi.";
        }
        return "Araç işlemi.";
    }

    private static string MeterLabel(string source) => source switch
    {
        "fuel_distribution" => "Sayaç güncellendi (Yakıt dağıtımı)",
        "maintenance" => "Sayaç güncellendi (Bakım)",
        _ => "Sayaç güncellendi (Manuel)"
    };

    private static void EnsureVehicleOwned(DbConnection conn, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
    }

    private static string? BranchName(DbConnection conn, DbTransaction tx, string companyId, string? branchId)
    {
        if (string.IsNullOrEmpty(branchId)) return null;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT name FROM branches WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", branchId);
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() as string;
    }

    private static string JsonStr(string? v)
        => v is null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // ---- yardımcılar ----
    private NewVehicle ApplyTemplate(DbConnection conn, DbTransaction tx, string companyId, NewVehicle dto)
    {
        if (dto.TemplateId is null) return dto;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT vehicle_type_id, category_id, brand_id, vehicle_model_id, production_year, default_meter_unit
FROM vehicle_templates WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", dto.TemplateId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
        // Kullanıcı değeri öncelikli (?? ile yalnız boş alanlar doldurulur)
        return dto with
        {
            VehicleTypeId = dto.VehicleTypeId ?? (r.IsDBNull(0) ? null : r.GetString(0)),
            CategoryId = dto.CategoryId ?? (r.IsDBNull(1) ? null : r.GetString(1)),
            BrandId = dto.BrandId ?? (r.IsDBNull(2) ? null : r.GetString(2)),
            VehicleModelId = dto.VehicleModelId ?? (r.IsDBNull(3) ? null : r.GetString(3)),
            ProductionYear = dto.ProductionYear ?? (r.IsDBNull(4) ? (int?)null : r.GetInt32(4)),
            MeterUnit = dto.MeterUnit == "km" && !r.IsDBNull(5) ? r.GetString(5) : dto.MeterUnit,
        };
    }

    private static void CopyTemplateMaterials(DbConnection conn, DbTransaction tx, string templateId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO material_compatible_vehicles(material_id, vehicle_id) " +
            "SELECT material_id, @v FROM vehicle_template_materials WHERE template_id=@t ON CONFLICT DO NOTHING;";
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@t", templateId);
        cmd.ExecuteNonQuery();
    }

    private static decimal ReadMeter(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
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

    private static void UpdateMeter(DbConnection conn, DbTransaction tx, string vehicleId, decimal value, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE vehicles SET current_meter=@m, version=version+1, updated_at=@now WHERE id=@id;";
        cmd.AddWithValue("@m", Money.Serialize(value));
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@id", vehicleId);
        cmd.ExecuteNonQuery();
    }

    private static void WriteMeterLog(DbConnection conn, DbTransaction tx, string companyId, string vehicleId,
        decimal oldVal, decimal newVal, string source, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES(@id,@c,@v,@o,@n,@src,@now);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@o", Money.Serialize(oldVal));
        cmd.AddWithValue("@n", Money.Serialize(newVal));
        cmd.AddWithValue("@src", source);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static bool CodeExists(DbConnection conn, DbTransaction tx, string companyId, string code)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE company_id=@c AND internal_code=@ic;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@ic", code.Trim());
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // Plaka benzersizlik kontrolü (silinmiş araçlar hariç; excludeId=düzenlenen kayıt). Yalnız interaktif
    // create/update yolunda çağrılır — sync upsert bu kontrole girmez (offline çakışma sistemi çökertmez).
    private static bool PlateExists(DbConnection conn, DbTransaction tx, string companyId, string plate, string? excludeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE company_id=@c AND is_deleted=0 AND plate=@p" +
                          (excludeId is null ? ";" : " AND id<>@ex;");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@p", plate.Trim());
        if (excludeId is not null) cmd.AddWithValue("@ex", excludeId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>B-7: şube bu firmaya ait mi? null = şubesiz kayıt (serbest).</summary>
    private static void EnsureBranchOwned(DbConnection conn, DbTransaction? tx, string companyId, string? branchId)
        => EnsureOwned(conn, tx, "branches", companyId, branchId, "Şube bulunamadı veya başka firmaya ait.");

    /// <summary>B-7: personel bu firmaya ait mi? null = sürücü atanmamış (serbest).</summary>
    private static void EnsurePersonnelOwned(DbConnection conn, DbTransaction? tx, string companyId, string? personnelId)
        => EnsureOwned(conn, tx, "personnel", companyId, personnelId, "Personel bulunamadı veya başka firmaya ait.");

    /// <summary>B-7: istemciden gelen bir id'nin firmaya ait olduğunu doğrular (B-2/B-3 deseni).
    /// Tablo adı YALNIZ bu sınıftaki sabitlerden gelir — dışarıdan parametre alınmaz.</summary>
    private static void EnsureOwned(DbConnection conn, DbTransaction? tx, string table,
        string companyId, string? id, string error)
    {
        if (string.IsNullOrEmpty(id)) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException(error);
    }
}
