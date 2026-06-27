# DepoWise UI — Kullanıcı Kabul Kontrol Listesi (UAT)

**Nasıl:** "DepoWise (Gercek DB)" kısayoluyla (dotnet host — COMODO izolasyonsuz) aç (admin / admin123).
Her satırı işaretle. Görsel kanıt: `docs/ui-evidence/final/` (1366×768, 1920×1080, %125/%150).

## Açılış & kabuk
- [ ] Uygulama açılıyor; pencere başlığı **DepoWise**; login "DepoWise — Giriş".
- [ ] İkon rayı + açıklamalı menü + üst bar görünür; ☰ ile panel daralıp genişliyor.
- [ ] İkon butonlarda tooltip (☰, ray ikonları); klavye focus halkası belirgin.

## Navigasyon
- [ ] Tüm menü grupları açılır; seçili modülde mavi vurgu + koyu seçili satır.
- [ ] Her modül açılıyor (Dashboard, Malzemeler, Araçlar, Bakım, Yakıt, Talepler, Raporlar, Ayarlar).

## Dashboard
- [ ] 5 KPI (Araç/Malzeme/Düşük Stok/Bekleyen Talep/Personel) gerçek değerlerle; yalnız ilk kart mavi.
- [ ] Kritik uyarı varsa renkli+metinli satır; yoksa kompakt "Aktif kritik uyarı yok ✓".
- [ ] **Eşitle** butonu görünür (NOT: şu an yer tutucu, komut bağlı değil — bilinen sınır).

## Malzemeler
- [ ] Liste açılıyor; arama + Ara; stok badge (Düşük/Yeterli) metin+renk.
- [ ] Yeni Malzeme: gruplu form; Kod/Ad boş Kaydet → alan hata metni; Enter=Kaydet, Escape=İptal.
- [ ] Geçerli kayıt eklenince liste yenilenir.

## Araçlar
- [ ] Liste + arama (iç kod/plaka); durum badge (Aktif/Pasif/Bakımda) + Bakım/Muayene badge.
- [ ] Yeni Araç: İç Kod zorunlu (hata); kaydet → liste yenilenir.

## Bakım Takibi
- [ ] Uyarı listesi: Gecikti=kırmızı, Kritik/Yaklaşıyor=turuncu, Güncel=yeşil (metin+badge); ilerleme %.

## Yakıt
- [ ] KPI (Depo Bakiyesi / Güncel Fiyat); dağıtım listesi (sayısal sağa hizalı).
- [ ] Depo Girişi (Litre+Fiyat) ve Dağıtım (Araç+Litre+Sayaç) kaydedilince bakiye/liste güncellenir.

## Talepler
- [ ] Liste + durum filtresi + arama; durum badge.
- [ ] Satır seçince detay (kalemler + durum geçmişi); duruma göre Gönder/Onayla/Reddet/İptal.
- [ ] Reddet'te gerekçe boşsa hata mesajı.

## Raporlar
- [ ] Rapor tipi + tarih filtreleri; Sorgula'dan önce "Rapor hazır" bilgisi.
- [ ] Stok Durumu → tablo + **pasta** (Düşük/Yeterli); Yakıt Tüketim → tablo + **bar** (araç/litre).
- [ ] Grafik hover tooltip; veri yoksa empty mesajı.

## Ayarlar
- [ ] Marka alanları yükleniyor; Kaydet → onay paneli → Onayla; Geri Al yeniden yükler.

## DPI / ölçek (1366×768, 1920×1080, %125, %150)
- [ ] Metin kesilmesi/buton taşması/menü kayması/kart daralması yok; dialog ekran içinde; tablo scroll çalışıyor.
