using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ PRJ-01 (ADR-164, 2026-08-27) — PROJE / ŞANTİYE YÖNETİMİ: VERİ MODELİ ═══
///
/// Yol haritası FAZ 1 / SIRA 1 (bkz. docs/project-control/MASTER_ROADMAP.md §2).
/// Ürün kararları: PK-C1 "şimdilik tek şantiye, ileride çok" · PK-C3 tüm kart alanları opsiyonel ·
/// PK-C4 ayrı yetki kapısı YOK (branches modülü + BranchAccess).
///
/// <b>CANLI VERİ GÜVENLİĞİ — bu migration'ın mevcut verilere dokunMAMA kanıtı:</b>
/// <list type="bullet">
///   <item>Yalnız iki YENİ tablo oluşturur (<c>CREATE TABLE</c>); hiçbir mevcut tabloya
///     <c>ALTER/UPDATE/DELETE/INSERT</c> uygulamaz — <c>branches</c> dahil.</item>
///   <item>Yeni tablolar boş doğar; mevcut kayıtlara otomatik proje ataması YAPILMAZ
///     (kullanıcı kuralı: backfill yok).</item>
///   <item>FK'ler yalnız YENİ satırlar yazılırken doğrulanır; mevcut satırları etkilemez.</item>
///   <item>Rollback: iki tabloyu DROP etmek yeterlidir; başka hiçbir iz kalmaz
///     (<c>DROP TABLE project_branches; DROP TABLE projects;
///     DELETE FROM schema_migrations WHERE version=73;</c>).</item>
/// </list>
///
/// <b>NEDEN AYRI TABLO + İLİŞKİ TABLOSU (branches'a kolon değil):</b> PK-C1 gereği gelecekte bir proje
/// birden fazla şantiyeye yayılabilmeli. <c>project_branches</c> ilişki tablosu bunu bugünden taşır —
/// tek→çok geçişi yalnız UI değişikliğidir, yeni migration GEREKTİRMEZ. Ayrıca canlı <c>branches</c>
/// tablosuna kolon eklememek "mevcut tabloya sıfır dokunuş" ilkesini korur.
///
/// <b>SENKRON:</b> bu tablolar <c>BusinessSyncService.Tables</c>'a EKLENMEZ. Projeler, şubeler gibi
/// SUNUCU-OTORİTELİDİR (masaüstü CRUD'u çevrimiçi API ile yapar). Teknik zorunluluk da var: ebeveyn
/// <c>branches</c> senkron paketinde taşınmadığından <c>project_branches</c> FK sırasına konamaz.
///
/// <b>LEHÇE:</b> yalnız iki lehçede ortak sözdizimi (CREATE TABLE/INDEX, TEXT/BIGINT) kullanılır —
/// Migration066 (parties) ile aynı sınıf.
/// </summary>
public sealed class Migration073_Projects : IMigration
{
    public int Version => 73;
    public string Name => "projects";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE projects (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'active',    -- active | on_hold | completed (PK-C3)
    start_date BIGINT NULL,                   -- iş günü anlamı (ADR-162 ile tutarlı: plan tarihi, Unix ms)
    end_date BIGINT NULL,
    manager_personnel_id TEXT NULL,           -- sorumlu personel (vehicles.driver_personnel_id emsali)
    location TEXT NULL,                       -- konum / adres (serbest metin)
    description TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- fiziksel silme YOK (CLAUDE.md §4); Çöp Kutusu geri getirir
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (manager_personnel_id) REFERENCES personnel(id)
);
CREATE INDEX ix_projects_company ON projects(company_id, is_deleted);

-- Proje ↔ şantiye bağı. BUGÜN UI tek satır yazar (PK-C1 ilk sürüm); tablo çoklu bağa hazırdır.
-- company_id çocukta da tutulur (Migration062 deseni: tenant filtreleri JOIN'siz çalışsın).
CREATE TABLE project_branches (
    project_id TEXT NOT NULL,
    branch_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    PRIMARY KEY (project_id, branch_id),
    FOREIGN KEY (project_id) REFERENCES projects(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id),
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE INDEX ix_project_branches_branch ON project_branches(branch_id);
CREATE INDEX ix_project_branches_company ON project_branches(company_id);";
        cmd.ExecuteNonQuery();
    }
}
