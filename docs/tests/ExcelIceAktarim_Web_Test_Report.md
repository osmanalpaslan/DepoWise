# Test Raporu — Excel İçe Aktarım → Web (İş #7)

Tarih: **2026-08-09** · Kapsam: **yalnız yeni ekran** (Web → Excel İçe Aktarım) — CLAUDE.md §7.1
Migration: **YOK** · Production yazma/deploy: **YOK**

---

## 1. Başlangıç durumu (koddan doğrulandı)

- İçe aktarım servisleri (7 tür) **vardı** ve masaüstünde kullanılıyordu.
- **Sunucuda tek bir içe aktarım ucu yoktu** (`grep -n "import" Program.cs` → 0 sonuç).
- `ServerServices` içinde import servislerinin **hiçbiri kayıtlı değildi**.
- Web'de içe aktarım ekranı yoktu; menüde de yer almıyordu.

Yani web kullanıcısı Excel'den toplu kayıt **hiç** ekleyemiyordu — masaüstüne gitmek zorundaydı.

## 2. Yapılan

**Yeni iş kuralı yazılmadı.** Sunucu, masaüstüyle **aynı import servislerini** çağırır; doğrulama,
otomatik tanım oluşturma ve idempotenlik davranışı birebir aynıdır.

| Katman | Değişiklik |
|---|---|
| Sunucu | `ServerServices`: 7 import servisi + `Scopes` (şube kapsamı) kaydedildi |
| API | `GET /api/import/entities` · `GET /api/import/{tür}/template` · `POST /api/import/{tür}/preview` · `POST /api/import/{tür}/commit` |
| Web | `Components/Pages/ImportExcel.razor` (yeni ekran) + menüde "Ayarlar › Excel İçe Aktarım" |
| Web altyapı | `ApiClient.UploadImportAsync` — çok parçalı (multipart) yükleme + sunucu hata metnini çıkarma |

Akış masaüstüyle aynı: **şablon indir → dosya seç → ÖN KONTROL (hiç yazmaz) → onay penceresi → aktar**.

**Dışa aktarım bu ekrana konmadı:** her liste ekranında zaten "Excel'e Aktar" butonu var (ADR-087/088/089).

## 3. Güvenlik kararları

- **Yetki:** `import_export` (deny-by-default). Şablon indirmek bile bu yetkiyi ister.
- **Hedef şube ZORUNLU** (masaüstü kuralı 2026-07-26). Seçilmezse 400.
- **Şube kapsamı fail-closed:** seçilen şube kullanıcının kapsamında değilse `ScopeResolver` 403 verir →
  başka firmanın şubesine aktarım imkânsız.
- **Dosya sınırı 20 MB**; bozuk dosya teknik hata değil, ne yapılacağını söyleyen mesaj döndürür.

## 4. Test sonuçları

| Paket | Sonuç |
|---|---|
| `ApiImportTests` (gerçek HTTP hattı) | **14 / 14** |
| SQLite tam paket | **929 geçti / 0 başarısız / 31 atlandı** |
| `dotnet build DepoWise.sln` | **0 hata** |

Kapsanan senaryolar: 7 türün listelenmesi · şablon indirme · bilinmeyen tür · ön kontrolün
**veritabanını değiştirmediği** (satır sayısı önce/sonra karşılaştırıldı) · hatalı satır raporu ·
aktarımın gerçekten kayıt oluşturduğu · **aynı dosyanın iki kez aktarılmasında kopya oluşmadığı** ·
şubesiz istek · başka firmanın şubesi · dosyasız istek · Excel olmayan dosya · yalnız başlıklı dosya ·
yetkisiz kullanıcı · girişsiz istek.

## 5. Gerçek tarayıcı doğrulaması (yerel API + yerel web)

Testlerin kapsayamadığı katman (Blazor dosya okuma, çok parçalı yükleme, onay penceresi, ekran):

| Adım | Sonuç |
|---|---|
| Menü → "Excel İçe Aktarım" | ekran açıldı |
| Tür listesi | **7 tür** API'den geldi |
| "Örnek Şablon İndir" | `GET .../template` → **200**, dosya indi |
| Bozuk dosya + Ön Kontrol | **400** → ekranda *"Dosya okunamadı. Geçerli bir .xlsx dosyası seçin."* (ham JSON değil) |
| Gerçek .xlsx (2 geçerli + 1 hatalı satır) + Ön Kontrol | **200** → "Toplam 3 · Geçerli 2 · Hatalı 1", tabloda *"Satır 4: Kod zorunlu."* |
| "İçe Aktar" | onay penceresi masaüstüyle aynı bilgiyi gösterdi |
| Onay sonrası | **200** → "toplam 3, eklenen 2, güncellenen 0, hatalı 1" + *"2 tanım otomatik oluşturuldu"* uyarısı |
| Malzemeler ekranı | `WEB-1` ve `WEB-2` listede — **kayıtlar gerçekten oluştu** |

Ön kontrolde satır sayısı **değişmedi**; kayıtlar yalnız "İçe Aktar"dan sonra oluştu.

## 6. Bu sırada bulunup düzeltilen gerçek hata

Blazor'da `IBrowserFile.OpenReadStream` **yalnız bir kez** açılabilir. "İçe Aktar" önce ön kontrol
sonra aktarım yaptığı için dosyayı iki kez okuyordu → ikinci okuma çalışma anında hata verecekti.
Düzeltme: dosya **seçilir seçilmez** okunup bellekte tutulur. (Testler bunu yakalayamazdı; gerçek
tarayıcı doğrulaması olmasa canlıda patlardı.)

## 7. Coverage Matrix (§7.13)

| Alan | Durum |
|---|---|
| Form Açıldı · Yeni Kayıt (toplu) · Doğrulamalar · Yetki · Hata Mesajları | ✅ |
| Database (ön kontrol yazmaz · idempotenlik · otomatik tanım) | ✅ |
| Security (yetki, tenant/şube kapsamı, dosya boyutu, bozuk dosya) | ✅ |
| UI / UX (3 adımlı akış, onay penceresi, hata tablosu, yazım hatası uyarısı) | ✅ |
| Düzenleme · Silme · Grid · Offline · Sync | bu ekranda yok → kapsam dışı |

## 8. Risk ve açık uçlar

- **Yayın gerekir:** web bu uçları **uzak API'den** çağırır. API deploy edilmeden web'de ekran açılır
  ama işlem yapamaz (uçlar 404). Yayında **önce API (fly.toml), sonra web (fly.web.toml)** gitmelidir.
- **Masaüstü push adımı web'de yok — gerekmiyor:** masaüstü içe aktarımdan sonra veriyi sunucuya
  *push* eder; web zaten doğrudan sunucuya yazar.
- **Büyük dosya:** 20 MB sınırı içinde tüm işlem tek istekte, senkron yapılır. Çok büyük listelerde
  (10.000+ satır) istek uzun sürebilir; şu an ilerleme çubuğu göstergesi belirsiz (indeterminate).
  Gerçek bir sorun görülürse ayrı iş olarak ele alınmalı — şimdilik erken iyileştirme yapılmadı.
