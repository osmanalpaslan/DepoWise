using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-39 (AR-GE 2026-10-19, 05 F-084, kullanıcı kararı K-19 "a + çift yön") — KİRALIK MAKİNE SÖZLEŞMESİ ═══
/// <c>vehicles.rental_party_id</c>: kiralayan (ya da kiraya verdiğimiz) CARİ — serbest metin <c>rental_company</c> korunur.
/// <c>vehicles.rental_direction</c>: <c>in</c> = biz kiraladık (maliyet) · <c>out</c> = biz kiraya verdik (gelir); NULL = in.
/// K-19: otomatik cari borç/alacak YOK (fatura ile girilir); dönem maliyet/gelir hesabı ve bitiş uyarısı serviste. Yalnız ekleme.
/// </summary>
public sealed class Migration114_RentalContract : IMigration
{
    public int Version => 114;
    public string Name => "rental_contract";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Ekle(string kolon)
        {
            if (DbIntrospect.ColumnExists(conn, tx, "vehicles", kolon)) return;
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = $"ALTER TABLE vehicles ADD COLUMN {kolon} TEXT NULL;";
            c.ExecuteNonQuery();
        }
        Ekle("rental_party_id");
        Ekle("rental_direction");
    }
}
