# FİNAL STABİLİZASYON · UÇTAN UCA DENETİM · RAPORLARIN TAMAMLANMASI

> Tur tarihi: **2026-08-26** · Başlangıç HEAD: `9d97f9d` (yayınlanmış durum: API v166 · Web v190 · Masaüstü 1.0.149)
> Amaç: yeni mimari kurmak değil — **mevcut sistemi yeniden denetlemek, gerçek hataları bulup en küçük
> ve geri alınabilir değişikliklerle düzeltmek**, eksik testleri yazmak, RAPORLAR modülünü kapatmak.

---

## 1. Başlangıç durumu — önceki rapora güvenilmedi, yeniden ölçüldü

| Kontrol | Sonuç |
|---|---|
| `git status` | temiz (yalnız kullanıcının iki dosyası: `SECURITY_CREDENTIAL_ROTATION_PLAN.md`, `docs/kilavuzlar/`) |
| HEAD = origin/master | ✅ `9d97f9d` |
| Release derlemesi | **0 hata** (41 uyarı — hepsi önceden vardı) |
| Tam test paketi | **2221 geçti · 0 başarısız · 35 atlandı** — bildirilen sayıyla **birebir aynı** |
| API sağlık | `/health` **200** |
| Web sağlık | **200** |
| Yayındaki masaüstü | **1.0.149** · checksum `AE52DEC4…` (sunucu kaydıyla aynı) |
| Migration kataloğu | **1…72 kesintisiz**, tekrar eden sürüm **yok**, 72 = üretim şeması |

Yani taban gerçekten iddia edilen yerdeydi; bu tur oradan başladı.

---

## 2. Bulunan gerçek sorunlar ve yapılanlar

Aşağıdaki her madde için sıra şuydu: **hatayı üreten test → testin kırıldığını gör → en küçük düzeltme →
hedefli test → ilgili testler → tam test**.

