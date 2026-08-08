# FAZ 3 ÖNCESİ KARAR VE RİSK ANALİZİ

**Tarih:** 2026-08-08
**Durum:** YALNIZ ANALİZ — kod yazılmadı, migration uygulanmadı, deploy yapılmadı, canlı veriye dokunulmadı.
**Kapsam:** Talep Yönetimi Faz 3 (karşılama / fulfillment + gerçek stok hareketleri) öncesi teknik doğrulama.
**Yöntem:** Her tespit mevcut koddan kanıtla verilmiştir (dosya:satır). Varsayım yapılmamıştır.

> **Terim notu (yazılım terimleri parantez içinde açıklanır):**
> *transaction* = "ya hepsi ya hiçbiri" çalışan işlem bloğu · *race condition / yarış durumu* = iki işlemin aynı
> anda aynı veriye dokunması · *oversell* = stokta olmayan malı düşmüş görünmek · *idempotency* = aynı isteğin
> iki kez gelmesi durumunda ikinci kez etki yaratmaması · *migration* = veritabanı şema değişikliği ·
> *watermark* = "buraya kadar gönderdim" işareti · *delta* = yalnız değişenleri gönderme.

---

## 1. POSTGRESQL EŞZAMANLILIK / OVERSELL RİSKİ

### 1.1 İncelenen kod

| Konu | Yer |
|---|---|
| Belge motoru (transaction sınırı) | `StockService.cs:312-339` (`RunDocument`) |
| Satır uygulama (kontrol + düşüm) | `StockService.cs:341-361` (`ApplyLine`) |
| Bakiye yazımı | `StockService.cs:364-381` (`ApplyDelta`) |
| Bakiye okuma | `StockService.cs:383-390` (`ReadBalance`) |
| Şube bakiyesi (defterden) | `StockService.cs:134-149` (`BranchBalance`) |
| Çıkış | `StockService.cs:79-91` (`IssueOut`) |
| Transfer | `StockService.cs:94-117` (`Transfer`) |
| Transaction açma | `DbCommandExtensions.cs:37-38` (`BeginImmediate`) |
| `stock_balances` şeması | `Migration005_Materials.cs:127-134` |
| `stock_movements` idempotency indeksi | `Migration005_Materials.cs:123` |

### 1.2 Mevcut akış (kanıt)

`ApplyLine` sırayla şunu yapar:

1. Miktar > 0 ve malzeme firmaya ait mi (`StockService.cs:344-345`).
2. **Şube bakiyesi OKUNUR** — defterden `SUM` ile hesaplanır; yetmiyorsa `NegativeStockException`
   (`StockService.cs:351-356`).
3. **Firma bakiyesi OKU → hesapla → YAZ** (`ApplyDelta`):

```csharp
var current = ReadBalance(conn, tx, materialId);   // SELECT quantity FROM stock_balances
var updated = current + signedQty;                 // hesap C# tarafında
if (!allowNegative && updated < 0) throw new NegativeStockException(...);
INSERT INTO stock_balances(...) VALUES(...)
ON CONFLICT(material_id) DO UPDATE SET quantity = excluded.quantity, ...;   // MUTLAK değer yazılıyor
```
(`StockService.cs:364-381`)

4. Hareket defterine satır eklenir (`InsertMovement`, `StockService.cs:359-360`).

Transaction sınırı `RunDocument`'tedir: **bir belge = bir transaction** (`StockService.cs:321-337`).

### 1.3 Transaction davranışı — iki veritabanı GERÇEKTEN farklı

```csharp
public static DbTransaction BeginImmediate(this DbConnection conn)
    => conn is SqliteConnection s ? s.BeginTransaction(deferred: false) : conn.BeginTransaction();
```
(`DbCommandExtensions.cs:37-38`)

- **SQLite (masaüstü):** `deferred:false` = **IMMEDIATE** → transaction BEGIN anında yazma kilidi alınır →
  **aynı anda tek yazar** → "oku–kontrol et–yaz" zinciri baştan sona serialize olur.
  `StockService.cs:322`'deki `// IMMEDIATE → eş zamanlı çıkış serialize` yorumu **SQLite için doğrudur.**
- **PostgreSQL (sunucu + web):** düz `BeginTransaction()` → **READ COMMITTED** (PostgreSQL varsayılanı) →
  hiçbir serileştirme yoktur. Yorum PostgreSQL için **geçerli değildir.**

### 1.4 SORU: PostgreSQL'de aynı stok üzerinde eşzamanlı iki çıkış olursa TAM OLARAK ne olur?

Örnek: `stock_balances.quantity = 10`. T1 kullanıcısı 6, T2 kullanıcısı 7 çıkış yapıyor.

| An | T1 | T2 | Sonuç |
|---|---|---|---|
| t0 | BEGIN | BEGIN | iki ayrı transaction |
| t1 | `BranchBalance` = 10 → yeterli | `BranchBalance` = 10 → yeterli | **T1'in henüz commit etmemiş hareketi T2'ye GÖRÜNMEZ** (READ COMMITTED) → ikisi de geçer |
| t2 | `ReadBalance` = 10 → updated = 4 | `ReadBalance` = 10 → updated = 3 | negatif kontrolü **ikisinde de geçer** |
| t3 | `ON CONFLICT DO UPDATE` → satıra kilit alır, `quantity=4` yazar | aynı satıra yazmak ister → **T1 commit edene kadar BEKLER** | yazma serialize olur ama **hesap zaten yapılmıştır** |
| t4 | COMMIT | kilit çözülür, `quantity=3` yazar, COMMIT | **son yazan kazanır (lost update)** |

**Sonuç:**
- Hareket defterine **iki hareket de** yazılır (toplam 13 çıkış) → gerçek **oversell**.
- `stock_balances` **3** olur; doğrusu `10 − 13 = −3`'tür ve zaten **engellenmesi gerekirdi**.
  Yani hem fazla satış olur, hem önbellek yanlış kalır (bir düşüm tamamen kaybolur).

### 1.5 SORU: Oversell gerçekten mümkün mü, başka bir mekanizma engelliyor mu?

**Kısmen engelleyen mekanizmalar var, ama bu senaryoyu engellemiyorlar:**

| Mekanizma | Kanıt | Neyi korur | Bu senaryoyu korur mu? |
|---|---|---|---|
| `ux_stock_movements_operation` **UNIQUE** indeksi | `Migration005_Materials.cs:123` | Aynı `operation_id` ile ikinci hareket **imkânsız** (çift tık / tekrar gönderim) | **Hayır** — iki farklı kullanıcının `operation_id`'si farklıdır |
| `FindDocumentByOperation` | `StockService.cs:325-326` | Tekrarlanan isteğe mevcut belgeyi döndürür | Hayır (aynı sebep) |
| `BranchBalance` şube kalkanı | `StockService.cs:351-356` | Şubede stok yoksa çıkışı engeller | **Hayır** — commit edilmemiş hareketi görmez |
| `ApplyDelta` negatif kalkanı | `StockService.cs:369-370` | Tek başına çalışan işlemde negatifi engeller | **Hayır** — iki işlem de "yeterli" görür |
| `RecomputeBalances` | `StockService.cs:398-434` | Bakiyeyi defterden yeniden kurar (**onarım**) | Bakiyeyi **düzeltir**, ama oversell'i **geri almaz** (defterde iki hareket kalır, bakiye −3'e döner) |
| `BeginImmediate` | `DbCommandExtensions.cs:37-38` | SQLite'ta tam koruma | **PostgreSQL'de koruma yok** |

**Kesin cevap: PostgreSQL'de oversell MÜMKÜNDÜR.** Bugün bu risk düşük görünüyor çünkü çıkışlar ağırlıklı
masaüstünden (SQLite) yapılıyor; Faz 3 talep üzerinden **sunucu tarafında** stok düşeceği için risk gerçek hale
gelir.

### 1.6 SORU: En az müdahaleyle güvenli çözüm nedir?

**Önerilen: iyimser kilit (optimistic CAS = compare-and-swap, "okuduğum değer hâlâ aynıysa yaz") + sınırlı tekrar.**

