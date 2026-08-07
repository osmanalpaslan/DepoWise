using System.Data.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Rapor ŞUBE KAPSAMI + yetki (kullanıcı isteği 2026-08-07, madde 5). NON-BREAKING kural:
///  • Şube seçimi BOŞ ise → MEVCUT davranış: oturum (login) şube kapsamı (BranchScope) — hiçbir şey değişmez.
///  • YALNIZ yetkili kullanıcı (btn-branch-select veya admin) AÇIKÇA şube seçtiğinde o şubeler uygulanır
///    (ölü filtre bug'ı böyle güvenli düzelir). Yetkisiz kullanıcı gönderse bile yok sayılır (fail-closed).
/// "Tüm şubeler" = "Tüm Şubeler" ile giriş (mevcut mekanizma) ya da ileride açık seçenek.
/// </summary>
public static class ReportScope
{
    /// <summary>Kullanıcı raporda şube SEÇEBİLİR mi (yetki). Admin/süper admin bypass.</summary>
    public static bool CanSelectBranches(SessionContext s)
        => AccessControl.CanUseButton(s, SpecialButtons.BranchSelect);

    /// <summary>Etkin şube listesi. Yetkili + açık seçim → o şubeler; aksi halde oturum şubesi (mevcut davranış).
    /// null → filtre yok (Tüm Şubeler / atanmamış oturum).</summary>
    public static IReadOnlyList<string>? Effective(SessionContext s, ReportRequest req)
    {
        if (CanSelectBranches(s) && req.BranchIds is { Count: > 0 })
            return req.BranchIds;   // yetkili + açık seçim → honor
        return BranchScope.Active(s) is { } b ? new[] { b } : null;   // yetkisiz/boş → oturum şubesi (değişmez)
    }

    /// <summary>WHERE parçası. Boş/null → ""; aksi halde "AND (col IN (@rb0,...) OR col IS NULL)".
    /// NULL kayıtlar korunur (eski, şubesiz kayıtlar gizlenmez — BranchScope ile aynı ilke).</summary>
    public static string BranchSql(SessionContext s, ReportRequest req, string col)
    {
        var b = Effective(s, req);
        if (b is null || b.Count == 0) return "";
        var ps = string.Join(",", System.Linq.Enumerable.Range(0, b.Count).Select(i => "@rb" + i));
        return $" AND ({col} IN ({ps}) OR {col} IS NULL)";
    }

    /// <summary>Etkin şube parametrelerini (@rb0..) bağlar. BranchSql ile AYNI kaynağı kullanır (deterministik).</summary>
    public static void BindBranch(DbCommand cmd, SessionContext s, ReportRequest req)
    {
        var b = Effective(s, req);
        if (b is null) return;
        for (int i = 0; i < b.Count; i++) cmd.AddWithValue("@rb" + i, b[i]);
    }
}
