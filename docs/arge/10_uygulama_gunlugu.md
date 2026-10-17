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
