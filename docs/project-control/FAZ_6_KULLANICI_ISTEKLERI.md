# FAZ 6 — Kullanıcı hataları ve istekleri (2026-09-06)

> Kullanıcının kendi cümleleriyle kaydedilmiştir. Promt uzun olduğu için fazlara ayrıldı ve
> **sırayla** işlenecek. Her fazın sonunda bu dosyadaki durum güncellenir.

## Kullanıcının koyduğu kurallar (her fazda geçerli)

- Testler **masaüstünde** yapıldı; belirtilen hatalar **web'de de var demek değildir**.
  **İki ortam da ayrı ayrı analiz edilecek**; biri analiz edilmeden işleme devam edilmeyecek.
- İsteklerle ilgili **ve etkilediği bütün alanlar** eksiksiz kontrol ve analiz edilecek.
- **Çalışan hiçbir yapı bozulmayacak.** Çalışma tam ve eksiksiz olmalı.
- Önce **düzeltmeler**, sonra sıraya alınan işler.
- Önce küçük testler + geliştirmeler; işler bitince **tam kapsamlı test + otomatik deploy**.
- **Hiçbir faz ve süreç için onay/izin istenmeyecek.**

---

## HATALAR (önce bunlar)

| # | Hata (kullanıcının cümlesi) | Ortam | Durum |
|---|---|---|---|
| H1 | "sohbet butonuna masaüstü uygulamanın ana sayfasında erişemiyorum" | masaüstü | ⏳ |
| H2 | "webte çevirim dışı kullanıcıları göstermiyor. 2 ortam içinde çevirim dışıykende mesaj atabilmeliyim ve kişiler çevirim içi olduklarında mesajları görebilmeliler" | web (+ kural iki ortam) | ⏳ |
| H3 | "sohbet penceresi bir duvar gibi olmalı ve arka planındaki şeyleri etkilememeli… tabloya sohbet penceresi üzerinden tıklama yapabiliyorum" | web | ⏳ |
| H4 | "webten mesaj yollayamadım… gönder butonuna tıkladığım halde sohbet penceresinde görünmedi. ama karşıdaki kullanıcıya bildirim düştü" | web + masaüstü | ⏳ |
| H5 | Cari Hesaplar: `Liste yüklenemedi: "Object reference not set to an instance of an object."` | masaüstü (web temiz) | ✅ **6D bitti** |
| H6 | Excel Merkezi: başka şubenin **yanlış şifresi** girildiği hâlde dosya yükleme ekranı açılıyor 🔴 güvenlik | masaüstü (web zaten doğruydu) | ✅ **6E bitti** |
| H7 | "webte ekip tanımı yaptım ama masaüstüne kayıt atmadı. oluşturduğum kaydı görüntüleyemedim" | web → masaüstü senkron | ⏳ |

---

## İSTEKLER

### İ1 — Alt bar: taşan sekmeler + "Diğer Sayfalar" + sabit Sohbet
Kullanıcının eklediği görsele **sadık kalarak**:

1. **Yerleşim (soldan sağa):** sığdığı kadar sekme → hemen ardından **"Diğer Sayfalar ∨"**
   açılır menü düğmesi → onun **sağında** her zaman görünen ve **en sağda sabit** **"Sohbet"**.
2. **Açılır menü:** "Diğer Sayfalar ∨"a tıklanınca **yukarı doğru açılan** dikey liste; bara
   sığmayan tüm sekmeler **ikonlarıyla** listelenir. Aynı isimli gruplanmış sekmelerde **adet**
   gösterilir (örn. `Bakım Takibi (x2)`). Menüden seçilen sekme aktif olur ve gerekiyorsa ana bara taşınır.
3. **Dinamik:** pencere boyutu değişince taşan sekmeler otomatik olarak panele aktarılır.
   Koyu **ve açık** temaya, yuvarlatılmış düğme/panel hatlarına tam uyum.

### İ2 — 10.000 kayıtlık gerçek yük testi
> "yeni kayıt girilebilen ekranlarda 10.000 data olmalı ki tablolar dolu olduğunda sistemin nerelerde
> hataya düşeceğini bilelim… ekranlara boş girip çıkıyorsun. kod test ederken bu kadar veri girişi
> testini QA testlerini de ekrana bağlanıp bu kadar veri varken test et."

Kayıt girilebilen + listeleyen her ekran için **10.000 kayıt** üretilecek ve testler bu veriyle
koşulacak (hem kod testleri hem **ekrana bağlanan** QA testleri).

---

## FAZ PLANI (sıra bağlayıcı)

| Faz | Kapsam |
|---|---|
| **6A** | H1 — masaüstü ana ekranda Sohbet düğmesine erişim |
| **6B** | H2 + H4 — çevrimdışı kişiler, çevrimdışıya mesaj, gönderilen mesajın pencerede görünmemesi (iki ortam) |
| **6C** | H3 — web sohbet penceresi arka planı etkilemesin (tıklama sızıntısı) |
| **6D** | H5 — Cari Hesaplar null referans (iki ortam) |
| **6E** | H6 — Excel Merkezi şube şifresi doğrulaması 🔴 güvenlik (iki ortam) |
| **6F** | H7 — web'de açılan ekip masaüstüne gelmiyor (senkron) |
| **6G** | İ1 — alt bar taşma menüsü + sabit Sohbet (iki ortam) |
| **6H** | İ2 — 10.000 kayıt üretimi + yük altında kod ve QA testleri |
| **6I** | Tam kapsamlı test + otomatik yayın |

