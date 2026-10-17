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
