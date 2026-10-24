# 10 — Uygulama günlüğü (B fazları): tamamlanan değişiklikler ve test sonuçları

> Her kayıt: madde · ne değişti · kanıt (test) · eski kodda kırmızı mı · commit. Yayın YOK (AR-GE) — istisna: .NET 10 (ayrı dal).

## 2026-10-17

### B2 — .NET 10 geçişi (dal `net10-gecis`, master'dan; ayrı ve erken yayın — kullanıcı kararı)
- Tüm projeler `net10.0`; `Microsoft.AspNetCore.Authentication.JwtBearer`, `Mvc.Testing`, `Microsoft.Data.Sqlite` → 10.0.13;
  Docker `sdk/aspnet:10.0`; CI `10.0.x`. İş mantığı değişmedi.
- **M-07 kapandı:** `dotnet list package --vulnerable --include-transitive` → tüm projeler temiz (SQLitePCLRaw açığı gitti).
- Test: tam takım 4052 geçti / 0 başarısız; worktree'de `.env.pgtest.local` eksik olduğu için atlanan 51 PostgreSQL testi
  dosya kopyalandıktan sonra ayrıca **53/53** geçti; masaüstü arayüz **47/47**; self-contained masaüstü paketi açılış testi
  ("Alpnex — Giriş") geçti. Commit `53c0459`. Yayından hemen önce atlamasız tek tam koşu yapılacak.

### B1 adım 1 — M-02, M-05, M-04 (dal `arge/2026-10-kapsamli`)
| Madde | Değişiklik | Test | Eski kodda |
|---|---|---|---|
| M-02 | `BusinessSyncService`: push'ta satırın KENDİ kimliği başka firmadaysa reddedilir (`RowOwnedByOtherCompany`, kalıcı atlama + mesaj) + upsert `WHERE {t}.company_id = excluded.company_id` ikinci güvence | `TenantLinkTableTests.TNT03` (ezilemez), `TNT03b` (kendi kaydı güncellenir) | TNT03 **kırmızı**, TNT03b yeşil |
| M-05 | Defter tabloları (`stock_movements`, `party_ledger`, `finance_transactions`, `assignment_movements`): push'ta mevcut satırda yalnız `is_reversed` (0→1), `reversal_reason`, `updated_at`, `version` güncellenir; pull etkilenmez | `DefterDegismezlikTests.LDG1–LDG4` | LDG1, LDG3 **kırmızı**; LDG2, LDG4 yeşil |
| M-04 | API: yakıt dağıtım/düzeltme/depo, bakım, ekipman bakımı, günlük hareket/bakım/ilave uçları `operationId`'yi istemciden alır (`Op()`; yoksa eski davranış). Web: `Services/IslemKimligi` — aynı gövde → aynı kimlik, gövde değişti → yeni kimlik, başarıdan sonra yenilenir | `FazKUctanUcaApiTests.UUA6` güncellendi: aynı kimlikle iki HTTP isteği = **1 kayıt**, aynı id; kimliksiz eski istemci = eski davranış | UUA6 **kırmızı** |
- İlgili takım (Tenant, Sync, Fuel, Maintenance, Daily, Ledger, Idempotency + yeni testler): **563/563** geçti.
- Kanıt yöntemi: yeni testler düzeltmesiz son commit'e kopyalanıp koşuldu → tam beklenen 4 test kırmızı, 4 yeşil.

### B1 adım 2 — M-03 çekme imleci: sunucu işlem kimliği ufku (Migration099, şema 99)
- **Sorun:** çekme imleci istemci saatli `updated_at`; "alınan en büyük damga" hiç atanmadığı için imleç ilerlemiyor → her tur
  tam paket (canlıda ≈4–6 GB/ay); geriye damgalı geç satır başka makinede kaçabiliyordu.
- **Değişiklik:** her eşitleme tablosuna `server_txid` + indeks + tetikleyici (PG: `pg_current_xact_id()`; SQLite yalnız sunucu
  rolünde sayaç). `BusinessSyncService.BuildDeltaV2` ufuk = `pg_snapshot_xmin(pg_current_snapshot())`; yeni uç
  `GET /api/sync/business-pull-v2?after=`; masaüstü imleci `sync_pull_txid` (yalnız başarılı uygulamadan sonra ilerler), uç yoksa
  eski uçtan TAM çekim (damga imleci verilmez — SNK-09 kaybı geri gelmesin); 60 sn güvenlik turu da çeker (boş yanıt ucuz);
  ekran yalnız veri indiyse yenilenir; "yereli sıfırla" çekme imleçlerini de temizler. Ölü `AlinanWatermark` kaldırıldı.
  Tasarım `14` §1 uygulanan yönteme göre güncellendi (taslaktaki firma kilidi yerine kilitsiz ufuk).
- **Testler:** `SenkronUfukTests` UFK1–6 (SQLite sunucu rolü: ilk tam/sonra boş, geç satır, değişen ebeveynin çocukları, firma
  kapsamı, masaüstünde tetikleyici etkisiz, her tabloda kolon+tetikleyici), `PostgresSenkronUfukTests` PUF1–3 (gerçek iki
  bağlantı: uzun işlem açıkken sonra başlayıp önce biten işlem onu atlatamaz), `SenkronUfukApiTests` UFA1–2 (HTTP ucu, 401,
  masaüstüne uygulama, eski uç çalışıyor) → **11/11**.
- **Kanıt (mutasyon):** ufuk `xmin` yerine `xmax` yapılınca PUF1, ebeveyn-çocuk koşulu kapatılınca UFK3 kırmızı. UFK2 eski damga
  imlecinin geç satırı kaçırdığını aynı test içinde gösterir.
- **Tam takım:** 4123/4125; yakalanan 2 hata düzeltildi: (1) `(company_id, server_txid)` indeksi Migration062'nin belgelenmiş geri alma adımını bozuyordu → indeks yalnız `server_txid`; (2) SQLite→PG kopyalayıcısı SQLite'a özgü yeni tabloları PG'de arıyordu → hariç listesi. Ek: PG tetikleyicisi değeri değişmeyen güncellemede kimliği korur (PUF4). İlgili 57 test yeniden geçti.

### B1 adım 3 — M-03 gönderme tarafı: yerel değişiklik sayacı + yankı kesme (Migration100, şema 100)
- **Sorun:** gönderme imleci yerel en büyük `updated_at`; saati geri alınan makinede yeni kayıt hiç gönderilmiyordu; çekilen
  satırlar geri gönderiliyordu (yankı; v2 çekmeyle birleşince sunucuda tekrar "değişti" sayılıp herkese yeniden inerdi).
- **Değişiklik:** `local_change_seq` + tetikleyici (yalnız masaüstü), çekme sırasında `sync_apply_guard` ile durur;
  `BusinessSyncService.LocalChangeSeq/BuildPushSnapshot`; `BusinessSyncPushService` imleci `sync_push_seq` + tek seferlik geçiş;
  "yereli sıfırla" bu imleci de temizler; SQLite→PG kopyalayıcısı `local_change_seq`'i atlar.
- **Testler:** `YerelSayacTests` YRL1–7 (saat geri alınmış makine — eski imlecin kaçırdığı aynı testte gösterilir; yankı; ebeveyn-çocuk;
  geçiş; sunucu rolünde etkisiz; her tabloda tetikleyici), `SenkronUfukApiTests.UFA3` (gerçek HTTP gönder → çek → paket boş).
  İlgili 41 test geçti. **Mutasyon:** koruma kapatılınca YRL3+UFA3, ebeveyn koşulu kapatılınca YRL4 kırmızı.
- **Tam takım:** 4134/4134 geçti (atlanan 0).

### B1 adım 4 — M-15 gönderilemeyen kayıtlar, M-79 sıfırlamadan önce gönder, M-80 sunucu veri dönemi (Migration101, şema 101)
- **M-15:** sunucu `ApplyResult.Rejected` ile reddettiği her satırı (tablo, anahtar, neden, kalıcı; en fazla 500; PG kurtarma yolunda
  tekil) döndürür; push yanıtında `rejected`. Masaüstü yerel `sync_rejections` (Migration101, eşitlenmez) — `GonderimRedleriService`:
  kaydet / tekrarda deneme sayısı / kabulde sil / listeden kaldır / yeniden gönder (yerel sayaç). Üst bar rozeti "⚠ N kayıt gönderilemedi",
  tıklanınca yeni **Gönderilemeyen Kayıtlar** penceresi (kayıt adı, neden, ipucu, Yeniden Gönder / Listeden Kaldır). Web'de karşılığı
  yok (web doğrudan sunucuya yazar; reddedilen kayıt formda hata olarak görünür).
- **M-79:** kullanıcının "Yereli Sıfırla"sı önce gönderir; bekleyen/reddedilen kayıt kalırsa sayılı uyarıyla ayrıca onay ister.
  Yöneticinin uzaktan sıfırlama komutu bilinçli olarak göndermez (SIF-02: eski veri gitmesin) — değişmedi.
- **M-80:** v2 çekme yanıtında `epoch` (PG: veritabanı + companies iç kimliği). `SunucuDonemi.Degerlendir`: dönem değişti ya da ufuk
  geriledi → paket uygulanmaz, `sync_push_full` ile TAM yeniden gönderim + tam çekme. Geri yükleme belgesine (§4.4 adım 6) eklendi.
- **Testler:** `GonderimRedleriTests` (RED1–6, DNM1 7 durum, DNM2, SIF1), `PostgresSenkronUfukTests` PUF5 (kurtarma yolu tekil),
  PUF6 (dönem sabit; şema yeniden kurulunca değişir).
