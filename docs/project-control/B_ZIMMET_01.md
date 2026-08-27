# ZMT-01 — Zimmet Yönetimi · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-167** · Roadmap: FAZ 2 / SIRA 4 (MASTER_ROADMAP §1)
> Karar değişirse silinmez, tarihle güncellenir.

## 1. Ürün kararları (kullanıcı, 2026-08-28)

| Karar | İçerik |
|---|---|
| **PK-B1** | **Stoklu hibrit:** MALZEME teslimi mevcut stok ÇIKIŞINI çağırır (depodan düşer), iade GİRİŞİ çağırır (döner) — aynı transaction'da (`IssueOutTx`/`ReceiveInTx`, fatura servisi emsali; stok defteri DEĞİŞTİRİLMEDİ, yalnız çağrılıyor). EKİPMAN stok dışıdır ve TEK kişide olabilir. |
| **PK-B2** | **Tek işlemle devir:** kullanıcı tek "Devir" yapar; defterde çift kayıt (verenden çıkış + alana giriş, aynı grupla). Stok depoya uğramaz. Zincir (Osman→Ahmet→Mehmet) sonsuza dek okunur. |
| **PK-B3** | **Kayıp stoğa dönmez** (malzeme fiilen yok); **hasarlı iade stoğa döner** ("Hasarlı İade" hareketi olarak izlenir). |
| **PK-B4** | Hedef yalnız **PERSONEL**; araçlar DAHİL DEĞİL (sürücü ataması mevcut alanıyla sürer); **tek ana ekran**. |

## 2. Veri modeli — Migration076 (şema v76)