Yalnız `ApplyDelta`'nın YAZMA cümlesi değişir (okuduğu değeri koşula koyar):

- satır varsa: `UPDATE stock_balances SET quantity=@yeni, updated_at=@now WHERE material_id=@m AND quantity=@okunanAynenTEXT`
- satır yoksa: `INSERT ... ON CONFLICT(material_id) DO NOTHING`
- etkilenen satır 0 ise → **başkası araya girdi** → transaction geri alınır ve **belgenin tamamı** baştan denenir
  (en fazla N kez).

**Neden bu, en az müdahale:**
- `quantity` kolonu **TEXT**'tir (`Migration005_Materials.cs:127-134`; `Money.Serialize` ile yazılır) →
  SQL'de sayısal karşılaştırma (`quantity >= @q`) güvenilir DEĞİLDİR (metin sıralaması: `'9' > '10'`).
  CAS ise **metin eşitliği** kullanır → tip değişimi/migration **gerekmez**.
- Aynı SQL hem SQLite hem PostgreSQL'de çalışır → **davranış farkı oluşmaz** (talebiniz).
- SQLite'ta zaten IMMEDIATE serialize olduğu için CAS **hiç düşmez** → masaüstü davranışı **değişmez**.
- Şube kontrolü de dolaylı olarak korunur: yeniden deneme sırasında `BranchBalance` tekrar okunur ve bu kez
  rakibin **commit edilmiş** hareketini görür → doğru sonuç verir.

### 1.7 SORU: `SELECT ... FOR UPDATE` (satır kilidi) gerekli mi?

**Gerekli değil** — ve bu projede sakıncalı:
- `FOR UPDATE` **SQLite'ta yoktur** (sözdizimi hatası) → dialect (veritabanı türü) dallanması gerekir; bu tam
  olarak "iki veritabanı farklı davransın istemiyorum" kuralınıza aykırı bir bakım yükü yaratır.
- Bakiye satırı henüz yoksa (ilk hareket) kilitlenecek satır da yoktur → önce "boş satır oluştur" adımı gerekir.
- CAS aynı garantiyi (tek kazanan) **ek sözdizimi olmadan** verir.

**Alternatif olarak kabul edilebilir:** `FOR UPDATE` + `SqlDialect` dallanması. Fark: daha az tekrar (retry),
daha fazla veritabanına-özel kod. **Önerim CAS.**

### 1.8 SORU: SQLite davranışını bozmadan ortak çözüm kurulabilir mi?

**Evet.** CAS düz ANSI SQL'dir; iki veritabanında da aynı cümle çalışır. SQLite'ta CAS koşulu **her zaman
tutar** (tek yazar olduğu için), dolayısıyla:
- masaüstünde ekstra sorgu maliyeti ≈ yok,
- masaüstü davranışı **birebir aynı** kalır,
- yalnızca PostgreSQL'de gerçek koruma devreye girer.

### 1.9 SORU: Yalnız Faz 3'e özel mi, StockService seviyesinde genel mi?

**Genel — `StockService` seviyesinde.** Gerekçe (kanıtlı):
- Hata Faz 3'e ait değil; bugün **Giriş-Çıkış**, **Stok Sayım**, **Transfer**, **Günlük Faaliyet → depo çıkışı**,
  **Bakım malzemesi** yollarının hepsinde aynı desen vardır.
- `MaintenanceService` **kendi `ApplyDelta` kopyasını** kullanır (`allowNegative: true`) → yalnız StockService
  düzeltilirse bakım tarafında risk sürer.
- Faz 3'e özel bir çözüm, sistemde **iki farklı stok yolu** doğurur — projenin "tek stok defteri motoru"
  ilkesine aykırı.

### 1.10 SORU: Diğer ekranlarda regresyon riski var mı?

Etkilenen çağrı yolları: `ReceiveIn` (79 satır öncesi), `IssueOut` (`:79`), `Transfer` (`:94`), `Count` (`:152`),
`ReverseDocument` (`:177`), `RecomputeBalances` (`:398`), ayrıca `MaintenanceService` (kendi kopyası).

| Risk | Değerlendirme |
|---|---|
| Yanlış tekrar döngüsü → çift kayıt | **Düşük** — tekrar yalnız **rollback'ten sonra** yapılır; ayrıca `ux_stock_movements_operation` ikinci güvenlik ağıdır |
| Sonsuz döngü | **Düşük** — deneme sayısı sınırlı (öneri: 3) |
| SQLite'ta davranış değişikliği | **Çok düşük** — CAS koşulu tek yazarda hep tutar |
| `Count` (sayım) yolunda etki | Sayım da `ApplyLine` kullanır → aynı korumadan yararlanır; kural değişmez |
| `ReverseDocument` (iptal) | Aynı düzeltmeden yararlanır; iptal kuralları değişmez |
| Performans | Ek maliyet: çakışma yoksa **yok**; çakışma varsa yalnız çakışan işlem tekrarlanır |

**Zorunlu regresyon testi:** Giriş-Çıkış, Stok Sayım, Transfer, Bakım malzemesi, Günlük Faaliyet — hepsi
SQLite'ta mevcut testlerle koşturulmalı (§9 test listesi).

### 1.11 Transaction sınırı NEREDE olmalı?

**Sınır bugün de doğru yerdedir ve DEĞİŞMEMELİDİR: `RunDocument` (bir belge = bir transaction).**
(`StockService.cs:321-337`)

Faz 3 için tek gereken ekleme:

> **Karşılama (fulfillment) kaydı, stok belgesiyle AYNI transaction içinde yazılmalıdır.**

Bugün bu **mümkün değildir**: `RunDocument` kendi bağlantısını ve transaction'ını açar
(`using var conn = _factory.Create(); using var tx = conn.BeginImmediate();`) ve dışarıya vermez.
→ **Gerekli değişiklik:** `StockService`'e "verilen bağlantı/transaction üzerinde çalış" biçiminde bir **iç**
(internal) giriş noktası. İş kuralları, imzalar ve mevcut davranış **değişmez**; yalnız transaction paylaşılır.

Tekrar (retry) sınırı da **aynı yerde** olmalıdır: çakışma olduğunda **belgenin tamamı** (fulfillment kaydı
dahil) geri alınıp baştan denenir. Kısmi tekrar YOK.

### 1.12 "Biri başarılı, diğeri kontrollü şekilde başarısız" tasarımı

```
BEGIN (RunDocument)
  ├─ idempotency: operation_id daha önce işlendi mi? → evet ise mevcut belgeyi döndür (yeni hareket yok)
  ├─ talep kalemi kilidi/kontrolü: kalan miktar (aktif fulfillment'lardan) yeterli mi?      [aynı tx]
  ├─ şube defter bakiyesi yeterli mi?                                                       [aynı tx]
  ├─ CAS ile bakiye düşümü  →  0 satır etkilendi mi?  ── evet ──► ROLLBACK + TEKRAR (max 3)
  ├─ stok hareketi INSERT (operation_id UNIQUE → ikinci kez imkânsız)
  ├─ request_fulfillments INSERT (aynı tx)
  └─ audit + geçmiş kaydı (aynı tx)
COMMIT
```

Garantiler:
- **Kazanan:** ilk commit eden.
- **Kaybeden:** ya otomatik tekrar dener ve doğru sonuca ulaşır, ya da **temiz** `NegativeStockException` /
  "kalan miktar aşıldı" hatası alır — **yarım kayıt oluşmaz.**
- 3 denemede de çakışırsa: kullanıcıya *"Bu malzeme üzerinde aynı anda başka bir işlem yapıldı. Lütfen tekrar
  deneyin."* (öneri metni — onayınıza tabidir).

**"Stok yeterli mi?" ile "stok düşümü" arasındaki yarış bu tasarımda kalmaz**, çünkü kontrolün dayandığı değer
(`quantity`) yazma koşuluna dahil edilmiştir: değer değiştiyse yazma gerçekleşmez.

---

## 2. FAZ 3 FULFILLMENT MİMARİSİNİN DOĞRULANMASI

### 2.1 Mevcut şema (kanıt)

