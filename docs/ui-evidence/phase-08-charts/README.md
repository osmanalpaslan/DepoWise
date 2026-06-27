# Faz 8 — LiveCharts2 grafik entegrasyonu (kullanıcı tarafından alınacak)

**Neden otomatik alınamadı:** COMODO; asistan imzasız EXE'yi çalıştıramaz (CLAUDE.md §0).

## Karar
- Framework **Avalonia 12** (Faz 0) → **LiveChartsCore.SkiaSharpView.Avalonia 2.0.5** (kararlı NuGet, MIT).
- Kaynak repo (LiveCharts2-master.zip = v2.1.0-dev) **solution'a dahil edilmedi**; yalnız kontrollü PackageReference (#3).
- Paket erişimi vardı; Avalonia 12.0.4 ile **çakışmasız restore** (DataGrid'in aksine). Lisans: THIRD_PARTY_NOTICES.md.

## Grafikler (yalnız gerçek veri — #5/#6)
Raporlar ekranında, **çalıştırılan raporun gerçek `TableModel` satırlarından** türetilir (ek sorgu/sahte seri yok):
- **Yakıt Tüketim → bar:** araç bazında litre (`FuelConsumption`). X ekseni = araç kodu (etiket), Y = litre.
- **Stok Durumu → pasta:** Düşük (stok ≤ min) / Yeterli dağılımı (`StockStatus`). Dilim etiketi "Düşük: N" / "Yeterli: N".

## Tema / okunabilirlik (#7/#8)
- Koyu panel üstünde; eksen/etiket rengi merkezi palet (`TextSecondary #AEB7C4`), seri renkleri palet (Accent/Warning/Success — sınırlı, tutarlı).
- Renk **dışında** anlam: bar X-ekseni araç etiketi + Y "Litre" başlığı; pasta dilimlerinde **metin etiketi** (ad+değer). Hover tooltip açık (TooltipPosition=Top).

## Performans notu (#9)
- **Animasyon:** seri `EasingFunction = null` → giriş animasyonu kapalı (gereksiz animasyon yok).
- **Nokta sayısı:** bar grafiği ilk **20 araçla** sınırlı (`MaxBars`); büyük veri setinde taşma yok.
- **UI thread:** seri yalnız `Sorgula` ile, zaten yüklenmiş satırlardan kurulur (ek I/O yok; senkron ve hızlı).
- **Temizlik:** grafikler statik koleksiyona bağlı, zamanlayıcı/abonelik yok → View kapanınca elle temizlik gerekmez (ShowBar/ShowPie ile gizlenir).

## Filtre güncelleme (#10)
- Rapor tipi/tarih değişip **Sorgula** → seriler yeniden kurulur; tip değişince bar↔pasta otomatik geçer (ShowBar/ShowPie).

## Empty/Error (#11)
- Sorgula'dan önce / veri yokken: "Grafik için Sorgula'ya basın". Hata: rapor tablo alanında `StatePanel` Error. Grafik yalnız `ShowChart` (veri var) iken görünür.

## Ekran görüntüleri (kullanıcı, dotnet host)
- `reports-fuel-bar-1366x768.png`, `reports-fuel-bar-1920x1080.png`
- `reports-stock-pie-1366x768.png`, `reports-stock-pie-1920x1080.png`

## Smoke
Raporlar → "Yakıt Tüketim" + Sorgula → bar grafik (veri varsa). "Stok Durumu" + Sorgula → pasta. Veri yoksa empty mesajı. Tooltip hover'da görünür.
