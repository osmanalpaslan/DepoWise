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

## 2. ŞU AN NEREDEYIM? (son güncelleme: 2026-07-12)

### 🟢 Tek bakışta güncel durum

| Ne | Durum |
|---|---|
| **Testler** | **294/294 yeşil** (`dotnet test`) |
| **Şema** | Migration **037** (son: firma yetki düzeyi Serbest/Admin/Süper Admin) |
| **API (sunucu)** | `depowise-erp.fly.dev` — **canlı**, health 200 · ⚠️ Adım 1 API değişikliği **deploy edilmedi** |
| **Web** | `depowise-web.fly.dev` — **canlı** · ⚠️ Adım 1 web değişikliği **deploy edilmedi** |
| **Masaüstü** | **1.0.47 yayında** (Adım 1 masaüstü değişikliği yeni pakette gidecek) |
| **Git** | temiz + `origin/master` ile senkron |
| **Bekleyen iş** | **VAR — büyük yetki/ekran promptu, Adım 1 bitti, Adım 2+ sırada** → [docs/YARIM_KALAN_ISLER.md](docs/YARIM_KALAN_ISLER.md) |

> **Bekleyen işleri her zaman [docs/YARIM_KALAN_ISLER.md](docs/YARIM_KALAN_ISLER.md)'den oku.**
> Kullanıcı "yarıda kalan işler ne?" diye sorduğunda bakılacak tek liste odur; her değişiklikte güncellenir.

### Bu oturumda yapılanlar (2026-07-12) — ADR-064 … ADR-074

**Kritik olaylar (ikisi de çözüldü, önlem alındı):**
- **ADR-064 — Süper admin kilitlenmesi:** Firma silme, o firmadaki *tüm* kullanıcıları pasife alıyordu; süper admin
  kendi firmasını silince sistemden tamamen kilitleniyordu ("kullanıcı adı veya parola hatalı"). Artık firma silme
  süper admini **asla** pasife almaz + sunucu açılışında pasif süper adminleri aktifleştiren **self-heal** var.
- **ADR-070 — TAM KESİNTİ: sunucu diski doldu.** `/data` (974 MB) %100 dolunca SQLite yazamadı → **login dahil tüm
  API 500**. Sebep: her masaüstü paketi ~85 MB ve eski paketler hiç temizlenmiyordu (11 paket = 892 MB).
  Eski paketler silindi (%100 → %36) + **otomatik saklama politikası** (en yeni 3 paket tutulur, `ReleaseStore.PruneOld`).
  ⚠️ **Disk dolması sessiz değil ÖLÜMCÜLdür.** Teşhis: `flyctl ssh console --config fly.toml -C "df -h /data"`.

**Özellik / hata işleri:**
- **ADR-067 — #6 Personel ekranı NİHAİ hâli (Fikir A):** personel + uygulama kullanıcısı **tek ekranda**
  ("Uygulama erişimi ver" → kullanıcı adı/şifre/rol; "Hesabı kaldır"). Koşullar: **☐ Saha personeli** kutucuğu ·
  hesap yoksa/açılmıyorsa **ve** kutucuk işaretsizse **uyarı penceresi** (işaretliyse hiç çıkmaz) ·
  **unvan sabit tanım + "+"** · mükerrer kişi uyarısı · bir personele tek hesap.
  *(Geçmiş: önce Fikir B — ayrı ekran — yapıldı, kullanıcı beğenmedi → A'ya dönüldü, koşullar korundu. ADR-065 geçersiz.)*
- **ADR-066 — Silinen şubeler her yerde listeleniyordu:** şubeler sunucu-otoriteli ama masaüstü yerel kopyası
  yalnız *upsert* ediliyordu → silinen şube yerelde kalıyordu. Artık her girişte sunucu şube listesi **aynalanır**.
- **ADR-068 — Firma silince 401 + firmalar yüklenmiyor:** süper admin **içinde çalıştığı** firmayı silince
  token'daki firma geçersiz kalıyor, sonraki her istek 401 dönüyordu. Artık silinmiş firmada **home firmaya düşer**
  (oturum yaşar); *hiç var olmayan* firmada fail-closed korunur.
- **ADR-069 — SİLMEDE WEB TAM OTORİTER:** web'de silinen kayıt makinelerin yerel DB'sinden de **düşer**
  (silme LWW'yi aşar) **ve** sunucuda silinen kayıt **cihaz push'uyla diriltilemez**. Silme dışındaki LWW korundu.
- **ADR-071/072 — Firmalar sunucu-otoriteli + OFFLINE-FIRST kuyruk:** masaüstünde eklenen/silinen firma web'e hiç
  ulaşmıyordu. Artık işlem **önce yerele** yazılır + **kuyruğa** (`sync_outbox`) alınır; internet gelince **sırayla**
  işlenir. Yeniden denemede **hata düşmez** (idempotent). **Eşitleme sırası: 1) firma → 2) sabit tanımlar → 3) iş kayıtları.**
- **ADR-073 — Kota "ONLINE":** inceleme sonucu **zaten kullanıcı bazında tekildi** (aynı kişi web+masaüstü = 1);
  düzeltilecek hata yoktu. Şart 4 testle sabitlendi + gerçek bir bellek sızıntısı giderildi.
- **ADR-074 — Marka logoları** (web + masaüstü): tam logonun opak beyaz zemini flood-fill ile şeffaflaştırıldı
  (kamyonun beyaz kabini korunarak), sembolden 7 boyutlu `.ico` üretildi, **`.exe` simgesi** (hiç ayarlı değildi) eklendi.
  **Kullanıcı isteği: logoların arkasında beyaz kutu OLMAYACAK — yalnız logo.**