| ID | Önem | Problem | Kanıt (düzeltme öncesi) | Durum |
|---|---|---|---|---|
| **UPD-01** | **P1** | Masaüstü kurulumcusu `checksum boş değilse doğrula` diyordu → sunucudan **boş** checksum gelirse doğrulama **tamamen atlanıyor**, inen zip açılıp uygulamanın üzerine kuruluyor ve uygulama yeniden başlatılıyordu. Bu, güncelleme yolunu **"inen ne ise onu çalıştır"**a çeviren bir kod-çalıştırma yoludur. | Kaynak kilidi testi kırmızı | ✅ düzeltildi |
| **YED-01** | **P2** | Sunucu veritabanı yedeği `VACUUM INTO` + `PRAGMA integrity_check` kullanıyor — **yalnız SQLite**. Sunucu 2026-07-24'te PostgreSQL'e taşındığından beri "Yedek Al" **ham veritabanı hatasıyla** düşüyordu. Daha tehlikelisi **geri yükleme**: yedek dosyasını veritabanı **olmayan** bir hedefin üzerine kopyalamaya çalışıyordu. | Statik analiz + PostgreSQL'de koşan test | ✅ anlaşılır mesajla, **dosyaya dokunmadan** durduruluyor |
| **PRF-01** | **P2** | 20.000 satırlık rapor tarayıcıda **36.959 ms** ve **260.729 DOM düğümü**. Aynı sorgu sunucuda **162 ms** → darboğaz sorgu değil **çizim**. Rapor tavanı 50.000 olduğu için en kötü hâlde tarayıcı fiilen kilitleniyordu. | Gerçek tarayıcıda ölçüm | ✅ 36.959 ms → **378 ms** |
| **RPR-13** | **P2** | Tarih **zorunlu** bir rapordan (varsayılan "Bu Ay") tarih zorunlu **olmayan** bir rapora geçince alanlar dolu kalıyor ve yeni raporu **sessizce daraltıyordu**. Gerçek arayüzde yakalandı. | Muayene/Sigorta raporunda gelecek aydaki sigorta belgesi **görünmüyordu** | ✅ düzeltildi |
| **RPR-12** | **P2** | Rapor listesi, kullanıcının **çalıştıramayacağı** raporları gösteriyordu (Sorgula'ya basınca 403). Ayrıca yeni Personel raporu **kişisel veri** (ad, telefon, kullanıcı adı) gösterirken yalnız `reports` izniyle açılabilir olacaktı. | Gerçek arayüzde: depo personeli 6 ön muhasebe raporunu listede görüyordu | ✅ katalogda `RequiredModule`; web + masaüstü aynı süzme |
| **RPR-09** | **P2** | Operasyon rapor ekranında şube seçici **yok**; ama sunucu gövdedeki `branchIds`'i "şube seçme" yetkisi olanlar için uyguluyor ve **çalışma şubesinin yerine** geçiriyordu. Yetki kapısı korunduğu için **veri sızıntısı yoktu**, ama "operasyon raporu yalnız giriş yapılan şubeyi gösterir" güvencesi yetkiye bağlı hâle geliyordu. | R25/R26 testleri kırmızı | ✅ güvence koşulsuz |
| **RPR-10/11** | — | **Muayene/Sigorta** ve **Personel** raporları yoktu (ekranları, servisleri ve verileri vardı). | — | ✅ eklendi (katalog 19 → **21**) |
| **WEB-01+** | — | Devre koruması taraması iki büyük deliği açık bırakıyordu: **ifade gövdeli** yaşam döngüsü (10 sayfa/bileşen) hiç görülmüyordu ve yalnız `OnInitializedAsync`'e bakılıyordu (`OnAfterRenderAsync` 9 sayfada var). Ortak bileşenler ve Layout taranmıyordu. | Kasten bozma denemesi | ✅ kapsam genişletildi · **gerçek kodda yeni bulgu yok** |
| **TNT** | — | SEC-04 tek bir ucun hatası değil bir **kalıptı**; aynı kalıbın tekrar edip etmediği okuyarak değil **gerçek HTTP istekleriyle** ölçülmeliydi. | — | ✅ 13 senaryoluk süpürme · **tekrar yok** |

### 2.1 Denenip GERİ ALINAN değişiklik (dürüst kayıt)

**RPR-08.** Denetimde şu "tutarsızlık" göze çarptı: 14 operasyon raporundan 12'si `ReportScope` üzerinden
geçer (İZİNLİ ∩ **ÇALIŞMA ŞUBESİ**) ama **Stok Durumu** ve **Stok Sayım** `BranchAccess.Allowed` kullanır —
yani oturumun giriş şubesini yok sayar. `Effective`'e çevrildi ve **mevcut bir test kırıldı**
(`MaintenanceStockLocationTests`). Kural gereği "test yanlıştır" varsayılmadı, incelendi:

> Bu iki raporun filtre boyutu **şube değil, stoğun FİZİKSEL YERİDİR** (depo/şantiye). Kullanıcı Depo A'da
> çalışırken Depo B'den malzeme çekebilir (bakım stok lokasyonu, STK-04/05/06). Çalışma şubesini buraya
> uygulamak, ürünün **desteklediği** bu akışı kırardı.

Değişiklik **geri alındı**; gerekçe koda ve teste kalıcı olarak yazıldı ki aynı "düzeltme" ileride tekrar
denenmesin. Yerine kararı **iki yönden** kilitleyen testler eklendi: yetki uygulanır (sızıntı yok),
görünüm tercihi uygulanmaz (meşru akış kırılmaz).

> Bu turda kendi çalışmamda iki hata daha çıktı ve ikisi de **kasten bozma denemeleriyle** yakalandı:
> genişletilen devre koruması taramasındaki bir düzenli-ifade kaçış hatası (kural hiçbir şey görmüyordu)
> ve iki test kurulum hatası. Üçü de düzeltilip yeniden doğrulandı.

---

## 3. RAPORLAR modülü — 21 raporun tamamı

### 3.1 Kapsam haritası (koddan çıkarıldı, varsayılmadı)

| Rapor | Grup | Firma | Şube kapsamı | Gerekli izin |
|---|---|---|---|---|
| Araç Raporu | Operasyon | ✅ | ReportScope (izinli ∩ çalışma) | reports |
| **Muayene / Sigorta** 🆕 | Operasyon | ✅ | ReportScope (`vehicles.branch_id`) | reports + **inspection** |
| Stok Durumu | Operasyon | ✅ | `Allowed` — *fiziksel depo* (bkz. §2.1) | reports |
| Stok Hareketleri | Operasyon | ✅ | ReportScope | reports |
| Stok Sayım | Operasyon | ✅ | `Allowed` — *sayılan depo* (bkz. §2.1) | reports |
| Yakıt Tüketim | Operasyon | ✅ | ReportScope | reports |
| Depo Girişi | Operasyon | ✅ | ReportScope | reports |
| Bakım Raporu | Operasyon | ✅ | ReportScope | reports |
| Talep Raporu | Operasyon | ✅ | ReportScope | reports |
| **Personel Listesi** 🆕 | Operasyon | ✅ | ReportScope (`personnel.branch_id`) | reports + **personnel** |
| Cari Ekstre | Operasyon | ✅ | ReportScope | reports + **parties** |
| Cari Bakiye Özeti | Operasyon | ✅ | ReportScope | reports + **parties** |
| Fatura Özeti | Operasyon | ✅ | ReportScope | reports + **invoices** |
| Açık Faturalar / Vade | Operasyon | ✅ | ReportScope | reports + **invoices** |
| Tahsilat / Ödeme Özeti | Operasyon | ✅ | ReportScope | reports + **finance** |
| Kasa / Banka Özeti | Operasyon | ✅ | ReportScope | reports + **finance** |
| Malzeme — Şablonlu | **Yönetici** | ✅ | yok (malzeme kartı firma geneli — KARAR-7) | reports + yönetici |
| Malzeme — Şablon Dışı | **Yönetici** | ✅ | yok (aynı gerekçe) | reports + yönetici |
| Araç — Şablonlu | **Yönetici** | ✅ | `AllowedSql` (çalışma şubesini bilerek yok sayar) | reports + yönetici |
| Araç — Şablon Dışı | **Yönetici** | ✅ | `AllowedSql` (aynı) | reports + yönetici |
| Durum Rapor | **Yönetici** | ✅ | `Allowed` (şube bazlı özet) | reports + yönetici |

**Firma filtresi 21/21.** Şube kapsamı 18/21; kalan 3'ün kapsamsızlığı **tasarım gereğidir ve koddan
kanıtlanmıştır** (malzeme kartı firma genelidir; yönetici raporları çalışma şubesini bilinçli yok sayar).

### 3.2 Operasyon / Yönetici ayrımı — bu turda ölçülen

| | `/reports` Operasyon | `/reports/manager` Yönetici |
|---|---|---|
| Menüde kime görünür | herkese (rapor izni olana) | **yalnız yöneticiye** |
| Adresi elle yazınca | açılır | **"Yönetici Raporları yalnız yönetici yetkisiyle açılır."** |
| Şube seçici | **yok** | var |
| Kapsam | **yalnız giriş yapılan şube** | yetkili olduğu şubeler |
| Gövdede elle `branchIds` | **yok sayılır** (RPR-09) | uygulanır (yetkiliyse) |
| Rapor sayısı (izinlere göre) | değişir | değişir |

---

## 4. Yazılan yeni testler

| Dosya | Adet | Ne kanıtlar |
|---|---|---|
| `UpdateIntegrityTests.cs` | 7 | boş/null/boşluk/yanlış/yarım-inen paket reddi · doğru checksum kilidi · kurulumcunun kapıyı çağırdığı · sürüm karşılaştırması (ileri/aynı/geri/bozuk/min) |
| `ApiTenantSweepTests.cs` | 13 | firma kimliği alan **her uca** yabancı firma kimliği yazma (şube, makine, yedek, rapor kapsamı, rol/firma yetki, rapor gövdesi, export, kullanıcı oluşturma) + anonim + süper admin kilidi |
| `NewReportsTests.cs` | 28 | iki yeni raporun kolon/durum/iptal/firma/şube/çalışma-şubesi/tarih/araç filtresi/boş sonuç/yetki davranışı · katalog-servis kapısı tutarlılığı · RPR-12/13 · PRF-01 |
| `BackupDialectTests.cs` | 4 | SQLite yedeği çalışmaya devam ediyor · PostgreSQL'de yedek **ve geri yükleme** anlaşılır mesajla, dosyaya dokunmadan duruyor · yetki kapısı önce |
| `ApiReportScopeTests.cs` (ek) | 5 | operasyon ekranında elle şube listesi (ekran + **Excel içeriği açılarak**) · yönetici kipi kilidi · katalog süzmesi (izinsiz/izinli) |
| `ReportBranchScopeTests.cs` (ek) | 5 | RPR-08 kararının **iki yönlü** kilidi |
| `WebCircuitGuardTests.cs` | kapsam | ifade gövdeli + `OnAfterRenderAsync` + `OnParametersSetAsync` + ortak bileşenler |

---

## 5. Performans — ölçüldü, tahmin edilmedi

### Sunucu tarafı (SQLite, tek makine)

| Rapor | 20.000 hareket | 50.000 hareket |
|---|---|---|
| Stok Hareketleri (tavan 50k) | 162 ms | 287 ms |
| Stok Hareketleri (tavan 500) | 31 ms | 107 ms |
| **Muayene/Sigorta** (2.000 belge) | **7 ms** | 11 ms |
| **Personel** (500 kayıt) | **1 ms** | 5 ms |
| Stok Durumu | 0 ms | 0 ms |

**Yeni indeks/migration gerekmedi ve açılmadı.**

### Tarayıcı tarafı — asıl darboğaz buradaydı

| 20.000 satırlık rapor | Önce | Sonra |
|---|---|---|
| Sorgula → tablo görünür | **36.959 ms** | **378 ms** |
| DOM düğümü | **260.729** | **13.746** |
| Kolon filtresi tüm satırlarda mı | evet | **evet** (15.000'inci satır filtreyle bulundu) |
| Toplam satırı doğru mu | evet | **evet** (20.000 / 20.000,00) |
| Excel | eksiksiz | **eksiksiz** |

Sanallaştırma (`Virtualize`) yerine **çizim kırpması** seçildi: tablo sabit yükseklikli bir kaydırma
kabında değil; `Virtualize` bu bileşeni kullanan **tüm** ekranların görünümünü değiştirirdi. Kırpma
opsiyoneldir (`MaxRender`, varsayılan **sınırsız**) ve **yalnız rapor ekranı** uygular; kullanıcıya
açıkça bildirilir — sessiz kırpma yoktur.

---

## 6. Güvenlik — bu turda yeniden tarandı

| Konu | Sonuç |
|---|---|
| Firma kimliği alan uçlar | **13/13** senaryo doğru: ya reddediyor ya kendi firmasını dönüyor |
| Şube kimliği manipülasyonu | yetkisiz şube **403**; elle liste **yok sayılıyor** |
| Geliştirici modu (SEC-03) | 12 test yeşil; kod sabiti **süper admin olmadan hiçbir şey açmıyor**, döngüsel yetki yok |
| Sabit gömülü sır / izlenen sır dosyası | **yok** (`.env.*` ignore'da; depoda parola/anahtar dosyası yok) |
| Anonim uçlar | 3 uç (firma listesi, şube listesi, şube şifre doğrulama) — **giriş ekranı için gerekli**, hız sınırlı; parola/veri döndürmez |
| Yetki yükseltme | kullanıcı oluşturma/rol atama tavanları test edildi; yabancı firmaya kullanıcı **yazılmadığı DB'den de doğrulandı** |
| Yol geçişi (path traversal) | yedek indirmede `Path.GetFileName` ile kapalı |

---

## 7. Web / Masaüstü paritesi

- Rapor **motoru** ortak (`ReportService.Run` — tek switch). İki platform aynı hesabı yapar.
- Bu turdaki üç davranış değişikliği (RPR-12 liste süzmesi, RPR-13 tarih, yeni raporlar) **iki platforma
  birden** uygulandı ve testle kilitlendi.
- PRF-01 çizim sınırı **yalnız web** içindir — masaüstü tablosu farklı bir teknoloji kullanır ve aynı
  sorunu göstermez; sunucu tarafı ölçümleri iki platform için de geçerlidir.

---

## 8. Kalan gerçek problemler (kapatılmadı, gizlenmedi)

| ID | Önem | Durum |
|---|---|---|
| **YED-01/b** | **karar gerekiyor** | PostgreSQL için **dosya yedeği** hâlâ yok. Artık hata anlaşılır ve zararsız; ama gerçek bir `pg_dump` alma yeteneği **yeni bir özelliktir** (sunucu konteynerinde `pg_dump` yok). Bugün üretim yedeği sağlayıcının sürekli yedeğine (PITR) dayanıyor. |
| **PRF-01/b** | izlemede | Rapor tavanı 50.000 satır. Çizim artık sınırlı; sunucu sorgusu 287 ms. Sayfalı API (server-side pagination) ileride gerekebilir. |
| **TNT-04** | ürün gereği | Anonim uçlar firma/şube **adlarını** açar — giriş ekranı bunları seçtirmek zorunda. Hız sınırlı, veri yok. |
| **Satın Alma** | karar gerekiyor | `ReportCategory.Purchasing` etiketi var ama **satın alma domaini yok** (yalnız talep durumu olarak "Satın Alma Sürecinde" geçiyor). Sahte ekran üretilmedi. |

---

## 9. Dokunulmaması gereken kararlı alanlar

`BranchAccess` (tek şube otoritesi) · `ReportService.Run` dispatch · `AppScreens` kataloğu ·
migration kataloğu ve çalıştırıcı · senkron firma kapıları · idempotency indeksleri ·
**Stok Durumu / Stok Sayım'ın `Allowed` kullanması** (bkz. §2.1).

---

## 10. Test sonuçları (final)

| Koşu | Sonuç | Süre |
|---|---|---|
| **Taban** (tur başı) | 2221 geçti · 0 başarısız · 35 atlandı | 12 dk 57 sn |
| Ara koşu (YED-01 öncesi) | 2280 · 0 · 35 | 13 dk 07 sn |
| **Son koşu 1** | **2282 geçti · 0 başarısız · 37 atlandı** | 12 dk 21 sn |
| **Son koşu 2 (bağımsız)** | **2282 geçti · 0 başarısız · 37 atlandı** | 12 dk 38 sn |
| **PostgreSQL** (ayrı test DB) | **49 geçti · 0 başarısız · 0 atlandı** | 12 dk 51 sn |

İki bağımsız son koşu **birebir aynı** → kararsız (flaky) test yok. Tabana göre **+61 senaryo**,
regresyon **0**. Atlanan 37'nin tamamı PostgreSQL kapılıdır ve ayrı koşuda hepsi çalıştırılıp geçmiştir.
**Gizlenen, devre dışı bırakılan, gevşetilen veya retry ile örtülen test yoktur.**

Release derlemesi: **0 hata**. **Yeni migration YOK** → üretim şeması **72**'de kaldı, ek onay gerekmedi.

---

## 11. YAYIN — tamamlandı (2026-08-26)

Sıra `docs/DEPLOYMENT.md`'ye uygun: **önce API, sonra Web, en son masaüstü**.

| # | Adım | Sonuç |
|---|---|---|
| 1 | Yayın öncesi üretim durumu | disk **%42,8** · 3 paket · DB 18,13 MB · 1 firma · 3 kullanıcı · **2 kullanıcı çevrimiçi** |
| 2 | Yeni migration var mı | **YOK** → şema **72**'de kalır |
| 3 | `flyctl deploy -c fly.toml` | ✅ makine `started` · **API v166 → v167** |
| 4 | API sağlık | `/health` **200** |
| 5 | **PG gerçekten bağlandı mı** | ✅ **gerçek veri döndü** (1 firma · 3 kullanıcı · 18,13 MB) → boş SQLite'a **düşmedi** |
| 6 | Katalog canlıda | **21 rapor** · yeni `inspection` + `personnel` görünüyor |
| 7 | `flyctl deploy -c fly.web.toml` | ✅ makine `started` · **Web v190 → v191** |
| 8 | Web route'ları | `/` `/reports` `/reports/manager` `/branches` `/stock/movements` `/developer` `/inspection` `/personnel` → **hepsi 200** |
| 9 | Masaüstü publish 1.0.150 | ✅ **270 dosya · 243 MB** · 0 hata |
| 10 | Paketleme | `DepoWise-desktop-1.0.150.zip` · **89.969.530 bayt** |
| 11 | Sürüm yayını | ✅ `/api/releases/latest` = **1.0.150** · eski paketler budandı (3 paket kaldı) |
| 12 | İndirme ucu | **200** · **89.969.530 bayt** (birebir) |
| 13 | **Checksum** | ✅ **üç değer de aynı**: yerel = sunucu kaydı = sunucudan indirilen → `79DB5051E81436397BB5177DCEEFE101293BA8036DF58806DC8C4E70A3E04F0D` |
| 14 | Kurulum aracı | `/api/setup/download` **200** (71,9 MB) — `src/DepoWise.Setup` değişmediği için **yeniden yayınlanmadı** |
| 15 | Sürüm tutarlılığı | API **167** · Web **191** · Masaüstü **1.0.150** · Şema **72** |

### Yayın sonrası duman testleri (üretim, SALT OKUMA)

| # | Kontrol | Sonuç |
|---|---|---|
| 1 | Sunucu kaynakları | disk **%42,8** · CPU **%1,4** · bellek **%49,2** · 3 paket · crash-loop yok |
| 2 | Süper admin girişi | ✅ |
| 3 | Rapor kataloğu | **21** rapor |
| 4 | **Yeni rapor: Muayene/Sigorta** | **200** · 9 kolon · **126 ms** · 0 satır |
| 5 | **Yeni rapor: Personel** | **200** · 6 kolon · **105 ms** · 1 satır |
| 6 | **Rapor ↔ ekran tutarlılığı** | Muayene ekranı **0** kayıt → rapor **0** satır · Personel ekranı **1** kayıt → rapor **1** satır ✅ |
| 7 | Kimliksiz ziyaretçi | `/reports` `/reports/manager` `/personnel` → **girişe yönlendiriliyor** |
| 8 | Üretim logu | **0 hata / 0 exception** |

> **Üretim verisi hakkında not:** bu firmada **hiç şube tanımlı değil** (0 şube) ve muayene/sigorta
> kaydı yok. Yani şube kapsamıyla ilgili tüm düzeltmeler bugünkü veride **davranışı hiç değiştirmez**;
> ileride şube tanımlandığında doğru davranacaklardır. Bu, yayının risk seviyesini düşürür.

### Üretime yapılan işlemler (tam liste)
- İki `flyctl deploy` (API, Web) — kod dağıtımı.
- Bir sürüm yayını (masaüstü 1.0.150 paketi yüklendi; sunucu eski paketleri otomatik budadı).
- Süper admin ile **salt-okunur** doğrulama çağrıları (giriş, durum, katalog, iki rapor, muayene/personel listeleri).
- **Hiçbir iş verisi yazılmadı/silinmedi. Doğrudan SQL çalıştırılmadı. Migration çalıştırılmadı.**

### Yayın öncesi yedek — durum
⚠️ **Alınamadı ve zorlanmadı.** Üretim bağlantısı yalnız bir Fly *secret*'ıdır (`DEPOWISE_PG_URL`,
durum: `Deployed`); Fly secret'ları **geri okunamaz** ve yerelde tanımlı bir kopyası yok. Kullanıcının
kimlik bilgisi **istenmedi ve hiçbir yere yazılmadı**. Bu yayın **şemaya dokunmadığı** için (yeni
migration yok, şema 72) prosedür gereği durdurulması gerekmedi; koruma sağlayıcının sürekli yedeğidir (PITR).
Bu turda ayrıca **YED-01** bulundu: uygulamanın kendi yedekleme düğmesi PostgreSQL'de zaten çalışmıyordu
(bkz. §2 ve ADR-136).
