# EXL-01 — Excel Merkezi · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-176** · Roadmap: FAZ 5 / SIRA 13 (MASTER_ROADMAP §1)
> Analiz: [M_EXCEL_00_ANALIZ.md](M_EXCEL_00_ANALIZ.md) — PK-M1..M5 kullanıcı tarafından **A-A-A-A-A** olarak
> KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-M1=A | YENİ EKRAN AÇILMADI — mevcut ekran çifti (web `/import` + masaüstü İmport/Export) iki platformda **"Excel Merkezi"** oldu; web'e merkezi **Dışa Aktar** bölümü eklendi. Ekran anahtarları (`import`/`import_export`), rota ve menü konumu (Ayarlar) DEĞİŞMEDİ — yalnız görünen ad. |
| PK-M2=A | Merkezi dışa aktarım **15 kaynak** (iki platformda AYNI liste): mevcut 8 (Malzemeler · Araçlar · Personel · Muayene/Sigorta · Bakım · Talepler · Yakıt Dağıtım · Yakıt Depo Girişi) + **Ekipman · Zimmet · İş Emirleri · Satın Alma · Takvim (bu ay) · Duyurular · Maliyet Merkezi (son 30 gün)**. Tarih pencereli iki kaynağın penceresi etikette yazar ve kendi ekranlarının varsayılanıyla aynıdır. |
| PK-M3=A | İçe aktarım **AYNEN 7 set** — yeni import kaynağı EKLENMEDİ (ekipman/cari/stok hareketi/zimmet/iş emri importu bilinçli kapsam dışı). |
| PK-M4=A | Şablon/kolon tercihi SAKLANMAZ — **MIGRATION YOK, şema 81'de kaldı** (yeni tablo/ALTER/indeks yok). |
| PK-M5=A | **"zaten var → atla" kuralı korundu** — hiçbir import mevcut kaydı güncellemez (7 serviste de doğrulandı: hepsi skip-only; Bakım/Muayene her satırı yeni ekler, atlama 0 döner). Yalnız yanıltıcı **"Güncellenen" etiketi** iki platformda **"Zaten mevcut (atlandı)"** yapıldı (R17 düzeltmesi — davranış DEĞİŞMEDİ, yalnız yazı). |

## 2. Mimari — tek ortak üretici

- **YENİ: `ExcelCenterService`** (Infrastructure/Reporting) — `Sources` (15 kaynak: anahtar·etiket·dosya adı)
  + `Build(session, key)` → `TableModel`. Masaüstü ekranı, web sayfası ve API uçları AYNI sınıftan beslenir →
  **web/masaüstü paritesi yapısaldır** (liste/kolon ayrışamaz). Excel üretimi mevcut `ExcelExportService`
  (ClosedXML) ile — ikinci motor/kütüphane KURULMADI.
- İlk 8 kaynağın kolon mantığı eski `ImportExportViewModel.BuildTable`'dan AYNEN taşındı (satır satır aynı —
  ilk 8'in sütunları içe aktarım şablonuyla uyumlu: dışa aktar → düzelt → geri içe aktar döngüsü korunur).
  Yeni 7 kaynak, ekranlarının MEVCUT `ToTableModel`/`SummaryTable` üreticilerini çağırır (kolon tekrar YAZILMADI).
- **Güvenlik — çift kapı (ARA-01 ilkesinin aynısı):** (1) `export` modül yetkisi uçta/ekranda; (2) veri HEP
  kaynak modülün kendi servisiyle çekilir → kaynak `Require` + tenant + BranchAccess + `is_deleted` süzmesi
  SERVİSTE. `ExcelCenterService` **ham SQL yazmaz** — yetkisiz kaynak 403'e düşer, merkez yetki bypass'ı olamaz.
- **Yeni yetki AÇILMADI:** mevcut `export` (dışa) + `import_export` (içe) modeli aynen; rapor özel butonları aynen.

## 3. Değişen/yeni dosyalar

