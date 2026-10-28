---
name: yayin
description: DepoWise'ı canlıya yayınlar — API + web (Fly.io) ve masaüstü sürümü (otomatik güncelleme paketi). YALNIZ kullanıcı "yayınlayalım", "yayınla", "deploy et", "canlıya al", "yeni sürüm çıkar", "1.0.X yayınla" dediğinde kullan — kendiliğinden ÇAĞRILMAZ (2026-10-28 toplu yayın kuralı). Test kapısı, temiz kopyadan deploy, sıra (API → web → masaüstü), güncelleme notu, canlı duman testi ve temizliği içerir.
---

# Yayın (DepoWise)

**Onay:** 2026-10-28'den beri DepoWise'da yayın YALNIZ KULLANICI İSTEYİNCE (toplu yayın; kullanıcı kararı 2026-10-28: "otomatik yayın turuna geçme, ben yayınlayalım dediğimde gerekli uzun testlerini yaparsın"; proje CLAUDE.md §5).
İşler biriktirilir; testler geçse de kendiliğinden yayına geçilmez. Kullanıcı "yayınlayalım" deyince önce TAM koşu (~3 sa,
`test-kosusu`), sonra aşağıdaki kapı ve adımlar sorulmadan yürür. 2026-10-11 "otomatik yayın" kuralı kalktı. Release/update mekanizmasının KENDİSİNİ
değiştirmek ve ücret çıkaran adımlar yine onay ister (bu skill yalnız mevcut mekanizmayı kullanır).

