# PAKET 1 — UYGULAMA RAPORU

- **Tarih:** 2026-08-09
- **Kapsam:** KD-1 · T-1…T-6 · Y-1 · Y-2 · PostgreSQL flaky test · API çok-firmalı test paketi
- **Migration:** ❌ yok · **Deploy:** ❌ yapılmadı · **Canlı veriye yazma:** ❌ yok
- Plan: `docs/PAKET1_UYGULAMA_PLANI.md`

---

## 1. YÖNTEM — önce kırmızı, sonra yeşil

Düzeltmelerden **önce** gerçek HTTP hattı üzerinden testler yazıldı ve açıkların **hepsi kanıtlandı**:

```
ÖNCE  (düzeltme yapılmadan):  Başarısız: 7,  Başarılı: 8   (ApiMultiCompanyTests)
SONRA (düzeltmelerden sonra): Başarısız: 0,  Başarılı: 27  (API 15 + servis 12)
```

Kırmızı olan 7 test: T-1, T-3, T-4, T-5, T-6, T-2b, Y-2.

⚠️ **Ara bulgu:** İlk koşuda T-3 ve T-5 **yanlış yeşil** çıktı — tohum veri o kod yollarını
tetiklemiyordu (tanıma araç bağlı değildi, operasyon geçmişi yoktu). Tohum güçlendirildi;
ikisi de kırmızıya döndü ve açıklar böylece gerçekten kanıtlandı.

---

## 2. FİRMA İZOLASYONU — madde madde

### T-1 · Stok bakiyesi
| | |
|---|---|
| Önceki açık | `GET /api/stock/balance/{materialId}` — başka firmanın malzeme id'siyle stok miktarı okunuyordu |
| Kök neden | `StockService.GetBalance(string materialId)` — **`SessionContext` parametresi bile yoktu**; sorgu yalnız `WHERE material_id=@m` |
| Düzeltme | İmza `GetBalance(SessionContext s, string materialId)`; sorguya `AND company_id=@c` |
| Katman | **Servis** (sorgu düzeyi) |
| Neden ek sorgu yok | Bu metot `/api/materials` içinde **satır başına** çağrılıyor (N+1). `EnsureMaterialOwned` eklemek yükü ikiye katlardı → aynı sorguda filtre tercih edildi (planın kararı korundu) |
| Test | `S2_T1_...OKUNAMAZ` (HTTP) · `T1_Baska_firmanin_stok_bakiyesi_okunamaz` (servis) · `S1_...gorebilir` (regresyon) |
| Sonuç | ✅ Yabancı firma **0** görür; kendi firması doğru bakiyeyi görür |

### T-2 · Bakım tanımı ↔ araç (yazma)
| | |
|---|---|
| Önceki açık | **T-2a:** `SetVehicles` yabancı tanımı değiştirebiliyordu (uçta `Update` önce patladığı için *bu uçtan* sömürülemiyordu → latent). **T-2b:** kullanıcı KENDİ tanımına **başka firmanın aracını** bağlayabiliyordu (gerçek, sömürülebilir) |
| Kök neden | `SetVehicles` yalnız `AccessControl.Require(Edit)` yapıyordu; ne tanımın ne de araçların firması doğrulanıyordu |
| Düzeltme | `EnsureDefinitionOwned` (tanım) + her `vehicleId` için `EnsureVehicleOwned` |
| Katman | **Servis** — masaüstü (`MaintenanceViewModel.cs:168`) bu metodu **doğrudan** çağırıyor; API'de kapatmak masaüstünü korumazdı |
| Test | `S3_T2_...GUNCELLENEMEZ`, `S5_T2b_...BAGLANAMAZ` (HTTP) · `T2a_...`, `T2b_...`, `Kendi_aracini_baglayabilir` (servis) |
| Sonuç | ✅ Çapraz-firma referans oluşturulamıyor; kendi aracını bağlamak çalışıyor |

### Y-2 · Aynı açık `Create` yolunda
| | |
|---|---|
| Önceki açık | Yeni bakım tanımı **oluştururken** de yabancı araç bağlanabiliyordu |
| Düzeltme | `Create` döngüsüne aynı `EnsureVehicleOwned` |
| Katman | **Servis** |
| Test | `S5_Y2_...BAGLANAMAZ` (HTTP) · `Y2_Create_ile_...` (servis) |
| Sonuç | ✅ |

