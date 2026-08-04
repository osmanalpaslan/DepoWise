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

> 🗂️ **Çok görevli takip:** Aynı anda birden fazla iş yürütülüyor (PostgreSQL geçişi + babanın
> uygulamasına geliştirmeler). "Nerede kaldık / şu işe dön" için tek yer: **[docs/GOREV_PANOSU.md](docs/GOREV_PANOSU.md)**.
> 🔒 **Altın kural:** Babanın canlı gerçek verisine dokunulmaz — geçiş kopyayla, ayrı DB'de yapılır.

---

## 1. Bu proje nedir? (tek paragraf)

**DepoWise** — çok firmalı (multi-tenant) depo/stok/araç/bakım/yakıt yönetim sistemi.
Üç parça, tek beyin: **Masaüstü** (Windows/.NET 8 + Avalonia, yerel SQLite) + **Web** (Blazor Server/.NET,
MudBlazor, tarayıcı) + **API** (sunucu, Fly.io, SQLite). İş kuralları ve yetkiler API'de tek yerde. Detaylı
çalışma mantığı: [docs/PROJE_REHBERI.md](docs/PROJE_REHBERI.md) (ortak defterimiz).
> Not: `apps/web` (eski Next.js denemesi) 2026-06-27'den beri donmuş; aktif web `src/DepoWise.Web`'dir (ADR-057).

---

## 2. ŞU AN NEREDEYIM? (son güncelleme: 2026-07-26)

