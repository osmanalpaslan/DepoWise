# FAZ K — UÇTAN UCA DOĞRULAMA VE ONARIM (`UUD-01`)

> **Durum:** ⏳ BEKLİYOR — `FAZ J` bittikten sonra çalışır · **eklendi 2026-09-04 (kullanıcı promptu)**
>
> **Bu bir "test raporu yaz" görevi DEĞİLDİR.** Amaç: yapılan tüm ekran ve özellikleri gerçek bir
> Alpnex kullanıcısı gibi kullanmak, görünen ve görünmeyen tüm katmanları doğrulamak, hataları
> **kendi bulmak**, kök nedenini araştırmak, güvenli ve mimariye uygun düzeltmek, düzelttikten sonra
> **tekrar test etmek** ve ancak gerçekten doğrulandığında "tamam" demek.
>
> Kullanıcı yazılımcı değildir. Bu yüzden yalnız onun aklına gelebilecek yüzeysel kontrollerle
> (alan · buton · ekran · ekle · sil · listele) **yetinilmeyecek**. Bir yazılım mühendisi, QA
> mühendisi, ürün sahibi, gerçek depo/şantiye kullanıcısı ve uygulamayı normalden yoğun kullanan
> bir kullanıcı gibi düşünülecek.

---

## 0. Sıra ve bitiş

`FAZ J` biter → **`FAZ K` çalışır** → tek yayın → bilgisayar uykuya alınır.
Yayın `K` bitmeden yapılmaz: doğrulanmamış kod canlıya çıkmaz.

## 1. Temel kural — "çalışıyor gibi görünüyor" yeterli değil

Bir özellik ancak şu **34 katmanın tamamı** doğrulandığında başarılı sayılır; biri atlanmaz:

UI · UI state yönetimi · validation · API · backend/service · authorization ·
tenant/company izolasyonu · database · transaction davranışı · foreign key/ilişkiler ·
listeleme · filtreleme · sıralama · pagination/sanallaştırma · create · read · update ·
delete/cancel · duplicate kayıt davranışı · empty state · loading state · error state ·
network failure · retry davranışı · concurrent kullanım · masaüstü davranışı · web davranışı ·
senkronizasyon (kapsama giriyorsa) · audit/log · performans · büyük veri · güvenlik ·
kullanıcı deneyimi · gerçek iş akışının baştan sona tamamlanabilmesi.

## 2. Önce projeyi ve yapılan işi analiz et

Testlere rastgele başlanmaz. Önce incelenir: mimari · ilgili ekranlar ·
ViewModel/component/service/controller/API uçları · DB tabloları ve migration'lar · mevcut testler ·
authorization sistemi · tenant/company izolasyon kuralları · sync kuralları · validation kuralları ·
UI tasarım kuralları · `CLAUDE.md` ve proje kuralları · ilgili ADR'ler · daha önce yapılmış benzer ekranlar.

**Aynı iş için projede zaten kullanılan bir pattern varsa yeni ve farklı bir pattern icat edilmez.**
Özellikle: aynı tipteki eski ekranlarla davranış karşılaştırması · aynı veri tipini kullanan
ekranlarla validation karşılaştırması · mevcut API/service güvenlik yaklaşımı · mevcut hata yönetimi ·
mevcut loading/empty/error state standartları takip edilir.

Önce şu sorunun cevabı çıkarılır: *"Bu özellik bu projede nasıl yapılmalı?"*

## 3. Test matrisi

Tüm yeni/değişen ekran ve özellikler tek tek çıkarılır. Her ekran için en az şu senaryolar
değerlendirilir:

ekran açılışı · ilk yükleme · boş veri · tek kayıt · çok kayıt · 10.000+ kayıt · arama · filtre ·
sıralama · kayıt ekleme · düzenleme · görüntüleme · silme/cancel · modal/dialog · seçim kontrolleri ·
dropdown · autocomplete · tarih alanları · sayı alanları · para alanları · metin alanları ·
zorunlu alanlar · opsiyonel alanlar · uzun metin · minimum değer · maksimum değer · sınır değer ·
geçersiz değer · boş değer · duplicate değer · yanlış format · yetkisiz kullanıcı ·
başka company verisi · network hatası · backend hatası · tekrar deneme · hızlı ardışık tıklama ·
sayfa/ekran değiştirme · refresh · uygulamayı kapatıp açma · veri kalıcılığı.

