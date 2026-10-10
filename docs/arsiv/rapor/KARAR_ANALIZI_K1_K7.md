# KARAR ANALİZİ — K1…K7 (kod üzerinden doğrulanmış)

**Tarih:** 2026-08-09 · **Durum:** YALNIZ ANALİZ — kod yazılmadı, migration yok, deploy yok, canlı veriye dokunulmadı.
**Önceki rapor:** [YARIM_ISLER_VE_EKRAN_STANDARDIZASYONU_ANALIZI.md](YARIM_ISLER_VE_EKRAN_STANDARDIZASYONU_ANALIZI.md)

> Teknik terimler ilk geçtiği yerde parantezle açıklanmıştır.

---

## 1. ÖNCELİK SIRASI — YENİDEN DOĞRULAMA (bir değişiklik öneriyorum)

Kod incelemesinde **sıralamayı iyileştiren bir bulgu** çıktı:

| Ekran | Arka planda "düzenleme" var mı? | Sonuç |
|---|---|---|
| **Personel** | ✅ `Create` + `Update` (sürüm kontrollü) | Çift tık penceresi **saf ekran işi** |
| **Talepler** | ✅ `Update` + `GetForEdit` + `Approve/Reject/Cancel` | Çift tık penceresi **saf ekran işi** |
| Günlük Faaliyet · Yakıt · Bakım | ❌ Yok | Önce arka plan yazılmalı |

→ Yani **ortak düzenleme penceresini (tekrar kullanılabilir bileşen) önce Personel ve Talepler üzerinde
kurmak** en mantıklısı: arka plan hazır, risk sıfıra yakın, sonra aynı pencere zor ekranlarda yeniden
kullanılır. Önceki sıralamada bu iş 9. sıradaydı (P2); **4. sıraya alınmasını öneriyorum.**

Diğer sıralar doğrulandı; değişiklik gerekmedi.

---

## 2. K1 — YAKIT KAYDI DÜZELTME

### 2.1 Sistem bugün nasıl çalışıyor (kod kanıtı)

| Konu | Bulgu |
|---|---|
| Tablolar | `fuel_depot_entries` (alım) ve `fuel_distributions` (araca dağıtım) |
| **`is_deleted` kolonu** | ✅ **İKİSİNDE DE VAR** (`Migration009`) — kullanılmıyor ama şemada mevcut |
| `version` kolonu | ✅ İkisinde de var |
| Depo bakiyesi | `SELECT SUM(...) WHERE company_id=@c AND **is_deleted=0**` |
| Raporlar | `is_deleted` filtresi ReportService'te 23 yerde kullanılıyor |
| Denetim (audit) | ✅ Yakıtta yazılıyor (stok servisiyle aynı düzeyde) |
| Dağıtımın yan etkisi | **Araç sayacını ilerletiyor** (`UPDATE vehicles SET current_meter`) + `vehicle_meter_logs`'a kayıt |
| Senkronizasyon | İki tablo da push listesinde → iptal diğer makinelere yayılır |

### 2.2 Somut örnek: "100 litre yerine 120 litre girildi"

| Seçenek | Ne olur | Depo bakiyesi | Rapor | Sayaç | Geçmiş izi | Migration |
|---|---|---|---|---|---|---|
| **(a) İptal + yeni kayıt** | Yanlış kayıt `is_deleted=1` olur, doğru 100 L yeni kayıt girilir | ✅ **Kendiliğinden düzelir** (sorgu zaten filtreli) | ✅ Düzelir | ⚠️ İlerlemiş sayaç **geride kalmaz** (aşağıda) | ✅ Yanlış kayıt görülebilir | ❌ **Gerekmez** |
| (b) Gerçek düzenleme (üstüne yaz) | 120 → 100 olarak değiştirilir | ✅ Düzelir | ✅ Düzelir | ⚠️ Aynı sorun | ⚠️ Eski değer yalnız audit'te | ❌ Gerekmez |
| (c) Fiziksel silme | Kayıt tamamen yok olur | ✅ Düzelir | ✅ Düzelir | ⚠️ Aynı sorun | ❌ **İz kalmaz** | ❌ Gerekmez |

