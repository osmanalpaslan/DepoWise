# Test Raporu — Menü / Ekran Yönetimi (MNU)

**Tarih:** 2026-08-18 · **Kapsam:** yalnız değiştirilen ekran (CLAUDE.md §7.1) + doğrudan etkilenen
menü/senkron katmanları. **Üretime hiçbir işlem yapılmadı.**

---

## 1. Otomatik testler

| | Sayı |
|---|---|
| Tur öncesi toplam (tam takım) | 2085 (2050 geçti · 0 başarısız · 35 atlandı) |
| Bu turda eklenen yeni test (koşulan senaryo) | **48** (`MenuLayoutTests` · `ProtectedScreenTests` · `ApiMenuLayoutTests` + parite S17) |
| Güncellenen mevcut test | 2 (`AppScreensParityTests` S10 · S11) + 1 yeni (S17) |
| Tur sonrası tam takım | **2133 (2098 geçti · 0 başarısız · 35 atlandı)** |

### Yeni test sınıfları

- **`MenuLayoutTests` (M01–M24)** — geri uyumluluk (kayıt yokken menü katalogla birebir), ad/grup/sıra
  değiştirme, kullanıcı grubu oluşturma, yetkisiz okuma/yazma, yetim ekran reddi, kaçak grup anahtarı,
  uzun ad, ad temizliği, **tenant sızıntısı**, çakışan sıra determinizmi, kalıcılık, varsayılana dönüş,
  önbellek düşürme, audit, tablo yokken çökmeme.
- **`ProtectedScreenTests` (P1–P7)** — MNU-B2 kilit koruması; tek platformda kapatma serbest, hepsi
  kapalı yasak; korumasız ekranın davranışı değişmedi.
- **`ApiMenuLayoutTests` (A1–A4, B1–B7)** — uçların doğrudan çağrıldığında fail-closed olduğu,
  kaydet→yeniden oku turu, menü ucunun düzeni taşıması, varsayılana dönüş, boş gövde reddi,
  tanım senkronunun ekran ayarlarını taşıması, korumalı ekranın uçtan kapatılamaması.

### Testlerin bulduğu hatalar (kod düzeltildi)

1. **Birleşik platform maskesi** — `HasFlag(Desktop|Web)` "İKİSİNDE DE olan" demek olduğu için yalnız
   tek platformda bulunan **14 ekran** yönetim listesinden sessizce düşüyordu (ör. Kota İzleme,
   Malzeme Şablonları, Yedek Yönetimi). `(& != 0)` maske testine çevrildi + regresyon testi (M02b).

---

## 2. Gerçek tarayıcı GUI testi

İzole ortam: yerel API (`localhost:5224`, **boş ve yeni** veri klasörü) + yerel web (`localhost:5283`).
Üretim sunucusuna ve verisine **dokunulmadı**.

⚠️ **Ekran görüntüsü alınamadı:** tarayıcı paneli görüntülenmediği için `screenshot` çalışmadı.
Kanıtlar erişilebilirlik ağacı (`read_page`) ve sayfa metni üzerinden alındı.