## 4. Gerçek kullanıcı simülasyonu

Testler yalnız API üzerinden yapılmaz; mümkün olduğunca gerçek kullanıcı davranışı taklit edilir:

1. Uygulamayı aç → 2. menüye git → 3. ekranı aç → 4. listeyi incele → 5. yeni kayıt oluştur →
6. alanları tek tek doldur → 7. yanlış veri gir → 8. validation mesajını kontrol et →
9. doğru veriyi gir → 10. kaydet → 11. kaydın listede göründüğünü doğrula → 12. arama yap →
13. filtrele → 14. kaydı aç → 15. düzenle → 16. kaydet → 17. tekrar aç →
18. verinin gerçekten değiştiğini doğrula → 19. ilgili başka ekranlardan bu veriyi kontrol et →
20. iş akışının sonraki aşamasına geç → 21. DB/API tarafında beklenen durumun oluştuğunu doğrula.

**"Butona bastım, hata vermedi" bir test değildir.** Kullanıcı açısından işin gerçekten
tamamlandığı kanıtlanır.

## 5. Her alan için test

Her input/kontrol için ayrı ayrı: boş bırakılabiliyor mu · boş bırakılması gerekiyorsa
bırakılabiliyor mu · zorunluysa doğru uyarı çıkıyor mu · sayı alanı metin kabul ediyor mu ·
negatif kabul etmemesi gereken alan ediyor mu · 0 kabul edilmeli mi · çok büyük sayı ·
decimal davranışı · geçersiz tarih · başlangıç > bitiş olabiliyor mu · karakter limiti ·
**limit backend'de de korunuyor mu** · yalnız UI validation'a mı güvenilmiş · Türkçe karakterler ·
özel karakterler · Unicode · baştaki/sondaki boşluk · duplicate veri · copy/paste ·
klavye ile kullanım · tab sırası · hata mesajı anlaşılır mı · doğru alanı işaret ediyor mu.

**Bir alan UI'da doğru görünse bile backend'in aynı kuralı uyguladığı doğrulanır.**

## 6. Her buton için test

Tüm kontroller tek tek çıkarılıp kullanılır: Kaydet · Güncelle · Sil · İptal · Geri · İleri ·
Yenile · Ara · Filtrele · Temizle · Ekle · Düzenle · Görüntüle · Onayla · Reddet · Transfer ·
İçe/Dışa aktarma · modal açma/kapatma · sekme değiştirme · sayfalama · sıralama.

Her buton için: normal tıklama · hızlı çift tıklama · çoklu tıklama · loading sırasında tıklama ·
backend yavaşken tıklama · hata sonrasında tıklama · yetkisiz durumda tıklama.

**Çift tıklama sonucu duplicate kayıt oluşmamalı.**

## 7. 10.000+ kayıt testi — ZORUNLU

Etkilenen her liste ekranı en az 10.000 kayıt varmış gibi test edilir.
**Gerçek production verisi KULLANILMAZ**; test verisi güvenli bir test ortamında üretilir
(gerekirse seed/generator yazılır).

Ölçülecekler: ekran açılış süresi · ilk veri yükleme · scroll · arama · filtreleme · sıralama ·
pagination · kayıt açma/düzenleme/silme · toplu işlemler · dropdown/autocomplete · modal açılışı ·
veri yenileme.

Mimaride server-side pagination/filtering varsa **zorla kullanılır**. 10.000 kaydı tek seferde
istemciye çekmek gerekiyorsa performans ve RAM etkisi ölçülür.
Kademeler: **10 · 100 · 1.000 · 5.000 · 10.000 · 25.000**.

Kullanılabilirliği bozan belirgin performans problemi **hata sayılır**. Ancak rastgele bir
"milisaniye hedefi" uydurulmaz — mevcut proje standartları, gerçek ölçümler ve mevcut ekranlarla
karşılaştırma esas alınır.

