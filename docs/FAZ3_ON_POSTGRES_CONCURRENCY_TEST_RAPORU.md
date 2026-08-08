# FAZ 3-ÖN — POSTGRESQL EŞZAMANLILIK TEST RAPORU

> ⚠️ **BU RAPOR İKİ BÖLÜMDÜR.** §1–§9 **ilk koşuyu** (S1 düzeltmesinden ÖNCE) anlatır ve belge numarası
> yarışının nasıl bulunduğunu gösterir. **§10, S1 düzeltmesinden SONRAKİ sonuçtur — güncel durum odur:
> 3 PostgreSQL testinin de GEÇTİĞİ hâl.**

**Tarih:** 2026-08-08
**Ortam:** Ayrı ve **boş** PostgreSQL test veritabanı (`depowise_test`) — canlı veritabanına **hiç bağlanılmadı**.
**Çalıştırılan filtre:** `FullyQualifiedName~PostgresStockConcurrencyTests` (yalnız 3 test)
**Kapsam:** Faz 3-Ön'de yapılan stok bakiyesi eşzamanlılık (CAS + sınırlı tekrar) düzeltmesinin gerçek
PostgreSQL üzerinde doğrulanması.

> Bu raporda hiçbir bağlantı adresi, kullanıcı adı, parola veya API anahtarı yer almaz.

---

## 1. ÖZET

| | Sonuç |
|---|---|
| Toplam test | 3 |
| Geçti | **1** |
| Kaldı | **2** |
| Atlandı | 0 |
| **Oversell (fazla çıkış) oluştu mu?** | **HAYIR — hiçbir senaryoda** |
| **Bakiye ↔ `stock_movements` tutarlı mı?** | **EVET — tüm malzemelerde** |
| **Negatif bakiye oluştu mu?** | **HAYIR** |
| **Bakiye CAS düzeltmesi çalışıyor mu?** | **EVET — kanıtlandı (Test 3)** |
| **Yeni kusur bulundu mu?** | **EVET — belge numarası tahsisinde ikinci bir yarış** |

**Kısaca:** Faz 3-Ön'de düzelttiğimiz şey (stok bakiyesinin ezilmesi / oversell) **gerçekten düzelmiş**.
Ancak testler, **aynı sınıftan ikinci bir yarış durumunu** ortaya çıkardı: stok belgesinin **numarası**
(`doc_no`) da "oku → +1 → yaz" deseniyle üretiliyor ve eşzamanlı iki belge aynı numarayı alıyor.
Bu, veriyi bozmuyor (işlem tümüyle geri alınıyor) ama kullanıcıya anlamsız bir veritabanı hatası
gösteriyor ve başarılı olabilecek bir işlemi başarısız kılıyor.

---

## 2. GÜVENLİK KAPISI DOĞRULAMASI

Testler çalışmadan önce `PostgresTestGuard` kontrollerinin tamamı uygulandı ve geçti:

| Kontrol | Sonuç |
|---|---|
| K1 — `DEPOWISE_PG_TEST_CONFIRM` açık onayı | ✅ Geçti |
| K2 — veritabanı adında "test" (sunucudan `SELECT current_database()` ile doğrulandı) | ✅ Geçti (`depowise_test`) |
| K3 — public şema tamamen boş (ya da şemayı daha önce kapı sıfırlamış) | ✅ Geçti |
| K4 — veritabanı boyutu eşiği | ✅ Geçti |
| K5 — salt-okunur replika değil | ✅ Geçti |

- Canlı veritabanına **hiç bağlanılmadı**.
- Kapı hiçbir noktada devre dışı bırakılmadı/gevşetilmedi.
- Test veritabanı bu iş için **yeni ve boş** olarak oluşturuldu; canlı veritabanına dokunulmadı.

---

## 3. TEST BAZINDA SONUÇLAR

### Test 1 — `Eszamanli_Iki_Cikis_Oversell_Ve_Kayip_Dusum_Uretmez`

