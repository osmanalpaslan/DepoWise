# Standart geliştirme çalışma protokolü — VARSAYILAN MOD (kullanıcı kuralı, 2026-08-26)

> ⚠️ Bu dosya, normal geliştirme işlerinde **CLAUDE.md §7 (Ekran QA Motoru V2)** ve önceki kapsamlı
> denetim promptlarının yerine geçer. §7'nin ağır QA süreci (persona testleri, Coverage Matrix,
> tam ekran taraması) artık **yalnız kullanıcı açıkça kapsamlı denetim isterse** çalışır (§11 aşağıda).
> Çelişkide sıra: kullanıcının son açık talebi > bu dosya > CLAUDE.md §7.

Proje daha önce güvenlik, tenant izolasyonu, şube kapsamı, senkron, migration, rapor, performans,
update/checksum ve web/desktop paritesi denetimlerinden geçti. **Bu yüzden her işte projeyi baştan
denetleme.**

## 1. Temel ilke

> İşi **en dar kapsamda** analiz et → **en küçük doğru değişikliği** yap → **ilgili testi** çalıştır →
> doğrula → **bitir**.

Akış: isteği al → ilgili kodu bul → dar etki alanını analiz et → uygula → yalnız ilgili testler →
gerekirse ilgili build → diff kontrolü → bitir.

**Doğruluk > Hız > Token tasarrufu.** Ama doğruluk için görevle ilgisiz kapsamı büyütme.
Hızlı çalışmak "az kontrol etmek" değil; "yalnızca değişikliğin başarısını ve mevcut sistemi
bozmadığını kanıtlayacak kontrolleri yapmak"tır.

## 2. VARSAYILAN OLARAK YAPILMAYACAKLAR

Görev gerçekten gerektirmedikçe: tüm proje taraması · tam güvenlik audit'i · tüm API endpoint
taraması · tüm tenant/şube matrisi · tüm raporların yeniden incelenmesi · PostgreSQL test kümesi ·
mutasyon testleri · **tam test suite** · üç ayrı Release build · tüm migration denetimi · performans
ölçümü · görev dışı refactor · gereksiz dokümantasyon · uzun denetim raporu.

## 3. Baseline ve test kapsamı — kademeli

Baseline: git durumu + ilgili dosyalar. Hepsi bu.

Test sırası: **(1)** hedef testler → **(2)** ilgili modül testleri → **(3)** gerekirse ilgili build →
**(4)** yalnız gerçek etki alanı genişse tam suite.

| Değişiklik | Yeterli olan |
|---|---|
| Masaüstü layout/UI | ilgili desktop testleri + desktop build |
| Web UI | ilgili web testleri + web build |
| Ortak servis (ör. `StockService`) | ilgili servis/API/sync testleri |
| **Tenant · yetki · senkron · veri bütünlüğü** | kapsamı **otomatik genişlet** |

Sadece Avalonia layout değişikliği için PostgreSQL güvenlik testi çalıştırmak gereksizdir.

## 4. Test felsefesi

Yalnız **değişikliğin davranışını kanıtlayan** küçük ve hedefli testler:
(1) yeni davranış çalışıyor, (2) eski hata geri dönmüyor, (3) gerçekten önemli sınır durumları.

Test **sayısı** kalite değildir; testin hata yakalaması kalitedir. Bir forma opsiyonel alan
eklerken 30 senaryo değil, birkaç kritik senaryo yaz.

**Mutasyon (kasten bozma) yapma** — istisna: güvenlik · veri bütünlüğü · yetki · tenant · senkron ·
finans/stok hesabı · kritik iş kuralı.

## 5. Dokunulmayacak yapılar

Görevle **doğrudan** ilgili değilse: tenant izolasyonu · firma kapsam kuralları · `BranchAccess` ·
`BranchService` · yetki mimarisi · `AppScreens` · rapor dispatch · senkron firma kapıları ·
idempotency · update/checksum/release · migration runner ve kataloğu · mevcut veritabanı yapısı ·
ortak servis mimarisi · web/desktop ortak iş mantığı · tarih semantiği · audit · güvenlik kapıları.

"Daha temiz/modern/doğru olur" gerekçesiyle görev dışı refactor **yok**. Bug düzeltirken komponenti
yeniden yazma, isim temizliği yapma, ilgisiz uyarıları toplama, dosya düzenini değiştirme.