`material_request_items` (`Migration010_Requests.cs:39-49`):
```sql
id TEXT PRIMARY KEY, request_id TEXT NOT NULL, material_id TEXT NOT NULL,
quantity TEXT NOT NULL, vehicle_id TEXT NULL, note TEXT NULL
```
→ **company_id YOK, created_at/updated_at YOK, is_deleted YOK.** (Bu, §8'de ayrıca ele alınan bir sorunun da
kaynağıdır.)

`material_requests` (`Migration010_Requests.cs:20-37` + `Migration060/061`): `company_id`, `branch_id`,
`status`, `operation_status`, `priority`, `ops_from_branch_id`, `ops_to_branch_id`, `ops_note`,
`created_at/updated_at/version/is_deleted` **mevcut**.

### 2.2 12 kuralın doğrulaması

| # | Kural | Durum | Not / kanıt |
|---|---|---|---|
| 1 | Bir talep kalemi birden fazla karşılama alabilir | ✅ Uyumlu | Yeni tabloda `request_item_id` çoklu satır; mevcut şemayla çelişmiyor |
| 2 | Her karşılama kendi miktarını tutar | ✅ Uyumlu | `quantity TEXT` (projede para/miktar TEXT + `Money` deseni) |
| 3 | Kaynak türü depo / şube transferi ayrılabilir | ✅ Uyumlu | `source_type` alanı: `warehouse` / `branch_transfer` / (**ileride**) `purchase` |
| 4 | Kaynak şube bilgisi tutulur | ✅ Uyumlu | `source_branch_id`; **sunucuda** `BranchScope.Active(s)`'ten doğrulanır (Faz 2 deseni, `RequestOperationsService`) |
| 5 | Talep edilen `material_id` asla değişmez | ✅ Uyumlu | `material_request_items.material_id` **hiç UPDATE edilmez**; yeni tablo ayrı alan taşır |
| 6 | Gönderilen `material_id` ayrı tutulur | ✅ Uyumlu | `shipped_material_id` (normalde talep edilenle aynı) |
| 7 | Alternatif malzemede gerekçe zorunlu | ✅ Uyumlu | `alt_reason TEXT NULL` + **servis seviyesinde** zorunluluk (`shipped_material_id != material_id` ise boş olamaz) |
| 8 | Karşılanan miktar aktif kayıtlardan türetilir | ✅ Uyumlu | `SUM(quantity) WHERE status='active'`; **saklanan "karşılanan" kolonu OLMAMALI** (çift gerçek kaynağı olmaz) |
| 9 | İptal edilen karşılama aktif toplama girmez | ✅ Uyumlu | `status TEXT NOT NULL DEFAULT 'active'` → `'cancelled'`; fiziksel silme yok (CLAUDE.md §4) |
| 10 | Aynı `operation_id` ikinci kez işlenirse yeni hareket oluşmaz | ✅ Uyumlu (güçlü) | `ux_stock_movements_operation` UNIQUE (`Migration005:123`) + `FindDocumentByOperation` (`StockService.cs:325`). **Öneri:** `request_fulfillments.operation_id`'ye de UNIQUE indeks → karşılama kaydı da tekilleşsin |
| 11 | Karşılama ile stok hareketi aynı transaction'da | ⚠️ **Bugün MÜMKÜN DEĞİL** | `RunDocument` bağlantı/transaction'ı dışarı vermiyor (`StockService.cs:321`) → **iç giriş noktası eklenmeli** (bkz. §1.11) |
| 12 | Stok hareketi başarısızsa karşılama da oluşmaz | ⚠️ 11'e bağlı | 11 çözülünce otomatik sağlanır (tek transaction) |

### 2.3 Çelişki/eksik tespitleri (ÖNERİ — mevcut karar değil)

1. **`request_fulfillments` tablosu `company_id` TAŞIMALIDIR.** Gerekçe: (a) çok-kiracı (tenant) güvenliği tek
   JOIN'e bağlı kalmasın, (b) **senkron mekanizması `company_id` olmayan tabloyu firma bazında filtreleyemiyor**
   — `BusinessSyncService.BuildSnapshot` `if (hasCompany) where.Add("company_id=@c")` (`:122`) yazıyor;
   `company_id` yoksa **filtre hiç uygulanmıyor**. `material_request_items` ve `maintenance_materials` bugün
   tam olarak bu durumda (bkz. §8.5 — ayrı ve önemli bulgu).
2. **`created_at` + `updated_at` TAŞIMALIDIR.** Yoksa senkron "delta" filtresi uygulanamaz
   (`StampColumn`, `BusinessSyncService.cs:178-179`) → her eşitlemede tüm tablo gönderilir.
3. **`is_deleted` yerine `status`** ('active'/'cancelled') kullanılmalı — iptal, silme değildir (CLAUDE.md §4).
4. **`stock_document_id` NULL olabilmelidir** — satın alma (Faz 4) karşılamasında henüz stok hareketi yoktur.
5. **Yeni tablo `BusinessSyncService.Tables` + `TableModule` listelerine EKLENMELİDİR**
   (`BusinessSyncService.cs:30-56` ve `:60-84`). Eklenmezse masaüstünde oluşan karşılamalar **sunucuya hiç
   ulaşmaz** ve sessizce kaybolur. (Kolay atlanan, yüksek etkili madde.)

---

## 3. TRANSFER İPTALİ VE KARŞILAMA İPTALİ

**Mevcut karar (değiştirilmedi):** Transfer geri alınamaz; ters transfer **yeni bir transfer** olarak yapılır.
Kanıt: `StockService.cs:191-192`
> `if (doc.DocType == "transfer") throw new ForbiddenException("Transfer geri alınamaz. Hedef şubeden kaynağa yeni bir ters transfer yapın.");`

### 3.1 Senaryo bazlı iptal davranışı

| Senaryo | Stok tarafı | Karşılama kaydı | Riskler / gereken karar |
|---|---|---|---|
| **A. Normal depo çıkışı** (`source_type='warehouse'`) | `ReverseDocument(stock_document_id, gerekçe)` → ters hareket üretir, belge `cancelled` olur (`StockService.cs:177-207`) | `status='cancelled'`, iptal eden + gerekçe + zaman | ⚠️ `ReverseDocument` **`SpecialButtons.Reverse` özel buton yetkisi** ister (`:180`) → karşılama iptali yapacak kullanıcıda bu yetki yoksa iptal **çalışmaz**. Karar gerekir (bkz. §5) |
| **B. Şube transferi** (`source_type='branch_transfer'`) | `ReverseDocument` **REDDEDER** → **yeni bir ters `Transfer`** (hedef → kaynak) oluşturulmalı, kendi `operation_id`'siyle | Orijinal `status='cancelled'`; ters transfer **ayrı belge** olarak bağlanır (`reversal_of` alanı önerisi) | ⚠️ `Transfer` içinde `EnforceOwnBranch` (`StockService.cs:105, 122-130`) → ters transferin kaynağı artık **hedef şubedir**; şubeye bağlı bir operasyon kullanıcısı bunu **yapamaz** (`ForbiddenException`). Karar gerekir |
| **C. Alternatif malzeme** | Ters hareket **gönderilen** malzemeye (`shipped_material_id`) uygulanır — talep edilene DEĞİL | Aynı (A) veya (B) | Kural netleştirilmeli: iptal, **fiilen çıkan** malzemeyi geri alır. Karşılanan miktar hesabı talep kalemine göre azalır |
| **D. Satın alma (Faz 4 — ŞİMDİ KOD YAZILMAYACAK)** | Karşılama anında stok hareketi **yoktur**; mal geldiğinde `ReceiveIn` + ardından çıkış/teslim | Yalnız kayıt `cancelled` olur; stok tarafı **yoktur** | Geleceğe uyum için **bugünden**: `source_type` alanı `purchase` değerini kabul edecek şekilde tanımlansın, `stock_document_id` NULL olabilsin. **Kod yazılmayacak.** |

### 3.2 Ortak kural önerisi

