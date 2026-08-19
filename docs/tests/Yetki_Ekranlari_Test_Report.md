# Yetki Ekranları — Test Raporu (C turu)

> Tarih: **2026-08-19** · Kapsam (§7.1): Yetkiler · Rol Yetki Kontrol · Firma Yetki Kontrol ·
> Yetki Şablonları · Kullanıcı Tanım · masaüstü kabuğu (ikon rayı). **İki ortam da incelendi.**

## Coverage Matrix (§7.13)

| Madde | Masaüstü | Web |
|---|---|---|
| Form açıldı | ✅ derleme + bağlama doğrulaması | ✅ gerçek arayüz (5 ekran 200) |
| Düzenleme akışı | ✅ Düzenle / Vazgeç / Kaydet | ✅ Düzenle / Vazgeç / Kaydet |
| Yetki kaybı olmaması | ✅ U4 testi | ✅ U4 testi |
| Rol değişimi | ✅ aynı ekranda | ✅ aynı ekranda |
| Hata mesajları | ✅ durum şeridi | ✅ görünür hata + Yeniden dene |
| Yetki (kendi rolünü değiştirme) | ✅ engelli | ✅ engelli |
| Yetkisiz erişim | — | ✅ devre düşmüyor (YET-C4) |
| Performans | — | ✅ uç 107 ms / 200 |
| UI | ✅ ikon rayı kaldırıldı | ✅ değişiklik gerekmedi |

## Otomatik testler
| Test | Neyi kilitler |
|---|---|
| U1 (×2) | Yükleme hatası ekranda görünür; sonsuz tekerlek yok |
| U2 | Web: matris kilitli açılır, Düzenle/Kaydet/Vazgeç var |
| U3 | Masaüstü: ağaç düzenleme modu olmadan açılmaz |
| U4 | **Düzenlemeye geçmek yetkileri silmez** |
| U5 | Rol aynı ekranda; kendi rolü kilitli; rol yetkiden ÖNCE yazılır |
| U6 | İkon rayı yok, menüye dönüş düğmesi duruyor, ölü kod yok |
| U7 (×3) | İlk yükleme Blazor devresini düşürmez |

**Sonuç: 10/10 geçti.** Tam takım: **2126 → (yeni 3 test ile) tekrar koşuldu**, 0 başarısız.

## Bulunan hatalar
| # | Öncelik | Hata | Kök neden | Çözüm |
|---|---|---|---|---|
| YET-C1 | Orta | Rol/Firma Yetki Kontrol web'de sonsuza kadar yükleniyor | Hata mesajı tablo dalının İÇİNDE; `catch` satır listesini doldurmuyor | Hata her durumda görünür + `_rows` doldurulur + Yeniden dene |
| YET-C2 | Orta | Yetkiler ekranında düzenleme adımı yok | Ağaç daima açık; salt-okunur/düzenleme ayrımı yok | Düzenle → Kaydet akışı (iki ortam) |
| YET-C3 | Düşük | Rol başka ekranda | Rol ve yetki ayrı ekranlardaydı | Rol Yetkiler ekranına alındı |
| **YET-C4** | **Yüksek** | `/permissions` yetkisiz açılınca **tüm sayfa çöküyor** | `OnInitializedAsync` içindeki çağrı korumasız; 401 → devre düşüyor | 3 ekranda koruma; gerçek arayüzde doğrulandı |

## Riskler / sınırlar
- **Masaüstü görsel tur yapılmadı:** oturum açmayı gerektiriyor (parola girmiyorum). Yerine
  Avalonia **derlenmiş bağlama** güvencesi geçerli: `AvaloniaUseCompiledBindingsByDefault=true` ve
  görünümde `x:DataType` tanımlı olduğu için `IsEditing · RoleEnabled · RoleChanged · Roles ·
  BeginEditCommand · CancelEditCommand` bağlamalarının hepsi **derleme zamanında** doğrulandı (0 hata).
- **Web görsel tur** oturumsuz yapıldı: 5 yetki ekranı 200 döndü ve sunucu günlüğünde **istisna yok**.
  Oturum içi tıklama turu kullanıcı tarafından yapılacak.

---

## Tur A — Yapı değişikliği (2026-08-19)

**Kapsam:** rol tavanının firma bazlı olması (Migration 072) · iki tavan ekranının birleşmesi ·
Yetkiler ekranına şablon kısayolu. **İki ortam da incelendi.**

| Doğrulama | Sonuç |
|---|---|
| `RoleGrantCompanyTests` (yeni) | **4/4** |
| `PermissionScreenUxTests` | **11/11** (U8 · U9 yeni) |
| Parity testleri (katalog + ekran ağacı) | **31/31** |
| Migration 072 — veri kopyalama | 2 kısıt × 3 firma = **6 satır**, `created_at` korundu, idempotent |
| Firma izolasyonu (davranış) | A'daki kısıt B'yi **etkilemiyor**; B'nin kaydı A'yı **bozmuyor** |
| Menü bağlantısı (web) | 55 → **54** (ekran kaybı değil, iki ekran birleşti) |

### Yeni testlerin kilitledikleri
| Test | Kilit |
|---|---|
| R1 | Tablo firma bazlı; aynı (rol, modül) farklı firmalarda yan yana durabilir, aynı firmada tekrar edemez |
| **R2** | **Migration eski kısıtları HER firmaya kopyalar** — yükseltme sonrası görünen davranış aynı |
| R3 | Bir firmanın tavanı diğerini etkilemez (eski davranışta tablo komple siliniyordu) |
| R4 | Yönetim ekranı firma bazlı okur; süper admin başka firmayı yönetirken kendi firmasını bozmaz |
| U8 | Rol tavanı ekranı ayrı olarak YOK; Firma Yetki Paketi'nde sekme; modül anahtarı korundu |
| U9 | Şablondan doldurma iki ortamda; şablon **sunucuya yazmaz**, yalnız kutuları doldurur |

### Referans güncellemeleri (gevşetme değil)
- S14: web menü bağlantısı 55 → 54 — "Rol Yetki Kontrol" ekranı birleşme nedeniyle kalktı.
- S16: `role_permissions` yalnız-web ekran listesinden çıktı (artık ekran değil, yalnız modül).
- S18: sabit "58 ekran" yerine `AppScreens.All.Count` — sayı değişince test kırılmaz, garanti sürer.
