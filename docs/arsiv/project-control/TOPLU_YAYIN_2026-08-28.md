# TOPLU YAYIN — 2026-08-28 · C,A,E,B,D,P,F,H,I,J,K,L (12 iş · ADR-164..175)

> Kullanıcı onayı: "Migration073..081'in production'a alınmasını onaylıyorum." (2026-08-28)
> Sonuç: **BAŞARILI** — API **v173→v174** · Web **v198→v199** · Masaüstü **1.0.159→1.0.160** · Şema **72→81**.

## 1. Deploy öncesi (salt-okunur)

- `pg_dump -Fc` yedeği alındı ve doğrulandı: `D:\AlpnexYedek\depowise_prod_2026-08-28_200658.dump`
  (620.377 bayt · `pg_restore -l` 418 nesne satırı). Araç: taşınabilir PostgreSQL 17.2 istemcisi
  (sunucu: PostgreSQL 17.11, Neon `depowise_prod`).
- **ÖN sayım/karma:** 77 public tablonun her biri için satır sayısı + satır-içerik md5 karması alındı
  (`ORDER BY t::text` deterministik). Örnek canlı sayılar: materials 2492 · stock_movements 683 ·
  vehicles 160 · personnel 64 · branches 10 · companies 3 · users 9 · schema_migrations 72.

## 2. Deploy ve migration

- Sıra DEPLOYMENT.md'ye göre: **API → Web → masaüstü paketi.** Migration'lar API açılışında
  MigrationRunner ile otomatik koştu (her biri tek transaction, idempotent).
- Doğrulama: `MAX(version)=81`; uygulananlar tam liste **73,74,75,76,77,78,79,80,81** ✅.

## 3. Deploy sonrası — BİT-BİT KANIT

Aynı sayım/karma seti yeniden alındı ve ÖN ile karşılaştırıldı:

| Kontrol | Sonuç |
|---|---|
| Kayıp tablo | **YOK** (77 tablonun 77'si yerinde) |
| Yeni tablo | **15** — beklenen tam liste (projects, project_branches, equipment, equipment_types, assignment_movements, cost_centers, cost_center_links, purchase_orders, purchase_order_lines, work_orders, work_order_assignments, work_order_links, work_order_status_history, calendar_events, announcements) ve **HEPSİ BOŞ** |
| İçeriği değişen tablo | **YALNIZ `schema_migrations`** (72→81 satır — beklenen TEK fark) |
| Mevcut 76 tablonun satır-içerik karması | **BİT-BİT AYNI** → hiçbir canlı kayıt değişmedi/silinmedi; migration'lar yalnız beklenen şema eklemelerini yaptı |
| Yetki/rol tabloları | Karma aynı → **hiçbir role yeni yetki otomatik AÇILMADI** |

Rollback/başarısızlık güvencesi: MigrationRunner migration başına tek transaction (hata → o migration
tamamen geri alınır, şema eski sürümde kalır) + idempotent (yeniden başlatma zararsız — deploy'da makine
yeniden başladı, çift uygulama olmadı); ayrıca deploy öncesi doğrulanmış pg_dump yedeği eldedir.

## 4. Yayın sonrası sağlık (salt-okunur)

| Kontrol | Sonuç |
|---|---|
| `GET /health` | ✅ `{"status":"ok"}` |
| PG gerçekten bağlı (boş SQLite'a düşmedi) | ✅ `/api/public/companies` gerçek firmayı döndürdü |
| Yeni uçlar canlıda | ✅ calendar/announcements/search/alerts-count/work-orders/purchasing/equipment/cost-centers/assignments-holdings — auth'suz **401**, test oturumuyla **200** (yeni tablolar boş → boş diziler) |
| Web rotaları | ✅ `/` ve `/login` 200; loglarda bugünkü hata yok |
| Masaüstü sürüm ucu | ✅ `latest = 1.0.160`; checksum `EA688F2F…59CAE2` sunucu kaydı = yerel zip SHA-256 (86,1 MB) |
| `/data` diski | ✅ %40 (360 MB / 974 MB) — eski paketler otomatik budanıyor (en yeni 3) |
| Canlı senkron sözleşmesi (`tools/qa/live-sync-check.mjs`, TEST hesabı) | 6/7 ✅ — token'sız 401 · giriş · tenant · version · tam snapshot (562 satır) · **başka firma verisi SIZMIYOR** |

### Tek KALDI — yayından bağımsız, önceden var olan davranış
"Delta (since=version) boş döner" kontrolü 22 satır gördü. Kök neden teşhis edildi: satırların tamamı
`material_compatible_vehicles` — bu ESKİ tabloda zaman damgası kolonu YOK → delta filtresi ona hiç
uygulanamıyor (kod: stamp null ise filtresiz) ve tablo her deltada tam iniyor; sürüm hesabına da
girmiyor. Deterministik (art arda koşularda aynı 22 satır, version sabit) → canlı aktivite/yayın etkisi
DEĞİL. Bu turda eklenen 15 tablonun HEPSİ damgalıdır. Kayda geçirildi (KNOWN_ISSUES — SNK ailesi);
bu görev kapsamında değiştirilmedi.

## 5. Yeni yetkiler — kontrollü açılış BEKLİYOR (kullanıcı işi)

Aşağıdaki modüller **deny-by-default kapalı** yayınlandı ve hiçbir role otomatik açılmadı; Yetkiler
ekranından ilgili rollere açılmalıdır: **Ekipman (`equipment`) · Zimmet (`assignments`) · Maliyet
Merkezi (`cost_centers`) · Satın Alma (`purchasing`) · İş Emirleri (`work_orders`) · Takvim
(`calendar`) · Duyurular — YAZMA (`announcements`; okuma herkese açık)**. Notlar: Projeler `branches`
yetkisini kullanır (yeni yetki yok) · Evrak `files` mevcut · zimmet/tüketim/mal kabulde STOK yetkisi
de gerekir · Uyarılar/çan/arama yetki istemez (içerik kaynak yetkisinden süzülür).

## 6. Sürüm uyumu / senkron notu

Masaüstü 1.0.159 istemciler yeni API ile UYUMLU (uçlar yalnız eklemeli değişti; istemci, senkronda
KENDİ tablo listesini işler → sunucudaki yeni tabloları güvenle yok sayar). Uygulama açıkken ≤60 sn
içinde "Yeni güncelleme mevcut" uyarısı çıkar; 1.0.160'a güncellenen makine yerel şemasını 81'e
taşır ve yeni ekranlar/senkron tabloları devreye girer. Bu geliştirme makinesinde kurulu son-kullanıcı
uygulaması olmadığından güncelleme akışının GÖZLE doğrulaması kullanıcı makinesine kaldı
(updater checksum + rollback davranışı UPD-01 testleriyle kilitli).
