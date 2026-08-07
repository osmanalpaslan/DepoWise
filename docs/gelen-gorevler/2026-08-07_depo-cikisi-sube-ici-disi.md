# Gelen Görev — 2026-08-07 — Depo Çıkışı iş akışı (Şube İçi / Şube Dışı) + Günlük Faaliyet'e ekleme

> Ham prompt aşağıda değiştirilmeden. Analiz + plan en altta.

## HAM PROMPT

Giriş-Çıkış ve Günlük Faaliyet ekranlarında Depo Çıkışı iş akışının yeniden düzenlenmesi. Web + Masaüstü,
aynı iş mantığı + aynı UX. Kod yazmadan önce analiz. Ortak servis/bileşen, kod tekrarı yok, gereksiz refactor yok.

1. **Analiz:** kayıt tipleri (Giriş-Çıkış + Günlük Faaliyet), Depo Çıkışı, Transfer, stok düşme, şube/personel/
   birim/araç seçimi. İki ekran aynı iş mantığını kullanıyor mu?
2. **Günlük Faaliyet'e Depo Çıkışı ekle** (şu an yalnız Giriş-Çıkış'ta var). İki ekran aynı iş mantığı.
3. **Depo Çıkışı ikiye ayrılmalı** (ikinci seçim): ○ Şube İçi Çıkış  ○ Şube Dışı Çıkış (radio/toggle; tasarıma uygun).
4. **Şube İçi Çıkış:** malzeme aynı şube içindeki Personel/Birim/Araç alıcılara teslim; başka-şube alanları
   gizli; sadece gerekli alanlar; malzeme merkez depo stoğundan normal düşer.
5. **Şube Dışı Çıkış:** yalnız gerekli alanlar; Personel/Birim gibi gereksizler gizli; Transfer/diğer-şube
   alanları aktif.
6. **Dinamik alan yönetimi:** işlem tipine göre yalnız gerekli alanlar GÖRÜNÜR; ilgisizler PASİF değil GİZLİ.
7. **Görsel tasarım korunur** (sade/modern/profesyonel/koyu tema; boşluk yok; seçim değişince hizalı).
8. **Ortak iş mantığı:** iki ekran aynı iş kuralları/doğrulama/stok hareketi/davranış; ortak servis/bileşen.
9. **Stok hareketleri bozulmaz:** Şube İçi = normal stok düşümü; Şube Dışı = mevcut transfer/çıkış mantığı; ikisinde de doğru hareket.
10. **Test:** Senaryo1 Şube İçi (personel/birim/araç/stok düşümü), Senaryo2 Şube Dışı (transfer/şube/hareket),
    Senaryo3 aynısı Günlük Faaliyet'te; Web+Masaüstü aynı davranış.

Son kurallar: analiz → ortak bileşen → kod tekrarı yok → çalışanı bozma → geriye uyum → web+masaüstü aynı UX →
rapor.

---

## ANALİZ (Claude) — doldurulacak
