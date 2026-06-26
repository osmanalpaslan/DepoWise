# FAZ 02 - Veritabanı Temeli, Audit ve Ortak Veri Kuralları

Bu mesajda **yalnız Faz 02** çalışılacak. Faz tamamlanınca dur; sonraki fazı başlatma.

## Zorunlu başlangıç
1. Kök `CLAUDE.md` dosyasını uygula.
2. `docs/PROJECT_STATE.md`, `docs/DECISIONS.md`, `docs/KNOWN_ISSUES.md` ve son `docs/TEST_EVIDENCE.md` kayıtlarını oku.
3. `docs/DEPOWISE_ANALYSIS.md` içinden yalnız **3, 7-8** ile ilgili bölümleri oku. Bütün prompt dosyalarını okuma.
4. Hedefli glob/grep ile mevcut kodu keşfet. Çalışan yapıyı sıfırdan kurma, kullanıcı değişikliklerini silme.
5. Önce 5-10 maddelik uygulama planını kendi çalışma notuna çıkar; kullanıcıya uzun plan dökme.

## Amaç
Merkezi PostgreSQL ve yerel SQLite için güvenli, sürümlü ve tenant uyumlu veri temelini kur.

## Yapılacak işler
1. Şirket, şube/şantiye, users, roles, permissions, audit_logs, file_records, sync cihaz/outbox/inbox ve migration tablolarını tasarla.
2. Tüm operasyonel tablolarda kimlik, company_id, created/updated timestamps, version ve uygun soft-delete alanlarını standartlaştır.
3. Para için decimal + currency_code; zaman için UTC ve Unix ms sınır sözleşmesini belirle.
4. SQLite bağlantısını mutlak LocalAppData yolu, Cache=Private, WAL, foreign_keys=ON ve busy_timeout ile kur.
5. Migration runner ve schema version uyumluluğu ekle.
6. Tenant helper, soft-delete helper, keyset pagination ve audit servislerinin testlerini yaz.

## Kesin kurallar
- Web ve masaüstü ilgili özellikte aynı iş kuralı, doğrulama ve yetki sonucunu üretmeli.
- Tenant ve permission kontrolleri yalnız UI'a bırakılmayacak; API/servis katmanında fail-closed uygulanacak.
- Stok/sayaç/yakıt/bakım/onay gibi kritik işlemde transaction ve idempotency kurallarını atlama.
- Gereksiz refactor, paket yükseltme veya sonraki faz işi yapma.
- Secret, üretim verisi veya ham kişisel veri loglama.
- Geliştirme makinesinde proje EXE/BAT çalıştırma; dotnet host ve gerçek DB kurallarına uy.

## Doğrulama
- Migration sıfır DB ve mevcut DB üzerinde güvenli çalışır.
- Tenant filtresi unutulduğunda test kırılır.
- SQLite pragma ve gerçek dosya yolu testle doğrulanır.
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
5. **Faz 02 tamamlandı mı?** Evet/Hayır
6. Sıradaki tek iş; ancak kendiliğinden başlama