**Durum:** 6E bitti → sıradaki **6F** (web-masaüstü ekip senkronu).

---

## 6D — H5 çözümü (2026-09-06)

**Kök neden: yarış durumu (race condition — iki işin sırası şansa kalması).**
`BranchScopeSelector` kurucusundaki `Single = varsayilan;` ataması, `OnSingleChanged` üzerinden
yenileme geri çağrısını **kurucu daha bitmeden** çalıştırıyordu. Geri çağrı `() => _ = Load()` idi;
`Load()` ise çağıran ekranın `BranchScope` özelliğini okuyor — o özellik
`BranchScope = new BranchScopeSelector(...)` satırı **henüz tamamlanmadığı için null**.
Okuma `Task.Run` ile arka planda yapıldığından hata **bazen** oluşuyordu: iş parçacığı havuzu
kurucudan önce yetişirse hata, sonra yetişirse hata yok. Kullanıcının "bir açıyorum oluyor,
bir açıyorum olmuyor" gözlemi tam olarak budur.

**Etkilenen ekranlar (aynı kalıp):** Cari Hesaplar · Faturalar · Kasa-Banka · Tahsilat-Ödeme.

**Düzeltme — tek noktada, kaynağın kendisinde.** `_kurulumBitti` bayrağı kurucunun **sonunda**
kurulur; tüm yenileme çağrıları korumalı `Tetikle()` üzerinden geçer. Kurulum sırasındaki tetikleme
zaten **gereksiz ikinci yüklemeydi** (ekran, kurucusunun sonunda kendi `Load()`'unu çağırıyor)
→ davranış değişmez, üstelik bir sorgu tasarruf edilir.

**Web ayrıca analiz edildi — temiz, dokunulmadı.** `Parties.razor` içinde `_branchIds` alan
tanımında `Array.Empty<string>()` ile başlatılır ve `Load()` yalnız `OnInitializedAsync` içinde
çalışır; aynı yarış durumu **web'de yoktur**.

**Test:** `tests/DepoWise.Tests/SubeKapsamiKurulumYarisiTests.cs` (4 test) — bayrağın kurucunun
sonunda kurulduğunu, korumasız geri çağrı kalmadığını, seçiciyi kuran dört ekranın kalıbını ve
**yeni bir ekran eklenirse test listesinin güncellenmesi** gerektiğini doğrular.
İlgili küme **19/19 geçti** (yeni 4 + mevcut `BranchScopeParity` 15) · masaüstü derleme **0 hata**.

---

## 6E — H6 çözümü 🔴 güvenlik (2026-09-06)

**Hata:** başka şubenin **yanlış** şifresi girildiği hâlde Excel Merkezi dosya yükleme ekranını açıyordu.

**Kök neden — masaüstünde şube şifresi hiç doğrulanamıyordu:**
masaüstü şube listesini sunucudan **aynalar** (`BranchMirrorApply`) ve ayna, şifre karmasını
(`password_hash`) **bilinçli olarak taşımaz** — karmaların istemci makinelere kopyalanması
çevrimdışı kırma riski doğurur. Karma yerelde boş olunca `VerifyBranchPassword`
"şifre tanımlı değil → serbest" deyip `true` dönüyordu; yani **girilen her şifre kabul ediliyordu**.

**Düzeltme:** doğrulama **yetkili kaynağa (sunucuya)** taşındı — girişteki şube şifresi kontrolüyle
aynı uç (`/api/public/verify-branch`, deneme sınırlı). Sonuç gelene kadar buton **kapalı** kalır ve
`Import()` içinde dosya seçme ekranı açılmadan **önce** yeniden sorulur (arayüz kilidine güvenilmez).
Eski yerel kontrol tamamen kaldırıldı; yerine geri eklenmemesi için kaynağa gerekçeli not düşüldü.

**Bilinçli tek davranış değişikliği:** çevrimdışıyken **başka** bir şubeye aktarım artık yapılamaz
(doğrulanamayan şifre kabul edilemez). Kendi çalışma şubesi ve "Tüm Şubeler" şifre sormaz →
günlük kullanım etkilenmez.

**Web ayrıca analiz edildi — zaten doğruydu, dokunulmadı.** `ImportExcel.razor` isteği API'ye
gönderir; API gerçek karma ile doğrular ve 403 döner, sayfa da butonu yeniden kilitleyip
"Şube şifresi hatalı" der.

**Test:** `tests/DepoWise.Tests/SubeSifresiMasaustuKapisiTests.cs` (3 test) — sunucuda yanlış şifrenin
reddedildiğini, **aynalamadan sonra yerel doğrulamanın her şifreyi kabul ettiğini** (hatanın
mekanizması gerçekten kurularak) ve masaüstünün artık yerel doğrulamaya dönmediğini doğrular.
İlgili küme (Import · Excel · Branch · şube şifresi · şube kapsamı): **400/400 geçti** ·
masaüstü derleme **0 hata**.
