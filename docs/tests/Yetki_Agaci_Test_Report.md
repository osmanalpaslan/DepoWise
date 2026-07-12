# Yetki Ağacı — Test Raporu (Adım 1: Ağaç Temeli)

> Kapsam: **yalnız değiştirilen ekranlar** — Yetkiler, Yetki Şablonları, Talep (Form/Onaylama) + yetki
> çekirdeği. §7.1 gereği başka ekrana dokunulmadı. Tarih: **2026-07-12**. Motor: Opus 4.8.

## 0. Yapılan değişiklikler (Adım 1)
1. **Sync yetkisi kaldırıldı** — ölü madde (hiçbir gate değildi; eşitleme cihaz-token bazlı).
2. **Talep → iki ayrı yetki:** `requests` = **Talep Formu**, yeni `request_approval` = **Talep Onaylama**.
   Onay/ret artık `request_approval` Edit ister (form Edit'i yetmez). `btn-approve` özel butonu kaldırıldı.
3. **Migration035:** mevcut `btn-approve` yetkileri `request_approval` (view+edit) modülüne taşındı, eski buton izni temizlendi (idempotent).
4. **Özel işlem yetkileri ağacın içinde:** `PermMatrix` özel butonları tek-onaylı ağaç satırı olarak gösteriyor (web); ayrı kutu kaldırıldı. Masaüstü zaten aynı panelde gösteriyordu.

## 1. Otomatik testler
```
Başarılı! — Başarısız: 0, Başarılı: 283, Atlanan: 0, Toplam: 283, Süre: 33 s
```
- **283/283 yeşil** (281 → 283, +2 yeni). Solution build **0 hata**.
- Yeni/güncellenen testler:
  - `RequestTests.TalepFormu_ve_Onaylama_AyriYetki` — form-yetkisi onaylayamaz; onay-yetkisi onaylar (form yazma gerekmez).
  - `Migration035Tests.BtnApprove_RequestApprovalModulune_Tasinir_Idempotent` — taşıma + temizlik + idempotent.
  - `AuthPermissionTests` — deny-by-default örnekleri `request_approval` modülüne güncellendi.
  - Mevcut `RequestTests` negatifleri (çift onay, yetkisiz onay, tenant) yeni modelde **değişmeden** geçti.

## 2. Yetki senaryoları (§7.7) — kod seviyesinde doğrulandı
| Rol / durum | Talep Formu (requests) | Talep Onaylama (request_approval) | Sonuç |
|---|---|---|---|
| Süper Admin / Admin | tam (bypass) | tam (bypass) | ✅ |
| Yalnız form yetkisi (requests edit) | oluştur/düzenle | **onaylayamaz** (Forbidden) | ✅ ayrık |
| Yalnız onay yetkisi (request_approval edit) | form yazamaz | **onaylar/reddeder** | ✅ ayrık |
| Yetkisiz | gizli | gizli | ✅ deny-by-default |
| Başka firma admini | — | onay dener → **tenant Forbidden** | ✅ |

- **UI ↔ servis tutarlılığı düzeltildi:** web/masaüstü onay butonu eskiden `requests` Edit ile görünüyor ama
  servis `btn-approve` istiyordu (mismatch). Artık ikisi de `request_approval` Edit → görünen buton çalışır.

## 3. Idempotency / kritik davranış (§7.16)
- Onay hâlâ **durum makinesi** üzerinden: çift onay `InvalidOperationException` (LWW yok, korunuyor).
- Stok onayda **düşmez**; `CreateIssueFromRequest` ayrı kontrollü işlem (değişmedi).
- Migration035 **idempotent** (NOT EXISTS guard + iki kez çalıştırma testi).

## 4. Eksik ekran denetimi (kullanıcı kuralı)
- Tüm **operasyonel** ekranlar yetki ağacında mevcut; yeni `request_approval` onay ekranını kapsıyor.
- Ağaçta modülü olmayan ekranlar ve gerekçe: `company-permissions` (süper-admin konsolu — Adım 2 redesign),
  `developer` (IsAdmin tanı konsolu), `trash` (reauth + `btn-restore` ile temsil). Eksik operasyonel ekran **yok**.

## 5. Coverage Matrix (bu ekranlar)
| Alan | Durum |
|---|---|
| Yetki (rol ayrımı, deny-by-default) | ✅ testli |
| Database (migration taşıma + idempotent) | ✅ Migration035Tests |
| Hata/tenant | ✅ |
| UI (ağaçta buton satırları) | ✅ build; canlı tık deploy sonrası |
| Security (yetki atlama) | ✅ (form≠onay ayrımı) |
| Performans | ⚠ etkisiz (statik liste) |

## 6. Riskler / notlar
- **Deploy bekliyor (web):** Bu değişiklikler canlıya alınmadı; kullanıcı kararı = sonraki web işiyle birlikte.
- Bu, çok adımlı büyük promptun **Adım 1**'i. Adım 2+ (yeni ara rol, süper-admin-only reorg, firma tanım,
  şablon firma seçimi, malzeme şablonu, şube zorunluluğu) `docs/YARIM_KALAN_ISLER.md`'de.
