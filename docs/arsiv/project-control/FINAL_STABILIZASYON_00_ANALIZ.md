# FİNAL — KULLANICI SİMÜLASYONU ve STABİLİZASYON · ANALİZ RAPORU (FIN-00, kod yok)

> Tarih: **2026-08-29** · Durum: **ANALİZ — PK-FIN kararları bekleniyor** · Kod/migration/deploy/canlı erişim: **YOK**
> Roadmap: **FİNAL fazı** (MASTER_ROADMAP §1 son satır). **N — Mobil kullanıcı kararıyla ATLANDI**
> (2026-08-29): bu döngüde uygulanmayacak; sıradaki resmî iş tabloda N'den sonra gelen FİNAL'dir.
> Tanım (MASTER_ROADMAP §0): *"Nihai büyük kullanıcı simülasyonu (5–10k kayıt, tüm kombinasyonlar)
> roadmap bitince ayrı fazdır."* CLAUDE.md §7'nin (Ekran QA Motoru V2) devreye gireceği "kapsamlı
> denetim" tam olarak bu fazdır — ama yalnız kullanıcının PK onayından sonra.

## 1–5. Mevcut altyapı envanteri (fazın üzerine kurulacağı parçalar)

**Test süiti (gerçek sayılar, 2026-08-29 tam koşu):** 2.883 test — **2.846 aktif ve yeşil**, **37
atlanan** (tamamı PostgreSQL gerektiren testler; yerelde PG yok). Not: backlog'daki `TST-01 "33 atlanan
test"` sayısı ESKİMİŞ — güncel sayı 37 ve atlama nedeni tek: PG ortamı.

**Simülasyon araçları (VAR — sıfırdan kurulmayacak):**
- `tools/qa/multi-machine-sim.mjs` — **çok makineli gerçekçi kullanım simülasyonu** zaten yazılmış
  (N sanal makine × N tur; kaybolan güncelleme / mükerrer kod / tenant sızıntısı / 500 / bakiye
  tutarsızlığı avı; gecikme ölçümü; `depowise-erp.fly.dev` hedefini REDDEDEN canlı-koruması içinde).
  **EKSİĞİ:** yalnız çekirdek ekranları kapsıyor (auth·firma·şube·kullanıcı·personel·tanım·bakım·
  malzeme·araç·stok·yakıt·günlük) — **FAZ 1-5 modülleri kapsam DIŞI**: Proje, Evrak, Ekipman, Zimmet,
  Maliyet Merkezi, Satın Alma, İş Emri, Takvim, Bildirim, Duyuru, Global Arama, Excel Merkezi, QR,
  Dashboard yeni kartları.
- `tools/qa/live-sync-check.mjs` — canlı senkron SÖZLEŞME kontrolü (salt-okunur, test hesabı) — yayın
  sonrası doğrulamada kanıtlı.
- `PostgresTestGuard` — PG testleri için ÇİFT KİLİT: `DEPOWISE_PG_TEST_CONFIRM="EVET-BU-BOS-TEST-VERITABANI"`
  + veritabanı adında "test" işareti zorunlu → **canlı `depowise_prod`'a test bağlanması YAPISAL olarak
  imkânsız** (kapının kendisi de testli).
- `ApiTestHost` — API'yi süreç içinde ayağa kaldıran gerçek-HTTP test altyapısı.
- **İzole yerel hedef:** API, `DEPOWISE_PG_URL` TANIMSIZ başlatılınca boş YEREL SQLite ile çalışır
  (ServerServices bilinçli geri-dönüş yolu) → simülasyonun güvenli hedefi hazırdır; canlıya hiçbir yol yok.

**Ekran/servis/API/veri modeli/yetki durumu:** 56 ekran / 63 web bağlantısı parite kilitli; tüm modüller
deny-by-default + tenant + BranchAccess + soft-delete desenlerinde; şema 81; canlı v174/v199/1.0.160.
M (EXL-01) ve O (BAR-01) tamam ama **yayınlanmadı** — FİNAL bulgu düzeltmeleri de aynı "yayın bekleyen kod"
havuzuna eklenir.

