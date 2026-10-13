# A3a — Arka Plan Teknik Süreçleri, API, Güvenlik, Hata Yönetimi ve Operasyon Denetimi

> Tarih: 2026-10-13 · Dal: `arge/2026-10-kapsamli` · Yöntem: yalnız okuma (grep + hedefli okuma) + `dotnet list package` (salt okuma).
> Kaynak koda dokunulmadı, test/yayın çalıştırılmadı, canlı servise bağlanılmadı.
> Satır numaraları bu daldaki dosyalara göredir (`src/...:satır`).
> Karmaşıklık: **S** = küçük (≤1 gün) · **M** = orta (2–5 gün) · **L** = büyük (1+ hafta).

(Çalışma sırasında bölüm bölüm yazılmıştır; en sonda öncelik tablosu vardır.)

---

## 0. Kısa sonuç (önce bunu okuyun)

**Kritik (güvenlik / veri kaybı):**
1. **G4-1** Güncelleme paketleri imzasız; checksum paketle aynı sunucudan geliyor ve yayın yetkisi MFA'sız tek süper admin parolasında → parola sızarsa tüm masaüstlerine sahte paket kurulabilir.
2. **G4-2** Senkron push'ta aynı `id` başka firmada varsa satır üzerine yazılıp bu firmaya taşınabiliyor (yalnız GUID'in tahmin edilemezliği koruyor).
3. **B2-1** Eşitleme imleci istemci saatine dayanıyor; saat düzeltilince yeni yerel kayıtlar sessizce sunucuya gitmeyebilir.
4. **B1-5** Masaüstü işlemlerinin denetim kaydı (audit) sunucuya hiç gitmiyor; "kim ne yaptı" yalnız o bilgisayarda.
5. **G4-3 / G4-4** `SQLitePCLRaw.lib.e_sqlite3 2.1.11` Yüksek açık (Api/Desktop/Infrastructure); .NET 8 desteği 10 Kasım 2026'da bitiyor.
6. **G4-5 / P5-6** Üretim yedeği şifresiz Drive'a, haftalık ve geliştirici bilgisayarına bağlı (RPO 1 hafta).

**Önemli:** web'de yakıt/bakım uçlarında çift kayıt koruması yok (B1-3) · defter satırları senkronla değiştirilebiliyor (B1-4) · çevrimdışı negatif stok sessiz (B1-1) · web girişlerinin hepsi tek IP'den sayılıyor (B3-2) · tam eşitleme sayfalanmıyor (B3-4) · sunucuda hiç zamanlanmış iş yok (B2-5).

**İyi olanlar:** tek kural katmanı (Infrastructure servisleri), 13 tabloda operation_id tekil indeks, iş kaydı + audit aynı transaction, kalıcı/geçici hata ayrımı + jitter'lı geri çekilme, deny-by-default (423/439 uç yetkili), JWT anahtarı zorunlu, yedek geri yükleme provası.

Ayrıntı ve öncelik: §6.

---

## 2. Arka plan işleri ve otomasyon

### 2.1 Envanter — neler arka planda çalışıyor?

| İş | Nerede | Sıklık | Kanıt |
|---|---|---|---|
| Anlık gelen eşitleme (uzun yoklama `/api/sync/wait`) | Masaüstü `ShellViewModel.AnlikGelenDongusuAsync` | sürekli; sunucu 25 sn bekletir | `src/DepoWise.Desktop/ViewModels/ShellViewModel.cs:612-633`, `src/DepoWise.Api/Program.cs:524-542` |
| Yerel bekleyen değişiklik kontrolü → push | `_yerelTimer` | 3 sn (yalnız yerel DB) | `ShellViewModel.cs:592-610` |
| Güvenlik ağı turu (ping, makine kaydı, yetki imzası, push+pull, günlük yedek, foto önbellek) | `_connTimer` | 30 sn / 60 sn | `ShellViewModel.cs:680-705` |
| Otomatik günlük yerel yedek + buluta yükleme | `MaybeDailyBackupAsync` | saatte bir kontrol, günde bir yedek | `ShellViewModel.cs:728-761` |
| Güncelleme kontrolü | `_updateTimer` (kabuk) + `DashboardViewModel._updateTimer` | 1 dk + 60 sn | `ShellViewModel.cs:937-948`, `DashboardViewModel.cs:178-180` |
| Uyarı/duyuru zili yoklaması | `_alertTimer` | 1 dk | `ShellViewModel.cs:1849-1856` |
| Sunucu: stok bakiyesi yeniden hesaplama | her `business-push` sonrası, istek içinde | her push | `Program.cs:496` → `StockService.RecomputeBalances` `src/DepoWise.Infrastructure/Materials/StockService.cs:1283` |
| Sunucu: migration | API açılışında | her açılış | `ServerServices.cs:106` (OPERATIONS.md) |
| API'de **barındırılan arka plan servisi (HostedService/BackgroundService)** | — | **YOK** | `grep AddHostedService/BackgroundService` → 0 sonuç |

**Önemli gözlem:** Sunucuda zamanlanmış hiçbir iş yok. Tüm "otomasyon" ya masaüstü açıkken çalışan zamanlayıcılara ya da istek içinde yapılan işe bağlı.
Masaüstü kapalıysa: hatırlatma yok, rapor e-postası yok, sunucu tarafı yedek doğrulaması yok.

### 2.2 Bulgular

**B2-1 · Eşitleme imleci istemci saatine dayanıyor; saat geri alınırsa yerel kayıt SESSİZCE gönderilmeyebilir (veri kaybı riski) — ÖNEMLİ**
- Push, "yerel en büyük `updated_at` > bu makinenin watermark'ı" mantığıyla çalışıyor (`src/DepoWise.Desktop/BusinessSyncPushService.cs:59-63`, `BuildSnapshot(... pushWm)`).
  `updated_at` masaüstünün kendi saatinden geliyor; sunucu bu damgayı **olduğu gibi saklıyor** ve LWW karşılaştırmasında kullanıyor
  (`src/DepoWise.Infrastructure/Sync/BusinessSyncService.cs:1553-1554` `excluded.updated_at >= {table}.updated_at`).
- Saat kayması / saat düzeltmesine karşı hiçbir koruma yok (`grep skew|saat fark|overlap` → yalnız JWT ClockSkew).
- **Senaryo:** bilgisayarın saati ileri gitmişti (ör. pil/BIOS), kullanıcı düzeltti. Watermark "ileri tarih"te kaldığı için yeni kayıtların damgası watermark'ın altında → `updated_at > watermark` koşulunu sağlamıyor → **sunucuya hiç gitmiyor**, kullanıcı fark etmiyor.
  Ters yönde: saati ileri olan makinenin kaydı web'deki daha yeni düzenlemeyi LWW ile ezer; diğer makinelerin pull imleci ileri sıçrar (sunucudaki ileri damga), normal saatle yazılan sonraki satırlar delta'da kaçabilir (araştırma raporu "Risk 1").