- İptal **hiçbir zaman fiziksel silme değildir**; hem karşılama kaydı hem stok tarafı iz bırakır (CLAUDE.md §4).
- İptal, karşılama kaydını yaratan işlemin **aynası** olarak, **tek transaction** içinde yapılır.
- İptal sırasında **stok durumu değişmiş olabilir** (mal başka yere çıkmış olabilir) → ters hareket
  `allowNegative: false` ile uygulanır; negatife düşürecekse **kontrollü hata** verir ve iptal yapılmaz
  (mevcut `ReverseDocument` davranışı — `StockService.cs:198`).

---

## 4. OPERASYON DURUMU İLE FULFILLMENT İLİŞKİSİ

### 4.1 Mevcut durum makinesi (değiştirilmedi)

`RequestOperationStateMachine` (`RequestOperationStateMachine.cs:18-89`): 13 durum, kullanıcı onaylı matris.
`Delivered → Completed` tek geçiş; `Completed` ve `CancelledOps` **terminal** (`:71-76`).
`Delivered → PartiallyFulfilled` geçişi **kullanıcı kararıyla kaldırılmıştır** (`:62-63`).

### 4.2 Önerilen ilke: **fulfillment motoru operasyon durumunu ASLA otomatik değiştirmez**

| Karşılama durumu | Operasyon ekranındaki durum | Ekranda ne görünür |
|---|---|---|
| Hiç karşılanmamış (0) | **Değişmez** (kullanıcının seçtiği durum) | Karşılama sütunu: `0 / 10` · rozet: "Karşılanmadı" |
| Kısmen karşılanmış (0 < x < talep) | **Değişmez** | `4 / 10` · rozet: "Kısmi" · *(öneri)* pasif bir öneri ipucu: "Kısmen Karşılandı durumuna geçmek ister misiniz?" — **tıklamadan hiçbir şey değişmez** |
| Tüm kalemler tamamen karşılanmış | **Değişmez — terminal YAPILMAZ** (mevcut karar korunur) | `10 / 10` · rozet: "Tam" · *(öneri)* ipucu: "Teslim Edildi / Tamamlandı" önerisi |

**Gerekçe:** Durum, **insanın verdiği iş kararıdır** (mal çıktı ≠ teslim edildi ≠ iş kapandı). Miktar ise
**ölçülen gerçektir**. İkisini karıştırmak, Faz 1'de aldığınız "onay durumu ile operasyon durumunu asla
karıştırma" kararının aynısını miktar tarafında bozar.

### 4.3 "Kullanıcının seçtiği durum ezilmesin" garantisi (uygulama kuralı)

1. Karşılama servisi `material_requests.operation_status` kolonuna **hiçbir koşulda UPDATE yazmaz** —
   tek yazan yer `RequestOperationsService.ChangeStatus` olarak kalır.
2. Karşılama yüzdesi **saklanmaz**, `request_fulfillments`'tan **türetilir** → durumla çakışma imkânı yoktur.
3. Test: "karşılama sonrası `operation_status` değişmedi" doğrulaması (§9, T-14).

---

## 5. YETKİLER

### 5.1 Mevcut yapı (kanıt)

`RequestOperationStateMachine.cs:92-119`:
- `request_ops` — **her** operasyon işleminde `Edit` gerekir.
- `request_ops_warehouse` — depo/sevkiyat/teslim adımları.
- `request_ops_purchase` — satın alma adımları.
- `AccessControl.IsAdmin(s)` → **tam bypass** (`:115`).

Stok tarafı ayrı: `StockService.IssueOut/Transfer` → `AccessControl.Require(s, "stock", Create)`
(`StockService.cs:83, 99`); `ReverseDocument` → `stock` **Edit** + `SpecialButtons.Reverse` (`:179-180`).

### 5.2 Faz 3 için önerilen yetki eşlemesi

| İşlem | Önerilen yetki | Yeni yetki gerekir mi? |
|---|---|---|
| **Depodan karşılama** (stok çıkışı) | `request_ops` (Edit) **+** `request_ops_warehouse` (Edit) **+** `stock` (Create) | **Hayır** — mevcut yetkiler yeter |
| **Şube transferiyle karşılama** | `request_ops` + `request_ops_warehouse` + `stock` (Create) + şube kuralı (`EnforceOwnBranch`) | **Hayır** |
| **Alternatif malzeme seçimi** | Ek yetki YOK — `request_ops_warehouse` yeterli; **gerekçe zorunluluğu** iş kuralıyla sağlanır | **Hayır** |
| **Karşılama iptali** | `request_ops` + `request_ops_warehouse` **+** `stock` (Edit) **+** `SpecialButtons.Reverse` | **Hayır** — ama bkz. aşağıdaki uyarı |

### 5.3 Çözülmesi gereken iki yetki çelişkisi (KARAR GEREKİR)

**Ç1 — Depo operasyoncusunda `stock` yetkisi olmayabilir.**
`request_ops_warehouse` yetkisi olan bir kullanıcı `stock` modülünde `Create` yetkisine sahip olmayabilir; bu
durumda karşılama `ForbiddenException` ile **çalışmaz**. Üç seçenek:
- **(a) İki yetkiyi de zorunlu tut** (önerim): en güvenli, deny-by-default ilkesine uygun; kullanıcı
  yetkilendirilirken `stock/Create` de verilir. Hata mesajı açık olur: *"Bu işlem için stok yetkisi gerekiyor."*
- (b) Karşılama sırasında stok yetkisini **atla** (servis içinde yükseltilmiş bağlam) — **önermiyorum**;
  yetki modelinde delik açar.
- (c) Yeni bir `request_ops_fulfill` modülü ekle — gereksiz karmaşıklık; **önermiyorum**.

**Ç2 — Karşılama iptali `SpecialButtons.Reverse` özel butonuna bağlı.**
- **(a)** Mevcut `Reverse` özel butonunu kullan (önerim) — yeni yetki yok, tutarlı.
- (b) İptal için yeni bir özel buton tanımla (`RequestFulfillCancel`) — daha ince ayar, ama Yetki Ağacına yeni
  madde ekler.

> Not (hafıza kuralı): Yeni bir yetki eklenirse **Yetki Ağacına otomatik eklenir**, hatırlatma beklenmez.

---

## 6. VERİ BÜTÜNLÜĞÜ — BEKLENEN DAVRANIŞLAR

| # | Durum | Beklenen davranış | Nerede zorlanır |
|---|---|---|---|
| 1 | Miktar 0 | **Reddedilir** — "Miktar pozitif olmalı." | Servis + `ApplyLine` (`StockService.cs:344`) |
| 2 | Negatif miktar | **Reddedilir** (aynı) | Aynı + senkron `NonNegativeFields` (`BusinessSyncService.cs:93-100`) |
| 3 | Talep miktarından fazla karşılama | **Reddedilir** — "Kalan miktar aşılamaz (kalan: X)." Kontrol **aynı transaction içinde**, aktif karşılamaların SUM'ı ile | Faz 3 servisi (yeni) |
| 4 | Aynı karşılama isteği iki kez gönderildi | **İkinci istek yeni hareket üretmez**, mevcut sonuç döner | `ux_stock_movements_operation` (`Migration005:123`) + `FindDocumentByOperation` + *(öneri)* `request_fulfillments.operation_id` UNIQUE |
| 5 | Aynı talep kalemini iki kullanıcı aynı anda karşıladı | Biri başarılı; diğeri ya otomatik tekrar edip doğru sonuca ulaşır ya da **kontrollü hata** alır. Toplam karşılama **talebi aşmaz**, stok **iki kez doğru düşer** | §1.6 CAS + §1.12 akış |
| 6 | Stok yetersiz | **Reddedilir** — `NegativeStockException`; hiçbir kayıt oluşmaz | `ApplyLine` (`:351-356`) + `ApplyDelta` (`:369`) |
| 7 | Kaynak şube, kullanıcının yetkisiz olduğu şube | **Reddedilir** — "Yalnız kendi şubenizden ..." | `EnforceOwnBranch` (`StockService.cs:122-130`); şube bilgisi **sunucuda** `BranchScope.Active(s)`'ten alınır, istemciye güvenilmez (Faz 2 deseni) |
| 8 | Başka firmaya ait şube | **Reddedilir** — `company_id` oturumdan zorlanır; `EnsureBranchOwned` / `EnsureMaterialOwned` (`StockService.cs:345`) | Servis + API |
| 9 | Alternatif malzeme seçildi, gerekçe verilmedi | **Reddedilir** — "Alternatif malzeme için gerekçe zorunludur." | Faz 3 servisi (yeni iş kuralı) |
| 10 | İptal sırasında stok durumu değişmiş | Ters hareket negatife düşürecekse **kontrollü hata**, iptal yapılmaz; hiçbir yarım kayıt kalmaz | `ReverseDocument` → `ApplyDelta(allowNegative:false)` (`:198`) |
| 11 | Transaction ortasında hata | **Tam rollback** — ne karşılama ne hareket ne geçmiş kaydı kalır | Tek transaction (§1.11) |
| 12 | Ağ kopması / timeout | Sunucu tarafı ya tamamlanmış ya hiç başlamamıştır (atomik). İstemci aynı `operation_id` ile **güvenle tekrar** gönderir → çift kayıt oluşmaz | İdempotency (madde 4) |

