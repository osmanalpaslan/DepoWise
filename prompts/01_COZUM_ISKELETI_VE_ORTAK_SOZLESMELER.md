# FAZ 01 - Çözüm İskeleti ve Ortak Sözleşmeler

Bu mesajda **yalnız Faz 01** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **3, 13** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Web, masaüstü, ortak dokümantasyon ve test yapısını küçük ama çalışır bir temel halinde kur.

## Yapılacak işler
1. Monorepo klasörlerini oluştur/uyarla: apps/web, src/DepoWise.Domain, Application, Infrastructure, Desktop ve tests.
2. Next.js TypeScript strict, Drizzle/PostgreSQL; .NET 8 Avalonia, Dapper/SQLite ve nullable ayarlarını kur.
3. API /api/v1 hata modeli, correlation_id, UTC zaman sağlayıcı, pagination sözleşmesi ve OpenAPI temelini oluştur.
4. Merkezi config doğrulamasını fail-closed yap; .env.example ve gitignore düzenle.
5. Directory.Build.props Debug UseAppHost=false ve COMODO güvenli komutlarını doğrula.
6. Basit health endpoint, masaüstü açılış health kontrolü ve smoke test ekle.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Web lint/typecheck/build.
- .NET restore/build/test.
- Uygulama test çalıştırması yalnız dotnet host ile.
- İlgili build, lint/typecheck ve test komutlarını çalıştır. Başarısız sonucu saklama.
- Kanıtı `docs/TEST_EVIDENCE.md` içine komut, exit code, sonuç ve log yolu ile yaz.

## Faz sonu zorunlu güncelleme
- `docs/PROJECT_STATE.md`: tamamlananlar, açık işler, sıradaki tek iş.
- `docs/DECISIONS.md`: alınan teknik karar ve gerekçesi.
- `docs/KNOWN_ISSUES.md`: kalan riskler ve etkisi.
- Gereksinim kimliklerini `docs/REQUIREMENTS_TRACEABILITY.md` içinde kod/test yollarıyla eşleştir.

## Kullanıcıya verilecek kısa sonuç
1. Yapılanlar (en fazla 6 madde)
2. Değişen dosyalar
3. Test/build sonucu
4. Açık risk veya engel
5. **Faz 01 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
