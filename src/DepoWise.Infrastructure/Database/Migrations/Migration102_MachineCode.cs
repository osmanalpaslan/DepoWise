using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-78 / K-21 (AR-GE 2026-10-17) — BİLGİSAYAR KODU (belge numarasına gömülür) ═══
///
/// <b>Sorun (05 F-172).</b> Belge numaraları <c>MAX(...)+1</c> ile üretiliyordu; iki bilgisayar çevrimdışıyken aynı gün belge
/// açarsa ikisi de aynı numarayı (ör. <c>TLP-2026-0042</c>) üretir, sunucu benzersizlik ihlalini kalıcı atlar → belge ve ona
/// bağlı satırlar sunucuya HİÇ ulaşmaz.
///
/// <b>Çözüm (kullanıcı kararı K-21 a).</b> Sunucu her bilgisayara firma içinde benzersiz kısa bir kod atar (<c>A1</c>, <c>A2</c>…;
/// <c>sync_devices.machine_code</c>). Masaüstü numaraları <c>TLP-2026-A3-0042</c> biçiminde üretir; sıra bilgisayara özeldir →
/// çakışma imkânsız. Yalnız ekleme: kolon NULL doğar, kod ilk kayıtta atanır; eski numaralar aynen kalır.
/// </summary>
public sealed class Migration102_MachineCode : IMigration
{
    public int Version => 102;
    public string Name => "machine_code";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }
        if (!DbIntrospect.ColumnExists(conn, tx, "sync_devices", "machine_code"))
            Exec("ALTER TABLE sync_devices ADD COLUMN machine_code TEXT NULL;");
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS ux_sync_devices_machine_code ON sync_devices(company_id, machine_code) " +
             "WHERE machine_code IS NOT NULL;");
    }
}
