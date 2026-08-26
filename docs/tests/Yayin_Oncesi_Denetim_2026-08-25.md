# Yayın Öncesi Son Denetim · Onarım · Test Raporu — 2026-08-25 (2. tur)

> Odak: **veri güvenliği (firma/şube izolasyonu) · tüm raporların doğruluğu · stabilite · performans**.
> Bu tur bir önceki uçtan uca denetimin (ADR-121…124) üzerine yapıldı.

---

## 1. Başlangıç durumu (yeniden ölçüldü — eski rapora güvenilmedi)

| Ölçüm | Değer |
|---|---|
| HEAD | `8f906af` · `master` · origin ile senkron |
| Çalışma ağacı | temiz (yalnız kullanıcının kendi 2 dosyası izlenmiyor — **dokunulmadı**) |
| Release derlemesi | **0 hata** · 41 uyarı |
| Tam test paketi | **2165 geçti · 0 başarısız · 35 atlandı** (11 dk 12 sn) |
| Migration | 72 · katalog ↔ dosya birebir |
| Rapor sayısı | **19** (13 operasyonel + 6 ön muhasebe) |
| Üretim | API **200** (0,29 sn) · Web **200** (0,36 sn) |

---

## 2. Bulunan gerçek sorunlar ve çözümleri

Her bulgu için **önce hatayı üreten test yazıldı ve gerçekten kırıldığı görüldü**, sonra düzeltildi.

| ID | Önem | Sorun | Kanıt (düzeltme öncesi) |
|---|---|---|---|
| **SEC-03** | **P1** | Geliştirici modu: *Ayarlar* ekranını açabilen **herkes** sabit kodu girip süper admin yetkisine geçiyordu; yazdığı veri eşitlemeyle sunucuya gidiyordu | 12 testin **9'u kırıldı** |
| **RPR-06** | **P1** | Masaüstü raporlarında **bitiş gününün tamamı düşüyordu** (25.08 raporunda 25.08 kayıtları yok); web ile farklı sonuç | Gerçek rapor üzerinden boş sonuç gösterildi |
| **RPR-04** | P2 | Rapor **filtre listeleri** kapsamsızdı: tek şubeye yetkili personel firmanın **tüm araç plakalarını** ve **tüm personel adlarını** görüyordu | API testlerinin **2'si kırıldı** |
| **RPR-07** | P2 | İki rapor menüsü **aynı ekranı** açıyordu; ayrım kozmetikti. Web oturumu **çalışma şubesini taşımıyordu** (R33) → depo personeli tüm izinli şubelerini görüyordu | Yeni davranış 8 API senaryosuyla kilitlendi |
| **SEC-04** | P2 | `GET /api/backups` firma parametresini **doğrulamıyordu** → başka firmanın makine/yedek adları listelenebiliyordu | 3 testin **2'si kırıldı** |

---

## 3. SEC-03 — çözüm ayrıntısı (katman katman)

"UI'da gizlemek" güvenlik sayılmadı; **altı katman** birden kapatıldı:

| Katman | Önce | Sonra |
|---|---|---|
| Etkinleştirme (masaüstü) | yalnız kod karşılaştırması | `DeveloperMode.TryActivate` → **ham süper admin rolü** + kod |
| Gezinme (masaüstü `Navigate`) | kapı yok | `CanActivate` kapısı |
| Menü (masaüstü) | katalogdaki sözde-anahtarlar **hiç uygulanmıyordu** | `@super`/`@admin`/`@superr` artık masaüstünde de uygulanıyor |
| Web sayfası | `Auth.IsAdmin` | `Auth.IsSuperAdmin` |
| Web menüsü | `settings` modülü | `WebPermOverride: "@super"` |
| **Sunucu ucu** | `AccessControl.IsAdmin` (firma admini yetiyordu) | `s.IsSuperAdmin` |

**Kritik tasarım kararı:** kapı **`AccessControl.IsAdmin` OLAMAZ** — o metot `DeveloperMode.IsActive`'i
de sayar, yani mod bir kez açıldığında kapı **kendi kendini** açık tutardı (döngüsel yetki). Bu, ayrı bir
testle (SEC-03h) kilitlendi.

---

## 4. Raporlar — tam denetim

### 4.1 Envanter (19 rapor, tek katalog)
Stok Durumu · Stok Hareketleri · Stok Sayım · Araç Raporu · Araç Şablonlu · Araç Şablon Dışı ·
Yakıt Tüketim · Depo Girişi · Bakım · Talep · Malzeme Şablonlu · Malzeme Şablon Dışı · Durum Raporu ·
Cari Ekstre · Cari Bakiye Özeti · Fatura Özeti · Açık Faturalar/Vade · Tahsilat/Ödeme · Kasa/Banka.

