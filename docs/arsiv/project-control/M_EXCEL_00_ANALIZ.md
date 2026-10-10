# M — EXCEL MERKEZİ · ANALİZ RAPORU (M-00, kod yok)

> Tarih: **2026-08-28** · Durum: **ANALİZ — PK-M kararları bekleniyor** · Kod/migration/deploy: **YOK**
> Roadmap: FAZ 5 / SIRA 13 — tanım: *"Import/Export genişletmesi"* (MASTER_ROADMAP §1; başka bağlayıcı
> tanım belgesi YOK — kapsam bu analiz + PK-M kararlarıyla kesinleşir).
> Canlı: API v174 · Web v199 · Masaüstü 1.0.160 · şema 81. **Canlı veriye dokunulmadı** (salt kod okuma).

## 1. Mevcut Excel altyapısı (tam envanter)

### 1.1 Tek ortak motor — zaten merkezi
Tüm Excel üretimi/okuması TEK sınıftan geçer: `ExcelExportService` (Infrastructure/Reporting —
**ClosedXML 0.105**, yalnız Infrastructure.csproj'da). Üç yetenek:
- `Export(TableModel)` → .xlsx (başlık kalın, `NumCell` HAM sayı, `TotalRow` altta kalın, kolon sığdırma,
  sayfa adı temizleme ≤31). `TableModel(Title, Headers, Rows, TotalRow?)` evrensel "dışa aktarım para birimi"dir.
- `Template(title, headers)` → yalnız başlık satırlı boş şablon.
- `ReadRows(bytes)` → import satırları (1. satır başlık, **büyük/küçük harf duyarsız** eşleme, boş satır atlama).

İki platform da AYNI sınıfı kullanır: masaüstü doğrudan (`DesktopServices.Excel` + `FilePickerService.SaveExcelAsync`
→ dosya kaydet + aç), web API üzerinden (`Results.File(...)` → tarayıcı indirme, `ApiClient.GetFileAsync/PostFileAsync`).
**Excel üretim kodu tekrar EDİLMEMİŞ** — tekrar eden şey ekran başına ~20 satırlık buton işleyicisi + uç; bu,
ADR-087/088/089 deseninin bilinçli yapısıdır, sorun değildir.

### 1.2 Yetki modeli — zaten var, üçe ayrılmış (Migration056, 2026-07-26)
- **`export` modülü** (deny-by-default): tüm liste "Excel'e Aktar" butonları + uçları `Require(s,"export",View)`.
- **`import_export` modülü**: yalnız İÇE aktarım (+ masaüstü İmport/Export ekranı menü yetkisi; web ekran anahtarı `import`).
- **Raporlar**: özel butonlar `btn-export-reports` / `btn-export-mgr-reports` (rapor grubuna göre) + RPR-12/15
  (`RequiredModule`/`DataModule`) katalog süzmesi.
- **İkinci kapı — kaynak yetkisi:** merkez/uç hangi kaynağı okursa okusun, veri SERVİSTEN gelir ve her servis
  kendi modülünü `Require` eder (doğrulandı: `VehicleService` her uçta `Require(s, Module, ...)`). Yani
  "export yetkili ama araç yetkisiz" kullanıcı araç exportu ALAMAZ — Global Arama'daki "kaynak kendi
  yetkisiyle süzülür" ilkesinin eşdeğeri Excel tarafında BUGÜN de geçerlidir.

### 1.3 Dışa aktarım envanteri (ekran ekran)

**Web + API (filtreli TAM sonuç — liste kuralı 2):** Malzemeler (`/api/materials/grid/export`) ·
Araçlar (`vehicles/grid/export`) · Günlük Faaliyet (`daily/grid/export`) · İş Emirleri · Takvim · Duyurular ·
Satın Alma · Maliyet Merkezi (özet) · Zimmet · Ekipman (hepsi `/api/<modül>/export`) ·
Raporlar (**22 rapor**, `/api/reports/{type}/export`, ekranla AYNI gövde → filtreler otomatik yansır).
Hepsinde: `export` yetkisi + kaynak servis yetkisi + tenant + BranchAccess + `is_deleted` süzme (List/grid yolu).

**Masaüstü (yerel SQLite'tan, aynı ekran seti):** Malzemeler · Araçlar · Günlük Faaliyet · Raporlar ·
İş Emirleri · Satın Alma · Ekipman · Zimmet · Maliyet Merkezi · Takvim · Duyurular — kendi ekranlarında
"Excel'e Aktar". **+ merkezi İmport/Export ekranı** (yalnız masaüstünde) 8 kaynak dışa aktarır:
Malzemeler · Araçlar · Personel · Muayene/Sigorta · Bakım · Talepler · Yakıt Dağıtım · Yakıt Depo Girişi
(keyset `AllPages` ile sayfalama tavanı aşılır — 200 satır tuzağı çözülmüş; sütunlar import şablonuyla
BİREBİR aynı → "dışa aktar → düzelt → geri al" döngüsü çalışır).

### 1.4 İçe aktarım envanteri
İki platformda BİREBİR aynı **7 set** (aynı servisler — Material/Vehicle/Personnel/Maintenance/Inspection/
Fuel/FuelDepot ImportService): şablon indir → dosya seç → **ÖN KONTROL (dry-run, hiç yazmaz)** → onay →
aktar. Ortak kurallar: hedef şube ZORUNLU (kapsam dışı şube → 403, `ScopeResolver.EnsureBranchAllowed`;
ŞB-04 dersi: oturum kopyası şube kapsamını TAŞIR) · tanımlar isimle otomatik oluşur + oluşan liste raporlanır ·
satır bazlı hata raporu · web 20 MB dosya sınırı · masaüstünde aktarım biter bitmez **sunucuya push** +
sonuç kullanıcıya gösterilir (2026-07-19 dersi).
**Kritik güvenlik özelliği (doğrulandı, MaterialImportService):** commit MEVCUT kaydı ASLA değiştirmez —
aynı kod "zaten var → atla" (skip) sayılır. R17 (KNOWN_ISSUES): ekranda "Güncellenen" yazsa da gerçek
güncelleme YOKTUR; canlı veri açısından bu bir koruma, UX açısından yanıltıcı bir etikettir.

### 1.5 Ekran/menü mevcudu
`AppScreens`: tek ekran çifti — web `import` (rota `/import`, "Excel İçe Aktarım") + masaüstü
`import_export` ("İmport / Export"), ikisi de **Ayarlar** grubunda "Excel'e Aktarım" başlığıyla, modül
`import_export`. Web ekranında BİLİNÇLİ olarak dışa aktarım yok (yorum: "her liste ekranında zaten var").

## 2. M'in gerçek kapsamı — belge kanıtı

Roadmap'teki tek tanım **"Import/Export genişletmesi"**; V6 analiz 6.15 aynı yönde ("kayıt ekranlarında
şablon, ön doğrulama, hata raporu, toplu içe aktarma; raporlarda dışa aktarma" — bunların HEPSİ bugün var).
Ayrıca FAZ G parite listesi açıkça **"Personel/Muayene filtre+export"** eksiğini kaydeder. Sonuç: M yeni bir
sistem DEĞİL, mevcut kanıtlanmış import/export mimarisinin **eksiklerini kapatan eklemeli genişletmesi** +
tek "Excel Merkezi" ekran kimliğidir. Şablonlu-export/rapor-tasarımcısı/FTS benzeri yeni altyapı için hiçbir
belge dayanağı YOKTUR — önerilmez.

## 3. GERÇEK EKSİKLER (genişletmenin içeriği bunlardır)

| # | Eksik | Kanıt |
|---|---|---|
| E1 | **Web'de merkezi dışa aktarım YOK** → Personel · Bakım · Muayene/Sigorta · Yakıt Dağıtım · Yakıt Depo · Talepler webden Excel'e HİÇ aktarılamıyor (ekran butonları da yok; Requests'teki indirme PDF'tir) | grep: bu sayfalarda export yok; FAZ G "Personel/Muayene export" |
| E2 | Masaüstü merkez listesi 2026-07'de donmuş: yeni modüller (Ekipman/Zimmet/İE/Satın Alma/Takvim/Duyuru/Maliyet M.) merkezde YOK (kendi ekranlarında VAR — işlev kaybı değil, tek-pencere rahatlığı eksik) | ImportExportViewModel.ExportItems=8 |
| E3 | Ekran adı kimliği: menüde "Excel'e Aktarım"/"İmport / Export" — roadmap'in "Excel Merkezi" kimliği yok | AppScreens 282-286 |
| E4 | R17: import sonucundaki "Güncellenen" etiketi aslında "zaten vardı (atlandı)" (yakıtta düzeltilmiş, diğerlerinde yanıltıcı) | ImportExportViewModel 180-184 |
| E5 | İçe aktarılamayan modüller: ekipman, zimmet, cari, tanımlar, iş emri, takvim, duyuru, stok hareketi… (bilinçli — çoğu riskli) | ImportEntityKeys=7 |

## 4. Güvenlik analizi (veri sızdırma yan kapısı OLMAMASI)

Mevcut çift kapı AYNEN korunur ve yeni her uç için zorunludur:
1. `export` (veya import'ta `import_export`) modül yetkisi — uçta `Require`.
2. **Kaynak servis yetkisi** — veri her zaman kaynak modülün kendi `List/Grid` servisiyle çekilir; servis
   kendi `Require` + tenant (`company_id` session'dan) + `BranchAccess` + `is_deleted=0` süzmesini uygular.
   Merkez ekran HAM SQL YAZMAZ — Global Arama ilkesinin aynısı (bu, K'da kanıtlanmış desendir).
- **Duyurular (IsPublicRead):** View herkese açık → listede ne görünüyorsa export o; ek risk yok (mevcut
  `/api/announcements/export` zaten böyle çalışıyor).
- **Evrak (iki kapı) / Proje (şube yetkisi):** merkez export kapsamına v1'de ALINMAZ (öneri) — alınırsa da
  yalnız kendi servisleri üzerinden (metadata listesi; dosya içeriği asla).
- **Çöp Kutusu:** tüm List/grid yolları silinmişleri süzer → silinmiş kayıt Excel'e ÇIKMAZ (mevcut davranış;
  test planında kilitlenecek).
- Import tarafı: mevcut zorunlu-şube + kapsam kontrolü + dry-run + "mevcut kaydı değiştirmez" aynen korunur.

## 5. Offline / masaüstü

- Masaüstü merkez export YEREL SQLite'tan çalışır (çevrimdışı tam işlev) — mevcut davranış, değişmez.
- Sunucu-otoriteli kaynaklar (Proje, Evrak) masaüstünde yalnız çevrimiçi görülebilir → merkez export
  kapsamına alınmaları platform simetrisini bozar; **v1 dışı** (öneri). İleride gerekirse K'daki
  `OrgServerClient` deseniyle çevrimiçi-yalnız eklenebilir.
- ClosedXML masaüstünde (Avalonia/.NET 8) BUGÜN çalışıyor ve kanıtlı — kütüphane uyumluluk riski YOK.
- Web merkez export sunucudan üretilir (API) — offline mimariye dokunmaz.

## 6. Performans

- Üretim tamamen bellekte (ClosedXML workbook + `TableModel` satırları). Canlı ölçek: en büyük tablo
  materials 2492 · stock_movements 683 → mevcut desen fazlasıyla yeterli. `AllPages` 1M satır tavanlı.
- 22 rapor exportu ekranla aynı gövdeyi kullanır (Sorgula kuralı korunur; ağır rapor kendiliğinden koşmaz).
- **Streaming/kuyruk/job altyapısı GEREKSİZ** — ölçülmüş bir ihtiyaç yok; gelistirme-protokolu §8 gereği
  kurulmayacak. On binlerce satıra kadar bilinen desen yeterli; sorun görülürse ayrı iş açılır.

## 7. Veri modeli / migration

**MIGRATION GEREKMEZ (öneri).** Yalnız export/import genişletmesi için yeni tablo/ALTER gerekmiyor:
kaynaklar mevcut servislerden okunur, şablonlar koddan üretilir. Migration ancak "kullanıcı bazlı kolon/şablon
tercihi SAKLANSIN" kararı çıkarsa gerekir (1 tablo, CREATE-only) — **önerilmiyor** (PK-M4): bugünkü exportlar
zaten ekran filtre/kolonlarını yansıtıyor, saklama talebi yok, canlıya yayın da uzun süre yapılmayacak.

## 8. Import genişletmesi değerlendirmesi

Yeni import seti eklemek export eklemekten KAT KAT risklidir (canlı veriye YAZAR). Mevcut 7 set tanım/işlem
verisidir ve "mevcut kaydı değiştirmez" korumalıdır. Adaylar ve riskleri:
- Düşük risk: **Ekipman** (tanım benzeri; mevcut ImportService deseniyle bire bir), Cari/Tedarikçi (tanım).
- Yüksek risk / v1 dışı önerisi: stok hareketi (defter + idempotency), zimmet (hareket zinciri), iş emri
  (durum akışı), sayaçlı her şey. Bunlar iş kuralı atlamadan import edilemez → yapılmamalı.
Öneri (PK-M3): v1'de import seti **7 olarak SABİT kalsın**; canlı veri girişi sürerken içe-yazan yüzeyi
büyütmeyelim. E4 (yanıltıcı "Güncellenen" etiketi) ise yazısal düzeltmedir, güvenlidir.

## 9. Yetki kararı

**Yeni yetki AÇILMAZ (karar — soru değil):** mevcut `export` + `import_export` + rapor özel butonları modeli
bugünkü tüm akışları zaten ayrıştırıyor; `excel_center` gibi üçüncü bir yetki "aramada görünüyor ama
açamıyor" türü tutarsızlık üretir ve Migration/devir gerektirirdi. Ekran anahtarları (`import`/`import_export`)
ve modülleri DEĞİŞMEZ — yalnız görünen ad güncellenir (E3). Menü yeri **Ayarlar grubunda kalır** (taşımak
MenuSection/parite kilitlerini oynatır, kazanç yok).

## 10. UI planı (karar sonrası uygulanacak biçim)

- **Masaüstü:** mevcut İmport/Export ekranı adı "Excel Merkezi" olur; Dışa Aktar listesi PK-M2 kararına göre
  genişler; import akışı aynen (E4 etiket düzeltmesi).
- **Web:** `/import` sayfası "Excel Merkezi" olur; mevcut içe aktarım bölümünün YANINA masaüstündekiyle aynı
  mantıkta "Dışa Aktar" bölümü gelir (kaynak seç → indir; `export` yetkisi yoksa bölüm gizli). Yeni API ucu
  ailesi: `GET /api/export/{entity}` (eklemeli; mevcut hiçbir uca dokunulmaz).
- Dosya adları mevcut kalıpla Türkçe (`Personel.xlsx`…), tarihler ekranlardaki gibi `dd.MM.yyyy`, sayılar HAM
  (`NumCell`) — Türkçe Excel'de doğru açıldığı mevcut kullanımda kanıtlı.

## 11. Test planı (uygulama turunda `ExcelMerkeziTests`)

1) Tenant: A firması exportu B verisi İÇERMEZ. 2) Kaynak yetkisi: `export` var + kaynak modül yok → veri
YOK (403/hata; sessiz sızma yok). 3) `export` yetkisi yok → uç 403. 4) BranchAccess: şube kısıtlı kullanıcı
exportu yalnız kapsam satırları. 5) Silinmiş kayıt Excel'de YOK. 6) Türkçe karakter/boş liste/`NumCell` ham
sayı → `ReadRows` ile GERİ OKUYARAK doğrulama (açılabilirlik kanıtı). 7) Export→import döngüsü: şablon
sütun eşleşmesi. 8) Kaynak kayıtların bit-bit DEĞİŞMEZLİĞİ (export öncesi/sonrası snapshot). 9) Mevcut
export uçları regresyonu + Duyuru public-read exportu. 10) İmport regresyonu (7 set dry-run) + E4 etiketi.
11) Parite kilitleri (S13/S14) + menü bölüm testi. PG gerektiren testler yerelde atlanırsa sayı+nedenle raporlanır.

