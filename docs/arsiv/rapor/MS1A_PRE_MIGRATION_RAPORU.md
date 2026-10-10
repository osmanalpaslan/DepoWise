# M-S1a — CANLI MIGRATION ÖNCESİ RAPOR (onay bekliyor)

- **Tarih:** 2026-08-09
- **Durum:** kod + testler HAZIR · **canlı veritabanına HİÇBİR YAZMA YAPILMADI**
- **Bu raporda hiçbir bağlantı adresi, kullanıcı adı, parola veya API anahtarı yer almaz.**

---

## 1. Neden yapılıyor (tek cümle)

`material_request_items` (talep kalemleri) ve `maintenance_materials` (bakım malzemeleri) tablolarında
**firma bilgisi yok**. Eşitleme, firma süzgecini yalnız firma kolonu olan tablolara uygulayabildiği için
bu iki tablo **süzgeçsiz** taşınıyor — ikinci bir firma aktifleştiğinde bir firmanın kalemleri diğerinin
bilgisayarına gidebilir. Kolon eklenince aynı kod otomatik olarak hem süzer hem de yazarken firmayı zorlar.

### Bu risk artık teorik değil
Canlı veritabanında **3 firma** var (biri aktif: *Oze İnşaat*; ikisi pasif: *DEPOWISE*, *Oze Group*) ve
pasif firmalarda **gerçek veri duruyor** (DEPOWISE: 2459 malzeme, 94 araç, 663 stok hareketi).
Bugüne kadar sızıntı **oluşmadı**, çünkü bu iki tablodaki toplam kayıt sayısı çok küçük ve hepsi tek firmaya ait.
Yani **şu an müdahale etmek için en ucuz an.**

---

## 2. Salt-okuma canlı denetim (yapıldı)

| Kontrol | Sonuç |
|---|---|
| Bağlantı biçimi | Oturum + her sorgu `READ ONLY` |
| **Yazma denemesi kanıtı** | PostgreSQL **reddetti — SqlState 25006** (read-only transaction) ✅ |
| Veritabanı | `depowise_prod` · PostgreSQL 17.10 |
| Mevcut şema sürümü | **61** (Migration062 henüz uygulanmadı) |

### Etkilenecek tabloların BUGÜNKÜ hâli

| Tablo | Toplam satır | Yetim | Üstünün firması boş | Firması bilinmeyen | **Çözülebilir** |
|---|---|---|---|---|---|
| `material_request_items` | **2** | 0 | 0 | 0 | **2 / 2** |
| `maintenance_materials` | **0** | 0 | 0 | 0 | **0 / 0** |

**Çözülemeyen kayıt: 0.** Migration'ı durduracak hiçbir durum yok.

### Her kaydın hangi firmaya taşınacağı (tam liste — canlıda toplam 2 satır)

| Kalem (id) | Bağlı talep | Miktar | → `company_id` |
|---|---|---|---|
| `808fed13…` | `2f9ffe69…` (TLP-2026-0001, taslak) | 1 | `ed271d0ca2b04a73b97f5025a53a04b4` (**Oze İnşaat**) |
| `0e7c4a0c…` | `22cddd20…` (TLP-2026-0002, taslak) | 1 | `ed271d0ca2b04a73b97f5025a53a04b4` (**Oze İnşaat**) |

`maintenance_materials` boş olduğu için taşınacak satır yok.

**Migration sonrası beklenen sayılar:** `material_request_items` = **2** (değişmez),
`maintenance_materials` = **0** (değişmez), boş `company_id` = **0**, yanlış firma eşleşmesi = **0**.

---

## 3. Ne değişecek

### Şema
| Tablo | Eklenen kolon | Eklenen indeks |
|---|---|---|
| `material_request_items` | `company_id TEXT NOT NULL` (**varsayılan YOK**) | `ix_material_request_items_company(company_id)` |
| `maintenance_materials` | `company_id TEXT NOT NULL` (**varsayılan YOK**) | `ix_maintenance_materials_company(company_id)` |

Mevcut kolonlar, mevcut indeksler ve mevcut FK'ler **aynen korunur**. Hiçbir kolon silinmez/yeniden adlandırılmaz.

