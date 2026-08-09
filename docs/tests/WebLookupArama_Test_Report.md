# Test Raporu — Web lookup arama (İŞ A)

Tarih: **2026-08-09** · Migration: **YOK** · Production: **değişiklik yok** · Deploy/push: **yok**

---

## 1. Başlangıç durumu — iş beklenenden farklı çıktı

İş "web'de ~18 `MudSelect`'e arama ekle" diye açılmıştı. 78 `MudSelect` tarandı ve asıl sorunun
**kozmetik olmadığı** görüldü:

> API `Page()` ile 500 ister, ama `PageRequest.NormalizedLimit()` bunu **`MaxLimit = 200`**'de keser.
> Canlıda **2463 malzeme** var. Bazı seçiciler bu sayfalı ucu **aramasız** yüklüyordu →
> kullanıcı 200'den sonraki kaydı **hiç seçemiyordu** ve bunu belirten bir uyarı da yoktu.

Yani bulgu: **sessiz işlev kaybı (P1)**, "arama yok" (P2) değil.

## 2. Sınıflandırma

| Grup | Sayı | Karar |
|---|---|---|
| Zaten `MudAutocomplete` + `?search=` (Daily, Fuel, Inspection, Maintenance, Materials, Requests, Stock) | 8 | ✅ dokunulmadı |
| Sabit enum/durum/tür/sayfa boyutu | çoğunluk | ✅ dokunulmadı (lookup değil) |
| Sayfalı ucu **aramasız** yükleyen çoklu seçim | 4 | ❌ düzeltildi |
| Sayfalı ucu **istemci-taraflı** arayan personel seçicileri | 13 | ❌ düzeltildi |

## 3. Yapılan

### 3.1 Çoklu seçim → `SearchableMultiSelect` (yeni ortak bileşen)

| Ekran | Alan | Uç |
|---|---|---|
| Araç Şablonları | Uyumlu Malzemeler | `/api/materials` (**2463 kayıt — asıl kırık yer**) |
| Malzemeler | Uyumlu Araçlar | `/api/vehicles` |
| Bakım Tanımları | İlişkili Araçlar | `/api/vehicles` |
| Malzeme Şablonları | Uyumlu Araçlar | `/api/vehicles` |

**Tasarım (mevcut davranışı bozmadan):**
- **İlk yükleme aramasızdır** → bugünkü davranışın aynısı. Düzenlemede sunucudan gelen seçili
  kayıtların **adları böylece çözülür** (Materials ve Bakım Tanımları düzenlemede seçim yükler).
- Kullanıcı yazınca arama **sunucuda** yapılır → sayfa sınırının ötesine ulaşılır.
- **Seçili kayıtlar arama sonucuna daima eklenir** → arama daraldığında seçim kaybolmaz.
- Görülen adlar önbelleğe alınır; seçili kayıt sonuçtan çıksa da adı korunur.

> Not: Malzeme Şablonları önceden `/api/vehicles/options` (sayfasız) kullanıyordu, yani **kesilmiyordu**.
> Üç ekranı tek desende toplamak için aranabilir uca geçirildi; ≤200 araçta davranış aynıdır.

### 3.2 Personel/Teknisyen/Sürücü → `LookupSelect`'e **opsiyonel** sunucu araması

`/api/personnel` sayfalıydı ve **arama parametresi hiç yoktu**; `PersonnelService.List` de almıyordu.
Bu yüzden 13 seçicide `LookupSelect`'in istemci-taraflı araması 200 kaydın ötesini **asla** bulamıyordu.

| Katman | Değişiklik |
|---|---|
| Servis | `PersonnelService.List(..., string? search = null)` — `MaterialService.List` ile **aynı desen**, `SqlDialect.LikeTr` ile `full_name` üzerinde. Parametre sona eklendi → mevcut çağrılar bozulmadı. |
| API | `/api/personnel?search=` |
| Web | `LookupSelect`'e **opsiyonel** `SearchPath` + `NameField`. **Verilmezse davranış hiç değişmez** → 14+ mevcut kullanım (birim, marka, kategori… sayfasız lookup'lar) aynen istemci-taraflı kalır. |

13 kullanım: Günlük Faaliyet (4), Yakıt (2), Bakım (2), Talepler (3), Araçlar (1), Kullanıcılar (1).

**Firma izolasyonu servis katmanında kaldı** — arama filtresi `company_id` koşulunun yerine geçmez,
yanına eklenir. Şube kapsamı filtresi de aynen korundu. Testle doğrulandı.

## 4. Testler

`SelectorTruncationTests` (gerçek HTTP hattı) — **8/8**:

| Test | Kanıt |
|---|---|
| Aramasız uç sınırda kesilir | tam olarak `MaxLimit` döner; **en eski kayıt erişilemez** |
| Arama sınırın ötesine ulaşır | önce yokluğu kanıtlanır, sonra arama bulur |
| Arama başka firmayı getirmez | izolasyon UI'da değil serviste |
| Boş arama ilk sayfayı verir | |
| **Personel**: arama sınırın ötesine ulaşır | İş A'dan önce bu uçta arama **hiç yoktu** |
| **Personel**: başka firma gelmez | |
| **Personel**: boş arama eski davranışı korur | `search=` boşsa sonuç birebir aynı |
| **Personel**: Türkçe karakter doğru eşleşir | "şahin" → "İsmail Şahin", "Ahmet Yilmaz" hariç |

`PostgresPersonnelSearchTests` — PG'de Türkçe eşleşme + firma izolasyonu + aramasız davranış.

> ⚠️ İlk yazdığımda testin varsayımı tersti (en yeni kayıt kesiliyor sandım). Test bunu yakaladı;
> **assertion "yeşil olsun diye" değiştirilmedi**, gerçek davranış ölçülüp düzeltildi (liste en
> yeniden eskiye sıralı → kesilen taraf ilk oluşturulan kayıtlar).

| Paket | Sonuç |
|---|---|
| SQLite tam paket | **972 geçti / 0 başarısız / 32 atlandı** |
| PostgreSQL tam paket | **43 geçti / 0 başarısız / 0 atlandı** |
| `dotnet build DepoWise.sln` | **0 hata** |

32 atlanan = `DEPOWISE_PG_URL` tanımsızken atlanan PG testleri; URL ile hepsi koştu (43/43).

## 5. Doğrulanamayan: gerçek tarayıcı testi

`LookupSelect` 14+ ekranda kullanıldığı için tarayıcı duman testi yapmak istedim; **yapılamadı**:
yerel API mevcut geliştirme veritabanını kullanıyor ve şifresi bilinmiyor. Bunu aşmanın iki yolu
vardı, ikisi de sizin kurallarınızla çelişiyor:

- `.claude/launch.json`'a geçici bir yapılandırma eklemek → **`.claude/` kapsam dışı**,
- yerel geliştirme veritabanını sıfırlamak → **sizin dosyanız**.

Bu yüzden doğrulama API/servis katmanında yapıldı (8 test gerçek HTTP hattından). **Razor çalışma-anı
hatası riski** bu turda kapatılamadı; yayın öncesi tarayıcıdan bakılması önerilir.

## 6. Yeni bulgu

**P2 — `/api/vehicles` da sayfalıdır (`limit = 200`).** Canlıda 94 araç var, yani bugün kesilmiyor;
filo 200'ü geçerse aynı sessiz kayıp araçlarda da oluşur. Bu turda seçiciler aramaya geçirildiği için
**semptom kapatıldı**, ama sınır duruyor. Sınırın kendisi (`PageRequest.MaxLimit`) sistem geneli bir
karardır → değiştirilmedi.
