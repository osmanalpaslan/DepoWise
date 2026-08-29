# ARA İŞ 3 — YAYIN ÖNCESİ DOĞRULAMA RAPORU

> Tarih: **2026-08-29** · Karar temeli: **ADR-184** (PK-TAR-01=A · 02=A · 03=A · 04=A · 05=A · 06=B · 07=A)
> Durum: **YAYIN ÖNCESİ DOĞRULAMA TAMAM · ⏸️ "YAYINLA" ONAYI BEKLİYOR**
> ⚠️ **PRODUCTION'A HİÇBİR BAĞLANTI YAPILMADI** (SELECT dahil) · **MIGRATION YOK** · **DEPLOY YOK**

---

## 1. İşin kapsamı (değişmedi)

Takvim tarihinin **yerel saat dilimiyle** (TR = UTC+3) sayıya çevrilmesi yüzünden kaydın **bir gün
önceye** düşmesi hatası — **yalnız ileriye dönük** düzeltildi.

**Bu yayına DAHİL DEĞİL:** geçmiş kayıtların düzeltilmesi (PK-TAR-02=A) · canlı veri ölçümü
(PK-TAR-06=B) · FIN-B1/Migration082 · Custom Rapor · Ekip+Hiyerarşi+Onay · Mobil.

---

## 2. Gerçek commit durumu

| Alan | Değer |
|---|---|
| Yayın adayı commit | **`ab0d0d4`** |
| Bir önceki (kararlar) | `ae3b6d0` (ADR-184) |
| Dal | `master`, origin ile **senkron** |
| Çalışma ağacı | **Temiz** (takip edilen kirli dosya: 0) |
| Değişen dosya | **29** (+398 / −61) |
| Takip dışı kullanıcı dosyaları | `docs/SECURITY_CREDENTIAL_ROTATION_PLAN.md`, `docs/kilavuzlar/` — **dokunulmadı, commit edilmedi** |

---

## 3. Gerçek değişen dosyalar

**Yeni (2):**
- `src/DepoWise.Application/Common/IsGunuTarihi.cs` — merkezî dönüşüm kaynağı
- `tests/DepoWise.Tests/TarihKaymasiTests.cs` — 14 test

**Değişen kod (25):**
- `src/DepoWise.Application/Reports/ReportDateRange.cs` (merkeze devir; davranış aynı)
- 23 masaüstü ViewModel (aşağıda §4/§5)
- `src/DepoWise.Web/Components/Pages/Stock.razor`

**Değişen test (1):** `tests/DepoWise.Tests/YakitTarihGunTests.cs` (YKT3 yeni mimariye uyarlandı — gevşetme yok)

**Değişen belge (4):** `KNOWN_ISSUES.md` · `ARA_IS_3_00_ANALIZ.md` · `CURRENT_PHASE.md` · `MASTER_ROADMAP.md`

---

## 4. 20 noktanın doğrulama durumu

### Masaüstü — 19 nokta (hatalıydı → düzeltildi)

| # | Ekran | Nokta | Durum |
|---|---|---|---|
| 1–3 | Stok Girişi | `docDate` ×3 | ✅ |
| 4 | Stok Sayımı | belge tarihi | ✅ |
| 5 | Stok Dağıtım | belge tarihi | ✅ |
| 6–7 | Faturalar | fatura tarihi + vade | ✅ |
| 8–9 | Finans | işlem tarihi + transfer tarihi | ✅ |
| 10–11 | Muayene | sonraki muayene + son muayene | ✅ |
| 12 | Bakım | yapılış tarihi | ✅ |
| 13–15 | Günlük Faaliyet | ×3 | ✅ |
| 16–17 | Cari | giriş tarihi + vade | ✅ |
| 18 | Tahsilat | işlem tarihi | ✅ |
| 19 | Talepler | talep tarihi | ✅ |

### Web — 1 nokta

| # | Dosya | Durum |
|---|---|---|
| 20 | `Stock.razor` → `FieldChecks.ToUnixMs` | ✅ |

