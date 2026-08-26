# SON DENETİM VE STABİLİZASYON TURU — 2026-08-26 (dördüncü tur)

> Amaç: yeni özellik değil; mevcut sistemin güvenli, tutarlı ve regresyonsuz olduğundan **kanıtla** emin olmak.
> "Raporda sorun görünmüyor" kabul edilmedi — her iddia ölçüldü.

---

## 1. Başlangıç baseline (yeniden ÖLÇÜLDÜ)

| Ölçüm | Sonuç |
|---|---|
| HEAD | `375eebc` = `origin/master`, ağaç temiz (yalnız korunan iki dosyanız) |
| API · Web · Desktop Release | **0 hata** (üçü de) |
| Tam test | **2386 geçti · 0 başarısız · 37 atlandı** — önceki raporla **birebir** |
| Migration | **1…72 kesintisiz**, mükerrer yok, katalog **72/72** |
| Üretim API | `/health` → ok |
| Üretim Web | 200 |
| Masaüstü sürümü | 1.0.152 · checksum `8664E6BB…3308` |

### ⚠️ Baseline'da bulunan ÖNEMLİ değişiklik
Önceki üç turun tekrarladığı **"üretimde 0 şube var, şube davranışı gözlemlenemiyor"** varsayımı
**artık geçersizdir**. Salt-okunur kontrolde üretimde **9 şube** görüldü:

```
ANKARA GENEL MERKEZ (G-MRKZ)
├── DÜZCE (DZC-01)        ├── KARAMAN (KRMN-01)
├── NEVŞEHİR (NVSHR-01)   └── TEST ŞANTİYE (TST-01)
BEYŞEHİR · DENİZLİ · OSTİİM · SİVRİHİSAR
```

Yani **üst/alt şube (ağaç) kod yolu artık canlıda çalışıyor**. Bu turun en önemli bulgusu (**SB-01**)
tam da bu yüzden ortaya çıktı — önceki turlarda gizli kalmıştı.

---

## 2. Tarama kapsamı

| Alan | Yöntem |
|---|---|
| Rapor yetkilendirme zinciri | Katalog → izin → menü → route → API → export uçtan uca çıkarıldı (21 rapor × 5 boyut) |
| Makine yaşam döngüsü | A–G senaryoları **gerçek HTTP** ile ölçüldü (izole sunucu) |
| Tenant | Okuma + **değiştirme + silme** boyutu eklendi (15 senaryo, veritabanı satırı kontrolüyle) |
| Şube ağacı | İki kapsam otoritesi (`BranchAccess` ↔ `ScopeResolver`) karşılaştırıldı |
| Web hata dayanıklılığı | Tüm HTTP çağrıları tek noktaya alındı; ağ ↔ yetki hatası ayrımı test edildi |
| Masaüstü sızıntı | 31 olay aboneliği + tüm `DispatcherTimer` kullanımları tarandı |
| N+1 | Döngü içinde sorgu üreten 17 aday tek tek incelendi |
| Web/Masaüstü paritesi | Ortak dosya listesi + tarih kuralı + rapor listesi süzmesi |
| Test kalitesi | **9 mutasyon** (kasten bozma), kaynak her seferinde aynen geri alındı |

---

## 3. Bulunan gerçek sorunlar

| ID | Sınıf | Kısaca | Düzeltildi | Test | Mutasyon |
|---|---|---|---|---|---|
| **RPR-15** | **Yetki açığı** | Role KAPATILAN ekranın verisi rapordan okunabiliyordu | ✅ | 12 | ✅ N1 |
| **SB-01** | **Veri görünürlüğü + engellenen işlem** | Şube ağacı ikinci kapsam otoritesinde uygulanmıyordu | ✅ | 8 | ✅ N8, N9 |
| **MAS-02** | Kararlılık / kaynak | Sayfa değişince masaüstü zamanlayıcısı birikiyordu | ✅ | 4 | ✅ N5, N6 |
| **BAG-01** | UX (istenen) | Sunucu kapalıyken kullanıcıya sebep söylenmiyordu | ✅ | 13 | ✅ N7 |

