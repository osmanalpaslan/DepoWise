using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Firma Tanım için ek alanlar: vergi no/dairesi, adres, telefon, e-posta, yetkili. Yalnız Süper Admin düzenler.
/// </summary>
public sealed class Migration016_CompanyFields : IMigration
{
    public int Version => 16;
    public string Name => "company_fields";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE companies ADD COLUMN tax_no TEXT NULL;
ALTER TABLE companies ADD COLUMN tax_office TEXT NULL;
ALTER TABLE companies ADD COLUMN address TEXT NULL;
ALTER TABLE companies ADD COLUMN phone TEXT NULL;
ALTER TABLE companies ADD COLUMN email TEXT NULL;
ALTER TABLE companies ADD COLUMN authorized_person TEXT NULL;";
        cmd.ExecuteNonQuery();
    }
}
