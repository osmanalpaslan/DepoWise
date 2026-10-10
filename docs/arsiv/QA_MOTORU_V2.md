# Ekran QA Motoru V2 — kapsamlı denetim modu (CLAUDE.md §7'den arşivlendi, 2026-10-10)

> Yalnız kullanıcı açıkça "tam denetim / baştan sona tara / stabilizasyon turu" derse uygulanır.

## 7. Test ve bitirme — Ekran QA Motoru V2 (kullanıcı kuralı, 2026-07-12)
> ⏸️ **VARSAYILAN OLARAK DEVRE DIŞI (2026-08-26, kullanıcı kuralı).** Normal geliştirme işlerinde artık
> **`.claude/rules/gelistirme-protokolu.md`** geçerlidir: en dar kapsam, en küçük doğru değişiklik,
> yalnız ilgili testler. §7'nin ZORUNLU ağır QA süreci (persona testleri, 7.13 Coverage Matrix,
> 7.14 Test Raporu) **yalnız kullanıcı açıkça kapsamlı denetim isterse** ("tam denetim yap",
> "baştan sona tara", "stabilizasyon turu" vb.) çalışır. §7.16'daki kritik testler (tenant, permission,
> rollback, negatif stok, sayaç, idempotency, offline) ise ilgili katmana dokunulduğunda **her zaman**
> geçerlidir.
>
> Bu projede yalnızca geliştiren değil; aynı zamanda **Senior QA / Test Automation / Manual Tester /
> UX Tester / Security Tester / Performance Tester** gibi davranılır.
>
> **7.0 Token disiplini (aktifken de geçerli — kullanıcı kuralı 2026-07-21).** QA israfa dönüşmesin:
> - Kapsam **yalnız değiştirilen ekran** (7.1). Çalışan başka yere dokunma.
> - Coverage Matrix ve rapor **kısa tablo** olur; yanıta tam rapor yapıştırılmaz — dosyaya yazılır,
>   yanıtta yalnız *"X geçti / Y bulgu"* özeti verilir.
> - Aynı senaryo iki kez koşturulmaz; log dosyaya, yanıta yalnız ilgili hata satırı.
>
> **7.0.1 Test hesabı.** Canlı/uçtan-uca QA'de **yalnız** `.env.test.local` içindeki test kullanıcısı
> (`DEPOWISE_TEST_USER`) kullanılır. Gerçek yönetici hesapları (superadmin, mustafa.alpaslan) testte
> kullanılmaz. Parola hiçbir dosyada git'e girmez (`.env.*` ignore'da).

### 7.1 Kapsam — EN KRİTİK KURAL
- Her geliştirme tamamlandıktan sonra **SADECE değiştirilen ekran** test edilir (örn. Personel değiştiyse
  yalnız Personel; Araç değiştiyse yalnız Araç). **Başka ekrana dokunulmaz.**
- Genel regresyon testi **yalnızca kullanıcı açıkça isterse** yapılır — kendiliğinden yapılmaz.
- Yeni oluşturulan ekranlar da bu kurala otomatik dahildir.
- **Kod tamamlanmış sayılmaz.** İlgili ekranın QA süreci (bkz. 7.13 Coverage Matrix + 7.14 Test Raporu)
  bitmeden geliştirme bitmiş kabul edilmez.

### 7.2 İnsan gibi test et (persona'lar)
Gerçek kullanıcı · ilk defa kullanan · depo görevlisi · şantiye şefi · muhasebeci · firma yöneticisi ·
süper admin · yetkisiz kullanıcı · kötü niyetli kullanıcı · çok hızlı çalışan · çok yavaş çalışan.
Amaç **hata bulmaktır**.

### 7.3 Alan ve etkileşim kapsamı
Textbox, textarea, numeric, dropdown/combobox, autocomplete, arama, filtre, checkbox, radio, date/time
picker, treeview, tabs, grid/datagrid, context menu, toolbar, popup, modal, buton, icon buton.
Kısayollar: Enter, Tab, Shift+Tab, Esc, Delete, Insert, F2, Ctrl+C/V/X, çift tık, sağ tık, mouse wheel,
scroll, drag&drop.

