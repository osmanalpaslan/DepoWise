# DepoWise / Alpnex — Deployment ve Ortam Değişkenleri

> Son güncelleme: **2026-08-11** (FAZ H) · Şema sürümü: **63** · Kaynak: bu dosyadaki her satır
> **koddan doğrulanmıştır** (varsayım yok); doğrulandığı dosya/satır her maddede yazılıdır.
>
> 🔒 **Bu dosyada HİÇBİR gerçek değer yoktur** — parola, JWT anahtarı, PostgreSQL bağlantı dizesi, token
> yazılmaz ve yazılmayacaktır. Yalnızca **değişken ADLARI** ve **nereden sağlanacağı** belirtilir.
> Gerçek değerler yalnızca Fly.io secret deposunda tutulur.

---

## 1. Bileşenler

| Bileşen | Proje | Nerede çalışır | Veritabanı |
|---|---|---|---|
| API | `src/DepoWise.Api` | Fly.io — `depowise-erp` | PostgreSQL (Neon) |
| Web (yönetim konsolu) | `src/DepoWise.Web` | Fly.io — `depowise-web` | Yok — yalnız API'yi tüketir |
| Masaüstü | `src/DepoWise.Desktop` | Kullanıcı bilgisayarı | Yerel SQLite |
| Kurulum aracı | `src/DepoWise.Setup` | Kullanıcı bilgisayarı | — |

---

## 2. Production API — ortam değişkenleri

Tümü `DepoWise.Api` tarafından okunur.

### `DEPOWISE_JWT_KEY` — 🔴 ZORUNLU · **SECRET**
- **Kullanım:** JWT imza anahtarı (`Program.cs:28-38`). Önce `Jwt:Key` yapılandırması, sonra bu değişken okunur.
- **Tanımlı değilse:** Production'da **uygulama AÇILMAZ** — `InvalidOperationException` fırlatır.
  Development'ta bilinen bir dev anahtarına düşer (yalnız geliştirme için).
- **Nereden:** `fly secrets set DEPOWISE_JWT_KEY=<rastgele-64-karakter>` (değeri asla repoya/dokümana yazma).

### `DEPOWISE_PG_URL` — 🟠 Fiilen zorunlu · **SECRET**
- **Kullanım:** Sunucu veritabanı bağlantısı (`ServerServices.cs:102-105`). Npgsql bağlantı dizesi biçimi.
- **Tanımlı değilse:** Uygulama **hata vermez** — sessizce `{DEPOWISE_SERVER_DATA}/depowise-server.db`
  SQLite dosyasına düşer. Bu, **bilinçli geri dönüş yolu**dur (ADR-057) ama üretimde yanlışlıkla
  silinirse sistem eski/boş SQLite'a döner ve **veri yokmuş gibi görünür**.
  ⚠️ Deploy sonrası bu değişkenin var olduğu mutlaka teyit edilmelidir (bkz. §8).
- **Nereden:** Fly secret. Geri dönüş: `flyctl secrets unset DEPOWISE_PG_URL` + redeploy.

### `DEPOWISE_SERVER_DATA` — 🟠 Zorunlu (yapılandırmada mevcut) · normal env
- **Kullanım:** Veri/dosya kök dizini — yüklenen fotoğraflar (`files/`), makine yedekleri (`backups/`),
  güncelleme paketleri (`releases/`) ve PG kapalıyken SQLite dosyası.
- **Tanımlı değilse:** Kod bir varsayılan dizine düşer; Fly'da kalıcı disk bağlanmadığı için
  **yeniden başlatmada dosyalar kaybolur**.
- **Nereden:** `fly.toml` → `[env] DEPOWISE_SERVER_DATA = "/data"` + `[[mounts]]` `depowise_data` → `/data`.
  Ayrıca `Dockerfile` içinde de `ENV DEPOWISE_SERVER_DATA=/data` olarak set edilir.

### `DEPOWISE_SEED_ADMIN_PASSWORD` — ⚪ Opsiyonel · **SECRET**
### `DEPOWISE_SEED_SUPERADMIN_PASSWORD` — ⚪ Opsiyonel · **SECRET**
- **Kullanım:** YALNIZCA ilk tohumlamada (`ServerServices.EnsureSeedAdmins`, `:175-195`).
  `admin` ve `superadmin` hesapları veritabanında **hiç yoksa** oluşturulurken kullanılır.
