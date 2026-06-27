# Faz 7a — Malzemeler modülü ekran görüntüleri (kullanıcı tarafından alınacak)

**Neden otomatik alınamadı:** COMODO; asistan imzasız EXE'yi çalıştıramaz (CLAUDE.md §0).

## Nasıl alınır (dotnet host)
1. "DepoWise (Gercek DB)" kısayoluyla aç (admin / admin123) → Malzemeler.
2. Pencereyi şu boyutlara getirip kaydet:
   - `materials-1366x768.png`
   - `materials-1920x1080.png`

## Modül smoke (manuel doğrulama)
- **Liste açılıyor:** tablo başlığı + satırlar; uzun "Ad" ellipsis + üzerine gelince tooltip.
- **Durum badge:** Stok ≤ Min Stok satırlarında turuncu **"Düşük"**, diğerlerinde yeşil **"Yeterli"** (renk + metin birlikte).
- **Arama:** üst toolbar arama kutusuna yaz + "Ara" → liste filtrelenir; sonuç yoksa **boş durum** paneli.
- **Yeni kayıt:** "Yeni Malzeme" → gruplu form açılır (Tanım / Fiyat & Stok). Kod/Ad boş Kaydet → alan altında kırmızı hata metni + zorunlu (*).
- **Enter/Escape:** form içinde Enter = Kaydet, Escape = İptal.
- **Kaydet/İptal:** geçerli kayıt eklenir, liste yenilenir, form kapanır; İptal alanları temizler/kapatır.
- **Yetki:** `materials` create yetkisi yoksa "Yeni Malzeme" butonu görünmez.

## Not — Kategoriler
Masaüstü uygulamasında ayrı **Kategoriler ekranı/VM/servisi henüz yok** (yalnız Malzeme Listesi + satır-içi Yeni Kayıt mevcut). Sahte modül eklenmedi; Kategori ekranı ilgili modül oluşturulduğunda aynı ortak bileşenlerle modernleştirilecek.