## 12. Mevcut sisteme etki matrisi

**Yalnız OKUNUR (davranış değişmez):** Malzeme · Stok · Araç · Personel · Ekipman · Zimmet · Bakım ·
Yakıt · Satın Alma · Cari · Tedarikçi · Maliyet Merkezi · İş Emri · Takvim · Bildirim · Duyuru · Proje ·
Evrak · Raporlar · Çöp Kutusu (süzme davranışı okunur) · Yetki sistemi (mevcut modüller kullanılır).
**Dokunulan dosyalar (eklemeli):** `Program.cs` (yeni `/api/export/{entity}` uçları) · `ImportExportViewModel`
(+kaynaklar, etiket) · `ImportExcel.razor` (+Dışa Aktar bölümü) · `AppScreens` (yalnız Title metni).
**Değişmeyen ortaklar:** `ExcelExportService` · import servisleri (E4 hariç etiket) · senkron (**hiç dokunulmaz**;
export salt-okunur olduğundan SNK-13 ETKİLENMEZ — dokunulmayacak mevcut known issue) · migration kataloğu.
**Regresyon riski:** düşük — tek gerçek risk noktası `ImportExportViewModel.BuildTable` genişletmesi; her yeni
kaynak kendi servis çağrısıyla izole `case`tir.

