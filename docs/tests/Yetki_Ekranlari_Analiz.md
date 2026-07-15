# Yetki Ekranları — Analiz + Bağlantı Şeması (Faz 1)

**Tarih:** 2026-07-15 · **Test:** 319/319 yeşil

## 1. Yetki ekranları ve rolleri
| Ekran | Route | Kim erişir | Ne yapar |
|---|---|---|---|
| **Yetkiler** | `/permissions` | `permissions` (admin bypass) | Kullanıcı seç → modül×(Oku/Yaz/Düzelt/Sil) + özel butonlar |
| **Yetki Şablonları** | `/permission-templates` | `permission_templates` | Firma kapsamlı hazır yetki paketi |
| **Firma Yetki Kontrol** | `/company-permissions` | süper admin | Ekranın FİRMADAKİ düzeyi: Serbest/Admin/Süper Admin |
| **Rol Yetki Kontrol** | `/role-permissions` | süper admin | Ekran × ROL matrisi: bir ekranı role tamamen kapatır |
| **Kullanıcı Tanım** | `/users` | `users` | Kullanıcı + rol atama (firma: süper admin seçer) |

## 2. Katmanlar (tek doğru kaynak → uygulama)
```
AppModules.All  ──►  yetki ağacı (web /api/modules, masaüstü PermissionsViewModel)
     │
     ├─ IsPublic                → herkese açık (Ana Ekran/Tema/Hakkında)
     ├─ IsSuperAdminOnly        → yalnız süper admin + devri Kısıtlı Süper Admin
     ├─ IsAdminRestricted       → alt role verilemez (önce Admin'e yükselt)
     │
     ├─ CompanyGrantService     → FİRMA ekseni (company_grant_limits): Serbest/Admin/Süper Admin
     └─ RoleGrantService        → ROL ekseni (role_grant_limits): ekran role kapalı mı

AccessControl.Can(session, module, action)  ← UI + API AYNI sonucu üretir (deny-by-default)
   1) public → yalnız View
   2) BlockedModules (Rol Yetki Kontrol) → admin bypass'ından ÖNCE reddeder
   3) IsSuperAdminOnly → süper admin / (kısıtlı süper admin + açık grant)
   4) IsAdmin → tam yetki
   5) Explicit (user_permissions) → deny-by-default
```

## 3. Delegasyon tavanı (yetki ağacı görünürlüğü)
`CanGrantModule(actor, key)`: aktör **yalnız kendi verebileceğini** görür.
`/api/modules?userId=X` artık HEDEF bazlı: hedefe **verilemeyecek** ekranlar (rol kapalı VEYA hedef uygun
değilken süper-admin-only) ağaçta **hiç görünmez** — kilit/kısıt gösterimi kaldırıldı (kullanıcı isteği).

## 4. Tenant (firma) izolasyonu — DURUM
- **Veri katmanı sağlam:** tüm `List(...)` sorguları `WHERE company_id = s.CompanyId` (Branch/Material/Vehicle/…).
- **Yazma:** `TenantAccessGuard` payload company_id'yi reddeder; yalnız süper admin başka firma seçebilir.
- **Yeni:** `CompanyService.Selectable(s)` (firma seçicileri) + `/api/companies/options` — süper admin tümü,
  diğerleri YALNIZ kendi firması. Şube ekranına firma seçici eklendi (süper admin başka firmaya şube açar).
- **Test:** `TenantCompanySelectorTests` — B admini A firmasını asla göremez; payload zorlaması `ForbiddenException`.

## 5. Değerlendirme — mantıklı mı?
Yapı **sağlam ve tutarlı**: UI ve API aynı `AccessControl`'ü kullanıyor, üç bağımsız eksen (public / firma / rol)
deny-by-default'ta birleşiyor, tenant izolasyonu veri katmanında zorunlu. **Öneri gerektiren tek nokta yoktu**;
kullanıcının istediği iki davranış (hedef-bazlı gizleme + şube firma seçici) eklendi. Kalan maddeler sonraki fazlarda.
