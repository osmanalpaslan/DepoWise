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

---

## Faz 1 — Güvenli çalışma alanı & baseline kanıtı (TAMAMLANDI)
**Tarih:** 2026-06-27 · **Tür:** Görsel/iş mantığı değiştirilmedi.

### Branch bilgisi
- Yeni çalışma dalı: **`ui/modern-depowise`** (`master`'dan, son commit `74a7b12`).
- Çalışma `master` üzerinde yapılmıyor; modernizasyon bu dalda izlenecek.

### Git durumu
- İzlenen (tracked) ağaç: **temiz** (değiştirilmiş/staged dosya yok).
- İzlenmeyen (untracked) **kullanıcı dosyaları korundu** (silinmedi/stash/reset/commit edilmedi):
  - `Tasarım Paketi/` (referans görseller + ZIP paketleri, ~65 MB)
  - `DepoWise_Claude_Code_UI_Modernizasyon_Promptlari.docx`
- Dal oluşturma riski yok: izlenmeyen dosyalar branch geçişinde otomatik korunur.

### Baseline build/test
- `dotnet build DepoWise.sln -c Debug` → **0 hata**.
- `dotnet test tests/DepoWise.Tests` → **188/188 geçti**. Faz 0 ile **tutarlı**.

### Baseline ekran ölçüsü
- Baseline görsel: `docs/ui-evidence/baseline/genel-ozet-baseline.png` — **1181 × 748 px**.

### Kullanılan Windows ölçek oranı
- Sistem DPI **96** → **%100 ölçek**. Sanal ekran 1920×1080.

### Baseline kanıt yöntemi (ekran görüntüsü neden otomatik alınamadı)
- **Neden:** COMODO Auto-Containment, geliştirme makinesinde imzasız proje EXE'sini izole eder; uygulama yalnız kullanıcı tarafından `dotnet` host kısayoluyla çalıştırılır. Asistan uygulamayı çalıştırıp otomatik ekran görüntüsü **üretemez** (CLAUDE.md §0).
- **Çözüm (kullanıcıdan ekran görüntüsü istemeden):** Kullanıcının daha önce sağladığı **mevcut durum** görüntüsü (`Tasarım Paketi/Referanslar/DepoWise-Mevcut.png`, "Genel Özet" ekranı) baseline kanıtı olarak `docs/ui-evidence/baseline/genel-ozet-baseline.png` adıyla kopyalandı. Sonraki fazlarda "after" görüntüleri kullanıcı aynı yöntemle (dotnet host) sağlayıp `docs/ui-evidence/<faz>/` altına eklenecek; karşılaştırma bu baseline ile yapılacak.

### Başlangıç riskleri
- **Büyük izlenmeyen dosyalar** (`Tasarım Paketi/*.zip` ~65 MB) yanlışlıkla commit edilebilir → ilgili fazda `.gitignore` ile koruma önerilir (bu faz kapsamı dışı, yalnız not).
- Ekran görüntüsü otomasyonu yok (COMODO) → görsel doğrulama kullanıcı-destekli.
- ViewLocator isim kuralı + `Brand.*` tema anahtarları + `NavigateCommand` bağlamaları kırılmamalı (bkz. AUDIT §6).
- Emoji→Lucide ve ikon rayı eklenmesi mevcut menü binding'lerini etkilemeden yapılmalı.

**Bu faz tamamlandı; sonraki faza geçmedim.**

---

## Faz 4 — Genel Özet / Dashboard modernizasyonu (TAMAMLANDI)
**Tarih:** 2026-06-27 · **Dal:** `ui/modern-depowise` · İş verisi/sayımlar ve VM mantığı korundu.

### Yapılanlar
- **Başlık hiyerarşisi:** içerikteki tekrar eden büyük başlık kaldırıldı; üst bar = sayfa başlığı, içerikte küçük "ÖZET"/"KRİTİK UYARILAR" bölüm etiketleri.
- **Ortak metrik kartı** (`Border.Kpi` + `KpiValue/KpiLabel`): eşit yükseklik (104), 12px köşe, ince kenar + hafif elevation, küçük/ikincil etiket, büyük güçlü değer, ikon placeholder chip (emoji yok).
- **Yalnız ilk kart mavi** (`Classes.primary` = `KpiCard.Primary`); diğer 4 kart nötr koyu yüzey. **Beş metrik korundu** (Toplam Araç / Malzeme Çeşidi / Düşük Stok / Bekleyen Talep / Aktif Personel); değerler mevcut binding'den (sahte veri yok).
- **Responsive:** `WrapPanel` + sabit min/again genişlik → geniş ekranda tek satıra yaklaşır, dar ekranda min genişlikle wrap; içerik kaybolmaz.
- **Kritik Uyarılar yeniden tasarım:** satır = sol renk çubuğu (kritik=kırmızı / değilse turuncu, `AlertBrush`) + ikon placeholder + başlık + detay (oran/sayaç detayda).
- **Empty-state kompakt:** yeşil ✓ + "Aktif kritik uyarı yok." (büyük gri kutu yok, gereksiz yükseklik yok).
- **Durum modeli (minimum):** `IsLoading/LoadError/HasError/IsLoaded` eklendi (iş mantığı değişmeden); hata durumunda kırmızı kenarlı banner; boş veri empty-state. Sahte etkileşim eklenmedi (uyarı satırında gerçek komut yok → tıklama yok).
- Margin/padding **tasarım token'larından** (Space/Pad/Radius/Shadow). Emoji ikonlar kaldırıldı.

### Değiştirilen/eklenen dosyalar
- `Views/DashboardView.axaml` (yeniden tasarım), `ViewModels/DashboardViewModel.cs` (KpiCard: Icon kaldırıldı + Primary eklendi; min durum modeli)
- `Themes/Controls.axaml` (Border.Kpi/.primary, KpiValue/KpiLabel, IconChip, AlertRow)
- `docs/ui-evidence/phase-04-dashboard/README.md`

### Komutlar
- `dotnet build src/DepoWise.Desktop` → 0 uyarı/hata
- `dotnet build DepoWise.sln` + `dotnet test` → 0 hata, **188/188**

### Build/Test sonuçları
- Build: **0 uyarı / 0 hata** · Test: **188/188 geçti** · Dashboard smoke: kart/uyarı/empty-state kod düzeyinde doğrulandı; görsel smoke kullanıcı (dotnet host).

### Ekran görüntüleri
- `docs/ui-evidence/phase-04-dashboard/` (1366×768, 1920×1080, mevcut) — **kullanıcı tarafından** (COMODO; talimat README'de).

### Bilinen sorunlar / sonraki faza bırakılanlar
- İkon placeholder chip'leri Faz 5'te Lucide ile gerçek ikon olacak.
- **Uyarı şiddeti** şu an 2 seviye (kritik/diğer = kırmızı/turuncu); 4 seviye (yüksek/turuncu, yaklaşan/sarı, bilgi/yeşil) için `DashboardAlert`'e bir `Severity` alanı gerekir → **öneri** (bu fazda Application modeli/iş mantığı değiştirilmedi).
- Uyarı satırına detay/aksiyon komutu (NavigateKey) ileride bağlanabilir (şu an gerçek komut yok, sahte eklenmedi).

**Bu faz tamamlandı; sonraki faza geçmedim.**

---

## Faz 3 — Uygulama kabuğu modernizasyonu (TAMAMLANDI)
**Tarih:** 2026-06-27 · **Dal:** `ui/modern-depowise` · İçerik ekranlarının işlevleri değiştirilmedi.

### Yapılanlar
- **Çift katmanlı sol menü:** ikon rayı (56px) + açıklamalı accordion panel (210px). Üst bar 60px; içerik dış boşluk içerik ekranlarında.
- **Navigasyon binding'leri korundu:** `NavigateCommand`, `GoDashboardCommand`. Eklenenler: `SelectGroupCommand` (raydan grup seç → aç + birincil hedefe git), `ToggleNavPanelCommand` (panel daralt/genişlet), `CurrentContext`, `IsNavPanelOpen`, `Initial`.
- **Seçili durum:** `NavLinkVm.IsActive` / `NavGroupVm.IsActive` → mavi vurgu (ray + grup), alt menüde koyu seçili satır + sol accent çubuğu; hover/pressed; chevron aç/kapa; klavye odağı (`:focus-visible`).
- **Üst bar:** ☰ (panel toggle) + sayfa başlığı + bağlam metni + **Eşitle** (mevcut yer tutucu buton korundu, sahte sync yazılmadı) + kullanıcı adı/avatar.
- **Responsive:** panel `IsVisible` ile kontrollü daralır (Auto kolon → 0); içerik ayrı kolonda, kaybolmaz. Metinlerde `TextTrimming` ile taşma önlendi.
- **Marka:** yalnız "DepoWise"; ALPDEP türevi yok. Native başlık çubuğu korundu (özelleştirilmedi).
- İkon rayında **geçici emoji ikon** (Faz 5'te Lucide ile değişecek; Eşitle'den emoji kaldırıldı).
- Renkler semantik token'lardan (SidebarBackground/TopBarBackground/Accent/AccentSoft/SelectedRow/Border...).

### Değiştirilen/eklenen dosyalar
- `Views/MainWindow.axaml` (kabuk yeniden tasarım), `ViewModels/ShellViewModel.cs`, `ViewModels/Navigation.cs` (NavLinkVm + IsActive)
- `App.axaml` (NavRail/IconGhost/aktif/odak stilleri), `Themes/Palette.axaml` (AccentSoft/SelectedRow)
- `docs/ui-evidence/phase-03-shell/README.md`

### Komutlar
- `dotnet build src/DepoWise.Desktop` → 0 uyarı/hata
- `dotnet build DepoWise.sln` + `dotnet test` → 0 hata, **188/188**

### Build/Test sonuçları
- Build: **0 uyarı / 0 hata** · Test: **188/188 geçti** · Manuel smoke: navigasyon/aktif-durum kod düzeyinde doğrulandı; görsel smoke kullanıcı (dotnet host).

### Ekran görüntüleri
- `docs/ui-evidence/phase-03-shell/` (1366×768 ve 1920×1080) — **kullanıcı tarafından dotnet host ile alınacak** (COMODO; talimat README'de).

### Bilinen sorunlar / sonraki faza bırakılanlar
- Lucide ikonları (Faz 5) — şu an rayda geçici emoji.
- İçerik ekranlarının (Dashboard/Materials) yeni semantik token'lara ve hedef yoğunluğa taşınması sonraki fazda.
- "Eşitle" gerçek sync komutu henüz ShellViewModel'de yok (yer tutucu); sync UI fazında bağlanacak.

**Bu faz tamamlandı; sonraki faza geçmedim.**

---

## Faz 2 — Tasarım sistemi & tema altyapısı (TAMAMLANDI)
**Tarih:** 2026-06-27 · **Dal:** `ui/modern-depowise` · **Tür:** Ekran yerleşimi/iş mantığı değiştirilmedi.

### Yapılanlar
- Avalonia ayrı tema kaynakları oluşturuldu ve App.axaml'e merge edildi:
  - `Themes/Palette.axaml` — 16 semantik renk token'ı (Color+Brush) koyu palet (referans hex).
  - `Themes/Scales.axaml` — boşluk (4–32), köşe (4/6/8/12), tipografi boyutları, sistem font (Segoe UI/Aptos/Inter fallback), elevation (hafif gölge).
  - `Themes/Controls.axaml` — sınıf-tabanlı temel stiller (TextBlock tipografi sınıfları, Button.Primary, Border.Card/.SurfaceElevated/.Divider).
- **Renkler ekranlara dağıtılmadı**; yalnız tema kaynağında (DynamicResource). Mevcut `Brand.*` çalışma-zamanı sistemi korundu (çakışma yok).
- Tüm yeni stiller **opt-in (Classes)** → mevcut kontroller etkilenmedi (global `Button`/`TextBlock` seçici yok).
- Tema önizleme view'ı eklenmedi (gerekçe: sahte ekran/ölü kod riski — bkz. UI_DESIGN_SPEC §6).

### Değiştirilen/eklenen dosyalar
- Eklendi: `src/DepoWise.Desktop/Themes/{Palette,Scales,Controls}.axaml`
- Düzenlendi: `src/DepoWise.Desktop/App.axaml` (yalnız tema kaynak/style include eklendi)
- Belge: `docs/UI_DESIGN_SPEC.md` (§6 Tasarım Sistemi), bu dosya

### Komutlar
- `dotnet build src/DepoWise.Desktop` → 0 hata, **0 uyarı**
- `dotnet build DepoWise.sln` + `dotnet test` → 0 hata, **188/188**

### Build/Test sonuçları
- Build: **başarılı, 0 uyarı** · Test: **188/188 geçti** (yeni uyarı yok)

### Ekran görüntüleri
- Üretilmedi (bu faz görsel yerleşim değiştirmez; yalnız tema altyapısı). Token'lar build ile doğrulandı.

### Bilinen sorunlar / sonraki faza bırakılanlar
- Ekranların yeni semantik token'lara taşınması ve `Brand.*` → semantik köprüleme sonraki fazlarda.
- Emoji→Lucide ikon sistemi sonraki fazda.
- İkon rayı + açıklamalı menü çift katmanı sonraki fazda.

**Bu faz tamamlandı; sonraki faza geçmedim.**
