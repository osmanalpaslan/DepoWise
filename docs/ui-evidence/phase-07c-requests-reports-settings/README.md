# Faz 7c — Talepler / Raporlar / Tanımlar-Ayarlar ekran görüntüleri (kullanıcı tarafından alınacak)

**Neden otomatik alınamadı:** COMODO; asistan imzasız EXE'yi çalıştıramaz (CLAUDE.md §0).

## Bağlam
Bu üç modülün masaüstünde **ekranı yoktu** (placeholder). Faz 7b deseniyle aynı: mevcut servisler
(RequestService/ReportService/SettingsService) DesktopServices'e bağlandı ve ekranlar ortak bileşenlerle
**sıfırdan** kuruldu. Sahte veri yok; tüm liste/işlemler gerçek servislere bağlı.

## Nasıl alınır (dotnet host) — her modül 1366×768 + 1920×1080
- `requests-1366x768.png`, `requests-1920x1080.png`
- `reports-1366x768.png`, `reports-1920x1080.png`
- `settings-1366x768.png`, `settings-1920x1080.png`

## Smoke (manuel)
**Talepler:** liste açılır (boş/hata/empty-state); durum filtresi (Tümü/Taslak/Beklemede/Onaylı/Reddedildi/İptal) + arama (belge no/açıklama); **durum badge** (Taslak/İptal=nötr, Beklemede=turuncu, Onaylı=yeşil, Reddedildi=kırmızı); satır seçince **detay** (kalemler + durum geçmişi); duruma göre **Gönder/Onayla/Reddet/İptal** butonları (mevcut RequestService komutları; Reddet'te gerekçe zorunlu → boşsa servis hatası mesajı). *(Şemada öncelik alanı yok → öncelik göstergesi eklenmedi.)*
**Raporlar:** rapor tipi (Stok Durumu / Yakıt Tüketim) + tarih filtreleri (ortak FormField/DatePicker) + **Sorgula**; Sorgula'dan önce "Rapor hazır" bilgi durumu (ReportGate: tıklanmadan çalışmaz); sonuç tablosu ortak tablo stiliyle; **Grafik Alanı** boş container (LiveCharts2 ileri faz — paket eklenmedi, sahte grafik yok).
**Tanımlar/Ayarlar:** "Marka" bölümü açıklayıcı alt metinle; alanlar (Uygulama Adı*, Şirket Adı*, İletişim, Web, Telif); **Kaydet** → hassas ayar **onay paneli** (görünüm/başlık etkisi) → Onayla; **Geri Al** servisten yeniden yükler. Kalıcılık `SettingsService.Set` (mekanizma değişmedi).

## Ürün adı (#9)
Pencere başlığı `AppName` ("DepoWise"), login "DepoWise — Giriş". Kaynakta **ALPDEP/ALPDEPO yok** (grep temiz).

## Notlar / sonraki fazlar
- **Talep oluşturma (yeni talep) formu** bu turda eklenmedi (mevcut `Create` komutu korunur, değiştirilmedi); list/detay/filtre/aksiyon kapsandı. Kalem-builder formu ileri faza.
- **Kategoriler** ("definitions" altındaki) ekranı Ayarlar ile aynı anahtarı paylaşır; ayrı kategori CRUD ekranı yok (LookupService hazır) → ileri faz.
- Gerçek DataGrid Avalonia ≥12.0.5 ile (ListBox/ItemsControl tablo deseni).
