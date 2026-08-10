# DEPOWISE / ALPNEX — ANA GELİŞTİRME PLANI

> **Bu dosya nedir?** Bundan sonraki bütün geliştirmelerin ana çalışma dosyası.
> "Şu anda nerede kaldık?" sorusunun cevabı en üstteki durum bloğundadır.
> Mevcut durum fotoğrafı için: [PROJE_GENEL_DURUM_ANALIZI.md](PROJE_GENEL_DURUM_ANALIZI.md)

---

```
AKTİF AŞAMA:         FAZ 0 — Canlıya geçiş öncesi zorunlu düzeltmeler
AKTİF İŞ ID:         KLT-01
AKTİF İŞ:            Eksik düzenleme kilitleri (yakıt, stok belgeleri, muayene, kullanıcılar)
DURUM:               BEKLEMEDE — geliştirmeye hazır
SON TAMAMLANAN İŞ:   MLZ-01 — Malzeme silmede stok/kullanım koruması (2026-08-10)
SONRAKİ İŞ:          SNK-01 — Değişiklik yoksa push yapma
BEKLEYEN KARAR:      KARAR-4 (bakımda negatif stok ↔ onay) — FAZ 5'e kadar beklenebilir
                     YET-01 (yetki modeli) — FAZ 2'ye girmeden ÖNCE gerekli
SON GÜNCELLEME:      2026-08-10 (yetki mimarisi analizi; TMZ-02 → YET-01'e bağlandı)
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
| H-1 | **Fiziksel stok DEPO bazlı olacak** — zincir: `Firma → Şube → Depo → Stok → Malzeme`. Her şubenin kendi deposu var; raporlar firma toplamı / şube toplamı / depo kırılımı verebilmeli. İleride depo→depo transferi. | 2026-08-10'da **derinleşti** (önce "şube bazlı" idi) → KARAR-6 |
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

### KARAR-1 — Malzeme kataloğu firma genelinde KALIR, fiziksel stok DEPO bazlı OLUR ✅
> *(2026-08-10'da güncellendi: "şube bazlı" → "depo bazlı". Gerekçe KARAR-6'da.)*

**Karar:** `materials` tablosuna konum alanı **EKLENMEYECEK**. Konum boyutu yalnız
`stock_balances` ve `stock_movements` tarafına eklenecek.

**Neden (teknik olarak açık, kullanıcıya sorulmadı):**
- Bu, standart ERP desenidir: **tek ürün kataloğu + konum bazlı stok**.
- Malzemeyi konum bazlı yaparsak aynı "Filtre Yağı" her depoda ayrı kayıt olur →
  raporlar bölünür, muadil eşleştirme bozulur, kullanıcı aynı malzemeyi 5 kez tanımlar.
- Kullanıcının asıl derdi — *"bir şubenin işlemi diğerinin stok verisini değiştirmesin"* —
  **stok tarafını ayırmakla tamamen çözülür**, katalogu bölmeye gerek yok.

**Sonuç:** Malzeme **silme** işlemi katalog düzeyinde kalır → "hiçbir depoda stok yoksa
silinebilir" kuralı uygulanır (bkz. MLZ-01 + `MLZ-01-DEPO` uyarısı).

---

### KARAR-6 — ŞUBE / DEPO / STOK MİMARİSİ ✅ *(yeni, 2026-08-10)*

**Hedef zincir:** `Firma → Şube → Depo → Stok → Malzeme`

| Katman | Karar |
|---|---|
| **Malzeme kataloğu** | Firma genelinde **ORTAK** (tek "Filtre Yağı" tanımı) |
| **Fiziksel stok** | **DEPO** bazlı tutulur |
| **Depo** | Bir **şubeye** bağlıdır; bir şubenin **birden fazla** deposu olabilir |
| **Firma toplam stok** | Tüm depoların toplamı |
| **Şube toplam stok** | O şubeye bağlı depoların toplamı |
| **Depo stok** | Tek depo satırı |
| **Transfer** | Depo → depo (şube sınırı aşabilir), ileride |

#### Teknik model: **AYRI `warehouses` TABLOSU** (Seçenek B)

**`branches` tablosu genişletilmeyecek** (`kind='warehouse'` YAPILMAYACAK).

**Gerekçe — koddan doğrulandı, tahmin değil:**
- `branches.kind` bugün **yalnız ekranda etiket** ("Şube"/"Şantiye") için kullanılıyor.
  **Hiçbir sorgu `kind` ile süzmüyor** — 12+ yerde `FROM branches ... WHERE company_id`
  var, `kind` şartı olan **tek bir sorgu bile yok**.
- Depoyu `branches` içine koyarsak depolar **şube gibi** davranmaya başlar:
  şube seçicilerinde, `ScopeResolver`'da (yetki!), raporlarda, giriş ekranı şube listesinde,
  talep doğrulamasında görünürler. 12+ noktanın **hepsinin** düzeltilmesi gerekir ve
  biri atlanırsa hata **sessizce** oluşur (yanlış yetki / yanlış rapor).
- Kavramsal olarak da ayrıdır: şube bir **organizasyon birimi** (kullanıcı, yetki, personel
  bağlanır); depo bir **stok konumudur**.

**Model:**
```
warehouses(id, company_id, branch_id, name, is_default, created_at, updated_at,
           version, is_deleted)
