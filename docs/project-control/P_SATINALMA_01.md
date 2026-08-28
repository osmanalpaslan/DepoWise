# STN-01 — Satın Alma · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-169** · Roadmap: FAZ 2 / SIRA 6 (MASTER_ROADMAP §1) — **FAZ 2 SONU**

## 1. Analiz sonucu ve model kararları (mevcut üründen türetildi — ürün sorusu gerekmedi)

**Mevcut olan:** talep operasyon zinciri (Purchasing→OrderPlaced→OrderPreparing→Shipped→…→Delivered,
şartname 2026-08-08) + `request_ops_purchase` durum-geçiş yetkisi + suppliers + ReceiveIn + fatura/cari.
**Eksik olan:** gerçek SİPARİŞ kaydı (tedarikçi + satır fiyatları) ve MAL KABUL köprüsü. Yalnız bu boşluk
dolduruldu.

| Karar | İçerik |
|---|---|
| Talep bağı | **OPSİYONEL** — talepli ve talepsiz alım tek modelde; talep seçilince satırlar talepten KOPYALANIR (öneri, düzenlenebilir) |
| Onay/teklif | **EKLENMEDİ** (mevcut üründe yok; talep zincirinin kendi onay/durum akışı geçerli — ERP büyütme yasağı). İhtiyaç doğarsa ürün kararıyla açılır |
| Sipariş durumu | Asgari: **Açık → Tamamlandı (tüm satırlar kabul edilince OTOMATİK) / İptal**. Talep zincirinin durum makinesi DEĞİŞTİRİLMEDİ; sipariş ona bağla oturur |
| Mal kabul | **MEVCUT `ReceiveInTx`** çağrılır (ikinci stok mekanizması YOK); kısmi kabul serbest |
| Proje bağı | project_id EKLENMEDİ — teslim şubesi üzerinden türetilir (C kararı, üçlü gerçeklik yasak) |
| Maliyet merkezi | Sipariş başlığında seçilir; KABULDE oluşan stok belgesine **D'nin dış-bağıyla** aktarılır → özet gerçekleşme anında, TEK kaynaktan (stok belgesi) okur — çift sayım yok |

## 2. Veri modeli — Migration079… pardon **Migration078** (şema v78)

`purchase_orders` (order_no firma içi benzersiz · supplier/request/branch/cost_center NULLABLE FK'lar ·
status · order_date=İŞ GÜNÜ ADR-162 · not) + `purchase_order_lines` (malzeme · miktar · birim fiyat ·
para birimi · **received_qty** — kabulle artar, satırın yaşam-döngüsü alanı). Yalnız CREATE — STN12
(bit-bit) + STN13 (statik) kanıtlı. ⚠️ Canlıya UYGULANMADI. Rollback: iki DROP + schema_migrations.

## 3. Zincir ve güvenceler (testli)

**TALEP → SİPARİŞ → MAL KABUL → STOK:**
- Kabul TEK transaction'da: mevcut stok girişi + received_qty (C# decimal — REAL/float yok) + otomatik
  kapanış; **idempotency stok DEFTERİNDEN** doğrulanır (`po:<opId>` izi) → aynı kabul iki kez gönderilse
  İKİNCİ stok girişi de received artışı da OLMAZ (STN3); kalan aşımı engelli, hata tümünü geri alır (STN4).
- **Yetki:** yeni **`purchasing`** modülü (ekran/CRUD; kapalı gelir — rollere AÇILMALI).
  `request_ops_purchase` DEĞİŞTİRİLMEDİ (talep durum-geçiş yetkisi olarak kalır). Mal kabulde **stok
  kapısı DA** aranır (STN5 — stok yan kapısı yok). Kapsam: teslim şubesi BranchAccess (STN7); tenant STN6.
- **Toplam tutar** C# decimal; para birimleri karıştırılmaz. Sipariş tarihi geri-tarih yetkisine bağlı.
- **İptal:** status='cancelled' (kayıt/kabul geçmişi ve stok defteri aynen kalır; yanlış kabul mevcut
  ters-kayıt yoluyla düzeltilir). Satır düzenleme yok — iptal + yeniden açma (izlenebilirlik).