**Kapanan eski maddeler:** MAK-01/b (ölçüldü — çıkmaz yok), WEB-03 (teorik olduğu kanıtlandı),
"0 şube" sınırı (artık 9 şube var), "sunucusuz boş ekran" (BAG-01 ile düzeltildi).

---

## 4. Her sorunun kök nedeni ve düzeltmesi

### RPR-15 — Role kapatılan ekranın verisi rapordan okunabiliyordu · **yetki açığı**

**Kök neden.** `RoleGrantService` sözleşmesi şunu vaat eder: role kapatılan modül için
*"admin bypass'ı dahil API/UI erişimi kapanır"*. Ama rapor kapısı **yalnız `reports` modülünü**
soruyordu; raporun okuduğu ekranın kapalı olup olmadığına bakmıyordu.

**Kanıt (içerik kontrolüyle).** "Stok" ekranı Personel rolüne kapatılmışken:
- `AccessControl.Can(s,"stock",View)` → **false** (kapatma çalışıyor)
- `ReportService.Run(s,"stock-movements")` → **çalışıyor ve `GIZLI CIMENTO` satırını döndürüyor**

**Düzeltme (dar kapsam).** Kataloğa `DataModule` ("raporun okuduğu ekran") eklendi; kapı yalnız
**açıkça kapatılmış** modülü engeller. Tam izin istemek, bugün yalnız "Raporlar" yetkisi verilmiş
kullanıcıların erişimini keserdi → **çalışan davranış korundu**.

**Kapsam.** 21 raporun 8'i zaten tam modül izni istiyordu; 12'sine `DataModule` verildi;
`status` bilinçli istisna (çapraz-modül sayısal özet). Kapı **tek noktada** (`ReportService.Run`) →
masaüstü + web + API + Excel birlikte korunur. Rapor listesi iki platformda da süzülür ve **parite
testle kilitlendi**.

---

### SB-01 — Şube ağacı iki kapsam otoritesinde farklı uygulanıyordu · **veri görünürlüğü**

**Kök neden.** Projede **iki** kapsam otoritesi var:
- `BranchAccess` → `Expand` ile alt şubeleri kapsar (araç, rapor, stok hareketi bu yoldan geçer)
- `ScopeResolver` → `user_scopes` satırlarını **olduğu gibi** döndürür, ağacı **hiç genişletmiyordu**

`ScopeResolver`'ın canlı kullanıcısı `PersonnelService`'tir (hem liste hem yazma kapısı).

**Kullanıcıya yansıması.** "ANKARA GENEL MERKEZ"e yetkili bir kullanıcı:
- alt şantiyelerin **araçlarını/raporlarını görüyor** ✔
- ama aynı şantiyelerin **personelini görmüyor** ✘
- ve o şantiyelere **personel ekleyemiyor** ✘ ("şube kapsam dışı")

**Düzeltme.** `ScopeResolver` de `BranchTree.LoadDescendants` ile genişletir → iki otorite **aynı**
cevabı verir. Yeni kural yok; ŞB-04'ün kararı ikinci yerde de uygulanır. Genişleme yalnız **aşağı**
doğrudur: kardeş ve üst şubeler kapsama girmez.

> **Kendi testimin zayıflığı:** ilk kurguda ikinci bir üst şube yoktu, bu yüzden test **aşırı
> genişletmeyi yakalayamıyordu**. Kasten bozma denemesi (N9) bunu ortaya çıkardı; kurguya kapsam dışı
> bir alt şube eklendi ve ancak ondan sonra iki mutasyon da yakalandı.

---

### MAS-02 — Sayfa değişince masaüstü zamanlayıcısı birikiyordu · **kararlılık**

