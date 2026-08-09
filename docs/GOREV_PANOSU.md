# 🗂️ Görev Panosu — Nerede Kaldık? (Çok Görevli Takip)

> **Bu dosya ne işe yarar?** Aynı anda birden fazla bağımsız işi yürütürken, her işin **nerede kaldığını**
> ve **sıradaki adımını** tek yerde tutar. "X işinde nerede kalmıştık?" / "Y'ye devam edelim" dediğinde
> Claude cevabı **buradan** verir. Amaç: işler arasında geçiş yapınca hiçbir şeyin unutulmaması.
>
> **Nasıl güncel kalır?** Claude her anlamlı ilerlemeden sonra ilgili görevin **Durum / Nerede kaldık /
> Sıradaki adım** satırlarını yeniler ve commit'ler. Özet burada; teknik ayrıntı `docs/` içinde.
>
> **İlişki:** `DEVAM.md` = oturum girişi (kısa) · `docs/YARIM_KALAN_ISLER.md` = tüm bekleyen işler havuzu ·
> **bu dosya = aktif paralel işlerin durumu ve devam noktaları.**
>
> Son güncelleme: **2026-08-09**

---

## 🔒 ALTIN KURAL — Babanın gerçek verisine DOKUNMA

Bu geçiş boyunca **her işte** geçerli, istisnasız:

- **Canlı sunucudaki (`depowise-erp.fly.dev`) gerçek firma verisi asla silinmez, taşınmaz, üzerine yazılmaz.**
- PostgreSQL denemeleri **gerçek verinin KOPYASIYLA** ve **ayrı bir veritabanında** yapılır — canlıya dokunmaz.
- Eski SQLite sunucusu, yeni yapı kanıtlanana kadar **canlı ve yedekte kalır**. Baban kesintisiz kullanır.
- Test/simülasyon araçları yalnız **yerel sunucuda** ya da **ayrı test firmasında** çalışır (canlıya karşı çalışmayı reddeder).
- Silme/sıfırlama gibi geri alınamaz işlemler, açık onay olmadan **yapılmaz**.

---

## ▶️ AKTİF GÖREVLER

### Görev A — PostgreSQL geçişi (sunucu + web)
**Amaç:** Sunucu (API) ve web'in veritabanını SQLite'tan **PostgreSQL**'e (çok kullanıcıya ve yedekli
sunucuya uygun, ücretsiz başlanabilen veritabanı) taşımak. **Masaüstü SQLite'ta KALIR** (çevrimdışı
çalışması bundan geliyor). **Yeni repo AÇILMAZ** — mevcut projede, adım adım.

- **Durum:** ✅✅ **CANLIYA ALINDI (2026-07-24) — SUNUCU + WEB ARTIK PostgreSQL'DE.** Kullanıcı onayıyla
  üretim geçişi yapıldı: (1) güncel kod `depowise-erp`'e deploy (SQLite'ta doğrulandı), (2) canlının TAZE
  kopyası → `depowise_prod` yeniden yüklendi (8781 satır), (3) Fly secret `DEPOWISE_PG_URL` ayarlandı → sunucu
  **PG'ye geçti**, (4) doğrulandı: `/health` 200, giriş + TÜM okuma uçları + `server/status` (`dbSizeMb`
  `pg_database_size`'tan 14,2 → PG kesin) 200; web (API'yi HTTP ile çağırır, kendi DB'si yok) → otomatik PG.
  Masaüstü SQLite'ta kaldı (eşitleme API üzerinden PG'ye yazar). 🔒 **Eski SQLite `/data/depowise-server.db`
  el değmeden duruyor (yedek)** → geri dönüş: `flyctl secrets unset DEPOWISE_PG_URL` + redeploy. **580 test yeşil.**
- **⚠️ Geçiş sonrası izleme:** Baba normal kullanımda bir sorun bildirirse (yazma/eşitleme kenar durumu)
  önce secret'ı kaldırıp SQLite'a dön (anında), sonra hatayı gerçek veriyle çöz. Eski SQLite verisi Jul 22'den
  beri değişmemişti (kopya güncel).
