# FİNAL AUDIT + REPAIR + VERIFICATION TURU

> Tur tarihi: **2026-08-26** (aynı gün ikinci tur) · Başlangıç HEAD: `cfed0a9`
> Yayındaki durum (tur başı): API **v167** · Web **v191** · Masaüstü **1.0.150** · Şema **72**
> Amaç: yeni özellik değil — **çalışanı bozmadan**, gözden kaçmış gerçek hataları bulmak, önce testle
> kanıtlamak, en küçük güvenli düzeltmeyi yapmak ve regresyon olmadığını göstermek.

---

## 1. Başlangıç baseline — önceki rapora güvenilmedi

| Kontrol | Sonuç |
|---|---|
| Branch / HEAD / origin | `master` · `cfed0a9` · origin ile **eşit** |
| Çalışma ağacı | temiz (yalnız kullanıcının iki dosyası) |
| Solution Release derlemesi | **0 hata** (41 uyarı — hepsi eskiden vardı) |
| API · Web · Desktop ayrı derleme | **0 · 0 · 0 hata** |
| Tam test paketi | **2282 geçti · 0 başarısız · 37 atlandı** — bildirilen sayıyla **birebir** |
| Migration kataloğu | **1…72 kesintisiz** · tekrar eden sürüm **yok** · en yüksek **72** |
| API sağlık | `/health` **200** |
| Web sağlık | **200** |
| Yayındaki masaüstü | **1.0.150** · checksum `79DB5051…` (sunucu kaydıyla aynı) |
| Üretim şeması | **72** |

Yani taban gerçekten iddia edilen yerdeydi.

---

## 2. Tarama kapsamı (değişiklik yapmadan)

- **301 API ucu** tarandı; her biri için "kimlik doğrulama attribute'u VAR mı / gövdede oturum kapısı VAR mı"
  otomatik çıkarıldı → gerçekten korumasız **12 uç** kaldı, hepsi tek tek incelendi (aşağıda).
- SQL enjeksiyonu: `CommandText` içine değişken gömülen **14 yer** incelendi; hepsi kod-kontrollü
  (`orderSql`/`whereSql`/tablo adı). Kullanıcıdan gelen **sıralama alanı beyaz listeyle** eşlenir
  (`byKey` dizisi); bilinmeyen değer `null`'a düşer → **enjeksiyon yolu yok**.
