# Gelen Görev Paketi — 2026-08-06 (2. paket) — Çeşitli Modüllerde İyileştirme

> Ham prompt aşağıda değiştirilmeden saklanır. Analiz + sıralama en altta.

---

## HAM PROMPT

### 1. MALZEMELER ANA MENÜ

**1.1 Giriş/Çıkış ekranında mevcut malzemeye giriş yapılabilmeli**
Şu an yalnızca YENİ malzeme oluşturularak giriş yapılabiliyor. Mevcut kayıtlı bir malzemeye de giriş
yapılabilmeli — mevcut malzeme seçilebilecek bir alan eklenmeli. Malzeme seçilince şu alanlar PASİF/gizli
olmalı (zaten malzemeden gelir, tekrar düzenlenmez): Kod, Ad, Tür, Birim, Kategori, Alt Kategori, Marka.
Şu alanlar AKTİF kalmalı (aynı malzeme farklı tedarikçiden farklı fiyatla alınabilir): Tedarikçi, Birim
Fiyat, Fatura/Fiş/İrsaliye bilgileri, Açıklama.

**1.2 Malzeme düzenleme ekranına stok alanı ekle**
Malzeme Listesi düzenlemede mevcut stok miktarı görüntülenemiyor/değiştirilemiyor. Eklenmeli.

**1.3 Giriş/Çıkış kullanılmadan stok değiştirilirse güçlü uyarı**
Malzeme Düzenleme'den stok miktarı değiştirilmek istenirse, işlem öncesi güçlü uyarı: "Stok miktarını
doğrudan düzenlemeye çalışıyorsunuz. Stok hareketlerinin kayıt altına alınabilmesi için işlemleri mümkün
olduğunca Giriş/Çıkış ekranından gerçekleştirmeniz önerilir." (benzer profesyonel içerik olabilir).

**1.4 Uyarı kayıtlarının loglanması**
Bu tür uyarılar yalnız gösterilmekle kalmaz, KAYIT ALTINA alınır. Log en az: Kullanıcı, Tarih, Saat, Yapılmak
istenen işlem, Açılan uyarı, İşlem devam etti mi/iptal mi. Bu kayıtları görüntüleyecek YENİ bir ekran gerekir.

**1.5 Yetkilendirme**
Bu (log) ekranı yalnız yetkisi olan kullanıcılar görebilmeli — Yetki Ağacına gerekli izin eklenmeli.

**1.6 Yetki ağacı düzenleme problemi (BUG)**
Yetki yönetimi ekranında mevcut kullanıcı düzenlenmek istendiğinde sahip olduğu yetkiler GÖRÜNMÜYOR — tüm
kutular boş geliyor. Düzenleme açıldığında: mevcut yetkiler işaretli gelmeli, hangi yetkilere sahip olduğu
görülebilmeli, istenirse kaldırılabilmeli/eklenebilmeli.

**1.7 Kategori ve Alt Kategori problemi (BUG)**
Malzeme düzenleme ekranında Kategori/Alt Kategori alanları BOŞ geliyor — oysa oluşturulurken seçilen
değerler tekrar yüklenmeli. Düzelt. Benzer mantıkta çalışan DİĞER seçim alanlarını da incele, aynı sorun
varsa onları da düzelt.

---

### 2. YAKIT ANA MENÜ

**2.1 Tedarikçi alanına "+" butonu**
Depo Girişleri ekranındaki Tedarikçi alanının yanında "+" butonu olmalı — projedeki diğer ekranlarda aynı
alan bu özelliğe sahip, burada eksik. GENEL KURAL: bir sabit-tanım alanına "+" eklenmişse, aynı alanın
kullanıldığı DİĞER ekranlarda da aynı özellik bulunmalı — analiz edip eksikleri tamamla.

**2.2 Personel alanlarında metin araması**
"Yakıtı Veren Personel" / "Yakıtı Alan Personel" alanlarında liste geliyor ama metin araması YOK. Standart
arama davranışı uygulanmalı.

