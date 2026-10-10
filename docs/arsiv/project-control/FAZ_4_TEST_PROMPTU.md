# ALPNEX PROJESİ — FINAL QA MASTER PROMPT
## WEB + DESKTOP TAM KAPSAMLI E2E / GÜVENLİK / VERİ BÜTÜNLÜĞÜ / PERFORMANS / UI QA

> ⚠️ **BU DOSYA KULLANICININ 2026-09-06 TARİHLİ TEST PROMPTUDUR — BİREBİR KAYITTIR.**
> **Tetikleyici:** kullanıcı *"projeyi detaylı test etmeni istiyorum"* dediğinde bu dosya bulunur,
> analiz edilir ve test bu kapsamla başlatılır.
> Testler başarılıysa → **otomatik, eksiksiz yayın** → rapor → **bilgisayarı uykuya al**.
> Ön koşul: `FAZ_4_KULLANICI_ISTEKLERI.md` içindeki tüm işler tamamlanmış olmalı.

---

> **AMAÇ:**  
> Alpnex geliştirme sürecinin tüm özellikleri tamamlandıktan sonra, projeyi gerçek kullanıcı davranışına mümkün olduğunca yakın şekilde uçtan uca test etmek.
>
> Bu çalışma bir geliştirme fazı testi değildir. Bu, **nihai kalite güvence ve kabul öncesi test aşamasıdır.**
>
> Bu nedenle kapsamlı test, uzun süreli test, 10.000+ kayıt testi, Web + Desktop E2E, güvenlik, veri bütünlüğü, offline/sync, performans, UI/UX ve regresyon testleri bu aşamada yapılacaktır.

---

# 1. EN ÖNEMLİ KURALLAR

## 1.1 Önce analiz, sonra uygulama

İlk aşamada KOD DEĞİŞTİRME.

Önce:

- repository yapısını incele
- Web projesini incele
- Desktop projesini incele
- API/backend yapısını incele
- database yapısını incele
- mevcut test projelerini incele
- mevcut E2E altyapısını incele
- mevcut Playwright entegrasyonunu incele
- mevcut Desktop UI Automation altyapısını incele
- mevcut MCP'leri incele
- mevcut test scriptlerini incele
- authentication yapısını incele
- test ortamının nasıl izole edileceğini belirle
- offline/sync mimarisini incele
- mevcut permission / role / field permission sistemini incele
- mevcut rapor/export yapılarını incele

Ardından kısa bir:

**"FINAL QA TEST PLANI"**

oluştur ve ancak bundan sonra uygulamaya/testlere başla.

---

# 2. PRODUCTION KORUMASI — MUTLAK KURAL

Bu çalışma sırasında:

- production'a deploy YAPMA
- production migration ÇALIŞTIRMA
- production database'e yazma
- production verisini değiştirme
- production kullanıcısı oluşturma/değiştirme
- production configuration değiştirme
- production API üzerinde destructive test yapma
- production sync çalıştırma

## Production tespit edilirse:

TESTİ DURDUR.

Hangi URL/config/database/environment'in production olduğunu belirt.

Devam etmek için kullanıcıdan açık onay iste.

---

# 3. COMMIT / PUSH KURALI

Bu çalışma sırasında:

- git commit YAPMA
- git push YAPMA
- branch merge YAPMA

Kod değişiklikleri gerekiyorsa yalnızca çalışma ağacında bırak.

---

# 4. TEST ORTAMI İZOLASYONU

Final QA kesinlikle production'dan bağımsız olmalıdır.

Mümkünse:

- ayrı test database
- ayrı test company/tenant
- ayrı test kullanıcıları
- ayrı configuration
- ayrı local database
- ayrı desktop data directory
- ayrı sync state/cursor
- ayrı test API
- ayrı test authentication/session

kullan.

Test sırasında production configuration kullanıldığı tespit edilirse DUR.

---

# 5. TEST OTOMASYON ARAÇLARI

## 5.1 Web — Playwright

Web E2E için öncelikli araç:

**Playwright**