**Kök neden.** Her gezinmede yeni bir sayfa ViewModel'i oluşuyor; `DashboardViewModel` 60 saniyelik
bir zamanlayıcı başlatıyor ve **hiçbir yerde durdurmuyordu**. Çalışan zamanlayıcı kendi işleyicisini
canlı tutar → "Ana Ekran ↔ başka ekran" arasında N kez gidip gelen kullanıcıda **N zamanlayıcı**
birikir ve her biri **dakikada bir güncelleme sunucusuna ağ isteği** atar.

**MAS-01 ile aynı sınıf.** Bu yüzden yama değil **genel kural** yazıldı: zamanlayıcı başlatan her
ViewModel `IDisposable` uygular ve durdurur; kabuk açık sayfa değişince onu bırakır. Kural **mimari
testle taranır** → yeni ekranlarda tekrarlanamaz.

---

### BAG-01 — Sunucuya ulaşılamadığında sebep söylenmiyordu · **UX (talep edilen)**

**Kök neden.** API kapalıyken oturum düşmüyordu (doğru) ama ekran boş kalıyor ve hiçbir açıklama
görünmüyordu.

**Düzeltme (en küçük).** Tüm web istekleri zaten tek bir çağrıdan geçiyordu; yalnız o çağrı
sarmalandı. **Karar mantığı Application katmanındadır** çünkü web projesi ortak dosyaların aynasını
derlediği için test projesine referans **verilemez** (denendi → mevcut 4 testte tür çakışması → geri
alındı). Böylece riskli kısım — **ağ hatası ile yetki hatasının ayrımı** — gerçekten test edilir.

**Sınır.** Yalnız taşıma katmanı hatası "ulaşılamıyor"dur; 401/403/404/500 **sunucu yanıtıdır** ve
uyarı üretmez. Oturuma dokunulmaz. `TaskCanceledException` güvenle "zaman aşımı" sayılır çünkü
`ApiClient` hiçbir isteğe `CancellationToken` geçirmez (doğrulandı).

---

## 5. Kasten bozma (mutasyon) turu

Her mutasyondan sonra kaynak **aynen geri alındı** (her koşuda doğrulandı).

| # | Mutasyon | Sonuç |
|---|---|---|
| N1 | RPR-15 kapısı kaldırıldı | ✅ 4 test kırıldı |
| N2 | Makine kaydında kota kontrolü kaldırıldı | ✅ 3 test kırıldı |
| N3 | İptal edilen makine kendiliğinden aktifleşiyor | ✅ kırıldı |
| N4 | Cihaz jetonu firma süzmesi kaldırıldı | ✅ kırıldı |
| N5 | Pano zamanlayıcısı durdurulmuyor | ✅ 2 test kırıldı |
| N6 | Kabuk eski sayfayı bırakmıyor | ✅ kırıldı |
| N7 | Ağ ↔ yetki hatası ayrımı kaldırıldı | ✅ 4 test kırıldı |
| N8 | `ScopeResolver` ağacı genişletmiyor | ✅ 3 test kırıldı |
| N9 | Aşırı genişletme (kardeş şubeler de) | ❌ → kurgu güçlendirildi → ✅ 2 test kırıldı |

**9 mutasyon, 9'u da yakalandı** (biri ancak test kurgusu düzeltildikten sonra).

---

## 6. Doğrulanan ama sorun ÇIKMAYAN alanlar (kanıtla)

