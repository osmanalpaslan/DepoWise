# ARA-01 — Global Arama · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-174** · Roadmap: FAZ 4 / SIRA 11 (MASTER_ROADMAP §1)
> Analiz: [K_ARAMA_00_ANALIZ.md](K_ARAMA_00_ANALIZ.md) — PK-K1..K5 kullanıcı tarafından KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-K1 | Kayıt/kart nitelikli 15 kaynak: Malzeme · Araç · Personel · Ekipman · Şube/Şantiye · Cari · Tedarikçi · Maliyet Merkezi · İş Emri · Sipariş · Talep · Takvim · Duyuru · Proje · Evrak(metadata). Hareket defterleri HARİÇ. |
| PK-K2 | Yalnız KİMLİK alanları (kod/no/ad/başlık/plaka); açıklama/not aranmaz (ARA1). |
| PK-K3 | Sonuç → ilgili EKRANA gider; masaüstünde kayıt-açma altyapısı olan 4 ekranda (Malzeme, Araç, Bakım, Stok Hareketleri — `IDeepLinkTarget`) KAYIT da açılır (uyarılardaki mevcut davranış). |
| PK-K4 | Silinmişler ARANMAZ — yalnız Çöp Kutusu'nda (ARA7). |
| PK-K5 | Yeni `global_search` yetkisi YOK: her kaynak bloğu KENDİ modülünün View kapısıyla sarılı — yetkisiz kategori HİÇ SORGULANMAZ (ARA4); BranchAccess süzgeci (ARA5); tenant (ARA6). |

## 2. Mimari — birleşik, türetilmiş SearchService (paralel gerçeklik YOK)

`SearchService.Search(s, q, onlySources?)`: 12 kaynak dar SQL (yalnız firma+silinmemiş daraltır, kimlik
kolonları) + 3 ÖZEL KURALLI kaynak KENDİ servisinden (Duyuru — okuma-herkese+pencere kuralı içeride,
ARA8; Proje — kapsam içeride, ARA10; Evrak — iki kapı+kapsam içeride, yalnız metadata, ARA9).
**Süzme BELLEK İÇİNDE** (SQL LIKE değil): SQLite↔PostgreSQL birebir aynı sonuç + Türkçe büyük/küçük
doğru; firma başına hacim küçük — mevcut modül aramalarının çoğu da aynı desende.
Sıralama: kategori gruplu; kategori içinde aramayla BAŞLAYAN önce (ARA1); kategori başına **LIMIT 5** +
HasMore→"daha fazlası için ekrana git" (ARA3). Min 2 karakter (ARA2). Sonuçlar SALT-OKUNUR (ARA11 bit-bit).
**FTS/fuzzy/harici motor/indeks/cache KURULMADI** — ihtiyaç doğarsa servisin arkasına eklemeli girer.

## 3. UI / API

- **Üst bar** iki platformda (yeni menü/ekran YOK → AppScreens ve parite sayıları DEĞİŞMEDİ; yetki
  ağacına modül EKLENMEDİ): 🔍 kutu, **Enter ile arar** (anlık öneri yok) → açılır panel: kategori
  başlıkları + satırlar + "daha fazlası" + kapat; sayfa değişince/dışına tıklanınca kapanır.
- Web: `MainLayout` + `GET /api/search?q=&sources=`; masaüstü: `MainWindow` Popup + `ShellViewModel`
  komutları; tıkla → `NavigateTo(nav, id)` (web'de bildirimlerle aynı rota dönüşümü `_`→`-`, `:`→`/`).
- Tedarikçi kartları Tanımlar ekranında yönetildiğinden kapı+hedef `definitions`.

## 4. Offline / senkron / parite

Masaüstü: yerel kaynaklar ÇEVRİMDIŞI aranır (aynı SearchService, `documents=null` → Evrak sessiz atlanır);
**Proje+Evrak** çevrimiçiyse `/api/search?...&sources=projects,documents` ile eklenir
(`OrgServerClient.SearchRemoteAsync`), çevrimdışıysa panelde "çevrimiçi gerekli" notu. İki platform AYNI
servisi çağırır → sonuç kümesi birebir (tek bilinçli fark: masaüstü kayıt-açma derinliği, PK-K3).
Senkron mimarisine SIFIR dokunuş.

## 5. Testler

`AramaTests` **12/12**: eşleşme+başlayan-önce (ARA1) · min uzunluk (ARA2) · LIMIT+HasMore (ARA3) ·
**yan kapı: yetkisiz kategori hiç dönmez (ARA4)** · **kapsam (ARA5)** · **tenant (ARA6)** ·
**silinmiş aranmaz (ARA7)** · duyuru herkese+pencere (ARA8) · evrak metadata+offline+onlySources (ARA9) ·
proje (ARA10) · **salt-okunur bit-bit (ARA11)** · çoklu kategori (ARA12).
Hedefli regresyon (arama-parametreli grid'ler, bildirim/duyuru/takvim/iş emri/evrak/proje, yetki,
menü-bölüm, parite): **692 geçti / 0 başarısız / 3 atlanan** (atlananlar PostgreSQL gerektiren testler —
yerel ortamda PG yok). Üç Release build **0 hata**.

## 6. Canlı veri güvenliği

Canlıya yazma YOK · mevcut kayıt değişimi YOK (ARA11 bit-bit; tamamen salt-okunur türetme) ·
fiziksel silme YOK · **MIGRATION YOK — şema 81'de kaldı** (indeks de eklenmedi) · deploy YOK.

## 7. Bilinen sınırlar / elle test

Anlık öneri/autocomplete · fuzzy · evrak İÇERİK araması (OCR/metin çıkarma — ileride ayrı iş) ·
hareket defterleri · web'de kayıt-açma derinliği bilinçli KAPSAM DIŞI. İki platformda gözle doğrulama
size kaldı (üst bar kutusu + panel; dar pencerede sığma). Arama kutusu herkeste görünür — içerik zaten
kişinin yetkisinden süzülür.

## 8. Canlıya alınma durumu

✅ **YAYINLANDI — 2026-08-28 toplu yayın** (kullanıcı onayı; Migration073..081 canlıda birlikte uygulandı).
API **v174** · Web **v199** · Masaüstü **1.0.160** (SHA-256 EA688F2F…59CAE2). Kanıtlar:
[TOPLU_YAYIN_2026-08-28.md](TOPLU_YAYIN_2026-08-28.md) — deploy öncesi/sonrası canlı salt-okunur sayım/karma
karşılaştırması: mevcut TÜM tabloların satır içerikleri BİT-BİT AYNI; yeni tablolar BOŞ; şema 72→81.
Yeni yetkiler hiçbir role otomatik AÇILMADI — rollere kontrollü açılacak durumda.
## 9. Sonraki roadmap işi

**L — Dashboard** (FAZ 4/SIRA 12, mevcut ekran dönüşümü). 7b Bakım-Ekipman genişletmesi hâlâ serbest sırada.
