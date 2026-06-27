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

---

## Faz 04 kararları (2026-06-27)

### ADR-020 — Ortak UI mantığı platform-bağımsız
- **Karar:** Menü, doğrulama (tarih/numerik), çoklu seçim ve alan görünürlüğü saf mantık olarak iki tarafta da yazıldı (`Application/Ui/*` ve `apps/web/src/lib/ui/*`), aynı kabul senaryolarıyla test edildi. Avalonia/React yalnız bu mantığı bağlar.
- **Gerekçe:** Analiz §5; web ve masaüstü fonksiyonel eşitlik tek kaynaktan.

### ADR-021 — Tarih ve arama davranışı
- **Karar:** Tarih GG/AA/YYYY KESİN biçim + gerçek takvim doğrulaması (.NET `TryParseExact None`; web Date.UTC geri-doğrulama). Aranabilir çoklu seçim Türkçe büyük/küçük harf duyarsız (.NET tr-TR `CompareInfo`; web `toLocaleLowerCase('tr')`); arama seçimi korur; "tümünü seç" yalnız filtre sonucunu ekler.
- **Gerekçe:** Analiz §5; CLAUDE.md Türkçe duyarsız arama standardı.

### ADR-022 — Merkezi tema/branding (sabit değil)
- **Karar:** Renk ve marka metinleri ekrana sabit yazılmaz. `app_settings` (Migration003, global/firma override) → `ThemeTokens`/`BrandingSettings`. Masaüstü `ThemeApplier` ile `Brand.*` DynamicResource; web CSS değişkenleri (`--brand-*`) kök `:root`/layout'tan. Ayar değişiklikleri audit'lenir.
- **Gerekçe:** Kullanıcı talimatı + analiz §5 (tema merkezi yönetilebilir).

---

## Faz 05 kararları (2026-06-27)

### ADR-023 — Firma yönetimi yalnız Süper Admin; tenant fail-closed
- **Karar:** Firma oluşturma/listeleme `CompanyService` ile yalnız Süper Admin; Firma Admini yalnız kendi firmasını görür, `EnsureAccess` başka firmaya erişimi 403'ler. Tüm org servisleri `company_id`'yi session'dan alır.
- **Gerekçe:** Analiz §4; normal admin firma sınırını aşamaz.

### ADR-024 — Kullanıcı şube kapsamı (user_scopes)
- **Karar:** `user_scopes` ile kullanıcı bazlı şube kapsamı. `ScopeResolver`: açık scope öncelikli; yoksa admin → tüm firma şubeleri, admin-olmayan kapsamsız → boş. Şube/personel seçim listeleri ve yazma `EnsureBranchAllowed` ile kapsam dışına taşamaz. Web `lib/org/scope.ts` aynı kararı saf fonksiyonla aynalar.
- **Gerekçe:** Analiz §5/§6.2 (seçim listeleri yalnız kullanıcı kapsamını getirir).

---

## Faz 06 kararları (2026-06-27)

### ADR-025 — Para ve stok temsili
- **Karar:** Para/miktar SQLite'ta TEXT (invariant decimal) + `currency_code`; .NET `Money` ve web `money.ts` ile taşınır. Float YOK. Desteklenen: TRY (baz) / USD / EUR. İşlem anı kuru `stock_movements.fx_rate` snapshot; manuel kur `fx_rates`.
- **Gerekçe:** Analiz §7 (decimal + currency, kur snapshot).

### ADR-026 — Stok hareket defteri ana kaynak; açılış stoğu hareket olarak
- **Karar:** `stock_movements` ana kaynak, `stock_balances` cache (yalnız ledger'la aynı transaction'da güncellenir). Açılış stoğu kart alanı DEĞİL `OpeningStockService` ile 'opening' hareketi; `operation_id` ile idempotent. Doğrudan bakiye set eden API yok.
- **Gerekçe:** Analiz §7/§2; bu fazda bakiye doğrudan değiştirilmez (Faz 07 diğer hareket tipleri).

