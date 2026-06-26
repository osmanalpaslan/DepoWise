# FAZ 13 - Dosya/Fotoğraf, Audit, Çöp Kutusu ve Yedek

Bu mesajda **yalnız Faz 13** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **6.16-6.18, 9** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Dosya güvenliğini, işlem izini, geri alma ve yerel veri korumasını tamamla.

## Yapılacak işler
1. file_records + storage provider arayüzü; geliştirmede local storage, üretimde değiştirilebilir sağlayıcı.
2. Fotoğrafı max 1200 px/JPEG kalite hedefiyle optimize et; 7 MB, MIME ve magic-byte kontrolü uygula.
3. Audit logda önce/sonra özetleri, actor, tenant, correlation id ve zaman tut; secret/PII loglama.
4. Master data için soft-delete/restore; operasyonel kayıtlar için iptal/ters kayıt ekranı uygula.
5. Çöp Kutusu erişimini admin/özel yetki ve yeniden doğrulama ile sınırla.
6. Masaüstü SQLite otomatik yedek, saklama, bütünlük kontrolü ve gerçek geri yükleme testini kur.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Sahte dosya ve büyük dosya reddedilir.
- Geri yükleme permission testi geçer.
- Yedekten açılan DB integrity check geçer.
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
5. **Faz 13 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
