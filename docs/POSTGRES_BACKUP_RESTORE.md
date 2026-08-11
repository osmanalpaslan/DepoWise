# Üretim PostgreSQL — Yedekleme ve Geri Yükleme Prosedürü

> Son güncelleme: **2026-08-11** (FAZ H · H-1 kararı) · İlgili: `docs/DEPLOYMENT.md`
>
> 🔒 **Bu dosyada hiçbir gerçek bağlantı bilgisi, parola veya token YOKTUR.** Bağlantı bilgisi yalnızca
> `DEPOWISE_PG_URL` secret'ından gelir; komutlarda **yer tutucu** kullanılır.

---

## 1. Neden bu prosedür var?

Sunucu veritabanı yedeği bugün tamamen **Neon'un sağlayıcı yedeğine** bağlıdır. Uygulama içindeki
`BackupService` **yalnız SQLite** içindir (`VACUUM INTO`) ve **masaüstü** yedeği alır; "Sunucu Yedekleri"
ekranı da masaüstü yedeklerinin sunucuya yüklenmesidir — **üretim PostgreSQL'inin yedeği değildir**.

**Karar (2026-08-11):** Yedek sorumluluğu yalnız sağlayıcıya bırakılmaz. Neon'un kendi yedeği
**yerinde kalır**; bu prosedür onun **yerine geçmez**, **ek operasyonel yedeğimizdir**.

Bu aşamada **bilinçli olarak** uygulama içine yeni bir yedekleme özelliği / web ekranı / otomatik iş
eklenmemiştir. Prosedür **manuel / CLI tabanlıdır**.

| | Neon sağlayıcı yedeği | Bu prosedür (bizim yedeğimiz) |
|---|---|---|
| Kim alır | Neon otomatik | Operatör (elle) |
| Nerede durur | Neon altyapısı | Bizim seçtiğimiz güvenli konum |
| Ne zaman | Sürekli (PITR/branch) | **Her deploy öncesi** + düzenli aralık |
| Amaç | Altyapı kaynaklı kayıp | Hatalı migration / hatalı işlem / sağlayıcı erişim kaybı |

---

## 2. Araç gereksinimi