**Kaynak seviyesi kanıt:** masaüstü ViewModel'lerde tek argümanlı (yerel offset) `new DateTimeOffset(x)
.ToUnixTimeMilliseconds()` sayısı **0**; webde **0**. Kalan tüm dönüşümler ya `TimeSpan.Zero`/`DateTimeKind.Utc`
sabitli (doğru) ya da `DateTimeOffset.UtcNow` (gerçek zaman damgası — dokunulmaması gereken).

---

## 5. Regresyon: doğru çalışanlar bozulmadı

**Masaüstü — merkeze bağlanan 8 doğru ekran** (Zimmet · Duyuru · Evrak · Proje · İş Emri · Satınalma ·
Takvim · Yakıt): ürettikleri değer **birebir aynı**; yalnız kural kopyası kaldırıldı. `CostCenters`
kendi gün-sonu matematiğini korudu (yalnız açıklama notu eklendi).

**Web — doğru çalışan 10 nokta korundu:** `Audit` ×2 · `Daily` · `Finance` · `Inspection` · `Invoices` ·
`Maintenance` · `Parties` ×2 · `Payments` · `Reports` ×2 · `Requests` · `StockChangeLog` ×2 ·
`StockMovements` ×2 — hepsi mevcut `FieldChecks.ToUnixMs` / UTC desenini sürdürüyor. TAR11 ve TAR12
testleri bunu kaynak seviyesinde kilitliyor.

**Rapor tarih aralığı:** `ReportDateRange.ToMs` merkeze devredildi; çıktı değeri değişmedi, RPR-06
parite kilidi yerinde ve geçiyor.

---

## 6. Zaman damgaları etkilenmedi (PK-TAR-04=A)

`created_at`, `updated_at` ve denetim (audit) kayıtları **gerçek an** olarak yazılmaya devam ediyor;
takvim günü kuralı bunlara uygulanmadı. **TAR13** testi bu ayrımı kilitliyor.

---

## 7. Sözleşme ve etkilenmeyen katmanlar

`ab0d0d4` içinde **`DepoWise.Api/`, `DepoWise.Domain/`, `DepoWise.Infrastructure/` altında hiçbir dosya
yok** (0). Dolayısıyla:

- **API/DB sözleşmesi:** değişmedi — uçlar, alan adları, tipler aynı; tarih alanları hâlâ Unix ms.
- **Senkron protokolü:** dokunulmadı.
- **Yetki / tenant / BranchAccess / export:** dokunulmadı.
- **Migration runner ve kataloğu:** dokunulmadı.

---

## 8. Eski istemci uyumluluğu (PK-TAR-05=A)

Sözleşme değişmediği için **eski masaüstü sürümler (≤1.0.162) bozulmaz**: aynı biçimde sayı gönderir,
sunucu aynı şekilde kabul eder. Ancak güncellenene kadar **eski (kaymalı) değeri** yazmayı sürdürürler.
Bu, kararınız gereği kabul edilen ve `KNOWN_ISSUES.md`'de kayıtlı bilinen durumdur.

---

## 9. Migration durumu

| Kontrol | Sonuç |
|---|---|
| Katalog azamisi | **81** (Migration077…081; 082 yok) |
| `Migration082` kod dosyası | **YOK** — 6 metin geçişi yalnız test **yorum satırlarında** (ADR-179/180 geçmişi) |
| Bu işte açılan migration | **YOK** |
| Canlı şema | **81'de kalır** |

---

## 10. Test ve derleme sonuçları

| Doğrulama | Bu tur | Önceki rapor | Durum |
|---|---|---|---|
| Tam test süiti | **3026 başarılı / 0 başarısız / 39 atlanan** (22 dk 31 sn) | 3026 / 0 / 39 atlanan | ✅ birebir aynı |
| İzole PostgreSQL | **52 / 52**, 0 atlanan | 52 / 52 | ✅ aynı |
| API Release | **0 hata** | 0 hata | ✅ |
| Web Release | **0 hata** | 0 hata | ✅ |
| Masaüstü Release | **0 hata** | 0 hata | ✅ |

Test ortamı: geçici SQLite + **izole yerel PostgreSQL** (`127.0.0.1:5544`, `depowise_test`).
PostgresTestGuard çift kilidi **gevşetilmedi**; 0 atlanan sonucu testlerin gerçekten koştuğunu gösterir.
Hiçbir test gevşetilmedi, hiçbir başarısızlık gizlenmedi.

### 10.1 Not — süit toplamı neden değişmedi

`TarihKaymasiTests`'in 14 testi zaten FAZ 3 turunda sayıma girmişti; yayın öncesi tur aynı commit
(`ab0d0d4`) üzerinde koştuğu için toplam **3065** ve geçen **3026** birebir tekrar etti. 39 atlanan,
ayrı ortam gerektiren PostgreSQL sınıflarıdır ve **§10'daki izole PG turunda 52/52 olarak koşmuştur** —
yani atlanan testlerin hiçbiri doğrulama dışında kalmamıştır.

---

## 11. Tarih sınır kapsamı

`TarihKaymasiTests` (14 test) şunları kilitliyor: 6 farklı saat dilimi (UTC−8…UTC+13) · `DateTimeKind`
bağımsızlığı · **gün içi saatler, kritik 00:00–03:00 aralığı dâhil (TR UTC+3'te kaymanın göründüğü
aralık)** · ay/yıl/**artık yıl** sınırları · gün sonu sınırı · null · **eski hatalı dönüşümün bir gün
erkene düştüğünün belgelenmesi** · yazma↔okuma sınır tutarlılığı · kaynak seviyesi kilitler
(masaüstü 11 ekran + web).

---

## 12. Rollback (geri alma) etkisi

Değişiklik **yalnız koddur**; şema veya veri dönüşümü içermez. Geri alma = `ab0d0d4` revert + önceki
masaüstü sürümünün yayını. **Veri kalıntısı bırakmaz, geri dönüş veri kaybı doğurmaz.** Geri alınırsa
sistem yalnız eski (kaymalı) yazma davranışına döner.

---

## 13. Riskler ve açık noktalar

| Konu | Değerlendirme |
|---|---|
| Geçmiş kayıtlar hâlâ kaymış | **Bilinçli** (PK-TAR-02=A) — ayrı iş; bu yayına dâhil değil |
| Eski istemciler kaymalı yazmayı sürdürür | **Bilinçli** (PK-TAR-05=A) — güncelleme yayılana kadar |
| Canlı etki ölçülmedi | **Bilinçli** (PK-TAR-06=B) — production ölçümü yapılmayacak |
| Kapsam dışı gözlem | `AuditLog`, `StockChangeLog`, `StockMovements` masaüstü **okuma filtreleri** doğru çalışıyor ama merkeze bağlanmadı. Kapsam 20 **yazım** noktasıydı; bu görevde değiştirilmedi. |

**Engelleyici risk: YOK.**

---

## 14. Yayın paketi kapsamı

| Bileşen | Kapsam |
|---|---|
| **Masaüstü** | 1.0.162 → **1.0.163** (19 nokta düzeltmesi) |
| **Web** | `Stock.razor` düzeltmesi → `fly.web.toml` |
| **API** | Ortak kod (`IsGunuTarihi`, `ReportDateRange`) → `fly.toml` |
| **Migration** | **YOK** — canlı şema **81** kalır |
| **Veri dönüşümü** | **YOK** |

Sürüm, yayın betiğine komut satırından verilir (`scripts/publish_release.mjs <zip> <sürüm>`); depoda
sabit sürüm dosyası yoktur. Son yayınlanan sürüm `CURRENT_PHASE.md`'ye göre **1.0.162**'dir.

---

## 15. Sonraki adım

**Kullanıcının `YAYINLA` onayı.** Onay gelmeden API/Web dağıtımı ve masaüstü paketi yayını yapılmaz.