## 8. Database doğrulaması

UI testinden **sonra** DB kontrol edilir.

- **CREATE:** doğru kayıt oluştu mu · doğru `company_id` · FK'ler doğru mu · default değerler · audit alanları
- **READ:** doğru kayıt geliyor mu · başka company kaydı görünmüyor mu
- **UPDATE:** yalnız değişmesi gereken alanlar değişiyor mu · başka alanlar yanlışlıkla overwrite ediliyor mu
- **DELETE/CANCEL:** beklenen soft/hard delete uygulanıyor mu · orphan kayıt oluşuyor mu · FK ilişkileri bozuluyor mu
- **Transaction:** işlem yarıda kalırsa kısmi durum kalıyor mu · hata durumunda rollback gerçekleşiyor mu
- **Duplicate:** aynı işlem iki kez çalışırsa duplicate veri oluşuyor mu

## 9. Tenant / company güvenliği — ZORUNLU

Bir company'nin kullanıcısı başka company'nin kayıtlarını **görememeli · okuyamamalı ·
güncelleyememeli · silememeli · ID tahmin ederek erişememeli · API üzerinden erişememeli**.

**UI gizlemesi güvenlik kanıtı değildir** — API doğrudan test edilir: Company A kaydının ID'si ile
Company B kullanıcısından `GET`/`PUT`/`DELETE` denenir. IDOR/BOLA benzeri açıklar kontrol edilir.
**Güvenlik testlerinde production'a kesinlikle dokunulmaz.**

## 10. Yetki testleri

Mevcut permission/role sistemi incelenir. Her yeni işlem için: yetkili kullanıcı · yetkisiz kullanıcı ·
yalnız görüntüleme · düzenleme · silme · admin · normal kullanıcı kombinasyonları test edilir.

**UI'da butonun gizlenmesi yeterli değildir**; API/service tarafında da yetki kontrolü olmalıdır.

## 11. API testleri

Her yeni uç için: doğru request · eksik field · null · yanlış tip · yanlış ID · bulunmayan ID ·
duplicate request · yetkisiz request · başka company ID · aşırı uzun string · sınır değer ·
invalid enum · invalid date · malformed request.

HTTP durum kodlarının **mevcut proje standardına** uygunluğu ve response modeli kontrol edilir.
Sunucu hata verdiğinde istemci tarafının düzgün davrandığı doğrulanır.

## 12. Concurrency / double submit

Kaydet'e iki kez basma · Güncelle'ye iki kez basma · Sil'e iki kez basma · aynı kaydı iki
istemciden güncelleme · aynı işlemi eşzamanlı başlatma.

Sonuçta duplicate kayıt · çift stok hareketi · çift audit kaydı · bozuk state · race condition ·
beklenmeyen exception oluşuyor mu kontrol edilir.

## 13. Network / failure testleri

İnternet yok · API erişilemiyor · API timeout · 500 · 400 · 401 · 403 · 404 · bağlantı kısa süre
kesiliyor · bağlantı geri geliyor.

Kullanıcıya: anlaşılır hata · uygulamanın kilitlenmemesi · kaybolmayan veri ·
**yanlış "başarılı" mesajı vermeme** · tekrar deneme imkânı sağlanıyor mu.

## 14. Masaüstü testleri

Masaüstü ana/yoğun kullanılan istemcilerden biri olduğu için özel dikkat: pencere açılışı · boyut ·
resize · maximize · minimize · farklı çözünürlük · DPI/scaling · uzun listeler · scrollbar · klavye ·
mouse · tab navigation · dialog · modal · loading · hata · veri kaydetme · kapatma · tekrar açma.

UI elemanlarının üst üste binmediği · metinlerin kesilmediği · butonların görünür olduğu kontrol edilir.

## 15. Web testleri

login · navigation · route · browser refresh · back/forward · masaüstü tarayıcı · farklı ekran
genişlikleri · responsive davranış · modal · dropdown · table · filtreler · pagination · validation ·
API error · unauthorized state. Mümkün olan yerlerde **gerçek tarayıcı davranışıyla** test edilir.

