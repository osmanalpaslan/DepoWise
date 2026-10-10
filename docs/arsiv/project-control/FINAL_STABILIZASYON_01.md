# FIN-01 — FINAL: Kullanıcı Simülasyonu ve Stabilizasyon · KALICI KONTROL BELGESİ

> Tarih: **2026-08-29** · Karar: **ADR-178** · PK-FIN1..FIN5 = **A-A-A-A-A** aynen uygulandı.
> Analiz: [FINAL_STABILIZASYON_00_ANALIZ.md](FINAL_STABILIZASYON_00_ANALIZ.md) ·
> Karar paketi: [FINAL_KARAR_PAKETI.md](FINAL_KARAR_PAKETI.md)
> **Production'a HİÇBİR aşamada bağlanılmadı** — tüm koşular yerel izole ortamlarda.

## 1–4. Simülasyon kapsamı

- Araç: mevcut `tools/qa/multi-machine-sim.mjs` **eklemeli genişletildi** (ikinci sistem kurulmadı):
  FAZ 1-5 modül senaryoları (ekipman+mükerrer-kod · zimmet ver/iade+idempotent-retry · iş emri+durum
  geçişi+gecikmiş · satın alma+mal kabul+idempotent-retry · takvim · duyuru aktif/pasif/önemli/şubeli ·
  global arama tam-kod+tenant probu · dashboard/bildirim/okundu · Excel export (XLSX imza doğrulamalı) ·
  QR (PNG imza doğrulamalı)) + **güvenlik prob fazı** (yetkisiz staff · tenant-B işaretli kayıt ·
  duyuru public-read/yazma ayrımı · soft-delete · salt-okunurluk sürüm sabitliği) + **tohum modu**
  (`SIM_SEED`). **Koruma GÜÇLENDİRİLDİ:** araç artık yalnız localhost/127.0.0.1 hedefi kabul eder;
  fly.dev/neon.tech ve her uzak host program başlamadan reddedilir.
- **Sentetik kayıt:** koşu başına ~**6.570 birincil kayıt** (malzeme 3.300 · stoklu malzeme+hareket 1.500
  çifti · araç 375 · personel 300 · ekipman 225 · yakıt 225 · İE 150 · sipariş 113 · takvim 113 ·
  zimmet 75 · silinmiş 75 · duyuru 45 · proje 38 · 20 şube · 20 maliyet merkezi) + türetilmiş
  hareket/belge satırlarıyla **7.500+ satır** — iki lehçede AYRI AYRI üretildi. Tamamı API/servis
  üzerinden (ham SQL yok, validasyon atlanmadı); kenar durumlar bilinçli (pasif/pencere-dışı/şubeli/
  Türkçe karakter/kritik stok/silinmiş).
- **10 sanal makine × 12 tur** + eşzamanlı yazma yarışı + prob fazları; koşu başına ~7.900 HTTP isteği.

## 5–6. Lehçe sonuçları (son temiz koşular)

| | SQLite (yerel izole) | PostgreSQL 17 (yerel izole `depowise_sim_test`) |
|---|---|---|
| Bulgu | **0 — mantık hatası yok** | **0 — mantık hatası yok** |
| Eşzamanlı yarış (10 makine aynı sürümü yazar) | kazanan=1 · 409=9 ✅ | kazanan=1 · 409=9 ✅ |
| Yetkisiz problar | 403 ✅ (sızma yok) | 403 ✅ (sızma yok) |
| Gecikme (tur fazı p50) | ~14-15 ms | ~14-15 ms |
| Tohumlu koşu istek/sn | ~1.487 | ~1.399 |

Ara koşularda çıkan tüm işaretler KÖK NEDENE indirildi: 3'ü PROB/ARAÇ hatasıydı (zimmet araması etikete
bakar; B-firma kullanıcısına şube zorunlu; hız sınırlayıcı 429 — hepsi araçta düzeltildi), 2'si GERÇEK
bulgu (aşağıda FIN-B1/FIN-M1), 1'i tasarımın kendini kanıtlaması (login limiter 30/5dk devreye girdi).

## 7–10. Test sayıları

- **TAM SÜİT: 2.888 test → 2.853 GEÇTİ · 0 BAŞARISIZ · 35 atlanan.** Atlananlar PostgreSQL sınıfları —
  tam süit içinde ApiTestHost süreç-genel ortam değişkeni yazdığından bilinçli olarak ayrı koşulurlar.