## 13. v1 sınırları

- **v1 kapsamı (öneri):** E1+E2 (PK-M2 seçimine göre) + E3 + E4. Migration YOK, yeni yetki YOK, senkron dokunuşu YOK.
- **Bilinçli kapsam dışı:** yeni import setleri (PK-M3 HAYIR ise) · Proje/Evrak merkez exportu · şablon/kolon
  tercihi saklama · zamanlanmış/otomatik export · PDF · şablonlu (biçimli) rapor tasarımı · CSV.
- **İleride eklemeli gelebilir (bugünkü tasarımı ETKİLEMEZ):** her yeni kaynak = 1 `case` + 1 uç; yeni import
  seti = mevcut ImportService deseni. Şema genişletmesi bugünden YAPILMAZ.
- **Yeniden yazım riski:** yok — hiçbir mevcut desen değiştirilmiyor, yalnız çoğaltılıyor.
- **Yayın:** M canlıya YAYINLANMAYACAK (yeni çalışma stratejisi) — build+test seviyesinde kalır; migration da
  olmadığından yayın bekleyen şema borcu OLUŞMAZ.

## 14. PK-M KARAR SORULARI

### PK-M1 — Excel Merkezi'nin biçimi
- **Mevcut:** masaüstünde merkezi İmport/Export ekranı (8 export + 7 import); webde yalnız içe aktarım sayfası.
- **ÖNERİLEN (A):** YENİ EKRAN AÇILMAZ — mevcut ekran çifti "Excel Merkezi"ne dönüşür ve web'e merkezi
  "Dışa Aktar" bölümü eklenir (masaüstüyle aynı kaynak listesi; yeni `GET /api/export/{entity}` uçları).
  Maliyet: düşük · güvenlik: mevcut çift kapı aynen · performans: mevcut desen · yeniden yazım: yok.