**Tek yeni tablo `assignment_movements` — DEFTER (durum değil):** her teslim/iade/devir/kayıp AYRI
değişmez satır; "kimde ne var" Σ(yön×miktar) ile türetilir. Sahip değişiminde UPDATE yok → **geçmiş
yapısal olarak silinemez** (ZMT7 testi bunu bit-bit kilitler). Kolonlar: asset_type(material|equipment) +
asset_id (polymorphic, FK'sız — file_records emsali) · personnel_id · branch_id (işlem şubesi/deposu) ·
movement_type(issue|return|transfer_out|transfer_in|lost|damaged_return) · direction ±1 · quantity ·
group_id (devir çifti) · stock_operation_id (bağlı stok belgesi izi) · doc_date (İŞ GÜNÜ, ADR-162) ·
operation_id (TEKİL → idempotent) · standart damgalar.
Yalnız CREATE — ZMT15 (bit-bit) + ZMT16 (statik) kanıtlı. ⚠️ Canlıya UYGULANMADI.
Rollback: `DROP TABLE assignment_movements; DELETE FROM schema_migrations WHERE version=76;`

## 3. Mimari

- **Atomiklik:** malzeme işlemi = zimmet satırı + stok belgesi TEK transaction'da; stok yetersizse
  mevcut negatif-stok kalkanı işlemi tümüyle geri alır (ZMT3). Stok operation_id: `assign:<opId>`.
- **İdempotency:** aynı operationId ikinci kez → İKİNCİ hareket YOK ve İKİNCİ STOK DÜŞÜMÜ YOK (ZMT8).
- **Yetki:** yeni **`assignments`** modülü (deny-by-default, yayında rollere AÇILMALI). Malzeme
  teslim/iadesinde **stok kapısı (stock.Create) DA gerekir** — fatura ile aynı kural; zimmet stok
  yetkisinin yan kapısı değildir (ZMT9). Kapsam: işlem şubesi üzerinden BranchAccess (ZMT10); tenant ZMT11.
- **Tarih:** doc_date iş günüdür; geri/ileri tarih `btn-backdate` yetkisine bağlı (ZMT12); kayıt anı ayrı.
- **Senkron:** `assignment_movements` listeye FK sıralı eklendi (personnel/materials/equipment sonrası);
  push kapısı assignments modülü. ZMT14: zimmet + bağlı stok hareketi AYNI pakette uçtan uca taşınır,
  ikinci uygulama kopya üretmez. Masaüstü ÇEVRİMDIŞI çalışır (stok gibi yerel yazar).
- Ekran logu: `ScreenAuditMap["assignments"]`; audit entity `assignment_movement`.
- Silme YOK: defter satırı silinmez/düzenlenmez — yanlış işlem TERS işlemle düzeltilir (iade/teslim).
  Bu yüzden Çöp Kutusu'na girmez (silinecek şey yok).

## 4. Ekranlar / API (PK-B4: tek ekran)

| Katman | Ne |
|---|---|
| API | `GET /api/assignments/holdings` · `/history` · `POST /issue` `/return` `/lost` `/transfer` · `GET /export` (Excel, export yetkisi). operationId istemciden (idempotent retry). |
| Web | `Assignments.razor` (/assignments): işlem formu (Teslim/İade/Hasarlı İade/Devir/Kayıp) + "Kimde Ne Var" + satıra tıklayınca geçmiş zinciri + Excel. Tarih alanı backdate yetkisine kilitli. |
| Masaüstü | `AssignmentsView(.axaml)`: aynı düzen; YEREL (çevrimdışı çalışır); kehribar tarih; Excel yerel. |
| Menü | Yeni ana menü **"Zimmet"** (🧰, Operasyon, Ekipman'ın altında). Parite 51/58. |

## 5. Testler

`ZimmetTests` **16/16**: stok düşüş/dönüş (ZMT1) · ekipman stok dışı + tek kişide (ZMT2) · negatif stok
kalkanı (ZMT3) · fazla iade/devir engeli (ZMT4) · kayıp/hasar (ZMT5) · devir çift kayıt + stok
oynamaz (ZMT6) · **geçmiş bit-bit değişmez (ZMT7)** · **idempotent retry — çift stok düşümü yok (ZMT8)** ·
yetki + stok yan-kapı kapalı (ZMT9) · kapsam (ZMT10) · tenant (ZMT11) · geri-tarih yetkisi (ZMT12) ·
senkron sıra/kapı (ZMT13) · **uçtan uca senkron (ZMT14)** · **migration kanıtı (ZMT15-16)**.
Regresyon: stok/senkron/ekipman/tarih **485/494** (9 atlanan=PG) · parite 19/19 · üç Release 0 hata.

## 6. Bilinen sınırlar / notlar

- Toplu teslim/iade · teslim alan onayı · geçici zimmet süresi · zimmet tutanağı (PDF üretimi) —
  ilk sürümde YOK (ürün kararı olarak açık; tutanak yerine Evrak ekranından personele/ekipmana belge
  eklenebilir — Evrak'a "zimmet hareketi" bağlama satırı da ileride tek satırla eklenir).
- Zimmet RAPORU (Raporlar ekranında) yok — "Kimde Ne Var" + Excel bu ihtiyacı ilk sürümde karşılar.
- Kısmi senkron paketinde stok ve zimmet satırı farklı turlarda ulaşabilir (her ikisi idempotent —
  sonraki turda tamamlanır; talep/stok ilişkisinin bugünkü davranışıyla aynı sınıf).
- **Elle doğrulanacak:** iki platformda ekran açılışı + örnek teslim/iade akışı. Yeni **"Zimmet"
  yetkisi kapalı gelir**; malzeme zimmeti için kullanıcıda STOK yetkisi de olmalı.
- PostgreSQL koşusu yerelde atlandı (PG yok); ortak sözdizimi.

## 7. Canlıya alınma durumu

⛔ **Yayınlanmadı.** Yayın turunda Migration073..076 birlikte koşar; öncesi/sonrası canlı salt-okunur
sayım alınacak.

## 8. Sonraki roadmap maddesi

**D — Maliyet Merkezi** (FAZ 2 / SIRA 5). Ön koşul C tamam; başlamadan maliyet dağıtım kuralı ürün
sorusu netleşmeli (MASTER_ROADMAP notu).
