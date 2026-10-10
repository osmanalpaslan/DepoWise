# PRJ-01 — Proje / Şantiye Yönetimi (+ Saha) · KALICI KONTROL BELGESİ

> Oluşturma: **2026-08-27** · Karar: **ADR-164** · Roadmap: FAZ 1 / SIRA 1 (MASTER_ROADMAP §2)
> Bu belge, gelecekteki oturumların "bu özellikte ne yaptık?" hafızasıdır. Karar değişirse eski karar
> silinmez; tarihle güncellenir.

## 1. Ürün kararları (kullanıcı, 2026-08-27)

| Karar | İçerik |
|---|---|
| **PK-C1** | "Şimdilik tek, ileride çok": veri modeli bir projenin BİRDEN ÇOK şantiyeye yayılmasını bugünden taşır; ilk sürüm arayüzü TEK şantiye bağlar. Tek→çok geçişi yalnız UI işidir, migration GEREKTİRMEZ (PRJ5 testi kilitler). |
| **PK-C2** | Saha = `branches.kind` üçüncü değeri **`field`** (görünen adı "Saha"). AYRI tablo/ikinci kapsam sistemi YOK; mevcut `parent_id` hiyerarşisi kullanılır (Şube → Şantiye → Saha kurulabilir; hiyerarşi ZORLANMAZ — mevcut sistemde de üst seçimi serbesttir). |
| **PK-C3** | Proje kartı alanları: ad (zorunlu) · durum (Aktif/Beklemede/Tamamlandı) · başlangıç/bitiş · sorumlu personel · konum/adres · açıklama · bağlı şantiye — **ad dışında hepsi opsiyonel**; mevcut kayıtlara otomatik proje ataması YAPILMADI. |
| **PK-C4** | AYRI proje yetkisi YOK: ekran ve işlemler **`branches` modülü** üzerinden yetkilenir; veri kapsamı **`BranchAccess`**'ten gelir (bypass yok, ikinci "ProjectAccess" kurulmadı). Şantiyesini göremeyen projeyi de göremez. |

## 2. Veri modeli (Migration073_Projects, şema v73)

- **`projects`**: id · company_id · name · status(`active|on_hold|completed`) · start_date · end_date ·
  manager_personnel_id(→personnel) · location · description · created_at/updated_at/version/is_deleted.
- **`project_branches`**: (project_id, branch_id) PK + company_id (Migration062 deseni) + created_at.
  Çoklu şantiyeye HAZIR; UI bugün 0-1 satır yazar.
- **Mevcut tablolara SIFIR dokunuş**: `branches` dahil hiçbir tabloya ALTER/kolon eklenmedi; hareket
  tablolarına `project_id` EKLENMEDİ (proje bağı "proje → şantiye kümesi → mevcut branch_id" ile çözülür).
- Kanıt: **PRJ13** (72'ye kadar kur + canlı benzeri veri + yalnız 73'ü uygula → tüm satırlar bit-bit aynı,
  yeni tablolar boş) ve **PRJ14** (migration kaynağında ALTER/UPDATE/DELETE/DROP/INSERT yok).
- Rollback: `DROP TABLE project_branches; DROP TABLE projects; DELETE FROM schema_migrations WHERE version=73;`

## 3. Mimari

- **Sunucu-otoriteli** (şubeler deseni): masaüstü CRUD'u çevrimiçi API ile yapar; çevrimdışıysa anlaşılır
  uyarı, yerele YAZILMAZ. **BusinessSync'e GİRMEDİ** (ebeveyn `branches` senkron paketinde yok → FK sırası
  zaten imkânsız; ayrıca şubelerle aynı sınıf veri).
- Silme: **soft delete** + audit + **Çöp Kutusu** geri yükleme (`TrashService.Tables += projects`).
  Şantiye bağları silmede korunur → geri yüklemede aynen döner (PRJ9).
