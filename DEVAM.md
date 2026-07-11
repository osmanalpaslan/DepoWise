# DEVAM — Nerede Kaldım? (Sıfır PC İçin Giriş Dosyası)

> **Bu dosya, hangi bilgisayarda olursam olayım açtığımda ilk okuduğum yerdir.**
> Amaç: format atsam, PC değiştirsem, aylar sonra dönsem bile "ne yaptık, sırada ne var"
> sorusunu tek bakışta cevaplamak. Teknik bilgi gerektirmez.
>
> **İki PC nasıl aynı kalır?** Her şey GitHub'da (`github.com/osmanalpaslan/DepoWise`).
> - **Başlarken:** Claude otomatik `git pull` yapar → en güncel hâli alır → bu dosyayı okur.
> - **Bitirirken:** Claude bu dosyayı günceller → `git commit` + `git push` yapar → diğer PC bir sonraki `git pull`'da aynısını görür.
> - Kural `CLAUDE.md` §0'da yazılı; her oturumda otomatik uygulanır. Sen bir şey ezberlemek zorunda değilsin.

---

## 1. Bu proje nedir? (tek paragraf)

**DepoWise** — çok firmalı (multi-tenant) depo/stok/araç/bakım/yakıt yönetim sistemi.
Üç parça, tek beyin: **Masaüstü** (Windows/.NET 8 + Avalonia, yerel SQLite) + **Web** (Blazor Server/.NET,
MudBlazor, tarayıcı) + **API** (sunucu, Fly.io, SQLite). İş kuralları ve yetkiler API'de tek yerde. Detaylı
çalışma mantığı: [docs/PROJE_REHBERI.md](docs/PROJE_REHBERI.md) (ortak defterimiz).
> Not: `apps/web` (eski Next.js denemesi) 2026-06-27'den beri donmuş; aktif web `src/DepoWise.Web`'dir (ADR-057).

---

## 2. ŞU AN NEREDEYIM? (son güncelleme: 2026-07-10, akşam)

