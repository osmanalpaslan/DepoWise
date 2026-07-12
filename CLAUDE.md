# DepoWise - Claude Code Ana Kuralları

## 0. Oturum devri ve iki-PC senkronu (HER OTURUMDA — önce bu)
- Bu proje birden fazla bilgisayarda geliştiriliyor; tek gerçek kaynak GitHub: `github.com/osmanalpaslan/DepoWise`.
- **Kullanıcı dosya kaybı yaşıyor: git DAİMA güncel kalmalı.** Yerelde bırakılmış, push edilmemiş iş kabul edilemez.
- **Oturum başında:** önce `git pull` (temiz ağaçta; kirliyse önce kullanıcıya sor, ezme/reset yapma), sonra `DEVAM.md`'yi oku. Bağlamı buradan al, kullanıcıya tekrar sorma.
- **Her anlamlı değişiklikten HEMEN sonra commit + `git push`** yap; oturum sonunu bekleme. Bir dosya grubu tamamlandığında, bir hata düzeltildiğinde, bir özellik çalıştığında commit'le ve gönder. Kural: yerelde push'suz iş biriktirme.
- **Her push öncesi `DEVAM.md`'yi güncel tut:** §2 "en son yaptıklarım", §3 "sıradaki tek iş" ve üstteki "son güncelleme" tarihini yeniden yaz. Gerekirse `docs/PROJECT_STATE.md`/`KNOWN_ISSUES.md`/`DECISIONS.md`'yi de eşle ve aynı commit'e dahil et.
- **Oturum/yanıt bitmeden önce:** commit edilmemiş değişiklik kalmadığından emin ol (`git status` temiz + origin ile senkron). Kullanıcı açıkça "gönderme" demedikçe push'u asla atlama.
- **Bekleyen işlerin tek listesi: `docs/YARIM_KALAN_ISLER.md`.** Kullanıcı "yarıda kalan işler ne / sırada ne var" dediğinde buradan cevapla. Her anlamlı değişiklikten sonra güncelle: biten maddeyi "Tamamlananlar"a taşı, yeni iş çıkınca ekle, tarihi yenile.
- `DEVAM.md` kısa ve teknik-olmayan kalır; ayrıntı `docs/` altındadır. Çelişkide `DEVAM.md` özet, `docs/` bağlayıcıdır.
- **Arayüz fark etmez** (VS Code eklentisi / Claude Code masaüstü uygulaması / terminal): kurallar `CLAUDE.md` + `.claude/` + `DEVAM.md`'dedir ve git ile taşınır. Yeni arayüzde de akış aynıdır: `git pull` → `DEVAM.md` → `docs/YARIM_KALAN_ISLER.md`.

## 1. Proje kimliği ve kaynak önceliği
- Bu projenin tek adı **DepoWise**'tır.
- Bağlayıcı analiz: `docs/DEPOWISE_ANALYSIS.md`.
- Aynı anda yalnız `prompts/` altındaki tek aktif faz uygulanır. Sonraki faza kendiliğinden geçme.
- Çelişkide: kullanıcının son açık talebi > V6 analiz > aktif faz > bu dosya > mevcut kod. Kararı `docs/DECISIONS.md` içine yaz.

## 2. Kullanıcı ve çalışma biçimi
- Kullanıcının yazılım bilgisi yoktur. Teknik sorumluluğu kullanıcıya devretme.
- Belgede cevabı olmayan ve sonucu değiştiren gerçek ürün belirsizliği dışında soru sorma.
- Mevcut çalışan kodu yeniden yazma; küçük, geri alınabilir değişiklik yap.
- Kullanıcının git değişikliklerini silme, resetleme veya ezme.

### 2.1 Motor (model) seçimi — HER işin başında (kullanıcı kuralı, 2026-07-12)
> Kullanıcı maddi olarak dikkatli; fiyat/performans önemli. Bu yüzden her yeni iş talebinden **sonra**,
> işe başlamadan **önce** Claude hangi motorun uygun olduğunu **tek satırla** söyler; kullanıcı motoru
> değiştirir ("değiştirdim" / "devam" / "başla" der); Claude **ancak ondan sonra** işleme başlar.

- **Akış:** (1) talebi oku → (2) uygun motoru öner → (3) kullanıcının onayını bekle → (4) işe başla.
  Önerilen motor zaten açıksa "**mevcut motor (X) yeterli, değiştirme gerekmez**" de ve yine kısa onay bekle.
