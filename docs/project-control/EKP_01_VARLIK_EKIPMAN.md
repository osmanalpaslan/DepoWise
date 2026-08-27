# EKP-01 — Varlık / Ekipman Yönetimi · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-166** · Roadmap: FAZ 1 / SIRA 3 (MASTER_ROADMAP §1)
> Karar değişirse silinmez, tarihle güncellenir.

## 1. Ürün kararları (kullanıcı, 2026-08-28)

| Karar | İçerik | Gerekçe |
|---|---|---|
| **PK-E1** | **AYRI `equipment` tablosu** — vehicles genelleştirilMEDİ | "vehicle" 93 dosyada geçiyor; her araç sorgusuna tür filtresi eklemek canlı sistemde sessiz-bozulma (tek unutulan filtre = ekipman araç raporlarına sızar) riskiydi. Ayrı tabloda bu risk yapısal olarak yok. |
| **PK-E2** | Bakım entegrasyonu İLK SÜRÜMDE YOK | Sonraki küçük iş (F İş Emri'nden önce): mevcut bakım tablolarına eklemeli boş `equipment_id` ile TEK bakım sistemi iki türe hizmet edecek — kopya sistem kurulmayacak. |
| **PK-E3** | Yakıt ve muayene ekipmana UYGULANMAZ | Yakıt tüketen büyük makineler bugünkü gibi Araçlar'dan saatli takip edilir (mevcut fiilî kullanım); muayene/sigorta araçlara özgüdür. |

**Kritik sonuç:** hiçbir araç kaydı taşınmadı/etiketlenmedi; Araçlar'daki iş makineleri orada kalır ve
aynen çalışır (EKP12 testi araç şemalarında ekipman izi olmadığını kilitler).

## 2. Veri modeli — Migration075 (şema v75)

- **`equipment_types`**: serbest tanım (vehicle_types deseni) → tür genişletme migration'sız.
  Tanımlar ekranına "Ekipman — Türler" bölümü eklendi (web+masaüstü); Çöp Kutusu etiketi var.
- **`equipment`**: code (firma içinde benzersiz — anlaşılır hata ile) · name · type_id · status
  (`active|passive|maintenance`, araçla aynı küme) · status_note · branch_id (şube/şantiye/saha bağı) ·
  serial_no · location · description · standart damgalar. Edinim/bakım alanları İCAT EDİLMEDİ
  (gerekirse eklemeli kolon).
- Yalnız CREATE — **EKP10** (v74 + canlı benzeri araç+yakıt verisi + yalnız 75 → bit-bit aynı) ve
  **EKP11** (statik yalnız-CREATE) kanıtlı. Rollback: iki DROP + schema_migrations satırı.
  ⚠️ Canlıya UYGULANMADI (deploy ile koşar → yayın ayrı onay).

## 3. Mimari

- **Yerel + senkronlu** (araç deseni — projeler/evrakın aksine): masaüstü çevrimdışı ekipman açar/görür,
  senkron taşır. `BusinessSyncService.Tables`: `equipment_types` lookup bloğunda (LWW),
  `equipment` vehicles'tan sonra; `TableModule`: equipment_types→definitions, equipment→equipment
  (push yetki kapısı). **EKP9**: uçtan uca taşıma + idempotent tekrar + firma karışmazlığı kanıtlı.
- **Yetki:** yeni **`equipment`** modülü Yetki Ağacı'nda ("Ekipman") — deny-by-default; kullanılacak
  rollere AÇILMASI gerekir. **Şube kapsamı:** `BranchAccess` — kapsam dışı şubenin ekipmanı görünmez,
  düzenlenemez, ona bağlanamaz (EKP5); şubesiz ekipman gizlenmez. Tenant: EKP6.
- **Silme:** soft delete + audit + Çöp Kutusu (EKP7). Ekran logu: `ScreenAuditMap["equipment"]`.
- **Evrak bağı hazır:** Evrak ekranında "Ekipman" bağlı kayıt türü olarak seçilebilir
  (DocumentService haritasına eklendi). Zimmet/İş Emri/Barkod ileride entity_type+entity_id ile bağlanır.

## 4. Ekranlar / API

| Katman | Ne |
|---|---|
| API | `GET/POST /api/equipment` · `PUT/DELETE /{id}` · `GET /api/equipment/export` (Excel — export modül yetkisi) · tür tanımları mevcut `/api/lookups/equipment_types` |
| Web | `Equipment.razor` (/equipment): form + liste + durum filtresi + arama + satır-içi tür ekleme ("+") + **Excel'e Aktar** (filtrelenmiş TÜM sonuç, tooltip'li — liste kuralı 2) |
| Masaüstü | `EquipmentView(.axaml)` + `EquipmentViewModel`: YEREL çalışır (çevrimdışı dahil) + Excel'e Aktar (yerel üretim) |
| Menü | Yeni ana menü **"Ekipman"** (⚙️, Operasyon bölümü, Araçlar'ın altında) — iki platform. Parite 50/57. |

## 5. Testler

`EkipmanTests` **12/12**: CRUD+kilit (EKP1) · kod benzersizliği (EKP2) · fail-safe durum (EKP3) ·
deny-by-default (EKP4) · **şube kapsamı (EKP5)** · **tenant (EKP6)** · soft delete+trash+log (EKP7) ·
**senkron sıra+yetki kapısı (EKP8)** · **uçtan uca senkron+idempotent (EKP9)** ·
**migration canlı-veri kanıtı (EKP10-11)** · **araç şemasına sıfır dokunuş (EKP12)**.
Regresyon: araç/yakıt/bakım/çöp/log **280/281** (1 atlanan=PG) · parite+senkron 63/63 · üç Release 0 hata.

## 6. Bilinen sınırlar / elle test

- Bakım/yakıt/muayene/zimmet/iş emri/barkod bağları YOK (PK-E2/E3 + roadmap sırası) — bakım genişletmesi
  F'den önce ayrı küçük iş.
- Grid kolon-katalog deseni (ADR-087 kolon filtreleri/sayfalama) KURULMADI — basit liste + arama + durum
  filtresi + Excel; firma başına ekipman sayısı büyürse ADR-087'ye taşınır (Projeler/Evrak da aynı sınıf).
- **Elle doğrulanacak:** iki platformda ekran açılışı ve örnek kayıt akışı (giriş şifresi gerektiğinden
  gözle doğrulanamadı; mimari testler yeşil). Yeni **"Ekipman" yetkisi kapalı gelir** — rollere açılmalı.
- PostgreSQL koşusu yerelde atlandı (PG yok); sözdizimi ortak (Migration066 emsali).
- Kullanıcının söz ettiği ekran görüntüleri bu tura ULAŞMADI — tasarım dili koddan (Branches/Projects
  desenleri) alındı; görüntüler gelirse UX farkları ayrıca kontrol edilir.

## 7. Canlıya alınma durumu

⛔ **Yayınlanmadı.** Yayın turunda: Migration073+074+075 deploy ile koşar; öncesi/sonrası canlı
salt-okunur sayım alınacak.

## 8. Sonraki roadmap maddesi

**B — Zimmet** (FAZ 2 / SIRA 4). Ön koşulu (E) tamam; başlamadan ürün soruları: malzeme zimmeti stoktan
düşer mi, zimmet devri nasıl olur (MASTER_ROADMAP §1 notları).
