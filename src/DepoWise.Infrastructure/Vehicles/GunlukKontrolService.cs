using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Vehicles;

/// <summary>⭐ M-43: bir kontrol maddesinin sonucu.</summary>
public sealed record KontrolSonucu(string Madde, bool Uygun, string? Not = null);

/// <summary>⭐ M-43: kaydedilmiş günlük kontrol.</summary>
public sealed record GunlukKontrol(string Id, string VehicleId, long Tarih, IReadOnlyList<KontrolSonucu> Sonuclar, int UygunDegil,
    string? Not, string? IsEmriId, string? Kullanici)
{
    public string Satir => $"{DateTimeOffset.FromUnixTimeMilliseconds(Tarih).UtcDateTime:dd.MM.yyyy} · " +
                           (UygunDegil == 0 ? "tüm maddeler uygun" : $"{UygunDegil} madde UYGUN DEĞİL: " +
                               string.Join(", ", Sonuclar.Where(x => !x.Uygun).Select(x => x.Madde))) +
                           (IsEmriId is null ? "" : " · arıza iş emri açıldı") + (Kullanici is null ? "" : $" · {Kullanici}");
}

/// <summary>⭐ M-43: kayıt sonucu (arıza iş emri açıldıysa numarası; açılamadıysa nedeni).</summary>
public sealed record GunlukKontrolSonucu(string Id, string? IsEmriNo, string? Uyari);

/// <summary>
/// ═══ M-43 (AR-GE 2026-10-19, 05 F-087) — GÜNLÜK MAKİNE KONTROL LİSTESİ ═══
/// Operatör vardiya başında araç/makineyi firmanın madde listesine göre kontrol eder (uygun / uygun değil + not); kayıt
/// DEĞİŞMEZ (İSG kanıtı). Uygun olmayan madde varsa ve istenirse aynı anda "Arıza Bildir" (M-35) çalışır: araç "Arızalı",
/// taslak arıza iş emri. Araçta zaten açık arıza varsa yeni iş emri açılmaz, kontrol yine kaydedilir ve uyarı döner.
/// Madde listesi firma yöneticisince düzenlenir; tanımlanmamışsa <see cref="VarsayilanMaddeler"/>.
/// Yetki: kaydetmek araç görme + düzenleme (operatör), arıza açmak ayrıca iş emri oluşturma yetkisi ister.
/// </summary>
public sealed class GunlukKontrolService
{
    public static readonly IReadOnlyList<string> VarsayilanMaddeler = new[]
    {
        "Motor yağı seviyesi", "Hidrolik yağ / sızıntı", "Soğutma suyu", "Lastik / palet durumu", "Fren", "Lamba ve ikaz sesi",
        "Emniyet kemeri", "Yangın tüpü", "Ayna ve camlar", "Kova / ataşman pimleri",
    };
    public const int MaxMadde = 40;
    private const string Modul = "vehicles";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public GunlukKontrolService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static bool Destekli(System.Data.Common.DbConnection conn) => DbIntrospect.TableExists(conn, null, "daily_checks");

    /// <summary>Firmanın kontrol maddeleri (tanımlı değilse varsayılan liste).</summary>
    public IReadOnlyList<string> Maddeler(SessionContext s)
    {
        AccessControl.Require(s, Modul, PermissionAction.View);
        using var conn = _factory.Create();
        if (!Destekli(conn)) return VarsayilanMaddeler;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT items_json FROM daily_check_templates WHERE company_id=@c;";
        cmd.AddWithValue("@c", s.CompanyId);
        return cmd.ExecuteScalar() is string j && JsonSerializer.Deserialize<List<string>>(j) is { Count: > 0 } l ? l : VarsayilanMaddeler;
    }

