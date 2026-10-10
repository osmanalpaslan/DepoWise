# DepoWise UI Modernizasyon — Faz 0 İnceleme (Audit)

**Tarih:** 2026-06-27 · **Kapsam:** Salt okunur inceleme; üretim kodu değiştirilmedi.

## 1. UI Framework — KANIT
**Sonuç: Avalonia UI 12.0.4 (WPF DEĞİL).** Kanıtlar:
- `src/DepoWise.Desktop/DepoWise.Desktop.csproj` paketleri: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` (12.0.4) + `CommunityToolkit.Mvvm` 8.4.1.
- `Program.cs`: `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()` → klasik masaüstü Avalonia başlatma.
- Görünümler `.axaml` (Avalonia XAML), `AvaloniaUseCompiledBindingsByDefault=true`.
- TargetFramework `net8.0`, `OutputType WinExe`, `UseAppHost=false` (Debug, Directory.Build.props — COMODO).

**Karar gereği:** Proje Avalonia olduğundan **Wpf.Ui / WPF assembly bağlanmayacak.** Avalonia native tema/stil uygulanır.

## 2. Proje yapısı (Clean Architecture)
- `DepoWise.Domain` → `DepoWise.Application` → `DepoWise.Infrastructure` → `DepoWise.Desktop` (+ `tests/DepoWise.Tests`).
- İş kuralı/veri erişimi/sync Application+Infrastructure'da; Desktop yalnız UI. **MVVM korunmalı.**

## 3. Mevcut ekranlar (View ↔ ViewModel)
| Görünüm | ViewModel | İşlev |
|---|---|---|
| `Views/LoginWindow.axaml` | `LoginViewModel` | Giriş + "Beni Hatırla" (AuthService) |
| `Views/MainWindow.axaml` | `ShellViewModel` | Kabuk: sol menü + üst bar + içerik bölgesi |
| `Views/DashboardView.axaml` | `DashboardViewModel` | Ana ekran: KPI kartları + kritik uyarılar |
| `Views/MaterialsView.axaml` | `MaterialsViewModel` | Malzeme listesi + arama + yeni kayıt |
| `Views/PlaceholderView.axaml` | `PlaceholderViewModel` | Henüz bağlanmamış modüller (empty-state) |
| (kullanılmıyor) | `MainWindowViewModel` | Şablon artığı; aktif değil |

**ViewLocator** (`ViewLocator.cs`): VM tip adında `ViewModel`→`View` değişimiyle View bulur (namespace `ViewModels`→`Views`). Yeni ekranlar bu kurala uymalı.

## 4. Navigasyon, komutlar, veri bağlamı
- **Navigasyon:** `ShellViewModel.NavigateCommand(string key)` ve `GoDashboardCommand` (CommunityToolkit `[RelayCommand]`). İçerik `ContentControl.Content = {Binding CurrentPage}` (ViewModelBase) → ViewLocator çözer. `ActiveKey` (seçili vurgusu için, şu an XAML'de kullanılmıyor).
- **Menü modeli:** `ShellViewModel.Groups : IReadOnlyList<NavGroupVm>`; her grup `Icon`(emoji), `Title`, `ModuleKey`, `Children:NavLink(Title,Key)`, `IsExpanded`(observable). Görünürlük `AccessControl.CanSeeMenu(session, moduleKey)` ile (yetki).
- **Komut/binding envanteri (korunacak):**
  - Login: `LoginCommand`, `Username`, `Password`, `RememberMe`, `Error`, `IsBusy`, `OnLoggedIn`.
  - Shell: `NavigateCommand`, `GoDashboardCommand`, `CurrentPage`, `CurrentTitle`, `Welcome`, `DisplayName`, `Groups`, `ActiveKey`.
  - Dashboard: `Cards (KpiCard)`, `Alerts (DashboardAlert)`, `HasAlerts`, `AlertBrush` converter.
  - Materials: `LoadCommand`, `AddCommand`, `Search`, `NewCode/NewName/NewUnitPrice/NewMinStock`, `Items (MaterialRow)`, `CanWrite`, `Status`.
- **DI:** Container YOK. `DesktopServices` (statik servis tutucu): `Factory, Auth, Users, Materials, OpeningStock, Dashboard, Branding, Theme, Session`. İlk açılış admin seed + `DisplayName/ResolveCompanyId`. **Korunacak** (UI bunun üzerinden veri alır).
- **Açılış akışı (`App.axaml.cs`):** `DesktopBootstrap.Run()` (migration+health+tema) → `ThemeApplier.Apply` → `DesktopServices.Initialize` → `RememberMeService.TryAutoLogin()` → MainWindow veya LoginWindow.

## 5. Tema kaynakları (mevcut)
- **Tek doğru kaynak:** `DepoWise.Application.Theming.ThemeTokens.Default` (`Branding.cs`) + `SettingsService` (firma override). Renkler ekranlara sabit yazılmaz.
- **Runtime uygulama:** `Theming/ThemeApplier.Apply(app, tokens)` → `Application.Resources`'a yazar:
  - `Brand.Primary/OnPrimary/Surface/OnSurface/Accent/Danger/Warning/Success` (+ `.Brush`), `Brand.CornerRadius`.
  - Türetilen: `Brand.Panel(.Brush)` (Primary'den açık), `Brand.Border.Brush`, `Brand.Hover.Brush`.
- `App.axaml`: `RequestedThemeVariant="Dark"`, `<FluentTheme/>` + stiller: `Button.NavTop`, `ToggleButton.NavGroup` (+ `chev` rotasyon), `Button.NavLink`.
- **Mevcut palet (koyu):** Primary `#1E232C`, Surface `#161A21`, OnSurface `#E6E8EC`, Accent `#2563EB`, Danger `#DC2626`, Warning `#F59E0B`, Success `#16A34A`, radius 10.

## 6. Risk alanları (UI değişiminde KORUNACAK)
- `ViewLocator` isim kuralı (VM↔View).
- `NavigateCommand/GoDashboardCommand` ve `CommandParameter=Key` bağlamaları.
- `DesktopServices` servis erişimi + `Session` + ilk açılış seed.
- `RememberMeService` (DPAPI) otomatik giriş akışı + `AuthService.CreateSessionForUser`.
- `Brand.*` tema anahtarları (yeniden adlandırma kırar).
- `MaterialsViewModel` / `DashboardViewModel` public binding üyeleri.
- Mevcut testler (188) — gevşetilmeden korunacak.

## 7. Tasarım Paketi — paket değerlendirmesi (bkz. UI_DESIGN_SPEC §Karar Tablosu)
- **wpfui-main** (MIT, WPF): Avalonia projesinde **bağımlılık olarak kullanılmaz**; yalnız görsel/etkileşim referansı.
- **lucide-main** (ISC, 1743 SVG, 24×24 stroke): yalnız kullanılan ikonlar, lisans korunarak, merkezi ikon sistemiyle.
- **LiveCharts2-master** (MIT): Avalonia sürümü mevcut (`LiveChartsCore.SkiaSharpView.Avalonia`). Yalnız anlamlı rapor/grafiklerde.

## 8. Baseline (Faz 0 anı — DÜZELTME YAPILMADI)
- `git grep alpdep` (üretim) → **temiz** (arayüz/koda ALPDEP* yok).
- `dotnet build DepoWise.sln -c Debug` → **0 hata**.
- `dotnet test` → **188/188 geçti**.
- Bilinen kozmetik: `MainWindowViewModel` kullanılmayan şablon artığı; emoji ikonlar (Lucide ile değişecek); üst pencere çubuğu native (Mevcut görselde açık şerit).
