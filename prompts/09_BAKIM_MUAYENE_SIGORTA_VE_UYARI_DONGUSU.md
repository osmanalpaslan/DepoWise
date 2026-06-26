# FAZ 09 - Bakım, Muayene/Sigorta ve Uyarı Döngüsü

Bu mesajda **yalnız Faz 09** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **6.8-6.9, 7** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Periyodik bakım ve tarih bazlı belgeleri stok/sayaç bağlantılarıyla eksiksiz kur.

## Yapılacak işler
1. Bakım tanımı: ana/alt tür, interval value, km/saat/gün, araç kapsamı ve açıklama.
2. Bakım kaydı: araç, tanım, tarih/sayaç, teknisyen, açıklama ve kullanılan malzeme satırları.
3. Sonraki hedefi ve ilerleme yüzdesini hesapla; <85 sessiz, 85-95 sarı, 95-100 turuncu, >=100 kırmızı.
4. Bakım + malzeme stok düşümü + sayaç + hedef + audit/outbox işlemini atomik yap.
5. İptalde ters stok hareketi ve hedef yeniden hesaplama uygula.
6. Muayene, sigorta, kasko ve kalibrasyon tarihlerini, uyarı üretimini ve araç detay sekmelerini tamamla.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- km/saat/gün sınır ve eşik testleri.
- Yeni bakım uyarıyı kaldırır.
- Bakım malzemesi çift düşmez; iptal geri alır.
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
5. **Faz 09 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
