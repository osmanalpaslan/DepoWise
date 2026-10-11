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

## Arayüz sesleri (2026-10-11, kullanıcı seçimi) — hepsi CC0
- **Kullanım:** Bildirim sesleri. Dosyalar: `src/DepoWise.Desktop/Assets/Sounds/*.wav` ve
  `src/DepoWise.Web/wwwroot/sounds/*.wav` (iki ortamda aynı dosyalar; WAV'a çevrildi, seviye −16 dB'e indirildi).
  - `dugme-uyari` ← **UI SFX** (uisfx) `minimal/info` — https://github.com/romainsimon/uisfx (ses dosyaları CC0 1.0)
  - `bildirim` ← **akx/Notifications** `Polite`, `mesaj-gelen` ← `Glass` — https://github.com/akx/Notifications
    (CC BY 3.0 / CC0 ikili lisans; **CC0** seçildi — atıf gerekmez, yine de burada belirtilmiştir)
  - `mesaj-giden` ← **Kenney Interface Sounds** `drop_002` — https://github.com/Calinou/kenney-interface-sounds (CC0 1.0, kenney.nl)
- **Lisans:** CC0 — kamu malı; ticari kullanım, değiştirme ve dağıtım serbest, atıf zorunlu değildir.
- **Yeniden üretim:** `node scripts/ses_guncelle.mjs`
- Önceki set (2026-10-10, Octave — github.com/scopegate/octave) artık kullanılmıyor.

---
Lisans tam metni için ilgili paketlerin LICENSE dosyalarına / depolarına bakınız.
MIT lisansı, telif ve izin bildirimlerinin korunması koşuluyla kullanım/dağıtıma izin verir.
