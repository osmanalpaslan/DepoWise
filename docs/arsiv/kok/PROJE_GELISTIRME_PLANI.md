# DEPOWISE / ALPNEX — ANA GELİŞTİRME PLANI

> **Bu dosya nedir?** Bundan sonraki bütün geliştirmelerin ana çalışma dosyası.
> "Şu anda nerede kaldık?" sorusunun cevabı en üstteki durum bloğundadır.
> Mevcut durum fotoğrafı için: [PROJE_GENEL_DURUM_ANALIZI.md](PROJE_GENEL_DURUM_ANALIZI.md)

> ### 🧭 NEREYE BAKMALI — dört kova (2026-08-10, kullanıcı kuralı)
> | Soru | Bölüm |
> |---|---|
> | **Şimdi yapılacaklar** — mevcut aşamada kodlanacak işler | **§5** + §9 sırası + §12 aktif iş |
> | **Gelecek fazlar** — henüz kodlanmayacak, mimari olarak planlanmış | **§6** (ertelenen) + §5'teki `BEKLEMEDE` işler |
> | **Yatırım / canlıya geçiş öncesi** — para veya profesyonel altyapı isteyen | **§7** (iş) + [docs/MALIYET_KALEMLERI.md](docs/MALIYET_KALEMLERI.md) (**para**) |
> | **Kullanıcı kararı gerekenler** — ürün kararı olmadan uygulanmaz | **§14 KARARLAR** (`KARAR-4`, **`KARAR-7`**, `YET-01`) |
>
> **Her yeni geliştirmeden ÖNCE:** bu planı oku → uzun vadeli hedeflerle (§3, §3.1) çelişiyor mu bak →
> eksik faz varsa tespit et → yeni özellik gerekiyorsa **önce öner, onay al** → maliyetli işi §7'ye
> yaz → çalışan yapıyı gereksiz yeniden tasarlama → en minimal ve düşük riskli çözümü seç →
> kurumsal kullanımı engelleyecek teknik borç bırakma → önemli mimari kararı buraya **kalıcı** yaz.

---

