# Çevrimdışı-öncelikli (offline-first) senkron, büyük veri performansı ve yedek — Alpnex denetim notları

Erişim tarihi: 2026-10-12. Yalnız açık web. "(doğrulanmadı)" = birincil kaynakla teyit edilemedi. Satıcı pazarlaması ile belgelenmiş davranış ayrı işaretlendi.

Alpnex bağlamı (görevden): .NET 8 Avalonia masaüstü + makine başına SQLite (WAL) ↔ Supabase PostgreSQL (minimal API); Blazor Server web doğrudan çevrimiçi; firma (tenant) filtresi; stok hareket defteri (ledger) tek doğru kaynak; stok/sayaç/yakıt/bakım/onayda LWW YASAK (operation id + transaction + idempotency); diğer ana veride sürümlü LWW; `/api/sync/wait` long-poll + 60 sn güvenlik turu; web'de çakışma ekranı; operasyonel kayıt fiziksel silinmez (ters kayıt). Ölçek: tablo başına 200–300 bin kayıt.

## 1. Kanıtlanmış offline-first senkron mimarileri (outbox, idempotency, cursor, tombstone, çakışma stratejileri, yeniden deneme, zehirli mesaj)

### Takeaway
Olgun sistemlerin ortak deseni: istemcide sıralı, kalıcı bir değişiklik kuyruğu (outbox) + sunucuda istemci başına "son uygulanan işlem no" ile tam-bir-kez uygulama + sunucu yetkili (server-authoritative) uzlaştırma + sunucunun verdiği opak cursor/checkpoint ile delta çekme + silmelerin işaret (tombstone/soft delete) olarak taşınması. Alpnex'in "operation id + transaction + idempotency + ledger" yaklaşımı bu desenle örtüşüyor; asıl riskler cursor'ın zaman damgası (`updated_at`) üzerinden tutulması, kalıcı (zehirli) hata ile geçici hatanın ayrılmaması ve yeniden denemede jitter eksikliği olabilir — bunlar kodda doğrulanmalı.

