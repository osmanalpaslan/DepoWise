# DEPOWISE / ALPNEX — ANA GELİŞTİRME PLANI

> **Bu dosya nedir?** Bundan sonraki bütün geliştirmelerin ana çalışma dosyası.
> "Şu anda nerede kaldık?" sorusunun cevabı en üstteki durum bloğundadır.
> Mevcut durum fotoğrafı için: [PROJE_GENEL_DURUM_ANALIZI.md](PROJE_GENEL_DURUM_ANALIZI.md)

---

```
AKTİF AŞAMA:         FAZ 0 — Canlıya geçiş öncesi zorunlu düzeltmeler
AKTİF İŞ ID:         MLZ-01
AKTİF İŞ:            Malzeme silmede stok/kullanım koruması
DURUM:               GELİŞTİRMEYE HAZIR
SON TAMAMLANAN İŞ:   (yok — plan yeni oluşturuldu)
SONRAKİ İŞ:          KLT-01 — Eksik düzenleme kilitleri (yakıt, stok belgeleri, muayene)
BEKLEYEN KARAR:      KARAR-4 (bakımda negatif stok ↔ onay akışı çelişkisi) — FAZ 5'e kadar beklenebilir
SON GÜNCELLEME:      2026-08-10
```

**Kullanıcı tarafında paralel yürüyen görevler (kod işi değil):**
- `GUV-01` — Süper admin parolası değişimi ⚠️ **acil**
- `DOG-01` — Normal kullanıcıyla web girişi testi

---

## 1. PROJENİN AMACI

**DepoWise / Alpnex** — çok firmalı (multi-tenant) depo, stok, araç, bakım, yakıt ve
günlük faaliyet yönetim sistemi.

Başlangıçta tek kişinin (kullanıcının babasının) kullanımı için minimalist tasarlandı.
**Bugünkü hedef:** çok kullanıcılı, çok şubeli, web + masaüstü çalışan, farklı firmalara
satılabilecek ticari ürün.

**Kısıt:** Maddi imkanlar sınırlı. Her çözüm mevcut mimariyi koruyarak, minimum maliyetle,
minimum karmaşıklıkla ve büyümeyi engellemeyecek şekilde seçilir.

---

## 2. MEVCUT MİMARİ ÖZETİ

```
   Masaüstü (Avalonia, .NET 8)          Web (Blazor Server, MudBlazor)
   YEREL SQLite — çevrimdışı çalışır     kendi iş mantığı YOK
            │                                      │
            └──── 15 sn periyodik senkron ─────────┤
                                                   │
                        ┌──────────────────────────▼─────┐
                        │  API — Fly.io, 249 uç          │
                        │  PostgreSQL (Neon)             │
                        └────────────────────────────────┘
                                     │
                   DepoWise.Infrastructure — İŞ KURALLARI TEK YERDE
                   (hem API hem masaüstü aynı servisleri çağırır)
```

**Korunacak temel güçler:**
- İş kuralları tek yerde → web ve masaüstü **yapısal olarak** aynı davranır.
- Deny-by-default yetki, fail-closed şube kapsamı (`ScopeResolver`).
- Çevrimdışı masaüstü — ürünün ana özelliği, **hiçbir çözüm bunu bozmamalı**.
- 62 migration, boşluksuz, çift lehçe (SQLite + PostgreSQL) test ediliyor.
- Operasyonel kayıtta fiziksel silme yok (soft delete + Çöp Kutusu).

---

## 3. KULLANICININ KESİN PROJE HEDEFLERİ

Bunlar **karar verilmiş** hedeflerdir; yeniden tartışılmaz.

| # | Hedef | Analiz önerisiyle durum |
|---|---|---|
| H-1 | **Stoklar şube bazlı olacak** | Analiz "karar sorusu" demişti → **KARARLAŞTIRILDI** |
| H-2 | **Gerçek kayıt kilidi** — ikinci kullanıcı kayda giremeyecek, kilit sahibinin adı ve başlangıç saati görünecek | Analiz "soft warning yeter" demişti → **KULLANICI KARARI ÜSTÜN, plan değiştirildi** |
| H-3 | Tüm yetkili kullanıcılar web'e girebilmeli | Kodda kısıt yok → **yalnız doğrulama görevi** |
| H-4 | Yetki ağacı genişletilecek (Approve/Cancel/kayıt tipi/birim) — baştan yazılmayacak | Analizle uyumlu |
| H-5 | Personel birimi eklenecek (tek kaynak: `personnel`) | Analizle uyumlu |
| H-6 | Bakım → onay → stok akışı | Analizle uyumlu, sıraya kondu |
| H-7 | Günlük Faaliyet: kayıt tipi yetkisi + mükerrer uyarı (sunucu taraflı) | Analizle uyumlu |
| H-8 | Kullanıcı karar logları — ertelenebilir ama mimari buna kapanmamalı | Analizle uyumlu |
| H-9 | Performans: sunucu yoğunlukta çökmesin; ama gereksiz altyapı kurulmayacak | Analizle uyumlu |
| H-10 | Senkron: WebSocket YOK, periyodik sistem akıllandırılacak | Analizle uyumlu |
| H-11 | Otomatik güncelleme: izin sormadan indir/kur, yalnız yeniden başlatmayı sor | Analizle uyumlu |
| H-12 | Kod temizliği yalnız gerçek risk/borç için | Analizle uyumlu |

---

## 4. TEMEL MİMARİ KARARLAR

### KARAR-1 — Malzeme kataloğu firma genelinde KALIR, stok şube bazlı OLUR ✅

**Karar:** `materials` tablosuna `branch_id` **EKLENMEYECEK**. Şube boyutu yalnız
`stock_balances` ve `stock_movements` tarafına eklenecek.

**Neden (teknik olarak açık, kullanıcıya sorulmadı):**
- Bu, standart ERP desenidir: **tek ürün kataloğu + depo/şube bazlı stok**.
- Malzemeyi şube bazlı yaparsak aynı "Filtre Yağı" her şubede ayrı kayıt olur →
  raporlar bölünür, muadil eşleştirme bozulur, kullanıcı aynı malzemeyi 5 kez tanımlar.
