# 12 — Yayın öncesi nihai doğrulama kontrol listesi (AR-GE çalışması)

> Bu çalışma (`arge/2026-10-kapsamli`) canlıya alınmadan ÖNCE bu liste baştan sona kontrol edilir. Liste fazlar
> ilerledikçe doldurulur; "hatırlatma" maddeleri kullanıcı kararıyla ertelenmiş ama yayından önce mutlaka konuşulacak işlerdir.

## A) Kullanıcının "canlıya alırken hatırlat" dediği maddeler
- [ ] **K-20 / M-01 — Güncelleme paketlerinin dijital imzası + süper admin için iki adımlı giriş (MFA).**
  Kullanıcı 2026-10-13'te "şimdi değil (b), gerçekten canlıya alınacağı zaman hatırlat" dedi.
  Risk (değişmedi): süper admin şifresi sızarsa tüm masaüstlerine sahte güncelleme kurulabilir (04 G4-1, F-030).
- [ ] **K-15 — `/api/public/companies` giriş öncesi tüm firma adlarını veriyor.** Kullanıcı "sorun değil" dedi; yeni
  müşteri eklenmeden önce bir kez daha sorulacak (müşteri adlarının herkese açık olması).
- [ ] **M-42 — Bilgisayar kapalıyken hatırlatma** (kullanıcı: "Daha sonra").

## B) Teknik yayın kapısı (her B fazı sonunda güncellenir)
- [ ] Tam test takımı eksiksiz (PG dahil), arayüz testleri, iki ortam denetimi, canlı web duman testi
  - 2026-10-27 B9+Z: tam takım **4606/4606** (atlanan 0, 2 sa 54 dk, `artifacts/dw_tam_b9z.trx`), arayüz 71/71, yerel tarayıcı 5/5,
    iki ortam denetimi tamam. Yayın günü bu kapı YENİDEN koşulur (yayın öncesi tam koşu zorunlu); canlı duman testi yayından sonra.
- [ ] Senkron protokolü değişikliği: eski masaüstü sürümleriyle uyum + zorunlu güncelleme (M-12) birlikte
- [ ] Migration'lar yalnız ekleme; gerçek boyutlu kopya üzerinde prova (M-70)
- [x] Yedek alındı ve geri yükleme provası (M-08) — 2026-10-17 şifreli günlük yedek + başarılı prova; yayından hemen önce bir kez daha elle al (`scripts/yedek_al.ps1`)
- [x] .NET 10 geçişi canlıda (2026-10-17, masaüstü 1.0.201/1.0.202)
- [ ] **Migration099 (M-03, çekme ufku):** canlıda ilk açılışta her eşitleme tablosuna kolon + indeks + tetikleyici ekler ve
  mevcut TÜM satırları bir kez damgalar (tek işlemde büyük UPDATE). Önce gerçek boyutlu kopyada süre ölç (M-70), ardından
  `VACUUM (ANALYZE)` çalıştır. Yayın sırası: API önce (yeni uç), sonra masaüstü; eski masaüstleri eski uçla çalışmaya devam eder.
  Yayından sonra ilk turda her masaüstü bir kez tam paket çeker (imleç sıfırdan başlar), sonra yalnız değişenler iner.
- [ ] **M-81 + M-03 adım 3:** yayından sonra her masaüstü yeni sürüme geçişte BİR KEZ tam gönderim yapar (sayfalı, 2000 satır/sayfa).
  Aynı gün çok bilgisayar güncellenirse sunucu yükü artar; güncellemeyi birkaç saate yaymak ya da mesai dışı yayınlamak önerilir.
- [ ] **M-11 (web gerçek adres):** iki uygulamaya AYNI gizli anahtar tanımlanmadıkça özellik kapalıdır (bugünkü davranış sürer).
  Yayın günü: rastgele ≥32 karakter üret (ekrana yazma) → `flyctl secrets set DEPOWISE_ISTEMCI_IP_ANAHTARI=… -a depowise-erp`
  ve aynısı `-a depowise-web` (makineler yeniden başlar; API önce). Doğrulama: web'den iki farklı ağdan giriş; birinde 30 hatalı
  deneme yapılınca diğeri girmeye devam eder. Anahtar ayrıca özel hafıza deposuna (`claude-ortak-hafiza/anahtarlar`) yazılır.
- [ ] **M-12 (zorunlu güncelleme):** AR-GE masaüstü sürümü yayınlanınca (yeni senkron protokolü) `publish_release.mjs` ile
  **en düşük desteklenen sürüm = o sürüm** verilirse eski masaüstleri hemen zorunlu güncellemeye girer ve 426 ile veri yazamaz
  (çekme serbest). Karar: bilinçli olarak yükseltilsin mi (önerilen: evet, senkron protokolü değiştiği için) — kullanıcıya sor.
  Sonraki rutin yayınlarda en düşük sürüm DEĞİŞTİRİLMEZ (7 günlük erteleme yeter).
- [ ] **Yetki/şifre imzası biçimi değişti (B3 denetimi):** yayın anında açık olan masaüstleri bir kez "kullanıcınızda güncelleme
  yapıldı" uyarısıyla yeniden giriş ister (imza parola özeti yerine şifre dönemini kullanıyor). Masaüstü güncellemesiyle aynı anda
  olduğu için ek kesinti beklenmez; kullanıcıya duyuru notunda söylenmeli.
