# DepoWise — Proje Rehberi (Ekranlar, Çalışma Mantığı, Sıradaki İşler)

> **Bu dosya ikimizin ortak defteri.** Sen buradan güncelleme/düzeltme yazarsın, ben de senin
> yazdığın çalışma mantığına göre geliştirme yaparım. Sade tut; teknik derinlik gerekmiyor.
> **Nasıl güncellersin?** İlgili ekranın altına düz cümleyle "şöyle olsun / şu yanlış / şunu ekle"
> yaz. Anlamakta zorlanırsam sana sorarım, birlikte netleştiririz.
>
> Son güncelleme: 2026-07-11 · Son masaüstü paket: **1.0.35** (yayınlandı, self-contained) · Migration: **032** · Test: **244**
>
> **Mimari (ADR-057):** Web = **Blazor** (`src/DepoWise.Web`), sunucu DB = **SQLite**. (`apps/web` Next.js donmuş.)
>
> **09-11.07 eklenenler (canlı):** süper admin firma+şube seçimi (web) + zorunlu şube + Tüm Şubeler (ADR-058);
> admin-atanmış makine şubesi + IP'den il (ADR-059); masaüstü süper admin firma/şube seçimi + "makine firması/şubesi ile giriş" (ADR-060);
> "Süper Admin" rolü kullanıcı oluştururken yalnız süper admin'e görünür. API+Web canlıda; masaüstü değişiklikleri 1.0.35 ile görünür.
>
> **05.07 eklenenler (canlı):** güvenlik sertleştirmesi (JWT zorunlu, seed şifreleri, login rate-limit, business-push
> yetki+doğrulama, JWT yenileme, updater yedek+rollback), Çöp Kutusu gerçek, Canlı Sunucu grafik fix, oturum düşünce tekrar-giriş.

---

## 1. Genel Yapı (nasıl çalışıyor)

- **3 parça, tek beyin:** Masaüstü (Windows uygulaması) + Web (tarayıcı) + API (sunucu). İş kuralları
  ve yetkiler **API'de** (sunucuda) tek yerde. Web ince istemci — her şeyi API'ye sorar.
- **Sunucu:** Fly.io'da. API = `depowise-erp.fly.dev`, Web = `depowise-web.fly.dev`. Veri SQLite (`/data`).
- **Masaüstü:** Kendi yerel SQLite'ı var; login + iş verisini sunucuya gönderir (web adminleri görsün diye).
- **Güncelleme:** Web'den yeni paket yayınlanır → masaüstü 60 sn'de otomatik uyarır → indir + kur.
- **Çalıştırma (senin makinen):** Uygulamayı kapat → **"DepoWise (Gercek DB)"** kısayolundan aç
  (COMODO yüzünden .exe değil, kısayol; yoksa sanal/boş veritabanı okur).

## 2. Roller ve Yetki Mantığı

- **3 rol:** **Personel** · **Admin** · **Süper Admin** (sistemsel).
- **Personel:** sadece kendisine açıkça verilen ekranları görür (verilmeyen = gizli).
- **Admin:** firmasında her şeye erişir. Başka admini/süper admini **göremez/düzenleyemez**.
- **Süper Admin:** platform sahibi. Tüm kurallardan muaf; tüm firmaları yönetir. Kayıtları
  hiçbir firma ekranında görünmez.
- **Yetki verme (Yetkiler ekranı):** Personel'e ekran ekran yetki verilir. Bazı ekranlar
  (**Yönetim, Kullanıcı, Yetkiler, Sistem Logu, Yedek**) "kısıtlı" — Personel'e verilmek istenirse
  **uyarı çıkar ve kullanıcı Admin'e yükseltilir** (Admin zaten hepsine erişir).
- **Firma Yetki Kontrol** (yalnız süper admin, web): bir firmada hangi ekranlar Personel'e
  verilebilir/verilemez; süper admin firmaya özel ek kısıt koyabilir.

## 3. Menü Yapısı (güncel — Menu_ve_Ekran_Semasi'ne göre)

Ana Ekran · **Uyarılar** · Malzemeler · Araçlar · Personel · Günlük Faaliyet · Bakım Takibi · Yakıt ·
Yönetim(admin) · Talepler · Raporlar · Yönetici Raporları(admin) · Kullanıcı(admin) · Ayarlar ·
Web Yönetimi(süper admin) · Çöp Kutusu(admin)

> **Not:** Menüdeki ad ile ekran başlığı farklı olabilir (menü "Personel Girişi", başlık "Personel").

