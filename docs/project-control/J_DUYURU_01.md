# DYR-01 — Duyuru · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-173** · Roadmap: FAZ 4 / SIRA 10 (MASTER_ROADMAP §1)
> Analiz: [J_DUYURU_00_ANALIZ.md](J_DUYURU_00_ANALIZ.md) — PK-J1..J5 kullanıcı tarafından KESİNLEŞTİRİLDİ ve AYNEN uygulandı.

## 1. Uygulanan ürün kararları

| Karar | Uygulama |
|---|---|
| PK-J1 | OKUMA HERKESE: yeni `AppModules.IsPublicRead` kavramı — `Can(View)` herkese true (Rol Yetki Kontrol kapatması yine geçerli); YAZMA (Create/Edit/Delete) `announcements` yetkisiyle, **kapalı gelir**. Menüde iki platformda herkese görünür. Yönetici-dışı, isteste de aktif-dışı duyuruyu GÖREMEZ (fail-closed — DYR2). |
| PK-J2 | Opsiyonel TEK şube hedefi: boş=firma geneli; dolu=yalnız o şube kapsamındakiler (ekran + bildirim — yan kapı yok, DYR3); kapsam dışına duyuru AÇILAMAZ. |
| PK-J3 | Opsiyonel yayın penceresi: boşsa hemen+süresiz; **aktiflik TÜRETİLİR** (durum alanı yok) — pencere dışına çıkan duyuru ekrandan (yönetici hariç) ve bildirimden kendiliğinden düşer (DYR1/DYR5). Tarihler PLAN anlamında (ADR-162 kapısına girmez). |
| PK-J4 | Gösterim: yalnız Bildirim Merkezi (çan+sayaç+Uyarılar "Duyuru" kategorisi) + "Duyurular" ekranı. Ana ekran paneli YOK. |
| PK-J5 | Önem: normal \| important — "Önemli" kritik/kırmızı rozet (bildirimde IsCritical — DYR5). |

## 2. Veri modeli — Migration081 (şema v81, yalnız CREATE)

`announcements(id, company_id, branch_id?, title, body?, importance, publish_start?, publish_end?,
created_by, created_at/updated_at/version/is_deleted)` + 1 indeks. Mevcut tablolara **ALTER dahi yok**;
OKUNDU için tablo AÇILMADI — mevcut `alert_reads` kullanılır (DYR9 bit-bit kanıtı alert_reads'i de içerir).
⚠️ Canlıya UYGULANMADI. Rollback: tek DROP + schema_migrations satırı.

## 3. Mimari / entegrasyonlar

- **Bildirim (BLD-01 aynen):** aktif duyuru `GetSummary`'ye kalem olarak düşer (`AlertKind.Announcement` —
  SONA eklendi); **imza=version** (`DashboardAlert.SignatureOverride` — eklemeli alan, override yoksa
  davranış eskisiyle birebir) → duyuru DÜZENLENİNCE herkes için yeniden okunmamış olur (DYR6).
  Paralel okundu/bildirim sistemi KURULMADI.
- **Yetki altyapı dokunuşu (eklemeli):** `AccessControl.Can`'e IsPublicRead-View satırı; devretme
  TAVANINA girmez (herkese açık okuma "verilmiş yetki" değildir — PermissionGrantCeilingTests bu yeni
  kuralı açıkça kilitler); yetki özetinde "Duyurular (herkese açık)" görünür — bilinçli.
- **Ekranlar:** tek "Duyurular" ekranı iki platformda (web `/announcements` + masaüstü
  `AnnouncementsView`): yetkiliye form (başlık·metin·önem·hedef şube·pencere) + tüm liste (durum
  etiketli); herkese aktif duyuru kartları. Excel liste kuralı 2. Menü grubu KURUMSAL blokta
  (MenuSectionTests S01: grup sırası section bloklarıyla bitişik olmalı — Takvim'in yanına konamaz).
- **Silme:** soft delete + Çöp Kutusu (`announcements`→title); fiziksel silme yok (DYR7).
- **Senkron/offline:** `announcements` BusinessSync'te → masaüstü ÇEVRİMDIŞI okur/yazar; uçtan uca +
  tekrar-kopyasızlık + silme taşınması kanıtlı (DYR8). Bildirim kalemi senkronlanmaz (türetilmiş).
- Kapsam dışı (bilinçli): yorum/onay · kişi hedefleme · dosya eki · "okudum onayı" · zengin metin ·
  ana ekran paneli (L fazında değerlendirilebilir) · e-posta/push.

## 4. Testler

`DuyuruTests` **12/12**: CRUD+pencere+kilit (DYR1) · **okuma herkese/yazma kapalı+fail-closed (DYR2)** ·
**şube hedefi ekran+bildirim izolasyonu (DYR3)** · **tenant (DYR4)** · bildirim entegrasyonu+önem
(DYR5) · **okundu-imza: düzenleme yeniden okunmamış yapar (DYR6)** · soft delete+Çöp Kutusu (DYR7) ·
**senkron uçtan uca idempotent+silme (DYR8)** · **Migration081 bit-bit, alert_reads dahil (DYR9)** ·
statik CREATE-only (DYR10) · kaynaklar bit-bit (DYR11) · Excel (DYR12).
Hedefli regresyon (bildirim/uyarı-okundu/takvim/iş emri/YETKİ-tavan-sıfırlama/menü-bölüm/parite/çöp
kutusu/senkron/rapor): **1034 geçti / 0 başarısız / 2 atlanan** (atlananlar PostgreSQL gerektiren
testler — yerel ortamda PG yok). İlk koşuda 5 test kırıldı ve KÖK NEDENLE düzeltildi (gizlenmedi):
menü grubu section bloğuna taşındı; tavan/sıfırlama/özet testleri yeni PK-J1 kuralını (okuma herkese,
yazma kapalı, tavana girmez) AÇIKÇA kilitleyecek şekilde güncellendi. Parite 56 ekran / 63 bağlantı.
Üç Release build **0 hata**.

## 5. Canlı veri güvenliği

Canlıya yazma YOK · mevcut kayıt değişimi YOK (DYR9/DYR11 bit-bit) · fiziksel silme YOK ·
production migration/deploy YOK. Migration081 yalnız CREATE.

## 6. Bilinen sınırlar / elle test

Yukarıdaki kapsam-dışı liste. **"Duyurular" YAZMA yetkisi kapalı gelir** — duyuru açacak rollere
verilmelidir; okuma için hiçbir şey gerekmez. Okundu işaretleri cihaz-yerel (PK-I4 ile tutarlı).
İki platformda gözle doğrulama size kaldı (Duyurular ekranı + Uyarılar'daki "Duyuru" kategorisi + çan).

## 7. Canlıya alınma durumu

✅ **YAYINLANDI — 2026-08-28 toplu yayın** (kullanıcı onayı; Migration073..081 canlıda birlikte uygulandı).
API **v174** · Web **v199** · Masaüstü **1.0.160** (SHA-256 EA688F2F…59CAE2). Kanıtlar:
[TOPLU_YAYIN_2026-08-28.md](TOPLU_YAYIN_2026-08-28.md) — deploy öncesi/sonrası canlı salt-okunur sayım/karma
karşılaştırması: mevcut TÜM tabloların satır içerikleri BİT-BİT AYNI; yeni tablolar BOŞ; şema 72→81.
Yeni yetkiler hiçbir role otomatik AÇILMADI — rollere kontrollü açılacak durumda.
## 8. Sonraki roadmap işi

**K — Global Arama** (FAZ 4/SIRA 11). 7b Bakım-Ekipman genişletmesi hâlâ serbest sırada.