`pg_dump` / `pg_restore`, PostgreSQL istemci araçlarıyla gelir. **Sunucu kurulumu gerekmez** —
taşınabilir "binaries" paketi yeterlidir (Windows'ta servis/registry kurulumu yapmadan çalışır).

> **Sürüm kuralı:** `pg_dump` sürümü, yedeklenen sunucunun sürümünden **eski olmamalıdır**.
> Sunucu sürümünü öğrenmek için: `SELECT version();`

FAZ H doğrulaması **PostgreSQL 16.4** istemci araçlarıyla yapılmıştır.

---

## 3. Yedek alma (BACKUP)

### 3.1 Biçim: `-Fc` (custom)
`-Fc` seçilir çünkü: sıkıştırılmıştır, `pg_restore` ile **seçmeli** geri yükleme yapılabilir,
`pg_restore -l` ile **içeriği yedeği açmadan** listelenebilir.

### 3.2 Komut (Windows / PowerShell)

Bağlantı bilgisi komut satırına **yazılmaz**; ortam değişkeninden okunur.

```powershell
# 1) Bağlantı bilgisini yalnız BU oturuma al (Fly secret'ından elle kopyalanır, dosyaya yazılmaz)
$env:PGPASSWORD = "<parola>"          # oturum kapanınca kaybolur
$stamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$out   = "D:\AlpnexYedek\depowise_prod_$stamp.dump"

# 2) Yedeği al
& "<pgsql-bin>\pg_dump.exe" --host=<host> --port=5432 --username=<kullanici> `
    --dbname=<veritabani> --format=custom --no-owner --file=$out

# 3) Bağlantı bilgisini oturumdan sil
Remove-Item Env:\PGPASSWORD
```

### 3.3 Yedeği DOĞRULA (atlanmaz)
Alınan dosyanın gerçekten okunabilir olduğunu, **veritabanına dokunmadan** kontrol edin:

```powershell
& "<pgsql-bin>\pg_restore.exe" -l $out | Measure-Object -Line   # nesne sayısı > 0 olmalı
Get-Item $out | Select-Object Length                            # boyut 0 olmamalı
```

Doğrulanmamış yedek, yedek sayılmaz.

### 3.4 Ne zaman alınır
- 🔴 **Her deploy öncesi** — deploy = migration (bkz. `DEPLOYMENT.md` §5).
- 🔴 Şema değiştiren (migration ekleyen) her sürümden önce.
- 🟠 Düzenli aralıkla (haftalık öneri) — sağlayıcı erişimi kaybedilirse elimizde bir kopya olsun.

### 3.5 Yedeğin saklanması
- Yedek dosyası **tüm firma verisini** içerir → depolandığı yer en az veritabanı kadar korunmalıdır.
- **Repoya konmaz**, e-posta/sohbet ile gönderilmez.
- Dosya adında yalnız tarih/saat bulunur; içinde parola yoktur ama **veri vardır**.

---

## 4. Geri yükleme (RESTORE)

> ⚠️ **EN ÖNEMLİ KURAL: Geri yükleme, ÜRETİM veritabanının ÜZERİNE yapılmaz.**
> Önce **yeni/boş bir veritabanına** geri yükleyip doğrulanır; ancak ondan sonra üretime geçiş
> **ayrı ve açık bir karar** olarak ele alınır.
>
> Bu dosyada üretim veritabanına yönelik `DROP DATABASE`, `DROP SCHEMA`, `TRUNCATE`, `DELETE` veya
> migration geri alma komutu **bilerek verilmemiştir**. Aşağıdaki komutlar **yeni oluşturulmuş, boş**
> bir hedef veritabanı içindir.

### 4.1 Boş hedefe geri yükleme (doğrulama amaçlı — güvenli)

```powershell
$env:PGPASSWORD = "<parola>"

# 1) YENİ ve BOŞ bir hedef veritabanı oluştur (mevcut hiçbir DB'ye dokunulmaz)
& "<pgsql-bin>\createdb.exe" --host=<host> --port=5432 --username=<kullanici> depowise_restore_dogrulama

# 2) Yedeği bu YENİ veritabanına aç
& "<pgsql-bin>\pg_restore.exe" --host=<host> --port=5432 --username=<kullanici> `
    --dbname=depowise_restore_dogrulama --no-owner "D:\AlpnexYedek\<dosya>.dump"

Remove-Item Env:\PGPASSWORD
```

`--no-owner` kullanılır: yedekteki sahiplik bilgisi hedefteki roller ile aynı olmayabilir.

### 4.2 Geri yüklenen veritabanını doğrula

```sql
-- Şema sürümü beklenen mi?
SELECT MAX(version) FROM schema_migrations;

-- Temel satır sayıları (kaynakla karşılaştırın)
SELECT (SELECT COUNT(*) FROM companies)  AS firma,
       (SELECT COUNT(*) FROM users)      AS kullanici,
       (SELECT COUNT(*) FROM materials)  AS malzeme,
       (SELECT COUNT(*) FROM vehicles)   AS arac;

-- Kritik indeks yerinde mi (G6-03 / Migration063)
SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_users_username';
-- Beklenen: ... (company_id, username) WHERE (is_deleted = 0)
```

### 4.3 Uygulamayla doğrula
Geri yüklenen veritabanına **izole bir API örneği** bağlanır (üretim örneği değil):

```powershell
$env:DEPOWISE_PG_URL      = "Host=<host>;Port=5432;Username=<kullanici>;Database=depowise_restore_dogrulama"
$env:DEPOWISE_JWT_KEY     = "<test-anahtari>"
$env:DEPOWISE_SERVER_DATA = "D:\AlpnexYedek\gecici-veri"
$env:ASPNETCORE_URLS      = "http://127.0.0.1:5499"
dotnet DepoWise.Api.dll
```

Kontrol: `GET /health` → 200 · giriş yapılabiliyor · bir liste ekranı veri gösteriyor · yeni bir kayıt
yazılabiliyor. Migration runner idempotenttir; geri yüklenmiş güncel bir veritabanında **hiçbir migration
tekrar uygulanmaz**.

Doğrulama bitince geçici veritabanı temizlenebilir — ancak **silme komutu bu dokümana bilinçli olarak
konmamıştır**; hedef adının yanlış yazılması geri dönülemez sonuç doğurur. Silme, adı iki kez teyit
edilerek elle yapılır.

### 4.4 Üretime dönüş (felaket senaryosu)
Üretimi geri yüklenmiş bir kopyadan devam ettirmek **prosedür değil, karardır**. Sırasıyla:

1. **DUR.** Yazma trafiğini kes (uygulamayı durdur) — aksi halde iki farklı gerçek oluşur.
2. Mevcut bozuk üretim veritabanının **yedeğini al** (bozuk da olsa kanıttır; silme).
3. Yedeği **yeni** bir veritabanına aç ve §4.2 + §4.3 ile doğrula.
4. `DEPOWISE_PG_URL` secret'ını **yeni veritabanına** yönlendir + redeploy.
   (Eski veritabanı **silinmez**; bir süre saklanır.)
5. Kullanıcılara veri kaybı penceresini bildir.

> Neon tarafında PITR/branch ile geri dönüş daha hızlı olabilir; iki seçenek olay anında karşılaştırılır.
> Bu prosedür, sağlayıcıya erişilemediğinde de elimizde bir yol olması içindir.

---

## 5. Yapılmayacaklar

- ❌ Üretim veritabanına `DROP DATABASE` / `DROP SCHEMA` / `TRUNCATE` / toplu `DELETE`.
- ❌ Üretim veritabanının **üzerine** doğrudan `pg_restore`.
- ❌ Migration'ı elle geri alma (`schema_migrations`'tan satır silme). Tercih **ileri düzeltmedir**
  (forward-fix); gerekiyorsa yedekten yeni bir veritabanına dönülür.
