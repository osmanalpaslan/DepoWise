using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// M-S1a — ÇOK-KİRACI (firma) İZOLASYONU: <c>material_request_items</c> ve <c>maintenance_materials</c>
/// tablolarına <c>company_id</c> eklenir (kullanıcı kararı 2026-08-09).
///
/// NEDEN: Bu iki çocuk tabloda firma kolonu yoktu. <see cref="Sync.BusinessSyncService"/> snapshot üretirken
/// firma filtresini YALNIZ company_id kolonu olan tablolara uygular; bu iki tablo filtresiz gidiyordu
/// → sunucuda ikinci bir firma olduğunda bir firmanın talep kalemleri / bakım malzemeleri diğer firmanın
/// masaüstüne akabilirdi. Kolon eklenince aynı kod otomatik olarak hem PUSH/PULL'da filtreler hem de
/// upsert'te oturumun firmasını ZORLAR (UpsertRow: <c>values["company_id"] = companyId</c>).
///
/// TASARIM KARARLARI
///  • <b>NOT NULL, DEFAULT YOK.</b> Varsayılan değer, ileride company_id atamayı unutan bir INSERT'i sessizce
///    yanlış/boş firmaya bağlardı — korunmak istenen hatanın ta kendisi. Bu yüzden varsayılan konmaz;
///    eksik atama INSERT'te hata verir (gürültülü ve erken).
///  • <b>Veri, gerçek üst kayıttan taşınır</b> (tahmin YOK):
///      material_request_items → material_requests.company_id
///      maintenance_materials  → vehicle_maintenances.company_id
///  • <b>Çözülemeyen satır varsa migration DURUR</b> (transaction geri alınır) ve hangi satırların neden
///    çözülemediğini söyler. Hiçbir satır varsayılan/rastgele firmaya bağlanmaz, hiçbir satır silinmez.
///    (Üst kaydı olmayan satır zaten FK ile imkânsız; yine de savunma amaçlı kontrol edilir.)
///  • <b>FK EKLENMEZ</b> (yumuşak referans) — Migration055'teki gerekçenin aynısı: üst kayıt zaten
///    companies'e FK'li olduğu için firma değeri şemasal olarak da güvenilir; ek FK yalnız kalıcı silme
///    (DialectPurge) ve kopyalama sırasında FK-sırası yükü getirirdi.
///  • <b>İndeks:</b> her iki tabloya company_id indeksi — snapshot/izolasyon sorgusu (WHERE company_id=@c)
///    bunun üzerinden çalışır.
///  • <b>Benzersizlik/CHECK:</b> gerekmiyor — bu satırların doğal bir benzersizlik kuralı yok.
///
/// LEHÇE (SQLite ↔ PostgreSQL): SON DURUM İKİSİNDE DE AYNI (NOT NULL + varsayılansız + indeks).
/// PostgreSQL: ADD COLUMN (nullable) → geri-doldur → SET NOT NULL.
/// SQLite: ADD COLUMN NOT NULL'u varsayılansız KABUL ETMEZ ve sonradan SET NOT NULL yoktur
///         → tablo standart yöntemle yeniden kurulur (yeni tablo + kopyala + eski tabloyu bırak + adlandır).
///         Bu iki tabloya BAŞKA tablodan FK yoktur, bu yüzden yeniden kurma güvenlidir.
///
/// TEKRAR ÇALIŞTIRMA: kolon zaten varsa hiçbir şey yapılmaz (idempotent). Zaten runner uygulanmış sürümü
/// tekrar çalıştırmaz; bu kontrol ikinci savunma hattıdır.
///
/// GERİ ALMA: migration tek transaction içindedir — hata hâlinde tamamı geri alınır. Uygulandıktan sonra
/// geri almak gerekirse (her iki veritabanında da çalışır):
///     ALTER TABLE material_request_items DROP COLUMN company_id;
///     ALTER TABLE maintenance_materials  DROP COLUMN company_id;
///     DELETE FROM schema_migrations WHERE version = 62;
/// Bu, migration'dan ÖNCEKİ şemayı birebir geri verir ve hiçbir iş kaydını silmez.
/// </summary>
public sealed class Migration062_ChildTableCompanyId : IMigration
{
    public int Version => 62;
    public string Name => "child_table_company_id";

