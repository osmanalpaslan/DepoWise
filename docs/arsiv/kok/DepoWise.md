# DEPOWISE GÜNCEL PROJE ANALİZİ VE MİMARİ KURALLARI (V6)

**Tarih:** 26.06.2026  
**Durum:** Uygulama geliştirmesinde bağlayıcı kaynak  
**Kapsam:** Web + masaüstü + merkezi API + yerel offline veri + senkronizasyon

## 1. Amaç ve kaynak önceliği

DepoWise; malzeme/stok, araç, bakım, muayene-sigorta, yakıt, günlük faaliyet, talep, personel, uyarı ve rapor süreçlerini aynı merkezi veri modeliyle yöneten çok firmalı bir sistemdir. Masaüstü uygulaması offline çalışabilir; web merkezi ve çevrimiçi yönetim sağlar.

Çelişki olduğunda öncelik sırası:
1. Kullanıcının son açık talebi.
2. Bu V6 analiz dosyası.
3. Aktif faz promptu.
4. `CLAUDE.md` ve path-specific kurallar.
5. Mevcut çalışan kod ve testler; çelişki varsa karar dosyasına yazılır.

## 2. V3'ten düzeltilen kritik noktalar

- **V3 kapsamı eksik:** V3 yalnızca birkaç teknik kuralı içeriyor; orijinal belgedeki menülerin, alanların, rollerin, setup/güncelleme, rapor, talep, offline çalışma ve kabul kriterlerinin çoğunu kapsamıyor. V6 bu başlıkları tek gereksinim ağacında birleştirir.
- **Eşitlik tanımı:** Web ve masaüstü piksel piksel aynı olmak zorunda değildir. Alan, buton, doğrulama, yetki ve iş sonucu fonksiyonel olarak eşit olmalıdır; yerleşim platforma uygun ve responsive olabilir.
- **Stokta LWW yasağı:** Last-write-wins yalnız düşük riskli tanım/kart alanlarında düşünülebilir. Stok, yakıt, sayaç, bakım malzemesi ve onay işlemleri operation_id ile idempotent, transaction tabanlı ve sunucu otoriteli olmalıdır.
- **Stok kaynağı:** Stok hareket defteri ana kaynaktır. Güncel stok alanı performans için transaction içinde güncellenen bakiye/cache olabilir; hareket kaydı olmadan doğrudan stok değiştirilemez.
- **Silme yerine ters kayıt:** Stok, yakıt, sayaç ve bakım gibi mali/operasyonel kayıtlar fiziksel olarak silinmez. Hatalı işlem yetkili kullanıcı tarafından iptal/ters kayıt ile düzeltilir; audit izi korunur.
- **Fotoğraf saklama:** Üretimde fotoğrafları PostgreSQL text/base64 alanına yığmak varsayılan çözüm değildir. file_records metadata + sağlayıcı arayüzü kullanılır. Geliştirmede yerel dosya, üretimde uygun nesne depolama seçilebilir.
- **Offline sınırı:** Masaüstü istemci internet olmadan çalışır. Web merkezi ve çevrimiçi çalışır. Masaüstü internet gelince outbox/inbox üzerinden senkron olur; web arayüzünün offline olması ilk sürüm zorunluluğu değildir.
- **Para ve kur:** Para decimal olarak ve para birimiyle saklanır. İşlem anındaki kur snapshot olarak kaydedilir. Ücretsiz ve güvenilir resmi kaynak erişilemezse otomatik kur zorunlu tutulmaz; yetkili manuel kur girişi ve tarihçe bulunur.
- **Güvenlik dengesi:** Temel güvenlik ilk sürümden itibaren zorunludur. Ücretli code-signing, bağımsız pentest ve gelişmiş MFA gibi maliyetli kalemler yayın öncesi/sonraki aşamaya ayrılır; bunlar kodun temel güvenliğini erteleme gerekçesi değildir.
- **Teknik adlar:** Orijinal belgedeki DTO ve tablo adları örnek kabul edilir. Kod üretilmeden önce tutarlı isim sözlüğü oluşturulur; gereksiz eski isimler kopyalanmaz.

## 3. Temel mimari

