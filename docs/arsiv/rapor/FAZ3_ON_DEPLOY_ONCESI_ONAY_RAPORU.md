# FAZ 3-ÖN · ADIM 4 — DEPLOY ÖNCESİ ONAY RAPORU

**Tarih:** 2026-08-08
**Durum:** 🟡 **DEPLOY İÇİN HAZIR — ONAY BEKLİYOR**
**Bu rapor hazırlanırken production'a HİÇBİR değişiklik uygulanmadı.**

---

## 1. DEPLOY ÖNCESİ SON KONTROL

| # | Kontrol | Beklenen | Gerçek | Sonuç |
|---|---|---|---|---|
| 1.1 | Çalışma ağacı temiz mi | Temiz | `git status` **boş** | ✅ |
| 1.2 | origin ile senkron mu | 0 fark | origin önde **0**, yerel önde **0** | ✅ |
| 1.3 | Beklenmeyen kod değişikliği | Yok | Yalnız 5 üretim dosyası (§2) | ✅ |
| 1.4 | Test sonuçları | 788/0/14 | **788 geçti / 0 başarısız / 14 atlandı** | ✅ |
| 1.5 | Faz 3-Ön kapsamı dışı değişiklik | Yok | Yok (§2) | ✅ |
| 1.6 | Migration dosyası oluştu mu | **Hayır** | `Migrations/` klasörü **hiç dokunulmadı** | ✅ |
| 1.7 | UI değişikliği | Yok | `.axaml` / `.razor` / `.css` **değişmedi** | ✅ |
| 1.8 | Yetki değişikliği | Yok | `AppModules` / `AccessControl` / `Permission*` **değişmedi** | ✅ |
| 1.9 | Canlı stok tutarlılık raporu commit+push | Evet | `1bc371c` — push edilmiş | ✅ |

---

## 2. DEĞİŞEN DOSYALAR

### 2.1 Üretim kodu — yalnız **5 dosya**

| Dosya | Değişiklik | Satır |
|---|---|---|
| `src/DepoWise.Infrastructure/Materials/StockBalanceWriter.cs` | **YENİ** — tek ortak bakiye yazıcısı (CAS + tekrar + 2 istisna tipi + doc_no yarış tanımı) | +194 |
| `src/DepoWise.Infrastructure/Materials/StockService.cs` | `ApplyDelta`/`ReadBalance` ortak yazıcıya devredildi · `RunDocument` + `ReverseDocument` tekrar sarmalayıcısı · `RecomputeBalances` iyimser koruması | ±145 |
| `src/DepoWise.Infrastructure/Maintenance/MaintenanceService.cs` | Kendi bakiye kopyası **silindi** → ortak yazıcı · `Save`/`Cancel` sarmalandı | ±37 |
| `src/DepoWise.Infrastructure/Materials/OpeningStockService.cs` | Kendi bakiye kopyası **silindi** → ortak yazıcı · `RecordOpening` sarmalandı | ±32 |
| `src/DepoWise.Api/Program.cs` | `StockBusyException` → **HTTP 409** (3 satır) | +3 |

### 2.2 Testler — 12 dosya (yayına gitmez)

Yeni: `StockConcurrencyTests.cs`, `PostgresStockConcurrencyTests.cs`, `PostgresTestGuard.cs`,
`PostgresTestGuardTests.cs`. Güncellenen: 8 mevcut PostgreSQL test dosyası (yalnız güvenlik kapısı çağrısı).

### 2.3 Belgeler — 6 dosya

`DEVAM.md`, `docs/YARIM_KALAN_ISLER.md` ve 4 Faz 3-Ön raporu/planı.

### 2.4 Kapsam dışı değişiklik: **YOK**

Masaüstü ekranları, web sayfaları, yetki tanımları, senkron kodu, talep modülü, rapor modülü — **hiçbiri
değişmedi**.

---

## 3. DEĞİŞİKLİKLERİN DOĞRULANMASI (madde 2 listesi)

Her madde kod üzerinde tek tek arandı ve **bulundu**:

| Gereksinim | Durum | Kanıt |
|---|---|---|
| `StockBalanceWriter` ortak bakiye yazıcısı | ✅ VAR | `public static class StockBalanceWriter` |
| CAS + en fazla 3 tekrar / toplam 4 deneme | ✅ VAR | `MaxRetries = 3` |
| 10–40 ms bekleme | ✅ VAR | `_jitter.Next(10, 41)` |
| CAS yarışında tekrar | ✅ VAR | `UPDATE ... AND quantity=@expected` → 0 satır → `StockConcurrencyException` → tekrar |
| Yalnız gerçek `NextDocNo` yarışında tekrar | ✅ VAR | `IsDocumentNumberRace` — yalnız `ux_stock_documents_no` / `stock_documents.doc_no` |
| `NegativeStockException` / `ForbiddenException` / sistem hatası → tekrar YOK | ✅ VAR | `catch (Exception ex) when (ex is StockConcurrencyException \|\| IsDocumentNumberRace(ex))` — başka hiçbir tür yakalanmıyor; test `BASKA_Veritabani_Hatalari_YARIS_SAYILMAZ_Ve_Tekrar_EDILMEZ` kanıtlıyor |
| `RecomputeBalances` iyimser koruması | ✅ VAR | `LedgerSignature` (COUNT + MAX(created_at)), en fazla 2 yeniden hesaplama |
| `StockBusyException` → HTTP 409 | ✅ VAR | `Program.cs` global hata eşlemesi |
| Tüm bakiye yazma yolları ortak yazıcıda (OpeningStockService dâhil) | ✅ VAR | `stock_balances`'a yazan **yalnız 2 yer** kaldı: ortak yazıcı + `RecomputeBalances` (K-3 ile bilinçli istisna) |

---

## 4. TEST SONUÇLARI (bu rapor için yeniden koşuldu)

| Koşu | Sonuç |
|---|---|
| Tüm paket | ✅ **788 geçti / 0 başarısız / 14 atlandı** (802 toplam) |
| PostgreSQL eşzamanlılık (boş test veritabanında) | ✅ **3 geçti / 0 başarısız** |

Atlanan 14 test: PostgreSQL gerektiren testler (güvenlik kapısı onayı verilmediğinde otomatik atlanır).

**PostgreSQL testleri yalnız ayrı ve boş `depowise_test` veritabanında koşturuldu; canlıya bağlanılmadı.**

---

## 5. CANLI VERİ KONTROL SONUCU (Adım 3 — salt-okuma)

| Başlık | Sonuç |
|---|---|
| Kontrol sayısı | **17** |
| `stock_balances` ↔ `stock_movements` | ✅ **2 463 / 2 463 tutarlı** (0 fark) |
| Negatif bakiye | 66 malzeme — **tamamı `opening / yön=−1`** (ADR-086 negatif açılış). Bu malzemelerde tek bir `out`/`usage` hareketi yok → **oversell YOK** |
| Yarım / yetim / tutarsız kayıt | ✅ **0** (C1–C14 tamamı temiz) |
| Salt-okuma garantisi | `SHOW transaction_read_only = on`; kanıt amaçlı `UPDATE` PostgreSQL tarafından **`25006`** ile reddedildi; `ROLLBACK` |

**Sonuç: canlı veri sağlam; düzeltmenin onaracağı geriye dönük hasar yok.**

---

## 6. MIGRATION DURUMU

**MIGRATION YOK.**

- `src/DepoWise.Infrastructure/Database/Migrations/` klasöründe **hiçbir değişiklik yok** (git ile doğrulandı).
- Şema değişikliği yok, indeks eklenmedi, kolon eklenmedi, tip değiştirilmedi.
- **M-S1a (`company_id`) migration'ı bu deploy'a DÂHİL DEĞİLDİR** — ayrı adımdır ve ayrı onay ister.
- Deploy sırasında canlı veritabanında **hiçbir şema veya veri işlemi çalışmayacaktır**.

---

## 7. DEPLOY EDİLECEK SÜRÜM / COMMIT

| Bileşen | Değer |
|---|---|
| Commit | **`1bc371c`** (master, origin ile senkron) |
| Önceki yayın | `85b7504` — "Talep Operasyonları FAZ 2 YAYINLANDI … masaüstü 1.0.128" |
| API uygulaması | `depowise-erp` (`fly.toml`) |
| Web uygulaması | `depowise-web` (`fly.web.toml`) |
| Masaüstü sürümü | **1.0.129** (mevcut canlı: 1.0.128) |

**Masaüstü neden dâhil:** düzeltme `DepoWise.Infrastructure` içindedir; masaüstü de aynı stok motorunu
kullanır. Davranış SQLite'ta değişmez, ama kod tabanının üç bileşende aynı kalması için paket yenilenir.

---

## 8. DEPLOY SIRASINDA YAPILACAK İŞLEMLER

Onay verilirse, sırayla:

| # | İşlem | Komut / araç | Canlı veriye yazar mı |
|---|---|---|---|
| 1 | API yayını | `flyctl deploy -c fly.toml --now` | **Hayır** (yalnız uygulama imajı) |
| 2 | API sağlık kontrolü | `/health` + bir okuma ucu | Hayır |
| 3 | Web yayını | `flyctl deploy -c fly.web.toml --now` | Hayır |
| 4 | Web sağlık kontrolü | giriş sayfası + bir liste ekranı | Hayır |
| 5 | Masaüstü paketi | `dotnet publish -r win-x64 --self-contained -o artifacts/rc/desktop-1.0.129` → zip | Hayır |
| 6 | Sürüm yayını | `node scripts/publish_release.mjs <zip> 1.0.129 "<not>"` | Sunucudaki **sürüm kaydı** yazılır (iş verisi değil) |
| 7 | Belge güncellemesi | `DEVAM.md` / `YARIM_KALAN_ISLER.md` + commit + push | Hayır |

