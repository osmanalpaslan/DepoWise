# DepoWise — Kullanım Kılavuzu

> Bu belge son kullanıcı içindir; sade ve teknik-olmayan tutulur. **Her özellik değişikliğinde güncellenir.**
> Son güncelleme: 2026-07-11 · Masaüstü sürüm hedefi: 1.0.37

DepoWise; depo/stok, araç, bakım, yakıt ve personel yönetimini tek yerden yapmanızı sağlayan bir sistemdir.
İki şekilde kullanılır:
- **Web:** `https://depowise-web.fly.dev` — tarayıcıdan, kurulum gerekmez.
- **Masaüstü uygulaması:** İnternet olmadan da çalışır (çevrimdışı); bağlanınca verileri sunucuyla eşitler.

---

## 1. Giriş ve roller

**Giriş:** Firma → Kullanıcı adı → Şifre → Şube. Şube seçimi zorunludur. Şubenin şifresi varsa ayrıca sorulur.
"Tüm Şubeler" yalnız yetkili kullanıcılarda görünür.

**Roller (yetki seviyeleri):**
- **Süper Admin:** Sistemin sahibi. Tüm firmaları görür/yönetir. Firma açar/siler, sunucuyu izler.
- **Firma Admini (Admin):** Kendi firmasını yönetir; kullanıcı açar, yetki verir.
- **Personel:** Yalnız kendisine açılan ekranları kullanır.

> Yetki kuralı: Bir kullanıcı yalnız kendisine **açıkça verilen** ekranları görür (varsayılan olarak kapalıdır).

---

## 2. Günlük kullanım ekranları

- **Stok / Depo:** Malzeme giriş, çıkış ve transferleri. Bakiye doğrudan değiştirilmez; her hareket kayda geçer.
- **Malzeme Talebi:** Talep oluşturma, onaya gönderme, onay/ret. Onaylı talep stoktan düşer.
- **Stok Sayımı:** Fiziksel sayım ve fark raporu.
- **Araç / Makine:** Araç kartları, tanımlar, modeller.
- **Bakım Takibi:** Planlı ve arıza bakımları.
- **Yakıt:** Yakıt girişleri ve tüketim takibi.
- **Günlük Faaliyet:** Tek ekranda bakım kaydı + stok düşümü.
- **Raporlar:** Ağır raporlar yalnız **Sorgula/Filtrele**'ye basınca çalışır (sunucuyu yormamak için).

---

## 3. Yönetim ekranları (Admin / Süper Admin)

- **Firma Tanım:** Firma bilgileri, maksimum kullanıcı sayısı. (Süper Admin)
  - **Firma silme:** Firma silindiğinde bağlı kullanıcılar **silinmez, pasife alınır**; veriler korunur.
  - **Pasif Firmalar (Sözleşme Yenileme):** Silinen firmalar bu bölümde durur. **"Aktife Al"** ile firma geri
    gelir ve pasife alınan kullanıcılar tekrar giriş yapabilir. *(Yeni — 2026-07-11)*
- **Şube:** Şube tanımları ve şube şifreleri.
- **Çalışan Yönetimi (Personel + Kullanıcı, birleşik):** Ad soyad, unvan, telefon, şube, aktif.
  - Her satırda **erişim rozeti**: *Saha* (yalnız personel), *Kullanıcı* veya *Admin* (uygulama hesabı olanlar).
  - Yeni/düzenle formunda **"Uygulama erişimi ver"** (admin) ile aynı ekrandan kullanıcı hesabı (kullanıcı adı/şifre/rol) açılır — bir çalışana tek hesap.
  - **Olası aynı kişi** uyarısı (ad/telefon) mükerrer kaydı engeller; hesap açılmadan kaydederken **"yalnız saha personeli mi?"** onayı sorulur.
  - Admin, mevcut hesabın **bağını kaldırabilir** (hesap silinmez, çalışandan çözülür). *(Yeni — 2026-07-12; web + masaüstü)*
