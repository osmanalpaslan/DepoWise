# Ortak Tablo Bileşeni (Birim 4) — Test Raporu

**Tarih:** 2026-08-07 · **Motor:** Opus 4.8 · **Kapsam:** ortak tablo (web `DwDataGrid` + masaüstü
`GridController`/`DataGridView`) + kişisel kolon tercihi altyapısı + Raporlar entegrasyonu. Diğer ekranlara
dokunulmadı (kural 7).

## Sonuç özeti
- **Build:** 0 hata (tüm çözüm). Uyarılar yalnız önceden var olan AVLN5001 (Watermark/SystemDecorations).
- **Test:** tam paket **633/0** (11 PG atlandı) — 616→633, **+17 yeni**, regresyon yok.
- **Yeni testler:** `UserListPreferenceTests` +5 (pinned/sort/GetAll), `GridDataViewTests` +12 (filtre/sıralama).

## Coverage Matrix (otomatik doğrulanan)
| Alan | Durum | Nasıl |
|---|---|---|
| Kolon-altı filtre — metin (içerir, büyük/küçük duyarsız, Türkçe) | ✅ | GridDataViewTests |
| Kolon-altı filtre — sayısal (tam / `> < >= <=` / `5-10` aralık) | ✅ | GridDataViewTests |
| Sayısal filtre boş hücreyi eler | ✅ | GridDataViewTests |
| Çoklu filtre (VE mantığı) | ✅ | GridDataViewTests |
| Sıralama (sayısal artan/azalan, metin kültür-duyarlı) | ✅ | GridDataViewTests |
| Tercih: kolon sırası/seçim/genişlik round-trip, kişiye özel | ✅ | UserListPreferenceTests |
| Tercih: pinned + varsayılan sıralama (altyapı) round-trip | ✅ | UserListPreferenceTests |
| Tercih: `GetAll` tek sorguda hepsi + hiç kayıt yoksa null | ✅ | UserListPreferenceTests |
| Migration058 (SQLite+PG, idempotent) | ✅ | migration runner (test kurulumu) |
| Regresyon (Materials/Vehicles/Daily ListPrefs, rapor mimarisi) | ✅ | tam paket 633/0 |

## Görsel doğrulama (kod incelemesi + kullanıcı)
- **Web:** build + davranış testleri yeşil; `dw-grid` mevcut tasarım korundu (kolon-altı filtre satırı, sürükle-
  taşı, CSS genişlik). Canlı UX doğrulaması yayından sonra.
- **Masaüstü:** Avalonia bu ortamda önizlenemez → binding/davranış (dinamik kolon, Thumb genişlik, Kolonlar
  menüsü OneWay+komut, popup-güvenli kolon komutları) kod incelemesiyle doğrulandı. **Görsel doğrulama 1.0.112'de
  kullanıcıyla** yapılacak (kullanıcı notu).

## Riskler / notlar
- Masaüstü görsel davranış (genişlik sürükleme hissi, hizalama) yalnız çalıştırıldığında tam görülür → 1.0.112.
- Pinned + varsayılan sıralama yalnızca ALTYAPIDA (UI kapalı) — bilinçli (kural 2).
