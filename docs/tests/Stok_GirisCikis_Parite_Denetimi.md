# Stok Giriş/Çıkış Ekranı — Masaüstü ↔ Web ↔ Veritabanı Parite Denetimi

**Tarih:** 2026-07-23 · **Kapsam:** Faz 0 (4. ekran) · **Sonuç: 2 bulgu, ikisi de düzeltildi.**

## ✅ Eşit çıkanlar
| Konu | Durum |
|---|---|
| Üç işlem türü: Yeni Kayıt / Depo Çıkışı / Transfer | Masaüstü = Web ✅ |
| Zorunlu: Personel (işlemi yapan) | Aynı mesaj, sunucu da zorluyor ✅ |
| Şüpheli büyük miktar uyarısı (yumuşak) | Aynı merkezi kural ✅ |
| Negatif stok engeli | Sunucu merkezli, her iki arayüz de aynı mesajı görüyor ✅ (bkz. çok makineli simülasyon raporu B-1) |
| Hareket iptali (ters kayıt) | Aynı sebep metni, aynı davranış ✅ |

## 🟡 Bulgu 1 (DÜZELTİLDİ) — Onay penceresi, alan doğrulamasından ÖNCE geliyordu
Web'de "Yeni Kayıt" seçiliyken kod/ad boş bırakılıp Kaydet'e basılırsa, önce genel bir **"Yeni Kayıt
işlemi kaydedilsin mi?"** onayı çıkıyor, kullanıcı "Evet" dedikten SONRA "Kod zorunlu" hatası
görünüyordu. Masaüstünde sıra tam tersi: önce alanlar doğrulanır, onay EN SON sorulur — kullanıcı
başarısız olacak bir işlemi onaylamak zorunda kalmaz.

**Düzeltme:** Web'de doğrulama sırası masaüstüyle aynı hale getirildi (önce alanlar, sonra onay).

## 🟡 Bulgu 2 (DÜZELTİLDİ) — Onay metni yönü belirtmiyordu
Masaüstünde onay pencereleri işlemin **yönünü** açıkça söylüyor — bu, yanlış işlem türünü seçip stok
kaydını tersine çevirme hatasını önleyen bir güvenlik metni:
- "Malzeme kaydedilip stok girişi yapılsın mı? **(stok ARTAR)**"
- "Depo çıkışı kaydedilsin mi? **(stok AZALIR)**"

Web'de bunun yerine genel bir metin vardı: *"Yeni Kayıt işlemi kaydedilsin mi?"* — yön bilgisi yoktu.

**Düzeltme:** Web onay metinleri masaüstüyle birebir aynı yapıldı (üç işlem türü için de).

## Kod tarafı not
Doğrulama sırası düzeltilirken `_material` alanına erişimin derleyici tarafından "null olabilir"
uyarısı vermesi düzeltildi (`!` ile — zaten üstte kontrol edilmiş olduğu için güvenli).

## Coverage (§7.13, kısa)
Form açıldı ✅ · 3 işlem türü ✅ · Zorunlu alanlar ✅ · Onay sırası + metni ✅ (düzeltildi) · Negatif stok ✅ ·
İptal/ters kayıt ✅. Kapsam dışı: grid/filtre denetimi (bu ekranda sabit liste, düşük risk).

## Doğrulama
Masaüstü + web derleme: 0 hata, 0 yeni uyarı.

## Sıradaki
Günlük Faaliyet.
