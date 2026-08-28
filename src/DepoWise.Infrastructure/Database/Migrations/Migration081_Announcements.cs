using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ DYR-01 (ADR-173, 2026-08-28) — DUYURU: VERİ MODELİ ═══
///
/// Yol haritası FAZ 4 / SIRA 10. Ürün kararları KESİN (kullanıcı, 2026-08-28 — J_DUYURU_00_ANALIZ.md):
/// PK-J1 okuma HERKESE (yazma announcements yetkisiyle) · PK-J2 opsiyonel TEK şube hedefi ·
/// PK-J3 opsiyonel yayın penceresi (boşsa hemen+süresiz; aktiflik TÜRETİLİR — durum alanı YOK) ·
/// PK-J4 gösterim yalnız Bildirim Merkezi + Duyurular ekranı · PK-J5 önem: normal | important.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız TEK yeni tablo (CREATE); mevcut hiçbir tabloya ALTER dahi yok;
/// backfill yok. OKUNDU işareti için tablo AÇILMADI — mevcut alert_reads (Migration031) kullanılır.
/// Rollback: tek DROP + schema_migrations satırı.
/// </summary>
public sealed class Migration081_Announcements : IMigration
{
    public int Version => 81;
    public string Name => "announcements";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE announcements (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    branch_id TEXT NULL,                      -- PK-J2: hedef şube; boşsa FİRMA GENELİ (BranchAccess kapsamı buradan)
    title TEXT NOT NULL,
    body TEXT NULL,
    importance TEXT NOT NULL DEFAULT 'normal', -- PK-J5: normal | important (önemli = kritik rozet)
    publish_start BIGINT NULL,                -- PK-J3: PLAN tarihi (ADR-162: geri-tarih kapısına GİRMEZ); boşsa hemen
    publish_end BIGINT NULL,                  -- boşsa süresiz
    created_by TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- yumuşak silme işareti (Çöp Kutusu standardı)
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id)
);
CREATE INDEX ix_ann_company ON announcements(company_id, is_deleted);";
        cmd.ExecuteNonQuery();
    }
}