- **Neden önemli:** Çevrimdışı-öncelikli sistemin temel vaadi "yazdığın kayıt kaybolmaz". Saat hatası Windows'ta sık görülür.
- **Öneri:** (a) açılışta ve her turda sunucu saatiyle fark ölçümü (`/health` zaten `time` döndürüyor) — fark > 2 dk ise uyarı + eşitlemeyi durdurma; (b) sunucu `business-push`'ta kendi damgasını ayrı bir `server_updated_at`/sıra sütununa yazsın ve pull imleci ona dayansın (yalnız eklemeli migration); (c) push için "yerel değişiklik bayrağı/outbox" ile watermark'ı zamandan bağımsızlaştırma.
- **Yapılmazsa:** nadir ama sessiz veri kaybı; kullanıcı "kaydettim, web'de yok" der, kök neden bulunamaz. **Karmaşıklık:** (a) S · (b) M · (c) L.

**B2-2 · Her push'ta firmanın TÜM stok defteri yeniden okunuyor (ölçek riski)**
- `Program.cs:496` her `business-push` sonrası `svc.Stock.RecomputeBalances(s.CompanyId)` çağırıyor; bu `stock_movements` tablosundaki firmanın **tüm** satırlarını okuyor (`StockService.cs:1297-1300`), `BeginImmediate` transaction içinde, iki kez imza alıyor.
- Masaüstü değişiklik varsa 3 sn'de bir push atabiliyor; N makine × tüm defter taraması. 200–300 bin satırda her push saniyeler sürer, 512 MB makinede bellek baskısı.
- **Öneri:** yalnız push'ta gelen `material_id` kümesi için yeniden hesapla (artımlı), tam hesap gece/elle. **Yapılmazsa:** veri büyüdükçe eşitleme yavaşlar, zaman aşımı/429. **Karmaşıklık:** M.

**B2-3 · Çıkış/giriş döngüsünde uyarı zamanlayıcısı durdurulmuyor (MAS-01 kusurunun kısmi tekrarı)**
- `ShellViewModel.Release()` (`ShellViewModel.cs:~905-912`) `_connTimer` ve `_updateTimer`'ı durduruyor ama `StartAlertWatcher`'ın kurduğu `_alertTimer`'ı (`:1851-1853`) **durdurmuyor**. Eski kabuk bellekte kalır, her dakika eski oturum adına `RefreshAlertBadge` çalışır; N kez çıkış-giriş = dakikada N sorgu + çift ses.
- `_yerelTimer` kendini durduruyor (`:602`), sorun yok.
- **Öneri:** `Release()` içine `_alertTimer?.Stop()` ekle + mevcut MAS-01 testine bir satır. **Karmaşıklık:** S.

**B2-4 · Geçici/kalıcı hata ayrımı ve zehirli kayıt (poison) — İYİ, küçük eksikler**
- İyi: Sunucu `permanentSkipped` döndürüyor (`Program.cs:497-499`, `BusinessSyncService.cs:1119-1180`, `IsPermanentDbError` `:496`); istemci kalıcı atlananı kuyruğu kilitlemeden geçip **kalıcı uyarı** bırakıyor (`BusinessSyncPushService.cs:83-121`, `PoisonKey`), 5xx/429'da geri çekiliyor (`SyncFailureClassifier`, `:128-133`), son push başarısızsa 3 sn'lik zorlama yapılmıyor (`ShellViewModel.cs:603-606`).
- İyi: güvenlik ağı turunda üstel geri çekilme + **±%20 rastgele sapma (jitter)** var (`ShellViewModel.cs:397-418`, 15 sn → en çok 300 sn).
- Eksik: uzun yoklama (`/api/sync/wait`) hata verince **sabit 30 sn** bekleme, jitter yok (`ShellViewModel.cs:622`) — API yeniden başlayınca tüm masaüstleri aynı anda döner (bugünkü ölçekte küçük sorun).
- Kalıcı atlanan satırın kullanıcıya **kayıt bazında** gösterildiği, "yeniden dene / vazgeç" seçeneği sunulduğu bir ekran yok (yalnız metin uyarı). Araştırma raporu "Risk 3": `pending/applied/failed` günlüğü önerisi.
- **Öneri:** uzun yoklama beklemesine jitter (S); "Gönderilemeyen kayıtlar" listesi + "yeniden dene" (M). **Yapılmazsa:** kullanıcı reddedilen kaydını bulamaz; sunucu yeniden açılışında yük dalgası.

**B2-5 · Sunucuda zamanlanmış iş olmaması → eksik otomasyonlar**
Masaüstü açık değilken hiçbir şey çalışmıyor. Sektör için anlamlı, **gereksiz altyapı gerektirmeyen** (tek API sürecinde `BackgroundService` + PG tablosu) adaylar:

| Eksik iş | Neden önemli | Yapılmazsa | Kar. |
|---|---|---|---|
| Bakım zamanı / muayene / sigorta / periyodik kontrol bitiş hatırlatması (e-posta veya uygulama içi bildirim) | Rakiplerin hepsinde var (araştırma raporu); kaçan sigorta/muayene = cezai ve kaza riski | Kullanıcı ancak ekranı açınca görür | M |
| Geriye dönük kayıt (geçmiş tarihli hareket/sayaç) sonrası bakiye/maliyet yeniden hesaplama işi | Bakiye şu an push anında tam hesaplanıyor; ama ortalama maliyet/rapor önbellekleri için tetik yok | Eski tarihli düzeltme raporlara yansımaz | M |
| Periyodik rapor e-postası (haftalık stok/yakıt özeti) | Yönetici için değer; düşük maliyet | Yok — isteğe bağlı | M |
| Sunucu tarafı yedek doğrulama (yüklenen yedeğin açılıp `quick_check`'ten geçmesi, Supabase yedeğinin son tarihi) | "Yedek var" ≠ "yedek açılır" | Felakette bozuk yedekle karşılaşma | S–M |
| Eski `data_conflicts`, oturum/ön bellek, eski makine yedeklerinin temizliği | Disk dolunca tam kesinti yaşanmış (ADR-070, `Program.cs:280-287`) | Disk tekrar dolar | S |

- **Tekrar önleme notu:** Fly'da tek makine olduğu sürece `BackgroundService` çift çalışmaz; ileride 2. makine eklenirse PG `pg_try_advisory_lock` ile tek-çalışma kilidi gerekir (S).

**B2-6 · Gelen eşitleme (pull) imlecinde örtüşme penceresi yok; geç commit edilen satır kaçabilir**
- Pull, `updated_at > imleç` ile delta çekiyor (`BusinessSyncService.cs:619`); imleç "alınan satırların en büyük damgası" (`BusinessSyncPullService.cs:81-90`). Geriye pay (ör. imleç − 2 dk) bırakılmıyor.
- Sunucuda damga işlem **başında** atanır, satır **commit'te** görünür. Uzun süren bir web işlemi (ör. büyük içe aktarma) ile eşzamanlı bir masaüstü push'u arasında daha büyük damgalı satır önce görünürse imleç ilerler ve geç görünen satır **bir daha gelmez** (araştırma raporu Risk 1). Bunu ancak elle "tam eşitleme" (`ShellViewModel.cs:361`) düzeltir.
- **Öneri:** pull sorgusunda imleçten sabit bir pay geri gitmek (upsert zaten idempotent ve LWW korumalı → tekrar gelen satır zararsız). **Yapılmazsa:** nadiren bir makinede eksik kayıt, "web'de var masaüstünde yok" şikâyeti. **Karmaşıklık:** S (senkron protokolü olduğu için onay gerekir).


---

## 1. Veri ve iş kuralları

### 1.1 Kurallar nerede yaşıyor?
- **Tek kaynak doğru kurulmuş:** iş kuralları `src/DepoWise.Infrastructure/*Service.cs` içinde; masaüstü bu servisleri **yerel SQLite'a karşı doğrudan** çağırıyor, API aynı servisleri PostgreSQL'e karşı çağırıyor, web (Blazor) yalnız API'yi tüketiyor (`src/DepoWise.Web/Program.cs:38-41`, "web iş kuralı taşımaz").
- UI'daki tekrarlar yalnız kullanıcı kolaylığı (ör. web `FieldChecks.cs`; sunucu tarafı `FirmaAlanKontrol` `Program.cs:4333`). Sorun değil.
- **Yapısal boşluk:** masaüstünde yazılan kayıt sunucuya **satır kopyası (snapshot)** olarak gidiyor. Sunucu bu satırlara iş kuralını **yeniden çalıştırmıyor**; yalnız yapısal kapılar uyguluyor: tablo yetkisi, şube kapsamı, ebeveyn var mı, karşı uç aynı firmada mı, birkaç alanın negatif olmaması (`BusinessSyncService.cs:1205-1249`, `NonNegativeFields` `:254-261`) ve araç sayacının geri gitmemesi (`:1524-1536`). "Stok ≥ 0", "yakıt kaydındaki sayaç geri gidemez", "onay akışı durumu" gibi kurallar **yalnız yazan makinede** uygulanıyor.

**B1-1 · Çevrimdışı iki makinenin aynı malzemeyi çıkarması sunucuda negatif bakiyeyi sessizce oluşturuyor**
- İki makine çevrimdışıyken aynı son 10 adedi çıkarırsa her biri yerelde geçerli; sunucu iki hareketi de kabul ediyor, `RecomputeBalances` bakiyeyi defterden −10 hesaplıyor (`src/DepoWise.Infrastructure/Materials/StockService.cs:1283-1330`). Ne reddediliyor ne kimseye bildiriliyor (`grep negatif|alert` → yok).
- **Neden önemli:** Olgun sistemler (PowerSync, Datasync) bu durumu `insufficient_stock` gibi bir nedenle **görünür kılıyor**. "Negatif stok yasak" vaadi çok makineli kullanımda sessizce bozuluyor.
- **Öneri (yeni altyapı yok):** push sonrası hesaplamada eksiye düşen (malzeme, lokasyon) çiftlerini mevcut çakışma/uyarı akışına "negatif stok oluştu — sayım/düzeltme gerekli" olarak yazmak; reddetmek yerine **işaretlemek** (çevrimdışı gerçekliğe uygun). **Yapılmazsa:** raporlarda eksi stok, sorumlusu belirsiz. **Karmaşıklık:** M.

### 1.2 Transaction ve bölünmezlik (atomicity)
- İyi: servisler tek `conn+tx` içinde iş kaydı + audit yazıyor (`src/DepoWise.Infrastructure/Database/AuditWriter.cs:7-8`); stok bakiyesi defterden türetiliyor ve yarışta yazmıyor (`StockService.cs:1318-1326`); senkron uygulaması tablo başına savepoint (`BusinessSyncService.cs:1185-1196`).

**B1-2 · "Ana kayıt + maliyet merkezi bağlantısı" iki ayrı transaction; web'de çok adımlı kaydetme**
- 7 API ucunda önce kayıt commit ediliyor, sonra ayrı `svc.CostCenters.Link(...)` çağrısı (`Program.cs:4188, 4338, 4381` vb.; toplam 7). İkinci adım hata verirse kayıt maliyet merkezsiz kalır, kullanıcı hata görür ve tekrar kaydederse **çift kayıt** oluşur (B1-3 ile birleşince).
- Web'de aynı desen istemci tarafında: bakım kaydı → ayrı istekle malzeme talebi → ayrı istekle araç durumu (`src/DepoWise.Web/Components/Pages/Maintenance.razor:992, 1006, 1023`).
- **Öneri:** maliyet merkezini servis metoduna parametre olarak alıp aynı transaction'da yazmak (S–M); web'deki çoklu çağrıyı tek uçta birleştirmek (M). **Yapılmazsa:** nadir ama kalıcı tutarsızlık, maliyet raporunda eksik.

### 1.3 Tekrar koruması (idempotency) ve çift istek
- İyi: 13 tabloda `(company_id, operation_id)` tekil indeks (stock_movements, fuel_distributions, fuel_depot_entries, vehicle_maintenances, equipment_maintenances, daily_activities, assignment_movements, invoices, invoice_allocations, finance_transactions, party_ledger, sync_inbox, sync_outbox). Stok/zimmet/fatura/tahsilat/sayım web formları kendi `operationId`'sini üretiyor (`Invoices.razor:477`, `Assignments.razor:279`, `Finance.razor:524`).

**B1-3 · Web'de yakıt dağıtımı, yakıt depo girişi, araç/ekipman bakımı ve günlük faaliyet: operation id SUNUCUDA üretiliyor → çift kayıt koruması yok**
- `Program.cs:4187` (bakım), `:4231` (ekipman bakımı), `:4337` (yakıt dağıtım), `:4380` (yakıt depo), `:4389`, `:4398`, `:4412` (günlük faaliyet/bakım) → her istekte yeni `Guid.NewGuid()`. Web `Fuel.razor:547`, `Maintenance.razor:1006` operation id göndermiyor.
- Web `HttpClient` zaman aşımı 30 sn (`src/DepoWise.Web/Program.cs:41`). Sunucu yavaşken istek zaman aşımına düşer ama sunucuda commit olur; kullanıcı "kaydedilmedi" sanıp tekrar basar → **iki yakıt dağıtımı / iki bakım** (stok düşümü dahil).
- **Öneri:** form açılırken `operationId` üret, DTO'ya ekle; sunucu "verilmediyse üret" davranışını korusun (eski istemci uyumu). Stok uçlarında aynı desen zaten var (`Program.cs:2146`, "G1-05(a)"). **Yapılmazsa:** yakıt/bakım maliyetinde çift sayım. **Karmaşıklık:** S.
- Ek: `vehicle_meter_logs`, `vehicle_inspections`, `equipment_inspections`, `material_requests`, `stock_count_lines` tablolarında `operation_id` yok; koruma yalnız düğme kilidine dayanıyor (M, migration onayı gerekir).

### 1.4 Kayıt yaşam döngüsü, iptal ve geri dönüşüm
- İyi: gerekçeli iptal uçları (`/api/fuel/{id}/cancel` `Program.cs:4357`, `/api/maintenance/cancel` `:4198`); talep/iş emri durum geçmişi (`request_status_history`, `work_order_status_history`); onay akışı (`approval_instance/approval_step`); çöp kutusu (`/api/trash`, `TrashService`); silme senkronda işaretle taşınıyor, sunucuda silinmiş kaydın cihazdan "diriltilmesi" engelli (`BusinessSyncService.cs:1545-1559`); kalıcı silme yalnız süper admin (ADR-083).

**B1-4 · Defter (yalnız-ekle) tabloları senkronla DEĞİŞTİRİLEBİLİYOR ve bu değişiklik sunucu audit'ine yazılmıyor**
- `UpsertRow` tüm tablolara aynı `ON CONFLICT ... DO UPDATE SET <tüm kolonlar> WHERE excluded.updated_at >= ...` kuralını uyguluyor (`BusinessSyncService.cs:1538-1566`). `stock_movements`, `party_ledger`, `finance_transactions` için "yalnız ekle; güncellemede yalnız iptal alanları" ayrımı yok.
- Bozuk ya da kurcalanmış bir masaüstü eski bir hareketin `quantity`'sini daha yeni damgayla gönderirse sunucu kabul eder; iz kalmaz (B1-5).
- **Öneri:** defter tablolarında senkron güncellemesini kolon izin listesiyle (iptal/ters kayıt alanları) sınırla (S–M). **Yapılmazsa:** stok/cari geçmişi iz bırakmadan değişebilir.

### 1.5 Denetim kaydı (audit) kapsamı
- İyi: ~60 servis `AuditWriter` kullanıyor; kayıt + audit aynı transaction; `before_json/after_json` var ve 2026-09'dan beri alan bazlı anlık görüntü otomatik (`AuditWriter.cs:16-20`); senkron çakışmasında iki sürümün görüntüsü saklanıyor (`BusinessSyncService.cs:1366-1373`). Loglar arayüzden silinemiyor.

**B1-5 · Masaüstünde yapılan işlemlerin audit kaydı sunucuya HİÇ ulaşmıyor**
- `audit_logs` senkron tablo listesinde yok (`BusinessSyncService.cs:32-250`). Masaüstü "Sistem Logu" yerel DB'den okuyor (`src/DepoWise.Desktop/ViewModels/AuditLogViewModel.cs:44`); web `/api/audit` (`Program.cs:4876-4892`) yalnız sunucuda yapılan işlemleri görüyor. Sunucu, senkronla gelen satırlar için audit yazmıyor (tek `AuditWriter.Write` `BusinessSyncService.cs:1018`, çakışma kurtarma).
- **Sonuç:** kullanıcıların çoğu masaüstünde çalıştığı için **"kim, ne zaman, neyi değiştirdi" cevabı yalnız o bilgisayarda**. Bilgisayar bozulur/sıfırlanırsa geçmiş kaybolur; yönetici web'den masaüstü işlemlerinin geçmişini göremez.
- **Öneri:** `audit_logs`'u **yalnız-ekle** olarak push listesine almak (sunucu `company_id`'yi oturumdan zorlar, `user_id`'yi doğrular; pull gerekmez) (M). **Yapılmazsa:** anlaşmazlık/kayıp şüphesinde kanıt yok; iç/dış denetim için ciddi zayıflık.

### 1.6 İçe/dışa aktarma doğrulaması
- İyi: içe aktarmada yetki + 20 MB sınırı + zorunlu şube + şube şifresi + önizleme (`Program.cs:3957-3971`, `ReadImportAsync`); dışa aktarmada çift yetki kapısı (`:3977-3991`); dosya adlarında `Path.GetFileName` ile dizin atlama koruması (`:4935`, `src/DepoWise.Api/Stores/*Store.cs:14,26`).

**B1-6 · İçe aktarma satır satır commit ediliyor; yarıda kalan aktarımı geri almanın yolu yok**
- `MaterialImportService.CommitWithLookups` her satırı ayrı servis çağrısıyla yazıp hatayı yutarak devam ediyor (`src/DepoWise.Infrastructure/Reporting/MaterialImportService.cs:101-150`); import servislerinde transaction yok (`grep BeginTransaction *ImportService.cs` → 0).
- 2.600 satırlık dosya 1.400'te koparsa yarısı yazılmış olur; "bu aktarımı geri al" yok.
- **Öneri:** her aktarıma `import_batch_id` (yalnız-ekleme sütunu) + "bu partiyi çöpe taşı" (M); küçük dosyada tek transaction (S). **Yapılmazsa:** kirli ana veri, elle temizlik.

---

## 3. API ve entegrasyonlar

### 3.1 Genel yapı
- Tek dosyada minimal API: `src/DepoWise.Api/Program.cs` (5.555 satır, 439 `app.Map*`). En büyük gruplar: vehicles 25, stock 19, materials 16, maintenance 14, finance 14, work-orders 13, sync 13, users 12.
- 423 uçta `RequireAuthorization()`. Kimliksiz uçlar bilinçli ve sınırlı: `/`, `/health`, `/api/auth/login`, `/api/auth/sync-login`, `/api/public/{companies,branches,verify-branch}`, `/sync/{push,pull,enroll}` (cihaz jetonu), `/api/machines/register`, `/api/releases/latest`, `/api/setup/download`, `/api/releases/{v}/download`, `/api/backups` (içeride JWT/cihaz jetonu doğrulanıyor `:5044-5049`).
- Hata gövdesi tek biçim `{"error":"..."}`; istisna → kod eşlemesi merkezi (`Program.cs:165-188`; 500'de ham mesaj gizli).
- Her istekte tek satır erişim logu + `SLW` (>1,5 sn) etiketi (`Program.cs:148-161`).

**B3-1 · `InvalidOperationException` → 400 + ham mesaj**
- `Program.cs:182` tüm `InvalidOperationException`'ları 400 ve ham mesajla döndürüyor. Servisler iş hatası için bunu kullanıyor (doğru), ama .NET'in kendi hataları da bu tipte ("Sequence contains no elements", "Nullable object must have a value"…). Sonuç: (a) kullanıcıya teknik metin sızar, (b) 400 sayıldığı için `[ERR]` loguna düşmez → gerçek sunucu hataları gözden kaçar.
- **Öneri:** iş kuralı için özel `BusinessRuleException`; `InvalidOperationException`'ı 500'e bırak (M, çok servis). **Yapılmazsa:** gizli hatalar "kullanıcı hatası" görünür.

**B3-2 · Web'den yapılan tüm girişler API'ye TEK IP'den geliyor → hız sınırı herkesi birden kilitleyebilir**
- API giriş sınırı IP başına 30 / 5 dk (`Program.cs:194`, IP `Fly-Client-IP` → `X-Forwarded-For`). Web (Blazor Server) API'yi kendi sunucusundan çağırıyor ve kullanıcının IP'sini iletmiyor (`grep Forwarded|Fly-Client-IP src/DepoWise.Web` → 0).
- **Sonuç:** 5 dakikada web üzerinden 30 giriş denemesi (bir bot ya da sabah aynı anda giren ekip) **tüm web kullanıcılarının girişini 429 ile durdurur**; IP sınırı web üzerinden kaba kuvvete karşı da işlevsizleşir (yalnız kullanıcı-adı kilidi kalır).
- **Öneri:** web gerçek istemci IP'sini paylaşılan sırla korunan bir başlıkta iletsin, API yalnız sır doğruysa güvensin (S); web giriş sayfasına kendi sınırı (S). **Yapılmazsa:** kolay hizmet engelleme (DoS).

**B3-3 · Eski masaüstü sürümleri zorla güncellenemiyor; `min_supported_version` saklanıyor ama uygulanmıyor**
- Yayın kaydında doğrulanıp yazılıyor (`src/DepoWise.Infrastructure/Update/ReleaseService.cs:30,65`) ama masaüstü/API okumuyor (`grep MinSupported src/DepoWise.Desktop` → 0); API istemci sürümünü bilmiyor. "Ertele" sınırsız.
- Geriye uyum bugün `DbIntrospect.ColumnExists` ve "eski istemci yeni alanı yok sayar" disipliniyle sağlanıyor (`Program.cs:453, 497-499`) — iyi; ama protokol kırıcı bir değişiklikte eski sürümü durdurmanın yolu yok.
- **Öneri:** masaüstü isteklerine sürüm başlığı + API'de alt sınırın altını 426 ile reddet + "zorunlu güncelleme" penceresi (M; release mekanizması → onay gerekir). **Yapılmazsa:** yıllar önceki kurulum eski kurallarla veri yazmaya devam eder.

**B3-4 · İstemci zaman aşımı/yeniden deneme tutarlı; ama ilk/tam eşitleme sayfalanmıyor**
- Masaüstü: her istemcide `static HttpClient` + zaman aşımı (Org 20 sn, Auth 25 sn, Lookup 12 sn, push/pull 300 sn — `OrgServerClient.cs:27`, `ServerAuthClient.cs:23`, `LookupSyncService.cs:21`, `BusinessSyncPushService.cs:22`, `BusinessSyncPullService.cs:53`). Hata → "çevrimdışı" sonucu (`OrgServerClient.cs:90,171,599`); otomatik yeniden deneme yalnız senkron turunda (doğru: tek katman). Web: 30 sn, yeniden deneme yok (doğru, çünkü bazı uçlar idempotent değil — B1-3).
- **Risk:** tam eşitleme tek istek. Sunucu 58 tablonun tamamını belleğe `List<Dictionary>` olarak alıp tek JSON üretiyor (`BusinessSyncService.cs:583-640`). 512 MB API'de 200–300 bin satır = bellek taşması/300 sn aşımı; yarıda kopan kurulum baştan başlıyor (araştırma raporu "ilk eşitleme sayfa sayfa").
- **Öneri:** `business-pull`'a tablo+imleç bazlı sayfa ve istemcide "kaldığı yerden devam" (M–L). **Yapılmazsa:** büyük firmada ilk kurulum bitmeyebilir, API düşer.

**B3-5 · Küçük API kusurları**
- `/sync/pull` `limit` üst sınırı yok (`Program.cs:477`, `SyncServer.cs:85`) → `Math.Min(limit, 1000)` (S).
- `/api/setup` yüklemesinde boyut sınırı yok (`Program.cs:4986-4994`; kardeş uçta `ReleaseStore.MaxPackageBytes` var) (S).
- `CompanyVersion`, `company_id` sütunu olmayan çocuk tablolarda **tüm firmaların** MAX'ını alıyor (`BusinessSyncService.cs:671-673`) → başka firmadaki değişiklik bu firmanın masaüstlerini gereksiz uyandırır (trafik + küçük bilgi sızıntısı). Ebeveyn üzerinden süzmek (S).
- Kimliksiz `/api/public/companies` tüm firma adlarını listeliyor (`Program.cs:436-445`) — giriş ekranı için gerekli ama müşteri listesini herkese açıyor. Firma kodu ile giriş düşünülebilir (S–M, ürün kararı).

### 3.2 Dış servisler
- Supabase PostgreSQL (doğrudan bağlantı), Fly.io (API + web), GitHub Actions (kesinti takibi `.github/workflows/kesinti-takibi.yml`, 15 dk), Google Drive (haftalık yedek `scripts/yedek_al.ps1`), GeoIP (`src/DepoWise.Api/GeoIp.cs`). E-posta/SMS sağlayıcısı **yok** → hatırlatma/rapor e-postası için ileride tek bir SMTP sağlayıcısı yeter (B2-5).

---

## 4. Güvenlik

### 4.1 İyi durumda olanlar (kanıtlı)
- JWT anahtarı üretimde zorunlu, yoksa API açılmaz (`Program.cs:29-39`); ömür 12 saat, imza + süre doğrulanıyor (`src/DepoWise.Api/JwtTokens.cs:18,48-56`).
- Yetkiler token'dan değil **her istekte DB'den** (önbellekli) kuruluyor; pasif/silinmiş kullanıcı anında düşüyor (`src/DepoWise.Infrastructure/Security/AuthService.cs` `CreateSessionForUser/LoadSnapshot`); çapraz firma yalnız süper admin.
- Parola: PBKDF2-HMAC-SHA256, tuz + 100.000 tur (`src/DepoWise.Infrastructure/Security/PasswordHasher.cs:11,20`).
- Senkron push'ta tenant kapıları: `company_id` oturumdan zorlanıyor (`BusinessSyncService.cs:1508`), çocuk tablo ebeveyn kontrolü, karşı uç (TNT-02), şube (GAP-6) (`:1205-1246`).
- Kimliksiz uçlarda hız sınırı (giriş, şube şifresi, makine kaydı, indirme, yedek) (`Program.cs:194-210`); yüklemeden ÖNCE yetki (GUV-A1 `:4944-4952`).
- Güvenlik başlıkları: API `nosniff/DENY/no-referrer` (`Program.cs:92-99`); web + HSTS + antiforgery (`src/DepoWise.Web/Program.cs:60-91`); DataProtection anahtarları kalıcı birimde (`fly.web.toml` `/dpkeys`).
- Güncelleme: SHA-256 zorunlu, boş checksum'la kurulum yok (`src/DepoWise.Desktop/UpdateInstaller.cs:21-24`), önbellekteki paket yeniden doğrulanıyor (`AutoUpdateService.cs:76-83`).
- Depoda sır yok: izlenen dosyalarda bağlantı dizesi/jeton deseni taraması temiz (yalnız `.env.example`).

### 4.2 Bulgular

**G4-1 · KRİTİK ZİNCİR — Güncelleme paketleri imzasız + süper admin tek parolayla korunuyor → tek hesap ele geçerse TÜM masaüstlerinde kod çalıştırılabilir**
- Checksum paketle **aynı sunucudan** geliyor (`ReleaseService.cs:95`); masaüstünde gömülü açık anahtarla imza doğrulaması yok (`signed` alanı yalnız bilgi). Checksum yalnız **bozulmaya** karşı korur, **sahteciliğe** karşı korumaz.
- Yayın yetkisi = süper admin parolası (`Program.cs:4952`, `scripts/publish_release.mjs`). Süper adminde MFA yok (`docs/SECURITY.md`: "Gelişmiş MFA: Bekliyor").
- **Senaryo:** parola sızar (oltalama, başka sitede aynı parola) → sahte paket kendi checksum'ıyla yayınlanır → tüm firmaların masaüstleri onu "güncelleme" olarak kurar.
- **Öneri (ücretsiz):** (1) Paketleri **çevrimdışı tutulan Ed25519/ECDSA anahtarıyla** imzala, açık anahtarı masaüstüne göm, kurmadan önce doğrula — sunucu ele geçse bile sahte paket kurulmaz (M; release mekanizması → onay). (2) Süper admin için TOTP (M). (3) Yayın ucuna ek yayın sırrı / IP sınırı (S).
- **Yapılmazsa:** tek parola sızıntısı = tüm müşterilerde fidye yazılımı riski. **En yüksek öncelik.**

**G4-2 · Push'ta kayıt kimliği başka firmada varsa üzerine yazma/firmaya taşıma engellenmiyor (savunma derinliği eksik)**
- `UpsertRow` `ON CONFLICT(id) DO UPDATE SET company_id=excluded.company_id, ...` üretiyor (`BusinessSyncService.cs:1508, 1544, 1563-1566`); `WHERE`'de **mevcut satırın firmasının oturum firmasıyla aynı olduğu** kontrolü yok. Ebeveyn/karşı-uç kapıları yalnız **referansları** denetliyor, satırın kendi kimliğini değil.
- Koruma bugün yalnız id'lerin tahmin edilemez GUID olmasına dayanıyor. Başka firmanın bir kayıt id'si öğrenilirse (dışa aktarma, eski firma, süper admin makinesi), o satır push ile **kendi firmasına taşınabilir/değiştirilebilir**. Bunu sınayan test bulunamadı.
- **Öneri:** company_id'li tablolarda `whereLww`'ye `AND {table}.company_id = excluded.company_id` + red sayımı + test (S). **Yapılmazsa:** düşük olasılıklı ama tenant izolasyonunu kıran, iz bırakmayan değişiklik. *(Tenant davranışı → onay; ama "acil güvenlik düzeltmesi" sınıfında güvenli minimum.)*

**G4-3 · Bağımlılık açığı: `SQLitePCLRaw.lib.e_sqlite3 2.1.11` — Yüksek (GHSA-2m69-gcr7-jv3q)**

`dotnet list package --vulnerable --include-transitive` (2026-10-13):

| Proje | Açıklı paket (geçişli) | Önem |
|---|---|---|
| DepoWise.Api | SQLitePCLRaw.lib.e_sqlite3 2.1.11 | High |
| DepoWise.Desktop | SQLitePCLRaw.lib.e_sqlite3 2.1.11 | High |
| DepoWise.Infrastructure | SQLitePCLRaw.lib.e_sqlite3 2.1.11 | High |
| Application, Domain, Web, Setup | — | temiz |

- Gerçek etki alanı masaüstü (yerel SQLite). **Öneri:** `Microsoft.Data.Sqlite` 10.0.9 → 10.0.12 ve/veya `SQLitePCLRaw.bundle_e_sqlite3`'ü yamalı sürüme açıkça sabitle; tam test (S). Bu komutu yayın öncesi kontrol listesine ekle (S).

**G4-4 · .NET 8 desteği 10 Kasım 2026'da bitiyor (4 hafta kaldı)**
- Tüm projeler `net8.0`; Docker `sdk:8.0`/`aspnet:8.0` (`Dockerfile:2,7`, `Dockerfile.web:2,7`). Destek bitince güvenlik yaması gelmez. `--outdated`: JwtBearer 8.0.11 → 10.0.12, Npgsql 8.0.5 → 10.0.3, SignalR.Client 8.0.11 → 10.0.12.
- **Öneri:** .NET 10 (LTS) geçiş planı: önce API+web (Docker), sonra masaüstü (self-contained paket + kurulum testi) (M–L). Kısa vadede 8.0 son yamaları. **Yapılmazsa:** Kasım sonrası çıkan her .NET açığı kalıcı açık kalır.
- Diğer eski paketler (bilgi): Avalonia 12.0.4 → 12.1.4, MudBlazor 9.6.0 → 9.11.0, QuestPDF, Dapper, ClosedXML, QRCoder küçük sürümler; SkiaSharp 2.88.8 → 4.x (büyük sürüm, dikkat).

**G4-5 · Yedekler şifresiz**
- Üretim `pg_dump` kişisel Google Drive'a **şifrelenmeden** yükleniyor (`scripts/yedek_al.ps1:1-9`); içinde tüm firmaların verisi + parola özetleri var.
- Masaüstü günlük yedekleri yerelde (Belgeler\Alpnex_Yedekler) ve sunucuda `/data`'da şifresiz (`src/DepoWise.Infrastructure/Files/BackupService.cs:11-12`, `Program.cs:5044-5058`); şifreleme yalnız "beni hatırla" jetonunda (DPAPI, `RememberMeService.cs`).
- **Öneri:** Drive'a yüklemeden önce `age`/`7z AES-256` ile şifrele, anahtarı Drive dışında sakla (S). Sunucudaki makine yedeklerini firma anahtarıyla şifrele (M). **Yapılmazsa:** Google hesabı ya da Fly birimi ele geçerse tüm müşteri verisi açık.

**G4-6 · Jeton iptali yok; yenileme sınırsız**
- `/api/auth/refresh` geçerli herhangi bir JWT'yi yeni 12 saatlik jetona çeviriyor (`Program.cs:415-420`); çıkış/parola değişiminde eski jetonlar geçersizleşmiyor. Masaüstü `authsig` ile kendini çıkarıyor (`ShellViewModel.cs:767-783`) ama çalınmış jeton, kullanıcı pasife alınana dek yenilenerek yaşar.
- **Öneri:** `users.token_epoch` (yalnız-ekleme) → JWT'ye koy; parola değişimi/"tüm oturumları kapat"ta artır; `SessionFor` karşılaştırsın (S–M). **Yapılmazsa:** parola değiştirmek saldırganı dışarı atmaz.

**G4-7 · Masaüstü yerel veritabanı şifresiz ve yetki daralınca eski veri kalıyor**
- Yerelde firma verisi + çevrimdışı giriş için parola özetleri var. Şube filtresi yalnız ön muhasebe tablolarında (GAP-6 `BusinessSyncService.cs:586-588`); şube yetkisi daralınca önceden inmiş veri yerelde kalıyor (temizlik yalnız firma kalıcı silinince: `LocalPurgeService.cs:3-12`) — araştırma raporu Risk 4.
- PBKDF2 100.000 tur, OWASP güncel önerisinin (600.000) altında → çalınan dizüstünden parola kırma kolaylaşır.
- **Öneri:** tur sayısını artır + girişte yeniden özetle (S); yetki daralınca kapsam dışı şube satırlarını yerelde temizle (M); kurulum kılavuzunda BitLocker önerisi (S). SQLCipher şimdilik gereksiz.

**G4-8 · Güvenlik belgesi gerçeği yansıtmıyor (yanlış güven)**
- `docs/SECURITY.md` "loglar `LogRedactor` ile maskelenir" diyor; `LogRedactor` (`src/DepoWise.Application/Security/LogRedactor.cs`) **hiçbir yerde çağrılmıyor**. Masaüstü `SyncLog` sunucu yanıt gövdesinin 500 karakterini yazıyor (`BusinessSyncPushService.cs:130`), API 500'de tam exception yazıyor (`Program.cs:184`). Bugün parola/jeton görünmüyor ama koruma tesadüfi.
- Aynı belge "CSRF `csrf.ts`, rate limit sync push 60/dk, admin 30/dk" diyor — terk edilmiş Next.js'e ait; .NET API'de `business-push` ve yönetim uçlarında hız sınırı yok.
- **Öneri:** `SECURITY.md`'yi gerçeğe göre düzelt; `LogRedactor`'ı `SyncLog.Write` ve API 500 loguna bağla (S).

**G4-9 · Diğer küçük notlar**
- CORS `AllowAnyOrigin` (`Program.cs:65`): jeton başlıkta olduğu için risk düşük; gerekli kökenlerle sınırlamak (S).
- JWT anahtarı için yalnız "boş değil" kontrolü (`Program.cs:29-39`) → ≥32 bayt zorunluluğu (S).
- Kullanıcı adı başına 5 hatada kilit başkasının hesabını kilitlemeye açık (bilinen ödünleşim; B3-2 ile birlikte düşünülmeli).
- Depo herkese açık (hafıza notu "Repo PUBLIC → Actions ücretsiz"). Kodda sır yok ama `docs/` gerçek müşteri adı, iç mimari, güvenlik geçmişi ve `docs/claude-memory/` içeriyor → saldırgan için yol haritası. Özel depo + ücretsiz Actions dakikası (2.000 dk/ay) bugünkü kullanıma yetiyor mu **ölçüp karar ver** (S).

---

## 5. Performans ve güvenilirlik (kod düzeyi)

**P5-1 · Her push'ta tam stok defteri taraması** — bkz. B2-2 (M).

**P5-2 · Tam eşitleme sunucuda tek seferde belleğe alınıyor** — bkz. B3-4 (M–L). 512 MB API için en olası bellek taşması kaynağı.

**P5-3 · Masaüstünde çoğu ekran veritabanını arayüz iş parçacığında okuyor**
- 84 ViewModel'in yalnız 9'u `Task.Run` kullanıyor; örn. `StockMovementsViewModel.Load` senkron sorgu + sayım sorgusu (`src/DepoWise.Desktop/ViewModels/StockMovementsViewModel.cs:72-97`). `.Result/.Wait` neredeyse yok (yalnız açılış `DesktopBootstrap.cs:28`) — iyi.
- Tavanlar var (1000 satır + "toplam N" uyarısı) — bugün kabul edilebilir; ama `%arama%` LIKE indekssiz, 200–300 bin satırda her aramada donma olur (araştırma: FTS5 trigram).
- **Öneri:** en büyük 5 liste ekranında yüklemeyi arka plana al + iptal (S–M); büyük tablolarda keyset sayfalama (M). Ölçmeden indeks ekleme (proje kuralı).

**P5-4 · SQLite bakım komutları yok**
- `PRAGMA optimize`, `wal_checkpoint(TRUNCATE)`, `quick_check` periyodik çalışmıyor (`grep` → 0); yedek doğrulamasında `integrity_check` var (`BackupService.cs:105-121`) ama `foreign_key_check` yok.
- **Öneri:** günlük yedek adımına `PRAGMA optimize` + boşta `wal_checkpoint(TRUNCATE)` + `foreign_key_check` (S).

**P5-5 · Gözlemlenebilirlik ve alarm**
- Var: istek logu, `[ERR]/[SLW]`, `/api/server/status` (süper admin), GitHub Actions kesinti takibi (15 dk), masaüstü `SyncLog` + `startup.log`.
- Eksik: (1) disk %90 uyarısı yalnız **biri durum ekranını açınca** hesaplanıp loglanıyor (`Program.cs:280-287`) → alarm değil; kesinti takibi iş akışı bu eşiği kontrol edip bildirim üretebilir (S). (2) `fly.toml`/`fly.web.toml`'da **sağlık kontrolü (checks) yok** → web daha önce bellek dolup kilitlendiğinde (`src/DepoWise.Web/Program.cs:23-26` notu) Fly kendiliğinden yeniden başlatmadı; `/health` kontrolü eklemek (S). (3) Masaüstü senkron hatalarını uzaktan görmenin yolu yok; kişisel veri içermeyen hata özetini sunucuya "teşhis" olarak göndermek (M).

**P5-6 · Yedekleme / geri yükleme / felaket kurtarma (DR)**
- Var: masaüstü günlük `VACUUM INTO` + 30 gün + `integrity_check` + gerçek geri yükleme (`BackupService.cs:55-184`); sunucuya yükleme + aylık zip + 3 yıl (`Program.cs:5056`); haftalık PG yedeği + **geri yükleme provası** (`scripts/yedek_prova.ps1`) — sektörde nadir, iyi.
- Eksikler:
  - **Kurtarma noktası (RPO) 1 hafta** ve yedek **geliştiricinin kişisel bilgisayarındaki Görev Zamanlayıcı'ya** bağlı (`scripts/yedek_al.ps1:8`). Bilgisayar kapalı/bozuksa yedek yok. Supabase planının kendi günlük yedek/PITR durumu belgede doğrulanmalı. **Öneri:** aynı betiği GitHub Actions'ta günlük + şifreli çalıştır (S–M).
  - Fly `/data` tek makine/tek birim (`fly.toml` `[[mounts]]`): makine yedekleri ve paketler burada. Birim kaybında makine yedek arşivi gider (ana veri PG'de, ikincil risk). Birim anlık görüntü saklama süresi kontrol edilmeli (S).
  - Yerel geri yükleme gönderilmemiş kayıtları sessizce silebilir; "önce bekleyen değişiklikleri gönder / uyar" adımı (M).

**P5-7 · Eşitleme trafiği** — 2026-10-10 ölçümünden sonra `CompanyVersion` tek sorguya indirilmiş (`BusinessSyncService.cs:648-680`), uzun yoklamadaki ara kontrol kaldırılmış (`Program.cs:539`) — doğru yön. Kalan: B3-5'teki çapraz firma MAX düzeltmesi.

---

## 6. Öncelik tablosu

| # | Bulgu | Tür | Risk (yapılmazsa) | Kar. | Onay gerekir mi |
|---|---|---|---|---|---|
| 1 | G4-1 İmzasız güncelleme + MFA'sız süper admin | Güvenlik | Tüm masaüstlerinde kötü amaçlı kod | M | Evet (release) |
| 2 | G4-2 Push'ta başka firmanın id'sini ezme | Güvenlik/tenant | İz bırakmayan çapraz firma değişikliği | S | Evet (acil sınıfı) |
| 3 | B2-1 Saat kayması → kayıt gönderilmez | Veri kaybı | Sessiz kayıp | S (uyarı) / M (sunucu damgası) | Kısmen (senkron) |
| 4 | B1-5 Masaüstü audit'i sunucuya gitmiyor | Denetim | Kanıt kaybı | M | Evet (senkron) |
| 5 | G4-3 SQLite yerel kütüphane açığı | Güvenlik | Bilinen açık | S | Hayır |
| 6 | G4-4 .NET 8 destek sonu (10.11.2026) | Güvenlik | Yamasız platform | M–L | Evet (platform) |
| 7 | G4-5 Şifresiz yedekler + P5-6 haftalık RPO | Veri/güvenlik | Sızıntı / 1 haftalık kayıp | S–M | Hayır |
| 8 | B1-3 Web yakıt/bakım uçlarında idempotency yok | Veri bütünlüğü | Çift kayıt | S | Hayır (geriye uyumlu) |
| 9 | B1-4 Defter satırları senkronla değişebilir | Veri bütünlüğü | Geçmiş iz bırakmadan değişir | S–M | Evet (senkron) |
| 10 | B1-1 Çevrimdışı negatif stok sessiz | İş kuralı | Eksi stok, sorumsuz | M | Evet (iş kuralı) |
| 11 | B3-2 Web girişleri tek IP | Kullanılabilirlik/güvenlik | Toplu giriş engeli | S | Hayır |
| 12 | B2-6 Pull imlecinde örtüşme yok | Veri | Nadiren eksik kayıt | S | Evet (senkron) |
| 13 | B3-4/P5-2 Sayfasız tam eşitleme | Güvenilirlik | Büyük firmada kurulum bitmez | M–L | Evet (senkron) |
| 14 | G4-6 Jeton iptali yok | Güvenlik | Çalınan oturum sürer | S–M | Evet (migration) |
| 15 | B2-2 Her push'ta tam defter taraması | Performans | Ölçekte yavaşlama | M | Hayır |
| 16 | B2-5 Sunucu zamanlanmış işleri (hatırlatma vb.) | Ürün/otomasyon | Kaçan bakım/muayene | M | Ürün kararı |
| 17 | P5-5 Fly sağlık kontrolü + disk alarmı | Operasyon | Kilitlenmede uzun kesinti | S | Hayır |
| 18 | G4-8 SECURITY.md düzeltme + LogRedactor bağla | Belge/güvenlik | Yanlış güven | S | Hayır |
| 19 | B2-3 `_alertTimer` sızıntısı | Hata | Çift sorgu/ses | S | Hayır |
| 20 | B3-1, B3-3, B3-5, B1-2, B1-6, G4-7, G4-9, P5-3, P5-4 | Çeşitli | Düşük–orta | S–M | Karışık |

**Gereksiz altyapı uyarısı:** Bu bulguların hiçbiri kuyruk sunucusu, ayrı iş sunucusu, Redis veya ücretli izleme aracı gerektirmiyor. Hepsi mevcut API süreci (`BackgroundService`), mevcut PG, GitHub Actions ve kod değişiklikleriyle çözülebilir.

**Doğrulanmamış / sınır:** Canlı ortama bağlanılmadı; Supabase yedek planı, Fly birim anlık görüntü ayarı ve GitHub deposunun görünürlüğü yalnız belge/hafıza notundan okundu. G4-2 ve B2-1 kod okumasıyla çıkarıldı; düzeltmeden önce birer hedef testle kanıtlanmalı.
