# Çok Şubeli Stok — İzole Test Raporu (STK-MB)

- **Tarih:** 2026-08-12
- **Kapsam:** Çok lokasyonlu stok mimarisi (`material_id + company_id + location_id`)
- **Ortam:** İzole geçici SQLite (test başına ayrı dosya). **Production'a bağlanılmadı, yazılmadı.**
- **Son güncelleme:** 2026-08-12 — H-1 düzeltmesi eklendi (bkz. belge sonundaki ek)
- **Yeni test dosyaları:** `MultiBranchStockScenarioTests.cs` (22 test) + `UnassignedListLimitTests.cs` (15 test, H-1)
- **Sonuç:** yeni testler **37/37 GEÇTİ** · tüm paket **1591 geçti / 0 başarısız / 35 atlandı (hepsi PostgreSQL)**

---

## Doğrulanan model

```
MALZEME
  ├── ANKARA GENEL MERKEZ: 10
  ├── DÜZCE: 5
  ├── KARAMAN: 0
  ├── NEVŞEHİR: 2
  └── TEST ŞANTİYE: 7
```

Her testte üç kaynak birden doğrulandı:

1. `stock_balances` ham satırları (lokasyon → miktar)
2. `StockService.GetBalance` (servisin firma-geneli toplamı)
3. `stock_movements` hareket defteri (lokasyon bazında `Σ direction × quantity`)

**Muhasebe eşitliği** her adımda zorunlu:
`firma toplamı = ATANMAMIŞ + ANKARA + DÜZCE + KARAMAN + NEVŞEHİR + TEST ŞANTİYE`

---

## A) Başarılı testler

| # | Senaryo | Başlangıç | İşlem | Beklenen | Gerçek | Sonuç |
|---|---|---|---|---|---|---|
| S01 | Başlangıç dağıtımı | Atanmamış 24 | →ANK 10, DÜZ 8, KAR 6 | 3 ayrı satır, toplam 24 | aynı | PASS |
| S02 | Kısmi dağıtım | Atanmamış 24 | →NEV 9 | NEV 9 · Atanmamış 15 | aynı | PASS |
| S03 | 4 lokasyon aynı anda | Atanmamış 21 | 10/5/4/2 | 4 kova + kırılım adlı | aynı | PASS |
| S04 | Şube tükenmesi | ANK 10 · DÜZ 5 · ŞNT 7 | DÜZ −5 | DÜZ **tam 0**, diğerleri sabit, toplam 17 | aynı | PASS |
| S05 | Negatif koruma | DÜZ 0 | DÜZ −1 | red + **kısmi yazma yok** | belge/hareket sayısı değişmedi | PASS |
| S06 | Şubeler arası transfer | ANK 10 · DÜZ 0 | ANK→DÜZ 3 | ANK 7 · DÜZ 3 · **toplam sabit** | aynı | PASS |
| S07 | Kısmi transfer | ANK 10 | ANK→NEV 4 | ANK 6 · NEV 4 | aynı | PASS |
| S08 | Çok lokasyonlu çıkış | ANK 6 · DÜZ 4 · KAR 3 | DÜZ −2 | DÜZ 2, **ANK/KAR değişmez** | aynı | PASS |
| S09 | Şube bazlı raporlama | 4 kova | 5 rapor modu | tek satır / kırılım / toplam kopmaz | aynı | PASS |
| S10 | Uzun dizi (7 işlem) | — | açılış→dağıtım×2→transfer→giriş→çıkış→sayım | her adımda eşitlik | aynı + `RecomputeBalances` kırılımı korudu | PASS |
| S11 | Atomiklik | 10/10/1 | 3 satırlı dağıtım, 1'i geçersiz | **tamamı geri alınır** | belge/hareket 0 arttı | PASS |
| S12 | Idempotency | ANK 10 | aynı `operation_id` ×2 | tek belge, tek düşüm | aynı doc id/no | PASS |
| S13 | Eşzamanlılık (çıkış) | ANK 10 | paralel 7 + 7 | **1 başarılı**, negatif yok | 1/1, ANK=3 | PASS |
| S13b | Eşzamanlılık (dağıtım) | Atanmamış 10 | paralel 7 + 7 | 1 başarılı | Atanmamış=3 | PASS |
| S14 | Sıfır stok | DÜZ 5→0 | liste/transfer/giriş/sayım | listede yok, transfer red, giriş serbest | aynı | PASS |
| S15 | Atanmamış kaynak | Atanmamış 12 | boş kaynak / boş hedef | üçü de red | aynı (**biri düzeltmeyle**) | PASS |
| S16 | Silme kapısı (MLZ-01) | stok + hareket | `Delete` | engellenir | "stokta … / … stok hareketi" | PASS |
| S16b | Devralınan silinmiş malzeme | ANK 2, `is_deleted=1` | listeler/rapor | veri durur, ekranda yok | **rapor-ham fark 2** ölçüldü | PASS (bulgu) |
| S17 | Şubeye bağlı kullanıcı | ANK 10 · DÜZ 10 | 4 yetki senaryosu | yalnız kendi şubesi | 403'ler doğru | PASS |
| S18 | Tek-lokasyon varsayımı | 5 kova | grid + kart + toplu okuma | tek satır, 24 | aynı, ondalık 0.3 tam | PASS |
| S18b | 3 malzeme × 5 lokasyon | 15 kova | 1 kovadan çıkış | 14 kova sabit | aynı | PASS |
| S19 | Dağıtım listesi kesilmez | 520 malzeme | `ListUnassignedPage` | tamamı + sayım bilgisi | 520 · "520 kayıt bulundu." | PASS (H-1 sonrası) |

