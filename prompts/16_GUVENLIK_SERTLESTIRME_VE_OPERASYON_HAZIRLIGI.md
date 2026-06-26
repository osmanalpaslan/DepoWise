# FAZ 16 - Güvenlik Sertleştirme ve Operasyon Hazırlığı

Bu mesajda **yalnız Faz 16** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **9, 11** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Yayın öncesi güvenlik boşluklarını ve operasyonel kontrolleri kapat.

## Yapılacak işler
1. CSP, HSTS, X-Content-Type-Options, frame-ancestors/X-Frame, Referrer-Policy ve HTTPS davranışını uygula.
2. Login/sync/admin rate limits, CSRF yaklaşımı, CORS ve request body limitlerini doğrula.
3. Secret başlangıç doğrulama ve rotasyon runbook; log redaction ve PII minimizasyonu ekle.
4. npm/NuGet audit, lock dosyası ve bağımlılık risklerini raporla; kritik açıkları çöz.
5. Admin audit kapsamını, cihaz token rotasyonu/revoke cascade ve dosya erişim izinlerini test et.
6. MFA, code-signing ve bağımsız pentest için maliyet/öncelik kaydı oluştur; temel güvenlikten ayrı tut.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Security header testleri.
- Brute-force/rate-limit testleri.
- Repo secret taraması temiz; loglarda ham secret yok.
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
5. **Faz 16 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