`ReportService.Run` içindeki **tek `switch`** 19 anahtarın hepsini karşılar → web ve masaüstü aynı listeyi
ve aynı hesaplamayı kullanır (parite yapısal olarak garanti).

### 4.2 Kapı matrisi (19 metot tek tek tarandı)

| Kontrol | Sonuç |
|---|---|
| Yetki (`AccessControl.Require`) | **19/19 var** |
| Firma filtresi (`company_id=@c`) | **19/19 var** |
| Şube kapsamı | 16/19 var · 3'ü **firma-geneli tasarım** (malzeme katalogu şubesizdir) |
| Tarih filtresi | ilgili raporların hepsinde parametreli |
| `Sorgula` kapısı (`EnsureRunnable`) | 13/13 operasyonel raporda var; ön muhasebe raporlarında yok (API zaten `Executed: true` gönderir) |

### 4.3 Doğrudan uç (API) güvenlik testleri — 23 senaryo
Arayüz testi yapılmadı; **gerçek HTTP** ile denendi:

| Senaryo | Sonuç |
|---|---|
| Depo personeli → kendi şubesi | veri geliyor ✅ |
| Depo personeli → **yetkisiz şube ID'si elle yazıldı** | veri **gelmiyor** ✅ |
| Depo personeli → **yabancı firma ID'si** | **403** ✅ |
| Depo personeli → **yetkisiz çalışma şubesi** | **403** ✅ |
| Yetkisiz kullanıcı → rapor ucu | **reddedildi** ✅ |
| Anonim → rapor ucu / kapsam ucu | **401** ✅ |
| Başka firma admini → A firmasının verisi | **görmüyor** ✅ |
| Export → yetkisiz kullanıcı | **reddedildi** ✅ |
| Export → aynı kapsam | ekranla **aynı yoldan** geçiyor ✅ |
| Yönetici raporu → personel / admin | **kapalı / açık** ✅ |
| Boş sonuç | **200** + boş dizi, ekran çökmüyor ✅ |
| Filtre listeleri (araç/personel/şube) | **kapsamla kırpılıyor** ✅ |

---

## 5. Normal Rapor / Yönetici Raporu ayrımı (§4 talebi)

**Tespit:** iki menü girişi aynı route + aynı gezinme anahtarını kullanıyordu → tek ekran. Ayrım yalnız
web menüsündeki görünürlük kapısıydı; raporu **çalıştırmak** hiçbir yerde ayrılmıyordu.

**Uygulanan çözüm — tek bileşen, iki kip (kod kopyalanmadı):**

| | Operasyon Raporları | Yönetici Raporları |
|---|---|---|
| Route / gezinme | `/reports` · `reports` | `/reports/manager` · `reports:manager` |
| Şube kapsamı | **yalnız çalışma şubesi** (girişte seçilen) | izinli şubeler |
| Şube seçici | **YOK** | var (yetkisi olana) |
| Rapor listesi | yalnız `Standard` | tümü |
| Menü kapısı | modül yetkisi | `@admin` (artık **iki platformda**) |

**Depo personeli şube izolasyonu nasıl sağlandı (sunucu tarafı):**
1. İstek `operatingBranchId` taşır (masaüstünde oturumda zaten vardı; **web taşımıyordu** — R33).
2. Sunucu `BranchAccess.Require` ile **doğrular** → kapsam dışıysa **403**.
3. Değer oturumun **kopyasına** yazılır; `BranchAccess.Effective` zaten *izinli ∩ istenen ∩ oturum*
   kesişimini alır → **kapsam genişletilemez**, yalnız daralır.
4. Alan gönderilmezse davranış **eskisiyle birebir aynı** (eski istemciler kırılmaz).
5. Desen içe-aktarma ucundan alındı; **ikinci bir kapsam mekanizması kurulmadı**.

> ⚠️ **Bilinçli davranış değişikliği:** yönetici olmayan kullanıcı 5 yönetici raporunu (şablon dökümleri +
> Şube Bazlı Özet) artık çalıştıramaz. Gerekçe: bu raporlar oturumun çalışma şubesini **bilinçli olarak**
> yok sayar (ürün kararı, `BranchScopeTests` ile kilitli) → "yalnız giriş yapılan şube" kuralı orada
> **sağlanamaz**. Web menüsü bunu zaten `@admin` ile ima ediyordu ve Excel yetkisi de ayrıydı.

---

## 6. Performans — ölçüldü (tahmin yok)

Ortam: SQLite · 10 şube · 3.000 araç · 2.000 personel · 2.000 malzeme · **30.000 stok hareketi**.