## 6–11. Fazın konusu: neler birikti (stabilizasyon borç envanteri)

**A) Bilinen açık konular (KNOWN_ISSUES):** R30 sunucu diski (operasyonel, izleme) · SNK-13 (dokunulmayacak
— kullanıcı talimatı) · YET-01 (işlevsiz 2 yetki anahtarı — KARAR bekliyor) · MAK-01/b (makine aktivasyon
modeli — karar) · YET-02/b (iptal butonu kapısı UI tutarsızlığı) · PRF-01/b/c (rapor tavanı/yanıt boyutu) ·
YED-01/b (sunucu tarafında PG dosya yedeği yok — yerelden pg_dump alınabiliyor, sunucuda otomatik değil) ·
TNT-04 (anonim uçlar firma/şube ADI açıyor — bilinçli giriş-ekranı davranışı) · ARC-01 (araç seçici firma
geneli — ürün kararı) · WEB-03 · iki küçük N+1 · Personel 200 kayıt tavanı.
**Eskimiş görünenler (denetimde yeniden ölçülecek — "denetimde varsayımı yeniden ölç" kuralı):** R31/R32
(Migration064 depo-bazlı stok riskleri) — Migration064 canlıya 2026-08 öncesi çıktı ve toplu yayında bakiye
bit-bit doğrulandı; büyük olasılıkla KAPANMIŞTIR, belge tazelenecek. RPR-15'in bir kısmı ADR ile kapandı.

