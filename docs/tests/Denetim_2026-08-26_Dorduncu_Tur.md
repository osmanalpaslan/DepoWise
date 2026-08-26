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
