# Web ↔ Masaüstü Parite Matrisi

> Son güncelleme: **2026-08-11** · Kaynak: kod taraması (web 43 sayfa · masaüstü 46 görünüm)
> **Kural:** "Web'de var, masaüstünde yok" tek başına eksik DEĞİLDİR — gerekçesi bu tabloda yazılıdır.

Sınıflar: ✅ **Her ikisinde** · 🌐 **Bilinçli web-only** · 🖥️ **Bilinçli desktop-only** · ⚠️ **Gerçek eksik** · ❓ **Araştırılmalı**

---

## 1. Ekran paritesi

| Ekran | Web | Masaüstü | Sınıf | Gerekçe / Not |
|---|---|---|---|---|
| Ana Ekran / Dashboard | ✅ | ✅ | ✅ | |
| Uyarılar | ✅ | ✅ | ✅ | |
| Malzemeler | ✅ | ✅ | ✅ | PRT-01 Grup 2 ile eşitlendi |
| Malzeme Şablonları | ✅ | ✅ | ✅ | Grup 2b |
| Stok Girişi / Hareketler / Sayım | ✅ | ✅ | ✅ | Grup 1 |
| Stok Değişiklik Kaydı | ✅ | ✅ | ✅ | |
| Araçlar | ✅ | ✅ | ✅ | Grup 5 |
| Araç Şablonları | ✅ | ✅ | ✅ | |
| Muayene / Sigorta | ✅ | ✅ | ✅ | Grup 5 (iptal + gerekçe) |
| Personel | ✅ | ✅ | ✅ | `P-1` hariç (aşağıda) |
| Günlük Faaliyet | ✅ | ✅ | ✅ | Grup 5 |
| Bakım Takibi | ✅ | ✅ | ✅ | Grup 3 |
| Yakıt | ✅ | ✅ | ✅ | Grup 3 |
| Talepler / Onay / Operasyon | ✅ | ✅ | ✅ | Grup 4 |
| Raporlar | ✅ | ✅ | ✅ | içerik denetimi `RPR-01` |
| Şube / Şantiye | ✅ | ✅ | ✅ | Grup 6 |
| Kullanıcılar | ✅ | ✅ | ✅ | Grup 6 |
| Yetkiler | ✅ | ✅ | ✅ | `YTK-05` eksik (iki tarafta da) |
| Yetki Şablonları | ✅ | ✅ | ✅ | G6-01 ile sunucu-otoriteli oldu |
| Tanım Düzenle | ✅ | ✅ | ✅ | G6-02/G6-20 ile eşitlendi |
| Sistem Logu | ✅ | ✅ | ✅ | web'de ek filtreler var (`G6-13`) |
| Çöp Kutusu | ✅ | ✅ | ✅ | G6-04 ile parola kapısı eşitlendi |
| Tema | ✅ | ✅ | ✅ | |
| Hakkında | ✅ | ✅ | ✅ | |
| İçe/Dışa Aktarım | ✅ | ✅ | ✅ | |
| **Firma Tanım** | ✅ | ✅ | ✅ | masaüstünde "Web Yönetimi" altında |
| **Kota İzleme** | ✅ | ❌ | 🌐 | Platform yönetimi — masaüstünde gereksiz *(ShellViewModel yorumu)* |
| **Canlı Sunucu** | ✅ | ❌ | 🌐 | Aynı gerekçe |
| **Makine Yönetimi** | ✅ | ✅ | ✅ | |
| **Makine Yedekleri** | ✅ | ❌ | 🌐 | 2026-07-26 kararı: yedek yönetimi yalnız web |
| **Sunucu Yedekleri** | ✅ | ✅ | ✅ | |
| **Güncelleme Yönetimi** | ✅ | ✅ | ✅ | |
| **Rol Yetki Kontrol** | ✅ | ❌ | 🌐 | Platform geneli, yalnız süper admin |
| **Firma Yetki Kontrol** | ✅ | ❌ | 🌐 | Aynı |
| **Kalıcı Silme** (ADR-083) | ✅ | ❌ | 🌐 | Geri alınamaz; özel kod + web'e kilitli |
| **Firma İş Verisini Sıfırla** | ✅ | ❌ | 🌐 | Aynı |
| **Excel İçe Aktarım** | ✅ | ❌ | 🌐 | Masaüstünde İçe/Dışa Aktarım ekranı karşılıyor |
| **Geliştirici Modu** | ❌ | ✅ | 🖥️ | Yerel tanılama |
| **Ekran Bilgisi / Bileşen Galerisi** | ❌ | ✅ | 🖥️ | Geliştirme aracı |
| **Senkron penceresi** | ❌ | ✅ | 🖥️ | Yalnız masaüstünün ihtiyacı |

**Sonuç: ekran düzeyinde gerçek eksik YOK.** Tüm farklar gerekçeli.

---

## 2. Özellik paritesi (ekran içi)

| ID | Modül | Fark | Sınıf |
|---|---|---|---|
| `P-1` | Personel | Web'de "Bağı Kaldır" (kullanıcı↔personel) var, **masaüstünde yok** | ⚠️ (yayın sonrası kararlaştırıldı) |
| `G6-13` | Sistem Logu | Web'de "Kayıt/Kullanıcı" filtreleri var (istemci tarafı), masaüstünde yok | ⚠️ küçük |
| — | Personel / Muayene | Filtre + Excel export iki tarafta da eksik | ⚠️ ortak eksik |
| — | Personel listesi | 200 kayıt tavanı (iki tarafta) | ⚠️ ortak eksik |
| `WEB-02` | Şube kapsamı | `BranchScope` web'de **hiç çalışmıyor** (JWT şube taşımıyor) | ⚠️ → `STK-05` |
| — | Yetkiler | "Sıfırla" butonu **iki tarafta da yok** | ⚠️ → `YTK-05` |
| — | Tablolar | Satır seçimi sorunu **iki tarafta da** | ⚠️ → `UIX-01` |

---

## 3. Mimari fark (bilinçli)

| Konu | Web | Masaüstü |
|---|---|---|
| Veri kaynağı | Doğrudan API (durum tutmaz) | Yerel SQLite + senkron |
| Kullanıcı/şube/yetki/şablon | API | **API (sunucu-otoriteli)** — senkron dışı |
| İş verisi | API | Yerel → `BusinessSync` push/pull |
| Çevrimdışı | ❌ | ✅ |

Bu fark **kasıtlıdır** ve ADR-057'ye dayanır; parite eksiği sayılmaz.
