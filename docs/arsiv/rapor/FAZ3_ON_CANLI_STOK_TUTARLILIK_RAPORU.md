# FAZ 3-ÖN · ADIM 3 — CANLI STOK TUTARLILIK RAPORU (SALT-OKUMA)

**Tarih:** 2026-08-08
**Hedef:** Canlı üretim veritabanı (`depowise_prod`)
**Tür:** **YALNIZ OKUMA** — hiçbir veri değiştirilmedi
**Kapsam:** `docs/FAZ3_ON_UYGULAMA_PLANI.md` §N.5 (Adım 3)

> Bu raporda hiçbir bağlantı adresi, kullanıcı adı, parola veya API anahtarı yer almaz.

---

## 1. SONUÇ ÖZETİ

| Başlık | Sonuç |
|---|---|
| **A) `stock_balances` ↔ `stock_movements` tutarlılığı** | ✅ **TAM TUTARLI — 2463 malzemenin tamamında fark YOK** |
| **B) Negatif bakiye / oversell** | ⚠️ 66 malzemede negatif bakiye var — ancak **tamamı bilinçli "negatif açılış stoğu"**; **oversell YOK** |
| **C) Yarım / yetim / tutarsız belge-hareket** | ✅ **Hiçbiri bulunamadı (17 kontrolün tamamı 0)** |
| **Genel değerlendirme** | ✅ **Canlı stok verisi sağlam. Onarım gerektiren hiçbir bulgu yok.** |

**Faz 3-Ön açısından anlamı:** Eşzamanlılık düzeltmesinin geriye dönük **onarması gereken hiçbir bozukluk
yok**. Defter ile bakiye birebir tutuyor; yani bugüne kadar kayıp düşüm / oversell **yaşanmamış**.

---

## 2. VERİTABANI GENEL DURUMU

| Ölçüm | Değer |
|---|---|
| Veritabanı | `depowise_prod` |
| Boyut | 14 MB |
| Firma sayısı | 3 |
| Malzeme sayısı | 2 463 |
| Stok hareketi | 667 |
| Stok belgesi | 2 |
| Bakiye satırı | 664 |

---

## 3. SALT-OKUMA GARANTİSİNİN YÖNTEMİ

Güvence **uygulama vaadine değil, veritabanı seviyesine** dayandırıldı:

| Adım | Uygulanan | Sonuç |
|---|---|---|
| 1 | Oturum salt-okunur yapıldı: `SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;` | — |
| 2 | Her transaction ayrıca `SET TRANSACTION READ ONLY;` | — |
| 3 | **Doğrulama:** `SHOW transaction_read_only` | **`on`** |
| 4 | **Kanıt denemesi:** bilerek bir yazma çalıştırıldı (`UPDATE stock_balances SET updated_at = updated_at WHERE 1=0`) — hiçbir satırı etkilemeyecek olsa bile | **PostgreSQL REDDETTİ — `SqlState 25006` (read-only transaction)** |
| 5 | Denetim transaction'ı `ROLLBACK` ile kapatıldı | — |

**Yani veritabanı, bu oturumda yazma yapılmasına izin vermedi.** Kanıt denemesi bunu somut olarak gösterdi:
`25006` hatası, "salt-okunur transaction içinde yazma yapılamaz" demektir.

Ek olarak:
- Çalıştırılan **tüm** sorgular `SELECT` / `SHOW`'dur (aşağıda listelenmiştir).
- `RecomputeBalances` **çalıştırılmadı**.
- Migration, şema değişikliği, deploy **yapılmadı**.
- Denetim aracı proje deposunun **dışında** (geçici çalışma klasöründe) tutuldu; uygulama kodu değişmedi.

---

## 4. KONTROL SONUÇLARI

Aşağıdaki tabloda "tutarsız kayıt sayısı" **0 olmalıdır**; 0 dışındaki her değer bulgu demektir.

