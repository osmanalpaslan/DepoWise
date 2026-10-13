# 00 — Araç, MCP ve eklenti değerlendirmesi (A0, 2026-10-12)

> Talep §8: gerekli araçlar onaysız kurulabilir; her biri için amaç, kaynak, lisans, güvenlik ve tekrar kontrolü.
> Sonuç: **bu aşamada yeni araç kurulmadı** — mevcut araçlar her ihtiyacı karşıladı. Kurmamak bilinçli karardır.

| Araç | Durum | Amaç | Karar ve gerekçe |
|---|---|---|---|
| Yerleşik web arama + sayfa okuma (WebSearch/WebFetch) | var | Rakip, belge, açık kaynak araştırması (A1, A5) | Yeterli; 175 kaynak bununla toplandı. |
| deep-research becerisi (skill) | var | Çok kaynaklı, paralel araştırma ve kaynaklı rapor | A1'de kullanıldı. |
| Context7 MCP (kütüphane belgeleri) | projede tanımlı, KAPALI | Avalonia/.NET belge araması | Açılmadı: resmî belgeler doğrudan okunabildi; açmak oturum yeniden başlatma ister, fayda/maliyet düşük. |
| Playwright MCP | projede tanımlı, KAPALI | Tarayıcı otomasyonu | Açılmadı: `tests/web-e2e` npm Playwright aynı işi görüyor (tekrar olurdu). |
| Serena MCP (kod gezinme) | genel tanımlı | Sembol/başvuru arama | Bu oturum proje klasöründe açılmadığı için yüklenmedi; grep/okuma yeterli oldu. Proje klasöründe açılan oturumda kullanılabilir. |
| Avalonia Headless (proje içi) | var | Masaüstü ekran görüntüsü ve piksel ölçümü | A5 tasarım değerlendirmesinde kullanıldı. |
| `dotnet list package --vulnerable/--outdated` | yerleşik | Bağımlılık güvenlik taraması | A3a'da çalıştırıldı (SQLite paket açığı bulundu → M-07). |
| Ölçüm uygulaması (depo dışı, scratchpad/perf) | geçici | 200/300 bin kayıt performans ölçümü (A3b) | Depoya girmedi; yeniden üretme adımları `07` §6'da. |
| dotnet-counters / dotnet-trace (Microsoft, ücretsiz) | kurulmadı | Bellek/CPU profili | A3b ölçümü bunlarsız yapılabildi; B3 performans fazında gerekirse kurulacak. |

Güvenlik notu: hiçbir aşamada üretim veritabanına, canlı API'ye veya yayın betiklerine bağlanılmadı; ölçümler yerel
geçici SQLite ve yerel test PostgreSQL'inde yapıldı, geçici veriler silindi.
