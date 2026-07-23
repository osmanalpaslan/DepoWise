# Yakıt Ekranı — Masaüstü ↔ Web ↔ Veritabanı Parite Denetimi

**Tarih:** 2026-07-23 · **Kapsam:** Faz 0 (6. ekran) · **Sonuç: 3 bulgu, üçü de düzeltildi (yalnız web).**

## ✅ Eşit çıkanlar
| Konu | Durum |
|---|---|
| Alanlar: Araç, Litre, Sayaç, Birim Fiyat, Yakıtı Veren\*, Yakıtı Alan, Depo Girişi (Litre/Fiyat/Tedarikçi/Fatura) | Masaüstü = Web ✅ |
| Zorunlu: Araç, Yakıtı Veren (madde 8) | Aynı mesaj ✅ |
| Şüpheli büyük litre uyarısı | Aynı merkezi kural ✅ |
| Toplam tutar hesaplaması ve gösterimi (formda salt-okunur alan) | Zaten her iki tarafta da vardı ✅ |

## 🟡 Bulgu 1 (DÜZELTİLDİ) — İki zorunlu alan mesajı farklı sözcüklerle
Masaüstü: "Litre **pozitif olmalı**." / "Litre ve birim fiyat **pozitif olmalı**."
Web (öncesi): "Litre **girin**." / "Litre ve birim fiyat **girin**."
Anlam aynı; web metinleri masaüstüyle birebir eşitlendi.

## 🔴 Bulgu 2 (DÜZELTİLDİ) — Kaydetme onayında tutar/litre bilgisi eksikti
Masaüstünde dağıtım ve depo girişi onay pencereleri şunu gösterir:
*"50 L yakıt dağıtımı kaydedilsin mi? (Toplam 2125 ₺)"* — kullanıcı **parayı etkileyen** işlemi
onaylamadan önce tam miktarı görür. Web'de onay yalnız *"Yakıt dağıtımı kaydedilsin mi?"* diyordu —
tutar formda ayrıca görünüyor olsa da, son onay adımında tekrarlanmıyordu.

**Düzeltme:** Web onay metinleri masaüstüyle birebir aynı (litre + toplam ₺ dahil) yapıldı.
Ciddiyeti: para ile ilgili son kontrol noktası — Stok ekranındaki "stok ARTAR/AZALIR" bulgusuyla
aynı sınıf (yanlış işlemi geri dönülemez şekilde onaylama riski).

## Coverage (§7.13, kısa)
Form açıldı ✅ · Alanlar ✅ · Zorunlu alanlar ✅ (düzeltildi) · Onay metinleri ✅ (düzeltildi, tutar dahil) ·
Etiketler (Yakıtı Veren/Alan) ✅. Kapsam dışı: grid/özet raporu birebir eşleşmesi.

## Doğrulama
Testler 569/569. Web derleme: 0 hata. Masaüstü kod değişmedi.

## Sıradaki
Bakım.
