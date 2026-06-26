# FAZ 17 - Uçtan Uca Doğrulama, Dokümantasyon ve Yayın Adayı

Bu mesajda **yalnız Faz 17** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **11-14** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Tüm gereksinimleri kanıtlarla kapat, kurulum yapılabilir yayın adayı ve sade kullanıcı rehberi üret.

## Yapılacak işler
1. Gereksinim izlenebilirlik tablosundaki her maddeyi kod/test/kanıt yoluyla kapat veya açık risk olarak yaz.
2. Web unit/integration/e2e, .NET unit/integration/UI smoke ve sync uçtan uca testlerini temiz ortamda çalıştır.
3. Tenant, permission, transaction, idempotency, offline, COMODO, update rollback, backup restore ve import testlerini kanıtla.
4. Yeni kurulum, ilk kullanıcı/enrollment, günlük kullanım, yedek/geri yükleme ve güncelleme kullanıcı rehberlerini hazırla.
5. Üretim env kontrol listesi, migration/rollback, monitoring ve acil durum runbook oluştur.
6. PROJECT_STATE durumunu tamamlandı/açık riskler olarak güncelle; başarısız testi gizleme ve sıradaki faza geçme.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Tüm build/test komutları exit code 0 veya açıkça belgelenmiş engel.
- Kanıt dosyaları mevcut ve tekrar üretilebilir.
- Release candidate checksum ve sürüm bilgisi kaydedilmiş.
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
5. **Faz 17 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