- **PostgreSQL testleri İLK KEZ TOPLUCA koşuldu: 45/45 GEÇTİ** (izole yerel `depowise_test`;
  `PostgresTestGuard` çift kilidi doğrulanarak). Tek ortam düzeltmesi: art arda şema kurulumları DB'yi
  51 MB'a şişirince kapının 50 MB tavanı (bilinçli güvenlik eşiği — GEVŞETİLMEDİ) doğru şekilde durdurdu;
  DB tazelenip kalan test koşuldu. `TST-01` fiilen kapandı (sayı 37 idi, artık PG ile koşulabiliyor).
- Yeni kilitler: `FinalStabilizasyonTests` **5/5** (4 modülde aynı-firma idempotent retry + FIN-B1
  mevcut-sözleşme kilidi).

## 11–16. Bulgular ve durumları

| Seviye | Bulgu | Durum |
|---|---|---|
| KRİTİK | — | **YOK** |
| YÜKSEK→(yeniden sınıflandı) | **FIN-B1** — eski 6 tabloda operation_id benzersizliği FİRMA-ÜSTÜ; başka firmanın aynı op-id'si işlemi sessizce atlatır. Kod düzeltmesi denendi, **ŞEMA engeline takıldı** (global UNIQUE indeks) → kökten çözüm CANLI tabloda migration ister → **DURULDU, kod GERİ ALINDI** (§24/§32). Pratik canlı olasılık ~sıfır (GUID id'ler). | **KARAR PAKETİNDE** — mevcut sözleşme FIN5 testiyle kilitli |
| ORTA | **FIN-M1** — PG'de aşırı eşzamanlı aynı-tip belge girişinde doc-no yarışı tekrar hakkını (bilinçli 3) tüketip 409 verebiliyor (ölçüm: 8/60; veri bozulmaz, rollback+mesaj var; SQLite'ta yok) | KNOWN_ISSUES — düzeltilmedi (bilinçli tasarım; PK-FIN4) |
| ORTA/DÜŞÜK | **FIN-M2** — zimmet ekranı araması varlık koduyla bulmuyor (etikete bakıyor) | KNOWN_ISSUES — düzeltilmedi |
| Belge eskimesi | TST-01 sayısı · R31/R32 (Migration064 riskleri — toplu yayında bit-bit kanıtla fiilen kapandı) | KNOWN_ISSUES tazelendi |

**Düzeltilenler (kod):** ürün kodunda düzeltme GEREKMEDİ (KRİTİK/YÜKSEK sıfır kapandı: tek YÜKSEK aday
FIN-B1 migration'a takılıp karar maddesine dönüştü). Düzeltilen şeyler: simülasyon ARACI (probe hataları
+ koruma güçlendirme + koşuya özgü op-id) ve BELGELER. **Ürün kod değişikliği: yalnız yeni test dosyası.**

## 17–20. Güvenlik / tenant / BranchAccess / offline-sync

- **Tenant:** iki firma canlı probda — liste/detay/arama/QR yollarının hiçbirinden sızma YOK (işaretli
  kayıt hiçbir yoldan görünmedi; ID ile detay/QR açılamadı).
- **Yetki:** yetkisiz staff hiçbir kaynak listesini/exportunu/QR'ını alamadı (403); arama boş döndü;
  **duyuru public-read çalıştı (200) + duyuru YAZMA reddedildi** (PK-J1 ayrımı canlı probda doğrulandı).
- **BranchAccess:** birim testleriyle kilitli (tam süit yeşil); sim düzeyinde ayrıca modellenmedi
  (kapsam kullanıcı-scope tablosundan gelir — mevcut PRS/ŞB testleri koruyor).
- **Salt-okunurluk:** arama+dashboard+export+QR+duyuru-okuma fırtınası kayıt sürümünü DEĞİŞTİRMEDİ.
- **Soft-delete:** silinen kayıt arama/otomatik açılışa girmedi.
- **Offline/senkron:** senkron koduna DOKUNULMADI; mevcut senkron test kilitleri tam süitte yeşil.
  SNK-13 aynen (dokunulmadı). Çok-cihaz çevrimdışı döngüsünün UÇTAN UCA sim modellemesi bu araçta yok —
  bilinen sınır (senkron sertifikasyonu STK-07 + senkron birim testleri kapsıyor).

## 21–25. Modül sonuçları

