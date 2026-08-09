# Test Raporu — LookupBox ortak bileşeni (İş #9)

Tarih: **2026-08-09** · Kapsam: değiştirilen 4 masaüstü ekranı — CLAUDE.md §7.1
Migration: **YOK** · Production yazma/deploy: **YOK**

---

## 1. Başlangıç durumu (koddan doğrulandı)

`LookupBox` ortak bileşeni **zaten vardı** ve 13 görünümde kullanılıyordu. Yani "ortak bileşen yaz"
diye bir iş **yoktu**; iş, geride kalan ekranları bu bileşene geçirmekti (P2-2 / P2-3).

Tarama sonucu gerçekten geride kalanlar:

| Ekran | Sorun |
|---|---|
| `MaterialQuickEditWindow` (çift tık) | Kategori, Alt Kategori, Birim, Marka, Tedarikçi → düz `ComboBox` (**arama yok**) |
| `VehicleQuickEditWindow` (çift tık) | Makine Tipi, Kategori, Marka, Model, Şube → düz `ComboBox`; Sürücü → `AutoCompleteBox` (**üçüncü bir desen**) |
| `VehicleTemplatesView` | Makine Tipi, Kategori, Marka, Model → düz `ComboBox` |
| `BranchesView` | Firma seçici (süper admin) ve Üst Şube → düz `ComboBox` |

Kullanıcıya etkisi: yüzlerce marka/tedarikçi olan firmada, çift tıkla açılan hızlı düzenleme
penceresinde **arama yapılamıyor**, liste kaydırılmak zorunda kalıyordu — aynı alan ana ekranda
aranabilirken.

## 2. Yapılan

**Yeni bileşen YAZILMADI.** Mevcut `LookupBox` kullanıldı.

| Dosya | Değişiklik |
|---|---|
| `Controls/LookupBox.cs` | **Tek ekleme:** `SelectionChanged` olayı — kod-arkası ekranların `ComboBox`taki ile aynı şekilde bağlanabilmesi için (MVVM ekranları eskisi gibi `SelectedItem` bağlar) |
| `MaterialQuickEditWindow.axaml(.cs)` | 5 alan → `LookupBox` |
| `VehicleQuickEditWindow.axaml(.cs)` | 6 alan → `LookupBox` (`AutoCompleteBox` + `AsyncPopulator` kaldırıldı) |
| `VehicleTemplatesView.axaml` | 4 alan → `LookupBox` |
| `BranchesView.axaml` | 2 alan → `LookupBox` |

**Kasıtlı olarak DEĞİŞTİRİLMEYENLER:** "Tür" (5 sabit değer), "Durum" (3 sabit değer),
"Şube/Şantiye türü" (2 sabit değer). Bunlar **lookup değil, sabit liste (enum)** — `ComboBox` doğrudur.
Ekrana özgü iş kuralları (veri kaynağı, firma kontrolü, yetki, marka→model bağımlılığı) **bileşenin
içine gömülmedi**; eskisi gibi ekranda kalıyor.

## 3. Korunan davranışlar

- **Marka → Model bağımlılığı:** marka değişince model listesi yenilenir ve seçim sıfırlanır.
  `LookupBox.SelectionChanged` programatik atamada da tetiklenir → `ComboBox` ile **aynı** davranış;
  bu yüzden mevcut `resolvingCat` / "önce doldur, sonra abone ol" korumaları aynen çalışır.
- **Kategori → Alt Kategori** aynı şekilde.
- `LookupBox` ItemsSource değişince seçimi kendiliğinden sıfırlamaz; ilgili ekranlar zaten
  `SelectedItem = null` yazıyordu → davranış değişmedi.

## 4. Testler

Avalonia için **headless UI test altyapısı yok**. Bu yüzden pencerelerin dayandığı iki şey test edildi
(uydurma UI testi yazılmadı):

| Test | Ne kanıtlıyor |
|---|---|
| `Malzeme_penceresinin_listeleri_BASKA_firmayi_gostermez` | Marka/Tedarikçi/Birim listeleri firma-izole |
| `Arac_penceresinin_listeleri_BASKA_firmayi_gostermez` | Tip/Kategori/Şube listeleri firma-izole |
| `Malzemenin_MEVCUT_markasi_ve_tedarikcisi_listede_gelir` | Pencere açılınca **mevcut seçim kaybolmaz** |
| `Aracin_MEVCUT_tipi_kategorisi_ve_subesi_listede_gelir` | aynısı araç için |
| `Buyuk_listede_ilk_acilis_TEK_sayfa_gosterir_ve_sorgu_TEKRARLANMAZ` | 500 kayıtta açılışta 25 satır; arama **bellekte** → tuş başına sorgu yok |
| `Hizli_yazip_silme_ilk_sayfaya_doner_ve_TAM_listeyi_geri_verir` | yazıp silince liste eksik kalmıyor |
| `Turkce_arama_dogru_esler` | "iş" → "İŞ MAKİNESİ" eşleşir, "KAMYON" eşleşmez |

| Paket | Sonuç |
|---|---|
| `QuickEditLookupTests` | **7 / 7** |
| SQLite tam paket | **950 geçti / 0 başarısız / 31 atlandı** |
| `dotnet build DepoWise.sln` | **0 hata** |
| PostgreSQL | bu işte **SQL değişmedi**; yine de tam PG paketi regresyon için koşuldu |

## 5. Veri bütünlüğü / firma izolasyonu

Bu iş **yalnız görünüm katmanını** değiştirir: hiçbir servis, sorgu, transaction, audit, version veya
yetki kodu değişmedi. Firma izolasyonu eskisi gibi **veri kaynağında** (`LookupService`, `BranchService`)
uygulanıyor ve testle doğrulandı — bileşene taşınmadı.

## 6. Yeni bulgu

**P2 — Web'de aynı alanlarda arama yok.** Web tarafı bu lookup alanlarında `MudSelect` kullanıyor
(arama yok). Web'in kendi aranabilir deseni **zaten var** (`Stock.razor` → `MudAutocomplete`), ama
18+ kontrolün dönüştürülmesi ayrı bir iş ve kendi regresyon riski var. Bu iş masaüstü kapsamlıydı
(P2-2/P2-3 masaüstü ekranlarını sayar); bulgu backlog'a eklendi, sessizce kapsam büyütülmedi.

**P3 — LookupBox'ta "seçimi temizle" yok.** Opsiyonel alanlarda (Marka, Tedarikçi, Alt Kategori)
seçim geri alınamıyor. Ancak eski `ComboBox`ta da alınamıyordu → **regresyon değil**; ihtiyaç
duyulursa ayrı iş.
