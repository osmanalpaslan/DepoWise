using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Talep Onaylama ayrı ekran+yetki oldu: eski "btn-approve" ÖZEL BUTONU yerine "request_approval" MODÜLÜ.
/// Mevcut onay yetkisi olan kullanıcılar (user_button_permissions.btn-approve) yeni modüle taşınır:
/// request_approval → can_view=1, can_edit=1 (onay/ret bu modül Edit'ini ister). Erişim korunur (deny-by-default).
/// Admin/süper admin zaten bypass; bu yalnız açık onay yetkisi verilmiş Personel'leri etkiler.
/// Idempotent: yeni modül satırı zaten varsa tekrar eklemez. Eski buton satırı temizlenir.
/// </summary>
public sealed class Migration035_SplitRequestApproval : IMigration
{
    public int Version => 35;
    public string Name => "split_request_approval";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // 1) btn-approve sahiplerine request_approval (view+edit) modül izni ver (yoksa)
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version)
SELECT lower(hex(randomblob(16))), b.company_id, b.user_id, 'request_approval', 1, 0, 1, 0,
       CAST(strftime('%s','now') AS INTEGER)*1000, CAST(strftime('%s','now') AS INTEGER)*1000, 1
FROM user_button_permissions b
WHERE b.button_key = 'btn-approve'
  AND NOT EXISTS (SELECT 1 FROM user_permissions q WHERE q.user_id = b.user_id AND q.module_key = 'request_approval');";
            cmd.ExecuteNonQuery();
        }

        // 2) Eski btn-approve buton izinlerini temizle (artık kataloğda yok)
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM user_button_permissions WHERE button_key = 'btn-approve';";
            cmd.ExecuteNonQuery();
        }
    }
}