Mevcut Playwright altyapısı varsa onu kullan.

Yoksa resmi/aktif Playwright çözümünü kullan.

Testlerde mümkün olduğunca:

- role
- accessible name
- label
- test-id
- semantic selector

kullan.

Kırılgan CSS/XPath selector kullanımını minimuma indir.

Her test deterministik olmalıdır.

---

# 6. DESKTOP AUTOMATION

Avalonia Desktop için mevcut otomasyon altyapısını önce incele.

Öncelik:

1. mevcut çalışan Desktop Automation
2. Windows UI Automation tabanlı güvenilir çözüm
3. gerekiyorsa Avalonia'nın desteklediği uygun automation/debug protokolü
4. en son alternatif MCP

Birden fazla desktop automation MCP'sini gereksiz yere kurma.

Gerekirse küçük bir POC yap:

- uygulamayı aç
- login ol
- bir menüye tıkla
- bir textbox'a veri yaz
- bir butona bas
- tabloyu oku
- screenshot al

POC başarısızsa başka çözümü değerlendir.

Amaç en karmaşık aracı kurmak değil, **en güvenilir çalışan otomasyonu seçmektir.**

---

# 7. MCP POLİTİKASI

MCP'leri yalnızca gerçekten ihtiyaç varsa kullan.

Öncelikli:

- Playwright / browser automation
- Desktop UI Automation

Mevcut ve faydalı proje MCP/skill'lerini koru.

Gereksiz MCP kurma.

Bir MCP kurmadan önce:

- gerçekten gerekli mi?
- mevcut araçlarla yapılamıyor mu?
- aktif/bakımlı mı?
- Windows/Avalonia ortamıyla uyumlu mu?
- güvenli mi?

değerlendir.

Testleri yavaşlatacak veya gereksiz context/token tüketimine neden olacak araçları kullanma.

---

# 8. TEST DATA

Deterministik test datası oluştur.

En azından:

## Company / Tenant

- Company A
- Company B

## Kullanıcılar

- Super Admin
- Yönetici
- Normal kullanıcı
- Kısıtlı kullanıcı
- Yetkisiz kullanıcı

Gerekli modüller için uygun roller oluştur.

Test datası birbirinden ayırt edilebilir olmalı.

Örneğin:

`QA-001`, `QA-002`, `QA-003`

gibi isimlendirme kullanılabilir.

Test sonunda test datası mümkün olduğunca temizlenmeli veya test database'i resetlenebilir durumda bırakılmalıdır.

---

# 9. TEST SIRALAMASI — RİSK TABANLI

Testleri rastgele yapma.

Önce:

1. Login / Authentication
2. Tenant / Company isolation
3. Authorization / Permission
4. Kritik CRUD işlemleri
5. Stok ve hareketler
6. Finans / cari / fatura
7. Sync / offline
8. Raporlar / export
9. Veri bütünlüğü
10. E2E kullanıcı senaryoları
11. 10.000+ kayıt performansı
12. UI / UX
13. Accessibility
14. Full regression

şeklinde ilerle.

Kritik bir sistematik hata bulunduğunda aynı hatayı yüzlerce testte tekrar tekrar üretmek yerine önce root cause'u araştır ve düzelt.

---

# 10. WEB — TAM E2E TEST

Web uygulamasındaki **HER ekranı** tespit et.

Her ekran için mümkün olduğunca:

### Navigation
- menüden açma
- geri/ileri
- sayfa yenileme
- doğrudan URL ile açma
- yetkisiz URL erişimi

### Loading
- normal loading
- yavaş API
- boş veri
- hata durumu

### Listeler
- liste görüntüleme
- arama
- filtre
- sıralama
- pagination
- sayfa değişimi
- refresh
- kolonlar
- gizli alanlar

### CRUD
- yeni kayıt
- görüntüleme
- düzenleme
- silme
- iptal
- tekrar açma
- duplicate
- validation

### Dialog / Modal
- aç
- kapat
- ESC
- dışarı tıklama
- kaydet
- iptal