    /// <summary>Madde listesini kaydeder (yalnız yönetici). Boş liste → varsayılana döner.</summary>
    public void MaddeleriKaydet(SessionContext s, IReadOnlyList<string> maddeler)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Kontrol listesini yalnız yönetici düzenleyebilir.");
        var temiz = (maddeler ?? Array.Empty<string>()).Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (temiz.Count > MaxMadde) throw new ArgumentException($"En fazla {MaxMadde} madde olabilir.");
        if (temiz.Any(x => x.Length > 120)) throw new ArgumentException("Bir madde en fazla 120 karakter olabilir.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı günlük kontrol listesini henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM daily_check_templates WHERE company_id=@c;";
            del.AddWithValue("@c", s.CompanyId);
            del.ExecuteNonQuery();
        }
        if (temiz.Count > 0)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO daily_check_templates(company_id, items_json, updated_by, updated_at) VALUES(@c, @j, @u, @t);";
            ins.AddWithValue("@c", s.CompanyId); ins.AddWithValue("@j", JsonSerializer.Serialize(temiz));
            ins.AddWithValue("@u", s.UserId); ins.AddWithValue("@t", now);
            ins.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "daily_check_template", s.CompanyId, AuditActions.Update, s.UserId,
            AfterJson: JsonSerializer.Serialize(new { madde = temiz.Count })), _clock);
        tx.Commit();
    }

    /// <summary>Günlük kontrolü kaydeder; <paramref name="arizaAc"/> ve uygun olmayan madde varsa arıza iş emri açar.</summary>
    public GunlukKontrolSonucu Kaydet(SessionContext s, string vehicleId, long? tarih, IReadOnlyList<KontrolSonucu> sonuclar,
        string? not, bool arizaAc, string operationId)
    {
        AccessControl.Require(s, Modul, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        if (sonuclar is null || sonuclar.Count == 0) throw new ArgumentException("En az bir kontrol maddesi işaretleyin.");
        if (sonuclar.Count > MaxMadde) throw new ArgumentException($"En fazla {MaxMadde} madde olabilir.");
        if (sonuclar.Any(x => string.IsNullOrWhiteSpace(x.Madde))) throw new ArgumentException("Madde adı boş olamaz.");
        var temiz = sonuclar.Select(x => new KontrolSonucu(x.Madde.Trim(), x.Uygun, string.IsNullOrWhiteSpace(x.Not) ? null : x.Not.Trim())).ToList();
        var basarisiz = temiz.Where(x => !x.Uygun).ToList();
        if (arizaAc && basarisiz.Count > 0)
            AccessControl.Require(s, DepoWise.Infrastructure.WorkOrders.WorkOrderService.Module, PermissionAction.Create);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var gun = DateEntryPolicy.Uygula(s, tarih) ?? now;
        string id;
        using (var conn = _factory.Create())
        {
            if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı günlük kontrol listesini henüz desteklemiyor (güncelleme bekleniyor).");
            using var tx = conn.BeginImmediate();
            using (var chk = conn.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = "SELECT id, work_order_id FROM daily_checks WHERE company_id=@c AND operation_id=@op;";
                chk.AddWithValue("@c", s.CompanyId); chk.AddWithValue("@op", operationId);
                using var r = chk.ExecuteReader();
                if (r.Read())   // tekrar gönderim: aynı sonuç, ikinci kayıt/iş emri yok
                {
                    var eskiId = r.GetString(0);
                    var wo = r.IsDBNull(1) ? null : r.GetString(1);
                    r.Close();
                    tx.Commit();
                    return new GunlukKontrolSonucu(eskiId, wo is null ? null : IsEmriNo(conn, s.CompanyId, wo), null);
                }
            }
            using (var v = conn.CreateCommand())
            {
                v.Transaction = tx;
                v.CommandText = "SELECT branch_id FROM vehicles WHERE id=@v AND company_id=@c AND is_deleted=0;";
                v.AddWithValue("@v", vehicleId); v.AddWithValue("@c", s.CompanyId);
                using var r = v.ExecuteReader();
                if (!r.Read()) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
                BranchAccess.Require(s, r.IsDBNull(0) ? null : r.GetString(0), "günlük kontrol");
            }
            id = Guid.NewGuid().ToString("N");
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO daily_checks(id, company_id, vehicle_id, check_date, results_json, failed_count, note, " +
                                  "operation_id, created_by, created_at) VALUES(@id, @c, @v, @d, @j, @f, @n, @op, @u, @t);";
                ins.AddWithValue("@id", id); ins.AddWithValue("@c", s.CompanyId); ins.AddWithValue("@v", vehicleId);
                ins.AddWithValue("@d", gun); ins.AddWithValue("@j", JsonSerializer.Serialize(temiz)); ins.AddWithValue("@f", basarisiz.Count);
                ins.AddWithValue("@n", string.IsNullOrWhiteSpace(not) ? DBNull.Value : not.Trim());
                ins.AddWithValue("@op", operationId); ins.AddWithValue("@u", s.UserId); ins.AddWithValue("@t", now);
                ins.ExecuteNonQuery();
            }
            AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Create, s.UserId,
                AfterJson: JsonSerializer.Serialize(new { gunlukKontrol = id, uygunDegil = basarisiz.Count })), _clock);
            tx.Commit();
        }
        if (!arizaAc || basarisiz.Count == 0) return new GunlukKontrolSonucu(id, null, null);

        // Arıza bildir (M-35) — kendi işleminde; açık arıza varsa reddeder → kontrol kaydı kalır, uyarı döner.
        var aciklama = "Günlük kontrolde uygun değil: " + string.Join("; ", basarisiz.Select(x => x.Not is null ? x.Madde : $"{x.Madde} ({x.Not})"));
        if (aciklama.Length > 900) aciklama = aciklama[..900] + "…";
        try
        {
            var (woId, woNo) = new DepoWise.Infrastructure.WorkOrders.WorkOrderService(_factory, _clock).AractanIsEmri(s, vehicleId, aciklama, ariza: true);
            using var conn = _factory.Create();
            using var u = conn.CreateCommand();
            u.CommandText = "UPDATE daily_checks SET work_order_id=@w WHERE id=@id AND company_id=@c;";
            u.AddWithValue("@w", woId); u.AddWithValue("@id", id); u.AddWithValue("@c", s.CompanyId);
            u.ExecuteNonQuery();
            return new GunlukKontrolSonucu(id, woNo, null);
        }
        catch (InvalidOperationException ex)
        {
            return new GunlukKontrolSonucu(id, null, "Kontrol kaydedildi; arıza iş emri açılmadı: " + ex.Message);
        }
    }

    private static string? IsEmriNo(System.Data.Common.DbConnection conn, string companyId, string woId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT wo_no FROM work_orders WHERE id=@w AND company_id=@c;";
        cmd.AddWithValue("@w", woId); cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Aracın günlük kontrolleri (en yeni üstte, en çok <paramref name="limit"/>). Eski şemada boş.</summary>
    public IReadOnlyList<GunlukKontrol> Liste(SessionContext s, string vehicleId, int limit = 30)
    {
        AccessControl.Require(s, Modul, PermissionAction.View);
        using var conn = _factory.Create();
        if (!Destekli(conn)) return Array.Empty<GunlukKontrol>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT d.id, d.vehicle_id, d.check_date, d.results_json, d.failed_count, d.note, d.work_order_id, u.username " +
                          "FROM daily_checks d LEFT JOIN users u ON u.id=d.created_by " +
                          "WHERE d.company_id=@c AND d.vehicle_id=@v ORDER BY d.check_date DESC, d.created_at DESC LIMIT @l;";
        cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@v", vehicleId); cmd.AddWithValue("@l", Math.Clamp(limit, 1, 200));
        var list = new List<GunlukKontrol>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new GunlukKontrol(r.GetString(0), r.GetString(1), Convert.ToInt64(r.GetValue(2)),
                JsonSerializer.Deserialize<List<KontrolSonucu>>(r.GetString(3)) ?? new(), Convert.ToInt32(r.GetValue(4)),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));
        return list;
    }
}
