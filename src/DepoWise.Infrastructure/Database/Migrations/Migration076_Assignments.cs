using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ ZMT-01 (ADR-167, 2026-08-28) — ZİMMET YÖNETİMİ: VERİ MODELİ ═══
///
/// Yol haritası FAZ 2 / SIRA 4. Ürün kararları (kullanıcı, 2026-08-28):
/// PK-B1 <b>stoklu hibrit</b>: malzeme teslimi mevcut stok ÇIKIŞINI çağırır (aynı transaction, IssueOutTx),
/// iade GİRİŞİ çağırır; ekipman stok dışıdır · PK-B2 devir TEK işlemdir (defterde çift kayıt) ·
/// PK-B3 kayıp stoğa dönmez, hasarlı iade döner · PK-B4 hedef yalnız PERSONEL, araçlar dahil değil.
///
/// <b>DEFTER MODELİ (kullanıcı isteği §11):</b> zimmet bir "durum" değil HAREKET defteridir —
/// stock_movements'ın kardeşi. Her teslim/iade/devir/kayıp AYRI, DEĞİŞMEZ satırdır; "kimde ne var"
/// bu satırlardan TÜRETİLİR. Sahip değiştirirken UPDATE yapılmaz → geçmiş yapısal olarak silinemez.
/// operation_id ÜZERİNDE TEKİL indeks → aynı işlem ikinci kez uygulanamaz (idempotent, LWW değil).
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız BİR yeni tablo (CREATE); mevcut hiçbir tabloya dokunulmaz;
/// geçmiş stok hareketleri yeniden yorumlanmaz, bakiye yeniden hesaplanmaz.
/// Rollback: <c>DROP TABLE assignment_movements; DELETE FROM schema_migrations WHERE version=76;</c>
///
/// <b>SENKRON:</b> araç/stok deseniyle listeye FK sıralı girer (personnel/materials/equipment SONRASI).
/// asset_id bilinçli FK'sızdır (material VEYA equipment — file_records'un entity deseni emsali).
/// </summary>
public sealed class Migration076_Assignments : IMigration
{
    public int Version => 76;
    public string Name => "assignments";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE assignment_movements (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    asset_type TEXT NOT NULL,                 -- material | equipment (PK-B4: araç YOK)
    asset_id TEXT NOT NULL,                   -- materials.id veya equipment.id (polymorphic; FK'sız — bilinçli)
    personnel_id TEXT NOT NULL,
    branch_id TEXT NULL,                      -- işlem şubesi (malzemede stok deposu; BranchAccess kapsamı buradan)
    movement_type TEXT NOT NULL,              -- issue | return | transfer_out | transfer_in | lost | damaged_return
    direction BIGINT NOT NULL,                -- +1 kişiye geçti | -1 kişiden çıktı
    quantity TEXT NOT NULL,                   -- decimal (invariant), pozitif; ekipmanda '1'
    group_id TEXT NULL,                       -- devir çiftini bağlar (transfer_out + transfer_in)
    stock_operation_id TEXT NULL,             -- bağlı stok belgesinin operation_id'si (izlenebilirlik; FK değil)
    doc_date BIGINT NOT NULL,                 -- İŞ GÜNÜ (ADR-162: geri-tarih btn-backdate yetkisine bağlı)
    note TEXT NULL,
    operation_id TEXT NOT NULL,               -- idempotency
    created_at BIGINT NOT NULL,               -- KAYIT ANI (daima gerçek saat)
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- defter satırı silinmez; kolon yalnız şema tutarlılığı için
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (personnel_id) REFERENCES personnel(id)
);
CREATE UNIQUE INDEX ux_assign_operation ON assignment_movements(operation_id);
CREATE INDEX ix_assign_company ON assignment_movements(company_id, is_deleted);
CREATE INDEX ix_assign_person_asset ON assignment_movements(company_id, personnel_id, asset_type, asset_id);
CREATE INDEX ix_assign_asset ON assignment_movements(company_id, asset_type, asset_id);";
        cmd.ExecuteNonQuery();
    }
}
