# A3b — Veri bütünlüğü, senkron, çevrimdışı davranış ve performans (200–300 bin kayıt)

> Tarih: 2026-10-13 · Dal: `arge/2026-10-kapsamli` · Şema sürümü: 98 · Yöntem: kod okuma (dosya:satır kanıtlı) +
> geçici SQLite dosyasında gerçek servis kodu ile ölçüm. Üretim DB / canlı API'ye dokunulmadı; `src/` ve testler değişmedi.

## 0. Yönetici özeti

**En kritik veri kaybı riskleri**
1. **Çevrimdışı numara çakışması (P1):** iki makine çevrimdışıyken aynı belge/fatura/sipariş no (`MAX+1`) ya da aynı malzeme kodunu
   üretirse sunucu ikinciyi "kalıcı hata" sayıp **atlıyor**, kullanıcı görmüyor; bağlı satırlar da düşüyor. Makine öneki planı
   (`SENKRON_ID_KOD.md`) uygulanmamış.
2. **Çekme imleci istemci saatine dayanıyor (P2):** geç gelen (çevrimdışı) satırlar başka makinenin imlecinin gerisinde kalabilir.
   Bugün bu risk **bir hata sayesinde görünmüyor**: imleç değişkeni (`AlinanWatermark`) hiç atanmıyor (P3), her turda **tam paket**
   yeniden çekiliyor. İkisi birlikte düzeltilmeli — yalnız imleci düzeltmek sessiz veri kaybını başlatır.
3. **Kalıcı reddedilen ve 5 denemede gönderilemeyen satırlar bir daha gönderilmiyor**, yalnız günlük dosyasına yazılıyor (P7).
4. İki çevrimdışı makine aynı stoğu çıkarırsa sunucuda **sessiz negatif bakiye** (P8); şube yetkisi daralınca eski veri yerelde kalıyor (P9);
   sunucu yedekten dönerse masaüstleri aradaki kayıtları geri göndermiyor (P15).

**Performans (gerçek servis kodu, 300 bin satır/tablo, dizüstü SSD)**
- Liste ekranları sayfa başına saniyeler sürüyor: malzeme listesi ilk sayfa **7,8 sn**, ada göre sıralı / son sayfa **10,4 sn**;
  günlük faaliyet son sayfa **8,9 sn**; stok hareketleri **3,9–7,9 sn**; ana sayfa özeti **7,7 sn**.
