# Kullanıcı talebi 2026-10-12 — masaüstü yeni kayıt ekranları görsel iyileştirme (fazlı)

> **Kesin sınırlar (kullanıcı):** commit/push/deploy/yayın YOK (çalışma kopyasında kalır) · ücretli servis, harici görsel
> API, yerel YZ modeli, yeni NuGet/MCP/eklenti YOK · üretim/canlı veriye dokunma YOK (yalnız UiTest_* ortamı) · iş mantığı,
> ViewModel davranışı, servis, şema, API, senkron, yetki, kayıt işlemi DEĞİŞMEZ — yalnız görsel düzen/kullanılabilirlik ·
> mevcut testler kaldırılmaz/zayıflatılmaz · mevcut ekran görüntüleri ezilmez (yeni çıktılar ayrı klasörde) ·
> kullanıcının commitlenmemiş işleri korunur · token tasarrufu: turda en fazla 3–5 ekran görseli.

## Fazlar
| Faz | İş | Durum |
|---|---|---|
| 1 | İnceleme: altyapı, ortak bileşenler (FormField/Toolbar/LookupBox/TarihKutusu/StatePanel/StatusBadge/SectionHeader), öncelikli ekranların AXAML'i | tamam |
| 2 | Ekran görüntüsü düzeneği: sabit boyut (geniş + dar), güvenli tohum veri, "yeni kayıt" açılış durumu, otomatik taşma/kesilme kontrolü; çıktı `artifacts/ui-ekran/gorsel/` | tamam (`GorselTaramaTests`, 12 ekran) |
| 3 | Tur 1: Bakım · Depo Giriş/Çıkış · Araçlar (+Kiralık) — ortak bileşen önce | tamam |
| 4 | Tur 2: Yakıt · Günlük Faaliyet · Cariler · Kasa/Banka | tamam |
| 5 | Diğer yeni kayıt ekranları — gruplama + ortak düzeltmelerin etkisi; menüdeki "Yeni" ekranlarının eşlemesi | tamam (30 ekran taranıyor) |
| 6 | Build + ilgili UI testleri + regresyon; rapor `docs/raporlar/GORSEL_IYILESTIRME_2026-10-12.md` | tamam |
| 7 | **Kullanıcı bulgusu + onayı (2026-10-12):** formlar sabit kutuya sıkışmasın (ekran yüksekliğine göre); menü "Yeni" girişleri formu açsın; kalan 15+ ekran taransın; commit serbest, yayın kullanıcı kontrolünden sonra | tamam — tam takım + commit |