### Form
- boş değer
- geçerli değer
- geçersiz değer
- çok uzun değer
- negatif değer
- sıfır
- duplicate
- zorunlu alan
- yanlış format
- boundary değerler

### Keyboard
- TAB
- SHIFT+TAB
- ENTER
- ESC
- varsa keyboard shortcut'lar

### Error
- API 400
- API 401
- API 403
- API 404
- API 409
- API 500
- timeout
- network failure
- session expiration

Her durumda kullanıcıya anlaşılır hata gösterildiğini doğrula.

---

# 11. DESKTOP — TAM E2E TEST

Desktop uygulamasında da aynı kapsamı uygula.

Kontrol et:

- uygulama açılışı
- login
- logout
- restart
- pencere aç/kapat
- resize
- maximize/minimize
- menüler
- toolbar
- butonlar
- tablar
- form alanları
- combobox
- checkbox
- radio
- date picker
- numeric input
- grid
- scroll
- horizontal scroll
- vertical scroll
- pagination
- search
- filter
- sort
- dialog
- validation
- keyboard navigation
- focus
- TAB sırası
- shortcut'lar
- hata mesajları

Uygulama yeniden başlatıldığında kalıcı olması gereken verilerin gerçekten kaldığını doğrula.

---

# 12. GERÇEK KULLANICI SENARYOLARI

Sadece tek tek buton testleri yapma.

Gerçek kullanıcı akışları oluştur.

Örnek:

### Senaryo A
Login → Malzeme oluştur → düzenle → stok hareketi oluştur → listele → filtrele → raporla.

### Senaryo B
Cari oluştur → fatura oluştur → ödeme/collection işlemi → bakiye kontrolü → rapor.

### Senaryo C
Araç → bakım → yakıt → günlük faaliyet → ilgili kayıtların kontrolü.

### Senaryo D
Talep → onay → stok hareketi → transfer → sonuç kontrolü.

### Senaryo E
Desktop offline → işlem yap → uygulamayı kapat → tekrar aç → sync → Web'den sonucu doğrula.

Her kritik modül için benzer gerçek kullanıcı akışları oluştur.

---

# 13. UI → API → DATABASE → UI DOĞRULAMASI

Kritik işlemlerde sadece UI sonucuna güvenme.

Mümkün olduğunda:

**UI**

↓

**API/service**

↓

**Database**

↓

**UI tekrar kontrol**

şeklinde doğrula.

Örneğin:

- UI'dan kayıt oluştur
- database'de gerçekten oluştuğunu doğrula
- UI refresh yap
- kaydın tekrar geldiğini doğrula
- gerekiyorsa ikinci kullanıcıdan kontrol et

---

# 14. SECURITY / AUTHORIZATION

Her kritik endpoint ve işlem için authorization kontrolü yap.

Test et:

- normal kullanıcı
- yetkili kullanıcı
- yetkisiz kullanıcı
- admin

Aşağıdakileri özellikle kontrol et:

- URL ile doğrudan erişim
- API endpoint doğrudan çağrısı
- ID değiştirerek başka kayda erişim
- Company ID değiştirerek başka tenant'a erişim
- başka kullanıcının verisine erişim
- başka branch verisine erişim
- yetkisiz CRUD
- yetkisiz export
- yetkisiz report
- yetkisiz action

**IDOR / tenant isolation** özellikle test edilmelidir.

---

# 15. ROLE / PERMISSION TESTLERİ

Permission sistemini gerçek kullanıcı gibi test et.

Kontrol et:

- menu permission
- screen permission
- action permission
- button permission
- report permission
- record type permission
- branch scope
- role permission
- role grant limits
- delegation ceiling
- fail-closed davranışı

Permission UI'dan kapatılan işlem:

- UI'da görünmemeli veya disabled olmalı
- API'dan doğrudan çağrıldığında reddedilmeli
- service seviyesinde de korunmalı

Sadece UI gizlemesine güvenme.

---

# 16. FIELD PERMISSION TESTLERİ