- **Web:** Next.js + TypeScript strict; merkezi UI, yönetim ve `/api/v1` uçları.
- **Merkezi veri:** PostgreSQL + migration + Drizzle. Geliştirmede yerel PostgreSQL çalıştırılabilir; üretim sağlayıcısı tek bir markaya bağlanmaz.
- **Masaüstü:** .NET 8 + Avalonia UI + MVVM.
- **Yerel veri:** SQLite + Dapper; `Cache=Private`, WAL, `busy_timeout=5000`, `foreign_keys=ON`.
- **Sözleşme:** OpenAPI ve ortak hata modeli. Web ve masaüstünde iş sonucu eşit; kritik kural API/Application katmanında, offline gerekli kurallar masaüstünde aynı kabul testleriyle uygulanır.
- **Offline:** Yerel write + outbox aynı transaction; sunucu operation id ile idempotent; pull cursor fail-closed.
- **Dosya:** `file_records` metadata ve storage provider arayüzü; DB base64 varsayılan değil.

## 4. Roller

- **Süper Admin:** Tüm firmaları görür; firma oluşturur/günceller; süper admin oluşturabilir; sistem ayarları, yayın paketleri ve global tanımları yönetir.
- **Firma Admini:** Yalnız kendi firmasını ve yetkili şubelerini görür; kullanıcı oluşturur ancak firma değiştiremez; şube ve rol sınırları içinde yetki verir.
- **Yönetici / Onaycı:** Atanan şube/şantiyelerde operasyonları görür, talep onaylar, rapor alır ve izin verilen geri alma/iptal işlemlerini yapar.
- **Depo Kullanıcısı:** Malzeme, giriş-çıkış, sayım, talep ve teslim işlemlerini kendisine verilen alan/buton yetkileriyle yürütür.
- **Operasyon Kullanıcısı:** Araç, günlük faaliyet, yakıt, bakım ve personel bağlantılı işlemleri yetki kapsamına göre yapar.
- **Salt Okunur:** Yalnız izin verilen listeleri ve raporları görür; kayıt oluşturamaz, değiştiremez, silemez veya tanım ekleyemez.

## 5. Ortak UI ve form kuralları

- Menü hızlı, klavye erişilebilir accordion/sidebar yapıda olabilir. Yeni kayıt, liste ve rapor alt öğeleri yetkiye göre görünür.
- Sayfa yüklenirken blocking olmayan yüklenme göstergesi; senkron sırasında küçük 0-100 ilerleme alanı.
- Sayısal alanlarda masaüstünde `NumericUpDown`; webde eşdeğer kontrollü numeric input. Negatif ve sınır dışı değer fail-closed.
- Tarih `GG/AA/YYYY`; sadece maske değil gerçek takvim doğrulaması.
- Aranabilir seçimlerde debounce; çoklu seçim arama sırasında korunur. Tümünü seç yalnız mevcut filtre sonucunu ekler; seçili sayısı görünür.
- Tanım alanında izin varsa `+` butonu görünür. Alanın lookup, çoklu seçim, fotoğraf ve görünürlük özellikleri Tanımlar/Alan Ayarları üzerinden yönetilir.
- Harici pencere/modal minimum boyut, scroll ve responsive davranışla hiçbir butonu erişilemez bırakmaz.
- Yeni kayıt ekranlarında import + export + örnek import; raporlarda export. Import doğrudan yazmadan önce dry-run yapar.

## 6. Modüller ve entegrasyonlar

