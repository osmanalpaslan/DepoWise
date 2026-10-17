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