## 16. Offline / sync

Özellik sync kapsamındaysa: **ONLINE** kayıt oluştur → sync et → sunucuya ulaştığını doğrula.
**OFFLINE** kayıt oluştur/değiştir → uygulamayı kullanmaya devam et → bağlantı gelsin → sync çalışsın.

Özellikle: duplicate · conflict · FK sırası · parent-child · silinen kayıt · güncellenen kayıt ·
farklı company · tekrar sync.

**Kapsama girmiyorsa açıkça belirtilir ve gereksiz test üretilmez.**

## 17. UI / tasarım / UX denetimi

Mevcut Alpnex tasarım sistemiyle uyum: spacing · typography · button hierarchy · form hierarchy ·
alignment · consistency · empty state · loading state · error state · visual hierarchy ·
table density · responsive layout · accessibility · keyboard navigation.

**Yeni ekran, eski ekranlardan farklı bir uygulama gibi görünmemeli.**
Gereksiz animasyon kullanılmaz. Animasyon varsa: kısa · işlevsel · performans dostu · kullanıcıyı
bekletmeyen · reduced-motion yaklaşımına uygun. **10.000+ kayıtlı listelerde satır bazlı ağır
animasyon kullanılmaz.**

## 18. Setup / installer kapsamı

Çalışma Setup/Installer'a dokunuyorsa: temiz kurulum · tekrar kurulum · güncelleme · bozuk indirme ·
checksum hatası · network hatası · yarım indirme · retry · resume · disk yetersizliği · yazma izni
problemi · mevcut sürüm · yeni sürüm · aynı sürüm.

Mevcut Setup güvenlik mimarisi bozulmaz — **SHA-256 doğrulaması fail-closed kalır.**

## 19. Log / audit

Audit oluşması gerekiyorsa oluşuyor mu · yanlış audit oluşuyor mu · duplicate audit ·
kullanıcı/company bilgisi doğru mu · hassas bilgi loglanıyor mu.

**Şifre, token, connection string, secret loglara kesinlikle yazılmamalı.**

## 20. Performans

"Çalışıyor" denmez, **ölçülür**: ekran açılışı · API response · DB query · listeleme · arama ·
filtre · sıralama · save · update · delete.

Yavaş yerlerde önce kök neden bulunur: N+1 query · gereksiz DB sorgusu · tüm tabloyu belleğe alma ·
gereksiz re-render · gereksiz API çağrısı · yanlış pagination · eksik index · gereksiz JOIN ·
büyük response · UI thread bloklama. **Sadece timeout artırarak problem çözülmez.**

## 21. Hata bulunduğunda uygulama kuralı

Rastgele kod değiştirilmez. Sırayla: (1) problemi yeniden üret → (2) hangi katmanda oluştuğunu
belirle → (3) kök nedeni bul → (4) mevcut proje pattern'ini incele → (5) en küçük güvenli düzeltmeyi
planla → (6) düzelt → (7) ilgili testleri çalıştır → (8) regresyon testi yap →
(9) **aynı hatanın başka yerde olup olmadığını ara**.

## 22. Hataları önceliklendir

- **P0 — Kritik:** veri kaybı · güvenlik açığı · tenant izolasyonu ihlali · ciddi DB bozulması · production benzeri kritik risk
- **P1 — Yüksek:** ana iş akışı çalışmıyor · kayıt kaybı · yanlış hesaplama · ciddi performans · önemli API problemi
- **P2 — Orta:** belirli senaryoda hata · UX problemi · validation problemi · edge case
- **P3 — Düşük:** kozmetik · küçük UX iyileştirmesi · düşük etkili tutarsızlık

P0/P1 mümkünse aynı çalışma içinde düzeltilir. P2/P3'te de düzeltmenin kapsam ve riski değerlendirilir.
**Sırf uzun bir hata listesi çıksın diye sorunlar bırakılmaz.**

## 23. Kendi düzeltmelerini tekrar test et

