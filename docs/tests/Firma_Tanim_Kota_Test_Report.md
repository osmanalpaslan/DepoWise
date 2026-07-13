# Firma Tanım / Kota — Test Raporu (Adım 3)

> Kapsam: yalnız Firma Tanım ekranı + kullanıcı kotası enforcement (§7.1). Tarih: **2026-07-12**. Motor: Opus 4.8.

## 0. Yapılanlar
- **Ayrı kotalar:** `max_admins` (admin) ve `max_users` (NORMAL/personel) AYRI girilir. Eski **%20 admin kuralı kaldırıldı**.
- **Makine kotası** (`machine_quota`, mevcut kolon) Firma Tanım ekranına eklendi (web + masaüstü).
- Migration038 (`max_admins` kolonu). Kota enforcement UserService.CreateUser + SetRoles'ta yeni modele bağlandı.
- QuotaMonitor: admin (count/max_admins) + personel (count/max_users) ayrı gösterilir (metin alanları; UI değişmeden uyum sağladı).

## 1. Otomatik testler
```
Başarılı! — Başarısız: 0, Başarılı: 298, Atlanan: 0, Toplam: 298, Süre: 30 s
```
- **298/298 yeşil** (294 → 298, +4). Solution build **0 hata**.
- Yeni: `CompanyQuotaTests` — makine kotası saklanır/listelenir · admin kotası max_admins · normal kotası max_users
  (admin sayılmaz) · %20 yok (max_admins=0 → sınırsız).

## 2. Senaryolar (§7.4/§7.7)
| Senaryo | Sonuç |
|---|---|
| max_admins=1 → 2. admin | ✅ reddedilir ("Admin kotası") |
| max_users=1 → admin + 1 personel eklenebilir, 2. personel reddedilir | ✅ (admin normal kotaya sayılmaz) |
| max_admins=0 → sınırsız admin | ✅ (eski %20 kalktı) |
| makine kotası create/update/list round-trip | ✅ |
| süper/kısıtlı-süper admin normal kotaya sayılmaz | ✅ (sorgu rol dışlar) |

## 3. Coverage
| Alan | Durum |
|---|---|
| Doğrulamalar (numeric alanlar 0=∞) | ✅ |
| Database (Migration038, idempotent kolon) | ✅ |
| Yetki (Firma Tanım süper-admin-only) | ✅ (Adım 2) |
| UI (web + masaüstü form + liste) | ✅ build; canlı tık deploy sonrası |
| Sync (masaüstü offline create/update payload) | ✅ maxAdmins/machineQuota eklendi |

## 4. Riskler / notlar
- **Deploy bekliyor** (web + API): şema Migration035→038. Kullanıcı kararı = sonraki web işiyle birlikte.
- `max_users` semantiği "toplam"dan "normal/personel"e değişti; mevcut firmalarda max_users>0 varsa artık yalnız
  personeli kapsar (adminler max_admins=0 → sınırsız). Genç sistemde etki minimal.
