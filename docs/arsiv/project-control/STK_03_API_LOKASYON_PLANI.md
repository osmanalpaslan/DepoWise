# STK-03 — API Lokasyon Boyutu · Envanter + Sözleşme + Uygulama Planı

> Oluşturuldu: **2026-08-11** · FAZ C · Ön koşul: `STK-01` + `STK-02` ✅ (commit `07d77e0`)
> **Kod yazmadan önce** çıkarılan envanterdir. Sohbet hafızası kaynak değildir.

---

## 1. ENVANTER — stokla ilişkili TÜM API uçları

Kaynak: `src/DepoWise.Api/Program.cs` (minimal API; ayrı controller yok).

### 1.1 Yazma uçları (stok hareketi üretir)

| Uç | DTO | Lokasyon alanı | Servis | Durum |
|---|---|---|---|---|
| `POST /api/stock/receive` | `StockReceiveDto` | `BranchId` | `Stock.ReceiveIn` | Taşıyor, **doğrulanmıyor** |
| `POST /api/stock/issue` | `StockMoveDto` | `BranchId` | `Stock.IssueOut` | Taşıyor, **doğrulanmıyor** |
| `POST /api/stock/transfer` | `StockTransferDto` | `FromBranchId`, `ToBranchId` | `Stock.Transfer` | Taşıyor, **doğrulanmıyor** |
| `POST /api/stock/count` | `StockCountDto` | `BranchId` | `Stock.Count` | Taşıyor, **doğrulanmıyor** |
| `POST /api/stock/reverse` | `StockReverseDto` | — (belgeden çözülür) | `Stock.ReverseDocument` | Doğru (orijinal lokasyona döner, STK-02) |
| `POST /api/stock/change-log` | `StockChangeLogDto` | — | `Stock.LogChange` | Lokasyonla ilgisiz (alan değişikliği günlüğü) |

### 1.2 Okuma uçları (bakiye gösterir)