### 7.4 Veri senaryoları
Boş, null, 0, 1, -1, min, max, ondalık (virgül/nokta), çok büyük/küçük sayı, emoji, unicode, Türkçe
karakter, HTML/CSS/JS/SQL Injection/XSS/script, JSON/XML, 100/500/1000/10000 karakter, tek/çift/baş/son
boşluk, yalnız sayı, yalnız harf, karışık veri, kopyala-yapıştır, satır sonu, TAB karakteri.

### 7.5 Form senaryoları
Yeni kayıt, düzenleme, silme, iptal, kaydet, kaydetmeden çık, hızlı/çift kayıt, aynı kod/isim, pasif/aktif
kayıt, filtreli/arama-sonrası kayıt, kayıt sırasında hata.

### 7.6 Grid senaryoları
Kolon sıralama/gizleme/genişletme, filtre, çoklu filtre, arama, sayfalama, boş liste, 1/100/1000/10000
kayıt, performans, scroll, seçim, çift/sağ tıklama.

### 7.7 Yetki senaryoları
Süper Admin, Firma Admin, Yönetici, Depo Kullanıcısı, Personel, Salt Okunur, Yetkisiz — her rol için
**ayrı ayrı**: menüler, butonlar, alanlar, export, import, silme, düzenleme, yeni kayıt, rapor.

### 7.8 Veritabanı kontrolleri
Kayıt oluştu mu, duplicate oluştu mu, rollback doğru çalıştı mı, transaction tamamlandı mı, audit/history
oluştu mu, sync kuyruğu oluştu mu, offline kayıt doğru mu, soft delete doğru mu, ilişkili tablolar doğru
güncellendi mi.

### 7.9 UI testleri
Responsive, hizalama, boşluklar, yazı taşması, yanlış ikon/renk, tooltip, placeholder, label, sekme/odak
sırası, scrollbar, popup, modal, koyu/açık tema.

### 7.10 UX testleri
Kullanıcı burada hata yapabilir mi, buton ismi anlaşılır mı, mesaj/hata mesajı açık ve yeterli mi, işlem
gereksiz uzun mu, fazladan tıklama var mı, klavye ile kullanılabiliyor mu.

### 7.11 Performans testleri
Liste açılışı, filtreleme, arama, kaydetme, silme, düzenleme, import, export, render, bellek kullanımı,
gereksiz/tekrarlayan sorgular.

### 7.12 Güvenlik testleri
SQL Injection, XSS, HTML Injection, yetki atlama, URL/parametre manipülasyonu, boş yetki, çift gönderim,
race condition.

### 7.13 Coverage Matrix
Her geliştirme sonunda şu liste oluşturulur ve tamamlanan maddeler işaretlenir: Form Açıldı · Yeni Kayıt ·
Düzenleme · Silme · Arama · Filtre · Grid · Doğrulamalar · Yetki · Hata Mesajları · Database · Offline ·
Sync · Performans · UI · UX · Security.

### 7.14 Test raporu
Her geliştirme sonunda `docs/tests/<EkranAdi>_Test_Report.md` oluşturulur. İçerik: geçen testler, bulunan
hatalar, riskler, performans, coverage, tahmini test kapsamı, çalıştırılan senaryo sayısı.

### 7.15 Hata bulunursa
Kod yazmadan önce analiz et. Her hata için: öncelik, risk, tekrar üretme adımları, beklenen sonuç, gerçek
sonuç, muhtemel neden, çözüm önerisi yaz — **sonra** düzelt, tekrar test et; sorun kalmayana kadar döngüyü
sürdür. Başarısız testi gizleme veya yalnız tekrar çalıştırıp geçme.

### 7.16 Diğer
- Her değişiklikte en dar test; faz sonunda build + ilgili unit/integration/e2e.
- Kritik testler (proje geneli, ekrandan bağımsız): tenant sızıntısı, permission, rollback, negatif stok,
  sayaç geriye gitme, idempotent retry, offline kalıcılık, update rollback.
- `docs/PROJECT_STATE.md`, `DECISIONS.md`, `KNOWN_ISSUES.md`, `TEST_EVIDENCE.md` güncellenmeden fazı
  tamamlandı sayma.
