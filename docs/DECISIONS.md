# DECISIONS

## ADR-000 - V6 başlangıç kararları
- Web: Next.js + TypeScript strict + Drizzle + PostgreSQL.
- Masaüstü: .NET 8 + Avalonia + MVVM + Dapper + SQLite.
- Web çevrimiçi merkez; masaüstü offline-first.
- Stok hareket defteri ana kaynak; kritik operasyonlarda LWW kullanılmaz.
- Fotoğraf için file_records + storage provider; DB base64 varsayılan değildir.
- Geliştirme makinesinde dotnet host ve mutlak LocalAppData DB yolu zorunludur.

Fazlar ilerledikçe yeni kararlar tarih, bağlam, karar, alternatifler ve sonuç formatında eklenir.

---

## Faz 00 kararları (2026-06-26)

### ADR-001 — Çözüm/klasör düzeni
- **Bağlam:** Boş repo; web + masaüstü + ortak sözleşme bir arada.
- **Karar:** `src/DepoWise.Desktop` (Avalonia UI), `src/DepoWise.*` katman projeleri (Domain/Application/Infrastructure), `web/` (Next.js), `docs/`, `artifacts/`. Tek `.sln` masaüstü tarafını toplar.
- **Alternatif:** Tek monolit proje — reddedildi (test izolasyonu ve katman ayrımı zorlaşır).
- **Sonuç:** Faz 01'de iskelet bu düzene göre kurulacak.

### ADR-002 — Masaüstü mimarisi
- **Karar:** .NET 8, Avalonia, MVVM (CommunityToolkit.Mvvm), Dapper, SQLite. UI thread'de DB/ağ yok; Dapper parametreli; transaction tek connection üzerinde.
- **Gerekçe:** Analiz §3 ve `.claude/rules/desktop.md` ile birebir.

### ADR-003 — Yerel DB yolu ve bağlantısı
- **Karar:** SQLite mutlak yol `%LOCALAPPDATA%\DepoWise\Data\<environment>\depowise.db`. Connection: `Cache=Private`, WAL, `foreign_keys=ON`, `busy_timeout=5000`. Açılışta host/DB-yolu/journal_mode/health loglanır.
- **Gerekçe:** COMODO sandbox'ın sanal-DB tuzağını önler (relative path yasak).

### ADR-004 — COMODO güvenli çalıştırma
- **Karar:** Debug'da `UseAppHost=false`. Uygulama yalnız `dotnet build` + `dotnet run/--project` veya `dotnet <dll>` ile çalışır. Proje `.exe`/`.bat` ASLA çalıştırılmaz; PreToolUse hook bunu zorlar.
- **Sonuç:** Doğrulandı (hook + Directory.Build.props mevcut ve tutarlı).

### ADR-005 — Merkezi veri ve API
- **Karar:** PostgreSQL + Drizzle + migration; API `/api/v1`, ortak hata modeli + correlation id + OpenAPI sözleşmesi. `company_id` yalnız server session'dan; payload'dan tenant kabul edilmez (fail-closed).
- **Not:** Üretim PG sağlayıcısı tek markaya bağlanmaz (KNOWN_ISSUES).

### ADR-006 — Kritik operasyon bütünlüğü
- **Karar:** Stok/sayaç/yakıt/bakım/onay işlemlerinde LWW yasak; `operation_id` ile idempotency + transaction + audit/outbox tek transaction. Operasyonel kayıt fiziksel silinmez (iptal/ters kayıt). Stok hareket defteri tek doğru kaynak.
- **Gerekçe:** Analiz §7 ve §11 kabul testleri.

### ADR-007 — Para, zaman, kimlik, dosya
- **Karar:** Para `decimal` + `currency_code`, kur snapshot; zaman merkezi UTC / sözleşmede Unix ms; ana kayıtlar UUID/ULID, kullanıcı belge no ayrı; fotoğraf `file_records` metadata + storage provider (DB base64 değil).
- **Gerekçe:** Analiz §7, §6.16.

---

## Faz 01 kararları (2026-06-26)