- Kullanıcının §1.1'deki asıl derdi — *"bir şubenin işlemi diğerinin stok verisini
  değiştirmesin"* — **stok tarafını ayırmakla tamamen çözülür**, katalogu bölmeye gerek yok.
- Katalog paylaşımlı kaldığı için migration çok daha küçük ve risksiz olur.

**Sonuç:** Malzeme **silme** işlemi katalog düzeyinde kalır → "hiçbir şubede stok yoksa
silinebilir" kuralı uygulanır (bkz. MLZ-01, sonra STK-05'te şube bazlı hale gelir).

---

### KARAR-2 — Stok geçişi 6 fazda, canlı veri korunarak ✅

Tek seferde uygulanmayacak. Her faz tek başına geri alınabilir olacak.
Faz sırası §5'te `STK-01…STK-06`.

**Altın kural:** Babanın canlı verisi hiçbir fazda silinmez/dönüştürülmez;
yeni yapı eskinin **yanına** kurulur, doğrulanır, sonra okuma taşınır.

---

### KARAR-3 — Kayıt kilidi: KİRALAMA (lease) tabanlı GERÇEK kilit 🔴 ÖNEMLİ

Kullanıcı soft warning'i reddetti. Analiz önerisi **değiştirildi**. Yeni tasarım:

**Nasıl çalışır:**
1. Kullanıcı kaydı düzenlemeye açar → istemci sunucuya **kilit ister**.
2. Kilit boşsa alınır; kayıt `(kayıt_id, kullanıcı, kullanıcı_adı, başlangıç, son_geçerlilik)`.
3. Kilit doluysa istemci **409** alır → **kayda girilemez**, ekranda gösterilir:
   *"Bu kaydı şu anda **Ahmet Yılmaz** düzenliyor (14:32'den beri)."*
4. Düzenleme penceresi açıkken istemci ~45 saniyede bir **kilidi uzatır** (heartbeat).
5. Kaydet / İptal / pencere kapat → kilit **hemen bırakılır**.

**Stale lock (takılı kalan kilit) nasıl çözülür — §13'teki 6 senaryonun hepsi:**

| Senaryo | Ne olur |
|---|---|
| Uygulama çöktü | Heartbeat durur → kilit ~2 dk sonra **kendiliğinden düşer** |
| Ağ koptu | Aynı — kendiliğinden düşer |
| Bilgisayar kapandı | Aynı |
| Logout | Kilit **açıkça** bırakılır (anında) |
| Session timeout | Kilit açıkça bırakılır |
| Uygulama normal kapandı | Kilit açıkça bırakılır |

**Kritik nokta:** Kilidin **süresi vardır** (lease). Bu yüzden ayrı bir "takılı kilit
temizleme" işi, zamanlanmış görev veya bakım yükü **gerekmez**. Kilit kendi kendini iyileştirir.
Analizde pessimistic lock'a karşı çıkma gerekçesi buydu; kiralama modeli o gerekçeyi ortadan kaldırır.

**⚠️ DÜRÜST SINIR — bu net anlaşılmalı:**

> **Çevrimdışı masaüstü kilitlenemez.** Ağ yokken sunucuya ulaşılamaz; hiçbir yazılım
> internetsiz bir bilgisayarı uzaktan engelleyemez. Bu bir tasarım tercihi değil, fiziksel sınırdır.

Bu durumda ne olur:
- Çevrimdışı masaüstü düzenlemeye **izin verir** (çevrimdışı çalışma korunur — H-10/ürün özelliği).
- Kayıt eşitlenirken **mevcut optimistic koruma** (`version` → 409) devreye girer,
  çakışma yakalanır ve kullanıcı uyarılır. Yani veri sessizce ezilmez.
- Çevrimdışı düzenlenen kayıt, eşitlemede **"çevrimdışı düzenlendi"** işaretiyle gelir.

**Gerçekte ne kadarı karşılanır:** Kullanıcıların **çevrimiçi olduğu her durumda** (web ↔ web,
web ↔ çevrimiçi masaüstü, masaüstü ↔ masaüstü) istenen davranış **birebir** çalışır.
Karşılanamayan tek durum, bir tarafın **tamamen çevrimdışı** olmasıdır.
Pratikte bu, senaryoların büyük çoğunluğunu kapsar.

---

### KARAR-4 — Bakımda negatif stok ↔ onay akışı çelişkisi ⏳ KARAR BEKLİYOR

Mevcut kural (DEVAM.md, Birim 8): bakımda yetersiz stok **engellenmiyor**, stok eksiye düşebiliyor.
Hedeflenen akış (H-6): stok düşümü **onaya** bağlanacak.

İkisi çelişiyor: onay beklerken stok düşmeyecekse, "negatif stok serbest" ne anlama gelir?

**Bu karar FAZ 5'e kadar beklenebilir.** O aşamada §15'te soru olarak açılacak.

---

### KARAR-5 — Şu aşamada kurulmayacak altyapılar ✅

Queue · background worker · Redis · WebSocket/SignalR · harici monitoring · ücretli servisler.
**Gerekçe:** Analizde gerçek ihtiyaç bulunmadı; mevcut yük buna uzak. Yatırım sonrasına ertelendi.

---

## 5. ŞU ANDA YAPILACAK İŞLER

### FAZ 0 — Canlıya geçiş öncesi ZORUNLU (P0)

---

**ID:** `GUV-01`
**Başlık:** Süper admin parolasının değiştirilmesi
**Açıklama:** Yayın scriptinin kullandığı süper admin parolası zayıf ve canlıda çalıştığı doğrulandı.
**Neden gerekli:** Süper adminle her firmaya erişilebiliyor. Aktif güvenlik açığı.
**Öncelik:** P0 · **Bağımlılık:** Yok
**Web:** — · **Masaüstü:** — · **API:** — · **Veritabanı:** parola hash'i güncellenir
**Migration:** ❌ · **Canlı veri riski:** Yok · **Kullanıcı etkisi:** Yok
**Maliyet:** Çok düşük · **Şimdi/Ertelenmiş:** **ŞİMDİ**
**Önce yapılması gereken:** Yok · **Sonraki adım:** DOG-01
**Test gereksinimi:** Yeni parolayla giriş + yayın scriptinin çalıştığının doğrulanması
**DURUM:** `KARAR BEKLİYOR` *(kullanıcı aksiyonu — Claude parola giremez)*

