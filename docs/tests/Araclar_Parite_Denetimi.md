# Araçlar Ekranı — Masaüstü ↔ Web ↔ Veritabanı Parite Denetimi

**Tarih:** 2026-07-23 · **Kapsam:** Faz 0 Adım 0.2 (ilk ekran) · Kod değişmedi, yalnız analiz.

## ✅ Uyumlu (kontrol edildi)
- Form alanları: 16 alan (İç Kod, Plaka, Yıl, Sayaç, Birim, Durum, Durum Notu, Şase, Motor, Tip,
  Kategori, Marka, Model, Şube, Sürücü, Şablon) — üç tarafta da (masaüstü/web/veritabanı) birebir aynı.
- Sekmeler: Muayene/Sigorta, Bakım Takibi, Uyumlu Malzemeler, Araç Hareketleri — dört tarafta da eşit.
- Zorunlu alan kuralları (İç Kod, Şube, Yıl aralığı): API'de tek merkezde (`RequireVehicleFields`),
  her iki arayüz de aynı hata mesajını gösteriyor.

## ⚪ Bulgu 1 — YANLIŞ ALARM (denetim sırasında düzeltildi)
İlk bakışta "masaüstü sütun tercihini `Sanitize()` ile temizliyor, web temizlemiyor" sandım. **Yanlış.**
`Sanitize()` **hiçbir yerde çağrılmıyor** (masaüstü, web, API — tümünde ölü kod). Yani iki taraf da
temizlik yapmıyor → davranış zaten **aynı**. Web'e `Sanitize` eklemek yalnız yeni kullanılmayan kod
katardı. Parite hatası YOK.

Geriye kalan tek gerçek nokta bir **bakım riski** (defect değil): `VehicleListColumns` iki dosyada
elle senkron tutuluyor (web ortak katmana bağlanamadığı için). Bugün içerik aynı. Kalıcı çözüm —
web'i ortak `DepoWise.Application` katmanına bağlamak — büyük bir iştir ve **PostgreSQL geçişinin doğal
parçası** (Faz 3). Şimdi ayrıca uğraşmaya değmez; geçişte ele alınır.

## 🔴 Bulgu 2 (DÜZELTİLDİ) — Hızlı düzenle penceresi plaka uyarısını atlıyordu
Araçlar ekranında düzenleme **iki farklı yoldan** yapılabiliyor:
1. Ana form (soldaki panel) — `VehiclesViewModel.cs`
2. Çift-tıkla açılan "hızlı düzenle" penceresi — `VehicleQuickEditWindow.axaml.cs`

Ana form ve web plaka biçimi uyarısı (`PlateLooksTurkish`) gösteriyor; **hızlı düzenle penceresi
göstermiyordu.** Yani kullanıcı aracı **çift tıklayıp** düzenlerse anlamsız bir plaka ("asdf1234")
hiçbir uyarı almadan geçiyordu — aynı ekranın diğer yolundan girse uyarı alacaktı.

> Not: Ana formdaki "şüpheli büyük sayaç" uyarısı hızlı düzenle penceresine **gerekmiyor** — orada
> sayaç salt-okunur (değiştirilemez), dolayısıyla o kontrol için bir girdi yok.

**Düzeltme (yapıldı):** Aynı plaka uyarısı `VehicleQuickEditWindow.axaml.cs`'e eklendi (merkezi
`FieldChecks.PlateLooksTurkish` çağrısı, ana formla birebir aynı mesaj). Yumuşak uyarıdır — iş
makinesi/plakasız araç için kullanıcı yine "Evet, Kaydet" ile geçebilir. Masaüstü derleme: 0 hata.

## Kapsam dışı bırakılanlar (bu turda denetlenmedi, gerekirse ayrı ele alınır)
- Uyumlu Malzemeler / Muayene-Sigorta / Bakım Takibi / Hareketler sekmelerinin alan bazlı denetimi
  (yalnız sekme başlıklarının varlığı doğrulandı, içerikleri değil).
- Filtre/arama/sıralama davranışının birebir eşleşmesi.
- Excel içe/dışa aktarma alan eşleşmesi.

## Sonuç
- **Bulgu 1:** yanlış alarm (parite hatası yok). Bakım riski PostgreSQL geçişinde ele alınacak.
- **Bulgu 2:** düzeltildi (hızlı düzenle penceresine plaka uyarısı eklendi). Masaüstü build 0 hata.
- Araçlar ekranı alan+doğrulama düzeyinde masaüstü ↔ web parite: **TAMAM.**

## Coverage (§7.13, kısa)
Form açıldı ✅ · Alanlar (16) ✅ · Sekmeler (4) ✅ · Zorunlu alan kuralları ✅ · Plaka/yıl/sayaç uyarıları ✅
(hızlı düzenle dahil artık) · Yetki (API merkezli) ✅ · Sütun listesi ✅ (içerik aynı) · Düzenleme kilidi ✅
(önceki iş). Kapsam dışı: sekme içi alan denetimi, filtre/sıralama birebir eşleşmesi, Excel eşleşmesi.

## Sonraki adım
Malzemeler ekranı — aynı denetim.