- **Alternatif (B):** merkez ekrana dokunma; yalnız eksik ekranlara (web Personel/Bakım/Muayene/Yakıt/Talepler)
  tek tek "Excel'e Aktar" butonu ekle. Maliyet: benzer; ama "Excel Merkezi" kimliği oluşmaz ve Yakıt Depo gibi
  ekransız kaynaklar açıkta kalır.

### PK-M2 — Merkezi dışa aktarım kaynak listesi (iki platformda AYNI liste)
- **ÖNERİLEN (A):** mevcut 8 + yeni modüller: **Ekipman · Zimmet · İş Emirleri · Satın Alma · Takvim ·
  Duyurular · Maliyet Merkezi (özet)** = 15 kaynak. Yeni modüllerin verisi zaten export'lu (kendi ekranları) —
  merkezden de erişim yalnız kolaylıktır; ek güvenlik yüzeyi açmaz (aynı servisler).
- **Alternatif (B):** yalnız mevcut 8 (en dar kapsam — E1 pariteyi kapatır, E2 açık kalır).
- **Alternatif (C):** A + Stok Hareketleri/Cari gibi bugün hiç export'u olmayan kaynaklar — daha fazla iş;
  stok hareketi zaten Raporlar'dan (22 rapor) alınabildiği için önerilmez.