- [ ] **M-10 (oturum dönemi):** Migration104 yalnız ekleme (`users.token_epoch`, 0). Yayından önce alınmış jetonlar geçerli kalır;
  yayından sonra şifre değişen/sıfırlanan kullanıcının açık oturumları düşer (beklenen).

## C) B5–B9 ile eklenen yayın günü notları (Z fazı, 2026-10-27)
- [ ] **Dal birleştirme:** AR-GE dalı (`arge/2026-10-kapsamli`) `master`'dan ~157 commit önde. Yayın yalnız kullanıcı "canlıya
  al" dedikten sonra: önce `master`'a birleştirme (çakışma yoksa hızlı ileri sarma), sonra `yayin` skill'i (temiz kopyadan, API → web → masaüstü).
- [ ] **26 migration birden (099 → 124):** canlı şema 98. Hepsi yalnız ekleme (yeni tablo/kolon/indeks/tetikleyici); en ağırı
  Migration099 damgalaması (yukarıda). 100 bin satırlık PG provası (M-70, `PostgresMigrationProvasiTests` MP1) geçti; damga
  tetikleyiciden önce yazılıyor (sıra hatası prova ile bulundu ve düzeltildi). Yayından hemen önce elle yedek: `scripts/yedek_al.ps1`.
- [ ] **Güncelleme notu:** `AppScreens.cs` → `GuncellemeNotlari.Tumu` başına AR-GE yayınının sade notu + `MasaustuSurumu`
  (son canlı sürüm 1.0.202 → yeni sürüm). Notta mutlaka: şube şifresi düzeltmesi, web personel listesi düzeltmesi, yeni ekranlar
  (Asgari Stok Önerileri, e-Fatura içe alma, Muhasebe Aktarımı, olası mükerrer fatura uyarısı, Hakkında), yeniden giriş uyarısı (B3).
- [ ] **Canlıda bekleyen iki hata AR-GE yayınıyla düzelir — yayından sonra doğrula:** (1) şube şifresi atlanabiliyordu
  (masaüstü çevrimdışı/sunucu doğrulayamadı yolu); (2) web personel açılır listeleri boştu → web'den stok girişi/çıkışı yapılamıyordu.
  Duman testi: web'de Stok İşlemleri → personel listesi dolu; masaüstünde yanlış şube şifresiyle giriş reddedilir.
- [ ] **Eşitlemeye yeni katılan kayıtlar (Migration121/122):** B6'nın 6 tablosu (zimmet iade tarihi, zimmet tutanağı, zimmet kiti,
  ekipman taşıma, günlük kontrol şablonu ve kaydı — bu özellikler canlıda henüz yok, eski kayıt sorunu yok) ve "doğrudan stok
  değişikliği" uyarıları (`stock_change_logs`) artık bilgisayarlar arasında eşitlenir. Bu uyarıların yayından ÖNCE masaüstünde
  yazılmış eski kayıtları kendiliğinden gitmez; gerekirse her masaüstünde bir kez "tam gönderim" (TamGonderimIste) yapılır.
- [ ] **Sağlık kontrolü (M-72):** API `/health` artık disk seviyesini (ok/warning/critical) de söyler; `fly.toml` sağlık kontrolü
  ve kesinti takibi (GitHub Actions `kesinti-takibi.yml`) disk alarmını okur. Yayından sonra `curl https://depowise-erp.fly.dev/health`
  → "ok" görülmeli; takip iş akışının ilk çalışması yeşil olmalı.
- [ ] **Masaüstü görünüm (M-74, Avalonia 12.1.4):** 56 yerde eskiyen özellik taşındı (ipucu metni, pencere kenarlığı). Arayüz
  testleri geçti; yine de yeni paket kurulduktan sonra kullanıcı bir kez ana ekranları göz ile kontrol etsin (giriş, stok, rapor, ayarlar).
- [ ] **Web çalışma şubesi:** web artık seçili şubeyi her istekte sunucuya bildirir (9 uç). Birden çok şubeli kullanıcıyla web'de
  şube değiştirip stok listesi/çıkış dene — yalnız o şubenin stoğu görünmeli.
- [ ] **Yerel tarayıcı testleri** (`scripts/run_web_e2e_yerel.ps1`, 5 test: stok kaydı, çift tıklama, iptal, asgari stok, e-Fatura)
  yayın öncesi bir kez daha koşulur; canlı duman testi (`tests/web-e2e`, yalnız okur) yayından SONRA koşulur.
- [ ] **Kullanıcıya sorulacak (acil değil, `11` sonu):** "Şablon Dışı" raporunun şube kapsamı; "Atanmamış" stok toplamı farkı.

## D) Yayından sonra (teknik borç, acele yok)
- M-75 kalanı: `Program.cs` / `ReportService` / `ShellViewModel` bölünmesi, web için tipli API sözleşmesi (`WebApiSozlesmeTests` sessiz kırılmayı yakalıyor).
- M-40 (ertelendi), M-42 ("Daha sonra").
- **Tam test koşusu yavaşladı:** testlerin toplam süresi 34.587 sn (2026-10-25) → 68.080 sn (2026-10-27); artış tek sınıfta değil, geneline
  yayılmış (en ağır: RaporKapsamliTarama 46 dk). Kök neden bulunup kontroller gevşetilmeden hızlandırılacak (yayın öncesi tam koşu
  bu süreyi doğrudan etkiler).
