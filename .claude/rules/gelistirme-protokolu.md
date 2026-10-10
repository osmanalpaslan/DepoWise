# Geliştirme protokolü — varsayılan mod (kullanıcı kuralı 2026-08-26, sadeleştirme 2026-10-10)

**İlke:** isteği al → ilgili kodu bul → dar etki alanını analiz et → en küçük doğru değişiklik → ilgili testler +
ilgili build → diff kontrolü → bitir. Doğruluk > hız > token. Proje daha önce kapsamlı denetimlerden geçti; her işte baştan denetleme.

## Yapılmayacaklar (görev gerektirmedikçe)
Tüm proje/güvenlik/endpoint taraması · görev dışı refactor, isim temizliği, ilgisiz uyarı düzeltme · gereksiz doküman/uzun rapor ·
ölçmeden indeks/cache/sorgu değişikliği. İlgisiz bulguyu tek satır not et; **istisna:** aktif güvenlik açığı / veri kaybı riski → uyar.

## Test kapsamı (kademeli)
1. hedef testler → 2. ilgili modül testleri → 3. ilgili build → 4. etki geniş ise tam takım.
Masaüstü UI → ilgili desktop testleri + build · Web UI → web testleri + build · ortak servis → servis/API/senkron testleri ·
**tenant/yetki/senkron/veri bütünlüğü → kapsamı otomatik genişlet.** Test sayısı değil, hata yakalaması önemli.
Mutasyon (kasten bozma) yalnız güvenlik · tenant · senkron · stok/finans hesabı · kritik iş kuralında.

## Katman disiplini
UI'da hata görünce önce "veri mi yanlış, gösterim mi?" kanıtla; ortak davranış ortak servis/model/API'de, ayrışma yalnız UI'da.
Dokunma (görevle doğrudan ilgili değilse): tenant izolasyonu, `BranchAccess`/`BranchService`, yetki mimarisi, `AppScreens`,
rapor dispatch, senkron firma kapıları, idempotency, update/checksum/release, migration runner, audit, tarih semantiği.

## Onay gerektirenler — "şu nedenle mevcut davranışın dışına çıkıyor: X. Onay gerekli." de ve dur
Yeni migration · veri modeli · iş kuralı değişikliği · erişimi daraltma · tenant/şube/yetki davranışı · senkron protokolü ·
release/update mekanizması · üretim verisi dönüşümü. (Kullanıcı isteği bunu açıkça kapsıyorsa onay sayılır; gerekçeyi yaz.)
Acil ve açıkça doğru güvenlik düzeltmesi: güvenli minimumu yap, raporla.

## Bitirme
İstenen davranış uygulandı · ilgili testler + build geçti · mimari gereksiz bozulmadı → bitir, "başka ne bulurum" moduna geçme.
Kapsamlı denetim yalnız kullanıcı açıkça isterse: `docs/arsiv/QA_MOTORU_V2.md`.
