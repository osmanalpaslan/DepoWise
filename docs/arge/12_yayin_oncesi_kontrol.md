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
- [ ] .NET 10 geçişi canlıda (son tarih: .NET 8 desteği 2026-11-10'da biter) — yayın kullanıcı kararı
