# Uçtan Uca Denetim · Onarım · Test Raporu — 2026-08-25

> Kapsam: tüm çözüm (API · Web · Masaüstü · Infrastructure · Application · Setup · Testler · Migration'lar · Dokümanlar)
> **Üretime hiçbir yazma yapılmadı.** Canlıda yalnız iki salt-okunur sağlık kontrolü çalıştırıldı.

---

## 1. Başlangıç durumu (baseline)

| Ölçüm | Değer |
|---|---|
| HEAD | `456ddb9` · dal `master` · origin ile **senkron** |
| Çalışma ağacı | temiz (yalnız kullanıcının kendi 2 dosyası izlenmiyor — **dokunulmadı**) |
| Release derlemesi | **0 hata** · 41 uyarı |
| Tam test paketi | **2146 geçti · 0 başarısız · 35 atlandı** (12 dk 28 sn) |
| Migration sayısı | 72 (katalogda kayıtlı = dosyada var · **eksik/fazla yok**) |
| Üretim | API **200** (0,45 sn) · Web **200** (0,35 sn) |

Kod hacmi: 763 kaynak dosya / ~89.600 satır (src) + 204 test dosyası / ~47.400 satır.

---

## 2. Yöntem

1. **Önce analiz** — statik tarama araçları yazıldı (SQL enjeksiyonu, tenant filtresi, yetki kapısı,
   Blazor devre riski, N+1, boş `catch`, kimlik bilgisi sızıntısı).
2. **Sonra kanıt** — her bulgu için **önce hatayı üreten test** yazıldı ve **gerçekten kırıldığı**
   görüldü; ancak ondan sonra düzeltme yapıldı.
3. **Sonra ölçüm** — performans tahminle değil, üretim ölçeğinin üstünde veri kurularak ölçüldü.
4. **Sonra regresyon** — odaklı testler → ilgili gruplar → tam paket (iki bağımsız koşu).

---

## 3. Bulgular ve durumları

| ID | Önem | Alan | Sorun | Durum |
|---|---|---|---|---|
| TNT-01 | **P1** | Senkron / tenant | Başka firmanın araç şablonuna malzeme satırı yazılabiliyordu | ✅ düzeltildi |
| TNT-02 | **P1** | Senkron / tenant | Bağlantının karşı ucu başka firmanın kaydı olabiliyordu | ✅ düzeltildi |
| TNT-03 | P2 | Malzeme kartı | Firma ötesi muadil kod+adıyla görünüyordu (okuma) | ✅ düzeltildi |
| RPR-01 | P2 | Rapor | "Araç — Şablon Dışı" şube yetkisini uygulamıyordu | ✅ düzeltildi |
| RPR-02 | P2 | Rapor | "Araç — Şablonlu" aynı eksik | ✅ düzeltildi |
| RPR-03 | P2 | Rapor | "Stok Sayım" kapsamsız + parametre manipülasyonuna açık | ✅ düzeltildi |
| WEB-01 | P2 | Web | 3 stok ekranı ilk yüklemede Blazor devresini düşürebiliyordu | ✅ düzeltildi |
| SIF-02 | P1 | Senkron | Açık oturumda sıfırlama isteği algılanmıyordu (veri geri geliyordu) | ✅ düzeltildi |
| SEC-02 | P3 | Araç | `MeterHistory` oturumsuz + firma filtresiz | ✅ düzeltildi |
| SEC-03 | **P2** | Masaüstü | Geliştirici modu kodu kaynakta sabit, depo public | ⏸️ **kullanıcı kararı** |
| PRF-01 | P2 | Rapor | Stok Hareketleri raporu 50.000 satıra kadar dönebilir | 📋 ölçüldü, izlemede |
| UPD-01 | P3 | Güncelleme | Boş checksum'da doğrulama atlanıyor (bugün ulaşılamaz) | 📋 raporlandı |
| TNT-04 | bilgi | API | Anonim uçlar firma/şube **adlarını** açar (ürün gereği) | 📋 kayıt |

Ayrıntılar: [`docs/DECISIONS.md`](../DECISIONS.md) ADR-121…124 · [`docs/KNOWN_ISSUES.md`](../KNOWN_ISSUES.md)

---

## 4. "Sorun yok" denen alanlar — hangi kanıtla?

| Alan | Kanıt |
|---|---|
| **SQL enjeksiyonu** | 128 interpolasyon noktası tarandı. Liste/grid sorgularında sıralama kolonu **sunucu tarafı beyaz listeden** (`byKey`) çözülüyor, filtre değerlerinin **tamamı** `GridQuery.AddParams` ile parametre. Kullanıcı girdisi SQL metnine hiçbir yolda girmiyor. |
| **Servis yetki kapısı** | Oturum alan **120 yazma metodu** tarandı → yetki kontrolü görünmeyen 5 metodun **hepsi** delege ediyor (`Transition` · `WriteInTx` · `EnsureIsDesignatedApprover`). **Korumasız yazma metodu yok.** |
| **API kimlik doğrulama** | **301 uç** tarandı. `RequireAuthorization` olmayan 10 ucun her biri bilinçli ve kendi kapısı var: giriş (hız sınırlı), public liste (hız sınırlı), `/sync/*` + `/api/backups` (cihaz token'ı), kurulum/paket indirme. |
| **Hata sızıntısı** | Ortak middleware 500'de ham istisnayı **istemciye vermiyor**, sunucu loguna yazıyor. 403/409/400 iş mesajları bilinçli. |
| **Kimlik bilgisi** | Log/exception/kaynak taramasında parola-token-bağlantı dizesi **yok**. `.env*` git'te izlenmiyor (yalnız `.env.example`). PG bağlantı etiketi yalnız `host/db` gösteriyor. |
| **Migration bütünlüğü** | 72 migration: sürüm çakışması yok · katalog ↔ dosya birebir · artan sırada · her biri **tek transaction** · uygulanmışlar atlanıyor (idempotent). |
| **Idempotency** | `stock_movements · daily_activities · fuel_* · vehicle_maintenances · invoices · invoice_allocations · finance_transactions · party_ledger` tablolarının hepsinde `operation_id` üzerinde **benzersiz indeks** var (veritabanı düzeyinde çift kayıt imkânsız). |
| **Menü / Ekran Yönetimi** | 18 + 12 parite testi: benzersiz anahtar · modül varlığı · web route'u var · masaüstünde gezinilebilir · yetim ekran yok · menü katalogdan üretiliyor · varsayılan şema birebir. |
| **Ön muhasebe** | 40+ adlandırılmış senaryo: kısmi/tam tahsilat, fazla kapama reddi, başka carinin faturası, iptal edilmiş fatura, idempotency (3), ters kayıt (5), transfer (5), firma izolasyonu, şube izolasyonu, "para hareketi stoka dokunmaz". |
| **Şube kapsamı** | Tek otorite `BranchAccess` (izinli ∩ istenen ∩ oturum) — hem masaüstü hem web/API oturumu `user_scopes` + `users.branch_id` + şube ağacını **aynı** kaynaktan dolduruyor. |
| **Ön muhasebe raporları** | 6 raporun **hepsi** `company_id` + `ReportScope.BranchSql` + `AccessControl.Require` içeriyor (satır satır doğrulandı). |
| **Rapor kataloğu** | 19 rapor tek katalogda; `Run` içindeki tek `switch` **19 anahtarın hepsini** karşılıyor → web ve masaüstü aynı listeyi ve aynı hesaplamayı kullanıyor. |

---

## 5. Performans — tahmin değil, ölçüm

Ortam: SQLite, 3.000 malzeme · **20.000 stok hareketi** · 8 şube · 300 cari · **30.000 cari defter satırı**
(üretimdeki gerçek veri: 2.459 malzeme · 663 hareket → **ölçüm üretimin ~30 katı yükte**).

| İşlem | Süre |
|---|---|
| Malzeme listesi (1. sayfa / 50. sayfa) | 9 ms / 9 ms |
| Malzeme listesi (kod filtresi) | 20 ms |
| Rapor: Stok Durumu (firma geneli / tek depo) | 9 ms / 2 ms |
| Rapor: Stok Sayım · Araç · Durum | 0 ms |
| Rapor: Cari Bakiye Özeti (30.000 satır) | 66–97 ms |
| Rapor: Cari Ekstre (tek cari) | 0 ms |
| **Rapor: Stok Hareketleri (20.000 satır döner)** | **125 ms** |
| ↳ aynı verinin ham SQL'i | **6 ms** |

**Sonuç:** darboğaz sorguda değil, **20.000 satırın oluşturulup arayüze taşınmasında**.

**Denenip ELENEN çözüm:** `stock_movements(company_id, created_at)` indeksi eklendi →
sorgu planı `SCAN` + geçici sıralama yerine `SEARCH`e döndü, **ama rapor süresi değişmedi (125 → 123 ms)**.
Yani "eksik indeks" bu raporun sorunu değildir. **Ölçüm yapılmasaydı gereksiz bir migration açılacaktı.**
Bu nedenle **hiçbir indeks eklenmedi** (migration = üretim değişikliği, ayrıca onay gerektirir).

Günlük kullanım güvende: hareket **ekranı** sunucuda zaten 1000 satırla sınırlı.
İzleme eşiği: hareket sayısı ~20.000'i geçince rapora sayfalama gerekir (bugün 663).

---

## 6. Kalıcı korumaya alınanlar (yeni testler)

| Test | Neyi kilitler |
|---|---|
| `TenantLinkTableTests` (6) | Bağlantı tablolarının **iki ucu da** firma sınırında; meşru aynı-firma satırları çalışmaya devam ediyor; okuma savunması ayrıca var |
| `ReportBranchScopeTests` (+8) | Üç raporda şube yetkisi; parametre manipülasyonunda **boş sonuç**; sınırsız kullanıcıda eski davranış; **yönetici raporu sözleşmesi** (çalışma şubesi daraltmaz) |
| `WebCircuitGuardTests` (2) | Hiçbir Blazor sayfası ilk yüklemede korumasız çağrı yapamaz + **kuralın kendisi** çalışıyor mu |
| `CompanyLocalResetTests` (+3) | Sıfırlama kontrolü **push'tan önce**; tur durur ve oturum kapanır; çevrimdışında bayrak açılmaz |

> ⚠️ `WebCircuitGuardTests` ilk yazıldığında **çok satırlı imzaları göremiyordu** ve "her zaman yeşil"
> bir kabuktu. Düzeltme kasten geri alınıp koşularak yakalandı; şimdi bozuk hâlde **kırılıyor**,
> düzgün hâlde **geçiyor** (iki yön de doğrulandı).

---

## 7. Test sonuçları

| Koşu | Sonuç | Süre |
|---|---|---|
| **Taban** (denetim öncesi) | 2146 geçti · **0 başarısız** · 35 atlandı | 12 dk 28 sn |
| **Koşu B** (onarımlardan sonra) | **2165 geçti · 0 başarısız · 35 atlandı** | 11 dk 46 sn |
| **Koşu C** (bağımsız ikinci koşu) | **2165 geçti · 0 başarısız · 35 atlandı** | 11 dk 19 sn |

İki bağımsız koşu **birebir aynı** sonucu verdi → **flaky (kararsız) test yok**.
Taban ile fark: **+19 test** (hepsi bu denetimde yazıldı), **regresyon 0**.

**Atlanan 35 testin tamamı PostgreSQL kapılıdır** — `PostgresTestGuard`, boş bir test veritabanı
olduğu kanıtlanmadan (ad "test" içermeli · public şema boş olmalı · boyut < 50 MB · açık onay
değişkeni) çalışmayı reddeder. Gizlenen, devre dışı bırakılan ya da gevşetilen **hiçbir test yoktur**.

Release derlemesi: **0 hata** (uyarı sayısı tabandakiyle aynı: 41 — hiçbiri bu turda eklenmedi).

---

## 8. Gerçek arayüz (GUI) doğrulaması

Yerel API + web ayağa kaldırıldı (**ayrı bir çalışma dizininde, sıfır veritabanıyla** — kullanıcının
kendi geliştirme veritabanına ve **üretime dokunulmadı**), gerçek tarayıcıda giriş yapıldı:

| Kontrol | Sonuç |
|---|---|
| Giriş → ilk şifre belirleme → şube seçimi → panel | ✅ çalıştı |
| Menü ağacı | ✅ varsayılan şema (Malzeme ve Stok · Operasyon · Talepler · Finans · Raporlar · Kurumsal Yönetim · Sistem Yönetimi · Ayarlar) |
| **Stok Sayım** (düzeltilen) | ✅ açıldı, form ve boş durum doğru |
| **Stok Hareketleri** (düzeltilen) | ✅ açıldı, filtreler ve boş durum doğru |
| **Stok Dağıtım** (düzeltilen) | ✅ açıldı, "depo yok" bilgisi doğru |
| Raporlar ekranı | ✅ **19 raporun tamamı** kategorileriyle listelendi |
| Düzeltilen rapor çalıştırıldı | ✅ `POST /api/reports/vehicles-nontemplate` → **200** (9 ms), boş durum "Kayıt bulunamadı." |
| Tarayıcı konsolu / sunucu logu | ✅ hata yok |
| API kapalıyken sayfa gezinme | ✅ devre **düşmedi** (yeniden bağlanma penceresi çıkmadı) |

> **Masaüstü uygulaması bilinçli olarak ÇALIŞTIRILMADI:** açıldığında ÜRETİM sunucusuna bağlanıp
> yereldeki veriyi göndermeye başlar. Bu tur "üretime yazma yok" kuralıyla yürütüldüğü için
> masaüstü GUI turu yapılmadı; masaüstündeki değişiklikler (SIF-02) kaynak-kilidi testleriyle
> doğrulandı ve rapor/tenant düzeltmeleri masaüstüyle **ortak** koddadır (aynı `ReportService`).

---

## 9. Bilinçli olarak YAPILMAYANLAR

- **Migration açılmadı** — üretim değişikliğidir, ayrı onay ister. Ölçüm zaten indeksin fayda
  getirmediğini gösterdi.
- **Deploy yapılmadı** — kullanıcı bu turda deploy'u ayrı operasyon olarak istedi.
- **Refactor yapılmadı** — hiçbir çalışan yapı "daha güzel olsun" diye yeniden yazılmadı.
- **Geliştirici modu (SEC-03) değiştirilmedi** — davranışın bilinçli değişimi kullanıcı kararıdır.
- **Güncelleme checksum kapısı (UPD-01) sıkılaştırılmadı** — canlı `app_releases` satırları
  görülmeden yapılırsa çalışan güncelleme yolu durabilir.