---

**ID:** `DOG-01`
**Başlık:** Normal kullanıcıyla web girişi doğrulaması
**Açıklama:** "Sadece admin web'e girebiliyor" varsayımı koddan doğrulanamadı; kısıt **bulunamadı**.
Gerçek bir normal kullanıcıyla giriş denenip ne olduğu gözlenecek.
**Neden gerekli:** Olmayan bir problemi kodla çözmemek için. Sorun muhtemelen yetki verisinde
(kullanıcı giriyor ama menüsü boş) veya şube kapsamında.
**Öncelik:** P1 · **Bağımlılık:** Yok
**Web:** ✅ doğrulama · **Masaüstü:** — · **API:** — · **Veritabanı:** —
**Migration:** ❌ · **Canlı veri riski:** Yok · **Maliyet:** Çok düşük
**Şimdi/Ertelenmiş:** **ŞİMDİ** · **Sonraki adım:** sonuca göre yeni iş açılabilir
**Test gereksinimi:** Bir normal kullanıcıyla web'e giriş; menü/veri görünürlüğü gözlemi
**DURUM:** `BEKLEMEDE` *(kullanıcı aksiyonu)*

---

**ID:** `MLZ-01` 🔵 **AKTİF İŞ**
**Başlık:** Malzeme silmede stok ve kullanım koruması
**Açıklama:** Malzeme silme yalnız yetki kontrol ediyor. Stok bakiyesi ve kullanım kontrolü eklenecek.
**Neden gerekli:** Silme yetkisi olan herhangi bir kullanıcı, tüm firmanın kullandığı malzemeyi
listeden düşürebiliyor. Soft delete olduğu için kurtarılabilir ama operasyon durur.
**Öncelik:** **P0** · **Bağımlılık:** Yok
**Web:** ✅ hata mesajı · **Masaüstü:** ✅ hata mesajı · **API:** ✅ `MaterialService.Delete`
**Veritabanı:** Okuma · **Migration:** ❌ · **Canlı veri riski:** Yok (koruma ekliyor)
**Kullanıcı etkisi:** Orta — stoklu malzeme artık silinemez (istenen davranış)
**Maliyet:** Düşük · **Şimdi/Ertelenmiş:** **ŞİMDİ**
**Önce yapılması gereken:** Yok · **Sonraki adım:** KLT-01
**Test gereksinimi:** Stoklu malzeme silinemez · stoksuz silinebilir · hareketi olan uyarır ·
her iki platformda aynı mesaj · yetki kontrolü bozulmadı
**Not:** STK-05 sonrası bu kontrol "hiçbir şubede stok yoksa" haline gelecek.
**DURUM:** `GELİŞTİRMEYE HAZIR`

---

**ID:** `KLT-01`
**Başlık:** Eksik düzenleme kilitleri (optimistic) — yakıt, stok belgeleri, muayene, kullanıcılar
**Açıklama:** `EditLockGuard` 8 serviste var; yakıt, stok belgeleri, muayene ve kullanıcılarda yok.
**Neden gerekli:** CLAUDE.md §4 "stok, sayaç, yakıt, bakım ve onayda LWW yasaktır" kuralı
şu an **ihlal ediliyor**. İki kullanıcı aynı yakıt kaydını düzenlerse ikincisi birincisini sessizce eziyor.
**Öncelik:** **P0** · **Bağımlılık:** Yok
**Web:** ✅ · **Masaüstü:** ✅ · **API:** ✅ · **Veritabanı:** `version` kolonu (çoğunda mevcut)
**Migration:** ⚠️ `version` kolonu olmayan tablo varsa küçük additive — **önce doğrulanacak**
**Canlı veri riski:** Yok · **Maliyet:** Düşük (desen 8 serviste kanıtlı)
**Şimdi/Ertelenmiş:** **ŞİMDİ** · **Sonraki adım:** SNK-01
**Test gereksinimi:** Her ekran için "iki sekmeden kaydet → ikincisi 409" testi
**DURUM:** `BEKLEMEDE`

---

### FAZ 1 — Düşük maliyet, yüksek getiri (P1)

---

**ID:** `SNK-01`
**Başlık:** Değişiklik yoksa push yapma
**Açıklama:** Yerel değişiklik yoksa push HTTP isteği hiç yapılmasın.
**Neden gerekli:** İstemci başına 15 sn'de ~5-6 istek üretiliyor; çoğu boşa.
**Öncelik:** P1 · **Bağımlılık:** Yok · **Masaüstü:** ✅ (`ShellViewModel`, `BusinessSyncPushService`)
**Migration:** ❌ · **Canlı veri riski:** Yok · **Maliyet:** Çok düşük
**Test gereksinimi:** Değişiklik yokken ağ trafiği sıfır; değişiklik varken push çalışıyor
**DURUM:** `BEKLEMEDE`

**ID:** `SNK-02`
**Başlık:** Boştayken senkron aralığını seyreltme (15 sn → 60 sn)
**Açıklama:** Kullanıcı 5 dk işlem yapmadıysa aralık açılır; işlem yapınca hemen sıkılaşır.
**Neden gerekli:** Sunucu yükünü tek başına ~4 kat düşürür. **En yüksek getiri/maliyet oranlı iş.**
**Öncelik:** P1 · **Bağımlılık:** Yok · **Masaüstü:** ✅ · **Migration:** ❌
**Maliyet:** Çok düşük · **Test:** Boşta seyrelme · işlem sonrası hızlanma · veri gecikmesi kabul edilebilir
**DURUM:** `BEKLEMEDE`

**ID:** `SNK-03`
**Başlık:** Hata halinde exponential backoff
**Açıklama:** Sunucu hata verirse 15 sn'de bir tekrar denemek yükü artırır; aralık kademeli açılır.
**Neden gerekli:** Sunucu zorlanırken istemcilerin onu daha da zorlaması engellenir (§1.9).
**Öncelik:** P1 · **Bağımlılık:** SNK-02 · **Masaüstü:** ✅ · **Migration:** ❌ · **Maliyet:** Düşük
**Test:** Sunucu kapalıyken aralığın açıldığı, geri gelince toparladığı
**DURUM:** `BEKLEMEDE`

