# Stok (Depo Bazlı Bakiye) — Test Raporu

> Tarih: **2026-08-11** · Kapsam: `STK-01` + `STK-02` (FAZ C) · Dal: `feature/mlz-01-malzeme-silme-korumasi`
> Kapsam kuralı (CLAUDE.md §7.1): yalnız **değiştirilen alan** — stok bakiyesi okuma/yazma yolları ve
> bu bakiyeyi gösteren liste/rapor/dashboard sorguları. Başka ekrana dokunulmadı.

---

## 1. Ne değişti

`stock_balances` birincil anahtarı `(material_id)` → **`(company_id, material_id, location_id)`**.
`location_id = ''` → **ATANMAMIŞ** (lokasyonu bilinmeyen geçmiş stok). Bakiye artık **depo bazlıdır**;
transfer bakiyede görünür.

Eski tek-satır varsayımına dayanan **16 üretim noktası** dönüştürüldü (4 yazma · 4 okuma · 8 JOIN).

---

## 2. Çalıştırılan doğrulamalar

| # | Doğrulama | Sonuç |
|---|---|---|
| 1 | Çözüm derlemesi (`DepoWise.sln`) | **0 hata** |
| 2 | Tam test takımı | **1223 toplam · 1190 geçti · 0 kaldı · 33 atlandı** |
| 3 | Yeni senaryolar (`StockLocationTests`) | **17 / 17 geçti** |
| 4 | İzole PostgreSQL migration provası (üretim yedeğinin kopyası) | **GEÇTİ** |
| 5 | Dolu SQLite `v63 → v64` yükseltmesi | **GEÇTİ** |
| 6 | Migration doğrulama kapısı + geri alma | **GEÇTİ** |
| 7 | Dönüştürülen sorguların PostgreSQL'de çalıştırılması | **GEÇTİ** |

