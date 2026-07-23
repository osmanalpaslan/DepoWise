# Günlük Faaliyet Ekranı — Masaüstü ↔ Web ↔ Veritabanı Parite Denetimi

**Tarih:** 2026-07-23 · **Kapsam:** Faz 0 (5. ekran) · **Sonuç: 2 bulgu, ikisi de düzeltildi (yalnız web).**

## ✅ Eşit çıkanlar
| Konu | Durum |
|---|---|
| 6 kayıt türü (Hareket, Transfer, Bakım, İlave Yağ, İlave Filtre, Tamir) | Masaüstü = Web ✅ |
| Zorunlu: Araç seçimi, Bakım Tanımı (yalnız gerçek Bakım'da) | Aynı mesaj, aynı sıra ✅ |
| Transfer/Hareket onay metinleri | Zaten birebir aynı ✅ |
| Malzeme miktarı > 0 koruması | **Davranış eşdeğer, kod farklı** — masaüstünde satır sonradan
  düzenlenebildiği için kaydetme anında kontrol var; web'de satır eklenirken kontrol edildiği ve
  sonradan düzenlenemediği için kaydetmede tekrar kontrole gerek yok. Gereksiz kod eklenmedi. ✅ |

## 🟡 Bulgu 1 (DÜZELTİLDİ) — Bakım/İlave kayıt onay metni farklı sözcüklerle
Masaüstü: *"Bakım kaydı eklensin mi? (malzemeler stoktan düşülür)"*
Web (öncesi): *"Bakım kaydı oluşturulsun mu? (seçili malzemeler stoktan düşer)"*
Anlam aynı, sözcükler farklıydı. Web metni masaüstüyle birebir eşleşecek şekilde değiştirildi
(Bakım + İlave Yağ/Filtre/Tamir onaylarının ikisi de).

## 🔴 Bulgu 2 (DÜZELTİLDİ) — Silme onayında kayıt türü ve bakım bağlantısı uyarısı eksikti
Masaüstünde bir faaliyet silinirken onay metni **kaydın türünü** söyler (örn. "Bakım kaydı silinsin mi?")
ve eğer bu kayıt bir Bakım Takibi kaydına bağlıysa **"(Bağlı bakım kaydı Bakım ekranında kalır.)"** notunu
ekler — kullanıcı, günlük faaliyet satırını silmenin Bakım Takibi geçmişini SİLMEYECEĞİNİ bilir.

Web'de onay metni genel bir *"Bu faaliyet silinsin mi?"* idi — ne tür bir kayıt olduğu, ne de bakım
bağlantısı bilgisi vardı. Bir kullanıcı bakımla ilişkili bir günlük faaliyet satırını silerken bakım
geçmişinin de silineceğini sanabilirdi (silinmiyor, ama web bunu söylemiyordu).

**Düzeltme:** `Delete()` artık tüm satırı (`JsonElement`) alıyor; grid API'sinin zaten döndürdüğü
`type` (SQL'de Türkçe etikete çevrilmiş: "Bakım", "Hareket", "Transfer" vb.) ve `maintenanceId`
alanlarını kullanarak masaüstüyle birebir aynı mesajı üretiyor.

## Coverage (§7.13, kısa)
Form açıldı ✅ · 6 kayıt türü ✅ · Zorunlu alanlar ✅ · Onay metinleri ✅ (düzeltildi) ·
Silme uyarısı ✅ (düzeltildi) · Malzeme satırı koruması ✅ (eşdeğer). Kapsam dışı: grid filtre/sıralama
birebir eşleşmesi.

## Doğrulama
Testler 569/569. Web derleme: 0 hata. Masaüstü kod değişmedi (bu ekranda yalnız web düzeltildi).

## Sıradaki
Yakıt.
