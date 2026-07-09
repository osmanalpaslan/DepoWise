# DepoWise - Claude Code Ana Kuralları

## 0. Oturum devri ve iki-PC senkronu (HER OTURUMDA — önce bu)
- Bu proje birden fazla bilgisayarda geliştiriliyor; tek gerçek kaynak GitHub: `github.com/osmanalpaslan/DepoWise`.
- **Kullanıcı dosya kaybı yaşıyor: git DAİMA güncel kalmalı.** Yerelde bırakılmış, push edilmemiş iş kabul edilemez.
- **Oturum başında:** önce `git pull` (temiz ağaçta; kirliyse önce kullanıcıya sor, ezme/reset yapma), sonra `DEVAM.md`'yi oku. Bağlamı buradan al, kullanıcıya tekrar sorma.
- **Her anlamlı değişiklikten HEMEN sonra commit + `git push`** yap; oturum sonunu bekleme. Bir dosya grubu tamamlandığında, bir hata düzeltildiğinde, bir özellik çalıştığında commit'le ve gönder. Kural: yerelde push'suz iş biriktirme.
- **Her push öncesi `DEVAM.md`'yi güncel tut:** §2 "en son yaptıklarım", §3 "sıradaki tek iş" ve üstteki "son güncelleme" tarihini yeniden yaz. Gerekirse `docs/PROJECT_STATE.md`/`KNOWN_ISSUES.md`/`DECISIONS.md`'yi de eşle ve aynı commit'e dahil et.
- **Oturum/yanıt bitmeden önce:** commit edilmemiş değişiklik kalmadığından emin ol (`git status` temiz + origin ile senkron). Kullanıcı açıkça "gönderme" demedikçe push'u asla atlama.
- `DEVAM.md` kısa ve teknik-olmayan kalır; ayrıntı `docs/` altındadır. Çelişkide `DEVAM.md` özet, `docs/` bağlayıcıdır.

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

## 3. Token tasarrufu
- Önce glob/grep, sonra gerekli satır aralığı. Değişmemiş dosyaları tekrar okuma.
- Tam dosyayı yanıta yapıştırma; değişen dosyalar + kısa gerekçe + test sonucu ver.
- Uzun logu dosyaya yaz; yanıtta yalnız ilgili hata.
- 8'den fazla dosyaya yayılan işi alt adımlara böl.
- Her faz sonunda state dosyalarını güncelle. Bağlam büyürse `/compact` öner ve aynı fazdan devam et.

## 4. Mimari değişmezler
- Web: Next.js/TypeScript strict/Drizzle/PostgreSQL. Masaüstü: .NET 8/Avalonia/MVVM/Dapper/SQLite.
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

## 7. Test ve bitirme
- Her değişiklikte en dar test; faz sonunda build + ilgili unit/integration/e2e.
- Kritik testler: tenant sızıntısı, permission, rollback, negatif stok, sayaç geriye gitme, idempotent retry, offline kalıcılık ve update rollback.
- Başarısız testi gizleme veya yalnız tekrar çalıştırıp geçme.
- `docs/PROJECT_STATE.md`, `DECISIONS.md`, `KNOWN_ISSUES.md`, `TEST_EVIDENCE.md` güncellenmeden fazı tamamlandı sayma.

## 8. Yanıt formatı
1. Yapılanlar (en fazla 6 madde)
2. Değişen dosyalar
3. Çalıştırılan doğrulamalar ve sonuçları
4. Açık risk/engel
5. Sıradaki tek iş
