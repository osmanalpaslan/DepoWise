# Malzeme Şablonu + Şablon-Dışı Uyarı — Test Raporu (Adım 5)

> Kapsam: Malzeme Şablonları ekranı + malzeme/araç yeni-kayıt şablon seçimi/uyarısı (§7.1). Tarih: **2026-07-12**. Motor: Opus 4.8.

## 0. Yapılanlar
- **Malzeme Şablonu** (Araç Genel Tanım benzeri): yeni `material_templates` tablosu + servis + modül + endpointler.
  Web'de **Malzeme Şablonları** yönetim ekranı (Malzeme menüsü); malzeme yeni-kayıt formunda şablon seçici (prefill).
- **Görünürlük OLUŞTURANA göre** (kullanıcı kararı): admin şablonu (is_global) firmada herkese; diğer kullanıcının
  şablonu yalnız kendisine. Aynı kural **araç şablonlarına** da uygulandı (created_by + is_global; mevcutlar global).
- **Şablon-dışı uyarı** (malzeme + araç, web + masaüstü): şablon seçilmeden yeni kayıtta
  *"Ana Yetkiliye Bilgi verilmelidir! Şablon dışı kayıt girmektesiniz!"* penceresi (onayla devam).
- Masaüstü: malzeme formuna şablon seçici (ComboBox) + prefill + uyarı; araç formunda uyarı (şablon seçici zaten vardı).

## 1. Otomatik testler
```
Başarılı! — Başarısız: 0, Başarılı: 307, Atlanan: 0, Toplam: 307, Süre: 42 s
```
- **307/307 yeşil** (306 → 307; toplam Adım 5'te +5). Solution build **0 hata**.
- Yeni: `MaterialTemplateTests` (4) — admin/global görünür, kişisel yalnız oluşturana, yönetim yetkisi,
  içerik prefill. `VehicleTests.AracSablonu_Gorunurluk_OlusturanaGore` (1).

## 2. Senaryolar (§7.7 yetki + görünürlük)
| Senaryo | Sonuç |
|---|---|
| Admin şablonu → tüm kullanıcılarda görünür | ✅ |
| Personel şablonu → yalnız oluşturana; başkası (admin dahil) listede görmez | ✅ |
| Genel şablonu yalnız admin, kişiseli yalnız sahibi/admin yönetir | ✅ |
| Şablon içeriği yeni kayıt formunu doldurur | ✅ (web + masaüstü) |
| Şablon seçilmeden kayıt → uyarı penceresi | ✅ (web + masaüstü, malzeme + araç) |

## 3. Coverage
| Alan | Durum |
|---|---|
| Yetki (material_templates modülü, deny-by-default) | ✅ |
| Database (Migration040, idempotent tablo/kolon) | ✅ |
| Tenant + görünürlük (oluşturan-bazlı) | ✅ testli |
| UI (web yönetim + selector + uyarı; masaüstü selector + uyarı) | ✅ build; canlı tık deploy sonrası |

## 4. Riskler / notlar
- **Deploy bekliyor** (web + API): şema Migration035→040. Kullanıcı kararı = sonraki web işiyle birlikte.
- "Makine" kapsamı yerine **oluşturan (kullanıcı)** bazlı görünürlük seçildi (kullanıcı onayı) — sunucu oturumu
  makine kimliği taşımadığından en sade/güvenli yol; kullanıcı tek makinede çalıştığından pratikte "kendi makinesi" ile eşdeğer.
- Masaüstü **Malzeme Şablonları yönetim ekranı** (Avalonia) eklenmedi (öncelik web); masaüstünde şablon
  SEÇİMİ + uyarı çalışır, şablon OLUŞTURMA/yönetimi web'den yapılır.