| Uç | Bugünkü anlam | Lokasyon |
|---|---|---|
| `GET /api/stock/balance/{materialId}` | Firma geneli toplam | **Kırılım YOK** |
| `GET /api/stock` · `GET /api/stock/movements` | Hareket listesi | **Lokasyon alanı YOK** |
| `GET /api/materials` (hızlı arama) | Liste + toplam stok | Firma geneli (doğru) |
| `GET /api/materials/grid` · `/grid/export` | Liste ekranı | Firma geneli (doğru) |
| `GET /api/materials/{id}` | Malzeme kartı | Toplam var, **kırılım YOK** |
| `GET /api/materials/{id}/movements` | Kart "Son Hareketler" | **Lokasyon alanı YOK** |
| `POST /api/reports/{type}` | Stok Durumu / Şablonlu / Şablon Dışı | Firma geneli (STK-06'nın işi) |

### 1.3 İstemci kullanımı — **kritik envanter bulgusu**

| İstemci | Stok uçlarını kullanıyor mu? |
|---|---|
| **Web** (`DepoWise.Web`) | **EVET** — `Stock.razor` (receive/issue/transfer/reverse/balance) · `StockCount.razor` (count) · `StockMovements.razor` · `Daily.razor` (issue/transfer) · `StockChangeLog.razor` |
| **Masaüstü** (`DepoWise.Desktop`) | **HAYIR — tek bir `/api/stock/*` çağrısı yok.** Stok işlemleri **yerel** `StockService` (SQLite) ile yapılır; sunucuya `business-push`/`business-pull` ile **`stock_movements` olarak** gider. |

➡️ **Sonuç:** STK-03'ün uç değişiklikleri masaüstünün stok akışını **yapısal olarak bozamaz**.
Masaüstünün API yüzeyi: auth · branches · companies · users · permissions · machines · sync · lookups.
**Çevrimdışı mimari korunur** (madde 9) — çünkü zaten API'ye bağlı değil.

---

## 2. BULGU — 🔴 Lokasyon sahiplik doğrulaması YOK

`StockService.ReceiveIn / IssueOut / Transfer / Count` gönderilen `branchId`'nin **oturumun firmasına ait
olup olmadığını kontrol etmiyor**. Karşılaştırma: `RequestOperationsService` aynı iş için
`EnsureBranchOwned` çağırıyor (satır 160-161, 226-227) — desen projede **var**, stok yoluna bağlanmamış.

| | Önce (STK-02 öncesi) | Şimdi (STK-02 sonrası) |
|---|---|---|
| Etki | `stock_movements.branch_id` yabancı şube olur (yanlış referans) | **Ayrıca `stock_balances.location_id`** yabancı şube olur — artık **birincil anahtar kolonu** |

Yani lokasyon boyutu, var olan bir referans sızıntısını **yapısal** hâle getirdi. STK-03'ün asıl işi budur.

**Karar:** Doğrulama **servis katmanına** (`StockService`) konur, API katmanına değil.
**Gerekçe:** masaüstü aynı servisi çevrimdışı kullanıyor → koruma iki istemcide de aynı olur; API'ye
konsaydı masaüstü korumasız kalırdı. Bu, mevcut yetki mimarisini **değiştirmez**, yalnız var olan
`EnsureBranchOwned` desenini stok yoluna **bağlar** (madde 11).

---

## 3. API SÖZLEŞMESİ — uç bazında karar

| Uç | İstek | Yanıt | Filtre | Lokasyon verilmezse |
|---|---|---|---|---|
| `POST /api/stock/receive` | `branchId` **opsiyonel**, verilirse **doğrulanır** | değişmez | — | ATANMAMIŞ (`''`) — bugünkü davranış |
| `POST /api/stock/issue` | aynı | değişmez | — | ATANMAMIŞ |
| `POST /api/stock/transfer` | `fromBranchId`+`toBranchId` **zorunlu** (bugün de öyle), **doğrulanır** | değişmez | — | 400 (mevcut kural) |
| `POST /api/stock/count` | `branchId` opsiyonel, **doğrulanır** | değişmez | — | ATANMAMIŞ kovası sayılır |
| `GET /api/stock/balance/{id}` | — | **değişmez** `{ balance }` | — | — |
| `GET /api/stock/balance/{id}/locations` | **YENİ** | `{ total, locations:[{locationId, locationName, quantity}] }` | — | — |
| `GET /api/stock/balance/{id}/location` | **YENİ** `?locationId=` | `{ locationId, locationName, balance }` | `locationId` | ATANMAMIŞ okunur |
| `GET /api/stock/movements` · `/api/stock` | — | **+`locationId`,`locationName`,`fromLocationId`,`fromLocationName`** | mevcut şube kapsamı | `null` / `"Atanmamış"` |

### 3.1 Neden üç ayrı bakiye ucu (madde 6)
Aynı ucu "genel toplam / tek lokasyon / kırılım" diye üç anlamda kullanmak sözleşmeyi belirsizleştirir.
Her anlam **kendi ucunu** alır; `{id}` ucu **hiç değişmez** → eski Web sürümü aynen çalışır.

### 3.2 `ATANMAMIŞ` (`''`) kullanımı (madde 3)
Yalnız **iş kuralının gerçekten gerektirdiği** yerde: kullanıcı "Tüm Şubeler" ile çalışıyorsa
(`OperatingBranchId` null) lokasyon **bilinmiyordur** — uydurulmaz, ATANMAMIŞ'a yazılır.
`''` **istekte açıkça gönderilebilen bir değer değildir**; yokluğun karşılığıdır.

### 3.3 Zorunlu lokasyon neden ŞİMDİ dayatılmıyor
`receive`/`issue` için lokasyonu zorunlu yapmak, "Tüm Şubeler" oturumundaki Web ekranını **anında bozar**
(bugün `branchId = null` gönderiyor). Bu bir **UI kararı**dır → `STK-04`. STK-03 sözleşmeyi hazırlar,
dayatmayı STK-04'e devreder. **Bağımlılık kaydedildi** (madde 21).

### 3.4 Hata modeli (madde 16)
Yeni model **icat edilmedi**. Mevcut middleware sözleşmesi kullanılır:
`ForbiddenException` → **403** `{"error":"…"}`; iş kuralı/doğrulama → **400**.
Bilinmeyen **veya** başka firmaya ait lokasyon → **403** (`EnsureBranchOwned` ile birebir aynı mesaj deseni).
Ayrım yapılmaz: "var mı yok mu" bilgisini sızdırmamak zaten mevcut desenin tercihi.

---

## 4. GERİYE DÖNÜK UYUMLULUK (madde 8)

| Senaryo | Sonuç |
|---|---|
| Eski Web, `branchId` göndermez | ATANMAMIŞ — **bugünkü davranışın aynısı** ✅ |
| Eski Web, kendi şubesini gönderir | Doğrulamadan geçer, aynen çalışır ✅ |
| Yeni Web, yeni uçları çağırır | Yeni uçlar eklendi ✅ |
| Eski istemci, **başka firmanın** şubesini gönderir | **403** — davranış değişikliği, ama bu bir **güvenlik düzeltmesidir** |
| Masaüstü (her sürüm) | Stok uçlarını **kullanmıyor** → etkilenmez ✅ |

**Karar:** Zorunlu güvenlik kontrolü, "eski istemci çalışsın" diye opsiyonel yapılmadı (kullanıcı talimatı).
Meşru bir istemcinin başka firmanın şubesini göndermesi zaten **hatadır**; sessizce yazmak veri bozardı.

---

## 5. SENKRON (madde 10)
`stock_movements` şeması **değişmiyor**, DTO'lar sync'e girmiyor (sync tablo/kolon bazlı çalışır, API DTO'su
kullanmaz). ➡️ **Sync kodunda değişiklik YOK.** Kesişim analiz edildi, yok.

## 6. PERFORMANS (madde 14-15)
- `GetBalancesByLocation` zaten **tek sorgu**.
- Lokasyon **adları** için satır başına sorgu **YASAK** → hareket listesinde `LEFT JOIN branches` (aynı sorgu),
  kırılım ucunda **tek toplu** şube okuması.
- `/api/materials` ve `/grid` toplamları **değişmiyor** (STK-02'de zaten tek sorgu).

---

## 7. UYGULAMA ADIMLARI

1. `StockService`: `EnsureLocationOwned` + 4 yazma yolunda çağrı (transfer'de kaynak **ve** hedef).
2. `StockMovementRow` + `SearchMovements`: lokasyon alanları (`LEFT JOIN branches`, N+1 yok).
3. API: 2 yeni bakiye ucu (`/locations`, `/location`).
4. Testler (17 senaryo — §13).
5. Build + tam takım (taban **1223/1190/0/33** korunacak).
6. Dokümantasyon + project-control güncellemesi.

---

## 8. UYGULANAN SÖZLEŞME — istek/yanıt örnekleri

> Projede OpenAPI/Swagger **yoktur** (CLAUDE.md §4 — mevcut durum). API sözleşmesinin yazılı kaydı budur.

### 8.1 Yazma — lokasyon `branchId` alanıyla taşınır (alan adı DEĞİŞMEDİ)

```jsonc
// POST /api/stock/receive      → giriş, "depoA" deposuna
{ "operationId": "…", "materialId": "…", "quantity": 10, "branchId": "depoA", "personnelId": "…" }

// POST /api/stock/issue        → çıkış, YALNIZ "depoA"nın stoğundan düşer
{ "operationId": "…", "materialId": "…", "quantity": 3, "branchId": "depoA", "personnelId": "…" }

// POST /api/stock/count        → sayım, "depoA"nın mevcut miktarıyla karşılaştırılır
{ "operationId": "…", "reason": "sayım", "branchId": "depoA",
  "lines": [ { "materialId": "…", "countedQuantity": 12 } ] }

// POST /api/stock/transfer     → kaynak ve hedef AYRI alanlarda
{ "operationId": "…", "fromBranchId": "depoA", "toBranchId": "depoB",
  "lines": [ { "materialId": "…", "quantity": 4 } ] }
```

`branchId` **yoksa veya null ise** → **ATANMAMIŞ** (`""`). Reddedilmez; bugünkü davranışın aynısıdır.
`branchId` **doluysa** firmaya ait olduğu doğrulanır → değilse **403** `{"error":"Şube bulunamadı veya başka firmaya ait."}`

### 8.2 Okuma — üç ayrı anlam, üç ayrı uç

```jsonc
// GET /api/stock/balance/{materialId}                 → FİRMA GENELİ (DEĞİŞMEDİ)
{ "balance": 14 }

// GET /api/stock/balance/{materialId}/locations       → KIRILIM (ATANMAMIŞ en sonda)
{ "materialId": "…", "total": 15,
  "locations": [ { "locationId": "depoA", "locationName": "Depo A", "quantity": 10 },
                 { "locationId": "depoB", "locationName": "Depo B", "quantity": 4 },
                 { "locationId": "",      "locationName": "Atanmamış", "quantity": 1 } ] }

// GET /api/stock/balance/{materialId}/location?locationId=depoA   → TEK LOKASYON
{ "materialId": "…", "locationId": "depoA", "locationName": "Depo A", "balance": 10 }
// locationId verilmezse ATANMAMIŞ okunur:
{ "materialId": "…", "locationId": "",      "locationName": "Atanmamış", "balance": 1 }
```

`total`, kırılımın C#/decimal toplamıdır → "genel toplam" ile "depolar toplamı" **asla kopmaz** (testle kilitli).

### 8.3 Hareket listesi — 4 yeni alan (SONA eklendi)

```jsonc
// GET /api/stock  ·  GET /api/stock/movements
{ "…": "…",
  "locationId": "depoB", "locationName": "Depo B",        // hareketin lokasyonu
  "fromLocationId": "depoA", "fromLocationName": "Depo A", // YALNIZ transferde dolu
  "locationText": "Depo B", "fromLocationText": "Depo A" } // ekran için hazır metin ("Atanmamış" / "—")
```

Web bu yanıtları `JsonElement` + `TryGetProperty` ile okur → **eklenen alanlar eski ekranları bozmaz** (doğrulandı).

## 9. KAPSAM DIŞI (bilinçli)
Web ekranı değişikliği (**STK-04**) · masaüstü ekranı (**STK-05**) · rapor lokasyon boyutu (**STK-06**) ·
ATANMAMIŞ dağıtımı (**STK-08 / KARAR-8**) · zorunlu lokasyon dayatması (**STK-04**).