- Ekran açılışında arka planda çalışan `SearchGridAll` karesel büyüyor: 40 bin satırda 23–50 sn; 300 bin'de tahminen **1 saatin üstü**.
- Senkron: tam paket **567 MB JSON / 4,9 GB bellek**, yeni makineye uygulama **173 sn**; imleç hatası yüzünden aynı paket her turda
  yeniden uygulanıyor (**132 sn**). Sunucunun her yoklamada çalıştırdığı `CompanyVersion` **5,1 sn**; her push sonrası tüm defterden
  bakiye hesabı **12,1 sn** (SQLite'ta yazma kilidiyle).
- Hızlı kazanç: `ANALYZE` iki sorguyu **1,7 sn → 0 ms**'ye indirdi; uygulama hiç çalıştırmıyor (P18). Masaüstü gzip istemiyor (P12).

**Şema:** SQLite ve PostgreSQL şemaları birebir eşleşiyor (107 tablo, 110 FK, kolon/NOT NULL farkı yok); riskler davranışta
(metin içinde para, C# `like()`, kilit farkı, senkron damgasında indeks yok).

Öncelik sırası ve kabul ölçütleri: bölüm 5. Ölçümü tekrarlama: bölüm 6.


## 1. SQLite ↔ PostgreSQL şema karşılaştırması

**Nasıl ölçüldü:** aynı `MigrationCatalog` (98 migration) bir kez geçici SQLite dosyasına, bir kez yerel test PG sunucusunda
(`localhost:54329`) açılan geçici `dw_perf` veritabanına uygulandı; SQLite `PRAGMA table_xinfo / index_list / foreign_key_list`
ile PG `information_schema.columns / pg_indexes / pg_constraint` makineyle karşılaştırıldı (betik: bölüm 6).

| Ölçüt | SQLite | PostgreSQL | Sonuç |
|---|---:|---:|---|
| Tablo sayısı | 107 | 107 | Birebir aynı; yalnız birinde olan tablo yok |
| Kolon adı | — | — | Yalnız birinde olan kolon **yok** |
| NOT NULL farkı | — | — | **Yok** |
| Yabancı anahtar (FK) | 110 | 110 | Aynı |
| İndeks (PK hariç) | 167 | 172 | Fark 5 = PG'de satır içi `UNIQUE(...)` kısıtları ad alıyor; SQLite'ta `sqlite_autoindex_*` (aynı anlam) |
| Tip | `TEXT/BIGINT/INTEGER` | `text/bigint/integer` | Eşleşiyor (6 bayrak kolonu iki tarafta da 32 bit `INTEGER`) |
| CHECK | `material_equivalents` | aynı | Aynı |

**Sonuç:** şema eşliği çok iyi (migration'lar tek kaynaktan iki lehçeye uygulanıyor). Riskler şemada değil, **davranışta**:

1. **Tip katılığı.** SQLite "tip yakınlığı" (affinity) kullanır: `TEXT` kolona sayı, `BIGINT` kolona metin yazılabilir; PG reddeder.
   Para/miktar iki tarafta da `TEXT` (`stock_movements.quantity`, `materials.unit_price` …) ve **hiçbir tarafta "sayısal metin"
   CHECK'i yok**. Masaüstünde bozuk bir değer (`"1,5"`) yazılırsa senkronla sunucuya gider; PG'de `CAST(... AS numeric)` raporu düşürür.
2. **Arama/sıralama maliyeti.** Masaüstü `like()` fonksiyonunu ve `TRNOCASE` sıralamasını **C# ile** değiştirir
   (`SqliteConnectionFactory.cs:45-52` — her karşılaştırmada .NET'e geri çağrı + `Regex`); PG'de ICU `dw_tr`/`trnocase`
   (`Migration053`). Sonuç aynı, maliyet çok farklı: SQLite'ta `LIKE` hiçbir indeksi kullanamaz (bölüm 4'te ölçüldü).
3. **Eşit tarihli satırların sırası** SQLite'ta `rowid`, PG'de `id` (GUID) ile kırılıyor (`SqlDialect.RowTieBreaker`) → aynı veri iki
   platformda farklı sırada görünebilir (kozmetik).
4. **İşlem kilidi.** `BeginImmediate()` SQLite'ta yazma kilidini baştan alır, PG'de sıradan `READ COMMITTED`
   (`DbCommandExtensions.cs:37-38`). Bakiye yeniden hesabı SQLite'ta süresince bütün yazmaları bekletir (bölüm 4).
5. **`stock_movements.updated_at` NULL olabilir** (`Migration069`); senkron `COALESCE(updated_at, created_at)` kullandığı için
   `ix_stock_movements_updated` indeksi bu ifadede **kullanılamaz** (`BusinessSyncService.cs:711-714`).
6. **Senkron damgası için indeks yok.** Senkrona giren ~58 tablonun hiçbirinde `(company_id, updated_at)` indeksi yok; her delta
   sorgusu ve her `CompanyVersion` çağrısı tam tablo taraması yapar (iki lehçede de).

## 2. Senkron tasarımı — kodda gerçekte olan

| Konu | Uygulama (kanıt) |
|---|---|
| **Ne eşitlenir** | `BusinessSyncService.Tables` — ~58 iş tablosu, FK sırasıyla (`BusinessSyncService.cs:32-161`). `stock_balances` taşınmaz, defterden yeniden hesaplanır (SNK-11). Şubeler/tanımlar ayrı yol: `BranchMirror`, `LookupSyncService` (sunucu otoriteli, 2 dk'da bir). Firma işlemleri gerçek outbox ile (`CompanySyncService.cs:88` → `OutboxWriter`). Sohbet senkron dışı. |
| **Yön** | Çift yön: masaüstü → `POST /api/sync/business-push` (`Program.cs:490`), sunucu → `GET /api/sync/business-pull?since=` (`Program.cs:505`). Web doğrudan sunucu DB'sine yazar. |
| **Gönderim modeli** | **Durum tabanlı anlık görüntü (snapshot)**, işlem günlüğü (operation log) DEĞİL: `damga > watermark` olan satırların TAMAMI tek JSON'da (`BusinessSyncPushService.cs:59-64`, `BusinessSyncService.cs:583-637`). Sayfalama yok. |
| **İmleç (cursor)** | Gönder: makinenin kendi yerel `MAX(updated_at)` değeri (`sync_push_watermark`, `BusinessSyncPushService.cs:182-213`). Çek: "alınan satırların en büyük damgası" olması amaçlanmış (`BusinessSyncPullService.cs:73-119`, `ShellViewModel.cs:557-562`) — **ama bu değer hiç atanmıyor** (R1). Damgalar **istemci saatinden** gelir; sunucu `updated_at`'i yeniden basmaz (`UpsertRow`, `BusinessSyncService.cs:1500-1570`). |
| **Çakışma stratejisi** | Tüm tablolarda satır düzeyi **LWW** (`excluded.updated_at >= mevcut`, `BusinessSyncService.cs:1554`). İstisnalar: araç sayacı geri gidemez (yalnız push, `:1520-1538`); sunucudaki silme çekmede koşulsuz kazanır, push'ta silinmiş kayıt diriltilemez (`:1546-1559`). Çakışma **kaydı** yalnız 5 tabloda: materials, vehicles, personnel, material_requests, vehicle_maintenances (`:1042-1045`). Yakıt dağıtımı düzenleme, günlük faaliyet, fatura, kasa, iş emri, belge düzenlemeleri **sessiz LWW** (CLAUDE.md §3 "yakıt/bakımda LWW yasak" ile çelişir). |
| **İdempotency** | Satır `id` (GUID) birincil anahtarı + defter tablolarında `operation_id` tekil indeksi (`ux_stock_movements_operation`, `fuel_distributions`, `party_ledger`, `invoices`…). Aynı paket iki kez gelirse sonuç değişmez (bölüm 4'te ikinci uygulama ölçüldü). |
| **Silme / tombstone** | Yumuşak silme `is_deleted=1` LWW ile taşınır. `is_deleted`'i olmayan çocuk tablolar "ebeveyn gelince çocuk kümesini değiştir" kuralıyla (`ParentReplaceChildren`, `:522-531`). Firma kalıcı silme için `company_purges` künyesi var. Satır düzeyi fiziksel silme için künye **yok**. |
| **Yeniden deneme / backoff** | Tur bazında üstel bekleme + jitter, en çok 300 sn; yalnız geçici hatada (ağ, 5xx, 429) (`BusinessSyncPullService.cs:14-37`). |
| **Red işleme** | Sunucu yanıtı `{upserted, skipped, permanentSkipped, errors}` (`Program.cs:500`). Geçici atlananlar için watermark ilerlemez; 5 denemeden sonra "poison": watermark ilerletilir + kalıcı uyarı (`BusinessSyncPushService.cs:90-124`). **Kalıcı** atlananlar (ebeveyn yok, UNIQUE çakışması, yetki, şube kapsamı) anında geçilir ve **yalnız `sync.log`a** yazılır (`:96-102`). |
| **Çevrimdışı kuyruk kalıcılığı** | Ayrı kuyruk yok; "kuyruk" yerel tabloların kendisi + kalıcı watermark (`app_settings`). Kapanınca kaybolmaz. Tek kapı `SyncGate` ile aynı anda tek senkron (`SyncGate.cs:19-33`). |
| **Tetikleme** | Uzun yoklama `/api/sync/wait` (≤25 sn, `Program.cs:524-540`), yerel 3 sn kontrolü, 60 sn güvenlik turu (`ShellViewModel.cs:590-630`). Her `wait`/yoklamada sunucu `CompanyVersion` çalıştırır. |
| **Bakiye** | Sunucu her push'tan sonra; masaüstü satır uygulayan her çekmeden sonra **tüm firma defterini** okuyup bakiyeyi yeniden yazar (`Program.cs:497`, `BusinessSyncService.cs:1100-1104`, `StockService.cs:1283-1340`). |

### 2.1 Araştırma raporundaki risklerin kodda doğrulanması

| # | Risk | Karar | Kanıt ve açıklama |
|---|---|---|---|
| R1 | Zaman damgası imlecinin geç gelen satırı kaçırması | **DOĞRULANDI + yeni bulgu** | (a) İmleç istemci saatli `updated_at`'e dayanır; sunucu damgayı yeniden basmaz. A makinesi 2 gün çevrimdışı çalışıp dönünce satırları **2 gün önceki** damgalarla sunucuya gelir; B'nin çekme imleci o damgaların ilerisindeyse `> since` koşulu bu satırları **hiç getirmez** (`BusinessSyncService.cs:619`). Saati ileri bir makine de imleci "geleceğe" taşır. (b) **Yeni bulgu:** `BusinessSyncPullService.AlinanWatermark` yalnız tanımlı (`BusinessSyncPullService.cs:76`), hiçbir yerde atanmıyor; `AlinanEnBuyukDamga` hiç çağrılmıyor (commit `e5d04f2`'den beri). `ShellViewModel.cs:557-562` her zaman `null` okur → imleç hiç ilerlemez → her turda `since=eski imleç` (çoğu makinede `-1` → **tam paket**) çekilir. Bu bugün (a)'yı **kazara örtüyor** ama 200–300 bin satırda ağır performans/trafik sorunu (bölüm 4). İmleç tek başına düzeltilirse (a) anında sessiz veri kaybına dönüşür → ikisi **birlikte** çözülmeli. |
| R2 | Kalıcı/geçici hata ayrımı, kuyruk başının kilitlenmesi | **BÜYÜK ÖLÇÜDE ÇÖZÜLMÜŞ, kalıntı var** | HTTP düzeyinde ayrım (SNK-03) ve satır düzeyinde `permanentSkipped` (S1) var; kuyruk kilitlenmez (poison). **Kalıntı:** kalıcı sayılanlar ve poison sonrası satırlar watermark'ın altında kalır ve **bir daha gönderilmez** (yalnız yeniden düzenlenirse gider). Paket tek parça olduğundan tek bir geçici hatalı satır 5 tur boyunca bütün watermark'ı tutar. |
| R3 | Sunucunun reddettiği işlemin kullanıcıya gösterilmesi | **KISMEN DOĞRULANDI** | Geçici atlananlar rozet ve "Eşitle" mesajında görünür (`ShellViewModel.cs:296-306`), poison kalıcı uyarı bırakır. **Kalıcı** atlananlar (iki makinede aynı malzeme kodu, aynı belge no, ebeveyni olmayan satır) kullanıcıya **hiç gösterilmez** — `HasProblem` onları saymaz (`BusinessSyncPushService.cs:149-155`). Kayıt bazında "bekliyor/uygulandı/reddedildi" durumu yok. |
| R4 | Şube/yetki kapsamı daralınca yerel verinin temizlenmesi | **DOĞRULANDI** | Kapsam yalnız **çekilecek** satırları süzer (`BranchWhere`, `BusinessSyncService.cs:333-347`). Daha önce inmiş kapsam dışı satırları silen kod yok; `LocalPurgeService.PurgeBusinessData` yalnız elle "Yereli sıfırla" ve süper admin sıfırlama isteğinde çağrılır (`ShellViewModel.cs:360`, `LoginViewModel.cs:606`). Aynı bilgisayardaki farklı yetkili kullanıcılar aynı yerel DB'yi paylaşır. `materials`, `parties`, yakıt, günlük faaliyet, bakım tabloları şube kapsamında değil (`:283-311`). |
| R5 | İki çevrimdışı istemciyle negatif stok | **DOĞRULANDI** | Masaüstü çıkışta yerel bakiyeyle kontrol eder (`StockService.cs:152-180`). Sunucu push'ta defter satırını **koşulsuz** kabul eder, yalnız miktarın pozitif olduğunu denetler (`NonNegativeFields`, `:254-260`) ve bakiyeyi yeniden hesaplar. İki makine aynı 10 adedi ayrı ayrı çıkarırsa sunucuda bakiye −10 olur; red, uyarı ya da çakışma kaydı üretilmez (ADR-086 negatif bakiyeye izin verir). |
| R6 | Büyük ilk eşitlemenin kaldığı yerden sürmesi | **DOĞRULANDI (destek yok)** | İlk çekim `since=0` tek HTTP yanıtı, tek `JsonDocument.Parse`, tek transaction (`BusinessSyncPullService.cs:132-148`, `BusinessSyncService.cs:1131-1170`). 300 sn zaman aşımı; koparsa baştan başlar. İlerleme göstergesi yok. Gönderim de tek paket. |
| R7 | İstemciler çevrimdışıyken şema değişikliği | **KISMEN RİSK** | İyi: satır∩tablo kolon kesişimi yazılır (`:1500-1505`), bilinmeyen tablo atlanır, eski istemci yeni kolonları ezmez. Riskli: (a) yeni masaüstü, sunucunun henüz bilmediği kolon/tabloyu gönderirse sunucu sessizce düşürür ve watermark ilerler (API'nin önce deploy edilmesi kuralı korur — CLAUDE.md §5). (b) Senkron listesine **sonradan eklenen** tablolarda (SNK-A3 muayene, SIF-06 şablon, EKP ekipman, özel raporlar…) eklenmeden önce oluşmuş yerel satırların damgası watermark'ın altında kalır; watermark yalnız bir kez sıfırlanmış (`WatermarkEpoch = "2"`, 2026-07-22, `BusinessSyncPushService.cs:189-207`) → bu satırlar **hiç gönderilmemiş olabilir** (sahada doğrulanmalı). |

## 3. Veri kaybı ve mükerrer kayıt riskleri, migration güvenliği, yedek/geri yükleme

### 3.1 Veri kaybı senaryoları (kod kanıtlı)

| # | Senaryo | Sonuç | Kanıt |
|---|---|---|---|
| K1 | İki makine çevrimdışıyken aynı **doğal anahtarı** üretir: malzeme kodu, araç iç kodu, belge no (`MAX+1`), fatura no (seri sayacı), sipariş/iş emri no | İkinci gelen satır sunucuda `UNIQUE` ihlaliyle **kalıcı atlanır**, watermark ilerler, kullanıcı görmez. Ona bağlı çocuk satırlar (sayım satırları, fatura kalemleri, belgenin `stock_count_lines`'ı) "ebeveyn yok" diye kalıcı atlanır. Belgenin stok hareketleri ise (FK yok) sunucuya girer → sunucuda **başlıksız hareket** | Tekil indeksler: `materials(company_id, code)`, `vehicles(company_id, internal_code)`, `stock_documents(company_id, doc_type, doc_no)`, `invoices(company_id, direction, invoice_no)`, `purchase_orders`, `work_orders`, `material_requests(doc_no)`. Belge no: `StockService.cs:1449-1460` (`MAX(...)+1`, makine öneki yok). `SENKRON_ID_KOD.md` §2'deki "makine kodu" çözümü **uygulanmamış** (kodda `machine_tag` yok). Kalıcı atlama: `BusinessSyncService.cs:496-503`, `BusinessSyncPushService.cs:96-102`. |
| K2 | Bir satır 5 turda da geçici hata alır (poison) | Watermark ilerletilir, satırlar **bir daha gönderilmez**; yalnız uyarı metni kalır (kullanıcı "uyarıyı temizle" ile onu da silebilir) | `BusinessSyncPushService.cs:110-118`, `:259-266` |
| K3 | Geç gelen (geriye damgalı) satır başka makinenin imlecinin gerisinde kalır | İmleç düzeltilince **o makinede hiç görünmez** (bugün imleç hatası yüzünden tam çekme maskeliyor) | R1, `BusinessSyncService.cs:619` |
| K4 | İstemci saati yanlış (ileri/geri) | LWW yanlış kazananı seçer; ileri saatli makinenin düzenlemesi sonraki tüm düzenlemeleri ezer; saati geri olan makinenin düzenlemesi sessizce kaybolur (sunucuda daha yeni damga varsa `>=` koşulu güncellemeyi reddeder, satır "upserted" sayılır) | `BusinessSyncService.cs:1554`, sunucu damgayı yeniden basmıyor; `UpsertRow` her zaman `true` döner (`:1568`) |
| K5 | Senkron listesine sonradan eklenen tablonun eski yerel satırları | Watermark'ın altında kaldıkları için hiç gönderilmemiş olabilir | `WatermarkEpoch="2"` (`BusinessSyncPushService.cs:189-207`) yalnız bir kez artırılmış; SNK-A3/A4/A5, SIF-06, EKP-01, ZMT-01, ADR-186 tabloları daha sonra eklendi (`BusinessSyncService.cs:41-160` yorumları) |
| K6 | Sunucu haftalık yedekten geri yüklenirse | Masaüstlerinin watermark'ı ileride olduğu için yedekten sonraki **tüm masaüstü kayıtları sunucuya geri gönderilmez**; geri yükleme prosedüründe "tüm makinelerde watermark'ı sıfırla / tam gönderim" adımı yok | `docs/POSTGRES_BACKUP_RESTORE.md` §4.4; haftalık yedek `scripts/yedek_al.ps1` (Pazar 03:00) → RPO ≈ 7 gün |
| K7 | Masaüstünde "Yereli sıfırla" gönderilmemiş değişiklik varken | Önce gönderim **yapılmadan** yerel iş verisi silinir; bekleyen kayıtlar kaybolur. Onay penceresi bunu söylemiyor | `ShellViewModel.cs:343-366` (PurgeBusinessData → PullAsync(0); öncesinde PushAsync yok) |
| K8 | Çok makineli düzenlemede yakıt/günlük faaliyet/fatura/kasa | Sessiz LWW (çakışma kaydı yok); kaybeden sürüm saklanmaz | `ConflictTracked` yalnız 5 tablo (`BusinessSyncService.cs:1042-1045`) |
| K9 | Push sırasında sunucu bakiyeyi yeniden hesaplarken masaüstü/web eş zamanlı yazar | PG'de "defter imzası" (COUNT+MAX(created_at)) değişirse 3 denemeden sonra o tur **bakiye yazılmaz** (defter doğru, bakiye eski kalır) | `StockService.cs:1313-1321`; geriye damgalı satır MAX'ı değiştirmez ama COUNT'u değiştirir → yakalanır |

### 3.2 Mükerrer kayıt riskleri

- **Çift gönderim:** güvenli. GUID `id` + `operation_id` tekil indeksleri sayesinde aynı paket tekrar geldiğinde ikinci kayıt oluşmaz
  (bölüm 4'te aynı tam paket ikinci kez uygulandı, satır sayısı değişmedi).
- **Fiziksel silinip yeniden yazılan çocuklar** (talep kalemi, eşdeğer malzeme…): `ParentReplaceChildren` ile çözülmüş
  (`BusinessSyncService.cs:505-531`); yalnız ebeveyn de pakette geldiğinde çalışır — ebeveyn LWW'de kaybederse çocuk kümesi yine
  pakettekiyle değiştirilir (ebeveyn eski, çocuklar yeni karışımı olabilir).
- **Aynı mantıksal kayıt iki makinede ayrı GUID ile** (ör. iki depocu aynı faturayı çevrimdışı girer): sistem bunu ayırt edemez;
  doğal anahtar çakışıyorsa K1'e düşer (biri kaybolur), çakışmıyorsa **iki kayıt** olur. Kullanıcıya uyarı yok.
- **Echo (yankı) gönderimi:** çekilen satırlar yerelde sunucu damgasıyla yazılır, yerel `MAX(updated_at)` büyür → bir sonraki push
  bu satırları sunucuya geri gönderir (`BusinessSyncPushService.cs:59-64`; çekme sonrası watermark güncellenmiyor). Veri bozmaz
  (LWW `>=` aynı değeri yazar) ama trafik ve sunucuda her seferinde bakiye yeniden hesabı demektir.

### 3.3 Migration güvenliği

- Her migration kendi transaction'ında; SQLite ve PG'de DDL transactional → yarım migration kalmaz (`MigrationRunner.cs:20-40`).
- Masaüstü açılışta yerel DB'yi migrate eder; migration öncesi otomatik yedek **yok** (günlük yedek ayrı: `ShellViewModel.cs:723`,
  `BackupService.cs` `VACUUM INTO`). Uzun bir migration (ör. `Migration064_StockBalanceLocation` tablo yeniden kurma) 300 bin
  satırda dakikalar sürebilir; ilerleme göstergesi yok. Öneri: migration öncesi `VACUUM INTO` + süre ölçümü.
- Kural "yalnız ekleme" (CLAUDE.md §2) + `DbIntrospect.ColumnExists` ile eski şemaya dayanıklılık iyi. Eksik olan: **istemci şema
  sürümü ↔ sunucu şema sürümü** el sıkışması. Push gövdesinde sürüm yok; sunucu bilmediği kolonu sessizce atar (R7).

### 3.4 Yedek / geri yükleme (betikler okundu, çalıştırılmadı)

| Katman | Yöntem | Değerlendirme |
|---|---|---|
| Üretim PG | `scripts/yedek_al.ps1` haftalık `pg_dump -Fc` + sayım listesi → Google Drive; `scripts/yedek_prova.ps1` yerel PG'ye geri yükleyip sayımları karşılaştırır | İyi: yedeğin **geri yüklenebildiği** düzenli kanıtlanıyor. Zayıf: RPO 7 gün; Supabase ücretsiz katmanda PITR yok. Geri yükleme sonrası masaüstü yeniden gönderimi tanımsız (K6). |
| Masaüstü SQLite | Günlük otomatik `VACUUM INTO` + 30 gün rotasyon + `integrity_check` + şema doluluk kontrolü (`BackupService.cs:35-125`); açılışta `quick_check` (`DatabaseHealth.cs:54-81`) | İyi. Eksik: `PRAGMA optimize`/`ANALYZE` ve boşta `wal_checkpoint(TRUNCATE)` hiç çalışmıyor (kodda yok) — 300 bin satırda planlayıcı istatistiksiz kalıyor (bölüm 4). |
| Yerel geri yükleme | Eski yedek açılınca yedekteki watermark/imleç de geri gelir → yedekten sonra gönderilmiş kayıtlar sunucudan tekrar iner (doğru); yedekten sonra girilip **gönderilmemiş** kayıtlar kaybolur (beklenen) | Tutarlı. |

## 4. Performans ölçümleri (200 bin ve 300 bin satır)

**Veri:** tek firma; `materials`, `stock_movements`, `daily_activities`, `fuel_distributions`, `audit_logs` tablolarının **her birinde**
N satır (200.000 ve 300.000), 6 şube, 500 araç, 300 personel; tarihler 3 yıla yayılmış. Gerçek şema (98 migration) ve **gerçek servis
metotları** kullanıldı; SQL metni servisin kendisinden `sqlite3_profile` kancasıyla yakalandı (ayrıntı bölüm 6).
DB boyutu: 200 bin → **370 MB**, 300 bin → **513 MB** (yeni makinede tam eşitleme sonrası 446 MB).
Uygulamanın gerçek durumu gibi **ANALYZE yapılmadan** ölçüldü (uygulama hiç ANALYZE çalıştırmıyor).
Medyan/p95: 200 bin'de 9 ölçüm, 300 bin'de 5 ölçüm (ağır senaryolarda 3); p95 bu örneklemde ≈ en kötü ölçüm.

### 4.1 Ekran ve rapor sorguları

Hedef: liste ekranında sayfa değişimi < 300 ms. Görüldüğü gibi liste ekranlarının neredeyse tamamı 300 bin satırda 1 saniyeyi, çoğu 3 saniyeyi aşıyor.

| Senaryo (gerçek servis metodu) | 200 bin medyan ms | 200 bin p95 | 300 bin medyan ms | 300 bin p95 | SQL ifade (300 bin) | En yavaş ifade (300 bin) |
|---|---:|---:|---:|---:|---:|---|
| Malzeme listesi s.1 (SearchGrid 25) | 3228,6 | 3951,8 | 7805,2 | 8042,1 | 6 | 5469,0 ms: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALE |
| Malzeme listesi SON sayfa (derin OFFSET) | 4569,0 | 4620,1 | 10337,6 | 10685,1 | 6 | 5236,0 ms: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALE |
| Malzeme listesi ad ile ara 'rulman' | 2957,8 | 3366,2 | 6438,2 | 6852,8 | 6 | 3288,0 ms: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALE |
| Malzeme listesi ada göre sırala | 4396,1 | 5663,9 | 10368,0 | 10708,6 | 6 | 3235,0 ms: `SELECT * FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALESCE(mc. |
| Malzeme kritik stok filtresi | 2865,1 | 3010,4 | 5300,7 | 5463,5 | 6 | 3102,0 ms: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALE |
| Malzeme SearchGrid 500lük ORTA sayfa (SearchGridAll bunu N/500 kez çağırır) | 3302,6 | 3357,4 | 5183,1 | 5725,0 | 6 | 2910,0 ms: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALE |
| Stok hareketleri s.1 (SearchMovementsGrid 50) | 2817,7 | 2885,0 | 3943,7 | 4390,9 | 6 | 3945,0 ms: `SELECT COUNT(*) FROM stock_movements sm JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.comp |
| Stok hareketleri SON sayfa (derin OFFSET) | 5614,6 | 5730,8 | 7922,5 | 8062,0 | 6 | 4011,0 ms: `SELECT COALESCE(d.doc_date, sm.created_at), sm.movement_type, m.code, m.name, COALESCE(u.name,''), sm.direct |
| Stok hareketleri ekranı (SearchMovements tavan 1000) | 17,0 | 33,0 | 17,5 | 19,3 | 5 | 23,0 ms: `SELECT COALESCE(d.doc_date, sm.created_at), sm.movement_type, m.code, m.name, COALESCE(u.name,''), sm.directio |
| Stok hareketleri metin araması 'filtre' | 3483,5 | 4110,8 | 4755,9 | 4780,5 | 6 | 4694,0 ms: `SELECT COUNT(*) FROM stock_movements sm JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.comp |
| Stok hareketleri son 30 gün + tek şube | 1237,5 | 1267,4 | 1753,8 | 1756,8 | 6 | 1757,0 ms: `SELECT COUNT(*) FROM stock_movements sm JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.comp |
| Malzeme bakiyesi (GetBalance) | 0,0 | 0,1 | 0,0 | 0,0 | 5 | 0,0 ms: `PRAGMA foreign_keys = 1;` |
| Malzeme son hareketleri (RecentForMaterial) | 857,1 | 928,4 | 1184,7 | 1191,0 | 6 | 1202,0 ms: `SELECT sm.created_at, sm.movement_type, sm.direction, sm.quantity, COALESCE(br.name,''), COALESCE(d.doc_no,' |
| Bakiye yeniden hesap (RecomputeBalances, tüm firma) | 8287,3 | 8300,1 | 12137,0 | 12184,6 | 276301 | 2497,0 ms: `DELETE FROM stock_balances WHERE company_id=@c;` |
| Yakıt listesi s.1 (SearchDistributions 25) | 920,9 | 944,4 | 1258,2 | 1308,0 | 6 | 1250,0 ms: `SELECT COUNT(*) FROM fuel_distributions fd LEFT JOIN vehicles v ON v.id = fd.vehicle_id WHERE fd.company_id= |
| Yakıt listesi SON sayfa | 2011,1 | 2257,1 | 2742,5 | 2832,7 | 6 | 1554,0 ms: `SELECT fd.id, fd.vehicle_id, v.internal_code, fd.prev_meter, fd.current_meter, fd.liters, fd.unit_price, fd. |
| Yakıt listesi araç araması '34 AB 01' | 1375,0 | 1429,7 | 1858,5 | 1887,2 | 6 | 1853,0 ms: `SELECT COUNT(*) FROM fuel_distributions fd LEFT JOIN vehicles v ON v.id = fd.vehicle_id WHERE fd.company_id= |
| Yakıt aylık özet (MonthlySummary 12 ay) | 993,0 | 1002,3 | 1411,6 | 1418,8 | 5 | 1410,0 ms: `SELECT distribution_date, liters FROM fuel_distributions fd WHERE fd.company_id=@c AND fd.is_deleted=0;` |
| Günlük faaliyet s.1 (SearchGrid 25) | 2390,9 | 2637,7 | 3294,6 | 3437,1 | 6 | 1775,0 ms: `SELECT COUNT(*) FROM ( SELECT da.id AS id, da.activity_date AS date_raw, CASE da.activity_type WHEN 'mainten |
| Günlük faaliyet SON sayfa | 5615,1 | 6608,2 | 8888,7 | 9048,9 | 6 | 7118,0 ms: `SELECT * FROM ( SELECT da.id AS id, da.activity_date AS date_raw, CASE da.activity_type WHEN 'maintenance' T |
| Günlük faaliyet SearchGrid 500lük ORTA sayfa (SearchGridAll bunu N/500 kez çağırır) | 4856,6 | 4863,7 | 7786,7 | 8273,2 | 6 | 6267,0 ms: `SELECT * FROM ( SELECT da.id AS id, da.activity_date AS date_raw, CASE da.activity_type WHEN 'maintenance' T |
| Rapor: Stok Durumu (stock) | 2050,7 | 2078,5 | 3111,7 | 3137,5 | 5 | 3102,0 ms: `SELECT m.code, m.name, COALESCE(b.quantity,'0') AS qty, m.min_stock FROM materials m LEFT JOIN (SELECT mater |
| Rapor: Stok Hareketleri 1 yıl | 938,3 | 953,3 | 942,2 | 980,4 | 5 | 888,0 ms: `SELECT COALESCE(d.doc_date, sm.created_at), sm.movement_type, sm.direction, sm.quantity, m.code, m.name, COAL |
| Rapor: Yakıt Tüketimi 1 yıl (fuel) | 783,5 | 792,4 | 1069,6 | 1070,8 | 6 | 614,0 ms: `SELECT f.vehicle_id, f.distribution_date / 86400000, COALESCE(pr.full_name,''), COALESCE(pg.full_name,'') FRO |
| Rapor: Yakıt günlük 1 yıl (fuel-daily) | 1370,8 | 1383,3 | 1671,5 | 1686,2 | 6 | 1071,0 ms: `SELECT f.distribution_date / 86400000 AS gun, COALESCE(bch.name,'') AS branch_name, v.internal_code, COALESC |
| Rapor: Günlük faaliyet 1 yıl | 2073,8 | 2561,3 | 2324,1 | 2412,2 | 5 | 2323,0 ms: `SELECT da.activity_date, da.activity_type, COALESCE(da.movement_kind,'') AS kind, COALESCE(ob.name,'') AS br |
| Rapor: Araç raporu 1 yıl (vehicle) | 450,4 | 452,3 | 494,4 | 495,7 | 5 | 496,0 ms: `SELECT v.internal_code, COALESCE(v.plate,''), TRIM(COALESCE(br.name,'') // ' ' // COALESCE(vmd.name,'')) AS v |
| Denetim kaydı listesi (300) | 1,7 | 11,7 | 1,5 | 10,0 | 5 | 0,0 ms: `PRAGMA foreign_keys = 1;` |
| Denetim kaydı sayımı | 13,0 | 13,5 | 16,9 | 17,0 | 5 | 15,0 ms: `SELECT COUNT(*) FROM audit_logs a WHERE a.company_id = @c;` |
| Denetim: kayıt geçmişi (ForEntity) | 952,7 | 983,8 | 1192,2 | 1213,8 | 5 | 1185,0 ms: `SELECT a.created_at, COALESCE(NULLIF(u.full_name,''), u.username, a.user_id, ''), a.entity_type, a.entity_id |
| Ana sayfa özeti (Dashboard.GetSummary) | 5026,5 | 5263,2 | 7665,6 | 7764,7 | 73 | 2914,0 ms: `SELECT COUNT(*) FROM materials m LEFT JOIN (SELECT material_id, company_id, rtrim(rtrim(printf('%.6f', SUM(C |
| Senkron: CompanyVersion (her /sync/wait ve yoklamada) | 2635,9 | 2773,8 | 5144,1 | 5351,4 | 5 | 5008,0 ms: `SELECT MAX(v) FROM (SELECT MAX(COALESCE(updated_at, created_at)) AS v FROM units WHERE company_id=@c UNION A |
| Senkron: delta snapshot (son 1 saat) | 2648,4 | 2677,1 | 4642,4 | 4788,7 | 178 | 1572,0 ms: `SELECT * FROM fuel_distributions WHERE company_id=@c AND COALESCE(updated_at, created_at) > @since;` |

Ek ölçümler:
- **İlk bakiye hesabı** (`RecomputeBalances`, ölçüm öncesi): 200 bin → 8,7 sn; 300 bin → 15,4 sn. Tekrarlarında 8,3 / 12,1 sn ve
  sırasıyla **184 bin / 276 bin SQL ifadesi** (her bakiye satırı ayrı INSERT). SQLite'ta tüm süre boyunca yazma kilidi tutulur.
- `GetBalance` (tek malzeme) ve "Stok hareketleri ekranı" (`SearchMovements`, 1000 satır tavanı) hızlı (< 20 ms): indeks + `LIMIT`
  doğru kullanılıyor. Denetim listesi/sayımı da hızlı (< 20 ms).

### 4.2 `SearchGridAll` — karesel (O(N²)) büyüme

`SearchGridAll` tümünü 500'lük sayfalar hâlinde, **her sayfada yeniden COUNT + OFFSET** ile çeker
(`MaterialService.cs:919-931`, `DailyActivityService.cs:519-531`). Malzeme ve günlük faaliyet ekranları bunu **her açılışta** arka planda
filtre seçenekleri için çalıştırır (`MaterialsViewModel.cs:159`, `DailyActivityViewModel.cs:177`); Excel ve yazdır da aynı yoldan gider.

| Satır sayısı | Malzeme `SearchGridAll` | Günlük faaliyet `SearchGridAll` |
|---:|---:|---:|
| 10.000 | 0,9 sn | 1,0 sn |
| 20.000 | 3,7 sn | 7,6 sn |
| 40.000 | 23,1 sn | 50,0 sn |
| 200.000 | ölçüm 15 dakikada bitmedi, durduruldu; tahmin 400 sayfa × ~3,3 sn ≈ **22 dk** | tahmin 400 × ~4,9 sn ≈ **33 dk** |
| 300.000 | tahmin 600 × ~5,2 sn ≈ **52 dk** | tahmin 600 × ~7,8 sn ≈ **78 dk** |

### 4.3 Senkron ölçümleri (300 bin)

| İşlem | Süre | Boyut / not |
|---|---:|---|
| `CompanyVersion` (sunucu her `/sync/wait` ve yoklamada çalıştırır) | 200 bin: 2,6 sn · 300 bin: **5,1 sn** | ~58 tabloda `MAX(COALESCE(updated_at, created_at))`; indeks yok → tam tarama |
| Delta paket, son 1 saat (`BuildSnapshot since=…`) | 200 bin: 2,6 sn · 300 bin: **4,6 sn** | Değişen satır olmasa da her tablo taranıyor (178 ifade) |
| **Tam paket** üretimi (`business-pull since=0`) | **24,8 sn** | JSON **567 MB**; süreç bellek tepesi **≈ 4,9 GB** (sunucu makinesi 512 MB) |
| Aynı paketin gzip (en hızlı) hâli | 4,3 sn | 102 MB (masaüstü gzip istemediği için bugün kullanılmıyor, P12) |
| **İlk eşitleme** — tam paketi boş makineye uygulama (`ApplyPull`, bakiye hesabı dahil) | **173 sn** | 1.201.060 satır; bellek tepesi ≈ 6,1 GB; tek transaction, devam ettirilemez |
| Aynı tam paketi **ikinci kez** uygulama | **132 sn** | İmleç hatası (R1-b) nedeniyle bugün **her senkron turunda** yaşanan iş |

> Not: 300 sn HTTP zaman aşımı (`BusinessSyncPullService.cs:53`) ile 567 MB'lık yanıt + 173 sn uygulama birlikte, ev internetinde
> ilk eşitlemenin **tamamlanamayacağını** gösteriyor. 512 MB'lık API makinesi 4,9 GB bellek isteyen tam paketi üretemez (bellek
> yetersizliğinden çöker). Bu ölçüm masaüstü dizüstünde yapıldı; sunucuda PG'den okuma ek gecikme getirir.

### 4.4 Sorgu planları ve kök nedenler (300 bin, `EXPLAIN QUERY PLAN`)

| Sorgu | Plan özeti | Kök neden |
|---|---|---|
| Malzeme listesi COUNT + sayfa | `MATERIALIZE sb → SCAN stock_balances` + `SEARCH m USING INDEX ix_materials_company` + `AUTOMATIC COVERING INDEX` | `{STOCK_TOTALS}` alt sorgusu her seferinde **tüm** `stock_balances`'ı GROUP BY ile toplar ve geçici indeks kurar; COUNT, kullanılmayan uyumlu-araç/eşdeğer alt sorgularıyla birlikte tüm satırları üretir; sıralama `ORDER BY t.code` dış sorguda olduğundan `ux_materials_code` kullanılamaz → 300 bin satır sıralanıp `OFFSET` atlanır |
| Stok hareketleri COUNT | `SEARCH sm USING INDEX ix_stock_movements_company` + 5 JOIN | Filtre olmadan da sayım 5 LEFT JOIN ile yapılıyor; tarih filtresi `COALESCE(d.doc_date, sm.created_at)` ifadesi olduğundan indeks aralığı kullanılamıyor ("son 30 gün" bile 1,75 sn) |
| Günlük faaliyet sayfası | `MATERIALIZE mmq/mmt (maintenance_materials)` + `SEARCH da USING INDEX ux_daily_activities_op (company_id=?)` | Firma süzmesi için tarih indeksi değil **operation_id tekil indeksi** seçiliyor (ANALYZE yok); bakım malzemesi alt sorguları her sayfa için yeniden üretiliyor |
| Yakıt listesi | `SEARCH fd USING INDEX ux_fuel_dist_op (company_id=?)` | Aynı: `ix_fuel_dist_company(company_id, distribution_date)` yerine operation_id indeksi seçiliyor → `ORDER BY distribution_date` için tam sıralama |
| Denetim kayıt geçmişi (`ForEntity`) | ANALYZE yok: `ix_audit_company_time` → **1.721 ms**; ANALYZE sonrası: `ix_audit_entity` → **0 ms** | Planlayıcı istatistiksiz yanlış indeksi seçiyor |
| Malzemenin son hareketleri (`RecentForMaterial`) | ANALYZE yok: `ix_stock_movements_company` → **1.715 ms**; ANALYZE sonrası: `ix_stock_movements_material` → **0 ms** | Aynı (P18'in etkisi doğrudan ölçüldü) |
| `CompanyVersion` | Her tablo için `SEARCH ... (company_id=?)` sonra satır satır `COALESCE` | `(company_id, updated_at)` indeksi yok; `COALESCE` ifadesi indekslenemez |
| `RecomputeBalances` | `DELETE FROM stock_balances WHERE company_id=?` (1,6 sn) + 276 bin tekil INSERT | Artımlı değil; toplu yazım yok |

Ayrıntılı plan çıktısı (servisin ürettiği SQL ile):

- **Günlük faaliyet SON sayfa** — 7118 ms · SQL: `SELECT * FROM ( SELECT da.id AS id, da.activity_date AS date_raw, CASE da.activity_type WHEN 'maintenance' THEN 'Bakım' WHEN 'extra_oil' THEN 'İlave Y…`
  - Plan: MATERIALIZE mmq → SCAN maintenance_materials USING INDEX ix_maintenance_materials → MATERIALIZE mmt → SCAN mm2 USING INDEX ix_maintenance_materials → SEARCH m2 USING INDEX sqlite_autoindex_materials_1 (id=?) → SEARCH da USING INDEX ux_daily_activities_op (company_id=?) → SEARCH v USING INDEX sqlite_autoindex_vehicles_1 (id=?) LEFT-JOIN → SEARCH fb USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN
- **Malzeme listesi s.1 (SearchGrid 25)** — 5469 ms · SQL: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALESCE(mc.name,'') AS category, COALESCE(u.na…`
  - Plan: MATERIALIZE sb → SCAN stock_balances USING INDEX sqlite_autoindex_stock_balances_1 → SEARCH m USING INDEX ix_materials_company (company_id=? AND is_deleted=?) → SEARCH mc USING COVERING INDEX sqlite_autoindex_material_categories_1 (id=?) LEFT-JOIN → SEARCH u USING COVERING INDEX sqlite_autoindex_units_1 (id=?) LEFT-JOIN → SEARCH b USING COVERING INDEX sqlite_autoindex_brands_1 (id=?) LEFT-JOIN → SEARCH sup USING COVERING INDEX sqlite_autoindex_suppliers_1 (id=?) LEFT-JOIN → SEARCH sb USING AUTOMATIC COVERING INDEX (material_id=? AND company_id=?) LEFT-JOIN
- **Senkron: CompanyVersion (her /sync/wait ve yoklamada)** — 5008 ms · SQL: `SELECT MAX(v) FROM (SELECT MAX(COALESCE(updated_at, created_at)) AS v FROM units WHERE company_id=@c UNION ALL SELECT MAX(COALESCE(updated_at, created…`
  - Plan: CO-ROUTINE surum → COMPOUND QUERY → LEFT-MOST SUBQUERY → SEARCH units USING INDEX ux_units (company_id=?) → UNION ALL → SEARCH suppliers USING INDEX ux_suppliers (company_id=?) → UNION ALL → SEARCH brands USING INDEX ux_brands (company_id=?)
- **Stok hareketleri metin araması 'filtre'** — 4694 ms · SQL: `SELECT COUNT(*) FROM stock_movements sm JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.company_id LEFT JOIN units u ON u.id = m.unit_…`
  - Plan: SEARCH sm USING INDEX ix_stock_movements_company (company_id=?) → SEARCH m USING INDEX sqlite_autoindex_materials_1 (id=?) → SEARCH u USING COVERING INDEX sqlite_autoindex_units_1 (id=?) LEFT-JOIN → SEARCH d USING INDEX sqlite_autoindex_stock_documents_1 (id=?) LEFT-JOIN → SEARCH bl USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN → SEARCH bf USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN
- **Stok hareketleri SON sayfa (derin OFFSET)** — 4011 ms · SQL: `SELECT COALESCE(d.doc_date, sm.created_at), sm.movement_type, m.code, m.name, COALESCE(u.name,''), sm.direction, sm.quantity, sm.unit_price, sm.note, …`
  - Plan: SEARCH sm USING INDEX ix_stock_movements_company (company_id=?) → SEARCH m USING INDEX sqlite_autoindex_materials_1 (id=?) → SEARCH u USING INDEX sqlite_autoindex_units_1 (id=?) LEFT-JOIN → SEARCH d USING INDEX sqlite_autoindex_stock_documents_1 (id=?) LEFT-JOIN → SEARCH bl USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN → SEARCH bf USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN
- **Stok hareketleri s.1 (SearchMovementsGrid 50)** — 3945 ms · SQL: `SELECT COUNT(*) FROM stock_movements sm JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.company_id LEFT JOIN units u ON u.id = m.unit_…`
  - Plan: SEARCH sm USING INDEX ix_stock_movements_company (company_id=?) → SEARCH m USING INDEX sqlite_autoindex_materials_1 (id=?) → SEARCH u USING COVERING INDEX sqlite_autoindex_units_1 (id=?) LEFT-JOIN → SEARCH d USING COVERING INDEX sqlite_autoindex_stock_documents_1 (id=?) LEFT-JOIN → SEARCH bl USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN → SEARCH bf USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN
- **Malzeme listesi ad ile ara 'rulman'** — 3288 ms · SQL: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALESCE(mc.name,'') AS category, COALESCE(u.na…`
  - Plan: MATERIALIZE sb → SCAN stock_balances USING INDEX sqlite_autoindex_stock_balances_1 → SEARCH m USING INDEX ix_materials_company (company_id=? AND is_deleted=?) → SEARCH mc USING COVERING INDEX sqlite_autoindex_material_categories_1 (id=?) LEFT-JOIN → SEARCH u USING COVERING INDEX sqlite_autoindex_units_1 (id=?) LEFT-JOIN → SEARCH b USING COVERING INDEX sqlite_autoindex_brands_1 (id=?) LEFT-JOIN → SEARCH sup USING COVERING INDEX sqlite_autoindex_suppliers_1 (id=?) LEFT-JOIN → SEARCH sb USING AUTOMATIC COVERING INDEX (material_id=? AND company_id=?) LEFT-JOIN
- **Malzeme listesi ada göre sırala** — 3235 ms · SQL: `SELECT * FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALESCE(mc.name,'') AS category, COALESCE(u.name,'') …`
  - Plan: MATERIALIZE sb → SCAN stock_balances USING INDEX sqlite_autoindex_stock_balances_1 → SEARCH m USING INDEX ix_materials_company (company_id=? AND is_deleted=?) → SEARCH mc USING INDEX sqlite_autoindex_material_categories_1 (id=?) LEFT-JOIN → SEARCH u USING INDEX sqlite_autoindex_units_1 (id=?) LEFT-JOIN → SEARCH b USING INDEX sqlite_autoindex_brands_1 (id=?) LEFT-JOIN → SEARCH sup USING INDEX sqlite_autoindex_suppliers_1 (id=?) LEFT-JOIN → SEARCH sb USING AUTOMATIC COVERING INDEX (material_id=? AND company_id=?) LEFT-JOIN
- **Malzeme kritik stok filtresi** — 3102 ms · SQL: `SELECT COUNT(*) FROM ( SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type, COALESCE(mc.name,'') AS category, COALESCE(u.na…`
  - Plan: MATERIALIZE sb → SCAN stock_balances USING INDEX sqlite_autoindex_stock_balances_1 → SEARCH m USING INDEX ix_materials_company (company_id=? AND is_deleted=?) → SEARCH mc USING COVERING INDEX sqlite_autoindex_material_categories_1 (id=?) LEFT-JOIN → SEARCH u USING COVERING INDEX sqlite_autoindex_units_1 (id=?) LEFT-JOIN → SEARCH b USING COVERING INDEX sqlite_autoindex_brands_1 (id=?) LEFT-JOIN → SEARCH sup USING COVERING INDEX sqlite_autoindex_suppliers_1 (id=?) LEFT-JOIN → SEARCH sb USING AUTOMATIC COVERING INDEX (material_id=? AND company_id=?) LEFT-JOIN
- **Rapor: Stok Durumu (stock)** — 3102 ms · SQL: `SELECT m.code, m.name, COALESCE(b.quantity,'0') AS qty, m.min_stock FROM materials m LEFT JOIN (SELECT material_id, company_id, rtrim(rtrim(printf('%.…`
  - Plan: MATERIALIZE b → SCAN stock_balances USING INDEX sqlite_autoindex_stock_balances_1 → SEARCH m USING INDEX ux_materials_code (company_id=?) → SEARCH b USING AUTOMATIC COVERING INDEX (material_id=? AND company_id=?) LEFT-JOIN
- **Ana sayfa özeti (Dashboard.GetSummary)** — 2914 ms · SQL: `SELECT COUNT(*) FROM materials m LEFT JOIN (SELECT material_id, company_id, rtrim(rtrim(printf('%.6f', SUM(CAST(quantity AS REAL))), '0'), '.') AS qua…`
  - Plan: MATERIALIZE b → SCAN stock_balances USING INDEX sqlite_autoindex_stock_balances_1 → SEARCH m USING INDEX ix_materials_company (company_id=? AND is_deleted=?) → SEARCH b USING AUTOMATIC COVERING INDEX (material_id=? AND company_id=?) LEFT-JOIN
- **Bakiye yeniden hesap (RecomputeBalances, tüm firma)** — 2497 ms · SQL: `DELETE FROM stock_balances WHERE company_id=@c;`
  - Plan: SEARCH stock_balances USING COVERING INDEX ix_stock_balances_location (company_id=?)
- **Rapor: Günlük faaliyet 1 yıl** — 2323 ms · SQL: `SELECT da.activity_date, da.activity_type, COALESCE(da.movement_kind,'') AS kind, COALESCE(ob.name,'') AS branch_text, COALESCE(v.internal_code,'') AS…`
  - Plan: MATERIALIZE mm → SCAN maintenance_materials USING INDEX ix_maintenance_materials → SEARCH da USING INDEX ux_daily_activities_op (company_id=?) → SEARCH v USING INDEX sqlite_autoindex_vehicles_1 (id=?) LEFT-JOIN → SEARCH fb USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN → SEARCH tb USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN → SEARCH ob USING INDEX sqlite_autoindex_branches_1 (id=?) LEFT-JOIN → SEARCH p USING INDEX sqlite_autoindex_personnel_1 (id=?) LEFT-JOIN
- **Yakıt listesi araç araması '34 AB 01'** — 1853 ms · SQL: `SELECT COUNT(*) FROM fuel_distributions fd LEFT JOIN vehicles v ON v.id = fd.vehicle_id WHERE fd.company_id=@c AND fd.is_deleted=0 AND (v.internal_cod…`
  - Plan: SEARCH fd USING INDEX ux_fuel_dist_op (company_id=?) → SEARCH v USING INDEX sqlite_autoindex_vehicles_1 (id=?) LEFT-JOIN

### 4.5 En yavaş 10 iş ve somut öneriler (uygulanmadı)

| # | İş (300 bin medyan) | Öneri (indeks / sorgu) |
|---|---|---|
| 1 | Tam paket + yeniden uygulama her turda (24,8 sn üretim + 132 sn uygulama, 567 MB) | İmleç atamasını düzelt (P3) + sunucu sırası (P2) + gzip (P12) + sayfalı çekme (P11) |
| 2 | `SearchGridAll` (tahmini 52–78 dk) | Tek geçişli sorgu; filtre seçenekleri için `SELECT DISTINCT` (P4) |
| 3 | `RecomputeBalances` 12,1 sn, yazma kilidiyle | Artımlı hesap; toplu `INSERT … SELECT` ya da `ON CONFLICT` ile tek ifade; push başına değil "etkilenen malzeme" başına (P6) |
| 4 | Malzeme listesi ada göre sırala / son sayfa ≈ 10,4 sn | Sayfa kimliklerini önce seç: `SELECT id FROM materials WHERE company_id=? AND is_deleted=0 ORDER BY code LIMIT ? OFFSET ?` (`ux_materials_code` kullanır), sonra 25 satır için JOIN; `stock_balances`'tan `SUM` yalnız `material_id IN (...)`; ad sıralaması için `CREATE INDEX ix_materials_company_name ON materials(company_id, is_deleted, name)`; derin sayfada keyset |
| 5 | Günlük faaliyet son sayfa 8,9 sn | `CREATE INDEX ix_daily_activities_company_date ON daily_activities(company_id, is_deleted, activity_date DESC)`; malzeme alt sorgularını sayfa kimlikleriyle sınırla |
| 6 | Stok hareketleri son sayfa 7,9 sn / ilk sayfa 3,9 sn | COUNT'u yalın yap (`SELECT COUNT(*) FROM stock_movements WHERE company_id=?` + yalnız gerçekten süzülen JOIN'ler); keyset `(created_at, rowid) < (?, ?)`; tarih filtresi için belge tarihini harekete yaz (`movement_date` kolonu) ve `(company_id, movement_date)` indeksi |
| 7 | Ana sayfa özeti 7,7 sn (73 ifade) | Stok toplamlarını `stock_balances` üzerinde `(company_id, material_id)` gruplu tek geçişle; düşük stok sayısını önbellekle / bakiye yazılırken güncelle |
| 8 | Malzeme listesi ilk sayfa 7,8 sn | #4 ile aynı; COUNT'ta türetilmiş kolonları üretme (filtre yoksa yalın sayım) |
| 9 | `CompanyVersion` 5,1 sn, delta paket 4,6 sn | Firma başına `sync_version` sayacı (tek satır okuma); delta için her tabloda `(company_id, updated_at)` indeksi + `updated_at` NOT NULL (yeni kolonla, eklemeli migration) |
| 10 | Arama (malzeme 6,4 sn, stok hareketi 4,8 sn, yakıt araç 1,9 sn) | `like()` geri çağrısında `Regex` her satırda yeniden kuruluyor → önbellek/`IndexOf`; uzun vadede FTS5 trigram |
| + | Hızlı kazanç: `ANALYZE` / `PRAGMA optimize` | Ölçüldü: iki sorgu 1,7 sn → 0 ms. Açılışta `PRAGMA optimize` (P18) |

**OFFSET ↔ keyset:** derin sayfa ilk sayfanın 1,3–2,7 katı (malzeme 7,8 → 10,3 sn; stok hareketi 3,9 → 7,9 sn; günlük faaliyet
3,3 → 8,9 sn; yakıt 1,3 → 2,7 sn). Asıl maliyet ise OFFSET'ten önce **COUNT ve tüm sonuç kümesinin üretilmesi**; keyset tek başına
yetmez, önce sayım/alt sorgu düzeltilmeli.

**PostgreSQL:** şema karşılaştırması yapıldı (bölüm 1). PG üzerinde 300 bin satırlık zamanlama ölçümü bu turda **yapılmadı**
(satır satır kopyalayıcı 1,2 milyon satırda çok uzun sürerdi). Aynı sorgu şekilleri (COUNT alt sorgusu, `COALESCE` damgası, tam
defter okuyan bakiye hesabı) PG'de de geçerli; PG'de ek olarak her satır ağ/çıkış trafiğidir. Sonraki adım: `COPY` ile toplu
yükleme + `EXPLAIN (ANALYZE, BUFFERS)` (araç hazır: `pgbench measure`).

## 5. Önceliklendirilmiş sorun listesi

Boyut: S = küçük (≤1 gün), M = orta (2–5 gün), L = büyük (1–3 hafta). Hiçbiri bu çalışmada uygulanmadı.
Senkron protokolü / migration değişiklikleri kullanıcı onayı ister (`.claude/rules/gelistirme-protokolu.md`).

| # | Önem | Sorun | Neden önemli | Düzeltme fikri | Boyut | Kabul ölçütü |
|---|---|---|---|---|---|---|
| P1 | **Kritik (veri kaybı)** | Çevrimdışı makinelerde aynı doğal anahtar (malzeme kodu, belge/fatura/sipariş/iş emri no `MAX+1`) → sunucuda UNIQUE ihlali → satır ve çocukları **kalıcı atlanır**, kullanıcı görmez (K1) | Fatura/stok belgesi sunucuya hiç ulaşmaz; başlıksız stok hareketi kalır | `SENKRON_ID_KOD.md` §2 makine kodu önekini uygula (belge/fatura/sipariş no); malzeme/araç kodu çakışmasında "çakışma" kaydı + kullanıcıya kod değiştirme isteği; kalıcı atlanan satırları yerelde `sync_rejections` tablosunda tut | M | İki çevrimdışı istemci aynı gün belge açar → ikisi de sunucuda ayrı numarayla bulunur; zorla çakıştırılan malzeme kodu masaüstünde "reddedildi + neden" olarak görünür |
| P2 | **Kritik (veri kaybı, gizli)** | Çekme imleci istemci saatli `updated_at`'e dayanıyor; geç gelen/geri damgalı satırlar imlecin gerisinde kalır (R1-a) | İmleç düzeltildiği an çok makineli firmada satırlar sessizce kaybolur | Sunucuda **sunucu atamalı artan sıra** (`server_seq`, push sırasında sunucu yazar) ve imleci ona bağla; ara çözüm: örtüşme penceresi (`since - 10 dk`) + idempotent upsert | M–L | Test: B'nin imleci T+1 iken A, T damgalı satır gönderir → B bir sonraki turda alır |
| P3 | **Yüksek (performans/maliyet)** | `AlinanWatermark` hiç atanmıyor → imleç ilerlemiyor → her turda (60 sn + her değişiklik sinyalinde) **tam paket** çekiliyor (R1-b) | 300 bin satırda tur başına yüzlerce MB JSON, sunucu RAM'i 512 MB, Supabase çıkış trafiği; masaüstünde her turda tüm defterin yeniden hesaplanması | P2 ile **birlikte**: `PullAsync` içinde `AlinanWatermark = AlinanEnBuyukDamga(doc.RootElement)` (ya da `server_seq`) ata; tek başına uygulanmamalı | S (P2 ile) | Boş turda çekilen satır = 0; 1 değişiklikte paket ≈ 1 satır |
| P4 | **Yüksek (performans)** | `SearchGridAll` 500'lük sayfaları baştan sayarak (COUNT + OFFSET) döngüyle çekiyor → O(N²): 40 bin malzemede 23 sn, 200 bin'de ~20+ dk; malzeme ve günlük faaliyet ekranı **her açılışta** filtre seçenekleri için bunu arka planda çalıştırıyor (`MaterialsViewModel.cs:159`, `DailyActivityViewModel.cs:177`) | Ekran açılışı CPU'yu dakikalarca yer; Excel/yazdır da aynı yolu kullanır | Tek sorguda (sayfasız) oku; filtre seçeneklerini `SELECT DISTINCT kolon` ile ayrı al | S | 300 bin satırda filtre seçenekleri < 1 sn; SearchGridAll tek geçiş |
| P5 | **Yüksek (performans)** | Liste ekranlarının her sayfası tüm firmayı tarayan `COUNT(*) FROM (alt sorgu)` + `OFFSET` kullanıyor; malzeme listesi 200 bin'de ilk sayfa ~3,2 sn, son sayfa ~4,6 sn; stok hareketleri ~2,8 / 5,6 sn; günlük faaliyet ~2,4 / 5,6 sn | Kullanıcı her sayfa/sıralama/filtre değişiminde saniyelerce bekler | COUNT için filtre yoksa yalın `SELECT COUNT(*) FROM tablo WHERE company_id=? AND is_deleted=0`; gereksiz LEFT JOIN'leri sayımdan çıkar; sayfa için önce kimlikleri indeksle seç (`ORDER BY code LIMIT`), türetilmiş kolonları (stok toplamı, uyumlu araç, eşdeğer) yalnız o 25 satır için hesapla; derin sayfalarda keyset (`(created_at,id) < (?,?)`) | M | 300 bin satırda ilk sayfa < 150 ms, son sayfa < 300 ms |
| P6 | **Yüksek (performans + kilit)** | `RecomputeBalances` tüm defteri okuyup tüm bakiyeleri siler/yazar: 200 bin harekette ~8,3 sn, 184 bin SQL ifadesi; SQLite'ta `BEGIN IMMEDIATE` ile **yazma kilidi** süresince tutulur. Sunucuda **her push**, masaüstünde her satır uygulayan çekmeden sonra çalışıyor | Kilit süresi `busy_timeout=5000`'i aşar → kullanıcının o sırada yaptığı kayıt "database is locked" alabilir; sunucuda her push'ta tüm defter Supabase'den okunur | Artımlı hesap: yalnız paketle gelen `(material_id, branch_id)` çiftlerini yeniden topla; tam hesabı gece/elle | M | 300 bin harekette 1 satırlık push sonrası bakiye güncellemesi < 50 ms; tam hesap sırasında kayıt kilitlenmez |
| P7 | **Yüksek (veri kaybı)** | Kalıcı atlanan ve poison satırlar bir daha gönderilmiyor; yalnız `sync.log` (R2, R3, K2) | Kullanıcı verinin sunucuda olduğunu sanır | Yerel "eşitleme reddi" listesi (kayıt, neden, tarih) + masaüstü ekranı; kullanıcı düzeltince yeniden gönder; poison'da watermark'ı ilerletirken atlanan satır kimliklerini sakla | M | Reddedilen her satır ekranda görünür, düzeltme sonrası sunucuya ulaşır |
| P8 | **Yüksek (iş kuralı)** | İki çevrimdışı çıkış sunucuda negatif bakiyeye yol açıyor, kimse uyarılmıyor (R5) | Stok doğruluğu | Push sonrası negatife düşen (malzeme, lokasyon) için `data_conflicts` benzeri "negatif stok uyarısı" + iki tarafa bildirim; satırı reddetme (defter gerçeği) | S–M | Senaryo testi: iki cihaz 10'ar çıkış → sunucuda uyarı kaydı ve masaüstü bildirimi |
| P9 | **Yüksek (gizlilik)** | Şube/yetki daraldığında yerel SQLite'ta kapsam dışı veri kalıyor; aynı PC'yi farklı yetkili kullanıcılar paylaşıyor (R4) | Veri sızıntısı | Girişte kapsam imzası değişirse kapsam dışı satırları sil ya da tam yeniden çek; kullanıcı değişince kapsam kontrolü | M | Kapsamı daraltılan kullanıcı sonraki girişte kapsam dışı satırı yerelde göremez (SQL ile doğrulanır) |
| P10 | **Orta (performans)** | `CompanyVersion` her `/sync/wait`/yoklamada ~58 tabloda `MAX(COALESCE(updated_at, created_at))` → indeks yok, tam tarama (200 bin'de 2,6 sn SQLite) | Sunucuda her istemci yoklaması DB'yi tarar; çok makinede çarpılır | Firma başına `company_sync_version` tek satırlık sayaç (push/web yazmasında artır) ya da `(company_id, updated_at)` indeksleri + `COALESCE` yerine NOT NULL `updated_at` | S–M | CompanyVersion < 5 ms (300 bin satır) |
| P11 | **Orta** | Tam ilk eşitleme tek JSON, tek transaction, sayfasız, devam ettirilemez (R6) | Kopan bağlantıda baştan; bellek tepe değeri yüksek (bölüm 4.3) | Tablo + sıra numarasıyla sayfalı çekme (ör. 5.000 satır), her sayfadan sonra kalıcı imleç, ilerleme çubuğu | M | 300 bin satırlık kurulum yarıda kesilip yeniden başlatılınca kaldığı sayfadan devam eder |
| P12 | **Orta** | Masaüstü `HttpClient` gzip istemiyor (`AutomaticDecompression` yok) → sunucudaki sıkıştırma (SNK-08) masaüstünde **hiç devreye girmiyor** (`BusinessSyncPullService.cs:53`, `BusinessSyncPushService.cs:22`) | Tam paket ~5–8 kat büyük gidiyor | `new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip \| DecompressionMethods.Brotli })` | S | Yanıt başlığında `Content-Encoding: gzip`; aktarılan bayt ölçümle düşer |
| P13 | **Orta** | Sessiz LWW: yakıt, günlük faaliyet, fatura, kasa, iş emri düzenlemelerinde çakışma kaydı yok (K8); istemci saati LWW'yi belirliyor (K4) | CLAUDE.md §3 "yakıt/bakımda LWW yasak" kuralına aykırı | `ConflictTracked` listesini genişlet; `updated_at`'i sunucuda yeniden bas veya sürüm (`version`) karşılaştırması | M | İki tarafta aynı yakıt kaydı düzenlenir → çakışma ekranında görünür |
| P14 | **Orta** | "Yereli sıfırla" önce göndermeden yerel veriyi siliyor (K7) | Gönderilmemiş kayıtlar kaybolur | Sıfırlamadan önce push; bekleyen varsa uyar ve onay iste | S | Bekleyen değişiklik varken sıfırlama önce gönderir ya da açık uyarı verir |
| P15 | **Orta** | Sunucu yedekten dönünce masaüstleri yedek sonrası kayıtları yeniden göndermiyor (K6); RPO 7 gün | Felaket kurtarmada veri kaybı | Sunucuya "veri dönemi" (epoch) kimliği ekle; değişince masaüstü watermark'ı sıfırlayıp tam gönderim yapsın; günlük yedek | S–M | Geri yükleme provasında yedek sonrası masaüstü kayıtları sunucuya geri gelir |
| P16 | **Orta** | Sonradan senkrona eklenen tabloların eski yerel satırları gönderilmemiş olabilir (K5) | Sessiz eksik veri | Tablo bazlı watermark ya da yeni tablo eklendiğinde o tablo için tek seferlik tam gönderim | S | Yeni tablo eklenen sürümde o tablonun tüm yerel satırları bir kez gider |
| P17 | **Orta (performans)** | SQLite `like()` C# + Regex geri çağrısı; `%…%` aramalar indeks kullanamıyor (malzeme ad araması ~3 sn, stok hareket araması ~3,5 sn / 200 bin) | Arama yavaş | Kısa vadede Regex'i önbelleğe al/`IndexOf` ile değiştir; orta vadede FTS5 trigram (SQLite) / pg_trgm (PG) | S / M | 300 bin satırda ad araması < 300 ms |
| P18 | **Orta (hızlı kazanç)** | Uygulama `ANALYZE`/`PRAGMA optimize`/`wal_checkpoint(TRUNCATE)` çalıştırmıyor | Planlayıcı yanlış indeks seçiyor (ölçüldü: denetim geçmişi ve malzeme son hareketleri 1,7 sn → 0 ms); WAL büyüyebilir | Açılışta ve boşta `PRAGMA optimize`; günlük yedekten sonra checkpoint | S | `sqlite_stat1` dolu; WAL dosyası boşta küçülür |
| P19 | **Düşük** | Para/miktar `TEXT`, sayısal geçerlilik CHECK'i yok | Bozuk metin PG raporlarını düşürebilir | Yazma yolunda `Money` doğrulaması zaten var; senkron `ValidateRow`'a sayısal biçim kontrolü ekle | S | Bozuk miktarlı satır push'ta "kalıcı red + neden" alır |

## 6. Ölçüm nasıl tekrarlanır

Ölçüm kodu depo DIŞINDA tutuldu (oturum karalama klasörü `scratchpad/perf`); `src/` değişmedi. Yeniden üretmek için:

1. `src/DepoWise.Domain`, `DepoWise.Application`, `DepoWise.Infrastructure` klasörlerini (bin/obj hariç) geçici bir klasöre kopyalayın
   (depodaki `bin/obj`'a dokunmamak ve çalışan testlerle çakışmamak için).
2. Yanına bir konsol uygulaması (`bench.csproj`, net8.0, `ProjectReference → DepoWise.Infrastructure`) açın. Uygulama:
   - `migrate <db>` — gerçek `MigrationRunner` ile 98 migration'ı boş SQLite dosyasına uygular.
   - `seed <db> <N>` — tek firma (`cmp-perf`), 6 şube, 500 araç, 300 personel, 10 birim/50 kategori/100 marka/100 tedarikçi ve
     **N** satır `materials`, `stock_movements`, `daily_activities`, `fuel_distributions`, `audit_logs` üretir (3 yıla yayılmış rastgele
     tarih, sabit tohum 42; stok hareketlerinin %10'unda `updated_at` NULL — `Migration069` öncesi satırları taklit eder).
   - `run <db> <N> <çıktı>` — önce `StockService.RecomputeBalances`, sonra her senaryoda **gerçek servis metodunu** (ör.
     `MaterialService.SearchGrid`, `StockService.SearchMovementsGrid`, `ReportService.Run("stock-movements")`) 1 ısınma + 9 ölçüm
     (ağır senaryolarda 3) çalıştırır; medyan ve p95 (9 ölçümde ≈ en kötü) yazar. Her bağlantıya `sqlite3_profile` kancası takılır
     → servisin çalıştırdığı her SQL ifadesi ve süresi yakalanır; en yavaş ifadeler için `EXPLAIN QUERY PLAN` alınır. Yani ölçülen
     SQL, servisin ürettiği SQL'in kendisidir (elle kopyalanmadı).
   - `gridall <db> <çıktı>` — `SearchGridAll`'ı (ekran açılışında filtre seçenekleri, Excel, yazdır) tek sefer ölçer.
   - `sync <db> <boşDb> <çıktı>` — sunucunun `business-pull?since=0` ürettiği tam paketi (`BuildSnapshot`) üretir, boyutunu ve gzip
     boyutunu ölçer, yeni bir makineyi taklit eden boş DB'ye `ApplyPull` ile uygular, sonra **aynı paketi ikinci kez** uygular
     (imleç ilerlemediğinde her turda olan iş).
3. PostgreSQL için ayrı konsol (`pgbench`, Npgsql 8.0.5): `create` (yerel `localhost:54329` üzerinde geçici `dw_perf`), `migrate`,
   `dump` (şema karşılaştırması için), `copy <sqlite>` (`SqliteToPgCopier` ile), `measure`, `drop`. Bağlantı dizesi
   `.env.pgtest.local`'dan ortam değişkenine okunur, ekrana yazılmaz; araç `localhost` dışına bağlanmayı reddeder.
4. Şema karşılaştırması: `pgbench dump` çıktısı + SQLite `PRAGMA table_xinfo/index_list/foreign_key_list` (Python betiği `cmp.py`).

Ortam: Windows 11, Intel i7-1165G7 (4 çekirdek), 16 GB RAM, NVMe SSD, .NET 8.0.422, Microsoft.Data.Sqlite 10.0.9 (SQLite 3.4x),
PostgreSQL 17 (yerel test sunucusu). Tüm geçici veritabanları ve `dw_perf` iş bitince silindi.

## 7. Canlı ölçüm (2026-10-13, salt okuma) — imleç hatasının bugünkü gerçek etkisi
**Yöntem:** üretim DB'de yalnız `SELECT` (oturum `default_transaction_read_only=on`), `flyctl logs` (son 100 satır, salt okuma).
Hiçbir şey değiştirilmedi. Sorgular: oturum scratchpad'i `canli_imlec*.sql` (bağlantısız metin aşağıda özetlendi).

| Ölçüm | Sonuç |
|---|---|
| Üretim DB toplam boyutu | 31 MB |
| Oze İnşaat eşitlenen veri (ham, 13 ana tablo) | ≈ 1,9 MB (yakıt 1.469, stok hareketi 1.315, sayaç 1.266, belge 689, malzeme 555, bakım 491, günlük 439, araç 92, personel 126 satır) |
| Tahmini tam paket (JSON, alan adları tekrarlı) | ≈ 4–5 MB (çıkarım: ham × 2–2,5) |
| API günlüğü (35 dk, 16:28–17:02 UTC) | 6 `business-version` → **her birini** bir `business-pull` izliyor; pull 500–1.499 ms, version 22–34 ms |
| `pg_stat_statements` | eklenti kurulu değil → sorgu sayacı alınamadı |

**Değerlendirme:** pull süresi (0,5–1,5 sn) küçük bir delta ile değil büyük paketle uyumlu → masaüstleri her turda imleçten
bugüne kadarki (imleç hiç yazılmadıysa tamamını) yeniden çekiyor (kodla tutarlı: `AlinanWatermark` atanmıyor,
`ShellViewModel.cs:544-562`). Gözlenen tempo ≈ 10 pull/saat → iş günü ≈ 100 pull × ≈ 2 MB DB okuması ≈ **200 MB/gün**,
**≈ 4–6 GB/ay** veritabanı çıkış trafiği → Supabase ücretsiz ≈ 5 GB/ay sınırına dayanır; veri ve açık masaüstü sayısıyla
doğrusal büyür. Bugün arıza/bellek sorunu yok (günlükte OOM yok; veri küçük). **Kesinleştirmek için:** API günlüğüne
yanıt boyutu + `since` değeri eklenmeli ya da Supabase panelinden aylık egress okunmalı (doğrulanmadı).
**Sonuç:** M-03 (imleç + sunucu sıra numarası, birlikte) ve M-84 (gzip) acil; yalnız imleç atamasını düzeltmek
sessiz veri kaybı başlatır (bkz. §2.1) — ikisi tek iş biriminde yapılmalı.
