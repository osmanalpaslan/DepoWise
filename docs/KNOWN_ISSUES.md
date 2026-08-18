# KNOWN ISSUES

> Son güncelleme: 2026-08-18

## ⚠️ Operasyonel riskler (canlı sistemi durdurabilir)

- **R31 — Depo bazlı stok (Migration064) deploy edilince stoğun neredeyse tamamı "ATANMAMIŞ" görünecek**
  (ADR-102, 11.08.2026 — **henüz deploy EDİLMEDİ**, dalda duruyor). Mevcut 667 stok hareketinin **666'sı
  lokasyonsuz** olduğu için bakiye **8953,3 birim** ATANMAMIŞ kovasına düşer. Bu bir **veri kaybı değildir**
  (toplam korunur, kanıtlandı) ama kullanıcı "stoğum kayboldu / hepsi boşta" diye algılayabilir.
  **Önlem:** deploy öncesi kullanıcıya anlatılmalı; dağıtım **KARAR-8** (`STK-08`) ile yapılacak.
  Veri uydurulmadı — hangi malın hangi depoda olduğu defterde yazmıyor, tahmin edilmeyecek.

- **R32 — Migration064 bakiyesi defterle uyuşmayan veritabanında BİLİNÇLİ olarak durur** (ADR-102).
  Fail-closed: sessizce yanlış stok göstermek yerine açık hata verir ve transaction geri alınır
  (kanıtlandı). **Sonuç:** böyle bir **masaüstü** veritabanı varsa uygulama güncellemesi başlamaz.
  **Çözüm yolu:** o makinede önce sunucu-otoriteli yeniden hesaplama (`RecomputeBalances`) çalıştırılmalı.
  Üretim PostgreSQL kopyasında uyuşmazlık **yok** (664 malzemede uyuşmayan 0).

- **R30 — Sunucu diski dolarsa TÜM API 500 döner** (ADR-070, 12.07'de **yaşandı**). Fly.io kalıcı diski
  `/data` ~**974 MB**; her masaüstü paketi ~**85 MB** → **~11 sürümlük tavan**. Disk dolunca SQLite hiçbir şey
  yazamaz, **login dahil her uç 500** verir (sessiz değil, ölümcül).
  **Önlem (uygulandı):** `ReleaseStore.PruneOld` → yayında en yeni **3 paket** tutulur, eskiler otomatik silinir.
  **Teşhis:** `flyctl ssh console --config fly.toml -C "df -h /data"`.
  **Kalan risk:** paket boyutu büyürse veya sürüm hızı artarsa `KeepCount` düşürülmeli ya da
  `fly volumes extend` ile disk büyütülmeli. Etki: **kritik**.

## Çözüldü (12.07.2026)

- **Süper admin kilitlenmesi** (ADR-064): firma silme, süper admin dahil tüm kullanıcıları pasife alıyordu →
  süper admin kendi firmasını silince sistemden tamamen kilitleniyordu. Firma silme artık süper admini hariç
  tutar + sunucu açılışında **self-heal**. Regresyon testi var.
- **Firma silince 401 + firmalar yüklenmiyor** (ADR-068): süper admin içinde çalıştığı firmayı silince
  token'daki firma geçersiz kalıyordu → her istek 401. Artık home firmaya düşer (sahte firma id'de fail-closed).
- **Silinen şubeler her yerde listeleniyordu** (ADR-066): masaüstü yerel kopyası sunucudan yalnız upsert
  ediliyordu. Artık her girişte **aynalanır**.
- **Masaüstü firma ekle/sil web'e ulaşmıyordu** (ADR-071/072): firmalar iş senkronunda yoktu ve yalnız yerele
  yazılıyordu. Artık sunucu-otoriteli + **offline kuyruk** (idempotent, sıralı).
- **Webte silinen kayıt makinede kalıyordu** (ADR-069): LWW silmeyi eziyordu; ayrıca cihaz push'u sunucudaki
  silmeyi diriltiyordu. İkisi de kapatıldı.