| Alan | İçerik |
|---|---|
| **Senaryo** | (a) Stok 10; **aynı anda** 6 ve 7 çıkış. (b) Stok 10; **aynı anda** 6 ve 3 çıkış. |
| **Beklenen** | (a) Yalnız biri başarılı, diğeri **iş kuralı hatasıyla** (yetersiz stok) reddedilir; bakiye 4 veya 3; defterle tutarlı. (b) İkisi de başarılı; bakiye 1. |
| **Gerçek sonuç** | (a) **Tam olarak 1 işlem başarılı oldu** (6 geçti), bakiye **4**, defter toplamı **4** → **tutarlı, oversell YOK**. Ancak başarısız olan işlem beklenen `NegativeStockException` yerine **`PostgresException 23505 — duplicate key value violates unique constraint "ux_stock_documents_no"`** hatası verdi. Test bu hata tipi beklentisinde kaldı. (b) kısmına **sıra gelmedi** (test (a)'da durdu). |
| **Geçti/Kaldı** | ❌ **KALDI** (hata tipi beklentisi) — ama **oversell oluşmadı** |
| **Retry davranışı** | Bakiye CAS tekrarına gerek kalmadı; kaybeden işlem daha **önceki** adımda (belge numarası) çakıştı. `23505` bilinçli olarak tekrarlanmıyor (tasarım: yalnız `StockConcurrencyException` tekrarlanır) → tekrar **yapılmadı**. |
| **Bakiye ↔ `stock_movements`** | ✅ **Tutarlı** (4 = 4) |
| **Hata nedeni** | Belge numarası (`doc_no`) tahsisindeki yarış — bkz. §5 |

### Test 2 — `Yuksek_Cekismede_Bakiye_Negatife_Dusmez_Ve_Defterle_Tutarli_Kalir`

| Alan | İçerik |
|---|---|
| **Senaryo** | Stok 10; **20 paralel** 1'er birim çıkış. |
| **Beklenen** | Bakiye asla negatife düşmez; başarılı çıkış sayısı 1–10 arası; bakiye = 10 − başarılı sayısı; defterle tutarlı. |
| **Gerçek sonuç** | Test, beklenmeyen hata tipi nedeniyle tamamlanamadı: birden çok iş parçacığı aynı anda belge oluşturduğu için **`23505 ux_stock_documents_no`** hataları oluştu (`AggregateException` içinde en az 6 adet). Testin `catch` blokları yalnız `NegativeStockException` ve `StockBusyException` yakaladığı için istisna dışarı çıktı. |
| **Geçti/Kaldı** | ❌ **KALDI** |
| **Retry davranışı** | Aynı sebep: çakışma bakiye katmanında değil, belge numarası katmanında oldu → tekrar mekanizması devreye **girmedi**. |
| **Bakiye ↔ `stock_movements`** | ✅ Koşu sonrası genel doğrulamada tutarsızlık **bulunmadı**; negatif bakiye **yok** |
| **Hata nedeni** | Aynı — §5 |

### Test 3 — `Eszamanli_Giris_Ve_Cikis_Birbirini_Ezmez` ✅

| Alan | İçerik |
|---|---|
| **Senaryo** | Stok 100; **aynı anda** 40 giriş + 25 çıkış (aynı malzeme). |
| **Beklenen** | Bakiye 115 (100 + 40 − 25); defter toplamı da 115. İki işlemden hiçbiri diğerinin değişikliğini ezmemeli. |
| **Gerçek sonuç** | Bakiye **115**, defter toplamı **115**. |
| **Geçti/Kaldı** | ✅ **GEÇTİ** |
| **Retry davranışı** | Kayıp güncelleme olmadı; CAS koruması görevini yaptı. (Giriş "GIR", çıkış "CIK" numara dizisi kullandığı için belge-numarası çakışması bu testte oluşmadı.) |
| **Bakiye ↔ `stock_movements`** | ✅ **Tutarlı** (115 = 115) |
| **Hata nedeni** | — |

> **Bu test, Faz 3-Ön düzeltmesinin ASIL KANITIDIR.** Düzeltmeden önce bu senaryoda iki işlem aynı
> bakiyeyi okuyup mutlak değer yazdığı için sonuç **140** veya **75** çıkardı (biri diğerini ezerdi).
> Şimdi **115** çıkıyor — yani hiçbir güncelleme kaybolmuyor.

---

## 4. "10 STOK → EŞZAMANLI 6 + 7 ÇIKIŞ" SENARYOSU — ADIM ADIM

**Kurulum:** `M-CC1` malzemesi, açılış stoğu **10**. İki iş parçacığı **aynı anda** çıkış deniyor: biri **6**, biri **7**.

**Gerçekte olan (test veritabanından okunan kesin veriler):**

| Adım | Olay |
|---|---|
| 1 | İki işlem aynı anda başladı; ikisi de kendi transaction'ını açtı. |
| 2 | İkisi de belge numarası hesapladı: `MAX(doc_no)+1` → **ikisi de aynı numarayı** buldu (`CIK-2026-0001`). |
| 3 | Biri belgesini yazdı ve devam etti; diğeri aynı numarayı yazmaya çalışınca **veritabanı reddetti** (`ux_stock_documents_no` benzersizlik indeksi) → o işlemin **tamamı geri alındı**. |
| 4 | Devam eden işlem (6 birimlik çıkış) stok kontrolünü geçti, bakiyeyi **CAS ile** 10 → **4** yaptı, hareketi deftere yazdı ve commit etti. |
| 5 | **Sonuç:** başarılı işlem sayısı **1**, başarısız **1**. |

**Doğrulanan nihai durum (salt-okuma):**

| Ölçüm | Değer |
|---|---|
| `stock_balances` bakiyesi | **4** |
| `stock_movements` defter toplamı (Σ yön × miktar) | **4** |
| Tutarlı mı | **EVET** |
| Toplam çıkış | **6** (13 **değil**) |
| Negatif bakiye | **Yok** |
| Oluşan belge sayısı | **1** (yarım/artık belge yok) |

### Oversell oluşmadığının kanıtı

Eğer oversell olsaydı: iki çıkış da yazılır, defter toplamı **10 − 13 = −3** olur, `stock_balances` ise
düzeltmeden önceki "son yazan kazanır" davranışıyla **3** veya **4** kalırdı — yani **defter ile bakiye
birbirini tutmazdı**. Ölçülen sonuç bunun tam tersi: **defter 4, bakiye 4, tek çıkış hareketi, tek belge.**

### CAS/retry mekanizması bu senaryoda ne yaptı?

Bu turda **bakiye CAS'i devreye girmedi** — çünkü kaybeden işlem, bakiyeye hiç ulaşamadan bir **önceki**
adımda (belge numarası) elendi. Yani iki farklı koruma katmanı var ve bu senaryoda **birincisi** iş gördü:

1. **Belge numarası benzersizlik indeksi** (veritabanı düzeyi) → çakışan ikinci belgeyi reddetti.
2. **Bakiye CAS'i** (Faz 3-Ön'de eklendi) → Test 3'te devreye girdi ve kayıp güncellemeyi önledi.