**ID:** `SNK-04`
**Başlık:** Günlük yedek kontrolünü senkron turundan ayırma
**Açıklama:** `MaybeDailyBackupAsync` her 15 sn'de çalışıyor; saatte bir yeterli.
**Öncelik:** P2 · **Bağımlılık:** Yok · **Masaüstü:** ✅ · **Migration:** ❌ · **Maliyet:** Çok düşük
**DURUM:** `BEKLEMEDE`

**ID:** `PRT-01`
**Başlık:** Tam ekran parite denetimi (alan/işlev düzeyinde)
**Açıklama:** 43 web + 36 masaüstü ekranın alan, işlev, validasyon, yetki düzeyinde karşılaştırılması.
Analizde yalnız **ad düzeyinde** yapılabildi.
**Neden gerekli:** "Ortak olması gerekirken eksik" ekranlar/alanlar ancak böyle bulunur.
Sonucu yeni iş kalemleri doğurur.
**Öncelik:** P1 · **Bağımlılık:** Yok · **Migration:** ❌ · **Maliyet:** Orta
**Çıktı:** `PROJE_GENEL_DURUM_ANALIZI.md` §4-5 güncellenir + yeni işler bu dosyaya eklenir
**DURUM:** `ANALİZ BEKLİYOR`

**ID:** `PRT-02`
**Başlık:** Ekran adı eşleme tablosu
**Açıklama:** `Dashboard`/`Home`, `StockEntry`/`Stock`, `AuditLog`/`Audit` gibi tutarsız adlar için
`moduleKey` üzerinden tek eşleme tablosu. **Yeniden adlandırma yapılmaz**, sadece eşleme.
**Öncelik:** P2 · **Bağımlılık:** PRT-01 · **Maliyet:** Çok düşük
**DURUM:** `BEKLEMEDE`

---

### FAZ 2 — Yetki ağacı genişletme (P1)

> Mevcut yetki sistemi **çöpe atılmıyor**. deny-by-default, company/branch scope, rol, modül,
> View/Create/Edit/Delete ve özel buton mekanizması **aynen korunuyor**; üzerine ekleniyor.

---

**ID:** `BRM-01`
**Başlık:** Personel birimi altyapısı
**Açıklama:** `personnel_units` tanım tablosu + `personnel.unit_id` (nullable).
`users` tablosuna **ekleme yapılmaz** — kullanıcı zaten `personnel_id` ile bağlı, birim oradan okunur.
**Neden gerekli:** Raporlama, personel gruplama, birim bazlı operasyon, gelecekte yetki kapsamı (H-5).
**⚠️ İsimlendirme:** Projede `units` = **ölçü birimi** (adet, kg). Çakışma olmaması için
yeni tablo `personnel_units` olacak; `units` adı **kullanılmayacak**.
**Öncelik:** P1 · **Bağımlılık:** Yok
**Web:** ✅ personel formu + filtre · **Masaüstü:** ✅ aynı · **API:** ✅ · **Veritabanı:** ✅
**Migration:** ✅ **additive** (yeni tablo + nullable kolon) · **Canlı veri riski:** Yok
**Maliyet:** Düşük · **Sonraki adım:** YTK-01
**Test:** Birim atanmış/atanmamış personel · rapor kırılımı · senkron listesine eklenmesi
**DURUM:** `BEKLEMEDE`

---

**ID:** `YTK-01`
**Başlık:** `PermissionAction`'a Approve ve Cancel eklenmesi
**Açıklama:** `permissions` tablosuna `can_approve`, `can_cancel` kolonları (varsayılan 0).
`AccessControl` bunları deny-by-default değerlendirir.
**Neden gerekli:** Bugün `btn-approve` **tek global buton** — "bakımı onaylar ama talebi onaylamaz"
ifade edilemiyor. Bakım onayı ve stok kritik işlemleri buna bağımlı.
**Öncelik:** **P1** · **Bağımlılık:** Yok
**Web:** ✅ yetki ağacı UI · **Masaüstü:** ✅ aynı · **API:** ✅ · **Veritabanı:** ✅
**Migration:** ✅ **additive, küçük** · **Canlı veri riski:** Yok — mevcut yetkiler aynen korunur
(yeni kolon 0 = deny, deny-by-default zaten bunu bekliyor)
**Maliyet:** Düşük · **Sonraki adım:** YTK-02
**Test:** Mevcut yetkiler bozulmadı · yeni yetki verilmeden onay yapılamıyor ·
UI'da gizli onay API'den de reddediliyor
**DURUM:** `BEKLEMEDE`

---

**ID:** `YTK-02`
**Başlık:** Günlük Faaliyet kayıt tipi bazlı yetki
**Açıklama:** Kayıt tipleri ayrı yetkilendirilebilecek. **Yeni tablo/migration gerekmez** —
mevcut özel buton mekanizması (`SpecialButtons`) `btn-daily-<kayittipi>` anahtarlarıyla kullanılır.
**Neden gerekli:** H-7. Depocu yalnız malzeme çıkışı girsin, bakım kaydı giremesin gibi.
**Öncelik:** P1 · **Bağımlılık:** YTK-01
**Migration:** ❌ · **Canlı veri riski:** Yok · **Maliyet:** Düşük
**Test:** Her kayıt tipi için ayrı yetki · yetkisiz tip UI'da gizli **ve** API'de reddediliyor
**DURUM:** `BEKLEMEDE`

**ID:** `YTK-03`
**Başlık:** Stok kritik işlemleri için ayrı yetkiler
**Açıklama:** Ters kayıt, sayım düzeltmesi, manuel stok müdahalesi gibi işlemler ayrı yetkilendirilir.
**Öncelik:** P1 · **Bağımlılık:** YTK-01 · **Migration:** ❌ (buton mekanizması) · **Maliyet:** Düşük
**DURUM:** `BEKLEMEDE`