---

### 3. ORTAK ALAN DAVRANIŞLARI

Projedeki TÜM seçim alanları analiz edilmeli. Standart davranış: alana tıklayınca (arama yapmadan önce)
mevcut kayıtlar listelenir; kayıt sayısı 25'ten fazlaysa İLK ETAPTA en fazla 25 gösterilir. Kullanıcı arama
yapmaya başlayınca 25 sınırı KALKAR, arama sonucundaki TÜM uygun kayıtlar listelenir. Bu davranış ortak
kullanılan TÜM seçim alanlarında aynı olmalı.

---

### 4. YÖNETİM ANA MENÜ — Sistem Logu

Sistem Logu ekranına eklenmeli: Tarih Aralığı filtresi + Listelenecek kayıt sayısı seçimi. Performansı
olumsuz etkilemeyecek şekilde uygulanmalı.

---

### 5. BAKIM TAKİBİ ANA MENÜ

**5.1 Teknisyen alanı (BUG)**
Teknisyen seçilen personel alanında tutarsız davranış: bazen arama sonrası personel doğru seçiliyor, bazen
seçim kayboluyor. Gerçek nedeni analiz ederek düzelt — geçici çözüm üretme.

**5.2 "+" Personel ekleme butonu**
Teknisyen alanının yanında "+" butonu olmalı, yeni personel eklenebilmeli. Bu butondan eklenenler arka
planda otomatik "Saha Personeli" olarak işaretlenmeli. Bu yapı Personeller modülünde zaten mevcut — mevcut
yapıyı yeniden kullan.

