# DepoWise UI Modernizasyon — İlerleme

Hedef: Mevcut işlev/iş kuralı/veri/sync/navigasyonu bozmadan arayüzü `DepoWise-Hedef.png` koyu tasarım diline yaklaştırmak. Ürün adı her yerde **DepoWise**. Framework: **Avalonia 12** (kanıt: UI_MODERNIZATION_AUDIT §1).

## Faz 0 — İnceleme & Spesifikasyon (TAMAMLANDI)
**Tarih:** 2026-06-27 · **Tür:** Salt okunur; üretim kodu değiştirilmedi.

### Yapılanlar
- Solution/proje/giriş noktası/View-VM/tema/navigasyon/DI/test envanteri çıkarıldı.
- UI framework **Avalonia 12** olarak kanıtlandı (Wpf.Ui kullanılmayacak).
- Tasarım Paketi ZIP'leri salt-okunur incelendi (lisans + framework uyumu): wpfui (MIT/WPF→ref), lucide (ISC, 1743 SVG), LiveCharts2 (MIT, Avalonia sürümü var).
- Referans görseller ölçülebilir tasarım kurallarına çevrildi.
- Risk/korunacak binding-command-servis listesi çıkarıldı.
- Baseline: build 0 hata, **188/188 test geçti**, ALPDEP üretimde yok.

### Üretilen belgeler
- `docs/UI_MODERNIZATION_AUDIT.md`
- `docs/UI_DESIGN_SPEC.md` (karar tablosu dahil)
- `docs/UI_MODERNIZATION_PROGRESS.md` (bu dosya)

### Komutlar
- `git grep -niE alpdep` (üretim) → temiz
- `dotnet build DepoWise.sln -c Debug` → 0 hata
- `dotnet test tests/DepoWise.Tests` → 188/188

### Build/Test sonuçları
- Build: **başarılı (0 hata)** · Test: **188/188 geçti**

### Ekran görüntüleri
- Üretilmedi (Faz 0 kod değiştirmez). NOT: COMODO nedeniyle uygulama EXE'si geliştiricide çalıştırılmaz; masaüstü ekran görüntüleri kullanıcı tarafından `dotnet` host kısayoluyla alınır.

### Bilinen sorunlar / sonraki faza bırakılanlar
- Emoji ikonlar → Lucide merkezi ikon sistemiyle değişecek (Faz ≥1).
- Sol menüde ikon rayı + açıklamalı menü çift katmanı henüz yok (hedefte var).
- `MainWindowViewModel` kullanılmayan şablon artığı.
- LiveCharts2/Lucide entegrasyonu ilgili fazlarda.

**Bu faz tamamlandı; sonraki faza geçmedim.**