**ID:** `YTK-04`
**Başlık:** Yetki ağacı UI'ının yeni yetkileri göstermesi
**Açıklama:** Yeni eylemler ve kayıt tipi yetkileri yetki ağacı ekranına eklenir (web + masaüstü).
**⚠️ Proje kuralı:** Yeni yetki gerektiren her özellik Yetki Ağacına **hatırlatma beklemeden** eklenir.
**Öncelik:** P1 · **Bağımlılık:** YTK-01, YTK-02, YTK-03 · **Maliyet:** Orta
**DURUM:** `BEKLEMEDE`

---

### FAZ 3 — Gerçek kayıt kilidi (P1)

> Tasarım: **KARAR-3** (kiralama tabanlı kilit). Çevrimdışı sınırı orada açıkça yazılıdır.

---

**ID:** `KLT-02`
**Başlık:** Kilit altyapısı — sunucu tarafı
**Açıklama:** `record_locks` tablosu (`table_name`, `record_id`, `user_id`, `user_name`,
`acquired_at`, `expires_at`, `machine_id`) + `acquire` / `heartbeat` / `release` uçları.
Süresi geçmiş kilit **otomatik geçersiz** sayılır (ayrı temizlik işi yok).
**Neden gerekli:** H-2 — kullanıcının açık isteği gerçek engellemedir.
**Öncelik:** **P1** · **Bağımlılık:** KLT-01
**API:** ✅ 3 yeni uç · **Veritabanı:** ✅ · **Migration:** ✅ **additive** (1 yeni tablo)
**Canlı veri riski:** Yok · **Maliyet:** Orta
**Test:** İkinci kullanıcı 409 alıyor · heartbeat kilidi uzatıyor · heartbeat kesilince süre dolunca
düşüyor · release anında bırakıyor · **aynı kullanıcının ikinci sekmesi kendi kilidini görebiliyor**
**DURUM:** `BEKLEMEDE`

**ID:** `KLT-03`
**Başlık:** Web tarafı kilit entegrasyonu
**Açıklama:** Düzenleme diyaloğu açılırken kilit istenir; alınamazsa **açılmaz** ve
*"Bu kaydı şu anda Ahmet Yılmaz düzenliyor (14:32'den beri)"* gösterilir.
Pencere kapanınca/kaydedilince kilit bırakılır.
**Öncelik:** P1 · **Bağımlılık:** KLT-02 · **Web:** ✅ · **Migration:** ❌ · **Maliyet:** Orta
**Test:** İki tarayıcı sekmesi · sekme kapatınca kilit düşüyor · tarayıcı çökünce süre dolunca düşüyor
**DURUM:** `BEKLEMEDE`

**ID:** `KLT-04`
**Başlık:** Masaüstü kilit entegrasyonu (çevrimiçi) + çevrimdışı davranışı
**Açıklama:** Çevrimiçiyken web ile aynı davranış. **Çevrimdışıyken düzenlemeye izin verilir**
(çevrimdışı çalışma korunur) ve kayıt "çevrimdışı düzenlendi" işaretiyle eşitlenir;
çakışma mevcut `version` korumasıyla yakalanır.
**⚠️ Bu, KARAR-3'teki dürüst sınırın uygulamasıdır.**
**Öncelik:** P1 · **Bağımlılık:** KLT-02 · **Masaüstü:** ✅ · **Migration:** ❌ · **Maliyet:** Orta
**Test:** Çevrimiçi engelleme · çevrimdışı düzenleme çalışıyor · eşitlemede çakışma yakalanıyor ·
**ağ ortasında koparsa uygulama kilitlenmiyor**
**DURUM:** `BEKLEMEDE`

---

### FAZ 4 — Şube bazlı stok (P0/P1 — en büyük iş) 🔴

> **Canlı veriye dokunan tek iş grubudur.** Her faz ayrı onay, ayrı deploy, ayrı doğrulama ister.
> KARAR-1: malzeme kataloğu firma genelinde kalır; yalnız stok şube bazlı olur.

---

