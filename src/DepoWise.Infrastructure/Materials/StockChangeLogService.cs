using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Materials;

/// <summary>Doğrudan stok değişikliği uyarı logu satırı (madde 1.4). Denormalize snapshot — salt okuma.</summary>
public sealed record StockChangeLogRow(
    long CreatedAt, string User, string MaterialCode, string MaterialName,
    decimal OldQuantity, decimal NewQuantity, string Outcome, string? WarningText, string? BranchName)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string UserText => string.IsNullOrWhiteSpace(User) ? "—" : User;
    public string MaterialText => string.IsNullOrWhiteSpace(MaterialCode) ? MaterialName : $"{MaterialCode} — {MaterialName}";
    public string OldText => OldQuantity.ToString("0.##");
    public string NewText => NewQuantity.ToString("0.##");
    public string DiffText { get { var d = NewQuantity - OldQuantity; return (d >= 0 ? "+" : "") + d.ToString("0.##"); } }
    public string OutcomeText => Outcome == "continued" ? "Devam edildi" : "İptal edildi";
    public string WarningPreview => string.IsNullOrWhiteSpace(WarningText) ? "—" : WarningText!;
}

/// <summary>
/// Doğrudan stok değişikliği (Malzeme kartından, Giriş/Çıkış ekranı KULLANILMADAN) uyarı + LOG akışı
/// (kullanıcı isteği 2026-08-06, madde 1.2-1.5). Uyarı istemcide gösterilir; kullanıcının kararına göre bu
/// servis çağrılır: <c>continued=true</c> → stok, mimariye uygun biçimde SAYIM/DÜZELTME (adjustment) hareketiyle
/// güncellenir (doğrudan bakiye yazımı YOK — hareket defteri ana kaynak) + log("continued"); <c>false</c> →
/// yalnız log("cancelled"). Log audit_logs gibi senkron edilmez (her DB kendi kaydı). Görüntüleme yetkisi:
/// module <c>stock_change_log</c> (Admin-restricted; Yetki Ağacında otomatik).
/// </summary>
public sealed class StockChangeLogService
{
    private const string Module = "stock_change_log";
    /// <summary>Uyarı metni tek doğru kaynak (masaüstü + web AYNI metni gösterir; log da bunu kaydeder).</summary>
    public const string WarningMessage =
        "Stok miktarını doğrudan düzenlemeye çalışıyorsunuz. Stok hareketlerinin kayıt altına alınabilmesi için " +
        "işlemleri mümkün olduğunca Giriş/Çıkış ekranından gerçekleştirmeniz önerilir. Devam ederseniz bu " +
        "değişiklik bir stok düzeltmesi olarak kaydedilir ve loglanır.";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly StockService _stock;