---

## 7. RAPORLARLA UYUMLULUK (yalnız değerlendirme — rapor geliştirmesi YOK)

**Mevcut Talep Raporu:** kolonlar Şube · Tarih · Talep No · Talep Eden · Kalem · Durum … (`ReportService.Requests`);
"Kalem" satır sayısıdır, miktar bazlı bilgi taşımaz.

**Faz 3 sonrası raporlanmak istenen bilgiler:**

| Bilgi | Kaynak | Model yeterli mi? |
|---|---|---|
| **Talep edilen** miktar | `material_request_items.quantity` | ✅ Var |
| **Karşılanan** miktar | `SUM(request_fulfillments.quantity) WHERE status='active'` | ✅ Yeni tablo ile |
| **Kalan** miktar | talep − karşılanan (türetilmiş) | ✅ Hesaplanabilir |
| **Gönderilen malzeme** (alternatif dahil) | `request_fulfillments.shipped_material_id` → `materials` JOIN | ✅ Yeni tablo ile |
| Kaynak (depo / şube transferi / satın alma) | `source_type` + `source_branch_id` | ✅ |
| Karşılamayı yapan / zaman | `created_by`, `created_at` | ✅ (alanlar tabloya konursa) |

**Uyarılar:**
1. Rapor performansı için **indeks gerekir**: `(request_item_id, status)` ve `(company_id, created_at)`.
2. Karşılanan miktarı **saklamayın**; her zaman `SUM`'dan türetin — aksi halde iptal/senkron sonrası iki farklı
   "gerçek" oluşur.
3. `material_request_items`'ta `company_id` olmadığı için raporlar **`material_requests` üzerinden JOIN** ile
   firma kapsamını almalıdır (bugün de böyledir). Yeni tabloya `company_id` konulması bu bağımlılığı kaldırır.

**Sonuç: veri modeli (önerilen alanlarla) raporlamaya hazırdır.** Şu an rapor geliştirmesi yapılmayacaktır.

---

## 8. SENKRONİZASYON — MEVCUT DURUM VE DARBOĞAZ TESPİTİ

> Bu bölüm **teşhistir**; yeniden tasarım önerilmemiş, yalnız ölçülebilir darboğazlar ve düşük riskli
> iyileştirmeler sıralanmıştır.

### 8.1 Mevcut mekanizma (kanıt)

| Parça | Yer | Davranış |
|---|---|---|
| Periyodik tetik | `ShellViewModel.cs:300-302` | **Her 15 saniyede** `PingAsync` + `RegisterMachineAsync` + `CheckUserChangedAsync` + `MaybePushBusinessAsync` + `MaybeDailyBackupAsync` → tek turda **4-5 ayrı HTTP isteği** |
| Sürüm yoklaması | `ShellViewModel.cs:253` → `GET /api/sync/business-version` | Sunucu `CompanyVersion` çalıştırır |
| `CompanyVersion` | `BusinessSyncService.cs:144-164` | **22 tablo için 22 ayrı `SELECT MAX(...)` sorgusu** (döngü) |
| Push kararı | `BusinessSyncPushService.cs:53-55` | Yerelde de `CompanyVersion` (22 sorgu daha, SQLite) |
| Snapshot üretimi | `BusinessSyncService.cs:109-138` | Tablo başına `SELECT *` (+ delta filtresi varsa) |
| Sunucuda uygulama | `BusinessSyncService.cs:299-363` | **Tümü TEK transaction**; PG'de tablo başı savepoint, hata olursa satır başı kurtarma (`:376-427`) |
| Push sonrası | `Program.cs:381` | `RecomputeBalances(companyId)` — **firmanın TÜM `stock_movements` satırlarını okur** ve tüm bakiyeleri yeniden yazar (`StockService.cs:398-434`) |
| Pull | `ShellViewModel.cs:262-270` | Sunucu sürümü ilerlediyse delta çeker + **açık ekranı `RefreshData()` ile yeniler** |
| Hata/tekrar | `BusinessSyncPushService.cs:83-110` | Sunucu `skipped/errors` döndürürse **watermark ilerlemez** → aynı grup tekrar; 5 denemeden sonra "poison" → watermark ilerler + kalıcı uyarı |
| Zaman aşımı | `BusinessSyncPushService.cs:22` | 300 saniye |
| Eşzamanlılık kapısı | `SyncGate` (`ShellViewModel.cs:250`) | Manuel eşitleme ile tick aynı anda çalışmaz |

### 8.2 "Neden ~30 saniye sürüyor?" — sıralı şüpheliler

| # | Darboğaz | Kanıt | Etki |
|---|---|---|---|
| **B1** | `CompanyVersion` = **22 ayrı sorgu**, hem sunucuda (her yoklamada) hem yerelde (her push kararında) | `BusinessSyncService.cs:148-163` | Neon (uzak PostgreSQL) ile her sorgu ağ gidiş-dönüşü; 22 × gecikme → **tek başına saniyeler**. Üstelik **her 15 saniyede** tekrarlanır |
| **B2** | `RecomputeBalances` **her push'ta** tüm defteri okur | `Program.cs:381` + `StockService.cs:404-432` | Maliyet **defter büyüklüğüyle doğru orantılı ve sürekli artıyor**; her malzeme için ayrı `INSERT ... ON CONFLICT` (döngü) |
| **B3** | **Yankı (echo) pull**: push'tan sonra kendi gönderdiğin satırları geri çekiyorsun | `ShellViewModel.cs:253` (sürüm push'tan **ÖNCE** okunur) → `:267` (`_lastServerVersionPulled = sv` eski değer) | Her push, bir sonraki turda **gereksiz bir pull** doğurur; ayrıca o pull `RefreshData()` tetikler → **açık ekran yenilenir** |
| **B4** | Damgasız tablolar **her seferinde tam** gönderilir | `maintenance_materials` (`Migration008:66-75`) ve `material_request_items` (`Migration010:39-49`) → `created_at`/`updated_at` **yok** → `StampColumn` null (`BusinessSyncService.cs:178-179`) → delta filtresi uygulanmaz (`:123`) | Bu iki tablo **büyüdükçe her eşitlemede tamamı** gider |
| **B5** | Tek turda 4-5 sıralı HTTP isteği | `ShellViewModel.cs:301` | Gecikmeler toplanır (paralel değil, `await` zinciri) |
| **B6** | Sunucuda push'un tamamı **tek transaction** | `BusinessSyncService.cs:317-354` | Büyük push sırasında ilgili tablolar kilitli; eşzamanlı web kullanıcısı yavaşlar |

### 8.3 Sorularınızın tek tek cevabı

- **Hangi tablolar sırayla gönderiliyor?** `BusinessSyncService.Tables` sırasıyla (FK-güvenli):
  units → suppliers → brands → material_categories → vehicle_types → vehicle_categories → vehicle_models →
  maintenance_definitions → personnel_titles → personnel → materials → **stock_balances** → vehicles →
  vehicle_maintenances → maintenance_materials → fuel_depot_entries → fuel_distributions → daily_activities →
  **stock_movements** → stock_documents → material_requests → material_request_items (`:30-56`).
  ⚠️ **`request_status_history` bu listede YOKTUR** → masaüstünde oluşan onay/operasyon geçmişi sunucuya
  taşınmaz. Faz 3'te oluşacak `request_fulfillments` da eklenmezse aynı kaderi paylaşır.
