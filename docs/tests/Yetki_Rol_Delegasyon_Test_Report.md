# Yetki / Rol / Delegasyon — Test Raporu (Adım 2)

> Kapsam: yetki çekirdeği + Yetkiler, Yetki Şablonları, Firma Yetki Kontrol, Talep onaylama ekranları.
> §7.1 gereği yalnız bu ekranlar/katman. Tarih: **2026-07-12**. Motor: Opus 4.8.

## 0. Yapılanlar (Adım 2, 5 dilim)
- **2a — Kısıtlı Süper Admin rolü:** Admin ile Süper Admin arası. Admin bypass'ı YOK (deny-by-default);
  yalnız süper admin atar; firma admini bu kullanıcıyı yönetemez. Migration036.
- **2b — Süper-admin-only devri:** Kota/Canlı Sunucu/Yedekler/Makine/Güncelleme/Firma Tanım süper admin
  VEYA (kısıtlı süper admin + açık grant). Kota İzleme süper-admin-only oldu. SaveForUser: bu ekranlar
  yalnız Kısıtlı Süper Admin hedefe verilebilir.
- **2c — Ağaç görünürlüğü (delegasyon tavanı):** aktör yalnız KENDİ verebileceği yetkileri görür;
  veremeyeceği yetkiler ağaçta yok. /api/modules + /api/buttons + masaüstü filtrelendi.
- **2d — Firma Yetki Kontrol → Serbest/Admin/Süper Admin düzeyi:** "Global kilit" kaldırıldı; her ekran
  firma bazında 3 düzey. Migration037 (level kolonu + eski global kilit → firma 'admin' satırı).
- **2e — Admin yükseltme uyarısı sebep listesi:** hangi ekranların yükseltmeye sebep olduğu madde madde
  gösteriliyor (web + masaüstü).

## 1. Otomatik testler
```
Başarılı! — Başarısız: 0, Başarılı: 294, Atlanan: 0, Toplam: 294, Süre: 34 s
```
- **294/294 yeşil** (287 → 294; Adım 2'de net +7 test). Solution build **0 hata**.
- Yeni test dosyaları: `RestrictedSuperAdminTests` (10 senaryo: rol/atama/bypass-yok/devir/görünürlük),
  `Migration037Tests` (global→firma taşıma, idempotent). `CompanyGrantTests` yeni düzey modeline yazıldı.

## 2. Yetki senaryoları (§7.7)
| Senaryo | Sonuç |
|---|---|
| Kısıtlı Süper Admin atama — yalnız süper admin | ✅ Forbidden (admin) / OK (süper) |
| Kısıtlı Süper Admin — admin bypass yok (deny-by-default) | ✅ |
| Süper-admin-only devri yalnız Kısıtlı Süper Admin'e | ✅ (Personel'e InvalidOperation) |
| Devredilen kota ekranı — yalnız verilen işlem | ✅ (View verildi, Edit yok) |
| Firma admini Kota İzleme'yi göremez (süper-admin-only) | ✅ |
| Ağaç görünürlüğü — aktör yalnız kendi verebildiğini görür | ✅ (super=tümü, ilk admin=normal, sınırlı=sahip olduğu) |
| Firma düzeyi: admin → hedef admin; superadmin → Kısıtlı Süper Admin | ✅ |
| Migration037 global→firma 'admin' + idempotent | ✅ |

## 3. Kritik güvenlik (§7.16)
- Tenant/permission çekirdeği korunuyor; delegasyon tavanı (GrantableLimit + ClampModule) yerinde.
- Süper-admin düzeyi ekranlar tüm aktörlerde (süper admin dahil) yalnız Kısıtlı Süper Admin hedefe.
- Kısıtlı Süper Admin ve süper admin kullanıcılar firma adminince yönetilemez.

## 4. Coverage Matrix
| Alan | Durum |
|---|---|
| Yetki (rol/delegasyon/görünürlük) | ✅ testli |
| Database (Migration036/037, idempotent) | ✅ |
| Hata/tenant | ✅ |
| UI (3-düzey Firma Yetki Kontrol, sebep listesi) | ✅ build; canlı tık deploy sonrası |
| Security (yetki atlama, delegasyon tavanı) | ✅ |

## 5. Riskler / notlar
- **Deploy bekliyor** (web + API): kullanıcı kararı = sonraki web işiyle birlikte. Şema Migration035→037.
- Adım 2 tamam. Kalan büyük prompt adımları (3–7) `docs/YARIM_KALAN_ISLER.md`'de.
