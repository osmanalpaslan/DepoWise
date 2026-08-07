# Araç Raporu — Ayrıntılı Analiz (yalnız analiz; kod yok) — 2026-08-07 (Opus 4.8)

> Amaç: Ortak rapor mimarisi (Birim 1-4) üzerine **Araç Raporu**'nu yeniden tasarlamadan ÖNCE mevcut durumu,
> eksikleri, performansı ve maliyet/yakıt/bakım hesaplarını çıkarmak. **Mimari değişmeyecek**; rapor bu
> altyapının üzerine kurulacak. Bu belge nihai tasarım DEĞİL — birlikte karara bağlanacak öneriyi içerir.

---

## 1. MEVCUT DURUM

### 1.1 "Araç Raporu" bugün tek bir rapor değil — 3 rapora dağılmış
Katalogda (`ReportCatalog`) doğrudan **araç-başına maliyet raporu YOK**. Araçla ilgili maliyet üç ayrı yere dağılmış:

| Katalog anahtarı | Ad | Kategori | Ne yapar | Maliyet? |
|---|---|---|---|---|
| `general` | Genel Rapor | Yönetim | **Araç başına** KM, Litre, L/km, Malzeme Maliyeti, Yakıt Maliyeti, Toplam | ✅ (en yakın) |
| `fuel` | Yakıt Tüketim | Yakıt | Araç başına İşlem, KM, Litre, L/km, Tutar | kısmi (yalnız yakıt) |
| `maintenance` | Bakım Raporu | Bakım | Bakım kaydı başına satır (Tarih, Araç, Bakım, Teknisyen, Malzeme Maliyeti) | kısmi (yalnız bakım malzeme) |
| `vehicles-template` / `vehicles-nontemplate` | Araç — Şablonlu/Şablon Dışı | Araç | Yalnız **liste** (İç Kod/Plaka/Şube/Durum) — **maliyet YOK** | ❌ |

**Sonuç:** Kullanıcının tarif ettiği "Araç Raporu" (araç başına yakıt+bakım+parça+km/saat başına maliyet+ort.
tüketim+ort. fiyat+toplam) fiilen **`general` raporunun geliştirilmiş hâlidir**. "Araç" kategorisindeki iki
rapor sadece envanter listesidir. Yeni Araç Raporu ya `general`'i temel alacak ya da onu emen yeni bir rapor olacak.

### 1.2 Kullandığı tablolar (veri kaynakları)
- **`vehicles`** — araç kartı. Kritik alan: `meter_unit` = **`km` | `hour`** (araç km bazlı mı yoksa saat bazlı
  iş makinesi mi). `current_meter` (TEXT). Alış değeri / amortisman alanı **YOK**.
- **`fuel_distributions`** — araca yakıt dağıtımı: `liters`, `unit_price` (snapshot), `prev_meter`/`current_meter`,
  `distribution_date`, `op_branch_id`, `vehicle_id`. İndeks: `ix_fuel_dist_vehicle(vehicle_id, distribution_date)` ✅.
- **`vehicle_maintenances`** — bakım kaydı: `performed_km`, `performed_hour`, `performed_date`, `op_branch_id`,
  `technician_id`, `is_cancelled`. **İşçilik/servis ücreti alanı YOK** (yalnız malzeme maliyeti dolaylı gelir).
- **`maintenance_materials`** — bakımda kullanılan malzeme: `quantity`, `unit_price` (snapshot). İndeks: `(maintenance_id)` ✅.
- **`stock_documents`** — `doc_type` (in/out/transfer/count) + **`vehicle_id` (NULL edilebilir)** → **araca
  DOĞRUDAN parça çıkışı** mümkün (bakım dışı). + **`stock_movements`** (`quantity`, `unit_price` snapshot,
  `document_id`) = gerçek parça maliyeti burada.
- **`vehicle_inspections`** — muayene/sigorta/kasko: **maliyet alanı YOK** (yalnız tarih/sonuç).
- **`fx_rates`** + `currency_code`/`fx_rate` snapshot'ları — çoklu para birimi altyapısı var; raporlar bunu KULLANMIYOR.

