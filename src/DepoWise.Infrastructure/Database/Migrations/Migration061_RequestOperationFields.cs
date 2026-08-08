using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// TALEP OPERASYONLARI — FAZ 2 alanları (kullanıcı onayı 2026-08-08).
/// Gönderim bilgileri (şartname §6) + her operasyon adımının HANGİ ŞUBEDEN yapıldığı bilgisi.
///
/// CANLI VERİ GÜVENLİĞİ: 4 kolonun tamamı ADDITIVE ve NULL'a izinlidir; varsayılan/geri-doldurma YOKTUR →
/// mevcut kayıtlar NULL kalır, hiçbir veri dönüştürülmez/silinmez. Onay akışı ve operasyon durumu (Faz 1)
/// etkilenmez. SQLite + PostgreSQL ortak sözdizimi.
///
///  • material_requests.ops_from_branch_id — Gönderen Şube
///  • material_requests.ops_to_branch_id   — Gönderilecek Şube
///  • material_requests.ops_note           — Operasyon Notu
///  • request_status_history.op_branch_id  — işlemin YAPILDIĞI şube (sunucu tarafında oturumdan belirlenir;
///    istemciden gelen değere güvenilmez). Onay geçmişi ile operasyon geçmişi ayrımı `kind` alanıyla korunur.
/// </summary>
public sealed class Migration061_RequestOperationFields : IMigration
{
    public int Version => 61;
    public string Name => "request_operation_fields";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Exec(string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        Exec("ALTER TABLE material_requests ADD COLUMN ops_from_branch_id TEXT;");
        Exec("ALTER TABLE material_requests ADD COLUMN ops_to_branch_id TEXT;");
        Exec("ALTER TABLE material_requests ADD COLUMN ops_note TEXT;");
        Exec("ALTER TABLE request_status_history ADD COLUMN op_branch_id TEXT;");
    }
}