İkisi de sonuçta aynı şeyi garanti ediyor: **hiçbir koşulda fazla çıkış yazılamıyor.**

---

## 5. BULUNAN YENİ KUSUR — BELGE NUMARASI TAHSİSİNDE YARIŞ

**Nerede:** `StockService.NextDocNo` (`src/DepoWise.Infrastructure/Materials/StockService.cs:555-571`)

```sql
SELECT COALESCE(MAX(CAST(substr(doc_no, length(@p)+1) AS INTEGER)),0)
FROM stock_documents WHERE company_id=@c AND doc_type=@t AND doc_no LIKE @like;
-- ardından: next = MAX + 1
```

Bu, düzelttiğimiz bakiye deseniyle **birebir aynı** "oku → hesapla → yaz" problemi. Tek koruma,
`Migration006_StockDocuments.cs:37`'deki benzersizlik indeksi:
`CREATE UNIQUE INDEX ux_stock_documents_no ON stock_documents(company_id, doc_type, doc_no);`

**Sonuçları:**

| Etki | Değerlendirme |
|---|---|
| Veri bozulması / oversell | **YOK** — çakışan işlemin tamamı geri alınır (tek transaction) |
| Kullanıcı deneyimi | **Kötü** — anlamsız bir veritabanı hatası görür ("duplicate key ... ux_stock_documents_no") |
| İşlem kaybı | **Var** — aslında başarılı olabilecek bir çıkış/giriş boşuna reddedilir |
| Tekrar (retry) | **Yapılmıyor** — `23505` tasarım gereği "sistem hatası" sayılıp tekrarlanmıyor (K-5 kuralı) |
| Nerede görülür | Aynı firmada **aynı tipte** (çıkış-çıkış, giriş-giriş…) iki belge **aynı anda** oluşturulursa. Farklı tip (giriş+çıkış) çakışmaz — Test 3 bu yüzden geçti. |
| Hangi ortam | **Yalnız PostgreSQL** (sunucu/web). SQLite'ta `BeginImmediate` tek yazara izin verdiği için oluşmaz. |
| Faz 3'e etkisi | **Doğrudan** — talep karşılamada her karşılama bir stok belgesi üretecek; iki depo görevlisi aynı anda karşılama yaparsa bu hatayı alır. |

