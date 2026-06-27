# DepoWise Kullanıcı Rehberi

> Sade, adım adım. Teknik bilgi gerektirmez.

## 1. Kurulum (masaüstü)
1. DepoWise kurulum paketini açın (yöneticinizden alın).
2. Uygulama **dotnet host** ile çalışır; geliştirme makinesinde EXE/BAT'a çift tıklamayın — masaüstü kısayolunu kullanın.
3. İlk açılışta veriler `%LOCALAPPDATA%\DepoWise\Data` altında, gerçek diskte tutulur.

## 2. İlk kullanıcı ve kurulum
- İlk açılışta **Ana Makine (master)** kurulumu yapılır: şirket bilgisi girilir, yönetici (admin) hesabı oluşur.
- **Personel cihazı eklemek için:** master'da admin "Enrollment Anahtarı" üretir (tek kullanımlık, 10 dakika geçerli). Personel bu anahtarı girer → cihaz "onay bekliyor" durumuna düşer → admin onaylar → cihaz aktifleşir.
- Anahtar süresi dolarsa veya bir kez kullanıldıysa yenisini üretin.

## 3. Günlük kullanım
- **Giriş:** kullanıcı adı + parola. 5 hatalı denemede 5 dakika kilit.
- **Malzeme/Stok:** kart oluşturun; açılış stoğu "stok hareketi" olarak girilir. Giriş/çıkış/transfer/sayım belgeyle yapılır; stok asla hareketsiz değişmez.
- **Araç/Bakım:** araç sayacı geriye gitmez. Bakım kaydı malzemeyi stoktan bir kez düşer; %85/%95/%100 eşiklerinde uyarı çıkar; yeni bakım uyarıyı temizler.
- **Talep:** talep oluştur → onay/ret. **Onay stok düşürmez**; gerçek çıkış ayrı işlemdir. PDF çıktısı alınır.
- **Yakıt:** depo girişi + araç dağıtımı; fiyat işlem anında sabitlenir (sonradan değişmez).

## 4. Yedekleme ve geri yükleme
- Günlük otomatik yedek `Belgeler\DepoWise_Yedekler` altına alınır (30 gün saklanır).
- Geri yükleme: **yönetici** + ikinci doğrulama gerekir. Yedek bütünlüğü kontrol edilir; bozuksa yüklenmez. İşlemden sonra uygulamayı yeniden açın.

## 5. Güncelleme
- Uygulama yeni sürümü kontrol eder; indirme yüzdesi gösterilir.
- Paket **checksum** ile doğrulanır; bozuksa kurulmaz.
- Güncelleme başarısız olursa otomatik **eski sürüme dönülür** — veriniz etkilenmez.
- Sürüm imzasız ise uygulama şeffaf bir uyarı gösterir.

## 6. Sık sorunlar
- "Veriler boş görünüyor": yanlış (sanal) kopya çalışıyor olabilir — masaüstü kısayolundan açın (dotnet host).
- "Şube görünmüyor": kullanıcı kapsamınız o şubeyi içermiyor olabilir; yöneticinize sorun.
- "Çöp Kutusu": silinen kartlar yönetici + ikinci doğrulama ile geri yüklenir.
