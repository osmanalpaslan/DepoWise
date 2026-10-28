# DEVİR NOTU — başka bir Claude hesabı/oturumu buradan devam eder

> Güncellendi: 2026-10-28. **Yeni oturum: önce bu dosyayı, sonra `CLAUDE.md` ve `docs/project-control/CURRENT_PHASE.md`'yi oku.**
> Başka bilgisayarda: önce `git pull` (dal `arge/2026-10-kapsamli`); hafıza/genel kurallar için bir kez `~/.claude-yedek/geri-yukle.ps1`;
> PG test sunucusu yoksa `scripts/pg_test_sunucu.ps1`. Bu not her oturum sonunda güncellenir; AR-GE bitince silinir.

## 1. Nerede kaldık
- Dal: `arge/2026-10-kapsamli` (AR-GE çalışması). Faz sırası B7 → B8 → B9 → Z — **HEPSİ TAMAM (2026-10-27)**.
- 2026-10-26/27'de biten ve gönderilen (her biri testli, ayrı commit; ayrıntı `docs/arge/10_uygulama_gunlugu.md`):
  kullanıcının 7 isteği (2026-10-25) · iki ortam denetimi düzeltmeleri · M-72 · M-73 · M-68 · M-70 (3/3) · M-76 · M-69 · M-74 · M-75 · B6 eşitleme (Migration121) · eşitleme nöbetçisi + Migration122 · M-31 · M-53 · M-52 (e-Fatura + genel Excel aktarımı) · M-82 (Migration124, şema 124) · B9 sonu iki ortam denetimi (4 ayrışma düzeltildi) · tam koşu hızlandırma (ApiTestHost şablonu) ·
  **M-01 paket imzası + süper admin iki adımlı giriş (Migration125, şema 125)** · 1.0.203 yayın notu ·
  yarım SignalR sohbet denemesinin arşivlenmesi (kullanıcı onayıyla).
- **Yarım iş YOK** — çalışma kopyasında commitlenmemiş AR-GE değişikliği bırakılmadı.

## 2. Sıradaki işler
**YAYINLANDI (2026-10-28):** API + web + masaüstü 1.0.203 (imzalı, zorunlu) + imzalı kurulum aracı; tam takım 4629/4629. Ayrıntı CURRENT_PHASE.
- **YENİ ÇALIŞMA BİÇİMİ (kullanıcı kararı 2026-10-28: "otomatik yayın turuna geçme, ben yayınlayalım dediğimde gerekli uzun testlerini yaparsın"):** düzeltme/geliştirmeler BİRİKTİRİLİR, her biri testli + ayrı commit + push, ama YAYIN YOK.
  Tam takım da yalnız yayın öncesi. Kullanıcı "yayınlayalım" deyince: tam koşu → `yayin` skill'i.
- Açık: duman testi hesabında (süper admin yetkili TEST hesabı) iki adımlı girişi açmak KULLANICI ONAYI bekliyor
  (`scripts/e2e_test_hesabi_mfa.mjs`); onaya kadar canlı duman testinin 9 testi kurulum ekranında kalır.
- Kullanıcı ilk web girişinde iki adımlı girişi kurar (QR + kurtarma kodları kendisinde). Sonraki masaüstü yayınında
  `DEPOWISE_ADMIN_KOD` (o anki 6 haneli kod) gerekir.
Gizli imza anahtarı: `~/.claude-yedek/anahtarlar/DepoWise_paket_imza_ozel.pem` (kullanıcı onayıyla özel GitHub yedeğinde).
Ertelenenler: M-42, M-40. Sıradaki: M-75 kalanı + kullanıcı istekleri. Kaynak: `docs/arge/13_kullanici_secimleri.md`.

## 3. Kesin kurallar (kullanıcı)
- **Yayın yalnız kullanıcı "yayınlayalım" deyince** (toplu yayın, 2026-10-28); önce tam koşu. Otomatik yayın YOK.
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
- Web formu değişen işte yerel kayıt testi: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run_web_e2e_yerel.ps1` (~1 dk).
- Yanıtlar yalnız Türkçe ve sade (kullanıcının yazılım bilgisi az). Commit mesajı Türkçe, `git commit -F`, sonda
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## 4. Kullanıcıya sorulacak açık noktalar (acil değil)
`docs/arge/11_kalan_riskler.md` sonu: "Şablon Dışı" raporunun şube kapsamı ve "Atanmamış" stok toplamı farkı.
