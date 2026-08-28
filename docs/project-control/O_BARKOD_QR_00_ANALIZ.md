# O — BARKOD / QR · ANALİZ SONUCU (O-00, kod yok)

> Tarih: **2026-08-28** · Durum: **ANALİZ — PK-O kararları bekleniyor** · Kod/migration/deploy/canlı erişim: **YOK**
> Roadmap: FAZ 5 / SIRA 14 — tanım: *"Ortak özellik + alanlar"* (MASTER_ROADMAP §1; başka bağlayıcı tanım
> belgesi YOK — V6 analizde barkod/QR maddesi yoktur, kapsam bu analiz + PK-O kararlarıyla kesinleşir).
> Temel amaç tespiti: **"Taramayla mevcut kaydı hızlı ve güvenli şekilde bulmak"** — işlem kapısı DEĞİL.

## 1. Mevcut altyapı (kod taraması)

- **Barkod/QR altyapısı YOK** (2/3): kodda tek iz, EKP-01'in "ileride entity_type+entity_id ile bağlanır"
  notudur. Tarama, üretme, kamera, okuyucu — hiçbiri yok. Yeniden oluşturulacak bir şey de yok.
- **K — Global Arama (ARA-01) taramanın hazır omurgasıdır:** iki platformda üst bar kutusu, **Enter ile arar**
  (öneri yok, min 2 karakter) → kategori panelinde sonuçlar → tıklayınca ekrana gider; masaüstünde 4 ekran
  (Malzemeler·Araçlar·Bakım·Stok Hareketleri) `IDeepLinkTarget` ile **kaydı da açar**. 12 SQL kaynağı +
  3 servis kaynağı; her kaynak KENDİ modül yetkisi + tenant + BranchAccess + silinmiş-süzme kapısından geçer;
  bellek-içi Türkçe-doğru eşleşme; "BAŞLAYAN önce" sıralaması. **USB barkod okuyucu klavye taklidi yapar ve
  sonuna Enter basar → bugünkü arama kutusu, okuyucuyla FİİLEN zaten çalışır** (kutunun odakta olması şartıyla).
- **M — Excel Merkezi:** kod/no alanları tüm exportlarda zaten var; import 7 sette sabit (PK-M3 kararı korunur).

## 2–4. Barkod / QR / mevcut kimlik alanları

