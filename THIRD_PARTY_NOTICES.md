# Üçüncü Taraf Bildirimleri (THIRD_PARTY_NOTICES)

DepoWise aşağıdaki üçüncü taraf bileşenleri kullanır. Lisans metinleri ilgili paketlerin
deposunda/NuGet paketinde yer alır.

## LiveChartsCore.SkiaSharpView.Avalonia (2.0.5)
- **Kullanım:** Raporlar ekranındaki grafikler (araç bazında yakıt — bar; stok durum dağılımı — pasta).
- **Lisans:** MIT License — Copyright (c) 2021 Alberto Rodriguez Orozco.
- **Kaynak:** https://github.com/beto-rodriguez/LiveCharts2 (NuGet: LiveChartsCore.SkiaSharpView.Avalonia).
- **Not:** Kaynak repo (LiveCharts2-master.zip, v2.1.0-dev) solution'a dahil EDİLMEDİ; yalnız
  kararlı NuGet paketi **2.0.5** kontrollü PackageReference olarak eklendi (minimum, sürdürülebilir).
  Paket Avalonia 12.0.4 ile uyumlu restore oldu (sürüm çakışması yok).

### Transitif bağımlılıklar
- **LiveChartsCore (2.0.5)** — MIT (aynı proje).
- **SkiaSharp** — MIT License, Copyright (c) Microsoft Corporation / Xamarin Inc. (LiveCharts render motoru).

## Octave — arayüz sesleri (2026-10-10)
- **Kullanım:** Bildirim sesleri (onay/uyarı penceresi, sohbet gelen/giden mesaj, uyarı/duyuru).
  Dosyalar: `src/DepoWise.Desktop/Assets/Sounds/*.wav` ve `src/DepoWise.Web/wwwroot/sounds/*.wav`
  (beep-brightpop, beep-xylo, slide-paper, beep-piano → WAV'a çevrildi, seviye dengelendi).
- **Kaynak:** https://github.com/scopegate/octave — "A free library of UI sounds, handmade for iOS" (Fred Showell / Raised Beaches).
- **Lisans:** kişisel, açık kaynak ve ticari kullanım ücretsiz; atıf zorunlu değildir (yine de burada belirtilmiştir).
  Setin tamamını satmak, barındırmak veya kiralamak yasaktır — uygulamada yalnız 4 ses gömülüdür.
- **Yeniden üretim:** `node scripts/ses_guncelle.mjs`

---
Lisans tam metni için ilgili paketlerin LICENSE dosyalarına / depolarına bakınız.
MIT lisansı, telif ve izin bildirimlerinin korunması koşuluyla kullanım/dağıtıma izin verir.