### 🏷️ MARKA DEĞİŞTİ: DepoWise → **Alpnex** (2026-07-26, masaüstü **1.0.97** CANLI)
Proje adı hukuken başkasına ait olduğu için marka **Alpnex** oldu. Baba tüm veriyi (yerel+sunucu) sıfırladı
→ yerel klasör adı güvenle değişti (taşıma gerekmedi). **Seçenek A uygulandı:**
- **DEĞİŞTİ:** görünür isimler (web PageTitle/başlık, masaüstü pencere başlıkları+üst bar, Kurulum "Alpnex
  Kurulum" + Alpnex.lnk), merkezî marka (`BrandingSettings.Default`=Alpnex), yerel klasör/DB
  (`%LOCALAPPDATA%\Alpnex\...\alpnex.db`, update/logs/machine/branding + Belgeler\Alpnex_Yedekler),
  **logolar** (web `wwwroot` + masaüstü `Assets`; yeni şeffaf logodan üretildi).
- **KALDI (kasıtlı, A):** iç kod adı `DepoWise.*` namespace/assembly + **exe `DepoWise.Desktop.exe`**
  (kullanıcı görmez) + **Fly altyapısı** (`depowise-erp`/`depowise-web` app adları, URL, Neon `depowise_prod`,
  secret `DEPOWISE_*`) + varsayılan firma-id `"DEPOWISE"` (iç kimlik).
- **KAPSAM DIŞI:** `login-bg.png` / `login-hero.png` (yeni arka plan görseli verilmedi → dokunulmadı).
- Test 583/594 (yol/marka assert'leri Alpnex'e güncellendi). Web canlı doğrulandı (sekme "Giriş — Alpnex").
- ⚠️ **Kurulum notu:** klasör adı değiştiği için **en temiz yol yeni Kurulum aracıyla SIFIRDAN kurmak**
  (Alpnex klasörü + Alpnex.lnk). Eski `%LOCALAPPDATA%\DepoWise\` + eski kısayol elle silinebilir. (Oto-güncelleme
  de çalışır ama klasör geçişi nedeniyle bir kez fazladan güncelleme turu olabilir.)
- Envanter/analiz: [docs/REBRAND_ANALIZI.md](docs/REBRAND_ANALIZI.md).

### 🔐 İçe/dışa aktarım yetki ayrımı (2026-07-26, masaüstü **1.0.96** CANLI)
- `import_export` artık yalnız **İÇE AKTARIM**; **`export`** ayrı modül (Migration056: mevcut import_export
  sahiplerine export otomatik verildi — kimse sessizce kaybetmesin). Deny-by-default.
- Masaüstü + web: yetkisi olmayan kullanıcı için menü (import VEYA export ile görünür) ve **liste Excel
  butonları** (Malzeme/Araç/Günlük) "yetkiniz yok" uyarısı verir + işlem engellenir. API export uçları
  `Require(export)` → 403. Reports export kendi özel-buton yetkisinde (dokunulmadı).
- API+Web deploy (Migration056 canlı PG'de), masaüstü 1.0.96. Test 583/594.
- **Karaman veri notu (Tema B için):** firma OZE, KARAMAN şubesi var; test kullanıcısı test.personel / TEST
  ŞANTİYE. Beklenen: ortak malzeme listesi HER şubede; **stok şube-bazlı** → TEST ŞANTİYE'de stok 0, mevcut
  stok Karaman'da; başka şubede manuel giriş olmadan otomatik stok gelmez.

### 📥 İçe aktarımda zorunlu şube seçimi (2026-07-26, masaüstü 1.0.95)
- İçe aktarım ekranında **"Şube (zorunlu)"** seçici: **"Tüm Şubeler"** (firma geneli) + firmanın şubeleri.
  **Seçim yapılmadan import ENGELLENİR.** Seçilen şube oturum kopyasıyla (OperatingBranchId override) tüm
  import'lara geçer (yakıt/bakım/günlük/stok op_branch_id; araç/personel satırında Şube boşsa bu şubeye düşer).
  Seçilen hedef, çalışma şubesinden **farklıysa onay uyarısı** çıkar. Import masaüstüne özel (web'de import yok).
- ⏳ **Bekleyen (TEMA B — canlı veri):** babanın şubesi **Karaman**; mevcut kayıtları Karaman'a atama +
  **şube-bazlı stok** (stok_balances `material_id`→`material_id+şube`). Canlı stok defteri işi → verinin
  KOPYASINDA test edilip öyle canlıya alınacak. Karaman kararı alındı, uygulama onay + kopya-test bekliyor.

### 🔁 Malzeme modeli DEĞİŞTİ + Yedek yetkisi (2026-07-26, masaüstü 1.0.94)
- **Malzeme = ortak firma-geneli katalog** (kullanıcı kararı): madde 1'in şube-liste filtresi **geri alındı**;
  malzeme tüm şubelerde aynı görünür. **Ayrım STOK'a taşınacak** → **şube-bazlı stok** ayrı, büyük, canlı-defter
  işi olarak **PLANLANDI, henüz yapılmadı** (bkz. aşağıdaki "Sıradaki tek iş" ve karar notu). `materials.branch_id`
  kolonu duruyor (zararsız köken etiketi).
- **Yedek Yönetimi** masaüstünden kaldırıldı (web-only); web'de yalnız **süper + kısıtlı süper admin** görür
  (API `/me/menu` → `isRestrictedSuperAdmin`; NavMenu `@superr`; Backup.razor deny-by-default). Geri yükleme
  süreci korumalı süper-admin ekranı olarak sonra tasarlanacak (canlıyı doğrudan ezmeyen, doğrulama-kopyalı).
- API+Web deploy, masaüstü 1.0.94 yayında. Test 582/593.

### 🆕 4 maddelik istek TAMAM + CANLIYA ALINDI (2026-07-26, Opus 4.8) — masaüstü **1.0.92 YAYINDA** + API/Web deploy
Kullanıcının 4 isteği yapıldı ve **yayınlandı**: **API deploy** (Migration055 canlı PG'de, health 200) +
**Web deploy** (depowise-web, 200) + **masaüstü 1.0.92** (sunucuda "en güncel = 1.0.92", checksum `1a04091f…`, 85.2 MB).
Bu paket **birikeni** kapsar: rol atama güvenliği (eski 1.0.92 planı) + foto biçim uyarısı + detay paneli oto-kapanma + madde 1-4.
1. **Malzemeler şube-bazlı** (Madde 1, commit f625f65): `materials.branch_id` (Migration055) + `BranchScope`
   ile seçili şubeye filtre. Şubesiz eski kayıtlar HER şubede görünür (babanın canlı verisi gizlenmez).
   Malzeme kodu benzersizliği firma-geneli kaldı (canlı veride riskli index değişimi yapılmadı).
2. **Aranabilir alanlar** (Madde 2, commit 5449da2): masaüstü Kategori/Alt Kategori/Birim/Marka + tüm
   personel/sürücü açılırları `AutoCompleteBox` (metinle ara). Web zaten aranabilir (`LookupSelect`).
3. **Muadil malzeme köprüsü** (Madde 3, commit 769f211): malzeme detay panelinde muadiller tıklanınca
   ilgili malzemenin detayını açar (masaüstü). Web'de malzeme detay paneli yok → yalnız masaüstü.
4. **Uyarılar kategori butonları + bakım bug** (Madde 4, bu commit): Ana ekran + Uyarılar ekranında
   Malzeme/Bakım/Sigorta-Muayene/Yakıt **sayılı butonlar** (tıkla→filtrele, tekrar tıkla→Tümü); masaüstü+web.
   **Bug düzeltildi:** araca ATANIP hiç yapılmamış bakım tanımı uyarı vermiyordu → artık "İlk bakım yapılmadı"
   (Overdue) uyarısı çıkar. **Test 582/593 yeşil** (+1 yeni bakım testi).
> ✅ CANLIDA: API (Migration055 → canlı PG'de `materials.branch_id` var) + Web + masaüstü **1.0.93**.
> Makineler bir sonraki girişte 1.0.93'ü indirir. Web (Alerts/Home kategori butonları) depowise-web'de yayında.
> **1.0.93 (2026-07-26):** Uyarılar ana ekranda+Uyarılar ekranında ilk açılışta LİSTELENMEZ — yalnız kategori
> butonları+sayıları görünür; liste ancak ilgili butona tıklanınca gelir (tekrar tıkla → gizle). Masaüstü+web.

### 🟢 Tek bakışta güncel durum

| Ne | Durum |
|---|---|
| **PostgreSQL geçişi** | ✅✅ **CANLIYA ALINDI (2026-07-24)** — **sunucu (`depowise-erp`) + web PostgreSQL'de** (Neon `depowise_prod`). Masaüstü SQLite'ta kaldı (eşitleme API üzerinden PG'ye yazar). Gerçek verinin KOPYASIYLA prova edildi, canlıya alındı; eski SQLite yedekte (`/data/depowise-server.db`, el değmedi). Geri dönüş: `flyctl secrets unset DEPOWISE_PG_URL`. Detay: [docs/GOREV_PANOSU.md](docs/GOREV_PANOSU.md) Görev A. |
| **Testler** | **591 test** (580 SQLite yeşil + 11 gerçek Neon PG; `dotnet test`) + canlı eşitleme QA **7/7** |
| **1.0.91 (2026-07-25)** | **Şifre sıfırlama + kullanıcı görünürlük** sunucu-tabanlı (masaüstü çevrimiçiyken sunucudan okur/yazar → değişiklik hedefe ulaşır). **Şube-bazlı veri filtreleme:** belirli şubeyle girişte veri o şubeye filtrelenir ("Tüm Şubeler"→hepsi; şubesiz eski kayıtlar korunur); araç/günlük/yakıt/bakım/talep/stok + NORMAL raporlar. Yönetici raporları filtresiz (tüm şubeler). |
| **Son iki düzeltme (2026-07-25, 1.0.90)** | **Çıkış hızı:** kapanış push beklemesi 10sn→2sn. **Şube/Kullanıcı veri kaybı:** sunucu-otoriteli olduklarından her girişte aynalanıp siliniyorlardı → artık masaüstü çevrimiçiyken create/update/delete'i doğrudan SUNUCU API'sine yapar (`OrgServerClient`), kullanıcı yerele sunucu id'siyle işlenir (`ImportServerUser`); çevrimdışı → uyarı. |
| **Son 3 iş (2026-07-25)** | **1) Yetki ekranı:** admin/süper admin hedef → matris TAM işaretli + salt-okunur + bilgi (boş açılma sorunu bitti). **2) Kullanıcı:** liste herkese açık (Personel sınırlı, rol gizli), düzenleme admin; şifre tanımdan değişmez → **Şifre Sıfırla** (geçici=kullanıcı adı, ilk girişte kendi belirler). **3) Masaüstü oto-güncelleme:** oto açıkken eşitleme ekranında sessiz indir→Kur/Ertele (10 dk), onaysız kapatınca zorla kur, yarım kurulum self-heal (`AutoUpdateService`). Hepsi web+API canlı; masaüstü **1.0.89**'da. |
| **Yeni özellik** | **Durum Rapor + Rapor Excel dışa aktarma (2026-07-25)**: Yönetici raporları altına **Durum Rapor** — şube bazlı SAYISAL özet (Araç şablonlu/şablon-dışı; Personel/Bakım/Yakıt/Talep/Günlük toplamları; Malzeme firma-geneli tek satır çünkü şubesi yok), tarih filtreli. Ayrıca Raporlar + Yönetici Raporları ekranlarına **Excel'e Aktar** butonu — **iki ayrı özel yetki** (Rapor / Yönetici Rapor); yetki yoksa "yetkiniz yok" uyarısı (deny-by-default, UI+API). PG-güvenlik: tüm rapor sayımları `CAST(... AS INTEGER)`. Önceki: Yönetici raporları şablonlu/şablon-dışı + şablona bağlama (Migration054). |
| **Şema** | Migration **054** (materials.template_id). **Durum Rapor için yeni migration YOK** — mevcut kolonlar (branch_id, op_branch_id, template_id, created_at). |
| **API (sunucu)** | `depowise-erp.fly.dev` — **canlı** (PostgreSQL), health 200 · yeni: `/api/reports/{type}/export` + `status` rapor tipi |
| **Web** | `depowise-web.fly.dev` — **canlı** · yeni: Durum Rapor sekmesi + Excel'e Aktar (yetki kapılı) |
| **Masaüstü** | **1.0.91 YAYINDA** — 1.0.90'ın tümü + şifre sıfırlama/kullanıcı görünürlük (sunucu-tabanlı) + şube-bazlı veri filtreleme (Raporlar mevcut şube, Yönetici raporları tüm şubeler). Güncelleme: makine yalnız EN SON tam paketi indirir/kurar. |
| **Git** | temiz + `origin/master` ile senkron |
| **Bekleyen iş** | **Senkron çekirdeği ✓ · Düzenleme kilidi ✓ · 1.0.87 yayında ✓.** Sıradaki: giriş hız sınırı kararı (ortak ofis IP) · Giriş-Çıkış çoklu malzeme · makine bazlı güncelleme yetkisi · Yedek ekranları |

## 🔄 FORMAT SONRASI — BURADAN DEVAM ET (2026-07-22)

**PC formatlandı.** Kurulum: Git · **.NET 8 SDK** · flyctl · (VS Code/Claude Code) →
`git clone https://github.com/osmanalpaslan/DepoWise` → `flyctl auth login` → bana "devam" de.

### Sunucu durumu (ÖNEMLİ)
- Sunucu **fabrika ayarına sıfırlandı** (boş DB, eski veri yok). Firma/malzeme/araç **sıfırdan** kurulacak.
- **Süper admin giriş: `superadmin` / `DepoWise-2026`** → ilk girişte **şifreyi değiştir**.
- Fly secret'ları ayarlı: `DEPOWISE_SEED_ADMIN_PASSWORD` / `DEPOWISE_SEED_SUPERADMIN_PASSWORD` = `DepoWise-2026`
  (boş DB'de seed bu şifreyi kullanır; yoksa RASTGELE şifre üretip loga yazar — eski kafa karışıklığının sebebi buydu).

### Eşitlemede yapılanlar (canlı, masaüstü 1.0.85)
- **Z2** — push yanıtı (`upserted/skipped/errors`) okunuyor; `sync.log` + üst barda uyarı rozeti.
- **Z4** — delta kök neden: push artık **sunucu global max** yerine **makinenin kendi kalıcı watermark**'ını kullanır
  (`sync_push_watermark`) → başka kaydın zaman damgası yüzünden atlama imkânsız.
- **"Firma İş Verisini Sıfırla"** ekranı (web, süper admin): firma/şube/kullanıcı KALIR, yalnız iş verisi silinir.

### Senkron çekirdeği TAMAMLANDI (2026-07-22, masaüstü **1.0.86**)
1. **Z1 ✓** — `SyncGate` (tek SemaphoreSlim): 6 giriş noktası (giriş senkronu, tick, manuel Eşitle,
   Yereli Sıfırla, çıkış push'u, kapanış push'u) tek kapıdan. Reset↔tick yarışı bitti.
   Çıkış/kapanışta push atlanır ama **çıkış/kapanış daima yapılır**.
2. **Z3 ✓** — retry: sunucu bazı satırları uygulamazsa **watermark İLERLEMEZ** → sonraki turda otomatik
   yeniden denenir. 5 denemeden sonra **poison**: watermark ilerler (kuyruk kilitlenmez) + **kalıcı uyarı**.
   Sayaç/poison `SettingsService`'te kalıcı. Rozet artık sorun sürerken **kaybolmuyor**.
3. **Z5 ✓** — üst barda **daima görünür tıklanabilir rozet** ("✓ Senkron" / uyarı) → **Senkron Durumu** paneli:
   son başarılı push/pull zamanı, bekleyen/yeniden deneme, gönderilemeyen adet + sebep, `sync.log` yolu.

### QA yeniden aktif + eşitlemede gerçek hata bulundu (2026-07-22)
- **CLAUDE.md §7 (Ekran QA Motoru) yeniden yürürlükte** (senin isteğin). Yeni §7.0: QA israfa dönüşmesin —
  yalnız değiştirilen ekran, rapor dosyaya, yanıta kısa özet. Yeni §7.0.1: canlı testlerde **yalnız**
  `.env.test.local` içindeki test hesabı kullanılır (gerçek yönetici hesapları test edilmez).
- **Bulunan hata (düzeltildi):** stok hareket defteri `updated_at` taşımadığı için delta filtresine hiç
  girmiyordu → (a) her eşitlemede TÜM defter aktarılıyordu, (b) yeni hareket firma sürümünü yükseltmediği
  için **karşı makine çekmiyordu**. Damga artık `updated_at` yoksa `created_at`. Canlı: delta 663 → **0 satır**.
  Makine başına tek seferlik tam gönderim (`WatermarkEpoch`) ile eski watermark tuzağı da kapatıldı.
- **Testler 563/563**, canlı QA **7/7**. API canlıya alındı. Rapor: `docs/tests/Esitleme_Test_Report.md`.
- Canlı QA'yi istediğin an tekrar koşabilirsin: `node tools/qa/live-sync-check.mjs`

### Düzenleme kilidi — TAMAM (2026-07-22, API+web canlıda; masaüstü paket bekliyor)
Aynı kaydı iki kişi/iki makine düzenlerse ikincisi birincisini **sessizce eziyordu** (`version` yazılıyor
ama kontrol edilmiyordu). Artık kaydederken kayıt arada değiştiyse **üzerine yazmaz**, sorar:
**"Kaydı yenile"** / **"Formda kal"** (yazdıkların kaybolmaz).
- Gerçek kilit DEĞİL, sürüm karşılaştırması — çünkü sunucu kilidi **çevrimdışı çalışmaz** ve program
  çökerse kayıt kilitli kalırdı. Sürüm kontrolü çevrimdışı dahil her zaman çalışır.
- **Kapsanan ekranlar: Malzemeler · Araçlar · Personel · Bakım Tanımları** (masaüstü + web + API).
- **Kapsam dışı (kasıtlı):** Günlük Faaliyet, Yakıt, Bakım *kayıtları* zaten düzenlenemiyor (ekle-only
  defter kayıtları: oluşturulur, iptal/silinir; alanları hiç güncellenmez) → üzerine yazılacak şey yok.
- Canlı kanıt: her üçü için eski sürümle kaydetme **409**, ilk verinin ezilmediği doğrulandı (test kayıtları silindi).

### Çok makineli simülasyon + ölçek testi (2026-07-22) — masaüstü **1.0.87 YAYINDA**
10 sanal makine/kullanıcı, 3 şube, bütün ekranlarda eş zamanlı gerçekçi kullanım (yerel sunucu, boş DB).
Rapor: `docs/tests/Cok_Makineli_Simulasyon_Raporu.md` · Araç: `tools/qa/multi-machine-sim.mjs`
- **Düzenleme kilidi kanıtlandı:** 10 makine aynı sürümü aynı anda yazdı → **tam 1 kazanan, 9 × 409**.
- Mükerrer kod, negatif stok, tenant sızıntısı: hepsi doğru engellendi. Son koşu: **545 istek, 0 mantık hatası**.
- **Bulunan hata (düzeltildi):** stokta olmayan miktarı çıkarınca **500 "beklenmeyen hata"** dönüyordu.
  Kural doğruydu ama `NegativeStockException`/`MeterBackwardException` tanınmıyordu → artık **400 + gerçek mesaj**.
- **Açık bulgu (senin kararın):** giriş sınırı **IP başına 30/5dk**. Tek ofis internetinin arkasındaki 30+
  kişi vardiya başında birlikte girerse tıkanır. 500 kullanıcı hedefinde mutlaka değişmeli.
- **Ölçek:** okuma ~6.000 istek/sn (200 eşzamanlıda p95 51 ms), yazma ~**500/sn**'de düzleşiyor (SQLite tek
  yazıcı). 500 kullanıcı ≈ 50–100 istek/sn → **ham hız sorun değil**; duvar SQLite tek-yazıcı + tek makine
  + snapshot sayfalamasının olmaması. Ölçümler geliştirme PC'sinde/küçük veriyle alındı.

### Bilinen açıklar / kurallar
- ⚠️ **Aynı veriyi İKİ makinede import etme!** Her import farklı ID üretir → makineler birbirine oturmaz
  (araç/tanım FK'leri kırılır). **Tek makinede import et, diğeri eşitlemeyle çeksin.**
- Ertelenen: `server_seq` (saat-bağımsız pull sırası), ledger `op_id` idempotency, yakıt/bakımın LWW'den çıkarılması,
  snapshot sayfalama, **makine bazlı güncelleme yetkisi** (istendi, başlanmadı — `/api/releases/latest` makineyi
  tanımıyor, küçük bir masaüstü değişikliği gerekir).
- Araç import başlıkları birebir olmalı: **`İç Kod`**, **`Durum`**, **`Şantiye / Şube`** (boşluklu).
- Windows **Smart App Control** kapatıldı (açıkken git push + Avalonia derlemesi engelleniyordu).

### 🛡️ Senkron güvenilirlik planı — GPT ile mutabık, mimari DONDURULDU (2026-07-19)
Kök sorun: aynı firma+şubede iki masaüstü birbirini "zaman zaman" göremiyor. Kök neden (kanıtlandı):
delta watermark = tüm tabloların TEK global `max(updated_at)` + `updated_at` her makinenin KENDİ saatiyle →
gönderici ve alıcı atlaması. Çekirdek adımlar: **Z1** tek sync motoru+mutex · **Z2** push sonucunu oku
(sessiz başarısızlık bitsin) · **Z3** reset=sunucudan tam yenile (hard-delete yok) · **Z4** delta kök neden
(gerçekten gönderilmemiş/eksik kayıtları taşı; full-push/since=0 YASAK) · **Z5** basit sync durumu.
- **Z2 TAMAM (1.0.85):** push yanıtı (`upserted/skipped/errors`) artık okunuyor; `sync.log`; üst barda uyarı
  rozeti + manuel "Eşitle" diyaloğunda atlanan kayıt detayı. Canlı kanıt: HTTP 200 ama `{skipped:1,errors:[...]}`
  dönen "sessiz başarısızlık" senaryosu artık GÖRÜNÜR.
- **Z4 TAMAM (1.0.85) — DELTA KÖK NEDEN:** push artık "since = SUNUCU global max" DEĞİL, her makinenin KENDİ
  **kalıcı watermark**'ını (`sync_push_watermark`, SettingsService) kullanıyor. Böylece başka bir tablonun/
  makinenin yüksek zaman damgası, bu makinenin kendi kaydını atlatamaz (94-araç bug'ının kökü). since=0 yalnız
  ilk kurulumda; sürekli full push/resend YOK; watermark yalnız BAŞARILI push'ta ilerler (başarısızda tekrar
  denenir). Dosyalar: `BusinessSyncPushService.cs` (watermark), `ShellViewModel.cs`/`LoginViewModel.cs` (çağrı).
  Kanıt: `BusinessSyncTests.Z4_...` testi — eski (since=globalmax) kaydı ATLIYOR, yeni (watermark) GÖNDERİYOR,
  tekrar göndermiyor. **İki-makine (SIKIB3U↔8KN8USG) 6-senaryo testi kullanıcı tarafından yayından sonra yapılacak.**
  Sıradaki çekirdek: Z1 (tek mutex) · Z3 (reset=tam yenile) · Z5 (durum paneli).

### 🔧 Eşitleme kök düzeltme (2026-07-19) — "araçlar sunucuya ulaşmıyordu"
**Belirti:** Büyük firmada (2508 malzeme) push zaman aşımına uğruyor; araçlar sunucuya HİÇ ulaşmıyordu
(canlı kontrol: sunucuda 2508 malzeme, 0 araç). **Kök neden:** Sunucuda `ApplyCore` upsert döngüsü
transaction'sızdı → her satır ayrı commit (fsync) → 2508+ kayıt dakikalarca sürüyor → 120s'de yarıda kesiliyor
(malzemeler yazıldı, araçlar yazılamadı). Delta-push da araçları atlıyordu (updated_at ≤ sunucu sürümü).
**Düzeltme:** (1) `ApplyCore` tek `BEGIN/COMMIT` içinde → 1 commit, hızlı, atomik (yarıda kalma imkânsız).
(2) Girişte TAM push geri geldi (uzlaştırma: sunucuda eksik satır varsa tamamlar; artık hızlı olduğu için
zaman aşımı yok). Rutin push (ShellViewModel timer) DELTA kalır.

**✅ DOĞRULANDI + ÇÖZÜLDÜ (2026-07-19):** Kök neden canlı kanıtlandı — SIKIB3U yerelinde **94 araç VARDI**
(veri kaybı YOK), sunucuda 0. Düzeltilmiş sunucuya araçlar tek tek gönderildiğinde `upserted:94, skipped:0,
errors:[]` → sunucu tarafı kusursuz; sorun eski transaction'sız apply'ın büyük push'u (2508 malzeme+94 araç)
120s'de yarıda kesmesiydi (malzemeler FK sırasında önce → yazıldı; araçlar sıraya gelmeden koptu). 94 araç
sunucuya yüklendi (canlı doğrulama: /api/vehicles = 94 görünür). **Kullanıcının sorusu (süper admin çok-firma
yereli tetikler mi?): HAYIR** — push `company_id`'ye göre süzülüyor, çapraz-firma sızıntısı yok. **Baba makinesi
(8KN8USG) + web:** ~15 sn'de otomatik çeker (veya "Eşitle"/yenile). Her iki makineyi 1.0.84'e güncelle → tekrarı önlenir.

### 8 maddelik masaüstü-öncelikli paket (2026-07-19, ADR-098) — 7/8 canlı
Arıza Açıklaması · Enter ile filtre · Fluent menü rengi · Yakıtı Alan (Migration052) · PDF logolar (talep formu
büyük + ekonomik) · araç sayfalama alta. **Kalan (1):** Giriş-Çıkış çoklu malzeme. **Yeni kural:**
`.claude/rules/platform-priority.md`. **Web eşitleme sorunu = ADR-097 ile aynı kök neden** (sunucu boş, makine A
push edince gelir — ayrı web hatası yok). Detay: `docs/DECISIONS.md` ADR-098.

### Çift-tık "hızlı düzenle" penceresi (2026-07-19, ADR-096)
Malzemeler + Araçlar'da kayda çift tıklayınca ayrı pencerede Düzelt/Kaydet/Sil (tek tık detay panelini korur).
Web (MudDialog) + masaüstü (kod-arkası Window). Fotoğraf/muadil/uyumlu araç ve sayaç KORUNUR (hızlı pencere
silmez). Web canlı; masaüstü 1.0.73'te canlı. **⚠️ Görsel/uçtan-uca test kullanıcıya** (bu ortamda Avalonia +
web giriş formu test edilemedi). Detay: `docs/DECISIONS.md` ADR-096.

### Opus 4.8 gözden geçirmesi (2026-07-19, ADR-095)
Kullanıcı isteğiyle bu oturumdaki tüm iş (ADR-090…094) Opus 4.8 ile satır satır denetlendi (tenant/izin/
senkron/idempotency/web-masaüstü ayna). **Tek gerçek bulgu:** `EnsureExtraDefinition` atomik değildi →
eşzamanlı sunucu isteğinde çift gizli sabit tanım riski (masaüstü tek-kullanıcı, etkilenmez). Tek
`INSERT…SELECT WHERE NOT EXISTS` ile yarışsız yapıldı; API redeploy edildi (554/554). Diğer her şey TEMİZ.
Detay: `docs/DECISIONS.md` ADR-095.

### Günlük Faaliyet: "İlave Yağ / İlave Filtre / Tamir" (2026-07-19, ADR-091)
Bakım ile AYNI mekanizma (sayaç + malzeme stok düşümü dahil), yalnız Bakım Tanımı/Alt Bakım kullanıcıya
hiç sorulmaz — her tür firma başına otomatik oluşan sabit bir tanıma bağlanır. Web+masaüstü Kayıt Tipi
listesine eklendi. **Yan bulgu:** masaüstünde (ve sunucuda) servis başlatma sırası kusuru bulundu —
`DailyActivityService`, `Maintenance`/`MaintenanceDefs` atanmadan ÖNCE oluşturuluyordu (readonly alan kalıcı
`null` kalıyordu) → düzeltildi. Detay: `docs/DECISIONS.md` ADR-091. **Masaüstü 1.0.70'de canlı.**

### 🔴 KRİTİK: Senkron donma + sessiz başarısız push düzeltildi (2026-07-19, ADR-090)
Baba dosyasını içeri aldıktan sonra veri web'e ULAŞMAMIŞTI. Canlı sunucu doğrulandı: **OZE GRUP firmasında
0 malzeme, 0 araç** — push hiç başarılı olmamış. Kök neden: (1) senkron ağır işi (BuildSnapshot/ApplyPull)
Task.Run OLMADAN arayüz iş parçacığında çalışıyordu → "menüler arası donma" şikayetinin asıl sebebi budur
("sunucu kaynaklı" değil, istemci iş parçacığı bloklanması); (2) 30sn HttpClient zaman aşımı büyüyen veride
(2600+ kayıt) aşılıyor, `catch{}` bunu sessizce yutuyordu → veri SONSUZA KADAR sunucuya ulaşmıyordu, hata da
görünmüyordu. Düzeltme: ağır iş `Task.Run`'a alındı (arayüz artık donmaz) + zaman aşımı 120sn'e çıkarıldı +
"Eşitle" butonu artık başarısızlığı doğru gösteriyor. **Masaüstü 1.0.69'da canlı. Baba makinesini güncelleyip
"Eşitle"ye basması (veya normal girişi) gerekiyor** — geçmiş içe aktarılan veri o an push edilecek. Detay:
`docs/DECISIONS.md` ADR-090.

### 12 maddelik yeni istek listesi (2026-07-19) — sürüyor
Kullanıcı 12 madde verdi (Opus 4.8, "en son test edeceğim"). Durum:
- ✅ **Senkron donma/başarısız push** (yukarıda, ADR-090, KRİTİK+canlı).
- ✅ **Tanım adlarında fazla boşluk** normalize (Migration050 + Insert/Rename + import eşleştirme).
- ✅ **"Excel'e Aktar" butonu** Malzemeler+Araçlar'da (web+masaüstü) — aktif filtreyle TÜM sonuçları indirir.
- ✅ **Kural dosyası**: `.claude/rules/list-screens.md` (yeni filtrelenebilir alan + Excel export standardı).
- ✅ **Günlük Faaliyet'e 3 yeni tip** (İlave Yağ/İlave Filtre/Tamir) — ADR-091, masaüstü 1.0.70'de canlı.
- ✅ **Tanım Düzenle'de kilitli/sabit tanım** (ADR-092) + **form kutuları odaksız görünür + Semi arama
  kutusu Fluent ile aynı** (ADR-093) + **Günlük Faaliyet'e filtre+sayfalama+sıralama+Excel grid deseni**
  (ADR-094, madde 8/9 tamam) — masaüstü 1.0.72'de canlı. ⚠️ Görsel doğrulama kullanıcıda (bu ortamda
  Avalonia/giriş gerektiren web ekranları test edilemedi).
