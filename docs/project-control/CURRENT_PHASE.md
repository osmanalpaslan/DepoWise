# AKTİF DURUM

> Yalnız son ~10 günün girdileri burada durur (her oturumda okunur → kısa kalmalı). Daha eskisi:
> `../arsiv/project-control/CURRENT_PHASE_GECMIS_2026-08-11_2026-09-30.md`.

**Özet (2026-10-28):** YAYINDA — AR-GE büyük yenileme (master = ddfc38e0): API + web + masaüstü **1.0.203** (imzalı, ZORUNLU güncelleme: en düşük 1.0.203) + imzalı kurulum aracı. Şema 125. Sıradaki: kullanıcı web'de süper admin iki adımlı girişini kurar; duman testi hesabının iki adımlı girişi (kullanıcı onayı bekliyor).

## 🎨 2026-10-28 iOS TARZI TASARIM — dal `tasarim/ios` (YALNIZ masaüstü, yayın yok)
- Kullanıcı örnekleri onayladı (Alpnex turuncusu, 4 yeni parça, önce 3 ekran → onay → tümü). Örnek + gerçek ekran görüntüleri: claude.ai/artifact/V2VJyFZTCZoH3RAaPjPeEw.
- Bitti: e528c89c ortak görünüm (palet/köşe/düğme/tablo başlığı/menü) · 7e0f04d5 Ana Ekran (+ araç dağılım çubuğu, Themes/Ios.axaml) · b3ca785f Araç Listesi (eylemler kartta, "•••" menü, bakım çubukları) · 88cdcde5 Yeni Araç formu (grup kutuları, önizleme, kontrol listesi). Arayüz 74/74.
- Kullanıcı onayladı + tasarımı bana bıraktı (açılır bölümler, düğme gruplama, sağ tık). Sonra: d0f58431 sade üst çubuk (Eşitle/arama kalktı, arama Ctrl+K) + kullanıcı kartı sol altta + kart kapağı = ilk fotoğraf · 42f6b228 sağ tık menüsü 13 ekran · cd241a7e simgeli Yenile (30), PDF menüsü, Talep/Yakıt sağ tık · 754a7686 araç kartı/formu açılır bölümler · be3a4603 tüm Expander iOS + detay başlıkları. Arayüz 76/76.
- Ekran ekran (2026-10-29): 10f2c10b Malzeme+Ekipman kartları · c78a6a7f Personel kartı + Belge/Proje/Şube/Firma başlıkları · bd783ce0 İş Emri · 0ce991ca Talep+Cari · 91dc4d43 Bakım kartları + ekran sekmeleri hap · d4e816ca Uyarılar. Arayüz 77/77, her adım alan paketiyle.
- 74b09214 Duyurular/Yakıt seçili kayıt işlemleri "•••" menüsünde, Takvim Sil kırmızı. Faturalar çubuğu bilinçli olarak aynı (Muhasebe Aktarımı dönem seçiciyle birlikte çalışır).
- KARAR: tablo satırlarına simge EKLENMEDİ — sütun genişlikleri kullanıcı isteğiyle (2026-08-06) kaydedilebilir/sabit (Min=Max ColWidths) ve hiza testleri bunu korur; simge seçili kayıt kartında.
- Tasarım önerileri (kullanıcı onayı 2026-10-29, 6/6 TAMAM): 1a285a2e boş ekranda ana eylem düğmesi · 29aa06bc "İşlem tamam" bildirimi iOS kartı ("Geri al" YOK: ters kayıt = iş kuralı, onay gerekir) · 49468f79 ilk yüklemede iskelet · f1978634 durum filtre hapları (7 ekran) · 41368581 basılınca küçülme + 120 ms geçişler · 51ed44f9 ana ekran "Son 6 Ay Yakıt" grafiği. Arayüz 83/83.
- Hata maddeleri (2026-10-29, 3/3 TAMAM): 50054da2 Eksi stok raporu "Son Çıkış Notu" bakım kimliği yerine okunur ad · 3d1701a4 Giriş-Çıkış son hareketler sütun filtreleri (masaüstü+web+API) · 9d94ab0e tablolarda hücre boşluğu (yazı çizgiye değmez). Rapor 531/531, API/parite 256/256, arayüz 84/84.
- 2. tur (kullanıcı isteği 2026-10-29, açık mod hariç TAMAM): 90aa2828 hareket açıklaması okunur (iç kimlik yok; belge notu sütunda + aramada — STK-B2 kapandı) · 37abe94c iOS alanlar + şeffaf açılır liste (✓) · 9d38318c/a59eb2b2/e87bdff9 tüm yeni kayıt formları gruplu (27 form) + büyük formlarda önizleme/kontrol listesi · 259b96bd Excel/Yazdır → •••/sağ tık, iOS menüler (Sil kırmızı en altta) · d052c4a7 hap sekmeler + telefon gibi sohbet paneli (sunucu: kişi listesine son mesaj — yayında API önce). Arayüz 87/87. 39324ff3 açık mod "Yumuşak Gri" (kullanıcı seçimi) · 3b8942f8 Araç+Malzeme bilgi paneli 420 px, zeminsiz özellik satırları, kaydırma çubuğu ayrı. 2. tur TAMAM. · KALICI KURAL (2026-10-29) tablo sütunları veriyle kaymaz: başlık + filtre satırı + satırlar aynı genişlik, uzun veri esnek sütunu şişirip taşınca sütunlar görünür alana sabitlenir (list-screens.md Kural 3; UITTABLO1 + TABLO1). Arayüz 89/89.
- Kalan: tanım/ayar/rapor ekranları (genel iOS stilleri zaten uygulanıyor; ekran ekran ince ayar).