**Sayaç konusu (üç seçenekte de aynı):** Yakıt dağıtımı araç sayacını ileri taşıyor. Proje kuralı gereği
**sayaç geriye gidemez** (yanlış bakım/uyarı hesabı doğurur). Bu yüzden iptal, sayacı geri almamalı;
sayaç düzeltmesi ayrı ve bilinçli bir işlem olarak kalmalı. Bunu kullanıcıya iptal ekranında açıkça
yazmalıyız.

### 2.3 Önerim: **(a) İptal + yeni kayıt**

**Neden:**
1. **Projenin kendi deseniyle aynı.** Stok ve bakım kayıtlarında zaten "iptal = iz bırakır" kuralı var
   (CLAUDE.md §4: operasyonel kayıt fiziksel silinmez). Yakıt bugün bu kuralın dışında kalmış tek yer.
2. **Migration gerekmiyor** — `is_deleted` zaten var, bakiye ve raporlar zaten filtreliyor. Yani iş,
   sanılandan **çok daha küçük**.
3. **Denetlenebilir.** "Kim, ne zaman, neyi iptal etti" görülebilir; babanın işinde fatura eşleştirmesi
   için önemli.
4. (b) düzenleme, geçmişi belirsizleştirir: rapor dün 120 L derken bugün 100 L der ve **fark nereden geldi
   belli olmaz**. (c) silme ise izi tamamen yok eder.

**Gereken yetki:** Stoktaki `btn-reverse` (ters kayıt) mantığıyla aynı — yeni yetki icadı yok.

---

## 3. K2 — GÜNLÜK FAALİYET SİLME

### 3.1 Bugün ne oluyor (kod kanıtı)

`DailyActivityService.Delete` **yalnız** şunu yapıyor:

```sql
UPDATE daily_activities SET is_deleted=1 WHERE id=@id ...
```

Faaliyet tipleri ve stok etkisi:

| Faaliyet tipi | Stok etkisi | Nasıl |
|---|---|---|
| **Bakım** | ✅ **Var** | Ortak `MaintenanceService` → bakım kaydı + stok düşümü |
| **İlave (yağ/filtre/tamir)** | ✅ **Var** | Aynı ortak servis |
| **Hareket / sevkiyat** | ❌ Yok | `stockProcessed: false` |

### 3.2 Somut örnek: "10 adet malzeme kullanıldı → faaliyet silindi"

| Ne | Durum |
|---|---|
| Faaliyet kaydı | ✅ Listeden kalkar (`is_deleted=1`) |
| **Bakım kaydı** | ❌ **Yerinde kalır** — Bakım ekranında görünmeye devam eder |
| **Stok** | ❌ **10 adet düşük kalır** — geri gelmez |
| **Stok hareketi (defter)** | ❌ Defterde kalır |
| Raporlar | Günlük Faaliyet raporunda **yok**, Bakım ve Stok raporlarında **var** → **iki rapor birbirini tutmaz** |

Kodun kendi açıklaması bunu zaten söylüyor: *"Bakım tipinde bağlı bakım kaydı Bakım ekranında kalır
(orada iptal edilir)."* Yani **bilinçli bir tasarım**, ama kullanıcı için tuzak: sen "sildim" diyorsun,
malzeme hâlâ düşük.

### 3.3 Üç seçeneğin karşılaştırması

| Seçenek | Veri bütünlüğü | Kullanıcı deneyimi | Risk | Migration |
|---|---|---|---|---|
| **(A) Silince bakım+stok otomatik iptal** | ✅ Tam tutarlı | ✅ En kolay: tek işlem | ⚠️ Tek tıkla stok hareketi geri döner — yanlışlıkla basılırsa etkisi büyük (ama iptal edilebilir, iz kalır) | ❌ |
| **(B) Uyarı göster, davranışı değiştirme** | ❌ Tutarsızlık **devam eder** | ⚠️ Kullanıcı iki ekranda iş yapmak zorunda | Düşük | ❌ |
| **(C) Stok hareketi varsa silmeye izin verme** | ✅ Tutarsızlık oluşamaz | ⚠️ "Neden silemiyorum?" — önce Bakım'dan iptal etmeli | Düşük | ❌ |