| Alan | Kanıt |
|---|---|
| **Tenant okuma/değiştirme/silme** | **15 senaryo**: B firmasının malzeme/araç/personel/şube/kullanıcı kaydı okunamadı, değiştirilemedi, silinemedi, parolası değiştirilemedi, pasife alınamadı — **hepsi veritabanı satırına bakılarak** |
| **Cihaz jetonu izolasyonu** | A firmasının jetonu B'nin `server_changes` verisini çekemedi (**içerik** kontrolü) |
| **Makine yaşam döngüsü** | A–E + G senaryolarının hepsi geçti; kota dolu iken bile yönetici kurtarabiliyor |
| **Tarih paritesi (web ↔ masaüstü)** | İki uygulama birebir aynı; web tarafı zaten `WebDateConversionTests` ile kaynak düzeyinde kilitli |
| **WEB-03** | Satır listeleri `Select(e => new Row{…})` ile kurulur → null eleman üretemez; gerçek tarayıcıda olay tetiklenmedi |
| **N+1** | 17 aday incelendi; 15'i sınırlı koleksiyon (tablo/lookup listesi), 2'si tek kaydın küçük alt listesi → **darboğaz yok** |
| **Migration** | 1…72 kesintisiz, katalog 72/72, her biri tek transaction |

---

*(Test sayıları, performans, GUI/masaüstü turları, yayın ve karar matrisi §7'den itibaren.)*

---

## 7. Test sayıları

| Koşu | Sonuç |
|---|---|
| Taban (tur başı, yeniden ölçüldü) | 2386 · 0 · 37 — önceki raporla **birebir** |
| Ara regresyon (RPR-15 + BAG-01 + MAS-02 sonrası) | 2419 · 0 · 37 |
| Ara regresyon (SB-01 + yeni süpürmeler sonrası) | 2445 · 0 · 37 |
| **Final koşu 1** | **2451 · 0 · 37 (14 m 04 s)** |
| **Final koşu 2 (bağımsız)** | **2451 · 0 · 37 (13 m 45 s) — **birebir aynı**** |
| **PostgreSQL (izole küme)** | **47 · 0 · **0 atlanan** (+ yedek lehçe kapısı 4 · 0 · 0)** |

Bu turda **6 yeni test sınıfı** eklendi. Devre dışı bırakılan, gevşetilen veya retry ile örtülen
test **yoktur**; atlanan 37'nin tamamı PostgreSQL kapılıdır ve ayrı koşuda çalıştırılmıştır.

---

## 8. Performans (ölçüldü — izole yerel sunucu)

| Ölçüm | Sonuç |
|---|---|
| Stok Hareketleri (50.000 satır) | **390 ms** · 6,55 MB · 50.000 satır |
| Stok Durumu / Araç / Bakım | 10 ms · 5 ms · 4 ms |
| Excel dışa aktarma (50.000) | 5,4 sn · 2,2 MB |
| Personel listesi (SB-01 ek sorgusundan sonra) | **4–17 ms** |
| Araç listesi · rapor kapsam ucu | 10 ms · 5 ms |

**Yorum.** Bu turun değişiklikleri ölçülebilir bir maliyet getirmedi: rapor süresi önceki turla aynı
aralıkta (329→390 ms; ölçüm bu kez web süreci de açıkken yapıldı), SB-01'in eklediği ağaç sorgusu
personel listesinde fark yaratmıyor. **Yeni indeks veya migration açılmadı.**

**N+1 taraması:** döngü içinde sorgu üreten 17 aday incelendi. 15'i sınırlı koleksiyon üzerinde
(tablo/lookup listesi, 3 kez dönen yeniden-deneme döngüsü). Kalan 2'si (malzeme muadilleri,
şablonun uyumlu araçları) **tek kaydın küçük alt listesinde** çalışır → darboğaz değil, teknik borç.

---

## 9. GUI turu (gerçek tarayıcı, izole, ÜST/ALT şube hiyerarşisiyle)

Ortam üretimdekine benzetildi: **GENEL MERKEZ** + altında **DÜZCE** ve **KARAMAN** şantiyeleri +
ilgisiz **DENİZLİ** şubesi. Kullanıcı YALNIZ GENEL MERKEZ'e kapsamlı.

