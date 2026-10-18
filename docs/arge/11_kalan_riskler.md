# 11 — Kalan riskler ve bilinen açıklar (AR-GE çalışması)

> B fazları ilerledikçe doldurulur. Her madde: ne · etki · neden şimdi kapatılmadı · nerede ele alınacak.
> Yayın öncesi kontrol listesi `12`'dedir; buradaki maddeler yayın kararında birlikte değerlendirilir.

## Platform eşitliği (web ↔ masaüstü)
- **Web'de ekipman bakımına malzeme eklenemiyor** (yalnız masaüstünde var). K-05 "yetersiz stok" uyarısı bu yüzden yalnız
  masaüstü ekipman bakımında. Etki: web kullanıcısı ekipman bakımında kullanılan parçayı giremez. Kapsam: B6 (filo/bakım).
- **Gönderilemeyen Kayıtlar penceresi yalnız masaüstünde** — bilinçli: web doğrudan sunucuya yazar; reddedilen kayıt
  formda hata olarak görünür. Açık değil, tasarım gereği.

## Senkron
- **Eski masaüstü sürümleri** (1.0.202 ve öncesi) eski çekme ucunu ve damga imlecini kullanmaya devam eder; yeni korumalar
  (ufuk, yerel sayaç, bilgisayar kodlu numara) yalnız güncellenen bilgisayarlarda devreye girer. Zorunlu güncelleme M-12 ile.
- **K-22 b kapsamı:** isimle benzersiz tanımlar (birim, marka, tedarikçi, araç tipi…) iki bilgisayarda aynı adla açılırsa
  ikincisi yine reddedilir (gönderilemeyen kayıtlar listesinde görünür). Bilinçli: "Adet-2" aynı şeyin mükerrer kaydıdır;
  doğru çözüm birleştirmedir (ileride).
- **Kod çakışmasında mükerrer kart:** aynı malzeme iki bilgisayarda açıldıysa sunucuda `MLZ-100` ve `MLZ-100-2` olarak iki
  kart oluşur; birleştirme aracı henüz yok (K-22 kararında "birleştirme ileride").
- **Masaüstünde pull sırasında yerel kod çakışması** (kullanıcı yerelde elle `MLZ-100-2` açmışsa) çekilen satır o bilgisayarda
  reddedilebilir — çok düşük olasılık.
- **M-18 talep geçmişi:** talepte kaybeden (sonra gelen) onay/ret kararı talep satırına yazılmaz, ama o bilgisayarın ürettiği
  `request_status_history` satırı (yalnız ekleme) sunucuya gider → geçmişte "reddedildi" olayı görünür, güncel durum "onaylandı" kalır.
  Çakışma ekranı doğruyu gösterir. Düzeltme (kaybeden kararın geçmiş satırını işaretleme) B3 çakışma işinde.
- **Çakışma tespiti saat bazlı:** "eşzamanlı" kararı bilgisayarın son gönderim zamanı ile kayıt damgalarını karşılaştırır (mevcut
  tasarım). Saati çok yanlış bilgisayarda çakışma kaçabilir ya da fazladan çıkabilir; veri kaybolmaz (kaybeden sürüm saklanır).
- **Çakışmada "Üzerine Yazılanı Kazanan Yap" yalnız ana kaydı geri yazar** (`PromoteLoser`): bakım/ekipman bakımı çakışmasında
  kaybeden sürümün malzeme satırları ve stok hareketleri buna göre DÜZELTİLMEZ (iki platformda aynı; FAZ 4.4 tasarımı). Bakım
  kaydında malzeme farkı varsa yönetici stok düzeltmesini ayrıca yapmalı. Kalıcı çözüm (bağlı satırlarla birlikte geri yazma) B3 çakışma işinde.
  Kaynak: iki ortam denetimi 2026-10-18.

## Güvenlik
- **M-10 sohbet bağlantısı:** şifre değişince eski jetonlar API'de 401 olur, ama sohbet merkezi (`ChatHub.cs`, kullanıcının
  commitlenmemiş sohbet çalışması) jetonu doğrudan okur → iptal edilen jetonla sohbete bağlanılabilir. Dosya kullanıcıya ait
  olduğu için değiştirilmedi; sohbet commitlenirken `svc.OturumDonemi.Gecerli(...)` kontrolü eklenmeli.
- **M-10 kendi şifresini Kullanıcılar ekranından değiştiren yönetici** o oturumdan düşer, yeniden giriş yapar (bilinçli: tüm
  açık oturumlar kapanır). İlk giriş şifre belirlemede düşme yok (yeni jeton verilir).