- Riskli alan paketi 528/532 + düzeltme sonrası 80/80 (PG dahil). Yakalanan: Migration101 kataloğa eklenmemişti (RED2–4 kırmızı). Mutasyon: kurtarma yolu tekrar temizliği kapatılınca PUF5 kırmızı. Tam takım faz sonunda.

### B1 — M-84 yanıt sıkıştırması gerçekten kullanılıyor
- **Bulgu:** API sıkıştırması (SNK-08) açıktı ama hiçbir istemci `Accept-Encoding` göndermiyordu (düz `HttpClient`) → masaüstü eşitleme paketleri ve web listeleri hep sıkıştırmasız iniyordu.
- **Değişiklik:** `Infrastructure/Net/HttpIstemci` (gzip/Brotli/deflate ister + açar, bağlantı ömrü 10 dk); masaüstündeki 11 istemci buna geçti (süreler aynı); web `HttpClient` aynı işleyiciyle. İstek gövdesi sıkıştırması BİLİNÇLİ eklenmedi (1 GB gövde sınırı + 512 MB sunucu → açılan paket belleği taşırabilir; M-17 parçalı gönderimle ele alınacak).
- **Testler:** `SikistirmaTests` SKS1 (200 malzemelik v2 paketi gzip ile iner, ≥%70 küçülür), SKS2, SKS3 (düz HttpClient kalmadı — kaynak kilidi); `LoginMachineSyncTests.L1` aynı süreleri yeni yazımla denetler. 10/10 + ilgili 31.

### B1 — M-17 sayfalı, kaldığı yerden süren çekme
- **Sorun:** ilk eşitleme tek paket (büyük firmada 567 MB, sunucuda ~4,9 GB bellek → 512 MB makinede çökme); kopunca baştan.
- **Sunucu:** `BuildDeltaV2(limit, sayfa, ufukSabit)` tablo sırası + birincil anahtar (çok kolonlu dahil) ile keyset sayfalama; tüm sayfalar ilk sayfanın ufkuyla (şimdiki ufku aşamaz); sayfaya giren ebeveynin TÜM çocukları aynı sayfada (alıcı arada silmesin). Uç `limit` 100–5000 (varsayılan 2000, her zaman sınırlı), bozuk imleç 400.
- **Masaüstü:** `SayfaliCekim` döngüsü (ağdan bağımsız): sayfa → uygula → ilerlemeyi kalıcı yaz (`sync_pull_page`); kopunca aynı ufukla kaldığı sayfadan; imleç yalnız son sayfadan sonra; bakiye yeniden hesabı sonda bir kez (`ApplyPull(bakiyeHesapla:false)`); her sayfada jeton tazeleme; sıfırlama ilerlemeyi de temizler.
- **Testler:** `SayfaliCekimTests` SAY1–7 (birleşim = tam paket, sınır, aynı ufuk, çocuklar aynı sayfada, arada eklenen kayıt deltada gelir, bozuk imleç 5 durum, kopma+devam, ilk sayfa reddi, 1 satırlık sayfalarla masaüstü tam veri), PG `PUF7` (çok kolonlu anahtar), API `UFA5` (sınır + 400). 54/54. Mutasyon: çocukları aynı sayfaya ekleme kapatılınca SAY2 kırmızı.