stock_balances  → PK (material_id, warehouse_id)      [bugün: PK (material_id)]
stock_movements → + warehouse_id                       [bugün: branch_id var]
```

**Geçişte her mevcut şubeye 1 adet "varsayılan depo" otomatik oluşturulur.** Böylece
bugünkü "her şubenin bir deposu var" durumu birebir karşılanır; ileride bir şubeye ikinci
depo eklemek **yeniden mimari değişiklik gerektirmez**.

#### Maliyeti düşüren 3 kritik bulgu (koddan DOĞRULANDI)

1. **`stock_balances` bir ÖNBELLEKTİR (cache), ana kaynak değil.** Şemadaki kendi yorumu:
   *"Bakiye cache (ledger ile aynı transaction'da güncellenir; doğrudan değiştirilmez)"*.
   Ana kaynak `stock_movements` defteridir. → **Yeni bakiye tablosu defterden yeniden
   HESAPLANABİLİR.** Riskli veri dönüşümü gerekmez; bu, göç maliyetini kökten düşürür.
2. **Defter zaten konum biliyor:** `stock_movements.branch_id` VAR ve `movement_type`
   değerleri arasında **`transfer` zaten var**.
3. **Şubeler arası transfer ZATEN ÇALIŞIYOR** (`StockService`, "transfer" belgesi:
   kaynakta −1, hedefte +1, ikisi de şube etiketli). Koddaki kendi yorumu:
   *"net bakiye değişmez ama hareketler kayıtlı"* — **tam da eksik olanı anlatıyor**:
   defter doğru, **bakiye önbelleğinde konum boyutu yok**. Yani yapılacak iş
   büyük ölçüde **önbellek** işidir, defter işi değil.

#### Senkronizasyon: PK değişimi güvenli (DOĞRULANDI)
Senkron katmanı birincil anahtarı **şemadan dinamik okuyor** (`DbIntrospect.PrimaryKey`,
`List<string>` döner → **bileşik anahtar destekli**). `stock_balances` PK'sının
`(material_id)` → `(material_id, warehouse_id)` olması senkronu **kendiliğinden** takip eder.
Ayrıca çakışma takibi yalnız PK'sı tek `id` olan tablolara uygulanıyor; `stock_balances`
zaten o kapsamda değil → **davranış değişmez**.

#### ⚠️ ÖNCEKİ İDDİANIN DÜZELTMESİ
Daha önce *"`material_requests.warehouse_id` kullanılmıyor, depo maliyetini düşürebilir"*
denmişti. **BU YANLIŞTI.** Kod doğrulaması: `RequestsViewModel.cs:244` →
`FormWarehouse = Personnel.FirstOrDefault(x => x.Id == d.WarehouseId)`.
Bu alan **PERSONEL kimliği** tutuyor (depo sorumlusu), depo varlığı değil.
Kolon adı yanıltıcıdır. **Depo mimarisinde KULLANILMAYACAK**, dokunulmayacak.

---

### KARAR-2 — Stok geçişi fazlara bölünerek, canlı veri korunarak ✅

Tek seferde uygulanmayacak. Her faz tek başına geri alınabilir olacak.
Faz sırası §5'te `STK-01…STK-07`.

**Altın kural:** Babanın canlı verisi hiçbir fazda silinmez/dönüştürülmez;
yeni yapı eskinin **yanına** kurulur, defterden doğrulanır, sonra okuma taşınır.

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
**⚠️ 2026-08-10 ek doğrulama:** Web'de rol bazlı giriş kısıtı **yine bulunamadı** — `Guard()`
yalnız oturum açık mı diye bakıyor, `/api/auth/login` rol reddi yapmıyor. **"Giriş yapabilmek"
ile "modül görebilmek" zaten ayrık.** Kullanıcı hiçbir modül yetkisi olmadan giriş yapar ve
**boş menü** görür — bu "giremiyorum" gibi algılanıyor olabilir. Bu testin amacı hangisinin
doğru olduğunu belirlemektir. `YET-01` bu sonucu girdi olarak kullanacak.
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
**Not:** STK-05 sonrası bu kontrol "hiçbir **depoda** stok yoksa" haline gelecek — bkz. §15 `MLZ-01-DEPO`.
**DURUM:** `GELİŞTİRMEYE HAZIR`

---

**ID:** `KLT-01` ◄ **AKTİF İŞ**
**Başlık:** Eksik düzenleme kilitleri (optimistic) — yakıt, stok belgeleri, muayene, kullanıcılar
**Açıklama:** `EditLockGuard` 8 serviste var; yakıt, stok belgeleri, muayene ve kullanıcılarda yok.
**Neden gerekli:** CLAUDE.md §4 "stok, sayaç, yakıt, bakım ve onayda LWW yasaktır" kuralı
şu an **ihlal ediliyor**. İki kullanıcı aynı yakıt kaydını düzenlerse ikincisi birincisini sessizce eziyor.
**Öncelik:** **P0** · **Bağımlılık:** **YOK** — yetki yeniden tasarımına (`YET-01`) bağlı DEĞİL
**Web:** ✅ · **Masaüstü:** ✅ · **API:** ✅
**Migration:** ❌ **GEREKMİYOR** — 2026-08-10'da doğrulandı: `fuel_distributions`,
`fuel_depot_entries`, `stock_documents`, `vehicle_inspections`, `users` tablolarının
**hepsinde `version` kolonu ZATEN VAR**.
**Canlı veri riski:** Yok · **Maliyet:** Düşük (desen 8 serviste kanıtlı)
**Şimdi/Ertelenmiş:** **ŞİMDİ** · **Sonraki adım:** SNK-01
**Test gereksinimi:** Her ekran için "iki sekmeden kaydet → ikincisi 409" testi
**DURUM:** `GELİŞTİRMEYE HAZIR`

> **KLT-01 ≠ gerçek kayıt kilidi.** KLT-01 mevcut **iyimser** korumayı (kaydederken 409)
> eksik 4 servise yayar. Kullanıcının istediği *"ikinci kişi kayda giremesin, adı görünsün"*
> davranışı **`KLT-02/03/04`**'tür (kiralama tabanlı, KARAR-3). İkisi karıştırılmamalı.
> KLT-01 ucuz ve bağımsızdır; KLT-02+ sunucu tarafı altyapı ister.

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

### FAZ 2 — Yetki ağacı (P1)

> ⚠️ **2026-08-10'da YENİDEN DEĞERLENDİRİLDİ.** Kod incelemesi, yetki modelinin sanılandan
> farklı olduğunu ortaya çıkardı (aşağıda `YET-01`). `YTK-01…YTK-04` **olduğu gibi
> uygulanamaz** — çünkü bugünkü model "rol → yetki" değil, "**kullanıcı → yetki**"dir.
> Bu yüzden FAZ 2'nin başına **`YET-01` (tasarım kararı)** eklendi ve `YTK-*` işleri ona bağlandı.

---

**ID:** `YET-01` 🔴 **YENİ — FAZ 2'nin ilk işi**
**Başlık:** Yetki ağacının hedef modeline karar verilmesi (tasarım işi, kod değil)
**Açıklama:** Bugünkü modelin kritik gerçeği (koddan DOĞRULANDI):

| Katman | Bugünkü gerçek |
|---|---|
| Yetki nerede duruyor? | **`user_permissions` — KULLANICI bazında.** `role_permissions` tablosu **YOK**. |
| Roller ne işe yarıyor? | Yalnız 3 sabit rolün bypass'ı (SuperAdmin/CompanyAdmin/RestrictedSuperAdmin) + `role_grant_limits` (modül kapatma). **Roller yetki TAŞIMIYOR.** |
| Şablonlar? | `permission_templates` → kullanıcı **oluşturulurken KOPYALANIYOR**. Canlı bağ yok: şablon sonradan değişirse mevcut kullanıcılar **güncellenmez**. |
| Şube kapsamı | `users.branch_id` (**TEK şube**) veya `can_view_all_branches` (hepsi). Arası yok. |
| Çoklu şube | `user_scopes` tablosu var, `ScopeResolver` okuyor, **yazanı üretimde yok** → ulaşılamaz (bkz. `TMZ-02`). |
| Birim | **YOK** (ne `users`'ta ne `personnel`'de). |
| İşlemler | View / Create / Edit / Delete + 9 **global** özel buton. Modül bazlı Approve **yok**. |
| Kayıt tipi yetkisi | **YOK.** Günlük Faaliyet tipleri C# sabiti (`extra_oil`, `extra_filter`, `repair`). |

**Bunun ticari üründeki sonucu:** 50 kullanıcılı bir firmada "Depo personeli" tanımı değişirse
**50 kullanıcının yetkisi tek tek elle** güncellenmelidir. Rol değiştirmek kimseyi etkilemez.
Ölçeklenmiyor.

**Bu iş bir KARAR işidir, kodlama değil.** Verilecek karar: yetki kullanıcıda mı kalsın
(bugünkü), role mi taşınsın (hedef), yoksa hibrit mi (rol tabanı + kullanıcı istisnası) olsun.
**Bu karar verilmeden `YTK-01…YTK-04` yapılmamalı** — aksi halde yanlış temele eklenir.

**Öncelik:** **P1** · **Bağımlılık:** Yok · **Migration:** ❌ (karar aşaması)
**Maliyet:** Düşük (analiz) · **Kapsamı:** `TMZ-02`'yi de içine alır
**DURUM:** `ANALİZ BEKLİYOR`

---

> Mevcut yetki sisteminin **iyi çalışan kısımları çöpe atılmıyor**: deny-by-default,
> firma izolasyonu, `BlockedModules`, fail-closed şube kapsamı, senkron push'ta yetki kontrolü.
> `YET-01` bunların üzerine **ne ekleneceğine** karar verir.

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

**ID:** `YTK-01` ⚠️ **`YET-01` kararına bağlandı (2026-08-10)**
**Başlık:** `PermissionAction`'a Approve ve Cancel eklenmesi
**Açıklama:** `permissions` tablosuna `can_approve`, `can_cancel` kolonları (varsayılan 0).
`AccessControl` bunları deny-by-default değerlendirir.
**Neden gerekli:** Bugün `btn-approve` **tek global buton** — "bakımı onaylar ama talebi onaylamaz"
ifade edilemiyor. Bakım onayı ve stok kritik işlemleri buna bağımlı.
**Öncelik:** **P1** · **Bağımlılık:** **`YET-01` (zorunlu önkoşul)**
**Web:** ✅ yetki ağacı UI · **Masaüstü:** ✅ aynı · **API:** ✅ · **Veritabanı:** ✅
**Migration:** ✅ **additive, küçük** · **Canlı veri riski:** Yok — mevcut yetkiler aynen korunur
(yeni kolon 0 = deny, deny-by-default zaten bunu bekliyor)
**⚠️ Not:** Kolon `user_permissions`'a mı yoksa yeni bir `role_permissions`'a mı eklenecek —
bu **`YET-01`'in kararına** bağlıdır. Karar verilmeden başlanırsa iş boşa gidebilir.
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

### FAZ 4 — DEPO BAZLI STOK (P0/P1 — en büyük iş) 🔴
> *(2026-08-10'da depo mimarisine göre yeniden yazıldı. Önceki "şube bazlı stok" varsayımı
> KARAR-6 ile değişti: araya **Depo** katmanı girdi ve 7 faza bölündü.)*

> **Canlı veriye dokunan tek iş grubudur.** Her faz ayrı onay, ayrı deploy, ayrı doğrulama ister.
> KARAR-6: ayrı `warehouses` tablosu; `branches` genişletilmez.
> **Kolaylaştırıcı gerçek:** `stock_balances` bir ÖNBELLEKTİR — defterden yeniden hesaplanabilir.

---

**ID:** `STK-01`
**Başlık:** `warehouses` tablosu + her şubeye varsayılan depo
**Açıklama:** Yeni `warehouses(id, company_id, branch_id, name, is_default, …)` tablosu.
Migration mevcut **her şube için 1 adet varsayılan depo** oluşturur (adı: "<Şube adı> Deposu").
**Hiçbir stok yolu değişmez** — tablo boşta durur.
**Neden gerekli:** KARAR-6'nın temeli; depo katmanının taşıyıcısı.
**Öncelik:** **P0** · **Bağımlılık:** MLZ-01, KLT-01
**Migration:** ✅ **additive** (yeni tablo + veri üretimi; mevcut satır DEĞİŞMEZ)
**Canlı veri riski:** **Çok düşük** · **Maliyet:** Düşük
**Test:** Her şubeye tam 1 varsayılan depo · iki lehçede çalışıyor · şubesiz firma bozulmuyor ·
senkron listesine eklendi
**DURUM:** `BEKLEMEDE`

**ID:** `STK-02`
**Başlık:** Deftere `warehouse_id` eklenmesi + geriye dönük doldurma
**Açıklama:** `stock_movements`'a `warehouse_id` (nullable) eklenir. Geçmiş hareketler
`branch_id` → o şubenin **varsayılan deposu** ile doldurulur. `branch_id` **KALIR** (bozulmaz).
**Neden gerekli:** Defter ana kaynaktır; önbellek ondan hesaplanacak.
**Öncelik:** P0 · **Bağımlılık:** STK-01
**Migration:** ✅ additive + backfill · **Canlı veri riski:** **Düşük** (yalnız yeni kolon doldurulur)
**Maliyet:** Orta
**Test:** Tüm geçmiş hareketlerin `warehouse_id`'si dolu · `branch_id` değişmemiş ·
hareket sayısı öncesi = sonrası
**DURUM:** `BEKLEMEDE`

**ID:** `STK-03`
**Başlık:** Depo bazlı bakiye önbelleğinin defterden ÜRETİLMESİ (gölge)
**Açıklama:** `stock_balances`'ın yanına depo bazlı bakiye kurulur ve **`stock_movements`'tan
yeniden hesaplanır**. Okumalar hâlâ eskiden yapılır → hiçbir kullanıcı etkisi yok.
**⚠️ Riskli veri dönüşümü YOK** — önbellek olduğu için hesaplanıyor, taşınmıyor (KARAR-6, bulgu 1).
**Öncelik:** P0 · **Bağımlılık:** STK-02 · **Migration:** ✅ (tablo/PK)
**Canlı veri riski:** **Düşük** · **Maliyet:** Orta
**Test:** Yeni yapının depo toplamları = eski firma-geneli bakiye (kuruşuna kadar)
**DURUM:** `BEKLEMEDE`

**ID:** `STK-04`
**Başlık:** Çift yazım + doğrulama
**Açıklama:** Stok işlemleri hem eski hem yeni bakiyeyi günceller. Bir süre çalıştırılır ve
iki yapının **birbirini tuttuğu** doğrulanır. Fark varsa STK-05'e **GEÇİLMEZ**.
**Öncelik:** P0 · **Bağımlılık:** STK-03 · **Migration:** ❌
**Canlı veri riski:** Düşük · **Maliyet:** Yüksek
**Test:** Giriş/çıkış/transfer/açılış/düzeltme — her biri iki yapıya doğru yazıyor ·
karşılaştırma raporu sıfır fark
**DURUM:** `BEKLEMEDE`

**ID:** `STK-05`
**Başlık:** Okumaların depo bazlı hale getirilmesi (5 okuma noktası)
**Açıklama:** Bakiye okuyan **5 nokta** depo bazlı hale gelir:
`StockService.GetBalance`, `StockService` toplu okuma, `OpeningStockService.GetBalance`,
`StockBalanceWriter`, **`MaterialService.GuardDeletable` (MLZ-01)**.
Firma toplamı = tüm depoların SUM'ı · şube toplamı = o şubenin depolarının SUM'ı · depo = tek satır.
**⚠️ MLZ-01 bağımlılığı:** `GuardDeletable` bugün tek satır okuyor; burada **`SUM(quantity)`**
olmalı — bkz. §15 açık sorun `MLZ-01-DEPO`.
**Kullanıcı bu fazda değişikliği görür.**
**Öncelik:** P0 · **Bağımlılık:** STK-04 · **Web:** ✅ · **Masaüstü:** ✅ · **API:** ✅
**Migration:** ❌ · **Canlı veri riski:** **Orta** — geri dönüş planı hazır olmalı
**Maliyet:** Yüksek
**Test:** Firma/şube/depo toplamları tutarlı · yetkisiz şube verisi görünmüyor ·
**ID göndererek başka şubenin deposuna erişilemiyor** · MLZ-01 testleri depo senaryosuyla genişletildi
**DURUM:** `BEKLEMEDE`

**ID:** `STK-06`
**Başlık:** Depo bazlı raporlama (Firma → Şube → Depo dağılımı)
**Açıklama:** "Filtre Yağı hangi depolarda, ne kadar?" dağılım raporu; firma toplamı satırı.
**Öncelik:** P1 · **Bağımlılık:** STK-05 · **Web:** ✅ · **Masaüstü:** ✅
**Migration:** ❌ · **Maliyet:** Orta
**Test:** Dağılım toplamı = firma toplamı · sıfır stoklu depo da görünüyor · yetki kapsamına uyuyor
**DURUM:** `BEKLEMEDE`

**ID:** `STK-07`
**Başlık:** Eski firma-geneli bakiye yapısının kaldırılması
**Açıklama:** Çift yazım durdurulur, eski bakiye yapısı kaldırılır.
**Öncelik:** P1 · **Bağımlılık:** STK-06 (+ en az birkaç hafta sorunsuz çalışma)
**Migration:** ✅ · **Canlı veri riski:** **Orta** · **Maliyet:** Düşük
**Not:** Acele edilmez. Eski yapı "geri dönüş sigortası" olarak bir süre durur.
**DURUM:** `BEKLEMEDE`

---

### FAZ 4B — Depo transferi (P2, FAZ 4'ten SONRA)

**ID:** `TRF-01`
**Başlık:** Depo → depo transferi
**Açıklama:** **Şubeler arası transfer ZATEN VAR** (`StockService`, "transfer" belgesi:
kaynakta −1, hedefte +1). Yapılacak iş, mevcut transferi **şube** yerine **depo** kırılımına
taşımaktır — sıfırdan yazmak DEĞİL.
Kayıtta zaten tutulanlar: kaynak, hedef, malzeme, miktar, kullanıcı, tarih, belge, `operation_id`
(idempotency), `group_id`. **Yeni alan ihtiyacı görünmüyor** — yalnız konum alanı depo olacak.
**Öncelik:** P2 · **Bağımlılık:** STK-05 · **Migration:** ❌ (beklenen)
**Maliyet:** Düşük-Orta
**Test:** Kaynak depo azalır, hedef artar · firma toplamı DEĞİŞMEZ · çift gönderim ikinci kez işlemez ·
yetkisiz depoya transfer reddedilir
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
| `TMZ-02` | **İki `BranchService` + ulaşılamayan `user_scopes`** — aşağıda | ⚠️ **2026-08-10: BAĞIMSIZ TEKNİK BORÇ DEĞİL.** `YET-01`'in içine alındı — bkz. aşağıdaki karar | P1 (YET-01 ile) |

### 🔍 `TMZ-02` — İki `BranchService` ve ulaşılamayan çoklu-şube kapsamı
*(2026-08-10 doğrulaması — yalnız İNCELEME, kod değiştirilmedi)*

**Bulgu 1 — İki ayrı sınıf var, aynı ada sahip, farklı isim alanında:**

| | `Infrastructure/Org/BranchService.cs` | `Infrastructure/Organization/BranchService.cs` |
|---|---|---|
| Satır | 125 | 288 |
| Kurucu | `(factory, **ScopeResolver**, clock)` | `(factory, clock)` |
| Metotlar | `Create`, `ListInScope`, `SoftDelete`, `Restore`, **`AssignScope`** | `List`, `Create`, `Update` (düzenleme kilidiyle), `Delete`, `GetUsers` |
| Üretimde örneklenen | ❌ **HAYIR** — `src/` içinde tek bir `new` yok | ✅ **EVET** — API (`ServerServices.cs:137`) + masaüstü (`DesktopServices.cs:149`) |
| Nereden çağrılıyor | Yalnız `tests/OrgPersonnelTests.cs` | Üretim kod yolu |

→ **`Organization.BranchService` aktif; `Org.BranchService` üretimde ölü koddur.**
Aynı iş kuralının iki yerde olması riski **YOK** (biri hiç çalışmıyor), ama aynı adı taşımaları
okuyanı yanıltıyor ve yanlış olanı düzenleme riski doğuruyor.

**Bulgu 2 (daha önemli) — `user_scopes` tablosunun üretimde YAZANI YOK:**
- `user_scopes`'a yazan tek kod: `Org/BranchService.AssignScope` (satır 119).
- O metot **yalnız testlerden** çağrılıyor; `src/` içinde çağrı yok.
- `ScopeResolver` bu tabloyu **okuyor** → üretimde daima boş döner → "açık kapsam yok" dalına
  düşer → admin ise tüm şubeler, admin değilse **boş küme**.
- **Bugünkü etkisi sınırlı:** `EnsureBranchAllowed` yalnız **Excel içe aktarım** yolunda
  kullanılıyor; admin-olmayan bir kullanıcı belirli şube seçerek içe aktarım yaparsa 403 alır.
  Günlük şube sınırlaması farklı bir mekanizmadan (`users.branch_id` → `OperatingBranchId` →
  `BranchScope.Sql`) yürüdüğü için diğer ekranlar etkilenmiyor.
- **Sonuç:** "Bir kullanıcıyı birden fazla şubeye atama" özelliği **arayüzden ulaşılamaz** durumda.

**Neden şube/depo işinden önce karara bağlanmalı:** FAZ 4 (depo) ve yetki genişletmesi bu
kapsam mekanizmasının üzerine kurulacak. Hangi mekanizmanın kalıcı olduğu (çoklu `user_scopes`
mi, tekil `users.branch_id` mi) netleşmeden depo kapsamı tasarlamak yanlış temele inşa olur.

**Yapılmayacaklar (şimdilik):** dosya silme, birleştirme, yeniden adlandırma, refactoring.
Önce karar, sonra iş.

#### ⚖️ KARAR (2026-08-10): TMZ-02 bağımsız bir düzeltme DEĞİLDİR → `YET-01`'e bağlandı

**Soru:** TMZ-02 küçük bağımsız bir teknik borç mu (Seçenek A), yoksa yetki yeniden
tasarımının parçası mı (Seçenek B)?

**Cevap: SEÇENEK B.** Gerekçe:

TMZ-02'nin özü *"iki dosya var, biri ölü"* değildir — özü şudur: **şube kapsamının hangi
mekanizmayla yürüyeceği belirsizdir.** İki rakip mekanizma yan yana duruyor:

| Mekanizma | Durum |
|---|---|
| `users.branch_id` (**tek şube**) + `can_view_all_branches` | Üretimde **çalışıyor** |
| `user_scopes` (**çoklu şube**) | Tablo var, okunuyor, **yazanı yok** → ölü |

"Ölü kodu sil" dersek çoklu-şube yeteneğini gömmüş oluruz. "Yazma yolunu ekle" dersek
kullanıcı başına **iki farklı kapsam kaynağı** olur ve hangisi kazanır sorusu doğar.
Doğru cevap ancak hedef yetki modeli belirlenince verilebilir — **bu da `YET-01`'in kendisidir.**

Ayrıca depo kapsamı (FAZ 4) ve `YTK-*` işleri bu mekanizmanın üzerine kurulacak.
Yanlış temele inşa etmemek için **önce karar, sonra kod.**

**TMZ-02 tek başına kod işi olarak açılmayacak;** `YET-01` kapsamında karara bağlanacak.
**KLT-01'i engellemez** — KLT-01 kapsam mekanizmasına dokunmuyor.
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

YET-01 (yetki modeli KARARI)  ◄── FAZ 2'nin kapısı; TMZ-02 buraya DAHİL EDİLDİ
   ├─► BRM-01 (birim hangi katmana bağlanacak — buradan çıkar)
   └─► YTK-01 (Approve/Cancel — hangi tabloya eklenecek — buradan çıkar)
          ├─► YTK-02 (kayıt tipi yetkisi) ─► GNL-02
          ├─► YTK-03 (stok kritik yetkiler)
          ├─► YTK-04 (yetki ağacı UI)
          └─► BKM-01 (bakım onay durumu)
                 └─► BKM-02 (stok onaya bağlı)  ◄── KARAR-4 bekliyor
                        └─► BKM-03 (negatif stok uyumu)

KLT-01 (eksik iyimser kilitler)  ── YET-01'e BAĞLI DEĞİL, hemen yapılabilir
   └─► KLT-02/03/04 (gerçek kiralama kilidi)
          └─ yetki bağı: kilit almak için Edit yetkisi + şube kapsamı ŞART
             (ikisi de bugünkü sistemde MEVCUT → YET-01 beklemeye gerek yok)

MLZ-01 + KLT-01
   └─► STK-01 (warehouses tablosu)
          └─► STK-02 (deftere warehouse_id)
                 └─► STK-03 (bakiye önbelleği defterden üretilir)
                        └─► STK-04 (çift yazım + doğrulama)
                               └─► STK-05 (okuma geçişi — 5 nokta)
                                      ├─► MLZ-01-DEPO (GuardDeletable → SUM)
                                      ├─► STK-06 (depo bazlı raporlama)
                                      │      └─► STK-07 (eski yapı kaldırma)
                                      └─► TRF-01 (depo → depo transferi)

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
| 10b | **`YET-01`** | **Yetki modeli KARARI (TMZ-02 dahil)** ← FAZ 2'nin kapısı | 2 |
| 11 | `BRM-01` | Personel birimi | 2 |
| 12 | `YTK-01` | Approve/Cancel | 2 |
| 13 | `YTK-02` | Kayıt tipi yetkisi | 2 |
| 14 | `YTK-03` | Stok kritik yetkiler | 2 |
| 15 | `YTK-04` | Yetki ağacı UI | 2 |
| 16 | `KLT-02` | Kilit altyapısı (sunucu) | 3 |
| 17 | `KLT-03` | Kilit — web | 3 |
| 18 | `KLT-04` | Kilit — masaüstü + çevrimdışı | 3 |
| 19 | `STK-01` | `warehouses` tablosu + varsayılan depolar | 4 |
| 20 | `STK-02` | Deftere `warehouse_id` + geriye doldurma | 4 |
| 21 | `STK-03` | Bakiye önbelleği defterden üretilir | 4 |
| 22 | `STK-04` | Çift yazım + doğrulama | 4 |
| 23 | `STK-05` | Okuma geçişi (5 nokta) + `MLZ-01-DEPO` | 4 |
| 24 | `STK-06` | Depo bazlı raporlama | 4 |
| 24b | `STK-07` | Eski yapı kaldırma | 4 |
| 24c | `TRF-01` | Depo → depo transferi | 4B |
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

**Toplam: 36 ana iş** (5'i yatırım sonrasına ertelenmiş `Y-1…Y-5` hariç).
*(2026-08-10: depo mimarisi nedeniyle FAZ 4 altı faza değil **yedi** faza bölündü ve
`TRF-01` eklendi → 34 → 36.)*

**Yetki sistemine depo etkisi (analiz sonucu, DOĞRULANDI):** Mevcut yetki zinciri
`Firma → Şube → Rol → Modül → İşlem`. Depo kapsamı ileride gerekirse `user_scopes`
deseninin aynısıyla (`user_warehouse_scopes`) eklenebilir — **mevcut yapı buna kapalı değil**.
**Başlangıçta depo kapsamı EKLENMEYECEK:** bir şubeyi görebilen kullanıcı o şubenin
depolarını görür. Depo bazlı yetki gerçek ihtiyaç doğunca ayrı iş olarak açılır (P3).

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

### ✅ `MLZ-01` — Malzeme silmede stok/kullanım koruması (2026-08-10)

**Dal:** `feature/mlz-01-malzeme-silme-korumasi` · **commit EDİLMEDİ** (kullanıcı isteği)

**Yapılan:** Malzeme silme yalnız yetki ve firma kontrolü yapıyordu. Artık silmeden önce
aynı transaction içinde kullanım kontrolü yapılıyor: stok bakiyesi ≠ 0 **veya** operasyonel
geçmiş (stok hareketi / bakım kaydı / talep kalemi / sayım satırı) varsa silme **engelleniyor**
ve kullanıcıya sebebi yazan anlaşılır bir mesaj dönüyor.

**Değişen dosyalar:**
- `src/DepoWise.Infrastructure/Materials/MaterialService.cs` — **+90 satır, −0 satır.**
  `GuardDeletable` + `CountByMaterial` private metotları; `Delete()` içine tek satır çağrı.
  **Mevcut hiçbir satır değiştirilmedi.**
- `tests/DepoWise.Tests/MaterialDeleteGuardTests.cs` — **yeni**, 8 test.

**Neden tek dosya yetti:** Masaüstü `MaterialService.Delete`'i **doğrudan** çağırıyor, web ise
aynı metodu API üzerinden çağırıyor → tek nokta hem iki platformu hem **doğrudan API çağrısını**
birlikte koruyor. UI'da düğme gizlense bile koruma devrede.

**Migration:** YOK · **Veri değişikliği:** YOK · **Canlı sisteme dokunulmadı.**

**Testler:** Toplam **1025** — **992 geçti, 0 başarısız, 33 atlandı**.
(MLZ-01 öncesi 1017/984/0/33 → tam **+8** yeni test, sıfır kırılma.)

**İleriye bağımlılık:** `MLZ-01-DEPO` — depo mimarisi gelince `GuardDeletable`'daki stok okuması
`SUM(quantity)` olmalı. `STK-05`'e bağlandı, bkz. §15.

**Bu plandan ÖNCE tamamlanmış olan ilgili işler (bağlam için):**
- Tasarım paketi (FAZ 1-9 web + M1-M5 masaüstü) — yayınlandı, masaüstü 1.0.136
- Masaüstü menü vektör ikonları (M2.5) — `feature/masaustu-vektor-ikonlar` dalında,
  **görsel doğrulama bekliyor**, `master`'a alınmadı

---

## 12. AKTİF İŞ

**`KLT-01` — Eksik düzenleme kilitleri (yakıt, stok belgeleri, muayene, kullanıcılar)**
**DURUM:** `BEKLEMEDE` → geliştirmeye hazır

Detay §5'te. Kullanıcı "sıradaki iş" dediğinde bu iş için önce kısa analiz sunulacak,
sonra geliştirmeye geçilecek.

**Not:** MLZ-01 kodu dalda **commit edilmemiş** durumda duruyor (kullanıcı commit istemedi).
Bir sonraki işe geçmeden önce commit kararı verilmeli.

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
| 3 | ~~Malzeme silmede koruma yok~~ | ~~Yüksek~~ | ✅ **`MLZ-01` ile kapatıldı (2026-08-10)** |
| 3b | İki `BranchService` + `user_scopes` yazanı yok | Orta | `TMZ-02` — §6'da |
| 4 | Yakıt/stok belgeleri/muayenede LWW koruması yok (kendi kuralımıza aykırı) | **Yüksek** | `KLT-01` |
| 5 | Bakımda negatif stok ↔ onay çelişkisi | Orta | KARAR-4 |
| 6 | `ListColumns` iki kopya — biri unutulursa ekran sessizce bozulur | Orta | `TMZ-01` |
| 7 | 33 test neden atlanıyor bilinmiyor | Orta | `TST-01` |
| 8 | Tam ekran paritesi denetlenmedi | Orta | `PRT-01` |
| 9 | Masaüstü vektör ikonları görsel doğrulama bekliyor | Düşük | Kullanıcı bakacak |
| 10 | **`MLZ-01-DEPO`** — aşağıya bakınız | **Orta** (gelecekte yüksek) | `STK-05` |

### ⚠️ `MLZ-01-DEPO` — Depo mimarisi geldiğinde MLZ-01'de yapılacak zorunlu düzeltme

**Depo mimarisi uygulandığında `MaterialService.GuardDeletable` içindeki stok kontrolü,
toplam depo stoklarını dikkate alacak şekilde güncellenecek.**

Ayrıntı (koddan doğrulandı):
- Bugünkü satır: `SELECT quantity FROM stock_balances WHERE material_id=@m AND company_id=@c;`
- `stock_balances` bugün PK `(material_id)` olduğu için malzeme başına **tek satır** var →
  sorgu **bugün doğru çalışıyor**.
- Depo katmanı gelince tablo malzeme × depo başına **çok satırlı** olacak. `ExecuteScalar`
  yalnız **ilk satırı** okur → "toplam stok bu" sanır. Sonuç: **bir depoda mal olduğu hâlde
  malzeme silinebilir.** Hata **sessizdir** (istisna fırlatmaz).
- Düzeltme: tek satır okuma yerine **`SUM(quantity)`** (tüm depolar).
- Etkilenmeyen kısım: `CountByMaterial` (hareket/bakım/talep/sayım) `material_id` bazlıdır,
  depo katmanından **etkilenmez** — değişiklik gerekmez.

**Bugün MLZ-01 kodu DEĞİŞTİRİLMEYECEK** (bugünkü şemada doğru çalışıyor). Düzeltme `STK-05`
kapsamında yapılacak ve `MaterialDeleteGuardTests`'e depo senaryosu eklenecek.

---

## 16. TEST DURUMU

**Son ölçüm (2026-08-10, `feature/mlz-01-malzeme-silme-korumasi`):**
- Build: **0 hata**
- Test: **1025 toplam — 992 geçti, 0 başarısız, 33 atlandı**
- Önceki ölçüm (2026-08-09, `master`): 1017 — 984 / 0 / 33 → **+8 test, sıfır kırılma**

**`TST-01` cevaplandı (2026-08-10):** Atlanan 33 testin **tamamı `Postgres*` sınıflarındandır**
(`PostgresStockConcurrencyTests`, `PostgresStockMovementOrderingTests`, `PostgresSyncRecoveryTests`,
`PostgresTurkishSearchTests` vb.). Yerel makinede PostgreSQL bağlantı dizesi tanımlı olmadığı için
kendilerini atlıyorlar — **bozuk test değil, ortam eksikliği**. Gizlenmiş bir hata yok.
Geriye kalan iş: bu testlerin CI'da veya bağlantı dizesi verilerek düzenli koşturulmasını sağlamak.

**Her iş için zorunlu testler (proje geneli, ekrandan bağımsız):**
tenant sızıntısı · permission (UI **ve** API) · rollback · negatif stok · sayaç geriye gitme ·
idempotent retry · çevrimdışı kalıcılık · update rollback

**Ekran bazlı QA:** Değiştirilen ekran için Coverage Matrix + `docs/tests/<Ekran>_Test_Report.md`
(CLAUDE.md §7). Kapsam **yalnız değiştirilen ekran** — genel regresyon yalnız açıkça istenirse.

---

## 17. SONRAKİ ADIM

**`KLT-01` — Eksik düzenleme kilitleri (yakıt, stok belgeleri, muayene, kullanıcılar).**

⚠️ **Önce karar:** MLZ-01 kodu dalda commit edilmemiş duruyor. Yeni işe geçmeden önce
commit edilmeli (aksi halde iki işin değişikliği birbirine karışır).

Kullanıcı **"sıradaki iş"** dediğinde:
1. Bu dosya okunur, aktif iş ve son tamamlanan iş kontrol edilir.
2. `KLT-01` için kısa analiz sunulur (hangi servisler, `version` kolonu var mı, migration gerekir mi).
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
