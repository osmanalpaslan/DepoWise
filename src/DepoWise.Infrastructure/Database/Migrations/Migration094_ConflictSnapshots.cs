using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ FAZ 4.4 (kullanıcı isteği 2026-09-06) — SENKRON ÇAKIŞMASINDA KAZANAN/KAYBEDEN KAYIT ═══
///
/// <b>Kullanıcının isteği:</b> <i>"Kimin kazandığı kimin kaybettiği belirtilmeli… Üzerine yazılan kaydı
/// iptal edip istenen kaydı kazanan yapabilmeli."</i>
///
/// <b>Neden şema değişikliği ZORUNLU.</b> <c>data_conflicts</c> bugüne kadar yalnız "kim kazandı" ve
/// zaman damgalarını tutuyordu; <b>kaybeden sürümün verisi hiçbir yerde saklanmıyordu.</b> Kaybedeni
/// geri getirmek istendiğinde geri getirilecek bir şey yoktu — istek mevcut şemayla teknik olarak
/// karşılanamazdı. Bu yüzden çakışma anındaki İKİ sürümün de anlık görüntüsü saklanır.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız <c>ADD COLUMN</c>. Hiç <c>UPDATE</c>/<c>DELETE</c>/backfill yok,
/// hiçbir <c>NOT NULL</c> kısıtı yok → mevcut çakışma kayıtları olduğu gibi kalır ve boş görüntüyle
/// geçerli olmayı sürdürür (arayüz "eski kayıt: sürüm verisi saklanmamış" der, uydurma yapmaz).
/// Geri alma: beş <c>DROP COLUMN</c> + <c>schema_migrations</c> satırı.
///
/// <b>SENKRON:</b> <c>data_conflicts</c> iş verisi senkron listesinde DEĞİLDİR (sunucuya özgü kayıt) →
/// ek iş gerekmez.
///
/// <b>Gizlilik:</b> görüntüler <c>AuditFields.Gizli</c> süzgecinden geçirilerek yazılır — parola özeti
/// gibi hassas sütunlar çakışma kaydına da girmez.
/// </summary>
public sealed class Migration094_ConflictSnapshots : IMigration
{
    public int Version => 94;
    public string Name => "conflict_snapshots";

    private static readonly (string Column, string Type)[] Sutunlar =
    {
        ("winner_json", "TEXT"),     // çakışma anında KAZANAN sürümün anlık görüntüsü
        ("loser_json", "TEXT"),      // çakışma anında KAYBEDEN (üzerine yazılan) sürümün görüntüsü
        ("resolution", "TEXT"),      // hidden | loser_promoted  (nasıl kapatıldı)
        ("resolved_by", "TEXT"),     // kapatan kullanıcı
        ("resolved_at", "BIGINT"),   // kapatılma anı (Unix ms)
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var (sutun, tip) in Sutunlar)
        {
            if (DbIntrospect.ColumnExists(conn, tx, "data_conflicts", sutun)) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"ALTER TABLE data_conflicts ADD COLUMN {sutun} {tip} NULL;";
            cmd.ExecuteNonQuery();
        }
    }
}