- ❌ Bağlantı dizesi / parolayı betiğe, repoya, dokümana veya sohbete yazmak.
- ❌ Doğrulanmamış yedeğe güvenmek.
- ❌ Test amaçlı `DEPOWISE_PG_TEST_CONFIRM` değişkenini üretim bağlantısıyla birlikte kullanmak
  (yıkıcı test kapısını açar — bkz. `DEPLOYMENT.md` §7).

---

## 6. Bu prosedür nasıl doğrulandı (2026-08-11, FAZ H)

Tamamen **izole** bir ortamda, **canlıya hiç bağlanmadan** uçtan uca çalıştırıldı:

| Adım | Sonuç |
|---|---|
| Taşınabilir PostgreSQL 16.4, `127.0.0.1:55432` (servis kurulumu yok) | ✅ |
| Migration 1 → 63 sıfırdan uygulandı | ✅ `schema_migrations` = 63 |
| Dolu **v62 → v63** yükseltmesi | ✅ satır sayıları ve alanlar birebir korundu |
| `pg_dump -Fc` | ✅ 197.121 bayt, 140 nesne |
| `pg_restore -l` ile yedek doğrulaması | ✅ |
| Hedef DB düşür → yeniden oluştur → `pg_restore` | ✅ çıkış kodu 0 |
| Geri yükleme sonrası satır sayıları | ✅ kayıpsız |
| Geri yükleme sonrası kısmi indeks (`ux_users_username`) | ✅ korunmuş |
| Geri yüklenen DB'de API açılışı | ✅ migration tekrarı yok (idempotent) |
| Geri yüklenen DB'de giriş + okuma + **yazma** | ✅ HTTP 200 |

Not: doğrulama PostgreSQL **16.4** ile yapılmıştır; üretimdeki Neon sürümü ayrıca teyit edilmelidir.