**B) Roadmap backlog fazları (FİNAL'in çevresindeki bekleyenler):** TRF-01 (transfer UI paritesi) ·
STK-B2/RPR-02/SNK-05 (⛔ karar bekliyor) · STK-09/11 · SNK-06..10 (senkron ölçeklenme) · GNC-01..03
(güncelleme/sürüm uyumu) · PRT-02 · P-1 · MUH-02..05 (ön muhasebe genişlemesi) · TST-01 · index/N+1/
sayfalama · FAZ J (güvenlik sertleştirme · API sürümleme kararı · yük testi) · devredilen teknik borçlar
listesi (G6-*, H-6/7, TMZ-*, …).

**Sonuç:** FİNAL'in gerçekten eksik olan parçası ikidir: (1) **simülasyon aracının FAZ 1-5 modüllerine
genişletilmesi + 5-10k ölçekli tohum**, (2) **PG ayağının izole kurulup 37 atlanan testin ve PG
simülasyonunun koşulması**. Geri kalanı koşu + bulgu düzeltme + belge tazelemedir.

## 12–13. Önerilen mimari — aşamalı FİNAL planı (tamamı canlıya dokunmaz)

| Aşama | İçerik | Hedef ortam |
|---|---|---|
| **S1 — Simülasyon genişletme** | `multi-machine-sim.mjs`'e FAZ 1-5 modül senaryoları EKLENİR (proje/evrak-metadata/ekipman/zimmet ver-al/maliyet merkezi/satın alma→mal kabul/iş emri akışı/takvim/duyuru/arama/alerts okundu/excel export/QR ucu) + **5–10k kayıt tohum üreticisi** (mevcut API uçlarıyla, izole hedefe). Canlı-koruma satırı AYNEN kalır. Kod DEĞİL araç değişir; üründe dosya değişmez. | — |
| **S2 — SQLite koşusu** | Yerel API (PG URL'siz → boş yerel SQLite) + tohum + sim (ör. 10 makine × 12 tur) + masaüstü senkron provası (mevcut senkron testleri). Bulgular önem sırasıyla raporlanır. | izole yerel SQLite |
| **S3 — PG koşusu** | Yerel/izole **test** PG (adı "test" içeren boş DB; PostgresTestGuard çift kilidi) → **37 atlanan PG testi İLK KEZ topluca** + aynı sim PG hedefinde. İki lehçe farkı avı. | izole test PG |
| **S4 — Bulgu düzeltme turları** | Her bulgu: öncelik/tekrar üretme/kök neden → dar kapsamlı, testli düzeltme (gelistirme-protokolu §1); düzeltme sonrası ilgili sim senaryosu yeniden koşulur. | kod (yayınsız) |
| **S5 — Belge/karar temizliği** | KNOWN_ISSUES tazeleme (R31/R32, TST-01 sayısı, kapananlar) + karar bekleyen maddelerin (YET-01, ARC-01, STK-B2, RPR-02, SNK-05, MAK-01) TEK "karar paketi" olarak size sunulması. | belge |

Sıfırdan hiçbir sistem kurulmaz: sim aracı, test kapıları, izole hedef ve QA motoru (§7) zaten var.

## 14–22. Zorunlu güvence cevapları

- **Migration gerekli mi? HAYIR** — şema 81'de kalır. Yeni tablo/kolon/ALTER/indeks YOK. (S4'te bir bulgu
  şema düzeltmesi GEREKTİRİRSE: durulur, ayrı onay istenir — varsayılan plan migration'sızdır.)
- **Mevcut kayıtlar değişir mi? HAYIR** — simülasyon verisi YALNIZ izole yerel DB'lerde üretilir/çöpe
  gider; backfill/UPDATE/recompute yok. Canlı DB'ye hiçbir aşamada bağlanılmaz (sim aracı canlı adresi
  zaten reddediyor; PG kapısı "test" adı + onay değişkeni istiyor).
- **API değişikliği gerekir mi? HAYIR** (bulgu düzeltmeleri gerektirirse yalnız eklemeli; sözleşme bozulmaz).
- **Senkron değişikliği gerekir mi? HAYIR** — SNK-06..10 bu fazın DIŞINDA (ayrı işler); SNK-13'e dokunulmaz.
- **Yetki değişikliği gerekir mi? HAYIR** — YET-01/02b ancak karar paketinden onay çıkarsa ayrı iş olur.
- **Masaüstü/web etkisi:** koşularda kod değişmez; yalnız S4 bulgu düzeltmeleri dokunur (dar kapsam).
- **Performans:** sim gecikme ölçümü zaten topluyor → 5-10k ölçekte liste/rapor/arama/dashboard süreleri
  İLK KEZ sistematik ölçülmüş olacak (bugünkü canlı ~2,5k malzeme; hedef ölçek bunun 2-4 katı). Yeni
  cache/indeks ancak ölçüm kanıt gösterirse ve ayrı onayla.
- **Güvenlik:** sim, tenant sızıntısı/500 avını zaten yapıyor; genişletmede yeni modüllerin çift-kapıları
  (duyuru public-read, evrak iki-kapı, arama kaynak-yetkisi, QR ucu) senaryolara eklenir. Hiçbir kapı
  gevşetilmez; frontend'de gizleme güvenlik sayılmaz — denetim API seviyesindedir.

## 23–24. Test ve regresyon planı (uygulama turunda)

- S2/S3 koşu çıktıları rapor dosyasına (yanıta özet — §7.0 token disiplini).
- 37 PG testinin tamamı S3'te koşulur; kalan atlama varsa nedeniyle raporlanır.
- Her S4 düzeltmesi: kendi hedefli testi + ilgili modül regresyonu; faz sonunda TAM süit (2.883+).
- Kritik değişmezler her koşuda: tenant · yetki · BranchAccess · negatif stok · sayaç · idempotent retry ·
  offline kalıcılık · bit-bit (okuma yollarının kayıt değiştirmediği).
- Üç Release build her düzeltme turunda 0 hata.

## 25–26. Bilinen sınırlar / yeniden yazım riski

- FİNAL, yayın YAPMAZ (yeni strateji): tüm düzeltmeler M/O ile aynı "yayın bekleyen kod" havuzunda birikir.
- 5-10k ölçek SENTETİKTİR; canlı verinin birebir kopyası KULLANILMAZ (kopya almak bile bu fazda gereksiz —
  risk ve onay maliyeti var; sentetik tohum kombinasyon çeşitliliğini daha iyi verir).
- Masaüstü UI'sının kendisi (Avalonia pencereleri) otomasyonla sürülmez — masaüstü kapsamı servis/senkron
  katmanından test edilir (mevcut yaklaşım); gözle tur size kalır.
- Yeniden yazım riski: YOK — üründe yalnız bulgu düzeltmeleri; araç genişletmesi ürün kodu değildir.

## 27. PK-FIN KARAR SORULARI

### PK-FIN1 — Faz kapsamı
- **ÖNERİLEN (A):** S1→S5 planı: simülasyon genişletme + iki lehçede koşu + KRİTİK/YÜKSEK bulgu
  düzeltmeleri + belge/karar temizliği. Backlog borçları (SNK-06.., MUH-.., GNC-..) bu faza ALINMAZ —
  ayrı işler olarak sırada kalır. Maliyet: orta · canlı risk: sıfır · migration: yok.
- **Alternatif (B):** A + sizin seçeceğiniz backlog maddeleri aynı faza. — **(C):** Simülasyonsuz, yalnız
  backlog/belge temizliği (fazın asıl amacı olan ölçek+kombinasyon provası yapılmamış olur; önerilmez).

### PK-FIN2 — Simülasyon ölçeği
- **ÖNERİLEN (A):** tohum ~**7.500 kayıt** (5-10k bandının ortası: ~4k malzeme · ~400 araç · ~200 ekipman ·
  ~300 personel · 20 şube · 3+1 firma · geri kalanı hareket/belge) + **10 sanal makine × 12 tur** sim
  (aracın varsayılanı). — **Alternatif (B):** üst bant 10k+ / 20 makine (koşu süresi ve triage yükü büyür).

### PK-FIN3 — PostgreSQL ayağı (S3)
- **ÖNERİLEN (A):** yerelde İZOLE, adı "test" içeren BOŞ PG (Docker ya da elimizdeki taşınabilir PG 17
  istemcisiyle yerel sunucu) kurulur; PostgresTestGuard çift kilidiyle **37 atlanan test + PG simülasyonu**
  koşulur. Canlı Neon'a hiçbir bağlantı yok. — **Alternatif (B):** PG ayağı atlanır (iki lehçe provası
  eksik kalır; lehçe farkı hataları — Türkçe arama/limit/FK — yakalanamaz; önerilmez).

