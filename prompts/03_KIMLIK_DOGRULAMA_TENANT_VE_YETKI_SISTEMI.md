# FAZ 03 - Kimlik Doğrulama, Tenant ve Yetki Sistemi

Bu mesajda **yalnız Faz 03** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **4, 6.2-6.3, 9** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Süper Admin, Firma Admini ve diğer roller için UI + API düzeyinde fail-closed erişim kontrolü kur.

## Yapılacak işler
1. Güçlü parola hash, login/logout, güvenli session cookie ve masaüstü session akışını uygula.
2. Web ve masaüstü login rate limit/5 hatada geçici kilit davranışını kur.
3. Rol, permission, menu, action, field ve special_button yetkilerini modelle.
4. Süper Admin oluşturma ve firma değiştirme kurallarını; Firma Admini için otomatik firma kapsamını uygula.
5. company_id değerini yalnız session/server context üzerinden al; payload company_id alanlarını reddet.
6. Tenant sızıntısı, yetki yükseltme ve gizli buton/API testlerini yaz.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Farklı tenant erişimi 404/403 güvenli sonucu verir.
- Yetkisiz + butonu görünmez ve doğrudan API de reddedilir.
- Session ve kilit testleri geçer.
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
5. **Faz 03 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
