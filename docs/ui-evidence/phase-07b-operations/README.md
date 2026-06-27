# Faz 7b — Araçlar / Bakım / Yakıt ekran görüntüleri (kullanıcı tarafından alınacak)

**Neden otomatik alınamadı:** COMODO; asistan imzasız EXE'yi çalıştıramaz (CLAUDE.md §0).

## Bağlam
Bu üç modülün masaüstünde **ekranı yoktu** (placeholder). Bu fazda mevcut servisler (VehicleService/MaintenanceService/InspectionService/FuelService) DesktopServices'e bağlandı ve ekranlar ortak bileşenlerle **sıfırdan** kuruldu. Sahte veri yok; tüm liste/işlemler gerçek servislere bağlı.

## Nasıl alınır (dotnet host)
"DepoWise (Gercek DB)" ile aç → her modül için 1366×768 ve 1920×1080:
- `vehicles-1366x768.png`, `vehicles-1920x1080.png`
- `maintenance-1366x768.png`, `maintenance-1920x1080.png`
- `fuel-1366x768.png`, `fuel-1920x1080.png`

## Smoke (manuel)
**Araçlar:** liste açılır (boş veride empty-state); arama (iç kod/plaka) + Ara; durum badge (Aktif/Pasif/Bakımda) + Bakım/Muayene badge (Gecikti/Yaklaşıyor/Güncel); "Yeni Araç" gruplu form (İç Kod zorunlu, hata metni), Enter=Kaydet/Escape=İptal; kaydet sonrası liste yenilenir.
**Bakım Takibi:** periyodik uyarı listesi — Gecikti=kırmızı, Kritik/Yaklaşıyor=turuncu, Güncel=yeşil (metin + badge); ilerleme % + tüketilen/periyot; "Yenile".
**Yakıt:** KPI (Depo Bakiyesi, Güncel Fiyat); "Depo Girişi" formu (Litre+Birim Fiyat) ve "Dağıtım" formu (Araç seç + Litre + Güncel Sayaç); sayısal kolonlar sağa hizalı + formatlı; kaydet sonrası bakiye/liste güncellenir; yetersiz depo/negatif girişte servis hatası mesajı.

## Notlar / sonraki fazlar
- **Araç detay/düzenleme/silme** ve **bakım kaydı girişi/iptali** UI'si bu turda kapsanmadı (servislerde Cancel/SetMeter mevcut; ekranları sonraki faza). Şu an Araçlar=liste+yeni, Bakım=uyarı listesi (salt okuma), Yakıt=liste+depo/dağıtım.
- Dashboard kritik uyarı satırında gerçek navigasyon komutu yok (önceki fazlarda da yoktu) → sahte eklenmedi.