| Senaryo | Sonuç |
|---|---|
| Giriş şube listesi | ✅ GENEL MERKEZ + **iki alt şantiye**; ilgisiz DENİZLİ **yok** |
| Personel ekranı | ✅ **DÜZCE + MERKEZ** personeli görünüyor (SB-01 düzeltmesi), DENİZLİ yok |
| Personel raporu | ✅ iki satır, şube etiketleri doğru |
| **RPR-15** — "Stok" ekranı role kapatıldı | ✅ rapor **403** (anlaşılır Türkçe mesaj) · katalog **15 → 12** · üç stok raporu da listeden düştü · menüde stok yok |
| **BAG-01** — API durduruldu | ✅ boş ekran yerine **"Sunucuya ulaşılamıyor"** şeridi + **TEKRAR DENE**; oturum ayakta |
| **BAG-01** — API geri açıldı, TEKRAR DENE | ✅ uyarı kalktı, menü döndü, ekran yüklendi |
| **WEB-03** — kontrollü deney | ✅ veri satırı tıklaması handler'ı TETİKLİYOR (kontrol); başlık satırı + 7 başlık hücresi + alt bilgi tıklaması → **devre çökmedi, konsolda 0 hata** |

---

## 10. İzole masaüstü turu

| Kontrol | Sonuç |
|---|---|
| Ortam | `DEPOWISE_ENVIRONMENT=IzoleDenetim` → ayrı klasör |
| Sunucu adresi | `serverurl.txt` ile **yerel** sunucuya yönlendirildi |
| Açılış | `host=dotnet` · `journal=wal` · `fk=True` · `writeRead=True` · hata yok |
| Migration | **72/72**, şema 72, **79 tablo** |
| Üretim | **Bağlanmadı** (üretim veri klasörüne ve sunucusuna dokunulmadı) |

> ⚠️ **Sınır (değişmedi):** Avalonia arayüzü bu ortamda otomatize edilemiyor → **ekran içi tıklama
> akışları sürülemedi**. Uydurma test yazılmadı. MAS-02 kuralı bu yüzden **mimari testle** taranır.

---

## 11. Üretim durumu ve yayın

| Bileşen | Öncesi | Sonrası |
|---|---|---|
| API (`fly.toml`) | v169 | **v170** |
| Web (`fly.web.toml`) | v193 | **v194** |
| Masaüstü | 1.0.152 | **1.0.153** |
| Şema | 72 | **72 (değişmedi)** |

Masaüstü paketi: **89.973.480** bayt · SHA-256 `EEEB772A922C574BFB557FC613520E975FA7FA8492D6AD04A8339802CA7153C2`.
Üretimde **hiçbir yazma işlemi yapılmadı**: SQL, migration, DDL, ACL, secret değişikliği yok.
Salt-okunur kontroller: `/health`, `/api/public/companies`, `/api/public/branches`, `flyctl status`.

**Yayın notu (§20):** 1.0.152'deki yazım hatası **sırf onun için yeni sürüm üretilerek değil**, zaten
kod değişikliği olan bu yayında düzeltilmiştir.

---

## 12. SON KARAR MATRİSİ

### A) Mutlaka düzeltilmesi gereken gerçek bug / güvenlik — **HEPSİ DÜZELTİLDİ**
| ID | Konu |
|---|---|
| RPR-15 | Role kapatılan ekranın verisi rapordan okunabiliyordu (yetki açığı) |
| SB-01 | Şube ağacı ikinci kapsam otoritesinde uygulanmıyordu (veri görünürlüğü + engellenen işlem) |

### B) Düzeltilmesi önerilen UX / kararlılık — **DÜZELTİLDİ**
| ID | Konu |
|---|---|
| MAS-02 | Sayfa değişince zamanlayıcı birikiyordu (bellek + boşuna ağ isteği) |
| BAG-01 | Sunucu kapalıyken kullanıcıya sebep söylenmiyordu |