### Sorulan tasarım kararlarının cevapları

| Soru | Karar | Gerekçe |
|---|---|---|
| NOT NULL? | **Evet** | Boş bırakılabilirse garanti yok; korunmak istenen durumun ta kendisi. |
| Varsayılan (DEFAULT)? | **Hayır** | Varsayılan, firma atamayı unutan bir kaydı sessizce yanlış/boş firmaya bağlardı. Varsayılan yokken hata **anında** görünür. |
| Veri nasıl doldurulacak? | **Gerçek üst kayıttan** | Kalem → talep → firma · bakım malzemesi → bakım → firma. Tahmin/varsayılan YOK. |
| FK (companies)? | **Hayır** | Üst kayıt zaten `companies`'e FK'li → firma değeri şemasal olarak güvenilir. Ek FK yalnız kalıcı-silme ve kopyalama sırasında FK-sıra yükü getirirdi (Migration055'te aynı gerekçeyle alınmış karar). |
| İndeks? | **Evet** | İzolasyon sorgusu (`WHERE company_id=…`) bunun üzerinden çalışır. |
| Benzersizlik / CHECK? | **Hayır** | Bu satırların doğal bir benzersizlik kuralı yok. |
| SQLite ↔ PostgreSQL aynı mı? | **Son durum AYNI** | PG: `ADD COLUMN` → geri-doldur → `SET NOT NULL`. SQLite varsayılansız NOT NULL eklemeye izin vermediği ve sonradan `SET NOT NULL` desteklemediği için tablo, SQLite'ın kendi önerdiği yöntemle yeniden kurulur (yeni tablo + kopyala + eskiyi bırak + adlandır). Bu iki tabloya **başka tablodan FK yok** → güvenli. Eski indeksler yeniden kurulur (testle doğrulandı). |
| Tekrar çalıştırılırsa? | **Hiçbir şey yapmaz** | Sürüm zaten işlenmişse runner atlar; ayrıca "kolon zaten var mı" kontrolü ikinci savunma hattıdır. |

### Uygulama kodu (aynı pakette)
- 3 INSERT noktası artık `company_id` yazıyor (talep oluştur · talep düzenle · bakım malzemesi).
- Talep kalemi ve bakım malzemesi **okuma/silme** sorguları firma süzgeci aldı.
- **Yol boyunca bulunan gerçek açık kapatıldı:** `GetMaintenanceMaterials` (bakım malzemeleri listesi,
  `/api/maintenance/{id}/materials`) yalnız "bakım görüntüleme" yetkisi arıyor, kaydın **firmasını
  doğrulamıyordu** → başka firmanın bakım malzemeleri okunabilirdi. Artık firma süzgeci var.

---

## 4. Güvenlik davranışı (çözülemeyen kayıt)

Migration, kolonu eklemeden **önce** "firması kesin belirlenemeyen satır var mı?" diye bakar.
Varsa **durur**, transaction geri alınır (kolon eklenmez, hiçbir satır silinmez/taşınmaz) ve hangi
satırların neden çözülemediğini yazar. Tahminle taşıma **yoktur**.

⚠️ **Karar gereken tek nokta bu.** Sunucu için sorun yok (canlıda 0 çözülemeyen satır — yukarıda ölçüldü).
Ama aynı migration **babanın masaüstündeki yerel veritabanında da** çalışacak ve orayı buradan göremiyorum.
İki seçenek:

| | A — **DUR** (şu anki hâli) | B — **Karantina** |
|---|---|---|
| Davranış | Çözülemeyen satır varsa uygulama açılmaz, bana haber verilir | Çözülemeyen satır ayrı bir tabloya taşınır, migration devam eder, satırlar rapor edilir |
| Veri kaybı | Yok | Yok (satır silinmez, saklanır) |
| Risk | Baban uygulamayı açamaz, beklemek zorunda kalır | Uygulama çalışmaya devam eder |
| Not | Senin "güvenli şekilde durdur" talimatının birebir karşılığı | Aynı güvenlik (tahmin yok), ama masaüstünü kilitlemez |

Teknik olasılık düşük: veritabanı zaten FK zorluyor, bu yüzden "üstü olmayan kalem" normalde **oluşamaz**.
Yine de kararı sen ver. **Bir şey demezsen A ile devam ederim.**

---

## 5. Yedek ve geri dönüş

### Yedek (migration'dan hemen önce, senin onayınla)
1. **Neon anlık geri dönüş noktası:** canlı veritabanının `pre-ms1a` adlı bir **dalı (branch)** oluşturulur.
   Bu, verinin kopyasına anında dönebilmeyi sağlar ve **ana veritabanına dokunmaz** (kopya-üzerine-yazma).
   Neon aracı bu makinede kurulu ve çalışıyor (sürüm 2.36.0, salt-okuma listeleme ile doğrulandı).
2. Ayrıca migration öncesi sayımlar (yukarıdaki tablo) rapora yazılır; sonrasında birebir tekrarlanır.

### Geri alma (rollback)
- **Migration sırasında hata olursa:** tek transaction içinde çalıştığı için **kendiliğinden** tamamen geri alınır.
  Kolon eklenmez, hiçbir satır değişmez, sürüm işlenmez. Ek işlem gerekmez.
- **Uygulandıktan sonra geri almak gerekirse** (her iki veritabanında da AYNI betik — testle doğrulandı):
  ```sql
  DROP INDEX IF EXISTS ix_material_request_items_company;
  DROP INDEX IF EXISTS ix_maintenance_materials_company;
  ALTER TABLE material_request_items DROP COLUMN company_id;
  ALTER TABLE maintenance_materials  DROP COLUMN company_id;
  DELETE FROM schema_migrations WHERE version = 62;
  ```
  Bu betik şemayı migration ÖNCESİ hâline birebir döndürür ve **hiçbir iş kaydını silmez**
  (test: geri alma sonrası kayıt sayısı aynı kaldı ve migration yeniden uygulanabildi).
  Not: indeksler önce düşürülmeli — SQLite, indeksin kullandığı kolonu düşürtmüyor (bunu test yakaladı).
- **En kötü hâlde:** Neon dalından geri dönülür.

---

## 6. Test sonuçları (hepsi izole ortamda, canlıya bağlanmadan)

| Küme | Sonuç |
|---|---|
| `CompanyIdMigrationTests` (SQLite, M-S1a'ya özel) | **14 / 14 geçti** |
| `PostgresCompanyIdMigrationTests` (PostgreSQL, M-S1a'ya özel) | **6 / 6 geçti** |
| Tüm takım (SQLite) | **839 geçti · 0 başarısız · 20 atlandı** |
| Tüm PostgreSQL testleri (boş test veritabanı) | **30 / 30 geçti · 0 atlandı** |
| Derleme | **0 hata** |

Kapsanan senaryolar: boş veritabanı · dolu veritabanı · tek firma · **birden fazla firma** ·
doğru firmaya taşıma · **yanlış firmaya kayıt sızmasının engellenmesi** · çözülemeyen kayıtta güvenli duruş ·
tekrar çalıştırma · rollback · NOT NULL zorlaması · diğer kolonların bozulmaması · kayıt sayısının değişmemesi ·
eski indekslerin korunması · SQLite · PostgreSQL.

Ayrıca **açığın gerçek olduğunu kanıtlayan** bir regresyon testi var: 61. sürümde (kolon yokken) B firmasının
kalemi A firmasının eşitleme paketine giriyor; 62'den sonra girmiyor.

Hiçbir test "yeşil görünsün diye" değiştirilmedi. Değişen tek şey: 3 rapor testinin **veri hazırlama**
satırları yeni kolonu dolduruyor (iddiaların/beklentilerin hiçbiri değişmedi).

---

## 7. Uygulama sırası (ÖNEMLİ)

**Canlı sunucuda migration'ı çalıştıran şey, API'nin yayınlanmasıdır.** API açılışta bekleyen
migration'ları otomatik uygular. Yani:

> **API deploy = canlı migration.** Bu yüzden senin onayın gelmeden API'yi yayınlamayacağım.

Güvenli sıra:

| # | Adım | Canlı veriye dokunur mu? |
|---|---|---|
| 0 | Neon `pre-ms1a` geri dönüş noktası | Hayır (kopya) |
| 1 | Migration öncesi sayımlar (salt-okuma) | Hayır |
| 2 | **API deploy → migration çalışır** | **EVET — onay gerektiren tek adım** |
| 3 | Migration sonrası sayımlar + doğrulama (salt-okuma) | Hayır |
| 4 | Web deploy | Hayır (web'in kendi veritabanı yok) |
| 5 | Masaüstü 1.0.133 paketi | Hayır (kullanıcı güncelleyince kendi yerel veritabanı migrate olur) |

**Neden API önce, masaüstü sonra:** ikisi de tek başına çalışsa da (eski istemci sunucuya push ederse sunucu
firmayı kendisi zorlar; yeni masaüstü eski sunucudan çekerse kendi firmasını zorlar), sızıntının kapanması
gereken yer **sunucudur**. Önce sunucu kapatılır, sonra masaüstleri kendi hızlarında güncellenir.
Eski sürümde kalan masaüstleri çalışmaya devam eder — kırılma yok.

---

## 8. Canlıya uygulanırsa beklenen sonuç

- `material_request_items`: 2 satır → 2 satır, ikisi de `company_id = ed271d0c…` (Oze İnşaat)
- `maintenance_materials`: 0 satır → 0 satır
- Boş `company_id`: 0 · yetim: 0 · yanlış firma: 0 · **silinen kayıt: 0**
- Şema sürümü: 61 → **62**
- Kullanıcı tarafında görünür bir değişiklik **yok** (ekranlar aynı; yalnız arka planda izolasyon garantisi doğuyor)
- Tahmini süre: **saniyeler** (etkilenen satır sayısı 2)

---

## 9. Kapsam dışı bırakılanlar (ayrı iş olarak raporlanıyor)

Bu iş **yalnız** iki tablonun firmaya bağlanmasıdır. Denetim sırasında görülen, M-S1a'nın ön koşulu
**olmayan** maddeler:

1. **`request_status_history`** (talep durum geçmişi) — firma kolonu yok, aynı desende. Eşitleme
   listesinde **olmadığı** için bugün sızıntı yolu yok; yine de aynı işlemle firmaya bağlanabilir. → **M-S1b**
2. **`maintenance_definition_vehicles`** (bakım tanımı ↔ araç bağlantısı) — firma kolonu yok. Eşitleme
   listesinde değil. → **M-S1b**
3. **Genel kural eksikliği:** yeni bir çocuk tablo eklendiğinde firma kolonunun unutulmasını engelleyen
   otomatik bir kontrol yok. Küçük bir test ("eşitlenen her tabloda ya `company_id` olmalı ya da firmalı bir
   ebeveyne FK'si olmalı") bunu kalıcı olarak garanti eder. → **M-S1c**
4. **Eşitleme yazma yolu:** sunucu, gelen satırın **üst kaydının** aynı firmaya ait olduğunu ayrıca
   doğrulamıyor (firmayı zorluyor ama üst kayıt başka firmanınsa satır yine de yazılabilir; FK sağlam
   olduğu için "olmayan üst kayıt" mümkün değil). Çok firmalı gerçek kullanımda sıkılaştırılmalı. → **M-S1d**

Bunların hiçbiri bu işin çalışması için gerekli değil; ayrı ve küçük işler.

---

## 10. Onayın için gereken tek şey

> **"Canlı migration'ı uygula"** dersen: yukarıdaki 0→5 sırasını uygularım, her adımda doğrulama çıktısı alır,
> sonunda migration öncesi/sonrası sayıları yan yana raporlarım.
>
> Ayrıca **Bölüm 4'teki A/B seçimini** yazarsan onu uygularım; yazmazsan **A (dur)** ile devam ederim.

Şu ana kadar canlı veritabanına **hiçbir yazma yapılmadı**; yapılan tek şey salt-okuma denetimidir
(PostgreSQL'in yazmayı reddettiği kanıtla birlikte).
