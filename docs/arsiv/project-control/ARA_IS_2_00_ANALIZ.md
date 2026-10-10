# ARA İŞ 2 — YENİ ARA İŞLER · 00 ANALİZ (2026-08-29) — ✅ KARARLAR VERİLDİ (aşağıda) · UYGULAMA ONAYI BEKLİYOR

> **KARAR KAYDI (kullanıcı, 2026-08-29 devam promptu):** PK-F1=A · PK-F2=EVET · PK-F3=yalnız Düzenle
> modu + Silme yetkisi · PK-F4=A · PK-F5=A · PK-T1=A · PK-T2=EVET · PK-T3=A · PK-T4=A · PK-V1=A ·
> PK-G1=A · PK-G2=A · PK-D1=A · **İş 6 ve İş 7 = PAKET-1 DIŞI, ayrı fazlar (kodlanmaz)**.
> Uygulama planı: [ARA_IS_2_01_PLAN.md](ARA_IS_2_01_PLAN.md).

> Kapsam: kullanıcının 2026-08-29 tarihli 8 maddelik yeni ara iş talimatı. **Yalnız salt-okuma analiz
> yapıldı; kod/migration/deploy YOK; production'a bağlanılmadı.** Her madde masaüstü + web + ortak
> katman AYRI AYRI incelendi (varsayım yok — tüm tespitler dosya:satır kanıtlı).
> Önceki rapor ara işi (ADR-181) KORUNUYOR: kod+test tamam, **yayın onayı bekliyor**, canlı şema 81.

---

## 0. ÖZET TABLO — risk · migration · senkron · paketleme önerisi

| İş | Konu | Risk | Migration | Senkron etkisi | Önerilen paket |
|---|---|---|---|---|---|
| 1 | Fotoğraf senkronu + silme kapısı | ORTA | **YOK** | YOK (sunucu-otoriteli desen) | PAKET-1 (bu yayın dalgası) |
| 2 | Yakıt raporu tarih/araç listesi | ORTA (sözleşme değişikliği + gerçek bug) | **YOK** | YOK | PAKET-1 |
| 3 | "Yakıtı Veren" son seçim | DÜŞÜK | **YOK** | YOK | PAKET-1 |
| 4 | Yakıt Günlük + Stok Hareketleri Günlük | DÜŞÜK | **YOK** | YOK | PAKET-1 |
| 5 | Günlük Faaliyet — Detay raporu | ORTA (yeni filtre türü — 6 dosyalık zorunlu zincir) | **YOK** | YOK | PAKET-1 |
| 6 | Custom Rapor Tasarımcısı | **YÜKSEK** | **GEREKİR** (yeni tablo) | GEREKEBİLİR | PAKET-2 (ayrı faz, ayrı onay) |
| 7 | Ekip + Hiyerarşi + Onay + Onaylamalarım | **EN YÜKSEK** | **GEREKİR** (2+ yeni tablo) | GEREKİR | PAKET-2 (ayrı faz, ayrı onay) |

**Paketleme gerekçesi:** Canlı şema 81'de kilitli (ADR-180). İş 1–5'in HİÇBİRİ migration istemiyor →
mevcut yayın bekleyen havuza (M + O + FIN düzeltmeleri + rapor ara işi) eklenip **tek migration'sız
yayın** yapılabilir. İş 6 ve 7 yeni tablo istiyor → şema 81 kilidini kırar; FIN-B1/Migration082 gibi
**ayrı karar + ayrı yayın penceresi** ister. Kullanıcı "bu işlerden sonra yayın istiyorum" dedi;
öneri: **PAKET-1 biter bitmez yayın, PAKET-2 sonraki dalga.**

---

## İŞ 1 — FOTOĞRAF SENKRONU + FOTOĞRAF SİLME KAPISI

### Mevcut durum
- **Saklama:** BLOB değil. Künye `file_records` tablosunda (Migration001; `entity_type`, `entity_id`,
  `kind='photo'`, `storage_key`, `mime`, `sha256`…), dosya diskte: masaüstü `%LOCALAPPDATA%\DepoWise\Files`,
  sunucu `<dataDir>/files` (`LocalFileStorageProvider`). Kaydetmeden önce optimize edilir (1600px, JPEG Q82),
  7 MB sınır + magic-byte doğrulaması. Malzeme ve araç **AYNI altyapıyı** kullanır (tek `FileService`,
  yalnız `entity_type` farklı).
- **MASAÜSTÜ ekleme:** tamamen YEREL. Kaydet anında `DesktopServices.Files.SavePhoto` → yerel SQLite
  `file_records` + yerel disk (`MaterialsViewModel.cs:933-944`, `VehiclesViewModel.cs:794-805`).
  Masaüstünde `/photos` API'sini çağıran TEK satır yok.
- **WEB ekleme:** API'ye multipart yükleme → SUNUCU diski + sunucu `file_records`
  (`Materials.razor:843`, `ApiClient.UploadFilesAsync`).
- **SENKRON:** `file_records` **`BusinessSyncService.Tables` listesinde YOK** (bilinçli:
  `BusinessDataExtras.cs:35` "senkron listesinde YOKTUR"); ikili (binary) içerik için hiçbir yan kanal yok.