**ID:** `STK-01`
**Başlık:** Şema hazırlığı
**Açıklama:** `stock_balances`'a `branch_id` eklenir (önce **nullable**), PK
`(material_id)` → `(material_id, branch_id)` hazırlığı yapılır. `stock_movements.branch_id`
NOT NULL'a hazırlanır. **Hiçbir okuma/yazma yolu değişmez.**
**Neden gerekli:** H-1 — çok şubeli ürünün temeli.
**Öncelik:** **P0** · **Bağımlılık:** MLZ-01, KLT-01 (önce ucuz P0'lar bitsin)
**Migration:** ✅ **additive** · **Canlı veri riski:** **Düşük** (yalnız kolon ekleme)
**Maliyet:** Orta · **Test:** Migration iki lehçede (SQLite + PostgreSQL) çalışıyor · mevcut işlevler bozulmadı
**DURUM:** `BEKLEMEDE`

**ID:** `STK-02`
**Başlık:** Mevcut verinin güvenli dönüşüm planı
**Açıklama:** Mevcut bakiyeler ve `branch_id` NULL hareketler hangi şubeye atanacak?
(Öneri: firmanın **ana/varsayılan şubesi**.) Plan yazılır, **kopya veri üzerinde prova edilir**.
**⚠️ Bu faz canlı veriye DOKUNMAZ** — yalnız plan + prova.
**Öncelik:** P0 · **Bağımlılık:** STK-01 · **Migration:** ❌ (henüz)
**Canlı veri riski:** Yok (kopya üzerinde) · **Maliyet:** Orta
**Test:** Babanın verisinin kopyasında dönüşüm provası · toplam bakiye dönüşüm öncesi = sonrası
**DURUM:** `BEKLEMEDE`

**ID:** `STK-03`
**Başlık:** Çift yazım (eski + yeni birlikte)
**Açıklama:** Stok hareketleri hem eski (firma geneli) hem yeni (şube bazlı) yapıya yazılır.
Okumalar **hâlâ eskiden** yapılır. Böylece yeni yapı gerçek veriyle dolar ama hiçbir şey riske girmez.
**Öncelik:** P0 · **Bağımlılık:** STK-02 · **Migration:** ❌
**Canlı veri riski:** **Düşük** (yeni yapı yazılıyor, eski bozulmuyor) · **Maliyet:** Yüksek
**Test:** Her stok işlemi iki yapıya da doğru yazıyor
**DURUM:** `BEKLEMEDE`

**ID:** `STK-04`
**Başlık:** Doğrulama
**Açıklama:** Belirli bir süre çift yazım sonrası eski ve yeni yapının **birbirini tuttuğu** doğrulanır.
Fark varsa okuma geçişi **yapılmaz**.
**Öncelik:** P0 · **Bağımlılık:** STK-03 · **Migration:** ❌ · **Canlı veri riski:** Yok
**Maliyet:** Düşük · **Test:** Karşılaştırma raporu — şube toplamları = firma toplamı
**DURUM:** `BEKLEMEDE`

**ID:** `STK-05`
**Başlık:** Okumaların şube bazlı hale getirilmesi
**Açıklama:** Stok listeleri, raporlar, uyarılar, malzeme silme kontrolü (MLZ-01) şube bazlı okur.
**Kullanıcı bu fazda değişikliği görür.**
**Öncelik:** P0 · **Bağımlılık:** STK-04 · **Web:** ✅ · **Masaüstü:** ✅ · **API:** ✅
**Migration:** ❌ · **Canlı veri riski:** **Orta** — geri dönüş planı hazır olmalı
**Maliyet:** Yüksek · **Test:** Her ekranda şube bazlı doğru rakam · şube değiştirince rakam değişiyor ·
yetkisiz şube verisi görünmüyor · **ID göndererek başka şubeye erişilemiyor**
**DURUM:** `BEKLEMEDE`

**ID:** `STK-06`
**Başlık:** Eski yapının kaldırılması
**Açıklama:** Çift yazım durdurulur, eski firma-geneli bakiye alanı kaldırılır.
**Öncelik:** P1 · **Bağımlılık:** STK-05 (+ en az birkaç hafta sorunsuz çalışma)
**Migration:** ✅ · **Canlı veri riski:** **Orta** · **Maliyet:** Düşük
**Not:** Acele edilmez. Eski yapı bir süre daha "geri dönüş sigortası" olarak durur.
**DURUM:** `BEKLEMEDE`

---

### FAZ 5 — İş akışları (P1)

**ID:** `GNL-01`
**Başlık:** Günlük Faaliyet mükerrer kayıt uyarısı
**Açıklama:** Aynı tarih + aynı araç + aynı önemli bilgilerle kayıt varsa uyarı.
Butonlar: **Kaydı görüntüle** (yeni pencerede açar) · **Yine de devam et**.
**⚠️ Kontrol SUNUCUDA yapılır**, istemcide değil. "Yine de devam et" ikinci çağrıda
`allowDuplicate=true` bayrağıyla gider → sunucu kontrolü atlar → **sonsuz döngü olmaz**.
**⚠️ UNIQUE kısıtı KONULMAZ** — aynı gün aynı araca ikinci kayıt meşru olabilir (sabah/öğleden sonra).
**Öncelik:** P1 · **Bağımlılık:** Yok · **Migration:** ❌ · **Maliyet:** Düşük
**Test:** Mükerrer uyarı çıkıyor · "görüntüle" doğru kaydı açıyor · "devam et" kaydediyor ve
tekrar engellemiyor · **istemci bayrağı göndermeden sunucu koruması çalışıyor**
**DURUM:** `BEKLEMEDE`

**ID:** `GNL-02`
**Başlık:** Birim bazlı kayıt tipleri
**Açıklama:** Kayıt tipleri personel birimine göre filtrelenebilir.
**Öncelik:** P2 · **Bağımlılık:** BRM-01, YTK-02 · **Maliyet:** Orta
**DURUM:** `BEKLEMEDE`

**ID:** `BKM-01`
**Başlık:** Bakım onay durumu
**Açıklama:** `vehicle_maintenances`'a onay durumu kolonu (bekliyor/onaylandı/reddedildi) + onaylayan/tarih.
**Öncelik:** P1 · **Bağımlılık:** YTK-01 · **Migration:** ✅ additive · **Maliyet:** Orta
**DURUM:** `BEKLEMEDE`

**ID:** `BKM-02`
**Başlık:** Stok düşümünün onaya bağlanması
**Açıklama:** Bakım kaydı oluşunca stok **düşmez**; yetkili onaylayınca stok hareketi oluşur.
**Neden gerekli:** H-6 — bakım personeli doğrudan stok düşürmemeli.
**Öncelik:** P1 · **Bağımlılık:** BKM-01, **KARAR-4** · **Migration:** ❌
**Canlı veri riski:** **Orta** — stok akışı değişiyor · **Maliyet:** Orta
**Test:** Onaysız bakımda stok düşmüyor · onayda düşüyor · onay geri alınınca ters kayıt ·
idempotent (çift onay çift düşürmüyor) · audit kaydı oluşuyor
**DURUM:** `KARAR BEKLİYOR`

**ID:** `BKM-03`
**Başlık:** Negatif stok kuralının onay akışıyla uyumlanması
**Açıklama:** KARAR-4 sonucuna göre mevcut "negatif stok serbest" kuralı gözden geçirilir.
**Öncelik:** P1 · **Bağımlılık:** KARAR-4 · **DURUM:** `KARAR BEKLİYOR`

---

## 6. ERTELENEN İŞLER

| ID | İş | Neden ertelendi | Öncelik |
|---|---|---|---|
| `GNC-01` | Otomatik güncelleme davranışı (izin sorma, ertele seçeneği, boştayken yeniden başlat) | P0/P1 işler önce; kullanıcı deneyimi iyileştirmesi | P2 |
| `LOG-01` | Kullanıcı karar logu (ayrı tablo, `audit_logs` kirletilmeden) | Bugün zorunlu değil. **Mimari buna kapatılmıyor** — GNL-01'deki `allowDuplicate` bayrağı ileride bu loga yazılacak şekilde tasarlanacak | P2 |
| `RPR-01` | Rapor envanteri + standart denetimi | Analizde çıkarılamadı; P0'lardan sonra | P2 |
| `TST-01` | 33 atlanan testin neden atlandığının doğrulanması | Geliştirmeleri durdurmaz ama bilinmeli | P2 |
| `TMZ-01` | `ListColumns` çift kopya tekilleştirme | Gerçek teknik borç (biri güncellenip diğeri unutulursa ekran sessizce bozulur) ama acil değil | P2 |
| `GNL-02` | Birim bazlı kayıt tipleri | BRM-01 ve YTK-02'ye bağımlı | P2 |

---

## 7. YATIRIM SONRASI İŞLER

| ID | İş | Neden şimdi değil |
|---|---|---|
| `Y-1` | Kuyruk / background worker | Gerçek ihtiyaç yok; mevcut yük buna uzak (KARAR-5) |
| `Y-2` | Alan / Kolon Yönetimi ekranı | Büyük iş; `TMZ-01` önkoşulu. Mevcut kolon gizle/göster çoğu ihtiyacı karşılıyor |
| `Y-3` | Platform / Ekran görünürlüğü yönetimi | P3. **Uyarı:** eklenirse görünürlük yalnız menüyü etkilemeli, API'de hiçbir şey değişmemeli |
| `Y-4` | Gelişmiş izleme (monitoring/alerting) | Ücretli servis; mevcut "Canlı Sunucu" ekranı + Fly.io metrikleri yeterli |
| `Y-5` | Sürekli bağlantı (WebSocket/SignalR) | Analizde gereksiz olduğu gösterildi (KARAR-5) |

---

## 8. BAĞIMLILIK AĞACI

```
GUV-01 (parola) ──────────────── bağımsız, ACİL
DOG-01 (web giriş testi) ─────── bağımsız

MLZ-01 (silme koruması) ──────── bağımsız ◄── AKTİF
KLT-01 (eksik kilitler) ──────── bağımsız
   └─► KLT-02 (kilit altyapısı)
          ├─► KLT-03 (web)
          └─► KLT-04 (masaüstü + çevrimdışı)

SNK-01, SNK-02 ───────────────── bağımsız
   └─► SNK-03 (backoff)
SNK-04 ───────────────────────── bağımsız

PRT-01 (parite denetimi) ─────── bağımsız
   └─► PRT-02 (ad eşleme)

BRM-01 (personel birimi)
   ├─► GNL-02 (birim bazlı kayıt tipleri)
   └─► LOG-01 (birim bazlı karar logu)

YTK-01 (Approve/Cancel)
   ├─► YTK-02 (kayıt tipi yetkisi) ─► GNL-02
   ├─► YTK-03 (stok kritik yetkiler)
   ├─► YTK-04 (yetki ağacı UI)
   └─► BKM-01 (bakım onay durumu)
          └─► BKM-02 (stok onaya bağlı)  ◄── KARAR-4 bekliyor
                 └─► BKM-03 (negatif stok uyumu)

MLZ-01 + KLT-01
   └─► STK-01 ─► STK-02 ─► STK-03 ─► STK-04 ─► STK-05 ─► STK-06
                                                   └─► MLZ-01 şube bazlı hale gelir

GNL-01 (mükerrer uyarı) ──────── bağımsız (LOG-01'e hazır tasarlanacak)
```

---

## 9. ANA GELİŞTİRME SIRASI

| # | ID | İş | Faz |
|---|---|---|---|
| 1 | `GUV-01` | Süper admin parolası ⚠️ | 0 |
| 2 | `DOG-01` | Web giriş doğrulaması | 0 |
| 3 | **`MLZ-01`** | **Malzeme silme koruması** ◄ AKTİF | 0 |
| 4 | `KLT-01` | Eksik düzenleme kilitleri | 0 |
| 5 | `SNK-01` | Değişiklik yoksa push yapma | 1 |
| 6 | `SNK-02` | Boştayken seyrelme | 1 |
| 7 | `SNK-03` | Exponential backoff | 1 |
| 8 | `SNK-04` | Günlük yedeği ayır | 1 |
| 9 | `PRT-01` | Tam parite denetimi | 1 |
| 10 | `PRT-02` | Ekran adı eşleme | 1 |
| 11 | `BRM-01` | Personel birimi | 2 |
| 12 | `YTK-01` | Approve/Cancel | 2 |
| 13 | `YTK-02` | Kayıt tipi yetkisi | 2 |
| 14 | `YTK-03` | Stok kritik yetkiler | 2 |
| 15 | `YTK-04` | Yetki ağacı UI | 2 |
| 16 | `KLT-02` | Kilit altyapısı (sunucu) | 3 |
| 17 | `KLT-03` | Kilit — web | 3 |
| 18 | `KLT-04` | Kilit — masaüstü + çevrimdışı | 3 |
| 19 | `STK-01` | Şema hazırlığı | 4 |
| 20 | `STK-02` | Dönüşüm planı + prova | 4 |
| 21 | `STK-03` | Çift yazım | 4 |
| 22 | `STK-04` | Doğrulama | 4 |
| 23 | `STK-05` | Okuma geçişi | 4 |
| 24 | `STK-06` | Eski yapı kaldırma | 4 |
| 25 | `GNL-01` | Mükerrer kayıt uyarısı | 5 |
| 26 | `BKM-01` | Bakım onay durumu | 5 |
| 27 | `BKM-02` | Stok onaya bağlı | 5 |
| 28 | `BKM-03` | Negatif stok uyumu | 5 |
| 29 | `GNL-02` | Birim bazlı kayıt tipleri | 5 |
| 30 | `GNC-01` | Otomatik güncelleme | 6 |
| 31 | `LOG-01` | Kullanıcı karar logu | 6 |
| 32 | `RPR-01` | Rapor envanteri | 6 |
| 33 | `TST-01` | 33 atlanan test | 6 |
| 34 | `TMZ-01` | ListColumns tekilleştirme | 6 |

**Toplam: 34 ana iş** (5'i yatırım sonrasına ertelenmiş `Y-1…Y-5` hariç).

**Sıra gerekçesi:** Önce ucuz ve bağımsız P0'lar (canlı riski kapatır), sonra en yüksek
getiri/maliyet oranlı senkron işleri, sonra diğer her şeyin önkoşulu olan yetki altyapısı,
sonra kayıt kilidi, **en son** en riskli ve en büyük iş olan stok geçişi.
Stok geçişi en sonda çünkü canlı veriye dokunan tek iş odur ve ondan önce kilit + yetki
altyapısının oturmuş olması gerekir.

---

## 10. MEVCUT AŞAMA

**FAZ 0 — Canlıya geçiş öncesi zorunlu düzeltmeler**

Bu fazın amacı: çok kullanıcılı kullanıma geçmeden önce **veri kaybı ve yetki riski** taşıyan
açıkları kapatmak. Faz 0 bitmeden Faz 4'e (stok) başlanmaz.

---

## 11. TAMAMLANAN İŞLER

*(Henüz yok — plan 2026-08-10'da oluşturuldu.)*

**Bu plandan ÖNCE tamamlanmış olan ilgili işler (bağlam için):**
- Tasarım paketi (FAZ 1-9 web + M1-M5 masaüstü) — yayınlandı, masaüstü 1.0.136
- Masaüstü menü vektör ikonları (M2.5) — `feature/masaustu-vektor-ikonlar` dalında,
  **görsel doğrulama bekliyor**, `master`'a alınmadı

---

## 12. AKTİF İŞ

**`MLZ-01` — Malzeme silmede stok ve kullanım koruması**
**DURUM:** `GELİŞTİRMEYE HAZIR`

Detay §5'te. Kullanıcı "sıradaki iş" dediğinde bu iş için önce kısa analiz sunulacak,
sonra geliştirmeye geçilecek.

---

## 13. BEKLEYEN İŞLER

33 iş `BEKLEMEDE` / `KARAR BEKLİYOR` / `ANALİZ BEKLİYOR` durumunda — tam liste §5, §6, §7.

---

## 14. KARARLAR

| ID | Karar | Durum |
|---|---|---|
| KARAR-1 | Malzeme kataloğu firma genelinde kalır, stok şube bazlı olur | ✅ VERİLDİ |
| KARAR-2 | Stok geçişi 6 fazda, canlı veri korunarak | ✅ VERİLDİ |
| KARAR-3 | Kayıt kilidi: kiralama (lease) tabanlı gerçek kilit; çevrimdışı sınırı kabul edilir | ✅ VERİLDİ |
| KARAR-4 | Bakımda negatif stok ↔ onay akışı çelişkisi | ⏳ **BEKLİYOR** (FAZ 5) |
| KARAR-5 | Queue/Redis/WebSocket/monitoring şimdilik kurulmayacak | ✅ VERİLDİ |

---

## 15. AÇIK SORUNLAR

| # | Sorun | Etki | Ne zaman çözülecek |
|---|---|---|---|
| 1 | Süper admin parolası zayıf ve canlıda çalışıyor | **Yüksek** — her firmaya erişim | `GUV-01` — **acil** |
| 2 | Stok firma geneli — çok şubeli çalışılamıyor | **Yüksek** | FAZ 4 |
| 3 | Malzeme silmede koruma yok | **Yüksek** | `MLZ-01` — aktif |
| 4 | Yakıt/stok belgeleri/muayenede LWW koruması yok (kendi kuralımıza aykırı) | **Yüksek** | `KLT-01` |
| 5 | Bakımda negatif stok ↔ onay çelişkisi | Orta | KARAR-4 |
| 6 | `ListColumns` iki kopya — biri unutulursa ekran sessizce bozulur | Orta | `TMZ-01` |
| 7 | 33 test neden atlanıyor bilinmiyor | Orta | `TST-01` |
| 8 | Tam ekran paritesi denetlenmedi | Orta | `PRT-01` |
| 9 | Masaüstü vektör ikonları görsel doğrulama bekliyor | Düşük | Kullanıcı bakacak |

---

## 16. TEST DURUMU

**Son ölçüm (2026-08-09, `master`):**
- Build: **0 hata**
- Test: **1017 toplam — 984 geçti, 0 başarısız, 33 atlandı**

**Her iş için zorunlu testler (proje geneli, ekrandan bağımsız):**
tenant sızıntısı · permission (UI **ve** API) · rollback · negatif stok · sayaç geriye gitme ·
idempotent retry · çevrimdışı kalıcılık · update rollback

**Ekran bazlı QA:** Değiştirilen ekran için Coverage Matrix + `docs/tests/<Ekran>_Test_Report.md`
(CLAUDE.md §7). Kapsam **yalnız değiştirilen ekran** — genel regresyon yalnız açıkça istenirse.

---

## 17. SONRAKİ ADIM

**`MLZ-01` — Malzeme silmede stok ve kullanım koruması.**

Kullanıcı **"sıradaki iş"** dediğinde:
1. Bu dosya okunur, aktif iş ve son tamamlanan iş kontrol edilir.
2. `MLZ-01` için kısa analiz sunulur (hangi dosyalar, hangi kurallar, hangi mesajlar).
3. Onay alınırsa geliştirmeye geçilir.
4. Geliştirme döngüsü: ANALİZ → KARAR → PLAN → GELİŞTİRME → TEST → WEB DOĞRULAMA →
   MASAÜSTÜ DOĞRULAMA → SENKRON DOĞRULAMA → SONUÇ RAPORU → **bu dosya güncellenir** → SONRAKİ İŞ.

**Paralel olarak kullanıcıdan beklenen:** `GUV-01` (parola) ve `DOG-01` (web giriş testi).

---

## EK — DURUM ETİKETLERİ

`BEKLEMEDE` · `ANALİZ BEKLİYOR` · `KARAR BEKLİYOR` · `GELİŞTİRMEYE HAZIR` · `GELİŞTİRİLİYOR` ·
`TESTTE` · `WEB DOĞRULAMASI` · `MASAÜSTÜ DOĞRULAMASI` · `SENKRONİZASYON TESTİ` · `TAMAMLANDI` ·
`ERTELENDİ` · `İPTAL`

## EK — İŞ SONU RAPOR FORMATI

```
TAMAMLANAN İŞ:
İŞ ID:

YAPILANLAR:
-

DEĞİŞEN DOSYALAR:
-

VERİTABANI / MIGRATION:
-

WEB:
-

MASAÜSTÜ:
-

API:
-

TESTLER:
-

TEST SONUCU:
-

RİSK / SORUN:
-

KULLANICI TARAFINDAN DOĞRULANMASI GEREKEN:
-

PLAN DOSYASI GÜNCELLENDİ:
EVET

SONRAKİ İŞ:
İŞ ID + başlık
```