### 3.4 Önerim: **(A)** — ama **(C)** güvenlik ağıyla birlikte

Somut öneri: faaliyet silinirken **bağlı bakım kaydı da iptal edilsin** (ters kayıtla, iz bırakarak) ve
kullanıcıya **silmeden önce** şu açık onay çıksın:

> *"Bu faaliyete bağlı bakım kaydı ve 10 adet malzeme çıkışı da iptal edilecek. Devam edilsin mi?"*

Bakım kaydı **başka bir yerden zaten iptal edilmişse** ikinci kez iptal edilmez (tekrar güvenli).
Böylece hem tutarlılık sağlanır hem kullanıcı ne olacağını bilir. **(B) tek başına yetersiz**, çünkü sorunun
kendisini çözmüyor; sadece tarif ediyor.

---

## 4. K3 — DÜZENLEME MODELİ (hangi alan nasıl değişsin)

Mevcut kayıtların alanlarını, **hesabı etkileyip etkilemediğine** göre ayırdım:

### A) Stok / para / sayaç sonucunu ETKİLEYEN alanlar → **iptal + yeni kayıt**

| Kayıt | Alanlar |
|---|---|
| Yakıt depo girişi | litre, birim fiyat, para birimi, kur, tedarikçi, tarih |
| Yakıt dağıtımı | litre, birim fiyat, araç, sayaç değerleri, tarih |
| Bakım kaydı | malzeme, miktar, araç, bakım tanımı, yapılan km/saat, tarih |
| Günlük Faaliyet (bakım/ilave) | yukarıdakilerin aynısı |

**Neden:** Bunlar deftere yazılmış ve bakiyeyi/maliyeti değiştirmiş değerler. Üstüne yazmak, geçmiş
raporları sessizce değiştirir; "dün 120 L yazıyordu, bugün 100 L" olur ve farkın nedeni kaybolur.

### B) Hesabı ETKİLEMEYEN alanlar → **doğrudan güncelleme**

| Kayıt | Alanlar |
|---|---|
| Hepsi | açıklama / not, fatura no, sipariş/irsaliye no, operatör-şoför adı *(bilgi amaçlı)* |
| Günlük Faaliyet (hareket tipi) | rota (nereden/nereye), süre, açıklama — **stok etkisi yok** |

**Neden:** Bu alanlar hiçbir bakiye veya maliyet hesabına girmiyor; yazım hatası düzeltmek için iptal
gerektirmez. Değişiklik yine **denetim kaydına** yazılır.

### İş kuralı (geliştirmede kullanılacak net cümle)

> **Bir alan bakiyeyi, maliyeti veya sayacı değiştiriyorsa: kaydı iptal et, doğrusunu yeniden gir.
> Değiştirmiyorsa: doğrudan düzenlenebilir, ama değişiklik denetim kaydına yazılır.**

Ekranda bu, kullanıcıya iki ayrı düğme olarak görünür: **"Bilgileri Düzenle"** (B grubu) ve
**"İptal Et ve Yeniden Gir"** (A grubu).

---

## 5. K4 — EXCEL İÇE AKTARMA WEB'E TAŞINSIN MI?

### 5.1 Mevcut mimari — beklediğimden iyi

| Parça | Nerede | Web kullanabilir mi |
|---|---|---|
| Excel dosyasını okuma (`ExcelExportService.ReadRows`) | **Infrastructure (ortak)** | ✅ **Evet, aynen** |
| 7 içe aktarma servisi (araç, personel, malzeme, yakıt ×2, bakım, muayene) | **Infrastructure (ortak)** | ✅ **Evet, aynen** |
| Önizleme + onay mantığı (`DryRun` / `Commit`) | Infrastructure | ✅ **Korunur** |
| Satır hatası (`ImportRowError`) | Infrastructure | ✅ Korunur |
| API ucu | ❌ **Yok** (0 adet) | ➕ **Yeni yazılmalı** |
| Web ekranı | ❌ Yok | ➕ **Yeni yazılmalı** |