### PK-FIN4 — Bulgu düzeltme politikası
- **ÖNERİLEN (A):** bu fazda yalnız **KRİTİK + YÜKSEK** bulgular düzeltilir (veri bütünlüğü, tenant/yetki,
  500, kaybolan güncelleme, bakiye tutarsızlığı); ORTA/DÜŞÜK bulgular KNOWN_ISSUES'a kaydedilir ve ayrı
  işlere bırakılır (kapsam patlaması önlenir). — **Alternatif (B):** tüm bulgular aynı fazda (süre belirsizleşir).

### PK-FIN5 — Karar bekleyen eski maddeler
- **ÖNERİLEN (A):** YET-01 · ARC-01 · STK-B2 · RPR-02 · SNK-05 · MAK-01/b, S5'te TEK "karar paketi"
  belgesi olarak önerilerle size sunulur; bu fazda KOD yazılmaz, siz onaylayınca ayrı işler açılır. —
  **Alternatif (B):** karar paketi de bu fazın dışında kalsın (belgeler bekler).

**Karar gerektirmeyenler (raporlanır, sorulmaz):** canlı veri/canlı DB hiçbir aşamada kullanılmaz ·
migration yok · yayın yok · SNK-13 dokunulmaz · sim aracının canlı-koruması ve PG çift kilidi aynen ·
sentetik tohum (canlı kopya yok) · masaüstü offline mimarisine dokunulmaz.