## Açık
- **R34 (12.08.2026) — ✅ KÖK NEDEN BULUNDU VE DÜZELTİLDİ (kapatıldı):** tam test takımında ara sıra
  `SyncBalancePayloadTests.Yalniz_Bakiye_Degisirse_Sunucu_Etkilenmez_Yerel_Calismaya_Devam_Eder`
  kırılıyordu. Neden **üretim kodu değil, TESTİN KENDİSİYDİ**: `Assert.DoesNotContain("777", Snapshot())`
  senkron paketinin TAMAMINDA ham `"777"` metnini arıyordu; pakette rastgele üretilen GUID'lerden biri
  `777` dizisini içerdiğinde test sebepsiz kırılıyordu (yakalanan örnek: `…0077788757fd6`).
  **Düzeltme gevşetme DEĞİL, keskinleştirme:** artık paketin `tables` bölümünde `stock_balances`
  tablosunun HİÇ olmadığı (asıl sözleşme) + `"quantity":"777"` alanının bulunmadığı doğrulanıyor.
  Retry/skip **kullanılmadı** (proje kuralı). Bu kırılganlık STK-10b-4 ile ilgisizdi, önceden vardı.
- **R33 (YENİ 12.08.2026, `RPR-02`):** **Web'de rapor isteği, giriş ekranında seçilen ŞUBEYİ taşımıyor.**
  JWT yalnız kullanıcı+firma bilgisini taşır; `AuthService.CreateSessionForUser` oturuma
  `OperatingBranchId` **atamaz** (tek istisna: içe-aktarma ucu, formdan `branchId` alır). Sonuç:
  `ReportScope.Effective` → `BranchScope.Active(s)` **null** döner ve web raporları **firma geneli**
  çalışır; şube daralması yalnız kullanıcı **açıkça** şube seçtiğinde (`branchIds`) olur.
  **Masaüstü etkilenmiyor** — orada oturum şubesi gerçekten dolu ve daraltma testli.
  **Etki:** orta — bu bir tenant (firma) sızıntısı DEĞİL; firma içi şube görünürlüğü beklenenden geniş.
  Tüm raporları etkileyen **mevcut** mimari; STK-10a/10b artımları getirmedi. STK-10b-3'te tespit
  edildi ve kasten düzeltilmedi (kapsam dışı). Kayıt: `STK_10_HAREKET_RAPORU_PLANI.md` §23.5.