| Kod | Ne kontrol edildi | Beklenen | Gerçek | Sonuç | Risk |
|---|---|---|---|---|---|
| **A** | Her malzeme için defter toplamı `Σ(yön × miktar)` ile kayıtlı bakiyenin eşitliği | 0 fark | **0** | ✅ GEÇTİ | — |
| **B** | Negatif bakiyeli malzeme | 0 *(beklenti)* | **66** | ⚠️ BULGU (bkz. §5) | **Düşük** |
| C0 | Sayısal olmayan hareket miktarı metni | 0 | **0** | ✅ GEÇTİ | — |
| C0b | Sayısal olmayan bakiye metni | 0 | **0** | ✅ GEÇTİ | — |
| C1 | Yetim hareket (malzemesi silinmiş/yok) | 0 | **0** | ✅ GEÇTİ | — |
| C2 | Belgesi bulunmayan hareket (`document_id` dolu ama belge yok) | 0 | **0** | ✅ GEÇTİ | — |
| C3 | Yetim bakiye satırı (malzemesi yok) | 0 | **0** | ✅ GEÇTİ | — |
| C4 | Hareketi olmayan belge (**yarım belge**) | 0 | **0** | ✅ GEÇTİ | — |
| C5 | Transfer belgesinde hatalı çift (çıkış/giriş eşleşmiyor ya da net ≠ 0) | 0 | **0** | ✅ GEÇTİ | — |
| C6 | Ters kaydın hedef hareketi yok | 0 | **0** | ✅ GEÇTİ | — |
| C7 | İptal edilmiş belgede geri alınmamış hareket | 0 | **0** | ✅ GEÇTİ | — |
| C8 | `is_reversed=1` işaretli ama ters kaydı olmayan hareket | 0 | **0** | ✅ GEÇTİ | — |
| C9 | Hareket miktarı ≤ 0 (defter sözleşmesi: miktar daima pozitif) | 0 | **0** | ✅ GEÇTİ | — |
| C10 | `direction` (−1, +1) dışında | 0 | **0** | ✅ GEÇTİ | — |
| C11 | Tekrarlı `operation_id` (idempotency ihlali) | 0 | **0** | ✅ GEÇTİ | — |
| C12 | Çapraz firma: hareketin firması ≠ malzemenin firması | 0 | **0** | ✅ GEÇTİ | — |
| C13 | Çapraz firma: bakiyenin firması ≠ malzemenin firması | 0 | **0** | ✅ GEÇTİ | — |
| C14 | Hareketi olup bakiye satırı olmayan malzeme | 0 | **0** | ✅ GEÇTİ | — |

**17 kontrolün 16'sı temiz; tek bulgu B maddesidir ve aşağıda açıklandığı üzere bir hata değildir.**

---

## 5. A / B / C SONUÇLARININ AYRINTISI

### A) `stock_balances` ↔ `stock_movements` tutarlılığı ✅

**2 463 malzemenin tamamında** defter toplamı ile kayıtlı bakiye **birebir eşit**. Fark bulunan malzeme
sayısı: **0**. Rapor çıktısında "fark bulunan malzemeler" listesi **boş** döndü.

Bu, Faz 3-Ön açısından en önemli sonuçtur: eşzamanlılık hatasının canlıda **geriye dönük bir hasar
bırakmadığını** gösterir. (Beklenen de buydu: canlı stok işlemleri ağırlıklı olarak masaüstünden —
SQLite üzerinden — yapılıyor ve orada `BeginImmediate` zaten tek yazara izin veriyor.)

### B) Negatif bakiye / oversell ⚠️ (hata değil)

**66 malzemede negatif bakiye var; toplam −563,82 birim.** Ancak bu **oversell değildir.**

**Kanıt — negatif bakiyeli malzemelerin hareket tipleri:**

```
opening / yön=-1 : 66      ← TAMAMI negatif AÇILIŞ hareketi
```

Yani bu 66 malzemenin negatif bakiyesi **yalnızca ve yalnızca negatif açılış stoğundan** geliyor. Bu 66
malzemede tek bir `out` (çıkış) veya `usage` (bakım tüketimi) hareketi bile yok.

**Tüm veritabanındaki hareket dağılımı:**

| Hareket tipi | Yön | Adet |
|---|---|---|
| `opening` (açılış) | +1 | 598 |
| `opening` (açılış) | **−1** | **66** ← negatif açılış |
| `out` (çıkış) | −1 | 1 |
| `usage` (bakım tüketimi) | −1 | 1 |
| `adjustment` (sayım düzeltme) | +1 | 1 |

Negatif açılış **bilinçli bir üründür** (ADR-086): firma sistemi devralırken eldeki/eksik stoğunu olduğu
gibi girebilsin diye açılış miktarının negatif olmasına izin verilir. Operasyonel **çıkışın** negatif
bakiye kalkanı ise aynen yürürlüktedir.

**Örnek kayıtlar (en büyük 10 negatif):**

| Kod | Ad | Bakiye |
|---|---|---|
| 50 | HD46 HİDROLİK YAĞ | −199 |
| 2457 | 1" R9 DÜZ FLANŞLI HİDROLİK HORTUM | −78 |
| 25 | SARI NİTRİL ELDİVEN | −59 |
| 1456 | M12 x 30mm CİVATA | −20 |
| 1909 | M16 FİBERLİ SOMUN | −19,1 |
| 1454 | M12 x 50mm CİVATA | −16 |
| 49 | ANTİFİRİZ | −15 |
| 1747 | EP3 GRES YAĞI | −15 |
| 95 | KUTUP BAŞI (+/−) | −13 |
| 1645 | 17 x 2100 V KAYIŞ | −10 |

