# Bakım Takibi Ekranı — Masaüstü ↔ Web ↔ Veritabanı Parite Denetimi

**Tarih:** 2026-07-23 · **Kapsam:** Faz 0 (7. ve son ekran) · **Sonuç: 2 bulgu, ikisi de düzeltildi (yalnız web).**

## ✅ Eşit çıkanlar
| Konu | Durum |
|---|---|
| Ana form alanları (Araç, Bakım Tanımı, Alt Bakım, Teknisyen, Açıklama, Km, Saat, Tarih, Araç Durumu + Notu) | Masaüstü = Web ✅ |
| Zorunlu: Araç, Bakım Tanımı | Aynı mesaj ✅ |
| **Bakım kaydı BAŞARILI ama araç durumu güncellenemezse** ayrık hata mesajı ("...ANCAK araç durumu güncellenemedi... Araçlar ekranından elle değiştirin.") | Web'de neredeyse birebir aynı kod, yorumlar dahil ✅ (önceden yazılırken parite gözetilmiş) |
| Malzeme miktarı > 0 koruması | Davranış eşdeğer (Günlük Faaliyet'teki gibi — satır eklenirken kontrol, sonradan düzenlenemiyor) ✅ |

## 🟡 Bulgu 1 (DÜZELTİLDİ) — Onay metni kelime farkı
Aynı desen (Günlük Faaliyet'te de bulunmuştu): masaüstü "Bakım kaydı **eklensin mi**? (malzemeler
stoktan **düşülür**)" — web "Bakım kaydı **oluşturulsun mu**? (seçili malzemeler stoktan **düşer**)".
Web metni masaüstüyle birebir eşitlendi.

## 🔴 Bulgu 2 (DÜZELTİLDİ, en önemli bulgu bu turda) — İptal gerekçesi hiç alınmıyordu
Masaüstünde bir bakım kaydı iptal edilirken kullanıcı **iptal gerekçesini yazmak zorunda**
("İptal Gerekçesi" alanı, boşsa "İptal gerekçesi zorunlu." hatası) — bu gerekçe denetim kaydına
(audit) gerçek sebep olarak girer.

Web'de `_cancelReason` adında bir alan **tanımlıydı ama hiçbir arayüz elemanına bağlı değildi** ve
kullanılmıyordu. "Sil" butonuna basınca kullanıcıya hiç soru sorulmadan sabit **"Kayıt silme"** metni
sunucuya gönderiliyordu. Sonuç: bir firma ileride "bu bakım kaydı neden iptal edildi?" diye denetim
kaydına baktığında, masaüstünden yapılan iptallerde gerçek sebep yazarken, **web'den yapılan HER
iptalde aynı anlamsız "Kayıt silme" metni** görünüyordu — gerçek bilgi kayboluyordu.

**Düzeltme:**
- Web'e "İptal Gerekçesi" giriş alanı eklendi (masaüstüyle aynı etiket).
- Kaydetmeden önce boş olamaz kontrolü eklendi (masaüstüyle aynı mesaj: "İptal gerekçesi zorunlu.").
- Sunucuya artık kullanıcının yazdığı gerçek gerekçe gönderiliyor (sabit metin değil).
- Farklı bir kayıt seçildiğinde alan sıfırlanıyor (masaüstüyle aynı davranış).

## Not (kapsam dışı bırakıldı, kullanıcı bilsin)
Web'de ayrıca bir **"Düzelt"** kısayolu var (eski kaydı sil + bilgileri forma taşı) — masaüstünde bu
kısayol **yok** (yalnız tekli iptal akışı var). Bu web'e özel bir kolaylık; masaüstünde karşılığı
olmadığı için birebir eşleştirilecek bir "doğru" davranış yok. Bu turda dokunulmadı. İstenirse ayrı
bir konu olarak (masaüstüne de "Düzelt" eklensin mi, yoksa web'den kaldırılsın mı) ele alınabilir.

## Coverage (§7.13, kısa)
Form açıldı ✅ · Alanlar ✅ · Zorunlu alanlar ✅ · Onay metni ✅ (düzeltildi) · İptal gerekçesi/audit ✅
(düzeltildi) · Araç durumu güncelleme hata senaryosu ✅. Kapsam dışı: grid/filtre/uyarılar (alerts) sekmesi.

## Doğrulama
Testler 569/569. Web derleme: 0 hata. Masaüstü kod değişmedi.

## FAZ 0 — SONUÇ (7 ekran tamamlandı)
Araçlar, Malzemeler, Personel, Stok Giriş/Çıkış, Günlük Faaliyet, Yakıt, Bakım Takibi — hepsi denetlendi.
Toplam bulgu: 11 (1 yanlış alarm hariç), hepsi düzeltildi. Yalnız 1 tanesi masaüstünü etkiledi (Araçlar
plaka uyarısı); geri kalan 10'u yalnız web'de düzeltildi. Ortak bakım riski (sütun listesi elle senkron)
PostgreSQL Faz 3'e not edildi.