| Kaynak | Taranabilir kimlik (firma içi BENZERSİZ) | Aramada bugün var mı |
|---|---|---|
| Malzeme | `materials.code` (`ux_materials_code`) | ✅ (name+code) |
| Araç | `vehicles.internal_code` (unique) + `plate` | ✅ (internal_code+plate) |
| Ekipman | `equipment.code` (unique) + `serial_no` (EKP-01'de hazır) | ✅ (name+code) |
| İş Emri | `work_orders.wo_no` | ✅ (title+wo_no, şube süzgeçli) |
| Satın Alma | `purchase_orders.order_no` (unique) | ✅ |
| Talep | `material_requests.doc_no` (unique) | ✅ |
| Cari / Şube / Maliyet M. | `code` alanları | ✅ |
| Zimmet | KENDİ kimliği yok — varlık = malzeme/ekipman/araç kodu | (varlık kodları üstteki satırlarla bulunur) |
| Stok hareketi | belge no `stock_documents.doc_no` | ✖ bilinçli (PK-K1: hareket defteri aranmaz) |
| Personel | kod yok (ad) | ✅ ad ile |

**Sonuç: her fiziksel varlığın taranabilir, benzersiz, SENKRONLANAN mevcut kimliği zaten var.** Yeni
`barcode_id`/`qr_id` alanı v1 için GEREKMEZ. Üretici (ambalaj) barkodu — EAN-13 — hiçbir tabloda yok; o ayrı
bir karardır (PK-O3).

## 3. Barkod ↔ QR ayrımı ve donanım

- **USB okuyucu (klavye emülasyonu)** — 1D ve 2D(QR) okuyucuların tamamı işletim sistemine "klavye" görünür:
  kod yazılır + Enter. **SIFIR yazılım bağımlılığı, sıfır donanım entegrasyonu, offline, iki platformda aynı.**
  Masaüstünün (birincil uygulama) doğal yolu budur.
- **Elle giriş fallback** — kutuya kodu yazmak zaten aynı yol; okuyucu bozulsa da akış durmaz.
- **Kamera taraması** — masaüstü Avalonia'da kamera+decode yığını ağır ve kanıtsız (yeni native bağımlılık);
  web'de getUserMedia+JS kütüphanesi mümkün ama pariteyi bozar ve birincil platform masaüstüne değer katmaz.
  **v1 dışı önerilir** (madde 9/15 gereği ağır altyapı kurulmaz).
- **Etiket ÜRETİMİ (yazdırılacak QR):** araç/ekipman/raf üstünde tarayacak etiket yoksa tarama ancak elle
  yazılan kodlar kadar hızlıdır. Öneri: kayıt başına **QR etiketi (PNG)** üretme — içerik **kaydın mevcut
  kodu DÜZ METİN** ("EKP-001"): okuyucu onu aynen "yazar", özel şema/çözümleyici gerekmez, telefon kamerası
  bile okur. 1D üretim (Code128) gereksiz — 2D okuyucular ve telefonlar QR okur; iki format tek işe iki
  altyapı olurdu. Üretim kütüphanesi: **QRCoder** (saf C#, MIT, native bağımlılık yok) — tek yeni NuGet
  (PK-O2). Masaüstü doğrudan üretir (offline), web mevcut desenle küçük bir API ucundan PNG indirir.

## 5–7. Önerilen v1 kapsamı ve tarama sonrası davranış

**v1 = "tara → bul → git" + "etiket bas"** (PK-O1):
1. **Tarama = mevcut global arama.** Yeni arama düzeneği YAZILMAZ. Eklenen tek davranış (PK-O4): sonuçlar
   içinde **TAM eşleşme (kod birebir) TEK kayıtsa** panel yerine doğrudan o kayda gidilir/açılır (mevcut
   `OpenSearchHit`/`IDeepLinkTarget` altyapısı — kayıt-açma yolu YENİDEN yazılmaz). Birden çok eşleşme
   (ör. aynı metin hem malzeme kodu hem ekipman kodu) → bugünkü panel aynen. Ayrıca arama kutusuna
   **klavye kısayolu ile odak** (okuyucu tetiklemeden önce tek tuş) eklenir.
2. **QR etiketi:** Malzeme · Araç · Ekipman kartlarında "QR Etiketi" (PNG üret → kaydet/yazdır; içerik = kod).
   Zimmet ayrıca etiket almaz (zimmet varlıkları zaten bu üç kaynağın koduyla taranır). İş emri kâğıda QR —
   v1 dışı (gelecek).
3. **Tarama SALT-OKUNURDUR:** stok düşme/zimmet verme/İE durumu/bakım başlatma gibi **hiçbir işlem kısayolu
   YOK** (madde 4 — roadmap'te de dayanağı yok). Kayda gidildikten sonra her işlem MEVCUT ekran ve MEVCUT
   servis kurallarıyla yapılır; hiçbir iş kuralı taramayla atlanamaz.

## 8–10. Web / Desktop / Offline

- **Masaüstü (birincil):** okuyucu → arama kutusu → yerel SQLite'tan bulma — **tamamen ÇEVRİMDIŞI** (malzeme,
  araç, ekipman, İE, satın alma, talep, cari yerelde; Proje/Evrak zaten çevrimiçi-yalnız — ARA-01 kuralı
  aynen). QR etiketi yerelde üretilir — offline.
- **Web:** aynı kutu → `/api/search` (sunucu tarafında aynı kapılar). Etiket PNG'si eklemeli tek uçtan.
- Platform farkı yalnız K'daki mevcut farktır (masaüstü 4 ekranda kaydı da açar, diğerlerinde ekrana gider) —
  O bunu değiştirmez, İYİLEŞTİRMEZ de (kapsam şişirmesi olur; deep-link hedefi eklemek ayrı küçük iştir).

## 11. Yetki / BranchAccess / tenant

Tarama = arama → **ARA-01'in kanıtlı kapıları aynen**: yetkisiz modül HİÇ SORGULANMAZ, tenant her sorguda,
şube kapsamı süzülür, silinmiş kayıt bulunmaz → **QR/barkod bilinen bir kodu taramak yetkisiz kayda erişim
VERMEZ** (yapısal). Etiket üretimi = tek kaydın kimliğini görüntüleme → **kaynak modülün View yetkisi**
yeterli (kayıt zaten ekranda görünüyor). **Yeni `barcode`/`qr` yetki modülü GEREKMEZ ve ÖNERİLMEZ**
(deny-by-default ağacını büyütür, "aramada var ama taranamıyor" tutarsızlığı üretirdi).

## 12–13. Global Arama ve Excel Merkezi etkisi

- **K:** tek eklemeli davranış PK-O4 (tam-tek eşleşmede otomatik açılış); diğer tüm K davranışı birebir korunur.
  Kod alanları zaten arandığından ekstra kaynak/kolon eklenmez (PK-O3=B çıkarsa `materials.barcode` SubCol olur).
- **M:** DEĞİŞMEZ — exportlar kod alanlarını zaten içerir; import kapsamı 7 sette SABİT (PK-M3 kararı korunur);
  QR görseli Excel'e gömülmez.

## 14. Senkron etkisi

**YOK.** Taranan kimlikler (code/plate/no) zaten senkronlanan mevcut kolonlardır; yeni veri üretilmez
(QR görüntüsü türetilmiş çıktıdır, saklanmaz). Yeni tablo/kolon/sync satırı gerekmez; `BusinessSyncService`
listesine dokunulmaz. **SNK-13'e dokunulmaz** (O etkilemiyor — salt okuma).

## 15. Migration gereksinimi

**Önerilen v1'de MIGRATION YOK — şema 81'de kalır.** Tek migration ihtimali PK-O3=B (üretici EAN-13 barkodu):
`materials`'a nullable `barcode TEXT NULL` kolonu (ALTER!) gerektirir — mevcut satırlar dokunulmadan kalır ama
(a) canlı tabloya İLK ALTER'ımız olur, (b) eski 1.0.160 istemcilerin senkron uyumu (bilinmeyen kolonlu
snapshot/delta) ayrıca kanıtlanmalıdır, (c) yayın yapılmayacağından şema borcu birikir. **Bu yüzden v1 DIŞI
önerilir**; ihtiyaç doğarsa ayrı, kanıtlı bir iş olarak açılır.

## 16. Performans

Tarama = mevcut arama sorguları (firma+silinmemiş daraltma + bellek-içi eşleşme). Canlı hacim (2492 malzeme,
160 araç, 64 personel…) için ARA-01'de kanıtlı; tam-eşleşme kontrolü ek maliyet getirmez. QR üretimi tek
kayıtlık, anlık, kütüphane saf C#. **FTS/ES/Redis/cache/servis YOK** — mevcut mimari + minimum ekleme.

## 17. Güvenlik riskleri

- Tarama yoluyla yetki bypass'ı → yapısal olarak kapalı (madde 11). QR içeriği düz kod metni olduğundan
  "kötü niyetli QR" en fazla arama sorgusu metnidir — arama parametrelidir, SQL'e girmez (mevcut kanıt).
- Tarama üzerinden yazma kapısı YOK (v1 tanımı gereği) → canlı kayıt riski sıfır.
- Etiket PNG'si kod metninden başka veri İÇERMEZ (ad/fiyat/stok QR'a gömülmez — bilgi sızıntısı olmaz).

