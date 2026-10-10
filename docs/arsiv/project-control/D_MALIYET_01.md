# MLY-01 — Maliyet Merkezi · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-168** · Roadmap: FAZ 2 / SIRA 5 (MASTER_ROADMAP §1)

## 1. Model kararları (kullanıcı talimatından türetildi — ürün sorusu gerekmedi)

Tek işlem = **TEK** maliyet merkezi · yüzde/çoklu dağıtım YOK · geçmiş kayıtlara backfill YOK (yalnız
yeni işlemler bağ taşır; dış-bağ modeli sayesinde istenirse eski kayda da SONRADAN merkez atanabilir —
kaydın kendisi değişmeden) · şube/proje/araç boyutları TEKRARLANMADI (üçlü gerçeklik yasak; maliyet
merkezi bunların YANINA gelen yeni boyuttur: departman, iş, özel merkez).

## 2. Veri modeli — Migration077 (şema v77) — **mevcut tablolara ALTER DAHİ YOK**

- **`cost_centers`**: code · name · status(active|passive) · description · standart damgalar.
- **`cost_center_links`**: kayıt→merkez DIŞ bağı (entity_type: stock_document · fuel_depot_entry ·
  fuel_distribution · vehicle_maintenance) — **UNIQUE(entity) = tek-merkez kuralı şemada**.
- **Neden bağ tablosu (kolon değil):** stok belge zinciri 5 katmanlı; kolon eklemek canlı tablolara
  ALTER + tüm imza zinciri demekti. Dış bağ, mevcut tablolara ve servis zincirine SIFIR dokunuşla aynı
  bilgiyi taşır (talimatın §5'i bu modeli açıkça davet ediyordu). Bağ, kayıt oluştuktan hemen sonra
  API/VM katmanında yazılır — maliyet bağı bilgilendiricidir, stok/para bütünlüğünü etkilemez;
  yazılamazsa kayıt "merkezsiz" kalır ve sonradan atanabilir.
- MLY10 (bit-bit) + MLY11 (statik yalnız-CREATE) kanıtlı. Rollback: iki DROP + schema_migrations.
  ⚠️ Canlıya UYGULANMADI.

## 3. Kapsam / davranış

- **Tanım + özet TEK ekranda** ("Maliyet Merkezleri" — Ön Muhasebe altında alt menü, iki platform).
  Özet Sorgula ile çalışır (ağır rapor kuralı) + Excel (liste kuralı 2, export yetkisi).
- **İşlem formlarında opsiyonel seçim:** stok ÇIKIŞI (şube içi) · yakıt depo girişi · yakıt dağıtımı ·
  bakım kaydı — web + masaüstü. Alan yalnız cost_centers **Edit** yetkisi olana görünür.
- **Özet mevcut hesapları DEĞİŞTİRMEZ** (yalnız okur): Malzeme Giriş/Çıkışı (satır qty×fiyat; satır
  fiyatı boşsa malzeme kartı fiyatına düşer — mevcut stok-değeri mantığıyla uyumlu) · Yakıt (litre×fiyat)
  · Bakım Malzemesi (Araç Raporu ile aynı kaynak). Para birimleri AYRI satır (kur çevrimi icat edilmedi);
  toplama C# decimal (Money kuralı). MLY8: merkezsiz akışlar bit değişmeden aynen çalışır.
- **Yetki:** yeni `cost_centers` modülü (deny-by-default, yayında rollere AÇILMALI). **Kapsam:** bağ
  kurma ve özet, kaynak kaydın şubesi üzerinden BranchAccess'ten geçer (MLY6 — yan kapı değil);
  yakıt/bakım tablolarında şube kolonu olmadığından oralarda "şubesiz kayıt gizlenmez" ilkesi geçerli.
- **Senkron:** `cost_centers` (lookup/LWW) + `cost_center_links` FK sıralı listede; push kapısı
  cost_centers modülü (MLY9). Soft delete + Çöp Kutusu + audit + ekran logu + düzenleme kilidi tam.

## 4. Testler

`MaliyetMerkeziTests` **11/11**: CRUD/kilit/pasif-seçenek (MLY1) · yetki+tenant (MLY2) · trash (MLY3) ·
**bağ kaynak kaydı bit-bit değiştirmez + tek-merkez upsert (MLY4)** · bağ tenant/tür koruması (MLY5) ·
**kapsam yan-kapı değil (MLY6)** · **özet doğru toplar + tarih aralığı (MLY7)** · **merkezsiz akış
regresyonu (MLY8)** · senkron (MLY9) · **migration kanıtı (MLY10-11)**.
Regresyon: stok/yakıt/bakım/rapor/senkron/zimmet **794 test → 783 geçti / 10 atlanan(PG) / 1 düzeltildi**
(ZMT7 kendi testimin sıralama varsayımı deterministik yapıldı — ürün hatası değil) → tekrar 16/16.
Üç Release derleme 0 hata. Parite 52/59.

## 5. Bilinen sınırlar

- Kayıt sonrası bağ ayrı adımdır (atomik değil — bilinçli, §2 gerekçesi); bağ hatasında kullanıcıya
  "kayıt alındı; merkez bağlanamadı" denir.
- Stok GİRİŞ ucu belge id döndürmediğinden ilk sürümde merkez bağı yalnız ÇIKIŞ+transfer değil,
  ÇIKIŞ (şube içi) belgesinde; transfer/giriş belgesine bağ ihtiyacı doğarsa uç genişletilir (not).
- "Diğer giderler" (kasa/banka gider hareketi) ilk sürümde bağlanamaz — Finans dokunuşu bilinçli
  ertelendi; ihtiyaç doğarsa entity haritasına satır eklenir.
- Raporlar ekranına ayrı "Maliyet Merkezi Raporu" eklenmedi (özet, merkez ekranının içinde — rapor
  dispatch'e dokunulmadı). Elle test: iki platformda ekran + form alanları + özet.

## 6. Canlıya alınma durumu

✅ **YAYINLANDI — 2026-08-28 toplu yayın** (kullanıcı onayı; Migration073..081 canlıda birlikte uygulandı).
API **v174** · Web **v199** · Masaüstü **1.0.160** (SHA-256 EA688F2F…59CAE2). Kanıtlar:
[TOPLU_YAYIN_2026-08-28.md](TOPLU_YAYIN_2026-08-28.md) — deploy öncesi/sonrası canlı salt-okunur sayım/karma
karşılaştırması: mevcut TÜM tabloların satır içerikleri BİT-BİT AYNI; yeni tablolar BOŞ; şema 72→81.
Yeni yetkiler hiçbir role otomatik AÇILMADI — rollere kontrollü açılacak durumda.
## 7. Sonraki roadmap maddesi

**P — Satın Alma** (FAZ 2 / SIRA 6). Ön koşullar (C, D) tamam; başlamadan ürün sorusu: satın alma kaç
aşamalı (teklif zorunlu mu, kim onaylar).
