# DepoWise — Claude Code Ana Kuralları
> Kısa tutulur: her mesajda yüklenir. Ayrıntı `docs/` altındadır (harita: `docs/README.md`). Son sadeleştirme 2026-10-10.

## 0. Oturum ve git
- Başta `git pull`; güncel durum **yalnız** `docs/project-control/CURRENT_PHASE.md`'den okunur. `docs/arsiv/` geçmiştir — tarama.
- Her anlamlı değişiklikten hemen sonra commit + push; push öncesi `CURRENT_PHASE.md`'ye kısa girdi.
- **Kullanıcının commitlenmemiş sohbet/SignalR çalışması** (`ChatHub.cs`, `Program.cs` sohbet bölümleri, iki csproj, `ChatDock.razor`,
  `ApiClient.cs`, `.gitignore`, `docs/ARAC_KURULUMU.md`, `docs/kilavuzlar/` …) commit'e GİRMEZ. `Program.cs`'te yalnız kendi
  değişikliklerini index'e koy (HEAD + kendi yamaların → `git update-index`). Kullanıcı değişikliğini silme/ezme/resetleme.

## 1. Kullanıcı ve çalışma biçimi
- Yazılım bilgisi yok: kısa, sade Türkçe; İngilizce terim (Türkçesi). Soru yalnız sonucu değiştiren ürün belirsizliğinde.
- Her yeni işte önce **tek satır motor önerisi**: Haiku (metin/tek dosya) · Sonnet (rutin, varsayılan) · Opus (yetki/tenant/senkron/
  migration/çok dosya/derin hata). Kullanıcı "başla/devam" ya da önden onay verdiyse beklemeden devam.
- Çelişkide öncelik: kullanıcının son talebi > bu dosya + `.claude/rules/` > `docs/DECISIONS.md` > mevcut kod.
- Çalışan kodu yeniden yazma; küçük, geri alınabilir değişiklik (ayrıntı: `.claude/rules/gelistirme-protokolu.md`).

## 2. Mimari (güncel gerçek)
- **Masaüstü:** .NET 8 · Avalonia 12 · MVVM · yerel SQLite `%LOCALAPPDATA%\DepoWise\Data` (Cache=Private, WAL, foreign_keys, busy_timeout=5000).
  Çevrimdışı çalışır; anlık eşitleme `/api/sync/wait` + 60 sn güvenlik turu.
- **Web:** Blazor Server + MudBlazor, `depowise-web.fly.dev` (256 MB, boşta uyur). `apps/web` (Next.js) terk edildi — dokunma.
- **API:** minimal API `/api/...` (sürüm öneki yok), `depowise-erp.fly.dev` (512 MB, tek makine). Hata gövdesi `{"error":"..."}`.
- **Üretim DB:** Supabase PostgreSQL 17 (proje *Alpnex*, `depowise_prod`). API doğrudan bağlantı; yerel araçlar pooler
  (`$TEMP/pgconn.txt`). `DEPOWISE_PG_URL` tanımsızsa API SQLite'a düşer. Neon 2026-10'da kaldırıldı. **Şema sürümü: 98.**
- Migration kataloğu iki lehçede yürür; lehçe farkı `SqlDialect`/`DbIntrospect`. Yeni migration yalnız ekleme (ADD COLUMN/CREATE)
  ve kullanıcı onayıyla; servisler eski şemaya dayanıklı kalır (`DbIntrospect.ColumnExists`).
- **Ortak dosya tuzağı:** Web, Application'daki bazı dosyaları csproj `Compile Include` ile bağlar (proje referansı yok). Web'de
  gereken yeni ortak tipi zaten bağlı bir dosyaya ekle (ör. `Ui/MeterUnitOptions.cs`, `Ui/ListColumns.cs`).
