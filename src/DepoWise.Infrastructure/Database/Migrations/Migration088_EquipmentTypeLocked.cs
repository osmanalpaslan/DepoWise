using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// 🔴 HATA DÜZELTMESİ (kullanıcı bildirimi 2026-09-04): Tanımlar ekranında
/// <c>SQLite Error 1: 'no such column: is_locked'</c>.
///
/// <b>Kök neden — sıra hatası:</b> <see cref="Migration051_LookupLocked"/> tanım tablolarına
/// <c>is_locked</c> ekliyor, ama listesi o tarihte var olan 8 tabloyu kapsıyordu.
/// <c>equipment_types</c> DAHA SONRA (Migration075) oluşturuldu ve sütun eklenmedi. Oysa
/// <c>LookupService.List</c> HER tanım tablosunda <c>SELECT id, name, is_locked</c> yapar →
/// "Ekipman — Türler" bölümü açılınca sorgu patlıyor.
///
/// Bu, yalnız EKLEMELİ bir düzeltmedir: tek sütun, varsayılan 0 (kilitsiz), hiçbir mevcut satır
/// değişmez ve hiçbir kayıt kilitlenmez — diğer 8 tabloda 051'in yaptığının aynısı.
///
/// <b>Kalıcı ders:</b> yeni bir TANIM tablosu eklerken <c>is_locked</c> sütunu da eklenmelidir,
/// aksi halde tablo Tanımlar ekranında açılamaz. (Aynı sınıf hata tekrar etmesin diye
/// <c>TanimTablosuSemaTests</c> bunu tüm tanım tablolarında kontrol eder.)
/// </summary>
public sealed class Migration088_EquipmentTypeLocked : IMigration
{
    public int Version => 88;
    public string Name => "equipment_type_locked";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // Sütun zaten varsa sessizce geç (mevcut DbIntrospect yardımcısı iki lehçeyi de bilir).
        if (DbIntrospect.ColumnExists(conn, tx, "equipment_types", "is_locked")) return;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE equipment_types ADD COLUMN is_locked BIGINT NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }
}