| İşlem | Süre |
|---|---|
| Araç filtresi — kapsamsız (3.000 araç) | **6 ms** |
| Araç filtresi — kapsamlı (tek şube) | **0 ms** |
| Personel filtresi — kapsamsız / kapsamlı | **1 ms / 0 ms** |
| Rapor: Stok Hareketleri — admin (30.000 satır) | **196 ms** |
| Rapor: Stok Hareketleri — **tek şube kapsamlı** | **31 ms** |
| Rapor: Stok Hareketleri — **çalışma şubesi (RPR-07)** | **28 ms** |
| Rapor: Araç Şablon Dışı — admin | **4 ms** |

**Sonuç:** bu turun kapsam düzeltmeleri performans **maliyeti getirmedi**, tersine depo personelinin en
sık çalıştırdığı raporu **7 kat hızlandırdı** (196 → 28 ms) ve taşınan satırı **10 kat azalttı**
(30.000 → 3.000). Yeni indeks/migration **gerekmedi ve açılmadı**.

---

## 7. Menü / Ekran Yönetimi regresyonu

Mevcut 24 + 18 + 12 test yeşil. Kullanıcının listelediği maddelerin karşılıkları:
tek kaynak (AppScreens) · web ve masaüstü menüsü katalogdan · platform görünürlüğü · ad değişince kimlik
sabit · grup anahtarı ↔ görünen ad ayrımı · sıralama · yetki ile görünürlük ayrımı · **yönetim ekranı
kendini kapatamaz** (P3/P5/P6) · tek platformlu ekranlar · geçersiz grup reddi · firma izolasyonu ·
tablo yoksa menü çökmez.

⚠️ Menü düzeni **ekran ANAHTARIYLA** saklanır (`screen_menu_layout.screen_key`) → bu turda değişen
route ve gezinme anahtarı **kayıtlı düzeni etkilemez**.

---

## 8. Yeni özellik olarak ayrılanlar (bu turda YAPILMADI)

- **Muayene / Sigorta raporu** — yok (ekranı ve uyarıları var).
- **Personel raporu** — yok (ekranı ve Excel dışa aktarımı var).
- **`Purchasing` kategorisi** — enum'da tanımlı, hiçbir rapor kullanmıyor.

Üçünün de "menü/route/isim sorunu" **olmadığı** doğrulandı: katalogda kayıtları yok ve aynı işlevi gören
başka bir ekran yok. Eklenmeleri kolon/filtre kararı gerektirir → kullanıcı kararına bırakıldı.

---

## 9. Test sonuçları (final)

| Koşu | Sonuç | Süre |
|---|---|---|
| **Taban** (tur başı) | 2165 geçti · 0 başarısız · 35 atlandı | 11 dk 12 sn |
| Ara koşu | 2217 · 0 · 35 | 12 dk 35 sn |
| **Son koşu 1** | **2221 geçti · 0 başarısız · 35 atlandı** | 12 dk 08 sn |
| **Son koşu 2 (bağımsız)** | **2221 geçti · 0 başarısız · 35 atlandı** | 12 dk 45 sn |
| **PostgreSQL** (ayrı test DB) | **45 geçti · 0 başarısız · 0 atlandı** | 15 dk 04 sn |

İki bağımsız son koşu **birebir aynı** → kararsız (flaky) test yok.
Tabana göre **+56 test**; **regresyon 0**.
Atlanan 35'in tamamı PostgreSQL kapılıdır (ayrı koşuda hepsi çalıştırıldı ve geçti) —
gizlenen, devre dışı bırakılan, gevşetilen **hiçbir test yok**.

Release derlemesi: **0 hata**. **Yeni migration YOK** → üretim şeması 72'de kalır, ek onay gerekmez.

---

## 10. Gerçek arayüz (GUI) turu — ve orada bulunan hata

Yerel API + web **sıfır veritabanıyla, ayrı dizinde** ayağa kaldırıldı (kullanıcının geliştirme
veritabanına ve **üretime dokunulmadı**). İki şube, iki araç ve bir **depo personeli** kuruldu.

| Kontrol | Sonuç |
|---|---|
| Giriş → şifre belirleme → şube seçimi | ✅ |
| Depo personelinin giriş şube listesi | ✅ **yalnız kendi şubesi** ("Tüm Şubeler" yok) |
| Admin · Operasyon ekranı | ✅ 14 rapor · şube seçici YOK |
| Admin · Yönetici ekranı | ✅ 19 rapor · şube seçici VAR |
| Depo personeli · Operasyon ekranı | ✅ 14 rapor · "Yalnız giriş yaptığınız şubenin verileri gösterilir." |
| Depo personeli · menüde Yönetici Raporları | ✅ **görünmüyor** |
| **Depo personeli · adresi ELLE yazarak /reports/manager** | 🔴 **AÇILIYORDU** → düzeltildi |
| Aynı senaryo, düzeltmeden sonra | ✅ "Yönetici Raporları yalnız yönetici yetkisiyle açılır." · liste ve Sorgula YOK |
| Yönetici raporunu çalıştırma denemesi (düzeltmeden önce) | ✅ sunucu **reddetti** → "Rapor alınamadı." (veri hiçbir zaman sızmadı) |
| Konsol / sunucu logu | ✅ hata yok |