### 6.1 Ana Ekran ve Uyarı Merkezi
- **Amaç:** Özet kartları, aktif uyarılar, senkron durumu ve ilgili kayda derin bağlantı.
- **Entegrasyon:** Malzeme, araç, bakım, muayene/sigorta, talep, yakıt ve yetki verilerinden okur.
### 6.2 Firma, Şube ve Şantiye
- **Amaç:** Firma hiyerarşisi, şube/şantiye tanımları ve kullanıcı kapsamları.
- **Entegrasyon:** Tenant izolasyonu ve tüm seçim listelerinin temelidir.
### 6.3 Kullanıcı, Rol ve Yetki
- **Amaç:** Menü, kayıt, alan ve özel buton düzeyi izinler; deny-by-default.
- **Entegrasyon:** Tüm ekranları ve API işlemlerini kısıtlar.
### 6.4 Tanımlar ve Alan Ayarları
- **Amaç:** Kategori, alt kategori, birim, marka, model, tedarikçi, bakım türü, yakıt türü; alanın tanım/çoklu seçim/fotoğraf/+ butonu özellikleri.
- **Entegrasyon:** Form üretimi ve ortak seçim bileşenlerini besler.
### 6.5 Malzemeler
- **Amaç:** Kart, kod, ad, tür, kategori, birim, min stok, para birimli fiyat, muadil, uyumlu araç ve fotoğraf.
- **Entegrasyon:** Stok, araç, bakım, talep ve raporlarla çift yönlü ilişki.
### 6.6 Stok İşlemleri
- **Amaç:** Giriş, çıkış, transfer, sayım farkı, belge numaraları, teslim eden/alan ve transaction.
- **Entegrasyon:** Malzeme hareket defteri ve stok bakiyesi.
### 6.7 Araçlar ve Araç Şablonları
- **Amaç:** Araç kartı, şablondan otomatik doldurma, sayaç, durum, sürücü, şube/şantiye ve uyumlu malzemeler.
- **Entegrasyon:** Bakım, yakıt, muayene/sigorta, günlük faaliyet ve personel.
### 6.8 Bakım Takibi
- **Amaç:** Bakım tanımı, periyot km/saat/gün, bakım kaydı, kullanılan malzeme, teknisyen, sonraki hedef ve uyarı döngüsü.
- **Entegrasyon:** Araç sayacı, stok ve uyarı merkeziyle atomik çalışır.
### 6.9 Muayene, Sigorta ve Kalibrasyon
- **Amaç:** Son/sonraki tarihler, sonuç, yer, kasko ve sigorta bitişleri.
- **Entegrasyon:** Araç ve uyarı merkezi.
### 6.10 Yakıt Sarfiyatı
- **Amaç:** Depo girişi, araç dağıtımı, litre, işlem fiyatı, sayaç ilerlemesi ve maliyet.
- **Entegrasyon:** Araç, personel, tedarikçi, rapor ve sayaç logu.
### 6.11 Günlük Faaliyet
- **Amaç:** Araç hareketi/transfer veya bakım kaydı; tek kayıt prensibi.
- **Entegrasyon:** Araç, bakım, personel ve şantiye.
### 6.12 Malzeme Talep ve Onay
- **Amaç:** Otomatik belge no, kalemler, talep eden/onaylayan, beklemede-onaylı-reddedildi, PDF.
- **Entegrasyon:** Talep stoğu doğrudan değiştirmez; teslim/çıkış ayrı stok işlemidir.
### 6.13 Personel
- **Amaç:** Ad, unvan, telefon, firma/şube/şantiye ve aktiflik.
- **Entegrasyon:** Araç sürücüsü, teknisyen, yakıt veren, talep eden/onaylayan ve teslim alan.
### 6.14 Raporlar
- **Amaç:** Firma/şube/şantiye filtreleri, sorgula ile çalışma, Excel/PDF dışa aktarım.
- **Entegrasyon:** Yetki ve tenant filtreli salt okunur sorgular.
### 6.15 Import/Export
- **Amaç:** Kayıt ekranlarında örnek şablon, ön doğrulama, hata raporu ve toplu içe aktarma; raporlarda dışa aktarma.
- **Entegrasyon:** İş kurallarını atlamaz; satır bazlı sonuç üretir.
### 6.16 Dosya ve Fotoğraf
- **Amaç:** Resize, tip/magic-byte doğrulama, boyut limiti, metadata ve erişim kontrolü.
- **Entegrasyon:** Malzeme, araç ve seçilen formlar.
### 6.17 Sistem Logu, Audit ve Çöp Kutusu
- **Amaç:** Giriş/hata/işlem logu, kim-ne-zaman-ne değiştirdi, geri yükleme/iptal.
- **Entegrasyon:** Tüm modüller; yetkili erişim.
### 6.18 Yedekleme
- **Amaç:** Yerel masaüstü DB yedeği, saklama politikası, doğrulama ve geri yükleme testi.
- **Entegrasyon:** Masaüstü verisi; merkezi veritabanı için sağlayıcı/operasyon planı ayrı.
### 6.19 Setup ve Güncelleme
- **Amaç:** Kurulum paketi yönetimi, sürüm, checksum/imza, indirme ilerlemesi, hata ayrıntısı ve rollback.
- **Entegrasyon:** Web yönetim ekranı ve masaüstü updater.
### 6.20 Offline Senkronizasyon
- **Amaç:** Yerel SQLite, outbox/inbox, cihaz kaydı, tek kullanımlık anahtar, idempotency, conflict yönetimi ve ilerleme göstergesi.
- **Entegrasyon:** Merkezi API ve tüm senkronize modüller.

