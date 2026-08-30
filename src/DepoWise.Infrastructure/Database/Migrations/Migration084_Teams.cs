using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187, 2026-08-30) — EKİP TANIMI ═══
///
/// Kararlar: PK-EK-07=B (ekip yetkisi **mevcut `users` modülünde** — yeni `teams` yetki modülü YOK) ·
/// İK-1=Evet (bir kullanıcı **birden fazla** ekipte olabilir → çoka-çok) · İK-8=Firma bazlı
/// (**`branch_id` YOK**; tenant sınırı yalnız `company_id`).
///
/// <b>ÖNEMLİ AYRIM (ADR-187 §3/§5):</b> ekipler ORGANİZASYONEL GRUPLAMADIR. Onay zincirinin kaynağı
/// ekip DEĞİL, <b>kullanıcı hiyerarşisidir</b>. Ekip lideri otomatik onaycı DEĞİLDİR; yalnız kendisine
/// düşen bir onay adımını onaylayabilir. Bu yüzden burada onay/approver kavramı YOKTUR — onay yapıları
/// ALT FAZ 2'de ayrı migration ile gelir (`user_hierarchy`, `approval_instance`, `approval_step`).
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız İKİ yeni tablo (CREATE). Mevcut hiçbir tabloya <c>ALTER</c> YOK —
/// özellikle <b>`users` tablosuna dokunulmaz</b> (PK-EK-02: hiyerarşi sütunu eklenmez). <b>Backfill YOK</b>,
/// veri dönüşümü YOK. Runner migration'ı tek transaction'da çalıştırır → hata olursa şema 83'te kalır.
///
/// <b>Senkron:</b> bu tablolar <c>BusinessSyncService.Tables</c>'a EKLENMEZ. Ekip verisi masaüstüne
/// <c>/api/lookups/sync</c> **sunucu-otoriteli aynası** ile iner (masaüstü asla yazmaz → LWW sorusu
/// doğmaz; duyuru/şube deseniyle aynı gerekçe).
/// </summary>
public sealed class Migration084_Teams : IMigration
{
    public int Version => 84;
    public string Name => "teams";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE teams (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,                 -- tenant sınırı (İK-8: FİRMA bazlı; branch_id YOK)
    name TEXT NOT NULL,
    lead_user_id TEXT NULL,                   -- ekip yöneticisi; ÜYELİĞİ serviste doğrulanır (ADR-187)
    is_active BIGINT NOT NULL DEFAULT 1,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- yumuşak silme (Çöp Kutusu standardı; fiziksel silme yok)
    FOREIGN KEY (company_id) REFERENCES companies(id)
    -- lead_user_id'ye FK YOK: `users` masaüstüne SENKRONLANMAZ ve aynada da yoktur (yerel users
    -- tablosuna hiçbir yazım yok). FK verilseydi ayna masaüstüne inerken foreign_keys=ON altında
    -- FK ihlaliyle kırardı. Bütünlük SUNUCU servis katmanında zorlanır (`users` orada otoritedir).
    -- Migration081/083 ile aynı içtihat: FK yalnız companies.
);
CREATE INDEX ix_teams_company ON teams(company_id, is_deleted);

CREATE TABLE team_members (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,                 -- tenant sınırı (ebeveynle aynı firma; serviste zorlanır)
    team_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    is_lead BIGINT NOT NULL DEFAULT 0,        -- ekip yöneticisi işareti (teams.lead_user_id ile tutarlı tutulur)
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (team_id) REFERENCES teams(id)
    -- user_id'ye FK YOK — gerekçe yukarıdaki `teams.lead_user_id` ile aynıdır.
);
CREATE INDEX ix_team_members_team ON team_members(team_id, is_deleted);
CREATE INDEX ix_team_members_user ON team_members(company_id, user_id, is_deleted);
-- İK-1: kullanıcı BİRDEN FAZLA ekipte olabilir; ancak AYNI ekibe AKTİF olarak iki kez eklenemez.
-- Kısmi indeks (is_deleted=0) sayesinde silinip yeniden eklenebilir — iki lehçede de desteklenir.
CREATE UNIQUE INDEX ux_team_members_active ON team_members(team_id, user_id) WHERE is_deleted = 0;";
        cmd.ExecuteNonQuery();
    }
}
