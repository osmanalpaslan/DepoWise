# Giriş · Makine · Eşitleme — Analiz ve Test Raporu (B turu)

> Tarih: **2026-08-19** · Tetikleyici: kullanıcı makineleri sıfırlayıp sildi; ardından giriş ve
> eşitleme bozuldu. **Sistemin bel kemiği** olduğu için tüm zincir incelendi.

## 1. Kanıta dayalı kök neden

| Kanıt | Anlamı |
|---|---|
| `machine_branch.txt` / `machine_status.txt` **7 gündür güncellenmemiş** (12 Ağustos) | `/api/machines/register` bir haftadır **hiç başarılı olmamış** |
| Aynı uç canlıda ölçüldü: **HTTP 200 / 1,4 sn** | Sunucu sağlam — sorun istemcide |
| `MachineGate` zaman aşımı **6 sn**, `ServerAuthClient` **10 sn** | Sunucu veritabanı uykudan uyanırken bu süre aşılabiliyor |
| Zaman aşımı → `catch` → **"çevrimdışı"** | Uygulama internet varken kendini çevrimdışı sanıyor |

**Tek kök neden iki şikâyeti birden açıklıyor:**
- Çevrimdışı sanılınca **makine şubesi önbellekten** okunuyor → silinen makinede şube boş →
  *"Bu makine ilk kez kuruluyor, internet gerekli"* (**babanın giremediği durum**).
- Çevrimdışı sanılınca **şube seçim ekranı atlanıyor** ve makinenin **eski önbellek şubesine**
  sessizce giriliyor (**kullanıcının kendi makinesinde yaşadığı durum — TEST ŞANTİYE**).

## 2. Yapılan düzeltmeler

| # | Düzeltme | Etki |
|---|---|---|
| B1 | Zaman aşımı 6→**20 sn** / 10→**25 sn**, makine kaydında **tek tekrar** | Uyanma gecikmesi artık çevrimdışı sayılmıyor |
| B1b | Sunucu doğruladıktan sonraki **yerel aynalama** kendi `try/catch`inde | Yerel yazma hatası "internet yok" sanılmıyor |
| B2 | Çevrimdışı **sessiz oto-şube girişi kaldırıldı** | Kullanıcı hangi şubeye girdiğini görür ve değiştirebilir |
| B3 | Makine şubesi yokken **giriş kilitlenmiyor** | Kendi şubesi bilinen kullanıcı girer; makine ataması ertelenir |
| B4 | **"Uyarıyı Temizle"** + sıfırlamalar eşitleme defterini siler | Silinmiş kayıtların uyarısı ekranda kalmıyor |
| B5 | Süper admin **yetki kilitlerinden muaf** | "Yetki tamamen süper adminin elinde" |

## 3. Eşitleme hatalarının kaynağı (6 kayıt)

Kalan uyarı, **6 Ağustos'ta kuyruğa girmiş** ve 5 denemede uygulanamamış satırlara ait:

| Hata | Anlamı |
|---|---|
| `material_categories 23505 duplicate key` | Aynı kategori sunucuda zaten var (firma sıfırlaması sonrası yerelde eski kopya kalmış) |
| `vehicle_template_materials 23503 FK` ×4 | Satırın bağlı olduğu **şablon sunucuda yok** (silinmiş) |
| `maintenance_materials 23503 FK` | Satırın bağlı olduğu **bakım kaydı sunucuda yok** |

Bunlar **öksüz test kayıtlarıdır**; otomatik gönderim zaten durdurulmuştu (bir daha denenmiyorlardı),
geriye yalnız temizlenemeyen uyarı kalmıştı. Artık temizlenebiliyor ve sıfırlamalar bunu bırakmıyor.

## 4. Testler
| Test | Kilit |
|---|---|
| L1 | Giriş zaman aşımları yeterince uzun + tekrar var |
| L2 | Yerel ayna hatası "çevrimdışı" sayılmaz |
| **L3** | **Çevrimdışı sessiz oto-şube girişi yok** |
| **L4** | **Makine şubesi yokken giriş kilitlenmez** |
| L5 | Eşitleme uyarısı temizlenebilir; sıfırlama defteri de siler |

Güncellenen referans testleri (yeni kurala göre, gevşetme değil): `RoleGrantTests` (yapısal kilit
süper admin için bağlayıcı değil) · `AuthPermissionTests` · `RestrictedSuperAdminTests` ·
`CompanyGrantTests` · `ScreenPlatformVisibilityTests` — hepsinde **alt roller için kural aynen
doğrulanmaya devam ediyor**.

## 5. Açık kalan (kaynağında çözülmedi)
Push kuyruğundaki **kalıcı hata sınıfı** (yinelenen anahtar / ebeveyni silinmiş satır) hâlâ satırın
sessizce atlanmasıyla sonuçlanıyor. Bu tur kullanıcıyı kilitleyen tarafı çözdü; kalıcı hataların
kaynakta ele alınması (doğal anahtarla upsert + öksüz satır elemesi) **ayrı bir iş** olarak durmalı.