- **Kullanıcı:** Uygulamaya girecek hesaplar; rol ve şube ataması.
- **Yetkiler:** Kullanıcı bazında hangi ekranı görebileceği/işlem yapabileceği (yetki matrisi).
- **Kota İzleme:** Firma başına kullanıcı/admin kotası **ve anlık ONLINE kullanıcı sayısı**
  (son 5 dakikada aktif olanlar). *(Yeni — 2026-07-11)*
- **Makine Yönetimi:** Kayıtlı masaüstü makineleri; firma+şube seçerek sorgulanır. "Kayıtsız Makineler"
  seçeneği şubesiz makineleri gösterir. Makineye ilk giren kullanıcı, onay penceresiyle makinenin
  firma/şubesini tanımlar.

---

## 4. Süper Admin — sistem ekranları

- **Canlı Sunucu Durumu:** 3 saniyede bir yenilenen metrikler. **İşlemci (CPU) ve Bellek (RAM) kullanımı için
  animasyonlu yüzde göstergeleri** (yeşil/sarı/kırmızı eşik), online kullanıcı, çevrimiçi makine, çalışma
  süresi, veritabanı boyutu vb. *(Yeni — 2026-07-11)*
- **Firma Yetki Kontrol:** Bir firmanın adminlerinin personele verebildiği ekranların belirlenmesi.
  **Yeni tasarım:** üstte özet kutular (Serbest / Yalnız Admin / Global kilit), ekran arama, gruplu liste,
  her ekranda 3 durumlu net kontrol ve değişiklik sayacı. *(Yenilendi — 2026-07-11)*
- **Sunucu Yedekleri:** Masaüstü uygulamalarının buluta yüklediği yedekler. Firma + tarih seçip **Listele**.

---

## 5. Yedekleme

- **Otomatik:** Masaüstü uygulaması **günde 1 kez otomatik** yerel yedek alır (veritabanının tutarlı kopyası).
  Yerelde 30 günlük döngü işler. *(Yeni — 2026-07-11)*
- **Elle:** Yedek Yönetimi ekranından "Yedek Al" ile istediğiniz an yedek alınır.
- **Buluta yükleme:** Masaüstünde **Ayarlar › Sunucu Yedek** altında sunucu adresi tanımlıysa her yedek buluta
  yüklenir ve "Sunucu Yedekleri" ekranında görünür. Sunucu yüklenen yedekleri **hiçbir zaman silmez**.
- **Geri yükleme:** Yedek Yönetimi'nden bir yedek seçilip geri yüklenir; ardından uygulama yeniden başlatılır.

---

## 6. Güncelleme (masaüstü)

Yeni sürüm çıktığında masaüstü uygulaması bunu kendisi algılar ve **tek bir** güncelleme penceresi açar
(pencereler birikmez; yeni paket çıkarsa açık pencerenin mesajı güncellenir). Uygulama "kendi kendine
yeterli" (self-contained) paketlenir; ayrıca .NET vb. kurmanız gerekmez.

- **"İndir ve Kur"** ile paket iner; ardından **"Şimdi Yeniden Başlat"** veya **"10 Dakika Ertele"** seçersiniz.
- **Her erteleme 10 dakikadır** (pencerede yazılıdır); süre dolunca tekrar sorulur. Ertelerseniz indirilen paket
  saklanır, tekrar inmez. *(Yeni — 2026-07-11)*

---

## 7. Bağlantı ve çevrimdışı çalışma

Masaüstü internet olmadan çalışır; üst barda bağlantı durumu görünür (Bağlı / Çevrimdışı). İnternet gelince
veriler otomatik eşitlenir. Stok, sayaç, yakıt, bakım ve onay işlemlerinde veri kaybı/çakışma yaşanmaması
için güvenli eşitleme kullanılır.

---

## Sürüm notları (kılavuz için)
- **2026-07-11:** Pasif firma yeniden aktifleştirme; CPU/RAM animasyonlu göstergeler; kota ekranında online
  kullanıcı; otomatik günlük yedek + yedek ekranı bilgi paneli. Firma Yetki Kontrol yeniden tasarım önerisi.