### 1.3 Kullandığı servis / veri akışı
- Tek servis: **`ReportService`** (Infrastructure/Reporting). Ortak giriş: `Run(s, key, req, maxRows)` →
  katalog dispatch + **Bu Ay** tarih varsayılanı + maks-kayıt. Web (`/api/reports/{key}`) ve masaüstü (`Reports.Run`)
  AYNI metodu çağırır (Birim 1). Yetki: `AccessControl.Require(reports, View)` + `ReportGate` (Sorgula'sız çalışmaz)
  + `ReportScope` (şube yetkisi). Çıktı: `TableModel` (Headers + Rows). Excel export TableModel'i olduğu gibi döker
  (yeni kolonlar otomatik gelir — export tarafı değişmez).
- **Veri akışı:** UI (Sorgula) → API/masaüstü → `ReportService.General` → tek SQL → satırlar → (Birim 4 sonrası)
  ortak tablo bileşeni istemcide filtre/sıralama/gizleme.

### 1.4 Mevcut hesaplama yöntemleri (`general`)
- **KM** = `Σ(current_meter − prev_meter)` yalnız iki sayaç da doluyken. (Sayaç birimi km ise km, **saat ise saat** —
  ama kolon her hâlde "KM" yazıyor.)
- **Litre** = `Σ liters`. **Yakıt Maliyeti** = `Σ(liters × unit_price)` (snapshot fiyat — gerçek ödenen).
- **L/km** = `litre / km` (km=0 → 0).
- **Malzeme Maliyeti** = **korelasyonlu alt-sorgu**: `Σ(maintenance_materials.quantity × unit_price)` (o araca ait,
  tarih filtreli bakımlar). — **yalnız bakım içi malzeme**; doğrudan stok çıkışı DAHİL DEĞİL.
- **Toplam** = Malzeme + Yakıt.
- Araçlar `fuel_distributions`'a LEFT JOIN → yakıtı/bakımı olmayan araç da 0 ile görünür (tam filo görünürlüğü ✅).

---

## 2. EKSİKLER

### 2.1 Eksik / yanlış kolonlar ve hesaplar
1. **Km/saat başına maliyet YOK.** Veri var (Toplam ÷ mesafe), hesaplanmıyor. Kullanıcının açık isteği.
2. **Saat başına maliyet + sayaç birimi ayrımı YOK.** `meter_unit` (km/hour) dikkate alınmıyor → saat bazlı iş
   makinelerinde "KM" ve "L/km" **yanlış etiket** ve yanlış yorum. Doğrusu: km aracında "₺/km", saat makinesinde "₺/saat".
3. **Ortalama yakıt fiyatı YOK.** Türetilebilir (`yakıt maliyeti ÷ litre` = ağırlıklı ort. ₺/L), gösterilmiyor.
4. **Ortalama tüketim** var (L/km) ama saat makinesinde L/saat olmalı (etiket sabit "L/km").
5. **Doğrudan parça maliyeti DAHİL DEĞİL.** `stock_documents.vehicle_id` üzerinden araca yapılan bakım-dışı stok
   çıkışları (`stock_movements.quantity × unit_price`) hiç sayılmıyor → **parça ve toplam maliyet olduğundan düşük**.
6. **Bakım işçilik/servis maliyeti YOK** (şemada alan yok) → bakım maliyeti yalnız malzeme; harici servis/işçilik hariç.
7. **Muayene/sigorta/kasko maliyeti YOK** (şemada alan yok) → toplam araç maliyetine giremiyor.
8. **Toplam araç maliyeti eksik:** yalnız (bakım malzemesi + yakıt); doğrudan parça + işçilik + sigorta hariç.
9. **Çoklu para birimi göz ardı ediliyor** — farklı currency'li satırlar TL gibi toplanıyor (TR tek-para için sorun
   değil; ileride risk). `fx_rate` snapshot'ı var ama kullanılmıyor.
