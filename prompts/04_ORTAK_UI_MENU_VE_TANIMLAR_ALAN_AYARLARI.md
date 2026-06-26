# FAZ 04 - Ortak UI, Menü ve Tanımlar/Alan Ayarları

Bu mesajda **yalnız Faz 04** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **5, 6.4** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Tüm modüllerin kullanacağı menü, form, arama, çoklu seçim ve dinamik alan altyapısını kur.

## Yapılacak işler
1. Platforma uygun accordion/sidebar menü, route guard ve yüklenme göstergesi oluştur.
2. NumericUpDown, masked date, searchable single/multi-select, seçili sayısı, filtre sonucunu tümünü seç/kaldır davranışlarını ortak bileşen yap.
3. Tanımlar ve Alan Ayarları ekranında lookup alanı, çoklu seçim, fotoğraf alanı ve + butonu özelliklerini tanımla.
4. Alan ve + butonu görünürlüğünü permission ile bağla; ayar değişikliklerini audit et.
5. Modal/pencere minimum boyut, scroll, responsive yerleşim ve klavye erişilebilirliğini uygula.
6. Web ve masaüstü için aynı doğrulama senaryolarını test et.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Arama sırasında seçimler kaybolmaz.
- Tümünü seç yalnız filtrelenenleri ekler.
- Geçersiz tarih ve negatif sayılar kaydedilemez.
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
5. **Faz 04 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
