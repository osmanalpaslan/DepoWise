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

## Talep karşılama (M-26)
- Sipariş Aç işlem kimliği taşımaz; çift tıklamada ikinci sipariş "kalandan fazla" kuralıyla reddedilir ama kalan tam bölünüyorsa
  (ör. iki ayrı kalem) ikinci istek kısmen geçebilir — ekran düğmeyi işlem sırasında kilitler.
- Talep operasyon durumu (ör. "Gönderim İçin Yola Çıktı") gönderimle otomatik değişmez; kullanıcı durum makinesinden seçer.

## İki adımlı transfer (M-27)
- Şube kapsamı kısıtlı kullanıcının çekme/temizlik yolları "~yolda" lokasyonlu hareketleri şube dışı sayar; bu kullanıcılar yoldaki
  malı yalnız transfer listesinde (kaynak ya da hedef kendi şubesiyse) görür, lokasyon kırılımında görmeyebilir.
- Yolda transfer iptal edilemez (tek adımlıdaki kural); yanlış gönderim = teslim al + ters transfer ya da kayıp kapatma.

## Sayım (M-28)
- Taslaklar eşitlenmez: masaüstü taslağı yalnız o bilgisayarda, web taslağı yalnız sunucuda görünür. Tolerans aşımında depocu
  masaüstünde taslak kaydederse yetkili AYNI bilgisayardan ya da web'de yeniden sayımla kesinleştirir.
- Sayım listesi yazdırma (PDF) yok; mevcut "Stok Sayım Raporu" Excel/PDF çıktısı kullanılır.

## Onay merkezi (M-33)
- Tutar eşiği / kademeli onay (F-115 isteğe bağlı kısmı) yapılmadı. Onay geri alma (unapproval) yok.

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

## Araç / yakıt / bakım (B6: M-34…M-44)
- **M-34:** toplu yakıtta "Yakıtı Alan" zorunluysa toplu giriş kapalı (satır başına personel seçimi yok).
- **M-35:** alarm ekranından doğrudan iş emri açma yapılmadı (yalnız Araçlar ekranından). Masaüstü `AlertsView.axaml`'de
  kullanıcının commitlenmemiş satır sonu farkı olduğu için o dosyaya dokunulmadı.
- **M-36:** "geçersiz say" yalnız tek tek yapılır; toplu geçersiz sayma yok. Eski sayaç uyarısı sabit 30 gün (firma ayarı yok).
- **M-41:** periyodik kontrol rapor dosyası (PDF) eklenemiyor; yalnız rapor numarası tutulur.
- **M-44:** şüpheli liste yalnız işaret eder; "incelendi" olarak kapatma yok (kayıt düzeltilince düşer; sayaç geçersiz sayılınca yalnız sayaç kaynaklı nedenler düşer).
  Eşikler sabit (2,5 kat, en az 4 kayıt).

## Zimmet / ekipman (B6: M-46…M-48)
- **M-46:** iade tarihi aşımı yalnız listede görünür; bildirim/e-posta yok.
- **M-47:** kabul/ret kararını zimmet düzenleme yetkisi olan kullanıcı işler (personelin kendi uygulama hesabıyla onaylaması yok).
  Islak imza için PDF yazdırılır.
- **M-48:** taşıma geçmişi yalnız yeni taşımalardan başlar; eski konum değişiklikleri geçmişe aktarılmadı.
- **M-50:** "zimmet talebi" (personelin alet istemesi + onay) yapılmadı; mevcut Talepler ekranıyla istenir. Alet → araç zimmeti yok.
- **M-49:** okutma yalnız yakıt dağıtımı ve zimmet formunda; bakım/iş emri formlarında yok. Kamera ile okutma yok (USB/Bluetooth
  okuyucu klavye gibi yazar).

## Günlük kontrol / duruş (B6: M-43, M-38)
- **M-43:** madde listesi firma genelinde tek (araç tipine göre farklı liste yok). Kontrol yapılmayan gün için uyarı yok.
- **M-38:** duruş süresi takvim saatidir (mesai/hafta sonu ayrımı yok); tamamlanma zamanı iş günü olarak yazıldığından gün içi
  saat hassasiyeti sınırlıdır.