**Neden Faz 3-Ön'de yakalanmadı:** Faz 3-Ön'ün kapsamı `stock_balances` idi. Belge numarası ayrı bir
kaynak ve envanterimde stok bakiyesi yazan yollar arasında yer almıyordu. **Bu testler olmasaydı bu kusur
canlıda ortaya çıkacaktı.**

---

## 6. TEST SONRASI VERİTABANI DURUMU (salt-okuma)

Koşudan sonra `depowise_test` üzerinde **yalnız okuma** sorguları çalıştırıldı:

| Ölçüm | Değer | Değerlendirme |
|---|---|---|
| Veritabanı | `depowise_test` | Doğru hedef |
| public şemadaki tablo sayısı | 65 | Migration'ların kurduğu normal şema |
| `stock_documents` | 1 | Son testin bıraktığı kayıt |
| `stock_movements` | 2 | Açılış + 1 çıkış |
| Negatif bakiyeli malzeme | **0** | ✅ |
| Bakiye ↔ defter tutarlılığı | **Tüm malzemelerde EVET** (`M-CC1`: defter 4 = bakiye 4) | ✅ |

**Beklenmeyen veya bozuk kalıcı veri: YOK.** Kalan kayıtlar son testin normal çıktısıdır; her test
başlangıcında şema zaten sıfırlanıyor. Yarım kalmış belge, sahipsiz hareket veya tutarsız bakiye
bulunmadı.

---

## 7. DEĞERLENDİRME

**Faz 3-Ön düzeltmesi çalışıyor:** Test 3, düzeltmeden önce kesinlikle bozulan bir senaryoyu (aynı
malzemede eşzamanlı giriş+çıkış) doğru sonuçla geçti. Hiçbir senaryoda oversell, kayıp düşüm, negatif
bakiye veya defter-bakiye tutarsızlığı oluşmadı.

**Ama Faz 3-Ön eksik:** Aynı hata deseni belge numarası tahsisinde de var ve düzeltilmedi. Bu, veriyi
bozmuyor ama Faz 3'te (talep karşılama) kullanıcıların sık karşılaşacağı bir hata üretecek.

**İki test "kaldı" ama bu bir gerileme değil:** Testler tam olarak yapmaları gereken şeyi yaptı —
**bilinmeyen bir kusuru ortaya çıkardılar.**

---

## 8. SIRADAKİ ADIM İÇİN SEÇENEKLER (uygulanmadı — onay bekliyor)

| Seçenek | İçerik | Değerlendirme |
|---|---|---|
| **S1** | `NextDocNo`'yu da aynı desende güvenli hale getir: belge numarası çakışmasını (`23505`, yalnız `ux_stock_documents_no` kısıtı) **yarış** olarak tanı ve mevcut tekrar mekanizmasıyla (en fazla 3 tekrar) yeniden dene | **Önerim.** Mevcut mimariye uyar, migration gerektirmez, SQLite davranışını değiştirmez |
| **S2** | Belge numarasını veritabanı dizisiyle (sequence) üret | PG'ye özel; SQLite'ta karşılığı yok → iki veritabanı farklı davranır (senin istemediğin durum) |
| **S3** | Şimdilik dokunma, Faz 3'e geç | **Önermiyorum** — Faz 3'te karşılama işlemleri bu hatayı üretecek |

Ayrıca testlerin beklentileri de S1'e göre güncellenmeli (belge numarası çakışması artık tekrar edilip
başarıyla sonuçlanacağı için).

---

## 9. İLK KOŞUDA YAPILMAYANLAR

- Kod değiştirilmedi, yeni test yazılmadı, HTTP 409 testi eklenmedi.
- Migration çalıştırılmadı, M-S1a'ya başlanmadı.
- Deploy yapılmadı.
- Canlı veritabanına bağlanılmadı, canlı salt-okuma kontrolü başlatılmadı.
- Başka hiçbir PostgreSQL testi çalıştırılmadı (yalnız adı geçen 3 test).

---
---

# 10. S1 DÜZELTMESİ SONRASI — GÜNCEL DURUM ✅

**Kullanıcı kararı S1 uygulandı:** belge numarası (`doc_no`) çakışması artık **yarış** olarak tanınıyor ve
mevcut tekrar mekanizmasıyla yeniden deneniyor. Migration yok, şema değişikliği yok, sequence yok.