- Yol geçişi: sürüm paketleri `Safe(version)` ile temizlenir (`Path.GetInvalidFileNameChars`), yedek
  indirme `Path.GetFileName` kullanır → **traversal yok** (Linux ve Windows'ta ayrı ayrı düşünüldü).
- Yutulan istisna: **servis katmanında `catch (Exception) {}` YOK**. Masaüstü arayüz katmanındakiler
  bilinçli (ekran çökmesin). API'deki 8 tanesi tek tek okundu → **biri gerçek hataydı (SIF-03)**.
- Olay aboneliği sızıntısı: web'de `+=` yapan 4 bileşenin **dördü de** `Dispose`'da `-=` yapıyor ve
  servisler `Scoped` (devre başına) → sızıntı yok.
- İdempotency: `sync_inbox`, `sync_outbox`, `stock_movements`, `vehicle_maintenances`,
  `fuel_depot_entries`, `fuel_distributions` üzerinde **UNIQUE(operation_id)** indeksleri yerinde.
- Transaction: yazma servislerinin tamamında `BeginTransaction/BeginImmediate` + `Commit` dengeli.
- Menü/ekran kataloğu: **109 mevcut test** (6 sınıf) benzersiz anahtar, route, web/masaüstü paritesi,
  yetim ekran, katalog dışı ekran, mükerrer route/anahtar ve korumalı ekranları zaten kilitliyor.

---

## 3. Bulunan gerçek hatalar

| ID | Önem | Kısaca | Durum |
|---|---|---|---|
| **TNT-05** | **P2** | Rapor ucu, başka **firmanın** şube kimliğini kabul ediyordu (403 yerine 200). Veri sızmıyordu ama kapı fail-open'dı. | ✅ düzeltildi |
| **SIF-03** | **P2** | Firma iş verisi sıfırlamada, makinelere "yerelini temizle" bildirimi **boş catch ile yutuluyordu** → silinen veri geri gelebilirdi. | ✅ düzeltildi |
| **MAK-01** | **P2** | Anonim `/api/machines/register` firmanın **makine kotasını tüketebiliyor**; iki büyük indirme ucu ve `/sync/enroll` **hız sınırsızdı**. | ⚠️ kısmen (sınır kondu; model kararı kullanıcıda) |
| **YET-02** | **P2** | `btn-reverse` üç işlemin kapısıydı ama **yetki ağacında yoktu** → yönetici kimseye veremiyor, kullanıcı çıkmaza giriyordu. | ✅ düzeltildi |
| **RL-01** | P3 | `RateLimiter` durum sözlüğü **sınırsız büyüyordu** (IP başına kalıcı satır; sunucu bellek sınırı 207 MB). | ✅ düzeltildi |
| **YET-01** | P3 | `btn-reset-db` ve `btn-logo` ağaçta görünür ama **hiçbir yerde kapı değil** → yönetici yetki verdiğini sanır. | 📋 raporlandı |

### 3.1 TNT-05 — şube kimliği firma aidiyeti doğrulanmıyordu
- **Kök neden:** `BranchAccess` yalnız **oturum** üzerinden çalışır; veritabanını bilmez. Sınırsız (admin)
  bir kullanıcıda izinli küme `null` olduğu için `CanAccess` **her** şube kimliğine `true` döner.
- **Kanıt:** izole matris testi `M15` (B firmasının admini, A firmasının şubesini istiyor) → **kırmızı**.
- **Düzeltme:** `BranchService.BelongsToCompany` eklendi, rapor oturumu kurulmadan **önce** çağrılıyor.
- **Test:** `M15` + kapı devre dışı bırakılıp **kasten bozma** ile iki yönde doğrulandı.
- **Üretim etkisi:** yok (veri zaten `company_id` ile filtreleniyordu); artık sözleşmeye uygun **403**.

### 3.2 SIF-03 — sıfırlama bildirimi sessizce yutuluyordu
- **Kök neden:** `try { … RequestReset … } catch { }` ve bu adım **silmeden SONRA** yapılıyordu.
- **Kanıt:** kaynak kilidi testi → **kırmızı**.
- **Düzeltme:** sıra tersine (önce bildirim, sonra silme). Bildirim yıkıcı olmadığı için silme sonradan
  başarısız olsa bile **veri kaybı yok**; bildirim başarısız olursa **hiçbir şey silinmez** ve kullanıcı görür.
- **Test:** 4 test + **öz-doğrulama** (kuralın kendisi yanlış gövdeyle sınanıyor).
  ⚠️ İlk sürümüm bir **yorum satırındaki** aynı metinden başlayıp yanlış bloğu ölçüyordu; kasten bozma
  denemesiyle yakalandı ve çapa kesinleştirildi.

### 3.3 MAK-01 — anonim makine kaydı ve sınırsız indirme
- **Kök neden:** üç uç kimlik doğrulaması **isteyemez** (kimlik bilgisi oluşmadan çağrılırlar) ve
  hiçbirinde hız sınırı yoktu. Yeni makine `ActiveCount < quota` ise kendiliğinden `active` oluyor.
- **Kanıt:** `MAK01_Anonim_Kayit_Kotayi_Tuketebiliyor` — kota 2 olan firmada iki sahte kayıt kotayı
  doldurdu, **gerçek makine `pending` kaldı**.
- **Sınırlar:** ⚠️ veri sızıntısı **yok** (kayıt cihaz jetonu vermez); **mevcut aktif makineler düşürülmez**.
- **Düzeltme (bu tur):** mevcut `RateLimiter` ile IP başına sınır — makine kaydı 30/5dk, indirme 30/10dk,
  enrollment giriş limiti. Meşru akış etkilenmez (aynı makinenin tekrar kaydı testle kilitlendi).
- **Kapsam dışı (karar gerekiyor):** aktivasyon **modelini** değiştirmek masaüstü kurulum akışını
  değiştirir; bilinçli olarak yapılmadı.

### 3.4 YET-02 — "iptal / ters kayıt" yetkisi verilemiyordu
- **Kök neden:** `btn-reverse` `SpecialButtons.All` listesinde yoktu → yalnız admin bypass'ıyla geçilebiliyor.
- **Kanıt:** düzeltmeden önce **3 test kırmızı**.
- **Düzeltme:** listeye eklendi (kimseye yetki vermez; yalnız verilebilir hâle getirir).
- **Test:** iki listeyi kalıcı hizalayan 4 test. ⚠️ İlk tarayıcım çok satırlı bir `ternary`'yi kaçırıp iki
  dışa aktarma butonunu yanlışlıkla "işlevsiz" saydı → kural genişletildi ve doğrulandı.

---

## 4. Geri alınan / yapılmayan değişiklikler (dürüst kayıt)

- **Aktivasyon modeli (MAK-01):** "yeni makine ancak kimlik doğrulanmış girişten sonra aktifleşsin"
  önerisi **uygulanmadı** — masaüstü kurulum akışını değiştirir ve bu tur "çalışanı bozmama" turudur.
- **`btn-reset-db` / `btn-logo` (YET-01):** anahtarları **silinmedi** — verilmiş yetki kayıtlarını öksüz
  bırakırdı. Test içinde bilinçli istisna olarak listelendi.
- **Arayüzde `btn-reverse` kapısı:** masaüstü Yakıt ekranı uyguluyor, Stok ekranı ve web uygulamıyor.
  Sunucu fail-closed olduğu için **güvenlik açığı değil**; arayüz tutarlılığı olarak raporlandı.
- **Stok Durumu / Stok Sayım'ın `Allowed` kullanması:** bir önceki turda denenip geri alınmıştı (ADR-131);
  bu turda **tekrar denenmedi** — karar kilitli ve gerekçesi kodda.

---

## 5. Tenant (firma) güvenliği

| Kontrol | Sonuç |
|---|---|
| Firma kimliği alan uçlara yabancı kimlik (13 senaryo) | **hepsi doğru** (ya reddediyor ya kendi firmasını dönüyor) |
| Rapor gövdesinde yabancı firma | 3 rapor × reddetme/boş sonuç ✅ |
| Kullanıcı oluşturmada yabancı firma | reddedildi **ve DB'ye yazılmadığı doğrulandı** |
| Yabancı firmanın admini A'nın verisini | **göremiyor** (3 rapor) |
| Süper adminin firma seçebilmesi | **korundu** (yanlış pozitif yok) |
| Anonim istekler | 7 yolun tamamında reddedildi |

---

## 6. Şube güvenliği — izole matris (üretimde 0 şube olduğu için ŞART)

> ⚠️ **Sınır açıkça belirtilir:** üretim veritabanında **hiç şube tanımlı değil**. Bu yüzden şube
> izolasyonu **canlı veriyle gözlemlenemedi** ve "üretimde çalışıyor" **denemez**. Kural izole ortamda
> kanıtlandı.

Kurgu: **FİRMA A → ŞUBE A1, A2** · **FİRMA B → ŞUBE B1** · 3 rapor (stok hareketleri, muayene, personel).

| # | Senaryo | Sonuç |
|---|---|---|
| 1 | A1 kullanıcısı A1'i görür | ✅ |
| 2 | A1 kullanıcısı A2'yi **göremez** | ✅ |
| 3 | A1 kullanıcısı B1'i **göremez** | ✅ |
| 4 | İsteğe A2 yazarsa **geçmez** | ✅ |
| 5 | İsteğe B1 yazarsa **geçmez** | ✅ |
| 6 | Çalışma şubesi olarak A2 yazarsa **reddedilir** | ✅ |
| 7 | Çalışma şubesi olarak B1 yazarsa **reddedilir** | ✅ (TNT-05 ile) |
| 8 | Yönetici A1+A2 seçebilir | ✅ |
| 9 | Yönetici B1'i **seçemez** (fail-closed: kendi şubeleri de gelmez) | ✅ |
| 10 | Yöneticinin kapsam listesi yalnız A1+A2 (araç/personel dahil) | ✅ |
| 11 | Depo personelinin kapsam listesi yalnız A1 | ✅ |
| 12 | Export operasyon kapsamını uygular (**Excel içeriği açılarak**) | ✅ |
| 13 | Export yetkisiz şubeyi vermez | ✅ |
| 14 | Depo personeli export **yapamaz** | ✅ |
| 15 | B admini A'nın şubesini isteyemez | ✅ |
| 16 | Yönetici raporu depo personeline kapalı (5 rapor) | ✅ |
| 17 | Yönetici raporu admine açık + firma sınırlı | ✅ |

---

## 7. Raporlar — 21/21

Bir önceki turda çıkarılan tam kapsam haritası (rapor × grup × firma × şube × izin) bu turda
değişmedi ve testlerle korunuyor. Bu turda eklenen tek şey **şube kapsamının izole matriste
uçtan uca ölçülmesi** ve **TNT-05 kapısı**.

**Operasyon / Yönetici ayrımı** gerçek tarayıcıda tekrar doğrulandı (bkz. §10).

---

## 8. Performans — ölçüldü

### Sunucu tarafı (Stok Hareketleri, tek makine, SQLite)

| Satır | Sorgu | Dönen satır | JSON boyutu | Serileştirme |
|---|---|---|---|---|
| 1.000 | **5 ms** | 1.000 | 121 KB | 21 ms |
| 10.000 | **57 ms** | 10.000 | 1,2 MB | 25 ms |
| 20.000 | **122 ms** | 20.000 | 2,4 MB | 74 ms |
| 50.000 | **275 ms** | 50.000 | 6,1 MB | 95 ms |

### Tarayıcı tarafı (önceki turda PRF-01 ile ölçülüp düzeltilmişti; kod değişmedi)

| 20.000 satır | Önce | Sonra |
|---|---|---|
| Sorgula → tablo | 36.959 ms | **378 ms** |
| DOM düğümü | 260.729 | **13.746** |

**Sonuç:** darboğaz sorgu değildi, çizimdi ve çözüldü. Sunucu tarafında **yeni indeks/migration
gerekmedi ve açılmadı**. Kalan teorik yük: 50.000 satırda ~6 MB yanıt (sayfalı API ileride gerekebilir).

---

## 9. Senkron / çevrimdışı

- **138 mevcut test** (17 sınıf) çevrimdışı, idempotent tekrar, firma/şube sınırı, sıfırlama sonrası,
  kısmi/başarısız gönderim, makine sıfırlama, çakışma ve kurtarma senaryolarını kapsıyor — hepsi yeşil.
- **SIF-02** koruması yerinde ve bu turda **SIF-03** ile aynı sınıftan ikinci bir sessiz hata kapatıldı.
- İdempotency indeksleri ve `sync_inbox` tekrar-kontrolü doğrulandı.

---

## 10. Gerçek arayüz turu (izole, üretime dokunulmadan)

Yerel API **sıfır veritabanıyla** ayrı dizinde ayağa kaldırıldı; **iki şube** (MERKEZ, SANTIYE-2) kuruldu.

| Kontrol | Sonuç |
|---|---|
| Depo personeli giriş şube listesi | **yalnız MERKEZ** ("Tüm Şubeler" ve SANTIYE-2 yok) |
| Operasyon ekranı | **10 rapor** (izne göre süzülmüş — 6 ön muhasebe raporu yok), **şube seçici YOK** |
| Muayene/Sigorta raporu | **yalnız MERKEZ** belgeleri · durum kuralı doğru (−10 gün → "Süresi geçti", +39 gün → "Normal") |
| Tarih taşınması (RPR-13) | alanlar **boş** geldi → düzeltme yerinde |
| Personel raporu | **yalnız MERKEZ** personeli |
| `/reports/manager` adresi (depo personeli) | **engellendi** · 0 rapor · Sorgula yok |
| Admin yönetici ekranı | **21 rapor** + **"Şube / Şantiye" seçici var** |
| Yerel sunucu logu | **60 istek · 0 hata · 0 başarısız yanıt** |

---

## 11. İzole masaüstü turu (üretime BAĞLANMADAN)

`serverurl.txt` → yerel sunucu · `DEPOWISE_ENVIRONMENT=DenetimIzole` → **ayrı** yerel veritabanı.

| Kontrol | Sonuç |
|---|---|
| Uygulama açılışı | ✅ çöküş yok |
| Hangi sunucuya bağlandı | **yerel** (`/api/public/companies` yerel logda göründü) — üretime **gitmedi** |
| Yerel veritabanı | `Data/DenetimIzole/alpnex.db` — **ayrı**, kullanıcının Development verisine dokunulmadı |
| Sıfırdan migration | **72/72 çalıştı** · 79 tablo oluştu |
| Üretim etkisi | makine listesi **değişmedi** (2 gerçek makine, ikisi de aktif) |

> **Sınır:** Avalonia arayüzü bu ortamda otomatize edilemediği için **ekran içi tıklama akışları
> sürülemedi**; açılış, izolasyon, sunucu yönlendirmesi ve migration doğrulandı. Masaüstü rapor/kapsam
> mantığı web ile **ortak koddadır** ve testlerle kilitlidir.

---

## 12. Kalan gerçek problemler ve karar gerektirenler

| Konu | Tür | Not |
|---|---|---|
| **Makine aktivasyon modeli** | karar | Anonim kayıt kotayı tüketebiliyor. Hız sınırı kondu; modeli değiştirmek kurulum akışını etkiler. |
| **PostgreSQL dosya yedeği** | karar | `pg_dump` sunucuda yok; uygulama içi dökümcü yeni özellik. Bugün PITR'e dayanıyor (ADR-136). |
| **`btn-reset-db` / `btn-logo`** | karar | Ağaçta var, kodda karşılığı yok. Silmek verilmiş kayıtları öksüz bırakır. |
| **Arayüzde `btn-reverse` kapısı** | iyileştirme | Sunucu fail-closed; arayüz tutarsız (yalnız masaüstü Yakıt ekranı uyguluyor). |
| **Rapor sayfalı API** | iyileştirme | 50.000 satırda ~6 MB yanıt. Çizim çözüldü, aktarım kalıyor. |
| **Satın Alma alanı** | yeni özellik | Kodda satın alma domaini **yok**; sahte ekran üretilmedi. |
| **TNT-04** | ürün gereği | Anonim uçlar firma/şube **adlarını** açar (giriş ekranı için gerekli, hız sınırlı). |

---

## 13. Test sonuçları (final)

| Koşu | Sonuç | Süre |
|---|---|---|
| **Taban** (tur başı) | 2282 geçti · 0 başarısız · 37 atlandı | 12 dk 26 sn |
| Ara koşu (YET-02 öncesi) | 2319 · 0 · 37 | 12 dk 51 sn |
| **Son koşu 1** | **2323 geçti · 0 başarısız · 37 atlandı** | 15 dk 45 sn |
| **Son koşu 2 (bağımsız)** | **2323 geçti · 0 başarısız · 37 atlandı** | 15 dk 57 sn |
| **PostgreSQL** (ayrı test veritabanı) | **49 geçti · 0 başarısız · 0 atlandı** | 13 dk 51 sn |

İki bağımsız son koşu **birebir aynı** → kararsız (flaky) test yok. Tabana göre **+41 senaryo**,
regresyon **0**. Atlanan 37'nin tamamı PostgreSQL kapılıdır ve ayrı koşuda hepsi çalıştırılıp geçmiştir.
**Gizlenen, devre dışı bırakılan, gevşetilen veya retry ile örtülen test yoktur.**

Derleme: API · Web · Desktop Release → **0 hata**. **Yeni migration YOK** → üretim şeması **72**.

### Bu turda eklenen testler
| Dosya | Adet | Ne kanıtlar |
|---|---|---|
| `BranchIsolationMatrixTests.cs` | 25 | Firma A(A1,A2)/Firma B(B1) matrisi × 3 rapor: görme/görmeme/seçememe/elle yazsa da geçememe + kapsam listeleri + Excel içeriği |
| `ButtonPermissionCatalogTests.cs` | 4 | Yetki ağacı ↔ kod tutarlılığı (iki yönlü) |
| `CompanyResetNotifyTests.cs` | 4 | SIF-03 sırası + yutma yok + öz-doğrulama |
| `MachineRegisterAbuseTests.cs` | 3 | MAK-01 kanıtı + hız sınırı + meşru akış kilidi |
| `RateLimiterHardeningTests.cs` | 3 | Bellek temizliği + kararın değişmediği |

---

## 14. YAYIN — tamamlandı

Sıra `docs/DEPLOYMENT.md`'ye uygun: önce API, sonra Web, en son masaüstü.

| # | Adım | Sonuç |
|---|---|---|
| 1 | Yeni migration var mı | **YOK** → şema **72**'de kalır, ek onay gerekmedi |
| 2 | `flyctl deploy -c fly.toml` | ✅ **API v167 → v168** · makine `started` |
| 3 | API sağlık | `/health` **200** |
| 4 | **PG gerçekten bağlandı mı** | ✅ **gerçek veri döndü** (1 firma · 3 kullanıcı · 18,2 MB) → boş SQLite'a **düşmedi** |
| 5 | `flyctl deploy -c fly.web.toml` | ✅ **Web v191 → v192** · makine `started` |
| 6 | Web route'ları (9 adres) | **hepsi 200** |
| 7 | Masaüstü publish 1.0.151 | ✅ 270 dosya · 243 MB · 0 hata |
| 8 | Paketleme | **89.970.062 bayt** |
| 9 | Sürüm yayını | ✅ `/api/releases/latest` = **1.0.151** · eski paketler budandı (3 paket) |
| 10 | İndirme ucu | **200** · **89.970.062 bayt** (birebir) |
| 11 | **Checksum** | ✅ **üç değer de aynı** → `431C0650E97669F9C0E902BB2F0C78428183A5A02B0F6D3F6C971CC1531212C7` |
| 12 | Kurulum aracı | `/api/setup/download` **200** (71,9 MB) — Setup değişmediği için yeniden yayınlanmadı |
| 13 | Sürüm tutarlılığı | API **168** · Web **192** · Masaüstü **1.0.151** · Şema **72** |

### Yayın sonrası duman testleri (üretim, SALT OKUMA)

| # | Kontrol | Sonuç |
|---|---|---|
| 1 | Sunucu kaynakları | disk **%42,8** · CPU **%2,6** · bellek **%48,8** · 3 paket · **crash-loop yok** |
| 2 | Rapor kataloğu | **21** rapor |
| 3 | Muayene/Sigorta raporu | **200** · 151 ms |
| 4 | Personel raporu | **200** · 106 ms · 1 satır |
| 5 | Stok Hareketleri raporu | **200** · 167 ms |
| 6 | **TNT-05 canlı doğrulaması** | olmayan şube kimliği → **403 REDDEDİLDİ** (düzeltme öncesi 200 dönerdi) |
| 7 | API + Web logları | **0 hata / 0 exception** |

### Üretime yapılan işlemler (tam liste)
- İki `flyctl deploy` (API, Web) — kod dağıtımı.
- Bir sürüm yayını (masaüstü 1.0.151; sunucu eski paketleri otomatik budadı).
- Süper admin ile **salt-okunur** doğrulama çağrıları.
- **Hiçbir iş verisi yazılmadı/silinmedi. Doğrudan SQL çalıştırılmadı. Migration çalıştırılmadı.**
- Yedek: bir önceki turdaki durum geçerli — üretim bağlantısı yalnız bir Fly *secret*'ıdır ve geri
  okunamaz; kimlik bilgisi **istenmedi**. Yayın şemaya dokunmadığı için prosedür durdurmayı gerektirmedi.
