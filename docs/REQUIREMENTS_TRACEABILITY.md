# REQUIREMENTS TRACEABILITY

| ID | Gereksinim | Faz | Durum | Kod/Test Kanıtı |
|---|---|---:|---|---|
| REQ-MOD-01 | Ana Ekran ve Uyarı Merkezi | 12 | Bekliyor |  |
| REQ-MOD-02 | Firma, Şube ve Şantiye | 05 | Bekliyor |  |
| REQ-MOD-03 | Kullanıcı, Rol ve Yetki | 03 | Bekliyor |  |
| REQ-MOD-04 | Tanımlar ve Alan Ayarları | 04 | Bekliyor |  |
| REQ-MOD-05 | Malzemeler | 06 | Bekliyor |  |
| REQ-MOD-06 | Stok İşlemleri | 07 | Bekliyor |  |
| REQ-MOD-07 | Araçlar ve Araç Şablonları | 08 | Bekliyor |  |
| REQ-MOD-08 | Bakım Takibi | 09 | Bekliyor |  |
| REQ-MOD-09 | Muayene, Sigorta ve Kalibrasyon | 09 | Bekliyor |  |
| REQ-MOD-10 | Yakıt Sarfiyatı | 10 | Bekliyor |  |
| REQ-MOD-11 | Günlük Faaliyet | 10 | Bekliyor |  |
| REQ-MOD-12 | Malzeme Talep ve Onay | 11 | Bekliyor |  |
| REQ-MOD-13 | Personel | 05 | Bekliyor |  |
| REQ-MOD-14 | Raporlar | 12 | Bekliyor |  |
| REQ-MOD-15 | Import/Export | 12 | Bekliyor |  |
| REQ-MOD-16 | Dosya ve Fotoğraf | 13 | Bekliyor |  |
| REQ-MOD-17 | Sistem Logu, Audit ve Çöp Kutusu | 13 | Bekliyor |  |
| REQ-MOD-18 | Yedekleme | 13 | Bekliyor |  |
| REQ-MOD-19 | Setup ve Güncelleme | 15 | Bekliyor |  |
| REQ-MOD-20 | Offline Senkronizasyon | 14 | Bekliyor |  |

**Faz 00 (2026-06-26):** REQ-MOD-01..20 → faz eşlemesi V6 analiz §12 ile doğrulandı; eksik/çelişkili gereksinim bulunmadı. Tüm satırlar "Bekliyor" (kod henüz yok). Kanıt sütunları ilgili faz tamamlandıkça doldurulacak.

**Faz 02 (2026-06-26):** Veri temeli — tüm operasyonel modüllerin (REQ-MOD-02/03/04/16/17/18/20) ön koşulu kuruldu:
- Çekirdek şema + migration: `src/DepoWise.Infrastructure/Database/Migrations/*` (companies, branches, users, roles, permissions, audit_logs, file_records, sync_*); PG: `apps/web/src/db/schema.ts` + `apps/web/drizzle/0000_core_schema.sql`.
- Tenant/soft-delete/keyset/audit kuralları: `src/DepoWise.Application/Common/{Tenant,Cursor,Audit}.cs`, `Infrastructure/Database/{TenantSql,AuditWriter,BranchRepository}.cs`; test `tests/DepoWise.Tests/DatabaseFoundationTests.cs`.
- REQ-MOD-17 (audit/çöp kutusu ön koşulu): audit_logs + soft-delete altyapısı hazır (UI Faz 13).

**Faz 01 (2026-06-26):** Modül gereksinimi tamamlanmadı; iskelet + ortak sözleşmeler kuruldu. Tüm modülleri besleyen ortak altyapı kanıtları:
- Hata modeli / pagination / zaman / correlation: `src/DepoWise.Application/Common/*`, `apps/web/src/lib/contracts.ts`, `apps/web/docs/openapi.yaml`; test `tests/DepoWise.Tests/SkeletonSmokeTests.cs`.
- Yerel DB temeli (REQ-MOD-18/20 ön koşulu): `src/DepoWise.Infrastructure/Database/*`.
- Fail-closed config (REQ-MOD-03 ön koşulu): `apps/web/src/lib/config.ts`, `/api/v1/health`.

Her faz sonunda ilgili satırlar güncellenir.