### B1 — M-85 SearchGridAll tek geçiş, M-20 planlayıcı istatistikleri
- **M-85:** Malzeme/Araç/Günlük faaliyet `SearchGridAll` (Excel'e aktar + hücre filtresi seçenekleri) 500'lük sayfalarla her sayfada COUNT + OFFSET çalıştırıyordu (karesel; 40 bin satırda 23–50 sn). Artık `SearchGridCore(tumu:true)` tek SELECT; ekrandaki sayfalı liste aynen. `TekGecisListeTests` TGL1–3: 1.100+ satırda 'tümü' = sayfaların birleşimi (sıralı/filtreli), SQL komutu 1 (eski döngü 6 → test kırmızı, kanıtlandı). Liste/rapor/Excel ilgili 334 test geçti.
- **M-20:** `VeritabaniBakimi.IstatistikleriGuncelle` — `analysis_limit=400` + ilk kez ANALYZE, sonra PRAGMA optimize; masaüstü açılışında arka planda (yalnız sağlıklı DB). `VeritabaniBakimiTests` (20 bin satırda <5 sn, istatistik oluşur, veri değişmez; kaynak kilidi).

### B1 — M-78 / K-21 bilgisayar kodlu belge numarası (Migration102, şema 102)
- **Sorun (F-172):** belge no `MAX+1`; iki çevrimdışı bilgisayar aynı numarayı üretiyor, sunucu ikincisini benzersizlik ihlaliyle kalıcı atlıyordu (belge + satırları sunucuya hiç gitmiyordu).
- **Değişiklik:** `sync_devices.machine_code` (firma içinde benzersiz, A1…Z9, AA1…; kayıtta atanır, aynı bilgisayar aynı kodu korur); kayıt yanıtında `machineCode`; masaüstü kodu firma başına önbellekler, `DesktopServices.Session` atanırken `SessionContext.MachineCode` doldurulur (tek nokta). `BelgeNumarasi.Sonraki`: kodlu `GIR/CIK/TRF/SAY/TLP-2026-A3-0042`, fatura `A-A3-00000042` (ortak seri sayacı masaüstünde ilerletilmez); kodsuz (sunucu/web) eski biçim kodlu numaraları saymaz (PG'de CAST hatası önlendi). Eski numaralar aynen.
- **Testler:** `MakineKoduNumaraTests` KOD1–5 (kod dizisi/geçerlilik, atama, iki çevrimdışı bilgisayar çakışmaz + ikisi de sunucuya gider, kodsuz sıra, kaynak kilidi), PG `PUF8`. Stok/talep/fatura/kayıt/migration/PG ilgili paket **901/901**.

### B1 — K-22 b aynı kod iki bilgisayarda → KOD-2 + bildirim
- **Değişiklik:** push'ta doğal kod/numara benzersizlik çakışması (malzeme/araç/ekipman/cari/kasa-banka hesabı kodu, sipariş/iş emri no, stok belgesi/talep/fatura no) REDDEDİLMEZ: ilk boş `KOD-n` (n=2…99) ile kaydedilir, `ApplyResult.Renamed` + push yanıtında `renamed`. İsimle benzersiz tanımlar ve işlem kimliği çakışması bilinçli olarak kapsam dışı (red listesinde kalır). PG'de satır savepoint'i ile. **Çekmede** kod çakışan satır tablonun sonuna ertelenip bir kez daha denenir (yoksa sunucudaki kayıt o bilgisayara hiç inmezdi). Masaüstü: `sync_rejections.kind='kod'` bilgi bildirimi (red sayısına girmez, kayıt sonra kabul edilse de kalır, kullanıcı kapatır), rozet "ℹ N kayıt kodu değiştirildi", pencerede "Bilgi" kartı (Yeniden Gönder yok).
- **Testler:** `KodCakismasiTests` KCK1–6 (KOD-2/KOD-3, isim/işlem kimliği adlandırılmaz, bildirim türü/kalıcılığı, gönderen yeni kodu çeker, ters sırada çekme), PG `PUF9`. Mutasyon: çekme ertelemesi kapatılınca KCK6 kırmızı. Senkron riskli alan paketi **413/413**.

### B1 — K-05 bilinçli negatif stok istisnaları uyarıyla + M-16 / K-06 eksi stok uyarısı
- **K-05:** CLAUDE.md §3'e istisna yazıldı (bakım malzemesi + açılış stoğu eksiye düşebilir, uyarıyla). Eksik iki uyarı eklendi: masaüstü **ekipman bakımı** yetersiz stokta kayıttan önce sorar (araç bakımıyla aynı depo kuralı; web'de ekipman bakımına malzeme girilemediği için orada gerekmez — `11`); **eksi açılış stoğu** masaüstü ve web'de aynı metinle ayrıca onay ister.
- **M-16 / K-06:** `DashboardService.NegativeStockList` — depo bazlı eksi bakiye ana ekran/uyarılar panelinde "Eksi stok: <malzeme>" (kod · depo · miktar), şube kapsamına uyar, miktar değişince okunmuş uyarı yeniden açılır. Masaüstü + web aynı servis.
- **Testler:** `EksiStokUyariTests` EKS1–4; malzeme/bakım/açılış ilgili 363 test (1 PG atlandı) + ana ekran/bildirim 50 test geçti.

### B1 — M-18 (K-04 a, K-07 a) yakıt / bakım / talepte ezme yok
- **Değişiklik:** `EzmeYasak` = yakıt dağıtımı, yakıt deposu girişi, araç bakımı, ekipman bakımı, talep. Eşzamanlı düzenlemede (iki taraf da son gönderimden beri değiştirdi) sunucu sürümü KORUNUR, gelen sürüm çakışma listesine "kaybeden" olarak yazılır (yönetici mevcut "Üzerine Yazılanı Kazanan Yap" ile seçer); satır yeniden deneme/red listesine girmez. Sunucu kaydının zamanı gelen sürümün üstüne taşınır (+1 ms, sürüm +1) → gönderen bilgisayar sonraki çekimde sunucu sürümünü alır (ayrışma yok). Talepte bu "ilk ulaşan onay kazanır" demektir. Kart tablolarında (malzeme/araç/personel) son yazan kazanır + çakışma kaydı aynen. Çakışma ekranında yeni etiketler (Yakıt dağıtımı, Yakıt deposu girişi, Ekipman bakımı).
- **Testler:** `EzmeYasakCakismaTests` EZY1–6 + mevcut `SenkronCakismaEkraniTests` 16/16. Mutasyon: ezme yasağı kapatılınca EZY1/3/6 kırmızı. Senkron/yakıt/bakım/talep/tenant paketi **754/754**.

### B1 — Sayfalı gönderim (M-17 gönderim tarafı) + M-81 geçişte tam gönderim (commit 389c21a)
- **Gönderim sayfaları:** `BusinessSyncService.GonderimSayfalari` paketi tablo sırasıyla ≤2000 satırlık sayfalara böler; sayfadaki ebeveynin çocukları aynı sayfada (sunucu arada silmesin), aynı çocuk ikinci kez gitmez; her sayfa `pushPage` taşır ve sunucuda çakışma tabanı (son gönderim anı) yalnız son sayfadan sonra ilerler. Masaüstü sayfaları sırayla gönderir; bir sayfa hata verirse imleç ilerlemez (tekrar zararsız); sonuçlar birleştirilir, red listesi sayfa başına işlenir. Tek sayfaya sığan paket aynen.
- **M-81:** sayaç imlecine geçiş bir kez TAM gönderimdir (eşitlemeye sonradan eklenen tabloların hiç gönderilmemiş eski satırları da gider); bekleyen gönderim olarak görünür.
- **Testler:** `GonderimSayfaTests` GSY1–6; mutasyon: çocukları aynı sayfaya koyma kapatılınca GSY2/3 kırmızı. Senkron riskli alan paketi **408/408** (21 dk 12 sn).

### B1 — M-08 şifreli günlük yedek + geri yükleme provası
- **Değişiklik:** `scripts/yedek_al.ps1` artık her gün 03:00 (Görev Zamanlayıcı "DepoWise Haftalik Yedek" tetiği günlük; kaçarsa açılışta) ve veritabanı yedeği + fotoğraf arşivi **AES-256** ile şifrelenir (`scripts/yedek_sifre.ps1`: standart OpenSSL, PBKDF2 200.000 tur; şifreli dosya geri açılıp SHA-256 ile özgünüyle karşılaştırılarak doğrulanır; Drive'da ve bu bilgisayarda yalnız şifreli kopya). Anahtar: bu bilgisayarda Windows DPAPI dosyası, bilgisayar dışı kopya **kullanıcı kararıyla** özel GitHub hafıza deposunda (`claude-ortak-hafiza/anahtarlar`); yeni bilgisayar için `scripts/yedek_anahtar_geri_yukle.ps1`. Anahtar yoksa yedek yine alınır (düz) + log uyarısı. `yedek_prova.ps1` şifreli yedeği anahtarla açar.
- **Kanıt (gerçek koşu, 2026-10-17 23:46–23:50):** şifreli yedek alındı (1.350 KB, 107 tablo) + fotoğraflar (7.762 KB) → Drive; prova Drive'dan indirip açtı, yerel PG'ye geri yükledi: **PROVA BAŞARILI** (şema 98, 107 tablonun sayısı tutarlı). Canlı DB'ye yalnız okuma.

### B1 — M-06 masaüstü işlem geçmişi sunucuya (Migration103, şema 103)
- **Değişiklik:** `audit_logs` eşitleme listesinin sonuna, **yalnız gönderim** yönünde (`BusinessSyncService.YalnizGonderim`): sunucuda yalnız eklenir (`LedgerTables` → mevcut satır "DO NOTHING"), sunucudan masaüstüne inmez (v2 + eski sunucu anlık görüntüsü), modül yetkisine bağlı değil (her kullanıcı kendi geçmişini gönderir; firma oturumdan zorlanır), kaynak bilgisayar sunucuda `source_device` olarak yazılır (gelen değer yok sayılır). Migration103: `source_device` kolonu + mevcut masaüstlerinde yerel sayaç (`Migration100.YerelSayacKur` yardımcıya ayrıldı); Migration099 bu tabloyu atlar. M-81 geçiş tam gönderimi masaüstünün tüm eski geçmişini de taşır.
- **Testler:** `IslemGecmisiGonderimTests` AUD1–5; mutasyon: yalnız-ekleme kapatılınca AUD2 kırmızı. Hedefli 122/122 (5 dk 54 sn), senkron riskli alan paketi **441/441** (21 dk 34 sn).

### B1 faz sonu — iki ortam denetimi (2026-10-18) ve düzeltmeler
- Denetçi (salt okuma, 14 commit): işlevi kıran eşitlik açığı YOK; bilinçli istisnalar (yalnız masaüstü senkron araçları, kodlu numara, yedek betikleri, web ekipman bakımı malzeme açığı) doğrulandı. 3 bulgu düzeltildi: (1) eksi stok uyarı başlığına depo eklendi — aynı malzeme iki depoda eksiyken uyarı anahtarları ayrışır (okundu durumu karışmaz, web'de tek tıkla ikisi gizlenmez); (2) ezme yasaklı tablolarda çakışma metinleri "Sunucudaki (ilk ulaşan) sürüm korundu" (`ConflictRow.SunucuKorundu`), iki platformun sabit açıklamaları güncellendi; (3) web eksi açılış onayı tehlike görünümünde. "Kazanan yap" bağlı stok satırlarını düzeltmiyor → `11`'e risk olarak yazıldı.
- Testler: hedefli 62/62, arayüz 48/48.

### ✅ B1 KAPANIŞ (2026-10-18 12:23)
- **Tam takım:** 4203/4204 (2 sa 1 dk). Tek başarısız `FinalStabilizasyonTests.FIN10` — eski "çevrimdışı LWW" sözleşmesini sabitliyordu; K-04/M-18 kararıyla bilinçli olarak değişti (yol haritası B0'da öngörülmüştü). Test yeni kurala göre yeniden yazıldı (ilk ulaşan onay korunur, ret çakışma listesinde kaybeden, yönetici "kazanan yap" ile reddi seçebilir) ve geçti. **Arayüz 48/48. İki ortam denetimi:** eşitlik açığı yok, 3 bulgu düzeltildi.
- **Süre notu:** tam takım ~2 sa sürüyor; en uzun test `PaketSaklamaPolitikasiTests.PKT3` tek başına 20 dk (B3'te incelenecek).

## 2026-10-18 — B3 (senkron sağlamlığı, performans, operasyon)

### B3 — M-21 artımlı bakiye + test süresi iyileştirmesi
- **M-21:** `StockService.RecomputeBalances(companyId, malzemeler)` — yalnız verilen malzemeler (boş küme → hiçbir şey; 400'den fazla → tam). Gönderim ucu ve masaüstü çekmesi paketteki `stock_movements` malzemelerini verir (`PakettekiMalzemeler`): stok hareketi taşımayan gönderim/çekim bakiyeye HİÇ dokunmaz (eskiden her seferinde tüm defter: büyük firmada 12,1 sn + yazma kilidi). Formül, yarış koruması, hayalet bakiye temizliği aynı. `ArtimliBakiyeTests` ART1–5 (kapsam dışı bozuk bakiyeye dokunulmaz; artımlı = tam; paket çıkarımı; çekme).
- **Test süresi:** ölçüm — boş veritabanı kurulumu 722 ms, bunun 458 ms'si Migration099/100. SQLite'ta sunucu rolü indeks + tetikleyicileri migration'dan `Migration099_SyncTxid.SunucuRoluKur` yardımcısına taşındı (yalnız sunucu rolünde kurulur: API SQLite modu + testler; masaüstünde hiç işe yaramıyordu). Kurulum ~530 ms; 914 testlik riskli alan paketi **24 dk** (benzer kapsam önce ~45 dk). Şablon veritabanı fikri reddedildi (rol kimlikleri tüm test DB'lerinde aynı olurdu → gerçeklik kaybı).
- **Testler:** riskli alan paketi (senkron/stok/migration/PG/kopyalayıcı) **914/914**.

### B3 — M-23 ucuz "sunucuda değişiklik var mı" yoklaması
- **Değişiklik:** yeni uç `GET /api/sync/business-version-v2` → `{ ufuk }` (`BusinessSyncService.SunucuUfku`: tek sorgu, tablo taramaz). Masaüstü (`BusinessSyncPullService.SunucuDurumuAsync`) ufuk > kendi çekme imleci ise çeker; uç yoksa (eski sunucu, 404) eski yönteme düşer, eski yöntemde ipucu (`sync_pull_cursor`) saklanmaya devam eder. Eski uç (`business-version`, tüm tabloların en büyük damgası; 300 bin satırda 5,1 sn) eski masaüstleri için aynen. PG'de ufuk sunucu geneli: başka firmanın yazımı yalnız boş (ucuz) bir çekim tetikler.
- **Testler:** `UfukSurumTests` UFS1–4 (401, yazımla artar/yazımsız sabit, çekimden sonra "ileri" değil, eski uç çalışır, kaynak kilidi) + ilgili 29 test: 33/33; masaüstü derlemesi temiz.

### B3 — M-10 şifre değişince eski oturum jetonları düşer (Migration104, şema 104)
- **Sorun:** jeton 12 saat geçerli ve yenileme ucu her geçerli jetonu yeniliyordu → şifre değişse/sıfırlansa bile eski (ör. çalınmış) jeton çalışıyordu.
- **Değişiklik:** `users.token_epoch` (Migration104, yalnız ekleme, 0 ile doğar). Yönetici şifre değiştirince/sıfırlayınca ve kullanıcı ilk girişte kendi şifresini belirleyince aynı işlem içinde 1 artar (`OturumDonemi.Arttir`). Jeton üretildiği dönemi `te` alanında taşır; API her istekte `Session()` içinde karşılaştırır → eskiyse 401 (yenileme dahil). Dönem alanı olmayan eski jetonlar 0 sayılır: şifre değişene kadar geçerli (yayın anında kimse düşmez). Önbellek nesneye özel, 30 sn; değişim anında bayatlatılır (işlem sürerken okunan eski değer de). İlk giriş şifre belirleme yeni dönemli jeton döndürür (web ve masaüstü kesintisiz devam eder). `users` iş verisi eşitlemesinde olmadığı için masaüstü dönemi ezemez.
- **Testler:** `OturumDonemiTests` OTD1–5 (sıfırlama/değiştirme sonrası eski jeton + yenileme 401, yönetici ve diğer kullanıcılar etkilenmez, ilk giriş yeni jeton çalışır, dönemsiz eski jeton değişene kadar geçerli). Mutasyon: kontrol kapatılınca 5/5 kırmızı. Güvenlik riskli alan paketi **743/743** (16 dk).
- **Açık:** sohbet merkezi (`ChatHub.cs`, kullanıcının commitlenmemiş işi) dönemi kontrol etmiyor → `11`'e yazıldı.

### B3 — M-11 web girişinde bir kişinin hatası herkesi kilitlemesin (F-041, F-051)
- **Sorun:** web sunucusu API'yi kendi adresinden çağırdığı için giriş sınırı (30 / 5 dk / adres) TÜM web kullanıcılarına ortak bir sayaçtı; sabah toplu giriş ya da tek kişinin hatalı denemeleri herkesi durdurabiliyordu. Masaüstü girişi de şifreyi 3 kez doğruluyordu (`sync-login` + jeton için `login` + tanım eşitlemesi için `login`).
- **Değişiklik:** `IstemciAdresiImzasi` (web'e bağlı ortak dosya `Common/BaglantiIzleyici.cs`): web, ilk sayfa isteğinde kullanıcının adresini (`Fly-Client-IP`) okur (`App.razor` → korumalı parametre → `Routes` → devre servisi `IstemciBaglami`), API'ye giden her isteğe adres + HMAC-SHA256 imzası ekler (`IstemciAdresiIsleyici`). API `ClientIp` yalnız imza tutarsa ve değer geçerli bir IP ise bu adresi kullanır; anahtar (`DEPOWISE_ISTEMCI_IP_ANAHTARI`, iki uygulamada aynı) tanımlı değilse başlık yok sayılır → yayına kadar davranış değişmez. F-051: `sync-login` aynı yanıtta jeton da döner (`AuthService.SenkronGirisiniOnayla`: `/login` ile aynı kilit kuralı + başarı/oturum kaydı, şifre ikinci kez doğrulanmaz; kilitliyse jeton yok); masaüstü bu jetonu kullanır, tanım eşitlemesi saklanan jetonu yalnız aynı kullanıcıya ve sunucuya aitse kullanır (`ServerAuthClient.JetonFor`), yoksa eski yol.
- **Testler:** `IstemciAdresiTests` IAD1–5 (imza kabul/ret, adres başına sınır, sahte imza ve anahtarsız durumda yok sayma, `sync-login` jetonu + paket alanları + kilitliye jeton yok, kaynak kilidi). Mutasyon: imza doğrulaması kapatılınca IAD1/IAD2, kilit kontrolü kapatılınca IAD4 kırmızı. Güvenlik + tanım eşitlemesi riskli alan paketi **840/840** (17 dk); web ve masaüstü derlemesi temiz.

### B3 — M-13 küçük güvenlik düzeltmeleri (F-107, F-110, F-050; F-154 K-15 gereği kapsam dışı)
- **F-107 parola özeti:** PBKDF2-SHA256 100.000 → **600.000 tur** (ölçüm .NET 10: 13 ms → 79 ms). Eski özetler geçerli; doğru şifreyle girişte (`AuthService.Login` ve masaüstü `ExportForSync`) yeni turla yeniden yazılır — yalnız özet o arada değişmediyse (eşzamanlı şifre değişimi ezilmez); oturum dönemi/`updated_at` değişmez. Masaüstü yeni özeti senkron girişi paketinden yerele yazar.
- **F-110:** CORS `AllowAnyOrigin` → yalnız `https://depowise-web.fly.dev` (ek köken `DEPOWISE_CORS_ORIGINS`); tarayıcıdan API'yi doğrudan çağıran ekran yok (doğrulandı: web Blazor Server, sohbet .NET istemcisi). JWT anahtarı 32 bayttan kısaysa API açılmaz (HS256 kütüphanesi zaten kısa anahtarla imzalamaz → canlı anahtar uygun).
- **F-050:** `LogRedactor` artık kullanılıyor: API 500 logu + bakiye hesaplama hatası, masaüstü `SyncLog`, `UpdateService` günlüğü. `docs/SECURITY.md` terk edilen Next.js yerine bugünkü koda göre yeniden yazıldı.
- **Testler:** `GuvenlikKucukDuzeltmeTests` GKD1–6 (tur sayısı, girişte yenileme/yanlış şifre dokunmaz, senkron paketi yeni özet, CORS 3 köken, kısa anahtar, maskeleme + kaynak kilidi) + mevcut güvenlik/yetki testleri 43/43. Mutasyon: girişte yenileme kaldırılınca GKD2, CORS herkese açılınca GKD4 (3 durum) kırmızı. Riskli alan paketi (güvenlik/yetki/migration/tanım eşitlemesi/güncelleme) **892/892** (21 dk; 600.000 tur özetleme test süresini biraz uzatır).

### B3 — M-14 Excel içe aktarımını geri alma (F-063)
- **Sorun:** aktarım satır satır kaydediyor (bilinçli: bir hatalı satır diğerlerini bozmaz) ama sonradan "bu aktarımda ne oluştu" bilinmiyordu → yanlış dosya aktarılınca kayıtlar tek tek elle silinmek zorundaydı.
- **Değişiklik:** `AktarimPartisi` (akışa özel bağlam, `AsyncLocal`): aktarım sürerken `AuditWriter` her işlem geçmişi satırına parti kimliğini (`import:<tür>:<guid>`, mevcut `correlation_id` kolonu) yazar → **migration yok**. `AktarimGeriAlmaService.GeriAl`: partide OLUŞAN kayıtlar en yeniden eskiye, her biri kendi servisinin kuralıyla geri alınır (malzeme/araç sil, personel sil, yakıt giriş/dağıtım iptal, bakım iptal, muayene iptal); kullanılmış kayıt bırakılır + nedeni (ör. açılış stoğu girilmiş malzeme: MLZ-01, defter değişmez; bakiyeyi eksiye düşürecek yakıt girişi), otomatik oluşan tanımlar bırakılır, tekrar çalıştırılabilir (geri alınmış kayıt "zaten"). Firma oturumdan: başka firmanın partisi 403. API: aktarım yanıtında `partiKimligi` (kayıt eklendiyse), `POST /api/import/undo`. Masaüstü (Excel Merkezi) ve web (`/import`) "Bu Aktarımı Geri Al" düğmesi + onay; masaüstü sonra sunucuya gönderir.
- **Testler:** `AktarimGeriAlmaTests` AGA1–5 (malzeme geri alma + açılış stoklu bırakılır + tanım bırakılır + ikinci geri alma "zaten"; damga yalnız aktarım süresince ve iç içe dönüş; araç + yakıt depo girişi; başka firma 403, geçersiz kimlik 400, önizleme/atlanan aktarım parti vermez; kaynak kilidi) + mevcut aktarım testleri: 138/138. Mutasyon: "zaten" kontrolü kaldırılınca AGA1, damga kaldırılınca AGA1/2/3/5 kırmızı. Arayüz 48/48. Riskli alan paketi (aktarım/işlem geçmişi/Excel/yakıt/bakım/muayene) **559/559** (12 dk).

### B3 — M-19 yedekten dönüşte gönderilmemiş işler kaybolmasın (F-043)
- **Sorun:** masaüstü "Yedek Geri Yükle" veritabanı dosyasını doğrudan eskisiyle değiştiriyordu → yedekten SONRA yapılıp sunucuya henüz gönderilmemiş işler sessizce kayboluyordu; geri yükleme sırasında arka plan eşitlemesi dosyaya yazabiliyordu; doğrulama yalnız bütünlük kontrolüydü (şemasız boş dosya da geçer → tüm veri silinirdi).
- **Değişiklik:** `BackupService.Restore` üzerine yazmadan önce mevcut verinin `…_geri_yukleme_oncesi.db` kopyasını alır ve doğrular (alınamazsa geri yükleme YAPILMAZ), yedeği üç kapılı `YedekGecerliMi` ile doğrular, kopyanın yolunu döndürür (yedek listesinde görünür → geri dönülebilir). Masaüstü (`BackupViewModel.Restore`): eşitleme kapısını tutar (başarılıysa yeniden başlatana kadar bırakmaz), önce firma kuyruğu + bekleyen işleri sunucuya gönderir; hâlâ gönderilemeyen varsa ayrı, açık bir uyarıyla onay ister. Geri yüklemeden sonraki ilk eşitleme sunucudaki güncel veriyi (gönderilenler dahil) geri getirir.
- **Testler:** `GeriYuklemeOncesiTests` GYO1–3 + yedek/uçtan uca testleri 35/35. Mutasyon: güvenlik kopyası ve güçlü doğrulama kapatılınca GYO1/GYO2 kırmızı.

### B3 — M-22 liste ve arama hızı (F-111, F-178; malzeme listesi)
- **F-111 arama işlevi:** SQLite Türkçe `like()` satır başına deseni küçültüp Regex kuruyordu. Artık desen bir kez çözülüp önbelleğe alınır; yalnız `%` içeren desenler Regex'siz sıralı arama (`StartsWith/EndsWith/Contains/IndexOf`), `_` içerenler önbellekli Regex. Sonuç eskisiyle birebir (`AramaHiziTests` ARH1: 20 bin rastgele + Türkçe örnek; eski uygulama karşılaştırma için `SqlLikeTrEski` olarak duruyor). Ölçüm (300 bin değer): 3,1–4,5 kat.
- **F-178 malzeme listesi:** her sayfa tüm firmanın türetilmiş kümesini (tüm `stock_balances` gruplaması, uyumlu araç/muadil alt sorguları) iki kez (sayım + sayfa) üretiyordu. Artık iki aşamalı: (1) filtre/sıralama yalnız kart kolonlarındaysa (kod, ad, tür, açıklama, para birimi) sayım + sayfa kimlikleri doğrudan `materials`'tan; türetilmiş kolon filtresinde / "yalnız kritik"te tek geçiş `COUNT(*) OVER ()`; (2) tam satırlar YALNIZ sayfa kimlikleri için (stok toplamı da yalnız o malzemeler). Excel/tam liste (`SearchGridAll`) yolu değişmedi. Migration/indeks YOK.
- **Ölçüm (300 bin malzeme, bu bilgisayar, medyan):** ilk sayfa 1.598 → 86 ms · son sayfa 2.215 → 159 ms · ada göre sırala 2.236 → 190 ms · ad ara 1.781 → 402 ms · kod ara 1.654 → 347 ms · stok filtresi 1.628 → 605 ms · yalnız kritik 1.409 → 653 ms. 8 senaryonun sonuç imzası önce/sonra birebir aynı.
- **Testler:** `MaterialGridHizTests` MGH1 (SQLite) + MGH2 (PostgreSQL): 18 filtre × 7 sıralama × 2 sayfa boyutu × tüm sayfalar (+ bir ileri) — her sayfa değişmemiş tam liste yolunun aynı dilimi ve toplam aynı; başka firma görünmez. Hedefli paket (malzeme/liste/tablo/arama) **493/493** (12 dk). Mutasyon: hızlı yolda silinmiş filtresi / genel yolda toplam bozulunca MGH1 kırmızı.
- **Kalan:** stok hareketleri, günlük faaliyet ve yakıt listelerinde aynı desen (F-178) henüz uygulanmadı → `11`.

### B3 — M-12 + M-83 zorunlu güncelleme ve sürüm uyumu (K-17 a; F-042, F-156, F-184, F-185)
- **Sorun:** "Ertele" sınırsızdı, `min_supported_version` hiçbir şeyi engellemiyordu → eski masaüstü yıllarca eski kurallarla veri yazabilirdi; masaüstü veritabanı güncellemesinden (migration) önce yedek yoktu.
- **Değişiklik:** `GuncellemePolitikasi` (ortak, test edilebilir): sürüm en son yayının en düşük desteklenen sürümünün altındaysa **hemen zorunlu** (kritik yayında yayıncı en düşük sürümü yeni sürüme çeker); BU BİLGİSAYARDAKİ sürüm güncelleme varken ilk görüldüğünden beri 7 gün geçtiyse zorunlu (sayaç çalışan sürüme bağlı → sık yayın sayacı sıfırlamaz); aksi hâlde ertelenebilir, kalan gün mesajda. Masaüstü: giriş sonrası ve 10 dk'lık yeniden sorma akışında zorunluda "Ertele" yok ve "otomatik güncelleme kapalı" ayarı uygulanmaz; başarısız kurulum her dakika tekrar sorulmaz. Her masaüstü isteği program sürümünü başlıkla gönderir (`HttpIstemci.UygulamaSurumu`). Sunucu: desteklenmeyen sürümün `business-push`'u **426** + anlaşılır neden (başlıksız eski masaüstü 1.0.202 sayılır; en düşük tanımsızsa/başlık bozuksa herkes yazar = bugünkü davranış); çekme serbest; masaüstü nedeni "Eşitle" penceresinde gösterir, kayıtlar yerelde kalır. M-83: masaüstü açılışında bekleyen migration varsa önce `…_guncelleme_oncesi_v<N>.db` yedeği (`BackupService.EtiketliYedek`, doğrulanır), migration süresi açılış günlüğüne.
- **Testler:** `ZorunluGuncellemeTests` ZG1–6 + güncelleme/yedek testleri 49/49; arayüz 48/48. Mutasyon: 7 gün kuralı kapatılınca ZG1, sunucu kapısı kapatılınca ZG3 kırmızı.
- **Yayın notu (M-12):** kapı bugün kimseyi etkilemez (canlıdaki yayınların en düşük sürümü düşük). İlk zorunlu yayında en düşük sürüm bilinçli yükseltilmeli → `12`.

### B3 — test süresi: PKT3 (tam koşuda 20 dk)
- `PaketSaklamaPolitikasiTests`: yayınlar arası 40 ms GERÇEK bekleme (Task.Delay) yerine dosyalara açık, kesin artan zaman damgası → PKT3 0,03 sn (doğrulanan kural aynı; saat çözünürlüğü kırılganlığı da kalktı; mutasyon: temizlik kapatılınca PKT3/4/5 kırmızı). Olası kök neden tam koşuda iş parçacığı havuzunun dolması (await devamları kuyrukta) → `TestIsParcacigiHavuzu`: test sürecinde havuz alt sınırı ≥ 64 (doğrulama değişmez). Etki B3 sonu tam koşusunda ölçülür.

### B3 — M-09 şube yetkisi daralınca yerel veri temizliği (F-040)
- **Sorun:** çekme yalnız kapsamdaki satırları indiriyordu ama daha önce (geniş yetkiyle / aynı bilgisayarı kullanan başka kullanıcıyla) inmiş kapsam dışı satırlar silinmiyordu → yetkisi alınan kişi eski şubenin verisini bilgisayarında taşıyordu. Tersi: kapsam genişleyince imleç ileride olduğundan diğer şubenin eski satırları inmiyordu.
- **Değişiklik:** `YerelKapsamTemizligi` (Infrastructure, test edilebilir): girişte kullanıcının etkin kapsamı (`BranchAccess.Allowed` — sunucu çekmesiyle aynı) yerel verinin kapsam imzasıyla (`sync_local_scope`; bilinmiyorsa sınırsız sayılır) karşılaştırılır. Daraldıysa çekmeyle AYNI şube kapsamlı tablolarda (araç, personel, stok hareketi, talep, ön muhasebe + çocukları) kapsam dışı ve şubesi dolu satırlar sert silinir (sunucuya silme olarak yayılmaz); **gönderilmemiş kayıt silinmez** (yerel sayaç > gönderilmiş sayaç), bekleyen çocuğu olan ebeveyn korunur, gönderim sayacı bilinmiyorsa ertelenir; stok hareketi silindiyse bakiye yeniden hesaplanır. Genişlediyse silme yok, çekme imleci sıfırlanır (tam çekim). Masaüstü girişte gönderimden SONRA, çekmeden ÖNCE çağırır (`BusinessSyncPullService.YerelKapsamiUygula`). Malzeme kartı/cari/yakıt/bakım bilinçli olarak kapsam dışı (çekme de süzmüyor; KARAR-7).
- **Testler (M-09):** `YerelKapsamTemizligiTests` YKT1–5 (daralma, bekleyen+ebeveyn koruması, genişleme/örtüşme/aynı kapsam, ertelenme, imza + kaynak kilidi) + şube/kapsam testleri 577/577. Mutasyon: bekleyen koruması kaldırılınca YKT2 kırmızı.

### B3 faz sonu tam koşu (2026-10-18 15:51–17:23)
- **Tam takım 4255/4255** (92 dk; önceki tam koşu 2 sa 1 dk). Arayüz 48/48. Trx: `artifacts/dw_tam_b3.trx`.
- PKT3 tam koşuda hâlâ 9,5 dk (tek başına 0,03 sn): sınıfın diğer 5 testi milisaniyelerde bitti → süre başlamak için boş iş parçacığı beklemesi. Sınıf paralel olmayan koleksiyona alındı (diğerleri bittikten sonra tek başına; doğrulama değişmedi), PKT 6/6.
- **İki ortam denetimi (14 commit):** eşitlik açığı yalnız M-14'te; 4 bulgu düzeltildi: (1) masaüstünde yeni aktarım denemesinde / tür değişince eski "Geri Al" kimliği sıfırlanmıyordu (B'nin hatası ekrandayken A geri alınabiliyordu) — web ile aynı hâle getirildi; (2) web geri alma hatasında sunucunun gerçek nedeni gösterilmiyordu → `ApiClient.PostJsonWithErrorAsync`; (3) web'de tamamı geri alınınca eski "İÇE AKTARIM SONUCU" kartı kalıyordu → kaldırılır; (4) **yan etki:** `/api/me/authsig` ham parola özetini içeriyordu → M-13 yeniden özetleme (şifre aynı) açık masaüstlerinde yersiz "şifre değişti" çıkışı yapardı → imza artık şifre değişim dönemini (`token_epoch`) kullanır (kolon yoksa eski davranış). Testler: GKD7 (özet yenilenince imza aynı, şifre değişince farklı; mutasyonla kırmızı) + ilgili 19/19, arayüz 48/48. Bilinçli bırakılan: geri alma asıl oturumla yapılır (başka şubeye şifreyle yapılan aktarımda bazı kayıtlar yetki nedeniyle bırakılabilir; iki platform aynı).

## 2026-10-18 — B5 (iş akışları: stok / satınalma / talep)

### B5 — M-24 çift stok uyarısı (F-045, K-09 a) + M-51 para işlemi onayı (F-053, K-13 a)
- **M-24:** `InvoiceService.CiftStokUyarilari`: alış faturası "stoğu etkilesin" açıkken (1) aynı tedarikçi fatura numarasıyla (dış no, büyük/küçük harf ve boşluk duyarsız) yapılmış iptal edilmemiş stok girişi, (2) cari Tanımlar'daki tedarikçiye bağlıysa (`parties.supplier_id`) son 60 günde aynı malzemeler için o tedarikçiden SİPARİŞLE mal kabul → uyarı. Kayıt öncesi iki platformda: uyarı varsa "Stoğu Etkilemeden Kaydet" (önerilen, tek tık) ya da ikinci açık onayla "Evet, Stoğa Girsin". Metinler servisten (web: `POST /api/invoices/check`, hiçbir şey yazmaz). Uyarı yoksa M-51 onayı.
- **M-51:** fatura (genel toplamla) ve tahsilat/ödeme (tutar + kapatılacak fatura sayısıyla) kaydı onay sorar; metin ortak (`InvoiceService.KayitOnayMetni`, `FinanceTxnTypes.KayitOnayMetni`; web `POST /api/finance/entries/check`).
- **Testler:** `CiftStokUyariTests` CSU1–5 + fatura/ödeme/finans/satınalma testleri 95/95; mutasyon: iki kontrol kapatılınca CSU1/2/3 kırmızı; arayüz 48/48.

### B5 — M-29 sipariş ve iş emri numarası otomatik (F-073)
- Yeni satın alma siparişinde no boşsa `SIP-<yıl>-0001`, iş emrinde `IE-<yıl>-0001`; masaüstünde bilgisayar kodlu (`SIP-2026-A3-0001`, çevrimdışı iki bilgisayar çakışmaz) — talep/fatura ile aynı `BelgeNumarasi`. Numara üretilecekse işlem IMMEDIATE (eşzamanlı iki kayıt aynı numarayı almasın). Elle yazılan numara aynen kalır; iş emri DÜZENLEMEDE no zorunlu. İki platformda alan "Otomatik" ipucuyla isteğe bağlı.
- **Testler:** `OtomatikBelgeNoTests` OBN1–4 + satınalma/iş emri/belge numarası testleri 35/35; arayüz 48/48.

### B5 — M-32 mal kabulde irsaliye no + kabulü geri alma (F-113; Migration105, şema 105)
- **Sorun:** mal kabul stok belgesi hangi siparişten geldiğini tutmuyordu; stok ekranı düzeltmeyi "Satın Alma ekranından yapın" diye reddediyor ama orada kabulü geri alma yoktu; stok ekranından doğrudan iptal edilirse siparişin teslim alınan miktarı düşmüyordu (sipariş ↔ defter ayrışması). İrsaliye no girilemiyordu.
- **Değişiklik:** Migration105 `stock_documents.purchase_order_id` (yalnız ekleme) + eski kabuller işlem geçmişindeki kabul kaydından eşlenir (tek tarama). Mal kabul irsaliye no'yu belgenin "Fatura/İrsaliye No" alanına yazar ve belgeyi siparişe bağlar. `PurchaseOrderService.Receipts` (kabul listesi) + `ReverseReceipt` (TEK işlem: ters stok kaydı — eksi stok kalkanı geçerli, mal kullanıldıysa yapılmaz — + teslim alınan miktar düşer + tamamlanan sipariş yeniden açılır + gerekçeli işlem geçmişi; tekrar zararsız). Siparişe bağlı kabul stok ekranından iptal edilemez (bağsız eski belgelerde eski davranış). İki platformda irsaliye alanı, "MAL KABULLERİ" listesi ve gerekçeli "Geri Al" (yetki: mal kabul + stok düzenleme; aynı metinler). API: `GET /api/purchasing/{id}/receipts`, `POST /api/purchasing/{id}/receipts/{doc}/reverse`.
- **Testler:** `MalKabulGeriAlmaTests` MKG1–6 + satınalma/iptal testleri 101/101; mutasyon: stok ekranı engeli ve teslim düşüşü kaldırılınca MKG4/MKG2 kırmızı; arayüz 48/48. Riskli alan paketi (senkron/migration/kopyalayıcı/PG/stok/defter/satınalma/fatura/firma ayrımı) **1017/1017** (37 dk).

### B5 — M-30 dönem kilidi / ay kapanışı (F-076; Migration106, şema 106)
- **Sorun:** kapanmış bir ayın raporu sonradan değişebiliyordu: geri tarih yetkisi olan kullanıcı eski aya kayıt açabiliyor,
  eski ayın faturası/para hareketi/stok belgesi iptal edilebiliyordu.
- **Değişiklik:** Migration106 `companies.period_lock_date` (yalnız ekleme; mevcut firmalarda kilit yok → davranış aynı).
  Tek kontrol noktası `DonemKilidi.Dogrula` (kilit günü DAHİL öncesi reddedilir); oturum kilidi taşır (`SessionContext.DonemKilidi`,
  giriş + sunucu oturumu; ayar değişince önbellek beklenmez). Bağlanan yollar: tarih kapısı `DateEntryPolicy` (stok, yakıt, sipariş,
  iş emri, zimmet), fatura kesme/iptal, finans hareketi/virman/iptal, stok belgesi iptali/düzeltmesi/sipariş kabulü geri alma
  (ortak gövde), yakıt iptal/düzeltme, bakım kaydı/iptali (Günlük Faaliyet iptali dahil). Tekrar gönderilen (aynı işlem kimlikli)
  kayıt hata vermez, mevcut sonucu döner. Ayar yalnız yönetici, bugünden önce bir gün, işlem geçmişine yazılır; masaüstü giriş
  paketiyle yerel firma satırına iner (çevrimdışı da uygulanır). API `GET/PUT /api/company/period-lock`; iki platformda
  "Alan Ayarları" ekranında "Dönem Kilidi" kartı (masaüstü ayarı sunucuya yazar, çevrimdışıyken anlaşılır uyarı).
- **Testler:** `DonemKilidiTests` DK1–9 9/9; mutasyon 8/8 yakalandı (sınır, stok iptali, sunucu oturumu, paket yazımı, önbellek,
  fatura iptali, yakıt, finans iptali). Riskli alan paketi (migration/PG/kopyalayıcı/stok/fatura/finans/yakıt/bakım/giriş-oturum/tarih/satınalma) **1234/1234** (36 dk).

### B5 — M-25 faturayı mal kabulden oluştur + sipariş-kabul-fatura eşleştirme (F-072, F-148, F-059; Migration107, şema 107)
- **Sorun:** alış faturası sipariş/kabul bağı taşımıyordu; mal kabulle stoğa giren mal, "stoğu etkilesin" açık faturayla ikinci kez
  girebiliyor (M-24 yalnız uyarıyordu), aynı mal iki kez faturalanabiliyor, faturalanan miktar kabulü aşabiliyordu (fazla ödeme).
- **Değişiklik:** Migration107 `invoices.receipt_document_id` (yalnız ekleme). `InvoiceService.KabulTaslagi`: cari, tedarikçiye bağlı
  cari karttan (`parties.supplier_id` — tedarikçi ↔ cari eşlemesi, F-059); satırlar kabulden (miktar + kabul fiyatı); not sipariş/kabul/
  irsaliye. Kayıtta (`NewInvoice.ReceiptDocumentId`): yalnız alış, stok YAZILMAZ (istemci ne derse desin), aynı kabule ikinci fatura
  yok (iptal edilince yeniden kesilebilir), malzeme başına faturalanan ≤ kabul (kısmi fatura ve hizmet satırı serbest), geri alınmış
  kabul ve başka firmanın kabulü reddedilir. Faturalı kabul geri alınamaz ("önce faturayı iptal edin"). Kabul listesinde fatura no.
  İki platformda kabul satırında "Faturalaştır" → fatura formu dolu açılır (KDV ve satıcı fatura no kullanıcıda; stok kutusu kilitli).
  API `GET /api/invoices/from-receipt/{belge}`, fatura gövdesinde `receiptDocumentId`, kabul listesinde `invoiceNo`/`canInvoice`.
- **Testler:** `KabuldenFaturaTests` KF1–8 + `MalKabulGeriAlmaTests` 14/14; mutasyon 7/7 (stok kapatma, ikinci fatura, miktar, geri alma
  engeli, firma, bağ yazımı, geri alınmış kabul — sonuncusu ilk denemede yakalanmadı, test hata nedenini doğrulayacak şekilde sıkılaştırıldı).
  Riskli alan paketi (senkron/migration/kopyalayıcı/PG/stok/fatura/satınalma/cari/firma ayrımı) **1187/1187** (42,5 dk).

### B5 — M-26 talepten tek tıkla karşılama: stoktan gönder / sipariş aç (F-071; Migration108)
- **Sorun:** onaylı talep stoktan karşılanırken kalem üç kez yazılıyordu (talep → transfer → sipariş); talep ekranında stok
  görünmüyordu, merkezdeki mal yeniden satın alınabiliyordu; "gönderildi" durumu stok hareketi üretmiyordu.
- **Değişiklik:** Migration108 `stock_documents.request_id`. `TalepKarsilamaService`: karşılama tablosu (istenen / gönderilen /
  siparişte / kalan / kaynak depo + toplam stok, yüzde), **Stoktan Gönder** = gerçek transfer (`StockService.TransferTx`, tek işlem,
  belge talebe bağlı, tekrar gönderim zararsız, eksi stok kalkanı), **Sipariş Aç** = kalan için talebe bağlı sipariş (teslim =
  gidecek şube). Gönderilen + siparişteki istenenden fazla olamaz; onaysız / operasyonu iptal talep karşılanamaz. İki platformda
  Talep Operasyonları ekranında "KARŞILAMA" bölümü. API `GET /api/request-ops/{id}/fulfilment`, `POST …/ship`, `POST …/order`.
- **Testler:** `TalepKarsilamaTests` TK1–8. Riskli paket (stok/onay/talep/senkron/migration/kaynak-metin) 1333/1333, 37 dk 36 sn (2026-10-19, `artifacts/dw_b5b_paket.trx`).

### B5 — M-27 iki adımlı transfer: gönder → teslim al, "yolda" (F-070; Migration109)
- **Sorun:** şube dışı çıkış tek adımdı; mal kaynaktan çıktığı an hedefte görünüyor, nakliyede kaybolan mal fark edilmiyordu.
- **Değişiklik:** Migration109 `stock_documents.transit_status / received_at`. Yoldaki mal defterde ayrı lokasyonda
  (`StockBalanceWriter.Transit` = "~yolda"; şube değil, firma toplamına dahil). `YoldaGonder` (kaynak → yolda), `TeslimAl`
  (yolda → hedef; gönderilenden fazla olamaz, eksikte durum "eksik"), `YoldaKayipKapat` (gerekçeli, ters kayıt yetkisi; yoldan
  çıkış), `Yoldakiler` listesi. Tek adımlı transfer aynen. Lokasyon kırılımında "Yolda (teslim bekliyor)". İki platformda giriş-çıkış
  ekranında "Yolda (teslimde girsin)" seçeneği + "YOLDAKİ TRANSFERLER" paneli. API `transfer` gövdesinde `yolda`,
  `GET /api/stock/in-transit`, `POST …/{belge}/receive`, `POST …/{belge}/close-loss`.
- **Testler:** `YoldaTransferTests` YT1–8. Riskli paket (stok/onay/talep/senkron/migration/kaynak-metin) 1333/1333, 37 dk 36 sn (2026-10-19, `artifacts/dw_b5b_paket.trx`).

### B5 — M-28 sayım: kör sayım, taslak, fark toleransı, masaüstünde tablo (F-075; Migration110)
- **Sorun:** kör sayım, taslak, fark onayı yoktu; masaüstünde sayım satır satırdı.
- **Değişiklik:** Migration110 `stock_count_drafts` (cihaza/sunucuya yerel, eşitlenmez) + `companies.count_tolerance_pct`.
  `SayimService`: taslak kaydet/aç/sil/kesinleştir (tek belge, işlem kimliği taslaktan → iki kez kesinleşmez), tolerans ayarı (yalnız
  yönetici, 0–100, işlem geçmişi; giriş paketiyle masaüstüne iner). `StockService.Count`: farkı toleransı aşan satır varsa yalnız ters
  kayıt yetkilisi kesinleştirir. Masaüstü: "Depodaki Tüm Malzemeleri Listele" (tabloda düzenlenebilir sayılan), kör sayım, taslaklar;
  web: kör sayım + taslaklar (tablo zaten vardı). Tolerans ayarı iki platformda Alan Ayarları'nda.
  API `GET/PUT /api/stock/count-tolerance`, `GET/POST /api/stock/count-drafts`, `DELETE …/{id}`, `POST …/{id}/commit`.
- **Testler:** `SayimIyilestirmeTests` SI1–7. Riskli paket (stok/onay/talep/senkron/migration/kaynak-metin) 1333/1333, 37 dk 36 sn (2026-10-19, `artifacts/dw_b5b_paket.trx`).

### B5 — M-33 tek onay merkezi: kalem önizleme, toplu onay/ret (F-115, F-092)
- **Sorun:** onay iki ekrandaydı; belge kalemleri onay listesinde görünmüyordu; her kayıt tek tek onaylanıyordu.
- **Değişiklik:** `ApprovalService.KalemOnizleme` (yalnız adım sahibine; fiyat yetkisi yoksa tutar gizli), `TopluOnayla` /
  `TopluReddet` (her adım kendi işleminde ve mevcut beş kapıdan; sonuç satır satır, en fazla 200). `RequestService.TopluKarar`
  (zincirsiz talepler; zincirli talep tek adımlı yoldan geçemez). "Onaylamalarım" iki platformda zincirsiz bekleyen talepleri de
  listeler, işaretli kayıtları toplu onaylar/reddeder, seçili belgenin kalemlerini gösterir. Tutar eşiği (isteğe bağlı) yapılmadı.
  API `GET /api/approvals/steps/{id}/items`, `POST /api/approvals/bulk`, `POST /api/requests/bulk-decision`.
- **Testler:** `TekOnayMerkeziTests` TO1–7. Riskli paket (stok/onay/talep/senkron/migration/kaynak-metin) 1333/1333, 37 dk 36 sn (2026-10-19, `artifacts/dw_b5b_paket.trx`).

### B6 — M-34 toplu yakıt dağıtımı (F-074, K-13)
- **Sorun:** sabah onlarca araca yakıt tek tek giriliyordu.
- **Değişiklik:** `FuelService.TopluDagit`: tek işlemde (biri hatalıysa hiçbiri yazılmaz), satır başına işlem anahtarı `{op}:{i}`,
  en çok 200 satır, aynı araç iki kez olamaz; sonda tek onay (`TopluOnayMetni`). Firma "Yakıtı Alan" zorunlu kıldıysa toplu giriş
  engellenir. Masaüstü yakıt ekranında tablo, web'de toplu panel. API `POST /api/fuel/distribute-bulk`.
- **Testler:** `TopluYakitTests` TY1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-35 araçtan arıza bildir / iş emri aç (F-077; Migration111)
- **Sorun:** arızalı araç ile iş emri arasında bağ yoktu; araç durumu elle değiştiriliyordu.
- **Değişiklik:** Migration111 `work_orders.fault_vehicle_id`. `WorkOrderService.AractanIsEmri`: "Arıza Bildir" aracı "Arızalı"
  yapar ve taslak iş emri açar (aynı araca ikinci açık arıza açılamaz); "İş Emri Aç" durumu değiştirmez. İş emri tamamlanınca
  (başka açık arıza yoksa) araç yeniden "Aktif". Arıza süresi saat olarak hesaplanır. Araçlar ekranında iki düğme (iki platform).
  API `POST /api/vehicles/{id}/work-order`, `GET …/open-fault`.
- **Testler:** `AracArizaTests` AA1–6. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-36 sayaç kuralları: geçmiş, geçersiz say, geriye tarihli okuma, eski sayaç (F-078; Migration112)
- **Sorun:** yanlış girilen sayaç (fazladan basamak) aracın sayacını ve bakım uyarılarını kalıcı bozuyordu; düzeltme yolu yoktu.
- **Değişiklik:** Migration112 `meter_void` / `is_void`. `SayacService`: yakıt + bakım + araç kartı okumaları tek geçmişte;
  "geçersiz say" (kayıt iptal edilmeden sayaç hesabından çıkar, geri alınabilir; ters kayıt yetkisi + gerekçe, işlem geçmişi);
  geriye tarihli okuma önceki/sonraki okumayla tutarsızsa UYARI (engel değil); son okuma 30 günden eskiyse uyarı.
  `VehicleMeterService.Hesapla` geçersiz okumaları dışlar. Araç detayında sayaç geçmişi, yakıt kaydında tutarlılık uyarısı (iki
  platform). API `meter-history`, `meter-void`, `meter-check`.
- **Testler:** `SayacKurallariTests` SK1–6. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-37 bakım planı: km/saat "ya da" gün (F-079; Migration113)
- **Değişiklik:** Migration113 `maintenance_definitions.interval_days`. Bakım tanımına "Ya da (gün)"; sonraki bakım tarihi hesaplanır,
  uyarıda hangisi önce gelirse o tetikler. Tanım formu iki platformda.
- **Testler:** `BakimPlaniTests` BP1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-39 kira sözleşmesi, iki yön (F-084, K-19; Migration114)
- **Değişiklik:** Migration114 `vehicles.rental_party_id / rental_direction` (kiralandı / kiraya verildi). `KiraService`:
  sözleşme bilgisi (cari, yön, başlangıç-bitiş), dönem raporu (gün, maliyet/gelir), bitişi yaklaşan sözleşme uyarıları. Otomatik
  cari borç YOK (K-19). Araçlar ekranında kira paneli + rapor (iki platform). API `rental/warnings`, `rental/report`,
  `{id}/rental-contract`.
- **Testler:** `KiraSozlesmesiTests` KS1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-41 periyodik kontrol (F-116; Migration115)
- **Değişiklik:** Migration115. Muayene/kontrol türüne "Periyodik kontrol"; bu türde kontrolü yapan yetkili kişi/firma ve rapor
  no zorunlu (araç ve ekipman). Rapor, gösterge paneli ve Excel içe aktarma etiketleri güncel. İki platform.
- **Testler:** `PeriyodikKontrolTests` PK1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-46 zimmet beklenen iade tarihi + gecikme (F-081; Migration116)
- **Değişiklik:** Migration116 `assignment_due_dates`. Zimmete beklenen iade tarihi; gecikenler listesi (kaç gün gecikti),
  iade edilince düşer. Zimmet ve personel ekranlarında (iki platform). API `GET /api/assignments/returns`, `PUT …/due-date`.
- **Testler:** `ZimmetIadeTests` ZI1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-47 zimmet tutanağı (F-082; Migration117)
- **Değişiklik:** Migration117 `assignment_receipts`. Tutanak oluşturulurken zimmet listesi dondurulur (sonradan değişmez),
  `ZT-yıl-` numarası; personel kabul/ret eder (ret gerekçeli, karar bir kez); PDF çıktısı (firma adıyla). Personel ekranında
  (iki platform). API `/api/assignments/receipts` (+ `/pdf`, `/decision`).
- **Testler:** `ZimmetTutanakTests` ZT1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-48 ekipman taşıma + konum geçmişi (F-058; Migration118)
- **Değişiklik:** Migration118 `equipment_moves`. `EkipmanTasimaService.Tasi`: şube/konum güncellenir, geçmişe kimden → kime,
  kullanıcı, not yazılır; tekrar gönderim zararsız; hedef şube firmaya ait ve kullanıcının kapsamında olmalı. Ekipman ekranında
  (iki platform). API `POST /api/equipment/{id}/move`, `GET …/moves`.
- **Testler:** `EkipmanTasimaTests` ET1–4. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-44 şüpheli yakıt kayıtları (F-117)
- **Değişiklik:** `YakitSupheService.Supheliler` — kayıt değişmez, yalnız işaret: sayaç geri, şüpheli sıçrama, çok büyük litre,
  aracın kendi ortanca tüketiminden 2,5 kat sapma (en az 4 karşılaştırılabilir kayıt varsa), aynı gün aynı araca birden fazla
  dağıtım. Geçersiz sayılan sayaç sapmaya girmez. Yakıt ekranında açılır liste (iki platform). API `GET /api/fuel/suspicious`.
- **Testler:** `YakitSupheTests` YS1–4. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-49 formda QR / barkod okutma (F-139, F-026)
- **Sorun:** QR etiketi vardı ama yalnız genel aramada okunuyordu; yakıt ve zimmet formlarında araç/alet elle aranıyordu.
- **Değişiklik:** `KodOkutmaService.Bul`: araçta iç kod ya da plaka (boşluksuz), ekipman/malzemede kod — büyük/küçük harf ve okuyucunun
  satır sonu önemsiz, yalnız TAM eşleşme; iki eşit eşleşmede hiçbiri seçilmez (iç kod plakaya üstün). Yalnız seçer, kayıt formun
  kendi kurallarından geçer. Yakıt dağıtımı ve zimmet formunda "QR / barkod okut" kutusu (Enter); zimmet teslimde okutulan her
  kalem listeye eklenir (art arda okutma). İki platform. API `GET /api/scan/resolve?type=&code=`.
- **Testler:** `KodOkutmaTests` KO1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-50 zimmet kit (set) şablonu (F-083; Migration119)
- **Sorun:** her yeni işçiye aynı alet/malzeme listesi tek tek seçiliyordu.
- **Değişiklik:** Migration119 `assignment_kits`. `ZimmetKitService`: teslim listesi adlandırılıp kit olarak saklanır (aynı adlı kit
  güncellenir), silinebilir; kit yalnız şablondur — hiçbir hareket yazmaz. Kit seçilince kalemler teslim listesine yüklenir, teslim
  her zamanki tek işlemli toplu teslimden geçer. Kurallar toplu teslimle aynı (ekipman 1 adet ve tekrar yok, kalem firmaya ait);
  silinmiş kalem "mevcut değil" görünür ve yüklenmez. Zimmet ekranında (iki platform). API `GET/POST /api/assignments/kits`,
  `DELETE …/{id}`. "Zimmet talebi" kısmı yapılmadı (mevcut Talepler ekranı kullanılır — bkz. 11).
- **Testler:** `ZimmetKitTests` ZK1–4. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-43 günlük makine kontrol listesi (F-087; Migration120)
- **Sorun:** vardiya başı makine kontrolünün (İSG) kaydı yoktu.
- **Değişiklik:** Migration120 `daily_check_templates` + `daily_checks`. `GunlukKontrolService`: firmanın madde listesi (yalnız
  yönetici düzenler; yoksa varsayılan 10 madde), araç başına günlük kontrol kaydı (madde: uygun / uygun değil + not; değişmez kayıt,
  tekrar gönderim tek kayıt). Uygun olmayan madde varsa ve istenirse aynı anda "Arıza Bildir" (M-35) çalışır: araç Arızalı, taslak
  arıza iş emri, kayda bağ; açık arıza zaten varsa iş emri açılmaz, kontrol yine kaydedilir ve uyarı döner. Araç detayında
  (iki platform). API `GET/PUT /api/daily-checks/items`, `POST /api/vehicles/{id}/daily-check`, `GET …/daily-checks`.
- **Testler:** `GunlukKontrolTests` GK1–5. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-38 arıza duruş süresi raporu (F-080)
- **Değişiklik:** durum akışı M-35 ile zaten var (arızalı → iş emri → tamamlandı). `WorkOrderService.DurusRaporu`: dönemdeki arıza
  iş emirlerinden araç başına arıza sayısı, açık arıza ve toplam duruş saati (bildirim → tamamlanma; açık arıza bugüne kadar; döneme
  taşan kısım kesilir; iptal sayılmaz). Araç listesinde "Arıza duruş süresi raporu" (iki platform). API `GET /api/vehicles/downtime`.
- **Testler:** `DurusRaporuTests` DR1–3. Tam takım (B5+B6) 4385/4385, 1 sa 54 dk (2026-10-20, `artifacts/dw_tam_b56.trx`).

### B6 — M-40 iç kira fiyatıyla şantiye makine maliyeti (F-085, F-086) — ERTELENDİ
- İsteğe bağlı ve büyük (L); K-02'ye (ön muhasebe + Logo/Mikro aktarımı) bağlı. Önerim: B8 (muhasebe/aktarım) işleri bittikten sonra
  ele alınsın; maliyet merkezine iç kira yazımı muhasebe aktarım biçimiyle birlikte tasarlanmalı. 11'e not düşüldü.

### B7 — M-55 web ↔ masaüstü çıktı ve zorunlu alan eşitliği (F-054, F-055)
- **Sorun:** web'de 6 listede Yazdır yoktu; masaüstünde Muayene'de Yazdır yoktu; iki ortamda zorunlu olan 4 alan masaüstünde `*` taşımıyordu.
- **Değişiklik:** web Malzeme, Araç, Günlük Faaliyet, Personel, Satın Alma, İş Emri listelerine "Yazdır": Excel ile AYNI uç ve AYNI
  süzgeç yolu (ortak `Disari(pdf)`), yalnız `format=pdf` eklenir → iki çıktı ayrışamaz. Masaüstü Muayene'ye Yazdır (Excel ile aynı tablo).
  Masaüstünde `*`: Araç şube, Giriş-Çıkış personel, Yakıtı Veren, Sayım gerekçesi (kurallar zaten iki ortamda vardı, yalnız işaret eksikti).
- **Eskimiş bulgular (iş gerekmedi):** web Ekipman QR zaten var (liste satırı); masaüstü içe aktarmada ön kontrol aktarım onay
  penceresinde satır satır gösteriliyor; ekran logu iki ortamda da `ScreenAuditMap` ile modül bazlı (kayıt bazlı geçmiş M-56'da).
- **Testler:** `PariteYazdirTests` PY1–3 (20 vaka: 7 uçta PDF + aynı uçta Excel, süzgeçli adres; web kaynak kilidi; zorunlu işaretler).
  Riskli paket (bu ekranlara dokunan 40 test sınıfı) 398/398; arayüz 48/48. Mutasyon: kritik alan değil (çıktı/görünüm), uygulanmadı.

### B7 — M-54 liste standardı: en yoğun 10 ekranda Excel'e Aktar + Yazdır, iki ortamda (F-057)
- **Sorun:** Faturalar, Cariler, Kasa/Banka, Tahsilat/Ödeme, Talepler, Stok Hareketleri, Yakıt, Bakım, Projeler, Belgeler listelerinde
  iki ortamda da Excel/Yazdır yoktu (liste kuralı 2: filtrelenmiş TÜM sonuç dışa aktarılır).
- **Değişiklik:** her ekrana API `…/export` ucu (liste ucuyla AYNI süzgeç + `format=pdf`, `export` yetkisi), ekran başına tablo modeli
  (`…ListeTablosu`, ekran kolonları), web ve masaüstünde Excel + Yazdır. Ortak `ListeDisaAktarim.TumSayfalar`: sayfalı servisi servisin
  kendi sayfa sınırıyla sayfa sayfa okur (üst sınır 50.000). Gizli alanlar (fatura tutarı, cari bakiye, kasa bakiyesi, finans tutarı)
  dışa aktarımda da gizli — kolon HİÇ konmaz. Web'de ve masaüstünde liste ile dışa aktarım TEK süzgeç kaynağını kullanır
  (FiltreQuery / SorguParcalari / Suzgec / …Sayfasi). Talepler ve stok hareketlerinde ekranın 200/1000 penceresi dışa aktarımı
  kısıtlamaz. Projeler ve Belgeler masaüstünde sunucudan okunduğu için masaüstü dışa aktarımı da sunucunun aynı ucundan indirir.
- **Testler:** `ListeDisaAktarimTests` LD1–13 (sayfa birleştirici; ekran başına: dışa aktarım satır sayısı = liste toplamı, süzgeçler,
  PDF, gizli alan kolonsuz; export yetkisi olmayan kullanıcı 403); ekran başına mutasyon (toplam 18/18: yetki, süzgeçler, gizleme,
  sayfa sınırı, yön/toplam); ekran başına riskli paketler (165 · 563 · 100 · 68 · 149 · 199 · 112 · 135 · 22 · 23) geçti; arayüz 48/48.
  `YakitEkranAramaTests` YKE1 kilidi ortak sayfa okuyucuya göre güncellendi (sayfalı sorgu şartı aynen).