| # | Senaryo | Sonuç |
|---|---|---|
| 1 | Giriş (3 adımlı akış + ilk giriş şifre/özel kod) | ✅ |
| 2 | "Menü / Ekran Yönetimi" ekranını aç | ✅ |
| 3 | Ekran listesini gör (58 ekran + 17 grup başlığı = 75 satır) | ✅ |
| 4 | Arama ("yakıt" → 1 satır) | ✅ |
| 5 | Üst menü filtresi ("Yakıt" → 3 satır) | ✅ |
| 6 | Platform filtresi ("Masaüstünde açık" → yalnız-web ekranlar düştü) | ✅ |
| 7 | Ekran adını değiştir (`fuel.dist` → "Yakıt Çıkışı") | ✅ |
| 8 | Üst menüsünü değiştir (`fuel.depot` → Yönetim) | ✅ |
| 9 | Sırasını değiştir (`fuel.summary` yukarı) | ✅ |
| 10 | Kaydet (özet + onay penceresi) | ✅ |
| 11 | Sayfayı yenile | ✅ |
| 12 | Değişikliğin korunduğunu doğrula | ✅ |
| 13 | Web menüsünde değişikliğin göründüğünü doğrula | ✅ |
| 14 | Bir ekranı gizle (`audit` web'de kapat) | ✅ |
| 15 | Menünün doğru davranması (menüden düştü, diğerleri yerinde; adres de kapandı) | ✅ |
| 16 | Yetkisiz erişim | 🟡 **kısmi** — GUI'de ayrı personel hesabıyla denenmedi; sunucu tarafı
  otomatik testlerle (A1–A4) ve tokensiz `curl` ile doğrulandı: üç uç da **401**. |
| 17 | Mevcut ekranların route/permission davranışı bozulmadı (`/fuel/dist` açıldı; adres ve yetki anahtarı aynı) | ✅ |
| + | **MNU-B2**: korumalı ekranı web'de kapatma denemesi engellendi ve menüde kaldı | ✅ |
| + | "Menüyü Önizle" kaydedilmemiş değişikliklerle doğru sonuç verdi | ✅ |
| + | Gizlenen ekranı geri açma (onay istemedi — doğru) | ✅ |

### GUI testinin bulduğu hatalar (kod düzeltildi)

1. **Satır taşındıktan sonra `<select>` eski değeri gösteriyordu.** Blazor öğeleri KONUMA göre yeniden
   kullandığı için, kullanıcının elle değiştirdiği DOM değeri bir alttaki satıra "yapışıyordu"
   (C# tarafı doğruydu, ekranda yanlış grup görünüyordu). Satırlara `@key` eklendi.
2. **Reddedilen/vazgeçilen değişiklikten sonra onay kutusu yanlış durumda kalıyordu.** Sunucu isteği
   reddettiğinde yeni değer eskisiyle aynı olduğu için Blazor DOM'u geri almıyordu — kullanıcı ekranı
   kapattığını sanabilirdi. Her yeniden yükleme sonrası satırların yeniden oluşmasını sağlayan
   sürüm anahtarı (`_rev`) eklendi.
3. **Onay kutuları tarayıcı otomasyonuyla tıklanamıyordu.** MudBlazor gerçek `<input>`'u görsel olarak
   gizliyor → erişilebilirlik ağacında yok. §22 gereği yerel, etiketli `<input type="checkbox">`
   kullanıldı (davranış aynı, klavye ve otomasyon erişimi kazanıldı).
4. **Değişiklik sayacı yanıltıcıydı:** bir ekran taşınınca altındakilerin index'i kaydığı için "1 taşıma"
   **7 ekran değişti** olarak raporlanıyordu. Sayaç ad/üst menü ve sıralama olarak ayrıldı; ayrıca servis
   artık gruba gerçekten düşen ekranların katalog sırasıyla karşılaştırma yapıyor → gereksiz satır yazılmıyor.

### Not (hata değil, tasarım)

Ekran adının değiştirilmesi **menü etiketini** değiştirir; ekranın kendi sayfa başlığı (ör. Yakıt
Dağıtımları ekranındaki "Yakıt — Yakıt Dağıtımları") kendi bileşenindedir ve değişmez. İstenirse ayrı
bir iş olarak sayfa başlıkları da katalogdan beslenebilir.

---

## 3. Coverage Matrix (§7.13)

| Madde | Durum |
|---|---|
| Form Açıldı | ✅ |
| Yeni Kayıt (yeni üst menü) | ✅ (M09 · GUI: buton mevcut) |
| Düzenleme | ✅ |
| Silme | ✅ (yalnız kullanıcı grubu kaldırma; ekran silme YOK — §15) |
| Arama | ✅ |
| Filtre | ✅ (üst menü · platform · durum) |
| Grid | ✅ |
| Doğrulamalar | ✅ (uzun ad · boş ad · kaçak grup · bilinmeyen ekran · yetim ekran) |
| Yetki | ✅ (M10/M11 · A1–A4 · curl 401) |
| Hata Mesajları | ✅ (kritik ekran mesajı GUI'de görüldü) |
| Database | ✅ (M20 kalıcılık · M21 sıfırlama · M23 audit) |
| Offline | ✅ (M24 tablo yokken çökmez · ADR-110 çevrimdışı korunumu) |
| Sync | ✅ (B7 tanım senkronu üç bölümü taşıyor) |
| Performans | 🟡 58 ekran + 17 grup; sayfalama gerekmedi, tümü tek istekte |
| UI | ✅ (1280px'de İŞLEM kolonu yatay kaydırma gerektiriyor — tablo `overflow-x:auto`) |
| UX | ✅ (değişiklik özeti · kaydetmeden çıkma uyarısı · önizleme · varsayılana dön) |
| Security | ✅ (tenant sızıntısı M17 · fail-closed uçlar · kendini kilitleme koruması) |

---

## 4. Regresyon

- Katalog (`AppScreens`) **değişmedi** (yalnız bir ekranın etiketi: "Ekran Platform Yönetimi" →
  "Menü / Ekran Yönetimi"). Route, anahtar ve modül aynı.
- `AppScreensParityTests` S13/S14 (menülerin taşımadan önceki hâliyle birebir aynı olması) **geçiyor**.
- S10/S11 mekanizma değiştiği için güncellendi; garanti **S17** ile davranış düzeyinde kilitlendi
  (boş düzenle üretilen menü = katalog).

---

## 5. Üretim

INSERT 0 · UPDATE 0 · DELETE 0 · DDL 0 · Migration 0 · Deploy 0 · Publish 0 · Restart 0 · Secret 0 · ACL 0

---

## 6. Tam takım sonucu

**2098 geçti · 0 başarısız · 35 atlandı · toplam 2133** (süre 14 dk 19 sn). Tur öncesi 2050 geçiyordu → **+48 senaryo**, sıfır regresyon.
