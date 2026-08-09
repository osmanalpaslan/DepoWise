# Test Raporu — Kolon kataloğu tekilleştirme (İş #10)

Tarih: **2026-08-09** · Kapsam: kolon kataloğu + 6 liste ekranı tercih yükleme yolu — CLAUDE.md §7.1
Migration: **YOK** · Production yazma/deploy: **YOK**

---

## 1. Analiz — gerçekten tekrar var mıydı?

Evet, ama beklenenden farklı bir biçimde. Önce ayrım netleştirildi:

| Kavram | Nerede tutuluyor | Kime ait |
|---|---|---|
| **Sistem kolon kataloğu** ("bu kolon var, adı şu, sayısal mı") | KOD (`ListColumns.cs`) | Herkese aynı |
| **Kullanıcı tercihi** ("ben bu kolonları görmek istiyorum") | `user_list_preferences` tablosu | Kişisel |
| Sıralama / sayfa boyutu / genişlik / sabitleme | aynı tablo | Kişisel |

Bu ayrım **zaten doğruydu** ve karıştırılmamıştı. Bulunan iki gerçek sorun:

**Sorun 1 — Katalog İKİ dosyada duruyordu.**
`DepoWise.Application/Ui/ListColumns.cs` ve `DepoWise.Web/Services/ListColumns.cs`.
İkisi elle senkron tutuluyordu (CLAUDE.md'de "ikisini BİRLİKTE güncelle" diye bir kural bile var).
Karşılaştırıldı: **şu an içerik birebir aynı** — yani hata henüz oluşmamış, ama tuzak gerçek.

**Sorun 2 — `Sanitize` yazılmış ama HİÇ ÇAĞRILMIYORDU (ölü kod).**
Üç katalogda da tercihi kataloğa göre süzen `Sanitize` metodu vardı; 6 çağrı yerinin **hiçbiri**
kullanmıyordu. Kaydedilmiş anahtarlar ham hâliyle uygulanıyordu.

## 2. Yapılan

### Sorun 1 → tek dosya (proje referansı DEĞİL)

`DepoWise.Web.csproj` içine:

```xml
<Compile Include="..\DepoWise.Application\Ui\ListColumns.cs" Link="Services\ListColumns.cs" />
```

Web'deki ayna dosya **silindi**; `_Imports.razor`'a `@using DepoWise.Application.Ui` eklendi.

**Neden proje referansı değil:** web bilinçli olarak tek başınadır — iş kurallarına (AccessControl,
servisler) derleme-zamanı erişimi yoktur, her şeyi API'den alır. Proje referansı bu sınırı gevşetirdi.

**Neden API'den çekme değil:** katalog `const` alanlar içeriyor ve Razor'da `switch` desenlerinde
kullanılıyor (`DailyActivityListColumns.Date => "dateText"`). Çalışma zamanında gelen veri `const`
yerine geçemez; API'ye taşımak üç sayfanın yeniden yazılmasını gerektirirdi.

Sonuç: **tek fiziksel dosya, iki projede derleniyor.** Kolon eklemek/çıkarmak artık tek yerde.

### Sorun 2 → mevcut `Sanitize` bağlandı

6 çağrı yerinin hepsinde kaydedilmiş tercih artık kataloğa göre süzülüyor:

| Platform | Dosyalar |
|---|---|
| Masaüstü | `MaterialsViewModel`, `VehiclesViewModel`, `DailyActivityViewModel` |
| Web | `Materials.razor`, `Vehicles.razor`, `Daily.razor` |

**Yeni kod yazılmadı** — zaten var olan metot çağrıldı.

## 3. Düzelen gerçek davranış

Bir kolon ileride kaldırılır/yeniden adlandırılırsa, o kolonu kaydetmiş kullanıcıda:

- **önce:** başlığı ham anahtar (`"eskiKolon"`) olan, tüm hücreleri boş bir **hayalet kolon** çizilirdi
  (tablo `@foreach (var key in _visibleColumns)` ile kurulur; bilinmeyen anahtar için `Label` ham
  anahtara düşer, `Str(row, key)` boş döner),
- **sonra:** o anahtar atılır; hiçbiri geçerli değilse varsayılana düşülür (kullanıcı kilitlenmez).

**Sıralama davranışı DEĞİŞMEDİ:** kolon seçici zaten katalog sırasında döndürüyordu
(`ColumnPickerDialog`: `Available.Where(...)`); `Sanitize` de aynı sırayı verir. Testle sabitlendi.

## 4. Testler

| Test | Ne kanıtlıyor |
|---|---|
| `Katalog_tutarli` (3 katalog) | anahtar tekrarı yok, boş anahtar/etiket yok, varsayılanlar katalogda |
| `Sanitize_KATALOGDA_OLMAYAN_anahtari_atar` | hayalet kolon oluşmaz |
| `Sanitize_HICBIRI_gecerli_degilse_VARSAYILANA_doner` | boş tablo yerine varsayılan |
| `Sanitize_tercih_YOKSA_varsayilani_verir` | ilk açılış |
| `Sanitize_KATALOG_SIRASINI_korur` | mevcut sıralama davranışı bozulmadı |
| `Sanitize_gecerli_secimi_AYNEN_korur` | en sık durum: hiçbir şey değişmiyor |

| Paket | Sonuç |
|---|---|
| `ListColumnCatalogTests` | **8 / 8** |
| SQLite tam paket | **958 geçti / 0 başarısız / 31 atlandı** |
| `dotnet build DepoWise.sln` | **0 hata** (web dahil — paylaşılan dosya iki projede de derleniyor) |
| PostgreSQL | bu işte **SQL değişmedi** (katalog kod tarafı) |

## 5. Veri bütünlüğü / firma izolasyonu

Bu iş kolon **görünürlüğünü** etkiler; hiçbir sorgu, transaction, audit, version veya yetki kodu
değişmedi. Kolon tercihi zaten firma-bağımsız ve **kişiseldir** (`user_list_preferences`, anahtar
`(user_id, list_key)`); firma izolasyonu verinin kendisinde uygulanır, kolon listesinde değil.
Bu ayrım korundu — katalog firma bazlı yapılmadı.

## 6. Migration

**Gerekmedi.** Katalog kod tarafında; tercih tablosu zaten mevcut ve şeması değişmedi.

## 7. Yeni bulgu

**P3 — `.claude/rules/list-screens.md` artık güncel değil.** İçinde "kolon kataloğu ... + AYNASI
`DepoWise.Web/Services/ListColumns.cs`, ikisi BİRLİKTE" yazıyor; o ayna dosya artık yok.
`.claude/` klasörü kullanıcı talimatıyla **kapsam dışı** olduğu için dokunulmadı — metnin
güncellenmesi kullanıcının kararı.