- **Gereksiz tekrar gönderim var mı?** **Evet, iki yerde:** damgasız iki tablo her turda tam gider (B4) ve her
  push bir yankı pull doğurur (B3).
- **Aynı kaydın iki kez gönderilme riski?** **Veri bozulması açısından YOK.** Uygulama birincil anahtar (PK)
  üzerinden upsert + LWW yapar (`UpsertRow`); aynı satır iki kez gelirse ikincisi etkisizdir. Maliyeti yalnız
  bant genişliği/zamandır.
- **Ağ hatalarında ne olur?** Sessizce yakalanır, `LastPushFailed=true`, watermark **ilerlemez**; bir sonraki
  turda aynı grup tekrar denenir (`BusinessSyncPushService.cs:118`).
- **Yarım kalan senkron nasıl devam eder?** Sunucu tarafı **atomiktir** (tek transaction — ya hepsi ya hiçbiri),
  istemci watermark'ı yalnız **temiz sonuçta** ilerletir → yeniden bağlanınca kaldığı yerden devam eder.
- **Başarısız kayıtlar nasıl tespit edilir?** Sunucu `{upserted, skipped, errors}` döndürür (en fazla 20 hata
  mesajı); istemci bunu okur, üst barda rozet + `sync.log`'a yazar; 5 denemeden sonra "poison" kalıcı uyarısı.
  ⚠️ **Ama bu kayıt bazında değil, GRUP bazındadır:** tek bir hatalı satır yüzünden watermark ilerlemediği için
  **o turdaki tüm satırlar** 5 tur boyunca yeniden gönderilir → sizin "hata alan kayıt diğerlerini durdurmasın"
  hedefinizle **çelişir**.
- **Sunucuya bir anda fazla yük biniyor mu?** Tek makinede ölçülü; ancak yük **makine sayısıyla doğrusal artar**
  (her makine 15 saniyede 4-5 istek + sunucuda 22 sorgu + tam bakiye yeniden hesabı).

### 8.4 Önerilen iyileştirmeler (risk sırasına göre — **uygulanmadı**)

| Öncelik | İyileştirme | Beklenen kazanç | Risk |
|---|---|---|---|
| **S1** | `CompanyVersion`'ı **tek sorguya** indir (`SELECT MAX(...) ... UNION ALL ...`) | 22 gidiş-dönüş → 1; en büyük ve en ucuz kazanç | **Çok düşük** (salt okuma, davranış aynı) |
| **S2** | `RecomputeBalances`'ı **yalnız etkilenen malzemeler** için çalıştır | Push süresi defter büyüklüğünden bağımsız hale gelir | Düşük (kapsam daraltma; tam hesap "Eşitle" butonunda kalır) |
| **S3** | Yankı pull'u kes: pull imleci push'tan **sonra** okunan sürümle güncellensin | Her push başına bir gereksiz pull + bir gereksiz ekran yenileme gider | Düşük |
| **S4** | Tick aralığını **uyarlanabilir** yap (hareket yoksa 15sn → 60sn) | Boştaki makinenin sunucu yükü ~4 kat azalır | Düşük |
| **S5** | `maintenance_materials` ve `material_request_items`'a `company_id` + `created_at/updated_at` ekle (migration) | Delta devreye girer; **§8.5'teki sızıntı da kapanır** | Orta (canlı veride additive ALTER + geri doldurma) |
| **S6** | Hata yönetimini **satır/kayıt bazlı kuyruğa** çevir (poison yalnız o kaydı bekletir) | Hedefiniz: "bir kayıt diğerlerini durdurmasın" | Orta-yüksek — **ayrı bir faz olmalı** |

**Öneri: senkron işi Faz 3'ün içine karıştırılmamalı; "Faz S — Senkron Performans" olarak ayrı yürütülmeli.**
S1+S2+S3 tek başına ölçülebilir bir hızlanma verir ve Faz 3'ten bağımsızdır.

### 8.5 ⚠️ EK BULGU — çok-kiracı (tenant) sızıntısı riski (Faz 3'ten bağımsız, ÖNEMLİ)

`BuildSnapshot` firma filtresini **yalnız `company_id` kolonu olan tablolara** uygular:
```csharp
var hasCompany = cols.Contains("company_id");
...
if (hasCompany) where.Add("company_id=@c");        // BusinessSyncService.cs:117, 122
```
`business-pull` ucu bunu **sunucu veritabanı üzerinde** çalıştırır (`Program.cs:392`).
`maintenance_materials` (`Migration008:66-75`) ve `material_request_items` (`Migration010:39-49`) tablolarında
`company_id` **yoktur** → bu iki tablo için **hiç firma filtresi uygulanmaz** → sunucudaki **tüm firmaların**
bu iki tablodaki satırları, çeken makineye döner ve yerel veritabanına yazılır.

- **Bugünkü fiili etki:** canlı sunucuda tek gerçek firma olduğu için pratik zarar görünmüyor; ancak bu bir
  **tasarım açığıdır** ve ikinci firma eklendiği anda gerçek sızıntıya döner.
- **Bu rapor kapsamında düzeltilmemiştir** (kod yazma yasağı). §10'da onayınıza sunulmuştur.
- Faz 3 tablosu `company_id` **ile** doğarsa aynı hataya düşmez (bkz. §2.3-1).

---

## 9. MIGRATION VE CANLI VERİ DEĞERLENDİRMESİ

### 9.1 Faz 3 için gereken migration'lar

**M-062 — `request_fulfillments` (YENİ TABLO)**

| Kriter | Değerlendirme |
|---|---|
| Additive mi? | ✅ **Evet** — yalnız yeni tablo + yeni indeksler; mevcut hiçbir tablo değişmez |
| Mevcut kayıtları etkiler mi? | ✅ **Hayır** — tek satır bile UPDATE edilmez, hiçbir veri dönüştürülmez |
| Rollback riski | Tablo boşken `DROP` güvenli; **canlıda veri oluştuktan sonra geri dönüş yok** → yayın öncesi sunucu yedeği alınmalı |
| SQLite / PostgreSQL uyumu | ✅ Mevcut migration deseniyle uyumlu (`TEXT` PK, `BIGINT` zaman, `Money` TEXT). PG'ye özel sözdizimi kullanılmaz |
| İndeks ihtiyacı | `ix (request_item_id, status)`, `ix (company_id, created_at)`, **`ux (operation_id)`** (idempotency) |
| Senkron etkisi | ⚠️ **Kritik:** tablo `BusinessSyncService.Tables` + `TableModule`'e eklenmezse masaüstü karşılamaları sunucuya **hiç ulaşmaz**. FK sırası: `material_request_items` ve `stock_documents`'tan **sonra** |

**Eşzamanlılık düzeltmesi için migration: YOK.** CAS çözümü şema değiştirmez (§1.6).

### 9.2 Onayınıza sunulan opsiyonel migration'lar (Faz 3 kapsamı DIŞI)

| Kod | İçerik | Neden | Risk |
|---|---|---|---|
| **M-S1** | `material_request_items` + `maintenance_materials` → `company_id`, `created_at`, `updated_at` ekle (NULL/varsayılan ile), sonra ebeveynden geri doldur | §8.5 sızıntısını kapatır, delta senkronu açar | **Orta** — canlı veride `UPDATE` içerir (geri doldurma). Ayrı ve dikkatli bir iş olmalı |

> Kararınız olmadan bu migration **planlanmayacaktır**.

---

## 10. SONUÇ

### A) Kesinlikle doğru bulduğum mevcut kararlar

1. **Transaction sınırı = bir belge** (`RunDocument`) — doğru yer, değişmemeli.
2. **Defter (ledger) ana kaynak, bakiye türetilmiş önbellek** — doğru; `RecomputeBalances` onarım yolu var.
3. **`operation_id` + UNIQUE indeks ile idempotency** — beklediğimden güçlü; çift tık/tekrar gönderim **zaten**
   veritabanı seviyesinde engelli.