- Audit: entity `project` (create/update/delete) · Ekran logu: `ScreenAuditMap["branches"] += "project"`.
- Düzenleme kilidi: `version` (BranchService deseni; 409 → "kaydı yenile / formda kal").
- Firma toplu silme (ADR-083): içgözlem (DbIntrospect) company_id'li tabloları kendisi keşfeder →
  `projects`/`project_branches` otomatik kapsanır, kod değişikliği gerekmedi.

## 4. Ekranlar ve API

| Katman | Ne |
|---|---|
| API | `GET/POST /api/projects` · `PUT/DELETE /api/projects/{id}` (+`search`,`status` filtreleri). Kapılar SERVİSTE. |
| Web | `Projects.razor` (`/projects`): liste + arama + durum filtresi + form + rozetli durum. |
| Masaüstü | `ProjectsView(.axaml)` + `ProjectsViewModel`: aynı işlevler; tarih alanları kehribar standardı; şantiye/personel açılır listeleri YERELDEN (çevrimdışı da dolu görünür). |
| Menü | `AppScreens`'e tek satır: "Şube ve Personel" grubu → **Projeler** (Both). Parite şeması 48/55 bağlantıya güncellendi. |
| Saha | Şube/Şantiye ekranlarında tür listesine "Saha" eklendi (masaüstü + web); `BranchMirrorApply` yeni türü düşürmez. |

## 5. Testler (`ProjeTests.cs`, 15 test — tümü yeşil)

CRUD + opsiyonellik (PRJ1-2) · düzenleme kilidi (PRJ3) · tarih doğrulama (PRJ4) · **çoklu şantiye
hazırlığı (PRJ5)** · deny-by-default (PRJ6) · **şube kapsamı okuma+yazma (PRJ7a/b)** · **tenant (PRJ8)** ·
soft delete + çöp kutusu (PRJ9) · audit + ekran logu (PRJ10) · Saha türü + fail-safe (PRJ11-12) ·
**migration canlı-veri kanıtı (PRJ13-14)**. Ek: AppScreensParity 19/19 · EkranLogu/BranchMirror/
BranchHierarchy/BranchParentScope/Trash 69/69 · üç Release derleme 0 hata.

## 6. Bilinen sınırlamalar / notlar

- Masaüstü Projeler ekranı ÇEVRİMİÇİ gerektirir (şubeler gibi); çevrimdışı görüntüleme istenirse ileride
  aynalama (mirror) eklenebilir — senkron protokolüne dokunmadan.
- PostgreSQL migration testi yerelde ATLANDI (PG yok); Migration073 yalnız iki lehçede ortak sözdizimi
  kullanır (Migration066 emsali). **Yayın turunda** deploy öncesi/sonrası canlı salt-okunur sayım
  (branches count + kind dağılımı + kritik tablo sayıları) alınacak — bu turda canlıya HİÇBİR ŞEY gitmedi.
- "İş verisi sıfırlama" (ResetCompanyBusiness) projeleri SİLMEZ (şubeler gibi korunur) — bilinçli.
- Raporlara proje filtresi BU TURDA EKLENMEDİ (talimat gereği yalnız tespit): anlamlı olacak raporlar —
  stok hareket/özet, yakıt, günlük faaliyet, araç raporu (hepsi şube filtreli; "proje → şantiye kümesi"
  çevirisiyle mevcut `ReportScope`/şube filtresi üzerinden eklenebilir, migration gerekmez).
- Duplicate proje adı ENGELLENMEZ (şube adlarıyla tutarlı — mevcut sistemde de ad benzersizliği yok).

## 7. Gelecek genişletme — çoklu şantiye (PK-C1 ikinci adım)

Yapılacaklar YALNIZ UI: web'de `MudSelect MultiSelection` / masaüstünde çoklu LookupBox; servis ve API
sözleşmesi (branchIds listesi) bugünden hazır. Migration GEREKMEZ (PRJ5 kilidi).

## 8. Sonraki roadmap maddesi

**A — Evrak / Belge Yönetimi** (FAZ 1 / SIRA 2). Başlamadan önce MASTER_ROADMAP §1 + bu belgenin
6. bölümü okunmalı (dosya senkron davranışı ilk netleştirilecek konu).