- **Tanımlı değilse:** 16 karakterlik **rastgele parola üretilir ve bir kez konsola yazılır**
  (`ServerServices.cs:211-219`). Log'u kaçırırsanız o parolayı bir daha göremezsiniz.
- **Her iki durumda da:** hesap `must_change_password=1` ile açılır → kullanıcı **ilk girişte kendi
  parolasını belirlemek zorundadır** (GUV-01).
- **Not:** Hesaplar zaten varsa bu değişkenler **hiçbir şey yapmaz** (mevcut parolayı DEĞİŞTİRMEZ).

### `DEPOWISE_ALLOW_RESET` — ⚪ Üretimde **TANIMLANMAMALI** · normal env
- **Kullanım:** `/api/admin/reset-data` ucunun güvenlik kapısı (`Program.cs:1198-1199`).
  Bu uç **tüm firmaların iş verisini siler**.
- **Tanımlı değilse:** Üretimde uç **403** döner (istenen durum). Development'ta zaten serbesttir.
- **Kural:** Üretimde `=1` yapılmaz. Geçici olarak gerekirse iş bitince **hemen geri alınır**.

### `DEPOWISE_PG_TEST_CONFIRM` — ⚪ YALNIZ TEST · normal env
- **Kullanım:** `PostgresTestGuard` güvenlik kapısı. Yıkıcı PostgreSQL testleri (`DROP SCHEMA public CASCADE`)
  ancak bu değişken tam olarak `EVET-BU-BOS-TEST-VERITABANI` ise çalışır.
- **Tanımlı değilse:** İlgili testler **atlanır** (üretim/CI için doğru davranış).
- ⚠️ **Production sunucusunda ASLA tanımlanmaz.** Kapının diğer koşulları (DB adında "test" geçmesi,
  public şemanın boş olması, ≤50 MB) da sağlanmadıkça testler yine çalışmaz — ama bu değişkeni
  üretimde tanımlamak koruma katmanlarından birini gereksiz yere zayıflatır.

---

## 3. Production Web — ortam değişkenleri

### `Api__BaseUrl` — 🔴 ZORUNLU · normal env
- **Kullanım:** Web'in konuşacağı API adresi (`DepoWise.Web/Program.cs:32`, yapılandırma anahtarı `Api:BaseUrl`).
  Web **iş kuralı taşımaz**; her şeyi bu API'den okur.
- **Tanımlı değilse:** `http://localhost:5224` varsayılanına düşer → canlıda **web hiçbir veri gösteremez**.
- **Nereden:** `fly.web.toml` → `[env] Api__BaseUrl = "https://depowise-erp.fly.dev"`.
  (`__` çift alt çizgi, .NET'te `Api:BaseUrl` anlamına gelir.)

---

## 4. Masaüstü

### `DEPOWISE_ENVIRONMENT` — ⚪ Opsiyonel · normal env
- **Kullanım:** Yerel SQLite yolunu seçer (`DesktopBootstrap.cs:19-25`).
- **Tanımlı değilse:** `Development` kabul edilir.

### Sunucu adresi — `serverurl.txt` ve build-time `ServerUrl`
Masaüstü, sunucu adresini şu sırayla çözer (`MachineGate.cs:112-120`, `OrgServerClient.cs:279`,
`ServerAuthClient.cs:322`, `BusinessSyncPushService.cs:298`, `BusinessSyncPullService.cs:130`,
`LookupSyncService.cs:175`, `AutoUpdateService.cs:92`, `ServerUserClient.cs:88`):

1. Uygulama klasöründeki **`serverurl.txt`** (varsa) — bu dosyayı **kurulum aracı yazar**.
2. Yoksa **kod içindeki varsayılan**: `https://depowise-erp.fly.dev`.

> ⚠️ Bunun pratik sonucu: `serverurl.txt` yoksa masaüstü **doğrudan canlı sunucuya** gider.
> Geliştirme/test sırasında masaüstünü çalıştırmadan önce bu dosyanın doğru adresi gösterdiğinden emin olun.

`serverurl.txt` publish çıktısında **bulunmaz**; kurulum sırasında oluşturulur.

### Kurulum aracının build-time sunucu adresi
`src/DepoWise.Setup/DepoWise.Setup.csproj` içinde `ServerUrl` MSBuild özelliği vardır; varsayılanı
`https://depowise-erp.fly.dev`. Derleme sırasında `AssemblyMetadata("ServerUrl", ...)` olarak exe'ye gömülür
ve `Setup/Program.cs:31-34` bunu okur.

Farklı bir sunucuya kurulum paketi üretmek için:

```bash
dotnet publish src/DepoWise.Setup/DepoWise.Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:ServerUrl=https://ORNEK-SUNUCU -o artifacts/setup
```

**Canlıya geçişte sunucu adresi değişecekse** iki şey birlikte ele alınmalıdır:

1. Setup'ın `-p:ServerUrl=...` değeri (kurulumda `serverurl.txt`'e yazılan adres).
2. Koddaki **varsayılan** adres — `serverurl.txt` yoksa devreye girer ve **7 ayrı dosyada tekrarlanır**:
   `MachineGate.cs:120` · `OrgServerClient.cs:283` · `ServerAuthClient.cs:330` · `ServerUserClient.cs:96` ·
   `BusinessSyncPullService.cs:138` · `BusinessSyncPushService.cs:306` · `LookupSyncService.cs:183`.
   ⚠️ Adres değişirse **yedisi birden** güncellenmelidir; biri atlanırsa o akış eski sunucuya gider.
   (Bu tekrar mevcut bir teknik borçtur; bu dokümanda yalnız kayda geçirilmiştir, değiştirilmemiştir.)