## B) Başarısız testler

**Yok.** (İlk koşuda 2 test kırmızıydı; ikisi de benim beklentimdi — biri gerçek bir kod açığını ortaya
çıkardı ve düzeltildi, diğeri mevcut davranışın daha güçlü olduğunu gösterdi ve test gerçeğe uyarlandı.
Hiçbir test gevşetilmedi, atlanmadı, retry ile yeşile boyanmadı.)

## C) Şüpheli / eksik testler

1. **PostgreSQL tarafı koşulamadı.** 35 PG testi atlandı: ortamda PostgreSQL sunucusu, `psql` ve Docker
   yok; `DEPOWISE_PG_URL` / `DEPOWISE_PG_TEST_CONFIRM` tanımsız. **Canlı veritabanı test için
   kullanılmadı.** Üretim PostgreSQL olduğu için bu gerçek bir kapsam boşluğudur.
   *Not:* eşzamanlılık davranışı iki lehçede FARKLIDIR — SQLite'ta `BeginImmediate` tek yazar
   bırakır, PostgreSQL'de CAS/retry asıl işini orada yapar. S13/S13b SQLite'ta geçti; PG'de
   `PostgresStockConcurrencyTests` aynı sınıfı zaten kapsıyor ama **bu koşuda çalıştırılamadı**.
2. **Gerçek arayüz (GUI) tıklama testi yapılmadı** — masaüstü Avalonia penceresi ve web tarayıcısı bu
   ortamda açılmadı. Masaüstü/web davranışı **ViewModel + servis + API sözleşmesi** seviyesinde
   doğrulandı. Yapılmamış görsel kontrol yapılmış gibi raporlanmadı.

## D) Kod hataları (bulunan)

**D-1 — `Transfer` hedefi boş bırakılabiliyordu (DÜZELTİLDİ).**
`StockService.Transfer` kaynağın boş olmasını reddediyor, ama **hedefin** boş olmasını kontrol etmiyordu.
Boş hedef `ApplyLine → ApplyDelta` yolunda sessizce `""` (ATANMAMIŞ) kovasına çevriliyordu → **transfer,
stoğu depodan çıkarıp "lokasyonu bilinmiyor" durumuna geri atabiliyordu.** Bu, STK-08'in ortadan
kaldırmaya çalıştığı belirsizliği yeniden üretirdi ve `DistributeUnassigned`'ın açıkça reddettiği
durumun aynısıydı (aynı kavram, iki farklı kural).