- ✅ **Çift-tık ayrı pencerede Düzelt/Kaydet/Sil** — Malzemeler + Araçlar (web+masaüstü, ADR-096, 1.0.73).
- ⏳ **Kalan (yalnız kullanıcı doğrulaması, geliştirme değil):** farklı makine aynı şube senkron doğrulaması
  (ADR-090 ile çözülmüş OLABİLİR, kullanıcı 1.0.69+ ile test etmeli) · ADR-096 çift-tık pencere görsel testi ·
  ADR-092/093/094 masaüstü görsel testleri.
Detay: `docs/YARIM_KALAN_ISLER.md`.

### 7 maddelik liste geliştirmeleri paketi (2026-07-18, ADR-089)
Kullanıcı 2600+ kayıtla çalışırken 7 istek verdi. **Web + backend TAMAM ve canlıda; masaüstü UI sürüyor.**
1. Sayfa boyutu varsayılan **25** (kişiye özel hatırlanır). 2. Sayfa numaraları + kayıt bilgisi tablonun
**üstünde-solunda**. 3. **Excel-benzeri grid**: pencere küçülünce taşma/kayma yok (yatay kaydırma) +
sürüklenebilir kolon genişliği (kişiye özel kalıcı). 4. **Tanım düzenleme** (rename artık definitions/Edit
yetkisiyle, süper-admin kısıtı kalktı; masaüstünde satır-içi düzenleme). 5. **Başlığa tıklayınca sıralama**
(metin A→Z/Z→A Türkçe; sayısal küçük→büyük). 6. Yeni tanım/rename **50 karakter** sınırı. 7. İçe aktarımda
**"Tür" harf duyarsız kanonik eşleme** ("YEDEK PARÇA"→"Yedek Parça") + Migration048 mevcut veriyi düzeltir.
Detay: `docs/DECISIONS.md` ADR-089. Test: 523/523. **Masaüstü — TÜMÜ 1.0.68'de canlı:** #1 (sayfa boyutu 25+
hatırlama), #4 (tanım düzenleme), #6 (50 kar), #7 (Tür), #2 (sayfalama üstte-sola taşındı), #5 (başlığa
tıklayınca sırala — yeni `SortHeader` + `IListGridViewModel`), #3 (Excel-benzeri: yatay kaydırma + sürüklenebilir
kolon genişliği, kişiye özel kalıcı). **⚠️ Görsel doğrulama yapılamadı** (Avalonia bu ortamda çalıştırılamıyor) —
yalnız temiz derleme ile güvence alındı; kullanıcının canlı ortamda gözden geçirmesi gerekiyor.