### Masaüstü publish

```bash
dotnet publish src/DepoWise.Desktop/DepoWise.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/rc/desktop-<surum>
```

- **Self-contained** — hedef makinede .NET kurulumu gerekmez.
- 2026-08-11 doğrulaması: **252 dosya, ~242 MB**, 0 hata.
- Temiz makine kurulumu için gereken tek dosya: **`AlpnexSetup.exe`** (paketi sunucudan indirir,
  klasöre kurar, `serverurl.txt` yazar, kısayol oluşturur).

---

## 5. Fly.io yapılandırması

| | API | Web |
|---|---|---|
| Uygulama adı | `depowise-erp` | `depowise-web` |
| Yapılandırma | `fly.toml` | `fly.web.toml` |
| Dockerfile | `Dockerfile` | `Dockerfile.web` |
| Bölge | `fra` | `fra` |
| İç port | 8080 | 8080 |
| Kalıcı disk | `depowise_data` → **`/data`** | `depowise_web_keys` → **`/dpkeys`** |
| `[env]` | `DEPOWISE_SERVER_DATA=/data` | `Api__BaseUrl=https://depowise-erp.fly.dev` |

- **`/data`** (API): fotoğraflar, makine yedekleri, güncelleme paketleri ve PG kapalıyken SQLite dosyası.
  ⚠️ Bu disk dolarsa **SQLite yazamaz ve TÜM uçlar 500 döner** (geçmişte yaşandı — bkz. `DECISIONS.md`).
- **`/dpkeys`** (Web): ASP.NET DataProtection anahtarları. Kaybolursa mevcut web oturumları/çerezleri geçersizleşir.

### Deploy sırası: **önce API, sonra Web**
Web hiçbir iş kuralı taşımaz; her şeyi uzak API'den çağırır. Bir servis/uç değiştiyse **yalnız web'i
deploy etmek yetmez** — API de deploy edilmelidir. Bu yüzden sıra: `fly.toml` (API) → `fly.web.toml` (Web).

### Migration deploy'da OTOMATİK çalışır
Ayrı bir migration adımı **yoktur**. API açılışında `ServerServices` yapıcısı `new MigrationRunner(Factory).Run()`
çağırır (`ServerServices.cs:106`). Runner (`MigrationRunner.cs`):

- yalnız **uygulanmamış** sürümleri **artan sırada** uygular,
- her migration'ı **tek transaction** içinde çalıştırır (hata → o migration tamamen geri alınır),
- uygulanan sürümleri `schema_migrations` tablosuna yazar → **idempotent** (yeniden başlatma zararsız).