    /// <summary>(çocuk tablo, üst tablo, çocuktaki üst anahtar) — firma bu zincirden taşınır.</summary>
    private static readonly (string Child, string Parent, string Fk)[] Targets =
    {
        ("material_request_items", "material_requests", "request_id"),
        ("maintenance_materials", "vehicle_maintenances", "maintenance_id"),
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        bool sqlite = SqlDialect.IsSqlite(conn);

        foreach (var (child, parent, fk) in Targets)
        {
            if (!DbIntrospect.TableExists(conn, tx, child)) continue;            // tablo yoksa atla
            if (DbIntrospect.ColumnExists(conn, tx, child, "company_id")) continue; // idempotent

            GuardResolvable(conn, tx, child, parent, fk);

            if (sqlite) RebuildSqlite(conn, tx, child, parent, fk);
            else AlterPostgres(conn, tx, child, parent, fk);

            Exec(conn, tx, $"CREATE INDEX IF NOT EXISTS ix_{child}_company ON {child}(company_id);");
        }
    }

    /// <summary>Taşınamayacak satır var mı? Varsa migration DURUR (tahmin yok, varsayılan yok).</summary>
    private static void GuardResolvable(DbConnection conn, DbTransaction tx, string child, string parent, string fk)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Üst kaydı bulunamayan VEYA üstünün firması boş olan satırlar
        cmd.CommandText = $@"
SELECT ch.id FROM {child} ch
LEFT JOIN {parent} p ON p.id = ch.{fk}
WHERE p.id IS NULL OR p.company_id IS NULL OR p.company_id = ''
LIMIT 20;";
        var bad = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) bad.Add(r.GetString(0));
        if (bad.Count == 0) return;

        throw new InvalidOperationException(
            $"M-S1a durduruldu: '{child}' tablosunda firması KESİN olarak belirlenemeyen satır var " +
            $"(üst kaydı '{parent}' bulunamıyor veya üstünün company_id'si boş). Bu satırlar tahminle " +
            $"taşınmaz. Önce bu satırlar düzeltilmeli. Örnek id'ler: {string.Join(", ", bad)}");
    }

    /// <summary>PostgreSQL: kolon ekle → üstten geri-doldur → NOT NULL yap.</summary>
    private static void AlterPostgres(DbConnection conn, DbTransaction tx, string child, string parent, string fk)
    {
        Exec(conn, tx, $"ALTER TABLE {child} ADD COLUMN company_id TEXT;");
        Exec(conn, tx, $@"
UPDATE {child} ch SET company_id = p.company_id
FROM {parent} p WHERE p.id = ch.{fk};");
        Exec(conn, tx, $"ALTER TABLE {child} ALTER COLUMN company_id SET NOT NULL;");
    }

    /// <summary>SQLite: NOT NULL'u varsayılansız eklemenin tek yolu tabloyu yeniden kurmaktır
    /// (SQLite'ın kendi önerdiği yöntem). Bu iki tabloya BAŞKA tablodan FK yok → güvenli.</summary>
    private static void RebuildSqlite(DbConnection conn, DbTransaction tx, string child, string parent, string fk)
    {
        var (createNew, copyCols) = SqliteRebuildPlan(child);
        Exec(conn, tx, createNew);
        Exec(conn, tx, $@"
INSERT INTO {child}__new ({copyCols}, company_id)
SELECT {string.Join(", ", copyCols.Split(',').Select(c => "ch." + c.Trim()))}, p.company_id
FROM {child} ch JOIN {parent} p ON p.id = ch.{fk};");
        Exec(conn, tx, $"DROP TABLE {child};");
        Exec(conn, tx, $"ALTER TABLE {child}__new RENAME TO {child};");
        // Eski indeks tablo ile birlikte düştü → yeniden kurulur (adı ve tanımı birebir aynı).
        Exec(conn, tx, $"CREATE INDEX IF NOT EXISTS ix_{child} ON {child}({fk});");
    }

    /// <summary>Yeniden kurulacak tablonun ŞEMASI — mevcut şemanın birebir aynısı + company_id NOT NULL.
    /// (Migration008/010/059'daki tanımlarla aynı; yalnız yeni kolon eklendi.)</summary>
    private static (string CreateNew, string CopyCols) SqliteRebuildPlan(string child) => child switch
    {
        "material_request_items" => (@"
CREATE TABLE material_request_items__new (
    id TEXT PRIMARY KEY,
    request_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL,
    vehicle_id TEXT NULL,
    note TEXT NULL,
    company_id TEXT NOT NULL,
    FOREIGN KEY (request_id) REFERENCES material_requests(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);", "id, request_id, material_id, quantity, vehicle_id, note"),

        "maintenance_materials" => (@"
CREATE TABLE maintenance_materials__new (
    id TEXT PRIMARY KEY,
    maintenance_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL,
    unit_price TEXT NULL,
    from_team_stock BIGINT NOT NULL DEFAULT 0,
    company_id TEXT NOT NULL,
    FOREIGN KEY (maintenance_id) REFERENCES vehicle_maintenances(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);", "id, maintenance_id, material_id, quantity, unit_price, from_team_stock"),

        _ => throw new InvalidOperationException("Bilinmeyen tablo: " + child),
    };

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