### Sayısal kolon filtresi: tam-sayı/karşılaştırma/aralık (2026-07-18, ADR-088)
Kullanıcı ADR-087'nin filtresini denerken: "stokta sadece 5 olanları listelemek istiyorum ama bütün içinde 5
olan malzemeler listeleniyor" — sayısal kolonda "içerir" araması 15/25/50'yi de yakalıyordu. **Çözüm:**
Malzemede Birim Fiyat/Min Stok/Stok, Araçta Üretim Yılı/Sayaç artık **sayısal** filtre — `5` artık TAM eşleşir
(içermez), `>5`/`<5`/`>=5`/`<=5` karşılaştırma, `5-10` aralık (negatif sınır destekli, bkz. ADR-086 negatif
stok). Tanınmayan söz dizimi eski "içerir" davranışına düşer (filtre kutusu asla sessizce boş kalmaz). Metin
kolonları (Kod/Ad/Marka…) DEĞİŞMEDİ. UI'da ipucu eklendi. Detay: `docs/DECISIONS.md` ADR-088. Test: 11 yeni
(509/509). **Canlıya alındı:** API+Web deploy, masaüstü **1.0.66** yayınlandı. Tarayıcı üzerinden görsel
doğrulama YAPILAMADI (giriş formuna kimlik bilgisi otomasyonu güvenlik politikasınca engellendi) — güvence
tamamen birim testlerinden (SearchGrid'e karşı gerçek SQL).

### Malzeme/Araç Listesi — kolon bazlı filtre + sayfalama + kişisel kolon seçimi (2026-07-17, ADR-087)
Kullanıcı 2600+ satırlık dosyayı içeri aldıktan sonra: "malzemeler ve araç listesinde filtre yapısı olması
gerek (içerir + başlangıca göre arama) + sayfa boyutu seçimi + 1,2,3… sayfalama." Netleştirme sorusunda
kullanıcı ekledi: sütun bazlı ayrı filtreler + sağ tık "Kolon Ayarla" ile hangi form alanının (fotoğraf
hariç) listede görüneceğini seçebilme, **her kullanıcıya özel** (farklı kullanıcıda görünmesin).

**Gizli kusur ortaya çıktı:** liste ekranları da (import/export'tan bağımsız) 200 satır varsayılanına
dayanıyordu — 2600+ kayıtlı firmada liste sessizce yalnız ilk 200'ü gösteriyordu. Yeni `SearchGrid` uçları
gerçek `COUNT(*)`+`LIMIT/OFFSET` kullanır; eski hızlı-arama uçları (Stok/Talep/Bakım seçicileri) dokunulmadı.

**Kolon kataloğu tek kaynak** (`MaterialListColumns`/`VehicleListColumns`) = yeni kayıt formundaki HER alan,
fotoğraf hariç ("Açılış Stok" ve "Şablon" da kasıtlı olarak yok — kalıcı kart alanı değiller). Kolon tercihi
KİŞİSEL (Migration 047, `user_list_preferences`, anahtar user_id+list_key — firma değil). Web + masaüstü
ikisinde de: filtre kutuları, sayfa boyutu seçici + numaralı sayfalama, sağ-tık/⚙ "Kolonları Ayarla".
Detay: `docs/DECISIONS.md` ADR-087. Test: 24 yeni (497/497).
**⚠️ Masaüstü UI görsel doğrulanamadı** (ortamda Avalonia çalıştırıp tıklama testi yapacak araç yok) —
temiz derleme + backend testleriyle güvence alındı. Web gerçek tarayıcıda uçtan uca doğrulandı.
**Canlıya alındı:** API+Web deploy, masaüstü **1.0.65** yayınlandı (sunucuda "en güncel" doğrulandı).

### Açılış stoğu NEGATİF olabilir (2026-07-17, ADR-086)
Babanın malzeme dosyasında (2507 satır) 63 satırda **Açılış Stok negatif**; içe aktarım reddediyordu.
Kullanıcı: "eksi stok kontrolünü kaldıralım; sistemi devralan firmalar mevcut stoklarını girebilsin."
→ **Yalnız BAŞLANGIÇ stoğu** girişinde negatif serbest bırakıldı (içe aktarım + web/masaüstü malzeme formu
+ API). **Operasyonel ÇIKIŞ'ın negatif-bakiye engeli AYNEN korunur** (bir çıkış bakiyeyi eksiye düşüremez —
§4'ün asıl kuralı). Fiyat/Min Stok yine negatif olamaz. Ledger temiz kalır: negatif açılış `stock_movements`'a
**pozitif miktar + direction=−1** yazılır (senkron kalkanı + `RecomputeBalances` doğru kalsın); yalnız türetilmiş
**bakiye** eksi olabilir. Detay: `docs/DECISIONS.md` ADR-086. Test: 6 yeni (473/473).
**⚠️ Kalan (babanın dosyası):** her satırda para birimi "TL" yazılı — sistem TRY/USD/EUR bekler. Bu ayrı bir
engel; Excel'de TL→TRY yapılmalı (istenirse TL→TRY otomatik eşlemesi eklenir). **Canlıya alındı:** API+Web
deploy, masaüstü **1.0.64** yayınlandı.

### Makine "tanım sıfırlama" (2026-07-17, ADR-085)
Kullanıcı: babasının makinesi (DESKTOP-SIKIB3U, süper admin makinesi) önce test firmasıyla giriş yapmıştı,
sonra asıl firmayla giremedi sandı → "makine tanımını sıfırlayan bir buton + login sonrası otomatik
algılama" istedi. **Yeni:** Makine Yönetimi ekranında (yalnız süper admin) **"Tanımı Sıfırla"** butonu —
o makine adına ait TÜM firmalardaki kayıtları siler (iş verisi ETKİLENMEZ, özel kod GEREKMEZ). Masaüstü
bir sonraki girişte (eşitleme adımında, purge/yerel-sıfırlama kontrollerinden ÖNCE) bunu görür → yerel
makine-firma/şube önbelleğini temizler → **girişi iptal eder, login ekranına döner**. Sonraki giriş yapan
kullanıcı makineyi kendi firması/şubesiyle yeniden tanımlar (mevcut "ilk kurulum" akışı). ADR-084'ten
(firma yerel sıfırlama) FARKI: o girişe izin verip devam eder, bu **durdurur** (makinenin hangi firmaya
ait olduğu artık belirsiz). Şema: Migration 046 (`machine_resets`, ADR-084 ile aynı iki-anlamlı desen ama
FİRMA yerine MAKİNE ADIYLA anahtarlı). Test: 8 yeni (`MachineResetTests`). Detay: `docs/DECISIONS.md` ADR-085. **Canlıya alındı:** API+Web deploy edildi, masaüstü **1.0.63**
yayınlandı (sunucuda "en güncel" doğrulandı). Gerçek makinede (DESKTOP-SIKIB3U) henüz test edilmedi.

### Personel içe aktarımı + "Saha Personeli" / "Kullanıcı Adı" sütunları (2026-07-16)
Kullanıcı sordu: "toplu personel listesini içeri almak istiyorum; saha personeli veya kullanıcı ise
sütunda nasıl belirtmem gerek?" → **Personel** içe/dışa aktarımı eklendi (7 sütun, formla birebir):
`Ad Soyad* · Unvan · Telefon · Şube · Aktif · Saha Personeli · Kullanıcı Adı`

**İki kavramın Excel karşılığı (BİRBİRİNİ DIŞLAR):**
- **Saha Personeli = Evet** → kişi uygulamaya HİÇ girmez (şoför/operatör). "Kullanıcı bağlanmadı" uyarısı çıkmaz.
- **Kullanıcı Adı** → kişi uygulamaya girer; **MEVCUT** hesap bağlanır. ⚠️ İçe aktarım **hesap AÇMAZ**
  (hesap açmak şifre+rol+yetki ister → Kullanıcılar ekranından yapılır). Bir personele TEK hesap.
- İkisi birden dolu → **çelişki, satır reddedilir** (ekranda da öyle: kutucuk işaretlenince kullanıcı bağı silinir).
- Evet/Hayır yazımı esnek: Evet/E/Var/X/1/true — Hayır/H/Yok/0/false. Tanınmayan değer **reddedilir**
  (sessizce "hayır" sayılmaz). Aktif boş = Evet, Saha Personeli boş = Hayır.

**Mükerrer:** personelin benzersiz kodu YOK → anahtar **normalize ad** (boşluksuz+küçük harf, mevcut
"mükerrer kişi" mantığıyla aynı). Aynı dosya iki kez → tekrarlanmaz. Bedeli: gerçekten aynı isimli iki
farklı kişi varsa ikincisi atlanır (rapor edilir). Unvan/şube yoksa otomatik oluşur (unvan Türkçe duyarlı:
"Şoför"="şoför" tek tanım).

**🔴 BULUNAN KUSUR (yine 200 sınırı):** Personel ve Malzeme **DIŞA aktarımı** `PageRequest{Limit=5000}`
kullanıyordu ama `MaxLimit=200` → **2600 personeli olan firma "dışa aktar" deyince sessizce yalnız 200
satır alıyordu.** Düzeltildi: `AllPages` yardımcısı keyset imleciyle tüm sayfaları dolaşıyor.
`PersonnelService.AllNameToId` (sayfalamasız) mükerrer kontrolü için eklendi. Test: 34 yeni (hacim 3000 dahil).

### ⚠️ İçe aktarma şablonları TAM ALAN + "Arızalı" durumu + 200 SATIR SINIRI KUSURU (2026-07-16)
**🔴 BULUNAN KUSUR (3000 satırlık hacim testi ortaya çıkardı — kullanıcının dosyası ~2600):**
`VehicleService.List` varsayılanı **200**, `PageRequest.MaxLimit` de **200**. İçe aktarıcılar bunlara
dayanıyordu → 200'den fazla aracı/malzemesi olan firmada: **bakım/muayene/yakıt aktarımı 201. araçtan
sonrasını "Araç bulunamadı" diye REDDEDİYOR**, araç/malzeme aktarımı mükerrer kontrolünü kaçırıp
**KOPYA oluşturuyordu**. Dün yayınlanan yakıt import'unda da vardı. Düzeltildi: import'lar
`List(s, null, int.MaxValue)` + yeni `MaterialService.AllCodeToId` (sayfalamasız) kullanıyor. 3 regresyon testi.

**Şablonlar artık YENİ KAYIT FORMUYLA BİREBİR** (fotoğraf hariç — kullanıcı kuralı):
Araç 4→**15** sütun · Malzeme 6→**15** · Bakım +Alt Bakım/Teknisyen · Muayene +Erteleme Tarihi/Açıklama.
Tanım alanları (marka/kategori/tip/model/şube/sürücü/birim/tedarikçi) **isimle yazılır, yoksa OTOMATİK
oluşur** (`ImportLookupResolver` — **önbellekli**: 3000 satırda satır başına DB sorgusu YOK). Aktarım sonrası
**"oluşturulan yeni tanımlar" raporu** verilir (yazım hatası "Caterpiller" ayrı marka olur → görülebilsin).
Araç artık **iç kod VEYA plaka** ile eşlenir (bakım/muayene/yakıt/uyumlu araçlar dahil).

**"Arızalı" durumu eklendi** (Aktif/Pasif/Bakımda/**Arızalı**) — ortak kaynak `VehicleStatus`
(Application + Web aynası); eskiden liste 5 yerde elle tekrarlıydı. **Yan kusur düzeltildi:** servis durum
notunu yalnız "maintenance"da saklıyordu → **Arızalı notu sessizce kayboluyordu**. Masaüstü durum kutusu
artık Türkçe gösteriyor (eskiden ham "active"/"passive" yazıyordu).
**Bakım ekranına "Araç Durumu"** eklendi (web+masaüstü): bakım kaydı açarken aracı Arızalı işaretleyebilirsin;
boş bırakılırsa araç durumu değişmez. Yeni uç: `POST /api/vehicles/{id}/status` (PUT tüm alanları ezerdi).

### ⚠️ Yakıt içe aktarımı + İMPORT'TA 10 KAT BOZULMA KUSURU DÜZELTİLDİ (2026-07-16)
**Bulunan KUSUR (kanıtlandı):** Malzeme içe aktarımı `Money.Parse` kullanıyordu; o InvariantCulture ile
çalışır ve **virgülü BİNLİK AYIRICI** sayar → Türk Excel'inin `"12,5"` değeri **sessizce 125** oluyordu
(fiyat/min-stok 10 kat şişiyordu, hata da vermiyordu). Düzeltildi: import kendi `ParseDecimal`'ını kullanıyor
(virgül→nokta). `Money.Parse` DEĞİŞTİRİLMEDİ — o veritabanı okuması için doğru (orada hep nokta saklanır).
**İkinci düzeltme:** Excel başlıkları artık büyük/küçük harf duyarsız ("litre" = "Litre") — elde tutulan
dosyalarda başlık farkı satırı sessizce reddediyordu.

**Yeni: Yakıt içe/dışa aktarımı** (İmport/Export ekranı, masaüstü). İki tür: **Yakıt Dağıtım** (araca yakıt
verme) + **Yakıt Depo Girişi** (satın alma). Gerçek dünya uyumu: yalnız **Araç + Litre zorunlu**; sayaç boş →
aracın mevcut sayacı (sayaç bozulmaz), fiyat boş → güncel depo fiyatı, personel/tarih boş → geçilir.
Araç **iç kod VEYA plaka** ile eşlenir (boşluk/harf duyarsız). Depo yetersizse **DryRun önceden uyarır**
(kaç litre eksik olduğunu söyler). Satırlar **tarihe göre** işlenir (sayaç zinciri doğru kurulsun).
**Aynı dosya iki kez aktarılırsa kayıt tekrarlanmaz** (deterministik operation_id). Test: 23 yeni.

### Firma "yerel sıfırlama" isteği (2026-07-16, ADR-084)
Sevgi A.Ş. bilgileri/adı web'den güncellendi; 2 yerel makine daha önce bu firmayla giriş yapmıştı.
**Teşhis:** firma ADI her çevrimiçi girişte zaten otomatik düzeliyordu; ama DİĞER alanlar (vergi/adres/
kota) hiç aynalanmıyordu → bu oturumda düzeltildi (`CompanySyncService.MirrorLocalAsync` artık TÜM alanları
aynalıyor). **Yeni özellik:** Firma Tanım listesinde "Yerel Sıfırlama İste" (turuncu ikon, süper-admin-only) —
firma sunucuda durur/erişim engellenmez, yalnız o firmanın makineleri bir sonraki çevrimiçi girişte yerel
kopyalarını BİR KEZ temizler ve sıfırdan yeniden doldurur. Makine o an kapalıysa istek sunucuda bekler,
makine aktif olunca (bugün/yarın fark etmez) algılanır. ADR-083'ten (kalıcı silme) farkı: YIKICI değil,
özel kod gerekmez, kendi firman için de kullanılabilir. Şema: Migration 045. Test: 7 yeni.

### Kullanıcı firması değiştirilemez — doğrulandı (2026-07-16)
Kullanıcı sordu: "kullanıcı oluşmuş ise süper admin dahil hiç kimse firmasını değiştirememeli — yapı böyle mi?"
Kod incelemesi: `users.company_id`'yi güncelleyen HİÇBİR UPDATE yok (7 UPDATE'te company_id yalnız WHERE
filtresinde), firma değiştiren API ucu yok, masaüstü senkronu `users` tablosuna hiç dokunmuyor. Tek istisna
(`AuthService.ImportRemoteUser`) firma DEĞİŞTİRMEZ — sunucudaki gerçeği yerele yansıtır. **Yapı doğru.**
6 yeni test (`UserCompanyImmutableTests`) bunu davranışsal olarak kilitler: şube atama/rol/aktif-pasif/
şifre/tüm-şubeler hiçbiri firmayı etkilemiyor + `UserService`'te "firma değiştir" imzalı metod yok.

### ⚠️ Kalıcı Silme ekranı (2026-07-16, ADR-083) — GERİ ALINAMAZ
**Ne işe yarar:** Firma Tanım firmayı *pasife alır*; bu yeni ekran firmayı ve TÜM verisini (kullanıcılar,
şubeler, malzeme, araç, stok, fotoğraflar, sunucu yedekleri) **kalıcı siler**. Temiz test ortamı içindir.

**Nasıl açılır:** Yönetim menüsü → **Kalıcı Silme** (yalnız web, yalnız süper admin). Ekran **özel kod** ile
açılır. Özel kod, süper adminin **ilk web girişinde** oluşturduğu, şifresinden AYRI bir sırdır; unutulursa
şifreyle yenisi belirlenir.

**Silme için gereken:** özel kod + şifre + firma adını birebir yazma. **Kendi firmanı silemezsin** (ADR-064/068
dersi: kilitlenirsin). Silinince geriye yalnız **künye** kalır; o firmanın makineleri bir sonraki girişte
eşitleme adımında künyeyi görüp **yerel veriyi siler ve login'e döner** → o firmayla artık girilemez.
Çevrimdışı makinede hiçbir şey silinmez (sunucu "silindi" demedikçe dokunulmaz).

**Masaüstünde:** yeni ekran YOK, login'de özel kod alanı YOK (kullanıcı kararı) — yalnız algılama var.

### Firma/şube karışmasını önleme — 3 faz (2026-07-16)
**Faz 1 — Şube ekranı:** firma kutusu "birden çok firma varsa" koşuluna bağlıydı + firma listesi hatası
sessizce yutuluyordu → süper adminde kutu HİÇ çıkmıyordu. Artık daima görünür, hata gösterilir ve
varsayılan **kendi firman** (alfabetik ilk firma değil). Masaüstü şube ekranına firma seçici eklendi (yoktu).

**Faz 2 — Aktif Firma (ADR: ekran-başı firma kutusu REDDEDİLDİ):** süper admin üst bardan firmayı değiştirir
(`/api/auth/select-company` → yeni jeton); tüm ekranlar o firmada çalışır, şube bağlamı sıfırlanır.
Gerekçe: CLAUDE.md §4 "firma kimliği yalnız güvenilir oturumdan gelir" — her ekrana firma kutusu koymak
bu kuralı deler ve riski 30 ekrana yayardı. Masaüstünde firma GİRİŞTE seçilir (yerel veri ona göre eşitlenir);
üst barda **aktif firma + çalışma şubesi rozeti** eklendi.

**Faz 3 — "Tüm Şubeler" koruması:** bu modda çalışma şubesi yoktur → stok hareketi şubesiz (`branch_id NULL`)
düşüyordu. Artık şube bazlı 7 ekranda (Malzemeler, Araçlar, Stok Giriş-Çıkış, Stok Sayım, Yakıt ×2, Bakım,
Muayene) **yazma engellenir**: uyarı penceresi çıkıp çıkış/giriş ile şube seçmesi istenir. **Okuma serbest.**
Ortak kod: `DepoWise.Web/Services/BranchGuard.cs` + `DepoWise.Desktop/BranchGuard.cs`. 4 yeni test.

### Kullanıcıda firma seçimi + Firma Tanım'da ilk şube (2026-07-16)
- **Kullanıcı Tanım:** firma seçme kutusu YALNIZ süper adminde; seçilen firmaya kullanıcı açılır.
  Firma değişince **şube listesi o firmaya göre yenilenir** (asıl kusur buydu: web'de kutu vardı ama
  şube listesi eski firmadan kalıyordu). Masaüstünde kutu hiç yoktu → eklendi (`FormBranches` ayrı liste).
  Personel bağlama yalnız KENDİ firmasında (personel listesi tenant'a kilitli) — başka firmada açıklama gösterilir.
- **Firma Tanım:** yeni firmada **"İlk Şube / Şantiye Adı" zorunlu**; firma ile birlikte o firmaya bağlı
  oluşturulur (şubesiz firmaya kullanıcı açılamıyordu). Düzenlemede alan gizli.
- 5 yeni tenant testi (`UserCompanySelectorTests`): başka firmaya kullanıcı · yabancı şube reddi ·
  admin'in firma seçememesi · şubesiz firma · firma+ilk şube akışı.

### QA alan doğrulamaları (2026-07-16)
Zorunlu: araç şantiye/şube + makul üretim yılı; yakıt/stok personel. Yumuşak uyarı (kullanıcı geçebilir):
plaka Türk biçimi (iş makinesi muaf), telefon biçimi, çok büyük sayı, muayene tarih mantığı. Sayaç kuralı
(düşük değer aracın KM'sini değiştirmez) zaten doğruydu. Web + masaüstü + API sınır katmanı; FieldChecks ortak.

### 17-maddelik istek — TAMAMLANDI (2026-07-15)
Tenant firma seçici · yetki ağacı tam gizleme · ilk-login şifre · bağlanacak kullanıcı (ad+şube) ·
seçili satır vurgusu · SignalR foto takılma düzeltmesi · araç foto silme (düzenleme modu) · tanım
tekilleştirme (dedup) + duplicate uyarısı + spinner · alt kategori aktif+bağlı+"+" · şablon fotoğrafları +
malzeme şablonu uyumlu araçlar · düzenlemeye giriş onayı · **temiz test ortamı** (sunucu+yerel sıfırlandı,
süper admin korundu).

### Bu oturumda (2026-07-15) tamamlananlar (17-maddelik istekten)
- **Tenant:** Şube ekranında firma seçici (süper admin tümü, diğerleri kendi firması); `/api/companies/options`.
- **Yetki ağacı:** yetkisiz/verilmeyecek kalemler kilit yerine TAMAMEN gizli; hedef-kullanıcı bazlı.
- **İlk giriş zorunlu şifre** (web+masaüstü Adım 4); Migration042.
- **"Bağlanacak kullanıcı"** yalnız Ad Soyad + şube.
- **Seçili satır** tema-uyumlu vurgu (CSS temeli).
- **KRİTİK:** Foto yüklerken ekran takılması → SignalR MaximumReceiveMessageSize 32KB→12MB.
- **Araç foto silme** yalnız düzenleme modunda.

### Bu oturumda yapılanlar (2026-07-14)

- **Makine Yedekleri ekranı** (süper admin): makine/firma/şube detayı + günlük yedekler + **aylık ZIP arşivi**.
  Masaüstü **her gün** yedek yükler; ay tamamlanınca günlükler tek ZIP'e alınır, hamlar silinir; arşivler
  **3 yıl** saklanır. **Disk koruması:** disk kritikleşirse en eski arşivler otomatik budanır (ADR-070 dersi).
- **Rol Yetki Kontrol ekranı** (süper admin): ekran × rol matrisi. Bir ekranı bir role kapatınca →
  yetki ağacında **görünmez**, grant **reddedilir**, verilmiş olsa bile **erişim kapanır** (Admin bypass'ı dahil).
  Süper admin muaf. Yapısal kilitler (süper-admin-only / admin-kısıtlı) değiştirilemez.
- **Kehribar menü teması:** web ve masaüstü üst bar + kenar menüye yarı şeffaf kehribar katman.
- Uygulama içi **logo boyutları** büyütüldü; masaüstü login "GİRİŞ YAP" yazısı ortalandı.

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

> **Aktif iş — PostgreSQL geçişi (Görev A):** Sunucu KODU artık uçtan uca PG-hazır ve 579 test yeşil.
> **Kod tarafında açık iş kalmadı.** Sıradaki tek şey **canlı geçiş** ve bu **senin onayınla** başlar
> (üretim + altın kural): Fly API'yi Neon bölgesinde çalıştır → babanın verisinin **KOPYASIYLA** prova →
> sağlamsa yeni makineleri yönlendir; eski SQLite sunucusu yedekte kalır. Hazır olduğunda "canlı geçişe
> başla" de. Ayrıntı ve nerede kaldık: [docs/GOREV_PANOSU.md](docs/GOREV_PANOSU.md).
>
> ---
>
> **(Geçmiş bağlam — masaüstü işleri)** Büyük yetki/ekran promptu (Adım 1–7) kod + test (313/313) + **CANLIYA ALINDI**
> (2026-07-13): API + Web deploy (health/login 200), masaüstü **1.0.48** yayınlandı (sunucuda "en güncel").
> Kullanıcı komutu olmadan yeni faza/işe kendiliğinden başlama (CLAUDE.md §1).
>
> **Bu turda yapılanlar (Adım 1–7):** Sync kaldırıldı · Talep→Form/Onaylama · Kısıtlı Süper Admin + delegasyon +
> Firma Yetki Kontrol 3-düzey · Firma Tanım ayrı admin/personel + makine kotası · Yetki Şablonu firma-kapsamlı ·
> Malzeme şablonu + şablon-dışı uyarı · Kullanıcı-şube zorunluluğu (admin dahil) · yeni login tasarımı (fotoğraf zemini).

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
