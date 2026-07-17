# DECISIONS

## ADR-000 - V6 başlangıç kararları
- Web: Next.js + TypeScript strict + Drizzle + PostgreSQL.
- Masaüstü: .NET 8 + Avalonia + MVVM + Dapper + SQLite.
- Web çevrimiçi merkez; masaüstü offline-first.
- Stok hareket defteri ana kaynak; kritik operasyonlarda LWW kullanılmaz.
- Fotoğraf için file_records + storage provider; DB base64 varsayılan değildir.
- Geliştirme makinesinde dotnet host ve mutlak LocalAppData DB yolu zorunludur.

Fazlar ilerledikçe yeni kararlar tarih, bağlam, karar, alternatifler ve sonuç formatında eklenir.

---

### ADR-087 — Malzeme/Araç Listesi: kolon bazlı filtre + sayfalama + kişisel kolon seçimi (17.07.2026, TAMAM — infra+API+web+masaüstü)

- **Bağlam:** Kullanıcı, malzeme dosyasını (2507 satır) düzeltip içeri aldıktan sonra fark etti: "2600 üstünde
  kayıt olduğu için geliştirme gerekli." İstek üç parça: (1) sütun bazlı filtreler ("içerir" + "başlangıca
  göre" arama), (2) sayfa boyutu seçimi + numaralı (1,2,3…) sayfalama, (3) — soru sorulup netleştirilince —
  hangi kolonların gösterileceğini sağ tık → "Kolonları Ayarla" ile seçebilme, **kişiye özel** (kullanıcı: "bu
  ayar işlemleri her kullanıcıya özel olsun, farklı kullanıcıda görünmesin").
- **Gizli kusur ortaya çıktı:** Malzeme/Araç LİSTE EKRANLARI da (import/export'tan bağımsız olarak)
  `MaterialService.List`/`VehicleService.List`'in **200 satır varsayılanına** dayanıyordu — 2600+ kayıtlı bir
  firmada liste ekranı sessizce yalnız ilk 200'ü gösteriyordu. Yeni `SearchGrid` uçları bunu ATLAR (gerçek
  `COUNT(*)` + `LIMIT/OFFSET`); eski `List(search)` uçları DOKUNULMADAN kaldı (Stok/Talep/Bakım gibi ekranlardaki
  hızlı-arama seçiciler onu kullanır).
- **Kolon kataloğu — TEK KAYNAK:** `DepoWise.Application/Ui/ListColumns.cs` (`MaterialListColumns`,
  `VehicleListColumns` — anahtar+etiket+varsayılan-görünür listesi). Web'in Application'a referansı olmadığından
  aynı liste `DepoWise.Web/Services/ListColumns.cs`'te AYNADIR (VehicleStatus ile aynı ikiz-dosya deseni).
  Kapsam = yeni kayıt formundaki HER alan, fotoğraf HARİÇ (kullanıcı isteği); "Açılış Stok" da BİLİNÇLİ OLARAK
  yok (kartın kalıcı alanı değil, yalnız kayıt anındaki bir hareket) — "Şablon" alanı da yok (form doldurma
  kolaylığı, kalıcı alan değil — malzeme içe aktarımındaki "Şablon" istisnasıyla AYNI gerekçe).
- **Sorgu motoru — `GridQuery` (Infrastructure/Database, paylaşılan):** her filtre alanı "içerir" (`LIKE
  '%terim%'`) arar; birden çok filtre aktifken "başlangıca göre" önceliği DETERMİNİSTİK sırayla uygulanır
  (kataloğun sabit sırasına göre, hangi kutunun önce doldurulduğuna bakılmaksızın). Hesaplanan/join'lenmiş
  kolonlar (stok bakiyesi, durum etiketi, uyumlu araç listesi gibi) SQL WHERE'de doğrudan kullanılamadığından
  (`SELECT * FROM (iç sorgu) t WHERE ...`) derived-table sarma deseni kullanılır — ham VE hesaplanan HER kolon
  aynı filtre/sıralama mantığından geçer. `MaterialService.SearchGrid` / `VehicleService.SearchGrid` bu deseni
  kullanır; `GridResult<T>` (Items+TotalCount+Page+PageSize+TotalPages) numaralı sayfalamayı besler.
- **Kolon tercihi — KİŞİSEL (Migration 047, `user_list_preferences`):** anahtar `(user_id, list_key)` — FİRMA
  değil, doğrudan kullanıcı (aynı firmadaki iki kullanıcı bile birbirinin seçimini görmez). Web: sunucu tarafında
  (`GET/POST /api/me/list-columns/{listKey}`, oturumdan user_id zorlanır). Masaüstü: KENDİ yerel SQLite'ında
  (aynı migration, ayrı anlam — dual-schema deseni ama bu kez "sunucu/yerel" değil "web/masaüstü" ayrımı; iki
  taraf SENKRONLANMAZ, kasıtlı — bir kullanıcının web'deki kolon seçimi masaüstünü etkilemez, ekranlar farklı
  kolon setleri sunabilir).
- **UI:** Web (MudBlazor) — her görünür kolon için `MudTextField` filtre kutusu + `MudPagination` (native
  numaralı sayfalama) + sağ-tık (`@oncontextmenu`) açılan `ColumnPickerDialog`. Masaüstü (Avalonia) — MudTable
  yok; kolon görünürlüğü SABİT XAML kolonları + yeni `Conv.ColumnVisible` converter (Auto+SharedSizeGroup kolon,
  görünmeyince 0'a çöker) ile çözüldü; sayfalama Prev/Next + numaralı buton `ItemsControl`; kolon seçici
  `ColumnPickerWindow` (ConfirmWindow ile AYNI modal desen). `MaterialRow`/`VehicleRow` eski 8-parametreli
  çağrılarla (Muadil Malzeme seçici) GERİYE UYUMLU — yeni alanlar varsayılan değerli, sonuna eklendi.
- **Test:** `MaterialGridTests` (12) + `VehicleGridTests` (7) + `UserListPreferenceTests` (5) — içerir arama,
  başlangıca göre öncelik, büyük/küçük harf duyarsız, birden çok filtre birleşimi, join'li/hesaplanan kolon
  filtresi, sayfalama (toplam/sayfa sayısı/tekrarsız/sınır kırpma), tenant izolasyonu, kişisel tercih izolasyonu.
  497/497.
- **⚠️ Masaüstü UI görsel olarak doğrulanamadı** (bu ortamda Avalonia masaüstü uygulamasını çalıştırıp
  etkileşimli test edecek bir araç yok) — yalnız temiz derleme + backend testleriyle güvence alındı. Web tarafı
  gerçek tarayıcıda uçtan uca doğrulandı (filtre/sayfalama/kolon seçimi/kalıcılık).

---

### ADR-086 — Açılış stoğu NEGATİF olabilir (17.07.2026, TAMAM — infra+API+web+masaüstü)

> ⚠️ Bu, `CLAUDE.md` §4 "negatif stok" değişmezinin BİLİNÇLİ ve SINIRLI bir yorumudur. Kullanıcının açık
> talebi (§1) bu satırın üstündedir; karar burada kayıt altına alınmıştır.

- **Bağlam:** Kullanıcının babasının gerçek malzeme dosyasında (2507 satır) 63 satırda **Açılış Stok negatif**
  (örn. −59, −1, −78). İçe aktarım bunları reddediyordu. Kullanıcı: "eksi stok kontrolünü kaldıralım.
  sonradan projemizi satın alan firmalar mevcut stoklarını ekleyebilirler." — yani sistemi devralan bir firma
  mevcut/eksik başlangıç stoğunu OLDUĞU GİBİ girebilmeli.
- **KAPSAM — yalnız BAŞLANGIÇ değeri gevşetildi; operasyonel koruma AYNEN korunur:**
  - **Gevşetilen:** açılış/ilk stok girişi (`OpeningStockService.RecordOpening`, malzeme içe aktarımı,
    web + masaüstü malzeme formu, `POST /api/materials`). Artık negatif açılış kabul edilir; yalnız **sıfır**
    reddedilir (anlamsız hareket).
  - **KORUNAN (dokunulmadı):** operasyonel ÇIKIŞ'ın negatif-bakiye engeli (`StockService.ApplyDelta`,
    `allowNegative:false`) — bir çıkış bakiyeyi eksiye DÜŞÜREMEZ. Bu §4'ün asıl koruduğu kuraldır.
  - Fiyat ve Min Stok negatif OLAMAZ (eşik/tutar anlamsız) — yalnız STOK MİKTARI negatif olabilir.
- **Ledger sözleşmesi korunur (kritik tasarım kararı):** negatif açılış, `stock_movements`'a **quantity DAİMA
  pozitif + direction=−1** olarak yazılır (ör. −9 → dir=−1, qty=9). Neden: (1) senkron içerik doğrulaması
  (`BusinessSyncService`) `stock_movements.quantity` negatifse satırı reddeder → hareket düzeyi kalkanı
  KORUNUR; (2) `RecomputeBalances` = Σ(yön×miktar) doğru kalır (−1×9 = −9). Türetilmiş **bakiye**
  (`stock_balances`) negatif olabilir → o alan senkron negatif-kalkanından ÇIKARILDI.
- **Bozuk-veri koruması nasıl sürüyor:** bakiye türetilmiştir; sunucu her push sonrası
  `RecomputeBalances` ile bakiyeyi hareketlerden yeniden hesaplar (otoriteli). Ham negatif `quantity` yalnız
  bozuk/kötü niyetli snapshot'tan gelebilir ve hâlâ reddedilir (`Apply_NegatifHareketMiktari_Reddedilir`).
- **Test:** `MaterialTests` (+3: negatif açılış yön/miktar & bakiye · sıfır reddedilir · RecomputeBalances
  round-trip) · `ImportFullFieldsTests` (+2: negatif açılış kabul & bakiye · negatif fiyat reddedilir) ·
  `BusinessSyncTests` (negatif BAKİYE artık uygulanır; negatif HAREKET miktarı hâlâ reddedilir). 473/473.
- **NOT (kapsam dışı, kullanıcıya bildirildi):** babanın dosyasındaki 2. sorun — her satırda para birimi
  "TL" yazılı (sistem TRY/USD/EUR bekler). Bu içe aktarım için hâlâ engel; kullanıcı Excel'de TL→TRY
  yapmalı (veya ayrı bir talep gelirse TL→TRY otomatik eşlemesi eklenir).

---

### ADR-085 — Makine "tanım sıfırlama" (17.07.2026, TAMAM — API+web+masaüstü)

- **Bağlam:** Kullanıcının babası bir makinede (DESKTOP-SIKIB3U, süper admin makinesi) önce bir "test
  firması" ile giriş yapmıştı; sonra aynı makinede **asıl firma** ile giriş yapamadığını düşündü. Kullanıcı
  istek: "makine yönetimi ekranına makine tanımı sıfırlama butonu oluştursak ve loginden sonra gelen ekranda
  eşitleme yaptıktan sonra kendini login ekranına yönlendirse. sonra ilk girilen kullanıcı ile firma makine
  tanımı tanımlansın."
- **Teşhis:** `sync_devices` zaten `(company_id, device_name)` çiftiyle anahtarlanır — aynı fiziksel makine
  birden çok firmada bağımsız satıra sahip olabilir; bu yüzden farklı firmayla giriş kendiliğinden ayrı bir
  satır açar. Asıl ihtiyaç, kullanıcının tarif ettiği **elle "tanımı temizle" düğmesi** — makineyi TÜM
  firmalardan tamamen koparıp "ilk kurulum" durumuna döndürmek (örn. bir makineyi bir müşteriden alıp
  başkasına devretmek, ya da kota/karışıklık şüphesinde temiz başlangıç). ADR-084 (firma yerel sıfırlama)
  ile KARIŞTIRILMAMALI: o firma verisini sıfırlar, bu makinenin firma/şube AİDİYETİNİ sıfırlar.
- **Karar:** `machine_resets` (Migration **046**), `company_local_resets`(ADR-084) ile AYNI iki-anlamlı
  desen ama **makine adıyla** anahtarlanır (firma ile DEĞİL) — çünkü sıfırlama isteği fiziksel makineye
  aittir, hangi firmayla giriş yapılırsa yapılsın algılanmalıdır:
  1. Süper admin Makine Yönetimi'nde bir satırın **"Tanımı Sıfırla"** butonuna basar → o makine adına ait
     **TÜM firmalardaki** `sync_devices` satırları silinir (`MachineResetService.RequestReset`) + künye yazılır.
  2. Masaüstü, girişten sonra eşitleme adımında (`LoginViewModel.FinalizeLoginAsync`, purge/yerel-sıfırlama
     kontrollerinden ÖNCE) künyeyi görür → `DesktopServices.MachineCompanyId/BranchId` + `MachineGate`
     önbellek dosyalarını (`machine_status.txt`/`machine_branch.txt`) temizler → **girişi iptal eder ve
     login ekranına döner** (`Back()`).
  3. Sonraki girişte makine "ilk kurulum" durumundadır (`MachineBranchId` boş) → giriş yapan **ilk
     kullanıcı** (süper admin değilse) mevcut "İlk Kurulum" onay akışıyla makineyi kendi firması/şubesiyle
     yeniden tanımlar; süper admin için de "makine firması" kısayolu (UseMachineCompany) temiz başlar.
- **ADR-084'ten kasıtlı FARKI — GİRİŞİ DURDURUR:** yerel sıfırlama girişe izin verip devam eder (veri
  sıfırdan yeniden dolar); makine sıfırlaması **durdurur** — çünkü sıfırlama sonrası makinenin hangi
  firmaya ait olduğu belirsizdir, o firmanın verisiyle devam etmek yanlış olur.
- **YIKICI DEĞİL:** iş verisi (malzeme/araç/stok/personel…) hiç etkilenmez; yalnız "bu makine hangi
  firmaya ait" bilgisi silinir. ADR-083'teki (kalıcı firma silme) ile karıştırılmamalı; özel kod GEREKMEZ.
