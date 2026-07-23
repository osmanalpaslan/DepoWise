# Malzemeler Ekranı — Masaüstü ↔ Web ↔ Veritabanı Parite Denetimi

**Tarih:** 2026-07-23 · **Kapsam:** Faz 0 (2. ekran) · **Sonuç: parite TAM — gerçek bulgu yok.** Kod değişmedi.

## Karşılaştırılan 4 giriş noktası
1. Masaüstü ana form (`MaterialsViewModel` + `MaterialsView.axaml`)
2. Masaüstü hızlı düzenle penceresi (`MaterialQuickEditWindow`)
3. Web ana form (`Materials.razor`)
4. Web düzenle penceresi (`MaterialEditDialog`)

## ✅ Eşit çıkanlar (kontrol edildi)
| Konu | Durum |
|---|---|
| Ana form alanları (Kod, Ad, Tür, Kategori, Alt Kategori, Marka, Tedarikçi, Birim, Min. Stok, Birim Fiyat, Açıklama, Açılış Stoğu) | Masaüstü = Web ✅ |
| İlişkili veriler: Muadil, Uyumlu Araçlar, Fotoğraflar | İki ana formda da var ✅ |
| Hızlı düzenle alanları (10 alan) | Masaüstü penceresi = Web penceresi ✅ |
| "Tür" seçenekleri + varsayılan ("Yedek Parça") | Dört yerde de aynı ✅ |
| Zorunlu alan: **Kod ve ad** | Dört yerde de aynı mesaj ✅ |
| Zorunlu alan: **Birim seçin** | Dört yerde de aynı mesaj ✅ |
| "Şablon dışı kayıt" uyarısı (yalnız yeni kayıt) | Masaüstü ana form = Web ana form ✅ |
| Kategori/Alt kategori mantığı (alt seçiliyse o kullanılır) | Masaüstü = Web ✅ |
| Hızlı düzenlemede kategori kutusu alt kategorileri de içeriyor (düz liste) | Masaüstü = Web (`/api/lookups/material_categories` düz liste döner) ✅ |
| Muadil/foto/uyumlu araç hızlı düzenlemede KORUNUR (değişmez) | Masaüstü = Web ✅ |
| Düzenleme kilidi (sürüm kontrolü) | Önceki iş — dört yerde de var ✅ |

## Not (yeni değil, Araçlar'la aynı)
`MaterialListColumns` (sütun listesi) web'de elle senkron tutuluyor (ortak katmana bağlanamadığı için).
İçerik bugün aynı; bakım riski. **PostgreSQL Faz 3'te** web ortak katmana bağlanınca kökten çözülecek.
Bu ekrana özel bir aksiyon gerektirmez.

## Coverage (§7.13, kısa)
Form açıldı ✅ · Alanlar ✅ · Hızlı düzenle ✅ · Zorunlu/uyarı kuralları (4 giriş noktası) ✅ ·
Tür/kategori ✅ · Muadil/araç/foto ✅ · Düzenleme kilidi ✅. Kapsam dışı: filtre/sıralama birebir eşleşmesi,
Excel alan eşleşmesi.

## Sonuç
Araçlar'da bir bulgu vardı (hızlı düzenle plaka uyarısı — düzeltildi). **Malzemeler'de düzeltilecek bir
tutarsızlık çıkmadı** — ekran zaten masaüstü ↔ web tam tutarlı. Kod değişmedi.