## 0. Kapı — bunlar olmadan yayın YOK
- [ ] Son TAM test koşusu "SONUC: TUM TESTLER GECTI" ile bitti (`test-kosusu` skill'i). Yarıda kalan koşu = geçmedi.
- [ ] Arayüz değiştiyse `scripts/run_ui_tests.ps1` geçti.
- [ ] Tüm değişiklikler commit + push edildi (`guvenli-commit`). `git status`'ta kalanlar YALNIZ kullanıcının
      commitlenmemiş işleri (sohbet/SignalR: `Program.cs` hunk'ları, iki csproj, `ChatDock.razor`, `ApiClient.cs`, `.gitignore`…).
- [ ] **Güncelleme notu:** `src/DepoWise.Application/Security/AppScreens.cs` → `GuncellemeNotlari.Tumu` EN BAŞINDA bu
      yayının notu var; sade Türkçe, teknik terimsiz (`GuncellemeNotlariTests.GN2` yakalar). Masaüstü yayınıysa
      `MasaustuSurumu` = yeni sürüm. Not commit'e girdikten SONRA yayınlanır (web pop-up ve masaüstü "Yenilikler" ondan okur).
- [ ] **Canlıdaki her şey bu dalda mı?** (2026-10-17 dersi: hotfix dalı master'a girmeden yayınlanmıştı; master'dan açılan
      dalın API yayını onun rapor değişikliğini canlıdan GERİ ALDI.) `git fetch` → her uzak dal için
      `git log --oneline HEAD..origin/<dal>` — yayınlanmış ama bu dalda olmayan commit varsa ÖNCE birleştir (kullanıcı onayıyla).
      Ayrıca `curl https://depowise-erp.fly.dev/api/releases/latest` → canlı masaüstü sürümü beklediğin önceki sürüm mü?
      Değilse yeni sürüm numarasını ona göre seç.
- [ ] Yeni migration varsa: yalnız ekleme (ADD COLUMN/CREATE) ve eski şemaya dayanıklı servis — API önce çıkar.

## 1. Temiz kopya (commitlenmemiş iş canlıya KARIŞMAZ)
```bash
D="$TEMP/dw_deploy_$(date +%H%M%S)"     # her seferinde YENİ klasör (meşgul klasör silme zinciri bozmuştu)
mkdir -p "$D" && git archive HEAD | tar -x -C "$D"
```

## 2. API → 3. Web (sıra önemli: yeni uç önce sunucuda olmalı)
```bash
cd "$D" && flyctl deploy --config fly.toml --remote-only        # depowise-erp
curl -s https://depowise-erp.fly.dev/api/releases/latest         # API ayakta mı
cd "$D" && flyctl deploy --config fly.web.toml --remote-only    # depowise-web (512 MB + swap; [[vm]] korunur)
```
Deploy hatasında: log oku (`flyctl logs -a <app>`), düzelt, commit, YENİ temiz kopyadan tekrar. Bellek/swap ayarını
fly.web.toml'da ÜST düzeyde tut (bir `[bölüm]` altına yazılırsa yok sayılır).

## 3b. ⭐ M-01 (2026-10-27): İMZA + İKİ ADIMLI GİRİŞ
- Paket ve kurulum aracı İMZALI yayınlanır; sunucu/masaüstü imzasızı reddeder. Gizli anahtar:
  `~/.claude-yedek/anahtarlar/DepoWise_paket_imza_ozel.pem` (ya da `DEPOWISE_IMZA_ANAHTARI`). Yoksa yayın YAPILAMAZ — yeni anahtar
  üretme (`paket_imza_anahtari_olustur.mjs` üzerine yazmaz); kullanıcıya sor.
- Süper adminde iki adımlı giriş açıksa betik kodu `DEPOWISE_ADMIN_TOTP` (gizli anahtar) ya da `DEPOWISE_ADMIN_KOD` ile alır.
- Zorunlu güncelleme gerekiyorsa `--min <sürüm>`; rutin yayında VERİLMEZ (7 günlük erteleme yeter).
- Kurulum aracı: `dotnet publish src/DepoWise.Setup …` → `node scripts/publish_setup.mjs <AlpnexSetup.exe>` (imzalı).

## 4. Masaüstü sürümü (yalnız masaüstü kodu değiştiyse)
Sürüm = `GuncellemeNotlari` en üst notundaki `MasaustuSurumu` (son yayından +1, ör. 1.0.196 → 1.0.197).
```powershell
dotnet publish src/DepoWise.Desktop/DepoWise.Desktop.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true -p:Version=X.Y.Z -o artifacts/rc/desktop-X.Y.Z
Compress-Archive artifacts/rc/desktop-X.Y.Z/* artifacts/rc/DepoWise-desktop-X.Y.Z.zip
$env:DEPOWISE_ADMIN_USER=[Environment]::GetEnvironmentVariable("DEPOWISE_ADMIN_USER","User")
$env:DEPOWISE_ADMIN_PASS=[Environment]::GetEnvironmentVariable("DEPOWISE_ADMIN_PASS","User")
node scripts/publish_release.mjs artifacts/rc/DepoWise-desktop-X.Y.Z.zip X.Y.Z "<NOT>"
```
- `<NOT>` = `GuncellemeNotlari.MasaustuMetni(not)` ile AYNI metin ("• madde" satırları). Masaüstündeki "Güncelleme
  Mevcut / Hazır" pencereleri bu sunucu notunu gösterir. Metni üretmek için katalogdaki masaüstü/ortak maddeleri
  "• " önekiyle satır satır birleştir (web'e özel maddeler HARİÇ).
- Zip'i yalnız `artifacts/rc/` içine koy (betik o klasörde eski paketleri temizler). Şifreyi ASLA ekrana yazma.
- Doğrula: `curl -s https://depowise-erp.fly.dev/api/releases/latest` → version X.Y.Z, releaseNotes dolu.

## 5. Canlı duman testi (salt okuma)
```bash
cd tests/web-e2e && npx playwright test --workers=1
```
Tek koşu; art arda koşturma (256→512 MB makineyi kilitlemişti). Takılırsa `.env.test.local` UTF-8 mi bak.
Kilitlenirse: `flyctl machine restart <id> -a depowise-web`.

## 6. Kapanış
- `docs/project-control/CURRENT_PHASE.md` özetine yayın satırı (commit + sürüm), commit + push.
- `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/temizlik.ps1` (bitmiş test/geçici artıklar) ve `$D`'yi sil.
- Kullanıcıya sade rapor: ne yayınlandı, hangi sürüm, test sonuçları, açık risk.