- **API servis:** GET liste + GET bayt + POST yükleme + DELETE uçları malzeme (`Program.cs:959-984`) ve
  araç (`:3500-3517`) için **zaten mevcut ve çalışıyor** — masaüstü hiç çağırmıyor.

### KÖK NEDEN (kullanıcının gözlemi)
Üç ayrı fotoğraf silosu var: A makinesi diski · B makinesi diski · sunucu diski. Masaüstünde eklenen
fotoğraf yalnız o makinede kalır; başka makine ve web onu HİÇBİR ZAMAN göremez. Bu bir hata değil,
eksik bağlantı: **belgeler (Evrak) için sunucu-otoriteli desen zaten kurulmuş** (`DocumentService.cs:28-31`
kararı + masaüstü `OrgServerClient.UploadDocumentAsync/DownloadDocumentAsync` multipart istemcisi) ama
fotoğraflar bu karardan ÖNCE yazıldığı için yerel yolda bırakılmış.

### Beklenmedik ek bulgular
1. **WEB'de kayıtlı fotoğraflar hiç GÖSTERİLMİYOR:** `_detailPhotos` yükleniyor ama hiçbir markup'ta
   kullanılmıyor; `DeletePhoto` çağrılmıyor (ölü kod — Materials.razor:692-714, Vehicles.razor:653-674).
   Web'den yüklenen fotoğraf da web'de görünmüyor.
2. **MASAÜSTÜ silme düğmesi tam ters kapıda:** detay paneli yalnız form KAPALIYKEN görünür → silme
   bugün YALNIZ görüntüleme modunda mümkün (kullanıcının istediğinin tersi). Ayrıca yetki uyumsuzluğu:
   düğme `CanEdit` ile gösteriliyor ama servis `Delete` yetkisi istiyor (`FileService.cs:114`) —
   Edit'i olup Delete'i olmayan kullanıcı düğmeyi görüp hata alıyor (VM'de kullanılmayan doğru
   `CanDelete` özelliği zaten var).

### Önerilen dar çözüm (yeni sistem YOK — mevcut desene bağlama)
- Masaüstü fotoğraf ekleme/listeleme/silmeyi belgelerdeki gibi **sunucu-otoriteli** yap: mevcut
  `/photos` uçları + `OrgServerClient` multipart deseni. Çevrimdışıda belgelerdeki gibi açık uyarı.
- Web'de görüntüleme + silme UI'sini bağla (ölü kod diriltilir; şablonlardaki çalışan desen kopyalanır).
- Silme kapısı her iki platformda: **yalnız Düzenle modunda + Delete yetkisiyle** görünür/çalışır;
  servis kapısı (Delete) aynen kalır, tenant/BranchAccess dokunulmaz.
- `file_records` senkrona EKLENMEZ (bayt taşınamaz → kırık küçük resim üretir; ajan analizi bunu kanıtladı).

### KARAR NOKTALARI
- **PK-F1 — Mimari:** (A) **ÖNERİLEN:** sunucu-otoriteli (belgeler deseni; migration YOK, senkron
  sözleşmesine dokunulmaz, uçlar hazır) · (B) `file_records`+binary'yi senkron paketine ekle (ağır,
  paket şişer, sözleşme değişir — ÖNERİLMEZ) · (C) dokunma.