- **Ekran ekleme:** `AppScreens.cs` tek satır + `MenuIcons` + masaüstü `ShellViewModel.Navigate` case + web `@page`. Menü sayıları
  testlerde sabittir (`AppScreensParityTests`, `MenuRenkTests`, `MasaustuTasarimPaketiTests`) — bilinçli güncelle.

## 3. Değişmez iş kuralları
- `company_id` yalnız oturumdan; her sorguda firma filtresi. Para/miktar `decimal` (metin saklanır), zaman UTC ms; tarih alanı UTC gün başı (`IsGunuTarihi`).
- Stok hareket defteri ana kaynak; bakiye doğrudan değişmez. Stok/sayaç/yakıt/bakım/onayda LWW yasak; operation id + transaction + idempotency.
- Operasyonel kayıt fiziksel silinmez (iptal/ters kayıt + audit). Tek istisna: süper adminin web "Kalıcı Silme" ekranı (ADR-083).
- Deny-by-default; menü/işlem/alan/buton yetkisi UI ve API'de aynı. Kayıt işlemleri `ConfirmService` onayı ister (form içi işlemler muaf listede).
- Web ve masaüstü işlevsel eşit; önce masaüstü, web aynı iş biriminde tamamlanır. Tablo istekleri rapor dahil TÜM tablolara uygulanır.

## 4. Test
- **Tek yol:** `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_tests.ps1 [-Filter "..."] [-Bekle] [-PostgresAtla]`.
  Tam takım ~4.000 test / ~60 dk; PG testleri yerel sunucuda (`localhost:54329`, `scripts/pg_test_sunucu.ps1`).
- Test tipine (normal/kapsamlı) Claude karar verir, gerekçe tek satır. Flaky testi tekrarla gizleme.
- **QA araçları** (varsayılan kapalı, açma kararı Claude'da, sonrasında kapatılır):
  masaüstü `scripts/run_ui_tests.ps1` (Avalonia Headless + Skia, görüntü `artifacts/ui-ekran/`);
  web `cd tests/web-e2e && npx playwright test --workers=1` (canlıda yalnız okur; 256 MB web → koşular arası ~3 dk bekle).
- Kritik alanlarda her zaman test: tenant, yetki, rollback, negatif stok, sayaç geriye gitme, idempotency, çevrimdışı, eski şema.

## 5. Yayın ve üretim
- Yayın kullanıcı onayıyla (önden onay verdiyse testler geçince). Deploy **yalnız temiz kopyadan**:
  `git archive HEAD | tar -x -C %TEMP%/dw_deploy` → `flyctl deploy --config fly.toml --remote-only` (API, önce) → `fly.web.toml` (web).
- Masaüstü: `dotnet publish src/DepoWise.Desktop -c Release -r win-x64 --self-contained -p:Version=X.Y.Z -o artifacts/rc/desktop-X.Y.Z`
  → zip `artifacts/rc/` içine → `node scripts/publish_release.mjs <zip> X.Y.Z "<not>"` (env `DEPOWISE_ADMIN_USER/PASS`).
- Üretim DB'de yalnız salt okuma. Gerçek firma **Oze İnşaat**'a yıkıcı işlem yok; test firması *Alpnex Test* (`.env.test.local`).
- Sır/şifre ekrana yazılmaz. Release/update mekanizması değişikliği onay ister.

## 6. Araçlar
- Serena: yalnız okuma (sembol/referans). Değişiklik Claude'un Edit/Write/Bash araçlarıyla.
- Context7 ve Playwright **MCP** tanımlı ama KAPALI; kullanıcı açıkça istemedikçe açma (Playwright testleri npm ile çalışır).
- Uzun yama: `node` + dosyaya yazılmış betik (bash heredoc/escape tuzaklarına karşı); CRLF korunur.

## 7. Yanıt formatı
Yapılanlar (≤6 madde) · değişen alanlar · doğrulamalar ve sonuç · açık risk · sıradaki tek iş. Ayrıntı dosyaya, yanıta özet.
