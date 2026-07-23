using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// #6 (şema notu) — Firma Yetki Kontrol: Süper admin, bir firmanın adminlerinin Personel'e VEREMEYECEĞİ
/// ek modülleri tanımlar. Satır varsa o modül, o firmada global kurallara EK olarak "verilemez" sayılır
/// (verilmek istenirse kullanıcı Admin'e yükseltilmelidir). Global IsAdminRestricted zaten geçerli; bu tablo
/// firmaya özel SIKILAŞTIRMA sağlar.
/// </summary>
public sealed class Migration032_CompanyGrantLimits : IMigration
{
    public int Version => 32;
    public string Name => "company_grant_limits";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE company_grant_limits (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    module_key TEXT NOT NULL,
    created_at INTEGER NOT NULL
);
CREATE UNIQUE INDEX ux_company_grant_limits ON company_grant_limits(company_id, module_key);";
        cmd.ExecuteNonQuery();
    }
}
