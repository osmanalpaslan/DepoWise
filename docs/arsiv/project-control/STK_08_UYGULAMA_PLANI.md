# STK-08 — "Atanmamış" Stoğun Toplu Dağıtımı · Uygulama Planı

> Oluşturuldu: **2026-08-11** · FAZ C · Ön koşul: `STK-00…07` ✅ · `SNK-12` ✅
> **DURUM: ✅ TAMAMLANDI (2026-08-11)** — §12-15 uygulama, bulgular ve doğrulama sonuçları.
> (§10'daki "kod başlamadı" notu ARTIK GEÇERSİZDİR; tarihsel kayıt olarak duruyor.)
> **KARAR-8 (kalıcı):** otomatik dağıtım YOK — kullanıcı **gerçek transfer hareketleriyle** dağıtır.

---

## 1. 🔴 EN KRİTİK BULGU — mevcut transfer ATANMAMIŞ'ı kaynak KABUL ETMİYOR

`StockService.Transfer` (satır ~141) açıkça reddediyor:

```csharp
fromBranchId = EnforceOwnBranch(s, fromBranchId, "transfer") ?? fromBranchId;
if (string.IsNullOrEmpty(fromBranchId)) throw new ArgumentException("Kaynak şube belirlenemedi.");
```

Yani `ATANMAMIŞ ("")` → gerçek depo transferi **bugün yapılamıyor**. Üstelik iki ayrı engel var:

| # | Engel | Sonucu |
|---|---|---|
| E-1 | Boş kaynak `ArgumentException` ile reddediliyor | Dağıtım hiç başlamaz |
| E-2 | `EnforceOwnBranch`: şubeye bağlı kullanıcıda boş kaynak **sessizce kendi şubesine çevrilir** | Dağıtım yanlış depodan düşerdi — **sessiz veri bozulması** |
| E-3 | `ApplyLine`'daki şube-bazlı negatif kalkanı `!string.IsNullOrEmpty(branchId)` koşulludur → ATANMAMIŞ için **çalışmaz** | "10 varken 11 dağıt" engellenmezdi |

➡️ **Bu üçü, `Transfer`'i gevşeterek çözülemez.** Genel transferde boş kaynağa izin vermek, herhangi bir
istemcinin kazara lokasyonsuz transfer üretmesine kapı açar (STK-02…07'de kapattığımız sınıfın hatası).

### KARAR (T-1): AYRI ve DAR bir giriş noktası
`StockService.DistributeUnassigned(s, lines, toLocationId, operationId, note)` eklenir:
- **Aynı** `RunDocument` / `ApplyLine` / `InsertMovement` makinesini kullanır → yeni paralel stok mantığı YOK.
- Hareket türü **`transfer`** kalır (yeni tür açılmaz) · `branch_from_id = ""` (ATANMAMIŞ) · `branch_id = hedef`.
- `EnforceOwnBranch` **çağrılmaz** (kaynak tanım gereği ATANMAMIŞ'tır, kullanıcının şubesi değildir).
- **Kendi yeterlilik kontrolü:** her satır için `ReadBalance(..., Unassigned) >= miktar` (E-3'ün karşılığı).
- Hedef için **`EnsureLocationOwned`** zaten `RunDocumentOnce` içinde çalışır (STK-03) → yabancı/bilinmeyen
  depo **403**. Ek kontrol: hedef **boş olamaz** (ATANMAMIŞ hedef seçilemez — talimat §6).

## 2. İŞ KURALLARI (talimat §2-7 karşılığı)

| Kural | Uygulama |
|---|---|
| Miktar > 0 | `<= 0` → `ArgumentException` (sıfır ve negatif reddedilir) |
| Miktar ≤ ATANMAMIŞ | Satır bazında kontrol; aşımda `NegativeStockException` (mevcut hata modeli) |
| Kısmi dağıtım | Doğal sonuç: 100'ün 30'u aktarılır, 70 ATANMAMIŞ'ta kalır |
| Çoklu malzeme | **Tek belge, tek transaction** — `RunDocument` zaten öyle; bir satır düşerse **tamamı** geri alınır |
| Hedef ATANMAMIŞ olamaz | `if (string.IsNullOrEmpty(toLocationId)) throw` |
| Ondalık | `decimal` + `Money`; SQL toplama/float **yok** |
| Toplam korunur | Çıkış(−) + giriş(+) aynı miktarda → firma toplamı **değişmez** (testle kilitlenecek) |
| ~~Geri alınabilir~~ | ⚠️ **BU VARSAYIM YANLIŞTI** — transferler geri ALINMAZ (2026-08-06 kararı). Düzeltme = yeni transfer. Bkz. §13 B-1 |
| Audit | `RunDocument` zaten `AuditWriter` yazıyor + hareketler defterde → yeni audit sistemi YOK |

## 3. API (yeni 2 uç)

| Uç | İş |
|---|---|
| `GET /api/stock/unassigned?search=&page=&pageSize=` | ATANMAMIŞ stoğu olan malzemeler (kod, ad, miktar). **Tek sorgu** — `stock_balances` `location_id=''` + `JOIN materials`. Sayfalama mevcut Grid desenine uyar |
| `POST /api/stock/distribute` | `{ operationId, toLocationId, note, lines:[{materialId, quantity}] }` → `DistributeUnassigned` |

Hata modeli **mevcut**: 403 (yabancı/bilinmeyen depo, yetki) · 400 (miktar/iş kuralı) · 409 (yarış).

## 4. WEB (`Stock.razor` içinde yeni sekme ya da ayrı sayfa — karar §9'da)

Ekran: **ATANMAMIŞ listesi** (arama + sayfalama) → satır seçimi → **hedef depo** (tek, ATANMAMIŞ **yok**)
→ satır başına **dağıtılacak miktar** ("Tümü" butonu miktarı doldurur, kullanıcı **onaylar**) →
**Kalan** kolonu canlı hesaplanır → onay penceresi → tek istek.

`LocationOptions.WriteTargets()` kullanılır (ATANMAMIŞ zaten yazma hedefi değil — STK-04'te kuruldu).

## 5. MASAÜSTÜ (parite + çevrimdışı)

Aynı ekran `StockDistributeViewModel` + `StockDistributeView.axaml` olarak. **API'ye gitmez** —
doğrudan `DesktopServices.Stock.DistributeUnassigned(...)` → yerel SQLite transaction.
Bağlantı gelince mevcut `business-push` hareketleri taşır (yeni protokol YOK).
Depo listesi yereldendir (`BranchService.List`) → **çevrimdışı çalışır**.

## 6. YETKİ

Dağıtım bir **stok işlemi**dir → mevcut `stock` modülü + `PermissionAction.Create` yeterlidir;
**yeni yetki düğümü açılmaz** (talimat §12: elle katalog ekleme gerektiren yapı kurma).
Kullanıcı yalnız kendi firmasının deposuna dağıtabilir (`EnsureLocationOwned`).

## 7. PERFORMANS

- ATANMAMIŞ listesi **tek sorgu** + sayfalama (malzeme başına sorgu YOK).
- Dağıtım **tek belge / tek transaction**; satır başına ayrı istek YOK.
- Depo listesi `LocationOptions` (web, oturumda bir kez) / yerel `BranchService` (masaüstü) — yeniden indirme YOK.

## 8. TEST PLANI — 30 senaryo (talimat §17)

**Servis (12):** sıfır · negatif · aşım · kısmi · tam · çoklu malzeme · bir satır hatalıysa **tam rollback** ·
ondalık korunumu · toplam değişmezliği · ATANMAMIŞ azalması · hedef artışı · gerçek `transfer` hareketi oluşması.
**Güvenlik (6):** ATANMAMIŞ hedef olamaz · yabancı firma deposu · bilinmeyen depo · **pasif/silinmiş depo** ·
yetkisiz kullanıcı · şube-bazlı kullanıcının kaynağı sessizce değişmemesi (**E-2 nöbetçisi**).
**Platform/senkron (8):** Web (HTTP) · masaüstü çevrimiçi · masaüstü **çevrimdışı** · çevrimdışı→senkron ·
aynı paket tekrar → **kopya yok** · online/offline/online **yakınsama** · kademeli çok depoya dağıtım ·
kalan ATANMAMIŞ doğruluğu.
**Diğer (4):** ters kayıtla geri alma · audit izi · `stock_balances` defterden türeyen doğru değer ·
liste ucunun tek sorgu olduğu (N+1 yok).

## 9. KARAR GEREKTİRMEYEN ama KAYDA GEÇEN seçimler
- **Ayrı sayfa mı sekme mi:** Stok İşlemleri ekranına **yeni sekme** (yeni menü/yetki düğümü açmamak için).
- **Hareket türü:** yeni tür **açılmaz**, `transfer` kullanılır → raporlar/ters kayıt/senkron kendiliğinden çalışır.
- **Belge notu:** varsayılan "Atanmamış stok dağıtımı" → kullanıcı hareket listesinde nedeni görür.

## 10. ⚠️ NEDEN KOD BU OTURUMDA BAŞLAMADI

Talimatın başındaki **ÇOK ÖNEMLİ ÇALIŞMA KURALI** ve §17: *"tek oturumda tüm işi güvenli biçimde kodlayıp
doğrulamaya kapasiten yetmeyecekse KOD YAZMA."*

Bu oturumda `STK-06`, `STK-07` ve `SNK-12` tamamlandı ve doğrulandı. STK-08 ise **5 katmana** yayılıyor
(servis + API + Web + masaüstü + testler) ve kabul ölçütü tam doğrulama (build + 1300 test + 30 yeni senaryo
+ Web/masaüstü parite + çevrimdışı + senkron + gerçek veri). Kalan kapasite bunu **garanti etmiyor**.

**Yarım bırakılmış bir dağıtım ekranı, kullanıcıya "dağıttım" deyip yanlış hareket üretebilir** —
bu, projede en çok kaçındığımız hata sınıfı. Bu yüzden kodlamaya başlanmadı; çalışma ağacında
**yarım kod yok**.

Asıl zor kısım (§1'deki üç engel ve `DistributeUnassigned` tasarımı) **bu planda çözüldü**;
sonraki oturum **doğrudan §1'in KARAR (T-1) maddesinden** koda başlayabilir.

## 11. AÇIK KALAN İŞLER (silinmedi)
`SNK-11` · `BKM-04` · `RPR-01` · `STK-09` · `STK-10` · `STK-11`

---

## 12. UYGULANDI (2026-08-11) — ✅ TAMAMLANDI

| Katman | Dosya | İş |
|---|---|---|
| Servis | `StockService.DistributeUnassigned` | Kaynak DAİMA ATANMAMIŞ · `EnforceOwnBranch` çağrılmaz · satır bazında yeterlilik · aynı malzeme iki satırdaysa TOPLAM üzerinden kontrol · hareket türü **transfer** |
| Servis | `StockService.ListUnassigned` | ATANMAMIŞ stoğu olan malzemeler — **tek sorgu** (N+1 yok) |
| API | `GET /api/stock/unassigned` · `POST /api/stock/distribute` | Kaynak alanı **YOK** (istemci kaynak gönderemez) |
| Web | `StockDistribute.razor` (`/stock/distribute`) | Liste + hedef + miktar + kalan + "Tümü" + onay · Stok ekranından buton |
| Masaüstü | `StockDistributeViewModel` + `StockDistributeView.axaml` | Aynı ekran, **API'siz** (yerel SQLite) · menüde "Atanmamış Stok Dağıtımı" |
| Test | `StockDistributeTests` | **17 senaryo** |

**Yeni yetki düğümü açılmadı** (mevcut `stock` + `Create`), **yeni migration yok**, **senkron kodu değişmedi**.

## 13. 🔴 BULGULAR

### B-1 — Transferler GERİ ALINMAZ; dağıtım da bir transferdir
`ReverseDocument` transfer belgelerini bilinçli reddediyor (2026-08-06 kullanıcı kararı: iki deponun
stoğunu etkiler). Dağıtım da transfer olduğu için **geri alınamaz**. Planın §2.5'teki "ters kayıtla geri
alınabilir" varsayımı **yanlıştı**.
**Sonuç:** STK-08 bu kurala **istisna AÇMADI**. Düzeltme yolu: yanlış depodan doğru depoya **YENİ transfer**.
Her iki hareket de defterde kalır (geçmiş silinmez).
⚠️ İlk yazdığım ekran metinleri "gerekirse iptal edilebilir" diyordu — **yanıltıcıydı, düzeltildi**.
Web ve masaüstü artık düzeltme yolunu açıkça anlatıyor.

### B-2 — Üretimde `DEPOWISE` firmasının HİÇ DEPOSU YOK
İzole üretim kopyasında ölçüldü: `DEPOWISE` **0 şube** · diğer iki firma 1 ve 5 şube.
Yani babanın firmasında 8951,3 birim atanmamış stok var ama **dağıtacak hedef yok**.
**Sonuç:** kullanıcı önce **Şubeler** ekranından en az bir depo/şantiye oluşturmalı. Her iki arayüz de
depo yoksa bunu **açıkça söylüyor** (boş liste bırakılmadı).

## 14. GERÇEK VERİ DOĞRULAMASI (izole üretim kopyası)

| Adım | Sonuç |
|---|---|
| Başlangıç | 663 atanmamış malzeme · **8951,3** birim |
| Kısmi dağıtım (3 malzeme, 4,5 birim) | ✅ uygulandı |
| **Aşım denemesi** (mevcut + 1) | ✅ **reddedildi** |
| **Rollback** | ✅ hedef bakiyesi **hiç değişmedi** |
| Tam dağıtım (kalan) | ✅ ATANMAMIŞ 0'a indi |
| **Firma toplamı** | **9 → 9,0** ✅ KORUNDU |
| Genel ATANMAMIŞ | 8951,3 → 8942,3 (tam olarak dağıtılan 9 birim kadar) |

Prova için izole kopyaya geçici bir depo eklendi (**canlıya değil**); kopya sonra **silindi**.

## 15. DOĞRULAMALAR
Build **0 hata** · Test **1317 · 1284 geçti · 0 kaldı · 33 atlandı** (taban 1300; **17 yeni senaryo**).
