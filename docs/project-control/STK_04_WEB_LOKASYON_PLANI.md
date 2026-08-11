# STK-04 — Web Lokasyon Desteği · Envanter + Tasarım + Uygulama Planı

> Oluşturuldu: **2026-08-11** · FAZ C · Ön koşul: `STK-01…03` ✅ (`07d77e0`, `cd5c4da`)
> **Kod yazmadan önce** çıkarılan envanterdir.

---

## 1. ENVANTER — Web'de stokla ilişkili ekranlar

| Ekran | Stok ilişkisi | Lokasyon durumu (STK-04 öncesi) |
|---|---|---|
| `Stock.razor` | Giriş · çıkış · transfer · ters kayıt · bakiye çipi · son hareketler | Şube **salt-okunur** (login şubesi) · transfer hedefi seçilir · **görünümde lokasyon yok** |
| `StockCount.razor` | Sayım | Şube gönderiliyor ama ekranda **yazmıyor** |
| `StockMovements.razor` | Hareket listesi | **Lokasyon kolonu YOK** (API'de STK-03'te hazırlandı) |
| `Daily.razor` | Depo çıkışı (issue/transfer) | Login şubesi · hedef seçilir |
| `Materials.razor` | Malzeme kartı: toplam stok, Açılış Stok alanı | **Kırılım YOK** · açılış lokasyonu **yok** |
| `StockChangeLog.razor` | Alan değişiklik günlüğü | Lokasyonla **ilgisiz** — dokunulmaz |
| `Home.razor` (Dashboard) | KPI + düşük stok uyarısı | API firma toplamı döner (STK-02'de düzeltildi) → **doğrulanacak** |
| `Reports.razor` | Stok Durumu / Şablonlu / Şablon Dışı | Firma geneli — lokasyon boyutu **STK-06** |
| `ImportExcel.razor` | "Hangi şubeye?" → içe aktarımda açılış stoğu | Zaten **zorunlu** şube seçimi var ✅ |
| `Maintenance.razor` | Bakım malzeme tüketimi | `branch_id = NULL` yazıyor → **ATANMAMIŞ'a düşer** (bulgu, §6) |
| `Requests` / `RequestOperations` | Talep → sevk/transfer | `RequestOperationsService` **zaten** `EnsureBranchOwned` yapıyor ✅ |
| `Fuel` · `Inspection` · `Vehicles` · `Personnel` · `Machines` · `Login` | `branchId` **var ama stok lokasyonu DEĞİL** | Dokunulmaz (§2) |

## 2. `branchId` anlam ayrımı (talimat madde 21)

`branchId` Web'de **beş farklı** anlama geliyor. Karıştırılmayacak:

| Anlam | Nerede | STK-04 kapsamı |
|---|---|---|
| **Stok lokasyonu** | Stock · StockCount · Daily(depo çıkışı) · açılış stoğu | ✅ **EVET** |
| Oturum çalışma şubesi | `Login.razor`, `AuthState.BranchId`, `BranchScope` | ❌ dokunulmaz |
| Kullanıcı/personel organizasyon şubesi | `Users`, `Personnel` | ❌ dokunulmaz |
| Makine ataması | `Machines` | ❌ dokunulmaz |
| Rapor filtresi | `Reports.branchIds` | ❌ STK-06 |

## 3. 🔴 BULGU — STK-03 raporundaki varsayım YANLIŞTI

STK-03'te *"bugün 'Tüm Şubeler' oturumu `branchId=null` gönderiyor, zorunlu yaparsam Web bozulur"* demiştim.
**Gerçek:** `BranchGuard.RequireBranchAsync` (2026-07-16 kullanıcı kuralı) "Tüm Şubeler" modunda stok
yazma işlemlerini **zaten tamamen engelliyor** (Stock · StockCount · Daily · Materials · Vehicles · Fuel…).

Yani Web stok yazma yolunda `branchId` **hiçbir zaman null gitmiyor** — lokasyon zaten zorunlu.
➡️ *"Lokasyon seçilmeden işlem gönderilmemeli"* kuralı **bugün de sağlanıyor**; eksik olan **görünürlük**tü.

**Bunun yerine ortaya çıkan gerçek eksik:** "Tüm Şubeler" ile giren yönetici **hiçbir stok işlemi
yapamıyor**. Çok depolu bir firmada yöneticinin depo seçip işlem yapabilmesi gerekir.

## 4. TASARIM KARARLARI

### K-1 · Yazmada lokasyon (madde 13/14)
| Kullanıcı | Davranış | Gerekçe |
|---|---|---|
| **Şubeye bağlı** | Bugünkü gibi **salt-okunur** şube alanı | 2026-08-06 kullanıcı kararı. `EnforceOwnBranch` başka şubeyi zaten 403 yapar → dropdown **sahte seçim** sunardı |
| **"Tüm Şubeler"** | **YENİ:** zorunlu lokasyon seçici; seçmeden kaydet çalışmaz | Belirsiz stok işlemi oluşmaz; yönetici artık işlem yapabilir (BranchGuard bloğu yerine açık seçim) |

**ATANMAMIŞ yazmada SEÇİLEMEZ.** Gerekçe: ATANMAMIŞ, geçmişte lokasyon *girilmemiş* olmasının dürüst
karşılığıdır — yeni kayıt için meşru bir hedef değildir. Yeni belirsizlik üretilmez.

### K-2 · "TÜM ŞUBELER" ≠ "ATANMAMIŞ" (madde 4/30)
İki kavram UI'da **asla** aynı kutuda aynı anlamda görünmez:

| Kavram | Anlamı | Nerede |
|---|---|---|
| **🌐 Tüm Şubeler** | Firmanın **tüm** lokasyonlarının **toplamı** (ATANMAMIŞ dahil) | Yalnız **görüntüleme/filtre** |
| **📦 Atanmamış** | Yalnız `locationId = ""` — lokasyonu **bilinmeyen** geçmiş stok | Görüntüleme/filtre **+ ayrı satır** |

Filtre kutusunda ikisi **ayrı ayrı** listelenir ve ATANMAMIŞ'a açıklama (tooltip) konur.

### K-3 · Lokasyon listesi tek yerden, önbellekli (madde 6/25)
Yeni `LocationOptions` servisi (scoped): şube listesini **oturumda bir kez** çeker, tüm stok ekranları
paylaşır. Ekran başına tekrar `/api/branches` çağrısı **yok**.

### K-4 · Kırılım yalnız istendiğinde (madde 25)
Malzeme kartında lokasyon kırılımı **kart açılınca tek çağrı** (`/locations`). Liste satırlarında
kırılım **çağrılmaz** → "100 malzeme × 5 lokasyon = 500 istek" yapısı oluşamaz.

## 5. UYGULAMA ADIMLARI

1. `LocationOptions` servisi (önbellekli lokasyon listesi + "Tüm Şubeler"/"Atanmamış" sabitleri).
2. `Stock.razor` — yazma lokasyonu (K-1) + seçili lokasyonun bakiye çipi.
3. `StockCount.razor` — sayılan lokasyon **ekranda açık**; firma toplamı **kullanılmaz**.
4. `StockMovements.razor` — lokasyon kolonu (`Nereden → Nereye`) + lokasyon filtresi (ATANMAMIŞ dahil).
5. `Materials.razor` — kartta **Toplam + lokasyon kırılımı**.
6. `Daily.razor` — depo çıkışında aynı lokasyon kuralı.
7. Dashboard/rapor **doğrulaması** (kod okuma + gerçek veri).
8. Testler (16 senaryo) + build + gerçek veri kontrolü.

## 6. BULGULAR → devredilen işler

| # | Bulgu | Etki | Karar |
|---|---|---|---|
| B-1 | `Maintenance` malzeme tüketimi `branch_id = NULL` yazıyor → **ATANMAMIŞ'a düşüyor** | Bakımda kullanılan malzeme hangi depodan çıktı bilinmiyor | **STK-05 sonrası ayrı iş (`BKM-04`)** — UI'da depo seçimi yok, uydurulmayacak |
| B-2 | Raporlarda lokasyon boyutu yok | Stok raporu firma geneli | **STK-06** (zaten planlı) |
| B-3 | "Tüm Şubeler" yöneticisi stok işlemi yapamıyor | Çok depolu firmada engel | **STK-04'te çözüldü** (K-1) |

## 7. KAPSAM DIŞI (bilinçli)
Masaüstü (**STK-05**) · rapor lokasyon boyutu (**STK-06**) · ATANMAMIŞ toplu dağıtımı (**STK-08/KARAR-8**) ·
bakım deposu seçimi (**B-1**) · yeni API ucu (STK-03 sözleşmesi yeterli).

---

## 8. UYGULANDI — ekran bazlı sonuç (2026-08-11)

| Ekran | Yapılan | Durum |
|---|---|---|
| **`LocationOptions`** (yeni servis) | Lokasyon listesi oturumda **bir kez** indirilir; `AllId`/`UnassignedId` sabitleri; yazma hedefleri **Atanmamış'ı içermez** | ✅ |
| `Stock.razor` | "Tüm Şubeler"de **zorunlu depo seçici** (eskiden hiç işlem yapılamıyordu) · bakiye çipi artık **seçili deponun** stoğu · transfer hedefi kaynağı dışlar | ✅ |
| `StockCount.razor` | **Sayılan depo ekranda açık** · sistem stoğu **o deponun** miktarı (`/count-sheet`) · POST'a **`branchId` eklendi** | ✅ 🔴 iki hata düzeltildi |
| `StockMovements.razor` | **Depo/Şantiye kolonu** (`Kaynak → Hedef`) · lokasyon **filtresi** (Tüm Şubeler / depo / Atanmamış) · Atanmamış'a açıklama | ✅ |
| `Materials.razor` | Kartta **Toplam + depo kırılımı** (tek istek) · **açılış stoğu deposu** zorunlu | ✅ 🔴 hata düzeltildi |
| `Daily.razor` | **Değiştirilmedi** — günlük faaliyet doğası gereği kullanıcının kendi şubesine aittir; lokasyon zaten açık ve belirsizlik yok (madde 5: her ekrana dropdown koyma) | ⚪ bilinçli |
| `Home.razor` (Dashboard) | **Değişiklik gerekmedi** — `DashboardService` STK-02'de toplayan alt sorguya geçmişti; gerçek veriyle doğrulandı (2459 malzeme, satır çoğaltma yok) | ✅ doğrulandı |
| `StockChangeLog` · `Reports` · `Fuel` · `Inspection` · `Vehicles` · `Personnel` · `Machines` · `Login` | Dokunulmadı (stok lokasyonu değil / STK-06) | ⚪ |

### Yeni API ucu (madde 26 gereği kaydedildi)
`GET /api/stock/count-sheet?locationId=&search=&limit=` — sayım listesi + **sayılan lokasyonun** miktarı.
**Neden gerekliydi:** `/api/materials` firma geneli toplam döndürür; sayımda o rakam yanlıştır. Satır başına
`/location` çağırmak N+1 üretirdi. Tek sorgu + tek `LEFT JOIN`.
`POST /api/materials` → **`openingLocationId`** (opsiyonel; yoksa Atanmamış — eski istemciler bozulmaz).

## 9. GERÇEK VERİ KONTROLÜ (üretim yedeğinin izole kopyası)

| Ölçüm | Sonuç |
|---|---|
| Migration sonrası bakiye satırı | 664 → **665** · uyuşmayan **0** · toplam korundu |
| **DEPOWISE firması** — Tüm Şubeler toplamı | **8951,3** |
| **DEPOWISE firması** — ATANMAMIŞ | **8951,3** (663 satır) · gerçek depo **0** |
| Diğer firma | Atanmamış 2 · bir şubede −1 |
| **3 firma toplamı (ATANMAMIŞ)** | **8953,3** |

⚠️ **Rakam düzeltmesi:** Önceki raporlarda geçen **8953,3** değeri **üç firmanın toplamıdır**;
babanın firmasının (`DEPOWISE`) kendi ATANMAMIŞ stoğu **8951,3**'tür. Değer **değiştirilmedi**, yalnız
doğru firmaya atfedildi.

Bugün DEPOWISE'ta gerçek depo stoğu **sıfır** olduğu için "Tüm Şubeler" ile "Atanmamış" **sayısal olarak
eşit** görünür — ama **kavramsal olarak ayrıdır** ve arayüzde ayrı ayrı gösterilir. Testler bu ayrımı
ikisinin FARKLI olduğu veriyle kilitler (15 ≠ 1).

Prova sonrası kopya veritabanı **silindi**, yerel sunucu **durduruldu**. Canlıya bağlanılmadı.