### ADR-027 — Muadil ve uyumlu araç ilişkileri
- **Karar:** Muadil simetrik (servis çift yön yazar) + self-FK CHECK + döngü güvenli BFS grup çözümü. Uyumlu araç çoklu seçim `material_compatible_vehicles` (vehicle_id FK Faz 08'e ertelendi). Araç→uyumlu malzeme sorgusu güncel stoğu (stock_balances join) gösterir.
- **Gerekçe:** Analiz §6.5; çift yönlü, döngü güvenli ilişki.

---

## Faz 07 kararları (2026-06-27)

### ADR-028 — Stok işlemleri concurrency: IMMEDIATE transaction
- **Karar:** Tüm bakiye değiştiren akışlar `BeginTransaction(deferred: false)` (BEGIN IMMEDIATE) ile yazma kilidini baştan alır → eş zamanlı çıkışlar serialize olur; ikinci işlem güncel bakiyeyi okuyup negatif guard'a takılır. Negatif düşüş `NegativeStockException` + rollback.
- **Alternatif:** Koşullu UPDATE (quantity TEXT karşılaştırması zor) — reddedildi. IMMEDIATE + busy_timeout yeterli ve sade.
- **Kanıt:** `EsZamanli_IkiCikis_NegatifStokOlusturamaz` (Parallel.For).

### ADR-029 — Belge/hareket modeli ve iptal = ters kayıt
- **Karar:** `stock_documents` (in/out/transfer/count) + hareketler belgeye bağlı; doc_no otomatik (PREFIX-YYYY-NNNN). Transfer kaynak çıkış + hedef giriş aynı group_id'de atomik. İptal hareketi FİZİKSEL SİLMEZ: ters hareket üretir, orijinali is_reversed=1 işaretler, belge cancelled. operation_id ile tüm akışlar idempotent.
- **Gerekçe:** Analiz §7 (silme yerine ters kayıt, idempotency, transaction).

### ADR-030 — Bakiye material-global (şube bazlı ertelendi)
- **Karar:** `stock_balances` material düzeyinde tek bakiye; transfer toplam stoğu değiştirmez (net-zero), hareketlerde from/to şube kayıtlı. Şube bazlı bakiye/negatif kontrolü sonraki bir fazda eklenecek (R13).
- **Gerekçe:** Faz 06 şemasını bozmadan ilerlemek; MVP için yeterli, kayıt izi şube bilgisini taşıyor.

---

## Faz 08 kararları (2026-06-27)

### ADR-031 — Sayaç geriye gitmeme + iki yöntem
- **Karar:** `MeterRule` ortak (web+masaüstü). `SetMeter` (doğrudan form düzenleme) geriye gidişi `MeterBackwardException` ile reddeder. `AdvanceMeter` (bakım/yakıt) ileri-only: yeni>mevcut ise ilerletir+loglar, değilse no-op (geçmiş tarihli düşük okumayı ENGELLEMEZ). Her ilerleme `vehicle_meter_logs`'a (old,new,source) yazılır. Güncellemeler IMMEDIATE transaction.
- **Gerekçe:** Analiz §7; kullanıcı talimatı "sayaç geriye düşmesin + tüm değişimler loglansın".

### ADR-032 — Şablondan doldurma (kullanıcı değeri öncelikli) + malzeme kopyalama
- **Karar:** Araç oluştururken `TemplateId` varsa boş alanlar şablondan doldurulur (`?? ` ile; kullanıcı girdisi ezilmez). Şablonun uyumlu malzemeleri yeni aracın `material_compatible_vehicles` kayıtlarına AYNI transaction'da kopyalanır (INSERT OR IGNORE). Otomatik iç kod önek+en büyük no+1 (genişlik korunur).
- **Gerekçe:** Analiz §6.7; AlpDepo deseni, kontrollü doldurma.

---

## Faz 09 kararları (2026-06-27)

### ADR-033 — Bakım atomik akışı + tek stok düşümü
- **Karar:** `MaintenanceService.Save` IMMEDIATE transaction'da: bakım kaydı + her malzeme için TEK 'usage' hareketi (negatif guard, fiyat snapshot `maintenance_materials.unit_price`) + sayaç ileri (AdvanceMeter mantığı) + sonraki hedef + audit. operation_id idempotent (ikinci çağrı çift düşmez). İptal: 'usage_reverse' +1 ile stok geri, kayıt is_cancelled (fiziksel silme yok), idempotent.
- **Gerekçe:** Analiz §7 (tek transaction, tek düşüm, ters kayıt, idempotency).

### ADR-034 — Uyarı eşikleri ve döngü
- **Karar:** `AlertRules` (web+masaüstü): progress=tüketilen/interval; <0.85 Normal, [0.85,0.95) Approaching, [0.95,1.0) Critical, ≥1.0 Overdue. Tüketilen km/saat = current_meter − performed; gün = now − performed_date. Uyarı her (araç,tanım) için EN SON non-cancelled bakımdan hesaplanır → yeni bakım girilince otomatik temizlenir.
- **Gerekçe:** Kullanıcı talimatı + analiz §6.8.

---

## Faz 10 kararları (2026-06-27)

### ADR-035 — Yakıt dağıtımı atomik + fiyat snapshot
- **Karar:** `FuelService.Distribute` IMMEDIATE transaction'da: depo bakiye yeterlilik kontrolü + dağıtım (birim fiyat **snapshot**; verilmezse güncel=son depo fiyatı) + araç sayacı ileri (MeterRule) + meter log + audit; operation_id idempotent. Depo bakiyesi = Σgiriş − Σdağıtım (tüm zamanlar). Güncel fiyat değişimi geçmiş dağıtımları ETKİLEMEZ.
- **Gerekçe:** Analiz §7 (tarihsel maliyet snapshot, sayaç bütünlüğü, transaction).

### ADR-036 — Günlük Faaliyet bakım = tek kayıt (çift düşüm yok)
- **Karar:** `DailyActivityService.SaveMaintenanceActivity` ortak `MaintenanceService.Save`'i çağırır (tek `vehicle_maintenances` + tek stok düşümü). `daily_activities` yalnız `maintenance_id` referansı + `stock_processed=1` tutar; burada stok DÜŞMEZ. Böylece kayıt hem Bakım Takibi hem Günlük Faaliyet ekranında görünür, veri tek.
- **Gerekçe:** Kullanıcı talimatı + analiz §6.11 (tek kayıt prensibi).

---

## Faz 11 kararları (2026-06-27)

### ADR-037 — Talep durum makinesi + onay stok düşürmez
- **Karar:** `RequestStatusMachine` (web+masaüstü) geçişleri kısıtlar: draft→pending→approved/rejected/cancelled; approved/rejected/cancelled terminal. Çift onay/yetkisiz/geçersiz geçiş fail-closed. Onay/ret approve butonu + requests edit yetkisi ister; tenant ownership zorunlu. **Onay stok bakiyesini DEĞİŞTİRMEZ.** Stok yalnız `CreateIssueFromRequest` ile (onaylı talep → açık `StockService.IssueOut`). Belge no TLP-YYYY-NNNN tenant/yıl benzersiz.
- **Gerekçe:** Analiz §6.12/§7; kullanıcı talimatı (onay stok düşürmez, stok yalnız gerçek çıkış/teslim).

### ADR-038 — PDF üretimi (QuestPDF)
- **Karar:** Masaüstü/Infrastructure PDF QuestPDF Community ile (`IRequestPdfService`/`RequestPdfService`), `RequestPdfModel` ortak veri modeli; Türkçe karakter korunur. Web tarafı aynı modeli kullanır; binary render hattı sonraya bırakıldı (R16).
- **Gerekçe:** Analiz §6.12 (PDF çıktısı); .NET'te yerleşik, lisans Community.

---

## Faz 12 kararları (2026-06-27)

### ADR-039 — Rapor kapısı + tenant/firma filtresi
- **Karar:** `ReportGate.EnsureRunnable` ağır raporu `Executed=false` iken çalıştırmaz (kullanıcı Sorgula/Filtrele'de Executed=true yapar). Raporlar tenant + "reports" permission fail-closed. Firma filtresi yalnız Süper Admin'e görünür (`ShowCompanyFilter`); hedef firma `TenantAccessGuard.ResolveCompanyId` ile çözülür (normal admin başka firma isteyemez). Web `lib/reports/gate.ts` aynı.
- **Gerekçe:** Analiz §6.14/§7 (ağır rapor manuel tetik, tenant sızıntısı yok).

### ADR-040 — Excel export (ClosedXML) + import dry-run politikası
- **Karar:** `TableModel` → `.xlsx` ClosedXML ile (sayısal hücreler sayı). İçe aktarım: örnek başlık + ön kontrol + **dry-run (DB'ye yazmaz)** + satır bazlı hata (ilk 15) + commit. Politika: **satır bazlı** (bir hatalı satır diğerlerini bozmaz), commit `MaterialService.Create` ile iş kurallarını atlamaz (tenant/permission/kod benzersiz/currency). Web `lib/reports/import.ts` aynı doğrulama.
- **Gerekçe:** Analiz §6.15; kullanıcı talimatı (örnek dosya + ön kontrol + satır hata + dry-run).

---

## Faz 13 kararları (2026-06-27)

### ADR-041 — Dosya güvenliği + ayrık dosya kaydı (base64 yok)
- **Karar:** `FileValidation` ortak: ≤7MB, izinli MIME (jpeg/png), **magic-byte** ile gerçek tip (uzantı/declared MIME'a güvenmez; sahte içerik + MIME-içerik uyuşmazlığı reddi), güvenli ad. Fotoğraflar `IFileStorageProvider` (yerel disk; swappable) ile saklanır; operasyonel tabloya **base64 yazılmaz** — yalnız `file_records` metadata (provider/key/mime/size/sha256). Storage kök içine sınırlı (path traversal koruması). Web `lib/files/validation.ts` aynı.
- **Gerekçe:** Analiz §6.16/§9.

### ADR-042 — Çöp Kutusu + yedekleme
- **Karar:** `TrashService` yalnız master-data soft-delete kayıtlarını listeler/geri yükler; özel buton (RestoreTrash) + **yeniden doğrulama (reauth)** + tenant fail-closed. Operasyonel kayıtlar çöp kutusunda DEĞİL (iptal/ters kayıt). `BackupService`: `VACUUM INTO` tutarlı yedek, 30 gün retention, `PRAGMA integrity_check`, geri yükleme admin+reauth ve `SqliteConnection.ClearAllPools()` ile dosya kilidi olmadan.
- **Gerekçe:** Analiz §6.17-6.18/§9; gerçek geri yükleme + bütünlük kanıtı.

---

## Faz 14 kararları (2026-06-27)

### ADR-043 — Offline write + outbox atomik; idempotent push
- **Karar:** Yerel write ve `sync_outbox` AYNI SQLite transaction (`OutboxWriter.Enqueue`); operation_id + payload_hash + base_version taşınır; rollback hiçbirini bırakmaz. Push'ta operation_id `sync_inbox` ile idempotent (ikinci ulaşım → already_applied; çift kayıt yok). Offline veri yeniden açılışta kalıcı.
- **Gerekçe:** Analiz §8 (yerel+outbox tek transaction, idempotent retry).

### ADR-044 — Kritik işlemlerde LWW yasak; sunucu otoriteli + conflict
- **Karar:** Kritik entity'lerde (stok/sayaç/yakıt/bakım/onay) basit LWW YOK: sunucu doğrulaması zorunlu (validator yoksa/red ise rejected + `sync_conflicts`). Düşük-riskli kart alanlarında base_version uyuşmazlığı → conflict (kör overwrite yok). Pull seq cursor; bozuk sayfada rollback + cursor sabit. Cihaz: tek-kullanımlık 10 dk enrollment anahtarı + master onay + token (hash saklı); pending/revoked cihaz push/pull'da 403.
- **Gerekçe:** Analiz §8-9; kullanıcı talimatı (LWW yok, operation_id + sunucu doğrulaması zorunlu).
