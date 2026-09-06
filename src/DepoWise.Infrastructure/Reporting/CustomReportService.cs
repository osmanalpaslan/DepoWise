using System.Data.Common;
using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// ═══ ARA İŞ 4 (ADR-186) — CUSTOM RAPOR: TANIM SAKLAMA + GÜVENLİ ÇALIŞTIRMA ═══
///
/// <b>İkinci bir rapor motoru DEĞİLDİR (PK-CR-03=A).</b> Bu servis yalnız (a) tanımları saklar/okur,
/// (b) tanımı mevcut, testli <c>SearchGrid</c> yollarına çevirir ve (c) sonucu mevcut
/// <see cref="TableModel"/> yapısına projeksiyonlar. Rapor kapıları ve dağıtım
/// <see cref="ReportService.Run"/> içinde kalır.
///
/// <b>SQL GÜVENLİĞİ (PK-CR-01/05=A):</b> kullanıcıdan gelen metin SQL'e ASLA birleştirilmez.
///  • Kaynak ve kolon: yalnız <see cref="CustomReportSources"/> beyaz listesinden ANAHTAR eşleşmesi;
///    eşleşmeyen anahtar sorguya hiç ulaşmadan reddedilir.
///  • Kolon SQL ifadeleri (alias) tamamen mevcut servislerin İÇİNDE kodludur — bu servis alias
///    üretmez, taşımaz veya dışarıdan almaz.
///  • Filtre DEĞERLERİ mevcut <c>GridQuery</c> yolundan PARAMETRE olarak geçer.
///  • JOIN / ham SQL / ORDER BY parçası KABUL EDİLMEZ (sıralama da anahtar eşleşmesidir).
///
/// <b>SATIR TAVANI (PK-CR-06/10=A):</b> her sorgu <c>SearchGrid</c>'in SQL'indeki
/// <c>LIMIT/OFFSET</c> ile sınırlıdır; toplam <see cref="CustomReportRules.MaxRows"/> ile kesilir.
/// Bellekte "önce hepsini çek sonra kes" YAPILMAZ.
///
/// <b>TARİH (PK-CR-10=A):</b> olay verisinde (Günlük Faaliyet) iş günü aralığı ZORUNLU ve SQL'e iner;
/// ana veride (Malzeme/Araç) tarih YOKTUR — <c>created_at</c> iş günü yerine KULLANILMAZ.
/// </summary>
public sealed class CustomReportService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly MaterialService _materials;
    private readonly VehicleService _vehicles;
    private readonly DailyActivityService _daily;

    public CustomReportService(IDbConnectionFactory factory, MaterialService materials, VehicleService vehicles,
        DailyActivityService daily, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _materials = materials;
        _vehicles = vehicles;
        _daily = daily;
    }

    private static readonly JsonSerializerOptions Json = new();

    // ══════════════════════════════════════ TANIM SAKLAMA ══════════════════════════════════════

    /// <summary>Firmanın aktif custom rapor tanımları (silinmişler hariç). Tenant süzgeci ZORUNLU.</summary>
    public IReadOnlyList<CustomReportDefinition> List(SessionContext s, bool includeInactive = false)
    {
        AccessControl.Require(s, "reports", PermissionAction.View);
        var list = new List<CustomReportDefinition>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, name, source_key, columns_json, filters_json, sort_column, sort_desc, " +
            "       is_active, created_at, updated_at " +
            "FROM custom_report_defs WHERE company_id=@c AND is_deleted=0 " +
            (includeInactive ? "" : "AND is_active=1 ") +
            "ORDER BY name;";
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    /// <summary>Tek tanım — BAŞKA firmanın tanımı ASLA dönmez (tenant izolasyonu).</summary>
    public CustomReportDefinition? ById(SessionContext s, string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, name, source_key, columns_json, filters_json, sort_column, sort_desc, " +
            "       is_active, created_at, updated_at " +
            "FROM custom_report_defs WHERE id=@i AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    private static CustomReportDefinition Read(DbDataReader r)
    {
        var columns = Deserialize<List<string>>(r.IsDBNull(4) ? null : r.GetString(4)) ?? new List<string>();
        var filters = Deserialize<List<CustomReportFilter>>(r.IsDBNull(5) ? null : r.GetString(5))
                      ?? new List<CustomReportFilter>();
        return new CustomReportDefinition(
            r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            columns, filters,
            r.IsDBNull(6) ? null : r.GetString(6),
            Convert.ToInt64(r.GetValue(7)) == 1,
            Convert.ToInt64(r.GetValue(8)) == 1,
            Convert.ToInt64(r.GetValue(9)), Convert.ToInt64(r.GetValue(10)));
    }

    /// <summary>Bozuk/elle düzenlenmiş JSON çalışma anında İSTİSNA ATMAZ — boş döner ve tanım
    /// doğrulamada reddedilir (istisna üzerinden güvenlik kapısı atlatılamaz).</summary>
    private static T? Deserialize<T>(string? raw) where T : class
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return JsonSerializer.Deserialize<T>(raw, Json); }
        catch (JsonException) { return null; }
    }

    /// <summary>Yeni tanım kaydeder (doğrulamadan geçmeyen tanım SAKLANMAZ).</summary>
    public string Create(SessionContext s, string name, string sourceKey, IReadOnlyList<string> columns,
        IReadOnlyList<CustomReportFilter>? filters, string? sortColumn, bool sortDesc)
    {
        AccessControl.Require(s, "reports", PermissionAction.Create);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        var def = new CustomReportDefinition(id, s.CompanyId, name?.Trim() ?? "", sourceKey,
            columns ?? Array.Empty<string>(), filters ?? Array.Empty<CustomReportFilter>(),
            sortColumn, sortDesc, IsActive: true, now, now);

        var dogrulama = CustomReportRules.Validate(def);
        if (!dogrulama.Ok) throw new ArgumentException(dogrulama.Error);

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO custom_report_defs(id, company_id, name, source_key, columns_json, filters_json,
    sort_column, sort_desc, is_active, created_by, created_at, updated_at, version, is_deleted)
VALUES(@i,@c,@n,@s,@cols,@f,@sc,@sd,1,@by,@now,@now,1,0);";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", def.Name);
            cmd.AddWithValue("@s", def.SourceKey);
            cmd.AddWithValue("@cols", JsonSerializer.Serialize(def.Columns, Json));
            cmd.AddWithValue("@f", JsonSerializer.Serialize(def.Filters, Json));
            cmd.AddWithValue("@sc", (object?)def.SortColumn ?? DBNull.Value);
            cmd.AddWithValue("@sd", def.SortDesc ? 1L : 0L);
            cmd.AddWithValue("@by", s.UserId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "custom_report_def", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Tanımı günceller (tenant süzgeçli; doğrulamadan geçmeyen kayıt yazılmaz).</summary>
    public void Update(SessionContext s, string id, string name, string sourceKey, IReadOnlyList<string> columns,
        IReadOnlyList<CustomReportFilter>? filters, string? sortColumn, bool sortDesc, bool isActive)
    {
        AccessControl.Require(s, "reports", PermissionAction.Edit);
        var mevcut = ById(s, id) ?? throw new ForbiddenException("Rapor tanımı bulunamadı veya başka firmaya ait.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var def = mevcut with
        {
            Name = name?.Trim() ?? "",
            SourceKey = sourceKey,
            Columns = columns ?? Array.Empty<string>(),
            Filters = filters ?? Array.Empty<CustomReportFilter>(),
            SortColumn = sortColumn,
            SortDesc = sortDesc,
            IsActive = isActive,
            UpdatedAt = now,
        };
        var dogrulama = CustomReportRules.Validate(def);
        if (!dogrulama.Ok) throw new ArgumentException(dogrulama.Error);

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE custom_report_defs SET name=@n, source_key=@s, columns_json=@cols, filters_json=@f,
    sort_column=@sc, sort_desc=@sd, is_active=@ia, updated_at=@now, version=version+1
WHERE id=@i AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", def.Name);
            cmd.AddWithValue("@s", def.SourceKey);
            cmd.AddWithValue("@cols", JsonSerializer.Serialize(def.Columns, Json));
            cmd.AddWithValue("@f", JsonSerializer.Serialize(def.Filters, Json));
            cmd.AddWithValue("@sc", (object?)def.SortColumn ?? DBNull.Value);
            cmd.AddWithValue("@sd", def.SortDesc ? 1L : 0L);
            cmd.AddWithValue("@ia", def.IsActive ? 1L : 0L);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "custom_report_def", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>YUMUŞAK silme (fiziksel silme YOK — proje standardı).</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, "reports", PermissionAction.Delete);
        _ = ById(s, id) ?? throw new ForbiddenException("Rapor tanımı bulunamadı veya başka firmaya ait.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE custom_report_defs SET is_deleted=1, updated_at=@now, version=version+1 " +
                              "WHERE id=@i AND company_id=@c;";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "custom_report_def", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>
    /// ⭐ KATALOG GÖRÜNÜRLÜĞÜ — kullanıcının rapor listesinde göreceği custom raporlar.
    ///
    /// Süzme kuralları mevcut katalog süzmeleriyle BİREBİR AYNIDIR (RPR-12/RPR-15/RPT-YETKI deseni):
    /// kullanıcının ÇALIŞTIRAMAYACAĞI rapor listede de görünmez. ⚠️ Bu yalnız GÖRÜNÜRLÜKTÜR —
    /// asıl kapılar <see cref="ReportService.Run"/> içinde durur ve buradan bağımsız çalışır
    /// (liste süzmesi tek başına güvenlik sayılmaz).
    /// </summary>
    public IReadOnlyList<ReportDescriptor> Catalog(SessionContext s)
    {
        // "reports" üst kapısı yoksa hiçbir custom rapor listelenmez.
        if (!AccessControl.Can(s, "reports", PermissionAction.View)) return Array.Empty<ReportDescriptor>();

        var sonuc = new List<ReportDescriptor>();
        foreach (var def in List(s))
        {
            var src = CustomReportSources.ByKey(def.SourceKey);
            if (src is null) continue;                               // bozuk tanım listede görünmez

            // RPR-15: rolüne KAPATILMIŞ ekranın raporu listede de görünmez.
            if (!s.IsSuperAdmin && !DeveloperMode.IsActive && s.BlockedModules.Contains(src.DataModule)) continue;
            // ADR-181: kategori yetkisi olmayan o türü görmez.
            if (!AccessControl.Can(s, ReportCatalog.CategoryModule(src.Category), PermissionAction.View)) continue;
            // PK-CR-04=A: rapora özel dinamik anahtarı olmayan görmez (deny-by-default).
            if (!AccessControl.Can(s, def.PermissionKey, PermissionAction.View)) continue;

            sonuc.Add(new ReportDescriptor(
                def.ReportKey, def.Name, $"Kullanıcı tanımlı rapor — {src.Label}", src.Category,
                src.IsManager ? ReportGroup.Manager : ReportGroup.Standard,
                src.RequiresDate ? ReportFilters.Date : ReportFilters.None,
                RequiresDate: src.RequiresDate,
                ExportButton: "btn-export-report",
                InfoNote: src.RequiresFilter
                    ? "Bu rapor tarih aralığı kullanmaz; çalıştırmak için en az bir filtre gerekir."
                    : null,
                DataModule: src.DataModule));
        }
        return sonuc;
    }

    /// <summary>⭐ TASARIMCI KATALOĞU — UI'nin ihtiyaç duyduğu GÜVENLİ metadata.
    /// Yalnız kaynak/kolon ANAHTARLARI ve görünen adlar döner; SQL ifadesi, tablo adı veya alias
    /// kullanıcıya ASLA açılmaz. Çalıştırma beyaz listesiyle AYNI kaynaktan (<see cref="CustomReportSources"/>)
    /// türetilir — ikinci bir liste YOKTUR.</summary>
    public static IReadOnlyList<CustomReportSource> DesignerCatalog() => CustomReportSources.All;

    // ══════════════════════════════════════ ÇALIŞTIRMA ══════════════════════════════════════

    /// <summary>
    /// Tanımı çalıştırır ve mevcut <see cref="TableModel"/> döndürür.
    ///
    /// ⚠️ Bu metot <b>yetki kapılarını kendi başına uygulamaz</b>: çağrı <see cref="ReportService.Run"/>
    /// üzerinden gelir ve dört kapı (yönetici · DataModule · kategori · katalog çözümleme) ORADA
    /// çalışır. Buna EK olarak alttaki <c>SearchGrid</c> servisleri kendi modül iznini (materials /
    /// vehicles / daily_activity) ve tenant + BranchAccess süzgecini AYNEN uygular.
    /// </summary>
    public TableModel Run(SessionContext s, CustomReportDefinition def, long? fromDate, long? toDate, int maxRows)
    {
        var dogrulama = CustomReportRules.ValidateRun(def, fromDate, toDate);
        if (!dogrulama.Ok) throw new ArgumentException(dogrulama.Error);

        var src = CustomReportSources.ByKey(def.SourceKey)!;
        var tavan = maxRows <= 0 ? CustomReportRules.MaxRows : Math.Min(maxRows, CustomReportRules.MaxRows);

        // ⭐ FAZ 3b (ADR-223): korumalı alan bu kullanıcıya kapalıysa kolon RAPORDAN ÇIKARILIR —
        // başlık, sayısallık bayrağı ve hücre birlikte. Değeri 0'lamak "gizleme" değildir; ayrıca
        // filtre de düşer (aşağıda MalzemeSatirlari), yoksa fiyat filtresiyle değer daraltılabilirdi.
        if (src.Key == CustomReportSources.Materials
            && !Materials.MaterialService.FiyatGorunur(s)
            && def.Columns.Contains(MaterialListColumns.UnitPrice))
        {
            def = def with { Columns = def.Columns.Where(k => k != MaterialListColumns.UnitPrice).ToList() };
        }

        var basliklar = def.Columns.Select(k => src.LabelOf(k) ?? k).ToList();
        var sayisal = def.Columns.Select(src.IsNumeric).ToList();
        var satirlar = src.Key switch
        {
            CustomReportSources.Materials => MalzemeSatirlari(s, def, tavan),
            CustomReportSources.Vehicles => AracSatirlari(s, def, tavan),
            CustomReportSources.DailyActivity => FaaliyetSatirlari(s, def, fromDate, toDate, tavan),
            _ => throw new ArgumentException($"Bilinmeyen rapor kaynağı: «{def.SourceKey}»."),
        };

        return new TableModel(def.Name, basliklar, satirlar, sayisal);
    }

    /// <summary>Filtre değerini kolon anahtarına göre bulur (beyaz liste dışı anahtar zaten
    /// doğrulamada elenmiştir; burada yalnız EŞLEŞME yapılır — SQL üretimi YOK).</summary>
    private static string? F(CustomReportDefinition def, string columnKey)
        => def.Filters?.FirstOrDefault(f => f.ColumnKey == columnKey)?.Value;

    private List<IReadOnlyList<object?>> MalzemeSatirlari(SessionContext s, CustomReportDefinition def, int tavan)
    {
        var filtre = new MaterialGridFilter(
            Code: F(def, MaterialListColumns.Code), Name: F(def, MaterialListColumns.Name),
            Type: F(def, MaterialListColumns.Type), Category: F(def, MaterialListColumns.Category),
            Unit: F(def, MaterialListColumns.Unit), Brand: F(def, MaterialListColumns.Brand),
            Supplier: F(def, MaterialListColumns.Supplier),
            // FAZ 3b: fiyat gizliyken fiyat FİLTRESİ de uygulanmaz (SearchGrid ayrıca düşürür;
            // burada da düşürülmesi niyeti açık bırakır).
            UnitPrice: Materials.MaterialService.FiyatGorunur(s) ? F(def, MaterialListColumns.UnitPrice) : null,
            Currency: F(def, MaterialListColumns.Currency), MinStock: F(def, MaterialListColumns.MinStock),
            Stock: F(def, MaterialListColumns.Stock), Status: F(def, MaterialListColumns.Status),
            Description: F(def, MaterialListColumns.Description),
            CompatibleVehicles: F(def, MaterialListColumns.CompatibleVehicles),
            Equivalents: F(def, MaterialListColumns.Equivalents));

        return Sayfala(tavan, (sayfa, boy) =>
            _materials.SearchGrid(s, filtre, sayfa, boy, def.SortColumn, def.SortDesc).Items,
            r => def.Columns.Select(k => MalzemeDeger(r, k)).ToList());
    }

    private static object? MalzemeDeger(MaterialGridRow r, string key) => key switch
    {
        MaterialListColumns.Code => r.Code,
        MaterialListColumns.Name => r.Name,
        MaterialListColumns.Type => r.Type,
        MaterialListColumns.Category => r.Category,
        MaterialListColumns.Unit => r.Unit,
        MaterialListColumns.Brand => r.Brand,
        MaterialListColumns.Supplier => r.Supplier,
        MaterialListColumns.UnitPrice => r.UnitPrice,
        MaterialListColumns.Currency => r.Currency,
        MaterialListColumns.MinStock => r.MinStock,
        MaterialListColumns.Stock => r.Stock,
        MaterialListColumns.Status => r.Status,
        MaterialListColumns.Description => r.Description,
        MaterialListColumns.CompatibleVehicles => r.CompatibleVehicles,
        MaterialListColumns.Equivalents => r.Equivalents,
        _ => null,   // beyaz liste dışı anahtar buraya ULAŞAMAZ (doğrulamada elenir)
    };

    private List<IReadOnlyList<object?>> AracSatirlari(SessionContext s, CustomReportDefinition def, int tavan)
    {
        var filtre = new VehicleGridFilter(
            InternalCode: F(def, VehicleListColumns.InternalCode), Plate: F(def, VehicleListColumns.Plate),
            ProductionYear: F(def, VehicleListColumns.ProductionYear), Meter: F(def, VehicleListColumns.Meter),
            Status: F(def, VehicleListColumns.Status), StatusNote: F(def, VehicleListColumns.StatusNote),
            VehicleType: F(def, VehicleListColumns.VehicleType), Category: F(def, VehicleListColumns.Category),
            Brand: F(def, VehicleListColumns.Brand), Model: F(def, VehicleListColumns.Model),
            Branch: F(def, VehicleListColumns.Branch), Driver: F(def, VehicleListColumns.Driver),
            ChassisNo: F(def, VehicleListColumns.ChassisNo), EngineNo: F(def, VehicleListColumns.EngineNo));

        return Sayfala(tavan, (sayfa, boy) =>
            _vehicles.SearchGrid(s, filtre, sayfa, boy, def.SortColumn, def.SortDesc).Items,
            r => def.Columns.Select(k => AracDeger(r, k)).ToList());
    }

    private static object? AracDeger(VehicleGridRow r, string key) => key switch
    {
        VehicleListColumns.InternalCode => r.InternalCode,
        VehicleListColumns.Plate => r.Plate,
        VehicleListColumns.ProductionYear => r.ProductionYear,
        VehicleListColumns.Meter => r.Meter,
        VehicleListColumns.Status => r.StatusLabel,
        VehicleListColumns.StatusNote => r.StatusNote,
        VehicleListColumns.VehicleType => r.VehicleType,
        VehicleListColumns.Category => r.Category,
        VehicleListColumns.Brand => r.Brand,
        VehicleListColumns.Model => r.Model,
        VehicleListColumns.Branch => r.Branch,
        VehicleListColumns.Driver => r.Driver,
        VehicleListColumns.ChassisNo => r.ChassisNo,
        VehicleListColumns.EngineNo => r.EngineNo,
        _ => null,
    };

    private List<IReadOnlyList<object?>> FaaliyetSatirlari(SessionContext s, CustomReportDefinition def,
        long? fromDate, long? toDate, int tavan)
    {
        var filtre = new DailyActivityGridFilter(
            Type: F(def, DailyActivityListColumns.Type), Vehicle: F(def, DailyActivityListColumns.Vehicle),
            Route: F(def, DailyActivityListColumns.Route), Operator: F(def, DailyActivityListColumns.Operator),
            Duration: F(def, DailyActivityListColumns.Duration),
            Description: F(def, DailyActivityListColumns.Description));

        // ⭐ PK-CR-10=A: iş günü aralığı SQL'e iner (bellekte süzme YOK).
        return Sayfala(tavan, (sayfa, boy) =>
            _daily.SearchGrid(s, filtre, sayfa, boy, def.SortColumn, def.SortDesc,
                includeCancelled: false, fromDateMs: fromDate, toDateMs: toDate).Items,
            r => def.Columns.Select(k => FaaliyetDeger(r, k)).ToList());
    }

    private static object? FaaliyetDeger(DailyActivityGridRow r, string key) => key switch
    {
        DailyActivityListColumns.Date => DateTimeOffset.FromUnixTimeMilliseconds(r.DateRaw).UtcDateTime.ToString("dd.MM.yyyy"),
        DailyActivityListColumns.Type => r.Type,
        DailyActivityListColumns.Vehicle => r.Vehicle,
        DailyActivityListColumns.Route => r.Route,
        DailyActivityListColumns.Operator => r.Operator,
        DailyActivityListColumns.Duration => r.Duration,
        DailyActivityListColumns.Description => r.Description,
        _ => null,
    };

    /// <summary>Sayfa sayfa çeker; her sorgu SQL'de <c>LIMIT</c>'lidir ve toplam <paramref name="tavan"/>
    /// aşılmaz. "Önce hepsini belleğe al, sonra kes" YAPILMAZ (PK-CR-06=A).</summary>
    private static List<IReadOnlyList<object?>> Sayfala<TRow>(int tavan,
        Func<int, int, IReadOnlyList<TRow>> getir, Func<TRow, IReadOnlyList<object?>> projeksiyon)
    {
        var sonuc = new List<IReadOnlyList<object?>>();
        for (int sayfa = 1; sonuc.Count < tavan; sayfa++)
        {
            var boy = Math.Min(CustomReportRules.PageSize, tavan - sonuc.Count);
            var parca = getir(sayfa, boy);
            if (parca.Count == 0) break;
            foreach (var r in parca)
            {
                if (sonuc.Count >= tavan) break;
                sonuc.Add(projeksiyon(r));
            }
            if (parca.Count < boy) break;   // son sayfa
        }
        return sonuc;
    }
}
