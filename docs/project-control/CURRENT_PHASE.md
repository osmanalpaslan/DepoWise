# AKTİF DURUM

> Son güncelleme: **2026-08-29** (⭐ **TOPLU YAYIN YAPILDI** — M + O + FIN(082 hariç) + ADR-181 + ADR-182 canlıda; masaüstü **1.0.161**; **canlı şema 81 DEĞİŞMEDİ**) · Bu dosya **her iş sonunda** güncellenir.

---

## ⭐ YAYIN — 2026-08-29 (ADR-182 dalgası + ADR-183 düzeltmesi) — ✅ BAŞARILI

### 🔧 YAYIN 2 — ADR-183 DÜZELTMESİ (aynı gün, kullanıcı canlıda iki hata bildirdi)
**Yayınlanan commit:** `7cbb52b` · **Masaüstü:** 1.0.161 → **1.0.162** (checksum `43048B6D…2A03E251`) ·
API + Web yeniden dağıtıldı · **yine MIGRATION YOK, canlı şema 81** · yayın sonrası kontroller **28/28**.

| Bildirilen hata | Düzeltme | Canlı kanıt |
|---|---|---|
| **Araç Raporu — Günlük** verisi olmayan satırları listeliyordu (ekran görüntüsünde tüm ölçüm sütunları "-") | O gün HİÇ verisi olmayan (araç, gün) satırı ÜRETİLMEZ; ölçüm sütunlarından biri bile doluysa satır GELİR (ör. yakıt yok ama bakım malzemesi var) | **1.972 → 195 satır**; "tüm ölçüm sütunları boş" satır sayısı **0** |
| **Stok Hareketleri — Günlük** gün×tür ÖZETİ veriyordu ("26.08 · Giriş · 20 işlem") | Gün gün ilerleyen DÖKÜM: o günün HER hareketi malzemesiyle TEK TEK | **1 özet satırı → 20 satır**, 5 → **10 kolon** (Tarih·Tür·Kod·Malzeme·Miktar·Birim·Kaynak·Hedef·Belge No·Durum) |

**Korunanlar:** dönem raporu `vehicle` TAM FİLO davranışını sürdürür (**68 araç** canlıda doğrulandı) ·
`stock-movements` detay raporu değişmedi (20 satır) · `fuel`/`fuel-daily` aynen (46 araç / 195 satır).
Doğrulama: tam süit **3.016 → 2.977 geçti / 0 başarısız / 39 atlanan** · izole PG **47/47** · 3 Release **0 hata**.

### YAYIN 1 — ADR-182 dalgası
**Yayınlanan commit:** `386b22d` · **Masaüstü:** 1.0.160 → **1.0.161** (checksum `FDEC8079…B38BFCB8`, 86,2 MB)
**API** (`fly.toml` → depowise-erp) ve **Web** (`fly.web.toml` → depowise-web) yeniden dağıtıldı.

| Bileşen | Durum |
|---|---|
| **M — Excel Merkezi** (ADR-176) | ✅ YAYINLANDI |
| **O — Barkod/QR** (ADR-177) | ✅ YAYINLANDI |
| **FIN düzeltmeleri — Migration082 HARİÇ** (ADR-178/179 + ADR-180 geri çekmesi) | ✅ YAYINLANDI |
| **Rapor Ara İşi** (ADR-181: `vehicle-daily` + 8 kategori yetkisi) | ✅ YAYINLANDI |
| **ARA İŞ 2 PAKET-1** (ADR-182: S1–S5) | ✅ YAYINLANDI |

**MIGRATION SONUCU: HİÇBİR MİGRATION UYGULANMADI — canlı şema 81'de KALDI.** Kanıt: yayınlanan imajda
`Migration082` dosyası YOK ve katalogda 0 referans var; katalog azamisi **81** = yayın öncesi canlı şema;
runner yalnız mevcut sürümden BÜYÜK migration uygular → uygulanacak hiçbir şey yoktu. API açılışta
migration çalıştırır; başarısız olsaydı ayağa kalkamazdı — API sağlıklı ve gerçek istemciler senkron oluyor.
⚠️ Üretim veritabanına **doğrudan bağlanılmadı** (SELECT dahil) — kanıt yapısaldır, DB'den okuma değil.

**Yayın sonrası salt-okunur kontroller: 28/28 BAŞARILI** (test hesabıyla; hiçbir kayıt oluşturulmadı/
değiştirilmedi). Öne çıkanlar: canlı rapor kataloğu **25 rapor** · yeni raporların üçü de katalogda ve
çalışıyor (`fuel-daily` 213 satır · `stock-movements-daily` 1 satır · `daily-activity` 8 kolon) ·
**yeni kapsam sözleşmesi canlıda görünür: `vehicle` 68 araç (tam filo) ↔ `fuel` 46 araç (yalnız fişi olan)** ·
token'sız 401 · bilinmeyen rapor türü 400 · sahte `companyId` ile veri sızmıyor · QR/fotoğraf/kişisel
tercih uçları canlı. `daily-activity` 0 satır döndü çünkü **Günlük Faaliyet ekranının kendisi canlıda
0 kayıt içeriyor** (`/api/daily` → 0 ile doğrulandı) — sorun değil.

⚠️ **KULLANICI İŞİ:** yeni **`Rapor: Günlük Faaliyet`** (`report_daily_activity`) yetkisi hiçbir role
otomatik açılmadı (deny-by-default) — Yetkiler ekranından açılmalı. Admin/firma admini bypass ile görür.
Masaüstü makineler uygulama açıkken ≤60 sn'de "Yeni güncelleme mevcut" uyarısı alır → **1.0.161**.

---

---

## 📌 AKTİF ARA İŞLER — 2026 (durum makinesi: ANALİZ BEKLİYOR → ANALİZ TAMAM/KARAR BEKLİYOR → KARAR VERİLDİ → UYGULAMA → TEST → YAYIN ÖNCESİ → YAYIN BEKLİYOR → YAYINLANDI)

