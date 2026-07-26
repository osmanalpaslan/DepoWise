namespace DepoWise.Application.Security;

/// <summary>
/// ŞUBE KAPSAMI (2026-07-25, kullanıcı isteği): belirli bir şubeyle giriş yapıldığında veri o şubeye göre
/// filtrelenir; "Tüm Şubeler" ile girişte (OperatingBranchId null) TÜM firma verisi gösterilir.
///
/// Kurallar:
///  • <see cref="Active"/> null → filtre YOK (Tüm Şubeler / şube atanmamış oturum).
///  • Şubesi OLMAYAN (NULL) eski kayıtlar GİZLENMEZ — her şubede görünür (veri kaybı/görünmezlik olmasın).
///  • Malzeme firma-genelidir (şube kolonu yok) → bu yardımcı ORAYA uygulanmaz; malzeme her şubede görünür.
///  • Yönetici raporları TÜM şubeleri gösterir → onlarda bu filtre KULLANILMAZ.
///
/// Kullanım (Infrastructure servis sorgusu):
///   cmd.CommandText = "... WHERE company_id=@c" + BranchScope.Sql(s, "op_branch_id") + " ORDER BY ...";
///   cmd.AddWithValue("@c", companyId);
///   if (BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
/// </summary>
public static class BranchScope
{
    /// <summary>Etkin çalışma şubesi: belirli şube seçiliyse id; "Tüm Şubeler"/atanmamış → null (filtre yok).</summary>
    public static string? Active(SessionContext s)
        => string.IsNullOrEmpty(s.OperatingBranchId) ? null : s.OperatingBranchId;

    /// <summary>SQL filtre parçası. Belirli şube seçiliyse "AND (col=@opb OR col IS NULL)"; aksi halde boş.
    /// NULL kayıtlar korunur (gizlenmez). Parametreyi çağıran <c>@opb</c> adıyla BAĞLAR (bkz. <see cref="Active"/>).</summary>
    public static string Sql(SessionContext s, string col, string param = "@opb")
        => Active(s) is null ? "" : $" AND ({col} = {param} OR {col} IS NULL)";
}