### T-3 · Bakım tanımının araç listesi
| | |
|---|---|
| Önceki açık | `GET /api/maintenance/definitions/{id}/vehicles` — yabancı tanımın araç id'leri okunuyordu |
| Kök neden | `GetVehicleIds` üst tanımın firmasını doğrulamıyordu (`maintenance_definition_vehicles`'ta firma kolonu yok) |
| Düzeltme | `EnsureDefinitionOwned` |
| Katman | **Servis** |
| Test | `S2_T3_...OKUNAMAZ` (HTTP) · `T3_...okunamaz` + `T3_Kendi_...okuyabilir` (servis) |
| Sonuç | ✅ |

### T-4 · Talep onay geçmişi
| | |
|---|---|
| Önceki açık | `GET /api/requests/{id}/history` — yabancı talebin durum geçmişi okunuyordu |
| Kök neden | `RequestService.GetHistory(string requestId)` — **`SessionContext` parametresi yoktu** |
| Düzeltme | İmzaya session + `LoadStatus(s, requestId)` tenant guard'ı (aynı sınıftaki `GetItems` deseni) |
| Katman | **Servis** |
| Test | `S2_T4_...OKUNAMAZ` · `S1_...gorebilir` (regresyon) |
| Sonuç | ✅ |

### T-5 · Operasyon geçmişi
| | |
|---|---|
| Önceki açık | `GET /api/request-ops/{id}/history` — yabancı talebin operasyon geçmişi (kim/ne zaman/gerekçe/şube) okunuyordu |
| Kök neden | Yetki kontrolü vardı ama **tenant** kontrolü yoktu |
| Düzeltme | `LoadOperationStatus` (zaten firma filtreli) + `operation_status` NULL olabildiği için ek `RequestBelongsToCompany` kontrolü |
| Katman | **Servis** |
| Test | `S2_T5_...OKUNAMAZ` |
| Sonuç | ✅ |

### T-6 · Kullanıcı rolleri
| | |
|---|---|
| Önceki açık | `GET /api/users/{id}/roles` — yabancı firmanın kullanıcı rolleri okunuyordu |
| Kök neden | `GetRoleKeys(actor, userId)` — `actor` alınıyor ama **hiç kullanılmıyordu**; yetki kontrolü de yoktu |
| Düzeltme | `AccessControl.Require(actor,"users",View)` + `EnsureUserOwned` (PermissionService'teki desen) |
| Katman | **Servis** |
| Test | `S2_T6_...OKUNAMAZ` (HTTP) · `T6_...okunamaz` + `T6_Kendi_...okuyabilir` (servis) |
| Sonuç | ✅ |

### Y-1 · Muadil malzeme silme (yazma)
| | |
|---|---|
| Önceki açık | Başka firmanın iki malzemesi arasındaki muadil ilişkisi **silinebiliyordu** |
| Kök neden | `RemoveEquivalent` firma doğrulaması yapmıyordu; oysa `AddEquivalent` yapıyordu → **asimetri** |
| Düzeltme | Her iki malzeme için `EnsureOwned` (Add ile aynı kontrol) |
| Katman | **Servis** — **API ucu YOK**, yalnız masaüstünden çağrılıyor → HTTP testi imkânsız, **servis testi zorunlu** |
| Test | `Y1_Baska_firmanin_muadil_iliskisi_SILINEMEZ` + `Y1_Kendi_...SILEBILIR` |
| Sonuç | ✅ |

---

## 3. KD-1 — `rowid` PostgreSQL uyumsuzluğu

| Uç | Önceki hata | Sonuç |
|---|---|---|
| `GET /api/stock` | **500** · `42703: column sm.rowid does not exist` | ✅ |
| `GET /api/stock/movements` | **500** · aynı | ✅ |
| `GET /api/materials/{id}/movements` | **500** · aynı (**planlama sırasında bulundu — ilk raporda yoktu**) | ✅ |

**Uyumsuzluk:** `rowid` SQLite'a özel bir sütundur; PostgreSQL'de yoktur. `ctid` fiziksel konumdur ve
VACUUM ile değişir → ikame olarak kullanılamaz.

**Çözüm:** `SqlDialect.RowTieBreaker(conn, alias)`
- **SQLite:** `sm.rowid` → bugünkü davranış (ekleme sırası) **birebir korundu**
- **PostgreSQL:** `sm.id` (birincil anahtar, TEXT) → ekleme sırası vermez ama **deterministik**

**Migration gerekmedi** — yalnız sorgu metni değişti.

**Test:** `StockMovementOrderingTests` (SQLite, 4 test) + `PostgresStockMovementOrderingTests`
(PostgreSQL, 5 test) — üç sorgunun da patlamadığı ve sıralamanın **iki koşuda aynı** olduğu doğrulanır.

---

## 4. POSTGRESQL FLAKY TEST

**Kök neden (kanıtlanmış):** `PostgresTestGuardTests.WithEnv` süreç-geneli ortam değişkenlerini geçici
olarak değiştiriyor; bu sınıfta `[Collection]` yoktu → xUnit **paralel** çalıştırıyordu → aynı anda
`SkipUnlessSafe()` çağıran `PostgresConnectionTests` guard'ı bozuk görüp **atlanıyordu**.
trx raporu atlanan testi kesinleştirmişti: `PostgresConnectionTests.PostgreSQL_Sunucusuna_Baglanip_Surum_Okunabiliyor`.

**Düzeltme:** her iki sınıfa `[Collection("PostgresSchema")]` — env'i bozan sınıf artık diğer PG
testleriyle **seri** çalışıyor. Ürün kodu etkilenmedi (yalnız test öznitelikleri).

---

## 4b. TEST SONUÇLARI (gerçek koşular)

| Küme | Sonuç |
|---|---|
| **SQLite — tüm takım** | **866 geçti · 0 başarısız · 20 atlandı** (2 dk 48 sn) |
| **PostgreSQL — tüm PG testleri** | **35 geçti · 0 başarısız · 0 ATLANDI** (6 dk 41 sn) |
| **API testleri** (gerçek HTTP hattı) | **15 / 15** |
| **Servis testleri** (izolasyon) | **12 / 12** |
| **KD-1 sıralama** — SQLite | 4 / 4 |
| **KD-1 sıralama** — PostgreSQL | 5 / 5 |
| **Derleme** | **0 hata** |

**Baseline karşılaştırması**
- SQLite: 839 → **866** (+27 yeni test) · başarısız **0 → 0** ✅
- PostgreSQL: 30 (**1 atlandı**) → **35 (0 atlandı)** ✅
  Beklenen `30/30` idi; **35/35** oldu — çünkü KD-1 için 5 yeni PostgreSQL testi eklendi.
  Kritik olan: **atlanan test 0** → flaky sorunu kalıcı olarak çözüldü.
- 20 atlanan SQLite testi: PostgreSQL testleri (ortam değişkeni verilmediğinde bilinçli atlanır) — değişmedi.
- Hiçbir test "yeşil görünsün diye" değiştirilmedi: mevcut testlerdeki tek değişiklik **imza uyumu**
  (`GetBalance`/`GetHistory` çağrılarına oturum eklendi); **beklenen değerlerin hiçbiri değişmedi**.

---

## 5. DEĞİŞMEYEN BULGULAR (kapsam dışı, uygulanmadı)

| Kod | Bulgu | Neden dışarıda | Sınıf |
|---|---|---|---|
| **Y-3** | `VehicleTemplateService.GetMaterials` — session/firma yok | **Latent**: API ucu yok, yalnız masaüstünden (yerel tek-firma DB) çağrılıyor | P2 |
| **Y-4** | `OpeningStockService.GetBalance` — firma filtresi yok | **Ölü kod** — hiçbir yerden çağrılmıyor | P3 |
| **Y-5** | `BranchRepository.SoftDelete` — firma bağlamı yok | **Ölü kod** — çağıranı yok | P3 |
| **Y-6** | `/api/materials` her malzeme için ayrı `GetBalance` → **N+1** | Güvenlik değil, performans | P3 |
| **D-1** | `CLAUDE.md` satır 53-54 "sunucu SQLite" diyor (gerçekte PostgreSQL) | Ayrı küçük doküman işi | P1 |

Ayrıca **M-S1b** (`request_status_history` + 5 tablo firma kolonu) ve **M-S1d** (eşitlemede üst kayıt
firma doğrulaması) hâlâ açık — migration gerektirdikleri için ayrı ve onaylı iş olmalı.

---

## 6. UYGULAMA SIRASINDA KEŞFEDİLEN

1. **`StockChangeLogService.cs:57`** — `GetBalance` çağrısı planın taramasında görünmemişti;
   **derleyici yakaladı** (imza değişikliğinin bilinçli faydası). Düzeltildi.
2. **KD-1 üçüncü uç** (`/api/materials/{id}/movements`) planlama aşamasında bulunmuştu ve doğrulandı.
3. **`MaterialService.EnsureOwned`** imzası `DbTransaction` (nullable değil) idi; `RemoveEquivalent`
   transaction dışında çağırdığı için `DbTransaction?` yapıldı → benim eklediğim 2 CS8625 uyarısı giderildi.
   (Dosyada **önceden var olan** bir CS8625 uyarısı `StockService.cs:288` satırında duruyor — bağlam
   satırı, bu pakette dokunulmadı.)