- **R5:** Web ve masaüstü health şu an DB'ye fiilen bağlanmıyor (web config-kontrolü, masaüstü yerel SQLite write/read). Gerçek PostgreSQL bağlantı health'i Faz 02'de eklenecek. Etki: düşük.
- **R6:** `dotnet test` çıktısında MSBuild "MSB4011 Directory.Build.props ikinci kez içe aktarıldı" benzeri bilgi mesajı görülebilir; build/test sonucunu etkilemiyor. Etki: kozmetik.
- **R2:** Üretim hosting, object storage, e-posta ve code-signing sağlayıcıları maliyet değerlendirmesi yapılmadan seçilmeyecek. Etki: yayın (Faz 15-17) öncesi.
- **R3:** Otomatik döviz kuru kaynağı kesinleşmedi; manuel kur + tarihçe güvenli fallback olarak tasarlanacak. Etki: para/maliyet modülleri (Faz 06+).
- **R4:** (Güncellendi 09.07.2026, ADR-057) Gerçek/canlı sunucu (`depowise-erp.fly.dev`) **SQLite** kullanıyor (`depowise-server.db`, Fly.io kalıcı disk). PostgreSQL'e hiç geçilmedi; `apps/web/drizzle` altında üretilmiş migration SQL'i **kullanılmıyor/donmuş**. PostgreSQL'e geçiş artık aktif bir plan değil, kullanıcı karar verirse ele alınacak bir gelecek seçenek. Etki: düşük (mevcut SQLite tek-dosya/tek-disk mimarisi çok şirketli kullanım için şimdilik yeterli; çok yüksek eşzamanlı yazma/ölçek ihtiyacı doğarsa yeniden değerlendirilir).
- **R7:** (Güncellendi 09.07.2026) PostgreSQL üretime hiç alınmadığı için "PG ↔ SQLite şema eşitliği" konusu şu an geçerli değil — tek gerçek şema SQLite (`MigrationRunner`/`IMigration`). `apps/web/drizzle` donmuş, aktif bakımı yok. Etki: düşük (drift riski yok, çünkü ikinci bir canlı şema yok).
- **R23:** `npm audit`: 9 advisory (1 high @eslint/plugin-kit, moderate esbuild/drizzle-kit, postcss/next) — tümü **dev/build araçları**, üretim runtime'ında yok. `npm audit fix --force` breaking (next downgrade) olduğu için uygulanmadı; lock dosyası commit'li, periyodik izlenecek. Etki: düşük (runtime maruziyeti yok).
- **R22:** Code-signing (imzalı dağıtım) henüz yapılmadı; maliyetli kalem, yayın öncesi karara bırakıldı. İmzasız sürümde updater kullanıcıya şeffaf uyarı verir (signedWarning). Etki: orta (yayın öncesi).
- **R21:** UpdateService dosya tabanlı kurulum/rollback mantığı + testleri hazır; gerçek HTTP indirme transport, masaüstü güncelleme UI ekranı (yüzde göstergesi) ve canlı uygulama dosyalarının değişimi henüz bağlanmadı. Etki: orta.
- **R20:** SyncServer push'ta `accepted` işlemler şu an `sync_inbox` + `server_changes` feed'ine yazılıyor; gerçek iş tablolarına apply (upsert) iş-servisleriyle bağlanacak. Idempotency/doğrulama/conflict çekirdeği hazır. Etki: orta.
- **R19:** Sync HTTP transport katmanı (push/pull endpoint'leri), DPAPI `ISecretProtector` gerçek implementasyonu, retry/backoff ve 0-100 non-blocking ilerleme UI henüz yok (servis mantığı + testler hazır). Etki: orta.
- **R17:** İçe aktarım şu an yalnız malzeme seti (dry-run+commit). Araç/diğer setler aynı desenle (`ImportRow`/dry-run) eklenecek. Ayrıca commit'te mevcut kod "updated" sayılıyor ama alanlar güncellenmiyor (idempotent no-op); gerçek güncelleme akışı sonra. Etki: orta.
- **R16:** Talep PDF binary üretimi şu an yalnız .NET (QuestPDF). Web tarafı aynı `RequestPdfModel`'i kullanıyor ama binary render hattı (ör. server-side PDF lib) henüz eklenmedi. Etki: düşük (web PDF sonraki bir adımda).
- **R15:** Günlük faaliyet bakımında `MaintenanceService.Save` ve `daily_activities` insert ayrı transaction'larda (MaintenanceService kendi tx'ini commit eder). Her ikisi de idempotent → retry ile tutarlı; nadir partial-fail penceresinde bakım kaydı oluşup faaliyet referansı eksik kalabilir (retry düzeltir). İleride tek tx'e alınabilir. Etki: düşük.
- **R14:** `MaintenanceService.GetAlerts` GROUP BY + MAX(created_at) ile en-son bakımı seçerken SQLite bare-column davranışına dayanıyor; aynı created_at'te tie belirsiz olabilir (testlerde saat ilerletilerek garanti). İleride pencere fonksiyonu/alt sorgu ile sağlamlaştırılabilir. Etki: düşük.
- **R13:** Stok bakiyesi material-global (şube bazlı değil); transfer net-zero. Şube bazlı bakiye + şube negatif kontrolü sonraki fazda. Etki: orta (çok şubeli stok ayrımı henüz yok).
- **R11:** `material_compatible_vehicles.vehicle_id` şu an FK'siz serbest metin (vehicles tablosu Faz 08). Faz 08'de FK + referans bütünlüğü eklenecek. Etki: düşük (geçici).
- **~~R10~~ (KAPANDI 11.07.2026):** Operasyonel + yönetim modül ekranları BAĞLANDI. Masaüstü: her menü anahtarı gerçek bir ViewModel'e yönleniyor (ShellViewModel switch tamam; PlaceholderViewModel yalnız tanımsız anahtar için fallback). Web: 34 Blazor sayfası. GUI'nin gerçek kullanımda test edilmesi kullanıcıya kaldı (birim/entegrasyon iş mantığı testlerle kapalı).
- **R9:** Masaüstü shell şu an **preview admin oturumu** ile menüyü gösteriyor (login akışı Faz 05). Yetki mantığı testlerle doğrulandı; gerçek oturum + firma override tema Faz 05'te bağlanacak. Etki: orta (UI önizleme).
- **R8:** Web `getServerSession` henüz oturum çözmüyor (imzalı cookie + DB session lookup Faz 05'e bırakıldı); şu an fail-closed null döner → `/api/v1/me` daima 401. Davranış güvenli; işlevsel oturum web tarafında Faz 05'te bağlanacak. Etki: orta.

## Kapatılan
- **R18:** Foto optimizasyonu yapıldı — `ImageOptimizer` (SkiaSharp, ücretsiz; ImageSharp lisans maliyeti yerine): en uzun kenar >1600px küçültme + JPEG Q82; çözülemezse orijinal (graceful). Fly Linux native asset doğrulandı. Test: `ImageOptimizerTests`.
- **R12:** LIKE araması artık Türkçe duyarsız — `SqliteConnectionFactory` `like()`'ı `SqlLikeTr` ile override eder (İ/ı/ş/ç/ğ/ü/ö). Tüm sorgular otomatik faydalanır. Test: `TurkishLikeTests`.
- Büyük tek prompt yerine faz bazlı çalışma paketi oluşturuldu.
- Proje adı ve dosyalar DepoWise olarak standartlaştırıldı.
- CLAUDE.md ↔ V6 analiz çelişki taraması yapıldı; çelişki yok (Faz 00).
- COMODO güvenli çalıştırma zinciri (hook + UseAppHost=false + mutlak DB yolu) doğrulandı (Faz 00).
- R1 (kaynak kod yoktu): Faz 01'de çözüm iskeleti kuruldu, baseline build+test+web build yeşil.
- `next` CVE-2025-66478: 15.5.19 yamalı sürüme yükseltilerek kapatıldı (Faz 01).

## 05.07.2026 — Açık kalan bilinen sorunlar (canlı test + inceleme)
- Sync üretim yolu LWW'li tek yönlü snapshot (`business-push`); operation-id'li `/sync/push` masaüstünce kullanılmıyor. `stock_balances` LWW satırı olarak taşınıyor; iş verisi pull edilmiyor (2. makine senaryosunda veri ezilir/görünmez). Çok makineli kullanım öncesi çözülmeli.
- (ÇÖZÜLDÜ 05.07.2026 ADR-053) business-push artık modül-bazlı yetki + negatif değer doğrulaması yapıyor.
- (ÇÖZÜLDÜ 05.07.2026 ADR-054) JWT refresh eklendi; kayan oturum + SessionExpired sinyali.
- (ÇÖZÜLDÜ 05.07.2026 ADR-055) Updater artık yedekliyor + başarısızlıkta rollback yapıyor + bütünlük guard./gerçek PS yolu Windows entegrasyon testi bekliyor.
- (ÇÖZÜLDÜ 05.07.2026) Çöp Kutusu web API'si eklendi: `POST /api/trash` + `/api/trash/restore` (parola ile yeniden doğrulama), web `Trash.razor` (/trash). `soon/about` hâlâ placeholder.
- (ÇÖZÜLDÜ 05.07.2026) Server-status bellek grafiği min-max normalize edildi (artık hep %100 değil).
- (ÇÖZÜLDÜ 05.07.2026) SessionExpired UI'ya bağlandı: masaüstü oturum düşünce dialog + tekrar giriş (`ShellViewModel.OnSessionExpired`).
- Sunucuda ILogger yok; ~40 boş catch bloğu gözlemlenebilirliği düşürüyor (500 loglaması eklendi, gerisi açık). *(orta öncelik — launch için kabul edilebilir)*
- Güvenlik sertleştirme adayları (bu turda dokunulmadı, ayrı inceleme): CORS AllowAnyOrigin (Blazor Server side-call olduğundan tarayıcıdan kullanılmıyor, düşük risk), `/api/machines/register` anonim, `serverurl.txt` düz metin, 1 GB gövde limiti.

## 2026-08-13 — Masaüstü GUI doğrulama turunda AÇILAN konular

**Kapatılanlar (bu turda düzeltildi, regresyon testi eklendi):** GUI-01 masaüstünde şube kapsamının hiç
uygulanmaması · GUI-02 elle cari hareketinin şubesiz yazılması · GUI-02b ters kaydın şubesiz + kapsam
kontrolsüz olması · GUI-03 "tüm yetkili şubeler" etiketi ile verinin çelişmesi · GUI-04 rapor şube
filtresinde yetkisiz şubenin listelenmesi · GUI-05 "Şube Kapsamı" bölümünün sessizce kaybolması.
Ayrıntı: [`docs/tests/Sube_Kapsami_GUI_Test_Report.md`](tests/Sube_Kapsami_GUI_Test_Report.md).

**AÇIK — veri geçişi kararı (kullanıcıya sorulacak):** GUI-02 düzeltmesi yalnız bundan sonra girilecek
hareketleri şubeye bağlar. Canlıda daha önce elle girilmiş cari hareketler `branch_id = NULL` olabilir;
şubesiz satır tasarım gereği HER şubede görünür. Yayın öncesi canlı veride şubesiz hareket sayılmalı,
varsa toplu şube ataması kullanıcı onayıyla yapılmalıdır. **Bu tur canlı veriye bakmadı.**

**AÇIK — masaüstü GUI'de koşturulamayan 3 madde:** negatif stok kapısı (izole ortamda malzeme kurulmadı) ·
idempotency ikinci gönderim (kayıttan sonra form kapanıyor) · senkron şube izolasyonu (iki makine gerekir).
Üçü de otomatik testlerle örtülüdür ama **GUI kanıtı yoktur**.

**AÇIK — masaüstü Yetkiler ekranı ile yerel veritabanı ilişkisi:** kullanıcı listesi ve yetkiler
sunucudan gelir; masaüstünün yerel veritabanında ise yalnız o makinede giriş yapmış kullanıcılar bulunur.
GUI-05 ile kapsam okuma/yazma sunucuya taşındı, ama **çevrimdışıyken** web'de oluşturulmuş bir kullanıcının
kapsamı hâlâ yerelden okunamaz (panelde sebep yazar). Kalıcı çözüm kullanıcı aynalaması olurdu — mimari
karar gerektirir, bu turda yapılmadı.

## 2026-08-18 — ŞUBE / SIFIRLAMA / YETKİ TURUNDA KAPATILANLAR

Aşağıdakiler bu turda **düzeltildi**; kayıt olarak duruyor (tekrar ederse aynı yerlere bakılır).
Ayrıntı: [`docs/ANALIZ_SUBE_VE_SIFIRLAMA.md`](ANALIZ_SUBE_VE_SIFIRLAMA.md)

- **SIF-01 (kritik, kapatıldı)** — masaüstü, sunucudan gelen "yerelini sıfırla" isteğini uygularken
  ADR-083'ün TAM SİLME fonksiyonunu çağırıyordu → yerel `users` satırı silindiği için o makinede
  **çevrimdışı giriş imkânsız** hâle geliyordu. Çağrı yeri artık kaynak düzeyinde testle kilitli
  (`BusinessResetCoverageTests.LoginEkraniDogruFonksiyonuCagirir`).
- **SIF-03 (kapatıldı)** — silme kapsamı senkron sözleşmesinden okunuyordu; ortak liste
  `BusinessDataExtras` ile ayrıldı.
- **ŞB-01 (kapatıldı)** — şube aynası `kind`/`parent_id` taşımıyordu.
- **ŞB-04 (davranış değişikliği)** — üst şube artık **işlevsel**: kapsam alt şubelere yayılır,
  rapor üst şube seçilince altları toplar. ⚠️ Bu bir **yetki genişlemesidir**: üst şubeye yetkili
  kullanıcı artık alt şubelere de **yazabilir** ve alt şubeleri **devredebilir**. Ağacı yöneten
  admindir; mevcut kullanıcı kapsamları gözden geçirilmelidir.
- **İçe aktarım kapsam açığı (kapatıldı)** — içe aktarım oturum kopyası şube kapsamını taşımıyordu →
  kapsam dışı şubeye kayıt basılabiliyordu (web + masaüstü).

### ⚠️ Bu turda AÇIK KALAN
- **SIF-02 (açık)** — yerel sıfırlama kontrolü **yalnız giriş anında** çalışır; `ShellViewModel`
  içinde kontrol YOKTUR. Program açık ve giriş yapılmışsa 15 saniyelik eşitleme turu eski yerel
  veriyi sunucuya göndermeye devam eder. **Operasyonel önlem:** sıfırlama öncesi tüm kullanıcılara
  programı **tamamen kapattırın**, sıfırlayın, sonra açtırın. Kalıcı çözüm ayrı iş olarak önerildi.