## 📌 2026-10-28 ÇALIŞMA BİÇİMİ — TOPLU YAYIN (kullanıcı kararı, kalıcı)
- Otomatik yayın KALKTI: işler biriktirilir (testli, ayrı commit + push), canlıya çıkmaz. Tam takım ve yayın YALNIZ kullanıcı "yayınlayalım" deyince. Ayrıntı CLAUDE.md §4–§5.

## ✅ 2026-10-28 YAYIN — AR-GE (dal `arge/2026-10-kapsamli` → master), masaüstü 1.0.203
- Kapı: yayın öncesi tam takım **4629/4629** (atlanan 0, 2 sa 47 dk, `artifacts/dw_tam_yayin.trx`; PI9/MA4/MA5/MF7 içinde), arayüz 71/71, yerel tarayıcı 7/7, iki ortam denetimi.
- Sıra: şifreli elle yedek (107 tablo, Drive) → master ileri sarma → sırlar `--stage` (erp: MFA + istemci IP anahtarı; web: IP anahtarı, özetler aynı) → API (şema 125 canlıda doğrulandı: `app_releases.signature`, `user_mfa`) → web → masaüstü 1.0.203 (imzalı, 15 madde not; indirilen paketin özeti sunucuyla aynı) → kurulum aracı (imzalı).
- Canlı duman testi: 5/14 geçti (personel yetki + genel sayfalar). 9 test süper admin yetkili TEST hesabıyla giriyor → web onu tasarım gereği iki adımlı giriş kurulumuna yönlendiriyor (hata değil; 9'u da aynı ekranda). Çözüm hazır: `scripts/e2e_test_hesabi_mfa.mjs` + duman yardımcısı kodu kendisi üretir; test hesabında açmak kullanıcı onayı bekliyor.

## 🔧 2026-10-17/18 AR-GE B1 (dal `arge/2026-10-kapsamli`, YAYIN YOK) — veri güvenliği ve senkron bütünlüğü
- Biten (her biri testli + mutasyonla kanıtlı, ayrıntı `docs/arge/10_uygulama_gunlugu.md`): M-02 firma ezme kapısı · M-05 defter değişmezliği · M-04 web işlem kimliği · M-03 çekme ufku (Migration099) + yerel değişiklik sayacı/yankı kesme (100) · M-15 gönderilemeyen kayıtlar (101) · M-79 sıfırlamadan önce gönder · M-80 sunucu dönemi · M-17 sayfalı çekme + sayfalı gönderim · M-84 gzip · M-85 tek geçiş · M-20 istatistik · M-78/K-21 bilgisayar kodlu numara (102) · K-22 KOD-2 · K-05 eksi stok uyarıları · M-16 eksi stok uyarısı · M-18 ezme yasağı · M-81 geçişte tam gönderim · M-06 işlem geçmişi sunucuya (103) · M-08 şifreli günlük yedek (+ prova başarılı).
- Şema 103 (master 98). **B1 KAPANDI (2026-10-18 12:23):** tam takım 4203/4204 (FIN10 yeni K-04 sözleşmesine güncellendi → geçti), arayüz 48/48, iki ortam denetimi 3 bulgu düzeltildi. Sıradaki: B3.
- **B3 (sürüyor):** M-21 artımlı bakiye · M-23 ucuz değişiklik yoklaması · M-10 şifre değişince eski jetonlar düşer (Migration104, şema 104; güvenlik paketi 743/743) · M-11 web gerçek adres (imzalı başlık; anahtar yayında tanımlanır) + masaüstü tek şifre doğrulaması (840/840) · M-13 parola özeti 600.000 tur + CORS yalnız web + log maskeleme + SECURITY.md (892/892) · M-14 Excel aktarımını geri alma, iki platform (559/559, arayüz 48/48) · M-19 yedekten dönüşte önce gönder + güvenlik kopyası (35/35) · M-22 malzeme listesi 10–18 kat hızlı + arama işlevi (493/493, SQLite+PG eşdeğerlik) · M-12+M-83 zorunlu güncelleme (7 gün / en düşük sürüm), 426 kapısı, migration öncesi yedek (49/49) · PKT3 20 dk → 0,03 sn · M-09 şube yetkisi daralınca yerel temizlik (bekleyen korunur). Sıradaki: B3 sonu tam koşu.
- **B5 (tamam, 2026-10-19):** M-24 çift stok uyarısı · M-51 finans kayıt onay metni · M-29 otomatik SIP-/IE- numarası · M-32 mal kabul irsaliye no + geri alma (Migration105) · M-30 dönem kilidi / ay kapanışı (Migration106; DK1–9 + mutasyon 8/8) · M-25 faturayı mal kabulden oluştur + eşleştirme (Migration107; KF1–8 + mutasyon 7/7) · M-26 talepten stoktan gönder / sipariş aç (108) · M-27 iki adımlı "yolda" transfer (109) · M-28 kör sayım + taslak + fark toleransı (110, şema 110) · M-33 tek onay merkezi + toplu onay. B5 riskli paket 1333/1333 (37 dk 36 sn); her iş ayrı commit, index ayrı klasörde derlenip doğrulandı. M-31 (isteğe bağlı) sonraya. Sıradaki: B6; B5+B6 sonunda tam takım.
- **B6 (kod tamam, 2026-10-20):** M-34 toplu yakıt · M-35 araçtan arıza → iş emri (111) · M-36 sayaç geçmişi / geçersiz say (112) · M-37 bakım planı "ya da gün" (113) · M-39 kira sözleşmesi iki yön (114) · M-41 periyodik kontrol (115) · M-46 zimmet iade tarihi (116) · M-47 zimmet tutanağı + PDF (117) · M-48 ekipman taşıma (118) · M-44 şüpheli yakıt · M-49 formda QR okutma · M-50 zimmet kiti (119) · M-43 günlük kontrol listesi (120, şema 120) · M-38 duruş raporu. Hedefli 67/67, mutasyon 31/31; her iş ayrı commit, index ayrı klasörde derlenip testleri koştu. M-40 ertelendi (K-02/B8 sonrası). B5+B6 tam takım 4385/4385 (1 sa 54 dk, atlanan 0), arayüz 48/48, iki ortam denetimi 10 bulgu düzeltildi (ac665f0f). **B5+B6 KAPANDI (2026-10-24).** Sıradaki: B7.
- **B7 (tamam, 2026-10-25):** M-55 yazdır + zorunlu alan işareti · M-54 en yoğun 10 listede Excel/Yazdır (iki ortam) · M-56 kayıt kartında Belgeler + stok hareketinden kaynağa gitme (sipariş/talep/iş emri/fatura/araç-ekipman bakımı; iki ortam) · yayın öncesi iki kullanıcı isteği (Sayım KOD — AD; Yeni Kayıt çok malzeme) tamam. Ara bulgu: menüdeki bakım "Uyarılar" kısayolu Ekipman sekmesini açıyordu → düzeltildi. M-57 üç yönetici raporu (Eksi Stok İstisna · Ay Sonu Stok Değeri · Makine Kullanım Oranı; katalog 30 → 33) · M-58 kayıtlı rapor görünümü (kişisel, iki ortam aynı kayıt) + raporlarda PDF düğmesi. B7 sonu iki ortam denetimi: 4 ayrışma düzeltildi. 
- **B8 (TAMAM, 2026-10-25):** M-59 tema kontrastı iki temada/iki ortamda + yeni renk anahtarları + tek yazı tipi Inter (gövde 14) · M-61 düğme sırası İptal … Kaydet · M-62 ekran okuyucu etiketleri (masaüstü 63 / web 45 simge düğmesi), masaüstü kaydedildi bildirimi, klavye kısayolları (F5, Ctrl+N/F/W/Tab, F1), yükleniyor göstergesi · ara bulgu: web Ekipler silme onaysızdı → düzeltildi, onay kuralı testi genişletildi · M-60 ortak parçalar (Pager, SelectionBar, InfoBar, durum paneli çevrimdışı/yetki yok + simgeler) eklendi; ekranlara uygulanması M-63/M-64 ekran dönüşümünde. M-63 işlem ekranları (Sayım, Zimmet, Yakıt, Giriş-Çıkış) tamam · M-64 liste ekranları TAMAM (Cari, Fatura, Tahsilat, Talep, Malzeme, Araç; ortak Pager solda, numaralı seçenek; Raporlar sayfalamasız). M-65 tanım ekranları (ortak SideSheet, 5 ekran) + ana ekran hızlı işlemler tamam. İki ortam denetimi bulguları düzeltildi (web Vazgeç…Kaydet sırası 7 yer). B7+B8 tam takım 4504/4504 (atlanan 0, 2 sa 28 dk), arayüz 69/69. **B8 KAPANDI.**
- **B9 (sürüyor):** M-66 test güvenilirliği (500 ≠ ret, atlanan test = eksik koşu) · M-67 tüm API uçlarına yetki taraması. Araya giren kullanıcı istekleri (2026-10-25 akşam, öncelikli): şube şifresi atlanıyor (P0), şube stoğu yanlış, şube dışı çıkışta teslim alma + kusurlu, günlük faaliyet tam düzenleme, web açılış animasyonu kaldır, menü okunaklılığı, Listeye Ekle kehribar — **7 istek TAMAM (2026-10-26)**; günlük faaliyet düzenlemesi: bakım kaydında tüm alanlar + malzeme miktarı/ekleme/kaldırma tek işlemde (eski bakım iptal + yeni kayıt, stok geri döner), iptal/kapanmış dönem/iptal bakımda uyarı bandı, transferde araç/tür kilitli; ara bulgu: kapanan kabuk ortak değerde kalıyordu (ana ekran testi sıraya bağlıydı) → düzeltildi. M-72 tamam (2026-10-26): /health disk seviyesi (yalnız ok/warning/critical), fly.toml sağlık kontrolü, kesinti takibinde disk alarmı — etkisi sonraki yayında. M-73 tamam (Semi paketi kaldırıldı, ikiz dosya eşitlik testi; Org servisleri, masaüstü yedek ekranı, eski senkron uçları bilinçli bırakıldı). İki ortam denetimi bulguları düzeltildi (web çalışma şubesi sunucuda, kayıp kapatma hedefte, küçük parite farkları). M-68 iki bilgisayar + sunucu çevrimdışı zincir testi · M-70 sonuç eşitliği (yakıt toplamı ayrışması düzeltildi) + görsel eşik + 100 bin satırlık migration provası (099 damgalama sırası düzeltildi) · M-76 belgeler + Hakkında · M-69 web kayıt akışları yerel tarayıcı testi (2 bulgu düzeltildi: personel listesi boş, çift onay) · M-74 Avalonia 12.1.4 + eskiyen API taşıması · M-75 web↔API sözleşme testi (dosya bölme yayın sonrasına) · B6 kayıtları eşitlemeye (Migration121; bulgu: 6 tablo bilgisayarlar arasında hiç eşitlenmiyordu) · eşitleme kararı nöbetçisi + stok değişiklik kaydı eşitlemeye (Migration122) · M-31 depo bazında asgari stok + öneri listesi + talep (Migration123, şema 123; iki ortam) · M-53 tedarikçi tek yerde (Tanımlar ↔ Cari köprüsü kendiliğinden + eski veri için yönetici eşleme) · M-52 e-Fatura XML içe alma + genel Excel muhasebe aktarımı (iki ortam) · M-82 olası mükerrer fatura uyarısı (Migration124, şema 124) · B9 sonu iki ortam denetimi (hata yok, 4 ayrışma düzeltildi; yerel tarayıcı testinde seçim kararsızlığı giderildi) tamam. **B9+Z tam takım 4606/4606 (2026-10-27, 2 sa 54 dk), arayüz 71/71 → B9 KAPANDI. Z tamam** (`docs/arge/12_yayin_oncesi_kontrol.md`). AR-GE geliştirmesi bitti; yayın kullanıcı kararıyla. Tam koşu hızlandırma ilk adımı tamam (ApiTestHost şablon veritabanı). Sıradaki: kullanıcı yayın kararı.

## ✅ 2026-10-17 YAYIN — .NET 10 geçişi (masaüstü 1.0.201 + API + Web), dal `net10-gecis`
- Yalnız çerçeve yükseltmesi (net8.0 → net10.0, Microsoft paketleri 10.0.13, Docker 10.0); iş mantığı değişmedi. .NET 8 desteği
  2026-11-10'da biter. SQLite paket açığı (GHSA-2m69-gcr7-jv3q) kapandı. AR-GE çalışmasının (yayın yasağı) kullanıcı onaylı istisnası.
- Kapı: tam takım 4103/4103 (atlanan 0), arayüz 47/47, self-contained masaüstü açılış testi. AR-GE dalı (`arge/2026-10-kapsamli`) yayına GİRMEDİ.

## ✅ 2026-10-12 YAYINDA — masaüstü 1.0.200: tablo başlığı hizası · web: /new yetki kapısı
- Tam takım 4103/4103, arayüz 47/47 (5×), iki ortam denetimi (2 risk yayından önce düzeltildi), canlı web Playwright 14/14, paket sağlaması canlıyla aynı.
- `TableHeaderSync` sağlamlaştırıldı (geç/yeniden şablon, boş liste, satırın gerçek kolon genişliğini kopyalama) ve 34 tabloya bağlandı (toplam 39). İstisnalar: Malzeme/Araç/Günlük Faaliyet (ortak kaydırıcı), ItemsControl'lü 3 tablo.
- Web `Parties.razor`/`Finance.razor`: `/new` yetkisiz kişiye form açmıyor (Invoices kalıbı).
- Test: arayüz G31 (zorunlu piksel hizası, kaydırılmış dahil), tarama 30 ekran temiz; TH5 (tüm ekranlarda bağ), YKF7 (web /new yetki). İkisi de bozuk sürümde kırmızı olduğu kanıtlandı.

## ✅ 2026-10-12 YAYINDA — masaüstü 1.0.199: yeni kayıt formları görsel iyileştirme
- Kullanıcı kararı: testler tam geçince yayınla. Tam takım 4101/4101, arayüz 41/41, iki ortam denetimi engel yok. Paket temiz HEAD kopyasından; canlı indirme sağlaması yerelle aynı; API/Web /health 200.
- Rapor: `docs/raporlar/GORSEL_IYILESTIRME_2026-10-12.md`. Yalnız masaüstü görünümü + menü yönlendirmesi; iş mantığı/şema/API değişmedi.
- Form yüksekliği ekrana göre (sabit 380–640 px kutular kaldırıldı): Malzeme/Araç formu listenin esneyen satırında; liste görünen ekranlarda `FormListeOrani` (Depo Giriş kalıbı). Kullanıcılar formuna kaydırma.
- Menü "Yeni Araç Ekle / Yeni Cari / Yeni Fatura / Yeni Hesap" artık formu açar (web zaten açıyordu), yetki yoksa açmaz.
- Sarmalı form satırları (Bakım, Yakıt, İş Emri, Satın Alma, Duyuru), `FormBolum` bölüm başlığı, kapalı kutu görünümü, yer tutucu birliği.
- Test: `GorselTaramaTests` (30 ekran × 3 boyut, SIKIŞIK FORM/ULAŞILAMAZ dahil, eski hatalı sürüme karşı kanıtlandı); `MasaustuYeniKayitFormuTests` (25). Arayüz 41/41.
- Açık: tablo başlığı dar pencerede kayıyor (ortak `TableHeader` + `ListBox.Table`) → ayrı iş önerildi.

## ✅ 2026-10-11 gece YAYINDA — yeni logo + açılış animasyonu (masaüstü 1.0.198)
- Logo: kaynak JPG şeffaflığı çizilmiş dama → kroma+parlaklık anahtarı, renk sızdırma, ön-çarpılmış küçültme (`scripts/logo_uret.py`). Exe/görev çubuğu şeffaf `app-gorev.ico`; masaüstü kısayolu + Setup arka planlı `app-logo.ico` (Setup IconLocation + `KisayolSimgesi`).
- Açılış animasyonu: masaüstü şeffaf çerçevesiz pencere (240 WebP kare, `AcilisAnimasyonu`, tıkla geç); web giriş sayfasında oturum başına bir kez şeffaf WebM (`dwAcilisOynat`).
- Doğrulama: tam takım 4072/4072, arayüz 11/11, canlı Playwright 14/14. Setup.exe yeniden paketlenmedi (yeni kurulum dosyası gerekirse ayrıca).

## ✅ 2026-10-11 YAYINDA — kullanıcı talebi (6 iş, fazlı): `TALEP_2026-10-11.md`
- Faz 1 ✅ web alt ekran geçiş hatası: ana yerleşim gövdeyi `WebSayfaAnahtari` ile @key'ler (Araç/Kiralık, Malzeme, Rapor/Yönetici, Talep/Onay, Yeni Cari/Fatura/Hesap).
- Faz 2 ✅ ses adayları masaüstünde (kullanıcı seçecek) · Faz 3 ✅ yakıt tüketim raporlarına Yakıtı Teslim Alan / Teslim Eden.
- Faz 4 ✅ yeni rapor «Depo Çıkışları» (stock-issues): teslim alan + çoklu malzeme filtresi; rapor sayısı 30.
- Faz 5 ✅ Zimmet toplu teslim: bir personele birden fazla kalem tek işlemde (masaüstü+web+API).
- Faz 6 ✅ Güncelleme notları (masaüstü önce+sonra, web pop-up, iki ortamda ekran; tek kaynak `GuncellemeNotlari`). Ekran sayısı 73. Ek: app.css MOB-W yorum açılışı onarıldı.

## ✅ 2026-10-10 (akşam) — belge sadeleştirme · yakıt sapma + kira bitişi uyarıları · Kiralık Araç Maliyeti raporu · aylık yedek provası
- **Belgeler (d4c63e4):** CLAUDE.md 217→70 satır (güncel mimari); her mesajda yüklenen ~461→171 satır; eski raporlar `docs/arsiv/`; harita `docs/README.md`.
- **Filo uyarıları (979415a):** `FleetAlerts` — son dolum aracın 180 günlük ortalamasının %30+ üstü → "Yakıt tüketimi yüksek" (≥%60 kritik); kira bitişi ≤7 gün / geçmiş+aktif → yeni **Kiralık** uyarı kategorisi (masaüstü+web, ana ekran+Uyarılar).
- **Rapor:** `rental-cost` Kiralık Araç Maliyeti (kesişen gün × bedel gün/ay/saat + dönem yakıtı).
- **Yedek provası:** `scripts/yedek_prova.ps1` + görev "DepoWise Aylik Yedek Provasi" (ayın ilk Pazarı 04:00); `yedek_al.ps1` artık `*.counts.json` sayım listesi yazar. İlk prova BAŞARILI; bozuk listeyle hata yakalama kanıtlandı.
- Not: yakıt deposu kritik seviye uyarısı ZATEN vardı (%20).
- **YAYIN (gece):** API + Web + masaüstü 1.0.196. Yakıt sapma tabanı MEDYAN (canlı ölçüm: ağırlıklı ortalama 40+ sahte uyarı üretecekti → medyanla 6).
- **Olay:** art arda Playwright koşuları 256 MB web makinesini kilitledi (~22:57) → makine yeniden başlatıldı. Önlem: swap 256 MB (fly.web.toml ÜST düzeyde!), DOTNET_GCConserveMemory=5, kopan devre tutma 1 dk/20. Testten sonra MemAvailable ~40 MB, swap boş.
- Tam takım 3.996/4.017 (21 PG testi yerel test DB 51 MB > 50 MB kapısı) → pg_test_sunucu.ps1 artık 40 MB üstünde DB'yi yeniden kurar; PG + hedefli 80/80. Canlı web Playwright 6/6.

## ✅ 2026-10-10 YAYINDA — API+Web 41f47a7 · masaüstü 1.0.195 · şema 98 · Talep çoklu araç · Kiralık Araçlar · Yakıt Özeti · yeni sesler
- **Talep Formu (4e0cf18):** kalemde birden fazla araç (`material_request_items.vehicle_ids`, Migration097; `vehicle_id` = ilk araç,
  geriye uyumlu); arama sonucunda + kalem tablosunda + standart PDF'te **malzeme kodu**. Araç kimliği artık firmaya göre doğrulanıyor.
- **Kiralık Araçlar (c3101f5):** ayrı ekran (`vehicles.rental`, masaüstü `vehicles:rental`, web `/vehicles/rental`); `vehicles.is_rental`
  + kira sütunları (Migration098). Araç Listesi kiralıkları göstermez; tüm araç seçicileri ikisini "(Kiralık)" etiketiyle listeler.
  Değişim kaydı: yeni kiralık araç açılırken giden araç aynı işlemde PASİF + kira bitişi yazılır. "Kiralamayı Bitir" (iade).
  Kiralık araç şablon zorunluluğundan muaf. Ayrı fiziksel tablo AÇILMADI (gerekçe Migration098 başlığında).
- **Yakıt Özeti (e599cf1):** iOS tarzı kartlar; haftalar mavi oran çubuğu, günler yeşil takvim kutucuğu; bu ay açık, boş aylar soluk.
- **Sesler (494193c):** matematiksel tonlar → GitHub **Octave** (ücretsiz iOS arayüz sesleri); `scripts/ses_guncelle.mjs`.
- **QA (41de313):** Avalonia Headless artık Skia ile çizer + ekran görüntüsü; UIT10-12. Web: `tests/web-e2e/tests/yeni-ekranlar.spec.js`.
- **Doğrulama:** tam takım 4008 (9 bulgu düzeltildi, ilgili 155 test yeniden geçti) · masaüstü arayüz 6/6 · canlı web Playwright 7/7 (salt okuma).
  Canlı web 256 MB: art arda çok Playwright koşusu Blazor oturumlarıyla makineyi yavaşlatır → koşular arası ~3 dk bekle.
- **Not:** `TASK_BACKLOG.md`'deki birçok "SIRADA" madde kodda zaten kapalı (GUV-A1/A2, DEN-D1, DEN-E2, DEN-F1 doğrulandı) → liste temizlenmeli.

## ✅ 2026-10-04 (2. tur) YAYINDA — API+Web a6294ac · masaüstü 1.0.194 · sayaç kuralı · tablo başlıkları · rapor sütun genişliği · ANLIK EŞİTLEME
- **Bakım sayacı (e44e46a):** formlarda tek alan, türü aracın sayacı; bakımda zorunlu (masaüstü + web + sunucu `RequireMeter`).
  Güncelden küçük/eşit değer kabul. Tamir/İlave Yağ/Filtre'de tek alan ama opsiyonel.
- **Tablolar (72fd044):** tüm başlıklar yatay+dikey ortada (rapor dahil); rapor tablosu tutamağı Thumb → işaretçi yakalamalı Border.
- **Anlık eşitleme (d7acce4):** `/api/sync/wait` uzun yoklama + `SyncNotifier`; masaüstü gelen için bekler, giden için 3 sn'de bir YEREL kontrol.
  Zamanlayıcı: 30 sn güvenlik kontrolleri + 60 sn güvenlik ağı turu. Geri sayım halkası kalktı; aktarımda dönen yay + ↑giden/↓gelen.
- **Kalıcı kurallar (hafıza):** test tipine Claude karar verir; tablo istekleri rapor dahil TÜM tablolara uygulanır.
- **Yayın (2026-10-04 19:00):** tam takım 3.988/3.990 (2 hata = yeni test sınıflarında seri koleksiyon eksikti, düzeltildi, yeniden geçti) → API + Web deploy + masaüstü 1.0.194. Sıradaki: kullanıcı rapor tablosunda sütun sürüklemeyi ekranda doğrulasın.

## ✅ 2026-10-04 — uyarılar 2. tur · fotoğraflar cihaza iner · muadil düzenleme (1.0.194 ile YAYINDA)

- **Uyarı kök nedenleri (canlı veride doğrulandı, salt-okuma):**
  - Bakım: 1.0.193 tanımın biriminde sayaca göre seçiyordu; GREY 010'da saat tanımının yeni kaydı sayacı **km alanına** girilmiş → eski kayıt seçildi.
    Artık: **yapıldığı tarih** önce, sayaç = km ?? saat (89215f7).
  - Muayene: uyarı doğruydu; **liste ekranı** yenilenmiş eski belgeyi "Süresi geçti" gösteriyordu → artık "Yenilendi".
  - Bilgilendirme notu (masaüstü + web): sayaçsız son bakım (5 kalem "Kontrol gerekli"), şüpheli araç sayacı (EKS-P 004: 122045 vs 12404),
    esas alınmayan sonradan girilen kayıt, bitiş tarihsiz / geçmiş bitişli yeni belge.
  - Evrak modülü canlıda boş (uyarı üretmiyor); diğer kaynaklar (stok/yakıt/iş emri/talep/duyuru) kayıt-bazlı, "eski kayıt" sorunu yok.
- **Fotoğraflar (8248789):** sunucu diski tam (251 kayıt → hepsinin dosyası var). Taşıma günündeki erişimsizlik havuz tükenmesi (500 → "çevrimdışı" sanıldı).
  Masaüstü artık fotoğrafları `%LOCALAPPDATA%\DepoWise\FotoOnbellek` altına indirir (30 dk'da bir arka planda, yeni uç `/api/photos/index`); çevrimdışıyken buradan gösterir.
- **Muadil (b192462):** sağ panelde KOD — Ad; çift tık penceresinde (masaüstü + web) liste + Düzelt modunda ekle/çıkar.
- **Sıradaki:** tam test takımı → kullanıcı onayıyla API deploy + masaüstü 1.0.194 yayını. Önce API (yeni uç), sonra masaüstü.

## ✅ 2026-10-01 akşam — uyarı düzeltmesi + tablo güncellemeleri YAYINDA (API bf5e20e · masaüstü 1.0.193)

- **Uyarı:** bakım/muayene "en son kayıt" giriş zamanına değil yapıldığı noktaya göre (canlıda 21+1 grup eski uyarı tutuyordu).
- **Tablolar:** ekran geneli sağ tık, belirgin sütun çizgisi (ColumnRuleBrush), başlık–filtre hizası, tasarım tüm tablolarda, rapor tablosunda kayıtlı genişlik.
- **DB bağlantısı:** API Supabase **doğrudan** bağlantıya alındı (IPv6, `Maximum Pool Size=25`, DB max_connections 60).
  Sebep: pooler adresinde 10'luk havuz yoğun eşitlemede tükendi → `/api/dashboard` 500. Yerel araçlar pooler adresini kullanır (PC'de IPv6 yok).
- **Test:** tam takım 3.885/3.885 (58 dk) + sonraki değişiklikler için ilgili 821 test.


## ✅ ÜRETİM VERİTABANI TAŞINDI: Neon → Supabase (2026-10-01) · API 512 MB · masaüstü 1.0.191 sunucuda

- **Neden:** Neon ücretsiz compute kotası ~17.09'da doldu (masaüstü 15 sn yoklaması DB'yi hiç uyutmuyordu) → API çöktü.
- **Yapılan:** Neon `depowise_prod` pg_dump (yedek: `Documents/DepoWise_Yedekler/neon_depowise_prod_2026-10-01.dump`, 1,1 MB) →
  Supabase `Alpnex` / `depowise_prod` (eu-central-1, PG17, session pooler :5432, `Maximum Pool Size=10`; Data API kapalı).
  Doğrulama: 107/107 tablo satır sayısı birebir (24.419 satır); `updated_at` olan 70 tablonun son güncelleme zamanı birebir;
  Neon yedekten sonra değişmedi (veri kaybı yok). Fly secret `DEPOWISE_PG_URL` → Supabase; API + Web temiz HEAD (f428464)
  kopyasından deploy; API `shared-cpu-1x` 512 MB; masaüstü **1.0.191** `publish_release.mjs` ile yayınlandı (otomatik güncelleme).
- **Ders:** bu Npgsql sürümü `Max Pool Size` takma adını tanımıyor → `Maximum Pool Size` (ilk deploy bu yüzden çöktü, hemen düzeltildi).
- **Geri dönüş:** Neon `depowise_prod` silinmedi (yedek). Bağlantı bilgisi Neon API'den alınır (.env'deki eski şifre geçersizdi).
- **Açık:** CLAUDE.md §4 hâlâ "Neon" diyor (dosyada kullanıcının commitlenmemiş değişiklikleri var, dokunulmadı). SNK-15: birkaç gün
  sonra Supabase aylık trafik ölçümü.