## 18. Uygulama büyüklüğü (tahmin)

KÜÇÜK-ORTA: QRCoder paketi + Infrastructure'da ~1 küçük yardımcı sınıf + API'ye 1 eklemeli uç + iki
platformda arama kutusuna odak kısayolu ve tam-tek-eşleşme davranışı + 3 karta "QR Etiketi" düğmesi + testler.
Dokunulan mevcut davranış: yalnız arama Enter akışındaki PK-O4 eklemesi. Yeniden yazım riski: yok.

## 19. Test planı (uygulama turunda)

Kod bulma (malzeme/araç/ekipman/İE) · bilinmeyen kod → sonuç yok, hata yok · **yetkisiz kaynak taramada
sorgulanmaz** · BranchAccess · tenant · silinmiş kayıt bulunmaz · **aynı kod iki kaynakta → otomatik açılış
YOK, panel** · tam-tek eşleşme → doğru kayıt · elle giriş = okuyucu girişi (aynı yol — tek test) · QR üretimi:
PNG geçerli + içerik = kod (decode ile doğrulanır — kütüphane içinde okuma yoksa içerik üretim girdisiyle
doğrulanır) · **bit-bit: tarama+etiket üretimi kaynak satırları DEĞİŞTİRMEZ** (salt-okunur kanıtı, EXL8
deseni) · K regresyonu (AramaTests) + parite kilitleri · senkron değişmedi (dokunulmadığı için hedefli koşu).

