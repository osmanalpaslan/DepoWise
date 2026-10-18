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