- **M-40 (ertelendi):** iç kira fiyatıyla şantiye makine maliyeti — K-02 muhasebe aktarımıyla birlikte (B8 sonrası) tasarlanacak.

## Liste standardı (B7: M-54, M-55)
- **M-54:** dışa aktarım üst sınırı 50.000 satır (sunucu belleği); aşılırsa kesilir — çok büyük firmada tarih süzgeci önerilir.
  Masaüstünde Projeler/Belgeler dışa aktarımı internet ister (ekranın kendisi de sunucudan okunuyor).
- **M-54:** Bakım ekranı araç kartından "araca daraltılmış" açıldığında dışa aktarım ekrandaki arama/tarih süzgecini kullanır
  (araç daraltması aktarıma taşınmaz) — gerekirse arama kutusuna araç kodu yazılır.
- **M-54:** liste sıralaması (kolon başlığına tıklama) dışa aktarıma taşınmaz; dışa aktarım servisin varsayılan sırasıyla gelir.
- **M-55/M-54:** "her listede sıralama, 6 ekranda arama, tek tip sayfalama" kısımları (F-057'nin geri kalanı) B8 ortak ekran parçalarıyla
  (M-60) birlikte yapılacak.
- **M-56:** web kartlarında Belgeler paneli yalnız düzenleme formunda (kayıt düzenleme yetkisi olan görür); masaüstünde kart
  panelinde görme yetkisiyle görünür. Kayıt kartından belge EKLEME yok (Belgeler ekranından eklenir).
- **M-56 kaynağa gitme:** bir harekete birden fazla kayıt bağlıysa tek kaynak açılır (sıra: sipariş → talep → iş emri → fatura).
  Web ekipman bakımının ayrı detay paneli yok (kayıt listede vurgulanır). Bakım tüketimi satırının "Not" sütununda bakım kimliği
  (uzun kod) görünmeye devam ediyor — okunur ad M-60 ortak tablo parçalarıyla ele alınabilir.
- **Yeni Kayıt sepeti:** sepete yalnız mevcut malzemeler girer; birden fazla YENİ malzeme kartı tek seferde açılamaz (her yeni kart
  formdan; o kayıtta sepetle birlikte işlenir).
- **M-57 yönetici raporları:** Ay Sonu Stok Değeri geçmiş ayı da malzeme kartındaki GÜNCEL birim fiyatla değerler (geçmiş fiyat
  saklanmıyor; hareket anı fiyatıyla ortalama maliyet ayrı bir karar ister). Makine kullanım oranında "çalışılan gün" yakıt fişi ve
  ileri giden sayaç kaydından gelir; yalnız günlük faaliyet kaydı girilen (yakıtsız, sayaçsız) gün çalışma sayılmaz. Sayaç
  kaydının günü KAYIT anıdır (geri tarihli girilen sayaç düzeltmesi kayıt gününe düşer).
- **M-58 kayıtlı görünüm:** tarih aralığı saklanmaz (görünüm açılınca «Bu Ay» ya da raporun varsayılanı). Masaüstü çevrimdışıyken
  kaydedilen görünüm sunucuya o an gitmez; bir sonraki kayıt/silmede sunucuya yazılır. Son görünüm silinince sunucu boş liste döner ve
  masaüstü yerel kopyayı kullanır (başka bilgisayarda eski görünüm görünebilir; kaydet/sil ile eşitlenir). Görünüm paylaşılamaz (kişisel).
- **M-64 Talepler:** liste en yeni 200 talebi gösterir, ortak sayfalayıcı yok (sayfalama servis/API değişikliği ister). 200'den eski
  talepler arama/durum süzgeciyle ya da Excel/Yazdır (tüm sonuç) ile bulunur. Gerekirse B9'da servis sayfalamasıyla ele alınır.
- **M-62 ekran okuyucu:** web `ChatDock.razor`'daki 2 simge düğmesi etiketsiz (kullanıcının commit'lenmemiş sohbet işi; `ErisilebilirlikTests`
  ER2'de istisna) — sohbet işi birleşince etiketlenip istisna kaldırılmalı. M-61: Özel Raporlar'da Kaydet ikincil, Zimmet'te dar pencerede
  Kaydet alta sarılıyor (M-63/M-64).

## Test güvenilirliği ve uç taraması (B9: M-66, M-67)
- **M-67 makine kaydı:** `POST /api/machines/register` girişsiz çağrılabilir (tasarım gereği — yeni makine kendini kaydeder, kayıt
  yönetici onayına düşer, IP hız sınırı var). Boş gövdeyle çağrılınca varsayılan firmaya ("DEPOWISE") "Bilinmeyen Makine" adlı
  bekleyen kayıt açar. Zararı: onay listesinde gereksiz satır. Öneri: firma kimliği boşsa reddet — kayıt akışı (güvenlik) değişikliği
  olduğu için kullanıcı kararına bırakıldı.
- **M-66:** atlanan test artık koşuyu "EKSIK KOSU" yapar; PG sunucusu (`scripts/pg_test_sunucu.ps1`) kapalıyken filtreli koşular da
  kırmızı döner — bilinçli PG'siz koşu için `-PostgresAtla` kullanılır (yayın kararı için tam sayılmaz).

## Kullanıcı bildirimleri 2026-10-25
- **ŞUBE ŞİFRESİ AÇIĞI CANLIDA (bilinçli bekletiliyor):** canlı kodda da (master) masaüstünde sunucu "doğrulayamadı" deyince ve
  internetsiz girişte şube şifresi atlanabiliyor; sunucuda firma kimliği boş girişte şifre varsayılan firmada aranıyor. Düzeltme AR-GE
  dalında yapılıyor; **kullanıcı kararı: AR-GE bitene kadar canlıya acil yayın YOK** — AR-GE sonu yayınıyla düzelir.
- **Stok kapsamı — kullanıcıya sorulacak iki ayrıntı (iki ortam denetimi 2026-10-26):** (a) "Malzeme — Şablon Dışı" raporu firma
  geneli stok toplar (şube oturumunda da) — şubeye göre mi olmalı? (b) "Tüm Şubeler"le giren ve yalnız bazı şubelere yetkili
  kullanıcıda malzeme listesi "Atanmamış" (deposu belirsiz) stoğu saymaz, "Stok Durumu" raporu sayar — hangisi esas alınsın?
  İkisi de veri sızıntısı değil, yalnız toplam farkı.
- **CANLIDA web personel listeleri boş (2026-10-26 M-69 bulgusu):** 2026-09-04'ten beri web'in personel açılır listeleri ilk açılışta
  boş; web Stok İşlemleri'nde zorunlu "Personel" seçilemediği için web'den stok girişi/çıkışı yapılamıyor (masaüstü etkilenmez).
  Düzeltme AR-GE dalında; AR-GE yayın yasağı nedeniyle canlıya AR-GE sonu yayınıyla gelir — kullanıcıya bildirildi.
- **Yayın sonrası teknik borç (M-75 kalanı):** Program.cs / ReportService / ShellViewModel bölünmesi ve web için tam tipli API sözleşmesi — gerekçe günlükte; sessiz kırılma riski WebApiSozlesmeTests ile kapatıldı.
- **Canlıda (AR-GE sonu yayınına kadar):** masaüstündeki "doğrudan stok değişikliği" uyarı kayıtları web'de görünmüyor (Migration122
  ile düzeltildi; canlıya AR-GE yayınıyla gelir). Yayından ÖNCE masaüstünde yazılmış eski kayıtların yerel değişiklik sayacı boş
  olduğundan bunlar kendiliğinden gönderilmez — yayın sonrası gerekirse "tam gönderim" (TamGonderimIste) ile bir kez gönderilir.