## 20–21. Bilinen sınırlar / geleceğe bırakılanlar

- v1 sınırları: kamera taraması yok · üretici barkodu alanı yok (PK-O3) · toplu etiket basımı yok (kayıt
  başına PNG; toplu sayfa düzeni ayrı iş) · deep-link hedefleri mevcut 4 ekranla sınırlı (diğerlerinde ekrana
  gidilir) · stok hareketleri taranmaz (PK-K1 korunur).
- Gelecek (bugünkü tasarımı ETKİLEMEZ, eklemeli gelir): iş emri çıktısına QR · toplu etiket sayfası ·
  deep-link hedefi genişletme · üretici barkodu kolonu · mobil kamera (N — Mobil fazının konusu) ·
  sayım/otomasyon senaryoları (bilinçli kapsam dışı — madde 15).

---

## PK-O KARAR SORULARI

### PK-O1 — v1 kapsamı
- **Mevcut:** barkod/QR yok; arama kutusu okuyucuyla fiilen çalışıyor ama tıklama istiyor, etiket üretimi yok.
- **ÖNERİLEN (A):** "tara→bul→git" (mevcut global arama üzerinden; USB okuyucu + elle giriş; kutuya odak
  kısayolu) **+ QR etiket üretimi** (Malzeme·Araç·Ekipman; içerik = kayıt kodu düz metin). Migration YOK,
  yeni yetki YOK, senkron dokunuşu YOK. Maliyet: küçük-orta · canlı veri riski: sıfır (salt-okunur).
- **Alternatif (B):** yalnız tarama (etiket üretimi yok) — sıfır bağımlılık ama sahadaki varlıklara etiket
  basılamaz, özelliğin değeri yarım kalır.
- **Alternatif (C):** A + kamera taraması — ağır, parite bozar, birincil platforma değer katmaz; önerilmez.

### PK-O2 — QR üretim kütüphanesi (PK-O1=A ise)
- **ÖNERİLEN (A):** **QRCoder** NuGet (saf yönetilen C#, MIT, native bağımlılık yok) Infrastructure'a;
  masaüstü doğrudan (offline), web eklemeli tek API ucundan PNG. Tek yeni bağımlılık.
- **Alternatif (B):** kütüphane eklenmez → etiket üretimi düşer (fiilen PK-O1=B).

### PK-O3 — Üretici (ambalaj) barkodu — EAN-13 — saklansın mı?
- **Mevcut:** hiçbir tabloda yok; kendi etiketlerimizle tarama bunsuz da tam çalışır.
- **ÖNERİLEN (A): v1 DIŞI** — migration'sız kalınır; ihtiyaç kanıtlanırsa ayrı iş.
- **Alternatif (B):** `materials`'a nullable `barcode` kolonu (Migration082, canlı tabloya İLK ALTER) +
  aramada ek kolon. Maliyet: orta; eski istemci senkron uyumu ayrıca kanıt ister; yayın yapılmadığından
  şema borcu birikir. Açık saha ihtiyacı yoksa önerilmez.

### PK-O4 — Tam-tek eşleşmede otomatik açılış (taramayı tek adım yapan davranış)
- **Mevcut:** Enter → panel → tıklama (2 adım).
- **ÖNERİLEN (A):** arama sonucu **TAM eşleşme ve tüm kaynaklarda TEK kayıtsa** panel atlanır, kayda
  gidilir/açılır (mevcut açılış altyapısıyla); diğer her durumda bugünkü panel AYNEN. K'ya tek eklemeli
  davranış; geri alınabilir.
- **Alternatif (B):** dokunma — tarama 2 adım kalır (okuyucunun hız avantajı kısmen kaybolur).

**Karar gerektirmeyenler (raporlanır, sorulmaz):** yeni yetki modülü YOK (madde 11) · tarama üzerinden yazma
kısayolu YOK (madde 4; roadmap dayanağı da yok) · yeni kimlik alanı YOK (madde 5) · FTS/cache/servis YOK ·
SNK-13 ve senkron dokunulmaz · M import kapsamı sabit kalır.
