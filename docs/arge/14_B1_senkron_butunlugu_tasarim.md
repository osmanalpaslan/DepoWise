# 14 — B1 tasarımı: veri güvenliği ve eşitleme bütünlüğü (2026-10-17)

> Kapsam (kullanıcı seçimi, `13`): M-02, M-03, M-04, M-05, M-06, M-07, M-08, M-15, M-17, M-78 (K-21 a, K-22 b), M-79, M-80,
> M-84, M-85, M-20 + K-04/K-06/K-07 (M-16, M-18). Kanıtlar: `07` §2–§5, `04`. **Senkron protokolü değişir** — CLAUDE.md
> "onay gerekir" şartı kullanıcının bu maddeleri seçmesiyle karşılandı (`09` başlık notu). Yayın yok (AR-GE dalı).
> Eski masaüstü sürümleriyle uyum zorunlu: yeni sunucu eski istemciyi bozmaz; eski istemci yeni uçları çağırmaz.

## 1. Çekme imleci: sunucu işlem kimliği ufku (M-03 — R1, K3, K4)
**Sorun:** çekme `updated_at > since` (istemci saati) → geç gelen/geri damgalı satır kaçar; ayrıca `AlinanWatermark` hiç atanmıyor
→ her turda tam paket (canlıda ≈4–6 GB/ay DB trafiği, `07` §7).
**Çözüm (UYGULANAN, 2026-10-17 — Migration099; ilk taslaktaki kilit+dizi fikrinin yerine):**
- Her senkron tablosuna `server_txid BIGINT NULL` + `server_txid` indeksi (company_id katılmaz: Migration062 geri alma adımını bozuyordu).
- PG tetikleyicisi `BEFORE INSERT OR UPDATE`: `NEW.server_txid := pg_current_xact_id()` (satırı yazan işlemin kimliği). Kilit YOK →
  yazanlar birbirini beklemez (taslaktaki firma kilidi yoğun push'ta yazımları sıraya sokardı).
- **Ufuk** = `pg_snapshot_xmin(pg_current_snapshot())`: hâlâ süren en eski işlem. Çekme yalnız `after ≤ server_txid < ufuk`
  (ilk çekimde `server_txid IS NULL OR < ufuk`) satırları verir, yanıt `nextAfter = ufuk`. Süren işlemin satırı bir sonraki tura
  kalır, asla atlanmaz (uzun işlem açıkken sonra başlayıp önce biten işlem de bekletilir).
- Değişen ebeveynin (ParentReplaceChildren) değişmemiş çocukları da pakete girer — alıcı çocuk kümesini silmesin.
- SQLite sunucu rolü (yerel/test API): satır başı sayaç `sync_txid_counter`, tetikleyiciler yalnız `sync_server_role` satırı varsa
  çalışır; o satırı yalnız API (`ServerServices`) ekler → masaüstünde tetikleyici etkisiz, çekilen değer olduğu gibi saklanır.
- Uç: `GET /api/sync/business-pull-v2?after=` (eski `business-pull` aynen kalır). Masaüstü imleci `sync_pull_txid`; uç yoksa
  (404, eski sunucu) eski uca düşer. İmleç yalnız uygulama başarılı olunca ilerler. `AlinanWatermark` ölü kodu kaldırıldı.
- Sayfalama (`limit/hasMore`, M-17) ve sunucu `epoch` (M-80) ayrı adımlarda bu uca eklenir.
**Kabul:** (a) geriye damgalı geç satır başka makinede iner (test: A çevrimdışı eski damgalı satır gönderir, B'nin imleci ileridedir → B
satırı alır); (b) değişiklik yokken çekme 0 satır; (c) 300 bin satırda ilk eşitleme sayfalı, bellek tavanı sabit, kesintiden sonra
devam; (d) eski masaüstü eski uçla çalışmaya devam eder.

## 2. Gönderme imleci: yerel değişiklik sayacı (B2-1, K4 push tarafı, yankı)
- SQLite'ta `local_change_seq` kolonu + tetikleyici: yerel yazımda `app_counters`'tan artan numara. Çekme uygulanırken tetikleyici
  devre dışı (`sync_ctx` geçici tablosu) → inen satır yeniden gönderilmez (yankı biter).
- Push watermark = gönderilip **sunucunun kabul ettiği** son `local_change_seq` (saat bağımsız).
**Kabul:** saat geri alınmış makinede yeni kayıt gönderilir; çekilen satır push paketinde yer almaz.

## 3. Bilgisayar kodlu numara ve kod çakışması (M-78 — K1; K-21 a, K-22 b)
- Kurulumda makineye kısa kod (`A3` vb., sunucudan tekil atanır; çevrimdışı ilk kurulumda geçici kod + ilk bağlantıda kesinleşir).
- Belge/fatura/sipariş/iş emri numarası: `ÖNEK-YIL-MAKİNE-SIRA` (`TLP-2026-A3-0042`), sıra yerel ve makineye özel → çakışmaz.
  Eski numaralar aynen kalır (yalnız yeni kayıtlar yeni biçim).
- Malzeme/araç kodu (kullanıcının yazdığı doğal anahtar) çakışırsa (K-22 b): sunucu ikinci kaydı reddetmek yerine kodu `KOD-2`
  yapar, kaydeder ve "kod otomatik değiştirildi" bildirimi + listesi üretir (mükerrer kart birleştirme ileride).
**Kabul:** iki çevrimdışı makine aynı gün fatura/belge keser → ikisi de sunucuda; aynı malzeme kodu → `KOD` ve `KOD-2`, bildirim var.

## 4. Reddedilen kayıtlar görünür ve yeniden gönderilebilir (M-15, M-79, M-80 — R2, R3, K2, K6, K7)
- Masaüstü `sync_rejections` tablosu: tablo, kayıt id, neden, ilk/son deneme, durum. Kalıcı red ve poison satırları buraya; watermark'ın
  altında kalsa da kaybolmaz. Ekran: üst bardaki eşitleme rozetinden "Gönderilemeyen kayıtlar" listesi (düzelt → yeniden gönder / yoksay).
- "Yereli sıfırla": önce push; bekleyen/reddedilen kayıt varsa sıfırlama durur ve listeyi gösterir (M-79).
- Sunucu `epoch` (yedekten dönüşte yeni kimlik): istemci epoch değişimini görünce push watermark'ını sıfırlar → tam yeniden gönderim
  (id + operation_id ile tekrar zararsız) (M-80).
**Kabul:** UNIQUE çakışması/ebeveyn yok kaydı listede görünür; sıfırlama gönderilmemiş kaydı silmez; epoch değişince kayıtlar geri gelir.

## 5. Firma ve defter korumaları (M-02, M-05)
- `UpsertRow`: mevcut satırın `company_id`'si oturumdakinden farklıysa yazma reddedilir + güvenlik günlüğü (M-02).
- Defter tabloları (`stock_movements`, `party_ledger`, `finance_txns`, `assignment_movements`…): push ile yalnız **ekleme**; mevcut satırın
  miktar/tutar alanları güncellenemez (iptal = ters kayıt) (M-05).
**Kabul:** başka firmanın id'siyle push reddedilir (test); defter satırının miktarını değiştiren push yok sayılır ve raporlanır.

## 6. Web idempotency (M-04 — K-08)
- Web formu kayıt açılışında `operationId` üretir, API'ye gönderir; sunucu üretmez. UUA6 testi "çift istek = tek kayıt" olarak düzeltilir.

## 7. Çakışma politikası (K-04 a, K-06 a, K-07 a → M-16, M-18)
- Onay: ilk onay kazanır; ikincisi çakışma listesine. Yakıt/bakım/talep: ezme yok → çakışma kaydı (`ConflictTracked` genişler).
- Çevrimdışı eksi stok: kabul + "eksi stok oluştu" bildirimi/listesi (sunucu push sonrası bakiye kontrolü).
- CLAUDE.md §3'e K-05 istisnası (bakım malzemesi + açılış stoğu eksiye düşebilir, uyarıyla) yazılır.

## 8. Hızlı performans kazançları (M-84, M-85, M-20) — B1 içinde, düşük risk
- Yanıt sıkıştırma (gzip/Brotli) API'de + masaüstü `AutomaticDecompression`.
- `SearchGridAll` tek geçiş; açılışta/boşta `PRAGMA optimize` + periyodik `ANALYZE`.

## 9. Diğer B1 maddeleri
- M-06 masaüstü denetim kaydı sunucuya (senkron tablosu olarak, yalnız ekleme).
- M-07 SQLite paket açığı: .NET 10 dalında `Microsoft.Data.Sqlite 10.0.13` ile kapandı (`dotnet list --vulnerable` temiz).
- M-08 yedek: günlük, şifreli (AES, anahtar kullanıcının kasasında), geri yükleme provası; bulut zamanlayıcı (GitHub Actions — depo
  özel yapılınca dakika sınırı ölçülür, ücret gerekirse sorulur).

## 10. Uygulama sırası ve test stratejisi
1. M-02, M-05, M-04 (küçük, bağımsız) → hedef testler + negatif testler.
2. Sunucu `server_seq` + tetikleyici + v2 çekme ucu (PG test sunucusunda) → protokol testleri (geç satır, sıralama, sayfalama, eski uç).
3. Masaüstü v2 çekme + yerel değişiklik sayacı + yankı kesme → iki istemcili uçtan uca test (M-68 ile birlikte).
4. Red listesi + sıfırlama + epoch.
5. Bilgisayar kodlu numara + kod çakışması.
6. Çakışma politikası, eksi stok bildirimi.
7. Sıkıştırma, SearchGridAll, optimize; M-06; M-08.
Her adım sonunda: ilgili testler + tam takım; yeni testler önce eski kodda KIRMIZI görülür. Geri dönüş: her adım ayrı commit.