- **Erişilebilirlik:** API (`/api/stock/transfer`) ve masaüstü (`StockEntryViewModel`) hedefi zaten
  zorunlu tutuyor → **bugün kullanıcı arayüzünden tetiklenemiyordu.** Eksik olan **servis katmanındaki
  savunma katmanıydı** (masaüstü bu servisi çevrimdışı doğrudan çağırıyor).
- **Düzeltme:** `src/DepoWise.Infrastructure/Materials/StockService.cs` — `Transfer` içine
  `DistributeUnassigned` ile **birebir aynı** mesajı veren kapı eklendi (8 satır, davranış değişikliği
  yalnız reddetme yönünde). Mevcut çağıranların hiçbiri boş hedef göndermiyor (repo geneli tarandı).
- **Regresyon kilidi:** `S15` testi hem `""` hem `null` hedefi reddettiğini doğrular.

## E) Mimari riskler

**E-1 — "Tek lokasyon" varsayımı: kalmadı.** Denetlenen 10 dosyada `stock_balances`'a dokunan her yol
kontrol edildi:
- 8 adet `LEFT JOIN stock_balances` → `SqlDialect.StockTotalSubquery` (malzeme başına tek satır)
- Doğrudan kullanımlar (`ListUnassigned`, `GetLocationBalances`, `GetCountSheet`, `GetBalancesByLocation`,
  `GetBalances`, `ReportService.StockStatusByLocation`) **lokasyon anahtarlı** → çoğaltma yok
- Yazma yolları (`StockBalanceWriter`, `StockService`, `OpeningStockService`, `MaintenanceService`)
  lokasyonu **açıkça** alır; varsayılan değer yok, rastgele şube seçimi yok
