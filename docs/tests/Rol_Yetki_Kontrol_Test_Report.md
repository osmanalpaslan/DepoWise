# Rol Yetki Kontrol — Test Raporu

**Tarih:** 2026-07-14 · **Şema:** Migration041 (`role_grant_limits`) · **Test:** 317/317 yeşil (4 yeni)

## 1. Ne yapıldı
Süper admine özel **ekran × rol** matrisi (`/role-permissions`). Yönetilen roller: Kısıtlı Süper Admin, Admin,
Personel. Bir ekran bir role **kapatıldığında** üç yerde birden uygulanır:

1. **Yetki ağacında görünmez** — `/api/modules?userId=…` hedefin rolüne kapalı modülleri hiç döndürmez
   (web `Permissions.razor`, masaüstü `PermissionsViewModel` ağacı kullanıcı seçilince yeniden kurar).
2. **Grant reddedilir** — `PermissionService.SaveForUser` süper admin dahil kimsenin vermesine izin vermez.
3. **Erişim kapanır** — oturum kurulurken `SessionContext.BlockedModules` doldurulur; `AccessControl.Can`
   bunu **admin bypass'ından ÖNCE** uygular → daha önce verilmiş izin ya da Admin rolü bunu aşamaz.

**Süper admin muaftır** (aksi halde platform sahibi kendini kilitler). **Yapısal kilitler** değiştirilemez:
süper-admin-only ekran Admin/Personel'e, admin-kısıtlı ekran Personel'e zaten verilemez. Public ekranlar
(Ana Ekran/Tema/Hakkında) matriste yoktur.

## 2. Otomatik testler (`tests/DepoWise.Tests/RoleGrantTests.cs`)
| Senaryo | Sonuç |
|---|---|
| Personel'e kapatılan ekran: ağaçta yok + grant reddedilir + oturumda erişim kapanır | ✅ |
| Admin'e kapatılan ekran: **admin bypass'ı aşamaz**; süper admin muaf | ✅ |
| Yalnız süper admin yönetir (admin `ForbiddenException`); yapısal kilitler sabit; public modül matriste yok | ✅ |
| Matris tam değiştirir; açılınca yetki yeniden verilebilir ve erişim geri gelir | ✅ |

## 3. Coverage Matrix
| Alan | Durum |
|---|---|
| Form açıldı / Yükleme / Kaydet / Geri al | ✅ |
| Arama (ekran filtresi) · Toplu "tümü açık/kapalı" | ✅ |
| Yetki (deny-by-default, süper-admin-only modül `role_permissions`) | ✅ testli |
| Hata mesajları (grant reddi kullanıcıya sebebiyle döner) | ✅ |
| Database (Migration041, idempotent, UNIQUE(role_key,module_key)) | ✅ |
| Security (admin bypass aşılamaz; tenant bağımsız platform ayarı; süper admin muaf) | ✅ testli |
| Performans (matris tek sorgu; oturumda tek `IN (...)` sorgusu) | ✅ |
| UI / UX (kilit ikonu + açık/kapalı etiketi + sticky kaydet çubuğu) | ⏳ canlı tık doğrulaması kullanıcıda |

## 4. Riskler / notlar
- **Etki anı:** kapatma, ilgili kullanıcı **yeniden giriş yapınca** tam etkin olur (oturum yükleme anında
  hesaplanır). Ekranda bu açıkça yazıyor.
- Kullanıcı birden çok rol taşıyorsa, **herhangi bir rolünde kapalı** olan ekran kapalıdır (deny-by-default).
- Yetki Şablonları ekranı ağacı aktörün tavanına göre kurar; şablon bir role kapalı modül içerse bile
  `SaveForUser` reddeder (fail-closed).