- **Künye SİLİNMEZ:** çevrimdışı bir makine haftalar sonra açılsa bile isteği görüp bir kez uygular (ADR-083/
  084 ile aynı fail-safe ilkesi — çevrimdışıyken hiçbir şey silinmez).
- **Test:** `MachineResetTests` (8) — istek durumda görünüyor · tekrar istek zamanı güncelliyor · süper admin
  olmayan bırakamıyor · boş makine adı reddediliyor · **TÜM firmalardaki kayıtlar silinir** · başka makine
  etkilenmez · sıfırlama sonrası aynı makine adıyla farklı firmaya yeniden kayıt çalışıyor.

---

### ADR-084 — Firma "yerel sıfırlama" isteği (16.07.2026, TAMAM — API+web+masaüstü)

- **Bağlam:** Kullanıcı bir firmanın (Sevgi A.Ş.) bilgilerini/adını web'den güncelledi; bu firmayla 2 yerel
  makinede daha önce giriş yapılmıştı. "Bu bir soruna yol açar mı, ve bu firmanın TÜM yerel kayıtlarını
  (o makineler o an kapalı olsa bile) bir sonraki girişte bir kerelik temizleyecek bir yapı istiyorum" dedi.
- **Teşhis (rename'in etkisi):** Kod incelemesi iki ayrı davranış ortaya çıkardı:
  1. Firma **adı** her çevrimiçi girişte otomatik düzeliyordu (`CompanySyncService.MirrorLocalAsync`,
     `ON CONFLICT DO UPDATE SET name=...`) — sorun yoktu.
  2. **Diğer alanlar** (vergi no/dairesi, adres, telefon, e-posta, yetkili, kotalar) hiç aynalanmıyordu —
     yalnız `id` ve `name` okunup yazılıyordu. Web'de bunlar değişince yerel makinelerde **sonsuza kadar
     eski** kalıyordu. Bu, gerçek (küçük ama gerçek) bir kusurdu; **aynı oturumda düzeltildi** (aşağıya bkz).
- **Karar — iki parça:**
  1. **`MirrorLocalAsync` tüm alanları aynalar** artık (tax_no/tax_office/address/phone/email/
     authorized_person/max_users/max_admins/machine_quota) — yalnız isim değil. Bu düzeltme olmadan,
     aşağıdaki yeni özellik firma satırını sıfırladıktan sonra bu alanları **NULL/0** bırakırdı (eskiden
     "bayat" olan alanlar daha da kötüleşirdi) — bu yüzden ikisi birlikte yapıldı.
  2. **Yeni "Yerel Sıfırlama" isteği** (`company_local_resets`, Migration **045**) — ADR-083'ten (kalıcı
     silme) KASITLI olarak FARKLI bir mekanizma: firma **sunucuda durur**, erişim **engellenmez**; yalnız
     o firmanın makineleri bir sonraki **çevrimiçi** girişte kendi yerel kopyalarını **bir kez** temizleyip
     yeni-makine-ilk-girişiyle aynı yoldan sıfırdan yeniden doldurur.
- **Aynı tablo, iki anlam (server ↔ masaüstü):** `company_local_resets` şeması sunucuda VE her masaüstünün
  kendi yerel SQLite dosyasında **aynıdır** ama farklı yorumlanır: sunucuda "en son istenen zaman", her
  makinede "BU makinenin en son UYGULADIĞI zaman". Karşılaştırma `sunucu > yerel` ise wipe uygulanır ve
  yerel satır sunucunun zamanına eşitlenir — böylece istek **tam bir kez** uygulanır, tekrar tekrar değil.
- **"Makine o an kapalı olabilir" şartı:** İstek EPHEMERAL bir sinyal değil, sunucuda KALICI bir satırdır
  (silinene kadar durur). Makine hangi zaman çevrimiçi girişe geçerse (bugün, yarın, ay sonra) o zaman
  algılanır ve uygulanır — bekleme süresi sınırsızdır.
- **Sıra kritik (ADR-083 ile birebir aynı ilke):** kontrol, çevrimdışı kuyruk/push'tan ÖNCE çalışır — aksi
  halde makine, henüz temizlenmemiş eski veriyi sunucuya geri gönderirdi.
- **Silme mantığı ADR-083'teki `LocalPurgeService.PurgeLocalCompany` ile AYNIDIR** (kod tekrarı yok) — tek
  fark, bu akışta **giriş engellenmez**; wipe sonrası normal senkron adımları (mirror/pull) devam eder.
- **Kapsam dışı (ADR-083 ile aynı kullanıcı kararı):** masaüstünde yeni ekran yok; buton yalnız **web**
  Firma Tanım listesinde ("Yerel Sıfırlama İste" ikonu, süper-admin-only). Özel kod GEREKMEZ (bu, ADR-083'ün
  aksine YIKICI/erişim-engelleyici değildir — sunucu verisi hiç etkilenmez).