### PK-M3 — İçe aktarım kapsamı
- **ÖNERİLEN (A):** v1'de import seti **AYNEN 7** kalır (canlı veri girişi sürerken içe-yazan yüzey
  büyütülmez); yalnız E4 etiket düzeltmesi yapılır ("Güncellenen" → "Zaten vardı (atlandı)", tüm setlerde).
- **Alternatif (B):** + Ekipman importu (düşük risk, mevcut desenle; dry-run+skip korumaları aynen).
- **Alternatif (C):** + Cari/Tedarikçi importu (tanım verisi; düşük-orta risk).
- Stok hareketi/zimmet/iş emri importu HER DURUMDA kapsam dışı (iş kuralı/idempotency atlanamaz).

### PK-M4 — Kolon/şablon tercihi saklama (= tek migration ihtimali)
- **ÖNERİLEN (A): HAYIR** — migration'sız; exportlar ekran filtre/kolonlarını zaten yansıtıyor.
- **Alternatif (B):** kullanıcı bazlı tercih tablosu (1 CREATE migration + UI) — yayın yapılmayacağı için
  şema borcu birikir; talep de yok. Önerilmez.

### PK-M5 — R17 "import mevcut kaydı güncellemez" davranışı
- **ÖNERİLEN (A):** DAVRANIŞ KORUNUR (skip-only) — canlı veri koruması olarak bu bir ÖZELLİKTİR; yalnız
  etiket düzeltilir (E4). Risk: yok.
- **Alternatif (B):** "eşleşen kaydı güncelle" seçeneği eklenir — canlı kayıtları toplu DEĞİŞTİREN bir yol
  açar; canlı veri koruma protokolüyle çelişir. Önerilmez (istenirse ayrı, korumalı bir iş olarak tasarlanmalı).

---
**Karar gerektirmeyen kararlar (raporlanır, sorulmaz):** yeni yetki YOK (§9) · menü Ayarlar'da kalır (§9) ·
Proje/Evrak merkez exportu v1 dışı (§5) · streaming/kuyruk YOK (§6) · SNK-13'e dokunulmaz (§12) ·
migration YOK (§7, PK-M4 A ise) · yayın YOK (§13).