- **PK-F2 — Web görüntüleme/silme UI eksiği bu işte tamamlansın mı?** ÖNERİLEN: EVET (aynı işin parçası).
- **PK-F3 — Silme kapısı:** ÖNERİLEN: iki platformda da yalnız Düzenle modu + Delete yetkisi
  (UI `CanDelete`'e bağlanır; servis değişmez).
- **PK-F4 — Masaüstü çevrimdışıyken fotoğraf:** (A) **ÖNERİLEN:** belgelerdeki gibi çevrimiçi-gerektirir
  (net uyarı; kayıt kaydedilir, fotoğraf çevrimiçiyken eklenir) · (B) yerel kuyruk + sonradan yükleme
  (karmaşık, yeni senkron benzeri mekanizma — bu turda ÖNERİLMEZ).
- **PK-F5 — Mevcut yerel fotoğraflar:** masaüstü sunucudan okumaya geçince ESKİ yerel fotoğraflar
  görünmez olur. (A) **ÖNERİLEN:** masaüstü açılışta/kayıt açılınca yerel fotoğrafı bir defalık sunucuya
  yükler (fırsatçı taşıma; ekleme niteliğinde, canlı veri silinmez/değişmez; sha256 ile mükerrer önlenir)
  · (B) eski fotoğraflar yalnız o makinede "yerel" etiketiyle gösterilmeye devam eder (karma görünüm) ·
  (C) taşınmaz, kabul edilir kayıp. NOT: (A) canlıya İLK kez fotoğraf verisi YAZAR (kayıt değil, dosya
  ekler) — yayın onayının parçası olarak açıkça onaylanmalı.

**Veri değişikliği:** canlı kayıt değişmez; PK-F5=A seçilirse sunucuya YENİ dosya+künye eklenir (ekleme).
**Migration:** YOK. **Senkron:** YOK. **Yetki:** mevcut kapılar aynen (Edit=ekleme, Delete=silme).
**Canlı risk:** ORTA-DÜŞÜK (okuma yolu değişir; belge deseni kanıtlı).
**Test planı:** masaüstü ekle→web'de gör, web ekle→masaüstünde gör (iki makine simülasyonu API üzerinden);
silme yalnız düzenle modunda; Edit-var-Delete-yok kullanıcı düğmeyi hiç görmez; tenant (B firması
fotoğrafına erişemez — mevcut sweep testi var); çevrimdışı davranış; 7MB/mime sınırları; şablon
fotoğrafları regresyonu.

---

## İŞ 2 — YAKIT TÜKETİM RAPORU TARİH/GÜN DAVRANIŞI

### Mevcut durum (varsayım değil, SQL kanıtlı)
- Rapor `FROM vehicles v` ile başlar; tarih filtresi YALNIZ LEFT JOIN'lenen yakıt toplamının içindedir
  (`ReportService.cs:449-471`). Dış WHERE'de yakıt-varlığı şartı YOK → **kapsamdaki HER araç HER tarih
  aralığında listelenir** (verisi olmayan "-" satırıyla). Bu bilinçli "tam filo" tasarımı ve
  `FuelConsumptionTests.cs:124-131` ile kilitli. 1 Ağustos ve 2 Ağustos'ta aynı araçların gelmesinin
  birinci açıklaması BU: liste hep aynıdır, yalnız rakamlar değişir.
- Tarih filtresi kendisi DOĞRU (test kanıtlı: aralık dışı fiş toplama girmiyor). Rapor okuma yolunda
  masaüstü/web tarih paritesi birebir (ReportDateRange ↔ FieldChecks, testli).

### 🐞 GERÇEK HATA (ikinci açıklama): masaüstü YAZIM yolunda saat dilimi
- Web yakıt girişi tarihi **UTC gece yarısı** olarak yazar (`Fuel.razor:357-359` — doğru).
- Masaüstü `new DateTimeOffset(DateTime.Today)` + `ToUnixTimeMilliseconds()` kullanır
  (`FuelViewModel.cs:58,258`) → yerel +03:00 taşınır → kullanıcının seçtiği **2 Ağustos, UTC'de
  1 Ağustos 21:00** olur. Sonuç: masaüstünden girilen fiş tüm tarih-filtreli raporlarda (fuel ·
  vehicle · vehicle-daily · fuel-depot) **bir gün erken** görünür. Aynı hata depo dolum tarihi
  `_depotDate`'te de var (`FuelViewModel.cs:60`).
- Bu, kullanıcının "1→1 ve 2→2 aynı geliyor" gözlemini iki bulgunun bileşimi olarak tam açıklar.

### Önerilen dar çözüm
1. **PK-T2 (bug düzeltme, ÖNERİLEN=EVET):** masaüstü yakıt yazım yolunda tarih dönüşümünü
   `ReportDateRange.ToMs` semantiğine (UTC gece yarısı) çek — yalnız FuelViewModel'ın 2 tarih alanı;
   web zaten doğru; ortak yardımcı kullanılır, davranış web ile birebir eşitlenir.
2. **PK-T1 (sözleşme değişikliği — kullanıcı talebi):** "Yakıt Tüketim" raporu SEÇİLEN ARALIKTA VERİSİ
   OLMAYAN aracı LİSTELEMESİN. Bu "tam filo" sözleşmesini yalnız `fuel` raporu için değiştirir;
   `vehicle`, `vehicle-daily`, `acc-cash` AYNEN tam-filo kalır (kullanıcı talimatı: başka raporlara
   sıçratma). `FuelConsumptionTests` tam-filo kilidi bilinçli olarak yeni sözleşmeye güncellenir
   (gevşetme değil, yeni kuralın kanıtı — raporda açıkça belirtilecek).
   Seçenekler: (A) **ÖNERİLEN:** yalnız verisi olan araçlar listelenir · (B) tam filo kalsın +
   "verisi olmayanları gizle" onay kutusu (iki davranış birden — daha çok yüzey) · (C) dokunma.
3. **PK-T3 — canlıdaki mevcut kayıtlar:** masaüstünden bugüne dek girilmiş fişlerin
   `distribution_date` değerleri yerel-kaymalı duruyor. (A) **ÖNERİLEN (bu tur):** yalnız ileriye dönük
   düzeltme; mevcut kayıtlara DOKUNULMAZ (canlı veri koruma protokolü — değer değiştirmek yasak) ·
   (B) ayrı, ayrıca onaylı tek seferlik veri düzeltme işi (pg_dump + kapsam listesi + geri alma planıyla;
   İLERİDE ayrı karar). Not: (A) seçilirse eski kayıtlar raporlarda bir gün erken görünmeye devam eder —
   bu bilinçli kabul edilmiş olur.
4. **PK-T4 — aynı hata sınıfı taraması:** masaüstünde diğer tarih-giriş ekranlarında (bakım, günlük
   faaliyet, sayım, fatura…) aynı yerel-ofset dönüşümü var mı hedefli tarama yapılsın mı?
   (A) **ÖNERİLEN:** evet, SALT tarama + rapor (bulunanlar ayrı karara sunulur; kendiliğinden düzeltme
   yapılmaz) · (B) hayır, yalnız yakıt.

**Veri değişikliği:** PK-T3=A ise sıfır. **Migration:** YOK. **Senkron:** YOK (alan formatı aynı, yalnız
değer üretimi düzelir). **Yetki:** değişmez. **Canlı risk:** DÜŞÜK (rapor + tek VM dönüşümü).
**Test planı (kullanıcının istediği deterministik senaryolar):** izole veri ile yalnız-1-Ağustos,
yalnız-2-Ağustos, iki günde farklı kayıtlar, gün sınırı 00:00:00.000 ve 23:59:59.999 fişleri, gece
yarısı fişi; masaüstü-yazım→rapor-okuma uçtan uca gün tutarlılığı (yeni kilit testi); verisi olmayan
araç listelenmiyor; verisi olan araç tam görünüyor; `vehicle`/`vehicle-daily` tam-filo REGRESYONU
(değişmediklerinin kanıtı); iki lehçe (SQLite+izole PG).

---

## İŞ 3 — "YAKITI VEREN" SON SEÇİMİ HATIRLANSIN

### Mevcut durum
- Masaüstü: `DistPersonnel` her formda boş başlar; tek varsayılan birim fiyat (`FuelViewModel.cs:231`).
- Web: `_person` boş başlar; aynı şekilde tek varsayılan fiyat (`Fuel.razor:212`).
- Kayıt `fuel_distributions.personnel_id` (veren) / `recipient_personnel_id` (alan) olarak yazılır.
- **Migration'sız hazır altyapı VAR:** (1) `user_list_preferences` — kullanıcı başına anahtar/değer;
  yeni anahtar = yeni satır, şema değişmez (Migration047 belgesinde açıkça yazıyor); masaüstü YEREL
  SQLite'a, web API üzerinden SUNUCUYA yazar; Raporlar ekranı dahil 4+ ekran zaten kullanıyor.
  (2) `app_settings` + `anahtar:kullanıcıId` bileşik anahtarı — web teması emsali (`/api/me/theme`).
- İki tercih deposu da senkron listesinde YOK → hangi seçenek seçilirse seçilsin **senkron sözleşmesine
  dokunulmaz**.

### Önerilen dar çözüm
Kaydet başarılı olunca seçilen "veren" kimliği kullanıcı tercihi olarak yazılır; form açılışında okunur,
kişi listesinde HÂLÂ varsa ön-seçilir (silinmiş/pasif personel ön-seçilmez). **"Yakıtı Alan"a
UYGULANMAZ** (kullanıcı talimatı). Tercih kullanıcıya özeldir (user_id anahtarlı), başka kullanıcıya
taşamaz; tenant izolasyonu mevcut mekanizmalarla korunur.

### KARAR NOKTASI
- **PK-V1 — Saklama yeri:** (A) **ÖNERİLEN:** platform-yerel basit çözüm — masaüstü yerel
  `user_list_preferences`, web sunucu tarafı (mevcut `/api/me/list-prefs` ailesi veya tema deseni).
  Artı: en küçük değişiklik, çevrimdışı masaüstünde her zaman çalışır. Eksi: aynı kullanıcı farklı
  makinede farklı "son seçim" görebilir (kolaylık özelliği için kabul edilebilir).
  (B) her iki platformda sunucu-otoriteli (makineler arası aynı; masaüstü çevrimdışı ilk açılışta boş
  kalır, küçük API işi ek). Hata maliyeti düşük bir konfor özelliği olduğundan A öneriyorum.

**Migration:** YOK. **Senkron:** YOK. **Yetki:** değişmez. **Canlı risk:** ÇOK DÜŞÜK.
**Test planı:** kaydet→yeni formda ön-seçili; manuel değiştir→sonrakinde yeni kişi; alan alanı
etkilenmiyor; kullanıcı A'nın tercihi kullanıcı B'ye görünmüyor; silinmiş personel ön-seçilmiyor;
tenant; iki platform.

---

## İŞ 4 — GÜN BAZLI YENİ RAPORLAR (Yakıt Günlük + Stok Hareketleri Günlük)

### Mevcut durum
- `vehicle-daily` deseni hazır ve kanıtlı: `tarih_ms/86400000` gün anahtarı (iki lehçe birebir),
  sabit sorgu sayısı, bellekte birleştirme, günlük≡dönem tutarlılık testi, maxRows, TOPLAM satırı.
- Yakıt: `fuel_distributions.distribution_date` indeksli (`vehicle_id, distribution_date`) —
  gün-gruplu sorgu indeks dostu.
- Stok Hareketleri raporu ZATEN satır-satır detay (her satır bir hareket, tarih sıralı; transfer 2 satır);
  tarih kolonu `COALESCE(d.doc_date, sm.created_at)` (tek kaynak: `StockMovementFilterSql.IslemTarihiSql`).
  Hareket türleri TEK kaynak `MovementTypeOptions` (8 tür). "Günlük" biçimi burada kopya değil TASARIM
  sorusu: detay zaten günlü — katma değer GÜN BAZLI ÖZETTİR.
- Kategori yetkileri hazır: `fuel-daily` → `report_fuel`, `stock-movements-daily` → `report_stock`
  (ADR-181 çift kapısı otomatik uygulanır). Katalog sayacı testi 22→24 bilinçli güncellenir.

### Önerilen dar çözüm
- **`fuel-daily` ("Yakıt Tüketim — Günlük"):** vehicle-daily desenıyle; kolonlar: Tarih · İç Kod ·
  Plaka · Araç Adı · Şube · Sayaç Birimi · İşlem Sayısı · Günlük Mesafe · Litre · Ort. Tüketim ·
  Ort. Fiyat · Yakıt Maliyeti · Birim Başına Maliyet (oranlar GÜNÜN değerlerinden; TOPLAM dönemden;
  günlük≡dönem tutarlılık testi zorunlu). Mevcut `fuel` raporuna dokunulmaz.
- **`stock-movements-daily` ("Stok Hareketleri — Günlük"):** gün bazlı ÖZET önerilir (aşağıda PK-G2).
  Mevcut detay raporu aynen kalır. Filtreler mevcut `StockMovementFilterSql` üzerinden (ekran=rapor
  tek kaynak korunur); stok senkron sözleşmesine dokunulmaz (salt SELECT).

### KARAR NOKTALARI
- **PK-G1 — `fuel-daily` boş gün/araç davranışı:** (A) **ÖNERİLEN:** yalnız verisi olan (araç,gün)
  satırları listelenir (amaç hatalı günlük girişi görmek; tam-filo×tüm-günler görünümü zaten
  `vehicle-daily`'de var; İş 2'deki "verisi olmayan gizlensin" talebiyle de tutarlı) ·
  (B) vehicle-daily gibi tüm günler×tüm araçlar 0'lı.
- **PK-G2 — `stock-movements-daily` biçimi:** (A) **ÖNERİLEN:** gün × hareket türü özeti (satır=gün+tür;
  kolonlar: İşlem Sayısı · Giriş Miktar Toplamı · Çıkış Miktar Toplamı; TOPLAM satırı dönemden; mevcut
  Location/MovementType/Material/Search filtreleri aynen çalışır) · (B) gün × depo özeti ·
  (C) gün × tür × depo (satır sayısı büyür) · (D) yalnız gün toplamı (tür kırılımsız — bilgi kaybı).
  Miktarlar TEXT tutulduğundan toplama mevcut `SqlDialect.ExactSumText` yaklaşımıyla (kesin ondalık)
  yapılır; birim karışıklığına karşı miktar toplamları malzeme filtresi TEKİL malzemeyken anlamlıdır —
  (A)'da bu sınırlama InfoNote ile açıkça yazılır.

**Migration:** YOK. **Senkron:** YOK. **Yetki:** mevcut kategori anahtarları (yeni anahtar YOK).
**Canlı risk:** DÜŞÜK (yalnız yeni katalog satırları; mevcut raporlara sıfır dokunuş).
**Test planı:** her rapor için: katalog tanımı · gün bölmesi sınır testleri (00:00/23:59.999) ·
günlük≡dönem tutarlılığı · boş aralık · filtreler (şube/araç/tür · lokasyon/tür/malzeme/arama) ·
tenant · BranchAccess · kategori çift kapı (403) · sıralama · iki lehçe (izole PG) · Excel ·
mevcut `fuel`/`stock-movements` bit-bit regresyonu.

---

## İŞ 5 — "GÜNLÜK FAALİYET — DETAY" RAPORU

### Mevcut durum
- Katalogda Günlük Faaliyet raporu YOK (22 rapor). Veri: `daily_activities` tablosu;
  **"kayıt tipi" = `activity_type` TEXT + `movement_kind` TEXT ikilisi** (DB enum/lookup YOK):
  `maintenance`→Bakım · `extra_oil`→İlave Yağ · `extra_filter`→İlave Filtre · `repair`→Tamir ·
  `movement`+`movement`→Hareket · `movement`+`transfer`→Transfer (6 tip; "Depo Çıkışı" UI seçeneği
  faaliyet tipi DEĞİL, stok defterine gider). Etiket eşlemesi bugün 2 yerde tekrarlı
  (`TypeText` + `GridInnerSql CASE`) — rapor bunu ÜÇÜNCÜ kez kopyalamamalı: `MovementTypeOptions`
  deseninde paylaşımlı `DailyActivityTypeOptions` sınıfı açılır (kod, migration değil).
- Rapor ekranı filtreleri katalog bayraklarıyla tamamen jeneriktir; **sabit-listeli ÇOKLU SEÇİM deseni
  zaten var** (Hareket Türü / Durum filtreleri) → istenen "kayıt tipi çoklu seçimi" için birebir emsal.
  Yeni filtre eklemenin 6 dosyalık zorunlu zinciri `ReportFilterParityTests` ile makine-denetimli;
  `ReportRequest`'e alan **SONA** eklenir (konum bazlı kurulum — kritik kural).
- Şube kapsamı: faaliyet ekranı `op_branch_id` ile süzer; rapor tarafında diğer raporlarla tutarlı
  olarak `ReportScope.BranchSql(s, req, "da.op_branch_id")` kullanılmalı.

### Önerilen dar çözüm
Yeni katalog satırı `daily-activity` ("Günlük Faaliyet — Detay"): `RequiresDate=true` (tarih ZORUNLU —
kullanıcı şartı), `DataModule:"daily_activity"` (ekran kapalıysa rapor görünmez/çalışmaz — RPR-15),
filtreler: Date + Branch + Vehicle + **YENİ: ActivityType (çoklu seçim; hiç seçilmezse TÜM tipler)**.
Satırlar gün gün (tarih azalan → faaliyet ekranıyla aynı sıralama mantığı): Tarih · Kayıt Tipi · Araç ·
Nereden → Nereye · Operatör · Süre (gün) · Açıklama. Silinmiş kayıtlar hariç (`is_deleted=0` — ekranla
aynı). Yeni ekran/menü YOK; Excel mevcut mekanizmadan. Detay raporu olduğu için TOPLAM satırı yalnız
kayıt sayısı taşır.

### KARAR NOKTASI
- **PK-D1 — Kategori/yetki anahtarı:** (A) **ÖNERİLEN:** yeni kategori "Günlük Faaliyet" + yeni yetki
  anahtarı `report_daily_activity` (yetki ağacına diğer 8'in yanına eklenir; anahtar serbest-metin
  olduğundan MIGRATION YOK; kullanıcının kategori-bazlı yetki niyetine en uygun; PK-R3 gereği herkese
  kapalı başlar, yayın sonrası elle atanır) · (B) mevcut `ReportCategory.Vehicle` → `report_vehicle`
  altına koy (sıfır yeni anahtar; ama araç raporu yetkisi olan herkes faaliyet detayını da görür) ·
  (C) `Management` → `report_management`.

**Migration:** YOK. **Senkron:** YOK. **Yetki:** PK-D1'e göre (A'da yeni anahtar — migration'sız).
**Canlı risk:** ORTA-DÜŞÜK (6 dosyalık filtre zinciri disiplin ister; parite testleri güvence).
**Test planı:** tip çoklu seçimi (tek/çok/hiç=tümü) · tarih zorunlu · gün sınırları · tenant ·
BranchAccess (op_branch_id) · soft-delete hariç · `reports`+kategori çift kapı · DataModule kapalıyken
görünmez · filtre parite testleri (6 katman) · iki lehçe · Excel · katalog sayacı güncellemesi.

---

## İŞ 6 — CUSTOM RAPOR TASARIMCISI (fizibilite sonucu: AYRI FAZ)

### Fizibilite bulguları (özet — ayrıntı ajan raporunda dosya:satır ile mevcut)
- **Lehte:** `TableModel`'den aşağısı %100 jenerik (masaüstü grid · web tablo/özet · Excel · API) —
  dinamik üretilen rapor sıfır UI değişikliğiyle akar. Güvenli yapı taşları hazır: `GridQuery`
  (parametreli filtre üretici), `ListColumns` (Türkçe etiketli beyaz-liste kolon katalogları),
  `ExcelCenterService` ("HAM SQL YAZMAZ; veri her zaman sahibi servisten geçer" doktrini — 15 kaynaklı
  beyaz-liste kayıt defteri emsali), `SqlDialect.PortableSql`. Yetki tarafında `user_permissions.module_key`
  serbest metin → **rapor başına dinamik yetki anahtarı çalışma zamanında migration'sız çalışır**.
- **Engeller (hepsi sayılı):** `ReportCatalog.All` sabit dizi + `Run/Dispatch` kapalı switch → dinamik
  çözümleyici eklenmeli; yetki AĞACI statik katalogdan kurulur → dinamik girişler için ekleme noktası
  gerekir; `RoleGrantService`/`CompanyGrantService` katalog-dışı anahtarı sessizce düşürür (genişletilmeli);
  **tanımların saklanacağı yer = YENİ TABLO = MIGRATION** (şema 81 kilidini kırar). Masaüstü çevrimdışı
  çalışacaksa tanım tablosu senkrona girmeli (duyurular deseni: yalnız CREATE, FK yalnız
  companies/branches, eski istemciler bilinmeyen tabloyu SESSİZCE YOK SAYAR — kanıtlı, düşük risk).
- **Güvenlik tasarımı (kullanıcı şartıyla uyumlu):** serbest SQL YOK; kaynaklar = mevcut rapor/servis
  yöntemleri beyaz-listesi; kolonlar = `ListColumns` beyaz-listesi; filtreler = mevcut `ReportFilters`
  yapı taşları; birleştirme (join) v1'de YOK (en riskli yüzey). Tenant: tanım satırı `company_id`'li;
  çalıştırma her zaman sahibi servisin tenant/BranchAccess/soft-delete süzgecinden geçer.

### Önerilen fazlama + KARAR NOKTALARI
- **PK-C1 — v1 kapsamı:** (A) **ÖNERİLEN:** tek kaynak seç → kolonlarını seç/sırala → filtre + zorunluluk
  belirle → çalıştır → "Raporu Kaydet" → katalogda görün. Çapraz-kaynak birleştirme YOK (v2+ ayrı karar) ·
  (B) tam vizyon (çok kaynak + join) — güvenlik/performans yüzeyi büyük, ÖNERİLMEZ.
- **PK-C2 — Saklama/senkron:** (A) **ÖNERİLEN:** yeni `custom_report_defs` tablosu (yalnız CREATE,
  duyuru deseni) + senkron listesine ekleme → masaüstü çevrimdışı da çalıştırır ·
  (B) sunucu-otoriteli/yalnız-çevrimiçi (şablonlar deseni; masaüstü çevrimdışıda custom rapor yok) ·
  (C) makine-yerel (paylaşılamaz — kullanıcının "diğer kullanıcılara yetki verme" şartıyla çelişir, ÖNERİLMEZ).
- **PK-C3 — Zamanlama:** (A) **ÖNERİLEN:** PAKET-1 yayınından SONRA ayrı faz (migration kararıyla
  birlikte; FIN-B1/082 penceresiyle birleştirilebilir) · (B) şimdi (yayını migration'a bağlar, ÖNERİLMEZ).

**Migration:** GEREKİR (1 yeni tablo — PK-C2=A/B'de sunucuda; A'da iki lehçede). **Senkron:** PK-C2=A'da
tablo eklenir (eski istemci güvenli). **Yetki:** rapor başına dinamik anahtar + sahibine otomatik yetki +
`reports` üst kapı korunur; grant-yazma servislerinde genişletme. **Canlı risk:** YÜKSEK (bu yüzden ayrı faz).

---

## İŞ 7 — EKİP TANIMI + HİYERARŞİ + ONAY SİSTEMİ (fizibilite sonucu: AYRI FAZ, EN BÜYÜK İŞ)

### Mevcut durum (24 sorunun koddan cevabı — özet)
1-4. Approval altyapısı VAR ama TEK modülde ve TEK adımlı: Malzeme Talebi
  (`status: draft→pending→approved/rejected/cancelled`; `approver_id`=personel, `approved_by`=kullanıcı;
  ret gerekçesi ZORUNLU ve `request_status_history`'de; yetki = `request_approval` modülü + "belirlenen
  onaycı" kapısı). İş Emri ve Satın Alma'da onay katmanı BİLİNÇLİ OLARAK YOK (ADR kayıtlı).
5-6. Ekip/üst-ast ilişkisi ŞEMADA HİÇ YOK (`users` kolonları sayıldı; `is_manager`/`parent_user_id` yok;
  tek hiyerarşi `branches.parent_id`). → Ekip tanımı = YENİ TABLO(lar) = **MIGRATION ZORUNLU**.
7-9. Masaüstü çevrimdışı KISITI: firmanın kullanıcı LİSTESİ masaüstünde yerelde YOK (yalnız giriş yapan
  kullanıcı aynalanır; `users/user_permissions` senkronlanmaz). Çevrimdışı onay zinciri değerlendirmesi
  için ya ekip üyeliği `personnel` üzerinden kurulur (personnel senkronlu) ya da sunucu-otoriteli
  yapılandırma aynası (menü/görünürlük deseni) açılır. `material_requests` senkronludur; **SNK-05
  sözleşmesi bağlayıcı: onay LWW YASAK, sunucuya ulaşan İLK geçerli onay kazanır** — zincir bu kurala uymalı.
10-21. Zincir anlık görüntüsü (snapshot), döngü engeli, kendi kendini üst atama, hiyerarşi değişince açık
  onayların durumu, şirket-mi-şube-mi tabanı → hepsi AÇIK ürün kararı (aşağıda).
22. Red açıklaması görünürlüğü API'DA süzülmeli (UI'da gizlemek yetmez — kullanıcı şartı); bugün history
  ucu gerekçeyi yetkili herkese döndürür → yeni görünürlük kuralı API filtresi ister.
23-24. Bildirim tarafı HAZIR: bekleyen talepler bugün zaten Uyarılar'a türetilmiş kalem olarak düşüyor
  (`AlertKind.Request` + `alert_reads` + NavigateKey); "Onaylamalarım" için yeni bildirim altyapısı
  GEREKMEZ — mevcut mekanizmaya "sıradaki onaycı BEN miyim" süzgeci eklenir. Yeni ekran ekleme yolu
  standart (AppScreens + parite testleri; Duyurular emsali).

### Önerilen fazlama + KARAR NOKTALARI (kodlamadan ÖNCE cevaplanmalı)
- **PK-E1 — Onay zinciri HANGİ süreçlere uygulanacak?** (A) **ÖNERİLEN (v1):** yalnız Malzeme Talebi
  (tek mevcut onay akışı; İş Emri/Satın Alma'ya onay eklemek ayrı üründür ve "onay katmanı yok" ADR'lerini
  bozar) · (B) talep + iş emri · (C) talep + satın alma · (D) üçü birden.
- **PK-E2 — Hiyerarşi tabanı:** (A) **ÖNERİLEN:** kullanıcı (user) bazlı, firma kapsamlı, sunucu-otoriteli
  yapılandırma aynasıyla masaüstüne inen (menü deseni; LWW sorusu doğmaz) · (B) personel bazlı
  (senkronlu ama onaycı=kullanıcı eşlemesi dolaylılaşır).
- **PK-E3 — Zincir anlık görüntüsü:** (A) **ÖNERİLEN:** onay süreci BAŞLARKEN zincir dondurulur
  (snapshot; sonradan hiyerarşi değişse de açık süreç aynı zincirle biter — SNK-05 ilk-kazanır kuralıyla
  ve denetlenebilirlikle uyumlu) · (B) her adımda güncel hiyerarşiden hesaplanır (açık süreçler
  hiyerarşi değişince kayar — çakışma riski).
- **PK-E4 — "Sadece kullanıcı mı, altındaki herkes mi" seçeneği** UI'da bağlama başına seçilir
  (kullanıcı şartı — tasarımda ekip düğümüne "alt ağacı dahil et" bayrağı olarak modellenir).
- **PK-E5 — Red açıklaması görünürlüğü:** kural "yalnız en üst yöneticinin BİR ALTINDAKİ yönetici görür"
  — zincir 2 kişiyse (C→B, B en üst) kim görür? Öneri: açıklamayı yalnız zincir anlık görüntüsünde en
  üstün hemen altındaki onaycı görür; en üst dahil diğer herkes yalnız "RED" durumunu görür; süzme
  API'da. Uç durum tanımları (tek kademeli zincir, reddeden kişinin kendisi) karar paketinde netleşmeli.
- **PK-E6 — Zamanlama:** (A) **ÖNERİLEN:** PAKET-1 + (varsa) İş 6'dan SONRA, kendi analiz→karar→uygulama
  döngüsüyle 3 alt faza bölerek: E-a Ekip Tanımı ekranı+tablosu → E-b zincir motoru (talep onayına) →
  E-c Onaylamalarım ekranı + Uyarılar entegrasyonu + red görünürlüğü · (B) şimdi (ÖNERİLMEZ — en yüksek
  riskli iş; migration + senkron + güvenlik üçü birden).
- Zorunlu güvenlik tasarım maddeleri (karar değil, şart): döngü engeli (A→B→C→A reddi) DB+servis
  düzeyinde; kullanıcı kendini üst atayamaz; ekip düzenleme ayrı yetki anahtarıyla (deny-by-default);
  tenant: tüm tablolar `company_id`'li; mevcut tek-adımlı onay davranışı ekip TANIMLANMAMIŞSA aynen
  çalışmaya devam eder (geriye uyumluluk).

**Migration:** GEREKİR (en az: ekip/hiyerarşi tablosu + zincir adım tablosu). **Senkron:** GEREKİR
(PK-E2'ye göre ayna veya tablo). **Canlı risk:** EN YÜKSEK → ayrı faz + ayrı yayın onayı.

---

## SIRALAMA ÖNERİSİ (karar sonrası uygulama sırası)

1. **İŞ 2** (gerçek bug + sözleşme değişikliği — kullanıcının aktif şikâyeti)
2. **İŞ 3** (küçük, bağımsız)
3. **İŞ 4** (2 yeni rapor — İş 2'nin PK-G1 kararıyla tutarlı)
4. **İŞ 5** (yeni filtre türü — en disiplinli zincir)
5. **İŞ 1** (fotoğraf — PAKET-1'in en geniş dokunuşu, en sona)
6. → **PAKET-1 YAYIN ÖNCESİ RAPORU → kullanıcı "YAYINLA" onayı → yayın** (migration'sız; şema 81 kalır)
7. **İŞ 6** ve **İŞ 7** ayrı fazlar (her biri kendi karar paketi + migration onayı + yayın penceresi;
   FIN-B1/Migration082 ile aynı dalgada birleştirilebilir — ayrı karar).

Her adımda: yalnız ilgili testler + ilgili build; commit+push; korunan davranışlar (ADR-181 raporları ·
kategori yetkileri · `vehicle` SQL'i · Excel Merkezi · Barkod/QR · Dashboard · Global Arama ·
stok/senkron · SNK-13 · M import · tenant/BranchAccess/soft-delete) DOKUNULMAZ.

## YAYIN STRATEJİSİ (PAKET-1)

Yayın bekleyen havuz büyür: **M + O + FIN düzeltmeleri (082 HARİÇ) + rapor ara işi (ADR-181) + PAKET-1**.
Tek migration'sız deploy (API + Web + masaüstü paketi); canlı şema **81 KALIR**; geri dönüş = önceki
imaja dönüş (şema geri alma gerekmez). Yayın öncesi rapor kullanıcının 14 maddelik şablonuyla verilecek.
PK-F5=A seçilirse "masaüstü yerel fotoğrafları sunucuya taşır" davranışı yayın raporunda AYRICA
vurgulanır (canlıya dosya EKLEme — tek yazma yüzeyi).