## 10.1 Kod değişikliği (tek dosya)

`src/DepoWise.Infrastructure/Materials/StockBalanceWriter.cs`

1. **`IsDocumentNumberRace(Exception)`** eklendi. Lehçeye özel tip kullanmaz (Infrastructure Npgsql'e
   bağımlı değildir); bir `DbException`'ın (ve iç istisnalarının) metninde **yalnız** şu iki ayırt edici
   ifadeden biri aranır:
   - `ux_stock_documents_no` → PostgreSQL'in ürettiği metin
   - `stock_documents.doc_no` → SQLite'ın ürettiği metin
2. **`Run<T>` tekrar sarmalayıcısı** artık iki yarış türünü de yakalıyor:
   `catch (Exception ex) when (ex is StockConcurrencyException || IsDocumentNumberRace(ex))`
   Logda **ayrı etiketlenir**: `[stock-cas]` (bakiye) ve `[stock-docno]` (belge numarası).

**Kapsam bilerek dardır.** Genel `23505`, başka benzersizlik kısıtları, yabancı anahtar hataları,
doğrulama hataları ve gerçek sistem arızaları **tekrarlanmaz** (test edildi — §10.3).

## 10.2 Tekrar sınırları — DEĞİŞMEDİ

| Kural | Değer |
|---|---|
| En fazla tekrar | **3** (toplam en fazla 4 deneme) |
| Bekleme | **10–40 ms** rastgele, döngüsüz |
| Tükenince | `StockBusyException` → kullanıcıya teknik olmayan mesaj → HTTP **409** |
| `NegativeStockException` | Tekrar **YOK** |
| `ForbiddenException` | Tekrar **YOK** |
| Gerçek sistem/veritabanı hatası | Tekrar **YOK** |
| Bakiye CAS yarışı | Tekrar **VAR** |
| Belge numarası yarışı | Tekrar **VAR** (yeni) |

## 10.3 Eklenen testler

**SQLite (deterministik, her zaman koşar)** — `tests/DepoWise.Tests/StockConcurrencyTests.cs`:

| Test | Kanıtladığı |
|---|---|
| `DocNo_Cakismasi_YARIS_Sayilir_Ve_Tekrar_Edilir` | Hem SQLite hem PostgreSQL metni yarış sayılıyor; 2 çakışmadan sonra 3. denemede başarı (**tekrar gerçekten yapıldı**) |
| `DocNo_Cakismasi_Tekrar_Hakki_Bitince_Kullanici_Mesajina_Doner` | Tam 4 deneme, sonra kullanıcı mesajı — **sınır değişmedi** |
| `BASKA_Veritabani_Hatalari_YARIS_SAYILMAZ_Ve_Tekrar_EDILMEZ` | `ux_stock_movements_operation`, başka bir `ux_..._no`, FK ihlali, "database is locked", `InvalidOperationException` → hepsi **tek deneme** |

**PostgreSQL** — `tests/DepoWise.Tests/PostgresStockConcurrencyTests.cs` (Test 1 güçlendirildi):
tekrarın gerçekten olduğu **log üzerinden** doğrulanıyor; yarım belge/artık kayıt sayımı eklendi;
"tekrar hakkı tükenmedi" kontrolü eklendi.

## 10.4 Test sonuçları (S1 sonrası)

| Koşu | Sonuç |
|---|---|
| PostgreSQL eşzamanlılık (3 test) | ✅ **3 geçti / 0 kaldı** |
| Tüm paket | ✅ **788 geçti / 0 kaldı / 14 atlandı** (802 toplam) |

### Tekrarın gerçekleştiğinin kanıtı (koşu logundan)

```
[stock-docno] conflict document:out op=pg-cc-d  attempt=1/4  23505: duplicate key value violates ...
[stock-docno] conflict document:out op=pg-cc-x0 attempt=1/4 ... attempt=2/4 ... attempt=3/4
[stock-cas]   conflict document:in  op=pg-cc-in attempt=1/4  stock_balances yarışı: ...
```

Yani **iki koruma da gerçek yarışta tetiklendi**: belge numarası çakışması yeniden denendi, bakiye CAS'i
de eşzamanlı giriş/çıkışta devreye girdi. "Sonuç doğru çıktı" değil, **mekanizmanın çalıştığı** kanıtlandı.

## 10.5 Test bazında sonuçlar (S1 sonrası)