Yani **deploy = migration**. Bu nedenle deploy öncesi yedek almak zorunludur (bkz. `POSTGRES_BACKUP_RESTORE.md`).

---

## 6. İlk kurulum (tohumlama) davranışı

`ServerServices.EnsureSeedAdmins()` her açılışta çalışır ama **yalnız eksik olanı** oluşturur:

1. `users` tablosu **tamamen boşsa** → `DEPOWISE` firması + `admin` kullanıcısı (Firma Admini).
2. Sistemde **hiç süper admin yoksa** → `superadmin` kullanıcısı (Süper Admin).
3. Parola: ilgili env değişkeni varsa o, **yoksa rastgele üretilip konsola yazılır**.
4. Her iki hesap da `must_change_password=1` → **ilk girişte kendi parolasını belirlemek zorunda** (GUV-01).
5. **Self-heal:** pasife düşmüş süper admin(ler) her açılışta yeniden aktifleştirilir — platform sahibi
   hiçbir koşulda kilitli kalmamalıdır.

---

## 7. Test ortamı

### PostgreSQL testlerini etkinleştirme
Varsayılan olarak PostgreSQL testleri **atlanır** (2026-08-11 itibarıyla 33 test). Çalıştırmak için:

```bash
DEPOWISE_PG_URL="Host=127.0.0.1;Port=55432;Username=<kullanici>;Database=depowise_test_<ad>" \
DEPOWISE_PG_TEST_CONFIRM="EVET-BU-BOS-TEST-VERITABANI" \
dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj --filter "FullyQualifiedName~Postgres"
```

`PostgresTestGuard` fail-closed bir kapıdır; **hepsi** sağlanmadıkça testler çalışmaz:

| # | Koşul |
|---|---|
| K1 | `DEPOWISE_PG_TEST_CONFIRM` tam olarak `EVET-BU-BOS-TEST-VERITABANI` |
| K2 | Veritabanı adında **`test`** geçmeli |
| K3 | `public` şema **tamamen boş** olmalı (ya da kapının kendi işaret şeması mevcut olmalı) |
| K4 | Veritabanı boyutu ≤ **50 MB** |
| K5 | Bağlantı salt-okunur replika olmamalı |

> 🔒 `DEPOWISE_PG_TEST_CONFIRM` **yalnızca test ortamına aittir**. Production sunucusunda tanımlanmaz;
> canlı bağlantı dizesiyle birlikte kullanılması **veri kaybına yol açar**.

### İzole PostgreSQL (canlıya dokunmadan)
FAZ H'de kullanılan yöntem: taşınabilir PostgreSQL binaries (servis/registry kurulumu yok), `initdb` +
`pg_ctl` ile `127.0.0.1` üzerinde standart dışı bir portta küme. Ayrıntı: `POSTGRES_BACKUP_RESTORE.md` §6.

---

## 8. Deploy kontrol listesi

**Öncesi**
- [ ] `pg_dump -Fc` ile yedek alındı ve doğrulandı (`POSTGRES_BACKUP_RESTORE.md`).
- [ ] `dotnet build` 0 hata, test paketi yeşil.
- [ ] Migration eklendiyse izole PostgreSQL'de denendi.

**Deploy**
- [ ] Önce API (`fly.toml`), sonra Web (`fly.web.toml`).

**Sonrası**
- [ ] `GET /health` → `{"status":"ok"}` (API canlı mı).
- [ ] Süper admin ile giriş → bir liste ekranı veri gösteriyor mu (PG bağlantısı gerçekten kuruldu mu).
      ⚠️ Bu adım kritik: `DEPOWISE_PG_URL` eksikse uygulama **hata vermeden** boş SQLite'a düşer.
- [ ] Web açılıyor ve API'den veri çekiyor mu.
- [ ] Masaüstü paketi yayınlandıysa bir makinede güncelleme akışı denendi mi.

---

## 9. Bu dosyanın kapsamadıkları
- **Sır rotasyonu** ayrı bir belgede yönetilir; bu dosya yalnız değişken adlarını listeler.
- **Yedek/geri yükleme prosedürü:** `docs/POSTGRES_BACKUP_RESTORE.md`.
- **Arıza/olay yönetimi:** `docs/OPERATIONS.md`.
