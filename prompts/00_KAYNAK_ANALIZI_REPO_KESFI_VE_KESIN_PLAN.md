# FAZ 00 - Kaynak Analizi, Repo Keşfi ve Kesin Plan

Bu mesajda **yalnız Faz 00** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **1-3, 12-14** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Kod yazmadan önce mevcut klasörü, araçları ve gereksinimleri doğrula; belirsizlikleri karar kaydına çevir ve güvenli uygulama sırasını kesinleştir.

## Yapılacak işler
1. Repo ve mevcut dosya envanterini çıkar; varsa çalışan kodu, testleri ve kullanıcı değişikliklerini koru.
2. CLAUDE.md ile docs/DEPOWISE_ANALYSIS.md arasında çelişki olmadığını kontrol et.
3. Mimari kararları ADR biçiminde docs/DECISIONS.md içine yaz: web/API, desktop, local DB, central DB, file storage, sync ve auth.
4. Gereksinim izlenebilirlik tablosunu fazlara bağla; eksik veya çelişkili maddeleri risk olarak işaretle.
5. COMODO koruma dosyalarını, gerçek DB yolu stratejisini ve güvenli build/run komutlarını doğrula.
6. Mevcut baseline build/test mümkünse çalıştır; proje boşsa yalnız iskelet planı hazırla, sonraki faza geçme.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Git durumunu ve mevcut değişiklikleri raporla.
- PROJECT_STATE, DECISIONS, KNOWN_ISSUES ve TEST_EVIDENCE dosyalarını güncelle.
- Sıradaki tek iş Faz 01 olsun.
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
5. **Faz 00 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