> **✅ ÇÖZÜLDÜ — masaüstü .NET runtime hatası (bu PC'ye özeldi, kod değil):**
> Belirti: masaüstü apphost `.exe` çift tıklanınca "You must install or update .NET" (SDK+runtime kurulu olmasına rağmen).
> Kök neden: winget ile kurulan .NET, apphost'un runtime'ı bulması için gereken kaydı düzgün yapmamıştı.
> **Kalıcı çözüm:** Masaüstü kısayolu ("DepoWise (Gelistirme)") artık apphost `.exe` yerine **DLL'i doğrudan muxer ile**
> çalıştırıyor: hedef `C:\Program Files\dotnet\dotnet.exe`, argüman `"…\src\DepoWise.Desktop\bin\Debug\net8.0\DepoWise.Desktop.dll"`.
> Muxer runtime'ı daima bulur → hata yok (5 sn'de açılıyor, doğrulandı). Ayrıca `DOTNET_ROOT` (user+machine) ve registry InstallLocation da ayarlandı.
> **DİKKAT:** Bu kısayol DLL'i çalıştırır, kod değişince otomatik derlemez. Kod değiştirdikten sonra masaüstünü güncel görmek için önce:
> `dotnet build src/DepoWise.Desktop -c Debug` çalıştır (sonra kısayol yeni DLL'i açar). Bu sorun repoyu/diğer PC'leri etkilemez.

**Genel durum:** Backend + iş mantığı **yayın adayı (1.0.0-rc)** olgunlukta — 17 fazın hepsi
bitti, **243 test yeşil**. Şu an **UI bağlama + canlı yayın cilası** aşamasındayım. Web + API canlıda
(`depowise-erp.fly.dev`, `depowise-web.fly.dev`); masaüstü paketi **1.0.34** (1.0.35 henüz web'e yüklenmedi).

**Bugünkü büyük işler (hepsi canlıda + commit'li):** ADR-060 (MASAÜSTÜ süper admin login: "makine firması/şubesi ile giriş" kutucukları VEYA firma+şube seçimi; süper admin hiçbir koşulda engellenmez; seçilen firma yerele upsert + çapraz-firma oturumu — canlı deploy sürüyor), ADR-058 (süper admin firma seçimi + zorunlu şube + Tüm Şubeler),
ADR-059 (admin-atanmış makine şubesi + IP'den il; masaüstü: ana ekranda makine şubesi, çevrimdışı oto-giriş,
kullanıcı/makine şubesi yoksa giriş engeli). Masaüstü değişiklikleri **1.0.35 paketi yayınlanınca** görünür.
**Açık küçük iş:** Oze Group firmasının sunucuda 0 şubesi var (şubeler web-otoriteli; geçmişte masaüstünde kalmış) →
kullanıcı web'den "Şube/Şantiye" ekranından ekleyecek.

**Bugün (2026-07-10) — makine-şube modeli TAMAM (ADR-059), sunucu+web canlıda, masaüstü kodda:**
- **Web (canlı):** Admin makine ekranından her makineye **şube atıyor** (otoriter); makine login şubesini yazmıyor. **İl** sütunu (IP'den, best-effort).
- **Masaüstü (kodda; yeni pakette görünür):** ana sayfa **makine şubesini** gösterir; internet yoksa **makine şubesine otomatik giriş**; internet varsa şube seçimi; **kullanıcı veya makine şubesi yoksa giriş engellenir**; farklı-şube uyarısı admin-şubesine göre. Kullanıcı şubesi artık sunucudan senkron olur.
- Test 243/243 (+1). **Masaüstü GUI akışı gerçek makinede test edilmeli.** Görmek için 1.0.35 paketi yayınlanmalı (aşağı §3).

**Bugün (2026-07-10) — giriş (login) davranışı yeniden düzenlendi (ADR-058), canlıda:**
- **Web 3 adımlı giriş:** kimlik → (süper admin ise) FİRMA seçimi → şube (ZORUNLU). Süper admin seçtiği firmayı o firmanın admini gibi yönetir (operasyonel veriler o firmaya kapsamlanır — yerel API'de e2e doğrulandı). Yeni uç: `POST /api/auth/select-company`.
- **"Tüm Şubeler"** artık admin + süper admin'de daima açık (rapor için) — web + masaüstü.
- Firma izolasyonu (personel başka firmayı görmez) zaten sağlanıyordu (TenantAccessGuard); doğrulandı.
- Masaüstü: "Tüm Şubeler" kuralı eklendi; süper admin FİRMA seçimi masaüstünde YOK (çevrimdışı tek-firma yerel DB kısıtı). Bu değişiklik masaüstünde ancak **yeni paket** (1.0.35 sonrası) yayınlanınca görünür.
- API + Web yeniden yayınlandı ve doğrulandı (login sayfası 200, select-company ucu canlı).

**Önceki bugün (2026-07-09), yeni bilgisayara geçiş sonrası:**
- Proje bu makineye klonlandı; `dotnet build` (0 hata) ve `dotnet test` (238/238 yeşil) ile doğrulandı — geliştirmeye devam edilebilir.
- **Masaüstü 1.0.35 paketi yerelde toplandı** (`dotnet publish -c Release -p:Version=1.0.35`), zip'lendi, SHA-256 hesaplandı. **Henüz web'e yüklenip yayınlanmadı** — bu adım Süper Admin girişi gerektirdiği için tarayıcıdan elle yapılmalı (bkz. §3).
- Dokuman/gerçek mimari tutarsızlığı düzeltildi (ADR-057): `apps/web` (Next.js) donmuş olarak işaretlendi; gerçek/canlı web `src/DepoWise.Web` (Blazor/MudBlazor), sunucu DB'si SQLite (PostgreSQL hiç kullanılmadı).
- **API + Web güvenlik yeniden-yayını tamamlandı:** `DEPOWISE_JWT_KEY` fly secret olarak ayarlandı, her iki servis yeniden yayınlandı ve doğrulandı (HTTP 200). 05.07 güvenlik/sync/oturum/updater değişiklikleri artık canlıda.

**Önceki (2026-07-05):**
- **Grup 1 (login):** Masaüstü login'de şube kodu gösteriliyor; makinenin kendi şubesinde şifre sorulmuyor.
- **Grup 2 (şube damgalama):** Zorunlu şube seçimi + farklı şube seçilince netleştirilmiş uyarı.
- Güvenlik sertleştirmesi (JWT anahtarı zorunlu, seed şifre env/rastgele, login rate-limit,
  business-push yetki+doğrulama, JWT yenileme/kayan oturum, updater yedek+rollback).
- Çöp Kutusu gerçek yapıldı (parola ile), Canlı Sunucu grafik düzeltmesi, oturum düşünce tekrar-giriş uyarısı.

---

## 3. SIRADAKI TEK IŞ — Masaüstü 1.0.35'i yayınla (yarın, farklı PC'de)

> Kullanıcı komutu olmadan yeni faza/işe kendiliğinden başlama (CLAUDE.md §1 kuralı).
> Kullanıcı 09.07 gecesi ara verdi; yarın **farklı bir PC'den** devam edecek. Önce §0'daki
> "yeni PC'de ilk yapılacaklar" adımlarını uygula.

**Tek kalan iş: Masaüstü 1.0.35 paketini yayınlamak.** İki yol var:

**A) Otomatik (önerilen) — `scripts/publish_release.mjs` ile, tarayıcı gerekmez:**
   1. Yeni PC'de paket YOK (zip gitignore'lu, repoya girmez). Önce YENİDEN TOPLA:
      `dotnet publish src/DepoWise.Desktop/DepoWise.Desktop.csproj -c Release -o artifacts/rc/desktop-1.0.35 -p:Version=1.0.35`
      sonra klasörü zip'le (PowerShell: `Compress-Archive artifacts/rc/desktop-1.0.35/* artifacts/rc/DepoWise-desktop-1.0.35.zip`).
   2. Süper Admin bilgisini ortam değişkeni yap (kullanıcı kendi terminalinde):
      `setx DEPOWISE_ADMIN_USER "..."` ve `setx DEPOWISE_ADMIN_PASS "..."`
   3. Çalıştır: `node scripts/publish_release.mjs artifacts/rc/DepoWise-desktop-1.0.35.zip 1.0.35 "foto opt + guvenlik + login/sube"`
   4. Script login yapar, checksum'ı KENDİ hesaplar, yükler, sunucuda "latest = 1.0.35" doğrular.
   5. Bittiğinde ortam değişkenlerini SİL (şifre kalıcı kalmasın):
      `[Environment]::SetEnvironmentVariable("DEPOWISE_ADMIN_PASS",$null,"User")` (USER için de).

**B) Elle — web'den:** `https://depowise-web.fly.dev/releases` → Süper Admin girişi → Sürüm `1.0.35`,
   notlar, zip'i seç → **"Yayınla"**. (Bu da geçerli; Süper Admin girişi ister.)

> Her iki yolda da: yayından sonra masaüstü açık makineler 60 sn içinde otomatik güncelleme uyarısı alır.
> Not: 09.07'de bu adım Süper Admin şifresi bende olmadığı için tamamlanamadı — kullanıcının kimlik bilgisi lazım.

**(TAMAMLANDI 09.07.2026) Deploy:** `DEPOWISE_JWT_KEY` fly secret olarak ayarlandı, API (`depowise-erp`)
+ Web (`depowise-web`) yeniden yayınlandı ve doğrulandı (ikisi de HTTP 200). 05.07 güvenlik/sync/oturum/updater
değişikliklerinin tamamı artık canlıda.

**Senden girdi bekleyenler** (PROJE_REHBERI §6):
- Yönetici Raporları alt raporları hangileri olsun?
- Menü adı ↔ ekran başlığı hizalansın mı?

---

## 4. AÇIK YAYIN ENGELLERI (genel kullanıcı yayını öncesi)

- **R10:** Kalan operasyonel modül ekranlarının UI bağlanması (Malzemeler bağlı, gerisi sırada).
- **R8/R9:** Web oturum kalıcılığı + masaüstü/web login akışı (büyük kısmı 05.07'de bağlandı).
- **R4/R7:** (ADR-057) PostgreSQL'e geçilmedi; gerçek sistem uçtan uca SQLite. Artık "engel" değil — PostgreSQL sadece gelecek bir seçenek (karar kullanıcıya bırakıldı).
- **R22:** Code-signing (imzasız sürümde şeffaf uyarı var — maliyet kararı bekliyor).

> Tam açık/kapalı liste: [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md).

---

## 5. Çalıştırma / Güvenli Komutlar

**Yeni/temiz PC'de ilk kurulum (araçlar):** git, GitHub CLI (`gh`), .NET 8 SDK, Node.js, flyctl gerekir.
Windows'ta hepsi winget ile: `winget install Git.Git GitHub.cli Microsoft.DotNet.SDK.8 OpenJS.NodeJS.LTS Fly-io.flyctl`.
Sonra `gh auth login` (GitHub), `flyctl auth login` (deploy için), `git clone https://github.com/osmanalpaslan/DepoWise`.
`OPENAI_API_KEY`, `DEPOWISE_ADMIN_*` gibi ortam değişkenleri makineye özeldir — yeni PC'de yeniden ayarlanır.

- Bu makinede COMODO yok (2026-07-09'da yeni PC'ye geçildi) — EXE/BAT doğrudan çalıştırma yasağı kalktı (ADR-056). `dotnet` ile çalıştırma yine de önerilir.
- Masaüstü (senin makinen): uygulamayı kapat → **"DepoWise (Gercek DB)"** kısayolundan aç.
- Geliştirme derleme: `dotnet build DepoWise.sln`
- Test: `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj`
- Masaüstü çalıştır: `dotnet run --project src/DepoWise.Desktop`
- Web (Blazor, gerçek/aktif): `dotnet run --project src/DepoWise.Web`
- API (sunucu, yerel): `dotnet run --project src/DepoWise.Api`
- (`apps/web` eski Next.js denemesi — donmuş, kullanılmıyor; bkz. ADR-057)

---

## 6. Nereye Bakayım? (dosya haritası)

| İhtiyaç | Dosya |
|---|---|
| Ekranların çalışma mantığı + backlog (ortak defter) | [docs/PROJE_REHBERI.md](docs/PROJE_REHBERI.md) |
| Detaylı faz faz ne yapıldı | [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) |
| Açık/kapalı bilinen sorunlar (R-numaraları) | [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) |
| Alınan teknik kararlar (ADR) | [docs/DECISIONS.md](docs/DECISIONS.md) |
| Test kanıtları | [docs/TEST_EVIDENCE.md](docs/TEST_EVIDENCE.md) |
| Bağlayıcı analiz (ürün sözleşmesi) | [docs/DEPOWISE_ANALYSIS.md](docs/DEPOWISE_ANALYSIS.md) |
| Ana kurallar (Claude nasıl çalışır) | [CLAUDE.md](CLAUDE.md) |