Düzeltmeden sonra: ilgili test · ilgili ekran · ilgili API · ilgili DB · ilgili regresyon senaryosu
yeniden çalıştırılır. **Bir test yeşil diye iş tamamlanmış varsayılmaz.**

## 24. Regresyon testi

Yeni ekranların mevcut çalışan özellikleri bozmadığından emin olunur; özellikle ortak servislerde:
eski ekranlar · eski API'ler · eski DB işlemleri · araç işlemleri · stok · bakım · talep ·
kullanıcı/yetki · sync.

**"Yeni özellik çalışıyor ama eski özellik bozuldu" kabul edilemez.**

## 25. Test verisi ve temizlik

Test verisi **production'a kesinlikle yazılmaz**. Test ortamında üretilen 10.000+/25.000+ veri ·
seed · geçici dosyalar · cache · test DB iş bitince güvenli şekilde temizlenir.
Mevcut kullanıcı verileri ve takip dışı kullanıcı dosyaları **silinmez**.
Temizlikten önce neyin test verisi olduğu kesin olarak belirlenir.

## 26. Testlerin kendisini de doğrula

**KESİNLİKLE YAPILMAZ:** assertion kaldırma · assertion zayıflatma · test skip ekleme · guard
gevşetme · mevcut testi silme · sadece testi yeşile çevirmek için production kodunu anlamsız değiştirme.

Bir test gerçek problemi ortaya çıkarıyorsa önce problemin kendisi araştırılır.
**Testi susturmak çözüm değildir.**

## 27. Build ve tam test

Debug build · Release build · unit · integration · API · DB · PostgreSQL · SQLite · masaüstü · web
testleri, mevcut test altyapısının izin verdiği ölçüde çalıştırılır.

**Build komutlarının exit code'u gerçekten kontrol edilir.** Pipeline çıktısı `tail`/`grep` gibi pipe
kullanımıyla yanlışlıkla yeşile çevrilmez. "0 hata" demeden önce gerçekten yeni build üretildiği
doğrulanır.

## 28. MCP / skill kullanımı

