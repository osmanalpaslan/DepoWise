# Alpnex — MASTER ROADMAP

> Son güncelleme: **2026-08-11** · Kaynak: [`AUDIT_2026-08-11.md`](AUDIT_2026-08-11.md)
> **Hedef:** satılabilir ilk sürüm (MVP+). "Güzel olur" fikirleri FAZ 9+'a.

---

## Faz sırası ve gerekçesi

Sıra **bağımlılığa** göredir, isteğe göre değil. Bir faz, öncekinin çıktısına dayanır.

| Faz | Ad | Neden bu sırada | Durum |
|---|---|---|---|
| **FAZ C** | **Depo bazlı stok** (`STK-00…08`, `TRF-01`) | **Projenin 1 numaralı mimari borcu**; ön muhasebe ve şantiye maliyeti buna bağlı. **KARAR-7=A ile açıldı** | 🔵 **AKTİF** — `STK-00…07` ✅ |
| **FAZ A** | Kullanıcı bug'ları + yetki tamamlama (`YTK-05`, `UIX-01`, `YTK-06`, `YTK-08`) | Küçük, bağımsız, düşük riskli. **Silinmedi** — stok altyapısı mimari öncelik olduğu için sonraya alındı; FAZ C içinde uygun boşlukta veya FAZ C sonrası yapılır | BEKLEMEDE |
| **FAZ B** | Ekran görünürlük yönetimi (`GRN-01`) | Yetki sistemine dokunur; yeni stok ekranları doğduğunda hazır olması iyi olur | BEKLEMEDE |
| **FAZ D** | Ön muhasebe **alan hazırlığı** (`MUH-01`) | FAZ C ile **aynı migration ailesinde** yapılmalı; sonra eklenirse geçmiş veri boş kalır | FAZ C'ye bağlı |
| **FAZ E** | Senkron ölçeklenme (`SNK-06…10`) | FAZ C şemayı büyütür; senkron optimizasyonu ondan sonra anlamlı | FAZ C'ye bağlı |
| **FAZ F** | Güncelleme + sürüm uyumu (`GNC-01…03`) | Çok makineli kullanım öncesi | BEKLEMEDE |
| **FAZ G** | Kalan parite + rapor envanteri (`PRT-02`, `P-1`) — **`RPR-01` ✅ erken tamamlandı** | Çekirdek oturduktan sonra | BEKLEMEDE |
| **FAZ H** | Ön muhasebe **modülü** (`MUH-02…05`) | Alan hazırlığı + depo stoku bitmeden başlanmaz | BEKLEMEDE |
| **FAZ I** | Test/veri bütünlüğü + performans (`TST-01`, index, N+1) | Özellikler bitince | BEKLEMEDE |
| **FAZ J** | Canlıya geçiş: güvenlik sertleştirme, API sürümleme | En son | BEKLEMEDE |
| FAZ 9+ | Backlog: mobil, BI, e-Fatura, lastik ömrü, puantaj | Gelir sonrası | ERTELENDİ |

---

## Bağımlılık ağacı

```
KARAR-7 (malzeme kartı: firma geneli mi şube bazlı mı?)
   │
   ▼
FAZ C — STK-01 (stock_balances'a depo boyutu) ──┬──▶ STK-02..07 (UI/API/rapor)
                                                 ├──▶ TRF-01 (depo→depo transfer)
                                                 └──▶ MUH-01 (cari + maliyet merkezi alanları)
                                                            │
FAZ A (YTK-05, YTK-06, YTK-08, UIX-01) ── bağımsız ─────────┤
FAZ B (GRN-01) ── yetki sistemine dokunur ──────────────────┤
                                                            ▼
                                        FAZ E (SNK-06..10)  →  FAZ H (MUH-02..05)
                                                            →  FAZ I (test/perf)
                                                            →  FAZ J (deploy/güvenlik)
```

**Kural:** Aynı özelliğin web ve masaüstü tarafı **aynı faz içinde** bitirilir. Biri diğerini bekleyemez.

---

## FAZ A — Kullanıcı bug'ları + yetki tamamlama *(A sınıfı, maliyetsiz)*

| ID | İş | Ortam | Bağımlılık |
|---|---|---|---|
| `YTK-05` | Yetkiler ekranına **"Tümünü Temizle / Sıfırla"** + seçili kullanıcının yetkisini toptan güncelleme | Web + Masaüstü | — |
| `UIX-01` | **Tablo satır seçimi** — yazıya tıklayınca seçilmeme sorunu; ortak bileşen düzeyinde çöz | Web + Masaüstü | — |
| `YTK-06` | Yeni ekranın **yetki kataloğuna otomatik dâhil olması** — kaçırmayı imkânsız kılan mekanizma (rota/menü ↔ `AppModules.All` eşleşmesini doğrulayan test) | Ortak | — |
| `YTK-08` | Delegasyon tavanı **regresyon testi** (kendinde olmayan yetkiyi verememe — zaten çalışıyor, kilitlenecek) | API testi | — |

## FAZ B — Ekran görünürlük yönetimi

| ID | İş |
|---|---|
| `GRN-01` | Ekranın **web/masaüstü görünürlüğünü** yönetim ekranından açıp kapatma. Yetki sisteminden **ayrı** eksen: yetki "kim görebilir", görünürlük "nerede görünür". `AppModules` yanına `screen_platforms` tablosu; menü kurucu ikisini birden uygular |

## FAZ C — Depo bazlı stok 🔵 **AKTİF** *(KARAR-7 = A)*

Tasarım + migration planı: [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)

