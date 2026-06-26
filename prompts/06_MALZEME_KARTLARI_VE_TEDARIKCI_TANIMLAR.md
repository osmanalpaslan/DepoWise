# FAZ 06 - Malzeme Kartları ve Tedarikçi/Tanımlar

Bu mesajda **yalnız Faz 06** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **6.4-6.5, 6.16** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Malzeme ana verisini muadil, uyumlu araç, fiyat/para birimi ve fotoğraf ilişkileriyle kur.

## Yapılacak işler
1. Malzeme kartı alanlarını oluştur: kod, ad, tür, kategori/alt kategori, birim, min stok, fiyat, currency, marka, tedarikçi, açıklama.
2. Uyumlu araçlar çoklu seçim ve muadil malzeme ilişkisini çift yönlü ve döngü güvenli kur.
3. Harici muadil notu, fotoğraf bağlantıları ve benzersiz kod kuralını uygula.
4. TL varsayılan; USD/EUR seçenekleri ve işlem anı kur snapshot altyapısını hazırla.
5. Liste arama, keyset pagination, detay paneli, import dry-run ve export oluştur.
6. Açılış stoğunu doğrudan kart alanı olarak değil kontrollü stok açılış hareketi olarak kaydet.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Kod benzersizliği ve tenant kapsamı.
- Uyumlu araç detayı malzeme stoğunu gösterir.
- Açılış stoğu hareket defterinde görünür.
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
5. **Faz 06 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