---

## 4. Ekranlar — Ne İşe Yarar + Çalışma Mantığı

> Her ekran: **[Ne yapar] · [Mantık/kural] · [Yetki]**. Web dosyası `Components/Pages/*.razor`,
> masaüstü `Views/*.axaml` + `ViewModels/*ViewModel.cs`, iş servisi `Infrastructure/*Service.cs`.

### Ana Ekran (Dashboard)
- **Ne yapar:** Özet sayılar (araç, malzeme, düşük stok, bekleyen talep) + kritik uyarılar.
- **Mantık:** Karta tıkla → ilgili ekran. Uyarıda **"okundu"** → uyarı ana ekrandan kalkar; durumu
  değişirse (kötüleşirse) yeniden görünür. Herkese açık.

### Uyarılar
- **Ne yapar:** TÜM aktif uyarılar (bakım + muayene/sigorta + düşük stok + yakıt) tek listede.
- **Mantık:** Ana ekranda "okundu" yapılsa da aktif olduğu sürece burada kalır. Uyarıya tıkla →
  kaynağına git. Herkes görür; ekran kişinin yetkisine göre filtreler.

### Malzemeler
- **Malzeme Listesi:** malzeme kartları (kod, ad, tür, stok, fiyat). Ekle/düzelt/sil.
- **Yeni Kayıt / Giriş-Çıkış:** stok giriş (Yeni/Transfer) → stok artar; Çıkış → azalır. Belge no
  (fatura/irsaliye/sipariş/veresiye) alanları. Her hareket stok_movements + tek transaction.
- **Stok Sayım:** sistem stoğu vs sayılan → fark kadar düzeltme hareketi.
- **Yetki:** `materials` / `stock`.

### Araçlar
- **Araç Listesi / Yeni Araç Ekle:** araç kartları; yeni araçta **Şablon** seçilince alanlar + uyumlu
  malzemeler + fotoğraflar otomatik dolar. Durum "Bakımda" ise açıklama alanı çıkar.
- **Şablonlar (Araç Genel Tanım):** araç tipi kalıbı (marka/model/iç kod deseni/uyumlu malzeme/foto).
  Yeni araçta kopyalanır; otomatik iç kod üretir (KM-001→KM-002). Yetki: **`vehicle_templates`** (ayrı).
- **Muayene / Sigorta:** araç belgeleri; sonuç Geçti/Kaldı/Ertelendi; tarihe göre uyarı.
- **Yetki:** `vehicles` / `inspection`.

### Personel
- **Ne yapar:** Personel CRUD (ad/unvan/telefon/şube/aktif). Yetki: `personnel`.

### Günlük Faaliyet
- **Ne yapar:** Tek form, "Kayıt Tipi" ile: **Hareket / Transfer / Bakım**.
- **Mantık:** Bakım → gerçek bakım kaydı + tek stok düşümü (çift düşüm YOK). Transfer → araç otomatik
  pasife alınır. Yetki: `daily_activity`.

### Bakım Takibi
- **Bakım Tanımları:** periyodik bakım tanımı (aralık km/gün/saat) + hangi araçlara bağlı.
- **Araç Bakımları:** bakım kaydı + kullanılan malzeme (stok düşer) + sayaç ileri güncelleme.
  Periyodik: her bakım bir sonrakini belirler; %85 yaklaşıyor, %100 gecikti uyarısı. Yetki: `maintenance`.

### Yakıt
- **Dağıtımlar / Depo Girişleri / Özet.** Her dağıtım kendi birim fiyatıyla saklanır (tarihsel maliyet).
  Depo kalanı = tüm alınan − tüm dağıtılan. %20 altına düşünce uyarı. Yetki: `fuel`.

### Yönetim (admin)
- **Şube / Şantiye:** şube CRUD. **Kod + şifre yalnız admin görür/değiştirir**; login'de şube seçiminde
  kullanılır. Yetki: `branches`.
- **Sistem Logu:** işlem kayıtları (salt okunur). Yetki: `audit`.
- **Yedek Yönetimi:** yerel db yedeği al/geri yükle. Yetki: `backup`.

### Talepler
- **Talep Formu:** malzeme talebi; belge no otomatik (TLP-YYYY-NNNN). Stok DÜŞMEZ (sadece belge).
- **Talep Onaylama:** beklemede → onayla/reddet. "Onayla/Reddet" özel buton yetkisiyle. Yetki: `requests`.

