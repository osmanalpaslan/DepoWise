# FAZ 3-ÖN — TEKNİK UYGULAMA PLANI + AÇIK KARAR ANALİZLERİ

**Tarih:** 2026-08-08 · **Motor:** Opus 5 (değişiklik gerekmiyor)
**Durum:** YALNIZ PLAN/ANALİZ — kod yazılmadı, migration uygulanmadı, deploy yapılmadı, canlı veriye dokunulmadı.
**Önceki rapor:** [FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md](FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md)
**Kullanıcı kararları:** 1–10, 12, 13, 14 onaylandı · **11 ve 15 varsayım yapılmadan analiz edildi, onay bekliyor.**

---

## 1. FAZ 3-ÖN TEKNİK UYGULAMA PLANI

### 1.1 Amaç

`stock_balances` üzerinde "oku → kontrol et → yaz" arasındaki yarış durumunu (race condition) **tüm** stok
değiştiren giriş noktalarında kapatmak. PostgreSQL ve SQLite'ta **aynı** kod, **aynı** davranış.

### 1.2 Stok bakiyesini DEĞİŞTİREN tüm giriş noktaları (tam envanter — kanıtlı)

| # | Giriş noktası | Yer | Bakiyeye nasıl dokunuyor | Faz 3-Ön'de |
|---|---|---|---|---|
| 1 | `ReceiveIn` (giriş) | `StockService.cs:65-76` | `ApplyLine` → `ApplyDelta` | ✅ Kapsamda |
| 2 | `IssueOut` (çıkış) | `StockService.cs:79-91` | `ApplyLine` → `ApplyDelta` | ✅ Kapsamda |
| 3 | `Transfer` | `StockService.cs:94-117` | `ApplyLine` ×2 (çıkış + giriş) | ✅ Kapsamda |
| 4 | `Count` (sayım) | `StockService.cs:152-174` | `ReadBalance` + `ApplyLine` (adjustment) | ✅ Kapsamda |
| 5 | `ReverseDocument` (iptal) | `StockService.cs:177-207` | **Doğrudan** `ApplyDelta` (kendi transaction'ı) | ✅ Kapsamda |
| 6 | `MaintenanceService.Save` | `MaintenanceService.cs:114` | **Kendi `ApplyDelta` kopyası** (`:351-374`) | ✅ Kapsamda (karar 3) |
| 7 | `MaintenanceService.Cancel` | `MaintenanceService.cs:155` | Aynı kopya | ✅ Kapsamda |
| 8 | `RecomputeBalances` | `StockService.cs:398-434` | Defterden **mutlak** yeniden yazım | ⚠️ **Karar gerekiyor — K-3** |
| 9 | Senkron snapshot upsert | `BusinessSyncService.UpsertRow` (`:553-601`) | `stock_balances` satırlarını LWW ile yazar | ⚠️ **Bilinçli istisna — K-4** |

> 6 ve 7 numaralı maddeler, kararınız gereği (madde 3) **aynı ortak mekanizmaya taşınacak** — aynı stok için
> iki farklı güvenlik mantığı kalmayacak.

### 1.3 Yapılacak değişikliğin özeti

1. **Tek ortak bakiye yazıcısı** oluşturulur: `StockBalanceWriter` (Infrastructure).
   `StockService.ApplyDelta` ve `MaintenanceService.ApplyDelta` **silinir**, ikisi de bu ortak sınıfı çağırır.
   → *Aynı stok için tek güvenlik mantığı* (kararınız madde 3).
2. Ortak yazıcı, bakiyeyi **iyimser CAS** ile yazar (§5).
3. Çakışmada `StockConcurrencyException` fırlatılır; **transaction sınırında** yakalanıp **en fazla 3 kontrollü
   tekrar** yapılır (§4, §5).
4. Tekrar da tükenirse: kullanıcıya **teknik olmayan** mesaj, log'a **tam teknik ayrıntı** (kararınız madde 5).
5. **Hiçbir iş kuralı, imza, yetki veya ekran değişmez.** Amaç yalnızca yarış durumunu kapatmaktır.

### 1.4 Kapsam DIŞI (bilinçli)

- Faz 3'ün kendisi (karşılama tablosu, ekranlar) — **kararınız madde 4** gereği sonra.
- Senkron performansı (Faz S) — ayrı iş.
- `material_request_items` / `maintenance_materials` `company_id` migration'ı — **§9, onay bekliyor**.
- Hiçbir kullanıcı arayüzü değişikliği.

---

## 2. DEĞİŞECEK DOSYA / KATMANLAR

| Katman | Dosya | Değişiklik | Risk |
|---|---|---|---|
| Application | `DepoWise.Application/Common/` → **yeni** `StockConcurrencyException.cs` | Yeni istisna tipi (mevcut `NegativeStockException` ile aynı desende) | Yok |
| Infrastructure | **yeni** `Materials/StockBalanceWriter.cs` | Tek ortak CAS'li bakiye yazıcısı + tekrar yardımcısı | Orta (çekirdek) |
| Infrastructure | `Materials/StockService.cs` | `ApplyDelta` (`:364-381`) ortak yazıcıya devredilir; `RunDocument` (`:312-339`) ve `ReverseDocument` (`:177-207`) tekrar sarmalayıcısıyla çevrilir | **Yüksek** (kritik kod) |
| Infrastructure | `Maintenance/MaintenanceService.cs` | Kendi `ApplyDelta` kopyası (`:351-374`) silinir → ortak yazıcı; `Save` (`:65`) ve `Cancel` (`:135`) tekrar sarmalayıcısına alınır | Orta-yüksek |
| Infrastructure | *(değişmez)* `Database/DbCommandExtensions.cs` | `BeginImmediate` **aynen kalır** — SQLite koruması bozulmaz | Yok |
| API | `DepoWise.Api/Program.cs` | Yalnız hata eşlemesi: `StockConcurrencyException` → **HTTP 409** + kullanıcı mesajı + `Console.Error` log satırı | Düşük |
| Desktop | Hata gösterimi (mevcut ortak hata yolu) | Yeni mesaj metni; ekran/akış değişmez | Düşük |
| Web | Aynı | Aynı | Düşük |
| Test | **yeni** `tests/.../StockConcurrencyTests.cs` + **yeni** `PostgresStockConcurrencyTests.cs` | §7 | — |

**Toplam: 6 kaynak dosya + 2 test dosyası.** (CLAUDE.md §3 sınırının içinde; alt adıma bölmeye gerek yok.)

---

## 3. MIGRATION GEREKİYOR MU?

**HAYIR — Faz 3-Ön için hiçbir migration yoktur.**

- `stock_balances` şeması **değişmez** (`Migration005_Materials.cs:127-134`).
- Kolon tipi (`quantity TEXT`) **değişmez** → canlı veride hiçbir dönüşüm yok.
- Yeni indeks **gerekmiyor**: `stock_balances` birincil anahtarı zaten `(material_id)`; CAS bu anahtar üzerinden
  çalışır.
- Mevcut satırlar **hiç UPDATE edilmez** (plan hiçbir toplu güncelleme içermez).

> Kararınız madde 13 gereği **yayın öncesi salt-okuma tutarlılık kontrolü** ayrıca yapılacaktır (§7.4) —
> bu da veri değiştirmez.

---

## 4. TRANSACTION SINIRI

### 4.1 Sınır değişmiyor

| İşlem | Transaction sınırı | Kanıt |
|---|---|---|
| Giriş / Çıkış / Transfer / Sayım | **`RunDocument`** — bir belge = bir transaction | `StockService.cs:321-337` |
| Belge iptali | `ReverseDocument`'in kendi transaction'ı | `StockService.cs:184-206` |
| Bakım kaydı | `MaintenanceService.Save`'in kendi transaction'ı | `MaintenanceService.cs:72` |
| Bakım iptali | `MaintenanceService.Cancel`'ın kendi transaction'ı | `MaintenanceService.cs:142` |

### 4.2 Tekrar (retry) sınırı = transaction sınırı

Tekrar **her zaman en dıştaki transaction sınırında** yapılır: transaction geri alınır (rollback), **her şey**
(belge, hareketler, denetim kaydı) baştan üretilir. **Kısmi tekrar yoktur.**

**Doğrulanan ön koşul:** `RunDocument`'a verilen gövde (`body`) yalnızca veritabanı işlemi yapar
(`ApplyLine`, `InsertCountLine`) — dosya yazma, ağ çağrısı, sayaç artırma gibi **geri alınamaz yan etkisi
yoktur** (`StockService.cs:71-75, 86-90, 110-116, 158-172`). Bu yüzden gövdeyi yeniden çalıştırmak güvenlidir.

### 4.3 İdempotency ile ilişkisi

Tekrar sırasında **aynı `operation_id`** kullanılır. Başarısız deneme geri alındığı için o `operation_id` ile
hiçbir satır kalmaz → `FindDocumentByOperation` (`StockService.cs:325`) boş döner → işlem normal ilerler.
Başarılı bir denemeden sonra gelen tekrar istekleri ise mevcut belgeyi döndürür.
Ek güvenlik ağı: `ux_stock_movements_operation` UNIQUE indeksi (`Migration005_Materials.cs:123`).

### 4.4 Faz 3 bağlantısı (kararınız madde 6 — şimdi uygulanmıyor)

Faz 3'te karşılama kaydı **aynı transaction sınırının içine** girecek şekilde `StockService`'e "verilen
bağlantı/transaction üzerinde çalış" iç giriş noktası eklenecektir. Faz 3-Ön bu noktayı **hazırlar** ama
kullanmaz: tekrar sarmalayıcısı, gövdeye ne konursa konsun (karşılama kaydı dahil) tamamını geri alıp
yeniden çalıştıracak biçimde tasarlanır. → "Yarım durum" (karşılama var/stok yok veya tersi) **mimarî olarak
imkânsız** olur.

---

## 5. CAS / RETRY ALGORİTMASI

### 5.1 Ortak bakiye yazıcısı (sözde kod)

```
ApplyDelta(conn, tx, companyId, materialId, signedQty, now, allowNegative):

    rawText ← SELECT quantity FROM stock_balances WHERE material_id = @m      -- HAM METİN olarak saklanır
    current ← Money.Parse(rawText)                                            -- yoksa 0
    updated ← current + signedQty

    if (!allowNegative && updated < 0)
        throw NegativeStockException(...)                                     -- mevcut davranış, DEĞİŞMEZ

    if (rawText is NULL)                                    -- bakiye satırı henüz yok
        n ← INSERT INTO stock_balances(company_id, material_id, quantity, updated_at)
            VALUES(@c,@m,@q,@now)
            ON CONFLICT(material_id) DO NOTHING
        if (n = 0) throw StockConcurrencyException          -- araya biri girip satırı oluşturdu
    else
        n ← UPDATE stock_balances
            SET quantity=@q, updated_at=@now
            WHERE material_id=@m AND quantity=@rawText      -- ⚠️ OKUNAN HAM METNİN AYNISI
        if (n = 0) throw StockConcurrencyException          -- değer değişmiş → yarış
```

### 5.2 ⚠️ Kritik tasarım detayı — karşılaştırma HAM METİNLE yapılmalı

`Money.Serialize(decimal)` = `value.ToString(InvariantCulture)` (`Money.cs:18`). .NET `decimal` tipi **ondalık
basamak sayısını korur**: `10m` → `"10"`, `10.00m` → `"10.00"` — **değer olarak eşit, metin olarak farklı.**

→ CAS koşuluna **yeniden üretilmiş** bir metin (`Money.Serialize(Money.Parse(x))`) konursa, veritabanındaki
metinden farklı olabilir ve **her denemede kalıcı olarak** 0 satır etkilenir → **sonsuz sahte çakışma**.
Bu yüzden koşula **veritabanından okunan ham metnin kendisi** konur. (Bu, planın en kolay gözden kaçan ve en
tehlikeli noktasıdır; teste ayrıca konu edilecektir — T-05.)

**Metin karşılaştırması iki veritabanında da güvenli mi? Evet:**
`Migration053_PostgresTurkishCollations.cs` yalnız **collation (harf sıralama kuralı) tanımlar** (`:37-39`) ve
bunlar **sorgu ifadelerinde** kullanılır; **hiçbir kolon tanımında** kullanılmaz (`ALTER TABLE ... COLLATE`
yok — arandı, bulunmadı). Dolayısıyla `stock_balances.quantity` varsayılan, **deterministik** karşılaştırma
kullanır → `=` bayt düzeyinde kesindir. SQLite'ta da aynıdır.

### 5.3 Tekrar (retry) politikası — kararınız madde 5

```
RunWithRetry(islem):
    for deneme in 1..4:                     -- ilk deneme + EN FAZLA 3 TEKRAR
        try:  return islem()                -- kendi transaction'ını açar/commit eder
        catch StockConcurrencyException:
            log("[stock-cas] conflict ...") -- tam teknik ayrıntı
            if (deneme = 4) throw StockBusyException(kullanıcı mesajı)
            bekle(10..40 ms arası, rastgele) -- kısa ve sınırlı; toplam en fazla ~120 ms
```

**Kararınıza uygunluk:**
- ✅ En fazla **3 tekrar** (toplam 4 deneme).
- ✅ **Sonsuz retry yok** — sabit üst sınır.
- ✅ **Agresif polling yok** — bekleme yalnız çakışma anında, milisaniye ölçeğinde, döngüsüz.
- ✅ **Sunucuya ek yük yok** — çakışma olmadığında hiçbir ek sorgu çalışmaz; CAS zaten yapılan UPDATE'in
  kendisidir (ekstra `SELECT` **eklenmiyor**, mevcut `ReadBalance` kullanılıyor).

### 5.4 Hata mesajları — kararınız madde 5

| Yer | Metin |
|---|---|
| **Kullanıcı (masaüstü + web)** | *"İşleminiz tamamlanamadı. Bu malzeme üzerinde aynı anda başka bir işlem yapıldı. Lütfen ekranı yenileyip tekrar deneyin."* |
| **Sunucu logu** | `[stock-cas] conflict company=<id> material=<id> op=<operationId> attempt=3/4 expected='10.00' rows=0 user=<id> branch=<id> ts=<UTC>` |
| **Tükenme logu** | `[stock-cas] give-up ... (3 tekrar sonrası)` |
| **HTTP** | `409 Conflict` + aynı kullanıcı mesajı (istemci "tekrar dene" diyebilsin) |

> Kullanıcı mesajında "CAS", "transaction", "concurrency" gibi teknik terim **geçmez** (CLAUDE.md §2).

### 5.5 SQLite'ta ne olur?

`BeginImmediate` (`DbCommandExtensions.cs:37-38`) SQLite'ta aynı anda tek yazara izin verdiği için
CAS koşulu **her zaman tutar** → çakışma istisnası **hiç** fırlamaz, tekrar **hiç** çalışmaz.
→ **Masaüstü davranışı birebir aynı kalır.** (T-06 ile kanıtlanacak.)

---

## 6. YETKİ KONTROL NOKTALARI

### 6.1 Faz 3-Ön'de yetki DEĞİŞMİYOR (bilinçli)

Faz 3-Ön yalnız eşzamanlılık düzeltmesidir; **hiçbir yetki eklenmez, kaldırılmaz veya gevşetilmez.**
Mevcut ve korunacak kontroller:

| İşlem | Kontrol | Yer |
|---|---|---|
| Giriş / Çıkış / Transfer / Sayım | `stock` + `Create` | `StockService.cs:69, 83, 99, 155` |
| Belge iptali | `stock` + `Edit` **ve** özel buton `btn-reverse` | `StockService.cs:179-180` |
| Şube kısıtı (çıkış/transfer) | `EnforceOwnBranch` — şubeli kullanıcı yalnız kendi şubesinden | `StockService.cs:122-130` |
| Malzeme sahipliği (tenant) | `EnsureMaterialOwned` | `StockService.cs:345` |
| Bakım kaydı/iptali | `maintenance` modül yetkileri | `MaintenanceService.Save/Cancel` |

**Doğrulama kuralı:** Faz 3-Ön'ün kabul kriterlerinden biri, mevcut yetki testlerinin **tamamının değişmeden**
geçmesidir.

### 6.2 Faz 3'te eklenecek kontrol noktaları (kararınız madde 9 — şimdi uygulanmıyor)

| Nokta | Kontrol | Katman |
|---|---|---|
| Karşılama oluşturma | `request_ops` (Edit) **+** `request_ops_warehouse` (Edit) **+** `stock` (Create) | **Servis (Infrastructure)** — API ve UI'dan bağımsız, fail-closed |
| Aynı işlem | Şube: `BranchScope.Active(s)` (**sunucuda** belirlenir; istemciden gelen şubeye güvenilmez) | Servis |
| Aynı işlem | Tenant: `company_id` oturumdan zorlanır | Servis |
| API ucu | Yetkisiz → `403`; UI ayrıca butonu gizler ama **UI gizlemesi güvenlik sayılmaz** | API + UI |
| Karşılama iptali | `stock` (Edit) + `btn-reverse` (bkz. §6.3) | Servis |

### 6.3 Kararınız madde 10 — mevcut `Reverse` yetkisinin kapsamı incelendi

**Bulgu (kanıtlı): `btn-reverse` yetkisi bugün SADECE tek bir yerde kullanılıyor.**

- Tanım: `AppModules.cs:122` → `public const string Reverse = "btn-reverse";  // ters kayıt / iptal`
- **Tüm kod tabanında tek kullanım:** `StockService.cs:180` (`ReverseDocument` içinde).
  (`src` altında `SpecialButtons.Reverse` araması başka sonuç vermedi.)
- Kontrol mantığı: `CanUseButton` = `IsAdmin(s) || s.Permissions.HasButton(buttonKey)`
  (`AccessControl.cs:87-88`) → **deny-by-default**, admin bypass'lı.

**Değerlendirme:**

| Soru | Cevap |
|---|---|
| Fazla geniş mi? | **Hayır.** Yalnız "stok belgesini ters kayıtla iptal et" işlemini kapsıyor; başka hiçbir ekranda kullanılmıyor. |
| Yetersiz mi? | **Hayır ama tek başına yeterli değil** — `ReverseDocument` ayrıca `stock` + `Edit` de arıyor (`:179`). İkisi birlikte yeterli koruma sağlıyor. |
| Karşılama iptali için uygun mu? | **Evet.** Karşılama iptali fiilen bir ters stok kaydıdır; aynı yetkiyi kullanmak tutarlıdır ve yeni yetki eklemeyi gerektirmez. |
| Dikkat edilecek nokta | `btn-reverse` **firma/modül ayrımı yapmaz** — verildiğinde tüm stok belgesi tiplerinde iptal hakkı verir. Karşılama iptalini de kapsayacağı için, yetkiyi vereceğiniz kişilerin **stok belgesi iptali** de yapabileceğini bilerek vermelisiniz. |

**Sonuç: mevcut yetki uygundur; "fazla geniş" veya "yetersiz" değildir → kodlamadan önce ayrıca bildirim
gerektiren bir sorun yok.** (Kararınız madde 10'un koşulu karşılandı.)

---

## 7. TEST STRATEJİSİ

### 7.1 Eşzamanlılık testleri (PostgreSQL — asıl kanıt)

Mevcut PG test altyapısı var (`tests/DepoWise.Tests/Postgres*Tests.cs` — 8 dosya), **ayrı test veritabanında**
çalışır, canlıya dokunmaz.

| Kod | Senaryo | Beklenen |
|---|---|---|
| T-01 | Stok 10; **eşzamanlı** 6 ve 7 çıkış (2 iş parçacığı, 2 bağlantı) | Biri başarılı, diğeri kontrollü hata; defter toplamı 6; bakiye 4 |
| T-02 | Stok 10; eşzamanlı 6 ve 3 | **İkisi de başarılı**; bakiye 1 (kayıp düşüm YOK — bugünkü hatanın kanıtı) |
| T-03 | Eşzamanlı transfer + çıkış (aynı malzeme) | Tutarlı; negatif yok; iki şube bakiyesi doğru |
| T-04 | 20 iş parçacığı × 1 birim çıkış, stok 10 | Tam **10** başarılı, 10 kontrollü hata; bakiye 0 |
| **T-05** | **Ondalık basamak tuzağı**: bakiye `"10.00"` metniyle yazılı iken çıkış | CAS **sahte çakışma üretmez**, ilk denemede başarılı (§5.2) |
| T-06 | **SQLite'ta** T-01…T-04 | Davranış **birebir aynı**; çakışma istisnası hiç fırlamıyor |
| T-07 | 4 denemenin hepsi çakışırsa | Temiz hata; **hiçbir kayıt oluşmamış** (belge, hareket, denetim) |
| T-08 | Aynı `operation_id` ile tekrar (tekrar sonrası) | Tek belge, tek hareket seti |

**Çakışmayı deterministik üretme yöntemi:** testte iki gerçek bağlantı açılır; birinci transaction bakiyeyi
okur, ikinci transaction commit eder, sonra birinci yazmayı dener → CAS 0 satır döndürür. (Zamanlamaya bağlı
"flaky" test yazılmayacak.)

### 7.2 Regresyon (kararınız: mevcut davranış değişmeyecek)

- **Mevcut 767 testin tamamı** değişmeden geçmeli (CLAUDE.md §7.16).
- Ekran bazlı QA (CLAUDE.md §7.1): Faz 3-Ön hiçbir ekranı değiştirmediği için QA kapsamı **stok yazan
  ekranlarla** sınırlıdır: Giriş-Çıkış · Stok Sayım · Araç Bakımları · Günlük Faaliyet.
- Coverage Matrix + `docs/tests/StokEszamanlilik_Test_Report.md` (CLAUDE.md §7.13/§7.14).

### 7.3 Negatif/sınır testleri

Miktar 0 · negatif miktar · bakiye satırı hiç yokken ilk hareket · çok büyük ondalık · aynı anda giriş+çıkış ·
ters kayıt sırasında yarış · bakım malzemesi (negatife izinli yol) yarışı.

### 7.4 Yayın öncesi salt-okuma tutarlılık kontrolü (kararınız madde 13)

**Ne yapar:** Her malzeme için `Σ(direction × quantity)` (hareket defteri) ile `stock_balances.quantity`
karşılaştırılır; **fark olanlar listelenir.**
**Ne yapmaz:** Hiçbir `INSERT/UPDATE/DELETE` çalıştırmaz. `RecomputeBalances` **çalıştırılmaz** (onayınız
olmadan asla).
**Ne zaman:** Faz 3-Ön kodu hazır olduğunda, **deploy'dan hemen önce** — yani şu an değil (bu aşamada canlıya
dokunulmayacak talimatınız gereği).
**Çıktı:** malzeme kodu/adı · defter toplamı · kayıtlı bakiye · fark · son hareket tarihi → sana ayrıntılı rapor.
Fark çıkarsa ne yapılacağına **birlikte** karar veririz.

---

## 8. MADDE 11 — TRANSFER İPTALİ YETKİ ANALİZİ *(varsayım yapılmadı; ONAY BEKLİYOR)*

### 8.1 ⚠️ Önce dürüst tespit: istediğin 4 aşamanın 3'ü bugün veri modelinde YOK

`StockService.Transfer` (`:109-116`) kaynak çıkışı ve hedef girişi **tek transaction içinde, aynı anda** yazar:

```csharp
ApplyLine(..., -1, $"{operationId}:out", "transfer", fromBranchId, ...);
ApplyLine(..., +1, $"{operationId}:in",  "transfer", toBranchId,   ...);
```

**Sonuç: stok açısından transfer ANLIKTIR.** Mal "yolda" iken hedef şubenin bakiyesi **zaten artmıştır**.
`stock_documents` tablosunda yalnız `status` = `active | cancelled` vardır (`Migration006:31`) — *yola çıktı /
ulaştı / teslim edildi* gibi bir sevkiyat durumu **yoktur**.

Bu bilgi bugün **yalnız talep operasyon durumunda** var: `Shipped` (Sevk Edildi) · `ArrivedAtBranch` (Şubeye
Ulaştı) · `Delivered` (Teslim Edildi) — ama bunlar `material_requests` üzerindedir ve **stok belgesine bağlı
değildir** (`RequestOperationStateMachine.cs:53-63`).

→ Aşağıdaki tabloda her senaryonun **bugünkü gerçek karşılığını** ayrıca yazdım. "Yolda" ayrımını gerçekten
istiyorsan, bu **veri modeli eklemesi** gerektirir (§8.4) — bu da senin ayrı kararın.

### 8.2 Teknik kısıt: ters transferi kim çalıştırabilir?

- `ReverseDocument` transferi **reddediyor** (`StockService.cs:191-192`) → iptal ancak **yeni bir ters
  transfer** (hedef → kaynak) ile yapılabilir.
- Ters transferin **kaynağı artık HEDEF şubedir**. `EnforceOwnBranch` (`:122-130`) şubeli kullanıcının yalnız
  **kendi** şubesinden transfer başlatmasına izin verir.
- **Dolayısıyla teknik olarak ters transferi yalnız şunlar yapabilir:** hedef şubenin kullanıcısı,
  "Tüm Şubeler" kapsamındaki kullanıcı (`BranchScope.Active(s) == null`), veya admin/süper admin
  (`AccessControl.IsAdmin`).
- **Gönderen şubenin kullanıcısı, teknik olarak ters transferi ÇALIŞTIRAMAZ** — bu, politika tercihinden
  bağımsız, kodun bugünkü gerçeğidir.

### 8.3 Senaryo tablosu — ÖNERİ (onayına sunuluyor)

Kısaltmalar: **G** = gönderen (kaynak) şube kullanıcısı · **A** = alıcı (hedef) şube kullanıcısı ·
**T** = "Tüm Şubeler" yetkili kullanıcı · **AD** = admin/süper admin

| # | Senaryo | Bugünkü karşılığı | İptal edebilmeli (önerim) | Neden |
|---|---|---|---|---|
| 1 | **Transferi başlatan şube** | `stock_documents.from_branch_id` | G ❌ (teknik olarak yapamaz) · T ✅ · AD ✅ | Ters transferin kaynağı hedef şubedir; G'nin şube kapsamı buna izin vermez (§8.2) |
| 2 | **Gönderen şube** | aynı | G ❌ · T ✅ · AD ✅ | Aynı |
| 3 | **Alıcı şube** | `to_branch_id` | **A ✅** · T ✅ · AD ✅ | Mal fiilen A'nın stoğundadır; iadeyi yapabilecek tek şube kullanıcısı A'dır |
| 4 | **Transfer yola çıkmadan önce** | ⚠️ **Yok** — belge oluştuğu an mal hedeftedir | *(bugün ayrım yapılamaz)* | Ayrım istiyorsan §8.4 gerekir |
| 5 | **Transfer yoldayken** | ⚠️ **Yok** (yalnız talep durumu `Sevk Edildi` olabilir, stoktan bağımsız) | A ✅ · T ✅ · AD ✅ | Aynı |
| 6 | **Transfer alındıktan sonra** | Fiilen 3. satırla aynı | A ✅ · T ✅ · AD ✅ | Mal hedefte |
| 7 | **Transfer tamamen kapandıktan sonra** | Talep operasyon durumu `Tamamlandı`/`İptal` (terminal) — `RequestOperationStateMachine.cs:71-76` | **Hiç kimse ❌** (yalnız AD ✅, gerekçe zorunlu) | Kapanmış işi geriye almak denetim izini bozar; istisna yalnız yönetici düzeltmesi olmalı |
| 8 | **İptali yapanın şube yetkisi** | `BranchScope.Active(s)` | Yalnız **kendi şubesi hedef şube ise** | `EnforceOwnBranch` kuralı (`:122-130`) |
| 9 | **"Tüm Şubeler" yetkisi** | `BranchScope.Active(s) == null` | ✅ Her senaryoda | Şube kısıtı yok |
| 10 | **Admin / süper admin** | `AccessControl.IsAdmin` | ✅ Her senaryoda (7 dahil, gerekçe zorunlu) | Mevcut bypass deseni |

**Her durumda ortak koşullar (öneri):** `stock` + `Edit` yetkisi · `btn-reverse` özel butonu · **gerekçe
zorunlu** · ters transfer **ayrı belge** olarak kaydedilir ve orijinaline bağlanır · denetim (audit) kaydı.

### 8.4 Onayına sunulan üç politika seçeneği

| Seçenek | İçerik | Artı | Eksi |
|---|---|---|---|
| **P-1** *(önerim)* | Yukarıdaki tablo: **alıcı şube + Tüm Şubeler + admin** iptal edebilir; kapanmış işte yalnız admin | Teknik gerçekle uyumlu; gönderen şube istismar edemez | Gönderen şube kendi hatasını tek başına düzeltemez → alıcıdan veya yöneticiden ister |
| **P-2** | Yalnız **Tüm Şubeler + admin** (ilk raporumdaki basit öneri) | En sıkı | Günlük işte yönetici darboğazı olur |
| **P-3** | P-1 + **sevkiyat durumu veri modeli** eklenir (`stock_documents.shipment_state`: hazırlanıyor/yolda/teslim alındı) → "yola çıkmadan önce" gönderen şube de iptal edebilir | Senin 7 aşamalı modelini gerçekten karşılar | **Yeni migration + yeni ekran akışı**; Faz 3'ün kapsamını büyütür |

> **Bu maddede hiçbir şey uygulanmayacaktır.** P-1 / P-2 / P-3'ten birini seçmeni bekliyorum.

---

## 9. MADDE 15 — `company_id` EKSİKLİĞİ RİSK ANALİZİ *(ayrı karar maddesi; ONAY BEKLİYOR)*

### 9.1 Etkilenen tablolar (kanıt)

| Tablo | Kolonlar | Eksikler |
|---|---|---|
| `material_request_items` (`Migration010_Requests.cs:39-49`) | id, request_id, material_id, quantity, vehicle_id, note | **company_id ❌ · created_at ❌ · updated_at ❌ · is_deleted ❌** |
| `maintenance_materials` (`Migration008_Maintenance.cs:66-75`, + `Migration059` from_team_stock) | id, maintenance_id, material_id, quantity, unit_price, from_team_stock | **company_id ❌ · created_at ❌ · updated_at ❌** |

İkisi de senkron listesinde (`BusinessSyncService.cs:45-55`).

### 9.2 Soru 1 — Parent kayıtlardan `company_id` güvenle türetilebilir mi?

**EVET, ikisi de %100 türetilebilir:**

| Çocuk tablo | Ebeveyn | Bağ | Ebeveynde `company_id` |
|---|---|---|---|
| `material_request_items` | `material_requests` | `request_id` **NOT NULL + FOREIGN KEY** (`Migration010:41, 46`) | ✅ `company_id TEXT NOT NULL` (`Migration010:21`) |
| `maintenance_materials` | `vehicle_maintenances` | `maintenance_id` **NOT NULL + FOREIGN KEY** (`Migration008:68, 71`) | ✅ `company_id TEXT NOT NULL` (`Migration008:43`) |

Bağ zorunlu (NOT NULL) ve yabancı anahtarla korunmuş olduğu için **belirsizlik yoktur**; her çocuk satırının
firması tektir ve kesindir.

**Tek risk:** yabancı anahtarın kapalı olduğu bir dönemde oluşmuş **yetim (parent'ı olmayan) satır**.
→ Migration planı bunu **önce sayar**, sıfır değilse **durur ve sana bildirir** (§9.6).

### 9.3 Soru 2 — Mevcut tek firma verisi güvenle geri doldurulabilir mi (backfill)?

**Evet.** Bugün canlıda tek gerçek firma var. Geri doldurma:
- **Ebeveynden türetilir** (sabit değer yazılmaz) → ileride ikinci firma eklense de doğru kalır.
- **Var olan hiçbir kolona dokunmaz** — yalnız yeni eklenen boş kolon doldurulur.
- **Geri alınabilir**: yanlış giderse yeni kolon `NULL`'a çekilebilir; eski kolonlar hiç değişmediği için
  veri kaybı riski yoktur.

### 9.4 Soru 3 — Birden fazla firma aktifken gerçekten sızıntı olur mu?

**EVET — ve düşündüğümden daha ciddi: yalnız okuma değil, YAZMA da mümkün.**

**(a) Okuma sızıntısı — kanıt:**
```csharp
var hasCompany = cols.Contains("company_id");
...
if (hasCompany) where.Add("company_id=@c");        // BusinessSyncService.cs:117, 122
```
`company_id` yoksa **firma filtresi hiç eklenmez** → `business-pull` ucu (`Program.cs:392`) sunucu
veritabanından **tüm firmaların** bu iki tablodaki satırlarını döndürür ve istemci bunları yerel veritabanına
yazar.

**(b) Yazma sızıntısı (daha ağır) — kanıt:**
```csharp
if (hasCompany) values["company_id"] = companyId;   // tenant zorla — BusinessSyncService.cs:566
```
Tenant zorlaması **yalnız `company_id` olan tablolarda** yapılır. Bu iki tabloda satır **yalnız birincil
anahtara (`id`) göre** upsert edilir → uygun yetkisi olan bir istemci, **başka bir firmanın** talep kalemini
veya bakım malzemesi satırını (miktar, malzeme, not) **üzerine yazabilir**. Mevcut `CanWrite` kontrolü
(`:277-282`) yalnız *kendi* modül yetkisine bakar, **hedef satırın firmasına bakmaz**.

**(c) Bugünkü fiili durum:** canlıda tek firma olduğu için **şu an gerçek bir zarar yoktur**. Risk, **ikinci
firma eklendiği anda** gerçeğe döner.

### 9.5 Soru 4 & 5 — Faz 3'ten önce zorunlu mu? Sonraya bırakılırsa ne olur?

**Faz 3 için teknik bir engel DEĞİLDİR:**
- Faz 3'ün yeni tablosu (`request_fulfillments`) `company_id` **ile** doğacağı için aynı hataya düşmez
  (kararın madde 7).
- Faz 3'ün stok/karşılama mantığı bu iki tablodan **bağımsızdır**.

**Ama gerçek son tarih Faz 3 değil, İKİNCİ FİRMADIR.** Sonraya bırakılırsa somut riskler:

| Risk | Ne zaman gerçekleşir | Şiddet |
|---|---|---|
| Bir firmanın talep kalemleri/bakım malzemeleri başka firmanın makinesine iner | 2. firma + senkron | **Yüksek** (gizlilik) |
| Bir firmanın kaydı başka firma tarafından üzerine yazılır | 2. firma + kötü niyetli/bozuk istemci | **Yüksek** (veri bütünlüğü) |
| Bu iki tablo her eşitlemede **tam** gönderilir (damga kolonu yok → delta yok, `BusinessSyncService.cs:178-179`) | Bugün de sürüyor | Orta (yavaşlık, sürekli artan) |
| Tablolar büyüdükçe düzeltme maliyeti artar | Zamanla | Orta |

### 9.6 Soru 6 — Migration güvenli şekilde nasıl yapılır?

**Aşamalı ve durdurulabilir plan (M-S1) — onayınla ayrı iş olarak:**

| Adım | İşlem | Veri riski |
|---|---|---|
| 0 | **Ön kontrol (salt-okuma):** yetim satır sayısı, toplam satır sayısı, firma dağılımı raporlanır. **Yetim varsa DURULUR ve sana bildirilir.** | Yok |
| 1 | Sunucu yedeği alınır (mevcut yedek mekanizması) | Yok |
| 2 | `ALTER TABLE ... ADD COLUMN company_id TEXT NULL` (+ `created_at`, `updated_at` **NULL'a izinli**) — **additive**, mevcut satırlar etkilenmez | Yok |
| 3 | **Ebeveynden geri doldurma:** `UPDATE ... SET company_id = (ebeveynin company_id'si)`. Zaman damgaları da ebeveynin `created_at`/`updated_at` değerinden doldurulur | Düşük — yalnız yeni/boş kolonlara yazar |
| 4 | **Doğrulama (salt-okuma):** `company_id IS NULL` sayısı 0 mı? Firma bazında satır sayıları ebeveynle tutuyor mu? | Yok |
| 5 | İndeks: `(company_id)` ve `(company_id, updated_at)` | Yok |
| 6 | Kolonlar `NOT NULL` yapılır — **yalnız 4. adım temizse ve senin ayrı onayınla** | Düşük |

**⚠️ Senkronla ilgili kritik yan etki (kaçırılmaması gereken):**
Bu tablolara `created_at`/`updated_at` eklemek, `StampColumn`'un davranışını değiştirir
(`BusinessSyncService.cs:178-179`) → tablolar birden "damgalı" olur ve **delta filtresi devreye girer**.
Geri doldurulan damgalar eski tarihli olacağı için, mevcut push watermark'ının **altında** kalan satırlar bir
daha gönderilmeyebilir. Projede bunun için **hazır bir mekanizma var**: `WatermarkEpoch`
(`BusinessSyncPushService.cs:141-142`) — sürüm bir artırılırsa tüm makineler **tek seferlik tam gönderim**
yapar. **M-S1 uygulanırsa epoch mutlaka artırılmalıdır.** (Bu, daha önce `stock_movements` için birebir aynı
şekilde yaşanmış ve çözülmüş bir durumdur — kod yorumu `:136-142`.)

### 9.7 Önerim

**M-S1'i Faz 3-Ön ve Faz 3 ile karıştırma; ama "ileriye at" da deme.**
Önerilen sıra: **Faz 3-Ön → M-S1 → Faz 3** *(veya)* **Faz 3-Ön → Faz 3 → M-S1**, tek şartla:
**ikinci firma açılmadan önce M-S1 mutlaka tamamlanmış olsun.**
Bunu ayrı bir karar maddesi olarak aşağıya koydum (K-6).

---

## KODLAMAYA BAŞLAMADAN ÖNCE ONAYIN GEREKEN NOKTALAR

| # | Konu | Seçenekler | Önerim |
|---|---|---|---|
| **K-1** | **Madde 11 — transfer iptali politikası** | **P-1** (alıcı şube + Tüm Şubeler + admin; kapanmışta yalnız admin) · **P-2** (yalnız Tüm Şubeler + admin) · **P-3** (P-1 + sevkiyat durumu veri modeli, migration ister) | **P-1** |
| **K-2** | **Madde 15 — M-S1 migration'ı ne zaman?** | (a) Faz 3-Ön'den hemen sonra, Faz 3'ten önce · (b) Faz 3'ten sonra · (c) Şimdilik yalnız kayıtta kalsın | **(a) veya (b)** — ama **ikinci firma açılmadan önce mutlaka**; (c)'yi önermiyorum |
| **K-3** | `RecomputeBalances` (defterden mutlak yeniden yazım) CAS'e alınsın mı? | (a) **Hayır** — otoriteli yeniden kurma olduğu için bilerek üzerine yazmalı (mevcut davranış korunur) · (b) Evet, o da CAS'lensin | **(a)** — (b) yeniden kurmayı kilitler; ayrıca Faz S'teki "yalnız etkilenen malzeme" iyileştirmesi çakışma penceresini zaten daraltır |
| **K-4** | Senkron `stock_balances` snapshot yazımı (`UpsertRow`) CAS dışında kalsın mı? | (a) **Evet, kalsın** — sunucu her push sonrası defterden yeniden hesaplıyor (`Program.cs:381`), otoriteli değer korunuyor · (b) Değişsin | **(a)** — Faz S kapsamında ayrıca ele alınır |
| **K-5** | Tekrar sayısı ve kullanıcı mesajı | 3 tekrar (toplam 4 deneme) + §5.4'teki metin | **Onayına sunuldu** — metni değiştirmek istersen söyle |
| **K-6** | Faz sırası | **Faz 3-Ön → (K-2 kararı) → Faz 3a/3b/3c/3d → Faz S** | Onayına sunuldu |

**K-1 ve K-2 cevaplanmadan hiçbir kod yazılmayacaktır.**
K-3/K-4/K-5 için önerimi onaylaman yeterli; itirazın yoksa "onaylıyorum" demen kâfi.