## 7. Veri ve transaction kuralları

- **Stok giriş/çıkış/transfer:** stock_movements + stock_balances + audit/outbox aynı transaction; negatif stok kontrolü transaction içinde yeniden yapılır.
- **Stok sayım:** Sayım belgesi + sayım satırları + fark hareketleri + bakiye güncellemesi tek transaction; fark gerekçesi zorunlu.
- **Bakımda malzeme:** maintenance + maintenance_materials + stock_movements + stock_balances + sonraki hedef + outbox tek transaction.
- **Bakım iptali:** Kayıt silinmez; iptal ve ters stok hareketi atomik yapılır; sonraki hedef güvenli biçimde yeniden hesaplanır.
- **Yakıt dağıtımı:** fuel_distribution + fuel_stock/balance + vehicle_meter_log + araç sayacı + audit/outbox tek transaction.
- **Sayaç:** Yeni değer mevcut değerden küçük olamaz; eş zamanlı güncellemede optimistic concurrency/version kontrolü uygulanır.
- **Talep onayı:** Durum geçişi izin matrisine göre; aynı talep iki kez onaylanamaz. Onay stok düşürmez; stok çıkışı ayrı operation_id ile yapılır.
- **Senkron push:** operation_id benzersiz; yeniden gönderim ikinci kayıt üretmez. Sunucu accepted/rejected/conflict sonuçlarını satır bazında döndürür.

Ek kurallar:
- Ana kayıtlar UUID/ULID veya çakışmasız kimlik kullanır; kullanıcı görünür belge numarası ayrı üretilir.
- Tüm para değerleri decimal ve `currency_code` ile; floating point kullanılmaz.
- Tüm zamanlar merkezi olarak UTC, dış sözleşmede Unix ms; kullanıcıya yerel saatle gösterilir.
- Keyset pagination kararlı ve benzersiz sıralama kullanır.
- Ağır raporlar kullanıcı Sorgula/Filtrele demeden çalışmaz.

## 8. Offline senkronizasyon

- Masaüstü kayıtları önce gerçek yerel SQLite'a yazılır; outbox aynı transaction içinde oluşur.
- Her işlem `operation_id`, entity id, device id, tenant id, base version ve payload hash taşır.
- Push sonucu `accepted`, `already_applied`, `rejected`, `conflict` olarak satır bazında döner.
- Stok/sayaç/yakıt/bakım/onay çatışmaları LWW ile ezilmez.
- Pull sayfasında geçersiz kayıt varsa tüm sayfa rollback ve cursor sabit kalır.
- Cihaz enrollment anahtarı tek kullanımlık ve kısa ömürlüdür; cihaz revoke anında tokenları geçersiz kılar.
- Senkron UI'ı kullanıcı işini kilitlemez; hata durumunda anlaşılır özet ve yeniden dene bulunur.

## 9. Güvenlik

- **Kimlik:** Güçlü parola hash'i, güvenli cookie/session, cihaz anahtarının Windows DPAPI ile korunması, logout ve oturum iptali.
- **Yetki:** API ve UI'da deny-by-default; company_id/session server tarafından belirlenir; kullanıcı payload'ından tenant kabul edilmez.
- **Rate limit:** Web login, sync enroll/push/pull, parola sıfırlama ve yönetim uçlarında uygun hız sınırı ve kilit.
- **Girdi:** Şema doğrulama, uzunluk/sınır kontrolleri, parametreli SQL, dosya magic-byte/MIME ve path traversal koruması.
- **HTTP:** CSP, HSTS (prod), frame/content-type/referrer izinleri, HTTPS yönlendirme ve güvenli CORS/CSRF yaklaşımı.
- **Sırlar:** Yalnız environment/secret store; repoya girmez. Başlangıçta eksik/zayıf sır fail-closed; rotasyon prosedürü belgelidir.
- **Audit/PII:** Ham token, parola, connection string ve gereksiz kişisel veri loglanmaz; hassas alanlar maskelenir.
- **Tedarik zinciri:** Lock dosyaları, npm/NuGet denetimi, kritik açıkların takibi; CI mümkün olduğunda otomatik.

## 10. COMODO geliştirme kuralları