4. **Transfer geri alınamaz; ters transfer yeni işlemdir** — iki şubeyi etkilediği için doğru karar.
5. **Onay durumu ile operasyon durumunun ayrı tutulması** (Faz 1/2) — Faz 3'te de korunmalı.
6. **Kalem tamamen karşılanınca durum otomatik terminal yapılmaz** — doğru; miktar ≠ iş kararı.
7. **Fiziksel silme yok, iptal + iz** — Faz 3 karşılama iptalinde de aynen uygulanmalı.
8. **Şube bilgisinin sunucuda `BranchScope`'tan belirlenmesi** (Faz 2) — Faz 3'te aynen sürdürülmeli.

### B) Değiştirilmesini önerdiğim kararlar

| # | Mevcut karar / durum | Önerim | Fark |
|---|---|---|---|
| B-1 | `StockService.cs:322` yorumu "IMMEDIATE → eş zamanlı çıkış serialize" | Bu **yalnız SQLite için** doğru; PostgreSQL için koruma eklenmeli (CAS) | Yeni koruma katmanı |
| B-2 | Faz 3'te "yalnız gerekli olanı yap" | Eşzamanlılık düzeltmesi **Faz 3'ten önce, ayrı** yapılsın | Sıralama değişikliği |
| B-3 | Yeni tablo minimal olsun | `request_fulfillments` **`company_id` + `created_at`/`updated_at` ile** doğsun | Alan ekleme (senkron + tenant güvenliği) |
| B-4 | — | Senkron iyileştirmesi **ayrı "Faz S"** olsun, Faz 3'e karıştırılmasın | Kapsam ayrımı |

### C) Kodlamadan önce çözülmesi gereken kritik riskler

| # | Risk | Şiddet | Çözüm |
|---|---|---|---|
| **C-1** | PostgreSQL'de oversell + bakiye kaybı (§1.4) | **Yüksek** | CAS + sınırlı tekrar (Faz 3-Ön) |
| **C-2** | Karşılama ile stok hareketi **aynı transaction'da olamıyor** (§2.2-11) | **Yüksek** | `StockService`'e iç "aynı tx" giriş noktası |
| **C-3** | Yeni tablo senkron listesine eklenmezse veri sunucuya hiç ulaşmaz (§9.1) | **Yüksek** | Migration ile birlikte `Tables` + `TableModule` güncellemesi, testi |
| **C-4** | `MaintenanceService`'in ayrı `ApplyDelta` kopyası (§1.9) | Orta | Aynı düzeltme oraya da uygulanmalı |
| **C-5** | Transfer iptalinde `EnforceOwnBranch` engeli (§3.1-B) | Orta | Yetki/şube kuralı kararı (§10-J, madde 5) |
| **C-6** | `request_ops_warehouse` kullanıcısında `stock` yetkisi olmayabilir (§5.3-Ç1) | Orta | Yetki kararı |
| **C-7** | Çok-kiracı sızıntısı — `company_id`siz iki tablo (§8.5) | Orta (bugün), **Yüksek** (ikinci firmada) | Ayrı iş olarak M-S1 |

### D) Faz 3 için önerilen nihai veri modeli

```
request_fulfillments
  id                  TEXT PK
  company_id          TEXT NOT NULL           -- tenant güvenliği + senkron filtresi  (ÖNERİ)
  request_id          TEXT NOT NULL           -- rapor/liste kolaylığı
  request_item_id     TEXT NOT NULL           -- hangi talep kalemi
  quantity            TEXT NOT NULL           -- Money (decimal-kesin), > 0
  source_type         TEXT NOT NULL           -- 'warehouse' | 'branch_transfer' | ('purchase' → Faz 4, şimdi kullanılmaz)
  source_branch_id    TEXT NULL               -- sunucuda BranchScope'tan doğrulanır
  shipped_material_id TEXT NOT NULL           -- normalde talep edilenle AYNI; alternatifte farklı
  alt_reason          TEXT NULL               -- alternatif ise ZORUNLU (servis kuralı)
  stock_document_id   TEXT NULL               -- satın almada NULL olabilir
  operation_id        TEXT NOT NULL           -- idempotency anahtarı
  status              TEXT NOT NULL DEFAULT 'active'   -- 'active' | 'cancelled'
  cancel_reason       TEXT NULL
  cancelled_by        TEXT NULL
  cancelled_at        BIGINT NULL
  reversal_of         TEXT NULL               -- ters transfer bağlantısı (§3.1-B)
  note                TEXT NULL
  created_by          TEXT NULL
  created_at          BIGINT NOT NULL
  updated_at          BIGINT NOT NULL

  ux_request_fulfillments_op   UNIQUE (operation_id)
  ix_request_fulfillments_item (request_item_id, status)
  ix_request_fulfillments_co   (company_id, created_at)
```

**Türetilen (saklanmayan) değerler:** karşılanan = `SUM(quantity) WHERE status='active'`; kalan = talep − karşılanan.
**`material_request_items` HİÇ DEĞİŞMEZ** (talep edilen malzeme ve miktar dokunulmaz kalır).

### E) Önerilen transaction / eşzamanlılık modeli

1. **Sınır:** bir karşılama işlemi = **bir belge = bir transaction** (`RunDocument`), fulfillment kaydı dahil.
2. **Koruma:** `stock_balances` üzerinde **iyimser CAS** (okunan değer koşula konur).
3. **Tekrar:** çakışmada **tam rollback + baştan deneme**, en fazla **3** kez, sonra kontrollü hata.
4. **Idempotency:** aynı `operation_id` → yeni hareket yok (UNIQUE indeks + belge arama + yeni UNIQUE).
5. **Yetki/şube:** her şey sunucuda; istemciden gelen şube bilgisine güvenilmez.
6. **SQLite:** davranış değişmez (IMMEDIATE zaten serialize; CAS hiç düşmez).

### F) Fulfillment + stok + transfer nihai akışı

```
KARŞILAMA (depodan)
  yetki (request_ops + request_ops_warehouse + stock/Create)
  → BEGIN
      idempotency kontrolü (operation_id)
      kalan miktar kontrolü (aktif fulfillment SUM'ı, aynı tx)
      şube defter bakiyesi kontrolü
      CAS ile bakiye düşümü  → çakışma? ROLLBACK + tekrar (max 3)
      stock_movements INSERT (out)   +   stock_documents
      request_fulfillments INSERT (source_type='warehouse')
      audit
    COMMIT
  → operation_status DEĞİŞMEZ; ekranda karşılama göstergesi güncellenir

KARŞILAMA (şube transferiyle)
  aynı akış; StockService.Transfer (kaynak çıkış + hedef giriş, tek belge/grup)
  source_type='branch_transfer', source_branch_id = sunucuda doğrulanmış şube

ALTERNATİF MALZEME
  aynı akış; shipped_material_id ≠ material_id  →  alt_reason ZORUNLU
  stok düşümü GÖNDERİLEN malzemeden yapılır

İPTAL
  depo çıkışı        → ReverseDocument (ters hareket)      + fulfillment 'cancelled'   [tek tx]
  şube transferi     → YENİ ters Transfer (hedef→kaynak)   + fulfillment 'cancelled'   [tek tx]
  satın alma (Faz 4) → stok hareketi yok; yalnız 'cancelled'
```

### G) Senkronizasyon — mevcut durum ve önerilen iyileştirme

**Mevcut:** 15 saniyelik tur; her turda 4-5 HTTP; sunucuda **22 ayrı sorgu** ile sürüm hesabı; her push sonrası
**tüm defterden bakiye yeniden hesabı**; damgasız iki tabloda **her turda tam gönderim**; her push'un ardından
**yankı pull** + ekran yenileme; hata yönetimi **grup bazlı** (tek hatalı satır tüm grubu 5 tur bekletir).

**Önerilen (ayrı "Faz S"):** S1 tek sorgulu sürüm hesabı → S2 hedefli bakiye yeniden hesabı → S3 yankı pull'un
kesilmesi → S4 uyarlanabilir tur aralığı → (opsiyonel) S5 damga/company_id migration'ı → (ayrı) S6 kayıt bazlı
kuyruk. Ayrıntı ve kanıtlar: §8.