    public StockChangeLogService(IDbConnectionFactory factory, StockService stock, IClock? clock = null)
    {
        _factory = factory;
        _stock = stock;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Malzeme kartından doğrudan stok değişikliği kararı. Uyarı gösterildikten SONRA çağrılır.
    /// Tetikleyici malzeme düzenleme olduğundan materials:Edit gerekir; devam halinde stok düzeltmesi
    /// StockService.Count (stock:Create) ile uygulanır. Devam + gerçek fark yoksa yalnız log yazılır.</summary>
    public void Record(SessionContext s, string materialId, decimal newQuantity, bool continued, string? warningText)
    {
        AccessControl.Require(s, "materials", PermissionAction.Edit);
        var (code, name) = ReadMaterial(s.CompanyId, materialId);   // firma sahipliği + snapshot
        var oldQty = _stock.GetBalance(s, materialId);
        var branchId = s.OperatingBranchId;

        if (continued && newQuantity != oldQty)
            _stock.Count(s, new[] { new CountLine(materialId, newQuantity) },
                "Malzeme kartından doğrudan stok düzeltmesi", System.Guid.NewGuid().ToString("N"), branchId);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO stock_change_logs(id, company_id, user_id, user_name, material_id, material_code, material_name,
    branch_id, old_quantity, new_quantity, outcome, warning_text, created_at)
VALUES(@id,@c,@uid,@uname,@mid,@mcode,@mname,@b,@old,@new,@out,@warn,@now);";
        cmd.AddWithValue("@id", System.Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@uid", s.UserId);
        cmd.AddWithValue("@uname", ResolveUserName(conn, s.UserId));
        cmd.AddWithValue("@mid", materialId);
        cmd.AddWithValue("@mcode", code);
        cmd.AddWithValue("@mname", name);
        cmd.AddWithValue("@b", branchId);
        cmd.AddWithValue("@old", Money.Serialize(oldQty));
        cmd.AddWithValue("@new", Money.Serialize(newQuantity));
        cmd.AddWithValue("@out", continued ? "continued" : "cancelled");
        cmd.AddWithValue("@warn", warningText);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Log görüntüleme (madde 1.4/1.5) — Tarih Aralığı + kayıt sayısı (Sistem Logu ile AYNI desen,
    /// limit 1-5000). Yetki: module stock_change_log View.</summary>
    public IReadOnlyList<StockChangeLogRow> List(SessionContext s, long? fromMs = null, long? toMs = null, int limit = 300)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (limit < 1) limit = 1; if (limit > 5000) limit = 5000;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sb = new System.Text.StringBuilder(@"
SELECT l.created_at, COALESCE(l.user_name,''), COALESCE(l.material_code,''), COALESCE(l.material_name,''),
       l.old_quantity, l.new_quantity, l.outcome, l.warning_text, COALESCE(br.name,'')
FROM stock_change_logs l
LEFT JOIN branches br ON br.id = l.branch_id
WHERE l.company_id = @c");
        if (fromMs is not null) sb.Append(" AND l.created_at >= @from");
        if (toMs is not null) sb.Append(" AND l.created_at <= @to");
        sb.Append(" ORDER BY l.created_at DESC LIMIT @lim;");
        cmd.CommandText = sb.ToString();
        cmd.AddWithValue("@c", s.CompanyId);
        if (fromMs is not null) cmd.AddWithValue("@from", fromMs.Value);
        if (toMs is not null) cmd.AddWithValue("@to", toMs.Value);
        cmd.AddWithValue("@lim", limit);
        var list = new List<StockChangeLogRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StockChangeLogRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Money.Parse(r.GetString(4)), Money.Parse(r.GetString(5)), r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetString(8)));
        return list;
    }

    /// <summary>
    /// ⭐ LST-01 (2026-09-07) — AYNI FİLTREDEKİ GERÇEK TOPLAM.
    /// <see cref="List"/> tavanlıdır; ekran dönen satır sayısını "toplam" diye yazarsa kayıtlar
    /// SESSİZCE gizlenir. Ekran gerçek toplamı buradan sorar ve tavana takıldığını kullanıcıya söyler.
    /// </summary>
    public int Sayim(SessionContext s, long? fromMs = null, long? toMs = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sb = new System.Text.StringBuilder("SELECT COUNT(*) FROM stock_change_logs l WHERE l.company_id = @c");
        if (fromMs is not null) sb.Append(" AND l.created_at >= @from");
        if (toMs is not null) sb.Append(" AND l.created_at <= @to");
        cmd.CommandText = sb.Append(';').ToString();
        cmd.AddWithValue("@c", s.CompanyId);
        if (fromMs is not null) cmd.AddWithValue("@from", fromMs.Value);
        if (toMs is not null) cmd.AddWithValue("@to", toMs.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private (string? Code, string? Name) ReadMaterial(string companyId, string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT code, name FROM materials WHERE id=@m AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
        return (r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
    }

    private static string? ResolveUserName(System.Data.Common.DbConnection conn, string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(NULLIF(full_name,''), username) FROM users WHERE id=@id;";
        cmd.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() as string;
    }
}