### Raporlar / Yönetici Raporları
- **Raporlar:** Genel/Stok/Yakıt/Bakım/Depo/Talep sekmeleri + PDF/Excel. Yetki: `reports`.
- **Yönetici Raporları (admin):** ŞU AN placeholder (genel Raporlar'a bağlı). **Alt raporları sen tanımlayacaksın.**

### Kullanıcı (admin)
- **Kullanıcı Tanım:** kullanıcı CRUD; **tek rol** seçimi (Personel/Admin). Admin başka admini düzenleyemez
  (maskeli). Kota: firma kullanıcı sınırı + admin sınırı (%20). Yetki: `users`.
- **Yetkiler:** kullanıcı seç → ekran ekran Oku/Yaz/Düzelt/Sil + özel butonlar. Kısıtlı ekran seçilince
  Admin'e yükseltme uyarısı. Yetki: `permissions`.
- **Yetki Şablonları (süper admin):** hazır yetki paketi; yeni kullanıcıya uygulanır.

### Ayarlar
- **Tanım Düzenle:** lookup listeleri (marka/kategori/birim...). **Geliştirici Modu** (kod 621875).
  **Tema.** **Hakkında.**

### Web Yönetimi (süper admin — çoğu yalnız web)
- **Firma Tanım / Güncelleme Yönetimi / Makine Yönetimi / Sunucu Yedekleri** · **Canlı Sunucu**
  (sunucu durumu, animasyonlu) · **Kota İzleme** (firma kullanıcı/admin kullanımı) · **Firma Yetki Kontrol**
  (firma bazında verilebilir/verilemez yetkiler).

### Çöp Kutusu (admin)
- **Ne yapar:** Silinen kayıtları listeler → geri yükle. Yetki: `audit` + "geri yükle" butonu.

---

## 5. Önemli İş Kuralları (kısa)

- **Soft delete:** silme = gizle (`is_deleted=1`), veri durur → Çöp Kutusu'ndan geri gelir.
- **Stok her zaman transaction içinde** (hata → geri alınır). Düzenlemede önce eski geri alınır.
- **Sayaç geri gitmez:** araç km/saat yalnız ileri; geçmiş kayıt girilebilir.
- **Tarihsel maliyet:** yakıt/malzeme fiyatı işlem anındaki fiyatla saklanır; sonra değişse geçmişi etkilemez.
- **Arama Türkçe duyarsız** (İstasyon = istasyon).
- **Fotoğraf** yüklenince otomatik küçültülür + JPEG (büyük dosya sorunu yok).

---

## 6. Sıradaki İşler / Backlog

> Yeni istekleri buraya "- [ ] ..." olarak ekleyebilirsin; ben tamamlayınca "- [x]" yaparım.

### Senden girdi bekleyen
- [ ] **Yönetici Raporları** alt raporları — hangi raporlar olsun? (sen tanımla)
- [ ] **Menü adı ↔ ekran başlığı** hizalansın mı? (menü "Personel Girişi" iken başlık "Personel")
- [ ] **Test dönüşü:** eksik/yanlış listen → buraya eklenecek

### Onay verince yapılacak (maliyetsiz)
- [ ] İçe aktarımı Araç + diğer setlere genişletme
- [ ] Masaüstü foto optimizasyonu (kod hazır, 1.0.35 paketinde)

### Hatırlanan istek (beklemede)
- [ ] Personel görev-bazlı alan görünürlüğü (ör. "Şoför" yalnız Araç-Sürücü + Yakıt alanları)

### Maliyet/karar gerektiren (onayla)
- [ ] Üretim hosting · PostgreSQL geçişi · kod-imzalama

---

## 7. Test Notları (sen doldur)

> Test ederken bulduğun eksik/yanlışları buraya yaz; sıradaki işlere alırım.
> Örnek: `- [Araçlar] Yeni araçta şablon seçince yıl dolmuyor.`

- 

---

## 8. Bu Dosyayı Nasıl Kullanırız

- **Sen:** ilgili ekranın altına düz cümleyle değişiklik/eksik yaz. Çalışma mantığını değiştirmek
  istiyorsan "şöyle çalışsın" de.
- **Ben:** her geliştirme öncesi bu dosyayı okur, senin yazdığın mantığa göre yaparım. Yapı/mantık/plan
  bozulacaksa iki seçeneğin farkını anlatır, mantıklı olanı öneririm.
- **Anlaşılmazsa:** birlikte netleştirir, dosyayı düzeltiriz.