### C) Teknik borç — **BİLEREK DOKUNULMADI**
- İki küçük N+1 (malzeme muadilleri · şablonun uyumlu araçları) — ölçüldü, darboğaz değil.
- `Infrastructure/Org/BranchService` üretimde kullanılmıyor (yalnız bir test kullanıyor) — **önceki bir
  turda incelenip "ölü kod" bulgusu GERİ ÇEKİLMİŞTİ**; tekrar açılmadı.

### D) Yeni özellik — **KAPSAM DIŞI**
- PostgreSQL dosya yedeği (`pg_dump` + sır + saklama alanı → operasyon işi).
- Satın Alma alanı (kodda domain YOK; sahte ekran üretilmedi).
- Sayfalı rapor API'si.

### E) Bilinçli ürün kararı / dokunulmayan davranış
- **Stok Durumu / Stok Sayım** fiziksel depo mantığı — üçüncü turda kanıtlanmıştı, **tekrar dokunulmadı**.
- **Durum Rapor** çapraz-modül özet olduğu için `DataModule` verilmedi (bilinçli istisna).
- **ARC-01** araç seçicisinin firma geneli olması — kanıt iki yöne de çekiyor (araçlar şubeler arası
  hareket eder), 12+ çağrı noktasını etkiler → **varsayımla değiştirilmedi, karar sizde**.
- **YET-01** işlevsiz iki yetki anahtarı — silmenin teknik riski ölçüldü (FK yok, migration gerekmez)
  ama yetki ağacından satır kaldırmak **ürün kararıdır** → **karar sizde**.

---

## 13. Yayın kanıtları (yayın sonrası, salt-okunur)

| Kontrol | Kanıt |
|---|---|
| API sağlık | `/health` → **200** |
| API sürüm | v169 → **v170** (`flyctl status`) |
| Web sürüm | v193 → **v194** |
| Web sayfaları | `/` `/login` `/reports` `/reports/manager` `/personnel` `/vehicles` `/stock` → hepsi **200** |
| Masaüstü sürüm | **1.0.153** yayınlandı; `api/releases/latest` bunu döndürüyor |
| **Üç yönlü sağlama** | yerel dosya = yayın metadata'sı = sunucudan indirilen paket → `EEEB772A…53C2`, **89.973.480 bayt** (üçü de aynı) |
| 5xx / istisna | API **0**, Web **0** |
| Crash-loop / yeniden başlatma | **yok** |
| PostgreSQL gerçekten bağlı | `/api/public/companies` gerçek veri döndürüyor (`Oze İnşaat`) |
| Canlı trafik | Gerçek masaüstü istemciler yayın sonrası senkron oluyor (`machines/register`, `sync/business-version` → 200) |
| **Disk (ADR-070)** | `/data` **%39** (351 MB / 974 MB) · `releases/` klasöründe **tam 3 paket** (1.0.151-152-153) |

> ⭐ Son satır bu turun kendi bulgusunu canlıda doğruluyor: `PruneOld` saklama politikası (KeepCount=3)
> üretimde **çalışıyor**. Bu turda o mekanizmanın **testi yoktu** — PKT1–PKT6 ile kilitlendi.

**§20 — 1.0.152 yazım hatası.** Canlı metin `"kimlik doguruluyor"` idi. **SQL ile üretim metadata'sı
DEĞİŞTİRİLMEDİ.** Zaten kod değişikliği içeren bu yayının notu doğru yazımla üretildi; güncelleme
ekranı en son sürümün notunu gösterdiği için kullanıcının gördüğü metin artık hatasızdır.

**Üretimde yapılmayanlar (§1/§19):** SQL INSERT/UPDATE/DELETE **yok**, migration **yok**, DDL **yok**,
secret değişikliği **yok**, ACL değişikliği **yok**, test verisi **yok**. Şema sürümü **72 → 72**.
Yapılan tek yazma işlemi, açıkça talep edilen **uygulama yayınıdır** (yeni sürüm paketi + iki deploy).