Özellikle mevcut field-level permission sistemi için kapsamlı test yap.

Kontrol et:

- VIEW
- EDIT
- VIEW yok
- EDIT var → VIEW otomatik
- protected field
- unprotected field
- role + user birleşimi
- web
- desktop
- API
- write
- export
- report
- derived value
- filter
- sort

Gizli alan:

- UI'da görünmemeli
- API response'ta bulunmamalı
- export'ta bulunmamalı
- report'ta bulunmamalı
- derived value üzerinden sızmamalı
- filter/sort üzerinden inference vermemeli

Doğrudan API request ile gizli alan gönderilmeye çalışıldığında:

**database'deki korunmuş değer değişmemeli.**

Yetkisiz değişiklik mümkün olmamalı.

---

# 17. DATA INTEGRITY

Özellikle kritik işlemlerde database bütünlüğünü kontrol et.

Kontrol et:

- duplicate kayıt
- orphan kayıt
- yanlış foreign key
- yanlış toplam
- yanlış bakiye
- yanlış stok
- negatif stok kuralları
- transaction rollback
- yarım kalan işlem
- duplicate submit
- double click
- aynı işlemin tekrar gönderilmesi
- concurrent update

Bir işlem hata verdiğinde database'in yarım/bozuk durumda kalmadığını doğrula.

---

# 18. STOCK / TRANSFER

Stok sistemi kritik olduğu için ayrıca test et.

Kontrol:

- opening
- in
- out
- transfer
- adjustment
- stock count
- branch
- depot/location
- stock balance
- stock movements
- transfer sonrası kaynak
- transfer sonrası hedef
- tekrar refresh
- restart
- sync

Özellikle:

**stok balance ile stock movement arasındaki tutarlılığı kontrol et.**

---

# 19. OFFLINE / SYNC

Desktop offline çalışma senaryolarını gerçek kullanıcı gibi test et.

Örnek:

1. Online login
2. Offline duruma geç
3. kayıt oluştur
4. kayıt düzenle
5. kayıt sil
6. uygulamayı kapat
7. tekrar aç
8. offline veriyi kontrol et
9. tekrar online ol
10. sync
11. Web'den sonucu kontrol et

Ayrıca:

- network kesilmesi
- network geri gelmesi
- sync sırasında uygulamanın kapanması
- duplicate sync
- tekrar sync
- conflict
- cursor/state recovery
- veri kaybı

kontrol edilmeli.

**Sync sırasında veri kaybı veya duplicate oluşmamalıdır.**

---

# 20. RAPOR / EXPORT

Her önemli rapor için:

- aç
- filtrele
- tarih aralığı
- boş sonuç
- veri sonucu
- yetki
- field permission
- export

test et.

Excel/CSV vb. export varsa:

- doğru kolonlar
- doğru değerler
- gizli alanların çıkmaması
- toplamların doğru olması
- büyük dataset

kontrol edilmeli.

---

# 21. 10.000+ KAYIT PERFORMANS TESTİ

Final QA aşamasında en azından kritik listelerde **10.000+ kayıt** oluştur.

Kontrol et:

- initial load
- search
- filter
- sort
- pagination
- scroll
- virtualisation
- open detail
- edit
- refresh
- repeated open/close
- create
- delete
- export

Özellikle:

- UI freeze
- ciddi gecikme
- memory growth
- CPU spike
- pagination bozulması
- yanlış kayıt
- duplicate
- timeout

kontrol edilmeli.

Performans testleri gerçek kullanıcı davranışına mümkün olduğunca yakın yapılmalı.

---

# 22. MEMORY / RESOURCE LEAK

Özellikle Desktop için:

- ekran aç/kapat
- dialog aç/kapat
- liste aç/kapat
- tekrar tekrar navigation
- refresh
- login/logout
- sync

döngülerini uygula.

Uzun tekrarlar sonunda:

- RAM sürekli artıyor mu?
- CPU normal seviyeye dönüyor mu?
- UI yavaşlıyor mu?
- event handler / timer / subscription leak belirtisi var mı?