| İş | Durum | Not |
|---|---|---|
| RAPOR ARA İŞİ (ADR-181: vehicle-daily + 8 kategori yetkisi) | ⛔ **YAYIN BEKLİYOR** | Kod+test tamam; "YAYINLA" onayı bekleniyor |
| İŞ 1 — Fotoğraf sunucu-otoriteli + silme kapısı | ✅ **KOD+TEST TAMAM** (S5 · commit `a638c51`) | Sunucu-otoriteli; web görüntüleme/silme tamamlandı; silme yalnız Düzenle+Delete; eski yereller sha256 ile bir kez taşınır |
| İŞ 2 — Yakıt raporu tarih/araç listesi | ✅ **KOD+TEST TAMAM** (S1 · commit `fc3e2fd`) | Masaüstü tarih bugu düzeltildi (web'e dokunulmadı); rapor yalnız verisi olan araçları listeler; canlı kayıtlar korundu; PK-T4 taraması raporlandı |
| İŞ 3 — "Yakıtı Veren" son seçim | ✅ **KOD+TEST TAMAM** (S2 · commit `f2d7daf`) | Kişisel tercih; "Yakıtı Alan" kapsam dışı; migration/yeni API ucu YOK |
| İŞ 4 — Yakıt Günlük + Stok Hareketleri Günlük | ✅ **KOD+TEST TAMAM** (S3 · commit `142b2b5`) | `fuel-daily` + `stock-movements-daily`; mevcut raporlar regresyonla korundu |
| İŞ 5 — Günlük Faaliyet — Detay raporu | ✅ **KOD+TEST TAMAM** (S4 · commit `77805cd`) | Yeni çoklu-seçim filtresi (6 katman) + 9. kategori `report_daily_activity` (kapalı başlar) |
| İŞ 6 — Custom Rapor Tasarımcısı | ⏸️ **AYRI FAZ** (kullanıcı kararı — PAKET-1 dışı, kodlanmaz) | ⚠️ MIGRATION GEREKİR; çerçeve: Plan §5 |
| İŞ 7 — Ekip + Hiyerarşi + Onay + Onaylamalarım | ⏸️ **AYRI FAZ** (kullanıcı kararı — PAKET-1 dışı, kodlanmaz) | ⚠️ MIGRATION+SENKRON GEREKİR; çerçeve: Plan §5 |
| FIN-B1 / Migration082 | ⛔ **AYRI ONAY BEKLİYOR** | Tasarım `35d7bce`; canlı şema 81 (ADR-180) |
| N — Mobil | ⏭️ **ATLANDI** | Kod yazılmadı; bu döngüde uygulanmayacak |

**PAKET-1 (İş 1+2+3+4+5) ✅ TAMAMLANDI ve ✅ YAYINLANDI (2026-08-29).** Tamamı MIGRATION'SIZ;
uygulama kaydı: [ARA_IS_2_02_UYGULAMA.md](ARA_IS_2_02_UYGULAMA.md) · karar: ADR-182.

### 🔁 ARA İŞ FAZ TAKİBİ (kalıcı — her aşama geçişinde BURASI güncellenir)

> Kullanıcı protokolü (2026-08-29): her ara iş bu faz zincirini izler ve durum repository'de tutulur.
> `FAZ 0 DURUM DOĞRULAMA → FAZ 1 ANALİZ → FAZ 2 KARAR BEKLİYOR → FAZ 3 UYGULAMA → FAZ 4 TEST/DOĞRULAMA
> → FAZ 5 YAYIN ÖNCESİ ONAY → FAZ 6 YAYIN → FAZ 7 YAYIN SONRASI DOĞRULAMA → FAZ 8 TAMAMLANDI/ANA ROADMAP'E DÖNÜŞ`

| Ara iş | Bulunduğu faz | Not |
|---|---|---|
| **ARA İŞ 3 — TARİH DÖNÜŞÜM HATALARI** | **FAZ 0–8 ✅ TAMAMLANDI** — yayınlandı (kod `ab0d0d4`, masaüstü **1.0.163**, ADR-184, migration yok) | Analiz+kararlar: [ARA_IS_3_00_ANALIZ.md](ARA_IS_3_00_ANALIZ.md). Kullanıcının seçtiği takvim tarihinin yerel ofset yüzünden **bir gün erken** yazılması. **Yeniden sayıldı: 11 ekran / 19 masaüstü noktası** (S1d'deki "10/17" eksikti) **+ web'de 1 gerçek hata** (`Stock.razor:258` — S1d yalnız masaüstünü taramıştı); web'in kalan 10 noktası DOĞRU. **Kararlar: PK-TAR-01=A · 02=A · 03=A · 04=A · 05=A · 06=B · 07=A.** **KOD YOK · TEST YOK · MIGRATION GEREKMİYOR · production'a dokunulmadı.** |
| ARA İŞ 2 PAKET-1 (+ADR-183) | ✅ FAZ 8 — TAMAMLANDI, ana roadmap'e dönüldü | Yayınlandı: kod `7cbb52b`, kayıt `e5583c4`, masaüstü 1.0.162 |
| Rapor Ara İşi (ADR-181) | ✅ FAZ 8 | Yayınlandı |
| Custom Rapor | ⏸️ FAZ 1 öncesi (analiz çerçevesi var, KOD YOK) | Ayrı faz — **migration gerektirir** |
| Ekip + Hiyerarşi + Onay | ⏸️ FAZ 1 öncesi (analiz çerçevesi var, KOD YOK) | Ayrı faz — **migration + senkron gerektirir** |
| S1d tarih kayması bulguları | ⏸️ karar bekliyor (KOD YOK) | 10 ekran / 17 yazım noktası — ayrı iş |
| FIN-B1 / Migration082 | ⏸️ ANA ROADMAP maddesi — ayrı onay | Ara iş değil; AŞAMA 3'ün açık maddesi |

### 🤖 CHATGPT DEVAM NOKTASI (2026-08-29 · yayın sonrası · FAZ 0 yeniden doğrulandı)

**YAYIN DURUMU**
- **ARA İŞ 2 PAKET-1:** ✅ TAMAMLANDI + ✅ **YAYINLANDI**
- **Rapor Ara İşi ADR-181:** ✅ **YAYINLANDI**
- **M — Excel Merkezi:** ✅ **YAYINLANDI**
- **O — Barkod/QR:** ✅ **YAYINLANDI**
- **FIN düzeltmeleri (082 hariç):** ✅ **YAYINLANDI**
- **N — Mobil:** ⏭️ **ATLANDI** (kod yazılmadı)
- **FIN-B1 / Migration082:** ⏸️ **AYRI ONAY BEKLİYOR** — master'dan geri çekilmiş (ADR-180), tasarım `35d7bce`'de; **canlı şema 81**
- **Custom Rapor:** ⏸️ **AYRI FAZ — henüz başlanmadı**
- **Ekip + Onay:** ⏸️ **AYRI FAZ — henüz başlanmadı**

**CANLI DURUM**
- Son yayınlanan commit: **`7cbb52b`** (ADR-183 düzeltmesi) · Masaüstü **1.0.162** · API + Web güncel
- Aynı gün iki yayın yapıldı: `386b22d` → 1.0.161 (ADR-182 dalgası) · `7cbb52b` → 1.0.162 (ADR-183 düzeltmesi)
- **Canlı şema: 81** — iki yayında da **hiçbir migration uygulanmadı** (imajda Migration082 yok, katalog azamisi 81)
- Yayın sonrası salt-okunur kontroller: **28/28 başarılı** (her iki yayında); canlı rapor kataloğu **25 rapor**
- Production veritabanına doğrudan bağlanılmadı (SELECT dahil)
- ⭐ ADR-183 canlı kanıtı: `vehicle-daily` 1.972 → **195 satır** (boş satır 0) · `stock-movements-daily`
  1 özet → **20 ayrı malzeme satırı** (10 kolon) · `vehicle` dönem raporu **68 araç** (tam filo korundu)

**ANA DEVAM NOKTASI: AŞAMA 3 — FINAL KARAR PAKETİ**
Ara işlerin yayınlanmış olması ana roadmap sırasını **değiştirmez**. AŞAMA 3 maddelerinin dosyadaki
gerçek son durumu (kararlar TEKRAR SORULMAZ):
| Madde | Durum |
|---|---|
| **FIN-B1 / Migration082** | ⏸️ **TEK AÇIK MADDE** — ayrı onay bekliyor; canlı şema 81; tasarım `35d7bce` |
| YET-01 | ✅ uygulandı (ADR-179) — iki işlevsiz yetki anahtarı kaldırıldı |
| ARC-01(a) | ✅ incelemede zaten çözülmüş çıktı (kod gerekmedi) |
| STK-B2 | ✅ karar: HAYIR (mevcut davranış korunuyor; FIN8 kilidi) |
| RPR-02 | ✅ zaten çözülmüş çıktı (RPR-04/RPR-07) |
| SNK-05 | ✅ karar (a): mevcut sözleşme kilitlendi (online ilk-kazanır · offline LWW) |
| MAK-01/b | ✅ korundu |

**DURUM ÖZETİ (ChatGPT için tek bakışta)**
| Alan | Değer |
|---|---|
| Ana roadmap aşaması | **AŞAMA 3 — FINAL KARAR PAKETİ** (ara işler bu sırayı değiştirmez) |
| Aktif ara iş / aşaması | **YOK** — ARA İŞ 3 ✅ **YAYINLANDI** (masaüstü **1.0.163**, API + Web güncel, şema **81**); ana roadmap'e dönüldü |
| Ana roadmap aktif iş | **AŞAMA 3 — FIN-B1 / Migration082** · **FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ✅ KARARLAR ONAYLANDI (ADR-185)** · **FAZ 3 ⏸️ "UYGULAMA BAŞLASIN" BEKLİYOR** · kararlar: PK-FIN-01=A · **02=B (`sync_inbox` kapsamda)** · 03=C · 04=A · 05=A · kod/migration/test YOK, production'a bağlanılmadı, katalog azamisi **81** |
| Yayın bekleyen işler | **YOK — yayın havuzu BOŞ** |
| Kodlanmamış ayrı fazlar | Custom Rapor (migration'lı) · Ekip+Hiyerarşi+Onay (migration+senkron'lu) |
| Migration durumu | **Canlı şema 81** · katalog azamisi 81 · Migration082 master'da YOK |
| Production durumu | Son yayın 2026-08-29; **bu turda dokunulmadı** |
| Son commit | `e5583c4` (yayın kaydı) · son kod commit'i `7cbb52b` |
| Son başarılı test | Tam süit **2.977/0/39** · izole PG **47/47** · 3 Release **0 hata** (`7cbb52b`) |
| Bekleyen ana karar | **FIN-B1 / Migration082** (AŞAMA 3'ün tek açık maddesi) |
| Sonraki TEK iş | **Kullanıcının "UYGULAMA BAŞLASIN" onayı** → FIN-B1 FAZ 3 (Migration082 + kod + 1.0.164) |
| ARA İŞ 3 kararları (ADR-184) | **PK-TAR-01=A** 20 noktanın tamamı · **02=A** yalnız ileriye dönük (geçmiş veri AYRI iş) · **03=A** tek kaynaklı dönüşüm + parite/kaynak kilitleri · **04=A** zaman damgalarına dokunulmaz · **05=A** eski istemciler kabul + yayın notu · **06=B** production ölçümü YOK · **07=A** tek başına migration'sız yayın (şema 81 kalır) |
| Ara iş bitince dönülecek nokta | **AŞAMA 3 — FINAL KARAR PAKETİ → FIN-B1 / Migration082 ayrı onay süreci** |

**⭐ S1d ARTIK AKTİF ARA İŞTİR (ARA İŞ 3).** Aşağıdaki eski özet, ARA İŞ 3'ün FAZ 1 analiziyle
**düzeltildi ve genişletildi**: gerçek sayı **11 ekran / 19 masaüstü noktası + web'de 1 nokta**
(`Stock.razor:258`). Güncel ve bağlayıcı liste: [ARA_IS_3_00_ANALIZ.md](ARA_IS_3_00_ANALIZ.md) §8.
Henüz **düzeltilmedi** — PK-TAR kararları bekleniyor.

**ESKİ ÖZET (tarihsel):** S1d taramasında masaüstünde **10 ekran / 17 tarih yazım noktasında**
aynı saat-dilimi kayması sınıfı bulundu (en ağırı stok belge tarihleri: Stok Girişi ×3, Stok Sayım,
Stok Dağıtım — her seferinde bir gün kayıyor; ayrıca bakım, muayene, fatura, cari, ödeme, finans,
günlük faaliyet, talep ekranlarında kullanıcı gün seçince kayıyor). **Bu bulgular HENÜZ DÜZELTİLMEDİ**
— kullanıcı kararıyla ileride ayrı iş olarak ele alınacak. Tam liste: ARA_IS_2_02_UYGULAMA.md § S1d.

**KULLANICI İŞİ (yayın sonrası):** `Rapor: Günlük Faaliyet` (`report_daily_activity`) yetkisini
Yetkiler ekranından ilgili rollere açmak; açılana dek admin olmayanlar bu raporu göremez (bilinçli).

- **Bir sonraki yapılacak TEK iş:** AŞAMA 3 — FIN-B1/Migration082 için kullanıcı kararı
- **Yeni oturumda okunacak belgeler:** CURRENT_PHASE.md → MASTER_ROADMAP.md → ARA_IS_2_02_UYGULAMA.md → FINAL_KARAR_PAKETI.md → DECISIONS.md (ADR-180/181/182)
- Claude Code yeni oturumda önce CURRENT_PHASE.md + MASTER_ROADMAP.md + son uygulama raporunu okuyarak devam noktasını kendisi tespit etmelidir.

---

## ✅ ARA İŞ — RAPOR GÜNLÜK KIRILIM + RAPOR TÜRÜ YETKİLERİ — KOD+TEST TAMAM · ⛔ YAYIN ONAYI BEKLİYOR (2026-08-29, ADR-181)

PK-R1..R4 = **A·A·A·B** uygulandı — [RAPOR_ARA_IS_01.md](RAPOR_ARA_IS_01.md):
(1) **"Araç Raporu — Günlük"** (`vehicle-daily`) katalog satırı — mevcut toplam rapora dokunulmadı,
günlük≡dönem tutarlılığı testli, boş günler 0 satırıyla, iki lehçe birebir, MIGRATION YOK;
(2) **8 rapor kategori yetkisi** (`report_vehicle`…`report_accounting`) — `reports` üst kapı + kategori
ikinci kapı, üç katmanda (API katalog · masaüstü katalog · ortak `Run`), tek merkez eşleme, MIGRATION YOK;
(3) **ADR-180 ön koşulu:** FIN-B1/Migration082 çifti master'dan geri çekildi → **katalog azamisi 81 =
canlı şema, deploy'da migration ÇALIŞMAZ**; FIN-B1 ayrı onay bekliyor (tasarım `35d7bce`).
Doğrulama: tam süit **2.893/0/38** · izole PG **46/46** · 3 Release build **0 hata** · prod'a bağlanılmadı.
**Sıradaki adım: kullanıcının "YAYINLA" onayı** → deploy (M+O+FIN(082 hariç)+ara iş) → yayın sonrası
salt-okunur kontroller → kategorileri Yetkiler ekranından atama → ana plana dönüş (AŞAMA 3: FIN-B1/082 ayrı onay).

---

## ✅ ⭐ TOPLU YAYIN — 2026-08-28 (C,A,E,B,D,P,F,H,I,J,K,L · ADR-164..175)

Kullanıcı onayıyla **Migration073..081 canlıya uygulandı**. API **v174** · Web **v199** · Masaüstü
**1.0.160** · Şema **72→81**. Kanıtlar: [TOPLU_YAYIN_2026-08-28.md](TOPLU_YAYIN_2026-08-28.md) —
pg_dump yedeği doğrulandı; deploy öncesi/sonrası 77+15 tablonun sayım/karma karşılaştırması:
**mevcut TÜM tabloların satır içerikleri BİT-BİT AYNI, değişen tek şey schema_migrations, 15 yeni
tablo BOŞ**; sağlık/senkron/tenant kontrolleri yeşil.
⚠️ **KULLANICI İŞİ:** yeni yetkiler HİÇBİR role otomatik açılmadı — Yetkiler ekranından rollere açılmalı:
Ekipman · Zimmet · Maliyet Merkezi · Satın Alma · İş Emirleri · Takvim · Duyurular-YAZMA
(+ malzeme zimmeti/tüketim/mal kabul için STOK yetkisi). Masaüstü makineler uygulama açıkken ≤60 sn'de
güncelleme uyarısı alır → 1.0.160'a güncellenmeli.
İlgisiz bulgu (yayın dışı, kayda geçti): `material_compatible_vehicles` damgasız olduğundan delta
senkronda her tur tam iner (22 satır — zararsız; SNK ailesi).

---

## 🔨 AKTİF — YENİ ÖZELLİK YOL HARİTASI (MASTER_ROADMAP, 2026-08-27)

**FAZ 2 TAMAMLANDI:** B Zimmet ✅ · D Maliyet Merkezi ✅ · **P Satın Alma ✅ (ADR-169, SatinAlmaTests 13/13; Migration078).**
**FAZ 3/SIRA 7 — F İş Emri ✅ (ADR-170, IsEmriTests 16/16; Migration079; PK-F1..F9 aynen).**
**FAZ 3/SIRA 8 — H Takvim ✅ (2026-08-28, ADR-171, TakvimTests 16/16; Migration080; PK-H1..H5 aynen) —
[H_TAKVIM_01.md](H_TAKVIM_01.md). FAZ 3 BİTTİ (7b hariç).**
**FAZ 4/SIRA 9 — I Bildirim Merkezi ✅ (2026-08-28, ADR-172, BildirimTests 12/12; MIGRATION YOK — şema 80;
PK-I1..I4 aynen) — [I_BILDIRIM_01.md](I_BILDIRIM_01.md).** Türetilmiş bildirim + çan/sayaç iki platformda;
okundu cihaz-yerel (alert_reads'e dokunulmadı).
**FAZ 4/SIRA 10 — J Duyuru ✅ (2026-08-28, ADR-173, DuyuruTests 12/12; Migration081; PK-J1..J5 aynen) —
[J_DUYURU_01.md](J_DUYURU_01.md).** Okuma herkese (IsPublicRead) + yazma kapalı; bildirim entegrasyonu;
okundu=alert_reads imzası (düzenlenince yeniden okunmamış).
**FAZ 4/SIRA 11 — K Global Arama ✅ (2026-08-28, ADR-174, AramaTests 12/12; MIGRATION YOK — şema 81;
PK-K1..K5 aynen) — [K_ARAMA_01.md](K_ARAMA_01.md).** Üst bar kutusu iki platformda; kaynak-yetki kapılı
türetilmiş arama; masaüstü çevrimdışı yerel + çevrimiçi Proje/Evrak.
**FAZ 4/SIRA 12 — L Dashboard ✅ (2026-08-28, ADR-175, PanoTests 9/9; MIGRATION YOK — şema 81;
PK-L1..L4 aynen) — [L_DASHBOARD_01.md](L_DASHBOARD_01.md). FAZ 4 BİTTİ.** Uyarı kartları 4→8 + Açık İş
Emri/Sipariş kartları + Bugünün Takvimi/Aktif Duyurular şeritleri (yetki yoksa kart hiç görünmez).
**FAZ 5/SIRA 13 — M Excel Merkezi ✅ (2026-08-28, ADR-176, ExcelMerkeziTests 10/10 + hedefli regresyon
293/293; MIGRATION YOK — şema 81; PK-M1..M5 = A-A-A-A-A aynen) — [M_EXCEL_01.md](M_EXCEL_01.md).
⛔ YAYINLANMADI (yeni strateji gereği — build+test seviyesinde bitti).** Ekran çifti iki platformda
"Excel Merkezi" oldu; merkezi dışa aktarım 15 kaynak (ortak `ExcelCenterService` — parite yapısal);
web'e merkezi Dışa Aktar + `GET /api/export/{entity}` eklendi; import 7 sette SABİT, "zaten var → atla"
korundu ve testle kilitlendi; yanıltıcı "Güncellenen" etiketi düzeltildi. Yeni yetki YOK.
**FAZ 5/SIRA 14 — O Barkod/QR ✅ (2026-08-29, ADR-177, BarkodQrTests 15/15 + TAM paket regresyonu;
MIGRATION YOK — şema 81; PK-O1..O4 = A-A-A-A aynen) — [O_BARKOD_QR_01.md](O_BARKOD_QR_01.md).
⛔ YAYINLANMADI (yeni strateji gereği — build+test seviyesinde bitti).** Tara→bul→git mevcut Global
Arama üzerinden (Ctrl+K odak + tam-tek eşleşmede otomatik kayıt açılışı; panel davranışı diğer her
durumda aynen); QR etiketi Malzeme·Araç·Ekipman'da (QRCoder; içerik = kayıt kodu düz metin; masaüstü
çevrimdışı üretir, web `GET /api/qr/...`). Yeni yetki YOK; tarama salt-okunur (iş operasyonu tetiklemez).
Tam pakette FAZ 1-4'ten kalma eskimiş TSR12 sabiti bulunup kök nedenle düzeltildi (kilit gevşetilmedi).
**FAZ 5/SIRA 15 — N Mobil: ⏭️ ATLANDI (kullanıcı kararı 2026-08-29)** — bu geliştirme döngüsünde
uygulanmayacak; N için kod/migration/test yazılmadı, hiçbir sürüme dahil edilmedi.
**FİNAL — Kullanıcı Simülasyonu ve Stabilizasyon ✅ (2026-08-29, ADR-178; PK-FIN1..FIN5=A aynen;
MIGRATION YOK — şema 81; production'a HİÇBİR aşamada bağlanılmadı; yayın YOK) —
[FINAL_STABILIZASYON_01.md](FINAL_STABILIZASYON_01.md).** Simülasyon FAZ 1-5 modülleriyle genişletildi
(+yalnız-localhost koruması); ~7.500 kayıt sentetik tohum; 10 makine × 12 tur İKİ LEHÇEDE (izole SQLite +
izole test-PG) SON KOŞULARDA **0 BULGU**; PG testleri İLK KEZ topluca **45/45**; TAM SÜİT **2.888 →
2.853 geçti / 0 başarısız / 35 bilinçli-atlanan**; üç Release build 0 hata. KRİTİK bulgu YOK; FIN-B1
(op-id firma-üstü benzersizlik — migration ister) DURDURULUP karar paketine yazıldı, FIN-M1/M2 ORTA →
KNOWN_ISSUES. **KARAR PAKETİ UYGULANDI ✅ (2026-08-29, ADR-179):** FIN-B1 → **Migration082 hazır ve
iki lehçede testli, ⛔ PRODUCTION'DA ÇALIŞTIRILMADI — CANLI ŞEMA 81, yayın penceresi bekliyor**
(önkoşul: pg_dump + kısa indeks kilidi) + 8 noktada firma-kapsamlı idempotency · YET-01 iki işlevsiz
anahtar kaldırıldı · ARC-01(a) ve RPR-02 incelemede ZATEN çözülmüş çıktı (RPR-04/RPR-07 — kod
gerekmedi) · STK-B2 hayır (FIN8 kilidi) · SNK-05(a) mevcut sözleşme kilitlendi (online ilk-kazanır ·
offline LWW; senkron koduna dokunulmadı) · MAK-01/b korundu. **⚠️ ADR-180 (2026-08-29, PK-R4=B):
FIN-B1/Migration082 çifti master'dan GERİ ÇEKİLDİ** — rapor ara işinin yayınına karışmaması için;
eski sözleşme FIN5 ile yeniden kilitli, katalog azamisi 81 = canlı şema; **FIN-B1 tamamlanmış sayılmaz,
AYRI onay bekliyor** (tasarım `35d7bce`). **⛔ Yayın bekleyen kod havuzu: M (EXL-01) + O (BAR-01) +
FIN düzeltmeleri (082 HARİÇ) + rapor ara işi.**
(+ ayrı küçük iş: Bakım-Ekipman 7b.)

**FAZ 1 TAMAMLANDI:** C — Proje/Şantiye ✅ (ADR-164) · A — Evrak/Belge ✅ (ADR-165) ·
E — Varlık/Ekipman ✅ (ADR-166, 2026-08-28, EkipmanTests 12/12). Üçü de **canlıya YAYINLANMADI**:
Migration073+074+075 deploy anında koşacağından yayın AYRI onay ister; deploy öncesi/sonrası canlı
salt-okunur sayım alınacak. ⚠️ Yayında üç yeni yetki kapalı gelir: Ekipman modülü (+ Evrak'ın files
modülü mevcut) — rollere açılmalı. Sıradaki: **B — Zimmet** (önce ürün soruları: stok ilişkisi, devir).
Tek doğru kaynak: MASTER_ROADMAP.md + PRJ_01/EVRAK_01/EKP_01 kontrol belgeleri.

---
## 🔨 DEVAM EDEN — "EKSİK ALANLAR" LİSTESİ (kullanıcı maddeleri, 2026-08-27)

> Kullanıcı ekranları gezerken eksikleri **numaralı** yazıyor. Kural: **tek tek yapılır, hiçbiri tek
> başına yayınlanmaz — hepsi bitince TEK toplu yayın.** (Kullanıcı: *"bunları hemen yayınlamayacağız
> şimdi tek tek yapalım en son yayınlarız… ben aklıma gelenleri yazmaya devam edeceğim."*)

| # | Madde | Durum | Karar |
|---|---|---|---|
| 1 | Kayıt ekranlarında **tarih alanı** + iş günü / kayıt anı ayrımı + kehribar tarih alanları + `btn-backdate` yetkisi | ✅ bitti | **ADR-162** |
| 2 | Her ekrana özel **Ekran Araçları** menüsü (kayıt geçmişi + ekran bilgisi) + `btn-screen-log` yetkisi | ✅ bitti | **ADR-163** |
| 3+ | — | ⏳ kullanıcıdan bekleniyor | — |

**Yayın durumu:** ✅ **yayınlandı — 2026-08-27:** API **v173** · web **v198** · masaüstü **1.0.159**.
Kullanıcı 1. ve 2. madde bitince yayın istedi; sonraki maddeler yine biriktirilip toplu yayınlanır.

---

## ✅ TAMAMLANAN — WEB "AURORA CAM v4" TASARIM PAKETİ (2026-08-27)

Karar: **ADR-161** · Kaynak: kullanıcının tasarım aracıyla hazırladığı paket. **Kapsam yalnız web**;
`src/DepoWise.Desktop/` içinde **sıfır diff** (doğrulandı).

| Adım | Ne yapıldı |
|---|---|
| Stil katmanı | `app.css` §16 (Aurora Cam kabuk + Komuta tablo dili) + §17 (ZB-1…ZB-10) → **44 ekran** markup'a dokunulmadan yeni dili giydi |
| Yeni özellik | `/api/materials/grid` → **eklemeli** `summary` (kritik / kategori / stok değeri); liste, Excel ve özet artık AYNI filtre kurulumunu paylaşır |
| Kabuk | Kullanıcı rozetinde baş harf avatarı |
| Ekranlar | Ana ekran · Malzemeler (özet şeridi + Yeni Malzeme + kritik satır + stok barı) · Araçlar · Günlük Faaliyet · Stok Hareketleri · Soon · Çöp Kutusu · **47 ekranda ZB-1 başlık tipografisi** |

**Diff disiplini:** 43 ekranda değişiklik **tek satır** (başlık sınıfı); yalnız 4 ekranda daha geniş
düzenleme. Ekran yapısını yeniden kuran maddeler yerine sınıf/eklemeli değişiklikler tercih edildi.

### Pakette bulunan ve UYARLANAN nokta
Paketin §17 bloğu `.dw-badge` **tabanını** yeniden tanımlıyordu; projede §9.4'te zaten olgun bir rozet
sistemi var ve Araç Listesi onu kullanıyor. Paketin tanımı mevcut rozetleri **eziyordu** (21px yükseklik
kaybı, punto/kalınlık değişimi → tablo hücresinde hizadan çıkma). Taban korundu, paketin kısa adları
takma ad olarak bağlandı. Çakışan diğer 13 seçici incelendi: hepsi kasıtlı; `.dw-grid` **sticky ilk
kolon** kuralı korunuyor.

### Bilinçli uygulanmayanlar
Günlük Faaliyet tarih kısayolları (ekranda tarih aralığı filtresi yok) · Araçlarda "geciken/yaklaşan"
ayrımı (veri bu ayrımı vermiyor) · Kota mini-barı (kota hazır METİN geliyor) · MudChip→`.dw-badge`
dönüşümleri (rozet işlevi zaten karşılanıyor). Hepsi ADR-161'de gerekçeli.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Release derleme | masaüstü · API · web → **0 hata** |
| Tam test seti | **2660 geçti / 0 başarısız / 37 atlandı** |
| CSS | Tarayıcıda ayrıştı — 308 kural, yeni desenler canlı, MudBlazor'dan sonra yükleniyor |
| Rota | Kimlik gerektirmeyen rotalar 200, Blazor hata kutusu yok |
| Masaüstü | **Sıfır diff** |
| Migration | **Gerekmedi** (şema 72) |

**Görsel doğrulamanın sınırı:** web'e giriş yapılamadığı için kimlik arkasındaki ekranlar gözle tek tek
denetlenemedi. Markup değişiklikleri bu yüzden bilinçli olarak dar tutuldu.

---

## ✅ TAMAMLANAN — KAPSAMLI RAPOR DENETİMİ (2026-08-27)

Karar: **ADR-160** · Kullanıcı: *"Raporların hepsini kapsamlı analiz etmeni istiyorum. Giriş-Çıkış
ekranından bir sürü depo girişi yaptım ama depo girişi raporunda hiçbiri listelenmiyor."*

| # | Kusur | Kullanıcıya yansıması | Düzeltme |
|---|---|---|---|
| 1 | Çekme sonrası **stok bakiyesi hesaplanmıyordu** (`stock_balances` türetilmiş, senkronda taşınmaz) | Stok Durumu raporu **sıfır** · malzeme listesi STOK kolonu 0 · düşük stok uyarısı çalışmıyor | `ApplyPull` artık defterden yeniden hesaplıyor |
| 2 | **«Depo Girişi»** raporu yalnız YAKIT deposunu gösteriyor ama adı bunu söylemiyordu | Malzeme girişi yapan kullanıcı raporu boş buluyor | Ad **«Yakıt Depo Girişi»** · açıklama «Stok Hareketleri»ne yönlendiriyor |
| 3 | **Sonraki tarihi boş** muayene/sigorta belgesi hiçbir aralıkta listelenmiyordu | Ekranda duran belge raporda yok | Tarih süzgeci NULL'a izin veriyor (yalnız bu rapor) |

**Kalan 21 rapor temiz.** `RaporKapsamliTaramaTests` tek firmaya her modülden birer normal kayıt
girer ve katalogdaki HER raporu çalıştırıp en az bir satır döndüğünü, kolonların dolu olduğunu ve
satır/kolon sayısının uyuştuğunu doğrular. Yeni rapor eklenirse **otomatik kapsanır**.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Yeniden üretim | 3 kusurun üçü de düzeltmeden ÖNCE testle gösterildi |
| Mutasyon | 2/2 yakalandı (bakiye hesabı kapatıldı · NULL toleransı kaldırıldı) |
| Yeni test | **70** (`RaporVeriGorunurlukTests` 6 + `RaporKapsamliTaramaTests` 64) |
| Release derleme | masaüstü · API · web → **0 hata** |
| Migration | **Gerekmedi** (şema 72) |

---

## ✅ TAMAMLANAN — TSN: TANIM SENKRONU MARKA/ÜST ALANLARINI SİLİYORDU (2026-08-27)

Karar: **ADR-159** · Kullanıcı bildirimi: *"model alanında yeni kayıt oluşturuyorum ama farklı bir
kayıt gireceğim zaman daha önce eklediğim model listelenmiyor."*

**Kök neden (gerçek HTTP hattı üzerinde kanıtlandı).** `GET /api/lookups/sync` satırları sözlük
döndürür; anahtarlar veritabanı **sütun adlarıdır** (`brand_id`, `parent_id`, `brand_type`).
ASP.NET Core sözlük anahtarlarını camelCase'e ÇEVİRMEZ. Masaüstü ise `brandId` arıyordu ve
`TryGetProperty` harf duyarlı olduğu için alanı hiç bulamıyor, "boş geldi" sanıp sütunu
`UPDATE … SET brand_id=NULL` ile **siliyordu**. `updated_at` "şimdi" damgalandığı için LWW yerel
satırı yeni sayıyor → iş senkronu doğru değeri geri yazamıyor → bir sonraki push NULL'u **sunucuya**
taşıyor.

| Etkilenen | Görünen sonuç |
|---|---|
| `vehicle_models.brand_id` | **Model markasına göre listelenmiyordu** (kullanıcının bildirdiği hata) |
| `material_categories.parent_id` | Alt kategori üst seviyeye çıkıyordu |
| `brands.brand_type` | Marka hem malzeme hem araç listesinde görünüyordu (`ListBrands` NULL'a toleranslı olduğu için fark edilmemişti) |
| `branches.parent_id` | Yalnız yerelde; branches push'a dahil değil |

**Düzeltme.** `JsonAlan.AlanOku` alanı yazımdan bağımsız okur; `LookupSyncService` artık JSON adı
değil sütun adı verir. **Sunucu sözleşmesi değişmedi → API deploy'u gerekmedi.**

**Onarılmayan (bilinçli).** Hatadan önce açılmış modellerin markası sunucuda da kayıp olabilir.
Çıkarıma dayalı otomatik onarım YAPILMADI (mevcut veriyi değiştirir). Güvenli çözüm: kullanıcı o
modeli yeniden ekler — tekilleştirme ad+marka ikilisine baktığı için doğru yeni kayıt açılır,
**hiçbir şey silinmez**.

---

## ✅ TAMAMLANAN — M6 (İKON SETİ) + M7 (TABLO BAŞLIĞI) · 2026-08-27

Karar: **ADR-158** · Kaynak: kullanıcının Claude Code tasarım aracıyla hazırladığı paket.
**Kapsam yalnız masaüstü** (kullanıcının açık talimatı); web'de tek satır değişmedi.

| Adım | Ne değişti |
|---|---|
| M6 · ikonlar | `Themes/Icons.axaml` — **38 vektör ikon** |
| M6 · menü | 17 alt grup + **6 üst grup** (Malzeme ve Stok · Operasyon · Finans · Raporlar · Kurumsal Yönetim · Sistem Yönetimi) |
| M6 · ana ekran | 5 özet kartı **ayrı ayrı** ikon aldı (önce beşi de aynı kutuydu) · uyarı satırı artık TİPİNE göre ikon · "kategori seçin" ipucu · sürüm/güncelleme kartı |
| M6 · butonlar | 7 emoji buton vektöre çevrildi (paketin "opsiyonel" adımı) |
| M7 · tablo | Başlık bandı marka rengine döndü (38 başlık) · filtre satırı kendi sınıfına ayrıldı · filtre kutuları 8 px dikdörtgen · dolu filtre + sıralanan kolon aksan vurgusu |

**Paketin atladığı eksik:** ortak tablo kontrolünde (`Controls/DataGridView.axaml`) **dördüncü**
filtre satırı vardı; kapsama alındı, yoksa rapor ekranlarında filtre bandı başlık rengine bürünürdü.

**Cümle içindeki emojilere dokunulmadı** (TrashView 🔒, UsersView ✓, LoginWindow 🔒) — metin akışını bozar.

---
## ✅ TAMAMLANAN — ALTINCI TUR: TABLO KOLON HİZASI (2026-08-26)

Karar: **ADR-157** · Kullanıcının ekran görüntüsüyle bildirdiği sorun.

**MAS-04 — Liste tablolarında kolon adları, filtre kutuları ve veriler aynı hizada değildi.**
Dört ayrı kök neden bulundu:

| # | Kusur | Etki | Çözüm |
|---|---|---|---|
| 1 | Filtre hücresinde **dış boşluk** (`Margin`) | kolon başına +8 px, kayma **birikiyordu** | `Margin` → `Padding` (35 hücre + rapor tablosu) |
| 2 | Hücrelerde **üst sınır yok** | uzun değer gövdedeki kolonu genişletiyordu | `MaxWidth = MinWidth` + "…" + ipucu |
| 3 | Başlık ile gövde **ayrı** yatay kayıyordu | yana kaydırınca hiza kopuyordu | ortak kaydırıcı (3 ekran) |
| 4 | Başlık ile veri **kolon sayısı farklı** | Talepler'de başlık 5 / veri 7 | eksik başlıklar tamamlandı (3 ekran) |

Kullanıcının seçimiyle **sütun ayırıcı çizgileri** eklendi (`ColumnRules`): çizgi konum hesaplamaz,
kolonun içine sağa hizalanır → kolon sürüklenince/gizlenince/kaydırılınca kendiliğinden doğru kalır.

**Kapsam: 31 tablo ekranının tamamı.** Web değişmedi (gerçek HTML tablo kullandığı için hiza zaten doğru).

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Taban (tur başı) | 2482 · 0 · 37 |
| Tam test — final koşu 1 | **2523 · 0 başarısız · 37 atlandı** |
| Tam test — final koşu 2 (bağımsız) | **2523 · 0 · 37 → birebir aynı** |
| PostgreSQL (izole küme) | **47 · 0 · 0** (+ yedek lehçe kapısı 4 · 0 · 0) |
| Mutasyon | **16/16 yakalandı** (1 kaçtı → test güçlendirildi, 5 eksik tablo satırı daha bulundu) |
| Yeni test | **41** |
| Yayın | Masaüstü **1.0.155** · üç yönlü sağlama aynı |
| API / Web | **DEĞİŞMEDİ** → deploy gerekmedi (v171 / v195) · şema **72** |

### Bilinçli istisnalar
- Esnek (`*`) kolonlar ve `SharedSizeGroup` kullanan kolonlar — orada kolonu Avalonia zaten eşitler.
- Yazı olmayan hücreler (buton · sayı kutusu · durum rozeti) — sabit genişlik etiketi kırpardı.
  5'i son kolondadır (kayma yayılmaz), 2'si Talepler'in rozet kolonlarıdır.

### Kararınızı bekleyen (değişmedi)
- **ARC-01** — araç seçicisinin firma geneli olması.
- **YET-01** — işlevsiz iki yetki anahtarının yetki ağacından kaldırılması.

---

## 📦 ARŞİV — BEŞİNCİ TUR (2026-08-26)

Tam rapor: [`docs/tests/Denetim_2026-08-26_Besinci_Tur.md`](../tests/Denetim_2026-08-26_Besinci_Tur.md)
Kararlar: **ADR-155 · ADR-156**

Kullanıcının canlı kullanımda **bizzat bildirdiği** iki sorun kapatıldı.

| ID | Sorun | Kök neden | Çözüm |
|---|---|---|---|
| **MAS-03** | Masaüstü Malzeme Giriş-Çıkış tablosu görülemiyordu | **Veri geliyordu** ("19 hareket" sayacı doluydu); form `Auto` satırında ~700 px alıyor, listeye ~50 px kalıyordu | Form kapsayıcı yüksekliğinin **oranıyla** sınırlı + kendi içinde kayar; listeye taban yükseklik. **API/DB/senkron değişmedi** |
| **STK-11** | Formda işlem tarihi yoktu | Şemada ayrım **zaten vardı** (`doc_date` + `created_at`) ve servisler `docDate` alıyordu; eksik olan yalnız arayüz + API alanıydı | "İşlem Tarihi" alanı (varsayılan bugün, geçmiş/gelecek serbest); ekran+rapor+Excel işlem tarihini gösterir; audit gerçek zamanı korur |

> 🟢 **YENİ MIGRATION AÇILMADI — şema 72'de kaldı.**

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Taban (tur başı, yeniden ölçüldü) | 2451 · 0 · 37 |
| Tam test — final koşu 1 | **2482 · 0 başarısız · 37 atlandı** |
| Tam test — final koşu 2 (bağımsız) | **2482 · 0 · 37 → birebir aynı** |
| PostgreSQL (izole küme) | **47 · 0 · 0** (+ yedek lehçe kapısı 4 · 0 · 0) |
| Mutasyon | **12/12 yakalandı** (ilk turda 2 kaçtı → testler güçlendirildi) |
| Yeni test | **31** (2 sınıf) |
| Yayın | API **v171** · Web **v195** · Masaüstü **1.0.154** · üç yönlü sağlama aynı |
| Üretime yazma | **YOK** (SQL/migration/DDL/secret/ACL/test verisi) · şema **72 → 72** |

### Kararınızı bekleyen (değişmedi)
- **ARC-01** — araç seçicisinin firma geneli olması.
- **YET-01** — işlevsiz iki yetki anahtarının yetki ağacından kaldırılması.

---

## 📦 ARŞİV — DÖRDÜNCÜ TUR (2026-08-26)

Tam rapor: [`docs/tests/Denetim_2026-08-26_Dorduncu_Tur.md`](../tests/Denetim_2026-08-26_Dorduncu_Tur.md)
Kararlar: **ADR-150 … ADR-154**

### Bulunan ve düzeltilen gerçek hatalar
| ID | Önem | Kısaca |
|---|---|---|
| **RPR-15** | **P1** | "Rol Yetki Kontrol" ile role **kapatılan ekranın verisi raporlardan (ve Excel çıktısından) okunabiliyordu** → yetki açığı. |
| **SB-01** | **P1** | Şube ağacı **ikinci kapsam otoritesinde** (`ScopeResolver`) uygulanmıyordu → üst şubeye yetkili kullanıcı alt şantiyenin personelini göremiyor, oraya personel ekleyemiyordu. |
| **MAS-02** | P3 | Masaüstünde sayfa değişince **zamanlayıcı birikiyordu** → dakikada N ağ isteği + bellek büyümesi. |
| **BAG-01** | P3 | Web'de sunucuya ulaşılamadığında **boş ekran** çıkıyor, sebep söylenmiyordu. |

> ⭐ **SB-01 nasıl bulundu:** önceki üç rapor "üretimde 0 şube var" diyordu. Bu tur o **varsayım**
> yeniden ölçüldü ve üretimde **9 şube** (5'i bir üst şubenin altında şantiye) olduğu görüldü.
> Varsayım tazelenmeseydi bu hata bulunamayacaktı.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Taban (tur başı, yeniden ölçüldü) | 2386 · 0 · 37 — **önceki raporla birebir** |
| Tam test — final koşu 1 | **2451 · 0 başarısız · 37 atlandı** |
| Tam test — final koşu 2 (bağımsız) | **2451 · 0 · 37 → birebir aynı** |
| PostgreSQL (izole küme) | **47 · 0 · 0 atlanan** (+ yedek lehçe kapısı 4 · 0 · 0) |
| Mutasyon testi | **11/11 yakalandı** (N1–N11) |
| Yayın | API **v170** · Web **v194** · Masaüstü **1.0.153** · üç yönlü sağlama aynı |
| Üretime yazma | **YOK** (SQL/migration/DDL/secret/ACL/test verisi) · şema **72 → 72** |

### Kararınızı bekleyen (ürün kararı — bilerek dokunulmadı)
- **ARC-01** — araç seçicisinin firma geneli olması (kanıt iki yöne de çekiyor, 12+ çağrı noktası).
- **YET-01** — işlevsiz iki yetki anahtarının yetki ağacından kaldırılması (teknik risk yok, ürün kararı).

---

## 📦 ARŞİV — ÜÇÜNCÜ TUR (2026-08-26)

Tam rapor: [`docs/tests/Son_Stabilizasyon_2026-08-26.md`](../tests/Son_Stabilizasyon_2026-08-26.md)
Kararlar: **ADR-143 … ADR-149**

### Bulunan ve düzeltilen gerçek hatalar
| ID | Önem | Kısaca |
|---|---|---|
| **YED-02** | **P1** | `POST /api/backups` cihaz jetonunu **hiç doğrulamıyordu** ve firmayı formdan alıyordu → internetteki herhangi biri 1 GB'a kadar dosya yükleyip diski doldurabilirdi (disk dolunca TÜM API 500). |
| **SNK-01** | P2 | Senkron yolu araç **sayacını geriye alabiliyordu** (1000 → 10, sessizce) — yanlış yakıt raporu + kaçırılan bakım uyarısı. |
| **YOL-01** | P2 | Firma/makine adı doğrulanmadan **dosya yoluna** giriyordu; 4 yer, ikisi **özyinelemeli silme** → `".."` ile bütün firmaların dosyaları silinebilirdi. |
| **RPR-14** | P2 | 6 ön muhasebe raporu, rapor ekranındaki **firma seçimini yok sayıyordu** → süper admin B'yi seçse de A'nın mali verisi geliyordu (sessiz yanlış veri). |
| **PRS-01** | P2 | Personel listesinde şube kapsamı **sayfalamadan sonra** uygulanıyordu → tek şubeye yetkili kullanıcı kendi personelini hiç göremeyebilirdi. |
| **YET-05** | P3 | "İptal / Ters Kayıt" arayüz kapısı sunucudan farklıydı → verilen yetki kullanılamıyor, olmayan yetkide buton görünüyordu. |
| **MAS-01** | P3 | Masaüstünde her çıkış→giriş bir kabuk biriktiriyordu (duran zamanlayıcı yok, statik olay aboneliği çözülmüyor). |

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Taban (tur başı, yeniden ölçüldü) | 2323 geçti · 0 başarısız · 37 atlandı — **önceki raporla birebir** |
| Tam test — final koşu 1 | **2386 geçti · 0 başarısız · 37 atlandı** |
| Tam test — final koşu 2 (bağımsız) | **2386 geçti · 0 başarısız · 37 atlandı → birebir aynı** |
| PostgreSQL koşusu | **47 geçti · 0 başarısız (atlanan 37'nin tamamı çalıştı)** |
| Mutasyon (kasten bozma) turu | **10 mutasyon · 10'u da yakalandı** (1'i test düzeltmesinden sonra) |
| Performans (50.000 satır) | rapor **329 ms / 6,55 MB** · Excel **4,3 sn** |
| Performans (100.000 satır) | rapor **641 ms**, üst sınır (50.000) **doğru uygulandı** |
| Gerçek tarayıcı turu | izole, **iki şubeli** · ~160 istek · **0 ürün hatası** |
| İzole masaüstü turu | ayrı veritabanı · **72/72 migration sıfırdan** · üretime **bağlanmadı** |
| Release derlemesi (API·Web·Desktop) | **0 hata** |
| Yeni migration | **YOK** → şema **72**'de kaldı |

### Yayın
| Bileşen | Sürüm |
|---|---|
| API | **v168 → **v169**** |
| Web | **v192 → **v193**** |
| Masaüstü | **1.0.152** (checksum `8664E6BB…3308`) |
| Şema | **72** (değişmedi) |

### Sıradaki tek iş
Kullanıcı kararı bekleyenler (bkz. `KNOWN_ISSUES.md`): **makine aktivasyon modeli** · **işlevsiz iki yetki
anahtarı** · **rapor yetkisinin modül yetkisi istememesi (RPR-15)** · PostgreSQL dosya yedeği · rapor
sayfalı API'si · Satın Alma alanı.

---
## ✅ TAMAMLANAN — FİNAL AUDIT + REPAIR + VERIFICATION TURU (2026-08-26, ikinci tur)

Tam rapor: [`docs/tests/Final_Audit_2026-08-26.md`](../tests/Final_Audit_2026-08-26.md)
Kararlar: **ADR-138 … ADR-142**

### Bulunan ve düzeltilen gerçek hatalar
| ID | Önem | Kısaca |
|---|---|---|
| **TNT-05** | P2 | Rapor ucu BAŞKA FİRMANIN şube kimliğini kabul ediyordu (403 yerine 200). Veri sızmıyordu, kapı fail-open'dı. |
| **SIF-03** | P2 | Firma sıfırlamada makinelere "yerelini temizle" bildirimi **boş catch ile yutuluyordu** → silinen veri geri gelebilirdi. |
| **MAK-01** | P2 | Anonim makine kaydı firmanın **kotasını tüketebiliyor**; iki büyük indirme ucu + enrollment **hız sınırsızdı**. |
| **YET-02** | P2 | Ters kayıt/iptal yetkisi üç işlemin kapısıydı ama **yetki ağacında yoktu** → yönetici kimseye veremiyordu. |
| **RL-01** | P3 | Hız sınırlayıcının durum sözlüğü **sınırsız büyüyordu** (bellek). |

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Taban (tur başı) | 2282 geçti · 0 başarısız · 37 atlandı |
| Tam test — son koşu 1 | **2323 geçti · 0 başarısız · 37 atlandı** |
| Tam test — son koşu 2 (bağımsız) | **2323 geçti · 0 başarısız · 37 atlandı → birebir aynı, flaky yok** |
| PostgreSQL koşusu | **49 geçti · 0 başarısız** |
| Release derlemesi (API·Web·Desktop) | **0 hata** |
| Yeni migration | **YOK** → şema **72**'de kaldı |
| Gerçek tarayıcı turu | izole, **iki şubeli** kurulum · 60 istek · **0 hata** |
| İzole masaüstü turu | ayrı veritabanı + yerel sunucu · **72/72 migration** · üretime **bağlanmadı** |
| Şube izolasyonu | **17 senaryo** izole matriste (üretimde 0 şube → canlıda gözlemlenemez) |

### Yayın
| Bileşen | Sürüm |
|---|---|
| API | **v168** |
| Web | **v192** |
| Masaüstü | **1.0.151** (checksum `431C0650…`) |
| Şema | **72** (değişmedi) |

### Sıradaki tek iş
Kullanıcı kararı bekleyenler (bkz. `KNOWN_ISSUES.md`): **makine aktivasyon modeli** ·
**PostgreSQL dosya yedeği** · işlevsiz iki yetki anahtarı · rapor sayfalı API'si · Satın Alma alanı.

---
## ✅ TAMAMLANAN — FİNAL STABİLİZASYON TURU (2026-08-26)

Tam rapor: [`docs/tests/Final_Stabilizasyon_2026-08-26.md`](../tests/Final_Stabilizasyon_2026-08-26.md)
Kararlar: **ADR-130 … ADR-137**

### Bulunan ve düzeltilen gerçek sorunlar
| ID | Önem | Kısaca |
|---|---|---|
| **UPD-01** | **P1** | Boş checksum güncelleme doğrulamasını **tamamen atlıyordu** → "inen ne ise onu çalıştır". Fail-closed kapı. |
| **YED-01** | P2 | Sunucu yedeği PostgreSQL'de ham hata veriyordu; **geri yükleme yıkıcıydı**. İkisi de dosyaya dokunmadan, anlaşılır mesajla duruyor. |
| **PRF-01** | P2 | 20.000 satırlık rapor tarayıcıda **36.959 ms / 260.729 DOM** → **378 ms / 13.746**. |
| **RPR-13** | P2 | Tarih önceki rapordan taşınıyor, yeni raporu **sessizce daraltıyordu** (Muayene/Sigorta + iki PARA raporu). |
| **RPR-12** | P2 | Rapor listesi çalıştırılamayan raporları gösteriyordu; Personel raporu kişisel veriyi `reports` iznine açardı. |
| **RPR-09** | P2 | Operasyon ekranında elle `branchIds` çalışma şubesinin yerine geçiyordu (sızıntı yok, güvence yetkiye bağlıydı). |
| **RPR-10/11** | — | Eksik iki rapor tamamlandı: **Muayene/Sigorta** + **Personel Listesi**. Katalog 19 → **21**. |

> **RPR-08 denendi ve GERİ ALINDI** (ADR-131): Stok Durumu / Stok Sayım'ın çalışma şubesini yok sayması
> bir eksik değil, bilinçli karardır — o raporların filtre boyutu **şube değil, stoğun fiziksel yeridir**.
> Mevcut bir test kırıldı, gerekçe incelendi, değişiklik geri alındı ve karar **iki yönden** kilitlendi.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Taban (tur başı) | 2221 geçti · 0 başarısız · 35 atlandı |
| Tam test — son koşu 1 | **2282 geçti · 0 başarısız · 37 atlandı** |
| Tam test — son koşu 2 (bağımsız) | **2282 geçti · 0 başarısız · 37 atlandı → birebir aynı, flaky yok** |
| PostgreSQL koşusu (ayrı test DB) | **49 geçti · 0 başarısız · 0 atlandı** |
| Release derlemesi | **0 hata** |
| Yeni migration | **YOK** → üretim şeması **72**'de kaldı |
| Gerçek arayüz turu | izole yerel ortam (sıfır DB) · sunucu logunda **0 hata** |
| Yeni/güncellenen test | **+61** senaryo (4 yeni dosya + 3 dosyaya ek) |

### Yayın
| Bileşen | Sürüm |
|---|---|
| API | **v167** |
| Web | **v191** |
| Masaüstü | **1.0.150** (checksum `79DB5051…`) |
| Şema | **72** (değişmedi) |

### Sıradaki tek iş
Kullanıcı kararı bekleyenler (bkz. `KNOWN_ISSUES.md`): **PostgreSQL dosya yedeği (pg_dump)** ·
**Satın Alma domaini** · rapor sayfalı API'si.

## 🔵 MEVCUT FAZ: **FAZ C — Depo bazlı stok altyapısı**

**KARAR-7 = A** (malzeme kartı firma geneli, stok depo bazlı) — 2026-08-11 kesinleşti.
Tasarım: [`FAZ_C_DEPO_BAZLI_STOK_TASARIM.md`](FAZ_C_DEPO_BAZLI_STOK_TASARIM.md)

## ✅ TAMAMLANAN — `STK-01` + `STK-02` (tek iş birimi, 2026-08-11)

**Stok bakiyesi artık depo bazlı.** `stock_balances` anahtarı `(company_id, material_id, location_id)`
oldu; `location_id=''` = **ATANMAMIŞ**. Transfer bundan böyle bakiyede **görünür** (eskiden tek havuzda
toplandığı için görünmüyordu).

Migration ve kod **aynı iş biriminde** verildi — bilinçli: yalnız migration açılsaydı stok değerleri
**sessizce yanlış** görünürdü (en tehlikeli hata türü).

### Dönüştürülen 16 üretim noktası
| Tür | Adet | Ne yapıldı |
|---|---|---|
| CAS yazma | 4 | `ON CONFLICT(company_id, material_id, location_id)` · `locationId` **zorunlu parametre** (varsayılan yok — çağıran bilinçli seçsin) |
| Skaler + toplu okuma | 4 | `StockBalanceWriter.ReadTotal` / C#'ta `decimal` toplama (SQL `SUM` **yok** — SQLite'ta float hatası verir) |
| JOIN | 8 | `SqlDialect.StockTotalSubquery` → malzeme başına **tek satır**. `DISTINCT` ile gizleme **yok** |

**Yanında bulunan gerçek hata:** sayım, sistem miktarını **firma genelinden** okuyup düzeltmeyi
**şubeye** yazıyordu → "genelden oku, lokasyona yaz" tutarsızlığı. Düzeltildi ve test edildi.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **1223 / 1190 geçti / 0 kaldı / 33 atlandı** (taban 1206'ydı; **17 yeni senaryo**) |
| Çözüm derlemesi | **0 hata** |
| İzole PG provası (üretim yedeğinin kopyası) | 667 hareket · 664 → **665** bakiye satırı · **uyuşmayan 0** · toplam korundu (8952,3) · süre 173 ms |
| Dolu SQLite v63 → v64 yükseltmesi | 3 bakiye → 5 satır · toplam korundu · ondalıklar tam (0.1 / 0.2) |
| Doğrulama kapısı (kasten bozuk bakiye) | Migration **durdu**, şema 63'te kaldı, **hiçbir şey yazılmadı** |
| Dönüştürülen sorgular PostgreSQL'de | Liste **2459 satır = malzeme sayısı** → satır çoğaltma **yok**; detay/kırılım/servis toplamı **tutarlı** |

Yeni testler: [`tests/DepoWise.Tests/StockLocationTests.cs`](../../tests/DepoWise.Tests/StockLocationTests.cs)
QA raporu: [`docs/tests/Stok_Lokasyon_Test_Report.md`](../tests/Stok_Lokasyon_Test_Report.md)

> 🔒 **Masaüstü çevrimdışı mimarisine dokunulmadı.** Senkron **0 değişiklik** gerektirdi:
> `DbIntrospect.PrimaryKey` bileşik anahtarı okuyor, `BusinessSyncService` `ON CONFLICT` hedefini
> ondan kuruyor → üç kolonlu anahtar **otomatik** üretiliyor. `stock_movements` şeması değişmedi.

## ✅ TAMAMLANAN — `STK-03` API lokasyon boyutu (2026-08-11)

Envanter + sözleşme: [`STK_03_API_LOKASYON_PLANI.md`](STK_03_API_LOKASYON_PLANI.md)

**🔴 Asıl bulgu: lokasyon sahiplik doğrulaması YOKTU.** Stok yazma yolları gönderilen `branchId`'nin
firmaya ait olduğunu kontrol etmiyordu. STK-02'den sonra lokasyon bakiyenin **birincil anahtar** kolonu →
yabancı kimlik yazılsaydı o satır **hiçbir firmanın ekranında düzeltilemezdi**. Kapatıldı
(`EnsureLocationOwned`, 4 yazma yolunun tek geçiş noktasında + açılış stoğunda).

| Karar | Ne yapıldı | Neden |
|---|---|---|
| Doğrulama **serviste**, API'de değil | `StockService` + `OpeningStockService` | Masaüstü servisi **çevrimdışı** çağırıyor; API'de olsaydı o yol korumasız kalırdı |
| 3 anlam = 3 ayrı uç | `/balance/{id}` **değişmedi** · **yeni** `/locations` · **yeni** `/location` | Aynı ucu üç anlamda kullanmak sözleşmeyi belirsizleştirirdi; eski Web aynen çalışır |
| Hata kodu | Yabancı/bilinmeyen lokasyon → **403** | Mevcut standart (`EnsureBranchOwned`); yeni model icat edilmedi |
| Lokasyon **zorunlu değil** | Yoksa ATANMAMIŞ | Zorunluluk bir **UI kararı** → STK-04'e devredildi (bugün "Tüm Şubeler" `null` gönderiyor) |

**İstemci envanteri:** Web 5 sayfada stok uçlarını kullanıyor (`JsonElement`+`TryGetProperty` → eklenen
alanlar bozmaz) · **Masaüstü hiçbir stok ucunu kullanmıyor** (yerel servis + `business-push/pull`)
→ uç değişikliği masaüstünü **yapısal olarak** bozamaz.

**Kanıt:** **1240 / 1207 geçti / 0 kaldı / 33 atlandı** (taban 1223; **17 yeni senaryo** — 15 gerçek HTTP +
2 çevrimdışı) · build 0 hata · **sync kodu değiştirilmedi** · N+1 yok (lokasyon adları aynı sorguda JOIN).

## ✅ TAMAMLANAN — `STK-04` Web lokasyon desteği (2026-08-11)

Plan: [`STK_04_WEB_LOKASYON_PLANI.md`](STK_04_WEB_LOKASYON_PLANI.md)

**🔴 Üç gerçek hata bulundu ve düzeltildi (hepsi Web tarafında):**
1. **Sayım `branchId` göndermiyordu** → fark ATANMAMIŞ'a yazılıyor, kullanıcının saydığı depo hiç düzelmiyordu.
2. **Sayım ekranı firma geneli toplamı** "sistem stoğu" diye gösteriyordu → kullanıcı yanlış farkı görürdü.
3. **Açılış stoğu deposuz** gönderiliyordu → her açılış ATANMAMIŞ'a düşüyordu (canlıdaki 663 kaydın sebebi).

| Ekran | Yapılan |
|---|---|
| `Stock` | "Tüm Şubeler"de **zorunlu depo seçici** (eskiden hiç işlem yapılamıyordu) · bakiye çipi **seçili deponun** stoğu |
| `StockCount` | Sayılan depo **ekranda açık** · sistem stoğu **o deponun** (`/count-sheet`) · POST'a `branchId` |
| `StockMovements` | **Depo kolonu** (`Kaynak → Hedef`) · lokasyon **filtresi** (Tüm Şubeler / depo / Atanmamış) |
| `Materials` | Kartta **Toplam + depo kırılımı** (tek istek) · **açılış deposu** zorunlu |
| `Daily` · `Dashboard` · diğerleri | Bilinçli olarak **değiştirilmedi** / doğrulandı |

**🌐 Tüm Şubeler ≠ 📦 Atanmamış** ayrımı arayüzde, filtrelerde ve testlerde kilitlendi.
Yeni `LocationOptions` servisi lokasyon listesini **oturumda bir kez** indirir (N+1 yok);
kırılım yalnız **malzeme kartı** açılınca çekilir (liste satırlarında değil).

**Kanıt:** **1254 / 1221 geçti / 0 kaldı / 33 atlandı** (taban 1240; **14 yeni senaryo**) · build 0 hata ·
gerçek üretim kopyasında doğrulandı.

## ✅ TAMAMLANAN — `STK-05` Masaüstü + çevrimdışı lokasyon (2026-08-11)

Plan: [`STK_05_DESKTOP_OFFLINE_PLANI.md`](STK_05_DESKTOP_OFFLINE_PLANI.md)

**Yapısal bulgu:** Masaüstünde stok için AYRI veri katmanı YOK — doğrudan ortak `StockService`'i çağırıyor.
Bu yüzden STK-01…03'ün tamamı masaüstünde zaten yürürlükteydi; STK-05 **arayüz + eksik parametre** işiydi.

**🔴 Dört gerçek hata (Web'dekilerin masaüstü ikizleri) düzeltildi:**
1. Sayım `branchId` göndermiyordu → fark ATANMAMIŞ'a yazılıyordu.
2. Sayımda sistem miktarı **firma geneli** okunuyordu → kullanıcı yanlış farkı görürdü.
3. Açılış stoğu **deposuz** yazılıyordu → ATANMAMIŞ'a düşüyordu.
4. Giriş/çıkış bakiye çipi **firma geneli** gösteriyordu → "15 var" deyip çıkış reddedilirdi.

**Eklenenler:** Sayım ekranında **"Sayılan Depo"** · malzeme kartında **DEPO KIRILIMI** (tek sorgu) ·
hareket listesinde **DEPO / ŞANTİYE** kolonu (`Kaynak → Hedef`, Web ile **aynı metin**).

**🔒 Çevrimdışı mimariye DOKUNULMADI:** stok yazma yerel SQLite transaction'ı, API çağrısı yok ·
lokasyon listesi yerel veriden · `EnsureLocationOwned` serviste olduğu için çevrimdışı da koruyor ·
**senkron kodu değiştirilmedi**.

**Kanıt:** **1267 / 1234 geçti / 0 kaldı / 33 atlandı** (taban 1254; **13 yeni senaryo**) · build 0 hata ·
çevrimdışı→senkron lokasyonu **koruyor** · online→offline→online döngüsünde **kopya hareket yok** ·
şirket izolasyonu çevrimdışı da geçerli · dolu SQLite v63→v64 ve rollback kapısı **yeniden doğrulandı**.

## ✅ TAMAMLANAN — `STK-06` Rapor lokasyon boyutu (2026-08-11)

Plan + sonuçlar: [`STK_06_UYGULAMA_PLANI.md`](STK_06_UYGULAMA_PLANI.md)

**11 rapor tarandı; yalnız 2'si lokasyon gerektiriyordu ve ikisi de tamamlandı:**

| Rapor | Yapılan |
|---|---|
| **Stok Durumu** | Filtre boşken **eski davranış birebir** (regresyon yok) · depo seçilince **kırılım + "Depo / Şantiye" kolonu + decimal toplam satırı** · 📦 Atanmamış ayrı seçenek |
| **Stok Sayım** | 🔴 **"Sayılan Depo" kolonu** eklendi — hangi deponun sayıldığı raporda HİÇ görünmüyordu · lokasyon filtresi |

**Dashboard'da değişiklik gerekmedi** (STK-02 alt sorgusunun kopyası yok; üretim verisiyle doğrulandı).
**Export ayrı sorgu kullanmıyor** → filtreyi otomatik aldı. Diğer 9 rapora dokunulmadı.

`ReportFilters.Location` **ayrı bayrak** oldu — mevcut `Branch` filtresi "kaydı işleyen şube" demek,
stok lokasyonu değil; ikisi birleştirilmedi (testle kilitli).

**Kanıt:** **1281 / 1248 geçti / 0 kaldı / 33 atlandı** (taban 1267; **14 yeni senaryo**) · build 0 hata ·
izole PG üretim kopyasında doğrulandı · çevrimdışı ↔ sunucu rapor paritesi test edildi.

## ✅ TAMAMLANAN — `STK-07` Senkron sertifikasyonu (2026-08-11)

Kayıt: [`STK_07_SENKRON_SERTIFIKASYONU.md`](STK_07_SENKRON_SERTIFIKASYONU.md)

**11 senaryo GERÇEK HTTP senkron uçlarıyla koşturuldu** (masaüstü ayrı yerel SQLite ile temsil edildi;
stok işlemleri API'ye uğramadan çevrimdışı yazıldı, yalnız "bağlantı gelince" push edildi).

| Kanıtlanan | Sonuç |
|---|---|
| Çevrimdışı giriş/çıkış/transfer/sayım | Lokasyon senkronda **kaybolmuyor** |
| Transfer | **İki bacak da** taşınıyor; kaynak/hedef alanları birebir |
| Idempotency | Aynı paket **3 kez** gönderildi → kopya hareket ve bakiye değişimi **yok** |
| offline→online→offline→online | Yerel ve sunucu hareket sayısı **eşit** |
| Yakınsama | **Hareket kimlikleri dahil** iki taraf aynı |
| **Bakiyenin otoritesi** | Yerel bakiye kasten **999** yapıldı → senkron sonrası **10** = **defter kazandı** |
| **Delta pull** | Güncel sürümden sonrası **boş paket**; eski kayıt tekrar inmiyor; sürüm ilerliyor |
| Şirket izolasyonu | Yabancı depoya yazma **çevrimdışı da** reddediliyor |
| Bakiye tablosu | (malzeme, lokasyon) başına tek satır; **hayalet satır yok** |

🔒 **Senkron kodu DEĞİŞTİRİLMEDİ · offline mimariye dokunulmadı.**
**Kanıt:** 1281 → **1292 / 1259 geçti / 0 kaldı / 33 atlandı** · build 0 hata.

**Yeni bulgu `SNK-12`:** `branches` iş-senkronunda **yok** (web-otoriteli, ayrı org uçlarından geliyor).
Depo bazlı stokta sonucu: web'de açılan yeni depo, masaüstüne org senkronu inmeden stok işleminde
kullanılamıyor. **Hata değil** (çevrimdışı bilinemeyen depo uydurulmamalı) ama görünürlük işi.

## ✅ TAMAMLANAN — `SNK-12` Masaüstünde depo listesi tazeleme (2026-08-11)

**Sorun:** Şubeler iş-senkronunda taşınmaz (web-otoriteli, ayrı yoldan iner). Aynalama YALNIZ girişte
çalıştığı için, oturum açıkken web'de açılan yeni depo masaüstüne inmiyordu → o depoya stok işlemi
yapılamıyordu.

**Çözüm (en küçük):** Mevcut `BranchMirror` mekanizması **normal senkron turunda da** çağrılıyor,
**2 dakikalık kısıtlama** ile (şube listesi küçük ve nadir değişir; 15 sn'lik kadansta her tur indirmek
israf olurdu). **Yeni protokol / tablo / uç YOK · `stock_movements` senkronuna DOKUNULMADI.**
Saf aynalama mantığı `BranchMirrorApply`'a (Infrastructure) taşındı — test edilebilir olsun diye.

**Kanıt:** **1300 / 1267 geçti / 0 kaldı / 33 atlandı** (taban 1292; **8 yeni senaryo**) · build 0 hata.
Yeni depo aynalandıktan sonra **çevrimdışı** giriş/transfer/sayım çalışıyor · kopya yok · isim güncellemesi
yansıyor · silinen depo **pasife alınıyor, fiziksel silinmiyor** (geçmiş stok korunuyor) · **firma
izolasyonu** korunuyor · çevrimdışıyken yerel liste dokunulmadan kalıyor.

## ✅ TAMAMLANAN — `STK-08` Atanmamış stok toplu dağıtımı (2026-08-11)

Plan + sonuçlar: [`STK_08_UYGULAMA_PLANI.md`](STK_08_UYGULAMA_PLANI.md)

**FAZ C'nin son parçası tamamlandı.** Kullanıcı artık geçmişten kalan "Atanmamış" stoğu **kendi seçerek**
depolara dağıtabiliyor — sistem hiçbir tahmin yapmıyor (KARAR-8).

| Katman | Yapılan |
|---|---|
| Servis | `DistributeUnassigned` — **dar giriş noktası**; kaynak DAİMA ATANMAMIŞ, hareket türü `transfer` kalır |
| API | `GET /api/stock/unassigned` · `POST /api/stock/distribute` (kaynak alanı **yok**) |
| Web | `/stock/distribute` — liste + hedef + miktar + kalan + "Tümü" + onay |
| Masaüstü | Aynı ekran, **API'siz** (yerel SQLite) → **çevrimdışı çalışır** |

**🔴 İki gerçek bulgu:**
1. **Transferler geri alınmaz** (2026-08-06 kararı) — dağıtım da transfer olduğu için geri alınamaz.
   Planın "ters kayıtla geri alınır" varsayımı yanlıştı. İstisna **açılmadı**; düzeltme yolu yanlış
   depodan doğru depoya **yeni transfer**. İlk yazdığım ekran metinleri yanıltıcıydı → **düzeltildi**.
2. **Üretimde `DEPOWISE` firmasının hiç deposu yok** (0 şube). 8951,3 birim atanmamış stok var ama
   dağıtacak hedef yok → kullanıcı önce **Şubeler** ekranından depo oluşturmalı. İki arayüz de söylüyor.

**Kanıt:** **1317 / 1284 geçti / 0 kaldı / 33 atlandı** (taban 1300; **17 yeni senaryo**) · build 0 hata ·
izole üretim kopyasında doğrulandı: aşım **reddedildi**, **rollback** çalıştı, **firma toplamı korundu**.

## ✅ SON TAMAMLANAN — `SNK-11` Bakiye senkron yükünden arındırıldı (2026-08-11)

Kayıt: [`SNK_11_BAKIYE_SENKRON_YUKU.md`](SNK_11_BAKIYE_SENKRON_YUKU.md)

**Değişiklik TEK dosyada, iki satır:** `BusinessSyncService.Tables` listesinden `stock_balances`
çıkarıldı + gereksiz yetki eşlemesi kaldırıldı. **Tablo KALDIRILMADI** — yerel SQLite'ta ve sunucuda
aynen duruyor; masaüstü çevrimdışı stok işlemleri ve bakiye görüntüleme etkilenmedi.

**Neden güvenliydi:** taşınan bakiye zaten KULLANILMIYORDU — sunucu push sonrası defterden yeniden
hesaplıyor, masaüstü pull'u zaten hariç tutuyordu. STK-07 bunu kanıtlamıştı.

**Ölçülen fayda (üretim kopyası):** her senkron turunda **663 bakiye satırı / ~86 KB** artık taşınmıyor.
Paket 1807,1 KB · defter (663 hareket) yerinde.

**Kanıt:** **1325 / 1292 geçti / 0 kaldı / 33 atlandı** (taban 1317; +7 yeni senaryo) · build 0 hata ·
kasten bozuk bakiye sunucuya bulaşmıyor · çevrimdışı akışların TAMAMI çalışıyor · kopya yok.

⚠️ **3 mevcut test gerekçeli olarak yeniden yazıldı** (gevşetme değil — kilitledikleri davranış bilinçli
olarak kaldırıldı). Ayrıntı kayıtta §4.

## ✅ SON TAMAMLANAN — `RPR-01` Rapor filtre paritesi (2026-08-11)

Kayıt: [`RPR_01_FILTRE_PARITESI.md`](RPR_01_FILTRE_PARITESI.md)

**Üretim davranışı DEĞİŞMEDİ** — bu bir *koruma* işidir. Rapor verisi zaten ortak (`ReportService`),
ama filtre ARAYÜZLERİ iki tarafta elle yazılıyor: yeni filtre eklenirken biri unutulursa hiçbir şey
patlamaz, filtre o platformda **sessizce yok** olur.

**Çözüm (en küçük):** Tek doğru kaynak `ReportFilters` enum'u. Test, enum'un HER değeri için bir
kablolama satırı ister ve o satırı **4 katmanda** (Application · API · Web · Masaüstü) doğrular.
Ortak UI katmanı **kurulmadı**, üretim kodu **değişmedi** — test projesi Web/Desktop'a referans
vermediği için iki arayüzün **kaynak metni** okunur.

| Yakalanan hata türü | Durum |
|---|---|
| Filtre yalnız Web'e / yalnız masaüstüne eklenmiş | ✅ |
| Ekranda var ama **EXPORT gövdesinde** gönderilmiyor | ✅ |
| `[NotifyPropertyChangedFor]` unutulmuş (filtre yanlış raporda takılı kalır) | ✅ |
| API katalog yanıtından alan düşmüş (Web filtreyi hiç göremez) | ✅ |
| Kataloğa yeni bayrak eklenip parite tablosuna girmemiş | ✅ |

**🔴 Negatif ispat kendi testimdeki gerçek zayıflığı buldu:** ilk Web kontrolü `_sel?.UsesLocation == true`
arıyordu; bu metin istek gövdelerinde de geçtiği için **ekran bloğu silinse bile test geçiyordu**.
Token `@if (_sel?.UsesLocation ==` olarak sıkılaştırıldı. Negatif ispat olmasaydı RPR-01
**çalışmayan bir koruma** olarak "tamamlandı" sayılacaktı.

**Envanter:** 12 rapor · 10 filtre bayrağı · bir filtrenin tam bağlanması için **6 dosyada** iş var.
Mevcut 10 bayrağın **tamamı** her iki platformda tam bağlı çıktı (gerçek parite eksiği bulunmadı).

**Kanıt:** **1343 / 1310 geçti / 0 kaldı / 33 atlandı** (taban 1325; **+18 senaryo**) · build 0 hata ·
5 simüle hatanın 5'i yakalandı · çevrimdışı rapor filtreleri HTTP'siz doğrulandı.
⚠️ **Görsel (browser/XAML render) kontrolü YAPILMADI** — doğrulama kod/kaynak düzeyindedir.

## ✅ SON TAMAMLANAN — `BKM-04` Bakım malzemesinin çıktığı depo (KARAR-9, 2026-08-11)

Karar: [`DECISIONS.md` → ADR-103](../DECISIONS.md) · Analiz: [`BKM_04_LOKASYON_ANALIZI.md`](BKM_04_LOKASYON_ANALIZI.md)
Kabul kriterleri: [`TASK_BACKLOG.md` → BKM-04](TASK_BACKLOG.md)

**Sorun:** `MaintenanceService` stok yazarken lokasyonu **sabit boş** yazıyor → her bakım tüketimi
ATANMAMIŞ'a düşüyor. Üretim kodunda lokasyonu dışarıdan almayan **tek** stok yazarı.

**Analizin belirleyici bulguları:**
- `op_branch_id` bağımsız alan **değil** — `OperatingBranchId`'nin kopyası → A ≡ B.
- **API oturumu `OperatingBranchId`'yi hiç set etmiyor** → Web'de o alan **her zaman NULL**
  (`WEB-02`'nin bakım karşılığı). Oradan lokasyon türetmek Web'de hiçbir şeyi düzeltmezdi.
- Buna karşılık **iki bakım ekranı da "Tüm Şubeler"de kaydetmeyi zaten engelliyor** → kaydet anında
  somut şube her platformda garanti (masaüstünde oturumda, Web'de `Auth.BranchId`).

**KARAR-9:** oturum şubesi **varsayılan**; kullanıcı **"Malzemenin çekildiği depo"** alanından kendi
firmasına ait aktif başka bir depo seçebilir. Atanmamış hedef olarak **sunulmaz**. Depo yoksa bakım
engellenmez (ATANMAMIŞ'a düşer, 2026-08-06 korunur). `vehicles.branch_id` **kullanılmaz**;
`op_branch_id` ile **karıştırılmaz**.

⚠️ **İki kırmızı çizgi:** (1) kullanıcının depo seçimi **sessizce ezilemez**;
(2) **iptal, orijinal hareketin lokasyonuna** geri yazar — oturum şubesinden yeniden hesaplanmaz.

**Migration GEREKMEZ** · yeni senkron protokolü/tablo YOK · SNK-11 geri alınmaz.

### Uygulandı — 8 üretim dosyası (1 yeni)

| Katman | Yapılan |
|---|---|
| Servis | `NewMaintenance.StockLocationId` (opsiyonel, sona) · `EnsureLocationOwned` · defter **ve** bakiye aynı depoyu kullanır |
| **İptal** | Artık **defterden** besleniyor (`LoadUsageMovements`) → ters kayıt ORİJİNAL hareketin deposuna yazar; ekip-stoğu satırları **yapısal** olarak dışarıda; `reverses_movement_id` ile izlenebilir |
| API | 3 uçta opsiyonel `branchId` (eski istemci kırılmaz) |
| Masaüstü | Bakım + Günlük Faaliyet ekranlarına depo seçici; kural tek yerde (`StockLocationPicker`) |
| Web | Aynı seçici; liste `WriteTargets()` (Atanmamış YOK); POST'ta kullanıcının seçimi |

**`DailyActivityService` ve `MaintenanceImportService` DEĞİŞMEDİ** — kayıt modelini olduğu gibi
geçirdikleri için yeni alan kendiliğinden aktı (testle doğrulandı).

**Yanında düzeltilen:** eksik-stok uyarısı iki arayüzde de **firma geneline** bakıyordu → artık
**seçilen deponun** stoğuna bakıyor (STK-04/05'te düzeltilen aynı hata sınıfının bakımdaki ikizi).

**Kanıt:** **1387 / 1353 geçti / 0 kaldı / 34 atlandı** (taban 1343; **+44 senaryo**) · build 0 hata ·
mevcut 115 bakım/faaliyet testi **dokunulmadan** geçti · izole PostgreSQL'de doğrulandı (ters kayıt
aynı depoda + `reverses_movement_id` dolu) · çevrimdışı→senkron lokasyonu koruyor, kopya yok.

⚠️ **Görsel (tarayıcı render) kontrolü YAPILMADI.** Yerel API veritabanında hesap bilgisi olmadığı,
canlıya yazmak yasak olduğu ve parola girmem mümkün olmadığı için kapatılamadı — ayrıntı kayıtta §9.

## 📋 `STK-10` — Hareket raporu · **PLAN HAZIR, KOD BAŞLAMADI** (2026-08-11)

Plan + envanter: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md)

Envanter çıkarıldı, lokasyon semantiği koddan **doğrulandı** (tahmin yok), tasarım kararları alındı.
**Kod bilinçli olarak başlatılmadı** — gerekçe planın §9'unda (talimat §19: tek oturumda tam
doğrulanamayacak iş kodlanmaz).

**🔴 Üç bulgu:**
1. **Web'de lokasyon filtresi sessizce eksik sonuç verebilir** — süzme `limit` ile kesilmiş liste
   üzerinde **istemcide** yapılıyor. Filtre sunucuya taşınınca kapanır (STK-10 içinde).
2. **`STK-B1` STK-10'un ön koşuluydu** → ✅ **2026-08-11'de tamamlandı** (aşağıdaki bölüm).
3. **Masaüstünde lokasyon filtresi hiç yok** (Web'de var) — parite eksiği, STK-10 içinde kapanır.

**✅ KARAR (kullanıcı, 2026-08-11): SEÇENEK B.** Arama kutusu kataloğa **gerçek `Search` filtresi**
olarak girer; ekranda kalıp export dışında bırakılmaz. Ekran ve XLSX aynı filtrelenmiş kümeyi üretir.
Malzeme filtresi Search'ün yerine geçmez — ikisi birlikte bulunur. (ADR-104)

**Kalan boyut (adım 1'den itibaren):** **3** filtre bayrağı × RPR-01'in **6 katmanı** =
**18 kablolama noktası** · + 2 ekranın rapora bağlanması · + B-1 davranış düzeltmesi · + ~30 senaryo ·
+ ilk kez **6 kombinasyonda gerçek XLSX satır-satır karşılaştırması** · + izole PG sorgu planı.

## ✅ SON TAMAMLANAN — `STK-B1` Hareket türü kataloğu (STK-10 adım 0, 2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §12

**8 hareket türünün 8'i artık iki platformda AYNI Türkçe adla görünüyor.** Önceden aynı hareket
**üç** ayrı yerde, **üç farklı** biçimde gösteriliyordu ve üçü de eksikti.

| `movement_type` | **Nihai etiket (Web = Masaüstü)** | Önceki |
|---|---|---|
| `opening` · `in` · `out` · `transfer` | Açılış · Giriş · Çıkış · Transfer | zaten aynıydı |
| `adjustment` | **Sayım Düzeltme** | 🔴 masaüstü "Düzeltme" ↔ diğerleri "Sayım Düzeltme" |
| `usage` | **Bakım Tüketimi** | 🔴 üçünde de **ham İngilizce** |
| `usage_reverse` | **Bakım Tüketimi İptali** | 🔴 üçünde de **ham İngilizce** |
| `reverse` | **İptal (Ters Kayıt)** | 🔴 masaüstü ham · web "İptal (ters)" · kart "İptal" |

**Çözüm:** `MovementTypeOptions` tek doğru kaynak. Web Application'a referans vermediği için proje
zaten kullandığı deseni izledik: **tek dosya, iki projede derlenir** (`ListColumns` gibi) → ayna yok,
ıraksama imkânsız. Ölü `count` dalı kaldırıldı (o bir `doc_type`).

**Kanıt:** **1411 / 1377 geçti / 0 kaldı / 34 atlandı** (taban 1387; **+24 senaryo**) · build 0 hata ·
8 türün 8'i **gerçek servislerle üretilip** ekranda ne göründüğü doğrulandı · kaynak taraması yeni bir
tür kataloğa girmezse testi **kırıyor** · migration/senkron/veri **dokunulmadı**.

⚠️ **Yan bulgu — kendi testimi düzelttim:** BKM-04'ün 4 iptal testi ters kaydı sıra indeksiyle
seçiyordu; dondurulmuş saatte `created_at` eşit olunca sıralama GUID'e düşüyor → **flaky**'ydiler
(5 koşuda ~1 kırılma, BKM-04'te şans eseri geçmişler). Tür üzerinden seçime çevrildi, 5/5 kararlı.
Üretim etkilenmedi.

⚠️ **Görsel kontrol YAPILMADI** — BKM-04'teki aynı engel (yerel API veritabanında hesap yok, canlıya
bağlanmak yasak, parola giremem). Ayrıntı ve statik risk değerlendirmesi planın §12'sinde.

## ⏸️ `STK-10` kalanı — adım 1'e GEÇİLMEDİ, iki karar netleşti (2026-08-11)

Adım 1 öncesi planın dayandığı varsayımlar koddan sınandı → **iki plan hatam çıktı** (§13) ve
kullanıcının sorduğu kesişim **karara bağlandı** (§14). Kod yazılmadı (gerekçe §15).

**🔴 D-1 — "Export limit uygulamaz" YANLIŞTI.** Sorgu ve export ucu **AYNI** `BuildReport`'u aynı
tavanla (`maxRows`, varsayılan **50.000**) çağırıyor. "Ekran = export" hedefi için bu doğru davranış;
ama kabul kriteri düzeltildi: export ekranla **aynı** tavana tabidir.

**🔴 D-2 — "Run'ın limiti SQL'e iner" YANLIŞTI.** `Run` kesmeyi **bellekte**, `Dispatch`'ten SONRA
yapıyor. Yani `StockMovements` sorgusu **kendi SQL LIMIT'ini** taşımalı ve **filtre → sırala → LIMIT**
sırası **SQL içinde** kurulmalı. (B-1 düzeltmesinin teknik karşılığı.)

**✅ D-3 — ClosedXML test projesinden erişilebilir.** RPR-01'in açık bıraktığı gerçek XLSX satır-satır
karşılaştırması STK-10'da yapılabilir.

**✅ KARAR — `BranchScope` × `Location` kesişimi:** *kapsam DIŞ SINIRDIR, lokasyon filtresi içeride
daraltır, asla genişletmez* (`WHERE kapsam AND lokasyon`). Sonuç: Depo A oturumu **Depo B filtresiyle
BOŞ** sonuç alır → yetki aşılmaz. §3'teki "A→B hem A hem B'de görünür" kuralı **kapsamı yeten**
kullanıcı için geçerlidir. Tam tablo planın §14'ünde; testle kilitlenecek.

## ✅ SON TAMAMLANAN — `STK-10a` Stok Hareketleri raporu (2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §16

Hareket defteri artık **kataloglanmış bir rapor**: `Date` + `Location` filtreli, **Kaynak → Hedef**
kolonlu ve **Excel'e aktarılabilir**. (`Search`/`Material`/`MovementType` **STK-10b'nindir**.)

**🔴 En önemli bulgu: Web ve masaüstünde HİÇ KOD DEĞİŞMEDİ.** Rapor katalog-güdümlü olduğu için iki
platformun Raporlar ekranında **kendiliğinden** göründü; `Date` ve `Location` filtreleri STK-06'dan
zaten 6 katmanda bağlıydı. **RPR-01 hiç değiştirilmeden yeşil kaldı** — "yeni bayrak eklemeyen artım"
seçiminin doğrudan getirisi.

**⚡ Sorgu planı (izole PG, gerçek çıktı):** `Limit → Sort → Index Scan` — tarih filtresi mevcut
`ix_stock_movements_material` indeksini kullanıyor, lokasyon filtresi SQL'e inmiş.
➡️ **Yeni indeks EKLENMEDİ** (plan kuralı: yalnız ölçüm gerekçelendirirse). Tavan artık **SQL'de**
(`LIMIT`) — `Run`'ın bellekte kesmesi ikinci emniyet ağı (D-2 düzeltmesi uygulandı).

**🔴 XLSX boşluğu KAPANDI:** RPR-01'de "yalnız aynı servis çağrılıyor" dolaylı ispatıyla bırakılmıştı.
Artık **6 filtre kombinasyonu × 2 hat** (servis + gerçek HTTP) için XLSX **açılıp hücre hücre**
rapor sonucuyla karşılaştırılıyor.

**Kanıt:** **1452 / 1417 geçti / 0 kaldı / 35 atlandı** (taban 1411; **+41 senaryo**) · build 0 hata ·
29 çevrimdışı + 11 gerçek HTTP + 1 izole PG senaryosu · firma izolasyonu · **BranchScope × Location**
sınırı testli (Depo A oturumu + Depo B filtresi → **BOŞ**).

⚠️ **2 mevcut test gerekçeli güncellendi** (gevşetme değil — katalog sayısı 12→13, lokasyonlu rapor
listesi 2→3; ikisi de TAM EŞLEŞME ile sınanmaya devam ediyor + yeni nöbetçi eklendi). Ayrıntı §16.1.
⚠️ **Görsel render kontrolü YAPILMADI** — aynı engel (§16.5).

## ⏸️ `STK-10b` — doğrulama yapıldı, KOD BAŞLAMADI (2026-08-11)

Kayıt: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §18

**Zorunlu 7 doğrulamanın tamamı yapıldı → plan ile kod arasında ENGELLEYİCİ FARK YOK.**
STK-10a yerinde · ADR-104 kayıtlı · RPR-01 `Map` 10 satır · `MovementTypeOptions` 8 tür ·
`BranchScope` `AND`'leniyor · rapor iki platformda katalogdan görünüyor · testler yeşil.

**🔴 Kendi iddiamı düzelttim:** §15'te "18 kablolama noktası atomik, dilimlenemez" demiştim —
**fazla katıymış**. RPR-01 her bayrağı **kendi içinde** denetliyor; bir bayrağı tam bağlayıp
diğerlerine dokunmamak testi **yeşil** bırakıyor.
➡️ Atomik birim **1 bayrak × 6 katman = 6 nokta**. STK-10b, her adımı yeşil biten **dört** artıma
bölünebilir: **10b-1** `MovementType` (seçenek kaynağı zaten var) → **10b-2** `Search` →
**10b-3** `Material` + autocomplete → **10b-4** iki ekranın bağlanması + **B-1 düzeltmesi**.

**Neden kodlanmadı:** bu oturumda RPR-01 + BKM-04 + STK-B1 + STK-10a tamamlandı; STK-10b'nin tamamı
(18 kablolama + 2 ekran + autocomplete + ~40 senaryo + 10 XLSX kombinasyonu + PG + çoklu tam-takım)
kalan kapasiteyle güvenilir biçimde bitirilemez. Yarıda kalsa sonuç "eksik ama yeşil" değil
**KIRMIZI** olurdu (bayrak eklenip 6 katmanı bitirilmezse RPR-01 kırılır).

## ✅ SON TAMAMLANAN — `STK-10b-1` Hareket Türü filtresi (2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §19

`MovementType` filtresi **6/6 katmanda** bağlandı; **RPR-01 gevşetilmeden yeşil** (14/14) ve artık
yeni bayrağı **kendi denetliyor**. Seçenekler yalnız `MovementTypeOptions`'tan (STK-B1) →
**`/api/reports/scope`'a yeni alan eklenmedi**, ikinci harita oluşmadı.

**🔴 Uygulama sırasında kendi hatamı yakaladım:** `MovementTypes`'ı önce `LocationIds`'ten **önce**
eklemiştim. Bu kayıt API'de **pozisyonel** de kuruluyor → `LocationIds` argümanı sessizce kayar,
lokasyon filtresi çalışmayı bırakırdı. Sona taşındı, alanın yanına kalıcı uyarı yorumu kondu.

**Semantik:** kanonik anahtarla sorgulanıyor (etiketle değil) · bilinmeyen anahtar **fail-closed**
(veri sızdırmıyor) · boş liste = filtre yok · **`BranchScope` genişlemiyor** (Depo A oturumu, Depo B'deki
`usage` hareketini türle isteyince bile göremiyor) · transfer **iki satır** kalıyor.

**⚡ Sorgu planı:** `movement_type` filtresi **SQL'e indi**; Limit/Sort yerinde; **yeni indeks yok**.
Testle ayrıca kanıtlandı: tavan **filtrelenmiş küme** üzerine iniyor (bellekte süzülmüyor).

**Kanıt:** **1480 / 1445 geçti / 0 kaldı / 35 atlandı** (taban 1452; **+28 senaryo**) · build 0 hata ·
23 çevrimdışı + 5 yeni HTTP + izole PG (sorgu planı dahil) · **9 XLSX kombinasyonu** hücre hücre.

⚠️ **2 mevcut test güncellendi** — ikisi de **STK-10a'da benim koyduğum kapsam nöbetçileriydi** ve
yeni filtreyi doğru yakaladılar. Gevşetilmediler: tam-eşitlik korundu, üstüne **Search (2048) ve
Material (4096) hâlâ kapalı** nöbetçileri eklendi. Ayrıntı §19.7.
⚠️ **Görsel render kontrolü YAPILMADI** — aynı engel (§19.8).

## ✅ SON TAMAMLANAN — `STK-10b-2` Serbest metin arama (2026-08-11)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §21

`Search` filtresi **6/6 katmanda** bağlandı; **RPR-01 gevşetilmeden yeşil** (14/14). Semantik mevcut
`SearchMovements`'tan **birebir** taşındı — yeni arama mimarisi icat edilmedi.

**🔴 BULGU — belge notu aramada YOK (mevcut davranış, değiştirilmedi):**
`ApplyLine` hareket satırının `note`'unu **NULL** yazıyor; kullanıcının giriş/çıkış belgesine yazdığı
not `stock_documents.note`'a gidiyor. Mevcut arama ise **`sm.note`**'a bakıyor. Yani "not" araması
bugün yalnız **ters kayıt gerekçesi** ve **bakım tüketimi** kayıtlarını buluyor — kullanıcının stok
belgesine yazdığı notu **bulmuyor**, ekran etiketi bunu vaat etse de.
Semantiği taşıdım, **değiştirmedim**; mevcut davranışı testle kilitledim (rapor ve ekran aynı sonucu
veriyor → kayma yok). ⛔ **Kararı senin:** arama `d.note`'u da kapsasın mı? → yeni iş **`STK-B2`**.

**Büyük/küçük harf:** mutlak iddia edilmedi — `LIKE` davranışı **lehçeye bağlı** (SQLite duyarsız,
PG duyarlı). Test, **rapor ile mevcut ekranın AYNI sonucu verdiğini** kilitliyor.

**🔒 `BranchScope` genişlemiyor:** Depo A oturumu Depo B bacağını arayarak da göremiyor; yabancı firma
kaydı aramayla da çıkmıyor.

**Kanıt:** **1521 / 1486 geçti / 0 kaldı / 35 atlandı** (taban 1480; **+41 senaryo**) · build 0 hata ·
36 çevrimdışı + 5 yeni HTTP + izole PG (sorgu planı: arama `~~` ile SQL'de) · **10 XLSX kombinasyonu**.
**Yeni indeks eklenmedi** — `LIKE '%…%'` baştan joker içerdiği için B-tree kullanılamaz; trigram
gerekirdi, mevcut hacim gerektirmiyor.

⚠️ **2 mevcut test yine güncellendi** — aynı kapsam nöbetçileri, `Search`'ü de doğru yakaladılar.
Gevşetilmediler; **`Material` (4096) hâlâ kapalı** nöbetçisi duruyor.
⚠️ **Görsel render kontrolü YAPILMADI** (§21.7).

## ✅ SON TAMAMLANAN — `STK-10b-3` Malzeme filtresi + autocomplete (2026-08-12)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §23

`Material` filtresi **6/6 katmanda** bağlandı; **RPR-01 gevşetilmeden yeşil** (14/14).
`MaterialIds` bir **liste**tir (diğer kimlik filtreleriyle aynı sözleşme) ve kaydın **SON** alanına
eklendi — pozisyonel argüman kayması artık kalıcı testle korunuyor.

**⚡ 2461 malzeme İNDİRİLMİYOR:** iki platform da **mevcut** arama desenini kullanıyor
(Web `/api/materials?search=` + `MudAutocomplete` · masaüstü yerel `Materials.List(term)`).
`/api/reports/scope` **büyümedi** — kaynak taramasıyla kilitlendi. Yeni uç açılmadı.

**⚡ Yeni indeks EKLENMEDİ:** izole PG planı, filtrenin **zaten var olan**
`ix_stock_movements_material` indeksini kullandığını gösteriyor (`Index Cond: material_id`).

**🔴 BULGU — kapsam dışı, yeni iş `RPR-02`:** HTTP hattında rapor isteği oturumun şubesini
**taşımıyor** (JWT yalnız kullanıcı+firma; `CreateSessionForUser` şube atamıyor). Yani **web'de**
şube daralması yalnız açık `branchIds` seçimiyle oluyor; giriş ekranındaki şube rapora yansımıyor.
Bu STK-10b-3'ün getirdiği bir durum **değil**, tüm raporlarda mevcut mimari — masaüstü etkilenmiyor.
**Düzeltilmedi**, karar/iş olarak ayrıldı.

**Kanıt:** **1553 / 1518 geçti / 0 kaldı / 35 atlandı** (taban 1521; **+32 senaryo**) · build 0 hata ·
25 çevrimdışı + 7 yeni HTTP + izole PG · **10 XLSX kombinasyonu** hücre hücre.

⚠️ **2 mevcut test yine güncellendi** — aynı kapsam nöbetçileri, `Material`'ı doğru yakaladılar.
Gevşetilmediler; nöbetçi **bir sonraki bite (8192) kaydırıldı** ve "yalnız `stock-movements`'ta açık"
tam-eşleşmesi eklendi.
⚠️ **Görsel render kontrolü YAPILMADI** — Raporlar ekranı giriş formunun arkasında; parolayı bir alana
yazmam (güvenlik kuralı) ve canlıya bağlanmam. Yapılmış gibi gösterilmedi (§23.6).

## ✅ SON TAMAMLANAN — `STK-10b-4` Ekranlar + B-1 · **STK-10 BİTTİ** (2026-08-12)

Sonuç kaydı: [`STK_10_HAREKET_RAPORU_PLANI.md`](STK_10_HAREKET_RAPORU_PLANI.md) §24 · Karar: **ADR-105**

**🔴 B-1 KAPATILDI.** Web ekranı lokasyonu, sunucudan gelen **limitli** listenin üzerinde
**istemcide** süzüyordu → seçilen depoya ait hareket ilk N kaydın dışındaysa **sessizce
kayboluyordu**. Filtre artık **SQL'de ve LIMIT'ten önce**. Eski davranışın aynı veride kaydı
kaybettiği, regresyon testine dönüştürüldü (hem çevrimdışı hem gerçek HTTP).

**Ekran = Rapor = XLSX.** Lokasyon/tür/arama/malzeme filtrelerinin WHERE'i tek üreteçten geliyor
(`StockMovementFilterSql`) → rapor ve ekran aynı satır kümesini vermek **zorunda**. Ekran `TableModel`'e
çevrilmedi (gereksiz yeniden tasarım olurdu); yalnız filtre mantığı birleştirildi.

**Masaüstü paritesi:** masaüstü hareket ekranına da **lokasyon filtresi** eklendi (web'de vardı,
masaüstünde yoktu — STK-10 envanterindeki parite eksiği). Ağ gerekmiyor, çevrimdışı çalışıyor.

**Kanıt:** **1589 / 1554 geçti / 0 kaldı / 35 atlandı** (taban 1553; **+36 senaryo**) · build 0 hata ·
31 çevrimdışı + 5 yeni HTTP + izole PG · 9 "ekran = rapor" + 6 "rapor = XLSX" kombinasyonu.
**Yeni indeks yok, migration yok.**

⚠️ **Görsel render kontrolü YAPILMADI** — ekran giriş formunun arkasında (§24.6).
🔎 **Yol üstünde kararsız bir test bulundu ve KÖK NEDENİ DÜZELTİLDİ** (R34): `SyncBalancePayloadTests`
senkron paketinin TAMAMINDA ham "777" metnini arıyordu; rastgele bir GUID "777" içerdiğinde kırılıyordu.
Assertion **gevşetilmedi, keskinleştirildi** (bakiye TABLOSUNUN pakette olmadığı doğrulanıyor). Retry/skip yok.

## ▶️ SIRADAKİ İŞ
**STK-10 bitti.** Sıradaki en doğru iş: **`STK-08` (KARAR-8)** — Migration064 canlıya alınmadan önce
"Atanmamış" stoğun nasıl dağıtılacağının kararı; deploy'un önündeki tek iş kuralı engeli budur.
⛔ Karar bekleyenler: **`STK-B2`** (arama `stock_documents.note`'u kapsasın mı) ·
**`RPR-02`/R33** (web isteği oturum şubesini taşımıyor) · **`KARAR-8`**.

## ⛔ Karar bekleyenler
| İş | Neyi bekliyor |
|---|---|
| `STK-08` | **KARAR-8** — "Atanmamış" stok nasıl dağıtılacak (öneri: kullanıcı transferle) |
| `BKM-01…03` | KARAR-4 (bakımda negatif stok mu, onay kapısı mı) |
| `TMZ-02`, `BRM-01`, `YTK-01…04` | YET-01 (rol değişince yetkiler) |
| `SNK-05` | Çevrimdışı onay çakışması |

## 📌 Canlı ortam — **YAYIN TURU 2026-08-12** (commit `a0d5c91`)
API `depowise-erp` **v151** · Web `depowise-web` **v177** · Neon PG **17.10** · **canlı şema 64**
(migration **çalışmadı** — kod kataloğu da 64, uygulanacak yeni sürüm yoktu) ·
**Masaüstü sürümü `1.0.137`** (paket sunucuda, SHA-256 `5ADE0CE5…D9F6BE`, 85,5 MB) ·
1 aktif firma (`Oze İnşaat`, 5 şube) · Test **1591/0/35** · Release derlemesi 0 hata

> Yayın sırası DEPLOYMENT.md'ye uygun: **API → Web → masaüstü publish → masaüstü güncelleme paketi.**
> Production'a **hiçbir INSERT/UPDATE/DELETE yapılmadı**; STK-08 dağıtımı **yapılmadı**.

## ⚠️ Açık riskler
- **Deploy edilince** stoğun neredeyse tamamı **"ATANMAMIŞ"** görünecek → KARAR-8 alınmadan kullanıcıya
  sürpriz olur. Gerçek rakamlar (üretim kopyasında ölçüldü): **DEPOWISE firması 8951,3** (663 satır,
  gerçek depo stoğu 0) · üç firma toplamı **8953,3**. Web artık bunu "Atanmamış" olarak AÇIKÇA gösteriyor
  ve "Tüm Şubeler" ile karıştırmıyor; dağıtım yine de KARAR-8 bekliyor.
- Migration, bakiyesi defterle **uyuşmayan** bir veritabanında **bilinçli olarak durur** (sessiz bozulma
  yerine açık hata). Böyle bir **masaüstü** veritabanı varsa güncelleme başlamaz → önce yeniden hesaplama
  gerekir. Üretim PG kopyasında uyuşmazlık **yok**.
- Masaüstü **paketi yayınlanmadı** — Grup 6 masaüstü düzeltmeleri kullanıcıya ulaşmadı.
- Branch **`master`'a birleştirilmedi**.
- **66 malzemede negatif stok** zaten mevcut (ADR-086 devralınan eksik stok); migration **1 yeni** negatif
  üretir (defterin söylediği) — toplam 67.

---

## ✅ SON TAMAMLANAN — `STK-MB` Çok şubeli stok doğrulaması + `D-1` / `H-1` (2026-08-12)

Gerçek kullanıcı testine geçmeden önce çok lokasyonlu stok modeli uçtan uca izole ortamda doğrulandı
(`MultiBranchStockScenarioTests`, 22 test). Denetimde **iki gerçek kod açığı** bulundu ve kapatıldı.

### D-1 — `Transfer` hedefi boş bırakılabiliyordu
`StockService.Transfer` kaynağın boşluğunu reddediyor, **hedefin** boşluğunu kontrol etmiyordu. Boş hedef
`ApplyDelta` yolunda sessizce `""` (ATANMAMIŞ) kovasına çevriliyordu → transfer, stoğu depodan çıkarıp
**"lokasyonu bilinmiyor" durumuna geri atabiliyordu** (STK-08'in çözdüğü belirsizliğin yeniden üretimi).
API ve masaüstü hedefi zaten zorunlu tutuyordu → kullanıcıdan tetiklenemiyordu; eksik olan **servis
katmanındaki savunma katmanıydı** (masaüstü bu servisi çevrimdışı doğrudan çağırır).
`DistributeUnassigned` ile **aynı** kural/mesaj eklendi.

### H-1 — Dağıtım listesi sessizce kesiliyordu (+ sıfır-satır tuzağı)
1. `ListUnassigned` varsayılan 500 satır döndürüyordu; web/masaüstü limiti yükseltmiyor ve **kaç kaydın
   gizlendiğini söyleyen bilgi taşımıyordu**. Canlıda ATANMAMIŞ'ta 676 listelenebilir satır var.
2. **Asıl tehlike:** `qty == 0` elemesi `LIMIT`'ten **sonra** C#'ta yapılıyordu → sıfırlar limitten yer
   kapıyordu. Dağıtılan kalem ATANMAMIŞ'ta 0 satırı olarak kaldığı için **ikinci turda liste sıfırlarla
   dolup gerçek kalemleri dışarı itebilirdi**. İzole testte kanıtlandı: 500 sıfır + 10 pozitif kalemde
   eski yol **hiçbir pozitif kalem** döndürmüyor.

Çözüm: sıfır filtresi `LIMIT`'ten **önce SQL'e** indi (`SqlDialect.NumericValue`, iki lehçe) · yeni
`UnassignedPage` (toplam / dağıtılabilir / gizli / kullanıcı metni) · ekran varsayılanı **2000** ·
web ve masaüstü **aynı** cümleyi gösteriyor. Eski `ListUnassigned` imzası ve varsayılanı **korundu**.

**Ölçülen (izole, canlı dağılımın birebir kopyası):** 677 ham satır → 676 listelenebilir · **görünen 676** ·
**dağıtılabilir 610** · **gizli 0** · arama ile erişilebilen pozitif **610** · negatif 66 (görünür,
dağıtılamaz) · silinmiş 1 (hiç görünmez).

**Test:** `UnassignedListLimitTests` (15 test) — A/B/C/D sınırları (499·500·501·676), F arama, çok turlu
dağıtım, geriye uyum, yetki, ölçekli sıfır ("0.000"), web/masaüstü sözleşme taraması.
Rapor: [`docs/tests/CokSubeliStok_Test_Report.md`](../tests/CokSubeliStok_Test_Report.md).

**Tüm paket: 1591 geçti · 0 başarısız · 35 atlandı (hepsi PostgreSQL — ortamda PG sunucusu yok).**

## ▶️ SIRADAKİ İŞ
**Yayın turu:** Web deploy + **masaüstü publish/update paketi** (kullanıcı kararı 2026-08-12 —
geliştirme turu kapatıldı, gerçek kullanıcı testine geçiliyor). Migration **gerekmiyor**: kod kataloğu
ve canlı şema **ikisi de 64**. STK-08 gerçek dağıtımı **yapılmayacak** — kullanıcı canlıda kendi test edecek.

---

## 2026-08-13 — MASAÜSTÜ GUI DOĞRULAMA TURU (şube kapsamı)

**Masaüstü GUI otomasyonu kuruldu ve 28 maddelik checklist gerçek UI etkileşimiyle koşturuldu:
25 GEÇTİ · 0 BAŞARISIZ · 3 koşturulmadı (madde 8 · 11 · 27).** Windows UI Automation ile Avalonia
penceresi sürülüyor; ek paket YOK. Ortam tamamen izole (yerel API + `DEPOWISE_ENVIRONMENT=GuiTest`),
üretime hiç bağlanılmadı.

**GUI testi ALTI GERÇEK ÜRÜN HATASI buldu — hepsi düzeltildi + 15 regresyon testi eklendi:**

| Kod | Hata | Neden önemliydi |
|---|---|---|
| GUI-01 | Şube kapsamı **masaüstünde fiilen yoktu** (paket `user_scopes` taşımıyor + `AuthService.Login` oturuma koymuyor) | Kapsamı A+B olan kullanıcı **yetkisiz Şube C'ye giriş yapabiliyordu**; makine o şubeye bağlanıyordu |
| GUI-02 | Elle cari hareketi `branch_id = NULL` yazılıyordu | Şubesiz satır her şubede görünür → **A'nın bakiyesi B'nin ekstresinde ve altı raporun tamamında** |
| GUI-02b | Ters kayıt şubesiz + kapsam kontrolsüz | Yetkisiz şubenin hareketi iptal edilebiliyordu |
| GUI-03 | "Tüm yetkili şubeler" etiketi ile veri çelişiyordu | Etiket A+B derken yalnız çalışma şubesi geliyordu |
| GUI-04 | Rapor şube filtresinde yetkisiz şube listeleniyordu | Deny-by-default ihlali (masaüstü + `/api/reports/scope`) |
| GUI-05 | "Şube Kapsamı" bölümü **sessizce kayboluyordu** | Kapsam yerelden, kullanıcı/yetki sunucudan okunuyordu; hata da eziliyordu |

Düzeltmelerde **ikinci bir kapsam mantığı üretilmedi** — hepsi tek otorite `BranchAccess` üzerinden yürür.
Masaüstü **ve** web/API birlikte düzeltildi (platform önceliği kuralı).

Rapor: [`docs/tests/Sube_Kapsami_GUI_Test_Report.md`](../tests/Sube_Kapsami_GUI_Test_Report.md) ·
Checklist: [`docs/tests/Masaustu_GUI_Checklist.md`](../tests/Masaustu_GUI_Checklist.md)

### ▶️ SIRADAKİ TEK İŞ (güncellendi)
**Yayın turu — ama GUI-01/02 nedeniyle önce şu karar gerekir:** GUI-02 düzeltmesi yalnız BUNDAN SONRA
girilecek hareketleri şubeye bağlar. Canlıda **şubesiz (branch_id NULL) mevcut cari hareketler varsa**
bunlar hâlâ her şubede görünür. Yayın öncesi canlı veride şubesiz hareket olup olmadığı sayılmalı;
varsa kullanıcıya sorulup toplu şube ataması yapılmalıdır (veri düzeltme kararı kullanıcınındır).

---

## 2026-08-18 — ŞUBE YAPISI + SIFIRLAMA + YETKİ DEVRİ TURU

Kullanıcı talebi üzerine üç alanda **salt-okunur tam analiz** yapıldı (16 bulgu), ardından hepsi
düzeltildi. Analiz: [`docs/ANALIZ_SUBE_VE_SIFIRLAMA.md`](../ANALIZ_SUBE_VE_SIFIRLAMA.md)

**Kullanıcının şartı:** *"Web'den firma verisi sıfırladığımda babam mevcut kullanıcısıyla girip sıfırdan
veri girebilsin; şubeler ve kullanıcılar silinmesin."* → Sunucu tarafı bu şartı zaten karşılıyordu,
**masaüstü tarafı SIF-01 hatasıyla bozuyordu.** Düzeltildi ve testle kilitlendi.

### Düzeltilen bulgular

| Kod | Bulgu | Neden önemliydi |
|---|---|---|
| **SIF-01** 🔴 | Yerel sıfırlama, ADR-083'ün **tam silme** fonksiyonunu çağırıyordu (firma+kullanıcı+şube+yetki siliniyordu) | Sıfırlama sonrası o makinede **çevrimdışı giriş imkânsız** hâle geliyordu — şantiyede internet yoksa kilitlenme |
| **SIF-03** 🟡 | Silme listesi senkron sözleşmesinden okunuyordu → 7 tablo + 8 satır tablosu atlanıyordu | Sıfırlama sonrası **eski stok bakiyeleri / muayene / sayaç** öksüz kalıyordu |
| **SIF-06** 🟡 | Şablonlar (malzeme + araç) senkronda **hiç taşınmıyordu** | Masaüstünde açılan şablon web'e, web'deki masaüstüne gitmiyordu |
| **ŞB-01** 🔴 | Şube aynası `kind` + `parent_id` taşımıyordu | **Kullanıcının bildirdiği hata:** üst şube seçilip kaydediliyor, ekran hemen "—" gösteriyordu (sunucudaki veri doğruydu) |
| **ŞB-04** 🔴 | `parent_id` hiçbir yerde OKUNMUYORDU | "Üst Şube" yalnız bir etiketti: kapsam yayılmıyor, rapor toplamıyordu |
| **ŞB-02** 🟠 | Döngü koruması yoktu (A→B, B→A) | ŞB-04 ile ağaç gezildiğinden sonsuz döngü riski |
| **ŞB-03** 🟠 | Silinmiş üst şube listede görünüyor + alt şubesi olan şube silinebiliyordu | Kopuk referans |
| **ŞB-06** 🟠 | Web "+" hızlı ekleme `companyId` göndermiyordu | Süper admin başka firmadayken şube **kendi firmasına** açılıyordu |
| **YET** 🟠 | "Yerel Sıfırlama" yetki ağacında YOKTU, devredilemiyordu | Kullanıcı isteği: menü maddesi olsun + devredilebilsin |

### Yeni yetki katmanı — "AÇIK-VERİLİR" (`AppModules.IsExplicitOnly`)
Sistemde yalnız iki uç vardı: *hiç devredilemez* (süper-admin-only) veya *admin bypass ile örtük açık*.
Kullanıcının istediği üçüncü katman eklendi: **devredilebilir ama asla örtük verilmeyen.**
Zincir: **Süper Admin / Kısıtlı Süper Admin → Admin → Personel** — her kademe yalnız kendisinde olanı verir.
İlk üyesi: `local_reset` — **Yerel Veri Sıfırlama** (web ekranı `/local-reset`).

### Yanında bulunan ek açık (kapsam dışıydı, kapatıldı)
İçe aktarım oturum **kopyası** (web + masaüstü) `ScopeBranchIds`/`HomeBranchId` taşımıyordu →
içe aktarım yolunda kullanıcı **kısıtsız** sayılıyor, kapsam dışı şubeye kayıt basılabiliyordu.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2018 toplam · 1983 geçti · 0 başarısız · 35 atlandı** (bilinen PostgreSQL atlamaları) |
| Yeni testler | 43 (`BusinessResetCoverageTests` 5 · `BranchHierarchyTests` 11 · `TemplateSyncTests` 4 · `BranchParentScopeTests` 11 · `ExplicitOnlyModuleTests` 12) |
| Derleme | Masaüstü + Web + API — **0 hata** |

### ✅ YAYINLANDI (2026-08-18)
**API + Web + masaüstü 1.0.139 canlıya alındı.** Ayrıntı aşağıdaki "YAYIN TURU" bölümünde.

---

## 2026-08-18 — YAYIN TURU (API v? / Web / masaüstü 1.0.139)

Yayın öncesi **tam doğrulama** yapıldı; ardından `DEPLOYMENT.md` sırasına göre API → Web → masaüstü.

### Yayın öncesi testler

| Koşum | Sonuç |
|---|---|
| Tam takım (SQLite) | **2018 toplam · 1983 geçti · 0 başarısız · 35 atlandı** |
| Çözüm derlemesi (Release) | **0 hata** (40 uyarı — mevcut) |
| PostgreSQL — üretim lehçesi, **izole `depowise_test`** üzerinde | **20 / 20 geçti** |

**PostgreSQL sınıf sınıf:** `PostgresPurgeTests` 3/3 (SIF-03'ü doğrudan kapsar: *iş verisini siler,
firma+kullanıcı KORUR*) · `PostgresEndToEndTests` 1/1 (giriş → `BranchTree` PG'de) ·
`PostgresSyncRecoveryTests` 1/1 (SIF-06 apply) · `PostgresEditLockTests` 6/6 (şube düzenleme kilidi) ·
`PostgresMigrationTests` 1/1 · `PostgresStockMovementOrderingTests` 5/5 · `PostgresStockConcurrencyTests` 3/3.

> ⚠️ **Yanıltıcı bir ara sonuç kayda geçirilir:** PG testleri ilk kez topluca koşturulduğunda **9 test
> başarısız** oldu. İnceleme sonucu sebep **ağ kopması** çıktı (`SocketException: bilinen böyle bir ana
> bilgisayar yok` = DNS çözülemedi; ayrıca "bağlantı uzaktaki ana bilgisayar tarafından kapatıldı").
> Aynı testler bağlantı düzeldikten sonra sınıf sınıf koşturulduğunda **tamamı geçti**. Ürün hatası
> DEĞİLDİ. Bu testler zaten normal takımda atlanır (`ApiTestHost` `DEPOWISE_PG_URL`'i süreç genelinde
> null'lar) → taban koşumda da koşmuyorlardı, yani gerileme değildi.

### Yayın adımları ve kanıtlar

| Adım | Komut | Kanıt |
|---|---|---|
| Uçuş öncesi | `flyctl secrets list` | `DEPOWISE_PG_URL` **Deployed** → PostgreSQL aktif, SQLite'a düşmedi |
| Uçuş öncisi | `df -h /data` | %45 dolu · 506 MB boş (ADR-070 disk riski yok) |
| API | `flyctl deploy --config fly.toml --ha=false` | makine `started` · `/health` **200** |
| DB teyidi | `/api/releases/latest` | Gerçek veri döndü (1.0.138) → SQLite geri dönüşü OLMADI |
| Web | `flyctl deploy --config fly.web.toml --ha=false` | `/` **200** · yeni ekran `/local-reset` **200** |
| Masaüstü publish | `-p:Version=1.0.139` | 270 dosya — 1.0.138 ile **birebir aynı ağaç** |
| Paket | `Compress-Archive` | `DepoWise-desktop-1.0.139.zip` · **89.919.610 bayt** |
| Yayın | `node scripts/publish_release.mjs` | `/api/releases/latest` = **1.0.139** |
| Checksum | yerel ↔ sunucu | `D7AF2D3D9E47BED6CB22651F28525AC615901DDBFEC5555D704DF5E95D5D44D5` — **EŞLEŞTİ** |
| İndirme ucu | `/api/releases/1.0.139/download` | **200** · 89.919.610 bayt |
| Yayın sonrası disk | `df -h /data` | %45 (eski paketler otomatik temizlendi — ADR-070) |

**Migration ÇALIŞMADI** — bu turda şema değişikliği yoktu; üretim şema sürümü **68**'de kaldı.
Production veritabanına hiçbir INSERT/UPDATE/DELETE yapılmadı. DEPOWISE ve Öze İnşaat verisine dokunulmadı.

### ▶️ SIRADAKİ TEK İŞ
**Firma verisi sıfırlama işlemi artık güvenle yapılabilir** — ama SIF-02 hâlâ açık olduğu için
operasyonel adım zorunlu: kullanıcılara programı **tamamen kapattırın** → web'den sıfırlayın →
tekrar açtırın. Makineler açılışta 1.0.139'a güncellenip temizliği uygulayacaktır.

---

## 2026-08-18 — DENETİM YAYIN TURU (API + Web + masaüstü 1.0.140) · Migration **069**

Kapsamlı denetimin (7 tur, 18 bulgu) **tamamı** canlıya alındı.

### Yayın öncesi doğrulama
| Koşum | Sonuç |
|---|---|
| Tam takım (SQLite) | **2083 toplam · 2048 geçti · 0 başarısız · 35 atlandı** |
| PostgreSQL (izole `depowise_test`) | 6/6 — Migration069 iki lehçede de çalışıyor |
| Çözüm derlemesi (Release) | **0 hata** |
| Bu denetimde eklenen test | **69** |

### 🔐 Migration öncesi yedek
Neon **dal (branch) anlık kopyası** alındı — kopyala-yaz olduğu için anında ve maliyetsiz:
- Proje: `alpdepo` (`autumn-morning-75319830`) · Kaynak: `production` (`br-bold-snow-asagtawc`)
- **Yedek dalı: `pre-migration-069` (`br-bold-tooth-asxxn1dv`)** — 2026-08-18T18:29:30Z
- Geri dönüş gerekirse bu dal migration ÖNCESİ tam veriyi taşır.

### Yayın adımları ve kanıtlar
| Adım | Kanıt |
|---|---|
| Uçuş öncesi | `DEPOWISE_PG_URL` secret var · `/data` %45 (505 MB boş) · API/Web 200 |
| **API** | `flyctl deploy -c fly.toml --ha=false` → makine `started`, `/health` **200** |
| **Migration 069** | **Uygulandı.** Kanıt: `ServerServices:115` başlangıçta `MigrationRunner.Run()` çağırır; `Run()` içinde **try/catch YOKTUR** → migration hata verse uygulama hiç açılmazdı. Uygulama sağlıklı ve gerçek veriyle yanıt veriyor. Şema sürümü: **68 → 69** |
| DB teyidi | `/api/releases/latest` gerçek veri döndü → SQLite'a geri dönüş OLMADI |
| **Web** | `flyctl deploy -c fly.web.toml --ha=false` → `/` 200 · `/local-reset` `/branches` `/reports` `/trash` hepsi **200** |
| Masaüstü publish | 270 dosya — 1.0.139 ile **birebir aynı ağaç** |
| Paket | `DepoWise-desktop-1.0.140.zip` · **89.923.623 bayt** |
| Yayın | `/api/releases/latest` = **1.0.140** |
| Checksum | `7A86A68A6E328F3108324FDD8D3868404A3EC49018015F6D5DCAF5DE66E2F624` — **EŞLEŞTİ** |
| İndirme ucu | **200** |
| Yayın sonrası disk | %45 (eski paketler otomatik temizlendi — ADR-070) |

### ⚠️ Kullanıcıların fark edeceği davranış değişiklikleri
1. **Şubeyle sınırlı kullanıcılar** artık diğer şubelerin araç/personel/stok hareketi/talep verisini
   ne raporlarda ne de kendi bilgisayarlarında görecek (gizlilik düzeltmesi — SNK-A7, DEN-E1/E2).
2. **Üst şubeye yetkili kullanıcılar** alt şubeleri de görecek ve onlara **yazabilecek** (ŞB-04).
3. Yetkisi olmayan kullanıcılar web'de **Çöp Kutusu geri yükleme**, **Excel'e Aktar**, **stok/yakıt
   iptal** butonlarını artık **göremeyecek** (DEN-F1/F1b — sunucu bunları zaten engelliyordu).
4. Masaüstünde iptal edilen **cari hareket ve stok belgesi artık web'de de iptal görünecek**
   (SNK-A1/A2 — en kritik düzeltme; öncesinde web'de bakiye yanlıştı).

### ▶️ SIRADAKİ TEK İŞ
Kullanıcı talebi: **"Şube / Şantiye kendi tanım ekranı dışında oluşturulamamalı."**
İnceleme yapıldı (kod okuması, üretime dokunulmadı): şube oluşturabilen **3** nokta var —
(1) Şube/Şantiye Tanım ekranı ✔ · (2) aynı ekrandaki "+" kısayolu (yalnız ad sorar, yarım kayıt) ·
(3) **Firma ekranındaki "İlk Şube / Şantiye Adı" alanı** (web + masaüstü) ← **kuralın ihlali burada**.
Kaldırılması tek başına yetmez: kod "şubesiz firmaya kullanıcı eklenemez" kuralını uyguluyor →
akış kararı gerekiyor. Kullanıcı yayın sonrası bu konuya bakacağını belirtti.

---

## ✅ SON TAMAMLANAN — `MNU` Menü / Ekran Yönetimi (2026-08-18)

**Kullanıcı isteği:** web'e özel bir menü/ekran yönetim ekranı — ekran sırası, üst menü ataması,
üst menü adı/sırası, platform seçimi, menüde aktif/pasif, görünen ekran adı.

**Envanterde çıkan gerçek:** istenen 7 maddeden **2'si (platform + aktif/pasif) G5 ile 2026-08-12'de
zaten yapılmıştı.** Eksik olan yalnız **menü düzeni**: ad · üst menü · sıra.

**Karar (ADR-109):** ayrı ekran açılmadı; mevcut `/screen-visibility` genişletildi ve adı
**"Menü / Ekran Yönetimi"** oldu. Route · ekran anahtarı · yetki modülü **değişmedi**.
Grup kimliği için katalog başlığı "değişmez sistem anahtarı" kabul edildi → **katalogda tek satır bile
değişmeden** yeniden adlandırma güvenli hâle geldi. Büyük refactor gerekmedi.

**Yeni yapı:** `Migration070_MenuLayout` (`screen_menu_layout`, `menu_group_layout`) · `MenuLayout`
(saf çözümleyici, web projesine linklendi — masaüstü ve web **aynı sıralama kodunu** çağırır) ·
`MenuLayoutService` (`ScreenVisibilityService` deseni) · `/api/screens/layout/manage` · `/layout` ·
`/layout/reset`. **Satır yoksa katalog varsayılanı** → migration sonrası menü birebir aynı.

### Bu turda bulunan ve düzeltilen 5 gerçek hata
| Kod | Hata | Etki |
|---|---|---|
| **MNU-B1** | Platform ayarı gerçek masaüstü makinelere **hiç inmiyordu** | "Masaüstü" kutusu etkisizdi (ADR-110) |
| **MNU-B2** | Süper admin yönetim ekranını kapatıp **kendini kalıcı kilitleyebiliyordu** | Geri dönüşü yoktu (ADR-111) |
| — | `HasFlag(Desktop\|Web)` yüzünden **tek platformlu 14 ekran** listede yoktu | Yönetilemiyorlardı |
| — | Satır taşınınca `<select>` **eski değeri** gösteriyordu (Blazor konum bazlı yeniden kullanım) | Yanlış grup görünüyordu |
| — | Reddedilen değişiklikten sonra **onay kutusu yanlış durumda** kalıyordu | Kullanıcı kapattığını sanabilirdi |

### Doğrulama
- Release build: **API + Web + Masaüstü → 0 hata**.
- Otomatik test: **+48 senaryo**; tam takım **2098 geçti / 0 başarısız / 35 atlandı** (toplam 2133).
- Gerçek tarayıcı GUI testi: **17 maddenin 16'sı ✅, 1'i 🟡** (yetkisiz kullanıcı GUI'de değil, sunucu
  tarafında doğrulandı — üç uç da 401). Ayrıntı: `docs/tests/Menu_Ekran_Yonetimi_Test_Report.md`.
- **Üretime hiçbir işlem yapılmadı** (INSERT/UPDATE/DELETE/DDL/Migration/Deploy/Publish/Restart = 0).

### Sıradaki tek iş
Kullanıcı onayıyla **deploy** (API + Web + masaüstü sürümü). Deploy öncesi Neon yedek şubesi alınmalı;
Migration 070 üretim şemasını 70'e çıkaracak.

---

## 🚀 YAYIN — `MNU` Menü / Ekran Yönetimi canlıda (2026-08-19)

**API + Web deploy edildi. Masaüstü publish bu turda BİLİNÇLİ olarak yapılmadı** (kullanıcı kararı;
ayrı tur). Canlıdaki masaüstü sürümü **1.0.140** olarak kalıyor.

### Deploy öncesi doğrulama
| Kontrol | Sonuç |
|---|---|
| Release build (API · Web · Masaüstü) | **0 hata** |
| Tam takım (SQLite) | **2098 geçti · 0 başarısız · 35 atlandı** |
| PostgreSQL (izole `depowise_test`) | **48 geçti · 0 başarısız · 0 atlandı** |
| Web GUI (17 madde) | **17/17 geçti** — geçen turda kısmi kalan "yetkisiz kullanıcı" maddesi gerçek personel hesabıyla tamamlandı (4 uç da **403**) |
| Masaüstü GUI | **geçti** — gerçek Avalonia penceresi, UI Automation |
| Üretim yedeği | alındı + `pg_restore -l` ile doğrulandı |

### ⭐ MNU-B1'in GERÇEK MASAÜSTÜNDE kanıtı
İzole ortamda web'den yapılan düzen değişiklikleri **gerçek masaüstü uygulamasının menüsünde** göründü:
grup adı `Yakıt` → **Akaryakıt**, ekran adı → **Yakıt Çıkışı**, sıra (**Özet** başa), taşıma
(**Depo Girişleri** → Yönetim). Bu değişiklikler eskiden masaüstüne **hiç ulaşmıyordu**.

### Deploy sonucu
| | |
|---|---|
| API | `depowise-erp` v158 · makine `started` · `/health` 200 · `/api/public/companies` 200 |
| Üretim firması | `ed271d0ca2b04a73b97f5025a53a04b4 / Oze İnşaat` ✔ |
| Migration 070 | **uygulandı** — `70 \| menu_layout \| 2026-08-18 21:45:15+00` |
| Şema sürümü | **69 → 70** |
| Web | `depowise-web` v181 · `/`, `/login`, `/screen-visibility`, `/reports`, `/parties` → **200** |

### Üretim verisi (salt-okunur, READ ONLY + ROLLBACK)
| | Önce | Sonra |
|---|---|---|
| Firma | 3 | **3** |
| Kullanıcı | 8 | **8** |
| Şube | 10 | **10** |
| Stok hareketi | 663 | **663** |
| Tablo | 75 | **77** (yalnız Migration 070'in 2 tablosu) |

Yeni tabloların üçü de **0 satır** → hiçbir firmanın menüsü değişmedi, herkes katalog varsayılanıyla
devam ediyor. Özellik **kullanılmaya hazır ama kapalı** durumda.

### Sıradaki tek iş
Masaüstü sürüm publish'i (ayrı tur). Masaüstü kullanıcıları menü düzenini ancak yeni sürümle görecek;
**1.0.140 çalışmaya devam eder** (ayarlar inmez, katalog varsayılanı geçerlidir — bozulma yok).

---

## 🚀 YAYIN — Masaüstü **1.0.141** (2026-08-19)

Menü / Ekran Yönetimi turunu tamamlayan son adım. Bu sürümle birlikte **platform ve menü düzeni
ayarları masaüstü uygulamasına da iniyor** (1.0.140 ve öncesi bu ayarları almıyordu — MNU-B1).

| | |
|---|---|
| Sürüm | **1.0.141** (önceki 1.0.140) |
| Paket | `DepoWise-desktop-1.0.141.zip` · **89.944.546 bayt** (85,8 MB) · 252 dosya |
| SHA-256 | `14A304FD17392FE3E38DEF5B6F3FE2D10DC98BCE49B3D5E11258B221355B2C63` |
| Sunucu doğrulaması | `/api/releases/latest` → 1.0.141, checksum **birebir aynı** |
| İndirme ucu | `/api/releases/1.0.141/download` → **HTTP 200**, tam boyut |
| Publish build | 0 hata (yalnız Avalonia `AVLN5001` obsolete uyarıları) |
| Sunucu diski | **%45** (403M/974M) — eski paket otomatik temizlendi (~0,24 GB) |

### Bu sürümde ayrıca (2026-08-19 düzeltmesi)
**Platform kutuları düzenleme modunda kilitliydi** (kullanıcı bildirimi). Kök neden: platform ayarı
"anında kaydeden" ayrı bir akıştaydı ve düzen düzenlemesiyle çakışmasın diye `_edit` modunda
`disabled` yapılmıştı → Düzenle'ye basınca kutular kapanıyor, basmayınca ekran salt-okunur
görünüyordu; **platform ayrımı fiilen yapılamıyordu.** Tek akışa indirildi
(`Düzenle → değiştir → Kaydet`); PLATFORM kolonu kaydedilmemiş değişikliği anında gösterir,
kaydetme onayı **kapatılacak ekranları tek tek listeler**. Web **v182** ile canlıya alındı.

### Sıradaki tek iş
Kullanıcı geri bildirimi bekleniyor ("eksik veya hata görürsem yazarım"). Açık bir iş yok.

---

## 🚀 YAYIN — `SEC` Üst Grup (menüde üçüncü seviye) canlıda (2026-08-19)

Menü artık **ÜST GRUP → ÜST MENÜ → EKRAN** olabiliyor (ADR-112). Yönetim yine
**Menü / Ekran Yönetimi** ekranında; ayrı ekran açılmadı.

### Deploy öncesi doğrulama
| Kontrol | Sonuç |
|---|---|
| PostgreSQL (izole `depowise_test`) | **48 geçti · 0 başarısız** → Migration 071 (`ALTER TABLE`) PG 18'de sorunsuz |
| Tam takım (SQLite) | **2115 geçti · 0 başarısız · 35 atlandı** (öncesi 2098 → **+17**, sıfır regresyon) |
| Release build | API · Web · Masaüstü → **0 hata** |
| Web GUI | üst grup oluşturuldu, iki üst menü bağlandı, kaydedildi, yenilemede korundu, menüde `SAHA OPERASYONU › ARAÇLAR · YAKIT` göründü |
| Masaüstü GUI | aynı yapı Avalonia menüsünde de göründü; **ikon rayı ve mevcut ekranlar etkilenmedi** |
| Üretim yedeği | `depowise_prod_2026-08-19_110807.dump` · 568.009 bayt · SHA-256 `cf923ee2…65b4a` · `pg_restore -l` çıkış 0, **77 TABLE DATA** |

### Deploy sonucu
| | |
|---|---|
| API | `depowise-erp` **v159** · started · `/health` 200 · `/api/public/companies` 200 → `ed271d0ca2b04a73b97f5025a53a04b4 / Oze İnşaat` |
| Migration 071 | **uygulandı** — `71 \| menu_section \| 2026-08-19 08:09:43+00` · `parent_group_key` kolonu oluştu |
| Şema sürümü | **70 → 71** |
| Web | `depowise-web` **v183** · `/`, `/login`, `/screen-visibility`, `/reports`, `/parties` → 200 |
| Masaüstü | **1.0.142** · 89.950.837 bayt · SHA-256 `35D3955F…B56456` · indirme ucu 200 · disk %45 |

### Üretim verisi (READ ONLY + ROLLBACK)
Firma **3→3** · Kullanıcı **8→8** · Şube **10→10** · Stok hareketi **663→663** · Tablo **77→77**
(Migration 071 yalnız kolon ekledi, tablo sayısı değişmedi).
`menu_group_layout` ve `screen_menu_layout` **0 satır** → hiçbir firmanın menüsü değişmedi;
özellik **kullanılmaya hazır ama kapalı**.

### Bu turda düzeltilen hata
Web istemcisi düzen paketindeki `parentGroupKey` alanını okumuyordu → kaydedilen üst grup menüye
yansımıyordu. Gerçek GUI turunda yakalandı, `ApiClient.ParseLayout` düzeltildi.

### Sıradaki tek iş
Kullanıcı geri bildirimi. Açık iş yok.

---

## ✅ SON TAMAMLANAN — `SEMA` Nihai menü şeması canlıda uygulandı (2026-08-19)

Kullanıcı menüyü tek tek düzenlemek yerine **nihai şemasını iletti**; şema toplu olarak uygulandı.

### Uygulanan yapı (3 seviye)
`UYARILAR` · `MALZEME VE STOK` · `OPERASYON` · `TALEPLER` · `FİNANS` · `RAPORLAR` ·
`KURUMSAL YÖNETİM` · `SİSTEM YÖNETİMİ` · `AYARLAR`
— 6 üst grup · 17 üst menü · **58 ekranın tamamı tam bir kez** yerleşti (betik doğrulaması).

### Şemada yer almayan 4 ekran (gizlenmedi, yerleri raporlandı)
| Ekran | Yerleştirildiği yer | Gerekçe |
|---|---|---|
| Uyarılar | en üst seviyede kendi başlığı | şemada yoktu; ekran kaybolmasın |
| Malzeme Şablonları · Atanmamış Stok Dağıtımı | Malzemeler | yalnız masaüstünde var |
| Excel'e Aktarım | Ayarlar | web `import` + masaüstü `import_export`; her platformda yalnız BİRİ görünür |

### Bu turda yakalanan iki gerçek sorun
| # | Sorun | Kök neden | Çözüm |
|---|---|---|---|
| **SEMA-B1** | Şema önce **yanlış firmaya** (DEPOWISE) yazıldı | Menü düzeni **firma bazlıdır**; süper admin firma vermeden giriş yapınca kendi firmasına yazılır | Betiğe `--firma=` eklendi; web'in kullandığı `/api/auth/select-company` akışı kullanılıyor |
| **SEMA-B2** | `Oze Group` firmasına yazma **sessizce** DEPOWISE'a düşüyordu | Firma **silinmiş** (`is_deleted=1`); `AuthService` silinmiş firmada süper admini kilitlenmesin diye kendi firmasına düşürüyor (bilinçli davranış) | Canlı tek gerçek firma **Oze İnşaat**; ona uygulandı |

### Yeni kural — boş tanım saklanmaz (ADR-113)
Kullanıcı isteği: *"altında ekran olmayan menü ve üst menü kalacak olursa eğer tanımı sil."*
Şema sonrası boşalan üç grup (**Personel · Yönetim · İmport / Export**) firma kaydından silindi ve
`MenuLayoutService.Save` artık boş tanımı hiç yazmıyor → bir dahaki düzenlemede geri gelmez.
Katalog tanımı programda durur; bir ekran tekrar oraya taşınırsa grup kendiliğinden döner.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Betik doğrulaması | 58 ekran · tekrar 0 · bilinmeyen 0 · şemada olmayan 0 |
| Canlı ağaç okuması | şemayla **birebir** aynı |
| `menu_group_layout` | Oze İnşaat **23** · DEPOWISE **23** (boş 3 grup yok) |
| Üretim iş verisi (READ ONLY + ROLLBACK) | firma 3 · kullanıcı 8 · stok hareketi **663** — değişmedi |
| `MenuSectionTests` | **20/20** (S18 · S19 · S20 yeni) |

### Sıradaki tek iş
API + Web deploy (ADR-113 sunucu kuralının canlıya çıkması).

---

## ✅ SON TAMAMLANAN — `VAR` Nihai şema PROJENİN VARSAYILANI + firma izolasyon denetimi (2026-08-19)

### 1) Varsayılan menü şeması (ADR-114) — canlıda
Şema artık firma kaydı değil, **kataloğun kendisi**. Hiçbir kayıt olmadan menü şu düzende çıkar ve
**yeni açılan her firma** da bununla başlar:

`UYARILAR · MALZEME VE STOK · OPERASYON · TALEPLER · FİNANS · RAPORLAR · KURUMSAL YÖNETİM ·
SİSTEM YÖNETİMİ · AYARLAR` (6 üst grup · 17 üst menü · 58 ekran)

| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2119 geçti · 0 başarısız · 35 atlandı** |
| Masaüstü menü bağlantısı | **47 → 47** (ekran kaybı yok, S13 kilidi) |
| Web menü bağlantısı | **55 → 55** (S14 kilidi) |
| Gerçek web GUI (yerel, SIFIR kayıtla) | menü şemayla **birebir** |
| Canlı menü (API okuması, iki firma) | şemayla **birebir** |
| `menu_group_layout` / `screen_menu_layout` | **0 / 0 satır** — düzen tamamen katalogdan |
| Şema sürümü | **71 → 71** (migration yok) |
| Üretim iş verisi (READ ONLY + ROLLBACK) | firma 3 · kullanıcı 8 · malzeme 2459 · stok 663 — değişmedi |

Yayınlananlar: API **v161** · Web **v184** · Masaüstü **1.0.143** (85,8 MB · checksum `1f110f16…` ·
indirme ucu 200). Masaüstü sürümü ZORUNLU: katalog uygulamaya derlenir.

### 2) Firma (tenant) izolasyon denetimi — kullanıcı isteği
**Sonuç: kodda sızıntı bulunamadı.**

| Kontrol | Bulgu |
|---|---|
| `company_id` kolonu olan tüm tablolarda firmasız satır | yalnız `roles` (4 **sistem rolü**, tasarım gereği ortak) |
| Tanım sorguları (marka · birim · kategori · model) | hepsi `WHERE company_id=@c` ile süzülü |
| Başka firmanın kaydına id ile erişim | `"başka firmaya ait"` hatasıyla reddediliyor (ör. `FuelService.ReadMeter`) |
| Şube silme / güncelleme | `TenantSql.ScopePredicate()` ile firmaya kapalı |
| Otomatik test kapsamı | 5 ayrı izolasyon test dosyası |

### 3) ⚠️ Bulunan GERÇEK sorun — veri yanlış firmada (SEMA-B4)
İş verisinin TAMAMI **DEPOWISE** firmasının altında: 2459 malzeme · 94 araç · 663 stok hareketi ·
264 kategori · 26 marka · 12 birim · 50 model. Canlı firma **Oze İnşaat**'ta yalnız kullanıcılar,
şubeler ve makineler var. Kullanıcının "tanımlar birbirine karışıyor" şüphesinin kaynağı budur:
izolasyon çalışıyor, veri yanlış yerde duruyor.

### 4) Firma silme — KULLANICIYA BIRAKILDI
`Oze Group` ve `DEPOWISE` firmalarının kalıcı silinmesi istendi. Kalıcı Silme geri alınamaz ve
tasarım gereği **özel kod** (yalnız kullanıcıda olan gizli kod) ister; bu adım kullanıcı tarafından
web'deki **Kalıcı Silme** ekranından yapılacak. Kritik not: `superadmin` hesabı DEPOWISE'tadır →
silme **`osman.alpaslan`** (Oze İnşaat, süper admin) ile yapılmalıdır.

### Sıradaki tek iş
Kullanıcı iki firmayı Kalıcı Silme ekranından siler; sonucu doğrulayacağız.

---

## ✅ SON TAMAMLANAN — `YET-C` Yetki ekranı düzeltmeleri canlıda (2026-08-19)

Kullanıcı, sunulan üç yoldan **"önce C (hatalar), sonra A (yapı)"** sıralamasını onayladı.
Bu kayıt **C turudur**; ekran sayısı DEĞİŞMEDİ, A turu (dört ekran → iki ekran) sıradadır.

### Yapılanlar
| # | İş | Masaüstü | Web |
|---|---|---|---|
| C1 | İkon rayı (dikey şerit) kaldırıldı | ✅ | karşılığı yoktu |
| C2 | Sonsuz yükleme + görünmez hata | — | ✅ Rol + Firma Yetki Kontrol |
| C3 | Düzenle → Kaydet akışı + rol aynı ekranda | ✅ | ✅ |
| C4 | **YET-C4:** yetkisiz açılışta sayfa çöküyordu | — | ✅ 3 ekran korumaya alındı |

### Gerçek arayüz turunda BULUNAN hata (YET-C4)
`/permissions` oturumsuz açıldığında `OnInitializedAsync` içindeki korumasız çağrı 401 alıyor,
**Blazor devresi tamamen düşüyordu** (bembeyaz ekran). Aynı desen `PermissionTemplates` ve `Users`
ekranlarında da vardı. Üçü de düzeltildi; düzeltme aynı turda doğrulandı — sunucu günlüğünde
**artık istisna yok**.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2129 geçti · 0 başarısız · 35 atlandı** |
| Yeni testler (`PermissionScreenUxTests`) | **10/10** |
| Web yetki ekranları (canlı) | `/permissions` · `/role-permissions` · `/company-permissions` · `/permission-templates` · `/users` → **hepsi 200** |
| Masaüstü bağlamaları | Avalonia **derlenmiş bağlama** ile doğrulandı (0 hata) |
| Şema sürümü | **71 → 71** (migration yok) |
| Üretim verisi (READ ONLY + ROLLBACK) | firma 3 · kullanıcı 8 · malzeme 2459 · araç 94 · stok 663 — **değişmedi** |

Yayınlananlar: API **v162** · Web **v185** · Masaüstü **1.0.144** (85,8 MB · checksum `bde1490c…` ·
indirme ucu 200).

### Bilinen sınır
Masaüstü ve web'in **oturum içi** tıklama turu yapılmadı: giriş yapmayı gerektiriyor.
Yerine derlenmiş bağlama + 10 kaynak kilidi testi + oturumsuz sayfa turu kullanıldı.

### Sıradaki tek iş
**A turu** — dört yetki ekranını ikiye indirmek ve rol tavanını firma bazlı yapmak
(onaylandı, ayrı tur olarak yapılacak). Ayrıca iki pasif firmanın Kalıcı Silme ekranından silinmesi
kullanıcıda bekliyor.

---

## ✅ SON TAMAMLANAN — `YET-A` Yetki mimarisi A turu canlıda (2026-08-19)

C turunun ardından onaylanan **A turu** tamamlandı. Kullanıcının şikâyet ettiği "birden fazla,
çakışan yetki ekranı" sorunu kaynağından çözüldü.

### A1 — Rol tavanı artık FİRMA BAZLI (Migration 072)
`role_grant_limits` tablosunda firma kolonu **yoktu**: tablo platform geneliydi ve kaydetme
`DELETE FROM role_grant_limits;` ile tabloyu komple siliyordu → **bir firmadaki tek değişiklik bütün
firmaları etkiliyordu.** Artık `company_id` var, benzersizlik `(company_id, role_key, module_key)`.
**Veri kaybı yok:** mevcut ortak kısıtlar her firmaya kopyalanır (doğrulama kapısı:
`yeni == eski × firmaSayısı`, tutmazsa migration durur ve hiçbir şey yazılmaz).

### A2 — İki ekran tek ekran oldu
"Firma Yetki Kontrol" + "Rol Yetki Kontrol" → **Firma Yetki Paketi** · iki sekme (*Ekran paketi* /
*Rol tavanı*) · **tek firma seçicisi** ikisini birden yükler. Eski `/role-permissions` adresi artık
**404** (tek giriş noktası); `role_permissions` **modül** anahtarı korundu.

### A3 — Şablon kısayolu (kapsam bilinçli daraltıldı)
Yetkiler ekranına **"Şablondan doldur"** eklendi (iki ortam): şablon yalnız kutuları doldurur,
**sunucuya yazmaz**. **Yetki Şablonları ekranı KALDI** — şablonlar kalıcı nesnelerdir; tam yönetimini
açılır pencereye gömmek kullanımı ve riski kötüleştirirdi. Sonuç: **4 ekran → 3 ekran + kısayol.**

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı (SQLite) | **2134 geçti · 0 başarısız · 35 atlandı** |
| PostgreSQL (izole test DB) | **45/45** |
| Yeni testler | `RoleGrantCompanyTests` 4/4 · `PermissionScreenUxTests` 11/11 |
| Üretim yedeği | `pg_restore -l` ile doğrulandı — **77 tablo verisi** |
| Migration 072 (canlı) | `72 \| role_grant_limits_company` uygulandı · şema **71 → 72** |
| Canlı uç (iki firma) | `/api/role-permissions?companyId=…` → 200, doğru firmayı döndürüyor (77–117 ms) |
| Web ekranları | `/permissions` · `/company-permissions` · `/permission-templates` · `/users` · `/screen-visibility` → **200**; `/role-permissions` → **404** (birleşti) |
| Üretim iş verisi | malzeme **2459** · stok **663** · firma **3** — değişmedi (kullanıcı sayısı 8→9: yöneticinin kendi eklediği kullanıcı) |

Yayınlananlar: API **v163** · Web **v186** · Masaüstü **1.0.145** (85,8 MB · checksum `4665c44b…`).

### Sıradaki tek iş
Kullanıcı geri bildirimi. İki pasif firmanın (DEPOWISE · Oze Group) Kalıcı Silme ekranından silinmesi
hâlâ kullanıcıda bekliyor.

---

## ✅ SON TAMAMLANAN — `B` Giriş · makine · eşitleme düzeltmeleri canlıda (2026-08-19)

Kullanıcı makineleri sıfırlayıp sildi; ardından giriş bozuldu. **Sistemin bel kemiği** olduğu için
zincirin tamamı (giriş → makine kapısı → şube seçimi → eşitleme) incelendi.

### Kök neden — tek sebep, iki şikâyet
Giriş yolundaki ağ çağrılarının zaman aşımı **6 sn / 10 sn** idi. Sunucu veritabanı uykudan uyanırken
bu süre aşılıyor, istek düşünce uygulama kendini **çevrimdışı** sayıyordu.
**Kanıt:** makinenin yerel önbellek dosyaları **7 gündür güncellenmemişti** (`/api/machines/register`
bir haftadır hiç başarılı olmamış); aynı uç canlıda ölçüldüğünde **200 / 1,4 sn** dönüyor.
- Çevrimdışı sanılınca makine şubesi **önbellekten** okunuyor → silinen makinede şube boş →
  *"makine ilk kez kuruluyor, internet gerekli"* (**babanın giremediği durum**).
- Çevrimdışı sanılınca **şube seçim ekranı atlanıyor**, makinenin eski önbellek şubesine sessizce
  giriliyor (**TEST ŞANTİYE durumu**).

### Düzeltmeler (ADR-117)
| # | Düzeltme |
|---|---|
| B1 | Zaman aşımı **20/25 sn** + makine kaydında tek tekrar; yerel aynalama hatası artık "çevrimdışı" sayılmıyor |
| B2 | Çevrimdışı **sessiz oto-şube girişi kaldırıldı** — şube adımı daima gösterilir |
| B3 | Makine şubesi yokken **giriş kilitlenmiyor** (kendi şubesi bilinen kullanıcı girer) |
| B4 | **"Uyarıyı Temizle"** + her sıfırlama eşitleme defterini de sıfırlar |
| B5 | **Yetki tamamen süper adminin elinde** — yapısal kilitler süper admini bağlamaz |

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2139 geçti · 0 başarısız · 35 atlandı** |
| Yeni testler | `LoginMachineSyncTests` 5/5 |
| Güncellenen referanslar | RoleGrant · AuthPermission · RestrictedSuperAdmin · CompanyGrant · ScreenPlatformVisibility (alt rol kuralları aynen doğrulanıyor) |
| Canlı sağlık | API 200 · web 5 ekran 200 · indirme 200 |
| Şema | **72 → 72** (migration yok) |
| Üretim verisi | firma 3 · kullanıcı 9 · malzeme 2459 · stok 663 — değişmedi |

Yayınlananlar: API **v164** · Web **v187** · Masaüstü **1.0.146**.

### Açık kalan
Push kuyruğundaki **kalıcı hata sınıfı** (yinelenen anahtar / ebeveyni silinmiş satır) kaynağında
çözülmedi; bu tur kullanıcıyı kilitleyen tarafı çözdü. Ayrı iş olarak durmalı.

### Sıradaki tek iş
Baba 1.0.146'ya güncelleyip giriş denesin; sonucu bekliyoruz.

---

## ✅ SON TAMAMLANAN — `S` Kalıcı eşitleme hataları kaynağında çözüldü (2026-08-20)

B turunda "açık kalan" olarak bırakılan **son iş** tamamlandı. Artık açık iş yok.

### Sorun
Bir satır **hiçbir denemede** başarılı olamayacak olsa bile ("ebeveyni silinmiş çocuk satır",
"yinelenen doğal anahtar") "atlandı" sayılıyor, gönderim damgası ilerlemiyor ve 5 turdan sonra
temizlenemeyen kalıcı uyarı bırakılıyordu. Sahadaki 6 kayıt tam olarak buydu.

### Düzeltmeler (ADR-118)
| # | Düzeltme |
|---|---|
| S1a | **Öksüz çocuk ön kontrolü** — ebeveyni sunucuda olmayan satır veritabanına HİÇ gönderilmez |
| S1b | **Kalıcı/geçici ayrımı** — sunucu `permanentSkipped` döndürür |
| S1c | **İstemci kararı** — `Retryable = atlanan − kalıcı`; kuyruk kalıcı hatalara takılmaz |

### ⚠️ Yayından ÖNCE öz-denetimde bulunan hata
PostgreSQL kurtarma yolunda kalıcı sayaç sıfırlanmıyordu → **çift sayım** → istemci gerçekten
denenmesi gereken satırları **sessizce düşürebilirdi (veri kaybı)**. Diff incelemesinde yakalandı,
düzeltildi, **P4 testiyle** kilitlendi. Canlıya bu hâliyle çıkmadı.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2143 geçti · 0 başarısız · 35 atlandı** |
| Yeni testler | `SyncPermanentSkipTests` 4/4 (P1 öksüz · P2 geçerli veri · P3 istemci · P4 çift sayım) |
| Canlı sağlık | API 200 · web 6 ekran 200 · indirme 200 |
| Şema | **72 → 72** (migration yok) |
| Üretim verisi | firma 3 · kullanıcı 9 · malzeme 2459 · stok 663 · makine 2 — değişmedi |
| Sürüm uyumu | iki yönlü (eski istemci ↔ yeni sunucu ve tersi) davranış birebir aynı |

Yayınlananlar: API **v165** · Web **v188** · Masaüstü **1.0.147**.

### Sıradaki tek iş
Açık geliştirme işi yok. Kullanıcı tarafında bekleyen iki elle işlem:
iki pasif firmanın (DEPOWISE · Oze Group) **Kalıcı Silme** ekranından silinmesi ve
`depowise_test` veritabanı parolasının yenilenmesi.

---

## ✅ SON TAMAMLANAN — `MKN` Makine listeleme kilidi (2026-08-20)

**Kullanıcı bulgusu:** "webte makine yönetimi ekranında makineleri listeleyemiyorum — sunucudan mı
koddan mı?"

### Cevap: sunucu sağlam, sorun ekranda
| Ölçüm | Sonuç |
|---|---|
| `/api/machines` | **200 · 405 ms · 2 makine** |
| `/api/machines?companyId=…` | **200 · 2 makine** |

**Kök neden:** "Sorgula" düğmesi **şube seçilene kadar kapalıydı**. Firma seçilse bile düğme gri;
şube seçilse bile yalnız o şubenin makineleri geliyordu. Firmanın **9 şubesi** var, iki makine iki ayrı
şubede (DÜZCE · TEST ŞANTİYE) → makineyi bulmak pratikte imkânsızdı. "Kayıtsız Makineler" de boş
dönüyordu (ikisinin de şubesi atanmış).

### Düzeltme (ADR-119)
Şube **isteğe bağlı** oldu: firma seçilince Sorgula açılır, şube boşsa **firmanın tüm makineleri**
listelenir. **Sunucu değişmedi** — API `branchId` olmadan zaten bunu yapıyordu. Ayrıca ekranın ilk
yükleme çağrıları korumaya alındı (401/500'de sayfa çökmesin).

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2144 geçti · 0 başarısız · 35 atlandı** |
| Yeni test | `PermissionScreenUxTests.U10` |
| Canlı web | `/machines` · `/permissions` · `/company-permissions` · `/users` → **200** |
| Yayın | Web **v189** (yalnız web — masaüstü sürümü gerekmedi) |

### Sıradaki tek iş
Açık geliştirme işi yok.

---

## 🚀 SON TAMAMLANAN — `DEN-2` Yayın öncesi son denetim + yayın (2026-08-25/26)

Kullanıcı isteği: "yayın öncesi son kapsamlı denetim + onarım + test + release". Odak: veri güvenliği,
firma/şube izolasyonu, **tüm raporların doğruluğu**, stabilite, performans.

Tam rapor: [`docs/tests/Yayin_Oncesi_Denetim_2026-08-25.md`](../tests/Yayin_Oncesi_Denetim_2026-08-25.md)
Kararlar: [`ADR-125…129`](../DECISIONS.md)

### Düzeltilen gerçek sorunlar (her biri önce testle ÜRETİLDİ)
| ID | Önem | Ne bozuktu |
|---|---|---|
| **SEC-03** | **P1** | *Ayarlar* ekranını açabilen **herkes** sabit kodu girip **süper admin** yetkisine geçiyordu; yazdığı veri eşitlemeyle sunucuya gidiyordu |
| **RPR-06** | **P1** | Masaüstü raporlarında **bitiş gününün tamamı** düşüyordu (25.08 raporunda 25.08 kayıtları yok); web ile farklı sonuç |
| **RPR-04** | P2 | Rapor filtresi tek şubeye yetkili personele firmanın **tüm araç plakalarını** ve **personel adlarını** gösteriyordu |
| **RPR-07** | P2 | İki rapor menüsü **aynı ekranı** açıyordu (ayrım kozmetik) · web oturumu **çalışma şubesini taşımıyordu** (R33) |
| **SEC-04** | P2 | `GET /api/backups` firma parametresini doğrulamıyordu → başka firmanın makine/yedek adları listelenebiliyordu |

### Operasyon / Yönetici rapor ayrımı (kullanıcı talebi)
| | Operasyon Raporları | Yönetici Raporları |
|---|---|---|
| Route | `/reports` · `reports` | `/reports/manager` · `reports:manager` |
| Şube kapsamı | **yalnız çalışma şubesi** | izinli şubeler |
| Şube seçici | **YOK** | var (yetkisi olana) |
| Rapor listesi | yalnız `Standard` | tümü |
| Menü kapısı | modül yetkisi | `@admin` (artık iki platformda) |

Sunucu, istekteki çalışma şubesini **doğrular** (kapsam dışı → 403) ve yalnız **daraltır**; kapsam
genişletilemez. Ekran anahtarları değişmedi → kayıtlı menü düzeni ve platform görünürlüğü aynen çalışır.

⚠️ **Bilinçli davranış değişikliği:** yönetici olmayan kullanıcı 5 yönetici raporunu artık çalıştıramaz
(bu raporlar çalışma şubesini bilinçli olarak yok sayar → "yalnız giriş yapılan şube" kuralı orada
sağlanamaz). Web menüsü bunu zaten `@admin` ile ima ediyordu.

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Taban (tur başı) | 2165 geçti · 0 başarısız · 35 atlandı |
| Tam test — son koşu 1 | **2221 geçti · 0 başarısız · 35 atlandı** |
| Tam test — son koşu 2 (bağımsız) | **2221 · 0 · 35** → birebir aynı, flaky yok |
| PostgreSQL koşusu (ayrı test DB) | **45 geçti · 0 başarısız** |
| Release derlemesi | **0 hata** |
| Performans (30.000 hareket) | depo personelinin raporu **196 ms → 28 ms**, satır **30.000 → 3.000** |
| Yeni migration | **YOK** (ölçüm indeks gerekmediğini gösterdi) |

### ✅ YAYIN TAMAMLANDI (2026-08-26)
İlk denemede Fly.io faturası engellemişti; fatura kapandıktan sonra yayın eksiksiz yapıldı.

| Bileşen | Sürüm | Doğrulama |
|---|---|---|
| API | **v166** | `/health` 200 · **PG gerçek veri döndü** (boş SQLite'a düşmedi) |
| Web | **v190** | `/` `/reports` `/reports/manager` `/branches` `/stock/movements` `/developer` → 200 |
| Masaüstü | **1.0.149** | 270 dosya · 89.966.963 bayt · **checksum üç yerde de aynı** (`AE52DEC4…`) |
| Kurulum aracı | değişmedi | `/api/setup/download` 200 (Setup kodu bu turda değişmedi) |
| Şema | **72** (değişmedi) | yeni migration YOK → ek onay gerekmedi |
| Disk | %41,9 (408/973,7 MB) | 3 paket · R30 sınırının altında |

### Sıradaki tek iş
Kullanıcı kararı bekleyen: eksik raporlar (Muayene/Sigorta · Personel · boş `Purchasing` kategorisi) —
bunlar **yeni özellik**tir, bu turda bilinçli olarak kapsam dışı bırakıldı.

---

## 🔎 ÖNCEKİ — `DEN-2026-08-25` Uçtan uca denetim · onarım · test

Kullanıcı isteği: "projeyi uçtan uca denetle, gerçek sorunları bul, kök nedenlerini düzelt, çalışan
özellikleri koruyarak testlerle güvence altına al." **Üretime hiçbir yazma yapılmadı.**

Tam rapor: [`docs/tests/Uctan_Uca_Denetim_2026-08-25.md`](../tests/Uctan_Uca_Denetim_2026-08-25.md)
Kararlar: [`ADR-121…124`](../DECISIONS.md)

### Düzeltilen gerçek hatalar (her biri önce testle ÜRETİLDİ)
| ID | Önem | Ne bozuktu |
|---|---|---|
| TNT-01 | **P1** | Başka firmanın **araç şablonuna malzeme satırı yazılabiliyordu** (senkron firma kapısı bu tabloyu hiç kapsamıyordu) |
| TNT-02 | **P1** | Bağlantı tablolarının **karşı ucu** denetlenmiyordu → firma ötesi muadil/uyumlu araç bağı kurulabiliyordu |
| TNT-03 | P2 | Malzeme kartı, başka firmanın muadilini **kod + adıyla** gösterebiliyordu |
| RPR-01/02 | P2 | "Araç — Şablonlu / Şablon Dışı" raporları şube yetkisini uygulamıyordu (**plakalar** dahil) |
| RPR-03 | P2 | "Stok Sayım" raporu kapsamsızdı **ve** istek gövdesine yabancı depo yazılarak okunabiliyordu |
| WEB-01 | P2 | Stok Sayım / Dağıtım / Hareketler ekranları ilk yüklemede **Blazor devresini düşürebiliyordu** |
| SIF-02 | **P1** | Açık oturumda sıfırlama isteği algılanmıyor, **silinen veri sunucuya geri gönderiliyordu** |
| SEC-02 | P3 | `MeterHistory` oturumsuz + firma filtresizdi |

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı — koşu B | **2165 geçti · 0 başarısız · 35 atlandı** (11 dk 46 sn) |
| Tam test takımı — koşu C (bağımsız) | **2165 geçti · 0 başarısız · 35 atlandı** (11 dk 19 sn) → iki koşu **birebir aynı**, flaky yok |
| Taban (denetim öncesi) | 2146 geçti · 0 başarısız · 35 atlandı → **+19 yeni test** |
| PostgreSQL koşusu (ayrı test DB) | **45 geçti · 0 başarısız · 0 atlandı** — canlı lehçe kapsandı |
| Atlanan 35 test | **tamamı PostgreSQL kapılı** (boş test veritabanı onayı yoksa çalışmaz) — gizlenen/flaky test **yok** |
| Release derlemesi | **0 hata** |
| Gerçek arayüz (web) | Yerel API+web ayağa kaldırıldı, giriş yapıldı; düzeltilen 3 ekran **açıldı ve çalıştı**; `POST /api/reports/vehicles-nontemplate → 200` (9 ms); sunucu hatası yok |
| Üretim | API **200** · Web **200** (yalnız salt-okunur sağlık kontrolü) |

### Kullanıcı kararı bekleyenler
1. **SEC-03** — masaüstü geliştirici modu kodu kaynakta sabit ve **depo public**. Ayarlar ekranını
   açabilen herkes süper admin yetkisine geçebiliyor (yalnız masaüstü; sunucu etkilenmiyor).
2. **PRF-01** — Stok Hareketleri raporu tek seferde 50.000 satıra kadar dönebilir (bugün 663 hareket
   var, risk gelecekte). **İndeks çözüm değil** — ölçüldü, süre değişmedi.
3. **UPD-01** — güncelleme checksum kontrolü boş değerde atlanıyor (bugün ulaşılamaz).
4. **Eksik olabilecek raporlar** — Muayene/Sigorta ve Personel raporu yok; `Purchasing` kategorisi boş.
   Bunlar **yeni özellik**tir (kolon/filtre kararı gerekir), onarım değil.

### Sıradaki tek iş
Yukarıdaki 4 maddede kullanıcı kararı. Açık **onarım** işi yok.

---

## ✅ ÖNCEKİ — `ŞB-GİRİŞ` Giriş şube seçimi (2026-08-20)

**Kullanıcı isteği:** giriş ekranında şube kutusu kullanıcının **kendi şubesiyle** açılsın; isteyen
seçimi değiştirip makine şubesine geçebilsin; listede makine şubesi **simgeyle** belli olsun.

### Yapılanlar (ADR-120)
| # | Değişiklik |
|---|---|
| 1 | Varsayılan seçim = **kullanıcının kendi şubesi** (mevcut davranıştı; niyet koda yazıldı ve **L6** ile kilitlendi). Şubesi listede yoksa makine şubesine düşülür |
| 2 | Makine şubesi listede **🖥** ile işaretli (`LoginBranch.IsMachineBranch`, yalnız görüntü) |
| 3 | İşaretleme **kapsam kırpmasından SONRA** çalışır — **L7** bu sırayı kilitler |
| 4 | Şube kutusunun altında tek satır açıklama (yalnız listede makine şubesi varsa) |

### Bozulmayanlar (bilinçli)
Şube kimliği · yetki/kapsam kırpması · şube şifresi kuralı · "makinenin şubesi → şifre gerekmez" ·
süper admin akışı **aynen korundu**. **Sunucuya dokunulmadı.**

### Kanıtlar
| Doğrulama | Sonuç |
|---|---|
| Tam test takımı | **2146 geçti · 0 başarısız · 35 atlandı** |
| Yeni testler | `LoginMachineSyncTests` L6 · L7 |
| Yayın | Masaüstü **1.0.148** · indirme ucu 200 (yalnız masaüstü — API/web değişmedi) |

### Sıradaki tek iş
Açık geliştirme işi yok.