| Test | Senaryo | Beklenen | Gerçek | Sonuç |
|---|---|---|---|---|
| `Eszamanli_Iki_Cikis_Oversell_Ve_Kayip_Dusum_Uretmez` | 10 → eşzamanlı 6+7; ardından 10 → eşzamanlı 6+3 | Tam 1 başarılı; kaybeden **iş kuralıyla** reddedilir; toplam 13 çıkış asla olmaz. İkinci kısımda ikisi de başarılı, bakiye 1 | Tam 1 başarılı; kaybeden `NegativeStockException`; bakiye **4**, defter **4**. İkinci kısım: ikisi de başarılı, bakiye **1**, defter **1** | ✅ **GEÇTİ** |
| `Yuksek_Cekismede_Bakiye_Negatife_Dusmez_Ve_Defterle_Tutarli_Kalir` | 10 stok, 20 paralel 1'er birim çıkış | Negatife düşmez; bakiye = 10 − başarılı; defterle tutarlı | Sağlandı; birkaç işlem 4 denemede de çakışıp kullanıcı mesajıyla reddedildi (beklenen davranış) | ✅ **GEÇTİ** |
| `Eszamanli_Giris_Ve_Cikis_Birbirini_Ezmez` | 100 stok; eşzamanlı 40 giriş + 25 çıkış | Bakiye 115, defter 115 | Bakiye **115**, defter **115**; bakiye CAS'i tetiklendi | ✅ **GEÇTİ** (önceden de geçiyordu — korundu) |

### 10 → 6 + 7 senaryosunun S1 sonrası davranışı

1. İki işlem aynı anda başlar, ikisi de aynı belge numarasını hesaplar.
2. Biri belgesini yazar; diğerinin INSERT'ü `23505` ile reddedilir → **artık yarış olarak tanınır**.
3. Kaybeden işlemin transaction'ı geri alınır, **10–40 ms** beklenir ve **tamamı baştan** çalışır.
4. Yeniden denemede belge numarası bir sonrakini alır **ve stok kontrolü baştan yapılır**.
5. Kalan stok yetersiz olduğu için `NegativeStockException` → temiz, anlaşılır iş kuralı hatası.
6. **Sonuç:** tam 1 başarılı çıkış, bakiye **4**, defter **4**, tek çıkış belgesi, **oversell yok**.

> **Not (dürüst tespit):** Hangi işlemin kazandığı gerçek bir yarıştır. 6 kazanırsa bakiye 4, 7 kazanırsa 3
> olur; ikisi de doğrudur. Testin ilk sürümü "her zaman 6 kazanır" varsaydığı için bir koşuda kaldı;
> bu bir ürün hatası değil, **test beklentisinin fazla katı olmasıydı** ve düzeltildi. Değişmez kural
> korunuyor: **toplam çıkış asla 13 olamaz** ve defter ile bakiye her zaman eşittir.

## 10.6 Test veritabanı son durumu (salt-okuma)

| Ölçüm | Değer |
|---|---|
| Veritabanı | `depowise_test` |
| public tablo sayısı | 65 |
| `stock_documents` / `stock_movements` | 3 / 5 (son testin normal çıktısı) |
| Negatif bakiyeli malzeme | **0** |
| `M-CC1` defter ↔ bakiye | 4 ↔ 4 ✅ |
| `M-CC2` defter ↔ bakiye | 1 ↔ 1 ✅ |
| Yarım belge / artık kayıt | **Yok** |

## 10.7 SQLite ↔ PostgreSQL davranışı

- **SQLite:** `BeginImmediate` tek yazara izin verdiği için ne bakiye CAS'i ne doc_no çakışması oluşur →
  **davranış hiç değişmedi** (tüm mevcut testler aynen geçiyor).
- **PostgreSQL:** iki yarış da oluşabiliyor, ikisi de **aynı** politikayla yeniden deneniyor.
- Tek kod yolu, tek politika; PostgreSQL'e özel sequence veya SQL kullanılmadı.

## 10.8 Kapsam dışında bırakılanlar (bu adımda yapılmadı)

Migration yok · şema değişikliği yok · sequence yok · yetki/UI/ekran değişikliği yok ·
Transfer / ReverseDocument / MaintenanceService / OpeningStockService iş kuralları değişmedi ·
canlı veritabanına bağlanılmadı · deploy yok · M-S1a yok · Adım 3 yok · Faz 3 yok.

