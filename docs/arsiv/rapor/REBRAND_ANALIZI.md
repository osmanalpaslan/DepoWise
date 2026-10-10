# Marka Değişimi (Rebrand) Analizi — "DepoWise" → (yeni ad)

> Salt-okuma analiz (2026-07-26). Hiçbir dosya değiştirilmedi. Amaç: "DepoWise" adının geçtiği HER yeri
> çıkarıp, **kullanıcının gördüğü marka** (güvenle değişir) ile **projenin çalışan iç yapısı** (değiştirilirse
> bozar) ayrımını netleştirmek. Yeni ad + logo belirlenince bu belge uygulama checklist'i olur.

## 0. Büyük resim
- Tarama: **"depowise" (harf duyarsız) = 2366 kayıt / 570 dosya.**
- Bunun **%95+'ı iç kod adı** (`DepoWise.*` namespace + dosya yolları + `using`/`x:Class`). **Kullanıcı bunları
  hiç görmez** ve marka değişimi için bunlara DOKUNULMAZ (dokunmak dev, riskli bir refactor; görünür faydası yok).
- Gerçek "rebrand" 3 gruptur: **A) görünür metin**, **B) logo/görsel**, **C) teknik isimler** (çoğu KALMALI).

---

## GRUP A — Kullanıcının gördüğü marka METNİ  → DEĞİŞECEK (düşük risk)

### Web (`src/DepoWise.Web`)
- `Components/Layout/MainLayout.razor` — üst bar başlığı **"DepoWise Yönetim"** (satır 21) + logo `alt="DepoWise"` (20).
- `Components/Pages/Login.razor` — **"DepoWise Yönetim"** başlığı (17) + logo `alt="DepoWise"` (13) + `<PageTitle>Giriş — DepoWise`.
- **Sekme başlıkları** `<PageTitle>… — DepoWise</PageTitle>`: Alerts, Home ("DepoWise Yönetim"), CompanyPermissions,
  Machines, Releases, RolePermissions, MachineBackups, ServerStatus, Theme, Trash, Login (11 ekran).
- `Components/Pages/Backup.razor:19` — kullanıcıya gösterilen klasör metni "Belgeler\DepoWise_Yedekler".

### Masaüstü (`src/DepoWise.Desktop`)
- `Views/MainWindow.axaml:72` — üst bar **"DepoWise"** (SectionTitle).
- `Views/PhotoViewerWindow.axaml:5` — pencere başlığı **"DepoWise — Fotoğraf"**.
- `Views/AboutView.axaml` — Hakkında ekranı marka metni (kontrol edilecek).
- Pencere/taskbar başlıkları (MainWindow/LoginWindow `Title` — kod-arkası/axaml).

### Kurulum aracı (`src/DepoWise.Setup/Program.cs`)
- Pencere/etiket **"DepoWise Kurulum"** (36, 45, 122), tamamlanma mesajları (120–121).
- Masaüstü kısayolu **"DepoWise.lnk"** + açıklama "DepoWise" (199, 206).  *(kısayol adı görünür markadır)*

### Belge/çıktı + merkezî
- `Application/Requests/RequestPdfService.cs` — talep formu PDF marka/başlık (kontrol edilecek).
- `Infrastructure/Reporting/ExcelExportService.cs` — Excel çıktısı marka (varsa).
- **Merkezî marka:** `Application/Theming/Branding.cs` → `BrandingSettings.Default` **AppName/CompanyName = "DepoWise"**.
  Marka zaten `app_settings` (`brand.app_name` …) üzerinden firma-özel yüklenebilir tasarlanmış; birçok ekran yine de
  sabit yazıyor. **Öneri:** yeni adı buraya koy + sabit yazan yerleri buradan besle (gelecekte tek yerden yönetilir).

---

## GRUP B — Logo / görsel dosyaları  → DEĞİŞECEK

### Masaüstü — `src/DepoWise.Desktop/Assets/`
- `app-icon.png`, `app-icon-256.png`, `app-logo.ico` (exe + pencere ikonu), `login-bg.png`.