### H) Migration listesi

| Kod | İçerik | Zorunlu mu | Additive | Canlı veri riski |
|---|---|---|---|---|
| **M-062** | `request_fulfillments` yeni tablo + 3 indeks | **Evet (Faz 3)** | ✅ | Yok (yeni tablo) |
| — | Eşzamanlılık düzeltmesi | — | — | **Migration gerekmiyor** |
| **M-S1** | `material_request_items` / `maintenance_materials` → `company_id` + damga kolonları | Hayır (öneri) | Kısmen (geri doldurma UPDATE'i var) | **Orta** — ayrı iş, ayrı onay |

### I) Test senaryoları

**Eşzamanlılık (PostgreSQL):**
- T-01 Stok 10; eşzamanlı 6 ve 7 → biri başarılı, diğeri kontrollü hata; defter toplamı 6; bakiye 4.
- T-02 Stok 10; eşzamanlı 6 ve 3 → **ikisi de başarılı**; bakiye 1 (kayıp düşüm yok).
- T-03 Aynı `operation_id` iki kez → tek hareket, aynı belge döner.
- T-04 Eşzamanlı transfer + çıkış (aynı malzeme) → tutarlı sonuç, negatif yok.
- T-05 3 denemede de çakışma → temiz hata, **hiç kayıt oluşmamış**.
- T-06 SQLite'ta T-01..T-05 → davranış **değişmedi** (regresyon).
- T-07 `RecomputeBalances` sonrası bakiye = defter toplamı.

**Faz 3 karşılama:**
- T-08 Kısmi karşılama: 10'luk kalem → 4 + 3 → kalan 3.
- T-09 Fazla karşılama denemesi (kalan 3 iken 5) → reddedilir.
- T-10 Aynı kalemi iki kullanıcı aynı anda karşılar → toplam talebi aşmaz.
- T-11 Alternatif malzeme + gerekçe yok → reddedilir; gerekçe var → gönderilen malzemeden düşer.
- T-12 Stok yetersiz → hiçbir kayıt oluşmaz (fulfillment de yok).
- T-13 Transaction ortasında hata → tam rollback.
- T-14 **Karşılama sonrası `operation_status` DEĞİŞMEDİ** (otomatik ezme yok).
- T-15 İptal (depo çıkışı) → ters hareket + `status='cancelled'`; karşılanan miktar azaldı.
- T-16 İptal (şube transferi) → `ReverseDocument` reddeder; ters transfer üretilir; iki şube bakiyesi doğru.
- T-17 Yetkisiz şube / başka firma şubesi → `Forbidden`.
- T-18 `request_ops` var ama `request_ops_warehouse` yok → reddedilir.
- T-19 Ağ kesintisi sonrası aynı `operation_id` ile tekrar → çift kayıt yok.

**Senkron:**
- T-20 `request_fulfillments` masaüstünde oluşturulur → push sonrası sunucuda **var**.
- T-21 Sunucuda oluşturulan karşılama → pull sonrası masaüstünde **var**.
- T-22 Aynı snapshot iki kez → duplicate yok (upsert/LWW).

### J) Faz 3'ün önerilen küçük adımları

| Adım | İçerik | Migration | Deploy |
|---|---|---|---|
| **Faz 3-Ön** | Eşzamanlılık düzeltmesi (CAS + tekrar) — `StockService` **ve** `MaintenanceService`; sadece testler | **Yok** | API + masaüstü (davranış değişmez) |
| **Faz 3a** | `request_fulfillments` migration + servis (oluştur/iptal/listele) + senkron listesi + testler | **M-062** | API |
| **Faz 3b** | Masaüstü: Talep Operasyonları ekranında karşılama paneli (miktar, kaynak, alternatif, gerekçe) + karşılama göstergesi | Yok | Masaüstü sürümü |
| **Faz 3c** | Web: aynı işlevin MudBlazor karşılığı (platform kuralı: web eksik bırakılmaz) | Yok | Web |
| **Faz 3d** | Şube transferiyle karşılama + iptal/ters transfer akışı + yetki/şube kararlarının uygulanması | Yok | API + masaüstü + web |
| *(ayrı)* **Faz S** | Senkron performansı S1→S4 | Yok | API + masaüstü |

---

## KODLAMAYA BAŞLAMADAN ÖNCE ONAYLAMANIZ GEREKEN KARARLAR

| # | Karar | Seçenekler | Önerim |
|---|---|---|---|
| **1** | Eşzamanlılık çözüm yöntemi | (a) İyimser CAS + sınırlı tekrar · (b) `SELECT FOR UPDATE` + dialect dallanması · (c) Şimdilik dokunma | **(a)** — iki veritabanında aynı davranış, şema değişmez |
| **2** | Düzeltmenin kapsamı | (a) `StockService` geneli · (b) yalnız Faz 3 yolu | **(a)** — hata bugün tüm stok ekranlarında var |
| **3** | `MaintenanceService`'in ayrı bakiye kopyası da düzeltilsin mi | Evet / Hayır (sonraya) | **Evet, aynı pakette** |
| **4** | Sıralama | (a) "Faz 3-Ön" ayrı ve önce · (b) Faz 3a ile birlikte | **(a)** — tek başına test edilip yayınlanır |
| **5** | Tekrar (retry) sayısı ve hata metni | 3 deneme + *"Bu malzeme üzerinde aynı anda başka bir işlem yapıldı. Lütfen tekrar deneyin."* | **3 deneme**; metni onayınıza sunuyorum |
| **6** | Fulfillment + stok **aynı transaction** için `StockService`'e iç giriş noktası eklensin mi | Evet / Hayır (iki transaction + telafi) | **Evet** — "Hayır" veri bütünlüğünü riske atar |
| **7** | `request_fulfillments` tablosu `company_id` + `created_at`/`updated_at` **ile** mi doğsun | Evet (önerilen model) / Hayır (minimal) | **Evet** — senkron ve tenant güvenliği için |
| **8** | Yeni tablo senkron listesine (`Tables` + `TableModule`) eklensin mi | Evet / Hayır | **Evet** — aksi halde masaüstü verisi sunucuya ulaşmaz |
| **9** | Depodan karşılamada `stock/Create` yetkisi de aransın mı | (a) Evet, iki yetki de zorunlu · (b) Servis içinde stok yetkisi atlansın · (c) Yeni `request_ops_fulfill` modülü | **(a)** |
| **10** | Karşılama iptali hangi yetkiye bağlansın | (a) Mevcut `SpecialButtons.Reverse` · (b) Yeni özel buton | **(a)** |
| **11** | Şube transferiyle karşılamanın **iptali**, şubeye bağlı kullanıcıda `EnforceOwnBranch`'e takılıyor | (a) Yalnız "Tüm Şubeler"/admin iptal edebilsin · (b) Ters transfer için operasyon kullanıcısına özel izin · (c) Şimdilik iptal desteklenmesin | **(a)** — en güvenli ve kural bozmayan |
| **12** | Karşılama, operasyon durumunu **hiçbir zaman** otomatik değiştirmesin (yalnız gösterge + öneri ipucu) | Evet / Hayır | **Evet** — mevcut kararınızın devamı |
| **13** | Yayın öncesi **salt-okuma** tutarlılık kontrolü (defter ↔ `stock_balances` farkı) yapılsın mı | Evet (yalnız raporla) / Hayır | **Evet** — `RecomputeBalances` onayınız olmadan çalıştırılmaz |
| **14** | Senkron iyileştirmesi ayrı **"Faz S"** olarak mı yürüsün | Evet / Hayır (Faz 3'e dahil) | **Evet, ayrı** |
| **15** | §8.5 çok-kiracı sızıntısı (`company_id`siz iki tablo) için **M-S1** migration'ı planlansın mı | (a) Evet, ayrı iş olarak planla · (b) Şimdilik yalnız kayıt altına al | **(a)** — ama Faz 3'ten sonra |

> **Hiçbir madde onayınız olmadan uygulanmayacaktır.** Onaydan sonra yalnız onayladığınız kapsamla,
> CLAUDE.md §2.1 gereği motor önerisi sunularak başlanacaktır.