> Daha eski oturumların ayrıntısı: `docs/DECISIONS.md` (ADR-056…063) ve `docs/PROJECT_STATE.md`.

---

## 3. SIRADAKI TEK IŞ

> **Büyük yetki/ekran promptu — Adım 3:** Firma Tanım ekranı: admin + normal kullanıcı sayısı **ayrı ayrı**
> girilsin (mevcut %20 admin kuralı kalkacak) + **makine kotası** aynı ekrana eklensin. Tüm adımlar (3–7):
> [docs/YARIM_KALAN_ISLER.md](docs/YARIM_KALAN_ISLER.md) §A.
>
> **Adım 1 + 2 bitti** (test 294/294): Sync kaldırıldı · Talep→Form/Onaylama · Kısıtlı Süper Admin rolü +
> delegasyon tavanı + süper-admin-only reorg + Firma Yetki Kontrol 3-düzey. ⚠️ **Deploy edilmedi**
> (kullanıcı kararı: sonraki web işiyle birlikte; şema 035→037, **API'yi de** deploy et).

**Bu oturumda yapılanlar (2. prompt, ADR-076…082):** silinen makine firması/şubesi girişe sunulmuyor ·
makine yönetiminde firma değiştirme · canlı sunucu ekranında disk + paket silme · web logosu düzeltildi ·
ilk açılış tema varsayılanları · personel ekranı "mevcut kullanıcıyı bağla" · firma yetki kontrol global kilit.

**Kullanıcıdan onay/geri bildirim bekleyenler:**
- Yeni **Personel ekranını** (tek ekranda hesap açma + saha kutucuğu + unvan "+") canlıda gözden geçirmesi.
- **Logo yerleşimi**: arka plansız hâliyle beğendi mi? (Koyu temada logo lacivert ağırlıklı olduğu için kontrast
  düşebilir — kullanıcı bunu bilerek arka planı istemedi. Şikâyet gelirse koyu tema için açık renkli logo varyantı gerekir.)

**Yeni iş geldiğinde:** önce `docs/YARIM_KALAN_ISLER.md`'ye ekle, sonra uygula, bitince oraya "Tamamlananlar"a taşı.

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

### Canlıya alma (deploy) — doğrulanmış komutlar

```bash
flyctl deploy --config fly.toml     --ha=false   # API  → depowise-erp.fly.dev
flyctl deploy --config fly.web.toml --ha=false   # Web  → depowise-web.fly.dev
curl -s -o /dev/null -w "%{http_code}" https://depowise-erp.fly.dev/health   # 200 bekle
```
> **API'yi de deploy etmeyi unutma** eğer `src/DepoWise.Api`, `Infrastructure` ya da migration değiştiyse —
> yeni web eski API'ye çarparsa 404/500 alır.

### Masaüstü paketi yayınlama (sürüm artır!)

```bash
dotnet publish src/DepoWise.Desktop/DepoWise.Desktop.csproj -c Release -r win-x64 \
  --self-contained true -p:UseAppHost=true -p:Version=1.0.47 -o artifacts/rc/desktop-1.0.47
# PowerShell: Compress-Archive -Path "artifacts\rc\desktop-1.0.47\*" -DestinationPath "artifacts\rc\DepoWise-desktop-1.0.47.zip" -Force
node scripts/publish_release.mjs artifacts/rc/DepoWise-desktop-1.0.47.zip 1.0.47 "sürüm notu"
```
- Kimlik: `DEPOWISE_ADMIN_USER` / `DEPOWISE_ADMIN_PASS` **ortam değişkenlerinden** okunur (bu makinede kurulu).
- Script login olur, checksum'ı kendi hesaplar, yükler ve "en güncel sürüm" doğrulamasını yapar.
- Açık masaüstüler 60 sn içinde otomatik güncelleme uyarısı alır.
- Sunucu **en yeni 3 paketi** tutar (ADR-070); eskiler otomatik silinir.

### ⚠️ Sunucu diski (ADR-070 — tam kesinti yaşandı)

```bash
flyctl ssh console --config fly.toml -C "df -h /data"        # doluluk
flyctl logs --config fly.toml --no-tail | grep -i "disk is full"
```
Disk dolarsa SQLite yazamaz → **login dahil her uç 500 döner.** Çare: `/data/releases` altındaki eski
`.pkg` dosyalarını sil (en günceli koru).

---

## 6. Nereye Bakayım? (dosya haritası)

| İhtiyaç | Dosya |
|---|---|
| **Yarım kalan işler + testleri (sıradaki ne?)** | [docs/YARIM_KALAN_ISLER.md](docs/YARIM_KALAN_ISLER.md) |
| Ekranların çalışma mantığı + backlog (ortak defter) | [docs/PROJE_REHBERI.md](docs/PROJE_REHBERI.md) |
| Detaylı faz faz ne yapıldı | [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) |
| Açık/kapalı bilinen sorunlar (R-numaraları) | [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) |
| Alınan teknik kararlar (ADR) | [docs/DECISIONS.md](docs/DECISIONS.md) |
| Test kanıtları | [docs/TEST_EVIDENCE.md](docs/TEST_EVIDENCE.md) |
| Bağlayıcı analiz (ürün sözleşmesi) | [docs/DEPOWISE_ANALYSIS.md](docs/DEPOWISE_ANALYSIS.md) |
| Ana kurallar (Claude nasıl çalışır) | [CLAUDE.md](CLAUDE.md) |