- **Senkron:** iki tablo FK sıralı (material_requests SONRASI); push kapısı purchasing; uçtan uca taşıma +
  tekrar-kopyasızlık kanıtlı (STN11). Masaüstü ÇEVRİMDIŞI sipariş açar ve kabul yapar (stok yerel).
- **Evrak:** "Sipariş" bağlı kayıt türü eklendi — teklif/irsaliye/fatura PDF'leri siparişe iliştirilir.
- Ekran logu `purchase_order`; audit create/update/cancel.

## 4. Ekranlar / API

| Katman | Ne |
|---|---|
| API | `GET /api/purchasing` (+`/{id}/lines`) · `POST` · `PUT /{id}` (meta) · `POST /{id}/cancel` · `POST /{id}/receive` · `GET /export` |
| Web | `Purchasing.razor` (/purchasing): sipariş formu (çok satırlı + talepten kopyalama) + liste (durum filtre + arama + Excel) + detayda satırlar/mal kabul |
| Masaüstü | `PurchasingView(.axaml)`: aynı düzen, YEREL (çevrimdışı çalışır), kehribar tarih, Excel yerel |
| Menü | Yeni ana menü **"Satın Alma"** (🛒, Operasyon, Zimmet'in altında). Parite 53/60. |

## 5. Testler

`SatinAlmaTests` **13/13**: oluşturma+toplam+benzersiz no (STN1) · kabul→stok+kapanış (STN2) ·
**idempotent kabul (STN3)** · aşım/iptal engelleri (STN4) · **yetki+stok kapısı (STN5)** · **tenant (STN6)**
· **kapsam (STN7)** · **maliyet merkezi bağı (STN8)** · mevcut stok akışı regresyonu (STN9) ·
senkron sıra/kapı (STN10) · **uçtan uca senkron (STN11)** · **migration kanıtı (STN12-13)**.
Regresyon: stok/talep/senkron/log **580/592** (12 atlanan=PG) · parite+FAZ2 modülleri 80/80 ·
üç Release 0 hata. Tam süit koşulmadı: ortak zincirlere imza dokunuşu yok (ReceiveInTx yalnız çağrıldı);
hedefli regresyon yüzeyi kapsıyor.

## 6. Bilinen sınırlar

- Fatura/cari OTOMATİK üretilmez (fatura mevcut ekranından kesilir; fatura PDF'i Evrak'la siparişe
  bağlanır) — istenirse ayrı iş.
- Sipariş satırı düzenleme yok (iptal+yeniden açma) · talep operasyon durumu siparişten OTOMATİK
  ilerletilmez (kullanıcı talep panosundan yönetir — mevcut akış) · teklif karşılaştırma/çok seviyeli
  onay yok (bilinçli).
- Talep satırı kopyalama malzeme KODuyla eşlenir (talep satırı ucunda malzeme id yok — mevcut sözleşme
  değiştirilmedi); eşleşmeyen satır atlanır.
- Elle doğrulama: iki platformda ekran + sipariş→kabul akışı. **"Satın Alma" yetkisi kapalı gelir**;
  mal kabul için kullanıcıda STOK yetkisi de olmalı.

## 7. Canlıya alınma durumu

⛔ **Yayınlanmadı.** Yayın bekleyenler: **Migration073..078** (C+A+E+B+D+P — FAZ 1 + FAZ 2 birlikte).

## 8. Sonraki roadmap maddesi

**F — İş Emri** (FAZ 3 / SIRA 7) — büyük modül; C+E+B+D bağımlılıkları TAMAM. Başlamadan ürün soruları
gerekecek (iş emri akışı/atama). Ayrıca bakım-ekipman entegrasyonu (EKP kontrol belgesi §1 PK-E2 notu)
F'den önce küçük iş olarak değerlendirilmeli.