Mevcut proje politikasına uyulur. **Serena:** kod arama · sembol/referans analizi · mimari keşif
(salt okuma; değişiklik Claude'un kendi araçlarıyla). **frontend-design:** yeni/iyileştirilen UI.
**alpnex-arayuz-hareket:** gerçekten gerekli animasyon/geçişlerde. **design:** tasarım sistemi /
accessibility / design critique.

**Context7 ve Playwright proje politikasında kapalıysa kendiliğinden açılmaz.**
Bir MCP gerekiyorsa politikaya uyulur ve neden gerektiği belirtilir.

## 29. Otomatik olarak düzeltilebilecek her şeyi düzelt

Bulunan ve güvenle düzeltilebilecek sorunlar **yalnızca raporlanmaz, düzeltilir.**
Ancak kapsam dışı büyük mimari değişiklik · migration riski · production davranışını değiştiren büyük
değişiklik · geri dönüşü zor değişiklik gerekiyorsa **önce durulur ve raporlanır**.
Küçük/orta ve güvenli düzeltmeler için ayrıca izin beklenmez.

## 30. "Çalışıyor" kararı için kabul kriterleri

Bir ekran/özellik ancak şunların tamamı sağlandığında "tamamlandı" sayılır:

- [ ] UI çalışıyor
- [ ] tüm alanlar test edildi
- [ ] tüm butonlar test edildi
- [ ] validation test edildi
- [ ] CRUD test edildi
- [ ] API test edildi
- [ ] DB doğrulandı
- [ ] authorization test edildi
- [ ] company isolation test edildi
- [ ] duplicate davranışı test edildi
- [ ] error state test edildi
- [ ] network failure test edildi
- [ ] loading state test edildi
- [ ] empty state test edildi
- [ ] 10.000+ kayıt testi yapıldı
- [ ] performans değerlendirildi
- [ ] masaüstü test edildi
- [ ] web test edildi
- [ ] gerekiyorsa sync test edildi
- [ ] audit/log kontrol edildi
- [ ] regresyon test edildi
- [ ] ilgili hatalar düzeltildi
- [ ] düzeltmeler tekrar test edildi
- [ ] build başarılı
- [ ] ilgili testler başarılı

## 31. Son aşama — tam kullanıcı senaryosu

Tüm teknik testlerden sonra, **hiçbir teknik test çalıştırmıyormuş gibi**, uygulama gerçek bir
kullanıcı olarak baştan sona kullanılır: uygulamaya giriş → ana ekran → ilgili modüle gitme →
kayıt oluşturma → kaydı kullanma → başka ekrana geçme → oluşturulan veriyi bulma → düzenleme →
ilişkili başka bir işlem → rapor/listede sonucu görme → tekrar açma → uygulamayı kapatma →
yeniden açma → verinin kalıcı olduğunu doğrulama.

Gerçek kullanıcı "şimdi API'yi test ediyorum" demez. **Gerçek kullanıcı gibi davranılır.**

## 32. Son rapor

Sürecin sonunda şu başlıklarla ayrıntılı rapor üretilir:

1. Uygulanan ekran/özellikler · 2. Test edilen ekranlar · 3. Test edilen alan sayısı ·
4. Test edilen buton/aksiyon sayısı · 5. CRUD testleri · 6. API testleri · 7. DB doğrulamaları ·
8. Tenant/company testleri · 9. Authorization testleri · 10. 10.000+ kayıt testleri ·
11. Performans ölçümleri · 12. Masaüstü testleri · 13. Web testleri · 14. Sync testleri (varsa) ·
15. Network/error testleri · 16. Concurrency testleri · 17. UI/UX denetimi ·
18. Accessibility denetimi · 19. Bulunan hatalar · 20. Hata önem dereceleri · 21. Yapılan düzeltmeler ·
22. Tekrar test sonuçları · 23. Regresyon sonuçları · 24. Build sonuçları · 25. Test sonuçları ·
26. Kalan riskler · 27. Bilinçli olarak test edilemeyenler · 28. Önerilen sonraki işler.

**Her sayı gerçek ölçümden gelir. Tahmin edilen sayılar gerçekmiş gibi yazılmaz.**

## 33. En önemli son talimat

Bu çalışma **"testleri çalıştırdım ve hepsi yeşil" noktasında bitirilmez.** Hedef şudur:

> *"Gerçek bir kullanıcı bu ekranları ve özellikleri kullandığında sorun yaşamaması için elimden
> gelen tüm kontrolleri yaptım, bulduğum sorunları kök nedeniyle düzelttim ve düzeltmeleri tekrar
> doğruladım."*

- Bir butona basıp hata gelmemesi "başarılı" sayılmaz.
- UI'da görünen sonuç, backend ve DB'deki gerçek sonuçla karşılaştırılır.
- Bir kayıt oluşturulduysa, o kaydın **başka ilgili ekranlarda da** doğru göründüğü kontrol edilir.
- Bir kayıt silindiyse, gerçekten beklenen şekilde silindiği/cancel edildiği **DB'de** doğrulanır.
- Bir hesaplama varsa **bağımsız olarak** doğrulanır.
- Bir liste varsa **büyük veri altında** doğrulanır.
- Bir yetki varsa **UI'dan bağımsız, API üzerinden** doğrulanır.
- Bir tenant/company sınırı varsa **ID değiştirerek erişim denenir**.
- Bir sync akışı varsa **offline → online gerçek senaryo** uygulanır.
- Hata bulunursa yalnız raporlanmaz; güvenli ve kapsam dâhilindeki düzeltme yapılır.

### 🔴 PRODUCTION'A KESİNLİKLE DOKUNULMAZ

Bu faz boyunca production DB'ye `SELECT` · `INSERT` · `UPDATE` · `DELETE` · migration · deploy ·
release · seed **uygulanmaz**. Production secret'ları kullanılmaz. Production'a yalnız açıkça ayrıca
izin verilirse dokunulur. **Testler local/test/staging ortamında yapılır.**

> Bu faz bitince tek yayın yapılır (yayın `K`'nın parçası değildir, `K`'dan **sonra** gelir),
> ardından bilgisayar uykuya alınır.