Excel Merkezi: export uçları iki lehçede geçerli XLSX üretti (PK imzası doğrulandı); yetkisiz export 403;
import kapsamına DOKUNULMADI (7 set sabit; "zaten var → atla" tam süit + FIN kilitleriyle korunuyor).
Global Arama: tam-kod bulma ✅ · tenant/yetki/silinmiş süzme ✅ · davranış/limit değişmedi.
Barkod/QR: QR PNG üretimi ✅ · yetkisiz/tenant QR reddi ✅ · tarama salt-okunur.
Bildirim/Duyuru: sayaç/okundu akışı ✅ · aktif/pasif pencere ✅ · public-read/yazma ayrımı ✅.
Dashboard: GetSummary üzerinden 200 ✅ (paralel sistem kurulmadı); 8 kategori düzeni değişmedi.

## 26–28. Migration · production · build

- **Migration: YOK — şema 81'de kaldı.** (FIN-B1 çözümü migration isterdi → yazılmadı, karar sizde.)
- **Production: HİÇBİR bağlantı yok** — Neon/canlı API'ye tek istek atılmadı; sim aracı artık uzak
  hostları yapısal olarak reddediyor; PG testleri çift kilitli izole yerel DB'de; canlı veri değişmedi.
- **Build:** API + Web + Masaüstü Release **0 hata**.

## 29–30. Known Issues · karar bekleyenler

KNOWN_ISSUES'a eklendi: FIN-B1 (karar) · FIN-M1 · FIN-M2 · eskime tazelemeleri (TST-01, R31/R32).
Karar paketi (PK-FIN5=A): [FINAL_KARAR_PAKETI.md](FINAL_KARAR_PAKETI.md) — FIN-B1 · YET-01 · ARC-01 ·
STK-B2 · RPR-02 · SNK-05 · MAK-01/b (öneri+risk+maliyetle; kod yazılmadı).

## Değişen/yeni dosyalar

`tools/qa/multi-machine-sim.mjs` (genişletme+koruma) · `tests/DepoWise.Tests/FinalStabilizasyonTests.cs`
(YENİ, 5 test) · KNOWN_ISSUES · FINAL_KARAR_PAKETI.md (YENİ) · bu belge · ADR-178 · roadmap/CURRENT_PHASE.
**Ürün kaynak koduna dokunulmadı** (denenen FIN-B1 düzeltmesi tam geri alındı — git diff'te yok).

## EK — Karar paketi uygulaması (2026-08-29, ADR-179)

Kullanıcı kararlarıyla 7 madde kapatıldı: **FIN-B1** → Migration082 (yalnız 6 indeks, aynı adlarla
`(company_id, operation_id)`; veri/kolon dokunuşu yok; sync tabloları kapsam dışı) + 8 noktada
firma-kapsamlı idempotency; iki lehçede bit-bit/indeks/idempotency kanıtları
(`FinalStabilizasyonTests` FIN1-FIN10 + `PostgresMigration082Tests`). **⛔ Migration082 PRODUCTION'DA
ÇALIŞTIRILMADI — canlı şema 81** (yayın önkoşulları belgeli). **YET-01** kaldırıldı (buton kataloğu
kilidi tam güçte). **ARC-01(a)** ve **RPR-02** incelemede ZATEN çözülmüş çıktı (RPR-04/RPR-07) — kod
gerekmedi, kayıtlar kapatıldı. **STK-B2=HAYIR** (FIN8 kilidi). **SNK-05(a)** mevcut sözleşme kilitlendi
(online ilk-kazanır FIN9 · offline LWW FIN10; senkron koduna dokunulmadı). **MAK-01/b** korundu.
Ayrıntı: [FINAL_KARAR_PAKETI.md](FINAL_KARAR_PAKETI.md) · KNOWN_ISSUES 2026-08-29 bölümü · ADR-179.

## EK-2 — FIN-B1/Migration082 geri çekildi (2026-08-29, ADR-180 · PK-R4=B)

Rapor ara işinin yayınına Migration082'nin karışmaması için FIN-B1 çifti (Migration082 + 8 firma-süzgeci
+ yeni-sözleşme testleri FIN1–FIN7 + `PostgresMigration082Tests`) master'dan BİREBİR geri çekildi;
FIN8/FIN9/FIN10 kilitleri ve YET-01 KALDI. Eski sözleşme FIN5 ile yeniden kilitli; katalog azamisi 81.
**FIN-B1 tamamlanmış SAYILMAZ — ayrı onay bekliyor** (tasarım `35d7bce`).

## Sonraki iş

FINAL fazı ve karar paketi **build+test seviyesinde TAMAMLANDI** (yayın yok — yeni strateji).
Yayın bekleyen kod havuzu: M + O + FIN düzeltmeleri (**Migration082 HARİÇ — ADR-180 ile geri çekildi,
ayrı onay bekliyor**) + rapor ara işi. 7b Bakım-Ekipman serbest sırada.
