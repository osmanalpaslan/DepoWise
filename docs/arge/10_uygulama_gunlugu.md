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
- **Yayın notu:** kapı bugün kimseyi etkilemez (canlıdaki yayınların en düşük sürümü düşük). İlk zorunlu yayında en düşük sürüm bilinçli yükseltilmeli → `12`.