### 4 — İzole PostgreSQL provası (rakamlar)
| Ölçüm | Önce | Sonra |
|---|---|---|
| Şema | 62 | 64 |
| Stok hareketi | 667 (666'sı lokasyonsuz) | — |
| Bakiye satırı | 664 | **665** |
| Negatif bakiye | 66 | 67 (**+1**, defterin söylediği) |
| Lokasyon sayısı | — | 2 (ATANMAMIŞ + 1 şube) |
| ATANMAMIŞ miktar | — | **8953,3** |
| **Toplam stok** | 8952,3 | **8952,3** ✅ |
| **Uyuşmayan malzeme** | — | **0** ✅ |

Süre: **173 ms**. Prova bittikten sonra kopya veritabanı **silindi**, yerel sunucu **durduruldu**.
Canlı veritabanına **bağlanılmadı**; program canlı (Neon) adresini reddeden bir kontrol içeriyordu.

### 5 — Dolu SQLite yükseltmesi
3 malzeme · 7 hareket · 3 eski bakiye satırı → **5 lokasyon satırı**, toplam **8,3** korundu.
Ondalıklar tam: `0.1` + `0.2` iki ayrı depoda → `0.1` ve `0.2` (float kayması yok).
Lokasyonsuz negatif kalıntı (`-3`) ATANMAMIŞ kovasında korundu.

### 6 — Doğrulama kapısı
Defterle uyuşmayan bir bakiye (`999` yazılı, defter `10` diyor) bırakıldı →
migration **istisna fırlattı**, şema **63'te kaldı**, bakiye **değişmedi**. Fail-closed çalışıyor.

### 7 — PostgreSQL'de sorgu doğrulaması
| Sorgu | Sonuç |
|---|---|
| Malzeme listesi (grid) | **2459 satır = malzeme sayısı** → satır çoğaltma **yok** |
| Malzeme kartı / lokasyon kırılımı / servis toplamı | 5 = 5 = 5 = 5 → **tutarlı** |
| Rapor: Stok Durumu | 2459 satır |
| Rapor: Şablon Dışı | 2459 satır |
| Dashboard | 2459 malzeme · düşük stok sayısı 2136 · uyarı 20 (liste `LIMIT 20`) |

---

## 3. Yeni senaryolar (17)

**Ayrışma (hangi hareket hangi kovaya):** giriş belgenin deposuna · iki depoda ayrı satır ·
deposuz hareket ATANMAMIŞ'a · açılış stoğu kendi lokasyonuna.

**Kopmama (toplam okuma):** `GetBalance` firma geneli / `GetBalanceAt` tek lokasyon ·
Σ kırılım = genel toplam · toplu okuma (`GetBalances`) toplar, malzemeyi tekrarlamaz.

**Transfer ve negatif koruma:** transfer kaynağı azaltır/hedefi artırır, toplam sabit ·
başka depodaki stok bu deponun çıkışını **karşılamaz** (reddedilen çıkış hiçbir kovayı değiştirmez) ·
çıkış yalnız kendi deposunu düşürür · ters kayıt **orijinal** lokasyona geri verir ·
sayım **sayılan** deponun bakiyesiyle karşılaştırır.

**Yeniden hesaplama ve hassasiyet:** `RecomputeBalances` kırılımı korur ve **hayalet satırı temizler** ·
`0.1 + 0.2 = 0.3`, `10.25 + 99.99 = 110.24` (hem servis hem SQL liste yolunda).

**Çoğaltma ve koruma:** malzeme listesi iki depolu malzemeyi çoğaltmaz (kart da toplamı gösterir) ·
düşük stok uyarısı çoğaltmaz ve **firma toplamına** göre değerlendirir (sayı ile liste kopmaz) ·
malzeme silme koruması **başka depodaki** stoğu görür.

---

## 4. Bulunan hata (kod yazmadan önce analiz edildi)

| Alan | İçerik |
|---|---|
| **Bulgu** | Sayım (`StockService.Count`) sistem miktarını **firma genelinden** okuyor, fark hareketini **şubeye** yazıyordu. |
| **Öncelik / risk** | 🔴 Yüksek — depo bazlı modelde her sayım **yanlış** düzeltme üretirdi. |
| **Tekrar üretme** | Depo A'da 10, Depo B'de 5 varken Depo A'yı 12 say. |
| **Beklenen** | Fark `12 − 10 = +2`, Depo A = 12, Depo B = 5. |
| **Gerçek (düzeltmeden önce)** | Fark `12 − 15 = −3` hesaplanır, Depo A yanlış düşürülürdü. |
| **Muhtemel neden** | Bakiye tek havuzken "genel okuma" doğruydu; lokasyon boyutu gelince asimetrik kaldı. |
| **Çözüm** | Sistem miktarı **sayılan lokasyondan** okunuyor (`ReadBalance(..., branchId ?? Unassigned)`). |
| **Doğrulama** | Senaryo 12 — geçti. |

Başka bulgu **yok**. Başarısız test gizlenmedi; üretim kodu test geçirmek için zayıflatılmadı.

---

## 5. Riskler

| Risk | Durum |
|---|---|
| Deploy sonrası stoğun neredeyse tamamı **ATANMAMIŞ** görünecek (8953,3 birim) | ⚠️ Açık — **KARAR-8** bekliyor; veri uydurulmadı |
| Migration, bakiyesi defterle uyuşmayan veritabanında **durur** (masaüstü güncellemesi başlamaz) | ⚠️ Bilinçli fail-closed; üretim PG kopyasında uyuşmazlık yok |
| PostgreSQL provası PG **16.4** sunucuda yapıldı (canlı 17.10) | ⚠️ Devralınan kısıt — PG 17 sunucu bu makinede başlatılamıyor (FAZ H'de raporlandı) |
| 66 mevcut negatif bakiye (ADR-086) | Değişmedi; migration **1 yeni** negatif üretir (defterin söylediği) |

---

## 6. Coverage Matrix (§7.13)

| Madde | Durum | Not |
|---|---|---|
| Form Açıldı | ⚪ | Bu iş **veri katmanı**; ekran değişmedi (ekran işi `STK-04`/`STK-05`) |
| Yeni Kayıt | ✅ | Giriş/açılış/transfer/sayım belgeleri |
| Düzenleme | ✅ | Sayım düzeltmesi · ters kayıt |
| Silme | ✅ | Malzeme silme koruması (başka depodaki stok) |
| Arama | ✅ | Grid kod filtresi |
| Filtre | ✅ | `MaterialGridFilter` |
| Grid | ✅ | Satır çoğaltmama (SQLite + PostgreSQL) |
| Doğrulamalar | ✅ | Negatif stok · pozitif miktar · migration doğrulama kapısı |
| Yetki | ⚪ | Değişmedi (`AccessControl` yolları aynı) |
| Hata Mesajları | ✅ | "Bu şubede yeterli stok yok…" · migration durdurma mesajı |
| Database | ✅ | PK · indeks · transaction geri alma · iki lehçe |
| Offline | ✅ | Masaüstü SQLite yolu değişmedi; dolu yükseltme provası yapıldı |
| Sync | ✅ | Bileşik PK otomatik (`DbIntrospect`) — `BusinessSyncTests` güncellendi ve geçti |
| Performans | ✅ | Migration 173 ms (667 hareket) · toplu okuma hâlâ **tek sorgu** (N+1 yok) |
| UI | ⚪ | Bu iş biriminde ekran değişmedi |
| UX | ⚪ | Aynı |
| Security | ✅ | Tenant izolasyonu korundu; JOIN'lere `company_id` eşleşmesi **eklendi** (savunma derinliği) |

**Tahmini kapsam:** değiştirilen 16 çağrı noktasının tamamı doğrudan veya dolaylı olarak test edildi.
**Çalıştırılan senaryo sayısı:** 1223 otomatik test + 4 elle yürütülen migration/veritabanı provası.

---

# EK — `STK-03` API Lokasyon Sözleşmesi (2026-08-11)

> Kapsam: stok API uçları + lokasyon sahiplik doğrulaması. Ekran değişikliği YOK (STK-04/05).

## 1. Bulunan hata — 🔴 lokasyon sahiplik kontrolü YOKTU

| Alan | İçerik |
|---|---|
| **Bulgu** | `StockService` (giriş/çıkış/transfer/sayım) ve `OpeningStockService` gönderilen `branchId`'nin oturumun **firmasına ait** olduğunu kontrol etmiyordu. |
| **Öncelik / risk** | 🔴 Yüksek — STK-02'den beri lokasyon `stock_balances`'ın **birincil anahtar** kolonu. Yabancı kimlik yazılsaydı o bakiye satırı **hiçbir firmanın ekranında düzeltilemezdi**. |
| **Tekrar üretme** | `POST /api/stock/receive` gövdesine **başka firmanın** şube kimliğini koy. |
| **Beklenen** | 403 — hiçbir kayıt oluşmaz. |
| **Gerçek (düzeltmeden önce)** | 200 — hareket + bakiye yabancı lokasyon kimliğiyle yazılırdı. |
| **Muhtemel neden** | Desen projede vardı (`RequestOperationsService` → `EnsureBranchOwned`), stok yoluna bağlanmamıştı. |
| **Çözüm** | `EnsureLocationOwned` — **servis katmanında** (API'de değil), `RunDocumentOnce`'ın tek geçiş noktasında + açılış stoğunda. |
| **Neden serviste** | Masaüstü bu servisi **çevrimdışı**, API'ye uğramadan çağırır; API'ye konsaydı çevrimdışı yol korumasız kalırdı. |
| **Doğrulama** | Senaryo 8, 9, 10 (API) + 18 (çevrimdışı) — geçti. |

## 2. Çalıştırılan doğrulamalar

| Doğrulama | Sonuç |
|---|---|
| Çözüm derlemesi | **0 hata** |
| Tam test takımı | **1240 · 1207 geçti · 0 kaldı · 33 atlandı** (STK-02 tabanı 1223'tü; **17 yeni senaryo**) |
| `ApiStockLocationTests` (gerçek HTTP) | **15 / 15** |
| `StockLocationTests` (servis + çevrimdışı) | **19 / 19** |

## 3. 17 senaryonun karşılığı

| # | Senaryo | Test |
|---|---|---|
| 1 | Giriş + lokasyon | `Giris_GovdedekiDepoya_Yazilir` |
| 2 | Çıkış + lokasyon | `Cikis_YalnizKendiDeposunu_Dusurur` |
| 3 | Sayım + lokasyon | `Sayim_SayilanDeponun_Miktarini_Kullanir` |
| 4 | Transfer kaynak/hedef | `Transfer_KaynakVeHedefi_AyriTasir` |
| 5 | `GetBalanceAt` | `Tek_Lokasyon_Ucu_Depo_Ve_Adini_Doner` |
| 6 | `GetBalancesByLocation` | `Kirilim_Ucu_Her_Depoyu_Tek_Satir_Doner_Ve_Toplamla_Kopmaz` |
| 7 | Genel toplam | `Genel_Toplam_Ucu_Degismedi` |
| 8 | Yanlış firma lokasyonu | `Yabanci_Firmanin_Deposu_Yazmada_Reddedilir` + `…_Okumada_Reddedilir` |
| 9 | Yetkisiz lokasyon | `Stok_Yetkisi_Olmayan_Kullanici_Lokasyon_Uclarini_Cagiramaz` |
| 10 | Bilinmeyen lokasyon | `Bilinmeyen_Lokasyon_Reddedilir` |
| 11 | ATANMAMIŞ | `Lokasyonsuz_Eski_Istek_ATANMAMIS_Kovasina_Yazilir` |
| 12 | Çift transfer (idempotency) | `Transfer_AyniOperationId_Ikinci_Kez_Uygulanmaz` |
| 13 | Negatif kalkan | `Baska_Depodaki_Stok_Bu_Deponun_Cikisini_Karsilamaz` |
| 14 | Eski istek davranışı | (11 ile aynı test — eski istemci lokasyon göndermez) |
| 15 | Web istemci sözleşmesi | `Hareket_Listesi_Lokasyon_Alanlarini_Doner` |
| 16 | Masaüstü istemci sözleşmesi | `Masaustu_Cevrimdisi_Yolda_Da_Yabanci_Depo_Reddedilir` |
| 17 | Çevrimdışı → sync sonrası uyum | `Cevrimdisi_Yazilan_Lokasyonlu_Hareketler_Sunucuda_Ayni_Kirilimi_Uretir` |

## 4. Performans

| Kural | Uygulama |
|---|---|
| N+1 yok | Lokasyon **adları** hareket listesinde aynı sorguda `LEFT JOIN branches` ile gelir |
| Toplu ihtiyaç tek sorgu | `GetLocationBalances` tek `SELECT` + `JOIN` (100 malzeme × 5 depo = 500 sorgu senaryosu **oluşmaz**) |
| Tekrar hesaplama yok | `/locations` yanıtındaki `total` aynı satırlardan C#'ta toplanır; ikinci sorgu atılmaz |
| Yeni JOIN maliyeti | Hareket listesinde 2 `LEFT JOIN` (`branches`, birincil anahtar üzerinden) |

## 5. İstemci envanteri

| İstemci | Stok uçları | STK-03 etkisi |
|---|---|---|
| **Web** | `Stock.razor` · `StockCount.razor` · `StockMovements.razor` · `Daily.razor` · `StockChangeLog.razor` | Yanıtları `JsonElement`+`TryGetProperty` ile okur → **eklenen alanlar bozmaz**. İstek alanları değişmedi |
| **Masaüstü** | **HİÇBİRİ** — stok işlemleri yerel `StockService` + `business-push/pull` | Uç değişikliğinden **etkilenmez**; koruma servise konduğu için çevrimdışı da geçerli |

## 6. Coverage Matrix — STK-03

| Madde | Durum | Not |
|---|---|---|
| Yeni Kayıt / Düzenleme / Silme | ✅ | Giriş · çıkış · sayım · transfer · ters kayıt (HTTP) |
| Doğrulamalar | ✅ | Yabancı / bilinmeyen lokasyon · negatif stok · idempotency |
| Yetki | ✅ | Deny-by-default: stok yetkisi olmayan yeni uçları çağıramaz |
| Hata Mesajları | ✅ | 403 (lokasyon) · 400 (iş kuralı) — **mevcut** hata modeli, yenisi icat edilmedi |
| Database | ✅ | Bakiye anahtarına yabancı kimlik yazılamıyor |
| Offline | ✅ | Çevrimdışı yol da korunuyor (senaryo 18) |
| Sync | ✅ | Sync kodu **değiştirilmedi**; senaryo 19 hâlâ doğru olduğunu kanıtlıyor |
| Performans | ✅ | N+1 yok (§4) |
| UI / UX | ⚪ | Ekran işi STK-04 / STK-05 |
| Security | ✅ | Çapraz-tenant lokasyon referansı kapatıldı |
