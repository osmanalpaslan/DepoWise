using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

/// <summary>⭐ M-28: kaydedilmiş (kesinleşmemiş) sayım taslağı.</summary>
public sealed record SayimTaslagi(string Id, string? BranchId, string? BranchName, bool Kor, string? Gerekce,
    IReadOnlyList<CountLine> Satirlar, long UpdatedAt, string? CreatedBy)
{
    public string Ozet => $"{BranchName ?? "—"} · {Satirlar.Count} malzeme · {DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAt).ToLocalTime():dd.MM.yyyy HH:mm}" +
                          (Kor ? " · kör sayım" : "");
}

/// <summary>
/// ═══ M-28 (AR-GE 2026-10-18, 05 F-075) — SAYIM İYİLEŞTİRMELERİ ═══
/// <list type="bullet">
///   <item><b>Taslak:</b> yarım kalan sayım kaydedilir, sonra açılıp sürdürülür; kesinleştirilince TEK sayım belgesi oluşur
///   (<see cref="StockService.Count"/>) ve taslak kapanır. Taslak stoğa hiçbir şey yazmaz.</item>
///   <item><b>Fark toleransı:</b> firma yüzdesi (<c>companies.count_tolerance_pct</c>) aşılırsa sayımı yalnız yetkili
///   (ters kayıt yetkisi olan, ör. depo sorumlusu/yönetici) kesinleştirir — sayının sisteme "uydurulması" ya da yanlış
///   girişin fark edilmeden stoğa yazılması önlenir. Kontrol <see cref="StockService.Count"/> içinde, iki platform aynı.</item>
///   <item><b>Kör sayım</b> ekran seçeneğidir: sistem miktarı sayan kişiye gösterilmez (taslakta işaret saklanır).</item>
/// </list>
/// </summary>
public sealed class SayimService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly StockService _stock;

    public SayimService(IDbConnectionFactory factory, StockService stock, IClock? clock = null)
    {
        _factory = factory;
        _stock = stock;
        _clock = clock ?? new SystemClock();
    }

    // ── FARK TOLERANSI ──────────────────────────────────────────────────────────────────────────

    /// <summary>Firmanın sayım fark toleransı (yüzde); yoksa ya da kolon yoksa null.</summary>
    public static decimal? ToleransOku(DbConnection conn, DbTransaction? tx, string companyId)
    {
        if (!DbIntrospect.ColumnExists(conn, tx, "companies", "count_tolerance_pct")) return null;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT count_tolerance_pct FROM companies WHERE id=@c;";
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() is string v && v.Length > 0 ? Money.Parse(v) : null;
    }

    public decimal? Tolerans(SessionContext s)
    {
        using var conn = _factory.Create();
        return ToleransOku(conn, null, s.CompanyId);
    }

    /// <summary>Toleransı ayarlar (null = kontrol yok). Yalnız yönetici; 0–100 arası; işlem geçmişine yazılır.</summary>
    public void ToleransAyarla(SessionContext s, decimal? yuzde)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Sayım toleransını yalnız yönetici ayarlayabilir.");
        if (yuzde is { } y && (y < 0 || y > 100)) throw new ArgumentException("Tolerans 0 ile 100 arasında bir yüzde olmalı.");
        using var conn = _factory.Create();
        if (!DbIntrospect.ColumnExists(conn, null, "companies", "count_tolerance_pct"))
            throw new InvalidOperationException("Veritabanı sayım toleransını henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE companies SET count_tolerance_pct=@t WHERE id=@c;";
            cmd.AddWithValue("@t", yuzde is { } v ? Money.Serialize(v) : DBNull.Value);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "company", s.CompanyId, AuditActions.Update, s.UserId,
            AfterJson: yuzde is { } p ? $"{{\"sayimToleransi\":\"{Money.Serialize(p)}\"}}" : "{\"sayimToleransi\":null}"), _clock);
        tx.Commit();
    }

    /// <summary>Tolerans kuralı (saf): sistem miktarına göre farkın yüzdesi toleransı aşıyor mu? Sistem 0 ise fark 0 değilse aşar.</summary>
    public static bool ToleransiAsar(decimal sistem, decimal sayilan, decimal tolerans)
    {
        var fark = Math.Abs(sayilan - sistem);
        if (fark == 0) return false;
        if (sistem == 0) return true;
        return fark * 100m / Math.Abs(sistem) > tolerans;
    }

    // ── TASLAK ──────────────────────────────────────────────────────────────────────────────────

    private static string Json(IReadOnlyList<CountLine> lines)
        => JsonSerializer.Serialize(lines.Select(l => new { m = l.MaterialId, q = Money.Serialize(l.CountedQuantity) }));

    private static List<CountLine> Satirlar(string json)
        => JsonSerializer.Deserialize<List<JsonElement>>(json)!
            .Select(e => new CountLine(e.GetProperty("m").GetString()!, Money.Parse(e.GetProperty("q").GetString()))).ToList();

    /// <summary>Taslağı kaydeder (yeni ya da mevcut). Stoğa hiçbir şey yazmaz. Dönen değer taslak kimliği.</summary>
    public string TaslakKaydet(SessionContext s, string? taslakId, string? branchId, bool kor, string? gerekce, IReadOnlyList<CountLine> satirlar)
    {
        AccessControl.Require(s, StockService.Module, PermissionAction.Create);
        if (satirlar.Count == 0) throw new ArgumentException("Taslakta en az bir malzeme olmalı.");
        if (satirlar.Any(l => l.CountedQuantity < 0)) throw new ArgumentException("Sayılan miktar eksi olamaz.");
        BranchAccess.Require(s, branchId, "sayım taslağı");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!DbIntrospect.TableExists(conn, null, "stock_count_drafts"))
            throw new InvalidOperationException("Veritabanı sayım taslağını henüz desteklemiyor (güncelleme bekleniyor).");
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(taslakId))
        {
            taslakId = Guid.NewGuid().ToString("N");
            cmd.CommandText = "INSERT INTO stock_count_drafts(id, company_id, branch_id, created_by, blind, reason, lines_json, created_at, updated_at, version, is_deleted) " +
                              "VALUES(@id,@c,@b,@u,@k,@r,@l,@n,@n,1,0);";
        }
        else
            cmd.CommandText = "UPDATE stock_count_drafts SET branch_id=@b, blind=@k, reason=@r, lines_json=@l, updated_at=@n, version=version+1 " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", taslakId);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.AddWithValue("@u", s.UserId);
        cmd.AddWithValue("@k", kor ? 1L : 0L);
        cmd.AddWithValue("@r", (object?)gerekce ?? DBNull.Value);
        cmd.AddWithValue("@l", Json(satirlar));
        cmd.AddWithValue("@n", now);
        if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Taslak bulunamadı, silinmiş ya da başka firmaya ait.");
        return taslakId!;
    }

    /// <summary>Açık taslaklar (kullanıcının şube kapsamında).</summary>
    public IReadOnlyList<SayimTaslagi> Taslaklar(SessionContext s)
    {
        AccessControl.Require(s, StockService.Module, PermissionAction.View);
        using var conn = _factory.Create();
        if (!DbIntrospect.TableExists(conn, null, "stock_count_drafts")) return Array.Empty<SayimTaslagi>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT d.id, d.branch_id, b.name, d.blind, d.reason, d.lines_json, d.updated_at, d.created_by FROM stock_count_drafts d " +
                          "LEFT JOIN branches b ON b.id=d.branch_id AND b.company_id=d.company_id " +
                          "WHERE d.company_id=@c AND d.is_deleted=0 ORDER BY d.updated_at DESC;";
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<SayimTaslagi>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var sube = r.IsDBNull(1) ? null : r.GetString(1);
            if (!BranchAccess.CanAccess(s, sube)) continue;
            list.Add(new SayimTaslagi(r.GetString(0), sube, r.IsDBNull(2) ? null : r.GetString(2), r.GetInt64(3) != 0,
                r.IsDBNull(4) ? null : r.GetString(4), Satirlar(r.GetString(5)), r.GetInt64(6), r.IsDBNull(7) ? null : r.GetString(7)));
        }
        return list;
    }

    public void TaslakSil(SessionContext s, string taslakId)
    {
        AccessControl.Require(s, StockService.Module, PermissionAction.Create);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE stock_count_drafts SET is_deleted=1, updated_at=@n WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@n", _clock.UtcNow.ToUnixTimeMilliseconds());
        cmd.AddWithValue("@id", taslakId);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Taslağı KESİNLEŞTİRİR: tek sayım belgesi (tolerans kuralı dahil) + taslak kapanır. Aynı taslak iki kez
    /// kesinleşmez (işlem kimliği taslaktan türetilir).</summary>
    public StockDocResult Kesinlestir(SessionContext s, string taslakId, string gerekce, long? docDate = null)
    {
        var t = Taslaklar(s).FirstOrDefault(x => x.Id == taslakId)
            ?? throw new ForbiddenException("Taslak bulunamadı, kesinleşmiş ya da başka firmaya ait.");
        var r = _stock.Count(s, t.Satirlar, gerekce, $"count-draft:{taslakId}", t.BranchId, docDate);
        TaslakSil(s, taslakId);
        return r;
    }
}