- **✅ CANLI PROVA — gerçek veriyle bulunan 2 geçiş hatası (2026-07-24, SQLite'ta gizliydi):**
  1. `BranchService.ListForLogin`: SELECT projeksiyonundaki boolean ifade (`... IS NOT NULL AND <>''`) PG'de
     gerçek boolean döner → `GetInt64` patlıyordu (login 500) → `CAST(... AS INTEGER)`.
  2. 11 liste sorgusunda `@x IS NULL` → PG DBNull parametrenin tipini çıkaramıyor (42P08) → `CAST(@x AS TEXT)`.
  Araçlar: `SqliteToPgCopier` (kopya) + `tools/DepoWise.Migrate` (runner). Neon: proje `depowise-dev`, ayrı DB
  `depowise_prod` (testler `neondb`'yi siler, prod'a dokunmaz). Canlı sunucu `depowise-erp` = **fra (Frankfurt)**,
  Neon ile aynı bölge → düşük gecikme. Ücretsiz plan (0,5 GB) 3,6 MB için fazlasıyla yeterli.
- **Nerede kaldık:** Kullanıcı A'ya başlamak istedi. İki karar eklendi: (1) PostgreSQL web'i baştan
  YAZDIRMAZ (görünüm aynı kalır); web'i beğenmeme ayrı iş → **Görev C** (tasarım, ertelendi, istekler
  toplanacak). (2) Geçiş öncesi **her ekranın masaüstü↔web alan+mantık paritesi** sağlanacak — hem
  tutarlılık hem PostgreSQL tip-hazırlığı. Başlangıç yöntemi: **önce tüm ekran haritası** (kullanıcı seçti).
- **✅ FAZ 0 TAMAMLANDI (2026-07-23) — 7 yüksek öncelikli ekran denetlendi:**
  **Araçlar** (1 bulgu, masaüstü de etkilendi) · **Malzemeler** (tam parite, bulgu yok) ·
  **Personel** (tam parite, bulgu yok) · **Stok Giriş/Çıkış** (2 bulgu) · **Günlük Faaliyet** (2 bulgu) ·
  **Yakıt** (3 bulgu) · **Bakım Takibi** (2 bulgu — en önemlisi: iptal gerekçesi web'de hiç alınmıyordu,
  audit kaydı sabit metinle doluyordu → düzeltildi). Toplam 11 gerçek bulgu, hepsi düzeltildi; 10'u
  yalnız web'i etkiledi. Raporlar `docs/tests/*_Parite_Denetimi.md` (7 dosya).
  Sütun listesinin web'de elle senkron tutulması (ortak bakım riski) = PostgreSQL Faz 3'te web ortak
  katmana bağlanınca kökten çözülecek.
- **Kullanıcı kararı (2026-07-23):** Web deploy VE masaüstü 1.0.88 paketi **BEKLETİLİYOR** — ikisi de
  bilinçli olarak ertelendi (masaüstü: sonraki değişikliklerle birlikte tek pakette çıkacak). Kod git'te
  hazır ve test edilmiş durumda; kullanıcı ne zaman isterse deploy/paketleme tek komutla yapılabilir.
- **⚠️ Deploy notu:** Web'deki tüm düzeltmeler (Stok/Günlük Faaliyet/Yakıt/Bakım) henüz **canlıya
  alınmadı** — yalnız git'e commit edildi, kullanıcı deploy istediğinde yapılacak.
