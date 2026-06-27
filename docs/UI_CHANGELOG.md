# DepoWise UI — Değişiklik Günlüğü (UI Modernizasyon)

Dal: `ui/modern-depowise`. Tüm fazlar `main`'i etkilemeden eklendi. İş mantığı/servis/test korundu (188 → 191).

## Faz 0 — İnceleme & Spesifikasyon
- Salt-okuma denetim; framework Avalonia 12 kanıtlandı; tasarım spec + token kararları.

## Faz 1 — Güvenli çalışma alanı
- `ui/modern-depowise` dalı; baseline build/test (188); kullanıcı değişikliklerine dokunulmadı.

## Faz 2 — Tasarım sistemi
- `Palette.axaml` (semantik renk), `Scales.axaml` (boşluk/köşe/tipografi), `Controls.axaml` (opt-in sınıf stilleri).

## Faz 3 — Uygulama kabuğu
- İkon rayı (56px) + açıklamalı accordion menü (210px) + üst bar (60px); aktif/hover/chevron/focus; daraltılabilir.

## Faz 4 — Genel Özet / Dashboard
- Ortak KPI kart; yalnız ilk kart vurgulu mavi; semantik uyarı satırları; kompakt empty-state; min durum modeli.

## Faz 5 — Ortak bileşen kütüphanesi
- Butonlar (Primary/Secondary/Ghost/Danger/Icon), inputlar (Field/Search/Combo/Numeric/Date), `StatusBadge`/`FormField`/`SectionHeader`/`Toolbar`/`StatePanel`, tablo deseni, dialog/loading/skeleton/toast stilleri, dev galeri.

## Faz 7a — Malzemeler
- Toolbar + tablo + stok badge (Düşük/Yeterli) + gruplu yeni kayıt formu (validation/Enter-Escape); mevcut komutlar korundu.

## Faz 7b — Araçlar / Bakım / Yakıt (sıfırdan)
- Servislere read-query (`VehicleService.List`, `FuelService.List*`) + DesktopServices bağlama.
- Araçlar: liste+durum/bakım-muayene badge+yeni araç. Bakım: GetAlerts uyarı listesi. Yakıt: KPI+liste+depo/dağıtım formları.

## Faz 7c — Talepler / Raporlar / Tanımlar-Ayarlar (sıfırdan)
- `RequestService.List/GetItems` read-query.
- Talepler: liste+durum filtresi+detay+onay/ret/iptal. Raporlar: filtre+tablo+grafik container. Ayarlar: Marka bölümü+hassas onay+Kaydet/Geri Al.

## Faz 8 — LiveCharts2 grafikler
- `LiveChartsCore.SkiaSharpView.Avalonia 2.0.5` (MIT). Yakıt→bar, Stok→pasta; gerçek `TableModel` verisi; tema palet; animasyon kapalı; MaxBars=20. THIRD_PARTY_NOTICES.

## Faz 9 — Dayanıklılık / Erişilebilirlik / DPI
- İkon butonlara `AutomationProperties.Name`; focus-visible beyaz halka; `TextMuted` kontrast (#929DAD). Leak/perf doğrulandı.

## Faz 10 (bu) — Final doğrulama & teslim
- Temiz build **0 hata**; test **191/191**; denetim (ALPDEP yok, TODO yok, view'larda emoji yok); teslim belgeleri. Commit/push yapılmadı (kullanıcı onayı).
