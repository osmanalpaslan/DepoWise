# EMR-01 — İş Emri · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-170** · Roadmap: FAZ 3 / SIRA 7 (MASTER_ROADMAP §1)
> Analiz: [F_ISEMRI_00_ANALIZ.md](F_ISEMRI_00_ANALIZ.md) — PK-F1..F9 kullanıcı tarafından KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-F1 | Akış `Taslak→Atandı→Devam Ediyor⇄Beklemede→Tamamlandı` + her aktif durumdan `İptal`; geçiş matrisi serviste kilitli; onay katmanı YOK. İlerletme=Edit, iptal=Delete yetkisi. |
| PK-F2 | `Tamamlandı`/`İptal` TERMİNAL — durum/meta/atama/tüketim dahil HİÇBİR yol geriye açmaz (EMR3). Devam-Ediyor'a ilk geçiş `actual_start`, tamamlama `actual_end` yazar (iş günü, backdate yetkili). |
| PK-F3 | Tüketim = MEVCUT `IssueOutTx`, tek transaction, `wo:` iziyle; idempotency STOK DEFTERİNDEN (EMR6 — retry ikinci çıkış üretmez); negatif stok kalkanı (EMR7); STOK yetkisi DA aranır (EMR9). |
| PK-F4/F8 | Yalnız ATAMA (personel/araç/ekipman, polymorphic tek tablo) — saat/puantaj/sayaç/kullanım maliyeti YOK. Zimmet defteri hiç etkilenmez (EMR4 kanıtlı); araç sürücü atamasına dokunulmadı. |
| PK-F5 | Yalnız `branch_id` (şantiye/saha); proje TÜRETİLİR — project_id yok. BranchAccess okuma+yazma (EMR10). |
| PK-F6 | Numara elle + firma içi benzersiz (EMR1). |
| PK-F7 | Alt/tekrarlayan iş emri YOK (tekrarlayan → H Takvim). |
| PK-F9 | Bakım yalnız DIŞ BAĞ — kaynak bakım kaydı bit-bit değişmez (EMR12); maliyet özetine "Bakım Malzemesi" olarak girer. Bakım-ekipman genişletmesi AYRI küçük iş (F sonrası). |

## 2. Veri modeli — Migration079 (şema v79, yalnız CREATE)

`work_orders` (no·başlık·durum·öncelik[mevcut set]·şantiye·maliyet merkezi·sorumlu·plan/gerçek tarihler·
kapanış notu·oluşturan/tamamlayan) + `work_order_assignments` (polymorphic kaynak; kaldırma=soft) +
`work_order_links` (stock_document | vehicle_maintenance | purchase_order; UNIQUE(entity)=bir kayıt tek
iş emrine) + `work_order_status_history` (append-only defter). Mevcut tablolara **ALTER dahi yok** —
EMR15 (bit-bit) + EMR16 (statik) kanıtlı. ⚠️ Canlıya UYGULANMADI. Rollback: 4 DROP + schema_migrations.

## 3. Mimari / entegrasyonlar

- **Maliyet özeti**: bağlı stok belgeleri (satır×fiyat; boşsa kart fiyatı — MLY deseni) + bağlı bakım
  malzeme maliyeti; C# decimal, para birimi ayrık; MEVCUT hesaplara sıfır dokunuş. Merkez seçiliyse
  tüketim belgesi D dış-bağıyla merkeze de bağlanır (EMR8 — merkez özeti aynı kaynağı okur, çift sayım yok).
- **Evrak**: "İş Emri" bağlı kayıt türü (Documents ekranı, `wo_no` etiketiyle).
- **Yetki**: yeni `work_orders` modülü (kapalı gelir — rollere AÇILMALI); tüketimde stok kapısı;
  ekran logu `work_order`; audit create/update/status/cancel.
