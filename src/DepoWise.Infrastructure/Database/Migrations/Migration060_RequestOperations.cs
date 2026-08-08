using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// TALEP OPERASYONLARI — FAZ 1 temeli (kullanıcı isteği 2026-08-08). Talep belgesine, ONAY durumundan
/// BAĞIMSIZ bir "operasyon durumu" ve "öncelik" eklenir; durum geçmişi tablosu hem onay hem operasyon
/// geçişlerini tutabilsin diye türlenir.
///
/// CANLI VERİ GÜVENLİĞİ (kullanıcı kararı "B", 2026-08-08):
///  • <c>operation_status</c> NULL'a İZİN VERİR ve varsayılanı YOKTUR → mevcut satırlar NULL kalır
///    (ekranda "—"). Yalnız ONAYLI (approved) talepler geri-doldurma ile 'pending_ops' (Beklemede) alır.
///    Taslak/Beklemede/Reddedildi/İptal kayıtlara DOKUNULMAZ.
///  • Onay durumu (<c>status</c>) HİÇ değişmez; onay akışı ve durum makinesi korunur (V6 §6.12: talep
///    stoğu doğrudan değiştirmez).
///  • <c>priority</c> NOT NULL DEFAULT 'normal' → mevcut kayıtlar "Normal" olur (kullanıcı kararı).
///  • <c>request_status_history.kind</c> NOT NULL DEFAULT 'approval' → mevcut geçmiş satırları ONAY
///    geçişi olarak etiketlenir; hiçbir geçmiş kaydı silinmez/değişmez.
///
/// Hepsi ADDITIVE ve SQLite + PostgreSQL ortak sözdizimidir (ALTER TABLE ... ADD COLUMN + basit UPDATE).
/// </summary>
public sealed class Migration060_RequestOperations : IMigration
{
    public int Version => 60;
    public string Name => "request_operations";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Exec(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        // 1) Operasyon durumu — NULL olabilir (onaylanmamış talepte "—" görünür).
        Exec("ALTER TABLE material_requests ADD COLUMN operation_status TEXT;");
        // 2) Öncelik — varsayılan Normal.
        Exec("ALTER TABLE material_requests ADD COLUMN priority TEXT NOT NULL DEFAULT 'normal';");
        // 3) Geçmiş türü — mevcut satırlar onay geçişi sayılır (operasyon geçişleri Faz 2'de 'operation' yazacak).
        Exec("ALTER TABLE request_status_history ADD COLUMN kind TEXT NOT NULL DEFAULT 'approval';");

        // 4) Geri-doldurma (kullanıcı kararı B): YALNIZ onaylı talepler Beklemede ile başlar.
        //    Diğer durumdaki talepler NULL kalır — veri yorumlanmaz.
        Exec("UPDATE material_requests SET operation_status='pending_ops' WHERE status='approved' AND is_deleted=0;");
    }
}
