# Faz 4 — Genel Özet/Dashboard ekran görüntüleri (kullanıcı tarafından alınacak)

**Neden otomatik alınamadı:** COMODO; asistan imzasız EXE'yi çalıştırıp ekran görüntüsü üretemez (CLAUDE.md §0).

## Nasıl alınır (dotnet host)
1. "DepoWise (Gercek DB)" kısayoluyla aç (admin / admin123).
2. Genel Özet (Ana Ekran) açıkken pencereyi şu boyutlara getir ve kaydet:
   - `dashboard-1366x768.png`
   - `dashboard-1920x1080.png`
   - `dashboard-current.png` (mevcut pencere boyutu)

## Doğrulanacak (dashboard smoke)
- 5 metrik kartı: **Toplam Araç, Malzeme Çeşidi, Düşük Stok, Bekleyen Talep, Aktif Personel** (hiçbiri kaldırılmadı).
- Yalnız **ilk kart mavi** (Toplam Araç); diğerleri nötr koyu yüzey; tüm kartlar eşit yükseklik.
- Geniş ekranda kartlar tek satıra yaklaşır; dar ekranda min genişlikle alt satıra wrap eder (içerik kaybolmaz).
- Kritik Uyarılar: uyarı varsa renkli sol çubuklu satırlar (kritik=kırmızı/diğer=turuncu) + başlık + detay.
- Uyarı yoksa **kompakt empty-state**: yeşil ✓ + "Aktif kritik uyarı yok." (büyük gri kutu yok).
- Üst bar başlığı "Genel Özet"; içerikte yalnız küçük "ÖZET"/"KRİTİK UYARILAR" bölüm etiketleri (tekrar eden büyük başlık yok).
