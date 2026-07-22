---
name: superadmin-lockout-company-delete
description: Firma silme süper admini pasife alıp sistemden kilitleyen kritik hata ve alınan önlemler
metadata: 
  node_type: memory
  type: feedback
  originSessionId: b6c1eef5-05a1-4e5e-a21a-2092438a9228
---

Firma silme (soft-delete) o firmadaki TÜM aktif kullanıcıları `is_active=0` yapıyordu; süper admin kendi home firmasını (ör. "DEPOWISE") silince kendi hesabını pasife alıp sistemden tamamen kilitliyordu → sonraki login "Kullanıcı adı veya parola hatalı" (login `is_active=1 AND is_deleted=0` şartı arıyor). Sunucu restart'ı kurtarmıyordu çünkü seed yalnız süper admin YOKSA çalışıyor (mevcut ama pasif olanı aktifleştirmiyordu).

**Why:** Süper admin platform sahibidir; hiçbir firma/operasyon işlemi onu pasife/kilide düşürememeli. Tek süper admin kilitlenirse tüm platform yönetimi kilitlenir.

**How to apply:** (1) `CompanyService.Delete` deaktivasyon sorgusuna `AND id NOT IN (süper admin kullanıcıları)` ekli — süper admin asla pasife alınmaz. (2) `ServerServices.EnsureSeedAdmins` her açılışta pasif süper adminleri `is_active=1` yapan self-heal içeriyor → canlı kilit bir API redeploy ile açılır. (3) Regresyon testi: `OrgPersonnelTests.Firma_Silme_SuperAdmini_PasifeAlmaz`. Gelecekte kullanıcıları toplu pasife/silmeye alan HER yeni yolda süper admini hariç tut. Masaüstünde kilitli süper admin sunucudan sync (`ImportRemoteUser` `is_active=1` upsert) ile kurtulur.