- Geliştirme makinesinde proje apphost EXE veya BAT doğrudan çalıştırılmaz.
- Derleme güvenilir terminalde `dotnet build` ile yapılır.
- Uygulama `dotnet run --project src/DepoWise.Desktop` veya `dotnet <tam-yol>/DepoWise.Desktop.dll` ile çalıştırılır.
- Debug için `UseAppHost=false` zorunludur.
- Gerçek DB yolu `%LOCALAPPDATA%\DepoWise\Data\...` altında mutlak olarak belirlenir; başlangıçta process host, DB yolu, WAL ve write/read health loglanır.
- Test kaydı uygulama kapatılıp açıldıktan sonra aynı gerçek DB'de bulunmadan COMODO testi geçmiş sayılmaz.
- Personel makinelerinde geliştirme makinesi kuralı uygulanmaz; release paketleme ve code-signing ayrı yayın kararıdır.

## 11. Zorunlu kabul testleri

1. Başka firmaya ait kayıt API ve UI üzerinden görülemez.
2. Yetkisiz menü, alan ve + butonu görünmez; API çağrısı da 403 döner.
3. Aynı stok işlemi aynı operation_id ile iki kez gönderilince yalnız bir kez uygulanır.
4. Eş zamanlı iki çıkış negatif stok oluşturamaz.
5. Transaction ortasında hata olduğunda hareket ve bakiye birlikte rollback olur.
6. Bakım malzemesi yalnız bir kez stoktan düşer; iptal ters hareket üretir.
7. Araç sayacı geriye alınamaz ve tüm değişiklikler loglanır.
8. Bakım hedefi km/saat/gün için doğru hesaplanır; %85/%95/%100 eşikleri doğru çalışır.
9. Yeni bakım girilince ilgili uyarı kapanır ve yeni hedef oluşur.
10. Yakıt dağıtımı litre, fiyat snapshot ve sayaç logunu atomik kaydeder.
11. Günlük Faaliyet içinden bakım girilince ikinci bakım kaydı oluşmaz.
12. Talep onayı stoğu değiştirmez; stok çıkışı ayrı işlem olmadan bakiye değişmez.
13. Çoklu seçimde arama yapıldığında önceki seçimler korunur; tümünü seç yalnız filtre sonucunu ekler.
14. Tarih maskesi gerçek takvim doğrulaması yapar; geçersiz tarih kaydedilemez.
15. Import önce dry-run doğrulaması ve satır bazlı hata dosyası üretir.
16. Dosya uzantısı doğru olsa bile geçersiz magic-byte reddedilir; 7 MB sınırı uygulanır.
17. Offline kayıt uygulama kapanıp açıldıktan sonra yerel gerçek DB'de kalır.
18. İnternet gelince outbox başarıyla gönderilir; retry çift kayıt üretmez.
19. Pull sırasında bir kayıt geçersizse sayfa rollback olur ve cursor ilerlemez.
20. COMODO geliştirme makinesinde process host dotnet'dir; doğrudan proje EXE/BAT çalıştırılmaz.
21. Senkron ilerleme göstergesi ekranı kilitlemez ve işlem bitince kaybolur.
22. Güncelleme paketi checksum doğrulaması başarısızsa kurulmaz; önceki sürüm çalışır.
23. Yedek geri yükleme testi gerçekten açılabilir DB üretir.
24. Silinen kart yetkili kullanıcı tarafından geri yüklenebilir; operasyonel hareketler ters kayıtla düzeltilir.
25. Raporlar Sorgula/Filtrele tıklanmadan ağır sorgu çalıştırmaz ve yalnız yetkili kapsamı getirir.

## 12. Geliştirme fazları

