# EVR-01 — Evrak / Belge Yönetimi · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-28** · Karar: **ADR-165** · Roadmap: FAZ 1 / SIRA 2 (MASTER_ROADMAP §1)
> Gelecekteki oturumların "bu özellikte ne yaptık?" hafızası. Karar değişirse silinmez, tarihle güncellenir.

## 1. Amaç ve kapsam

Merkezi **"Evrak / Belgeler"** ekranı (web + masaüstü): belge yükleme (PDF, JPG/PNG, DOCX/XLSX/DOC/XLS),
listeleme, arama, kayıt türüne göre filtre, indirme, bilgi düzenleme, yetkili silme. Belgeler mevcut
kayıtlara bağlanır: **Malzeme · Araç · Personel · Şube/Şantiye · Proje · Genel (firma)**.
Meta: başlık (zorunlu) · tür (serbest metin) · geçerlilik başlangıç/bitiş · açıklama · dosya adı/boyut ·
yüklenme zamanı · yükleyen.

## 2. Kullanılan mevcut altyapı — İKİNCİ SİSTEM KURULMADI

- **`file_records` tablosu AYNEN yeniden kullanıldı**: belgeler `kind='document'`, fotoğraflar `kind='photo'`.
- **`LocalFileStorageProvider`** (fiziksel disk: sunucuda `/data/files`, path-traversal korumalı) ·
  `FileValidation.SafeFileName` · `AuditWriter` · `AppScreens` tek-kaynak katalog.