- Öneri kalıbı: **"Bu iş için önerilen motor: X — [tek cümle gerekçe]."**
- **Seçim rehberi (karmaşıklık × hata maliyeti):**
  - **Haiku 4.5** — çok basit: metin/etiket/yorum düzeltme, tek dosyada ufak değişiklik, log/dosya okuma, biçimlendirme, salt-okunur özet.
  - **Sonnet 5 (VARSAYILAN)** — rutin özellik/hata işleri, orta karmaşıklık, UI bağlama, birkaç dosya, testli ama riski düşük değişiklikler. Fiyat/performansın en iyisi.
  - **Opus 4.8** — zor/riskli: yetki-güvenlik, tenant sızıntısı, senkron/LWW/idempotency, migration/şema, ~6'dan çok dosyaya yayılan refactor, "neden kırıldı" derin hata avı, mimari karar. **Hatanın maliyeti yüksekse Opus.**
- Emin değilsen bir üst kademeyi öner (güvenlik/para/senkrona dokunan işte Sonnet yerine Opus).
- Kullanıcı açıkça "sen seç / geçme / beklemeden başla" derse: öneriyi yine yaz ama beklemeden devam et.

## 3. Token tasarrufu
- Önce glob/grep, sonra gerekli satır aralığı. Değişmemiş dosyaları tekrar okuma.
- Tam dosyayı yanıta yapıştırma; değişen dosyalar + kısa gerekçe + test sonucu ver.
- Uzun logu dosyaya yaz; yanıtta yalnız ilgili hata.
- 8'den fazla dosyaya yayılan işi alt adımlara böl.
- Her faz sonunda state dosyalarını güncelle. Bağlam büyürse `/compact` öner ve aynı fazdan devam et.

## 4. Mimari değişmezler (güncel — 2026-07-09, ADR-057)
- **Web: Blazor Server/.NET (MudBlazor)** — `src/DepoWise.Web`, canlıda `depowise-web.fly.dev`. `apps/web`
  (Next.js/Drizzle/PostgreSQL) 2026-06-27'den beri donmuş/terk edilmiş; yalnız referans/geçmiş, aktif
  geliştirme yok. Masaüstü: .NET 8/Avalonia/MVVM/Dapper/SQLite.
- **API/sunucu veritabanı: SQLite** (`depowise-server.db`, Fly.io kalıcı disk `/data`) — planlanan
  PostgreSQL/Drizzle (ADR-000/005) hiç üretime alınmadı, gerçek çalışan sistem uçtan uca SQLite (bkz. R4/R7).
- Web ve masaüstü işlevsel olarak eşit; piksel eşitliği zorunlu değil.
- API `/api/v1`, ortak hata modeli, correlation id, OpenAPI sözleşmesi.
- `company_id` yalnız güvenilir session/server context'ten gelir.
- Para decimal + currency; zaman UTC/Unix ms; sorgular parametreli.
- Stok hareket defteri ana kaynaktır; doğrudan bakiye değiştirme yok.
- Stok, sayaç, yakıt, bakım ve onayda LWW yasaktır. Operation id + transaction + idempotency kullan.
- Operasyonel kaydı fiziksel silme; iptal/ters kayıt ve audit kullan.

## 5. UI ve yetki
- Deny-by-default; menü, işlem, alan ve özel buton yetkisi UI ile API'da aynı uygulanır.
- Numeric alan kontrollü numeric input/NumericUpDown; tarih GG/AA/YYYY + gerçek takvim doğrulaması.
- Aranabilir çoklu seçimde seçimler aramada korunur; tümünü seç yalnız filtre sonucunu ekler.
- Ağır rapor Sorgula/Filtrele tıklanmadan çalışmaz.

## 6. COMODO - artık geçerli değil (2026-07-09, ADR-056)
- Geliştirme COMODO'suz yeni bir bilgisayara taşındı; EXE/BAT'ı doğrudan çalıştırma yasağı ve
  bunu zorlayan PreToolUse hook'u (`.claude/hooks/comodo_guard.ps1`) kaldırıldı.
  `dotnet build` / `dotnet run --project ...` / `dotnet <dll>` yine de geçerli ve önerilen yöntem.
- Geçmiş kurallar ve geri ekleme talimatı: `docs/COMODO_RUNBOOK.md` (yalnız ileride tekrar
  COMODO'lu bir makineye dönülürse kullanılır).
- SQLite mutlak `%LOCALAPPDATA%\DepoWise\Data` yolunda; Cache=Private, WAL, foreign_keys=ON, busy_timeout=5000 — bu kural COMODO'dan bağımsız, her zaman geçerli.

## 7. Test ve bitirme — Ekran QA Motoru V2 (kullanıcı kuralı, 2026-07-12, KALICI)
> Bu projede yalnızca geliştiren değil; aynı zamanda **Senior QA / Test Automation / Manual Tester /
> UX Tester / Security Tester / Performance Tester** gibi davranılır. Kural projenin tamamı için kalıcıdır
> ve yeni oluşturulan her ekrana otomatik uygulanır.

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

## 8. Yanıt formatı
1. Yapılanlar (en fazla 6 madde)
2. Değişen dosyalar
3. Çalıştırılan doğrulamalar ve sonuçları
4. Açık risk/engel
5. Sıradaki tek iş