→ **İş kuralı kodu yeniden yazılmayacak.** Gereken: dosya yükleme ucu + web ekranı. Yani "iki ayrı mantık"
riski yok; ortak altyapı zaten hazır.

### 5.2 Sorularının cevapları

| Soru | Cevap |
|---|---|
| Aynı import servisi kullanılabilir mi? | **Evet**, hepsi ortak katmanda |
| Yeni API gerekir mi? | **Evet** — dosya yükleme + önizleme + onay uçları |
| Yetki? | Mevcut modül yetkileri aynen kullanılır (`vehicles/Create`, `personnel/Create`…) — **yeni yetki icadı yok** |
| Büyük dosya riski? | ⚠️ **Var.** Masaüstü yerel çalışıyor; sunucuda 3000 satırlık dosya bellek + süre tüketir. Sunucunun yükleme boyutu sınırı ve zaman aşımı ayarlanmalı |
| DryRun/Commit korunur mu? | **Evet**, aynen |
| Migration? | ❌ **Gerekmez** |

### 5.3 Önerim: **Evet, ama acele değil (7. sırada)**

Baban ağırlıklı masaüstü kullanıyor; web'de içe aktarma bugün **acil değil**. Ama "web ve masaüstü eşit
olmalı" kuralına göre eksik ve tamamlanmalı. Orta büyüklükte bir iş.

---

## 6. K5 — M-S1a `company_id` MIGRATION

### 6.1 Teknik olmayan dille: ne yapıyor?

Sistemde her kayıt normalde "hangi firmaya ait" bilgisini taşır. **İki tabloda bu bilgi yok:**
`material_request_items` (talep satırları) ve `maintenance_materials` (bakımda kullanılan malzemeler).
Bunlar firmasını **bağlı oldukları ana kayıttan** (talep / bakım) dolaylı olarak alıyor.

Migration, bu iki tabloya **"firma" sütunu ekliyor** ve mevcut satırların firmasını **ana kaydından
bakarak** dolduruyor. Yeni bir mantık getirmiyor; sadece zaten bilinen bilgiyi kaydın kendisine yazıyor.

### 6.2 Neden gerekli?

Senkronizasyon kodu firma filtresini **yalnız bu sütunu olan tablolara** uyguluyor. Sütun olmayınca:

- **Okuma:** sunucudan veri çekilirken bu iki tablo **firma ayrımı yapılmadan** dönüyor.
- **Yazma:** gelen satır **sadece kimliğine göre** yazılıyor; firma zorlaması yapılmıyor → teoride başka bir
  firmanın satırı üzerine yazılabilir.

### 6.3 Neden P0 ve gerçekten şimdi mi?

**Dürüst cevap: bugün fiili zarar YOK, ama şu an yapmak en güvenli an.**

| Soru | Cevap |
|---|---|
| Şu an zarar veriyor mu? | **Hayır.** Canlıda gerçek kullanan tek firma var; diğer 2 firma test |
| İkinci firma eklenmeden zorunlu mu? | **Evet** — ikinci gerçek firma açıldığı gün risk gerçeğe döner |
| Ertelenebilir mi? | **Evet, ertelenebilir** — ama ertelemenin bir bedeli var (aşağıda) |
| Neden şimdi? | Canlı veri **şu an çok küçük** (667 stok hareketi) ve **0 yetim kayıt** ölçüldü → migration sorunsuz geçer. Veri büyüdükçe risk ve süre artar |

### 6.4 Canlı veriye etkisi

