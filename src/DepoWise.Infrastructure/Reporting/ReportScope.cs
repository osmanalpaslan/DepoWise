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
        // ⭐ G4-3b GÜVENLİK DÜZELTMESİ: seçilen şubeler artık KULLANICININ İZİNLİ KÜMESİYLE
        // KESİŞTİRİLİR. Önceden "şube seçme yetkisi varsa istediğini gönder" deniyordu; kullanıcı
        // rapor isteğine elle branch_id yazarak YETKİSİZ şubenin verisini okuyabiliyordu.
        // BranchAccess tek otoritedir (izinli ∩ istenen ∩ oturum) — ikinci bir kural yok.
        var istenen = CanSelectBranches(s) && req.BranchIds is { Count: > 0 } ? req.BranchIds : null;
        return BranchAccess.Effective(s, istenen);
    }

    /// <summary>WHERE parçası. Boş/null → ""; aksi halde "AND (col IN (@rb0,...) OR col IS NULL)".
    /// NULL kayıtlar korunur (eski, şubesiz kayıtlar gizlenmez — BranchScope ile aynı ilke).</summary>
    public static string BranchSql(SessionContext s, ReportRequest req, string col)
    {
        // ⭐ G4-4 GÜVENLİK DÜZELTMESİ (fail-open kapatıldı): önceden boş kesişimde '' dönülüyordu,
        // yani kullanıcı YETKİSİZ bir şube istediğinde filtre TAMAMEN KALKIYOR ve rapor kapsamsız
        // çalışıyordu. Artık üretim BranchAccess.Sql'e devredilir; o, boş kesişimde
        // "AND col IS NULL" yazar (yalnız şubesiz kayıtlar) — fail-closed.
        var istenen = CanSelectBranches(s) && req.BranchIds is { Count: > 0 } ? req.BranchIds : null;
        return BranchAccess.Sql(s, col, istenen, "@rb");
    }

    /// <summary>Etkin şube parametrelerini (@rb0..) bağlar. BranchSql ile AYNI kaynağı kullanır (deterministik).</summary>
    public static void BindBranch(DbCommand cmd, SessionContext s, ReportRequest req)
    {
        var istenen = CanSelectBranches(s) && req.BranchIds is { Count: > 0 } ? req.BranchIds : null;
        BranchAccess.Bind(cmd, s, istenen, "@rb");
    }
}