### ADR-008 — Çözüm yerleşimi ve hedef framework
- **Karar:** `src/DepoWise.{Domain,Application,Infrastructure,Desktop}` + `tests/DepoWise.Tests` + `apps/web`. Tüm .NET projeleri **net8.0** (Avalonia template'in ürettiği net10.0 hedefi düşürüldü; SDK 8.0.422).
- **Gerekçe:** CLAUDE.md .NET 8 değişmezi; katmanlı bağımlılık Domain←Application←Infrastructure←Desktop/Tests.

### ADR-009 — Ortak sözleşmelerin iki platformda eşlenmesi
- **Karar:** Hata modeli (`ApiError`+`ErrorCodes`), keyset pagination (`PageRequest`/`PagedResult`), zaman (UTC + Unix ms) ve correlation_id hem .NET (`Application/Common`) hem web (`lib/contracts.ts`) tarafında **birebir aynı kodlar/biçimle** tanımlandı. OpenAPI bu sözleşmeyi `apps/web/docs/openapi.yaml`'de belgeliyor.
- **Gerekçe:** Analiz §3/§5 fonksiyonel eşitlik; tek doğru sözleşme.

### ADR-010 — Config fail-closed
- **Karar:** Web `loadConfig()` zod ile doğrular; **Production**'da `DATABASE_URL`/`SESSION_SECRET` eksikse `ok=false` (health 503). Geliştirmede uyarı niteliğinde. Sırlar yalnız environment'tan.
- **Gerekçe:** Analiz §9 (başlangıçta eksik/zayıf sır fail-closed).

### ADR-011 — Güvenlik yükseltmesi (tedarik zinciri)
- **Bağlam:** `next@15.1.6` CVE-2025-66478 açığı içeriyordu.
- **Karar:** Yamalı `next@^15.5.19`'a yükseltildi (eslint-config-next eşlendi). "Gereksiz yükseltme yapma" kuralının istisnası: kritik güvenlik açığı (analiz §9 tedarik zinciri).
- **Sonuç:** Yükseltme sonrası typecheck/build yeşil.

---

## Faz 02 kararları (2026-06-26)

### ADR-012 — Migration stratejisi
- **Karar:** Yerel SQLite için kod tabanlı sürümlü migration (`IMigration`/`MigrationRunner`, `schema_migrations` izleme tablosu, her migration tek transaction, idempotent). Merkezi PostgreSQL için Drizzle Kit ile üretilen SQL migration dosyaları (`apps/web/drizzle`).
- **Gerekçe:** İki platform farklı motorlar; ortak şema kavramı korunur, her motor kendi migration aracını kullanır.

### ADR-013 — Standart kolon sözleşmesi
- **Karar:** Tüm operasyonel tablolar `id` (UUID/ULID, TEXT/text), `company_id`, `created_at`/`updated_at` (INTEGER/bigint Unix ms), `version` (optimistic concurrency), uygun olduğunda `is_deleted`. Para alanları decimal-as-TEXT (SQLite) / numeric (PG) + `currency_code`.
- **Gerekçe:** Analiz §7; tenant + soft-delete + concurrency + zaman tutarlılığı tek desende.

### ADR-014 — Tenant izolasyonu fail-closed
- **Karar:** `company_id` `TenantContext`/`TenantGuard` ile yalnız güvenilir bağlamdan; boşsa exception. Tüm okuma/yazma sorguları `TenantSql.ScopePredicate` kullanır. Regresyon: tenant izolasyon + başka-firma-silemez testleri.
- **Gerekçe:** Analiz §9; tenant kontrolü UI'a bırakılmaz.

### ADR-015 — Keyset pagination + soft-delete + audit
- **Karar:** Sayfalama keyset (created_at DESC, id DESC) + opak `Cursor`; toplam sayı zorunlu değil. Silme = `is_deleted=1` + version+1 (fiziksel silme yok). Kritik mutasyonlar `AuditWriter` ile aynı transaction'da audit yazar.
- **Gerekçe:** Analiz §7 (keyset kararlı sıralama), §2/§7 (silme yerine soft-delete/ters kayıt), §9 (audit).

---

## Faz 03 kararları (2026-06-26)

### ADR-016 — Parola hash algoritması (parite)
- **Karar:** PBKDF2-HMAC-SHA256, 100k iter, 16B salt, 32B hash; biçim `pbkdf2$sha256$<iter>$<saltB64>$<hashB64>`. Hem .NET (`Rfc2898DeriveBytes.Pbkdf2`) hem web (`node:crypto.pbkdf2`) aynı biçim → enroll/sync sırasında karşılıklı doğrulanabilir.
- **Alternatif:** BCrypt — reddedildi (iki platformda harici bağımlılık + parite zorluğu); PBKDF2 her iki runtime'da yerleşik.
- **Sonuç:** Parite testle doğrulandı (.NET + node:test).

### ADR-017 — Deny-by-default erişim kontrolü
- **Karar:** `AccessControl` UI ve API'de aynı sonucu üretir; izin kaydı yoksa erişim yok. Süper Admin/Firma Admini bypass. Dashboard/About herkese açık (yalnız okuma). Özel buton/alan da deny-by-default. API sınırında `Require*` → `ForbiddenException` (403).
- **Gerekçe:** Analiz §5/§9; yetki yalnız UI'a bırakılmaz.

### ADR-018 — Tenant kaynağı ve yetki yükseltme koruması
- **Karar:** `company_id` yalnız `SessionContext`'ten; istek payload'ındaki farklı company_id (süper admin değilse) 403. Firma Admini firma değiştiremez (foreign company → reddedilir, sessizce rescope EDİLMEZ). `RoleAssignmentGuard`: admin olmayan admin/süper-admin rolü atayamaz; süper admin yalnız süper admin tarafından oluşturulur.
- **Gerekçe:** Analiz §4/§9; tenant sızıntısı ve privilege escalation fail-closed.

### ADR-019 — Web içi TS import uzantıları (.ts)
- **Karar:** `lib/security` içi göreli importlar `.ts` uzantılı + `allowImportingTsExtensions`. Böylece aynı kaynak hem Next bundler ile derlenir hem de `node --test` (Node 24 type-stripping) ile harici test koşusunda çalışır.
- **Gerekçe:** Web için hafif birim test koşusu (ek bağımlılık olmadan) sağlanır.
