using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ ARA İŞ 4 (ADR-186, 2026-08-29) — CUSTOM RAPOR TANIM TABLOSU ═══
///
/// Kararlar: PK-CR-01=A (ham SQL YOK — tanım yalnız beyaz-listeli ANAHTARLAR taşır) ·
/// PK-CR-02=A (tanımlar senkronlanır → masaüstü ÇEVRİMDIŞI de custom rapor çalıştırır) ·
/// PK-CR-04=A (yetki anahtarı DİNAMİKTİR: <c>user_permissions.module_key</c> serbest metin olduğu için
/// yetki tarafında MIGRATION GEREKMEZ — burada yetki şeması DEĞİŞTİRİLMEZ).
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız TEK yeni tablo (CREATE); mevcut hiçbir tabloya ALTER dahi YOK;
/// <b>backfill YOK</b>, veri dönüşümü YOK. FK yalnız <c>companies</c> — duyuru deseni (Migration081)
/// ile aynı: senkron FK sıra bağımlılığı doğurmaz. Rollback: tek DROP + schema_migrations satırı.
/// Runner migration'ı tek transaction'da çalıştırır → hata olursa şema 82'de kalır.
///
/// <b>Neden `source_key`/`columns_json`/`filters_json` metin:</b> tanım bir SORGU DEĞİL, SEÇİM
/// listesidir. SQL üretimi çalışma anında <c>CustomReportSources</c> beyaz listesinden yapılır;
/// bu sütunlarda saklanan değerler SQL'e ASLA doğrudan yazılmaz (yalnız anahtar eşleştirmesi).
/// İki lehçede de düz TEXT kullanılır (PostgreSQL'e özgü JSON tipi KULLANILMAZ — SQLite paritesi).
/// </summary>
public sealed class Migration083_CustomReports : IMigration
{
    public int Version => 83;
    public string Name => "custom_reports";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE custom_report_defs (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,                 -- tenant kapsamı (senkron ve sorgu süzgeci buradan)
    name TEXT NOT NULL,                       -- kullanıcıya görünen rapor adı
    source_key TEXT NOT NULL,                 -- CustomReportSources beyaz listesindeki kaynak anahtarı
    columns_json TEXT NOT NULL,               -- seçilen KOLON ANAHTARLARI (sıralı) — SQL parçası DEĞİL
    filters_json TEXT NULL,                   -- filtre (kolon anahtarı + aranan metin) — SQL parçası DEĞİL
    sort_column TEXT NULL,                    -- sıralama KOLON ANAHTARI (beyaz listede doğrulanır)
    sort_desc BIGINT NOT NULL DEFAULT 0,
    is_active BIGINT NOT NULL DEFAULT 1,
    created_by TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- yumuşak silme (Çöp Kutusu standardı; fiziksel silme yok)
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE INDEX ix_crd_company ON custom_report_defs(company_id, is_deleted);";
        cmd.ExecuteNonQuery();
    }
}