- **Senkron**: 4 tablo FK sıralı (purchase_order_lines SONRASI; bağ hedefleri yukarıda); push kapısı
  work_orders; uçtan uca taşıma + tekrar-kopyasızlık kanıtlı (EMR14). Masaüstü ÇEVRİMDIŞI tam işlevli.
- **Silme**: fiziksel yok — iptal durumdur; geçmiş defteri append-only.
- Ekranlar: tek ana ekran + güçlü detay (genel/durum düğmeleri matristen/atamalar/tüketim/maliyet/
  ilişkiler/geçmiş) — web `WorkOrders.razor` (/work-orders) + masaüstü `WorkOrdersView`. Excel liste
  kuralı 2. Parite 54/61. Mobil notu: durum değişimi tek POST — ileriki responsive kullanım ek API istemez.

## 4. Testler

`IsEmriTests` **16/16**: no benzersiz+defter (EMR1) · matris+actual tarihler (EMR2) · **terminal kilidi
(EMR3)** · atamalar+zimmet-etkisizliği (EMR4) · **tüketim→stok+özet (EMR5)** · **idempotent (EMR6)** ·
negatif stok (EMR7) · merkez bağı (EMR8) · **yetki+stok kapısı+iptal-Delete (EMR9)** · **kapsam (EMR10)**
· **tenant (EMR11)** · **bakım bağı bit-bit (EMR12)** · senkron sıra/kapı (EMR13) · **uçtan uca (EMR14)**
· **migration kanıtı (EMR15-16)**.
Regresyon (hedefli, 12 alan): stok/zimmet/satın alma/maliyet/ekipman/araç/bakım/talep/evrak/senkron/log
**777/787** (10 atlanan = yerelde PostgreSQL yok — tek atlama nedeni bu). Parite 19/19.
Üç Release build 0 hata. Tam süit koşulmadı: ortak zincirlere imza dokunuşu yok (IssueOutTx yalnız
çağrıldı); 12 alanlık hedefli regresyon yüzeyi kapsıyor.

## 5. Canlı veri güvenliği

Canlıya yazma YOK · mevcut kayıt değişimi YOK (EMR15 bit-bit; EMR12 bakım kaydı bit-bit) · fiziksel
silme YOK · production migration/deploy YOK. Migration079 yalnız CREATE.

## 6. Bilinen sınırlar / elle test

Puantaj·kullanım metriği·alt/tekrarlayan emir·Raporlar-menüsü raporları·zimmet köprüsü bilinçli KAPSAM
DIŞI (analiz §17). İki platformda gözle doğrulama size kaldı (Avalonia otomasyonu yok; ekranlar mevcut
desenlerin — Zimmet/Satın Alma — birebir devamı olarak kod düzeyinde doğrulandı). **"İş Emirleri"
yetkisi kapalı gelir**; tüketim için kullanıcıda STOK yetkisi de olmalı.

## 7. Canlıya alınma durumu

✅ **YAYINLANDI — 2026-08-28 toplu yayın** (kullanıcı onayı; Migration073..081 canlıda birlikte uygulandı).
API **v174** · Web **v199** · Masaüstü **1.0.160** (SHA-256 EA688F2F…59CAE2). Kanıtlar:
[TOPLU_YAYIN_2026-08-28.md](TOPLU_YAYIN_2026-08-28.md) — deploy öncesi/sonrası canlı salt-okunur sayım/karma
karşılaştırması: mevcut TÜM tabloların satır içerikleri BİT-BİT AYNI; yeni tablolar BOŞ; şema 72→81.
Yeni yetkiler hiçbir role otomatik AÇILMADI — rollere kontrollü açılacak durumda.
## 8. Sonraki roadmap işi

**H — Takvim** (FAZ 3/SIRA 8). Ayrıca PK-F9 gereği **"Bakım-Ekipman genişletmesi"** ayrı küçük iş
olarak roadmap'e eklendi — H'den önce/sonra fark etmez (teknik bağımlılığı yok; iş emri bağı hazır).
