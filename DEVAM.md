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

## 2. ŞU AN NEREDEYIM? (son güncelleme: 2026-07-09)

**Genel durum:** Backend + iş mantığı **yayın adayı (1.0.0-rc)** olgunlukta — 17 fazın hepsi
bitti, 238 test yeşil. Şu an **UI bağlama + canlı yayın cilası** aşamasındayım (yayın engellerini
kapatıyorum). Web + API canlıda (`depowise-erp.fly.dev`, `depowise-web.fly.dev`); masaüstü paketi **1.0.34**
(yeni: 1.0.35 yerelde hazır, bkz. aşağı).

**Bugün (2026-07-09), yeni bilgisayara geçiş sonrası:**
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

## 3. SIRADAKI TEK IŞ

> Kullanıcı komutu olmadan yeni faza/işe kendiliğinden başlama (CLAUDE.md §1 kuralı).

1. **Masaüstü 1.0.35 paketini web'den yayınla (SEN yapmalısın — Süper Admin girişi gerekir):**
   - Paket hazır: `artifacts/rc/DepoWise-desktop-1.0.35.zip` (bu makinede, gitignore'lu — repo'ya girmez).
   - `https://depowise-web.fly.dev/releases` sayfasına Süper Admin ile giriş yap.
   - Sürüm: `1.0.35`, Notlar: (foto optimizasyonu, güvenlik sertleştirmesi, login/şube damgalama)
   - Dosya olarak yukarıdaki zip'i seç → **"Yayınla"** butonuna bas.
   - Masaüstü açık olan makineler 60 sn içinde otomatik günceleme uyarısı alır.
2. ~~Deploy bekleyenler~~ **TAMAMLANDI (09.07.2026):** `DEPOWISE_JWT_KEY` fly secret olarak ayarlandı,
   API (`depowise-erp`) + Web (`depowise-web`) yeniden yayınlandı ve doğrulandı (ikisi de HTTP 200).
   05.07 güvenlik/sync/oturum/updater değişikliklerinin tamamı artık canlıda.

**Senden girdi bekleyenler** (PROJE_REHBERI §6):
- Yönetici Raporları alt raporları hangileri olsun?
- Menü adı ↔ ekran başlığı hizalansın mı?

---

## 4. AÇIK YAYIN ENGELLERI (genel kullanıcı yayını öncesi)

- **R10:** Kalan operasyonel modül ekranlarının UI bağlanması (Malzemeler bağlı, gerisi sırada).
- **R8/R9:** Web oturum kalıcılığı + masaüstü/web login akışı (büyük kısmı 05.07'de bağlandı).
- **R4/R7:** Canlı PostgreSQL migration (SQLite tam; PG SQL üretildi ama uygulanmadı).
- **R22:** Code-signing (imzasız sürümde şeffaf uyarı var — maliyet kararı bekliyor).

> Tam açık/kapalı liste: [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md).

---

## 5. Çalıştırma / Güvenli Komutlar

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