| Konu | Etki |
|---|---|
| Mevcut kayıtlar | **Hiçbiri silinmez/değişmez** — yalnız yeni boş sütun doldurulur |
| Uygulama çalışmaya devam eder mi? | ✅ **Evet.** Eski sürüm de çalışır (sütunu görmezden gelir) |
| PostgreSQL (sunucu/web) | Sütun ekleme anında, tablo yeniden yazılmaz |
| SQLite (masaüstü) | Sütun ekleme anında; **`NOT NULL` kısıtı EKLENMEYECEK** (SQLite'ta sonradan eklenemez, iki veritabanı ayrışırdı) |
| Geri dönüş | Sütun **additive** (eklemeli) olduğu için eski sürüm etkilenmez; gerekirse sütun boşaltılır. Öncesinde sunucu yedeği alınır |
| Doğrulama | Migration **kendi içinde** sayım yapar; tutmazsa **kendini geri alır** (tek transaction) |

### 6.5 Önerim: **Evet, 3. sırada yapılsın** — ama ertelenebilir bir karardır

Erteleme riski: unutulur ve ikinci firma açıldığı gün acil işe dönüşür; o zaman veri de büyük olur.

---

## 7. K6 — ÇİFT TIK HANGİ EKRANLARLA BAŞLASIN?

| Ekran | Oluştur | Düzenle (arka plan) | Sil | Web durumu | Masaüstü durumu | Altyapı gerekir mi | Kilit gerekir mi | Veri riski |
|---|---|---|---|---|---|---|---|---|
| **Personel** | ✅ | ✅ **Hazır** (sürüm kontrollü) | ✅ | Form var | Satır içi form | ❌ **Hayır** | ✅ Zaten var | 🟢 Yok |
| **Talepler** | ✅ | ✅ **Hazır** (`GetForEdit`+`Update`) | ✅ (iptal) | Form var | Satır içi form | ❌ **Hayır** | ⚠️ Sürüm kontrolü yok | 🟢 Düşük |
| Günlük Faaliyet | ✅ | ❌ **Yok** | ✅ | — | — | ✅ **Evet** | Sonra | 🟠 Stok etkisi |
| Yakıt | ✅ | ❌ Yok | ❌ Yok | — | — | ✅ Evet (K1) | Sonra | 🟠 Bakiye etkisi |
| Bakım | ✅ | ❌ Yok | ✅ (iptal) | — | — | ✅ Evet | Sonra | 🟠 Stok etkisi |
| Stok Giriş/Çıkış | ✅ | ❌ (tasarım gereği) | ✅ (ters kayıt) | — | — | — | — | 🔴 Defter |

**Önerim doğrulandı: Personel + Talepler ile başlanmalı.** Sebep: arka planı hazır olan **tek iki ekran**
bunlar; ortak pencere bileşeni burada risksizce kurulur, sonra zor ekranlarda **yeniden kullanılır**.

> ⚠️ Talepler'de küçük bir ek: alan düzenlemede sürüm kontrolü (aynı kaydı iki kişi açarsa) yok. Çift tık
> işiyle **birlikte** eklenmesi doğal olur (küçük ek).

---

## 8. WEB + MASAÜSTÜ KONTROLÜ (her öneri için)

| İş | Web | Masaüstü | Ortak altyapı kullanılabilir mi |
|---|---|---|---|
| K1 Yakıt iptali | ❌ yok | ❌ yok | ✅ Tek servis (`FuelService`) → iki arayüz aynı ucu kullanır |
| K2 Faaliyet silme tutarlılığı | ⚠️ aynı sorun | ⚠️ aynı sorun | ✅ Tek servis |
| K3 Düzenleme modeli | ❌ | ❌ | ✅ Tek servis + iki arayüz |
| K4 Excel içe aktarma | ❌ **yok** | ✅ var | ✅ Servisler ortak; yalnız API+ekran eklenecek |
| K5 M-S1a | — | — | ✅ Tek migration, iki veritabanında da çalışır |
| K6 Çift tık | farklı desen | kısmi | ✅ Ortak pencere bileşeni (masaüstü) + web'de eşdeğer dialog |

**Kural:** Hiçbir iş tek platformda bırakılmayacak; servis katmanı **tek** yazılacak, iki arayüz onu kullanacak.

---

## 9. ALAN/KOLON YÖNETİMİ — MEVCUT MİMARİ VE ÖN KOŞULLAR

### 9.1 Bugünkü yapı

Bir listede görünen kolonlar **dört ayrı yerde** tanımlı:

| Katman | Dosya/yer | Not |
|---|---|---|
| Kolon kataloğu — masaüstü | `Application/Ui/ListColumns.cs` | 36 kolon |
| Kolon kataloğu — web | `Web/Services/ListColumns.cs` | 36 kolon — **ayrı dosya, elle senkron** |
| Filtre/sorgu | `*Service.SearchGrid` + `GridFilter` | Her yeni alan için ayrıca eklenir |
| API ucu | `/grid` parametreleri | Ayrıca eklenir |
| Rapor | `ReportCatalog` + `ReportService` | Ayrıca eklenir |

Şu an iki katalog **aynı** (36–36), ama **elle** tutuluyor: birine alan eklenip diğerine eklenmezse
ekranlar sessizce ayrışır.

### 9.2 Alan/Kolon Yönetimi için gerçek ön koşullar

| # | Ön koşul | Zorunlu mu |
|---|---|---|
| 1 | Kolon kataloğunun **tek kaynağa** indirilmesi | ✅ **Evet** — iki kopya varken "yönetim ekranı" hangisini yönetecek belirsiz |
| 2 | Yeni alan eklerken 5 adımın (katalog→filtre→API→UI→test) tek yerden akması | ⚠️ İyi olur |
| 3 | Kullanıcı bazlı kolon tercihi saklama | ⚠️ Zaten kısmen var (`list_preference`) — genişletme gerekebilir (**migration ihtimali**) |

### 9.3 Şu anki işler bu geleceği zorlaştırıyor mu?

**Hayır.** Önerilen P0/P1 işlerinin hiçbiri kolon kataloğuna dokunmuyor. **Şimdi gereksiz yeniden
düzenleme (refactoring) yapmıyorum**; katalog tekilleştirmesi kendi sırasında (10. adım) yapılacak.

---

## 10. KARAR TABLOSU

| Karar | Önerim | Neden | Risk | Migration | Şimdi karar vermem gerekiyor mu? |
|---|---|---|---|---|---|
| **K1** Yakıt yanlış kaydı | **İptal + yeni kayıt** (ters kayıt deseni) | Projenin stok/bakım deseniyle aynı; geçmiş izi kalır; `is_deleted` zaten var | 🟢 Düşük | ❌ Hayır | ✅ **Evet** — 1. iş bu |
| **K2** Faaliyet silme | **Bağlı bakım+stok da iptal edilsin + silmeden önce açık onay** | Tek işlemde tutarlılık; kullanıcı ne olacağını görür | 🟡 Orta (tek tıkla stok geri döner, ama iz kalır) | ❌ Hayır | ✅ **Evet** — 2. iş bu |
| **K3** Düzenleme modeli | **Hesabı etkileyen alan → iptal+yeni; etkilemeyen → doğrudan düzenle** | Defter bozulmaz, yazım hatası kolay düzelir | 🟢 Düşük | ❌ Hayır | ⚠️ İlke onayı yeter (uygulama 5. işte) |
| **K4** Excel → web | **Evet, ama 7. sırada** | Ortak servisler hazır; yalnız API+ekran eksik | 🟡 Orta (büyük dosya) | ❌ Hayır | ❌ Sonra karar verilebilir |
| **K5** M-S1a migration | **Evet, 3. sırada** | Veri şu an küçük ve temiz (0 yetim); ikinci firmadan önce zorunlu | 🟡 Orta (canlı veri) | ✅ **EVET — tek migration** | ✅ **Evet** |
| **K6** Çift tık başlangıcı | **Personel + Talepler** | Arka planı hazır tek iki ekran; ortak pencere burada risksiz kurulur | 🟢 Düşük | ❌ Hayır | ⚠️ Sıra onayı yeter |
| **K7** Sıra | **Aşağıdaki 11 adım** (4. sıra değişti) | Bağımlılıklara göre | — | — | ✅ **Evet** |

---

## 11. NİHAİ GELİŞTİRME SIRASI

| # | İş | Öncelik | Web | Masaüstü | Bağımlılık | Migration | Risk | Neden bu sırada |
|---|---|---|---|---|---|---|---|---|
| 1 | **Yakıt kaydı iptali** | P0 | eklenecek | eklenecek | — | ❌ | 🟢 | Bağımsız, en somut veri riski, şema hazır |
| 2 | **Günlük Faaliyet silme ↔ stok tutarlılığı** | P0 | eklenecek | eklenecek | — | ❌ | 🟡 | Küçük kapsam, kullanıcı tuzağını kapatır |
| 3 | **M-S1a `company_id`** | P0 | — | — | — | ✅ **EVET** | 🟡 | Veri küçük ve temizken en güvenli an |
| 4 | **Ortak düzenleme penceresi + Personel & Talepler çift tık** | P1 | eşdeğer dialog | yeni pencere | — | ❌ | 🟢 | Arka planı hazır; bileşen burada kurulur |
| 5 | Günlük Faaliyet + Bakım düzenleme (K3 kuralıyla) | P1 | eklenecek | eklenecek | 4 | ❌ | 🟡 | 4'teki pencere yeniden kullanılır |
| 6 | Düzenleme kilidi (Günlük/Yakıt/Bakım) + Talep sürüm kontrolü | P1 | ✅ | ✅ | 1, 5 | ❌ | 🟢 | Düzenleme var olunca anlam kazanır |
| 7 | **Excel içe aktarma → API + web** | P1 | **yeni** | var | — | ❌ | 🟡 | En büyük platform farkı; bağımsız |
| 8 | Giriş-Çıkış çoklu malzeme · Şube sürüm kontrolü | P1 | ✅ | ✅ | — | ❌ | 🟢 | Küçük, bağımsız |
| 9 | LookupBox geçişi (hızlı düzenleme pencereleri dahil) | P2 | — | ✅ | 4 | ❌ | 🟢 | Ekran ekran, risksiz |
| 10 | Kolon kataloğu tekilleştirme → **Alan/Kolon Yönetimi** | P2 | ✅ | ✅ | — | ⚠️ olabilir | 🟡 | Rapor alanlarının genişlemesi için |
| 11 | Faz S (senkron hızı) · FK ekleme · şube benzersizliği · makine yetkisi · giriş hız sınırı | P3 | — | — | — | kısmen | 🟢 | Acil değil |

---

## OSMAN'IN ŞİMDİ CEVAPLAMASI GEREKEN KARARLAR

Yalnızca **senin iş tercihin** olan, koddan çıkaramayacağım maddeler:

**1. Yakıt kaydı yanlış girildiğinde ne olsun?**
   → **(a) İptal et, doğrusunu yeniden gir** *(önerim — iz kalır, rapor düzelir)*
   → (b) Kaydın üstüne yaz (geçmiş iz kaybolur)
   → (c) Tamamen sil (iz kalmaz)

**2. Günlük Faaliyet silinince bağlı bakım ve malzeme çıkışı ne olsun?**
   → **(a) Onay sorduktan sonra o da otomatik iptal edilsin** *(önerim)*
   → (b) Sadece uyarı çıksın, kullanıcı Bakım ekranından ayrıca iptal etsin
   → (c) Stok hareketi varsa faaliyet silinemesin

**3. "Düzenle" ilkesi:** Bakiyeyi/maliyeti etkileyen alanlar için **iptal + yeniden giriş**, etkilemeyen
   alanlar (açıklama, not, fatura no) için **doğrudan düzenleme** — onaylıyor musun?

**4. `company_id` migration'ı 3. sırada şimdi yapılsın mı,** yoksa ikinci firma açılmadan önceye mi
   bırakılsın? *(önerim: şimdi — veri küçük ve temizken)*

**5. Yukarıdaki 11 adımlık sırayı onaylıyor musun?** *(tek değişiklik: çift tık işi 9→4. sıraya alındı)*

**6. Baban sahada masaüstünden Excel içe aktarma yapıyor mu, web'de de gerekiyor mu?** *(K4'ün sırasını
   bu belirler — bugün 7. sıradayım)*

---

## BU AŞAMADA YAPILMAYANLAR

Kod yazılmadı · migration oluşturulmadı/çalıştırılmadı · veritabanı değiştirilmedi · test amaçlı dahil
hiçbir veri yazılmadı · deploy yapılmadı · hiçbir iş başlatılmadı.
