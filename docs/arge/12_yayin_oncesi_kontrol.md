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
- [ ] Yedek alındı ve geri yükleme provası (M-08)
- [x] .NET 10 geçişi canlıda (2026-10-17, masaüstü 1.0.201/1.0.202)
- [ ] **Migration099 (M-03, çekme ufku):** canlıda ilk açılışta her eşitleme tablosuna kolon + indeks + tetikleyici ekler ve
  mevcut TÜM satırları bir kez damgalar (tek işlemde büyük UPDATE). Önce gerçek boyutlu kopyada süre ölç (M-70), ardından
  `VACUUM (ANALYZE)` çalıştır. Yayın sırası: API önce (yeni uç), sonra masaüstü; eski masaüstleri eski uçla çalışmaya devam eder.
  Yayından sonra ilk turda her masaüstü bir kez tam paket çeker (imleç sıfırdan başlar), sonra yalnız değişenler iner.
