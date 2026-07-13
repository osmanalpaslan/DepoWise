# Yetki Şablonu — Test Raporu (Adım 4)

> Kapsam: Yetki Şablonu ekranı + kullanıcı-oluşturma şablon tüketimi (§7.1). Tarih: **2026-07-12**. Motor: Opus 4.8.

## 0. Yapılanlar
- **Firma-kapsamlı şablon:** `scope_all` kolonu (Migration039). Süper admin şablonu **bir firmaya** veya **Tüm Firmalar**'a tanımlar.
- **Firma-bazlı görünürlük:** kullanıcı-oluşturma yetkili aktör (users/Create) KENDİ firması + tüm-firma şablonlarını görür (`ListForUserCreation`). Başka firmanın şablonu görünmez/okunamaz (tenant izolasyonu).
- **Ağaç firma-scoped:** şablon ekranında seçilen firmanın **admine açık** modülleri gelir (`/api/permission-templates/modules?companyId=`); "Süper Admin" düzeyine alınan ekranlar hariç.
- Web ekranına **firma seçici** (+ "Tüm Firmalar") + kapsam sütunu. Users ekranı şablon listesi `for-user` ucuna bağlandı (web + masaüstü).

## 1. Otomatik testler
```
Başarılı! — Başarısız: 0, Başarılı: 302, Atlanan: 0, Toplam: 302, Süre: 32 s
```
- **302/302 yeşil** (298 → 302, +4). Solution build **0 hata**.
- Yeni: `PermissionTemplateTests` — firmaya-özel/tüm-firma görünürlük · ListForUserCreation users/Create ister ·
  GetData başka firma şablonuna erişemez · Create/Delete yalnız süper admin.

## 2. Senaryolar (§7.7 yetki + tenant)
| Senaryo | Sonuç |
|---|---|
| B admini: B'ye özel + tüm-firma şablonu görür, A'yı GÖRMEZ | ✅ |
| Süper admin yönetim listesi: tümü + kapsam (firma adı / Tüm Firmalar) | ✅ |
| users/Create yetkisi olmayan personel şablon göremez | ✅ Forbidden |
| A admini B'nin şablon içeriğini okuyamaz | ✅ Forbidden (süper admin okur) |
| Create/Delete yalnız süper admin | ✅ |

## 3. Coverage
| Alan | Durum |
|---|---|
| Yetki (süper admin oluşturur, admin tüketir) | ✅ testli |
| Tenant izolasyonu (firma-scoped şablon) | ✅ |
| Database (Migration039, idempotent kolon) | ✅ |
| UI (web firma seçici + ağaç + kapsam; masaüstü consumer) | ✅ build; canlı tık deploy sonrası |

## 4. Riskler / notlar
- **Deploy bekliyor** (web + API): şema Migration035→039. Kullanıcı kararı = sonraki web işiyle birlikte.
- Masaüstü şablon OLUŞTURMA ekranına firma seçici eklenmedi (öncelik web); desktop-oluşturulan şablon süper adminin
  home firmasına scope'lanır. Consumer tarafı (Users) firma-scoped doğru çalışır.
