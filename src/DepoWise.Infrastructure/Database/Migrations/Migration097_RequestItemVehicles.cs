using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ TALEP KALEMİNDE BİRDEN FAZLA ARAÇ (kullanıcı isteği 2026-10-10) ═══
///
/// <b>Kullanıcının isteği:</b> <i>"Talep Formu ekranının yeni kayıt formunda malzemede birden fazla araç
/// ekleyebilme olmalı çünkü malzemeyi birden fazla araç için talep ediyor olabilirim."</i>
///
/// <b>Neden şema değişikliği ZORUNLU.</b> <c>material_request_items</c> kalem başına TEK
/// <c>vehicle_id</c> taşıyordu. Aynı malzemeyi birkaç araca istemenin tek yolu kalemi bölmekti; bu da
/// toplam miktarı parçalıyor ve PDF'te aynı malzemeyi birkaç satıra dağıtıyordu.
///
/// <b>Eklenen sütun:</b> <c>vehicle_ids</c> — kalemin TÜM araçları, virgülle ayrılmış kimlikler.
/// <c>vehicle_id</c> KALIR ve listenin İLK aracını taşımaya devam eder → bu alanı okuyan eski
/// masaüstü sürümleri, raporlar ve senkron hiçbir şey kaybetmez (geriye uyumlu).
/// Okuma kuralı: <c>vehicle_ids</c> doluysa o; boşsa (eski kayıt / eski istemci) <c>vehicle_id</c>.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız <c>ADD COLUMN … NULL</c>. UPDATE/DELETE/backfill yok; mevcut
/// talepler aynen kalır. Geri alma: <c>DROP COLUMN vehicle_ids</c>.
///
/// <b>SENKRON:</b> tablo zaten senkronda; genel upsert sütunları tablo ∩ satır kesişiminden okur →
/// yeni sütun ek iş gerektirmeden taşınır, eski istemci onu görmezden gelir.
/// </summary>
public sealed class Migration097_RequestItemVehicles : IMigration
{
    public int Version => 97;
    public string Name => "request_item_vehicles";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.ColumnExists(conn, tx, "material_request_items", "vehicle_ids")) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE material_request_items ADD COLUMN vehicle_ids TEXT NULL;";
        cmd.ExecuteNonQuery();
    }
}