**Bakiye dağılımı:** pozitif 598 · negatif 66 · sıfır 0 (toplam 664 bakiye satırı).

**Değerlendirme:** Bu bir veri bozukluğu değil, **veri girişi durumudur** — bu 66 malzemenin gerçek giriş
kaydı henüz yapılmamış olabilir. Karar senindir; teknik bir onarım gerektirmez. **Risk: düşük.**

### C) Yarım / yetim / tutarsız belge ve hareket ✅

C1–C14 kontrollerinin **tamamı 0** döndü. Özellikle:

- **Yarım belge yok** (C4): hareketi olmayan hiçbir stok belgesi yok.
- **Yetim kayıt yok** (C1, C2, C3): her hareketin malzemesi ve (varsa) belgesi mevcut; her bakiyenin
  malzemesi mevcut.
- **Transfer tutarlılığı** (C5): transfer belgelerinde çıkış/giriş çifti kuralı ihlal edilmemiş.
  *(Not: canlıda henüz transfer belgesi bulunmuyor — belge dağılımı: 1 sayım + 1 çıkış.)*
- **Ters kayıt / iptal tutarlılığı** (C6, C7, C8): iptal edilmiş belgede geri alınmamış hareket yok;
  ters kaydın hedefi olmayan hareket yok.
- **İdempotency** (C11): tekrarlı `operation_id` yok — çift yazma yaşanmamış.
- **Çok kiracılık** (C12, C13): hareket/bakiye ile malzemenin firması her kayıtta aynı — **sızıntı yok**.

---

## 6. CANLI VERİDE DEĞİŞİKLİK YAPILMADIĞININ DOĞRULANMASI

| Yöntem | Kanıt |
|---|---|
| Oturum + transaction düzeyinde salt-okuma | `SHOW transaction_read_only` → **`on`** |
| Yazma denemesi bilerek yapıldı | PostgreSQL **`25006`** ile reddetti — veritabanı yazmaya izin vermedi |
| Sorgu türleri | Yalnız `SELECT` / `SHOW` (tam liste §7) |
| Transaction kapanışı | `ROLLBACK` |
| `RecomputeBalances` | **Çalıştırılmadı** |
| Migration / şema | **Yok** |
| Uygulama kodu | **Değişmedi** |
| Denetim aracı | Proje deposunun dışında (geçici klasör); git'e girmedi |

> Kritik nokta: yazma yasağı benim tercihime değil, **PostgreSQL'in kendi kısıtına** dayanıyor. Salt-okunur
> transaction içinde bir `UPDATE` denemesi bile veritabanı tarafından reddedildi.

---

## 7. ÇALIŞTIRILAN SORGULARIN TÜRÜ

Tümü salt-okumadır; hiçbiri veri değiştirmez:

- `SHOW transaction_read_only`
- `SELECT current_database()`, `SELECT pg_database_size(current_database())`
- `SELECT COUNT(*) ...` (companies, materials, stock_movements, stock_documents, stock_balances)
- `SELECT ... FROM materials / stock_movements / stock_balances / stock_documents` (karşılaştırma ve
  gruplama sorguları)
- Tek `UPDATE` **kanıt amaçlı** çalıştırıldı ve **reddedildi** (hiçbir satırı hedeflemiyordu: `WHERE 1=0`)

---

## 8. SONUÇ VE ÖNERİ

- **Canlı stok verisi tutarlıdır**; Faz 3-Ön düzeltmesinin onaracağı bir hasar yoktur.
- **Oversell izi yoktur**; negatif bakiyelerin tamamı bilinçli negatif açılış kayıtlarından gelir.
- Yapısal bütünlük (yetim/yarım/ters kayıt/idempotency/çok kiracılık) **tamamen temizdir**.
- Devam için teknik bir engel görünmüyor.

**Karar senin:** 66 malzemedeki negatif açılış bakiyesi iş açısından beklenen bir durum mu, yoksa eksik
giriş mi? Teknik onarım gerektirmiyor; istersen listeyi ayrıca çıkarabilirim.

---

## 9. BU ADIMDA YAPILMAYANLAR

Adım 4'e (yayın) geçilmedi · M-S1a migration'ına başlanmadı · başka migration yapılmadı · deploy
yapılmadı · kod değiştirilmedi · başka test çalıştırılmadı · canlı veriye **hiçbir yazma** yapılmadı.
