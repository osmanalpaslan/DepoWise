# Kullanıcı Oluşturma — Şube Zorunluluğu (Adım 6)

> Kapsam: kullanıcı oluşturma akışı (§7.1). Tarih: **2026-07-12**. Motor: Opus 4.8.

## 0. Yapılanlar
- **Şube/şantiye zorunlu** — **Admin dahil** tüm firma kullanıcıları oluştururken. Muaf YALNIZ platform rolleri:
  **Süper Admin, Kısıtlı Süper Admin**. Admin için firmanın **herhangi bir** şubesi geçerlidir. (Kullanıcı onayı 2026-07-13.)
- **Şube yoksa** özel yönlendirme mesajı ("önce Şube/Şantiye ekranından oluşturun"); şube varsa "şube seçin";
  seçilen şube **firmaya ait ve geçerli** olmalı (tenant + geçerlilik).
- Enforcement **oluşturma-akışı sınırında** (web API `/api/users` + `/api/personnel/{id}/account`, masaüstü
  `UsersViewModel.Add`) — `UserService.ValidateBranchForNewUser`. Servis çekirdeği (CreateUser) davranışı
  değişmedi → mevcut 300+ test bozulmadı.
- Web kullanıcı formunda şube alanı **zorunlu** işaretlendi + şube yoksa **uyarı** (Şube ekranına link).

## 1. Otomatik testler
```
Başarılı! — Başarısız: 0, Başarılı: 312, Atlanan: 0, Toplam: 312, Süre: 29 s
```
- **312/312 yeşil** (307 → 312, +5). Solution build **0 hata**.
- Yeni: `UserBranchRequirementTests` — şube yok/özel mesaj · şube var seçilmemiş/uyarı · geçerli şube geçer ·
  başka firma şubesi reddedilir · süper/kısıtlı-süper admin + admin muaf.

## 2. Senaryolar (§7.5 form)
| Senaryo | Sonuç |
|---|---|
| Personel + şube yok | ✅ "önce şube oluşturun" hata |
| Personel + şube var ama seçilmemiş | ✅ "şube seçin" hata |
| Personel + geçerli şube | ✅ geçer |
| Personel + başka firma şubesi | ✅ "geçersiz" reddedilir |
| Süper/Kısıtlı Süper Admin | ✅ muaf (şubesiz oluşturulur) |
| Admin + şube yok | ✅ reddedilir (artık muaf değil); firmanın herhangi bir şubesiyle geçer |

## 3. Coverage
| Alan | Durum |
|---|---|
| Doğrulamalar (zorunlu alan + tenant) | ✅ testli |
| Yetki (rol bazlı muafiyet) | ✅ |
| UI (web zorunlu alan + şube-yok uyarısı; masaüstü kayıt-anı mesaj) | ✅ build; canlı tık deploy sonrası |

## 4. Riskler / notlar
- **Deploy bekliyor** (web + API): şema değişmedi (Migration040 hâlâ son). Kullanıcı kararı = sonraki web işiyle birlikte.
- **Ürün kararı (netleşti 2026-07-13):** Admin de şubeye **zorunlu** (yalnız Süper/Kısıtlı Süper Admin muaf).
  Admin için firmanın **herhangi bir** şubesi geçerlidir. Test için oluşturulmuş şubesiz adminler önemsiz
  (enforcement akış sınırında olduğundan servis testleri etkilenmedi).
- Enforcement servis çekirdeği yerine **istemci sınırında** (API + masaüstü VM): tüm gerçek akışlar kapsanır;
  yalnız doğrudan servis çağrısı (testler/senkron) muaf — şube bir alan doğrulaması (yetki değil).
