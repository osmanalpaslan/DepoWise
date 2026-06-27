# Faz 9 — Dayanıklılık / Erişilebilirlik / DPI ekran görüntüleri (kullanıcı tarafından alınacak)

**Neden otomatik alınamadı:** COMODO; asistan imzasız EXE'yi çalıştıramaz (CLAUDE.md §0).

Detaylı bulgular: `docs/UI_ACCESSIBILITY_DPI_REPORT.md`.

## Alınacak (dotnet host) — ölçek kombinasyonları
Windows Görüntü ayarlarından ölçeği değiştirip her birinde Genel Özet + bir liste (Malzemeler/Araçlar) + Raporlar (grafik):
- `1366x768-100.png`
- `1920x1080-100.png`
- `1920x1080-125.png`
- `1920x1080-150.png`
- `2560x1440-150.png` (mümkünse)

## Kontrol listesi (her ölçekte)
- Metin kesilmesi / buton taşması / menü kayması / kart aşırı daralması yok.
- Diyalog/onay panelleri ekran içinde ve ortalı.
- Tablo dar ekranda yatay scroll; içerik kaybı yok.
- Tab/Shift+Tab sırası mantıklı; Enter=Kaydet, Escape=İptal; **focus halkası belirgin** (beyaz 2px).
- İkon butonlarda tooltip görünür (☰, ray ikonları).
- Yardımcı (gri) metinler okunur (kontrast iyileştirildi).
