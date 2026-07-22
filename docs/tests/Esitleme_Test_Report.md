# Eşitleme (Senkron) — Test Raporu

**Tarih:** 2026-07-22 · **Kapsam:** eşitleme çekirdeği (Z1–Z5 + snapshot/delta motoru) ·
**QA durumu:** CLAUDE.md §7 yeniden aktif (2026-07-22) · **Test hesabı:** `.env.test.local` (§7.0.1)

## 1. Çalıştırılan doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| `dotnet build` (Desktop) | 0 hata, 8 uyarı |
| Birim/entegrasyon testleri (tam set) | **563 / 563 geçti**, 0 başarısız |
| Eşitleme filtresi (BusinessSync + Sync + MachineReset) | 44 → 47 geçti (3 yeni test) |
| Canlı sunucu QA (`tools/qa/live-sync-check.mjs`) | **7 / 7 geçti** (düzeltme öncesi 4/7) |

## 2. Bulunan hatalar

### B-1 (YÜKSEK, düzeltildi) — Hareket defteri delta dışında kalıyordu
`stock_movements` append-only olduğu için `updated_at` taşımıyor. `BuildSnapshot` damgayı yalnız
`updated_at`'te aradığından:
- **(a) Performans:** delta filtresi hiç uygulanmıyordu → *her* eşitlemede tüm defter aktarılıyordu.
  Canlı ölçüm: `since=version` çağrısı **663 satır** döndürüyordu (0 dönmeliydi). Defter hiç
  silinmediği için (§4) bu sonsuz büyür — 2026-07-19'daki zaman aşımının aynı sınıfı.
- **(b) DOĞRULUK:** `CompanyVersion` damgasız tabloyu atladığından **yeni bir stok hareketi firma
  sürümünü yükseltmiyordu** → karşı makine "değişiklik yok" sanıp çekmiyordu.

**Çözüm:** `StampColumn()` — damga `updated_at`, yoksa `created_at`. Defter satırı hiç güncellenmediği
için `created_at` tam olarak "ne zaman değişti" demektir.
**Geçiş tuzağı:** düzeltmeden önce yazılmış push watermark, artık filtrelenebilen defter satırlarını
kalıcı atlayabilirdi → `WatermarkEpoch` ile makine başına **tek seferlik tam gönderim**.
**Doğrulama:** `Defter_UpdatedAtsiz_Tablo_DeltayaGirer_VeSurumuYukseltir` + canlı: 663 → **0 satır**.

### B-2 / B-3 (yanlış alarm) — QA betiğinin kendi hatası
İlk koşuda "token firma taşımıyor" ve "firma sızıntısı" kaldı; sebep betiğin JWT claim adını yanlış
tahmin etmesiydi (`company_id` yerine gerçek ad `company`). Ürün hatası değil; betik düzeltildi ve
sızıntı kontrolü artık gerçekten çalışıyor (4274 satır tarandı, temiz).

## 3. Eklenen regresyon testleri

| Test | Neyi koruyor |
|---|---|
| `Apply_BuyukCokTabloluBatch_ArkadakiTablolarDaUygulanir_VeTekTransactionKalir` | Canlı hata: 1200 satırlık batch'te **arkadaki tablo (araçlar) uygulanmıyordu**. Ayrıca süre eşiği ile tek-transaction'ın kaldırılmasını yakalar. |
| `Snapshot_BaskaFirmaninVerisini_Sizdirmaz` | §7.12 tenant sızıntısı — snapshot'ta tek satır bile başka firmaya ait olamaz. |
| `Defter_UpdatedAtsiz_Tablo_DeltayaGirer_VeSurumuYukseltir` | B-1 (yukarıda). |

## 4. Coverage Matrix (§7.13)

| Alan | Durum | Not |
|---|---|---|
| Database (transaction/rollback) | ✅ | tek transaction + ROLLBACK; büyük batch testi |
| Sync (delta / tam / LWW) | ✅ | delta, LWW, sunucu-otoriter silme, çakışma tespiti |
| Offline / kalıcılık | ✅ | `Offline_Kalicilik_YenidenAcilis`, outbox atomik rollback |
| Yetki | ✅ | modül-bazlı tablo atlama, cihaz onayı (403), tokensiz 401 |
| Security (tenant/race) | ✅ | tenant sızıntısı (birim + canlı), Z1 tek kapı ile yarış |
| Performans | ✅ | 1200 satır < 20 sn; delta 663 → 0 satır |
| Hata mesajları / UI / UX | ✅ | Z5 senkron rozeti + durum paneli (daima görünür, tıklanabilir) |
| Grid / Form / Arama / Filtre | — | Ekran değil altyapı; ilgili ekran QA'lerinde |

## 5. Açık riskler

- **Alıcı taraf imleci saate bağlı.** Damga hâlâ duvar saati (`updated_at`/`created_at`). İki makinenin
  saati kayarsa satır atlanabilir. Kalıcı çözüm `server_seq` (monotonik sunucu sırası) — ertelendi.
- **Makineler arası ID ayrışması.** Aynı veri iki makinede ayrı ayrı import edilirse farklı ID üretilir
  → FK kırılır. Geçici kural: **import yalnız TEK makinede** yapılır.
- **Ledger `op_id` idempotency** ve yakıt/bakımda LWW kaldırılması hâlâ açık (bkz. YARIM_KALAN_ISLER).

## 6. Tekrar çalıştırma

```bash
dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj --filter "FullyQualifiedName~BusinessSync"
```
```bash
node tools/qa/live-sync-check.mjs
```