| Dosya | Değişiklik |
|---|---|
| `Infrastructure/Reporting/ExcelCenterService.cs` | **YENİ** — 15 kaynaklı ortak üretici |
| `Api/Program.cs` | **Eklemeli** 2 uç: `GET /api/export/entities` (kaynak listesi) + `GET /api/export/{entity}` (.xlsx indirme; `export` yetkisi + kaynak servis kapıları). Mevcut hiçbir uç/DTO değişmedi. |
| `Api/ServerServices.cs` · `Desktop/DesktopServices.cs` | `ExcelCenter` kaydı (aynı bağlama) |
| `Desktop/ViewModels/ImportExportViewModel.cs` | Export listesi/üretim ortak servise devredildi (BuildTable kaldırıldı — koda taşındı); import sonuç etiketi düzeltildi |
| `Desktop/Views/ImportExportView.axaml` · `ShellViewModel.cs` | Başlık "Excel Merkezi", "Kaynak" etiketi |
| `Web/Components/Pages/ImportExcel.razor` | Başlık "Excel Merkezi" + merkezi **Dışa Aktar** bölümü (`export` yetkisiyle görünür; kaynaklar `/api/export/entities`'ten) + etiket düzeltmesi. Rota `/import` AYNEN. |
| `Application/Security/AppScreens.cs` | Yalnız Title: "Excel'e Aktarım" → "Excel Merkezi" (anahtar/modül/grup aynı) |
| `Web/Components/Pages/Soon.razor` | Kozmetik etiket eşleme |
| `tests/DepoWise.Tests/ExcelMerkeziTests.cs` | **YENİ** — 10 test |

## 4. Testler

`ExcelMerkeziTests` **10/10**: 15 kaynaklı ortak liste + bilinmeyen anahtar 400 (EXL1) · boş veride 15
kaynağın tamamı üretilir (EXL2) · Türkçe karakter + dosyanın GERİ OKUNARAK açılabilirliği/kolon doğruluğu
(EXL3) · **kaynak modül yetkisi olmadan sızma yok — 6 kaynakta ForbiddenException (EXL4)** · **tenant
(EXL5)** · **BranchAccess şube kapsamı süzmesi (EXL6)** · silinmiş kayıt exportta yok (EXL7) · **export
salt-okunur: 15 kaynak üretilirken kaynak satırlar bit-bit değişmez (EXL8)** · **import mevcut kaydı asla
güncellemez — değiştirilmiş adla tekrar import bile satırı bit-bit aynı bırakır (EXL9)** · dry-run yazmaz
(EXL10). PostgreSQL gerektiren test yok (0 atlanan).
Hedefli regresyon (import ×5 sınıf + API import hattı + parite/menü/ekran ağacı + yetki/tavan):
**293 geçti / 0 başarısız / 0 atlanan**. Üç Release build (API+Web+Masaüstü) **0 hata**.

## 5. Canlı veri güvenliği

Canlıya yazma YOK · mevcut kayıt değişimi YOK (EXL8/EXL9 bit-bit) · fiziksel silme YOK ·
**MIGRATION YOK (şema 81)** · production deploy YOK · canlı DB'ye bağlanılmadı (tüm testler izole geçici
SQLite). Import'un "mevcut kaydı değiştirmeme" koruması artık testle KİLİTLİ.

## 6. Offline / platform davranışı

Masaüstü merkez export YEREL SQLite'tan çalışır (çevrimdışı tam işlev — 15 kaynağın hepsi yerel).
Web merkez export sunucudan üretilir. Sunucu-otoriteli Proje/Evrak merkez kapsamına ALINMADI (bilinçli —
analiz §5; masaüstü offline simetrisi bozulmasın). Liste ekranlarındaki filtreli "Excel'e Aktar" butonları
(ADR-087/088/089) AYNEN duruyor — merkez, filtresiz TAM listedir; ikisi farklı ihtiyaçtır.
SNK-13'e dokunulmadı (export salt-okunur; senkron değişmedi — etkilenmiyor).

## 7. Bilinen sınırlar / ileride eklemeli gelebilecekler

- Talepler kaynağı mevcut servis tavanıyla ilk 200 talebi aktarır (eski merkez davranışıyla aynı — değiştirilmedi).
- Takvim/Maliyet Merkezi pencereleri sabittir (bu ay / son 30 gün — etikette yazar); pencere seçimi istenirse
  eklemeli küçük iştir. Yeni kaynak = `Sources`'a 1 satır + `Build`'e 1 case (+API/UI otomatik). Yeni import
  seti = mevcut ImportService deseni (ayrı karar ister — PK-M3).
- Eski istemci uyumu: yalnız EKLEMELİ uçlar; eski masaüstü/web sürümleri etkilenmez.

## 8. Canlıya alınma durumu

⛔ **YAYINLANMADI** — yeni çalışma stratejisi (kullanıcı, 2026-08-28): uzun süre production yayın yok;
iş build+test seviyesinde tamamlandı. Migration olmadığından yayın bekleyen ŞEMA borcu da yok — ileride
toplu yayına yalnız kod olarak girer (API+Web+masaüstü paketi birlikte).

## 9. Sonraki roadmap işi

**O — Barkod / QR** (FAZ 5/SIRA 14). 7b Bakım-Ekipman genişletmesi hâlâ serbest sırada.
