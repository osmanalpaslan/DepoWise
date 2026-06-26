# FAZ 10 - Yakıt Sarfiyatı ve Günlük Faaliyet

Bu mesajda **yalnız Faz 10** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **6.10-6.11, 7** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Yakıt depo/dağıtım maliyetini ve günlük araç hareketlerini sayaç bütünlüğüyle kur.

## Yapılacak işler
1. Yakıt depo girişi: tedarikçi, litre, fiyat, currency, fatura/not, tarih ve yakıt bakiyesi.
2. Araç dağıtımı: araç, tarih, önceki/güncel sayaç, litre, fiyat snapshot ve yakıtı veren personel.
3. Yakıt dağıtımı + depo bakiyesi + araç sayacı/log + audit/outbox atomik olsun.
4. Günlük faaliyet kayıt tipi hareket/transfer/bakım; bakım seçilirse ortak bakım servisini kullan ve tek kayıt üret.
5. Nereden/nereye, operatör, süre ve açıklama alanlarını uygula.
6. Tüketim raporu için güvenli hesaplamalar ve veri kalite uyarıları ekle.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Yakıt stoğu ve araç sayacı tutarlı.
- Günlük Faaliyet bakım kaydını çoğaltmaz.
- Fiyat snapshot geçmişte değişmez.
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
5. **Faz 10 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