kontrol et.

---

# 23. VISUAL QA

Web + Desktop için görsel kontrol yap.

Kontrol:

- light theme
- dark theme
- farklı pencere boyutları
- farklı ekran genişlikleri
- mobil web
- tablet genişliği
- desktop genişliği
- uzun metin
- boş state
- error state
- loading state
- tablo
- modal
- form

Özellikle:

- header/body hizası
- taşan text
- kesilen text
- yanlış spacing
- görünmeyen buton
- üst üste binen element
- yanlış scroll
- responsive bozulma
- gizli field sonrası layout bozulması

kontrol edilmeli.

---

# 24. ACCESSIBILITY

Mümkün olduğunca:

- accessible name
- role
- label
- keyboard navigation
- focus visibility
- TAB order
- button semantics
- form labels
- dialog semantics

kontrol et.

Accessibility problemi bulunduğunda önem derecesini belirt.

---

# 25. NEGATIVE / ABUSE TESTLERİ

Her kritik işlem için mümkün olduğunca:

- boş
- null
- negatif
- 0
- çok büyük sayı
- çok uzun text
- özel karakter
- duplicate
- olmayan ID
- silinmiş ID
- başka company ID
- başka branch ID
- yetkisiz kullanıcı
- double click
- hızlı tekrar submit
- aynı anda iki işlem
- network timeout
- server error
- session timeout

test et.

---

# 26. UYGULAMA RESTART / PERSISTENCE

Hem Web hem Desktop'ta kritik akışlarda:

- refresh
- logout/login
- application restart
- browser restart
- desktop restart

sonrasında verilerin ve gerekli state'in doğru kaldığını kontrol et.

---

# 27. BUG BULUNDUĞUNDA

Bir hata bulunduğunda sadece testi başarısız olarak işaretleme.

Şunları yap:

1. hatayı reproduce et
2. root cause'u araştır
3. ilgili kodu incele
4. backend/database/API/UI katmanlarını kontrol et
5. düzelt
6. aynı hatayı tekrar üretmeye çalış
7. focused regression testi ekle
8. ilgili E2E testini tekrar çalıştır

Ancak:

**TESTİ GEÇİRMEK İÇİN UYGULAMAYI ZAYIFLATMA.**

Kesinlikle:

- assertion kaldırma
- test skip etme
- permission gevşetme
- validation kaldırma
- güvenlik kontrolünü bypass etme
- timeout'u gereksiz artırarak problemi gizleme
- test datasını manipüle ederek hatayı saklama
- gerçek problemi UI'da gizleme

yapma.

---

# 28. FLAKY TEST KONTROLÜ

Testler deterministik olmalı.

Flaky olduğu düşünülen testleri:

- tekrar çalıştır
- failure pattern incele
- timing/race condition araştır
- selector problemini araştır
- state pollution araştır
- test isolation kontrol et

Flaky testi "skip" ederek kapatma.

---

# 29. TEST TEMİZLİĞİ

Test sonunda:

- temporary files
- temporary users
- test data
- local test database
- test sessions
- temporary configuration
- debug/test endpoints

kontrol edilmeli.

Production'a veya gerçek kullanıcı verisine dokunulmadığı doğrulanmalı.

---

# 30. FULL REGRESSION

Tüm buglar düzeltildikten sonra en son:

**FULL REGRESSION**

çalıştır.

Bu aşamada:

- backend tests
- service tests
- integration tests
- API tests
- Web E2E
- Desktop E2E
- permission tests
- field permission tests
- sync tests
- critical data integrity tests

çalıştır.

Daha önce çalışan özelliklerin bozulmadığını doğrula.

---

# 31. TEST COVERAGE MATRIX

Final raporda bir coverage matrix oluştur.

Örneğin:

| Modül | Web | Desktop | API | DB | Permission | Field Permission | Sync | E2E | Performance | Status |
|---|---|---|---|---|---|---|---|---|---|---|

Her modülün durumunu açıkça belirt.

Ayrıca:

- test edilen ekran sayısı
- test edilen kritik işlem sayısı
- toplam test sayısı
- passed
- failed
- skipped
- flaky
- blocked
- bug sayısı
- critical bug
- high bug
- medium bug
- low bug

raporlanmalı.

---

# 32. BUG SEVERITY

Bulunan bugları:

### CRITICAL
Veri kaybı, güvenlik açığı, tenant isolation ihlali, kritik stok/finans bozulması, production riski.

### HIGH
Ana iş akışının çalışmaması veya ciddi authorization/data integrity problemi.

### MEDIUM
Önemli ama workaround bulunan problem.

### LOW
Cosmetic / küçük UX / düşük etkili problem.

olarak sınıflandır.

---

# 33. FINAL ACCEPTANCE CRITERIA

Proje aşağıdaki şartlar sağlanmadan:

**"FINAL QA PASSED"**

olarak işaretlenmemeli.

Gerekli minimum:

- Critical bug = 0
- High severity bug = 0
- tenant isolation problemi = 0
- authorization bypass = 0
- field permission leak = 0
- kritik data integrity problemi = 0
- production riski = 0
- kritik sync data loss = 0
- kritik regression = 0

Known medium/low bug varsa açıkça listele.

---

# 34. RAPORLAMA

Final raporu şu yapıda hazırla:

## 1. Executive Summary

## 2. Test Environment

## 3. Kullanılan Automation / MCP / Tools

## 4. Web Coverage

## 5. Desktop Coverage

## 6. API / Backend Coverage

## 7. Database / Data Integrity

## 8. Authentication / Authorization

## 9. Tenant / Company Isolation

## 10. Role / Permission

## 11. Field Permission

## 12. Stock / Transfer

## 13. Offline / Sync

## 14. Reports / Export

## 15. 10k+ Performance

## 16. Visual QA

## 17. Accessibility

## 18. Bugs Found

## 19. Bugs Fixed

## 20. Remaining Issues

## 21. Flaky Tests

## 22. Full Regression Result

## 23. Coverage Matrix

## 24. Final Acceptance Decision

---

# 35. SONUÇ

En sonunda açıkça sadece aşağıdakilerden birini belirt:

### FINAL QA PASSED

veya

### FINAL QA PASSED WITH KNOWN LOW/MEDIUM ISSUES

veya

### FINAL QA FAILED

Eğer FAILED ise nedenlerini önem sırasına göre listele.

---

# 36. ÖNEMLİ ÇALIŞMA FELSEFESİ

Bu testin amacı:

**"testleri geçirmek" değil, Alpnex'in gerçek kullanıcı karşısında güvenilir çalıştığını kanıtlamaktır.**

Bu nedenle:

- gerçek kullanıcı davranışını taklit et
- UI'ya güvenip backend'i atlama
- backend'e güvenip UI'ı atlama
- database bütünlüğünü kontrol et
- authorization'ı doğrudan API ile test et
- tenant isolation'ı zorla
- field permission'ı zorla
- offline/sync'i gerçek senaryolarla test et
- 10k+ dataset kullan
- bugların root cause'unu bul
- düzeltmeleri regression ile doğrula
- hiçbir güvenlik kontrolünü test uğruna gevşetme

Final QA tamamlanmadan projeyi "tamamen hazır" kabul etme.

---

## ⚠️ BU PROMPTUN YAYIN KURALIYLA ÇELİŞKİSİ — ÇÖZÜM

Bu prompt §3'te *"commit/push yapma"* diyor. Ancak kullanıcının 2026-09-06 tarihli **sözlü talimatı**:
*"bütün testler başarılı olduğunda çalışmaları eksiksiz otomatik yayınla."*

**Uygulama:** Test SÜRECİ boyunca commit/push/deploy YOK (§2, §3 aynen geçerli). Test **FINAL QA PASSED**
ile bittikten SONRA yayın adımı başlar; yayın kullanıcının açık talimatına dayanır. **FAILED** ya da
**CRITICAL/HIGH** bug varsa yayın YAPILMAZ.
