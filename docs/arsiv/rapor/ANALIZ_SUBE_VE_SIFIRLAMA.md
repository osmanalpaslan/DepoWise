# Analiz — Şube Yapısı, Firma Verisi Sıfırlama ve Yetki Devri

Tarih: 2026-08-18 · Kapsam: salt-okunur kod analizi (hiçbir dosya değiştirilmedi)
Talep: kullanıcı isteği — "şube yapısı + sıfırlama akışı + yetki devri tam analiz"

---

## 1. ÖZET

| Alan | Bulgu | Kritik | Orta | Düşük |
|---|---|---|---|---|
| Şube yapısı | 6 | 2 | 3 | 1 |
| Sıfırlama akışı | 6 | 1 | 1 | 2 (+2 doğrulandı, sorun yok) |
| Yetki devri | 4 | — | 4 | — |

**Kullanıcının şartı** ("web'den firma verisi sıfırlayınca babam mevcut kullanıcısıyla girip sıfırdan
veri girebilsin; şubeler ve kullanıcılar silinmesin") **sunucu tarafında KARŞILANIYOR**, ancak
**masaüstü tarafında SIF-01 hatası bu şartı bozuyor.**

---

## 2. ŞUBE YAPISI

### ŞB-01 KRİTİK — Üst şube ve tür masaüstünde kayboluyor
**Kullanıcının bildirdiği hata budur.** ("tanımlıyordum ama tanımlanmamış gibi normale dönüyordu")

Kök neden üç yerdedir:

1. `src/DepoWise.Infrastructure/Organization/BranchService.cs:283` — `ListForLogin` sonucu
   `BranchRow(..., ParentId: null, ParentName: null, ...)` olarak sabit **null** döner.
   Bu metodu `/api/public/branches` kullanır.
2. `src/DepoWise.Desktop/BranchMirror.cs:47` — sunucudan gelen şube için yalnız
   `(Id, Name, Code)` taşınır; **kind ve parent_id yolda düşer**.
3. `src/DepoWise.Infrastructure/Organization/BranchMirrorApply.cs:34-36` — INSERT'te
   `kind` sabit `branch`, `parent_id` hiç yazılmaz (NULL); ON CONFLICT güncellemesi de
   bu iki kolona **dokunmaz**.

**Akış:** masaüstünde üst şube seçilir → `BranchesViewModel.Save()` sunucuya yazar (**sunucu doğru
kaydeder**) → hemen ardından `BranchMirror.RefreshAsync` çağrılır → yerel kopya üst şubesiz ve
"Şube" olarak tazelenir → `Load()` **yerel** veritabanından okur → ekranda "—" ve "Şube" görünür.

**Etki:** sunucudaki veri doğrudur, web doğru gösterir; yalnız masaüstü ekranı yanlıştır.
Kullanıcı tekrar tekrar tanımlar, her seferinde "geri döner".
Tür alanı (Şantiye) da aynı hatadan etkilenir — muhtemelen fark edilmemiştir.

**Not:** `LookupSyncService` (giriş + "Elle Eşitle") kind ve parent_id'yi DOĞRU taşır.
Bu yüzden yeniden giriş yapıldığında değer geri gelir — hata "bazen düzeliyor" gibi görünür.

### ŞB-04 KRİTİK (tasarım) — Üst şube hiçbir işe yaramıyor
`parent_id` kod tabanında **yalnız saklanır ve gösterilir**. Şu yerlerin HİÇBİRİ onu okumaz:
- `BranchAccess` (yetki kapısı) — üst şubeye yetkili kullanıcı alt şubeleri **göremez**
- Raporlar — üst şube alt şubelerin toplamını **almaz**
- Stok / araç / personel filtreleri — hiyerarşi **uygulanmaz**

Yani bugün "Üst Şube" alanı sadece bir etikettir. Kullanıcının beklentisi büyük olasılıkla
"üst şube alt şubeleri görür / toplar" yönündedir; bu **hiç yapılmamıştır**.

### ŞB-02 ORTA — Döngü koruması yok
`BranchService.Update:118` yalnız "şube kendi üst şubesi olamaz" kontrolü yapar.
A'nın üstü B, B'nin üstü A yapılabilir. Bugün zararsızdır (ŞB-04 nedeniyle kimse ağacı
gezmiyor), ama ŞB-04 düzeltilirse **sonsuz döngü** olur.

### ŞB-03 ORTA — Silinen üst şube listede görünmeye devam ediyor
`BranchService.List:58` — `LEFT JOIN branches p ON p.id = b.parent_id` — `p.is_deleted=0`
koşulu **yoktur**. Üst şube silinince alt şubede o şubenin adı görünmeye devam eder.
Ayrıca `Delete` → `EnsureNoDependents` yalnız araç ve personel sayar; **alt şube kontrolü yoktur**,
üst şube silinince altındakiler kopuk referansla kalır.

### ŞB-06 ORTA — Web'de "+" ile hızlı üst şube ekleme yanlış firmaya yazıyor
`Branches.razor:52` — `LookupSelect` "+" düğmesi `/api/branches` ucuna `{ name, kind }` gönderir,
**companyId göndermez**. Süper admin başka bir firma seçiliyken "+" ile üst şube eklerse,
şube **kendi firmasına** açılır (`BranchService.Create` → `ResolveCompany(s, null)` → oturum firması).

### ŞB-05 DÜŞÜK — Giriş ekranı şube listesi düz
`ListForLogin` hiyerarşi taşımadığı için giriş ekranındaki şube seçimi düz listedir.
Çok şubeli firmada üst/alt ilişkisi görünmez.

---

## 3. FİRMA VERİSİ SIFIRLAMA

### Bugün ne oluyor (doğrulanmış akış)

1. Web → **Firma İş Verisini Sıfırla** (özel kod + şifre + firma adı teyidi, yalnız süper admin)
2. `CompanyPurgeService.ResetBusinessData` → sunucuda `BusinessSyncService.Tables` listesindeki
   **tüm iş verisi ve tanımlar** silinir (PostgreSQL'de yabancı-anahtar zinciriyle çocuk tablolar dahil)
3. `Program.cs:1503` → **otomatik** olarak tüm makinelere "yerelini temizle" isteği bırakılır
4. Masaüstü bir sonraki **girişte** bu isteği görür ve yerelini temizler — **veri göndermeden ÖNCE**
5. Ardından tam yeniden indirme (`PullAsync(0)`) çalışır

**Sunucuda SİLİNEN:** Birimler · Tedarikçiler · Markalar · Malzeme Kategorileri · Araç Tipi/Kategori/Model ·
Bakım Tanımları · Personel Unvanları · Personel · Malzemeler · Araçlar · Stok hareketleri ve belgeleri ·
Bakımlar · Muayene/Sigorta · Sayaç geçmişi · Yakıt · Günlük Faaliyet · Talepler · Cariler ve hareketleri ·
Faturalar · KDV/Seri · Kasa/Banka hesapları ve hareketleri

**Sunucuda KORUNAN:** Firma · Şubeler · Kullanıcılar · Roller ve yetkiler · Yetki şablonları ·
Makine kayıtları · Sistem logu · Güncelleme paketleri

### SIF-04 DOĞRULANDI (sorun yok) — sunucu tarafı kullanıcının şartını karşılıyor
`CompanyPurgeService.ResetBusinessData:165` → `deleteCompaniesRow: false` ve silme listesi
yalnız `BusinessSyncService.Tables` ile sınırlı. Şube, kullanıcı, rol, yetki **silinmez**.

### SIF-05 DOĞRULANDI (sorun yok) — eski veri sunucuya geri gitmiyor (giriş anında)
`BusinessSyncPushService:59-61` — push "watermark" (bu makinenin en son gönderdiği zaman damgası)
ile çalışır. Yerel temizlik push'tan ÖNCE koştuğu için gönderilecek satır kalmaz.

### SIF-01 KRİTİK — Yerel sıfırlama fazlasını siliyor
`src/DepoWise.Desktop/ViewModels/LoginViewModel.cs:544`

```
try { LocalPurgeService.PurgeLocalCompany(companyId); }   // <- YANLIŞ FONKSİYON
```

`PurgeLocalCompany` (LocalPurgeService.cs:29) ADR-083'ün **tam silme** fonksiyonudur:
`user_roles` → company_id kolonu olan **TÜM** tablolar → en son `DELETE FROM companies`.
Yani yerelde firma kaydı, kullanıcılar, roller, yetkiler, şubeler ve şube kapsamları da silinir.

Doğru fonksiyon `PurgeBusinessData` (LocalPurgeService.cs:72) — açıklamasında yazdığı gibi:
*"YALNIZ iş verisi tablolarını … HARD siler; firma/kullanıcı/şube/tanım-dışı sistem verisi KORUNUR
(oturum bozulmaz)"*. `ShellViewModel.cs:215` (elle "yerelimi temizle") bu doğru fonksiyonu kullanır.

ADR-084 sözü: *"Bu, GİRİŞİ ENGELLEMEZ"* — mevcut çağrı bu sözü ihlal ediyor.

**Somut etki:** sıfırlama sonrası ilk girişte yerel `users` satırı silinir. `AuthService.cs:17`
açıkça yazar: *"password_hash (bcrypt) taşınır → sonraki açılışlarda offline giriş de çalışır."*
Satır silindiği için **o makinede çevrimdışı giriş artık yapılamaz** — şantiyede internet yoksa
kullanıcı kilitlenir. İnternetli bir giriş durumu onarır, ama bu kabul edilebilir değil.

### SIF-02 ORTA — Kontrol yalnız giriş anında yapılıyor
`ShellViewModel.cs` içinde `LocalReset` / `CompanyPurge` / `MachineReset` kontrolü **yoktur**
(arama sonucu: 0 eşleşme). Program açık ve giriş yapılmışsa 15 saniyelik eşitleme turu döner ve
eski yerel veriyi sunucuya **göndermeye devam eder**.

**Pratik sonuç:** sıfırlama sırasında açık kalan bir bilgisayar, sıfırladığınız veriyi geri yükler.
Bu yüzden bugünkü haliyle "sıfırlamadan önce tüm programları kapattırın" adımı **zorunludur**.

### SIF-03 DÜŞÜK/ORTA — Yerel temizlikte 7 tablo atlanıyor
`PurgeBusinessData` yalnız `BusinessSyncService.Tables` listesini siler ve yabancı anahtarları
kapatır (`PRAGMA foreign_keys=OFF`) → zincirleme silme olmaz. company_id kolonu olduğu halde
listede olmayan tablolar yerelde **öksüz** kalır:

| Tablo | Kullanıcıya ne olarak görünür |
|---|---|
| `stock_balances` | Eski stok bakiyeleri |
| `vehicle_inspections` | Eski muayene / sigorta kayıtları |
| `vehicle_meter_logs` | Eski araç sayaç geçmişi |
| `stock_change_logs` | Eski stok değişiklik kaydı |
| `file_records` | Eski dosya / fotoğraf kayıtları |
| `material_templates` | Eski malzeme şablonları |
| `vehicle_templates` | Eski araç genel tanımları |

Ayrıca company_id kolonu olmayan çocuk tablolar (`stock_count_lines`, `request_status_history`,
`material_equivalents`, `material_compatible_vehicles`, `maintenance_definition_vehicles`,
`vehicle_template_materials`) da kalır.

Sunucu tarafında bu sorun **yoktur** (PostgreSQL yolu yabancı-anahtar zinciriyle çocukları da siler).
Sorun yalnız masaüstündedir.

### SIF-06 DÜŞÜK — Şablonlar hiç senkron olmuyor (sıfırlamadan bağımsız)
`material_templates` ve `vehicle_templates` ne `BusinessSyncService.Tables` içinde ne de
`/api/lookups/sync` yanıtındadır. Yani **masaüstünde oluşturulan şablon web'e, web'de oluşturulan
şablon masaüstüne hiç gitmez**. Bu ayrı bir eksiktir; kullanıcı kararı bekliyor.

---

## 4. YETKİ DEVRİ

### Bugünkü durum
- **"Yerel Sıfırlama İsteği"** düğmesi `Companies.razor:176` içindedir; `companies` modülü
  `AppModules.IsSuperAdminOnly` listesindedir → ekran **hiç kimseye devredilemez**.
- `CompanyLocalResetService.RequestReset:32` → `if (!actor.IsSuperAdmin) throw` — sunucu da
  sert biçimde yalnız süper admine izin verir.
- Sonuç: bu yetki **yetki ağacında hiç görünmez**, kimseye verilemez.

### YET-02 ORTA — Özel butonlarda "admin bypass" var
`AccessControl.cs:139` → `CanUseButton(s, key) => IsAdmin(s) || s.Permissions.HasButton(key)`
Yani düğme kataloğa öylece eklenirse **her Firma Admini otomatik olarak** kullanabilir hale gelir.
Kullanıcının isteği ("açıkça verilmedikçe kimse almasın") bununla çelişir.

### YET-03 ORTA — "İlk admin her şeyi verebilir" kuralı aynı deliği açıyor
`AccessControl.cs:125` ve `:134` → `if (s.IsCompanyAdmin && !HasAnyExplicit(s)) return true;`
Hiç açık yetki satırı olmayan bir firma admini **her modülü ve her düğmeyi** devredebilir.

### YET-04 ORTA — Rol Yetki Kontrol yalnız modülleri kapsıyor
`RoleGrantService` matrisi `AppModules.All` üzerinden kuruludur; `SpecialButtons` **dahil değildir**.
Yani "bu yetki Personel rolüne verilemesin" kuralı düğmeler için ifade edilemez.

### YET-01 ORTA — Devretme zinciri kavramı mevcut ama eksik katman var
`CanGrantButton` / `CanGrantModule` zaten **"kendinde olmayanı veremezsin"** ilkesini uygular
(`AccessControl.cs:110-135`). Eksik olan tek şey: "devredilebilir **ama** asla örtük verilmeyen"
ara katman. Bugün yalnız iki uç vardır:
- `IsSuperAdminOnly` → hiç devredilemez
- normal modül / düğme → admin bypass ile herkese örtük açık

---

## 5. ÖNERİ

### 5.1 Yetki modeli — yeni "AÇIK-VERİLİR" katmanı
Kullanıcının isteği ("süper admin **veya kısıtlı süper admin** bu yetkiyi bir role verirse,
o rol de alt rollerine verebilsin") tek bir yeni kavramla tam karşılanır:

**`AppModules.IsExplicitOnly(moduleKey)`** — bu modül için:
- `AccessControl.Can` içindeki **admin bypass'ı geçersizdir** (açıkça verilmedikçe kimse alamaz)
- `CanGrantModule` → `IsSuperAdmin || IsRestrictedSuperAdmin || (kendisinde açıkça var)`
- `IsCompanyAdmin && !HasAnyExplicit` kestirmesi bu modüller için **uygulanmaz**

Bu, istenen zinciri kendiliğinden üretir:
`Süper Admin / Kısıtlı Süper Admin → Admin → Personel` — her kademe yalnız **kendisinde olanı**
aşağı verebilir.

### 5.2 Düğme değil, MODÜL olsun
"Yerel Veri Sıfırlama" bir **özel düğme** değil, kendi **modülü + kendi ekranı** olmalıdır. Gerekçe:
1. Kullanıcının ifadesi birebir karşılanır: yetki ağacında **menü maddesi** olarak görünür.
2. **Rol Yetki Kontrol** matrisine otomatik girer → rol bazlı yasak konabilir (YET-04 çözülür).
3. Bugün düğme süper-admin-only bir ekranın (Firmalar) içindedir; yetki verilen kişi o ekrana
   giremediği için düğmeye **ulaşamaz**. Kendi ekranı bu sorunu kökten çözer.
4. Audit ve menü altyapısı modüller için zaten hazırdır.

Önerilen anahtar: `local_reset` — "Yerel Veri Sıfırlama" · `IsExplicitOnly` · web ekranı
(`purge_company` ve `reset_company_business` ile aynı grup: "Web Yönetimi").

### 5.3 Uygulama sırası (önerilen)

| # | İş | Neden bu sırada |
|---|---|---|
| 1 | **SIF-01** — doğru fonksiyon çağrısı + regresyon testi | Kullanıcının şartını bozan tek hata; sıfırlamadan ÖNCE çözülmeli |
| 2 | **SIF-03** — eksik 7 tablo + çocuk tablolar temizliğe eklensin | 1 ile aynı dosya / aynı test |
| 3 | **ŞB-01** — üst şube + tür masaüstüne taşınsın (3 nokta) | Kullanıcının bildirdiği hata |
| 4 | **ŞB-03 / ŞB-02 / ŞB-06** — silinen üst şube, döngü, yanlış firma | Şube yapısının tutarlılığı |
| 5 | **YET (5.1 + 5.2)** — yeni modül + açık-verilir katmanı | Şube / sıfırlama düzelmeden yetki açmak riskli |
| 6 | **SIF-02** — açık oturumda da kontrol | Daha büyük; ayrı iş olarak önerilir |

**ŞB-04** (üst şubenin işlevsel hale gelmesi — alt şubeleri görme / toplama) ve **SIF-06**
(şablon senkronu) kullanıcı kararı bekliyor: ikisi de **yeni davranış** getirir, hata düzeltmesi değildir.

---

## 6. DEĞİŞMEYECEKLER (koruma listesi)
- Sunucudaki firma / şube / kullanıcı / rol / yetki verisi — sıfırlama bunlara dokunmaz (SIF-04)
- ADR-083 "Kalıcı Silme" akışı — bu analizde değişiklik önerilmiyor
- `PurgeLocalCompany` fonksiyonunun kendisi — yalnız yanlış çağrı yeri düzeltilecek