**5.3 Negatif stok davranışı (Bakım Takibi'nde)**
Şu an: "Kaydedilemedi: Negatif stok engellendi: mevcut 0, talep 1." — bu davranış DEĞİŞMELİ. Negatif stok
işlemi ENGELLENMEMELİ (kullanıcı stok girişini henüz yapmamış olabilir, iş süreci durmasın). Yeni davranış:
işlem engellenmez, ama uyarı gösterilir: "İlgili malzeme için yeterli stok bulunmamaktadır. İşleme devam
edebilirsiniz. İsterseniz bu işlem için otomatik bir malzeme talebi oluşturabilirsiniz." Uyarı penceresinde
"Taslak Talep Oluştur" butonu bulunur; basılınca otomatik taslak talep oluşur, sonra "Talep taslak olarak
oluşturulmuştur. Talebi düzenleyerek gönderim sağlayabilirsiniz." bilgisi verilir. Bu işlemden sonra kullanıcı
bakım kaydını oluşturmaya DEVAM edebilmeli — iş akışı kesintiye uğramamalı.

---

### PROJE STANDARDI (ham prompttan)
Önce analiz et. Ortak bileşenleri kullan. Kod tekrarından kaçın. Merkezi çözüm geliştir. Çalışan sistemi
bozma. Geriye dönük uyumluluğu koru. Gereksiz refactoring yapma. İlgili tüm ekranları test et. **Yeni özellik
yetki gerektiriyorsa Yetki Ağacına EKLE — kullanıcı bunu artık her seferinde hatırlatmayacak (hafızaya
alındı).**

---

## SIRALAMA VE PLAN (Claude analizi)

8 uygulama birimine ayrıldı. Sıra: **gerçek hatalar (veri kaybı riski) önce, sonra düşük-riskli tutarlılık
işleri, sonra tek-ekran özellikler, en büyük yeni özellik + en riskli iş kuralı değişikliği sona.**
Her birim masaüstü önce → web hemen ardından; birim bitince commit+push.

| # | Birim | Kapsam | Risk | Önerilen motor |
|---|-------|--------|------|-----------------|
| 1 | **Düzenleme-ekranı boş-alan hataları** | 1.6 (yetki ağacı boş) + 1.7 (kategori/alt kategori boş + benzer alanlar) + 5.1 (teknisyen seçim kayboluyor) | Yüksek (gerçek veri kaybı riski — kullanıcı "boş" görüp yanlışlıkla yetki/kategori siler) | **Opus 4.8** |
| 2 | **Yakıt tutarlılık** | 2.1 (+ buton) + 2.2 (personel arama) | Düşük | Sonnet 5 |
| 3 | **Ortak seçim alanı davranışı (25 kayıt sınırı)** | Madde 3 — tüm seçim alanları | Orta (geniş kapsam) | Sonnet 5 |
| 4 | **Giriş/Çıkış'ta mevcut malzemeye giriş (1.1)** | Tek ekran, orta karmaşıklık | Orta | Sonnet 5 |
| 5 | **Sistem Logu filtreleri (4)** | Tarih aralığı + kayıt sayısı | Düşük-orta | Sonnet 5 |
| 6 | **Bakım "+ Personel" butonu (5.2)** | Personel modülünü yeniden kullan | Düşük | Sonnet 5 |
| 7 | **Malzeme stok alanı + uyarı + log ekranı + yetki (1.2-1.5)** | En büyük yeni özellik: yeni ekran + yeni yetki + yeni log mekanizması | Orta-yüksek (yeni altyapı) | Opus 4.8 (gerekirse) |
| 8 | **Bakım negatif stok davranışı (5.3)** | Stok kuralı değişikliği + yeni Taslak Talep akışı | Yüksek (stok/talep iş kuralı) | **Opus 4.8** |

**Neden bu sıra:** Gerçek hatalar (veri kaybı riski taşıyanlar) her zaman önce — kullanıcı bunları test
sırasında zaten yaşıyor olabilir. Sonra düşük riskli tutarlılık/mekanik işler (momentum + düşük hata payı).
Ortak seçim-alanı standardı (25 kayıt sınırı), bir önceki paketin (Birim 5 arama standardı) doğal devamı
olduğu için erken sıraya alındı — aynı bileşenlere dokunuyor, context taze. Tek-ekran özellikler ortada.
En büyük yeni altyapı (log ekranı+yetki) ve en riskli iş-kuralı değişikliği (negatif stok davranışı,
stok/talep modüllerini birbirine bağlıyor) sona bırakıldı.

**Durum:** Birim #1 sürüyor (2026-08-06, Opus 4.8). Kök neden analizi + bulgular:

- **1.7 (kategori/alt kategori boş) — DÜZELTİLDİ, masaüstüne özel.** Kök neden: `MaterialsViewModel.BeginEdit`
  malzemenin YAPRAK `category_id`'sini naif `Categories.FirstOrDefault` (yalnız üst-seviye) + `SubCategories`
  (yüklü değil) ile arıyordu → malzemenin bir ALT kategorisi varsa ikisi de null → iki kutu da boş. Web ana
  form (`ResolveEditCategory`) ve iki QuickEdit ekranı zaten ebeveyn-tara ile doğru çözüyordu; masaüstü ana
  form da aynı `ResolveEditCategory` mantığına hizalandı. (Unit/Marka/Tedarikçi düz liste, çözüm sorunu yok.)
- **1.6 (yetki ağacı boş geliyor) — MEVCUT KODDA HATA DEĞİL (ampirik kanıt).** Hem masaüstü (PermissionsViewModel)
  hem web (Permissions.razor + PermMatrix) yetki-yükleme kodu doğru; web'de zaten önceki oturumdan "artık boş
  açılmaz" düzeltme yorumları var. **Canlı API'de kaydet→yükle round-trip'i test edildi** (`personel` test
  kullanıcısına materials yetkisi kaydedildi → sunucudan doğru geri geldi → 0'a restore edildi): round-trip
  KUSURSUZ. Tek non-admin kullanıcı `personel` gerçekten 0 yetkiye sahip → boş kutular DOĞRU davranış. Rapor
  muhtemelen (a) önceki düzeltmeden eski/bayat, ya da (b) "atadım ama Kaydet'e basmadan tekrar açtım" senaryosu.
  **Değişiklik yapılmadı** (çalışan kodu bozmamak için); kullanıcının güncel sürümde tekrar test etmesi önerilir.
- **5.1 (bakım teknisyen seçimi kayboluyor) — VM'de hata YOK; Avalonia `AutoCompleteBox` çerçeve davranışı.**
  `MntTechnician` yalnız `ClearMnt`'te sıfırlanıyor; hiçbir property-changed cascade temizlemiyor. Teknisyen
  alanı, projedeki Personel/Araç seçicileriyle BİREBİR aynı `AutoCompleteBox` desenini kullanıyor
  (`SelectedItem` + `ValueMemberBinding` + `FilterMode=Contains`). "Bazen kayboluyor", Avalonia
  AutoCompleteBox'ın `SelectedItem`/`Text` desync'i olarak biliniyor. Bu ortamda Avalonia görsel testi
  YAPILAMIYOR → kesin fix ampirik doğrulanamıyor. Kullanıcıya danışılacak (aday fix + masaüstünde test).

**5.1 KARARI (kullanıcı, 2026-08-06):** "Şimdilik atla, Birim 2'ye geç." → 5.1 ertelendi; daha net tekrar-üretim
adımları ya da masaüstü test turu ile sonra dönülecek. Birim 1'in kod tarafı (1.7) tamam, 1.6 hata değil.

**Birim #2 (2026-08-06, Sonnet-önerildi ama Opus'ta yapıldı) — Yakıt tutarlılık. MASAÜSTÜNE ÖZEL bulundu.**
- **Web zaten doğru:** Fuel.razor ortak `LookupSelect` bileşenini kullanıyor — Tedarikçi'de `AddTable="suppliers"`
  ("+" dahili) + arama dahili; "Yakıtı Veren/Alan" `LookupSelect` (arama dahili). Web'de değişiklik gerekmedi.
- **Masaüstü 2.1:** Depo Girişleri Tedarikçi alanına "+" ekleme (StartAddSupplier/ConfirmAddSupplier/CancelAdd +
  IsAddingSupplier/NewSupplierName + inline ekleme satırı) — Malzemeler ekranıyla AYNI desen. `CanAddLookup`
  (ViewModelBase) yetkisiyle gösteriliyor.
- **Masaüstü 2.2:** "Yakıtı Veren"/"Yakıtı Alan" ComboBox → AutoCompleteBox (arama). Build 0 hata, test 590/0.
- ⏭️ **2.1 genel kural — kalan masaüstü boşluğu:** StockEntryView "Yeni Kayıt" formundaki malzeme-oluşturma
  lookup'ları (Birim/Kategori/Alt Kategori/Marka/Tedarikçi) MaterialsView'da "+" var ama StockEntry'de YOK.
  Ayrı, sınırlı bir takip işi olarak kullanıcıya sunuldu (5 alan × tam "+" tesisatı; görsel test bu ortamda yok).
  Web tüm ekranlarda merkezi `LookupSelect` sayesinde zaten tam.

- [x] 1 — Düzenleme-ekranı boş-alan hataları (1.7 ✅ düzeltildi · 1.6 hata değil/ampirik kanıt · 5.1 ERTELENDİ)
- [x] 2 — Yakıt tutarlılık (2.1 Fuel + · 2.2 Fuel arama ✅ · genel-kural StockEntry "+" takip işi olarak sunuldu)
- [ ] 3 — Ortak seçim alanı davranışı (madde 3)
- [ ] 2 — Yakıt tutarlılık (2.1+2.2)
- [ ] 3 — Ortak seçim alanı davranışı (madde 3)
- [ ] 4 — Giriş/Çıkış'ta mevcut malzemeye giriş (1.1)
- [ ] 5 — Sistem Logu filtreleri (madde 4)
- [ ] 6 — Bakım "+ Personel" butonu (5.2)
- [ ] 7 — Malzeme stok alanı + uyarı + log ekranı + yetki (1.2-1.5)
- [ ] 8 — Bakım negatif stok davranışı (5.3)
