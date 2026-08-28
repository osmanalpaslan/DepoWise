using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ TKV-01 (ADR-171, 2026-08-28) — TAKVİM: VERİ MODELİ ═══
///
/// Yol haritası FAZ 3 / SIRA 8. Ürün kararları KESİN (kullanıcı, 2026-08-28 — H_TAKVIM_00_ANALIZ.md):
/// PK-H1 HİBRİT (türetilmiş + el ile kayıt) · PK-H2 türetilmiş kaynaklar iş emri planı, muayene/sigorta,
/// evrak geçerlilik, proje tarihleri, gün-bazlı bakım hedefi · PK-H3 kaynak planlama/çakışma denetimi YOK
/// (yalnız opsiyonel sorumlu personel) · PK-H4 gün bazlı, saat YOK (ms kolonu saati İLERİDE eklemeli
/// taşıyabilir) · PK-H5 opsiyonel iş emri bağı — YALNIZ gezinme; durum/iş mantığı tetiklemez.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız TEK yeni tablo (CREATE); mevcut hiçbir tabloya ALTER dahi yok;
/// backfill yok. Türetilmiş kaynaklar bu tabloya KOPYALANMAZ (salt-okunur SELECT ile toplanır).
/// Rollback: tek DROP + schema_migrations satırı.
///
/// Tekrarlayan işler (PK-F7 → Takvim'in gelecek konusu) bu şemayı DEĞİŞTİRMEDEN, ileride eklemeli
/// kolonlarla (kural/aralık/seri) gelir — v1'de tekrar mantığı YOKTUR.
/// </summary>
public sealed class Migration080_CalendarEvents : IMigration
{
    public int Version => 80;
    public string Name => "calendar_events";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE calendar_events (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    branch_id TEXT NULL,                      -- opsiyonel şantiye/saha (BranchAccess kapsamı buradan; boşsa herkese görünür)
    title TEXT NOT NULL,
    note TEXT NULL,
    start_date BIGINT NOT NULL,               -- PLAN tarihi, gün bazlı (ADR-162: plan tarihleri geri-tarih kapısına GİRMEZ)
    end_date BIGINT NULL,                     -- çok günlü aralık; boşsa tek gün
    responsible_personnel_id TEXT NULL,       -- PK-H3: tek opsiyonel sorumlu; çoklu kaynak planlama YOK
    work_order_id TEXT NULL,                  -- PK-H5: yalnız gezinme bağı; iş emri durumuna DOKUNMAZ
    created_by TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- yumuşak silme işareti (Çöp Kutusu standardı)
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id),
    FOREIGN KEY (responsible_personnel_id) REFERENCES personnel(id),
    FOREIGN KEY (work_order_id) REFERENCES work_orders(id)
);
CREATE INDEX ix_cal_company ON calendar_events(company_id, is_deleted);
CREATE INDEX ix_cal_date ON calendar_events(company_id, start_date);";
        cmd.ExecuteNonQuery();
    }
}