> **UI-01 (aynı turda bulundu):** rapor bilgi notlarında tırnak karakterleri bozulmuş ve kullanıcıya
> `0022Atanmamış0022` olarak **görünüyordu** (3 rapor, 8 yer). Düzeltildi.

> **Masaüstü uygulaması bilinçli olarak çalıştırılmadı:** açıldığında ÜRETİM sunucusuna bağlanıp yereldeki
> veriyi göndermeye başlar. Masaüstü değişiklikleri kaynak-kilidi testleriyle doğrulandı ve rapor/kapsam
> mantığı iki platformda **ortak koddadır**.

---

## 11. YAYIN — TAMAMLANDI (2026-08-26)

İlk denemede Fly.io hesabındaki **ödenmemiş fatura** yüzünden uzak derleyici 403 vermişti; kullanıcı
faturayı kapattıktan sonra yayın **eksiksiz tamamlandı**. İlk başarısız denemede üretim etkilenmemişti
(derleme hiç başlamamıştı).

### Yayın sırası (DEPLOYMENT.md'ye uygun: önce API, sonra Web, en son masaüstü)

| # | Adım | Sonuç |
|---|---|---|
| 1 | Yayın öncesi kontrol | HEAD `7d70336` · ağaç temiz · API 200 · Web 200 |
| 2 | Yeni migration var mı | **YOK** → şema **72**'de kalır, ek onay gerekmedi |
| 3 | `flyctl deploy -c fly.toml` | ✅ makine `started` · **API v165 → v166** |
| 4 | API sağlık | `/health` **200** → `{"status":"ok"}` |
| 5 | **PG gerçekten bağlandı mı** | ✅ giriş OK · **gerçek veri döndü** (1 firma · 3 kullanıcı · 18,12 MB) → boş SQLite'a **düşmedi** |
| 6 | `flyctl deploy -c fly.web.toml` | ✅ makine `started` · **Web v189 → v190** |
| 7 | Web sağlık + route | `/` `/reports` **`/reports/manager`** `/branches` `/stock/movements` `/developer` → hepsi **200** |
| 8 | Masaüstü publish 1.0.149 | ✅ **270 dosya · 243 MB** (1.0.148 ile aynı ağaç yapısı) |
| 9 | Paketleme | `DepoWise-desktop-1.0.149.zip` · **89.966.963 bayt** |
| 10 | Sürüm yayını | ✅ `/api/releases/latest` = **1.0.149** · eski paketler budandı (3 paket kaldı) |
| 11 | İndirme ucu | `/api/releases/1.0.149/download` **200** · **89.966.963 bayt** (birebir) |
| 12 | **Checksum doğrulaması** | ✅ **üç değer de aynı**: yerel paket = sunucu kaydı = sunucudan indirilen → `AE52DEC4B11B462B9AF9C3F7B9E605F26569F22125397E6DF2E56F868FBFDEA3` |
| 13 | Kurulum aracı | `/api/setup/download` **200** — `src/DepoWise.Setup` bu turda değişmediği için **yeniden yayınlanmadı** |
| 14 | Sürüm tutarlılığı | API **166** · Web **190** · Masaüstü **1.0.149** |
| 15 | Yayın sonrası sağlık | API 200 · Web 200 · disk **%41,9** (408/973,7 MB) · CPU %1,8 · bellek %50,9 |
| 16 | Yeni route canlı doğrulaması | `/reports/manager` kimliksiz ziyaretçiyi **girişe yönlendiriyor** (deny-by-default) · konsolda hata yok |

### Yayın öncesi yedek — durum
⚠️ Yerelde saklı üretim parolası geçersiz olduğu için (kullanıcının rotasyonu) **taze bir `pg_dump`
alınamadı**; parola sohbette **istenmedi**. Risk düşük tutuldu çünkü **bu yayın şemaya dokunmuyor**
(yeni migration yok, şema 72'de kaldı) ve Neon'un sürekli PITR yedeği devrede. Yine de bir sonraki
şema değişikliğinden önce güncel bağlantıyla yedek alınması gerekir.

### Üretime yapılan işlemler (tam liste)
- İki `flyctl deploy` (API, Web) — kod dağıtımı.
- Bir sürüm yayını (masaüstü 1.0.149 paketi yüklendi; sunucu 2 eski paketi otomatik budadı).
- Süper admin ile **salt-okunur** doğrulama çağrıları (giriş, `/api/server/status`, `/api/releases/latest`).
- **Hiçbir iş verisi yazılmadı/silinmedi. Doğrudan SQL çalıştırılmadı. Migration çalıştırılmadı.**