- **✅ FAZ 1 TAMAMLANDI (2026-07-23):** Kullanıcı bulut (Neon) seçti, GitHub ile giriş yaptı, API anahtarı verdi.
  - `neonctl` kuruldu; API anahtarı `.env.test.local`'e (git-ignored) yazıldı.
  - **Yeni proje:** `depowise-dev` (id `nameless-shape-66675056`), **PostgreSQL 17**, **Frankfurt** (aws-eu-central-1),
    org `alpdepo`. Eski proje (`alpdepo`/autumn-morning-75319830) **silinmedi, dokunulmadı** (yan yana durabilir).
  - Bağlantı adresi `.env.test.local` → `DEPOWISE_PG_URL` (Npgsql biçimi, git'e girmez).
  - **Bağlantı DOĞRULANDI:** `PostgresConnectionTests` 2/2 geçti (`SELECT version()` → PostgreSQL 17, `SELECT 1+1`).
  - 🔒 Neon deneme veritabanı BOŞ; babanın canlı verisiyle ilgisi yok (altın kural korunuyor).
  - Not: ücretsiz plan (0,5 GB, 100 saat/ay, 100 proje) geliştirme için fazlasıyla yeter. En düşük ücretli
    "Launch": sabit ücret yok, kullandıkça öde (depolama ~0,35 $/GB-ay, işlem ~0,106 $/saat).
- **FAZ 2 — GERÇEK KAPSAM KEŞFEDİLDİ (2026-07-23):** İş, "52 migration çevir"den ÇOK daha büyük.
  Kod tip düzeyinde SQLite'a kilitli:
  - **84 dosya** doğrudan `SqliteConnection` tipini kullanıyor (`DbConnection` taban tipine geçmeli — ikisini
    de Npgsql + SQLite destekler).
  - **1216 parametre** `$` önekiyle (`AddWithValue("$x", ...)`); Npgsql `$` kabul etmez, `@` ister.
  - SQLite'a özel SQL: `INSERT OR IGNORE/REPLACE` (19), `strftime/datetime` (7) → PostgreSQL karşılığı.
  - SQLite'a özel çalışma-anı: `CreateFunction` (Türkçe arama), `CreateCollation` (Türkçe sıralama),
    PRAGMA'lar (32) → PostgreSQL'de ILIKE/ICU collation ile çözülecek, PRAGMA yok.
  - **İyi haber:** çoğu MEKANİK ve GÜVENLİ — her adımda 569 test masaüstünün (SQLite) çalıştığını kanıtlar,
    baban hiç etkilenmez. ID'ler zaten TEXT/GUID (PostgreSQL'e uygun), AUTOINCREMENT neredeyse yok (1).