- **M-10 önbellek:** dönem 30 sn önbellekte tutulur; aynı süreçte değişim anında görülür. API birden fazla makineye çıkarsa
  diğer makinede eski jeton en çok 30 sn daha geçerli kalabilir (bugün tek makine).

- **M-11 web gerçek adres:** gizli anahtar iki uygulamaya tanımlanmadıkça özellik kapalı (yayın günü işi, `12`).
- **M-12 sürüm kapısı yalnız `business-push`'ta:** masaüstünün diğer yazma uçları (firma kuyruğu, tanım, kullanıcı) eski
  sürüme açık kalır; ana veri yolu kapalı olduğu için kabul edildi. İlk zorunlu yayında en düşük sürüm bilinçli yükseltilmeli.
- **M-09 kapsam temizliği:** yalnız çekmenin süzdüğü şube kapsamlı tablolar temizlenir; malzeme kartı, cari, yakıt, bakım,
  günlük faaliyet şube kapsamında değil (çekme de süzmüyor — KARAR-7). Bunları kapsama almak ayrı bir ürün kararıdır.
  Bilgisayarın kendisi şifrelenmediyse (BitLocker) veritabanı dosyası yine okunabilir → kurulum kılavuzunda önerilmeli.

## Satın alma (M-32)
- Migration105 öncesi mal kabullerinden işlem geçmişinde kabul kaydı bulunmayanlar (çok eski / geçmişi silinmiş) siparişe
  bağlanamaz: bunlar stok ekranından eskisi gibi iptal edilebilir ama siparişin teslim alınan miktarı kendiliğinden düşmez.
- Kısmi geri alma yok: bir kabul belgesi bütün olarak geri alınır (yanlış miktar → geri al + doğru miktarla yeniden kabul).

## Dönem kilidi (M-30)
- Çevrimdışı bilgisayarda kilitten ÖNCE girilmiş ama kilitten SONRA gönderilen eski tarihli kayıt sunucuda reddedilmez (senkron
  uygulaması iş kurallarını yeniden çalıştırmaz). Bilgisayar bir sonraki girişte kilidi alır; aradaki pencere kabul edildi.
- Kilitle kapanmayan alanlar: günlük faaliyet hareket kaydı (stok/para etkisi yok), cari açılış/elle cari hareketi, sayım.
  Fatura bilgi alanları (not, vade, karşı belge no) kapalı dönemde de düzeltilebilir — tutar/stok değişmez.

## Fatura ↔ mal kabul (M-25)
- Bir fatura TEK mal kabulüne bağlanır; birden çok kabulü tek faturada birleştirme (toplu fatura) yok — her kabul ayrı faturalanır.
- Siparişsiz (doğrudan stok girişi) mallar için eşleştirme yok; M-24 çift stok uyarısı orada geçerli kalır.
- Fiyat farkı (fatura fiyatı ≠ sipariş fiyatı) engellenmez, yalnız miktar kontrol edilir; fiyat farkı uyarısı istenirse ayrı iş.

## Performans (M-22)
- Malzeme listesi ve SQLite arama işlevi hızlandı. **Stok hareketleri, günlük faaliyet ve yakıt listeleri** aynı "her sayfada tam
  sayım + tüm küme" desenini hâlâ kullanıyor (300 bin satırda 2–9 sn, 07 §4.1). Aynı iki aşamalı yöntem uygulanabilir; her biri
  için eşdeğerlik testi (MGH deseni) şart. Ada göre sıralama ve `%…%` arama indekssiz (SQLite FTS5 / PG pg_trgm orta vadede).

## Excel içe aktarma (M-14)
- **Açılış stoğu girilmiş malzeme geri alınamaz** (stok defteri değişmez, MLZ-01): geri almada "bırakıldı" olarak listelenir;
  stok düzeltmesiyle sıfırlanıp elle silinmeli. Otomatik oluşan tanımlar (birim, marka, kategori, bakım tanımı) da bırakılır.
- **"Geri Al" düğmesi yalnız aktarımın hemen ardından** görünür (ekrandan çıkınca parti kimliği unutulur). Eski bir aktarımı
  geri almak için liste ekranı yok — ileride "son aktarımlar" listesi eklenebilir (kimlikler işlem geçmişinde duruyor).
- Parti araması `audit_logs.correlation_id` üzerinde indekssiz (firma filtresiyle); seyrek yapılan bir yönetici işlemi olduğu için
  kabul edildi. Büyük geçmişte yavaşlarsa yalnız ekleme bir indeks yeterli.