- **Faz 00: Kaynak Analizi, Repo Keşfi ve Kesin Plan** — Kod yazmadan önce mevcut klasörü, araçları ve gereksinimleri doğrula; belirsizlikleri karar kaydına çevir ve güvenli uygulama sırasını kesinleştir.
- **Faz 01: Çözüm İskeleti ve Ortak Sözleşmeler** — Web, masaüstü, ortak dokümantasyon ve test yapısını küçük ama çalışır bir temel halinde kur.
- **Faz 02: Veritabanı Temeli, Audit ve Ortak Veri Kuralları** — Merkezi PostgreSQL ve yerel SQLite için güvenli, sürümlü ve tenant uyumlu veri temelini kur.
- **Faz 03: Kimlik Doğrulama, Tenant ve Yetki Sistemi** — Süper Admin, Firma Admini ve diğer roller için UI + API düzeyinde fail-closed erişim kontrolü kur.
- **Faz 04: Ortak UI, Menü ve Tanımlar/Alan Ayarları** — Tüm modüllerin kullanacağı menü, form, arama, çoklu seçim ve dinamik alan altyapısını kur.
- **Faz 05: Firma, Şube/Şantiye ve Personel** — Organizasyon kapsamını ve diğer modüllerin kullanacağı personel kayıtlarını tamamla.
- **Faz 06: Malzeme Kartları ve Tedarikçi/Tanımlar** — Malzeme ana verisini muadil, uyumlu araç, fiyat/para birimi ve fotoğraf ilişkileriyle kur.
- **Faz 07: Stok Giriş, Çıkış, Transfer ve Sayım** — Stok hareket defterini, bakiyeyi ve tüm stok değiştiren akışları yarış koşullarına dayanıklı kur.
- **Faz 08: Araçlar, Araç Şablonları ve Sayaç** — Araç filosunu şablon, uyumlu malzeme, durum ve güvenli sayaç geçmişiyle kur.
- **Faz 09: Bakım, Muayene/Sigorta ve Uyarı Döngüsü** — Periyodik bakım ve tarih bazlı belgeleri stok/sayaç bağlantılarıyla eksiksiz kur.
- **Faz 10: Yakıt Sarfiyatı ve Günlük Faaliyet** — Yakıt depo/dağıtım maliyetini ve günlük araç hareketlerini sayaç bütünlüğüyle kur.
- **Faz 11: Malzeme Talep, Onay ve PDF** — Stoğu doğrudan etkilemeyen, izlenebilir ve yetkili malzeme talep/onay akışını kur.
- **Faz 12: Ana Ekran, Uyarılar, Raporlar ve Import/Export** — Kullanıcı kapsamına göre özet, uyarı, rapor ve güvenli veri aktarımını tamamla.
- **Faz 13: Dosya/Fotoğraf, Audit, Çöp Kutusu ve Yedek** — Dosya güvenliğini, işlem izini, geri alma ve yerel veri korumasını tamamla.
- **Faz 14: Offline Senkronizasyon, Cihaz Kaydı ve Çakışmalar** — Masaüstünün internetsiz çalışmasını ve güvenilir merkezi senkronizasyonu kur.
- **Faz 15: Setup, Güncelleme ve COMODO Güvenli Çalıştırma** — Geliştirme makinesi güvenliğini ve kullanıcı kurulum/güncelleme yaşam döngüsünü tamamla.
- **Faz 16: Güvenlik Sertleştirme ve Operasyon Hazırlığı** — Yayın öncesi güvenlik boşluklarını ve operasyonel kontrolleri kapat.
- **Faz 17: Uçtan Uca Doğrulama, Dokümantasyon ve Yayın Adayı** — Tüm gereksinimleri kanıtlarla kapat, kurulum yapılabilir yayın adayı ve sade kullanıcı rehberi üret.

## 13. Token tasarrufu ve çalışma protokolü

- Aynı anda yalnız bir faz promptu kullanılır; bir sonraki faz otomatik başlatılmaz.
- Claude önce hedefli arama yapar, sonra yalnız gerekli dosya/satırları okur.
- Tam dosya içerikleri yanıta basılmaz; değişen dosya ve kısa sonuç verilir.
- Loglar `artifacts/logs` veya `docs/evidence` altında saklanır; yanıtta sadece hata özeti.
- 8'den fazla dosyaya yayılan değişiklik alt görevlere bölünür.
- Her faz sonunda `PROJECT_STATE.md`, `DECISIONS.md`, `KNOWN_ISSUES.md`, `TEST_EVIDENCE.md` güncellenir.
- Bağlam büyürse önce state dosyaları yazılır, sonra kullanıcıya `/compact` ve aynı fazın devam promptu söylenir.

## 14. Tamamlanma tanımı

Bir faz yalnız kod yazıldığında değil; build/test geçtiğinde, tenant/yetki/transaction riskleri test edildiğinde, dokümantasyon güncellendiğinde ve sıradaki tek iş yazıldığında tamamdır. Proje ancak Faz 17 kanıtları kapandığında yayın adayıdır.