### Web — `src/DepoWise.Web/wwwroot/`
- `favicon.png`, `favicon.ico`, `logo.png` (giriş + üst bar), `login-bg.png`, `login-hero.png`.

*(Kapsam dışı: `apps/web/public/*` = donmuş Next.js; `Tasarım Paketi/`, `assets-incoming/design/` = referans görseller.
Shipping edilen yalnız yukarıdaki iki klasördür.)*

---

## GRUP C — TEKNİK isimler  → ÇOĞU KALMALI (değiştirmek yapıyı bozar / yüksek risk)

| Öğe | Nerede | Neden dikkat | Öneri |
|---|---|---|---|
| **Namespace / assembly** `DepoWise.*` | Tüm `.cs`, `.csproj`, `DepoWise.sln` | Kullanıcı görmez; değiştirmek 500+ dosyalık refactor, sıfır görünür fayda | **KALSIN** |
| **Yerel veri klasörü + DB** `%LOCALAPPDATA%\DepoWise\Data\…\depowise.db` | `Infrastructure/Database/AppPaths.cs` (`AppFolderName`) | Değişirse mevcut kurulumlar **yeni boş klasöre** bakar → **babanın yerel verisi görünmez olur** | **KALSIN** (ya da veri taşıma adımıyla) |
| **Kurulum klasörü / exe** `…\DepoWise\app`, `DepoWise.Desktop.exe` | `DepoWise.Setup/Program.cs`, csproj çıktı adı | Kurulum/kısayol/güncelleme buna dayanır | Tercihen **KALSIN** |
| **Fly uygulama adları** `depowise-erp`, `depowise-web` | `fly.toml`, `fly.web.toml` | = canlı **URL/DNS** (`depowise-web.fly.dev`). Değiştirmek yeni app + yeni deploy hedefi + DNS + secret taşıma demek | **Karar senin** (URL de değişecek mi?) |
| **Veritabanı/secret adları** Neon `depowise_prod`, `DEPOWISE_PG_URL`, disk `depowise_data`/`/data`, `depowise-server.db` | fly secret + kod | Değiştirmek migration/secret işi, canlı riski | **KALSIN** |
| **Env/secret** `DEPOWISE_*` (ADMIN/SEED/PG…) | kod + fly secrets | Kod ve secret birlikte değişmeli; gereksiz | **KALSIN** |
| **GitHub repo** `osmanalpaslan/DepoWise` | git remote | İstersen sonra yeniden adlandırılır (remote güncellenir) | Opsiyonel, sonra |
| **Yedek klasörü** "Belgeler\DepoWise_Yedekler" | masaüstü yedek | Değişirse eski yedekler eski klasörde kalır | Karar senin |

---

## Strateji (önerilen sıra)
1. **Yeni ad + logo** belirle (metinlerde birebir bu yazılacak; logolar Grup B dosyalarının yerine konacak).
2. **Grup A + B**'yi değiştir → kullanıcı için "rebrand" budur. Düşük risk, çalışan yapı bozulmaz.
3. **Grup C**'yi **koru** (iç adlar) — tek olası istisna: **Fly URL'leri** (web linki) değişecek mi? Evetse bu AYRI,
   planlı bir iş (yeni fly app + DNS + secret taşıma + masaüstü `update.server_url` güncellemesi).
4. Mümkünse görünür metinleri `BrandingSettings` üzerinden merkezileştir (gelecekte tek yerden marka).

## Senden karar bekleyenler
- **Yeni marka adı** ne? (Grup A metinlerine birebir yazılacak.)
- **Web adresi** (`depowise-web.fly.dev`) de değişsin mi, yoksa yalnız **görünen isim** mi? (URL değişimi ayrı iş.)
- **İç kod adı** `DepoWise.*` kalsın mı? (**Öneri: kalsın** — görünmez, değiştirmek risk + faydasız.)
- Yeni **logo dosyaları** hazır mı? (Grup B'deki adlarla birebir koyacağız: `logo.png`, `favicon.ico`, `app-logo.ico`, …)