### ⚠️ Deploy sırasında canlı veriye yazma yapılacak mı?

**İş verisine (stok, malzeme, talep, bakım, firma) HİÇBİR yazma yapılmaz.**

Tek istisna, 6. adımdaki **sürüm kaydı**: masaüstü güncelleme sistemi için sunucuya "yeni sürüm yayınlandı"
kaydı eklenir. Bu, her yayında yapılan normal işlemdir ve **stok/iş verisine dokunmaz**. İstersen bu adımı
atlayıp yalnız API+web yayınlayabiliriz (o durumda masaüstü 1.0.128'de kalır).

Ayrıca: uygulama açılışında **migration çalıştırıcısı** çalışır; ancak yeni migration olmadığı için
**uygulanacak hiçbir migration yoktur** (sürüm 61'de kalır).

---

## 9. GERİ DÖNÜŞ (ROLLBACK) YÖNTEMİ

| Senaryo | Yöntem | Süre | Veri kaybı |
|---|---|---|---|
| API'de sorun | `flyctl releases -a depowise-erp` → `flyctl deploy -a depowise-erp --image <önceki imaj>` **veya** `85b7504` commit'inden yeniden deploy | Dakikalar | **Yok** |
| Web'de sorun | Aynı yöntem, `depowise-web` | Dakikalar | **Yok** |
| Masaüstünde sorun | Sunucudaki sürüm kaydını 1.0.128'e geri al; makineler eski sürümde kalır | Dakikalar | **Yok** |
| Veritabanı | **Geri alınacak bir şey yok** — şema ve veri değişmiyor | — | **Yok** |

**Geri dönüş neden risksiz:** Bu yayın yalnız uygulama kodu içerir. Şema değişmediği için eski sürüm yeni
veriyle, yeni sürüm eski veriyle sorunsuz çalışır (ileri/geri uyumlu).

---

## 10. DEPLOY SONRASI DOĞRULAMA ADIMLARI

| # | Kontrol | Beklenen |
|---|---|---|
| 1 | API `/health` | 200, `ok: true` |
| 2 | Web giriş + bir liste ekranı (Malzemeler) | Açılıyor, veri geliyor |
| 3 | Sunucu logunda `[stock-cas]` / `[stock-docno]` / `[500]` | Beklenmedik hata yok |
| 4 | Canlı stok tutarlılığı (**salt-okuma**, Adım 3'teki araçla) | 2 463/2 463 tutarlı — **değişmemiş** |
| 5 | Stok hareketi ve belge sayısı | Deploy öncesiyle **aynı** (667 hareket / 2 belge) — yayın veri üretmemeli |
| 6 | Masaüstü 1.0.129 açılışı + eşitleme | Sorunsuz |
| 7 | Bir örnek stok çıkışı (babanın normal kullanımı) | Eskisi gibi çalışıyor |

4. ve 5. maddeler yayının veriye dokunmadığının kanıtı olacaktır.

---

## 11. RİSK DEĞERLENDİRMESİ

| Risk | Seviye | Gerekçe |
|---|---|---|
| Veri kaybı | **Yok** | Şema/veri değişmiyor; migration yok |
| Davranış değişikliği (masaüstü) | **Çok düşük** | SQLite'ta CAS hiç düşmez; 788 test yeşil |
| Davranış değişikliği (sunucu/web) | **Düşük** | Yalnız eşzamanlı çakışmada davranış değişir: eskiden hata/oversell → şimdi tekrar veya temiz hata |
| Yeni hata mesajı (409) | **Düşük** | Yalnız 4 deneme de çakışırsa görünür; metin teknik değil |
| Geri dönüş zorluğu | **Yok** | Tek komutla eski imaja dönülür |

**Bilinen açık (bu deploy'un kapsamı dışı):** `material_request_items` ve `maintenance_materials`
tablolarında `company_id` yok → M-S1a ile ayrıca ele alınacak. Bugün tek gerçek firma kullandığı için
etkisi yok.

---

## 12. SONUÇ

**🟡 DEPLOY İÇİN HAZIR — ONAY BEKLİYOR**

Tüm ön kontroller geçti; eksik, risk veya belirsizlik bulunamadı. Production'a hiçbir değişiklik
uygulanmadı. Kullanıcı açıkça **"DEPLOY ET"** demeden yayın yapılmayacaktır.