## 6. Katman disiplini

- UI sorunu görünce hemen API/DB değiştirme: **"veri mi yanlış, gösterim mi?"** sorusunu önce kanıtla.
- API sorunu görünce: **"API mi yanlış, UI mı yanlış kullanıyor?"** kontrol et.
- Web + masaüstü ortak davranış gerektiriyorsa **ortak servis/model/API'yi** kullan; aynı iş kuralını
  iki yere yazma. Ayrışma yalnız UI katmanında olur.

## 7. Migration

Önce mevcut şemada alan/altyapı var mı bak. Kullanılabiliyorsa **yeni migration açma**.
Gerçekten gerekiyorsa: neden gerekli · mevcut yapı neden yetmiyor · hangi tablolar · geriye dönük
etki → **kullanıcı onayı olmadan açma**.

## 8. Performans ve güvenlik

- Küçük görevlerde performans denetimi yapma. Ölçmeden indeks/cache/sayfalama/sorgu değişikliği ekleme.
- Güvenlikle ilgisiz görevlerde güvenlik sistemini yeniden denetleme. Ama değişiklik
  authorization/authentication/tenant/şube/dosya yolu/kullanıcı girdisi/SQL/dosya yükleme/senkron/veri
  erişimini etkiliyorsa **ilgili sınırı mutlaka kontrol et**.
- Güvenlik açığı bulursan görmezden gelme: **etkilenen sınırı düzelt + testini ekle**, ama tüm projeyi
  taramaya geçme.

## 9. Kapsam dışı bulgu

İlgisiz bir problem fark edersen düzeltme, tarama başlatma. Tek satır kaydet:
*"İlgisiz bulgu: X. Bu görev kapsamında değiştirilmedi."*
**İstisna:** aktif güvenlik açığı veya veri kaybı riski → kullanıcıyı uyar.

## 10. Onay gerektiren durumlar

Yeni migration · veri modeli değişikliği · iş kuralı değişikliği · kullanıcı erişimini daraltma ·
tenant/şube/yetki davranışı · senkron protokolü · release/update mekanizması · üretim verisi dönüşümü.

Bunlarda: *"Bu görev şu nedenle mevcut davranışın dışına çıkıyor: X. Onay gerekli."* de ve **dur**.
İstisna: acil ve açıkça doğru güvenlik düzeltmesi → güvenli minimum düzeltmeyi yap ve raporla.

## 11. Üretim ve yayın

Normal geliştirmede üretim veritabanına test verisi/SQL/DDL/silme/ACL/secret **yok**; gerekirse
salt-okunur kontrol. **Yayın, açıkça istenmedikçe görevin parçası değildir** — build/test seviyesinde bırak.

## 12. Bitirme koşulu

Şu dördü sağlanınca **bitir**: istenen davranış uygulandı · ilgili testler geçiyor · ilgili build
geçiyor · mimari gereksiz bozulmadı. "Başka ne bulabilirim?" moduna geçme.

## 13. Kapsamlı denetim modu (geçici)

Kullanıcı açıkça *"tam denetim yap / baştan sona tara / güvenlik denetimi / kapsamlı audit /
stabilizasyon turu / her şeyi kontrol et"* derse bu protokolü **geçici olarak bırak**, kapsamlı denetim
moduna geç. Sonraki normal görevde bu protokole **geri dön**.

## 14. Çalışma sonu raporu (kısa)

```
## Tamamlandı
**Yapılan:** ...
**Değişen ana alanlar:** ...
**Test:** İlgili testler: X geçti / Y başarısız · Build: başarılı/başarısız
**Migration:** Gerekti / Gerekmedi
**Ek not:** varsa yalnız önemli not
```

Hata yoksa uzun denetim raporu yazma.

## 15. Değişmeyen kurallar

Bu protokol şunları **iptal etmez**: CLAUDE.md §0 (git daima güncel, commit+push, durum dosyaları),
§2.1 (her işin başında motor önerisi + onay), `.claude/rules/platform-priority.md` (masaüstü önce,
web eksik bırakılmaz), `.claude/rules/testing.md` (flaky testi retry ile gizleme).
