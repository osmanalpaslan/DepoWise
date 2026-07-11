# DepoWise — PostgreSQL'e Geçiş Hazırlığı (Ölçek İçin)

> **Ne zaman?** Şimdi DEĞİL. Sunucu SQLite ile çalışıyor ve küçük/orta kullanım için yeterli. PostgreSQL,
> **aynı anda çok kullanıcı** (ör. 200-300 eşzamanlı) veya **birden çok sunucu makinesi** gerekince devreye girer.
> Maliyet: bkz. `MALIYET_KALEMLERI.md` #2 (ücretsiz katman var; ölçek için ücretli).
>
> **Neden PostgreSQL?** SQLite tek dosyadır ve **aynı anda tek yazıcıya** izin verir; yatay ölçeklenemez
> (tek makine). PostgreSQL gerçek eşzamanlı yazma + birden çok uygulama makinesi + okuma kopyaları sağlar.
>
> Son güncelleme: 2026-07-11 · Durum: PLAN (uygulanmadı)

## İyi haber: temel hazır
- **38 servis** veri erişimini soyut `IDbConnectionFactory` üzerinden yapıyor → PG için **yeni bir fabrika**
  eklemek çekirdek yaklaşımdır (servisleri tek tek elden geçirmek gerekmez).
- Web zaten ince istemci (API'ye sorar) → yalnız **sunucu** (API) veritabanı değişir.

## Yapılacaklar (port kapsamı)
1. **Bağlantı fabrikası:** `SqliteConnectionFactory` yanına `PostgresConnectionFactory` (Npgsql). Ortam
   değişkeniyle seçilir (`DEPOWISE_DB=postgres` → PG, yoksa SQLite). Dapper aynı kalır.
2. **SQLite'a özgü SQL'i uyarla** (~20 nokta):
   - `INSERT OR IGNORE` (8 dosya) → `INSERT ... ON CONFLICT DO NOTHING`.
   - `ON CONFLICT(x) DO UPDATE` (7) → PG'de büyük ölçüde aynı; `excluded.` sözdizimi uyumlu, gözden geçir.
   - `PRAGMA ...` (4) → PG'de yok, kaldır (WAL/journal SQLite'a özgü).
   - `VACUUM INTO` yedek (1) → PG yedeği farklı (`pg_dump`/sağlayıcı yedeği) — `BackupService` PG dalı.
   - Tip eşleme: para **TEXT+currency** (aynı kalabilir), zaman **Unix ms INTEGER** (aynı), boolean SQLite
     `INTEGER 0/1` → PG `boolean`/`smallint` (sorgularda `=1` yerine `=true` uyarlaması gerekebilir).
3. **Migration'lar:** 34 SQLite `IMigration` adımının PG karşılığı üretilir (aynı şema, PG tipleriyle). Not:
   `apps/web/drizzle` altındaki eski Drizzle SQL, terk edilmiş Next.js içindi — .NET şeması için yeni PG
   migration'ları gerekir (veya `MigrationRunner`'a PG-uyumlu SQL dalı).
4. **Senkron/yedek:** `BusinessSyncService` generic upsert PG'de de çalışır (ON CONFLICT uyarlaması ile);
   `stock_balances` recompute (2b) aynı mantık.
5. **Test:** Aynı test paketi (247) PG'ye karşı da koşturulur (bağlantı fabrikası testte PG'ye çevrilir).
   Ücretsiz katman (Neon/Supabase) ile geliştirme + CI'da test.

## Önerilen yol (ücretsiz geliştirme → ücretli üretim)
1. Geliştirme: ücretsiz PostgreSQL (Neon/Supabase free) ile portu yaz + testleri PG'de yeşile al.
2. Üretim: ölçek gerektiğinde ücretli PG + Fly.io'da daha büyük/çok makine (MALIYET_KALEMLERI #1, #2).
3. Geçiş: SQLite verisini PG'ye taşıyan tek seferlik aktarım scripti (satır bazlı kopya).

## Efor/risk
- **Efor:** birkaç gün (tüm veri erişimini etkilemez ama SQL uyarlaması + PG migration + test şart).
- **Risk:** orta-yüksek (veri katmanı). Bu yüzden **çalışan SQLite bozulmadan**, PG bir SEÇENEK olarak eklenir
  (ortam değişkeniyle). Ölçek kararı verilene kadar üretim SQLite'ta kalır.
