# FAZ 11 - Malzeme Talep, Onay ve PDF

Bu mesajda **yalnız Faz 11** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **6.12, 7** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Stoğu doğrudan etkilemeyen, izlenebilir ve yetkili malzeme talep/onay akışını kur.

## Yapılacak işler
1. Belge numarasını tenant/yıl bazında güvenli ve benzersiz üret.
2. Talep başlığı ve kalemleri: tarih, şantiye, talep eden, depo sorumlusu, onaycı, açıklama, malzeme, miktar ve araç.
3. Durum makinesi: taslak/beklemede/onaylı/reddedildi/iptal; geçişleri rol/permission ile sınırla.
4. Onay işleminin stok bakiyesini değiştirmemesini garanti et.
5. Talep Onaylama ekranı, audit geçmişi ve PDF çıktısını web/masaüstünde tamamla.
6. Onaylı talepten kontrollü stok çıkış belgesi başlatma bağlantısı ekle; otomatik düşüm yapma.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Çift onay ve yetkisiz geçiş engellenir.
- Onay sonrası stok aynı kalır.
- PDF tenant ve Türkçe karakterlerle doğru oluşur.
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
5. **Faz 11 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