- `SqlDialect.StockTotalSubquery` toplamayı SQL'de yapar ve **6 ondalıkla metne** çevirir; yazma
  yollarında SQL toplaması kullanılmaz (C# `decimal`). S18(c) ondalık kaybı olmadığını doğruladı.

**E-2 — Bakım tüketimi negatife izin verir, stok çıkışı vermez (bilinçli asimetri).**
`MaintenanceService` `allowNegative: true` ile çalışır (ADR-086 / kullanıcı kararı 2026-08-06: bakım iş
akışı stok yüzünden durmamalı), `StockService.IssueOut` ise şube bakiyesini kontrol eder. Yani **bir
şube bakiyesi bakım üzerinden eksiye düşebilir.** Bu tasarım kararıdır; `MaintenanceStockLocationTests`
(26 test) kapsıyor. Çok şubeli dağıtımda etkisi: eksiye düşmüş bir şube kovası dağıtım/transfer
kaynağı olamaz — doğru davranış.

**E-3 — `stock_balances` senkronda taşınmaz (SNK-11); sunucu defterden yeniden hesaplar.**
`RecomputeBalances` lokasyon kırılımını korur (S10 doğruladı). Çok makineli senaryoda kırılımın
korunması `SyncStockLocationCertificationTests` ile ayrıca kilitli.

## F) Masaüstüne özel sorunlar

- **F-1 (bulgu):** `StockDistributeViewModel.Load()` → `ListUnassigned(session, search)` **varsayılan
  limit 500**. Canlıda ATANMAMIŞ'ta **676 satır** var → **176 satır ekranda hiç görünmez ve kullanıcı
  uyarılmaz.** Ekran "hepsi bu" izlenimi verir. Servis 2000'e kadar destekliyor ama çağıran kullanmıyor.
  (S19 bunu ölçtü: 520 malzemede 500 döndü, kesildiğine dair sinyal yok.)
- Diğer masaüstü stok ekranları lokasyon-doğru: `StockEntryViewModel` `GetBalanceAt(login şubesi)`,
  `StockCountViewModel` `GetBalanceAt(sayım lokasyonu)`, `MaterialsViewModel` `GetLocationBalances`
  kırılım paneli, `StockMovementsViewModel` sunucu-taraflı lokasyon filtresi (STK-10b-4).

## G) Web'e özel sorunlar

- **G-1:** Aynı 500 limiti web'de de var — `StockDistribute.razor` `/api/stock/unassigned` çağrısına
  `limit` **hiç göndermiyor** → sunucu 500 uyguluyor. Masaüstüyle aynı sessiz kesilme.
- Web parite tamam: `Materials.razor` `/api/stock/balance/{id}/locations` ile kırılım gösteriyor,
  `StockMovements.razor` lokasyon filtresini sunucuya indiriyor (B-1 kapandı), `Stock.razor` yeni
  kayıtta boş lokasyon göndermiyor.

## H) Düzeltilmesi gerekenler (karar sizin — **uygulanmadı**)

| # | Konu | Neden önemli | Öneri |
|---|---|---|---|
| ~~H-1~~ | ~~Dağıtım listesi 500'de sessiz kesiliyor~~ | — | **✅ KAPANDI 2026-08-12** — bkz. aşağıdaki "H-1 Düzeltme Eki" |
| H-2 | Devralınan `is_deleted=1` + bakiyesi olan malzeme (S16b) | Stok tabloda duruyor ama hiçbir ekranda/raporda görünmüyor → depo raporu toplamı ham bakiyeden ayrışıyor (canlıdaki "TEST" +2) | Karar gerekiyor: (a) yönetici için "silinmiş ama stoklu" raporu, (b) malzemeyi geri aç + stoğu sıfırla, (c) olduğu gibi bırak |
| H-3 | PostgreSQL testleri koşulamadı (C-1) | Üretim PG; eşzamanlılık davranışı lehçeye bağlı | Boş bir test PG veritabanı sağlanırsa 35 test koşulur |

## I) Düzeltilmesine gerek olmayan mevcut davranışlar

- **Sıfır bakiyeli satır silinmiyor.** Kova 0'a düşünce satır kalır (S04). Doğru: geçmiş ve
  raporlanabilirlik korunur; dağıtım listesi zaten 0'ları göstermiyor.
- **Transfer geri alınamaz.** `ReverseDocument` `doc_type == "transfer"` için reddeder. Bilinçli
  (2026-08-06 kararı): iki deponun stoğunu etkiler. Düzeltme = yeni ters yönlü transfer.
- **Negatif kalemler dağıtım listesinde görünür ama dağıtılamaz.** ADR-086 devralınan eksik stok;
  kullanıcı görmeli.
- **Bakımda negatif stoğa izin (E-2).** Kullanıcı kararı.
- **Malzeme kartı/grid firma-geneli toplam gösterir**, kırılım ayrı panelde. Doğru: liste satır
  çoğaltmaz, detay ister isteyen kırılımı görür.

---

## Coverage Matrix (§7.13)

| Alan | Durum | Alan | Durum |
|---|---|---|---|
| Form Açıldı | — (GUI koşulmadı) | Database | ✅ ham tablo + defter çift kontrol |
| Yeni Kayıt | ✅ giriş/açılış | Offline | ✅ servis katmanı (masaüstü aynı yolu kullanır) |
| Düzenleme | ✅ transfer/sayım | Sync | ✅ mevcut sertifikasyon testleriyle (yeniden koşuldu) |
| Silme | ✅ MLZ-01 kapısı | Performans | ✅ N+1 yok (tek sorgu yolları doğrulandı) |
| Arama | ✅ `ListUnassigned(search)` | UI | — (GUI koşulmadı) |
| Filtre | ✅ rapor lokasyon filtresi 5 mod | UX | ⚠️ H-1 (sessiz kesilme) |
| Grid | ✅ `SearchGrid` tek satır | Security | ✅ yetki 403 + tenant izolasyonu (mevcut testler) |
| Doğrulamalar | ✅ negatif/sıfır/boş hedef | Yetki | ✅ S17 şube kapsamı |
| Hata Mesajları | ✅ teknik olmayan, malzeme/miktar içeriyor | | |

**Çalıştırılan senaryo sayısı:** 22 yeni test (≈95 ayrı iddia) + 1575 mevcut test regresyonu.

---

## Yapılmayanlar (kullanıcı kuralı)

Production bağlantısı **0** · production yazma **0** · STK-08 dağıtımı **0** · migration **0** ·
deploy **0** · desktop publish **0** · update paketi **0** · `git commit/push` **0**.

Değişen üretim kodu: `src/DepoWise.Infrastructure/Materials/StockService.cs` (+8 satır, D-1 kapısı).
Yeni test: `tests/DepoWise.Tests/MultiBranchStockScenarioTests.cs`.

---

# H-1 DÜZELTME EKİ — Dağıtım listesi sessiz kesilmesi (2026-08-12)

**Durum: KAPANDI.** Yeni test dosyası: `tests/DepoWise.Tests/UnassignedListLimitTests.cs` (15 test, 15 geçti).

## Bulunan iki hata

**Hata 1 — sessiz kesilme.** `ListUnassigned` varsayılan 500 satır döndürüyordu; web ve masaüstü limiti
yükseltmiyor, kaç kaydın gizlendiğini söyleyen hiçbir bilgi taşımıyordu.

**Hata 2 — SIFIR SATIRLARI LİMİTTEN YER KAPIYORDU (daha sinsi, ilk raporda görülmemişti).**
`qty == 0` elemesi SQL'de değil, `LIMIT` uygulandıktan **sonra** C#'ta yapılıyordu. Dağıtımı biten kalemler
ATANMAMIŞ'ta 0 satırı olarak kalır (bilinçli davranış) → **çok turlu dağıtımın ikinci turunda liste
sıfırlarla dolup gerçek kalemleri dışarı itebilirdi.** İzole testte kanıtlandı: 500 sıfırlanmış + 10 pozitif
kalemde eski yol **hiçbir pozitif kalem döndürmüyor** (ekran boş → "dağıtım bitti" sanılır); yeni yol 10'unu
da gösteriyor.

## Yapılan değişiklik (mimari korundu, yeni katman eklenmedi)

| Dosya | Değişiklik |
|---|---|
| `SqlDialect.cs` | `NumericValue(conn, expr)` — TEXT miktarın sayısal karşılığı, **yalnız filtre/sayım için** (okuma yolu hâlâ `Money.Parse`) |
| `StockService.cs` | Sıfır filtresi **LIMIT'ten önce SQL'e** indi · yeni `UnassignedPage` kaydı · yeni `ListUnassignedPage` · `DefaultUnassignedLimit=500` / `MaxUnassignedLimit=2000` sabitleri · eski `ListUnassigned` imzası **aynen korundu** (yeni yola delege eder) |
| `Api/Program.cs` | `/api/stock/unassigned` artık nesne döner (`items,total,distributable,shown,hidden,truncated,limit,countText`); ekran varsayılanı **2000** |
| `StockDistribute.razor` | `items` okur · sayım kutusu; sığmayan kayıt varsa **uyarı rengine** döner ve ne yapılacağını yazar |
| `StockDistributeViewModel.cs` | `ListUnassignedPage` kullanır · `CountText` + `Truncated` |
| `StockDistributeView.axaml` | Sayım metni ekranda; kesilme varsa vurgulu (DangerBrush) |

**Metin tek kaynaktan** (`UnassignedPage.CountText`) → web ve masaüstü **aynı cümleyi** gösterir:
`"676 kayıt bulundu."` / `"676 kayıt bulundu. 500 kayıt gösteriliyor (176 kayıt ekranda değil)."`

## Test sonuçları (A–I)

| Senaryo | Sonuç |
|---|---|
| **0a** Eski davranışın yeniden üretimi | 520 kalemde eski yol 500 döndü, sinyal yok → **yeni yol 520 + "520 kayıt bulundu."** |
| **0b** Sıfırlar limitten yer kapıyor | 500 sıfır + 10 pozitif → **eski yol 0 kalem**, yeni yol **10 kalem** |
| **A** 499 kayıt | 499 gösterildi · kesilme yok |
| **B** tam 500 kayıt | 500 gösterildi · kesilme yok |
| **C** 501 kayıt | 501 gösterildi · kesilme yok (dar pencerede 1 gizli **ve söyleniyor**) |
| **D** 676 kayıt | 676 gösterildi · kesilme yok |
| **E/G** gerçek STK-08 dağılımı | aşağıdaki tablo |
| **F** ilk 500 dışındaki kalemin aranması | dar pencerede görünmüyor, **arama buluyor** (SQL'de aranıyor) |
| **F2** arama sıfır/silinmiş getirmiyor | doğrulandı · sonuç yoksa "Dağıtılacak atanmamış stok yok." |
| **Çok tur** 200'lük dar pencere ile 610 kalem | **610/610 dağıtıldı**, hiçbiri gözden kaçmadı; sonda 66 negatif kaldı, dağıtılabilir 0 |
| **H** Web/API | sözleşme taraması: `ListUnassignedPage` · `countText` · `truncated` · `items` |
| **I** Desktop | sözleşme taraması: `ListUnassignedPage` · `CountText` · `Truncated` · AXAML binding |
| Geriye uyum | `ListUnassigned` varsayılanı hâlâ 500 · üst sınır 2000 aşılamıyor |
| Yetki | yetkisiz kullanıcı yeni yolda da 403 |
| Ölçekli sıfır ("0.000") | sayısal filtre eliyor (metin karşılaştırması eleyemezdi) |

### STK-08 gerçek dağılımı — ölçülen sayılar

677 ham ATANMAMIŞ bakiye satırı (610 pozitif + 66 negatif + 1 silinmiş malzeme) birebir kuruldu:

| Ölçüm | Değer |
|---|---|
| Ham bakiye satırı (`location_id=''`) | **677** |
| Toplam kayıt sayısı (listelenebilir) | **676** (silinmiş malzeme iş kuralı gereği elenir) |
| Görünen kayıt sayısı | **676** |
| Dağıtılabilir kayıt sayısı | **610** |
| Gizli kalan kayıt sayısı | **0** |
| Arama ile erişilebilen pozitif kayıt (`"P-"`) | **610** |
| Negatif (görünür, dağıtılamaz) | 66 |
| Silinmiş malzeme (hiç görünmez, aramayla da bulunmaz) | 1 |

## Regresyon

- Tüm paket: **1591 geçti · 0 başarısız · 35 atlandı (hepsi PostgreSQL)** — H-1 öncesi 1575'ti (+16 yeni test).
- Stok grupları hedefli koşu (`Unassigned*`, `MultiBranch*`, `StockDistribute*`, `StockLocation*`,
  `ApiStockLocation*`, `StockConcurrency*`, `SyncBalancePayload*`): **133 geçti · 0 başarısız · 3 atlandı (PG)**.
- `DepoWise.sln` tamamı derlendi; `DepoWise.Desktop` (Avalonia XAML derlemesi dâhil) hatasız.

## Bu turda da yapılmayanlar

Production bağlantısı **0** · production INSERT/UPDATE/DELETE **0** · STK-08 dağıtımı **0** ·
migration **0** · deploy **0** · desktop publish **0** · update paketi **0** · `git commit/push` **0**.

**Tarayıcı/GUI ile görsel doğrulama YAPILMADI** — web uygulaması uzak API'ye (production) bağlandığı için
başlatılmadı. Web ve masaüstü davranışı ViewModel + API sözleşmesi + kaynak taraması seviyesinde
doğrulandı. Yapılmamış görsel kontrol yapılmış gibi raporlanmadı.
