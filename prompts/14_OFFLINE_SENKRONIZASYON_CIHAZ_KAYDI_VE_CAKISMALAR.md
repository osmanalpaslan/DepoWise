# FAZ 14 - Offline Senkronizasyon, Cihaz Kaydı ve Çakışmalar

Bu mesajda **yalnız Faz 14** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **6.20, 8-10** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Masaüstünün internetsiz çalışmasını ve güvenilir merkezi senkronizasyonu kur.

## Yapılacak işler
1. Yerel write + outbox aynı SQLite transaction içinde; operation_id, entity id, version ve payload hash tut.
2. Tek kullanımlık 10 dk install/enrollment anahtarı, cihaz kimliği, onay/revoke ve DPAPI token saklama uygula.
3. Push accepted/rejected/conflict; pull cursor ve sayfa rollback protokolünü uygula.
4. Stok/sayaç/yakıt/bakım/onayda LWW kullanma; sunucu otoriteli ve manuel conflict akışı tasarla.
5. Düşük riskli kart alanlarında version/updated_at ile kontrollü merge politikası kullan.
6. Retry/backoff, bağlantı kesilmesi, uygulama yeniden başlatma ve 0-100 non-blocking ilerleme göstergesini tamamla.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Offline kalıcılık ve yeniden açılış.
- Retry çift kayıt üretmez.
- Geçersiz pull cursor ilerletmez; revoked cihaz 403 alır.
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
5. **Faz 14 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
