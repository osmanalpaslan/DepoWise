# AKTİF DURUM

> Son güncelleme: **2026-08-11** · Bu dosya **her iş sonunda** güncellenir.

---

## 🔵 MEVCUT FAZ: **FAZ A — Kullanıcı bug'ları + yetki tamamlama**

## ✅ SON TAMAMLANAN
**PRT-01 Grup 6 + G6-20 + FAZ H (deploy).** 32 commit push edildi (`5813424`) ve
**production'a deploy edildi** (API + Web, 2026-08-11). Migration 63 canlıda uygulandı, veri kaybı yok.
Production yedeği alındı ve doğrulandı.

## ▶️ SIRADAKİ İŞ
**`YTK-05` — Yetkiler ekranına "Tümünü Temizle / Sıfırla" butonu (web + masaüstü).**
Kullanıcı NOT 1. Küçük, bağımsız, maliyetsiz. Ardından `UIX-01` → `YTK-06` → `YTK-08`.

## ⛔ ENGELLENEN İŞLER (karar bekliyor)
| İş | Neyi bekliyor |
|---|---|
| **FAZ C — Depo bazlı stok** (`STK-01…07`, `TRF-01`) | **KARAR-7**: malzeme kartı firma geneli mi, şube bazlı mı? |
| `FAZ D — MUH-01` | FAZ C |
| `SNK-05` | Çevrimdışı onay sunucuya yansısın mı? |
| `YET-01` | Rol değişince yetkiler ne olsun? |
| `KARAR-4` | Bakımda negatif stok mu, onay kapılı stok mu? |

## 📌 Canlı ortam durumu
| | |
|---|---|
| API | `depowise-erp.fly.dev` · v149 · `/health` 200 |
| Web | `depowise-web.fly.dev` · v175 |
| DB | Neon PostgreSQL **17.10** · `depowise_prod` · şema **63** |
| Veri | 3 firma · 8 kullanıcı · 6 şube · 2463 malzeme · 94 araç · 667 stok hareketi |
| Disk | `/data` %31 |
| Yedek | `Desktop\backups\depowise_prod_2026-08-11_124449.dump` (doğrulandı) |
| Git | `feature/mlz-01-malzeme-silme-korumasi` · `5813424` · remote ile senkron |
| Test | 1206 / 1173 ✅ / 0 ❌ / 33 atlanan · PG paketi 43/43 ✅ |
| Build | 0 hata |

## ⚠️ Açık riskler
- Masaüstü **paket yayınlanmadı** — Grup 6'nın masaüstü düzeltmeleri kullanıcılara ulaşmadı.
- Branch **`master`'a birleştirilmedi** (32 commit feature dalında).
- Depo bazlı stok yok → çok şubeli kullanım eksik (bkz. AUDIT §1).