```
AKTİF AŞAMA:         FAZ 1 — Senkron optimizasyonu (FAZ 0 kod tarafı BİTTİ)
AKTİF İŞ ID:         YOK — aktif kod işi yok
AKTİF İŞ:            —
DURUM:               ✅ FAZ 1 SENKRON OPTİMİZASYONU (SNK-01…04) TAMAMLANDI (2026-08-10)
                     SNK-01 ❌ · SNK-02 ✅ · SNK-03 ✅ · SNK-04 ❌
                     ✅ KLT-01 KAPANDI · ✅ MLZ-01
SON TAMAMLANAN İŞ:   SNK-04 — analiz sonucu ZATEN YAPILMIŞ (2026-08-10)
SONRAKİ İŞ (ÖNERİ):  PRT-01 Grup 3 (Bakım + Yakıt) — önce ANALİZ
                     ✅ GRUP 2 TAMAM: 2a (G2-01…G2-05, G2-07) + 2b (Şablonlar)
                     ⚠️ Her aşama için ayrı ONAY gerekir; kendiliğinden başlanmaz
YENİ TEKNİK BORÇ:    MUA-01 (muadil transitif↔doğrudan uyuşmazlığı — ÜRÜN KARARI)
                     MUA-02 (EnsureOwned silinmiş malzemeyi kabul ediyor — yalnız kayıt)
                     ARC-01 (Vehicles.razor'da EditNav ölü — araç tam formu web'den
                             ulaşılamıyor; G2-01'in aynısı, Grup 5 kapsamı)
                     B-6/B-7/B-9 (şablon: virgüllü TEXT · FK yok · sayfalama yok)
İPTAL (2026-08-10):  SNK-01 — koruma kodda zaten vardı (c8d3dc7, 2026-07-19)
                     SNK-04 — koruma kodda zaten vardı (b2604de, 2026-07-11)
AÇIK DOĞRULAMA:      SNK-02 + SNK-03 çalışma zamanı/HTTP davranışı — GUI oturumu sınırı (§5)
                     G1-02 · G2-04 · G2-05 · G2-07 · Grup 2b masaüstü GUI davranışı —
                     Avalonia GUI otomasyon sınırı (kod+test doğrulandı, GUI gözlenmedi)
BEKLEYEN KARAR:      KARAR-4 (bakımda negatif stok ↔ onay) — FAZ 5'e kadar beklenebilir
                     KARAR-7 (malzeme silme şube bazlı mı?) — FAZ 4 KAPISINDA gerekli 🆕
                     YET-01 (yetki modeli) — FAZ 2'ye girmeden ÖNCE gerekli
YENİ BULGU:          WEB-01 — web hata mesajlarında ham JSON (§6, ayrı iş, fazlanmadı)
                     GNL-03 · LOG-02 · PRF-01 — 2026-08-10 ikinci gözden geçirmede eklendi
SON GÜNCELLEME:      2026-08-10 (PRT-01 GRUP 2 tamamlandı: 2a G2-01…G2-05+G2-07 ve
                     2b Şablonlar. YET-01 kararları A1-A6 + F0 PermissionSnapshot.
                     Güncel ölçüm: Build 0 hata · Test 1080/1047/0/33)
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

### 3.1 Hedeflerin yeniden teyidi + ÜRÜN VİZYONU (2026-08-10, ikinci gözden geçirme)

**Kullanıcının ürün vizyonu (kalıcı kayıt):** Proje tek kişilik (babasının kullandığı) minimalist bir
depo uygulaması olarak başladı; artık hedef **web + masaüstü, çok kullanıcılı, çok birimli, çok rollü,
çok şubeli, büyük firmalara SATILABİLİR ticari ürün**. Bundan sonraki her geliştirme yalnız mevcut
bulguyu kapatmakla yetinmez; **mevcut mimarinin bu hedefi engellememesi** gözetilir.

**Maliyet politikası (bağlayıcı):** Yatırım/finansman bulunana kadar **maliyetli profesyonel altyapı
ertelenir**; önce mevcut teknolojiyle mümkün olan **en düşük maliyetli** çözüm. Gereksiz ücretli servis /
SaaS / üçüncü parti altyapı / sunucu maliyeti / lisans **eklenmez**. Ancak ertelenen her maliyetli iş
**§7 + [docs/MALIYET_KALEMLERI.md](docs/MALIYET_KALEMLERI.md)** içinde kayıt altında tutulur; kullanıcı
"yatırım buldum, canlıya almadan önce neleri parayla yapmamız gerekiyordu?" diye sorduğunda cevap
**proje dosyalarından** çıkarılabilmelidir.

**Gözden geçirme sonucu:** Kullanıcının 2026-08-10'da ayrıntılandırdığı 17 uzun vadeli gereksinim
madde madde bu planla karşılaştırıldı. **Çoğu zaten H-1…H-12 altında kayıtlıydı ve iş kalemine
bağlanmıştı** (mükerrer iş açılmadı). Yalnız aşağıdaki **dört konu gerçekten eksikti** ve bu turda
eklendi; ayrıca **bir konu önceki bir kullanıcı kararıyla çelişiyor** ve karara bağlanmayı bekliyor:

| Yeni/eksik | Nereye eklendi |
|---|---|
| Kayıt tipi **kataloğu yok** (bugün `activity_type` sabit metin) → `YTK-02` ve `GNL-02`'nin **önkoşulu** | §5 `GNL-03` (yeni) |
| `audit_logs.before_json/after_json` **var ama doldurulmuyor** → "önceki/yeni değer" denetimi bugün imkânsız | §5 `LOG-02` (yeni) |
| **Ölçek darboğaz haritası** yazılı değil (H-9 hedef var, ölçüm yok) | §5 `PRF-01` (yeni) |
| `PRT-01` **Grup 2'nin Şablonlar yarısı** ayrı analiz aşaması olarak işaretli değildi | §5 `PRT-01` altına eklendi |
| ⚠️ **Şube bazlı malzeme silme** isteği, 2026-07-26 "malzeme kataloğu firma-geneli" kararıyla **çelişiyor** | §15 `KARAR-7` (kullanıcı kararı bekliyor) |

**Mimari not (yetki genişletmesini ucuzlatır):** `PermissionAction` yalnız **View/Create/Edit/Delete**
içerir; `Approve`/`Report` **yoktur**. Ama projenin kendi çözdüğü desen bellidir: Talep Onaylama, buton
yetkisinden (`btn-approve`, LEGACY) **ayrı bir MODÜLE** taşınmıştır (`request_approval`, Migration035).
→ **Yeni işlem yetkileri için enum genişletilmez; modül eklenir** veya `user_button_permissions`
kullanılır. `YTK-01`/`YTK-02` bu yüzden düşük maliyetlidir. **Yetki sistemi baştan YAZILMAYACAK.**

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

**ID:** `KLT-01` ◄ ✅ **TAMAMLANDI (2026-08-10)**
**Başlık:** Eksik iyimser (optimistic) düzenleme kilitleri
**DURUM:** ✅ `TAMAMLANDI` — 3 alt iş bitti, 2 alt iş gerekçeli iptal
— ⚠️ **KAPSAM 2026-08-10'da DÜZELTİLDİ**

> ### ⚠️ ÖNEMLİ KAPSAM DÜZELTMESİ
> Planın ilk hâli hedefleri **yakıt, stok belgeleri, muayene, kullanıcılar** diye yazıyordu.
> Bu, *"bu servisler `EditLockGuard` kullanmıyor"* sinyaline dayanıyordu. **Sinyal yanıltıcıymış:**
> bu servislerin çoğunun **düzenleme yolu hiç yok**. Kod incelemesi sonucu (2026-08-10):
>
> | Eski hedef | Gerçek durum | Yapılacak |
> |---|---|---|
> | **Yakıt** | `FuelService`'te **Update metodu YOK** — yalnız `AddDepotEntry`, `Distribute` (ekleme) ve `CancelDepotEntry`/`CancelDistribution` (iptal). İptal `BeginImmediate` + transaction içi durum kontrolü + *"zaten iptal edilmiş"* engeliyle **KORUMALI**. | ❌ **İŞ YOK** |
> | **Stok belgeleri** | `StockService`'te **Update metodu YOK** — oluşturma (`ReceiveIn`/`IssueOut`/`Transfer`) + `ReverseDocument` (ters kayıt). Ters kayıt `BeginImmediate` ile korumalı. | ❌ **İŞ YOK** (ters kayıtta çift-tersleme testi eklenmeli) |
> | **Muayene** | `InspectionService.Save` **yalnız INSERT** — her çağrıda yeni id üretiyor, güncelleme yolu yok. Kayıtlar ekleme-yalnız. | ❌ **İŞ YOK** |
> | **Kullanıcılar** | **GERÇEK RİSK VAR — ama beklenen yerde değil.** Aşağıda `KLT-01c`. | ✅ **İŞ VAR** |
>
> **Sonuç:** CLAUDE.md §4'ün "yakıt/stokta LWW yasak" kuralı **ihlal edilmiyordu** —
> o kayıtlar zaten düzenlenemiyor. Plan bu noktada yanlıştı, düzeltildi.

**Yeni kapsam — gerçek boşluklar (ölçüt: `version=version+1` yapıyor AMA `expectedVersion`
kontrolü yok, ve gerçek bir düzenleme yolu var):**

| Alt iş | Servis / metot | Risk | Öncelik |
|---|---|---|---|
| **`KLT-01a`** | `RequestOperationsService.ChangeStatus` + `UpdateShipmentInfo` | ✅ **TAMAMLANDI (2026-08-10)** — `ef905d6`. `UpdateShipmentInfo`'ya `material_requests.version` jetonu eklendi; `ChangeStatus` durum makinesiyle zaten korunuyordu, kontrol EKLENMEDİ (regresyon testi eklendi) | **P1** |
| ~~**`KLT-01b`**~~ | ~~`LookupService.Rename`~~ | ❌ **İPTAL (2026-08-10)** — LWW bu tanım tablolarında **mimari politika** (`BusinessSyncService` + `CLAUDE.md` §4 yasağı tanımları kapsamıyor). Kural ihlali yok, risk düşük. `expectedVersion` eklemek senkron LWW'siyle **iki farklı çakışma politikası** yaratırdı | — |
| **`KLT-01c`** | `PermissionService.SaveForUser` | ✅ **TAMAMLANDI (2026-08-10)** — `18a21f8`, aşağıya bakınız | **P1** |
| **`KLT-01d`** | **`MaterialTemplateService.Update`** — *(2026-08-10 DARALTILDI)* | ✅ **TAMAMLANDI (2026-08-10)** — `4f3524a`. 12 alan körlemesine yazılıyordu → `material_templates.version` jetonu. Çakışmada commit yok → alanlar **ve audit** yazılmıyor. `PersonnelTitleService` (Update yolu YOK) ve `CompanyService` (yalnız süper admin) **kapsamdan ÇIKARILDI**. Otomatik test **+ gerçek HTTP/web QA** ile doğrulandı | P2 |

**Ayrıca doğrulanan bir tuhaflık:** `users` tablosunda `version` kolonu **var ama hiç
artırılmıyor** (`UserService`'te 8 UPDATE, `version=version+1` sıfır). Kullanıcı düzenlemesi
alan-alan ayrı metotlara bölünmüş (`SetActive`, `ChangePassword`, `LinkPersonnel`,
`SetViewAllBranches`) → farklı kolonlara yazdıkları için **birbirlerini ezmiyorlar**.
Yani *"Admin A şubeyi, Admin B yetkiyi değiştiriyor"* senaryosu **yapı gereği güvenli**;
gerçek risk `KLT-01c`'deki toplu yetki değişimidir.

**Öncelik:** **P1** (P0 değil — düzeltildi) · **Bağımlılık:** **YOK** — `YET-01`'e bağlı değil
**Web:** ✅ · **Masaüstü:** ✅ · **API:** ✅
**Migration:** ❌ **GEREKMİYOR** — ilgili tablolarda `version` kolonu mevcut
**Canlı veri riski:** Yok (yalnız koruma ekliyor) · **Maliyet:** Düşük-Orta
**Test gereksinimi:** Her alt iş için "aynı version ile iki kayıt → ikincisi 409" ·
farklı kayıtlar birbirini engellemiyor · conflict'te yarım veri yazılmıyor (transaction) ·
`KLT-01c` için "iki admin aynı kullanıcının yetkisini düzenler → ikincisi uyarı alır"

---

#### ✅ `KLT-01c` TAMAMLANDI (2026-08-10) — commit EDİLMEDİ

**Seçilen çözüm:** Yetki kümesinin eşzamanlılık jetonu **`users.version`**.

**Neden bu (üç seçenek değerlendirildi):**
| Seçenek | Değerlendirme |
|---|---|
| `user_permissions` satır sürümü | ❌ **İşe yaramaz** — kayıt "sil + yeniden yaz" olduğu için satırlar zaten yok ediliyor; satır sürümü KÜMEYİ koruyamaz. |
| Mevcut kümeden parmak izi/hash | ❌ Şema değişikliği istemez ama kırılgan ve projenin `EditLockGuard` desenine yabancı. |
| **`users.version`** | ✅ **Seçildi.** Kolon şemada **zaten vardı**, iki lehçede de mevcut → **migration YOK**. Yetki kümesinin sahibi kullanıcı kaydıdır (doğru sahiplik noktası). Koddan doğrulandı: kolonun **hiçbir okuyucusu yok**, hiç artırılmıyordu ve senkron upsert'i (`ImportServerUser` `ON CONFLICT DO UPDATE`) `version`'a **dokunmuyor** → jeton geri gitmiyor, hiçbir mevcut davranış bozulmuyor. |

**Uygulama:** `GetForUser` sürümü döndürür → API `version` alanıyla taşır → web/masaüstü ekranı
tutar → `SaveForUser`'a geri gönderilir. Sürüm artırma + kontrol, **silme/yazmadan hemen önce ve
aynı transaction içinde**; çakışmada hiçbir DELETE/INSERT çalışmaz (kısmi yazma imkânsız).
`expectedVersion = null` → kontrol yok (yeni kullanıcı oluşturma akışı ve eski istemciler bozulmaz).

**Değişen dosyalar (5 + 1 test):** `PermissionService.cs` · `Program.cs` (API GET/POST + DTO) ·
`OrgServerClient.cs` · `PermissionsViewModel.cs` · `Permissions.razor` ·
`tests/PermissionConcurrencyTests.cs` (yeni, 8 test)

**Testler:** 1033 toplam — **1000 geçti, 0 başarısız, 33 atlandı** (+8 yeni, regresyon yok).

**UI davranışı:** Masaüstü, Şube ekranındaki kanıtlanmış deseni kullanır
(*"Güncel yetkileri yükle / Ekranda kal"*) — yöneticinin işaretledikleri sorulmadan silinmez.
Web'de değişiklik korunur, mesaj gösterilir.

**Teknik bulgu (kapsam dışı, kaydedildi):** `RoleKeys` içinde `Warehouse`, `Manager`,
`Operation`, `ReadOnly` sabitleri **tanımlı ama `RoleKeys.Seed`'de YOK** → veritabanında
karşılıkları oluşmuyor, bu rollerle kullanıcı oluşturulamıyor (*"Rol bulunamadı"*).
Kullanılan roller: SuperAdmin, RestrictedSuperAdmin, CompanyAdmin, Staff.
**`YET-01` kapsamına aittir** (rol modeli kararı) — burada düzeltilmedi.

> **KLT-01 ≠ gerçek kayıt kilidi.** KLT-01 mevcut **iyimser** korumayı (kaydederken 409)
> eksik 4 servise yayar. Kullanıcının istediği *"ikinci kişi kayda giremesin, adı görünsün"*
> davranışı **`KLT-02/03/04`**'tür (kiralama tabanlı, KARAR-3). İkisi karıştırılmamalı.
> KLT-01 ucuz ve bağımsızdır; KLT-02+ sunucu tarafı altyapı ister.

---

### FAZ 1 — Düşük maliyet, yüksek getiri (P1)

---

**ID:** ~~`SNK-01`~~ ◄ ❌ **İPTAL EDİLDİ (2026-08-10)**
**Başlık:** ~~Değişiklik yoksa push yapma~~
**DURUM:** ❌ `İPTAL` — **yapılacak iş yok, koruma kodda zaten mevcut**

> **İptal gerekçesi (koddan doğrulandı, 2026-08-10 detay analizi):**
>
> | Kanıt | Bulgu |
> |---|---|
> | [`BusinessSyncPushService.cs:55`](src/DepoWise.Desktop/BusinessSyncPushService.cs:55) | `if (localV <= pushWm) return;` → yerel değişiklik yoksa **snapshot hiç üretilmiyor, push HTTP isteği hiç atılmıyor** |
> | `git log -S` | Mekanizma **`c8d3dc7`** commit'i ile **2026-07-19**'da eklenmiş; bu plan **2026-08-10**'da yazıldı → plan 22 gün geriden geliyordu |
> | Sonuç | **Kod değişikliği yok, performans kazancı sıfır** |
>
> **Planın doğru olan kısmı:** *"15 sn'de ~5-6 istek, çoğu boşa"* tespiti **doğrudur** —
> boşta tick başına **5** istek gidiyor: `/health` · `/api/machines/register` ·
> `/api/me/authsig` · `/api/sync/business-version` · `/api/sync/conflicts/unseen`.
> **Ama bunların hiçbiri push değildir**; en pahalı istek (snapshot POST) zaten eleniyor.
> Boştaki yükün kaynağı **aralığın kendisidir** → çözüm **`SNK-02`**'dir, `SNK-01` değil.
>
> Ayrıntılı kayıt + yanlış varsayım dersi:
> [docs/PROJE_DURUMU_VE_ILERLEME.md](docs/PROJE_DURUMU_VE_ILERLEME.md) §13.

**ID:** `SNK-02` ◄ ✅ **TAMAMLANDI (2026-08-10)**
**Başlık:** ~~Boştayken senkron aralığını seyreltme (15 sn → 60 sn)~~ →
**Seçici senkron kadansı (daraltılmış kapsam — 2a)**
**DURUM:** ✅ `UYGULANDI / KOD DOĞRULANDI — GERÇEK HTTP QA YAPILAMADI`

> **Kapsam neden daraltıldı (kullanıcı kararı, 2026-08-10):** Planın aslı (tüm döngü 15→60 sn)
> uçtan uca veri görünürlüğünü ~120 sn'ye çıkarırdı ve **ADR-099'daki "veriler anlık görünmeli"
> kararına aykırıydı** → reddedildi. Claude'un B önerisi de (`authsig` 60 sn) yetki değişikliği
> algılamasını gereksiz geciktirdiği için reddedildi. Uygulanan: **2a**.

| Uç | Kadans | Gerekçe |
|---|---|---|
| `business-version` (+push/pull) | **15 sn** | ADR-099 duyarlılığı korunur |
| `authsig` | **15 sn** | Yetki/şifre değişikliği algılama gecikmesi artmasın |
| `machines/register` | **15 sn** | Makine iptali algılama gecikmesi artmasın (**2a**) |
| `/health` | **60 sn** | Yalnız bağlantı rozeti |
| `conflicts/unseen` | **60 sn** | Çözülmüş çakışmaların bildirimi |

**Uygulama:** Mevcut 15 sn'lik timer korundu; **tick sayacı** (`_tick % 4`) eklendi.
**Yeni timer YOK · aktivite takibi YOK · `SyncGate` değişmedi · `WarnConflictsAsync` gating'i
korundu (parametreyle atlanıyor, dışarı taşınmadı) · push/pull/watermark/LWW DEĞİŞMEDİ.**
**Değişen dosya:** yalnız `ShellViewModel.cs` (+33/−3) · **Migration:** ❌ ·
**Web/API/Infrastructure:** dokunulmadı.

**Beklenen kazanç: TEORİK %30** (20 → 14 istek/dk/makine) — ⚠️ **hesaplandı, ölçülmedi**.
**Build:** 0 hata · **Test:** 1057 / 1024 / 0 / 33 (referansla aynı, regresyon yok).

> ### ⚠️ DOĞRULAMA SINIRI (başarısızlık değil)
> **Gerçek HTTP kadans ölçümü yapılamadı.** Kadansı çalıştıran `ShellViewModel` yalnız girişten
> sonra başlıyor; Avalonia giriş penceresi geliştirme ortamından görüntülenemiyor (etkileşimli
> masaüstü oturumu yok) → giriş yapılamıyor, zamanlayıcı başlamıyor.
> İzole QA ortamı **güvenle kuruldu** (ayrı build klasörü + localhost yönlendirmesi + ayrı
> veritabanı `QA-SNK02`); **canlı sunucuya 0 istek** gitti; gerçek veritabanı açılmadı; gerçek
> build klasörüne `serverurl.txt` konulmadı; QA süreçleri kapatıldı.
> **HTTP kadansı "gerçek ortamda doğrulandı" olarak yazılmayacaktır** — kadans mantığı yalnız
> **kod düzeyinde** doğrulandı. Ölçüm, kullanıcının kendi oturumunda ayrı bir tur olarak yapılabilir.

**ID:** `SNK-03` ◄ ✅ **TAMAMLANDI (2026-08-10)**
**Başlık:** Hata halinde exponential backoff
**DURUM:** ✅ `TAMAMLANDI / UYGULANDI` — **bağımlılık `SNK-02` karşılandı**
**Öncelik:** P1 · **Masaüstü:** ✅ · **Migration:** ❌ · **API:** ❌ · **Yeni bağımlılık:** ❌

**Seçilen çözüm: B2 — sınıflandırmalı backoff** (kullanıcı kararı). Geçici sunucu/ağ hatasında
iş verisi senkron turu (`business-version` + push + pull) kademeli olarak seyreltilir.

| Hata türü | Backoff |
|---|---|
| Taşıma/ağ/DNS/bağlantı · zaman aşımı · HTTP 5xx · HTTP 429 | ✅ **tetikler** |
| HTTP 401 / 403 / diğer 4xx · JSON/veri hataları | ❌ **tetiklemez** (normal hata akışı) |
| Z3 "sunucu satır atladı" | ❌ tetiklemez (kendi retry'ı var) |

**Dizi:** `15 → 30 → 60 → 120 → 240 → 300 sn` · **±%20 jitter** ·
**jitter dahil mutlak maksimum 300 sn** · **başarılı turda sıfırlanır**.

**Korunanlar:** Backoff kontrolü **`SyncGate`'ten ÖNCE** (kapı tutulmaz) · manuel "Eşitle"
**bypass** eder ve başarıda sıfırlar · login/import/personel bağlama/kapanış push'u backoff'a
tabi değil · `authsig`, `machines/register`, `/health`, `conflicts` kadansları **değişmedi**
(SNK-02 2a kararı korundu) · yeni timer yok · `Task.Delay` yok · push/pull/watermark/LWW değişmedi.

**Değişen dosyalar (3):** `BusinessSyncPullService.cs` · `BusinessSyncPushService.cs` ·
`ShellViewModel.cs` — toplam **+109/−7**. `tests/` **değişmedi**.
**Build:** 0 hata · **Test:** 1057 / 1024 / 0 / 33 (regresyon yok).

> ### ⚠️ DOĞRULAMA SINIRI
> **Kod incelemesi + build/regresyon testleri ile doğrulandı; çalışma zamanı/HTTP davranışı
> GUI/QA ortamı sınırı nedeniyle gözlenmedi.** (Sebep `SNK-02` ile aynı; ayrıca `DepoWise.Desktop`
> test projesinde referanslı değil.) Ayrıntı:
> [docs/PROJE_DURUMU_VE_ILERLEME.md](docs/PROJE_DURUMU_VE_ILERLEME.md) §6.2.

**ID:** ~~`SNK-04`~~ ◄ ❌ **ZATEN YAPILMIŞ / İPTAL (2026-08-10)**
**Başlık:** ~~Günlük yedek kontrolünü senkron turundan ayırma~~
**DURUM:** ❌ `ZATEN YAPILMIŞ / İPTAL` — **yapılacak iş yok, koruma kodda zaten mevcut**

> **İptal gerekçesi (koddan doğrulandı, 2026-08-10 detay analizi):**
>
> | Kanıt | Bulgu |
> |---|---|
> | `ShellViewModel.cs:410` | Metodun **İLK** satırı: `if ((DateTime.UtcNow - _lastBackupCheck).TotalHours < 1) return;` → saatlik kısıt |
> | `git log -S "_lastBackupCheck"` | **`b2604de` · 2026-07-11** |
> | `git log -S "MaybeDailyBackupAsync"` | **`b2604de` · 2026-07-11** — **aynı** commit |
> | Sonuç | Koruma, metodun **oluşturulduğu commit'ten beri** var; plan (2026-08-10) **bir ay geriden** geliyordu |
>
> **Plan ne diyordu:** *"`MaybeDailyBackupAsync` her 15 sn'de çalışıyor; saatte bir yeterli."*
> **Gerçek:** metot her tick'te **çağrılıyor** ama ilk satırında dönüyor — *"çağrılıyor"* ile
> *"iş yapıyor"* karıştırılmış. İki katmanlı koruma var: **saatlik kısıt** + **günlük kısıt**
> (`hasToday`). 15 sn'de gerçekten çalışan iş bir `DateTime` çıkarma+karşılaştırmadır; pahalı
> işler (yetki kontrolü, `ListBackups()` disk taraması, yedekleme, buluta yükleme) **zaten
> saatlik kısıtın arkasındadır**.
>
> **Kod değişikliği yapılmadı · yeni test gerekmedi · `SNK-02` ve `SNK-03` davranışları
> değiştirilmedi · migration/API/`.csproj`/yeni bağımlılık yok.**
>
> Ayrıntılı kayıt + yanlış varsayım dersi:
> [docs/PROJE_DURUMU_VE_ILERLEME.md](docs/PROJE_DURUMU_VE_ILERLEME.md) §6.3 ve §13.

**ID:** `PRT-01`
**Başlık:** Tam ekran parite denetimi (alan/işlev düzeyinde)
**Açıklama:** 43 web + 36 masaüstü ekranın alan, işlev, validasyon, yetki düzeyinde karşılaştırılması.
Analizde yalnız **ad düzeyinde** yapılabildi.
**Neden gerekli:** "Ortak olması gerekirken eksik" ekranlar/alanlar ancak böyle bulunur.
Sonucu yeni iş kalemleri doğurur.
**Öncelik:** P1 · **Bağımlılık:** Yok · **Migration:** ❌ · **Maliyet:** Orta
**Çıktı:** `PROJE_GENEL_DURUM_ANALIZI.md` §4-5 güncellenir + yeni işler bu dosyaya eklenir
**DURUM:** 🔵 `DEVAM EDİYOR` — envanter + **Grup 1 (stok) tamamlandı**, kalan 5 grup bekliyor

> ### Envanter sonucu (2026-08-10, koddan)
> Web **43 sayfa / 47 route** · Masaüstü **38 menü hedefi** (+5 menü dışı).
> Web'de olup masaüstünde olmayan **7 ekran** — yedisi de `IsSuperAdmin` kapılı sunucu yönetim
> ekranı → **kasıtlı**, parite kusuru değil. Masaüstünde olup web'de olmayan: "Hakkında" (P3),
> Eşitleme penceresi (doğası gereği masaüstü).
> **Kolon paritesi yapısal olarak garantili:** web `<Compile Include="…Application/Ui/ListColumns.cs">`
> ile masaüstüyle **aynı dosyayı** derliyor (İş #10, 2026-08-09).
> Yetki modülleri 12 ana ekranın **11'inde birebir aynı**.
>
> ### ✅ GRUP 1 — Stok ekranları (Giriş-Çıkış · Hareketler · Sayım) — commit `8bf27cb`
> 18 kategorilik derin karşılaştırma yapıldı; 9 fark bulundu, **6'sı giderildi**.
>
> | Bulgu | Durum | Doğrulama |
> |---|---|---|
> | **G1-01** Web'de mevcut bakiye gösterilmiyordu | ✅ **TAMAMLANDI** | Gerçek tarayıcı QA: "Mevcut stok: 137.5" = API değeri |
> | **G1-03** Sayımda fark=0 satırları gönderilmiyordu | ✅ **TAMAMLANDI** | Gerçek HTTP QA: fark=0 satırı raporda, adjustment üretmedi |
> | **G1-04** Web'de alt kategori alanı yoktu | ✅ **TAMAMLANDI** | Gerçek tarayıcı QA: kaskad + kayıtta alt kategori ID'si |
> | **G1-05(a)** Web `operationId` göndermiyordu | ✅ **TAMAMLANDI** | Gerçek HTTP QA: aynı jetonla 2 istek → bakiye **bir kez** düştü |
> | **G1-07** Hareketlerde hata sessizce boş liste görünüyordu | ✅ **TAMAMLANDI** | Gerçek tarayıcı QA: API kapalıyken uyarı, açılınca temizlendi |
> | **G1-02** Masaüstünde toplu sayım yoktu | ⚠️ **KOD TAMAM — GUI QA YAPILAMADI** | Kod + servis/veri katmanı doğrulandı (build + 1057 test + gerçek HTTP); **masaüstü sepet UI davranışı çalışırken GÖZLENEMEDİ** |
>
> **Uygulamada `StockService` / `ReportService` DEĞİŞMEDİ**; migration, `.csproj`, yeni bağımlılık
> ve `tests/` değişikliği yok. API sözleşmesi yalnız **genişledi** (opsiyonel `OperationId`).
>
> ### ⏳ Grup 1'den AÇIK KALANLAR (tamamlanmadı)
> | # | Konu | Durum |
> |---|---|---|
> | **G1-06** | Web başarı mesajları masaüstüne göre az ayrıntılı (P3) | ⏳ **AÇIK** |
> | **G1-08** | Web sayım ekranında "son düzeltmeler" listesi yok (P3) | ⏳ **AÇIK** |
> | **G1-09** | Hareketlerde "Yön" ayrı kolon değil (P3) | ⏳ **AÇIK** — değişiklik **önerilmedi** (işlevsel eşdeğer) |
> | — | **Hareketsiz belge idempotency boşluğu:** tamamı fark=0 olan sayım `stock_movements` üretmediği için `FindDocumentByOperation` belgeyi bulamaz → aynı jetonla tekrar gönderilirse ikinci belge oluşur | ⏳ **AÇIK** — `StockService` değişikliği ister, kapsam dışı bırakıldı |
> | — | **G1-02 GUI QA** — 6 senaryo (tek satır, çoklu, aynı malzeme tekrar, satır silme, fark=0, boş sepet) | ⏳ **AÇIK** |
>
> ### 🔵 GRUP 2 — Malzemeler + Şablonlar (2026-08-10: analiz YAPILDI, kısmen uygulandı)
> **Grup 2 iki yarıdan oluşur; yalnız birincisi analiz edildi:**
>
> | Yarı | Durum |
> |---|---|
> | **2a — Malzemeler** (`Materials.razor` · `MaterialEditDialog.razor` · `MaterialsView.axaml` + VM · `MaterialQuickEditWindow`) | 🔵 **Analiz TAMAM, uygulama SÜRÜYOR** — 8 bulgu (G2-01…G2-08). ✅ `G2-04` + `G2-02` + `G2-03` (**commit `ffbb995`**) · ✅ `G2-01` *(commit edilmedi)* · ⏳ `G2-05` **son kod işi** · `G2-07` ürün kararı · `G2-06` değişiklik önerilmedi · `G2-08` yalnız kayıt (`_v`/`CS0169` kısmı `G2-02` ile kapandı) |
> | **2b — Şablonlar** (`MaterialTemplates.razor` · `MaterialTemplatesView.axaml` + VM · `MaterialTemplateService` · `/api/material-templates`) | ✅ **ANALİZ + UYGULAMA TAMAM** — commit `305619d` + `ae11e02`. **B-3** şablon silinince `template_id` temizliği · **K2** web'de para birimi kaybı bitti · **B-4** masaüstünde uyumlu araç yönetimi + firma izolasyonu · **B-5** masaüstünde şablon fotoğrafı. **Kararlar: K1** web'e şablon seçici **geri eklenmedi** (2026-08-05 kararı korundu) · **K3** senkronizasyon **açılmadı**. Kalan borç: `B-6` virgüllü TEXT · `B-7` FK yokluğu · `B-9` sayfalama |
>
> **Grup 2a bulguları (özet):** `G2-01` ✅ web'de tam düzenleme formuna **giriş yolu yoktu** (muadil/
> uyumlu araç/fotoğraf web'den hiç değiştirilemiyordu) — hızlı düzenleme penceresine "Tam Düzenleme"
> düğmesi eklendi; ayrıca **yetki kapısı düzeltildi** (yeni kayıt=Create, düzenleme=Edit; eskiden ikisi
> de Create'ti) ve `/materials` ↔ `/materials/new` **aynı bileşen** olduğu için `forceLoad` gerekti ·
> `G2-02` web ana formu düzenleme kilidi göndermiyor ·
> `G2-03` ✅ `PUT /api/materials/{id}` `equivalentIds`'i yok sayıyordu — **yalnız `Program.cs` yetmedi**,
> `SetCompatibleVehicles`'ın simetriği olan **`MaterialService.SetEquivalents`** (tek transaction,
> `null`≠`[]`, çift yönlü) eklendi · `G2-02` ✅ web tam formunda düzenleme kilidi (ölü `_v` alanı
> amacına uygun kullanıldı, `CS0169` giderildi) · `G2-04` ✅ hızlı düzenleme şablon
> bağını siliyordu (iki platformda da) · `G2-05` masaüstünde "Yalnız kritik" filtresi yok ·
> `G2-06` kritik stok paneli çapraz eksik (P3, değişiklik önerilmedi) · `G2-07` düzenlemede boş "Tür"
> varsayılanı platformlar arası farklı (**ürün kararı gerekir**) · `G2-08` `Materials.razor`'da ölü kod
> (**yalnız kayıt** — `_v`, `DeleteSelected`, `DeletePhoto`, `OpenDetail`, `ApplyTemplate`; derleyici
> `CS0169` ile `_v`'yi zaten uyarıyor).
>
> **Silme derin denetimi (Grup 2a):** Malzeme silme koruması **gerçek ve tek noktalıdır** —
> `MaterialService.Delete` dışında `materials` tablosuna silme yazan kod **yoktur**; web HTTP'den,
> masaüstü doğrudan **aynı metoda** gider → UI butonu gizleme değil, **veri katmanında** koruma.
> Yetkili kullanıcının elle API çağırması korumayı atlatmaz. Yakıt tabloları (`fuel_depot_entries`,
> `fuel_distributions`) ve `daily_activities` **`material_id` taşımaz** → o taraf için kontrol
> gerekmiyor. ⚠️ **Önemli:** silme SOFT olduğu için **FK hiç devreye girmez**; tek güvence
> `GuardDeletable`'dır (bkz. §15 `MLZ-01-DEPO`).
>
> ### Kalan gruplar (henüz başlanmadı)
> 3 Bakım+Yakıt · 4 Talepler · 5 Araç/Muayene/Personel/Günlük ·
> 6 Yönetim ekranları *(Grup 6'da ayrıca: masaüstünde **Audit görüntüleme ekranı yok**, web'de var —
> bkz. §6 `LOG-02`)*

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
**Öncelik:** P2 · **Bağımlılık:** BRM-01, YTK-02, **GNL-03** · **Maliyet:** Orta
**DURUM:** `BEKLEMEDE`

**ID:** `GNL-03` ◄ 🆕 **2026-08-10'da EKLENDİ — `YTK-02` ve `GNL-02`'nin ÖNKOŞULU**
**Başlık:** Günlük Faaliyet — kayıt tipi kataloğu (yönetilebilir liste)
**Açıklama:** Bugün kayıt tipi **yönetilebilir bir varlık değil**: `daily_activities.activity_type`
serbest metin kolonudur ve şemada yalnız iki değer belgelenmiştir (`maintenance | movement`,
Migration009). Yeni bir tip (Arıza, Sevkiyat, Nakliye…) eklemek **kod değişikliği** gerektirir.
**Neden gerekli:** H-7. `YTK-02` "her kayıt tipine ayrı yetki" diyor, `GNL-02` "tipleri birime göre
filtrele" diyor — **ikisi de var olmayan bir listeye yetki/filtre bağlamaya çalışıyor.** Satır olmayan
bir tipe yetki verilemez. Ayrıca kullanıcının hedefi Günlük Faaliyet'in *"her iş için ilgili modüle
gitmeden günlük işleri hızlıca kaydeden merkezi faaliyet alanı"* olmasıdır → tip listesi büyüyecektir.
**Önerilen minimum çözüm:** Mevcut **lookup deseninin aynısı** (`units`/`brands`/`suppliers` gibi):
firma bazlı `activity_types` tanım tablosu + `daily_activities.activity_type_id`. Eski iki değer
tohumlanır, **eski metin kolonu KALIR** (geriye uyumluluk, sync bozulmaz). Yeni mekanizma tasarlanmaz.
**Öncelik:** P1 (YTK-02'den ÖNCE) · **Bağımlılık:** Yok · **Migration:** ✅ **additive** (1 tanım tablosu
+ 1 nullable kolon) · **API:** mevcut `/api/lookups/{table}` deseni · **Canlı veri riski:** Yok
· **Maliyet:** Düşük-orta
**⚠️ Sıra uyarısı:** `YTK-02` bugünkü hâliyle (`btn-daily-<kayittipi>`) sabit iki tip için çalışır;
tip listesi dinamikleşince buton anahtarı **tip id'sinden üretilmelidir**. `YTK-02` GNL-03'ten sonra
uygulanırsa ek iş çıkmaz.
**DURUM:** `BEKLEMEDE` — *analiz yapıldı, kod yazılmadı*

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
| **`LOG-02`** | **Audit içeriğinin zenginleştirilmesi (önceki/yeni değer)** — aşağıda | 🆕 2026-08-10. Şema HAZIR, yazanlar kullanmıyor → bugün "ne değişti" sorusu cevaplanamıyor | P2 |
| **`PRF-01`** | **Ölçek darboğaz haritası (yalnız ölçüm/analiz, kod yok)** — aşağıda | 🆕 2026-08-10. H-9 hedefi var ama **hangi noktanın darboğaz olduğu yazılı değil**; yatırım kararı ölçümsüz verilemez | P2 |
| `GNL-02` | Birim bazlı kayıt tipleri | BRM-01, YTK-02 **ve GNL-03**'e bağımlı *(2026-08-10: bu satır tablonun dışına düşmüştü, geri alındı)* | P2 |
| `RPR-01` | Rapor envanteri + standart denetimi | Analizde çıkarılamadı; P0'lardan sonra | P2 |
| `TST-01` | 33 atlanan testin neden atlandığının doğrulanması | Geliştirmeleri durdurmaz ama bilinmeli | P2 |
| `TMZ-01` | `ListColumns` çift kopya tekilleştirme | Gerçek teknik borç (biri güncellenip diğeri unutulursa ekran sessizce bozulur) ama acil değil | P2 |
| `TMZ-02` | **İki `BranchService` + ulaşılamayan `user_scopes`** — aşağıda | ⚠️ **2026-08-10: BAĞIMSIZ TEKNİK BORÇ DEĞİL.** `YET-01`'in içine alındı — bkz. aşağıdaki karar | P1 (YET-01 ile) |
| **`WEB-01`** | **Web hata mesajlarında ham JSON gösterimi** — aşağıda | 2026-08-10'da `KLT-01` kapanış QA'sinde bulundu. **`KLT-01`'in parçası değildir**; ayrı iş olarak açıldı, henüz fazlanmadı | P2 |

### 🔍 `WEB-01` — Web hata mesajlarında ham JSON gösterimi
*(2026-08-10, `KLT-01` kapanış QA'si — yalnız İNCELEME, kod değiştirilmedi)*

**Gerçek tarayıcı koşusunda kullanıcının gördüğü metin:**

```
Hata 409: {"error":"Bu kayıt siz düzenlemeye başladıktan sonra bir başkası tarafından değiştirildi. ..."}
```

**Sebep:** `src/DepoWise.Web/Services/ApiClient.cs` içindeki `PutAsync` ve `DeleteAsync`,
sunucunun `{"error":"..."}` gövdesini **ayrıştırmadan** kullanıcı mesajına yapıştırıyor
(`$"Hata {kod}: {gövde}"`). Aynı dosyadaki **`UploadImportAsync` aynı gövdeyi doğru ayrıştırıyor**
(`TryGetProperty("error")`) → doğru desen projede zaten var, **7 çağrı noktasında** uygulanmamış.

**Bu `KLT-01d` tarafından oluşturulmuş bir hata DEĞİLDİR.** Uygulama genelinde önceden var olan
bir **UX / hata gösterimi** problemidir; tüm ekranlardaki tüm hata kodlarını (400/403/409/500)
etkiler. Mesajın **içeriği doğru ve görünür** — yalnız teknik gövdeyle sarılı.

**Neden ayrı iş:** düzeltme `ApiClient`'ın ortak metotlarına dokunur → **bütün web ekranlarını**
etkiler; `KLT-01`'in dar kapsamına sığmaz.
**Kapsam:** 7 çağrı noktası + tek ortak ayrıştırma yardımcısı · **Migration:** yok ·
**Masaüstü:** etkilenmiyor (servisleri doğrudan çağırır, HTTP gövdesi görmez).
**Yanına iliştirilebilecek düşük öncelikli gözlem:** başarılı kayıttan sonra "Güncellendi."
mesajı `ClearForm()` tarafından hemen siliniyor → hiç görünmüyor (kayıt yine de yapılıyor).
Tek başına iş açılması **önerilmiyor**.

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

### 🔍 `LOG-02` — Audit içeriğinin zenginleştirilmesi (önceki değer / yeni değer)
*(2026-08-10 koddan doğrulandı — yalnız İNCELEME, kod değiştirilmedi)*

**Kullanıcı hedefi:** İleride şu sorular cevaplanabilmeli — *"Bu personel hangi işlemlerde uyarı aldı?
Hangi gün/saatte? Uyarıya rağmen devam etti mi? Hangi kayıt yüzünden uyarı çıktı?"* ve genel olarak
**kim / ne zaman / hangi ekran / hangi kayıt / önceki değer / yeni değer**.

**Koddan gerçek durum — şema HAZIR, yazanlar KULLANMIYOR:**

| Katman | Durum |
|---|---|
| `audit_logs` şeması (Migration001) | `user_id`, `entity_type`, `entity_id`, `action`, **`before_json`**, **`after_json`**, `correlation_id`, `created_at` → **gerekli kolonlar zaten var** |
| `AuditWriter` | `BeforeJson`/`AfterJson` parametrelerini **destekliyor** (satır 27-28) |
| Çağıranlar | Neredeyse tamamı **null geçiyor**. `AfterJson` dolduran yalnız `FileService` ve `MaintenanceService`; **`BeforeJson` dolduran hiçbir yer yok** |
| Görüntüleme | Web'de **`Audit.razor` VAR** · masaüstünde **YOK** (parite farkı — `PRT-01` Grup 6'da denetlenecek) |

**Sonuç:** Denetim altyapısı **baştan yazılmayacak** — yalnız mevcut çağrılara önceki/sonraki değer
eklenecek. Bu, "büyük log sistemi kurmak"tan çok daha ucuzdur ve kullanıcının istediği soruların
çoğunu karşılar. Uyarı/karar tarafı (`allowDuplicate`, "Yine de devam et") **`LOG-01`**'in konusudur;
ikisi birlikte planlanır.
**Öncelik:** P2 · **Bağımlılık:** Yok (LOG-01 ile birlikte yapılması önerilir) · **Migration:** ❌
(kolonlar mevcut) · **Maliyet:** Düşük-orta (çağrı noktası sayısına bağlı) · **DURUM:** `BEKLEMEDE`

### 🔍 `PRF-01` — Ölçek darboğaz haritası (yalnız ölçüm + belge, KOD YOK)
*(2026-08-10 — H-9'un ölçülebilir hâli)*

**Neden:** H-9 *"sunucu yoğunlukta çökmesin ama gereksiz altyapı kurulmasın"* diyor. Bugün **hangi
noktanın önce kırılacağı yazılı değil** → yatırım kararı (sunucu yükseltme, kuyruk, önbellek) ölçüm
olmadan verilemez. Bu iş **kod değiştirmez**; ölçer ve belgeler. **Maliyeti yoktur.**

**Bu oturumda kod okunurken görülen aday darboğazlar (doğrulanmadı, ölçülecek):**
- Sunucu **tek Fly makinesi / 256 MB** (bkz. [docs/MALIYET_KALEMLERI.md](docs/MALIYET_KALEMLERI.md) #1).
- `MaterialService.SearchGridAll` — dışa aktarım **tüm** sonuç kümesini 500'lük sayfalarla **belleğe**
  toplar; çok kayıtlı firmada bellek tepe noktası yapar.
- `MaterialService.GetDetail` — muadil grubu BFS'i **malzeme başına ayrı sorgu** açar.
- Fotoğraf uçları dosyayı **tamamen belleğe** okuyup döner (`Storage.Read`), akış (stream) kullanılmaz.
- İstek sınırlama (rate limit) yalnız **login** için var (`loginLimiter`); diğer uçlarda yok.

**Kapsam:** ölçüm + rapor (`docs/` altında mevcut bir rapor dosyası kullanılır, yeni dosya açılmaz).
**Öncelik:** P2 · **Bağımlılık:** Yok · **Migration:** ❌ · **Maliyet:** **Sıfır (ücretsiz)**
**DURUM:** `BEKLEMEDE` — *yatırım kararından ÖNCE yapılması önerilir (ölçümsüz para harcanmasın)*

---

## 7. YATIRIM SONRASI İŞLER

| ID | İş | Neden şimdi değil |
|---|---|---|
| `Y-1` | Kuyruk / background worker | Gerçek ihtiyaç yok; mevcut yük buna uzak (KARAR-5) |
| `Y-2` | Alan / Kolon Yönetimi ekranı | Büyük iş; `TMZ-01` önkoşulu. Mevcut kolon gizle/göster çoğu ihtiyacı karşılıyor |
| `Y-3` | Platform / Ekran görünürlüğü yönetimi | P3. **Uyarı:** eklenirse görünürlük yalnız menüyü etkilemeli, API'de hiçbir şey değişmemeli |
| `Y-4` | Gelişmiş izleme (monitoring/alerting) | Ücretli servis; mevcut "Canlı Sunucu" ekranı + Fly.io metrikleri yeterli |
| `Y-5` | Sürekli bağlantı (WebSocket/SignalR) | Analizde gereksiz olduğu gösterildi (KARAR-5) |
| **`Y-6`** | **Yük / dayanıklılık testi** (eşzamanlı kullanıcı simülasyonu) | 🆕 2026-08-10. `PRF-01` ölçümü **ücretsiz** yapılır; gerçek yük testi araç/ortam ister. Yatırım kararını *doğrulamak* için, *vermek* için değil |
| **`Y-7`** | **Audit/denetim kaydı uzun süreli saklama + arşiv** | 🆕 2026-08-10. `LOG-01`+`LOG-02` veriyi üretir; yıllarca saklama ve arşivleme **depolama maliyeti** doğurur. Kurumsal müşteri gelince gerekir |

### 💰 Yatırım / canlıya geçiş öncesi MALİYETLİ İŞLER — tek liste nerede?

**Ücretli kalemlerin tek ve güncel listesi: [docs/MALIYET_KALEMLERI.md](docs/MALIYET_KALEMLERI.md).**
Yukarıdaki `Y-1…Y-7` **iş** kalemleridir; o dosya **para** kalemleridir (ne işe yarar, öncelik,
yaklaşık maliyet, ücretsiz karşılığı). Kullanıcı *"yatırım buldum, canlıya almadan önce parayla neleri
yapmamız gerekiyordu?"* diye sorduğunda **önce o dosya, sonra bu bölüm** okunur.
⚠️ **Fiyat uydurulmaz** — kesin bilinmeyen maliyet "değişken/bilinmiyor" olarak yazılır.

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

SNK-01 ❌ İPTAL (koruma zaten vardı)
SNK-02 ✅ UYGULANDI (seçici kadans 2a)
   └─► SNK-03 ✅ UYGULANDI (sınıflandırmalı backoff)
SNK-04 ❌ ZATEN YAPILMIŞ (koruma b2604de ile mevcut)
   ► FAZ 1 senkron optimizasyonu (SNK-01…04) TAMAMLANDI

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
| 3 | ~~`MLZ-01`~~ | Malzeme silme koruması — ✅ **TAMAMLANDI** `b932f75` | 0 |
| 4 | ~~`KLT-01`~~ | Eksik düzenleme kilitleri — ✅ **TAMAMLANDI** (3 commit, 2 alt iş iptal) | 0 |
| 5 | ~~`SNK-01`~~ | Değişiklik yoksa push yapma — ❌ **İPTAL** (koruma zaten vardı) | 1 |
| 6 | ~~`SNK-02`~~ | Seçici senkron kadansı (2a) — ✅ **TAMAMLANDI** | 1 |
| 7 | ~~`SNK-03`~~ | Exponential backoff — ✅ **TAMAMLANDI** | 1 |
| 8 | ~~`SNK-04`~~ | Günlük yedeği ayır — ❌ **ZATEN YAPILMIŞ** (koruma mevcut) | 1 |
| 9 | **`PRT-01`** | **Tam parite denetimi** — 🔵 Grup 1 (stok) ✅ `8bf27cb`, kalan 5 grup | 1 |
| 10 | `PRT-02` | Ekran adı eşleme | 1 |
| 10b | **`YET-01`** | **Yetki modeli KARARI (TMZ-02 dahil)** ← FAZ 2'nin kapısı | 2 |
| 11 | `BRM-01` | Personel birimi | 2 |
| 12 | `YTK-01` | Approve/Cancel | 2 |
| 12b | **`GNL-03`** | **Kayıt tipi kataloğu** 🆕 ← `YTK-02`'nin ÖNKOŞULU | 2 |
| 13 | `YTK-02` | Kayıt tipi yetkisi *(GNL-03'ten sonra)* | 2 |
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
| 31b | **`LOG-02`** | **Audit içeriği: önceki/yeni değer** 🆕 *(LOG-01 ile birlikte)* | 6 |
| 31c | **`PRF-01`** | **Ölçek darboğaz haritası** 🆕 *(ölçüm+belge, KOD YOK, ücretsiz)* | 6 |
| 32 | `RPR-01` | Rapor envanteri | 6 |
| 33 | `TST-01` | 33 atlanan test | 6 |
| 34 | `TMZ-01` | ListColumns tekilleştirme | 6 |

**Toplam: 39 ana iş** (7'si yatırım sonrasına ertelenmiş `Y-1…Y-7` hariç).
*(2026-08-10 ikinci gözden geçirme: `GNL-03`, `LOG-02`, `PRF-01` eklendi → 36 → 39.)*
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

**AKTİF KOD İŞİ YOK.** `KLT-01` 2026-08-10'da kapandı; sıradaki iş **kullanıcı onayı** bekliyor.

### ✅ `KLT-01` — KAPANDI (2026-08-10)

| Alt iş | Durum |
|---|---|
| `KLT-01c` PermissionService.SaveForUser | ✅ TAMAMLANDI — `18a21f8` |
| `KLT-01a` RequestOperationsService | ✅ TAMAMLANDI — `ef905d6` |
| `KLT-01d` `MaterialTemplateService.Update` *(daraltıldı)* | ✅ TAMAMLANDI — `4f3524a` |
| `KLT-01e` Yakıt/stok regresyon testleri | ❌ **İPTAL** (2026-08-10) — gerekçesi çürütüldü, §15'e bakınız |
| `KLT-01b` LookupService.Rename | ❌ **İPTAL** (2026-08-10) — LWW mimari politika |
| Web + masaüstü 409 davranış kontrolü | ✅ TAMAMLANDI — gerçek HTTP + tarayıcı QA |

**Kapanış ölçümü:** Build **0 hata** · Test **1057 / 1024 başarılı / 0 başarısız / 33 atlanan**
(`MLZ-01` öncesi 1017'den toplam **+40 test**, sıfır kırılma). Üç alt işte de **migration yok**.

**409 doğrulaması kod okumasıyla yetinmedi:** temiz QA veritabanıyla yerel API + web açıldı,
tarayıcıdan gerçek çakışma üretildi. Stale kayıtta **HTTP 409**, çakışan veri **ezilmedi**,
web formundaki kullanıcı girdisi **korundu**, güncel sürümle **tekrar kaydetme başarılı**,
sürüm göndermeyen eski istemci **etkilenmedi**. Canlı veriye dokunulmadı.
Masaüstü servis yolu + 11 concurrency testiyle doğrulandı; **canlı Avalonia arayüz koşusu
yapılmadı** (araç sınırı + kullanıcının gerçek yerel veritabanına yazma riski).

**Yan bulgu:** `WEB-01` (§6) — `KLT-01`'in parçası **değil**, ayrı iş olarak kaydedildi.

> **Güncel ilerleme kaydı:** [docs/PROJE_DURUMU_VE_ILERLEME.md](docs/PROJE_DURUMU_VE_ILERLEME.md)

### ⏭️ Sıradaki iş — ÖNERİ (onay bekliyor)

**✅ FAZ 1 — senkron optimizasyonu (SNK-01…04) TAMAMLANDI (2026-08-10).**

**`PRT-01` — Tam ekran parite denetimi (FAZ 1).** FAZ 1'in kalan işi: 43 web + 38 masaüstü
ekranın alan/işlev/validasyon/yetki düzeyinde karşılaştırılması (genel analizde yalnız **ad
düzeyinde** yapılabilmişti).
**Başlamadan önce:** kullanıcı onayı + `PRT-01` detay analizi (kapsam koddan çıkarılmalı —
plan kapsamı `KLT-01`'de üç, `SNK-01` ve `SNK-04`'te birer kez yanlış çıktı).

**`SNK-04` ❌ ZATEN YAPILMIŞ / İPTAL (2026-08-10)** — saatlik koruma `b2604de` (2026-07-11) ile
metodun oluşturulduğu commit'ten beri mevcut; §5'e bakınız. Kod değişikliği yapılmadı.

**`SNK-03` ✅ TAMAMLANDI (2026-08-10)** — sınıflandırmalı backoff (B2); §5'e bakınız.
⚠️ Açık doğrulama: çalışma zamanı/HTTP davranışı GUI/QA ortamı sınırı nedeniyle gözlenmedi.

**`SNK-02` ✅ TAMAMLANDI (2026-08-10)** — seçici kadans (2a); §5'e bakınız.
⚠️ Açık doğrulama: gerçek HTTP kadans ölçümü GUI oturumu sınırı nedeniyle yapılamadı.

**`SNK-01` ❌ İPTAL (2026-08-10)** — koruma kodda zaten vardı (`c8d3dc7`); §5'e bakınız.

**FAZ 0'dan kalanlar kullanıcı aksiyonudur:** `GUV-01` ⚠️ acil · `DOG-01`.

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
| KARAR-6 | Depo = ayrı `warehouses` tablosu; `branches` genişletilmez *(ayrıntı §4'te; 2026-08-10'da bu tabloya da işlendi)* | ✅ VERİLDİ |
| **KARAR-7** | **Malzeme SİLME şube bazlı mı olsun?** — aşağıda | ⏳ **BEKLİYOR — kullanıcı kararı** |

### ⚖️ KARAR-7 — Malzeme silme şube bazlı mı olmalı? *(2026-08-10, kullanıcı isteğiyle açıldı)*

**Kullanıcının senaryosu:** *"Şube A'da bir malzeme oluşturdum, aynı malzeme Şube B'de de oluşturuldu.
İkisinde de stok 0. Şube B'deki kullanıcı sildi → Şube A'daki malzeme SİLİNMEMELİ."*

**⚠️ Bu istek `KARAR-1` ile ÇELİŞİYOR** — `KARAR-1` (✅ verilmiş, kullanıcı kararı 2026-07-26):
*"Malzeme kataloğu firma genelinde kalır, stok şube bazlı olur."*

**Koddan gerçek durum (doğrulandı, 2026-08-10):**

| Nokta | Bulgu |
|---|---|
| `materials.branch_id` | Kolon **VAR**; `Create` sırasında oturumun çalışma şubesi yazılıyor (`BranchScope.Active`, "Tüm Şubeler" → NULL) |
| `MaterialService.List` / `SearchGrid` | `branch_id`'yi **bilerek SÜZMÜYOR** — kod yorumu: *"Malzeme listesi FİRMA-GENELİdir (ortak katalog); şube ayrımı STOK'tadır"* |
| `MaterialService.Delete` | Firma bazlı: `WHERE id=@id AND company_id=@c` → **şube bakılmıyor** |
| `GuardDeletable` | Kontroller **bilinçli olarak firma geneli** (kod yorumunda yazılı) |

Yani bugün **aynı malzemenin iki şubede iki ayrı kaydı normalde OLUŞMAZ** — katalog ortaktır, iki şube
aynı kartı paylaşır. Kullanıcının senaryosu ancak **iki ayrı kart** (farklı kod) açılırsa oluşur; o
durumda B'nin sildiği kart zaten A'nınkinden farklı bir kayıttır ve **A etkilenmez**.

**Karara bağlanacak soru:** Katalog gerçekten ortak mı kalsın (KARAR-1), yoksa malzeme kartı da şube
bazlı mı olsun? İkincisi seçilirse **aynı malzemenin firmada birden çok kartı** oluşur; bu, raporlamayı,
muadil ilişkisini, talep/bakım eşleşmesini ve stok toplamlarını **kökten etkiler**.

**Yapılmayacaklar (karar gelene kadar):** migration yazılmayacak · `List`/`SearchGrid`/`Delete`
şube süzmesi eklenmeyecek · büyük refactor yapılmayacak.
**İlişki:** `MLZ-01-DEPO` (§15) ve `STK-05` — depo mimarisi geldiğinde bu soru **zaten** yeniden
gündeme gelecek. **Öneri: KARAR-7, `STK-01` başlamadan ÖNCE FAZ 4'ün kapısında karara bağlansın**
(sonradan verilirse depo şemasının üstüne ikinci bir dönüşüm biner).

---

## 15. AÇIK SORUNLAR

| # | Sorun | Etki | Ne zaman çözülecek |
|---|---|---|---|
| 1 | Süper admin parolası zayıf ve canlıda çalışıyor | **Yüksek** — her firmaya erişim | `GUV-01` — **acil** |
| 2 | Stok firma geneli — çok şubeli çalışılamıyor | **Yüksek** | FAZ 4 |
| 3 | ~~Malzeme silmede koruma yok~~ | ~~Yüksek~~ | ✅ **`MLZ-01` ile kapatıldı (2026-08-10)** |
| 3b | İki `BranchService` + `user_scopes` yazanı yok | Orta | `TMZ-02` — §6'da |
| 4 | ~~Yakıt/stok belgeleri/muayenede LWW koruması yok~~ | ~~Yüksek~~ | ❌ **VARSAYIM YANLIŞTI** (2026-08-10): bu kayıtların düzenleme yolu hiç yok; iptal/ters kayıt korumaları **var ve test edilmiş**. Gerçek açıklar `KLT-01c` ✅ ve `KLT-01a` ✅ ile kapatıldı; `KLT-01e` iptal edildi |
| 5 | Bakımda negatif stok ↔ onay çelişkisi | Orta | KARAR-4 |
| 6 | `ListColumns` iki kopya — biri unutulursa ekran sessizce bozulur | Orta | `TMZ-01` |
| 7 | 33 test neden atlanıyor bilinmiyor | Orta | `TST-01` |
| 8 | Tam ekran paritesi denetlenmedi | Orta | `PRT-01` |
| 9 | Masaüstü vektör ikonları görsel doğrulama bekliyor | Düşük | Kullanıcı bakacak |
| 10 | **`MLZ-01-DEPO`** — aşağıya bakınız | **Orta** (gelecekte yüksek) | `STK-05` |
| 11 | **Web hata mesajlarında ham JSON** (`{"error":...}` kullanıcıya görünüyor) | Düşük-Orta (kozmetik; mesaj doğru ama teknik görünüyor) | **`WEB-01`** — §6'da |
| 12 | `KLT-01d` masaüstü tarafının **canlı Avalonia arayüz koşusu yapılmadı** (servis yolu + 11 test ile doğrulandı) | Düşük | İstenirse izole QA veritabanıyla ayrı koşu |

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

**Son ölçüm (2026-08-10, `KLT-01` kapanışı, `feature/mlz-01-malzeme-silme-korumasi`):**
- Build: **0 hata**
- Test: **1057 toplam — 1024 geçti, 0 başarısız, 33 atlandı**

| Aşama | Toplam | Geçti | Yeni test |
|---|---|---|---|
| 2026-08-09 `master` (başlangıç) | 1017 | 984 | — |
| `MLZ-01` (`b932f75`) | 1025 | 992 | +8 |
| `KLT-01c` (`18a21f8`) | 1033 | 1000 | +8 |
| `KLT-01a` (`ef905d6`) | 1046 | 1013 | +13 |
| `KLT-01d` (`4f3524a`) | **1057** | **1024** | +11 |

**Toplam +40 test, sıfır kırılma.** Atlanan 33 test her ölçümde aynı (`Postgres*`, ortam eksikliği).

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

**`SNK-04` — Günlük yedeği senkron turundan ayırma (FAZ 1).** ⚠️ **ÖNERİ — onay alınmadan başlanmaz.**

FAZ 0'ın kod tarafı bitti (`MLZ-01` ✅, `KLT-01` ✅). **FAZ 1'in senkron optimizasyonu bölümü
(SNK-01…04) TAMAMLANDI:** `SNK-01` ❌ İPTAL (koruma zaten mevcuttu) · `SNK-02` ✅ (seçici kadans 2a) ·
`SNK-03` ✅ (sınıflandırmalı backoff) · `SNK-04` ❌ ZATEN YAPILMIŞ (koruma zaten mevcuttu) →
FAZ 1'in kalan işi **`PRT-01`**'dir; `YET-01` kararını beklemez.

**`SNK-02` ve `SNK-03`'ten devreden açık doğrulama:** çalışma zamanı/HTTP davranışı
GUI/etkileşimli oturum sınırı nedeniyle gözlenmedi. Ayrı bir tur olarak (kullanıcının kendi
oturumunda) tamamlanabilir.

> Kalıcı analiz kuralı: bkz. [docs/PROJE_DURUMU_VE_ILERLEME.md](docs/PROJE_DURUMU_VE_ILERLEME.md) §12.5
> — `version++` + `expectedVersion` yokluğu TEK BAŞINA concurrency açığı demek DEĞİLDİR.
> Aynı disiplin `PRT-01`'de de uygulanır: **plandaki kapsam varsayımları koddan yeniden doğrulanır.**
> Plan kapsamı `KLT-01`'de üç kez yanlış çıktı; `SNK-02`'de **kullanıcı kararıyla daraltıldı**
> (ADR-099 ile çelişiyordu); **`SNK-01` ve `SNK-04`'te ise madde ZATEN YAPILMIŞ çıktı.**
> Özel ders: **bir madde "zaten yapılmış" olabilir** — çağrı akışını uçtan uca izle ve
> `git log -S` ile kodun geçmişine bak (§13). Bu iki kez tekrarlandı.

Kullanıcı **"sıradaki iş"** dediğinde:
1. Önce [docs/PROJE_DURUMU_VE_ILERLEME.md](docs/PROJE_DURUMU_VE_ILERLEME.md) okunur, sonra `git status`/`git log`
   ile gerçek durum karşılaştırılır (fark varsa **gerçek durum esastır**).
2. Seçilen iş için **detay analiz promptu** beklenir — genel analiz tekrarlanmaz.
3. Analiz raporlanır, onay alınırsa geliştirmeye geçilir.
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
