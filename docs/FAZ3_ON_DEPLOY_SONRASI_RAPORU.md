# FAZ 3-ÖN · ADIM 4 — DEPLOY SONRASI RAPORU

**Tarih:** 2026-08-08
**Durum:** 🟢 **API, WEB ve MASAÜSTÜ 1.0.129 YAYINDA** (masaüstü yayını §5'te tamamlandı)
**Rollback gerekli mi:** **HAYIR**

> Bu raporda hiçbir bağlantı adresi, kullanıcı adı, parola veya API anahtarı yer almaz.

---

## 1. DEPLOY EDİLEN SÜRÜM

| | Değer |
|---|---|
| Onaylanan commit | `1bc371c` |
| Yayınlanan commit | `f343925` (üzerinde **yalnız** deploy öncesi onay raporu belgesi var) |
| Üretim kodu farkı | **YOK** — `git diff 1bc371c..f343925 -- src/` **boş**; yayınlanan uygulama kodu onaylanan sürümle **birebir aynı** |
| Önceki yayın | `85b7504` (masaüstü 1.0.128) |

---

## 2. API SONUCU — ✅ BAŞARILI

| Adım | Sonuç |
|---|---|
| Uygulama | `depowise-erp` (`fly.toml`) |
| Dağıtım stratejisi | Rolling update, tek makine |
| Makine durumu | `Machine ... is now in a good state` |
| Smoke / machine / health kontrolleri | Tamamı geçti |
| DNS | `✓ DNS configuration verified` |

---

## 3. WEB SONUCU — ✅ BAŞARILI

| Adım | Sonuç |
|---|---|
| Uygulama | `depowise-web` (`fly.web.toml`) |
| Makine durumu | `Machine ... is now in a good state` |
| Smoke / machine / health kontrolleri | Tamamı geçti |
| DNS | `✓ DNS configuration verified` |

---

## 4. SAĞLIK (HEALTH) SONUCU — ✅

| Uç | Sonuç |
|---|---|
| `GET https://depowise-erp.fly.dev/health` | **HTTP 200** · `{"status":"ok", ...}` · 0,22 sn |
| `GET https://depowise-web.fly.dev/` | **HTTP 200** · 0,51 sn |
| `GET https://depowise-web.fly.dev/login` | **HTTP 200** |
| API tekrar kontrolü (web deploy sonrası) | **HTTP 200** |

---

## 5. MASAÜSTÜ 1.0.129 SONUCU — ✅ YAYINLANDI

| Adım | Sonuç |
|---|---|
| Derleme (`dotnet publish -r win-x64 --self-contained`) | ✅ Başarılı — `artifacts/rc/desktop-1.0.129/` (270 dosya) |
| Paket (zip) | ✅ `artifacts/rc/DepoWise-desktop-1.0.129.zip` — **85,4 MB** (89 543 708 bayt), 270 girdi |
| Sunucuya sürüm yayını | ✅ **YAYINLANDI** — `node scripts/publish_release.mjs <zip> 1.0.129 "<not>"` |
| Giriş | Süper admin ile başarılı (firma: DEPOWISE) |
| Sunucu doğrulaması | **"sunucudaki en güncel sürüm = 1.0.129"** |
| İndirme adresi | `/api/releases/1.0.129/download` |

### Kimlik bilgisi mekanizması (ilk denemede neden yapılamamıştı)

İlk denemede `DEPOWISE_ADMIN_USER` / `DEPOWISE_ADMIN_PASS` **Bash oturumunun ortamında görünmüyordu** ve
`.env` dosyalarında da tanımlı değildi; bu yüzden durulmuştu. Sonraki kontrolde bu değişkenlerin projenin
normal yayınlama mekanizması olarak **Windows kullanıcı ortam değişkenlerinde** (User scope) tanımlı olduğu
görüldü. Yayın, projenin her zamanki komutuyla bu mekanizma üzerinden tamamlandı.
**Hiçbir kimlik bilgisi görüntülenmedi, kopyalanmadı, dosyaya veya git'e yazılmadı; yeni kimlik bilgisi
oluşturulmadı, mevcut olan değiştirilmedi.**

### Paket bütünlüğü doğrulaması (madde 2)

| Ölçüm | Yerel dosya | Sunucu |
|---|---|---|
| Boyut | 85,4 MB (89 543 708 bayt) | 85,4 MB |
| SHA-256 (ilk 12) | `58a29c5e58ab` | `58a29c5e58ab` |

✅ **Boyut ve sağlama birebir eşleşiyor.**

---

## 6. LOG SONUCU — ✅ KRİTİK HATA YOK

| Uygulama | Bulgu |
|---|---|
| `depowise-erp` (API) | Hata / exception / `[500]` / unhandled / migration hatası **bulunamadı** |
| `depowise-web` (Web) | Yalnız bilgi seviyesinde `Failed to determine the https port for redirect.` — **deploy öncesinde de mevcut**, davranışı etkilemiyor |

`[stock-cas]` veya `[stock-docno]` yarış kaydı **yok** (beklenen: eşzamanlı çakışma yaşanmadı).

---

## 7. DEPLOY ÖNCESİ / SONRASI HAREKET VE BELGE SAYILARI

| Ölçüm | Deploy ÖNCESİ | Deploy SONRASI | Değişim |
|---|---|---|---|
| Firma | 3 | 3 | **0** |
| Malzeme | 2 463 | 2 463 | **0** |
| **Stok hareketi** | **667** | **667** | **0** |
| **Stok belgesi** | **2** | **2** | **0** |
| Bakiye satırı | 664 | 664 | **0** |
| Veritabanı boyutu | 14 MB | 14 MB | **0** |

**Deploy hiçbir stok hareketi veya belgesi üretmedi.**

---

## 8. CANLI STOK TUTARLILIK SONUCU (deploy sonrası, SALT-OKUMA)

| Kod | Kontrol | Sonuç |
|---|---|---|
| **A** | `stock_balances` ↔ `stock_movements` | ✅ **2 463 / 2 463 tutarlı — 0 fark** |
| **B** | Negatif bakiye | 66 (deploy öncesiyle **aynı**) — tamamı ADR-086 negatif açılış |
| C0/C0b | Sayısal olmayan miktar/bakiye metni | 0 |
| C1 | Yetim hareket | 0 |
| C2 | Belgesi olmayan hareket | 0 |
| C3 | Yetim bakiye | 0 |
| C4 | **Yarım belge** (hareketsiz) | **0** |
| C5 | Transfer çifti tutarsızlığı | 0 |
| C6 | Ters kaydın hedefi yok | 0 |
| C7 | İptal belgede geri alınmamış hareket | 0 |
| C8 | `is_reversed=1` ama ters kayıt yok | 0 |
| C9 | Miktar ≤ 0 | 0 |
| C10 | Geçersiz `direction` | 0 |
| C11 | Tekrarlı `operation_id` | 0 |
| **C12** | **Çapraz firma: hareket ↔ malzeme** | **0 — sızıntı yok** |
| **C13** | **Çapraz firma: bakiye ↔ malzeme** | **0 — sızıntı yok** |
| C14 | Bakiye satırı olmayan hareketli malzeme | 0 |

### Kesin kanıt: çıktılar birebir aynı

Deploy öncesi ve sonrası denetim çıktıları **satır satır karşılaştırıldı** (`diff`):

```
FARK YOK — deploy hiçbir veriyi değiştirmedi
```

---

## 9. BEKLENEN SONUÇLARIN KARŞILANMASI

| Beklenti | Sonuç |
|---|---|
| 2463/2463 stok tutarlılığı korunmalı | ✅ Korundu |
| Deploy nedeniyle yeni stok hareketi/belgesi oluşmamalı | ✅ Oluşmadı (667/2 → 667/2) |
| Yarım/yetim/tutarsız kayıt oluşmamalı | ✅ Oluşmadı (C1–C14 tamamı 0) |

---

## 10. PRODUCTION'A YAPILAN VERİ DEĞİŞİKLİĞİ

**İŞ VERİSİNE HİÇBİR YAZMA YAPILMADI.**

| Kontrol | Sonuç |
|---|---|
| Migration çalıştırıldı mı | **Hayır** — yeni migration yok; şema sürümü değişmedi |
| Şema değişikliği | **Hayır** |
| INSERT / UPDATE / DELETE (iş verisi) | **Hayır** |
| Doğrulamalar | Tamamı **salt-okuma**; `SHOW transaction_read_only = on`, kanıt amaçlı `UPDATE` PostgreSQL tarafından **`25006`** ile reddedildi, `ROLLBACK` ile kapatıldı |
| Sürüm kaydı yazımı | **Yapılmadı** (masaüstü yayın adımı tamamlanmadı — §5) |
| Sayım kanıtı | Deploy öncesi/sonrası tüm sayımlar birebir aynı |

---

## 11. ROLLBACK DURUMU

**Rollback GEREKMİYOR.** API ve web sağlıklı, loglar temiz, veri değişmedi.

Gerekirse yöntem (hazır): `flyctl releases -a depowise-erp` / `-a depowise-web` ile önceki imaja dönüş.
Şema değişmediği için sürümler ileri/geri uyumludur — **veri kaybı riski yok**.

---

## 12. MASAÜSTÜ YAYINI SONRASI SALT-OKUMA DOĞRULAMASI

Masaüstü 1.0.129 yayınlandıktan **sonra** tekrarlanan kontroller:

| # | Kontrol | Beklenen | Gerçek | Sonuç |
|---|---|---|---|---|
| 1 | Sunucudaki güncel masaüstü sürümü | 1.0.129 | **1.0.129** | ✅ |
| 2 | Paket boyut / sağlama eşleşmesi | Aynı | 85,4 MB · `58a29c5e58ab` — **aynı** | ✅ |
| 3 | API `/health` | HTTP 200 | **HTTP 200** | ✅ |
| 4a | Stok hareketi | 667 | **667** | ✅ |
| 4b | Stok belgesi | 2 | **2** | ✅ |
| 4c | Malzeme | 2 463 | **2 463** | ✅ |
| 4d | Bakiye satırı | 664 | **664** | ✅ |
| 5 | `stock_balances` ↔ `stock_movements` | 2463/2463 | **2 463 / 2 463 — 0 fark** | ✅ |

Ek olarak C1–C14 yapısal kontrollerin tamamı yine **0**; negatif bakiye **66** (değişmedi, tamamı ADR-086
negatif açılış). Sürüm yayını öncesi/sonrası denetim çıktıları `diff` ile karşılaştırıldı → **FARK YOK**.

Tüm doğrulamalar salt-okuma: `transaction_read_only = on`, kanıt amaçlı `UPDATE` **`25006`** ile reddedildi,
`ROLLBACK`.

**Not:** Sürüm yayını sunucuda bir **sürüm kaydı** (release metadata + paket) oluşturur; bu, masaüstü
güncelleme sisteminin normal işleyişidir ve **stok/iş verisine dokunmaz** — yukarıdaki sayımların
değişmemesi bunun kanıtıdır.

---

## 13. AÇIK KALAN İŞLER (bu adımın kapsamı dışı)

- ⏸️ **M-S1a `company_id` migration'ı** — ayrı adım, ayrı onay. **Başlanmadı.**
- ⏸️ Faz 3'ün devamı — **başlanmadı.**
