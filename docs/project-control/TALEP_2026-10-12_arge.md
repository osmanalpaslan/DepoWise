# Kullanıcı talebi 2026-10-12 — Kapsamlı sektör araştırması, rakip analizi, teknik AR-GE ve sistem modernizasyonu

> **KESİN KURAL (kullanıcı):** bu çalışmanın HİÇBİR aşamasında canlıya yayın, üretim DB migration'ı, dağıtım ya da
> yayın otomasyonu YOK. CLAUDE.md §5'teki "otomatik yayın" kuralı bu çalışma için ASKIDADIR. Yayın öncesi kontroller
> yalnız geliştirme/test ortamında. Çalışma dalı: `arge/2026-10-kapsamli` (master'a birleştirme kullanıcı kararıyla).
> Diğer sınırlar: kullanıcı değişikliklerini koru · üretim verisi testte kullanılmaz · testler gevşetilmez ·
> doğrulanmamış iş "tamam" raporlanmaz · ürün/iş kuralı kararı gerekmedikçe kullanıcıya soru sorulmaz.
> Araç/MCP kurulumu serbest (gerekçe + güvenlik değerlendirmesiyle).

Çıktılar: `docs/arge/` (dizin haritası `docs/arge/README.md`).

## Fazlar (durum burada tutulur)
| Faz | İş | Durum |
|---|---|---|
| A0 | Proje + araç denetimi, çalışma dalı, belge iskeleti | tamam |
| A1 | Sektör ve rakip araştırması (dış kaynak) | tamam — 6 alan, 175 kaynak, 52 eksik adayı |
| A2 | Mevcut sistem envanteri (mimari, modül, ekran/alan/buton, akış) | tamam — 02, 03 |
| A3 | Arka plan teknik süreçleri, güvenlik, performans, veri/senkron denetimi | tamam — 04, 07 (200/300 bin ölçüm) |
| A4 | Test kapsamı ve doğrulama denetimi | tamam — 06 |
| A5 | Avalonia tasarım araştırması → tasarım sistemi | tamam — 08 |
| A6 | Fark analizi (8 kategori) + önceliklendirilmiş yol haritası | tamam — 05 (190 bulgu, 18 P0), 09 (22 karar + 85 madde) → kullanıcı seçimi alındı: `docs/arge/13_kullanici_secimleri.md` |
| B* | Kontrollü geliştirme fazları (yol haritası + 13 seçimleri) | B2 ✅ (.NET 10 yayında) · B1 ✅ kapandı (2026-10-18) · B3… bekliyor |
| Z | Nihai doğrulama, kalan riskler, yayın öncesi kontrol listesi | bekliyor |

---

## Kullanıcının talep metni (aynen)

ALPNEX — KAPSAMLI SEKTÖR ARAŞTIRMASI, RAKİP ANALİZİ, TEKNİK AR-GE VE SİSTEM MODERNİZASYONU.
Bölümler: 1 temel amaç · 2 dış kaynak ve sektör araştırması (2.1 benzer yazılımlar, 2.2 ekran görüntüleriyle
sınırlama) · 3 işlev/ekran/alan/buton analizi (3.1 işlevsel kapsam, 3.2 ekran envanteri, 3.3 kullanıcı deneyimi ve iş
akışı) · 4 arka plan teknik süreçleri (veri ve iş kuralları, arka plan görevleri, API ve entegrasyonlar, güvenlik,
performans ve güvenilirlik) · 5 test ve kalite güvencesi · 6 veritabanı, senkronizasyon ve çevrimdışı (200–300 bin kayıt
performans ölçümü) · 7 modern Avalonia UI araştırması · 8 MCP/eklenti/araç kurulumu (onaysız, gerekçeli) · 9 araştırmayı
Alpnex ile karşılaştırma (8 kategori; her öneride modül, mevcut davranış, bulgu+kaynak, fayda, risk, bağımlılık,
karmaşıklık, öncelik, kabul kriteri) · 10 faz planı ve uygulama yönetimi · 11 değişiklik güvenliği ve yayın yasağı ·
12 14 çıktı belgesi · 13 nihai tamamlanma kriterleri. Öncelik: araştırma → mevcut sistem analizi → fark analizi →
yol haritası → kontrollü geliştirme → test ve doğrulama → nihai kabul. "Hiçbir koşulda yayın yapma."
(Tam metin oturum kaydında; bu dosya bağlayıcı özet + durum tablosudur.)