- Yeni doğrulama: `DocumentValidation` (FileValidation'ın kardeşi — onu DEĞİŞTİRMEZ): magic-byte
  (%PDF / PK-zip / OLE2 / JPEG / PNG) + uzantı-içerik tutarlılığı + **7 MB** sınırı (fotoğrafla AYNI sınır
  — ikinci boyut kuralı icat edilmedi).

## 3. Kritik analiz bulgusu — dosya ikilisi (binary) senkronu

**Kanıtlanan gerçek:** `file_records` `BusinessSyncService.Tables`'ta YOK; masaüstü fotoğrafları yalnız
kendi `%LOCALAPPDATA%` diskine yazar → **bugün hiçbir dosya içeriği masaüstü↔sunucu arasında taşınmıyor**
(fotoğraflar makine-yereldir; mevcut davranış).

**Çözüm (minimum, ikinci dosya sistemi YOK):** Belgeler **SUNUCU-OTORİTELİ** — şubeler/projeler deseni.
İki platform da aynı `/api/documents` uçlarını çağırır; içerik sunucu diskinde tek kopyadır, her makineden
erişilir. Masaüstü çevrimdışıyken evrak eklenemez/görüntülenemez (anlaşılır uyarı; yerele yazılmaz).
Senkron protokolüne DOKUNULMADI; fotoğraf davranışı DEĞİŞMEDİ.

## 4. Veri modeli / Migration

**Migration074_DocumentFields** (şema v74): `file_records`'a **yalnız eklemeli** kolonlar —
`title, doc_type, valid_from, valid_until, description, uploaded_by` (hepsi NULL) + `ix_file_company_kind`
indeksi. Mevcut satırlara sıfır dokunuş — **EVR11** (v73 + gerçek fotoğraf kaydı + yalnız 74 → eski
kolonlar bit-bit aynı, yeni kolonlar NULL) ve **EVR12** (statik: UPDATE/DELETE/DROP/INSERT yok) kanıtlı.
Rollback: kolonlar boşken hiçbir kod yolu bağlı değildir; `DELETE FROM schema_migrations WHERE version=74`.
⚠️ **Canlıya UYGULANMADI** — deploy anında koşar → yayın onayı = migration onayı.

## 5. Yetki modeli — İKİ KAPI (LOG-01 deseni)

1. **`files` modülü** ("Dosya / Fotoğraf" — yetki ağacında ZATEN vardı; yeni yetki satırı açılmadı):
   View=listeleme/indirme · Create=yükleme · Edit=bilgi düzenleme · Delete=silme.
2. **Bağlı kaydın modülü**: malzemeyi göremeyen malzemenin belgesini de göremez (listede sessiz filtre,
   doğrudan id ile istekte ret — EVR6). Şube belgesi `BranchAccess` kapsamından, proje belgesi projenin
   şantiye bağlarından geçer (EVR7). Tenant: her sorguda company_id (EVR8).
"Genel (Firma)" evrakı yalnız files kapısından geçer (kayda bağlı değildir).

**Yan düzeltme (EVR10'un yakaladığı gerçek hata):** `FileService.GetPhotos/DeletePhoto` tür (kind)
filtrelemiyordu → belge, fotoğraf galerisine sızar ve fotoğraf-silme ucundan iki kapı atlanarak
silinebilirdi. `kind='photo'` koşulu eklendi; bugüne dek tüm kayıtlar photo olduğundan davranış değişmedi.

## 6. API / Web / Masaüstü

| Katman | Ne |
|---|---|
| API | `GET /api/documents` (+entityType/entityId/search) · `POST /api/documents` (multipart) · `GET /api/documents/{id}/download` · `PUT /api/documents/{id}` (yalnız meta) · `DELETE` · `GET /api/documents/entity-types` |
| Web | `Documents.razor` (`/documents`): form + liste + filtre + `dwDownload` ile indirme. `ApiClient.UploadFilesAsync`'e opsiyonel `extraFields` eklendi (mevcut çağrılar değişmedi). |
| Masaüstü | `DocumentsView(.axaml)` + `DocumentsViewModel` (çevrimiçi; `OrgServerClient`'a belge metotları + multipart yükleme). Dosya seç/kaydet: `FilePickerService` (+`SaveAnyAsync`). Seçimde ERKEN doğrulama — sunucu yine de doğrular. |
| Menü | Yeni ana menü **"Evrak"** (📁, Kurumsal Yönetim bölümü) → "Evrak / Belgeler" (Both). Parite şeması 49/56'ya güncellendi. |
| Ekran logu | `ScreenAuditMap["files"] = file_record` → Ekran Araçları menüsünde "Kayıt Geçmişi" çalışır (web `LogModules` dahil). |

## 7. Alınan / ALINMAYAN ürün kararları

**Alınan (bariz teknik):** tür alanı serbest metin · geçerlilik tarihleri opsiyonel · silme = soft delete +
audit (fotoğrafla aynı desen) · boyut sınırı 7 MB (mevcut kural).
**Bilinçli ALINMAYAN (ürün kararı olarak açık):**
- **Belge türlerinin sabit listesi** yok (serbest metin) — istenirse Tanımlar'a bağlanır.
- **Sürümleme yok**: yeni içerik = yeni belge kaydı; "yeni sürüm" kavramı icat edilmedi.
- **Geçerlilik bitişi yaklaşınca uyarı** yok (Bildirim Merkezi I fazının doğal adayı).
- Belgeler Çöp Kutusu ekranında LİSTELENMEZ (fotoğraflarla aynı — TrashService'e file_records eklemek
  fotoğrafları da göstermeye başlardı; davranış değişikliği olurdu). Geri getirme gerekirse ürün kararı.

## 8. Testler

`EvrakTests` **12/12**: binary bütünlük (EVR1) · genel evrak (EVR2) · meta/içerik ayrımı (EVR3) ·
sahte-uzantı/izinsiz-tür/boyut reddi (EVR4) · deny-by-default (EVR5) · **iki kapı (EVR6)** ·
**şube kapsamı (EVR7)** · **tenant (EVR8)** · soft delete+audit (EVR9) · **foto-belge karışmazlığı (EVR10)** ·
**migration canlı-veri kanıtı (EVR11-12)**. Regresyon: foto/dosya/malzeme 160/160 · parite 19/19 ·
üç Release derleme 0 hata.

## 9. Bilinen sınırlar / elle test edilecekler

- Masaüstü Evrak ekranı ÇEVRİMİÇİ gerektirir (belgeler sunucuda; §3).
- Kayıt ekranlarının İÇİNE gömülü "Belgeler" paneli bu turda YOK — aynı işlev merkezi ekranın
  "kayıt türü + bağlı kayıt" filtresiyle sağlanıyor; gömme istenirse sonraki adım.
- Firma başına belge sayısı/toplam boyut kotası yok (yalnız 7 MB/dosya) — sunucu diski izlenmeli
  (geçmişte güncelleme paketleriyle dolmuştu); kota istenirse ürün kararı.
- Bağlanabilir türler ilk sürümde 6 (malzeme/araç/personel/şube/proje/genel); bakım-muayene-fatura
  bağları harita satırı ekleyerek genişletilebilir.
- **Elle doğrulanacak:** iki platformda ekranın açılışı, gerçek PDF yükleme-indirme, kilitli/yetkisiz
  kullanıcı görünümü (web'e giriş şifre gerektirdiğinden gözle doğrulanamadı; mimari testler yeşil).
- PostgreSQL migration koşusu yerelde atlandı (PG yok); ADD COLUMN sözdizimi Migration060 emsali.

## 10. Canlıya alınma durumu

✅ **YAYINLANDI — 2026-08-28 toplu yayın** (kullanıcı onayı; Migration073..081 canlıda birlikte uygulandı).
API **v174** · Web **v199** · Masaüstü **1.0.160** (SHA-256 EA688F2F…59CAE2). Kanıtlar:
[TOPLU_YAYIN_2026-08-28.md](TOPLU_YAYIN_2026-08-28.md) — deploy öncesi/sonrası canlı salt-okunur sayım/karma
karşılaştırması: mevcut TÜM tabloların satır içerikleri BİT-BİT AYNI; yeni tablolar BOŞ; şema 72→81.
Yeni yetkiler hiçbir role otomatik AÇILMADI — rollere kontrollü açılacak durumda.
## 11. Sonraki roadmap maddesi

**E — Varlık / Ekipman Yönetimi** (FAZ 1 / SIRA 3). Başlamadan önce MASTER_ROADMAP §1 + araç/varlık
model kararı (vehicles genelleştirme vs ayrı tablo — ürün kararı) netleştirilmeli.