| ID | İş | Durum |
|---|---|---|
| `STK-00` | Migration güvenlik kanıtı — production kopyasında toplam korunumu | ✅ **TAMAM** (uyuşmayan 0) |
| `STK-01` | `stock_balances` → `(company_id, material_id, location_id)` + defterden yeniden hesaplama + **migration içi doğrulama** (iki lehçe) | ✅ **TAMAM** (Migration064 etkin) |
| `STK-02` | Tüm okuma/yazma yollarını (16 nokta) lokasyon bazlı yap — giriş/çıkış/sayım/transfer/ters kayıt + liste/rapor/dashboard | ✅ **TAMAM** (17 yeni test · PG+SQLite provası) |
| `STK-03` | API uçları + DTO (lokasyon parametresi) + **lokasyon sahiplik doğrulaması** | ✅ **TAMAM** (17 yeni senaryo · 2 yeni bakiye ucu) |
| `STK-04` | Web: giriş/çıkış/sayım/transfer lokasyonu · malzeme kartı kırılımı · hareket lokasyonu + filtre · açılış deposu | ✅ **TAMAM** (14 yeni senaryo · 3 hata düzeltildi) |
| `STK-05` | Masaüstü: lokasyonlu giriş/çıkış/sayım/açılış + kart kırılımı + hareket lokasyonu · **çevrimdışı korundu** | ✅ **TAMAM** (13 yeni senaryo · 4 hata düzeltildi) |
| `STK-06` | Rapor lokasyon boyutu: Stok Durumu (kırılım+filtre) · Stok Sayım (sayılan depo) | ✅ **TAMAM** (14 yeni senaryo) |
| `STK-07` | Senkron sertifikasyonu — 11 senaryo, gerçek HTTP senkron uçları · **kod değiştirilmedi** | ✅ **TAMAM** |
| `STK-08` | "Atanmamış → depo" toplu dağıtım ekranı (Web + masaüstü + çevrimdışı) | ✅ **TAMAM** (17 senaryo · gerçek veriyle doğrulandı) |
| `STK-B1` | `movement_type` kataloğu `usage`/`usage_reverse` ile tutarsız | BEKLEMEDE |
| `TRF-01` | Transfer **kodu zaten var** — UI paritesi + bakiyeye yansıma doğrulaması | BEKLEMEDE |

> **Önemli:** `StockService.Transfer` çok malzemeli, tek transaction, idempotent ve negatif-guard'lı olarak
> **zaten uygulanmış**; hareketler kaynak/hedef lokasyonla yazılıyor. Bugün yalnız **bakiyeye yansımıyor**
> çünkü bakiye lokasyonsuz. `STK-01` bunu kökten çözer.
>
> **Offline kısıtı (değişmez):** Bakiye türetilmiş bir önbellektir ve **LWW ile senkronlanmaz**;
> iki tarafta da defterden yeniden hesaplanır (CLAUDE.md §4 — stokta LWW yasak).

## FAZ D — Ön muhasebe alan hazırlığı

| ID | İş |
|---|---|
| `MUH-01` | Para hareketi doğuran her kayda **cari + maliyet merkezi (şantiye) + belge** alanları (malzeme alışı, yakıt, bakım, şantiye gideri). FAZ C migration'ları ile **birlikte** |

## FAZ E — Senkron ölçeklenme

| ID | İş |
|---|---|
| `SNK-06` | Girişte tam pull yerine **kalıcı imleçle delta pull** |
| `SNK-07` | Snapshot'ı **sayfala** (batch/chunk) |
| `SNK-08` | Yanıt **sıkıştırma** (gzip) |
| `SNK-09` | Delta ölçütünü **monoton sunucu sırasına** taşı (saat kaymasına karşı) |
| `SNK-10` | Silinen kayıtların delta ile taşındığını **test et** |

## FAZ F — Güncelleme + sürüm uyumu

| ID | İş |
|---|---|
| `GNC-01` | Otomatik güncelleme davranış denetimi (mevcut plandan devir) |
| `GNC-02` | **API ↔ istemci sürüm uyumu** (eski masaüstü / yeni API) |
| `GNC-03` | Sunucu disk politikası — paket saklama tavanı, `/data` doluluk uyarısı |

## FAZ G — Kalan parite + rapor

`PRT-02` (ekran adı eşleme) · `P-1` (masaüstü "Bağı Kaldır") · Personel/Muayene filtre+export

✅ `RPR-01` (rapor filtre paritesi) **2026-08-11'de tamamlandı** — FAZ C içinde erken alındı, çünkü
STK-06 aynı riski canlı olarak gösterdi. Kayıt: [`RPR_01_FILTRE_PARITESI.md`](RPR_01_FILTRE_PARITESI.md)

## FAZ H — Ön muhasebe modülü

`MUH-02` cari hesap · `MUH-03` kasa/banka + tahsilat/ödeme · `MUH-04` gider dağıtımı (şantiye maliyeti) · `MUH-05` ön muhasebe raporları

## FAZ I — Test / performans

`TST-01` (33 atlanan test) · index denetimi · N+1 taraması · büyük liste sayfalama

## FAZ J — Canlıya geçiş

Güvenlik sertleştirme · API sürümleme kararı · yük testi

---

## Devredilen teknik borçlar (fazlanmamış, kapanmadı)

`G6-10…G6-19` · `G6-21/22/24` · `H-6` (masaüstü sunucu adresi 7 dosyada tekrar) · `H-7` · `GRP3-JOIN` ·
`brands/vehicle_models JOIN` · `500→400` · `WEB-01b` · `GUV-01b` · `TLP-B5` · `MUA-01/02` · `G2-08` ·
`TMZ-01/03` · Personel 200 kayıt tavanı · `SNK-05` (karar bekliyor) · `WEB-02` · `YET-01` (karar bekliyor)

Ayrıntı: [`TASK_BACKLOG.md`](TASK_BACKLOG.md).