- **Önerilen plan (adım adım, her biri test edilir, küçük commit'ler):**
  1. ✅ **TAMAM (2026-07-23) — Temel:** kod `SqliteConnection` yerine `DbConnection` (her veritabanı) diyor.
     Yardımcılar: `DbCommandExtensions` (`AddWithValue` taban `DbCommand`'de + `BeginImmediate` — SQLite'ta
     `deferred:false` korunur). Factory `DbConnection` döndürüyor (SQLite içeride). ~130 dosya çevrildi
     (Infrastructure + Desktop + API + testler). BackupService/factory SQLite'a özel bırakıldı.
     **569 test yeşil + 4 proje 0 hata → masaüstü (SQLite) hiç bozulmadı.**
  2. ✅ **TAMAM (2026-07-23) — Parametreler:** SQL parametre önekleri `$` → `@` (Npgsql `$` kabul etmez;
     SQLite ikisini de kabul eder). 95 dosya, regex `\$([A-Za-z_]\w*)` → `@\1` (C# `$"..."` interpolasyonu
     `$"` olduğu için TAKILMAZ). Dışlanan 2 dosya: UpdateInstaller (PowerShell betiği üretir), Postgres
     bağlantı testi. **Yakalanan yanlış-pozitif:** PasswordHasher'ın hash biçimi `pbkdf2$sha256$...` idi;
     `$sha256` yanlışlıkla `@sha256`'ya döndü → parola doğrulama bozuldu → 89 test çöktü → geri düzeltildi.
     (Sentinel sanılan `@all`/`@me`/`@co` aslında gerçek SQL param'mış — gerçek sentinel `"__all__"`, dokunulmadı.)
     **569 test yeşil + 4 proje 0 hata.**
  3. ✅ **TAMAM (2026-07-23) — Lehçe SQL:** SQLite'a özel yapılar iki veritabanında da çalışır hale geldi:
     `IFNULL`→`COALESCE` (5, ikisi de destekler) · `INSERT OR IGNORE`→`ON CONFLICT DO NOTHING` (18) ·
     `INSERT OR REPLACE`→`ON CONFLICT(pk) DO UPDATE` (1, company_purges). Ortak karşılığı olmayanlar için
     yeni `SqlDialect` yardımcısı (bağlantıya göre): `NowMs` (strftime↔extract epoch), `NewHexId`
     (randomblob/hex↔gen_random_uuid), `AutoIncPk` (AUTOINCREMENT↔IDENTITY) — migration 011/030/034/035/037'de.
     **569 test yeşil (SQLite tarafı) + 4 proje 0 hata.** ⚠️ PostgreSQL tarafı Adım 4'te Neon'da doğrulanacak.
  4. ✅ **TAMAM (2026-07-23) — Migration'lar + uçtan uca:** 52 migration Neon'da temiz kuruluyor
     (`PostgresMigrationTests`) VE gerçek servis işlemleri PostgreSQL'de çalışıyor (`PostgresEndToEndTests`:
     malzeme/stok/araç/bakım/talep/tenant/idempotency/negatif-stok/generic-upsert). Çözülen farklar:
     INTEGER→BIGINT (zaman damgası taşması), PRAGMA/sqlite_master→information_schema (`DbIntrospect`),
     GROUP BY bare-kolon→pencere fonksiyonu/PK gruplama, dinamik `$`→`@` parametreler (UpsertRow/LookupService),
     savepoint-yerine-transaction-abort farkı tespit edildi. **573 test yeşil (569 SQLite + 4 PG).**
  5. ✅ **TAMAM (2026-07-23) — Çalışma-anı Türkçe arama/sıralama:** SQLite'ta çalışma-anı kaydedilen Türkçe
     `like()` + `TRNOCASE`'in PostgreSQL karşılığı kuruldu. **Migration053** PG'de 3 ICU collation açar:
     `dw_tr` (Türkçe küçük-harf, İ→i/I→ı), `nocase` (harf-duyarsız eşitlik), `trnocase` (Türkçe sıralama:
     Ç, C'den sonra). Böylece mevcut `COLLATE NOCASE`/`COLLATE TRNOCASE` SQL'leri PG'de **değişmeden** çalışır.
     `LIKE` operatörü PG'de ezilemediğinden yeni `SqlDialect.LikeTr` (SQLite: düz LIKE / PG:
     `lower(col COLLATE dw_tr) LIKE lower(param COLLATE dw_tr)`) — GridQuery + 5 arama sorgusunda. Ayrıca
     grid iç sorgusundaki SQLite-özel fonksiyonlar için `SqlDialect.PortableSql` (PG: `printf`→`to_char`,
     `GROUP_CONCAT`→`string_agg`; SQLite'ta aynen döner). SQLite yolu HİÇ değişmedi → **574 test yeşil**
     (569 SQLite + 5 PG; yeni `PostgresTurkishSearchTests` İ-katlaması + grid + TRNOCASE + NOCASE'i Neon'da
     kanıtlar). PRAGMA'lar SQLite'a özel bırakıldı (`DatabaseHealth` sunucuda Adım 6'da uyarlanacak).
  6. 🟡 **KOD KISMI TAMAM (2026-07-23) — Sunucuyu PG'ye bağlama altyapısı:** Üretim `PostgresConnectionFactory`
     (DepoWise.Api; Npgsql YALNIZ sunucuda, masaüstü SQLite kalır) + `ServerServices` artık `DEPOWISE_PG_URL`
     env değişkeniyle factory seçiyor — **değişken YOKSA eskisi gibi SQLite** (babanın canlı sunucusu birebir
     aynı). `DatabaseHealth` lehçe-duyarlı yapıldı (PG'de PRAGMA yok → FK=true, journal="postgres", gerçek
     write/read; `_health_check` tablosundan taşan/PK-null sorunları giderildi). **575 test yeşil** (yeni
     `PostgresServerHealthTests` gerçek factory + health'i Neon'da kanıtlar). Sağlık ucu DB boyutunu PG'de
     `pg_database_size` ile ölçer.
  7. ✅ **TAMAM (2026-07-24) — Sunucu SİLME yolları PG-güvenli:** Firma kalıcı silme (ADR-083) + iş-verisi
     sıfırlama + 2 dev/admin sıfırlama ucu artık PostgreSQL'de FK-güvenli çalışıyor. **Kritik bulgu (gerçek
     testle):** Neon'un tablo-sahibi rolü FK'yi KAPATAMAZ — ne `session_replication_role=replica` ne
     `ALTER TABLE ... DISABLE TRIGGER ALL` (ikisi de 42501 izin reddi). Çözüm: yeni `DialectPurge` —
     (1) hedefleri geçişli referans eden tüm tabloları **kapanış (closure)** ile toplar (company_id'siz junction
     tabloları + ör. `vehicle_meter_logs` gibi bağımlıları da → yetim/FK-ihlali kalmaz),
     (2) company_id varsa doğrudan, yoksa ebeveyne JOIN ile siler,
     (3) **savepoint + retry-fixpoint** ile FK sırasını kendiliğinden çözer (FK kapatmadan). Ortak
     `RunFkSafe` yardımcısı Program.cs'in iki sıfırlama ucunda (dev-reset, admin reset-test-data;
     `sqlite_master`→`DbIntrospect.ListTables`, `PRAGMA`→retry). **SQLite yolları HİÇ değişmedi.**
     **578 test yeşil** (yeni `PostgresPurgeTests`: kalıcı silme + iş sıfırlama + RunFkSafe Neon'da kanıtlar).

- **⚠️ Bilinen takip işleri (sağlamlık):**
  1. ✅ **ÇÖZÜLDÜ (2026-07-24) — ApplyCore satır-hatası deseni:** PG'de bir satır hatası tüm transaction'ı
     abort ediyordu (25P02). Artık `ApplyTableRows` iki kademeli: HIZLI YOL — tüm tablo tek savepoint'te
     (geçerli veride ekstra maliyet ~yok, normal durum); KURTARMA — bir satır patlarsa tablo geri alınıp
     satırlar satır-başı savepoint ile TEKRAR uygulanır (yalnız hatalı satır atlanır). Satır-başı maliyet
     YALNIZ hata olan nadir tabloda ödenir. SQLite yolu DEĞİŞMEDİ. `PostgresSyncRecoveryTests` Neon'da
     kanıtlar (FK-ihlali satırı atlanır, geçerli yazılır, push bütün olarak batmaz). **579 test yeşil.**
  2. ✅ **ÇÖZÜLDÜ (Adım 5, 2026-07-23):** Türkçe arama/sıralama artık PG'de çalışıyor (Migration053 collation'lar
     + `SqlDialect.LikeTr`/`PortableSql`). `PostgresTurkishSearchTests` Neon'da kanıtlar.
- **Dürüst not:** Bu, tüm geçişin EN BÜYÜK ve en hassas parçası — tek oturumluk iş değil. Ama her adım
  geri alınabilir + test edilir; istediğin an durulabilir. Masaüstü hiçbir adımda bozulmaz (SQLite'ta kalır).
- **Sıradaki adım — ✅ GEÇİŞ TAMAMLANDI.** Sunucu + web PostgreSQL'de (Neon `depowise_prod`), masaüstü SQLite'ta.
  Kalan takip işleri (aceleye gerek yok): (a) baba birkaç gün normal kullansın, sorun çıkarsa geri dönüş anında;
  (b) sağlamsa Neon yedek/otomatik-yedekleme ayarını gözden geçir; (c) eski `depowise-erp` SQLite yedeği bir süre
  daha volume'da kalsın, kanıtlandıktan sonra kullanıcı kararıyla temizlenir. **Görev A (PostgreSQL geçişi) bitti.**

**Yol haritası:**
| Faz | Ne yapılır | Durum |
|---|---|---|
| **0** | **Ekran denetimi + parite** — her ekran: masaüstü=web=veritabanı aynı (alan+mantık). PostgreSQL'e model hazırlığı da bu. Ekran ekran, kısa rapor + küçük commit | 🟢 başladı (haritalama) |
| 1 | Ücretsiz PostgreSQL kur, bağlantıyı doğrula | ✅ **TAMAM** — Neon (bulut, ücretsiz, Frankfurt, PG17) projesi `depowise-dev` kuruldu; Npgsql ile bağlantı 2 testle doğrulandı |
| 2 | 53 şema adımını (migration) PostgreSQL diline çevir | ✅ **TAMAM** — Neon'da temiz kuruluyor |
| 3 | Sunucu veri katmanını (okuma/yazma) PostgreSQL'e uyarla | ✅ **TAMAM** — servisler+arama+health+silme+eşitleme PG'de |
| 4 | Eşitleme kodunu iki veritabanına birden çalışır hâle getir | ✅ **TAMAM** — satır-hatası savepoint dayanıklılığı dahil |
| 5 | Gerçek verinin KOPYASIYLA prova → CANLIYA AL | ✅ **TAMAM (2026-07-24)** — sunucu+web PG'de, eski SQLite yedekte |

**Bilinen risk / not:** En çetin parça Faz 4 (eşitleme). SQLite gevşek, PostgreSQL katı tiplidir
(para yazı, tarih sayı, evet/hayır 0-1 olarak saklanıyor → her biri gözden geçirilecek). Ücretsiz
servis uzak olduğu için ağ gecikmesi olabilir; toplu sorgu gerekebilir. **Faz 0 parite işi migrasyondan
bağımsız çalışır, babanın verisine dokunmaz** (normal uygulama geliştirmesi).

---

### Görev B — Babanın masaüstü uygulaması (paralel geliştirmeler)
**Amaç:** Geçiş sürerken babanın günlük kullandığı uygulamaya istenen geliştirmeleri yapmak.
Bu görev **Görev A'dan bağımsız** ilerler; masaüstü zaten SQLite'ta kaldığı için geçişten etkilenmez.

- **Durum:** 🟢 DEVAM EDİYOR — 11 adımlık **onaylı sıra** işletiliyor (bkz. `docs/YARIM_KALAN_ISLER.md` başı).
- **Nerede kaldık (2026-08-09):** Sıranın **1. işi (Yakıt iptali, 1.0.131)** ve **2. işi (Günlük Faaliyet
  iptali → bakım/stok tutarlılığı, 1.0.132)** yayınlandı. Her ikisinde de migration YOK, canlı veri değişmedi.
- **Son biten iş (2026-08-09): sıranın 10. maddesi — kolon kataloğu tekilleştirildi.** Katalog iki
  dosyada duruyordu (elle senkron); web artık AYNI dosyayı derliyor (proje referansı değil, dosya
  paylaşımı) → ayna kopya silindi. Ayrıca yazılmış ama hiç çağrılmayan `Sanitize` 6 yere bağlandı:
  kaldırılmış bir kolon kullanıcının kaydında kalırsa artık hayalet kolon çizilmiyor.
  **Migration YOK.** Testler: 8/8, SQLite 958/0. [Rapor](tests/KolonKatalogu_Test_Report.md)
- **(aynı gün) sıranın 9. maddesi — LookupBox ortak bileşeni.** Ortak bileşen ZATEN
  vardı (13 görünüm); geride kalan 4 ekran (2 hızlı düzenleme penceresi + Araç Şablonları + Şubeler)
  ona geçirildi. Yeni bileşen yazılmadı. **Migration YOK.** Testler: 7/7, SQLite 950/0.
  [Rapor](tests/LookupBox_Ortak_Bilesen_Test_Report.md)
- **(aynı gün) sıranın 8. maddesi — çok malzemeli stok işlemi.** Depo çıkışı ve
  transferde belge başına 1 malzeme sınırı kaldırıldı (tek belge, tek transaction; bir satır
  yetersizse tamamı geri alınır). P1-7 (şube sürüm kontrolü) zaten #6'da yapılmıştı.
  **Migration YOK.** Testler: servis 8/8, API 6/6, SQLite 943/0.
  [Rapor](tests/CokluMalzeme_Stok_Test_Report.md)
- **(aynı gün) sıranın 7. maddesi — Excel içe aktarım WEB'e eklendi.** Sunucuda
  içe aktarım ucu HİÇ YOKTU; 4 uç + web ekranı + menü eklendi, masaüstüyle aynı servisler kullanıldı.
  Gerçek tarayıcıda uçtan uca doğrulandı. **Migration YOK.** Testler: API 14/14, SQLite 929/0.
  [Rapor](tests/ExcelIceAktarim_Web_Test_Report.md)
- **(aynı gün) sıranın 4, 5 ve 6. maddeleri — KOD TAMAM, YAYIN BEKLİYOR.**
  #4 Personel/Talepler çift tık · #5 Günlük Faaliyet + Bakım kaydı metadata düzenleme
  (stok/sayaç bilinçli kapsam dışı) · #6 Düzenleme kilidi **Talepler + Şube/Şantiye**'ye genişletildi
  (bu iki ekranda kilit HİÇ YOKTU — ikinci kaydeden birinciyi sessizce eziyordu).
  **Migration YOK.** Testler: SQLite 915/0. [Rapor](tests/DuzenlemeKilidi_Talepler_Subeler_Test_Report.md)
- (önceki, 2026-08-09) **Paket 1 YAYINLANDI** — masaüstü **1.0.134**.
  KD-1 (sunucuda Stok Hareketleri 3 ucu 500 veriyordu) düzeltildi + 8 firma izolasyonu açığı kapatıldı
  (T-1…T-6, Y-1, Y-2) + gerçek HTTP hattından çok-firmalı test paketi eklendi. **Migration YOK**, şema 62.
  Testler: SQLite 866/0 · PostgreSQL 35/0/0 atlandı. [Rapor](PAKET1_UYGULAMA_RAPORU.md)
- (önceki) M-S1a firma izolasyonu migration'ı — 1.0.133, şema 61→62, geri dönüş noktası `pre-ms1a` duruyor.
- **Sıradaki adım:** onaylı sıranın **11. maddesi** — Faz S: eşitleme performansı + FK + benzersizlik
  (önce analiz; migration gerekirse DUR ve kullanıcıya sor).

---

### Görev C — Web görünüm/tasarım iyileştirmeleri (ertelendi)
**Amaç:** Kullanıcı mevcut web tasarımını sevmiyor. Bu **görünüm** işidir (veritabanından bağımsız).
- **Durum:** ⏸️ ERTELENDİ — istekler toplanacak.
- **Nerede kaldık:** Karar: PostgreSQL bunu gerektirmiyor; ayrı iş. Parite için bir ekrana dokununca
  küçük görünüm iyileştirmeleri o an yapılabilir; büyük yeniden tasarım ayrıca planlanır.
- **Sıradaki adım:** Kullanıcı beğenmediği noktaları söyledikçe buraya not düş.

---

## ✅ TAMAMLANAN BÜYÜK KİLOMETRE TAŞLARI (kısa)
- Eşitleme çekirdeği **Z1–Z5** (tek sync kapısı, retry+poison, durum paneli) — canlı.
- Eşitleme **defter delta düzeltmesi** (stok hareketleri artık delta'ya giriyor) — canlı.
- **Düzenleme kilidi** (aynı kayıt iki kişide → ikincisi ezmez, sorar) — canlı.
- **Çok makineli simülasyon** aracı + iş-kuralı hatalarının 500 yerine 400 dönmesi — canlı.
- Masaüstü **1.0.87** yayında.

## Nasıl kullanılır (kullanıcı için)
- "**A'da / PostgreSQL'de nerede kaldık?**" → Görev A satırlarını okurum.
- "**B'ye / babanın uygulamasına dön**" → Görev B'den devam ederim.
- Yeni bağımsız iş verirsen → buraya yeni bir **Görev C/D...** açar, durumunu takip ederim.