### Cited Findings
**Outbox / işlem kuyruğu ve idempotency**
- PowerSync: istemci yazmaları "an ordered log of mutations" olan bir upload kuyruğunda tutulur ve geliştiricinin backend uç noktalarına sırayla boşaltılır; işlemler PUT/PATCH/DELETE türündedir. — [PowerSync: Handling Update Conflicts](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts)
- PowerSync: işlemler "must be idempotent"; her işlem istemci başına artan bir operation ID taşır ve tekrarları ayıklamak için kullanılabilir. Belgelenmiş varsayılan davranış: basit backend ile alan bazında LWW; silme her zaman kazanır. — [PowerSync: Handling Update Conflicts](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts)
- PowerSync örnek `uploadData` akışı `transaction.complete()`'i yalnız backend isteği başarılı olunca çağırır (yani başarısız işlem kuyrukta kalır); "your backend may receive the same operation multiple times". — [PowerSync: Custom Conflict Resolution](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts/custom-conflict-resolution)
- Replicache: her mutasyon sıralı mutation ID ile kalıcı olarak kaydedilir; push uç noktası mutasyonları sırayla uygular ve istemcinin "last mutation id"sini günceller — hedef bir değişikliğin sunucuda tam bir kez uygulanması. Sunucu yetkilidir; istemci sonuçları spekülatiftir ve sunucu farklı sonuç hesaplayabilir. — [Replicache: How it works](https://doc.replicache.dev/concepts/how-it-works)
- Replicache'te açık bir "reddetme" mekanizması yoktur; mutatör sunucuda farklı dala girip sonucu duruma yazar (örn. oda rezervasyonu başarısızsa kullanıcının durumuna bir bit yazılır). — [Replicache: How it works](https://doc.replicache.dev/concepts/how-it-works)
- Microsoft Datasync Community Toolkit (.NET; Avalonia test edilmiş platformlar arasında; sunucu PostgreSQL ve SQLite destekli): yerel kayıt her kaydedildiğinde operations queue'ya eklenir; `PushAsync` tamamlananları kuyruktan siler, başarısızları "failed" işaretler; `PushResult.FailedRequests` sunucu yanıtını içerir. `ParallelOperations` >1 ise "operations MAY be executed on the remote service out of order". — [Datasync client in-depth](https://communitytoolkit.github.io/Datasync/in-depth/client/); [Datasync ana sayfa](https://communitytoolkit.github.io/Datasync/)
- Datasync Toolkit güncel sürüm ASP.NET Core 10 / .NET 10 istemcisi ister ve sürüm eşleştirmeyi önerir (Alpnex .NET 8'de; doğrudan benimseme bu nedenle sürüm yükseltmesi gerektirir). — [Datasync ana sayfa](https://communitytoolkit.github.io/Datasync/)
- ERPNext için bir üçüncü taraf çevrimdışı POS ürünü: her satış istemci üretimli idempotency anahtarıyla yerelde kuyruğa yazılır, sunucu anahtarla tekilleştirir, stok/fiyat senkron anında yeniden doğrulanır, çakışmalar otomatik çözülmek yerine inceleme kuyruğuna gider. (SATICI PAZARLAMASI, bağımsız doğrulanmadı; POS Awesome'ın kendi davranışı bulunamadı.) — [ECOSIRE ERPNext offline POS](https://ecosire.com/apps/erpnext/erpnext-offline-pos)

**Delta çekme: sunucu cursor'ı/checkpoint vs zaman damgası**
- Replicache pull: istemci "a value opaque to the client identifying the canonical server state" olan cookie gönderir; yanıt yeni cookie + yama + onaylanan mutation ID'leri döner; istemci son sunucu sürümüne geri sarıp yamayı uygular ve bekleyen mutasyonları üstüne yeniden oynatır (rebase). — [Replicache: How it works](https://doc.replicache.dev/concepts/how-it-works)
- CouchDB replikasyonu: `_changes` akışındaki sequence ID'ler "MUST be incremental, but MAY NOT always be an integer"; checkpoint'ler her iki tarafta `_local` belgelerine her batch commit'inden sonra yazılır, böylece hata sonrası "resume from last point of success". — [CouchDB Replication Protocol](https://docs.couchdb.org/en/stable/replication/protocol.html)
- Datasync Toolkit delta token'ı = varlık tipi/sorgu başına en büyük `UpdatedAt` (Unix epoch ms). SQLite "stores DateTimeOffset using a second accuracy by default" uyarısı ve değer dönüştürücü önerisi var. — [Datasync client in-depth](https://communitytoolkit.github.io/Datasync/in-depth/client/)
- PowerSync: zaman damgası kontrolünde "the only real catch" saat kaymasıdır; "Timestamps can be unreliable if servers have clock skew… For critical data, use sequence numbers instead." Sürüm/sequence yaklaşımında sayaç veritabanınca yönetilir, ancak farklı alanlara yapılan düzenlemeleri birleştiremez. — [PowerSync: Custom Conflict Resolution](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts/custom-conflict-resolution)
- PostgreSQL sequence'leri işlemsel değildir: `nextval` değeri abort'ta geri alınmaz, boşluk oluşur, "cannot be used to obtain 'gapless' sequences". Belge commit sırasını tartışmaz; ancak bundan, sequence değerinin commit sırasını yansıtmadığı çıkar (daha küçük ID'li satır daha sonra görünür olabilir). — [PostgreSQL: Sequence Functions](https://www.postgresql.org/docs/current/functions-sequence.html)
- Bilinen tuzak (topluluk kaynakları, resmi belge değil): `max(updated_at)` watermark'ı uzun süren bir transaction'ın satırlarını kaçırabilir — satırlar transaction başlangıç zamanıyla damgalanır ve cursor geçtikten sonra görünür olur ("These rows are never synced"); xid sırası commit sırası değildir; öneri: cursor'ı en eski açık transaction'a (xmin) göre geride tutmak ya da örtüşme penceresi + idempotent upsert. Fivetran'ın sorgu tabanlı/XMIN bağlayıcıları da uzun transaction bitene kadar bekler. — [pgsql-general thread](https://postgrespro.com/list/thread-id/1506035); [Syncing an application cache with xmin](https://postgrespro.com/list/thread-id/1531732); [Fivetran query-based connector](https://fivetran.com/docs/connectors/databases/postgresql/troubleshooting/query-based-connector-not-syncing); [Fivetran XMIN connector](https://fivetran.com/docs/connectors/databases/postgresql/troubleshooting/xmin-connector-not-syncing)

**Silme / tombstone**
- CouchDB: silmeler `_changes` akışında `"deleted":true` bayrağıyla taşınır (silinmiş belge replike edilir). — [CouchDB Replication Protocol](https://docs.couchdb.org/en/stable/replication/protocol.html)
- Datasync: senkron edilen varlıkta `Id`, `UpdatedAt`, `Version` zorunlu; `Deleted` yalnız serviste soft delete kullanılıyorsa gerekir. — [Datasync client in-depth](https://communitytoolkit.github.io/Datasync/in-depth/client/)
- Dotmim.Sync çakışma tipleri arasında `RemoteIsDeletedLocalExists`, `RemoteCleanedupDeleteLocalUpdate` gibi silmeye özgü durumlar vardır (yani tombstone'ların temizlenmesi sonrası gelen güncelleme ayrı bir vaka). — [Dotmim.Sync Conflicts](https://dotmimsync.readthedocs.io/Conflict.html)
- Dynamics 365 Field Service: senkron sonrası çevrimdışı profil filtresine artık uymayan kayıtlar cihazdan kaldırılır; kullanıcı kaldırılmış kaydı açarsa "Record Not Found" görür. — [D365 Field Service: offline data sync](https://learn.microsoft.com/en-us/dynamics365/field-service/mobile/offline-data-sync)

**Çakışma stratejileri (ne zaman hangisi)**
- PowerSync stratejileri: zaman damgası tabanlı ret; sürüm/sequence ile ret; alan bazında LWW (alan başına zaman damgası); iş kuralı doğrulaması (sevk edilmiş siparişi kilitleme, geçerli durum geçişleri, fiyat değişikliğine onay); sunucu tarafında çakışma tablosuna her iki sürümü yazıp kullanıcıya seçtirme ("high-stakes data" için); alan değişiklik günlüğü (pending/applied/failed durumlu); stok için kümülatif/delta işlemler ve negatif stoka düşürecek değişikliği reddetme. — [PowerSync: Custom Conflict Resolution](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts/custom-conflict-resolution)
- Dotmim.Sync: varsayılan `ServerWins`; çakışmalar daima sunucuda çözülür; `OnApplyChangesFailed` ile `ServerWins/ClientWins/MergeRow/Rollback`; MergeRow'da `FinalRow` iki tarafa uygulanır. — [Dotmim.Sync Conflicts](https://dotmimsync.readthedocs.io/Conflict.html)
- Datasync: "By default, the library does not do conflict resolution automatically"; `IConflictResolver` ya da hazır ClientWins/ServerWins çözücüleri. — [Datasync client in-depth](https://communitytoolkit.github.io/Datasync/in-depth/client/)
- Dynamics 365 Field Service: çakışma "table level, not the field level" — teknisyen Başlangıç, dispeçer Bitiş saatini değiştirse bile çakışmadır, alan bazında birleştirme yapılmaz. Çakışma algılama kapalıyken (VARSAYILAN) çevrimdışı cihazın değişikliği kazanır ve dispeçerin değişikliğini ezer; açıkken sunucu kazanır ve teknisyen senkron hatası görebilir. Yöneticiler Settings > Sync Errors'ta geçmiş hataları görür. — [D365 Field Service: offline data sync](https://learn.microsoft.com/en-us/dynamics365/field-service/mobile/offline-data-sync)
- ElectricSQL yalnız okuma yolunu (shapes) senkronlar: "Electric does not do write-path sync"; dört yazma deseni (çevrimiçi yazma, iyimser durum, paylaşılan kalıcı iyimser durum, veritabanı üzerinden senkron: gölge tablo + tetikleyici + değişiklik günlüğü). Örnek uygulamada reddedilen yazma tüm yerel durumu siler (naif); kılavuz yazmayı "rejected" işaretlemeyi ya da yalnız nedensel bağımlı yazmaları temizlemeyi önerir; çakışmaların "extremely rare" olduğu görüşünü aktarır (görüş, ölçüm değil). — [ElectricSQL: Writes guide](https://electric.ax/docs/guides/writes)
- Ink & Switch "Local-first software": CRDT'ler aynı nesnenin aynı alanına eşzamanlı yazmayı otomatik çözemez, çakışan değerleri tutup uygulamaya/kullanıcıya bırakır; tüm geçmişi sakladıkları için performans/disk sorunu var ve geçmiş kolay budanamaz; ağ iletişimi "unsolved"; yazarlar üretim için deneysel CRDT araçlarının kanıtlanmış ürünlerin yerine konmasını henüz önermez. Makale CRDT'lerin değişmezleri (invariant, örn. stok ≥ 0) koruyup korumadığını tartışmıyor. — [Ink & Switch: Local-first software](https://www.inkandswitch.com/essay/local-first/)

**Yeniden deneme, backoff+jitter, zehirli mesaj**
- AWS Builders' Library: durum değiştiren çağrıları yalnız API idempotent ise yeniden dene (token tabanlı idempotency); üstel backoff'u sınırla ve deneme sayısını kısıtla; jitter ekle ki istemciler aynı anda dönmesin; periyodik işlerde de jitter (ama host başına tutarlı ofset); yeniden denemeyi tek katmanda yap (3 deneme × 5 katman = 243x yük); devre kesici yerine yerel token bucket ile yeniden deneme bütçesi; her uzak çağrıda bağlantı ve istek zaman aşımı, aşağı akış gecikme yüzdeliğine (örn. p99.9) göre. — [AWS: Timeouts, retries and backoff with jitter](https://builder.aws.com/content/3EumjoZascWd1oZiEgL8ORlv3qE/timeouts-retries-and-backoff-with-jitter)
- CouchDB: her istek hatası ölümcül sayılmamalı, zaman aşımları algılanıp tekrarlanmalı ("Data must flow"), ancak 401, 403, 409, 412 yanıtları tekrarlanmamalı (tekrar etmek kimlik/veri sorununu çözmez) — yani kalıcı hata = zehirli işlem, geçici hata = yeniden dene ayrımı. — [CouchDB Replication Protocol](https://docs.couchdb.org/en/stable/replication/protocol.html)
- Replicache "poke": sunucu veri göndermez, yalnız istemciye "yakında tekrar pull et" sinyali verir (Alpnex'in long-poll `/api/sync/wait` + periyodik tur desenine benzer). — [Replicache: How it works](https://doc.replicache.dev/concepts/how-it-works)

### Inferences
- Alpnex'in ledger + operation id + idempotency tasarımı, PowerSync/Replicache'in belgelediği "sıralı kuyruk + istemci başına işlem no + sunucu yetkili" deseninin alan-özgü bir uygulaması; CRDT gerekmez (stok ≥ 0 gibi değişmezler CRDT ile garanti edilemez, sunucu doğrulaması gerekir — bu çıkarımdır).
- Denetim maddesi: delta cursor `updated_at`/zaman damgası mı, yoksa sunucuda commit sonrası atanan bir sıra (ör. değişiklik günlüğü tablosu, tetikleyiciyle) mı? Zaman damgasıysa (a) saat kayması, (b) uzun transaction'da kaçan satır, (c) SQLite'ta saniye hassasiyeti riskleri var; çözüm: örtüşme penceresi (cursor − birkaç saniye/dakika) + idempotent upsert, ya da cursor'ı en eski açık transaction'ın gerisinde tutmak. 60 sn güvenlik turu kaçan satırı ancak cursor geride bırakılıyorsa yakalar.
- Denetim maddesi: başarısız işlem kuyrukta sonsuza dek ilk sırada kalıp sonrakileri bloke ediyor mu (head-of-line blocking)? Belgelenmiş sistemler kalıcı hatayı (4xx doğrulama/çakışma) "failed/rejected" olarak ayırıp kullanıcıya gösterir, geçici hatayı (ağ/5xx/zaman aşımı) backoff+jitter ile tekrarlar.
- Paralel push sırayı bozabilir (Datasync uyarısı); aynı kayda/aynı stok kalemine ait işlemler sıralı gönderilmeli.
- Yeni kuyruk/mesaj altyapısı (RabbitMQ vb.) gerekmiyor: tüm referans sistemler outbox'ı aynı veritabanında tablo olarak tutuyor.

### Gaps
- Dotmim.Sync'in PostgreSQL sağlayıcısının resmî/güncel durumu GitHub ana sayfasında net değil (yalnız SQL Server, MySQL, SQLite NuGet'leri sayılıyor; PostgreSQL yalnız konu etiketi/betik olarak geçiyor) — doğrulanmadı.
- Datasync Toolkit'in soft delete ve 409/412 HTTP davranışı ayrıntısı çekilen sayfada yoktu.
- POS Awesome'ın (ERPNext) kendi çevrimdışı davranışı için birincil kaynak bulunamadı.
- Salesforce Field Service offline priming belgesi bu turda çekilemedi.

## 2. Referans uygulamalar: çakışma, silme, büyük veri, izinler/kısmi senkron, şema göçü

### Takeaway
Referans ürünlerin hepsi kısmi/filtreli senkronu (profil/shape/kural) ve sunucu yetkili yazmayı benimsiyor; büyük ilk senkronda batch + checkpoint ile kaldığı yerden devam ediliyor. Kapanan Realm/Atlas Device Sync, kapalı kaynak bir senkron servisine bağımlılığın riskini gösteriyor — Alpnex'in kendi minimal API'si bu açıdan avantajlı. Çevrimdışı istemcilerde şema göçü için birincil kaynak bulunamadı.

### Cited Findings
- MongoDB Atlas Device Sync Eylül 2024'te kullanımdan kaldırıldı (deprecated), 30 Eylül 2025'te ömrü sona erdi; Device SDK'lar, Data API, HTTPS Endpoints ve kimlik doğrulama da etkilendi; cihaz üstü veritabanı açık kaynak olarak sürüyor; MongoDB alternatif olarak Ditto, PowerSync, ObjectBox, AWS AppSync vb. gösterdi; destek üzerinden 3–6 ay uzatma tipik denildi (forum, personel yanıtı). — [MongoDB forum: Atlas Device Sync EOL](https://mongodb.com/community/forums/t/atlas-device-sync-end-of-life-and-deprecation/296687); [Device Sync deprecation docs](https://mongodb.com/docs/atlas/app-services/sync/device-sync-deprecation); [Device SDKs deprecation](https://mongodb.com/docs/atlas/device-sdks/deprecation)
- Dynamics 365 Field Service: ilk indirmeden sonra uygulama offline-first çalışır; tablo bazında senkron aralığı 5 dk–1 gün; ilişkili tablo daha kısa aralıklıysa aralık aşılabilir; ilk senkron ve uzun aradan sonra büyük artımlı senkron sırasında kullanıcıya bildirim gösterilir; Offline Status sayfası tabloların son güncelleme zamanını, cihaz durumu sayfası son senkron ayrıntısını ve hata mesajını gösterir; telemetri Application Insights'a gönderilebilir. — [D365 Field Service: offline data sync](https://learn.microsoft.com/en-us/dynamics365/field-service/mobile/offline-data-sync)
- Topluluk yazısı: geniş çevrimdışı profiller yavaş ilk senkron ve daha büyük çakışma yüzeyi demektir; rol başına yalnız gereken tabloları senkronlayın (topluluk görüşü). — [Dynamics Communities: Mastering D365 Field Service mobile](https://dynamicscommunities.com/ug/dynamics-ce-crm-ug/mastering-d365-field-service-the-mobile-app/)
- Microsoft: "Incorrect or missing data in offline mode" genellikle çevrimdışı profil filtreleri/yayını ile ilgili. — [MS Troubleshoot: incorrect/missing data offline](https://learn.microsoft.com/el-gr/troubleshoot/dynamics-365/field-service/mobile-app/incorrect-missing-data-offline-mode)
- CouchDB: değişiklikler küçük parçalar halinde okunur, `_bulk_docs` ile gönderilir; checkpoint yalnız batch commit'inden sonra → kaldığı yerden devam (resumable). — [CouchDB Replication Protocol](https://docs.couchdb.org/en/stable/replication/protocol.html)
- Datasync: ardışık sayfalar sırayla istenir ve her istekten sonra delta token güncellenir (sayfa sayfa ilerleme kalıcı). Yeniden senkron için: bekleyen işlemleri ve delta token'ları sil, tekrar pull et. — [Datasync client in-depth](https://communitytoolkit.github.io/Datasync/in-depth/client/)
- ElectricSQL kısmi senkronu "shapes" ile yapar; yazma yolu uygulamaya bırakılır. — [ElectricSQL: Writes guide](https://electric.ax/docs/guides/writes)
- Ink & Switch: yerel-öncelikli yazılımda sunucunun rolü yok olmaz, yedek/aktarıcı/"cloud peer" olur; ancak bulut uygulamalarında sunucu verisi "primary, authoritative copy" kabul edilir. — [Ink & Switch: Local-first software](https://www.inkandswitch.com/essay/local-first/)

### Inferences
- Alpnex'te tenant filtresi = D365 "offline profile"/Electric "shape" karşılığı. Denetim: kullanıcı yetkisi/şube değişince, artık kapsam dışı kalan kayıtlar yerelden temizleniyor mu (D365 bunu açıkça yapıyor)?
- İlk senkron 200–300 bin satır/tablo için: sayfalı (keyset) çekme + her sayfa sonrası cursor'ı kalıcı yazma + ilerleme göstergesi; yarıda kopan kurulum baştan başlamamalı. Alternatif: sunucuda hazır SQLite anlık görüntüsü (snapshot) indirip sonra delta — bu çıkarımdır, kaynakta Dotmim.Sync snapshot özelliği doğrulanamadı.
- Atlas dersi: kritik senkron mantığını üçüncü taraf kapalı servise bağlamamak; Alpnex'in kendi API'si + PostgreSQL bu riski taşımıyor.

### Gaps
- Çevrimdışı istemciler varken şema göçü (sürüm müzakeresi, geriye uyumluluk penceresi) için bu turda birincil kaynak bulunamadı. Önerilen kontrol (çıkarım): istemci her istekte uygulama/şema sürümünü gönderir; sunucu desteklenen en eski sürümü ilan eder; yalnız eklemeli (additive) şema değişiklikleri; eski sürüm istemcinin kuyruktaki işlemleri için uyumluluk penceresi; pencere dışı istemci önce güncelleme ister ama kuyruğunu kaybetmez.
- Salesforce Field Service "offline priming" ayrıntısı çekilemedi.

## 3. Stoka özgü eşzamanlılık: iki çevrimdışı istemcinin aynı stoku düşmesi, rezervasyon, reddetme akışı

### Takeaway
Belgelenmiş yaklaşım: stok miktarını üzerine yazmak yerine delta (hareket) olarak göndermek ve negatif stoka düşürecek hareketi sunucuda transaction içinde reddedip kullanıcıya görünür bir "reddedildi" durumu ile döndürmek. Çevrimdışıyken gerçek bir garanti mümkün değil; ya ret+telafi akışı ya da çevrimiçi ön-rezervasyon/kota gerekir.

### Cited Findings
- PowerSync: envanter için miktar değişikliklerini delta olarak ele alıp mevcut değere ekleyin; backend stoku negatife düşürecek değişiklikleri reddeder; örnek ret yanıtları `stale_update`, `insufficient_stock`. — [PowerSync: Custom Conflict Resolution](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts/custom-conflict-resolution)
- PowerSync: reddedilen/başarısız yazmaları yüzeye çıkarma desenleri — çakışmalar tablosu senkronlanıp "Keep My Version / Keep Server Version" arayüzü; değişiklik günlüğünde `pending/applied/failed` durumu, hata mesajı ve yeniden dene düğmesi; ayrı hata tablosu senkronlanmazsa ayrıntılı sunucu hatası gösterilemez. — [PowerSync: Custom Conflict Resolution](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts/custom-conflict-resolution)
- PowerSync: iş kuralı örneği olarak "completed" olmuş siparişe güncellemenin reddi; "increment column by 1" gibi özel işlemler ileride desteklenebilir (henüz yok). — [PowerSync: Handling Update Conflicts](https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts)
- Replicache: sunucu mutatörü farklı sonuç üretebilir; başarısız rezervasyon kullanıcı durumunda bayrakla işaretlenir (ret = veri olarak senkronlanır). — [Replicache: How it works](https://doc.replicache.dev/concepts/how-it-works)
- ERPNext üçüncü taraf çevrimdışı POS: senkron anında stok yeniden doğrulanır; çevrimdışıyken tükenen ürün otomatik çözülmez, inceleme kuyruğuna gider; senkron günlüğü hangi terminalden ne geçti/ne başarısız oldu kaydeder (SATICI PAZARLAMASI). — [ECOSIRE ERPNext offline POS](https://ecosire.com/apps/erpnext/erpnext-offline-pos)
- ElectricSQL: reddedilen yazmada yalnız nedensel bağımlı yazmaları geri almak veya "rejected" işaretlemek önerilir (tümünü silmek naif). — [ElectricSQL: Writes guide](https://electric.ax/docs/guides/writes)

### Inferences
- Alpnex ledger tasarımı PowerSync'in "delta işlem" önerisiyle uyumlu. Denetim: sunucuda negatif stok kontrolü, bakiye hesabı ile hareket ekleme aynı transaction'da ve kilit/serileştirme ile mi yapılıyor (ör. `SELECT ... FOR UPDATE` ürün-depo satırında ya da SERIALIZABLE)? Aksi halde iki eşzamanlı çevrimiçi istek de yarış koşuluna girebilir (çıkarım; PostgreSQL kilitleme belgesi bu turda çekilmedi).
- Reddedilen çevrimdışı hareketin bağımlıları (aynı ürünün sonraki çıkışı, transferin karşı ayağı) için politika gerekli: ElectricSQL'in "yalnız nedensel bağımlıları geri al" önerisi.
- Kullanıcıya gösterim: masaüstünde de (yalnız web değil) kayıt bazında "beklemede / uygulandı / reddedildi (neden)" durumu + düzeltme/yeniden deneme eylemi; D365'teki gibi senkron durum simgesi ve yönetici hata listesi.
- Çevrimdışı negatif stok riski iş kuralıyla da daraltılabilir: yerelde bilinen bakiye + bekleyen çıkışlar ile ön uyarı (garanti değil).

### Gaps
- Çevrimdışı "rezervasyon/kota tahsisi" (escrow) desenine dair birincil, ürün belgesi düzeyinde kaynak bu turda bulunamadı.

## 4. SQLite (200–300 bin+ satır) ve PostgreSQL performansı

### Takeaway
Bu ölçek SQLite/PostgreSQL için küçük-orta; asıl risk yanlış/eksik indeks, OFFSET sayfalama, `%x%` LIKE araması ve WAL'in büyümesi. Çözümler belgelenmiş: bileşik/kapsayan indeks, keyset sayfalama, FTS5 trigram (SQLite) ve pg_trgm GIN (PostgreSQL), `PRAGMA optimize`, WAL checkpoint yönetimi; Supabase'de kalıcı sunucu için doğrudan bağlantı ya da session pooler.

### Cited Findings
**SQLite indeks ve planlayıcı**
- Kapsayan indeks (covering index) tabloya hiç bakmadan sorguyu yanıtlar, yaklaşık 2 kat sabit hız kazancı; bileşik indekste en soldaki sütun eşitlikle kullanılırsa sonraki sütun zaten sıralıdır ve ORDER BY sıralama adımı gerekmez; biri diğerinin ön eki olan iki indeks tutmayın; OR koşulunda her terim indeksli değilse tam tablo taraması; SQLite AND terimleri için birden çok indeksi kesiştirmez. — [SQLite: Query Planning](https://www.sqlite.org/queryplanner.html)
- `PRAGMA optimize`: kısa ömürlü bağlantıda kapanmadan önce; uzun ömürlü bağlantıda açılışta `PRAGMA optimize=0x10002;` sonra periyodik (saatlik/günlük) `PRAGMA optimize;`; `CREATE INDEX` sonrası; genelde çok hızlı; 3.46.0'dan beri analysis_limit yerine önerilir. — [SQLite PRAGMA](https://www.sqlite.org/pragma.html#pragma_optimize)
- `foreign_keys` varsayılan KAPALI, transaction içinde no-op, açıkça ayarlanmalı; `busy_timeout` bağlantı başına tek meşgul işleyici. — [SQLite PRAGMA](https://www.sqlite.org/pragma.html)

**SQLite WAL**
- Otomatik checkpoint varsayılanı WAL ≥1000 sayfa (~4 MB) olunca commit'te (PASSIVE); her zaman en az bir okuyucu aktifse checkpoint tamamlanamaz ve WAL "grows without bound"; çözüm okuyucu boşluklarında ya da RESTART/TRUNCATE ile elle checkpoint; WAL büyüdükçe okuma yavaşlar; çok büyük yazma transaction'ı büyük WAL üretir; WAL ağ dosya sisteminde çalışmaz; tek yazıcı. — [SQLite: WAL](https://www.sqlite.org/wal.html)
- WAL'de `synchronous=NORMAL` bozulmaya karşı güvenli ama güç kesintisinde son commit geri alınabilir (dayanıklılık kaybı; uygulama çökmesinde kayıp yok); `FULL` her commit'te ek sync ile güç kesintisine dayanıklı. `wal_checkpoint(TRUNCATE)` WAL'i sıfır bayta indirir; dönüş değerinin ilki 1 ise checkpoint engellenmiştir. — [SQLite PRAGMA](https://www.sqlite.org/pragma.html)

**Arama**
- FTS5 trigram tokenizer: alt dize eşleşmesi ve indeksli `LIKE '%...%'`/GLOB; 3 karakterden kısa alt dizeler eşleşmez; joker olmayan en az bir dizi yoksa tam tarama; `case_sensitive 1` iken yalnız GLOB; ESCAPE'li LIKE optimize edilmez. Harici içerik (external content) tablosu tetikleyicilerle senkron tutulmalı, bozulursa `'rebuild'`; `'optimize'` komutu indeksi tek b-tree'ye birleştirir (uzun sürebilir, `'merge'` ile parçalanabilir); `bm25`/`rank` sıralaması. — [SQLite FTS5](https://www.sqlite.org/fts5.html)
- PostgreSQL pg_trgm: GIN/GiST trigram indeksleri `LIKE`, `ILIKE`, `~`, `~*` aramalarını sol-sabitli olmadan destekler; trigram çıkarılamayan desen tam indeks taramasına düşer; en yakın N sıralaması (`<->`) GiST'te verimli, GIN'de değil. — [PostgreSQL: pg_trgm](https://www.postgresql.org/docs/current/pgtrgm.html)

**Sayfalama**
- OFFSET atlanan her satırı okuyup atar; keyset (seek) son satırın sıralama anahtarını tutup `(created_at, id) > (...)` gibi satır karşılaştırmasıyla eşleşen bileşik indekse doğrudan atlar; benzersizlik için `id` kırıcı sütunu gerekir; rastgele sayfaya atlama yok; sayfalar arasında eklenen/güncellenen satırlar kaçabilir/tekrarlanabilir. CockroachDB keyset'i "fast, recommended", OFFSET'i "slow, not recommended" olarak etiketler. — [Vlad Mihalcea: SQL Seek/Keyset](https://vladmihalcea.com/sql-seek-keyset-pagination); [CockroachDB: Pagination](https://www.cockroachlabs.com/docs/stable/pagination.md)

**Supabase bağlantı havuzu**
- Doğrudan bağlantı (5432) "persistent backends, such as virtual machines and long-running containers" için; IPv6 (IPv4 eklentisi yoksa). Paylaşımlı Supavisor session modu (5432) IPv4 ve prepared statement destekler; transaction modu (6543) sunucusuz içindir ve prepared statement, oturum ötesi cursor, `LISTEN/NOTIFY`, `SET`, geçici tablo desteklemez. Kütüphane havuz varsayılanları sunucusuz için yüksektir. — [Supabase: Connecting to Postgres](https://supabase.com/docs/guides/database/connecting-to-postgres)

### Inferences
- Denetim: masaüstü liste ekranları OFFSET mi kullanıyor? 200–300 bin satırda son sayfalar yavaşlar; keyset + `(tenant_id, tarih, id)` bileşik indeksi önerilir. Tenant filtresi her indeksin ilk sütunu olmalı (çıkarım, planlayıcı belgesinin eşitlik-önce kuralından).
- Denetim: `%arama%` LIKE varsa SQLite'ta FTS5 trigram, PostgreSQL'de `gin_trgm_ops` indeksi; 3 karakterden kısa arama için ön ek araması ya da minimum uzunluk.
- Denetim: uzun açık okuma transaction'ı (ör. açık DataReader, uzun rapor) WAL'i şişirebilir; uygulamada boşta `wal_checkpoint(TRUNCATE)` ve dönüş değerinin loglanması.
- Npgsql ile Supabase transaction pooler (6543) kullanılıyorsa prepared statement/`LISTEN` sorunları; Alpnex API kalıcı süreç olduğu için session pooler ya da doğrudan bağlantı daha uygun (bellekteki "Maximum Pool Size" notuyla birlikte denetlenmeli).
- Toplu ekleme (ilk senkron) tek transaction'da ve hazırlanmış komutla yapılmalı; ancak çok büyük tek transaction WAL'i büyütür → orta boy batch'ler (çıkarım; WAL belgesindeki büyük transaction uyarısından).

### Gaps
- PostgreSQL `EXPLAIN ANALYZE`, kısmi indeks (partial index) ve Supabase plan başına havuz limitleri bu turda birincil kaynaktan çekilmedi.
- Microsoft.Data.Sqlite bağlantı havuzu davranışı (pooling) bu turda doğrulanmadı.

## 5. Büyük veride masaüstü arayüz

### Takeaway
Avalonia'da sanallaştırma yalnız yüksekliği sınırlı sanallaştırıcı panelde çalışır; resmi performans kılavuzu düzenleme gerekmiyorsa DataGrid'den kaçınmayı ve büyük veri için TreeDataGrid'i önerir (lisans durumu değişken). İptal (cancellation) ve debounce için bu turda Avalonia'ya özgü birincil kaynak bulunamadı.

### Cited Findings
- Sanallaştırıcı panel yalnız görünen öğeler için kontrol oluşturur, kaydırınca geri dönüştürür; yüksekliği sınırlandırılmış olmalı — StackPanel içine konursa sanallaştırma devre dışı kalır. Performans kılavuzu: düzenleme gerekmiyorsa DataGrid'den kaçın; büyük veri için TreeDataGrid (Ekim 2025 itibarıyla Avalonia Pro/Accelerate kapsamında; adlandırma kaynaklarda tutarsız). — [Avalonia: Performance optimization](https://docs.avaloniaui.net/docs/app-development/performance); [Avalonia: Improving performance](https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance)
- ItemsRepeater sanallaştırmayı destekler ama seçim/kaydırma/çerçeve yoktur; ScrollViewer içindeki ItemsRepeater'ın bağlı `IList<T>`'nin tüm öğelerini "eagerly" numaraladığı rapor edilmiş (GitHub issue); ~600 bin öğede ~600 MB RAM raporu (topluluk, doğrulanmadı). — [Avalonia: ItemsRepeater](https://docs.avaloniaui.net/controls/data-display/collections/itemsrepeater); [Avalonia issue #6904](https://github.com/AvaloniaUI/Avalonia/issues/6904); [Avalonia discussion #15600](https://github.com/AvaloniaUI/Avalonia/discussions/15600)

### Inferences
- UI sanallaştırma yalnız görsel kontrolleri azaltır; 300 bin satırı belleğe yüklemek hâlâ maliyetli → veri sanallaştırma (keyset ile sayfa sayfa yükleme) + sanallaştırılmış liste.
- Arama kutusunda debounce (~250–400 ms) ve önceki sorgunun `CancellationToken` ile iptali, sonuçların UI iş parçacığına yalnız son isteğe aitse yazılması (genel .NET uygulaması; bu turda birincil kaynak çekilmedi — doğrulanmadı).
- Denetim: DataGrid'ler StackPanel/ScrollViewer içinde mi (sanallaştırma kırılır)?

### Gaps
- Avalonia DataGrid sanallaştırma iç ayrıntıları ve güncel sürümde #6904 durumu doğrulanmadı.
- Microsoft.Data.Sqlite'ta `CancellationToken`'ın çalışan sorguyu gerçekten kesip kesmediği bu turda doğrulanmadı.

## 6. Yedek/geri yükleme, felaket kurtarma ve bütünlük kontrolleri

### Takeaway
Açık bir SQLite dosyasını düz kopyalamak yerine Online Backup API ya da `VACUUM INTO` kullanılmalı; bütünlük için düzenli `quick_check` (O(N)), daha seyrek `integrity_check` (O(N log N)) ve ayrıca `foreign_key_check`. Bulut tarafı yedeği için Supabase doğrudan bağlantısı `pg_dump` için önerilen yol.

### Cited Findings
- Online Backup API kopya başladığı andaki tutarlı anlık görüntüyü üretir; artımlı (örn. 5 sayfa + 250 ms bekleme) çalışarak kaynağı kilitlemez; başka bağlantı yazarsa yedek baştan başlar ve çok sık yazımda hiç bitmeyebilir; aynı bağlantıdan yazımda hedef otomatik güncellenir. Düz dosya kopyalamanın sorunları: yazıcıları bekletir, güç/OS hatasında bozuk yedek. Alternatifler: `VACUUM INTO`, `sqlite3_rsync`. — [SQLite: Online Backup API](https://www.sqlite.org/backup.html)
- `integrity_check`: sıra dışı girdiler, bozuk kayıtlar, eksik sayfalar, eksik/fazla indeks girdileri, UNIQUE/CHECK/NOT NULL ihlalleri, freelist; O(N log N). `quick_check`: UNIQUE ve indeks-tablo eşleşmesini atlar, O(N). İkisi de yabancı anahtarı kontrol etmez → `foreign_key_check`. 3.33.0'dan beri tablo bazında `integrity_check(TABLO)`. — [SQLite PRAGMA](https://www.sqlite.org/pragma.html)
- WAL: son bağlantı kapanınca son checkpoint yapılır ve WAL/shm silinir; WAL ağ sürücüsünde çalışmaz (yerel veritabanını ağ paylaşımına koymayın). — [SQLite: WAL](https://www.sqlite.org/wal.html)
- Supabase doğrudan bağlantıyı migration, `pg_dump`, yedek ve replikasyon için önerir. — [Supabase: Connecting to Postgres](https://supabase.com/docs/guides/database/connecting-to-postgres)
- FTS5 harici içerik indeksi tutarsızlaşırsa `rebuild` ile yeniden kurulur (bütünlük onarımı). — [SQLite FTS5](https://www.sqlite.org/fts5.html)

### Inferences
- Masaüstü yerel DB'si için: kuyrukta gönderilmemiş işlem varken yerel veriyi sıfırlamak/geri yüklemek veri kaybıdır → geri yükleme öncesi bekleyen outbox'ın sunucuya iletildiği doğrulanmalı; kurtarmada tercih "sunucudan yeniden ilk senkron + yerel bekleyen işlemleri koru".
- Açılışta ya da haftalık `quick_check`; hata olursa yerel DB'yi karantinaya alıp sunucudan yeniden kurma akışı (çıkarım).
- Ledger tabanlı tasarımda ek bütünlük kontrolü: sunucuda "bakiye = hareket toplamı" mutabakat sorgusu, yerel ile sunucu arasında tenant bazlı sayım/sağlama karşılaştırması (çıkarım).

### Gaps
- Supabase plan başına yedek saklama süresi ve PITR (point-in-time recovery) koşulları bu turda çekilmedi.