- **Test:** `CompanyLocalResetTests` (7) — istek durumda görünüyor · tekrar istek zamanı güncelliyor ·
  süper admin olmayan bırakamıyor · olmayan firma reddediliyor · kendi firman İÇİN de istek bırakılabiliyor
  (ADR-083'ten farkı) · başka firmaya sızmıyor.

---

### ADR-083 — Firma KALICI silme + "özel kod" (16.07.2026, TAMAM — API+web+masaüstü)

> ⚠️ **Bu ADR, `CLAUDE.md` §4'ün "Operasyonel kaydı fiziksel silme; iptal/ters kayıt ve audit kullan"
> kuralının BİLİNÇLİ ve SINIRLI bir istisnasıdır.** `CLAUDE.md` §1 gereği kullanıcının açık talebi bu
> dosyanın üstündedir; karar burada kayıt altına alınmıştır.

- **Bağlam:** Kullanıcı sistemi gerçek verilerle uçtan uca test etmek istiyor ve bunun için bir firmanın tüm
  kayıtlarını hem sunucudan hem makinelerden **tamamen** silebilmesi gerekiyor. Mevcut Firma Tanım ekranı
  firmayı yalnız **pasife alır** (soft delete) — veri diskte ve makinelerde durmaya devam eder, temiz test
  ortamı kurulamaz.
- **Karar:** Yeni **"Kalıcı Silme"** ekranı (yalnız **web**, `purge_company`, süper-admin-only, devredilemez).
  Seçilen firmanın tüm satırları `company_id` üzerinden fiziksel silinir; fotoğraflar (`files/{id}`) ve makine
  yedekleri (`backups/{id}`) diskten silinir. **Kapsam yalnız FİRMA bazlıdır** — normal iş akışlarında silme
  YASAK olmaya devam eder (iptal/ters kayıt + audit).
- **Kilit (çok katmanlı, fail-closed):** süper admin **+ özel kod + şifre + firma adını birebir yazma**.
  - **Özel kod:** şifreden AYRI bir sır; yalnız süper adminde vardır, ilk **web** girişinde oluşturulur,
    `users.special_code_hash`'te **hash**'lenir. Unutulursa süper admin **şifresiyle** yenisi belirlenir
    (ekran kalıcı kilitlenmesin — kullanıcı kararı). Kod yoksa doğrulama **daima false** (kodsuz ekran açılmaz).
  - **Kendi firmanı silmek YASAK:** ADR-064'te kendi firmasını silen süper admin sistemden kilitlendi,
    ADR-068'de oturumu 401'e düştü. Kalıcı silmede telafisi YOK → hem serviste hem ekranda engellenir.
- **Künye (tombstone) — `company_purges`:** silme sonrası kalan tek iz. Purge sırasında **asla silinmez**.
  Masaüstü giriş sonrası eşitleme adımında `/api/sync/purge-status` ile bunu sorar; "silinmiş" ise **yerel
  veriyi temizler ve login'e döner**. Künye olmasaydı çevrimdışı bir makine kendi kopyasını sunucuya geri
  push edip **veriyi diriltirdi**.
- **Sıra kritik:** masaüstünde purge kontrolü, çevrimdışı kuyruk (`sync_outbox`) sunucuya **işlenmeden ÖNCE**
  çalışır — aksi halde makine silinmiş firmanın kayıtlarını geri gönderir.
- **Fail-safe:** sunucuya erişilemezse (çevrimdışı, `null`) yerel veriye **DOKUNULMAZ**. Silme yalnız sunucu
  açıkça "silindi" dediğinde uygulanır — "cevap alamadım" yerel veri silme gerekçesi değildir.
- **Korunanlar:** `schema_migrations`, `sqlite_sequence`, `company_purges` ve sistem rolleri
  (`roles.company_id IS NULL` = tüm firmalar) — aksi halde purge'den sonra hiçbir firmada rol atanamazdı.
- **Kapsam dışı (kullanıcı kararı):** masaüstünde **yeni ekran yok** ve **login'de özel kod alanı yok**;
  masaüstü yalnız silmeyi algılar. Silme işlemi web'den yapılır.
- **Şema:** Migration **044** (`users.special_code_hash` + `company_purges`).
- **Test:** `CompanyPurgeTests` (9) — kendi firması silinemez · süper admin olmayan silemez · firma+verisi gider
  ve künye kalır · silinen firmanın kullanıcısı giriş yapamaz · **başka firmaya dokunmaz** · sistem rolleri
  korunur · künye yalnız silinmiş firmada döner · özel kod fail-closed/kısa kod reddi/rol kısıtı.

---

## Faz 00 kararları (2026-06-26)

### ADR-001 — Çözüm/klasör düzeni
- **Bağlam:** Boş repo; web + masaüstü + ortak sözleşme bir arada.
- **Karar:** `src/DepoWise.Desktop` (Avalonia UI), `src/DepoWise.*` katman projeleri (Domain/Application/Infrastructure), `web/` (Next.js), `docs/`, `artifacts/`. Tek `.sln` masaüstü tarafını toplar.
- **Alternatif:** Tek monolit proje — reddedildi (test izolasyonu ve katman ayrımı zorlaşır).
- **Sonuç:** Faz 01'de iskelet bu düzene göre kurulacak.

### ADR-002 — Masaüstü mimarisi
- **Karar:** .NET 8, Avalonia, MVVM (CommunityToolkit.Mvvm), Dapper, SQLite. UI thread'de DB/ağ yok; Dapper parametreli; transaction tek connection üzerinde.
- **Gerekçe:** Analiz §3 ve `.claude/rules/desktop.md` ile birebir.

### ADR-003 — Yerel DB yolu ve bağlantısı
- **Karar:** SQLite mutlak yol `%LOCALAPPDATA%\DepoWise\Data\<environment>\depowise.db`. Connection: `Cache=Private`, WAL, `foreign_keys=ON`, `busy_timeout=5000`. Açılışta host/DB-yolu/journal_mode/health loglanır.
- **Gerekçe:** COMODO sandbox'ın sanal-DB tuzağını önler (relative path yasak).

### ADR-004 — COMODO güvenli çalıştırma
- **Karar:** Debug'da `UseAppHost=false`. Uygulama yalnız `dotnet build` + `dotnet run/--project` veya `dotnet <dll>` ile çalışır. Proje `.exe`/`.bat` ASLA çalıştırılmaz; PreToolUse hook bunu zorlar.
- **Sonuç:** Doğrulandı (hook + Directory.Build.props mevcut ve tutarlı).

### ADR-005 — Merkezi veri ve API
- **Karar:** PostgreSQL + Drizzle + migration; API `/api/v1`, ortak hata modeli + correlation id + OpenAPI sözleşmesi. `company_id` yalnız server session'dan; payload'dan tenant kabul edilmez (fail-closed).
- **Not:** Üretim PG sağlayıcısı tek markaya bağlanmaz (KNOWN_ISSUES).

### ADR-006 — Kritik operasyon bütünlüğü
- **Karar:** Stok/sayaç/yakıt/bakım/onay işlemlerinde LWW yasak; `operation_id` ile idempotency + transaction + audit/outbox tek transaction. Operasyonel kayıt fiziksel silinmez (iptal/ters kayıt). Stok hareket defteri tek doğru kaynak.
- **Gerekçe:** Analiz §7 ve §11 kabul testleri.

### ADR-007 — Para, zaman, kimlik, dosya
- **Karar:** Para `decimal` + `currency_code`, kur snapshot; zaman merkezi UTC / sözleşmede Unix ms; ana kayıtlar UUID/ULID, kullanıcı belge no ayrı; fotoğraf `file_records` metadata + storage provider (DB base64 değil).
- **Gerekçe:** Analiz §7, §6.16.

---

## Faz 01 kararları (2026-06-26)

### ADR-008 — Çözüm yerleşimi ve hedef framework
- **Karar:** `src/DepoWise.{Domain,Application,Infrastructure,Desktop}` + `tests/DepoWise.Tests` + `apps/web`. Tüm .NET projeleri **net8.0** (Avalonia template'in ürettiği net10.0 hedefi düşürüldü; SDK 8.0.422).
- **Gerekçe:** CLAUDE.md .NET 8 değişmezi; katmanlı bağımlılık Domain←Application←Infrastructure←Desktop/Tests.

### ADR-009 — Ortak sözleşmelerin iki platformda eşlenmesi
- **Karar:** Hata modeli (`ApiError`+`ErrorCodes`), keyset pagination (`PageRequest`/`PagedResult`), zaman (UTC + Unix ms) ve correlation_id hem .NET (`Application/Common`) hem web (`lib/contracts.ts`) tarafında **birebir aynı kodlar/biçimle** tanımlandı. OpenAPI bu sözleşmeyi `apps/web/docs/openapi.yaml`'de belgeliyor.
- **Gerekçe:** Analiz §3/§5 fonksiyonel eşitlik; tek doğru sözleşme.

### ADR-010 — Config fail-closed
- **Karar:** Web `loadConfig()` zod ile doğrular; **Production**'da `DATABASE_URL`/`SESSION_SECRET` eksikse `ok=false` (health 503). Geliştirmede uyarı niteliğinde. Sırlar yalnız environment'tan.
- **Gerekçe:** Analiz §9 (başlangıçta eksik/zayıf sır fail-closed).

### ADR-011 — Güvenlik yükseltmesi (tedarik zinciri)
- **Bağlam:** `next@15.1.6` CVE-2025-66478 açığı içeriyordu.
- **Karar:** Yamalı `next@^15.5.19`'a yükseltildi (eslint-config-next eşlendi). "Gereksiz yükseltme yapma" kuralının istisnası: kritik güvenlik açığı (analiz §9 tedarik zinciri).
- **Sonuç:** Yükseltme sonrası typecheck/build yeşil.

---

## Faz 02 kararları (2026-06-26)

### ADR-012 — Migration stratejisi
- **Karar:** Yerel SQLite için kod tabanlı sürümlü migration (`IMigration`/`MigrationRunner`, `schema_migrations` izleme tablosu, her migration tek transaction, idempotent). Merkezi PostgreSQL için Drizzle Kit ile üretilen SQL migration dosyaları (`apps/web/drizzle`).
- **Gerekçe:** İki platform farklı motorlar; ortak şema kavramı korunur, her motor kendi migration aracını kullanır.

### ADR-013 — Standart kolon sözleşmesi
- **Karar:** Tüm operasyonel tablolar `id` (UUID/ULID, TEXT/text), `company_id`, `created_at`/`updated_at` (INTEGER/bigint Unix ms), `version` (optimistic concurrency), uygun olduğunda `is_deleted`. Para alanları decimal-as-TEXT (SQLite) / numeric (PG) + `currency_code`.
- **Gerekçe:** Analiz §7; tenant + soft-delete + concurrency + zaman tutarlılığı tek desende.

### ADR-014 — Tenant izolasyonu fail-closed
- **Karar:** `company_id` `TenantContext`/`TenantGuard` ile yalnız güvenilir bağlamdan; boşsa exception. Tüm okuma/yazma sorguları `TenantSql.ScopePredicate` kullanır. Regresyon: tenant izolasyon + başka-firma-silemez testleri.
- **Gerekçe:** Analiz §9; tenant kontrolü UI'a bırakılmaz.

### ADR-015 — Keyset pagination + soft-delete + audit
- **Karar:** Sayfalama keyset (created_at DESC, id DESC) + opak `Cursor`; toplam sayı zorunlu değil. Silme = `is_deleted=1` + version+1 (fiziksel silme yok). Kritik mutasyonlar `AuditWriter` ile aynı transaction'da audit yazar.
- **Gerekçe:** Analiz §7 (keyset kararlı sıralama), §2/§7 (silme yerine soft-delete/ters kayıt), §9 (audit).

---

## Faz 03 kararları (2026-06-26)

### ADR-016 — Parola hash algoritması (parite)
- **Karar:** PBKDF2-HMAC-SHA256, 100k iter, 16B salt, 32B hash; biçim `pbkdf2$sha256$<iter>$<saltB64>$<hashB64>`. Hem .NET (`Rfc2898DeriveBytes.Pbkdf2`) hem web (`node:crypto.pbkdf2`) aynı biçim → enroll/sync sırasında karşılıklı doğrulanabilir.
- **Alternatif:** BCrypt — reddedildi (iki platformda harici bağımlılık + parite zorluğu); PBKDF2 her iki runtime'da yerleşik.
- **Sonuç:** Parite testle doğrulandı (.NET + node:test).

### ADR-017 — Deny-by-default erişim kontrolü
- **Karar:** `AccessControl` UI ve API'de aynı sonucu üretir; izin kaydı yoksa erişim yok. Süper Admin/Firma Admini bypass. Dashboard/About herkese açık (yalnız okuma). Özel buton/alan da deny-by-default. API sınırında `Require*` → `ForbiddenException` (403).
- **Gerekçe:** Analiz §5/§9; yetki yalnız UI'a bırakılmaz.

### ADR-018 — Tenant kaynağı ve yetki yükseltme koruması
- **Karar:** `company_id` yalnız `SessionContext`'ten; istek payload'ındaki farklı company_id (süper admin değilse) 403. Firma Admini firma değiştiremez (foreign company → reddedilir, sessizce rescope EDİLMEZ). `RoleAssignmentGuard`: admin olmayan admin/süper-admin rolü atayamaz; süper admin yalnız süper admin tarafından oluşturulur.
- **Gerekçe:** Analiz §4/§9; tenant sızıntısı ve privilege escalation fail-closed.

### ADR-019 — Web içi TS import uzantıları (.ts)
- **Karar:** `lib/security` içi göreli importlar `.ts` uzantılı + `allowImportingTsExtensions`. Böylece aynı kaynak hem Next bundler ile derlenir hem de `node --test` (Node 24 type-stripping) ile harici test koşusunda çalışır.
- **Gerekçe:** Web için hafif birim test koşusu (ek bağımlılık olmadan) sağlanır.

---

## Faz 04 kararları (2026-06-27)

### ADR-020 — Ortak UI mantığı platform-bağımsız
- **Karar:** Menü, doğrulama (tarih/numerik), çoklu seçim ve alan görünürlüğü saf mantık olarak iki tarafta da yazıldı (`Application/Ui/*` ve `apps/web/src/lib/ui/*`), aynı kabul senaryolarıyla test edildi. Avalonia/React yalnız bu mantığı bağlar.
- **Gerekçe:** Analiz §5; web ve masaüstü fonksiyonel eşitlik tek kaynaktan.

### ADR-021 — Tarih ve arama davranışı
- **Karar:** Tarih GG/AA/YYYY KESİN biçim + gerçek takvim doğrulaması (.NET `TryParseExact None`; web Date.UTC geri-doğrulama). Aranabilir çoklu seçim Türkçe büyük/küçük harf duyarsız (.NET tr-TR `CompareInfo`; web `toLocaleLowerCase('tr')`); arama seçimi korur; "tümünü seç" yalnız filtre sonucunu ekler.
- **Gerekçe:** Analiz §5; CLAUDE.md Türkçe duyarsız arama standardı.

### ADR-022 — Merkezi tema/branding (sabit değil)
- **Karar:** Renk ve marka metinleri ekrana sabit yazılmaz. `app_settings` (Migration003, global/firma override) → `ThemeTokens`/`BrandingSettings`. Masaüstü `ThemeApplier` ile `Brand.*` DynamicResource; web CSS değişkenleri (`--brand-*`) kök `:root`/layout'tan. Ayar değişiklikleri audit'lenir.
- **Gerekçe:** Kullanıcı talimatı + analiz §5 (tema merkezi yönetilebilir).

---

## Faz 05 kararları (2026-06-27)

### ADR-023 — Firma yönetimi yalnız Süper Admin; tenant fail-closed
- **Karar:** Firma oluşturma/listeleme `CompanyService` ile yalnız Süper Admin; Firma Admini yalnız kendi firmasını görür, `EnsureAccess` başka firmaya erişimi 403'ler. Tüm org servisleri `company_id`'yi session'dan alır.
- **Gerekçe:** Analiz §4; normal admin firma sınırını aşamaz.

### ADR-024 — Kullanıcı şube kapsamı (user_scopes)
- **Karar:** `user_scopes` ile kullanıcı bazlı şube kapsamı. `ScopeResolver`: açık scope öncelikli; yoksa admin → tüm firma şubeleri, admin-olmayan kapsamsız → boş. Şube/personel seçim listeleri ve yazma `EnsureBranchAllowed` ile kapsam dışına taşamaz. Web `lib/org/scope.ts` aynı kararı saf fonksiyonla aynalar.
- **Gerekçe:** Analiz §5/§6.2 (seçim listeleri yalnız kullanıcı kapsamını getirir).

---

## Faz 06 kararları (2026-06-27)

### ADR-025 — Para ve stok temsili
- **Karar:** Para/miktar SQLite'ta TEXT (invariant decimal) + `currency_code`; .NET `Money` ve web `money.ts` ile taşınır. Float YOK. Desteklenen: TRY (baz) / USD / EUR. İşlem anı kuru `stock_movements.fx_rate` snapshot; manuel kur `fx_rates`.
- **Gerekçe:** Analiz §7 (decimal + currency, kur snapshot).

### ADR-026 — Stok hareket defteri ana kaynak; açılış stoğu hareket olarak
- **Karar:** `stock_movements` ana kaynak, `stock_balances` cache (yalnız ledger'la aynı transaction'da güncellenir). Açılış stoğu kart alanı DEĞİL `OpeningStockService` ile 'opening' hareketi; `operation_id` ile idempotent. Doğrudan bakiye set eden API yok.
- **Gerekçe:** Analiz §7/§2; bu fazda bakiye doğrudan değiştirilmez (Faz 07 diğer hareket tipleri).

### ADR-027 — Muadil ve uyumlu araç ilişkileri
- **Karar:** Muadil simetrik (servis çift yön yazar) + self-FK CHECK + döngü güvenli BFS grup çözümü. Uyumlu araç çoklu seçim `material_compatible_vehicles` (vehicle_id FK Faz 08'e ertelendi). Araç→uyumlu malzeme sorgusu güncel stoğu (stock_balances join) gösterir.
- **Gerekçe:** Analiz §6.5; çift yönlü, döngü güvenli ilişki.

---

## Faz 07 kararları (2026-06-27)

### ADR-028 — Stok işlemleri concurrency: IMMEDIATE transaction
- **Karar:** Tüm bakiye değiştiren akışlar `BeginTransaction(deferred: false)` (BEGIN IMMEDIATE) ile yazma kilidini baştan alır → eş zamanlı çıkışlar serialize olur; ikinci işlem güncel bakiyeyi okuyup negatif guard'a takılır. Negatif düşüş `NegativeStockException` + rollback.
- **Alternatif:** Koşullu UPDATE (quantity TEXT karşılaştırması zor) — reddedildi. IMMEDIATE + busy_timeout yeterli ve sade.
- **Kanıt:** `EsZamanli_IkiCikis_NegatifStokOlusturamaz` (Parallel.For).

### ADR-029 — Belge/hareket modeli ve iptal = ters kayıt
- **Karar:** `stock_documents` (in/out/transfer/count) + hareketler belgeye bağlı; doc_no otomatik (PREFIX-YYYY-NNNN). Transfer kaynak çıkış + hedef giriş aynı group_id'de atomik. İptal hareketi FİZİKSEL SİLMEZ: ters hareket üretir, orijinali is_reversed=1 işaretler, belge cancelled. operation_id ile tüm akışlar idempotent.
- **Gerekçe:** Analiz §7 (silme yerine ters kayıt, idempotency, transaction).

### ADR-030 — Bakiye material-global (şube bazlı ertelendi)
- **Karar:** `stock_balances` material düzeyinde tek bakiye; transfer toplam stoğu değiştirmez (net-zero), hareketlerde from/to şube kayıtlı. Şube bazlı bakiye/negatif kontrolü sonraki bir fazda eklenecek (R13).
- **Gerekçe:** Faz 06 şemasını bozmadan ilerlemek; MVP için yeterli, kayıt izi şube bilgisini taşıyor.

---

## Faz 08 kararları (2026-06-27)

### ADR-031 — Sayaç geriye gitmeme + iki yöntem
- **Karar:** `MeterRule` ortak (web+masaüstü). `SetMeter` (doğrudan form düzenleme) geriye gidişi `MeterBackwardException` ile reddeder. `AdvanceMeter` (bakım/yakıt) ileri-only: yeni>mevcut ise ilerletir+loglar, değilse no-op (geçmiş tarihli düşük okumayı ENGELLEMEZ). Her ilerleme `vehicle_meter_logs`'a (old,new,source) yazılır. Güncellemeler IMMEDIATE transaction.
- **Gerekçe:** Analiz §7; kullanıcı talimatı "sayaç geriye düşmesin + tüm değişimler loglansın".

### ADR-032 — Şablondan doldurma (kullanıcı değeri öncelikli) + malzeme kopyalama
- **Karar:** Araç oluştururken `TemplateId` varsa boş alanlar şablondan doldurulur (`?? ` ile; kullanıcı girdisi ezilmez). Şablonun uyumlu malzemeleri yeni aracın `material_compatible_vehicles` kayıtlarına AYNI transaction'da kopyalanır (INSERT OR IGNORE). Otomatik iç kod önek+en büyük no+1 (genişlik korunur).
- **Gerekçe:** Analiz §6.7; AlpDepo deseni, kontrollü doldurma.

---

## Faz 09 kararları (2026-06-27)

### ADR-033 — Bakım atomik akışı + tek stok düşümü
- **Karar:** `MaintenanceService.Save` IMMEDIATE transaction'da: bakım kaydı + her malzeme için TEK 'usage' hareketi (negatif guard, fiyat snapshot `maintenance_materials.unit_price`) + sayaç ileri (AdvanceMeter mantığı) + sonraki hedef + audit. operation_id idempotent (ikinci çağrı çift düşmez). İptal: 'usage_reverse' +1 ile stok geri, kayıt is_cancelled (fiziksel silme yok), idempotent.
- **Gerekçe:** Analiz §7 (tek transaction, tek düşüm, ters kayıt, idempotency).

### ADR-034 — Uyarı eşikleri ve döngü
- **Karar:** `AlertRules` (web+masaüstü): progress=tüketilen/interval; <0.85 Normal, [0.85,0.95) Approaching, [0.95,1.0) Critical, ≥1.0 Overdue. Tüketilen km/saat = current_meter − performed; gün = now − performed_date. Uyarı her (araç,tanım) için EN SON non-cancelled bakımdan hesaplanır → yeni bakım girilince otomatik temizlenir.
- **Gerekçe:** Kullanıcı talimatı + analiz §6.8.

---

## Faz 10 kararları (2026-06-27)

### ADR-035 — Yakıt dağıtımı atomik + fiyat snapshot
- **Karar:** `FuelService.Distribute` IMMEDIATE transaction'da: depo bakiye yeterlilik kontrolü + dağıtım (birim fiyat **snapshot**; verilmezse güncel=son depo fiyatı) + araç sayacı ileri (MeterRule) + meter log + audit; operation_id idempotent. Depo bakiyesi = Σgiriş − Σdağıtım (tüm zamanlar). Güncel fiyat değişimi geçmiş dağıtımları ETKİLEMEZ.
- **Gerekçe:** Analiz §7 (tarihsel maliyet snapshot, sayaç bütünlüğü, transaction).

### ADR-036 — Günlük Faaliyet bakım = tek kayıt (çift düşüm yok)
- **Karar:** `DailyActivityService.SaveMaintenanceActivity` ortak `MaintenanceService.Save`'i çağırır (tek `vehicle_maintenances` + tek stok düşümü). `daily_activities` yalnız `maintenance_id` referansı + `stock_processed=1` tutar; burada stok DÜŞMEZ. Böylece kayıt hem Bakım Takibi hem Günlük Faaliyet ekranında görünür, veri tek.
- **Gerekçe:** Kullanıcı talimatı + analiz §6.11 (tek kayıt prensibi).

---

## Faz 11 kararları (2026-06-27)

### ADR-037 — Talep durum makinesi + onay stok düşürmez
- **Karar:** `RequestStatusMachine` (web+masaüstü) geçişleri kısıtlar: draft→pending→approved/rejected/cancelled; approved/rejected/cancelled terminal. Çift onay/yetkisiz/geçersiz geçiş fail-closed. Onay/ret approve butonu + requests edit yetkisi ister; tenant ownership zorunlu. **Onay stok bakiyesini DEĞİŞTİRMEZ.** Stok yalnız `CreateIssueFromRequest` ile (onaylı talep → açık `StockService.IssueOut`). Belge no TLP-YYYY-NNNN tenant/yıl benzersiz.
- **Gerekçe:** Analiz §6.12/§7; kullanıcı talimatı (onay stok düşürmez, stok yalnız gerçek çıkış/teslim).

### ADR-038 — PDF üretimi (QuestPDF)
- **Karar:** Masaüstü/Infrastructure PDF QuestPDF Community ile (`IRequestPdfService`/`RequestPdfService`), `RequestPdfModel` ortak veri modeli; Türkçe karakter korunur. Web tarafı aynı modeli kullanır; binary render hattı sonraya bırakıldı (R16).
- **Gerekçe:** Analiz §6.12 (PDF çıktısı); .NET'te yerleşik, lisans Community.

---

## Faz 12 kararları (2026-06-27)

### ADR-039 — Rapor kapısı + tenant/firma filtresi
- **Karar:** `ReportGate.EnsureRunnable` ağır raporu `Executed=false` iken çalıştırmaz (kullanıcı Sorgula/Filtrele'de Executed=true yapar). Raporlar tenant + "reports" permission fail-closed. Firma filtresi yalnız Süper Admin'e görünür (`ShowCompanyFilter`); hedef firma `TenantAccessGuard.ResolveCompanyId` ile çözülür (normal admin başka firma isteyemez). Web `lib/reports/gate.ts` aynı.
- **Gerekçe:** Analiz §6.14/§7 (ağır rapor manuel tetik, tenant sızıntısı yok).

### ADR-040 — Excel export (ClosedXML) + import dry-run politikası
- **Karar:** `TableModel` → `.xlsx` ClosedXML ile (sayısal hücreler sayı). İçe aktarım: örnek başlık + ön kontrol + **dry-run (DB'ye yazmaz)** + satır bazlı hata (ilk 15) + commit. Politika: **satır bazlı** (bir hatalı satır diğerlerini bozmaz), commit `MaterialService.Create` ile iş kurallarını atlamaz (tenant/permission/kod benzersiz/currency). Web `lib/reports/import.ts` aynı doğrulama.
- **Gerekçe:** Analiz §6.15; kullanıcı talimatı (örnek dosya + ön kontrol + satır hata + dry-run).

---

## Faz 13 kararları (2026-06-27)

### ADR-041 — Dosya güvenliği + ayrık dosya kaydı (base64 yok)
- **Karar:** `FileValidation` ortak: ≤7MB, izinli MIME (jpeg/png), **magic-byte** ile gerçek tip (uzantı/declared MIME'a güvenmez; sahte içerik + MIME-içerik uyuşmazlığı reddi), güvenli ad. Fotoğraflar `IFileStorageProvider` (yerel disk; swappable) ile saklanır; operasyonel tabloya **base64 yazılmaz** — yalnız `file_records` metadata (provider/key/mime/size/sha256). Storage kök içine sınırlı (path traversal koruması). Web `lib/files/validation.ts` aynı.
- **Gerekçe:** Analiz §6.16/§9.

### ADR-042 — Çöp Kutusu + yedekleme
- **Karar:** `TrashService` yalnız master-data soft-delete kayıtlarını listeler/geri yükler; özel buton (RestoreTrash) + **yeniden doğrulama (reauth)** + tenant fail-closed. Operasyonel kayıtlar çöp kutusunda DEĞİL (iptal/ters kayıt). `BackupService`: `VACUUM INTO` tutarlı yedek, 30 gün retention, `PRAGMA integrity_check`, geri yükleme admin+reauth ve `SqliteConnection.ClearAllPools()` ile dosya kilidi olmadan.
- **Gerekçe:** Analiz §6.17-6.18/§9; gerçek geri yükleme + bütünlük kanıtı.

---

## Faz 14 kararları (2026-06-27)

### ADR-043 — Offline write + outbox atomik; idempotent push
- **Karar:** Yerel write ve `sync_outbox` AYNI SQLite transaction (`OutboxWriter.Enqueue`); operation_id + payload_hash + base_version taşınır; rollback hiçbirini bırakmaz. Push'ta operation_id `sync_inbox` ile idempotent (ikinci ulaşım → already_applied; çift kayıt yok). Offline veri yeniden açılışta kalıcı.
- **Gerekçe:** Analiz §8 (yerel+outbox tek transaction, idempotent retry).

### ADR-044 — Kritik işlemlerde LWW yasak; sunucu otoriteli + conflict
- **Karar:** Kritik entity'lerde (stok/sayaç/yakıt/bakım/onay) basit LWW YOK: sunucu doğrulaması zorunlu (validator yoksa/red ise rejected + `sync_conflicts`). Düşük-riskli kart alanlarında base_version uyuşmazlığı → conflict (kör overwrite yok). Pull seq cursor; bozuk sayfada rollback + cursor sabit. Cihaz: tek-kullanımlık 10 dk enrollment anahtarı + master onay + token (hash saklı); pending/revoked cihaz push/pull'da 403.
- **Gerekçe:** Analiz §8-9; kullanıcı talimatı (LWW yok, operation_id + sunucu doğrulaması zorunlu).

---

## Faz 15 kararları (2026-06-27)

### ADR-045 — Sürüm yönetimi + güncelleme yaşam döngüsü
- **Karar:** `ReleaseService` (yalnız Süper Admin) `app_releases` yayınlar (SemVer benzersiz + 64-hex checksum + min_supported + signed). `UpdateService`: `Check` (güncelleme/min-supported/imzasız uyarı), `VerifyChecksum` ile **bozuk paket kurulmaz** (hiçbir değişiklik), `ApplyUpdate` 0-100 yüzde + hata logu, **başarısız kurulumda yedekten rollback** (eski sürüm açılır). Web `lib/update/update.ts` aynı SemVer/checksum/kontrol mantığı.
- **Gerekçe:** Analiz §6.19; kullanıcı talimatı (checksum, yüzde, hata kaydı, rollback).

### ADR-046 — COMODO güvenli çalıştırma kanıtı (sürdürülüyor)
- **Karar:** Geliştirme makinesinde proje EXE/BAT çalıştırılmaz; yalnız `dotnet` host. Hook `comodo_guard.ps1` .bat + imzasız `DepoWise*.exe`'yi engeller; Debug `UseAppHost=false`. Gerçek DB mutlak `%LOCALAPPDATA%\DepoWise\Data\<env>\depowise.db`; açılışta host/yol/WAL/health loglanır. Kapat-aç sonrası veri **aynı DB'de kalır** (testle kanıt; `ClearAllPools` ile kilit yok). Code-signing maliyetli kalem → yayın öncesi karara bırakıldı; imzasız sürümde kullanıcıya şeffaf uyarı.
- **Gerekçe:** CLAUDE.md §0/§6; kullanıcı talimatı + analiz §10.

---

## Faz 16 kararları (2026-06-27)

### ADR-047 — Güvenlik başlıkları + CSRF + rate limit + redaction
- **Karar:** Web başlıkları `next.config.mjs` (CSP/nosniff/X-Frame DENY+frame-ancestors none/Referrer/Permissions; HSTS yalnız Production). CSRF double-submit sabit-zaman doğrulama (fail-closed). `RateLimiter` (login 5/5dk, sync 60/dk, admin 30/dk) iki platformda. `LogRedactor`/`redact` ham secret/PII (password/token/secret/authorization/connstr/session/Bearer) maskeler. Sırlar koda yazılmaz; başlangıçta eksik sır fail-closed.
- **Gerekçe:** Analiz §9; kullanıcı talimatı (fail-closed, sır koda yazma).

### ADR-048 — Token rotasyonu + dependency advisory politikası
- **Karar:** Cihaz token rotasyonu (`RotateDeviceToken`) eski token'ı anında geçersiz kılar; revoke push/pull'da 403. `npm audit` açıkları yalnız **dev/build araçlarında** (eslint/drizzle-kit→esbuild/next→postcss) — runtime maruziyeti yok; `--force` breaking olduğu için uygulanmadı, R23'te izlenir. Code-signing/pentest/MFA maliyetli kalemler `SECURITY.md`'de yayın-öncesi/sonrası karara bırakıldı (temel güvenlikten ayrı).
- **Gerekçe:** Analiz §9 (tedarik zinciri, rotasyon); CLAUDE.md (gereksiz upgrade yapma).

---

## Faz 17 kararları (2026-06-27)

### ADR-049 — Yayın adayı kapsamı: backend RC, UI yayın-engeli
- **Karar:** DepoWise 1.0.0-rc; **backend/iş mantığı + sözleşmeler + testler yayın adayı olgunluğunda** (187 .NET + 66 web test, uçtan uca akış dahil). Genel kullanıcı yayını için UI ekran bağlama (R10), web login akışı (R8/R9) ve canlı PostgreSQL migration (R4/R7) **yayın engeli** olarak kayda geçti; test edilmeyen UI "tamamlandı" sayılmadı (analiz §14 dürüst tamamlanma tanımı).
- **Gerekçe:** Analiz §11-14; kullanıcı talimatı (test edilmemiş işlemi tamamlandı işaretleme).

### ADR-050 — Release candidate checksum yayın akışı
- **Karar:** RC artefaktı Release publish + zip; kimliği SHA-256 ile sabitlenir (`RELEASE_CANDIDATE.md`). Üretim dağıtımında bu checksum `ReleaseService.Publish` ile yayınlanır; updater indirme sonrası doğrular (bozuk paket kurulmaz). artefaktlar git'e dahil edilmez (`.gitignore artifacts/`).
- **Gerekçe:** Analiz §6.19; izlenebilir/yeniden üretilebilir yayın.

### ADR-051 — Yayın güvenliği sertleştirmesi (05.07.2026)
- **Karar:** (1) JWT anahtarı üretimde zorunlu; `DEPOWISE_JWT_KEY` yoksa API açılmaz (dev fallback yalnız Development). (2) Seed admin/superadmin şifreleri sabit değil: env (`DEPOWISE_SEED_ADMIN_PASSWORD` / `DEPOWISE_SEED_SUPERADMIN_PASSWORD`) veya rastgele üretilip bir kez konsola yazılır. (3) `/api/admin/reset-data` üretimde `DEPOWISE_ALLOW_RESET=1` olmadan 403. (4) `/api/auth/login` ve `/api/auth/sync-login` IP bazlı 30 istek/5 dk sınırlı (RateLimiter ilk kez bağlandı; NAT arkası ofisler için gevşek pencere). (5) 500 hatalarında ham exception mesajı client'a dönmez; konsol loguna yazılır.
- **Gerekçe:** Canlı test + kod incelemesi bulguları (bilinen dev anahtarı + bilinen seed şifreyle tam ele geçirme; brute-force sınırsızdı).

### ADR-052 — Web oturum geri yükleme kapısı (05.07.2026)
- **Karar:** MainLayout, oturum ProtectedLocalStorage'dan geri yüklenene kadar `@Body` render etmez (spinner). `Auth.Loaded` artık restore tamamlanınca set edilir.
- **Gerekçe:** F5/doğrudan URL'de sayfalar token'sız API çağrısı yapıp yanlış "kayıt yok"/"yalnız süper admin" gösteriyordu (canlıda doğrulandı: /users, /server-status, /definitions).

### ADR-053 — business-push yetki + içerik doğrulaması (05.07.2026)
- **Karar:** `BusinessSyncService.Apply(SessionContext, payload)` overload'ı eklendi. (1) Yetki: her iş tablosu bir yetki modülüne eşlendi (TableModule); kullanıcı ilgili modülde Create VEYA Edit yetkisi yoksa o tablonun tüm satırları UYGULANMAZ (hata değil, sessiz atla + errors'a not). Admin/SüperAdmin tam yetkili. (2) İçerik: NonNegativeFields ile stok/yakıt/tutar alanları negatifse satır reddedilir (sayı ve sayısal-string toleranslı). company_id zaten UpsertRow'da oturumdan zorlandığı için ayrıca kontrol edilmez. Endpoint `Apply(s, ...)` çağırıyor. Eski `Apply(companyId, ...)` overload'ı korundu (yetkisiz, testler için).
- **Gerekçe:** Y3 — en yetkisiz kullanıcının JWT'siyle firmanın tüm tablolarını ezmesi / negatif stok yazması engellendi. 3 yeni test (yetkisiz modül atlama, admin tam yazma, negatif bakiye reddi) + mevcut 6 BusinessSync testi geçti.

### ADR-054 — JWT yenileme (kayan oturum) (05.07.2026)
- **Karar:** Sunucuya `POST /api/auth/refresh` eklendi (RequireAuthorization; geçerli token → aynı kullanıcı/firma için taze token, yetkiler DB'den). `JwtTokens.ExpiryHours=12` sabiti + `ReadExpiry`. Masaüstü `ServerAuthClient`: token exp'i saklanır (TokenExpiresUtc), `EnsureFreshTokenAsync` süreye <2 saat kalınca yeniler; 401'de `SessionExpired=true` (UI tekrar-girişe yönlendirebilir). `BusinessSyncPushService.PushAsync` push öncesi token yeniler, 401'de bir kez daha dener.
- **Gerekçe:** Y5 — 12 saatten uzun masaüstü oturumda sync sessizce duruyordu; artık kayan oturum + açık sinyal. 4 yeni JwtToken testi (claim/süre, doğrulama, farklı-anahtar reddi, yenileme kimliği korur).

### ADR-055 — Updater yedek + rollback + bütünlük guard'ı (05.07.2026)
- **Karar:** `UpdateInstaller`: (1) kurulum öncesi paket ana exe içermiyorsa kurulum hiç başlatılmaz (bütünlük guard). (2) PowerShell yardımcısı önce mevcut kurulumu `backup` dizinine yedekler; yedek alınamazsa güncelleme başlatılmaz. (3) staging→install kopyalaması başarısızsa (robocopy>=8) yedekten geri alınır ve sürüm YAZILMAZ (bozuk/yarım güncelleme kalıcı olmaz). (4) yalnız başarıda current.txt yazılır. Checksum kontrolü korunur.
- **Gerekçe:** Y4 — eski yardımcı başarısız kopyada bile sürümü yazıp exe'yi başlatıyor, yedek almıyordu. NOT: gerçek PS yolu Windows entegrasyon testi gerektirir; senkron ApplyUpdate rollback'i (UpdateService) mevcut testlerde kapsanıyor.

> **NUMARA NOTU (ADR-076…082):** Aşağıdaki 7 ADR'nin **commit mesajları ADR-075…081** etiketlendi; ancak
> ADR-075 numarası zaten yukarıdaki "logo arka plan" kararına aitti → DECISIONS.md'de doğru sıra **076-082**
> (commit'ler birer eksik: commit-075 = ADR-076, …, commit-081 = ADR-082). Git history yeniden yazılmadı.

### ADR-076 — Silinen makine firması/şubesi girişe makine bilgisi olarak SUNULMAZ (12.07.2026) [commit: ADR-075]
- **Bağlam:** Süper admin, makinenin atanmış firmasını silince tekrar login'de "Makine firması ile giriş
  (silinmiş firma)" çıkıyor ve ona giriş yapılabiliyordu. Kök neden: `EnrollmentService.ReadDeviceInfo`
  join'leri `is_deleted` filtrelemiyordu → silinmiş firma/şube adı-id'si makine bilgisi olarak dönüyordu.
- **Karar:** (server) `ReadDeviceInfo` join'lerine `AND is_deleted=0`; silinmişse NULL döner. (masaüstü)
  `SetupSuperAdminStep2Async`: makine firması geçerli firma listesinde yoksa makine firması/şubesi sayılmaz
  (liste hiç yüklenemediyse dokunulmaz). 2 regresyon testi (SyncTests).
- **Kural:** Makineye hangi firma+şube atandıysa makine firması **odur**; silinmiş/atanmamış firma seçenek değildir.

### ADR-077 — Makine yönetiminde FİRMA değiştirme (web, süper admin) (12.07.2026) [commit: ADR-076]
- **Karar:** `EnrollmentService.AssignCompany(s, deviceId, companyId)` — yalnız süper admin (çapraz-firma);
  hedef firma var+silinmemiş olmalı; **şube ataması otomatik kalkar** (şube eski firmaya aitti). API:
  `POST /api/machines/{id}/company`. Web `Machines.razor`: süper admine "Firma (değiştir)" seçim sütunu + onaylı taşıma.
- **Kapsam:** Masaüstü makine ekranı zaten şube/firma değiştirme içermiyor (yalnız kota/aktif/sil) → dokunulmadı;
  kullanıcının "sadece şube değiştirebiliyorum" dediği ekran web'di. 1 regresyon testi.

### ADR-078 — Canlı sunucu ekranı: disk kapasitesi (canlı) + güncelleme paketi manuel silme (12.07.2026) [commit: ADR-077]
- **Karar:** `ReleaseStore`: `GetDiskInfo` (DriveInfo ile `/data` doluluk), `ListPackages`, `Delete`.
  `/api/server/status`'a disk alanları (diskPercent/Free/Used/packages) — 3 sn'de bir canlı. Yeni uçlar:
  `GET /api/releases/packages` + `DELETE /api/releases/packages/{version}` (süper admin; **en güncel sürüm silinemez**).
  Web `ServerStatus.razor`: canlı disk göstergesi (gauge + spark + %85 kritik uyarı) + KPI + paket tablosu (onaylı silme).
- **Gerekçe:** ADR-070'teki disk-dolması tam kesintisine karşı süper adminin diski canlı görüp eski paketi elle temizlemesi.

### ADR-079 — Web logosu masaüstünün temiz şeffaf logosuna eşitlendi (arka plan yok) (12.07.2026) [commit: ADR-078]
- **Bağlam:** Web `logo.png`'de flood-fill şeffaflık "Depo" harflerinin içine sızmıştı (dama deseni görünüyordu)
  + fazladan slogan vardı. Masaüstü login'de "tam olmuş" logo `Assets/app-icon.png` (şeffaf, arka plansız).
- **Karar:** `app-icon.png` → `wwwroot/logo.png` olarak kopyalandı (birebir). Login + üst bar CSS zaten şeffaf.
  Kullanıcının verdiği kaynak `masaüstü uygulama simge logosu.png` (2.2 MB, opak turuncu zeminli işlenmemiş orijinal)
  yerine, zaten şeffaf/işlenmiş masaüstü asset'i tercih edildi ("arka plan olmasın" garantisi).

### ADR-080 — İlk açılış tema varsayılanları (12.07.2026) [commit: ADR-079]
- **Karar (kayıt yoksa uygulanan varsayılan; kullanıcı değiştirince kaydı ezer):** Masaüstü **Fluent / Koyu / Kehribar**
  (`ThemeService`: accent varsayılanı blue→amber; mod Dark, stil fluent zaten hedefti). Web **Koyu / Yumuşak / Kehribar**
  (server `/api/me/theme` + ApiClient fallback + `ThemeState`: color→amber, style→soft; mode dark zaten).

### ADR-081 — Personel ekranı: hesap AÇMA yerine MEVCUT kullanıcıyı BAĞLAMA (12.07.2026) [commit: ADR-080]
- **Kullanıcı talimatı:** Personel ekranında kullanıcı **açma** alanı değil, personele **mevcut kullanıcıyı bağlama** alanı olmalı.
- **Karar:** ADR-067'deki inline "hesap aç" (kullanıcı adı/şifre/rol) alanı kaldırıldı; yerine **bağlanabilir
  (henüz bir personele bağlı olmayan, süper-admin olmayan) mevcut kullanıcı** seçimi geldi (web + masaüstü).
  `UserService.ListLinkableUsers`; `GET /api/personnel/linkable-users` + `POST /api/personnel/{id}/link-user`
  (mevcut `LinkPersonnel` kullanılır). Hesaplar artık yalnız "Kullanıcılar" ekranında açılır. 2 regresyon testi.
- **Not:** "Saha personeli" kutucuğu + bağlanmadıysa uyarı koşulu korundu (bağlama üzerinden). Eski `/account` (hesap aç) ucu kaldı ama kullanılmıyor.

### ADR-082 — Firma yetki kontrol: süper admin DİNAMİK global kilidi açıp kapatabilir (12.07.2026) [commit: ADR-081]
- **Bağlam:** "Global kilit" salt derleme-zamanı sabitiydi (`AppModules.IsAdminRestricted`) ve UI'da salt-okunurdu.
- **Karar:** İki katman: (1) **SABİT** kilit (IsAdminRestricted — değiştirilemez), (2) süper adminin yönettiği
  **DİNAMİK** global kilit (tüm firmalar). Dinamik kilit **migration'sız**, global `app_settings` satırında saklanır
  (`company_id NULL`, key=`global_grant_limits`). `CompanyGrantService.SetGlobalLocks`/`IsGlobalRestricted`;
  `GetControl` satırına `GlobalHardLocked` alanı. Enforcement `PermissionService.SaveForUser`'a `IsGlobalRestricted`
  eklendi (alt role verilemez). API: `POST /api/global-permissions` (süper admin). Web: "Global kilit" toggle
  (sabit olanlar "sabit" rozetiyle salt-okunur), Save hem firma hem global kilidi kaydeder. 1 regresyon testi.

### ADR-074 — Marka logoları web + masaüstüne eklendi (kalite korunarak) (12.07.2026)
- **Kaynak:** `Desktop\Logo Dosyalarım` — iki dosya, ikisi de **1536×1024**:
  - `Web +Uygulama içi Logo.png` — **istifli tam logo** (görsel + "DepoWise" + slogan). Arka planı **opak beyaz**di.
  - `masaüstü uygulama simge logosu.png` — **sembol** (yazısız değil, kısa marka; **şeffaf**, A=0).
- **İşleme (kalite korunur — yalnız küçültme, HighQualityBicubic, kayıpsız PNG; hiç büyütme yok):**
  1. **Tam logo şeffaflaştırıldı:** dış beyaz zemin **kenarlardan flood-fill** ile alfa=0 yapıldı. Basit "beyazı sil" yapılsaydı **kamyonun beyaz kabini ve yol çizgileri delinirdi**; flood-fill yalnız *dıştan erişilebilen* beyazı siler → iç beyazlar korundu (görsel doğrulandı). Kenar yumuşatma için eşik gradyanı (190–232) → halo yok. Sonra içerik sınırına kırpıldı: **1040×841**.
  2. **Sembol:** alfa sınırına kırpıldı (748×538) → **kare** tuvale ortalandı (%6 boşluk) → 838×838 → 16/24/32/48/64/128/256 px üretildi → **7 boyutlu `.ico`** (PNG gömülü, Vista+ standardı).
- **Yerleşim:**
  - Masaüstü: `Assets/logo.png` (tam logo), `Assets/app-icon-256.png` (sembol), `Assets/app-logo.ico` (pencere ikonu).
  - **`.exe` simgesi:** csproj'da `<ApplicationIcon>` **hiç ayarlı değildi** → exe varsayılan .NET ikonuyla çıkıyordu. Eklendi; gömülü olduğu doğrulandı. Kullanılmayan `avalonia-logo.ico` (şablon artığı) silindi.
  - Web: `wwwroot/logo.png`, `favicon.png` (256), `favicon.ico` (çok boyutlu) + `apple-touch-icon`.
- **Ölçek kararı:** Tam logo **istifli** (1040×841) → 30 px yükseklikte **okunmaz**. Bu yüzden dar alanlarda (masaüstü kenar çubuğu, web üst barı) **sembol** kullanılır; tam logo yalnız **giriş ekranlarında** (geniş, açık zemin) gösterilir.
- ⚠️ **GÜNCELLENDİ → bkz. ADR-075.** Bu ADR'de başlangıçta logoların arkasına "beyaz yuvarlak kutu" konmuştu (koyu temada lacivert logo kaybolmasın diye). **Kullanıcı bunu reddetti; arka plan KALDIRILDI.** Aşağıdaki ADR-075 bağlayıcıdır.

### ADR-075 — Logoların arkasında ARKA PLAN OLMAYACAK (yalnız logo) (12.07.2026)
- **Kullanıcı talimatı (bağlayıcı):** *"logo ve uygulama içine beyaz arka plan ekleyerek logoları uygulamışsın. arka plan olmamalı sadece logo olmalı."*
- **Karar:** Logo/sembol **hiçbir yerde** arka plan kutusuna sarılmaz. Şeffaf PNG **doğrudan** kullanılır. Kaldırıldığı 5 yer: masaüstü **LoginWindow**, masaüstü **MainWindow** (daraltılmış + açık kenar çubuğu), web **MainLayout** üst barı, web **Login** kartı.
- **Neden not düşülüyor:** ADR-074'te (kendi kararımla) beyaz kutu eklenmişti; belgede öyle kalırsa **sonraki oturumlar bunu geri koyar**. Bu ADR onu geçersiz kılar.
- **Bilinen ödünleşim (kullanıcı bilerek kabul etti):** Logo **lacivert ağırlıklı** olduğundan **koyu temada** kontrastı düşebilir (sarı/beyaz kısımlar görünür kalır). Kullanıcı şikâyet ederse çözüm **arka plan eklemek DEĞİL**, koyu tema için **açık renkli logo varyantı** üretmektir.
- **Masaüstü giriş ekranı** ayrıca tam logo yerine **sembol logosunu** kullanır (kullanıcı isteği); yüksek çözünürlük için `Assets/app-icon.png` (838×838) eklendi, kullanılmayan masaüstü `logo.png` kaldırıldı.

### ADR-073 — Kota İzleme "ONLINE": zaten kullanıcı-bazlı tekildi; testle sabitlendi + bellek sızıntısı düzeltildi (12.07.2026)
- **Talep (kullanıcı):** "Kota izleme ekranındaki online kolonunda aynı kullanıcı hem web'ten hem masaüstünden login olmuşsa **1 online** görünmeli; anlık login durumunu değil **kullanıcı** online durumunu almalı."
- **İnceleme sonucu (önemli):** Bu davranış **zaten doğruydu**. `ServerPresence` sözlüğü **ilk yazıldığı günden beri `userId` ile anahtarlı** (`_seen[userId] = …`, commit `03b4709`, #4 özelliği). Aynı kullanıcının ikinci platformu **yeni kayıt açmaz, mevcut kaydı tazeler** → çift sayım mimari olarak imkânsız. JWT `sub` claim'i her iki platformda da aynı `userId`'dir (tek token üretici). Yani düzeltilecek bir sayım hatası **yoktu**.
- **Yapılanlar (gerçek katkı):**
  1. **Kanıt/regresyon:** `ServerPresenceTests` (4 test) — aynı kullanıcı iki platformdan → **1**; farklı kullanıcılar → ayrı; 5 dk penceresi dışındaki düşer; **aynı kullanıcı iki farklı firmada bile tek kişi** sayılır (süper admin firma seçimi senaryosu). Şart artık koda çivilendi.
  2. **Gerçek kusur düzeltildi:** Pencere dışında kalan kayıtlar sözlükten **hiç silinmiyordu** (süresiz büyüme = bellek sızıntısı). `Prune()` eklendi; okuma sırasında eski kayıtlar düşürülür.
  3. `ServerPresence` test edilebilir hâle getirildi (`nowMs` enjekte edilebilir saat, `ResetForTests`).
- **Kullanıcıya not:** Ekranda 2 görülmüşse muhtemelen (a) **farklı iki kullanıcı** online'dı, ya da (b) **"AKTİF"** sütunu (firmadaki aktif kullanıcı sayısı) ile **"ONLINE"** karıştırıldı. Tekrar görülürse hangi kullanıcılarla olduğu bilgisiyle bildirilmeli.

### ADR-072 — Firma işlemleri OFFLINE-FIRST: yerele yaz + kuyruk, internet gelince SIRAYLA eşitle (12.07.2026)
- **Bağlam:** ADR-071 firma işlemlerini **çevrimiçi zorunlu** yapmıştı. Kullanıcı bunu reddetti: *"İnternete bağlanana kadar işlemleri yerel DB'ye yazsın, bağlanınca sırasıyla eşitlemeye başlasın. Ama eşitleme sırasında kayıtlar hataya düşmemeli. Önce sabit tanımlar ve hataya düşürebilecek tanımlar eşitlenmeli, sonra diğer kayıtlar."*
- **Karar (offline-first + kuyruk):**
  - Firma ekle/güncelle/sil/aktifleştir **ÖNCE YEREL DB'ye** yazılır (çevrimdışı tam çalışır), sonra **`sync_outbox`** tablosuna kuyruklanır (mevcut `OutboxWriter` — tanımlıydı ama hiç kullanılmıyordu; artık bağlandı).
  - İnternet gelince kuyruk **FIFO** (oluşturulma sırası) işlenir → aynı firmanın `create → update → delete` sırası korunur. Bir işlem kalıcı hata (4xx) verirse **sonrakiler işlenmez** (sıra bozulmasın); 5xx/ağ hatası **geçici** sayılır, kuyrukta kalır ve tekrar denenir.
- **"Hataya düşmemeli" şartı → İDEMPOTENCY (kritik):** Yeniden denemede aynı işlem birden çok kez gelebilir. Sunucu tarafı idempotent yapıldı:
  - `CompanyService.Create(s, dto, explicitId)`: masaüstünün **çevrimdışı ürettiği id** ile oluşturur (yerel ↔ sunucu id'leri eşleşir) ve `ON CONFLICT(id) DO UPDATE` ile **tekrar gelirse hata vermez**. API `NewCompanyDto.Id` alanı eklendi (web'den gelen istekte null → sunucu id üretir).
  - `Delete` / `Update` / `Reactivate`: "0 satır etkilendi" artık hata değil — kayıt **zaten o durumdaysa** sessizce başarılı. Yalnız firma **hiç yoksa** hata (fail-closed korunur).
- **SENKRON SIRASI (kullanıcının şartı — önce hataya düşürebilecek tanımlar):**
  1. **Firma kuyruğu** (en üst ebeveyn; olmadan diğer kayıtlar FK/tenant hatası verir)
  2. **Tanımlar/lookup** (`LookupSyncService`)
  3. **İş verisi** push→pull (`BusinessSyncService.Tables` **zaten FK-güvenli sırada**: units/suppliers/brands/kategoriler… → personel/malzeme/araç/stok…)
  Bunlar eskiden **paralel** başlatılıyordu (iş kaydı, ebeveyn tanımı gelmeden gidip hata verebilirdi) → artık **sırayla `await`** edilir.
- **Veri kaybı koruması:** `MirrorLocalAsync` kuyrukta **bekleyen işlem varken çalışmaz** — yoksa henüz gönderilmemiş yerel firma "sunucuda yok" sanılıp silinirdi.
- **UI:** Kuyrukta iş varsa kullanıcıya bildirilir: *"N işlem çevrimdışı kuyrukta — internet gelince eşitlenecek."*
- **Test:** `Firma_Kuyruk_TekrarGonderiminde_HataVermez_IDEMPOTENT` (aynı create/delete/reactivate iki kez → hata yok, mükerrer kayıt yok; olmayan firmada fail-closed). Suit **263/263**.

### ADR-071 — Masaüstü firma ekle/sil web ile eşitlenmiyordu → FİRMALAR SUNUCU-OTORİTELİ (12.07.2026)
- **Belirti (kullanıcı):** "Masaüstü firma tanım ekranından eklediğim/sildiğim firma verileri web ile zaman geçse de hâlâ eşitlenmemiş."
- **Kök neden:** Masaüstü `CompaniesViewModel` **yalnız YEREL DB'ye** yazıyordu (`DesktopServices.Companies` = yerel `CompanyService`). Firmalar iş senkronu tablo listesinde de **yok** (`BusinessSyncService.Tables` içinde `companies` bulunmuyor) → masaüstünde yapılan firma değişikliği sunucuya **hiçbir yoldan** ulaşmıyordu. Aynı şekilde web'de eklenen/silinen firma da masaüstüne inmiyordu.
- **Karar (kullanıcının "web tam otoriter" kuralı):** Firmalar **sunucu-otoriteli** yapıldı — şubelerdeki (ADR-066) modelin aynısı:
  - Yeni `CompanySyncService` (masaüstü): **ekle / güncelle / sil / aktifleştir** doğrudan **sunucu API'sine** gider (`/api/companies…`, JWT ile). **Çevrimiçi zorunlu** — çevrimdışıysa net mesaj (`OfflineException`), sessizce yerele yazıp sapma üretmez.
  - `MirrorLocalAsync()`: sunucudaki firma listesi yerele **aynalanır**; sunucuda **artık olmayan** yerel firmalar **pasife alınır**. Girişte (`FinalizeLoginAsync`), ekran açılışında ve "Yenile"de çalışır.
- **Sonuç:** Masaüstü ↔ web firma verisi birebir aynı. Yerel `CompanyService.Create/Delete` artık masaüstü UI'dan çağrılmıyor (sunucu tarafında API'nin kullandığı servis olarak kalır).
- **Test:** Build 0 hata, suit 262/262. (Ağ bağımlı akış olduğu için birim test yerine sunucu API'si + aynalama mantığı üzerinden doğrulanır; şube aynalama testi aynı deseni kapsar.)

### ADR-070 — TAM KESİNTİ: sunucu diski doldu (güncelleme paketleri) → saklama politikası (12.07.2026)
- **Olay:** 1.0.41 yayınlanırken önce yükleme, sonra **login bile 500** vermeye başladı. Log: `SQLite Error 13: 'database or disk is full'`. Fly.io kalıcı diski (`/data`, **974 MB**) **%100 dolmuştu** → SQLite hiçbir şey yazamıyor → **tüm API çöküyor** (login dahil). Kod hatası DEĞİL, operasyonel kapasite hatası.
- **Kök neden:** Her masaüstü paketi **~85 MB** ve `/data/releases` altında **hiç temizlenmiyordu**. 11 paket birikmişti (1.0.31…1.0.41) = **892 MB**. Sunucu DB'si yalnızca 1 MB. Güncelleyici **daima en son sürümü** indirdiği için eski paketler tamamen ölü ağırlıktı.
- **Acil müdahale:** Eski paketler silindi (en güncel 1.0.40 korunarak) + yarım kalmış bozuk 1.0.41 paketi silindi → disk **%100 → %17** (756 MB boş). Canlı düzeldi, 1.0.41 yeniden yayınlandı (checksum `2825aa71…`).
- **Kalıcı çözüm:** `ReleaseStore.SaveAsync` artık her yayından sonra `PruneOld()` çağırır: **en yeni `KeepCount=3` paket dışındakiler otomatik silinir** (geri dönüş ihtimaline karşı 3 tutulur). Temizlik hatası yayını bozmaz (sessiz geçilir).
- **Ders / gelecek:** ~1 GB disk + 85 MB paket = **~11 sürümlük tavan**. Paket boyutu self-contained olduğu için büyük. İleride paket boyutu artarsa veya sürüm hızı artarsa `KeepCount` düşürülmeli ya da disk büyütülmeli (`fly volumes extend`). Disk dolması **sessiz değil, ölümcül** bir arızadır: SQLite yazamaz → her uç 500.

### ADR-069 — SİLMEDE WEB (SUNUCU) TAM OTORİTER: silinen kayıt makinelerin yerel DB'sinden de düşer (12.07.2026)
- **Talep (kullanıcı):** "Web'te bir kayıt silindiyse ilgili şubenin makinesindeki yerel DB'de de silinsin. **Web tam otoriter olacak.**"
- **Mevcut durum / bulunan iki açık:**
  1. **Diriliş (pull):** Geri-çekmede `UpsertRow` **LWW** uyguluyordu (`excluded.updated_at >= tablo.updated_at`). Makinede kayıt web'deki silmeden SONRA düzenlenmişse (yerel `updated_at` daha büyük), gelen `is_deleted=1` **atlanıyor** ve kayıt yerelde canlı kalıyordu.
  2. **Diriliş (push):** Masaüstü girişte **önce PUSH sonra PULL** yapıyor. Makine, web'de silinmiş kaydı `is_deleted=0` + daha yeni `updated_at` ile push edince **sunucuda kayıt diriliyor**, ardından pull ile TÜM makinelere geri yayılıyordu. (Bu, tek başına (1)'i düzeltmeyi de boşa çıkarırdı.)
- **Karar (iki yönlü, simetrik):**
  - **PULL (`ApplyPull`, `serverAuthoritativeDeletes`):** Sunucudan gelen satır `is_deleted=1` ise **LWW koşulu uygulanmaz** → silme **her zaman kazanır**, yereldeki daha yeni düzenleme silmeyi engelleyemez.
  - **PUSH (`Apply`, `protectServerDeletes`):** Sunucuda `is_deleted=1` olan kayıt, cihazın `is_deleted=0` satırıyla **geri getirilemez** (`NOT (tablo.is_deleted=1 AND excluded.is_deleted=0)`). Kaydı geri getirmenin tek yolu **web'den** yeniden aktifleştirmektir.
  - **Silme dışındaki alanlarda LWW aynen korunur** (yerelde yapılmış yeni düzenleme, sunucunun eski sürümüyle ezilmez) — karşı-kontrol testiyle sabitlendi.
- **Ek:** `personnel_titles` (unvan sabit tanımları) senkron tablo listesine + `TableModule` (yetki eşlemesi, `personnel`) eklendi — yeni tablo hiç senkronlanmıyordu.
- **Kapsam notu:** `branches`/`companies` iş senkronunda değildir (web-otoriteli); şube silme yansıması ADR-066'da ayrıca çözüldü.
- **Test (3 yeni):** `Webte_Silinen_Kayit_Yerelde_De_Silinir_SUNUCU_OTORITER` · `Sunucuda_Silinen_Kayit_Cihaz_Pushuyla_Diriltilemez` · `GeriCekmede_SilinmemisKayitta_LWW_Korunur`. Suit **262/262**.

### ADR-068 — Firma silince 401 + liste yüklenmiyor: süper admin oturumu öksüz kalıyordu (12.07.2026)
- **Belirti (kullanıcı):** "Firma listesinde silinmiş firma listelenmeye devam ediyordu, tekrar sildim → **401 Unauthorized**; ayrıca firmalar hiç yüklenmiyor."
- **Kök neden:** Süper admin bir firmayı **seçip o firmanın bağlamında** çalışabiliyor (ADR-058, JWT company claim = seçilen firma). O firmayı **silince** token'daki firma geçersiz hâle geliyor. `AuthService.CreateSessionForUser` çapraz-firma dalında `CompanyExists` false görüp **null** dönüyordu → `Session(ctx)` null → **her istek 401**. Sonuç zinciri: silme başarılı olur (o an oturum geçerli) → liste yenileme isteği 401 → **UI'da eski/silinmiş firma görünmeye devam eder** → tekrar silmeye basınca 401 → sonrasında hiçbir şey yüklenmez. (Liste sorgusu zaten `is_deleted=0` filtreliydi; hata orada değildi.)
- **Karar:** Çapraz-firma dalında **"silinmiş firma"** ile **"hiç var olmamış firma"** ayrıldı:
  - Firma **kaydı hiç yoksa** (uydurma/sahte id) → `null` (fail-closed **korunur**; `SuperAdmin_OlmayanFirmada_Oturum_Acamaz` testi hâlâ geçer).
  - Firma **var ama silinmişse** → süper admin **kendi (home) firmasına düşürülür**, oturum yaşar. Süper admin platform sahibidir; hiçbir işlem onu kilitleyemez (ADR-064 ile aynı ilke).
- **Test:** `SuperAdmin_CalistigiFirmayiSilince_Oturum_Dusmez_401_Vermez` (seç → sil → oturum yaşar, home'a düşer, liste yüklenir ve silinen firma listede yoktur). Suit **259/259**.

### ADR-067 — #6 NİHAİ: **Fikir A** (tek ekran), B'nin koşulları korunarak (12.07.2026)
- **Bağlam:** ADR-065 ile Fikir B uygulandı (Personel/Kullanıcılar ayrı; hesap açma Kullanıcılar'a taşındı). Kullanıcı canlıda gördükten sonra **ayrı ekran yapısını beğenmedi** ve **Fikir A'ya dönülmesini** istedi: *"A'yı yapalım... ama koşullar aynı kalsın."*
- **Karar (A + B'de eklenen koşulların TAMAMI korunur):**
  - **Personel ekranında hesap açma GERİ GELDİ:** "Uygulama erişimi ver" anahtarı → kullanıcı adı / şifre / rol; hesap aynı formda açılır ve personele bağlanır (`POST /api/personnel/{id}/account`). Admin **"Hesabı kaldır"** ile bağı çözebilir.
  - **Korunanlar:** `☐ Saha personeli` kutucuğu · hesap yoksa/açılmıyorsa **ve** kutucuk işaretli değilse **uyarı penceresi** (kutucuk işaretliyse koşul hiç çalışmaz) · mükerrer kişi uyarısı · **unvan sabit tanım + "+"** · bir personele **tek** hesap.
  - **Çelişki önleme:** "Saha personeli" işaretlenirse hesap açma anahtarı otomatik kapanır ve gizlenir (kişi uygulamaya girmeyecek).
  - **Kullanıcılar ekranındaki "Personel seç (bağla)" KALDI** — kaldırmak gerekmedi; ikinci (isteğe bağlı) yol olarak duruyor, A'yı bozmuyor. PERSONEL sütunu da kalır.
- **Veri katmanı değişmedi** (Migration033/034 aynen geçerli): `users.personnel_id`, `personnel.is_field_staff`, `personnel_titles`. Yalnız UI/akış değişti → geri alınabilir.
- **Test:** 258/258. **Kapsam:** web + masaüstü.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). ADR-065'in yerini alır (B artık geçerli değil); ADR-063/064'teki A ise koşulsuz sürümdü — bu ADR "A + koşullar" nihai hâlidir.

### ADR-066 — Silinen şubeler masaüstünde listelenmeye devam ediyordu (12.07.2026)
- **Belirti:** Web'de silinen şube, masaüstünde **tüm şube alanlarında** (personel, kullanıcı, stok, araç…) görünmeye devam ediyordu.
- **Kök neden:** Sunucu/web tarafındaki TÜM şube okuma sorguları zaten `is_deleted=0` filtreliydi (hata orada değildi). Şubeler **sunucu-otoriteli** (`BusinessSyncService.Tables` içinde YOK — iş senkronuna dahil değil). Masaüstünün yerel şube kopyası ise sunucudan **yalnız UPSERT** ediliyordu (`LoginViewModel`), üstelik bu yalnız **süper admin firma seçimi** yolunda çağrılıyordu. Sunucuda silinen şube yerelde `is_deleted=0` olarak kalıyor, hiçbir zaman düşmüyordu.
- **Karar:** Şube aynalama `MirrorServerBranchesLocalAsync` metoduna çıkarıldı ve **her girişte** (`FinalizeLoginAsync`, tüm kullanıcılar) çağrılıyor. Sunucudan gelenler upsert edilir; **sunucunun listesinde ARTIK OLMAYAN yerel şubeler pasife alınır** (`is_deleted=1`). Çevrimdışıysa hiçbir şey yapılmaz (yereldekiyle devam — offline-first korunur).
- **Test:** `OrgPersonnelTests.Sube_Silinince_HicbirListede_Gorunmez` (liste + `ScopeResolver.AllowedBranchIds`). Suit 258/258.
- **Gerekçe:** Şube tek otoriteye (sunucu) bağlı olduğundan yerel kopya birebir ayna olmalı; yalnız-upsert modeli silmeyi hiç yansıtmıyordu.

### ADR-065 — #6 revizyon: Fikir A → **Fikir B** + saha personeli kutucuğu + unvan sabit tanım (12.07.2026)
- **Bağlam:** #6 (Personel+Kullanıcı birleştirme) ADR-063/064'te **Fikir A** ("tek Çalışan kaydı, aynı ekranda hesap açma") olarak uygulanmıştı. Kullanıcı 12.07'de **Fikir B'yi seçtiğini** belirtti (belgede A yazılıydı — çelişki kullanıcının son açık talebi lehine çözüldü, CLAUDE.md §1).
- **Karar (Fikir B + kullanıcının eklemeleri):**
  - **Personel** ve **Kullanıcılar** ekranları **ayrı** kalır. Personel ekranındaki hesap açma (kullanıcı adı/şifre/rol) ve "hesap bağını kaldır" **kaldırıldı**.
  - **Kullanıcılar** formuna **"Personel seç (bağla)"** eklendi → `users.personnel_id` (Migration033 zaten vardı). Yalnız **hesabı olmayan** personeller listelenir; bir personele **tek** hesap (mevcut kısmi tekil index korur). Kullanıcı listesine **PERSONEL** sütunu.
  - **`personnel.is_field_staff`** ("Saha personeli" kutucuğu, Migration034). Kaydederken **hesap bağlı değil VE kutucuk işaretli değilse** uyarı penceresi çıkar; **kutucuk işaretliyse koşul hiç çalışmaz** (kullanıcının açık talebi). Onaylanırsa kutucuk işaretlenir → tekrar sorulmaz.
  - **`personnel_titles`** tablosu (Migration034): **unvan sabit tanım** listesi (firma bazlı) + **"+"** ile yeni tanım. `personnel.title` serbest metin olarak kalır (geçmiş bozulmaz); migration mevcut unvanları tanım listesine taşır. Mükerrer kontrolü **tr-TR CompareInfo** ile yapılır — SQLite `LOWER()` Türkçe harfleri (Ş/İ/Ğ) küçültmediği için SQL'de değil C#'ta.
- **Kapsam:** Ortak ekran → **web + masaüstü** ikisinde de uygulandı. Diğer ekranlar bozulmadı.
- **Gerekçe/sonuç:** Kullanıcının son açık talebi. Küçük, geri alınabilir değişiklikler; veri kaybı yok (hesap-personel bağı ve unvan metinleri korunur). Test **257/257** (+4 yeni: saha kutucuğu, unvan mükerrer/tenant, kullanıcı-personel bağı).
- **Not:** Fikir A taslağı (`docs/mockups/calisan-yonetimi-A.html`) tarihsel kayıt olarak kalır.

### ADR-064 — Çalışan Yönetimi masaüstü (Faz4) + KRİTİK: süper admin kilitlenme düzeltmesi (12.07.2026)
- **Çalışan Yönetimi (Faz4, masaüstü):** Masaüstü Personel ekranı web (Faz3) ile eşitlendi — erişim rozeti (Saha/Kullanıcı/Admin), mükerrer kişi uyarısı, aynı formda "Uygulama erişimi ver" (kullanıcı adı/şifre/rol), saha-personeli onayı, hesap bağını kaldır (admin). Tek servis (`CompanyService`/`UserService`) iki platformca paylaşıldığı için iş kuralı tek yerde.
- **KRİTİK hata (kök neden):** `CompanyService.Delete` firma silinince o firmadaki TÜM aktif kullanıcıları `is_active=0` yapıyordu. Süper admin **kendi home firmasını** silince kendini pasife alıp sistemden tamamen kilitliyordu → login "Kullanıcı adı veya parola hatalı" (login `is_active=1` arar). Sunucu restart'ı kurtarmıyordu (seed yalnız süper admin YOKSA çalışır).
- **Karar / önlemler:** (1) `CompanyService.Delete` deaktivasyonu süper admin kullanıcılarını **hariç tutar** (`AND id NOT IN (…role-super-admin…)`). (2) `ServerServices.EnsureSeedAdmins` her açılışta pasif süper adminleri `is_active=1` yapan **self-heal** içerir → canlı kilit bir API redeploy ile açılır. (3) Regresyon testi `OrgPersonnelTests.Firma_Silme_SuperAdmini_PasifeAlmaz` (silme sonrası login başarılı). Hafıza notu: `superadmin-lockout-company-delete`.
- **Gerekçe:** Süper admin platform sahibidir; hiçbir operasyon onu kilide düşürememeli. Küçük, geri alınabilir SQL değişikliği; normal kullanıcı davranışı korunur. Test 253/253.
- **Açık takip:** Canlı süper admin kilidi ancak **API (`depowise-erp`) yeniden yayınlanınca** açılır (self-heal). Deploy kullanıcı onayı/flyctl gerektirir.

### ADR-063 — Güncelleme penceresi (Ertele/Yeniden Başlat, tek pencere) + Firma Yetki Kontrol yeni tasarım + Çalışan Yönetimi taslağı (11.07.2026)
- **(C) Yeniden başlatma onayı:** Eskiden indirme sonrası pencere iki "Tamam" butonuyla çıkıp ne olursa olsun yeniden başlatıyordu. Artık ayrı onay: **"Şimdi Yeniden Başlat" / "10 Dakika Ertele"**; her erteleme 10 dk ve pencerede yazılı. İndirilen paket (`_pendingBytes`) saklanır → erteleyince tekrar inmez.
- **(D) Biriken bildirimler:** Aynı anda **tek** güncelleme penceresi (`_availableWindow` guard + `_updateBusy` kritik bölüm). Pencere açıkken yeni paket çıkarsa **yeni pencere açılmaz**, açık pencerenin mesajı `ConfirmWindow.SetMessage` ile güncellenir. Snooze 10 dk (`_updateSnoozeUntilUtc`); kontrol aralığı 10 dk → **1 dk**.
- **#5 Firma Yetki Kontrol yeni tasarım (web):** Kullanıcı taslağı beğendi (`docs/mockups/firma-yetki-v2.html`) → `CompanyPermissions.razor` yeniden yazıldı: özet kutular, ekran arama, istemci-tarafı gruplama, 3 durumlu kontrol (Serbest/Yalnız Admin/🔒 kilit), grup-başı "tümünü serbest", değişiklik sayacı + yapışkan kaydet. **API sözleşmesi korundu** (`restrictedKeys`). Web-only ekran.
- **#6 Çalışan Yönetimi (Personel+Kullanıcı birleşik) — TASLAK, uygulanmadı:** Fikir A seçildi; `docs/mockups/calisan-yonetimi-A.html`. Kullanıcı kuralları: (1) mükerrer kişi (farklı şubede ad+telefon) uyarısı + birleştir/farklı-kişi; (2) bir personele **tek** kullanıcı; (3) yanlış bağ düzeltmesi yalnız Admin+; (4) kullanıcı seçilmezse "saha personeli mi?" onayı. Onay sonrası web+masaüstü uygulanacak. Detay: `docs/ONERILER_YETKI_PERSONEL.md`.
- **Gerekçe:** Kullanıcının açık talepleri. C/D/#5 uygulandı; #6 onay bekliyor.

### ADR-062 — Firma yeniden-aktifleştirme + sunucu izleme (CPU/RAM/online) + otomatik yedek + yetki/personel önerileri (11.07.2026)
- **#1 Firma yeniden-aktifleştirme (sözleşme yenileme):** `CompanyService.ListDeleted` + `Reactivate` — pasife alınan firma geri gelir, silme sırasında pasife alınan kullanıcılar (`is_active=0`) tekrar aktifleşir. `GET /api/companies/deleted`, `POST /api/companies/{id}/reactivate` (yalnız süper admin). Web `Companies.razor` "Pasif Firmalar" bölümü + masaüstü `CompaniesView` Expander (ortak `CompanyService` → iki platform).
- **#3 Canlı sunucu CPU/RAM:** `/api/server/status` yeni alanlar: `cpuPercent` (poll'lar arası `TotalProcessorTime` delta / duvar-saati / çekirdek), `memPercent` + `memLimitMb` (GC `TotalAvailableMemoryBytes` = cgroup limiti; yoksa 256MB), `usersOnline`. Web `ServerStatus.razor` animasyonlu gauge + sparkline (eşik renkleri %60/%85). Web-only ekran.
- **#4 Online kullanıcı:** `ServerPresence` (bellek-içi, son 5 dk; tek sunucu → kalıcı depo yok, ücretsiz); auth sonrası middleware `Touch(userId, companyId)`. `/api/quota-monitor` firma başına `onlineCount/onlineText`. Web `QuotaMonitor.razor` ONLINE sütunu. Web-only ekran.
- **#2 Otomatik yedek:** Gerçek durum: sunucuda otomatik yedek YOK; masaüstü elle yüklüyordu (koddaki "günlük otomatik" yorumu asılsızdı). Çözüm: `ShellViewModel.MaybeDailyBackupAsync` — bugün yerel yedek yoksa günde 1 kez `BackupService.Backup()` (VACUUM INTO + 30 gün rotasyon) + sunucu adresi tanımlıysa yükler. Web+masaüstü yedek ekranlarına "bu ekran nasıl dolar" bilgi paneli.
- **#5/#6 (ÖNERİ, uygulanmadı):** Firma Yetki Kontrol yeniden tasarım görsel taslağı (`docs/mockups/firma-yetki-v2.html`) + Personel/Yetki birleştirme fikirleri → `docs/ONERILER_YETKI_PERSONEL.md`. Kullanıcı onayı bekliyor.
- **Kılavuz:** `docs/KULLANIM_KILAVUZU.md` oluşturuldu; her değişiklikte güncellenecek.
- **Gerekçe:** Kullanıcının açık talepleri. #5/#6 onay-öncesi (kullanıcı "önce fikir/xml sun" dedi). Test: +1 (`Firma_YenidenAktiflestirme_KullanicilariGeriAktifEder`).

### ADR-061 — Makine şubesi ilk-kurulum oto-atama (onaylı) + firma silme kullanıcı koruması (11.07.2026)
- **Makine ilk-kurulum (ADR-059'u revize eder):** Önce "makineye şube atanmamışsa personel girişi ENGELLENİR, admin web'den atamalı" idi. Kullanıcı isteğiyle değişti: makinenin şubesi henüz YOKSA, **ilk giriş yapan kullanıcı** (çevrimiçi) şube seçer → **onay penceresi** ("bu makine [firma]/[şube] için tanımlanacak, onaylıyor musunuz?") → onaylarsa `POST /api/machines/self-assign` ile makinenin şubesi kullanıcının şubesine tanımlanır. `EnrollmentService.SelfAssignBranchIfUnset` yalnız `branch_id IS NULL` iken atar (zaten atanmışsa DOKUNMAZ → admin ataması otoriter kalır). Admin web'den her zaman değiştirebilir (AssignBranch). Çevrimdışı ilk kurulum yapılamaz (sunucu gerekir → bilgilendirme). Makine şubesi zaten varsa: eski davranış (çevrimdışı oto-giriş; çevrimiçi seçim + farklı-şube uyarısı).
- **Makine yönetimi ekranı (#2):** firma→şube seçimi + "Kayıtsız Makineler" (şubesiz, süper admin için firma bağımsız); her sorgu yalnız ilgili kümeyi çeker (menü açılışında tüm makineleri çekmez). `ListDevices(companyFilter, branchFilter, unassignedOnly)`; `/api/machines?companyId&branchId&unassigned`.
- **Firma silme (#1):** Önce "bağlı kullanıcılar var, önce silin" hatası veriyordu. Artık firma silinince bağlı kullanıcılar **pasife alınır** (`is_active=0`, `is_deleted=0` → korunur, kaybolmaz); yanlışlıkla silinirse veri durur, firma geri gelince aktifleştirilebilir.
- **Gerekçe:** Kullanıcının açık talebi. Suit 250/250 (+3 test: firma-silme-pasif, makine-filtre, ilk-kurulum self-assign). Masaüstü değişiklikleri yeni paketle (1.0.36) görünür.

### ADR-060 — Masaüstü süper admin girişi: firma+şube seçimi / makine firması-şubesi (10.07.2026)
- **Bağlam:** Masaüstünde süper admin kendi firmasına (DEPOWISE) giriyordu → web'de yönettiği firmanın (ör. Oze Group) şubelerini göremiyordu. Kullanıcı isteği: "süper adminin firması olmaz, bütün firmalara erişebilir. Login 2. aşamada 'makine firması ile giriş' + 'makine şubesi ile giriş' kutucukları olsun; işaretliyse makineye tanımlı firma+şube ile gir, değilse firma+şube seç; ve hiçbir koşul süper admini durdurmasın."
- **Karar:**
  - Sunucu: makine kayıt/heartbeat yanıtı artık makinenin **firmasını** da döner (`RegisterResult.CompanyId/Name`; `ReadDeviceInfo` companies join). Masaüstü bunu önbelleğe alır (çevrimdışı için).
  - Masaüstü `LoginViewModel`: süper admin ADIM 2 = iki kutucuk (**Makine firması ile giriş**, **Makine şubesi ile giriş**; makine firması/şubesi varsa varsayılan işaretli) + firma ComboBox (işaretsizken) + şube ComboBox. Süper admin **hiçbir koşulda engellenmez** (şube seçilmese bile → Tüm Şubeler).
  - Seçilen firma süper adminin kendi firması değilse: firma + şubeleri **yerel DB'ye upsert** edilir ve `AuthService.CreateSessionForUser` ile **çapraz-firma oturumu** kurulur (bu, masaüstü `AuthService`'inin ADR-057'deki süper admin çapraz-firma yeteneğini kullanır).
  - Normal (süper olmayan) kullanıcı akışı ADR-059'daki gibi kalır (makine/kullanıcı şubesi zorunlu, çevrimdışı oto-giriş).
- **Bilinen sınır:** Seçilen başka firmanın **operasyonel verisi** (stok/araç/bakım…) yerelde yoksa o ekranlar boş olabilir — bu akış yalnız firma+şube **tanımlarını** yerele senkronlar; iş verisi senkronu ayrı bir konu. Gerçek çok-firmalı kullanımda test edilmeli.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). Suit 244/244, masaüstü açılış smoke-test OK; GUI login akışı gerçek makinede doğrulanmalı. Görünürlük: yeni masaüstü paketi (1.0.35) veya dev kısayolu (güncel DLL).

### ADR-059 — Admin-tanımlı makine şubesi + IP'den il (10.07.2026, TAMAM — sunucu+web+masaüstü)
- **Bağlam:** Kullanıcı isteği: makinenin şubesi artık "ilk giriş yapanın şubesi" (yerel) değil, **admin'in web'den atadığı** şube olsun (otoriter). Ana sayfa bu şubeyi göstersin; farklı şube personeli girip işlem yaparsa "kayıtlar makine şubesine yazılmaz" uyarısı; internet yoksa makinenin şubesine otomatik giriş; kullanıcıya VEYA makineye şube tanımlı değilse giriş engellensin. Makine atama ekranı IP'den il gösterip tanımayı kolaylaştırsın.
- **Karar (Adım 1 — sunucu + web, TAMAM):**
  - `EnrollmentService.AssignBranch(admin, deviceId, branchId)`: admin makineye şube atar (tenant kontrollü; yalnız admin; boş→kaldırır). Yeni uç `POST /api/machines/{id}/branch`.
  - `RegisterSelf` **artık login şubesini `branch_id`'ye YAZMAZ** — şube yalnız admin atar (otoriter). Yeni makine şubesiz gelir. Kayıt/heartbeat yanıtı atanan şubeyi (id+ad) döndürür → masaüstü önbelleği için.
  - `/api/machines` yanıtına `branchId` + `province` (IP'den il) eklendi. `GeoIp`: best-effort ip-api.com, bellek-önbellekli, isteği bloklamaz, özel IP/başarısızlıkta boş.
  - Web makine ekranı: her satırda **şube atama açılır-listesi** + **İl** sütunu.
  - Test: 2 yeni (atama otoriter+login ezmez, yalnız admin+geçerli şube). Suit 243/243. Yerel e2e + canlı deploy doğrulandı.
- **Karar (Adım 2 — masaüstü, TAMAM):** (1) Kullanıcı şubesi (`users.branch_id`) artık sunucudan masaüstüne senkron olur (`RemoteUserBundle.BranchId` + `ExportForSync` + `ImportRemoteUser`; sync-login yanıtı e2e doğrulandı). (2) `MachineGate` makinenin admin-atanmış şubesini de getirir/önbelleğe alır (`machine_branch.txt`) ve login şubesini artık göndermez. (3) `LoginViewModel` ADIM 1'de: makineye şube yoksa → "makineye şube tanımlanmamış" (giriş yok); kullanıcıya şube yoksa (ve Tüm Şubeler yetkisi yoksa) → giriş yok; **internet yoksa makine şubesine otomatik giriş** (seçim yok); internet varsa şube seçimi (varsayılan = kullanıcının şubesi). Farklı-şube uyarısı artık admin-atanmış makine şubesine göre ("kayıtlar makine şubesine yazılmayacak"). Eski yerel "ilk giriş şubesi" mantığı kaldırıldı. (4) `DashboardViewModel` ana sayfada MAKİNE şubesini gösterir (çalışma şubesi farklıysa parantezde); heartbeat makine şubesini güncel tutar. Süper admin makine/kullanıcı şube kısıtlarından muaf. Suit 243/243 (+1 senkron testi); masaüstü açılış smoke-test OK. **Görünürlük: yeni masaüstü paketi (1.0.35) yayınlanınca.** NOT: masaüstü GUI login akışı (çevrimdışı oto-giriş, engel mesajları) gerçek çok-makineli ortamda kullanıcı testiyle doğrulanmalı.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). Additive + geriye dönük uyumlu sunucu değişikliği; op_branch_id (kullanıcının çalışma şubesi) mantığı korunur.

### ADR-058 — Çok firmalı süper admin girişi + zorunlu şube + Tüm Şubeler (09.07.2026)
- **Bağlam:** Kullanıcı talebi: (1) web'de şube seçmeden giriş yapılabiliyordu → engellenmeli; (2) süper admin girişte FİRMA + şube seçip o firmayı yönetmeli; (3) admin kendi firmasının bir şubesini seçmeli (zorunlu); (4) "Tüm Şubeler" seçeneği admin + süper admin'de daima açık olmalı (rapor için); (5) bir firma personeli başka firmanın kaydını görmemeli.
- **Karar:**
  - (5) zaten sağlanıyor: `TenantAccessGuard` (payload firma reddi + `EnsureOwnership` fail-closed), testlerle kanıtlı. Ek iş yok.
  - (2) **Çapraz-firma süper admin oturumu:** `AuthService.CreateSessionForUser` süper admin'in kendi (home) firması olmayan var olan bir firma için de oturum kurmasına izin verir (süper admin değilse null → fail-closed). Yeni uç `POST /api/auth/select-company` (yalnız süper admin) seçilen firma için YENİ JWT (company claim = seçilen firma) + o firmanın şubelerini döner. Böylece süper admin, operasyonel/veri uçlarında (şube/malzeme/stok/araç… — `s.CompanyId` ile kapsamlanan "Pattern B") seçtiği firma olarak çalışır. Uçtan uca doğrulandı: seçilen firmada oluşturulan şube yalnız o firmada görünür.
  - Not (Pattern A): `IsSuperAdmin ? tüm firmalar : kendi` mantığı taşıyan platform ekranları (kullanıcı listesi, firma listesi, makineler) süper admin'e çapraz kalmaya devam eder — bu kasıtlı platform gözetimi, sızıntı değil.
  - (1)(3) **Şube zorunlu:** web login'de şube seçilmeden giriş engellendi (masaüstünde zaten zorunluydu).
  - (4) **Tüm Şubeler:** sunucu login yanıtı + masaüstü, `canViewAllBranches = flag || IsCompanyAdmin || IsSuperAdmin` olarak hesaplar; enforcement de bu efektif değere göre.
- **Kapsam (ADR-058 kararı):** Süper admin FİRMA seçimi **yalnız web**. Masaüstünde yapılmadı çünkü masaüstü çevrimdışı-öncelikli ve yerel SQLite **tek firmaya** ait (senkronla gelen); seçilen başka firmanın verisi yerelde olmadığından anlamlı değil. Masaüstünde yalnız "Tüm Şubeler admin/süper admin" + (zaten var olan) şube-zorunlu geçerli.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). Küçük, geri alınabilir sunucu değişikliği (mevcut normal-kullanıcı davranışı birebir korunur; yalnız süper admin için yeni yetenek). 3 yeni güvenlik testi + tam suit 241/241 yeşil + canlı-benzeri yerel e2e.

### ADR-057 — Gerçek mimari kaydı: Web=Blazor, sunucu DB=SQLite (09.07.2026)
- **Bağlam:** `CLAUDE.md`/`DECISIONS.md` (ADR-000/005) web tarafını Next.js+Drizzle+PostgreSQL olarak
  tanımlıyordu. Commit geçmişi incelendiğinde: `apps/web` (Next.js) son kez 2026-06-27'de değişmiş (0 commit
  son 2 haftada); `src/DepoWise.Web` (Blazor Server, MudBlazor) 2026-07-02'den beri 56 commit almış ve
  canlıda (`depowise-web.fly.dev`) çalışan gerçek uygulama bu. Ayrıca `src/DepoWise.Api`/`Infrastructure`
  yalnız `Microsoft.Data.Sqlite` referans ediyor (Npgsql/PostgreSQL sürücüsü hiç eklenmemiş);
  `ServerServices.cs` sunucu DB'sini `depowise-server.db` (SQLite, Fly.io kalıcı disk `/data`) olarak açıyor.
  PostgreSQL/Drizzle hiç üretime alınmadı (R4/R7'de zaten "uygulanmadı" olarak işaretliydi, ama CLAUDE.md
  hâlâ PostgreSQL'i "değişmez mimari" gibi gösteriyordu — çelişki).
- **Karar:** Dokümanlar gerçeğe uydurulur: **Web = Blazor Server (`src/DepoWise.Web`)**, **API/sunucu DB =
  SQLite** (`depowise-server.db`). `apps/web` kod tabanında kalır ama **donmuş/referans** olarak işaretlenir;
  üzerinde aktif geliştirme yapılmaz. PostgreSQL'e geçiş (R4/R7) bir **gelecek karar** olarak açık kalır —
  şu an iptal edilmiyor, sadece "yapılıyor" değil "yapılmadı ve planlanmıyor (henüz)" olarak netleştirilir.
- **Kapsam dışı / karar verilmedi:** PostgreSQL'e geçilip geçilmeyeceği, `apps/web`'in silinip silinmeyeceği.
  Bunlar kullanıcı talimatı bekliyor; bu ADR yalnız **mevcut durumu doğru kaydetmek** içindir.
- **Gerekçe:** CLAUDE.md §1 "çelişkide kararı DECISIONS.md'ye yaz" kuralı; kod/dokuman tutarlılığı, gelecekte
  yanlış yönlendirme riski (ör. Next.js'e zaman harcamak veya PostgreSQL varmış gibi davranmak).

### ADR-056 — COMODO kısıtlaması kaldırıldı, yeni PC (09.07.2026)
- **Bağlam:** Kullanıcı bilgisayarını formatladı ve geliştirmeyi COMODO'nun kurulu olmadığı farklı bir PC'ye taşıdı. COMODO'nun Auto-Containment özelliği imzasız EXE/BAT'ı sanal alanda çalıştırıp sahte/boş bir DB'ye yazdırdığı için (bkz. `docs/COMODO_RUNBOOK.md`) bu kısıtlama konulmuştu; yeni makinede COMODO yok.
- **Karar:** `.claude/hooks/comodo_guard.ps1`'i tetikleyen PreToolUse hook `.claude/settings.json`'dan kaldırıldı. `CLAUDE.md` §6, `DEVAM.md` §5 ve `BASLAMA_REHBERI.md` güncellendi: proje EXE/BAT artık doğrudan çalıştırılabilir. `dotnet build`/`dotnet run` yine de önerilen yöntem olarak kaldı (alışkanlık/tutarlılık, zorunluluk değil).
- **Kapsam dışı:** `Directory.Build.props`'taki `UseAppHost=false` ayarına dokunulmadı (ayrı bir build/paketleme kararı; gerekirse ileride ayrıca değerlendirilir). SQLite mutlak DB yolu, WAL, Cache=Private kuralları COMODO'dan bağımsız olduğu için aynen korundu.
- **Geri alma:** İleride tekrar bir COMODO'lu makinede geliştirme yapılırsa `docs/COMODO_RUNBOOK.md`'deki adımlarla hook ve kısıtlamalar geri eklenmelidir.
