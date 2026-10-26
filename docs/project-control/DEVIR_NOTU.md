# DEVİR NOTU — başka bir Claude hesabı/oturumu buradan devam eder

> Yazıldı: 2026-10-26 ~17:50 (önceki oturum, plan kotası dolmak üzereyken). **Yeni oturum: önce bu dosyayı, sonra
> `CLAUDE.md` ve `docs/project-control/CURRENT_PHASE.md`'yi oku.** Bu not iş bitince güncellenir ya da silinir.

## 1. Nerede kaldık
- Dal: `arge/2026-10-kapsamli` (AR-GE çalışması). Faz sırası B7 → B8 (TAMAM) → **B9 (sürüyor)** → Z.
- Bugün biten ve gönderilen (her biri testli, ayrı commit; ayrıntı `docs/arge/10_uygulama_gunlugu.md`):
  kullanıcının 7 isteği (2026-10-25) · iki ortam denetimi düzeltmeleri · M-72 · M-73 · M-68 · M-70 (1/3, 2/3).
- **YARIM (commitlenmedi, çalışma kopyasında):**
  1. `src/DepoWise.Infrastructure/Database/Migrations/Migration099_SyncTxid.cs` — PG'de damga UPDATE'i tetikleyiciden ÖNCE
     (bulgu: eski sırada mevcut satırların hiçbiri damgalanmıyordu). + `tests/DepoWise.Tests/PostgresMigrationProvasiTests.cs`
     (MP1: 98'de duran PG'ye 100 bin hareket → 99→120 zinciri; sonunda kendi verisini siler).
     Durum: MP1 tek başına GEÇTİ. Riskli paket (Postgres|Migration|Sync|Senkron|Ufuk|Esitleme|IkiBilgisayar, ~400 test, ~13 dk)
     ilk koşuda 29 PG testi "veritabanı 64 MB > 50 MB" güvenlik kapısıyla reddedildi (prova verisi geride kalmıştı) → test
     temizliği eklendi, paket yeniden koşuluyordu. **Yapılacak:** paketi tekrar koş; geçerse günlüğe
     `devir/m70_3_gunluk.txt` metnini "### M-70" bölümüne ekle, commit et (mesaj: "M-70 (3/3): ...").
  2. **M-76 (belgeler)** — yapıldı ama commitlenmedi: `docs/UI_DESIGN_SPEC.md`, `TEST_LISTESI.md`, `TEST_ORTAMI.md` →
     `docs/arsiv/kok/` (git mv ile, index'te), `docs/README.md` güncellendi, `.claude/rules/list-screens.md` (ListColumns tek dosya).
     **Kalan:** "Hakkında" ekranı iki ortamda yer tutucu → hazır dosyalar `docs/project-control/devir/m76/`:
     `About.razor` → `src/DepoWise.Web/Components/Pages/`, `AboutView.axaml` → `src/DepoWise.Desktop/Views/`,
     `AboutViewModel.cs` → `src/DepoWise.Desktop/ViewModels/`, `BelgeVeHakkindaTests.cs` → `tests/DepoWise.Tests/`;
     `AppScreens.cs`'te `"about"` satırında `"soon/about"` → `"about"`. Sonra: hedefli test + arayüz testi + web derlemesi,
     günlük, commit. `devir/` klasörünü iş bitince sil.
  - ⚠ M-70 (3/3) ve M-76 AYRI commit: `git commit -F mesaj -- <dosyalar>` ile yalnız ilgili dosyaları kaydet (git mv'ler index'te).

## 2. Sıradaki işler (B9 kalanı, sırayla)
M-69 (web kayıt akışlarını yerel tarayıcı testi, Playwright) → M-74 (Avalonia 12.1) → M-75 (büyük dosya bölme / tipli web-API
istemcisi — isteğe bağlı "Evet") → düşük öncelik: M-31 (depo bazında asgari stok + öneri), M-53 (tedarikçi tek yer), M-52
(Logo/Mikro aktarım + e-Fatura XML), M-82 (olası mükerrer uyarısı) → B9 sonu iki ortam denetimi → **B9+Z TAM TAKIM**
(~4.500 test, son ölçüm 2 sa 28 dk; hatırlatıcı tam ortalamaya) + arayüz testleri → **Z**: `docs/arge/12_yayin_oncesi_kontrol.md`.
Ertelenenler: M-01 (yayın öncesi hatırlatılacak), M-42, M-40. Yapılmayacak: M-45, M-77. Kaynak: `docs/arge/13_kullanici_secimleri.md`.

## 3. Kesin kurallar (kullanıcı)
- **Hiçbir koşulda canlıya yayın yok**, üretim veritabanında migration yok, dağıtım otomasyonu tetiklenmez (AR-GE süresince).
  Üretim verisi yalnız okunur, testlerde kullanılmaz. Sırlar ekrana yazılmaz.
- Madde/faz arasında **onay sorulmaz** ("yetki sende"); ara özet için durulmaz.
- **Kullanıcının commitlenmemiş dosyaları ASLA commit'e girmez** (`git add -A` YASAK; her seferinde `git status`). 2026-10-26: yarım
  SignalR sohbet denemesi kullanıcı onayıyla `docs/arsiv/rapor/2026-09-07_anlik_sohbet_denemesi.patch` olarak arşivlendi ve kaldırıldı
  (sohbet yoklamayla çalışır; artık Program.cs/csproj/ChatDock/ApiClient TEMİZ). Yalnız ilk bilgisayarda kalan takip edilmeyen yerel
  dosyalar: `docs/ARAC_KURULUMU.md`, `docs/kilavuzlar/`, `docs/SECURITY_CREDENTIAL_ROTATION_PLAN.md`, `scripts/prod_salt_oku.sh`,
  `.claude/skills/alpnex-arayuz-hareket/` — dokunma. Karışık dosyada kendi değişikliğin olursa `node scripts/kismi_stage.mjs` (skill `guvenli-commit`).
- Test düzeni: her işte hedefli + kritik alanda mutasyon + riskli paket, her iş ayrı commit; tam takım B9+Z sonunda.
  Her koşuda ortalama süre + bitiş saati söylenir, ortalamaya ulaşınca kontrol (hafıza: `depowise-test-suresi`).
  Koşu sürerken boş bekleme yok. "Şube" = "şantiye" (ikisi birden).
- Yanıtlar yalnız Türkçe ve sade (kullanıcının yazılım bilgisi az). Commit mesajı Türkçe, `git commit -F`, sonda
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## 4. Kullanıcıya sorulacak açık noktalar (acil değil)
`docs/arge/11_kalan_riskler.md` sonu: "Şablon Dışı" raporunun şube kapsamı ve "Atanmamış" stok toplamı farkı.