10. **Filtre seçenekleri dar:** yalnız Tarih + Şube. **Araç seçimi (çoklu), araç tipi/kategori/marka, sayaç birimi**
    filtresi yok — oysa katalogda `ReportFilters.Vehicle` bayrağı TANIMLI ama hiçbir rapor kullanmıyor (Birim 2/3'te
    araç filtresi UI'da gizli çünkü hiçbir raporda işaretli değil).

### 2.2 Metodoloji (doğruluk) eksikleri
- **KM = yakıt fişleri arası fark** → dönemdeki **gerçek toplam km değil** (ilk dolumdan önce / son dolumdan sonra
  kat edilen yol hariç). Cost/km yaklaşık kalır. Alternatif: `vehicle_meter_logs` veya dönemdeki (max−min sayaç).
- **Negatif/atlayan sayaç doğrulaması yok** (`current < prev` veya sayaç sıfırlama → hatalı fark). Ele alınmıyor.

---

## 3. PERFORMANS SORUNLARI (yalnız tespit — optimizasyon değil)

1. **Korelasyonlu alt-sorgu (N+1 deseni) — `general`.** Malzeme maliyeti her araç satırı için ayrı çalışır
   (`SELECT … FROM vehicle_maintenances JOIN maintenance_materials WHERE vm.vehicle_id=v.id …`). N araç → N kez
   bakım taraması. **En önemli darboğaz.** Çözüm yönü (öneride): araç bazında **önceden toplanmış türetilmiş tabloya
   (derived table) LEFT JOIN** → tek geçiş.
2. **Sayısal değerler TEXT saklanıyor** (`liters/unit_price/quantity/*_meter/performed_*` hepsi TEXT) → her satırda
   `CAST(... AS REAL)`; sayısal indeks kullanılamaz, CPU maliyeti. (Şema değişikliği istenmiyor → CAST kalır, ama
   toplama zaten indeksli tarama üzerinde; asıl maliyet N+1'de.)
3. **İndeksler kısmen uygun:** `fuel_distributions(vehicle_id, distribution_date)` ✅ ve `maintenance_materials
   (maintenance_id)` ✅ var. Ama `vehicle_maintenances` indeksi `(vehicle_id, maintenance_def_id, created_at)` —
   rapor **`performed_date`** ile filtreliyor (indekste yok) → tarih daraltması indeks-dışı; `stock_documents.vehicle_id`
   indeksi **yok** (doğrudan parça yolu eklenirse gerekebilir).
4. **Naif çoklu-JOIN tuzağı (gelecekte):** yakıt + bakım-malzeme + doğrudan-çıkış AYNI sorguda düz JOIN'lenirse satır
   çarpımı (fan-out) → maliyet şişer. Mevcut kod alt-sorguyla bundan kaçınmış (doğru ama yavaş). **Yeni tasarımda her
   maliyet kaynağı ayrı derived-table olarak toplanmalı** (çarpım yok, tek geçiş).
5. **Sunucu vs istemci ayrımı:** Toplama (SUM, potansiyel binlerce yakıt/hareket satırı) **sunucuda** kalmalı. Sonuç
   kümesi = araç sayısı (küçük) → filtre/sıralama/gizleme ve türetilmiş oran kolonlarının GÖSTERİMİ **istemcide** (Birim 4
   ortak tablosu) yapılabilir. Öneri: türetilmiş oranlar (₺/km, ort. fiyat) **sunucuda kolon olarak** üretilsin ki
   Excel export'a da yansısın; istemci yalnız Birim 4 filtre/sıralama/gizleme yapsın.
6. **Bu Ay varsayılanı** (RequiresDate) tarama penceresini sınırlıyor ✅ (milyon satır taraması engelli). Maks-kayıt
   koruması sonuç kümesini de sınırlıyor ✅.

---

## 4. GELİŞTİRME ÖNERİLERİ (mimariyi değiştirmeden)

- **Tek "Araç Raporu" oluştur** (`vehicle` anahtarı, `ReportCategory.Vehicle`, `ReportGroup.Standard`,
  `Date|Branch|Vehicle`, RequiresDate). `general`'i temel al; katalog + `ReportService`'e 1 metod (mimari değişmez).
- **N+1'i kaldır:** malzeme/parça/yakıt toplamlarını araç bazında **derived-table LEFT JOIN** ile tek geçişte topla.
- **`meter_unit`'i her yere taşı:** kolon başlıkları ve oranlar araç bazında km/saat'e göre etiketlensin
  (₺/km ↔ ₺/saat, L/km ↔ L/saat). Karışık filo için "Sayaç" kolonu + birim-bilinçli oran.
- **Doğrudan parça maliyetini ekle:** `stock_documents(doc_type='out', vehicle_id)` → `stock_movements` toplamı.
  Gerekirse `stock_documents(vehicle_id)` indeksi (ayrı, küçük migration — yalnız performans; şema mantığı değişmez).
- **Türetilmiş kolonlar sunucuda:** Ort. yakıt fiyatı, ₺/birim, L/birim → SQL'de hesapla (export tutarlı olsun).
- **Filtreleri genişlet:** Araç (çoklu) + araç tipi/kategori/marka (opsiyonel) — `ReportFilters.Vehicle` zaten var,
  UI otomatik gelir (Birim 2/3 altyapısı). Sayaç birimi filtresi opsiyonel.
- **İstemci tarafı:** Birim 4 ortak tablosu ile kolon-altı filtre/sıralama/gizleme + kişisel tercih (rapor anahtarı
  bazlı) — ek iş yok, otomatik.
- **Karar bekleyen (şema dokunuşu — kullanıcı onayı):**
  - (a) **Bakım işçilik/servis maliyeti** alanı eklensin mi? (yeni kolon → küçük migration; istenmezse "yalnız malzeme")
  - (b) **Muayene/sigorta/kasko maliyeti** toplam maliyete girsin mi? (alan yok → küçük migration gerekir)
  - (c) **Araç alış değeri / amortisman** eklensin mi? (Toplam Sahip Olma Maliyeti için; yeni alan gerekir)
  - (d) **Doğrudan stok çıkışı parçaları** parça maliyetine dahil mi? (önerilen: EVET; şema hazır, migration gerekmez)
  - (e) **KM ölçümü:** yakıt-fişi-farkı mı (mevcut) yoksa dönem (max−min sayaç)/meter-log mu?

---

## 5. PLANLANAN YENİ YAPI (öneri — birlikte kesinleştirilecek)

### 5.1 Katalog kaydı
`new ReportDescriptor("vehicle", "Araç Raporu", "Araç başına yakıt + bakım + parça maliyeti ve birim maliyet",
ReportCategory.Vehicle, ReportGroup.Standard, Date|Branch|Vehicle, RequiresDate, ExportStandard)`

### 5.2 Filtreler
Tarih (Bu Ay varsayılan) · Şube (yetkiliyse çoklu) · **Araç (çoklu)** · (opsiyonel) Araç Tipi/Kategori/Marka · (opsiyonel) Sayaç birimi.

### 5.3 Önerilen kolonlar (araç başına tek satır + TOPLAM)
İç Kod · Plaka · (Şube) · **Sayaç Birimi** (km/saat) · **Dönem Mesafe/Çalışma** (birim-bilinçli) · Yakıt Litre ·
**Ort. Yakıt Fiyatı (₺/L)** · Yakıt Maliyeti · **Ort. Tüketim (L/birim)** · Bakım Malzeme Maliyeti ·
**Doğrudan Parça Maliyeti** · [Bakım İşçilik — karar (a)] · **Toplam Maliyet** · **Birim Başına Maliyet (₺/birim)**.

### 5.4 Hesaplama kaynakları (hepsi tek geçiş, derived-table LEFT JOIN)
- Yakıt: `fuel_distributions` (litre, ₺, sayaç farkı) araç+tarih bazında toplanır.
- Bakım malzeme: `vehicle_maintenances → maintenance_materials` araç+tarih bazında toplanır (korelasyon YOK).
- Doğrudan parça: `stock_documents(out, vehicle_id) → stock_movements` araç+tarih bazında toplanır (karar d).
- Türetilenler sunucuda: ort. fiyat = yakıt₺/litre; ₺/birim = toplam/mesafe; L/birim = litre/mesafe (0'a bölme korumalı).
- `meter_unit` araç kartından → kolon/etiket ve oranlar birim-bilinçli.

### 5.5 Performans hedefi
Tek sorgu, N+1 yok, indeksli araç+tarih taraması, sonuç = araç sayısı (küçük). İstemci yalnız Birim 4 filtre/sıralama/gizleme.

### 5.6 Kapsam DIŞI (bu rapor için)
Ortak mimari değişikliği yok · başka rapor/ekran dokunulmaz · şema dokunuşu yalnız kullanıcı (a/b/c) onaylarsa.

---

## Açık sorular (tasarımı kesinleştirmeden önce kullanıcı kararı)
1. Bakım **işçilik/servis** maliyeti alanı eklensin mi, yoksa "yalnız malzeme" mi kalsın? (a)
2. **Muayene/sigorta/kasko** ve **araç alış değeri/amortisman** bu rapora girsin mi? (b/c — şema dokunuşu gerektirir)
3. **Doğrudan stok çıkışı** parçaları maliyete dahil edilsin mi (öneri: evet)? (d)
4. **KM/saat ölçümü** yakıt-fişi-farkı mı kalsın, yoksa dönem sayaç farkı/meter-log mu? (e)
5. Yeni Araç Raporu `general`'in **yerine mi geçsin** yoksa `general` kalıp **ayrı yeni rapor** mu olsun?
