# DEVAM — Nerede Kaldım? (Sıfır PC İçin Giriş Dosyası)

> **Bu dosya, hangi bilgisayarda olursam olayım açtığımda ilk okuduğum yerdir.**
> Amaç: format atsam, PC değiştirsem, aylar sonra dönsem bile "ne yaptık, sırada ne var"
> sorusunu tek bakışta cevaplamak. Teknik bilgi gerektirmez.
>
> **İki PC nasıl aynı kalır?** Her şey GitHub'da (`github.com/osmanalpaslan/DepoWise`).
> - **Başlarken:** Claude otomatik `git pull` yapar → en güncel hâli alır → bu dosyayı okur.
> - **Bitirirken:** Claude bu dosyayı günceller → `git commit` + `git push` yapar → diğer PC bir sonraki `git pull`'da aynısını görür.
> - Kural `CLAUDE.md` §0'da yazılı; her oturumda otomatik uygulanır. Sen bir şey ezberlemek zorunda değilsin.

---

## 1. Bu proje nedir? (tek paragraf)

**DepoWise** — çok firmalı (multi-tenant) depo/stok/araç/bakım/yakıt yönetim sistemi.
Üç parça, tek beyin: **Masaüstü** (Windows/.NET 8 + Avalonia, yerel SQLite) + **Web** (Blazor Server/.NET,
MudBlazor, tarayıcı) + **API** (sunucu, Fly.io, SQLite). İş kuralları ve yetkiler API'de tek yerde. Detaylı
çalışma mantığı: [docs/PROJE_REHBERI.md](docs/PROJE_REHBERI.md) (ortak defterimiz).
> Not: `apps/web` (eski Next.js denemesi) 2026-06-27'den beri donmuş; aktif web `src/DepoWise.Web`'dir (ADR-057).

---

## 2. ŞU AN NEREDEYIM? (son güncelleme: 2026-07-18)

### 🟢 Tek bakışta güncel durum

| Ne | Durum |
|---|---|
| **Testler** | **523/523 yeşil** (`dotnet test`) |
| **Şema** | Migration **049** (sayfa boyutu + kolon genişliği tercihi — ADR-089) |
| **API (sunucu)** | `depowise-erp.fly.dev` — **canlı**, health 200 |
| **Web** | `depowise-web.fly.dev` — **canlı**, login 200 |
| **Masaüstü** | **1.0.67 yayında** (Tür düzeltme + tanım düzenleme + sayfa boyutu 25) |
| **Git** | temiz + `origin/master` ile senkron |
| **Bekleyen iş** | **ADR-089 masaüstü liste UI** (#2 sayfalama konumu / #5 sıralama / #3 Excel-grid) + baba dosyasında TL→TRY |

### 7 maddelik liste geliştirmeleri paketi (2026-07-18, ADR-089)
Kullanıcı 2600+ kayıtla çalışırken 7 istek verdi. **Web + backend TAMAM ve canlıda; masaüstü UI sürüyor.**
1. Sayfa boyutu varsayılan **25** (kişiye özel hatırlanır). 2. Sayfa numaraları + kayıt bilgisi tablonun
**üstünde-solunda**. 3. **Excel-benzeri grid**: pencere küçülünce taşma/kayma yok (yatay kaydırma) +
sürüklenebilir kolon genişliği (kişiye özel kalıcı). 4. **Tanım düzenleme** (rename artık definitions/Edit
yetkisiyle, süper-admin kısıtı kalktı; masaüstünde satır-içi düzenleme). 5. **Başlığa tıklayınca sıralama**
(metin A→Z/Z→A Türkçe; sayısal küçük→büyük). 6. Yeni tanım/rename **50 karakter** sınırı. 7. İçe aktarımda
**"Tür" harf duyarsız kanonik eşleme** ("YEDEK PARÇA"→"Yedek Parça") + Migration048 mevcut veriyi düzeltir.
Detay: `docs/DECISIONS.md` ADR-089. Test: 523/523. **Masaüstü:** #1 (sayfa boyutu 25+hatırlama), #4 (tanım
düzenleme), #6 (50 kar), #7 (Tür) 1.0.67'de canlı. **Masaüstü liste UI #2/#5/#3 (sayfalama konumu, sıralama,
Excel-grid) sürüyor** — Avalonia bu ortamda görsel test edilemediğinden dikkatli/build-doğrulamalı yapılacak.

### Sayısal kolon filtresi: tam-sayı/karşılaştırma/aralık (2026-07-18, ADR-088)
Kullanıcı ADR-087'nin filtresini denerken: "stokta sadece 5 olanları listelemek istiyorum ama bütün içinde 5
olan malzemeler listeleniyor" — sayısal kolonda "içerir" araması 15/25/50'yi de yakalıyordu. **Çözüm:**
Malzemede Birim Fiyat/Min Stok/Stok, Araçta Üretim Yılı/Sayaç artık **sayısal** filtre — `5` artık TAM eşleşir
(içermez), `>5`/`<5`/`>=5`/`<=5` karşılaştırma, `5-10` aralık (negatif sınır destekli, bkz. ADR-086 negatif
stok). Tanınmayan söz dizimi eski "içerir" davranışına düşer (filtre kutusu asla sessizce boş kalmaz). Metin
kolonları (Kod/Ad/Marka…) DEĞİŞMEDİ. UI'da ipucu eklendi. Detay: `docs/DECISIONS.md` ADR-088. Test: 11 yeni
(509/509). **Canlıya alındı:** API+Web deploy, masaüstü **1.0.66** yayınlandı. Tarayıcı üzerinden görsel
doğrulama YAPILAMADI (giriş formuna kimlik bilgisi otomasyonu güvenlik politikasınca engellendi) — güvence
tamamen birim testlerinden (SearchGrid'e karşı gerçek SQL).

### Malzeme/Araç Listesi — kolon bazlı filtre + sayfalama + kişisel kolon seçimi (2026-07-17, ADR-087)
Kullanıcı 2600+ satırlık dosyayı içeri aldıktan sonra: "malzemeler ve araç listesinde filtre yapısı olması
gerek (içerir + başlangıca göre arama) + sayfa boyutu seçimi + 1,2,3… sayfalama." Netleştirme sorusunda
kullanıcı ekledi: sütun bazlı ayrı filtreler + sağ tık "Kolon Ayarla" ile hangi form alanının (fotoğraf
hariç) listede görüneceğini seçebilme, **her kullanıcıya özel** (farklı kullanıcıda görünmesin).

**Gizli kusur ortaya çıktı:** liste ekranları da (import/export'tan bağımsız) 200 satır varsayılanına
dayanıyordu — 2600+ kayıtlı firmada liste sessizce yalnız ilk 200'ü gösteriyordu. Yeni `SearchGrid` uçları
gerçek `COUNT(*)`+`LIMIT/OFFSET` kullanır; eski hızlı-arama uçları (Stok/Talep/Bakım seçicileri) dokunulmadı.

**Kolon kataloğu tek kaynak** (`MaterialListColumns`/`VehicleListColumns`) = yeni kayıt formundaki HER alan,
fotoğraf hariç ("Açılış Stok" ve "Şablon" da kasıtlı olarak yok — kalıcı kart alanı değiller). Kolon tercihi
KİŞİSEL (Migration 047, `user_list_preferences`, anahtar user_id+list_key — firma değil). Web + masaüstü
ikisinde de: filtre kutuları, sayfa boyutu seçici + numaralı sayfalama, sağ-tık/⚙ "Kolonları Ayarla".
Detay: `docs/DECISIONS.md` ADR-087. Test: 24 yeni (497/497).
**⚠️ Masaüstü UI görsel doğrulanamadı** (ortamda Avalonia çalıştırıp tıklama testi yapacak araç yok) —
temiz derleme + backend testleriyle güvence alındı. Web gerçek tarayıcıda uçtan uca doğrulandı.
**Canlıya alındı:** API+Web deploy, masaüstü **1.0.65** yayınlandı (sunucuda "en güncel" doğrulandı).

### Açılış stoğu NEGATİF olabilir (2026-07-17, ADR-086)
Babanın malzeme dosyasında (2507 satır) 63 satırda **Açılış Stok negatif**; içe aktarım reddediyordu.
Kullanıcı: "eksi stok kontrolünü kaldıralım; sistemi devralan firmalar mevcut stoklarını girebilsin."
→ **Yalnız BAŞLANGIÇ stoğu** girişinde negatif serbest bırakıldı (içe aktarım + web/masaüstü malzeme formu
+ API). **Operasyonel ÇIKIŞ'ın negatif-bakiye engeli AYNEN korunur** (bir çıkış bakiyeyi eksiye düşüremez —
§4'ün asıl kuralı). Fiyat/Min Stok yine negatif olamaz. Ledger temiz kalır: negatif açılış `stock_movements`'a
**pozitif miktar + direction=−1** yazılır (senkron kalkanı + `RecomputeBalances` doğru kalsın); yalnız türetilmiş
**bakiye** eksi olabilir. Detay: `docs/DECISIONS.md` ADR-086. Test: 6 yeni (473/473).
**⚠️ Kalan (babanın dosyası):** her satırda para birimi "TL" yazılı — sistem TRY/USD/EUR bekler. Bu ayrı bir
engel; Excel'de TL→TRY yapılmalı (istenirse TL→TRY otomatik eşlemesi eklenir). **Canlıya alındı:** API+Web
deploy, masaüstü **1.0.64** yayınlandı.

### Makine "tanım sıfırlama" (2026-07-17, ADR-085)
Kullanıcı: babasının makinesi (DESKTOP-SIKIB3U, süper admin makinesi) önce test firmasıyla giriş yapmıştı,
sonra asıl firmayla giremedi sandı → "makine tanımını sıfırlayan bir buton + login sonrası otomatik
algılama" istedi. **Yeni:** Makine Yönetimi ekranında (yalnız süper admin) **"Tanımı Sıfırla"** butonu —
o makine adına ait TÜM firmalardaki kayıtları siler (iş verisi ETKİLENMEZ, özel kod GEREKMEZ). Masaüstü
bir sonraki girişte (eşitleme adımında, purge/yerel-sıfırlama kontrollerinden ÖNCE) bunu görür → yerel
makine-firma/şube önbelleğini temizler → **girişi iptal eder, login ekranına döner**. Sonraki giriş yapan
kullanıcı makineyi kendi firması/şubesiyle yeniden tanımlar (mevcut "ilk kurulum" akışı). ADR-084'ten
(firma yerel sıfırlama) FARKI: o girişe izin verip devam eder, bu **durdurur** (makinenin hangi firmaya
ait olduğu artık belirsiz). Şema: Migration 046 (`machine_resets`, ADR-084 ile aynı iki-anlamlı desen ama
FİRMA yerine MAKİNE ADIYLA anahtarlı). Test: 8 yeni (`MachineResetTests`). Detay: `docs/DECISIONS.md` ADR-085. **Canlıya alındı:** API+Web deploy edildi, masaüstü **1.0.63**
yayınlandı (sunucuda "en güncel" doğrulandı). Gerçek makinede (DESKTOP-SIKIB3U) henüz test edilmedi.

### Personel içe aktarımı + "Saha Personeli" / "Kullanıcı Adı" sütunları (2026-07-16)
Kullanıcı sordu: "toplu personel listesini içeri almak istiyorum; saha personeli veya kullanıcı ise
sütunda nasıl belirtmem gerek?" → **Personel** içe/dışa aktarımı eklendi (7 sütun, formla birebir):
`Ad Soyad* · Unvan · Telefon · Şube · Aktif · Saha Personeli · Kullanıcı Adı`

**İki kavramın Excel karşılığı (BİRBİRİNİ DIŞLAR):**
- **Saha Personeli = Evet** → kişi uygulamaya HİÇ girmez (şoför/operatör). "Kullanıcı bağlanmadı" uyarısı çıkmaz.
- **Kullanıcı Adı** → kişi uygulamaya girer; **MEVCUT** hesap bağlanır. ⚠️ İçe aktarım **hesap AÇMAZ**
  (hesap açmak şifre+rol+yetki ister → Kullanıcılar ekranından yapılır). Bir personele TEK hesap.
- İkisi birden dolu → **çelişki, satır reddedilir** (ekranda da öyle: kutucuk işaretlenince kullanıcı bağı silinir).
- Evet/Hayır yazımı esnek: Evet/E/Var/X/1/true — Hayır/H/Yok/0/false. Tanınmayan değer **reddedilir**
  (sessizce "hayır" sayılmaz). Aktif boş = Evet, Saha Personeli boş = Hayır.

**Mükerrer:** personelin benzersiz kodu YOK → anahtar **normalize ad** (boşluksuz+küçük harf, mevcut
"mükerrer kişi" mantığıyla aynı). Aynı dosya iki kez → tekrarlanmaz. Bedeli: gerçekten aynı isimli iki
farklı kişi varsa ikincisi atlanır (rapor edilir). Unvan/şube yoksa otomatik oluşur (unvan Türkçe duyarlı:
"Şoför"="şoför" tek tanım).

**🔴 BULUNAN KUSUR (yine 200 sınırı):** Personel ve Malzeme **DIŞA aktarımı** `PageRequest{Limit=5000}`
kullanıyordu ama `MaxLimit=200` → **2600 personeli olan firma "dışa aktar" deyince sessizce yalnız 200
satır alıyordu.** Düzeltildi: `AllPages` yardımcısı keyset imleciyle tüm sayfaları dolaşıyor.
`PersonnelService.AllNameToId` (sayfalamasız) mükerrer kontrolü için eklendi. Test: 34 yeni (hacim 3000 dahil).

### ⚠️ İçe aktarma şablonları TAM ALAN + "Arızalı" durumu + 200 SATIR SINIRI KUSURU (2026-07-16)
**🔴 BULUNAN KUSUR (3000 satırlık hacim testi ortaya çıkardı — kullanıcının dosyası ~2600):**
`VehicleService.List` varsayılanı **200**, `PageRequest.MaxLimit` de **200**. İçe aktarıcılar bunlara
dayanıyordu → 200'den fazla aracı/malzemesi olan firmada: **bakım/muayene/yakıt aktarımı 201. araçtan
sonrasını "Araç bulunamadı" diye REDDEDİYOR**, araç/malzeme aktarımı mükerrer kontrolünü kaçırıp
**KOPYA oluşturuyordu**. Dün yayınlanan yakıt import'unda da vardı. Düzeltildi: import'lar
`List(s, null, int.MaxValue)` + yeni `MaterialService.AllCodeToId` (sayfalamasız) kullanıyor. 3 regresyon testi.

**Şablonlar artık YENİ KAYIT FORMUYLA BİREBİR** (fotoğraf hariç — kullanıcı kuralı):
Araç 4→**15** sütun · Malzeme 6→**15** · Bakım +Alt Bakım/Teknisyen · Muayene +Erteleme Tarihi/Açıklama.
Tanım alanları (marka/kategori/tip/model/şube/sürücü/birim/tedarikçi) **isimle yazılır, yoksa OTOMATİK
oluşur** (`ImportLookupResolver` — **önbellekli**: 3000 satırda satır başına DB sorgusu YOK). Aktarım sonrası
**"oluşturulan yeni tanımlar" raporu** verilir (yazım hatası "Caterpiller" ayrı marka olur → görülebilsin).
Araç artık **iç kod VEYA plaka** ile eşlenir (bakım/muayene/yakıt/uyumlu araçlar dahil).

**"Arızalı" durumu eklendi** (Aktif/Pasif/Bakımda/**Arızalı**) — ortak kaynak `VehicleStatus`
(Application + Web aynası); eskiden liste 5 yerde elle tekrarlıydı. **Yan kusur düzeltildi:** servis durum
notunu yalnız "maintenance"da saklıyordu → **Arızalı notu sessizce kayboluyordu**. Masaüstü durum kutusu
artık Türkçe gösteriyor (eskiden ham "active"/"passive" yazıyordu).
**Bakım ekranına "Araç Durumu"** eklendi (web+masaüstü): bakım kaydı açarken aracı Arızalı işaretleyebilirsin;
boş bırakılırsa araç durumu değişmez. Yeni uç: `POST /api/vehicles/{id}/status` (PUT tüm alanları ezerdi).

### ⚠️ Yakıt içe aktarımı + İMPORT'TA 10 KAT BOZULMA KUSURU DÜZELTİLDİ (2026-07-16)
**Bulunan KUSUR (kanıtlandı):** Malzeme içe aktarımı `Money.Parse` kullanıyordu; o InvariantCulture ile
çalışır ve **virgülü BİNLİK AYIRICI** sayar → Türk Excel'inin `"12,5"` değeri **sessizce 125** oluyordu
(fiyat/min-stok 10 kat şişiyordu, hata da vermiyordu). Düzeltildi: import kendi `ParseDecimal`'ını kullanıyor
(virgül→nokta). `Money.Parse` DEĞİŞTİRİLMEDİ — o veritabanı okuması için doğru (orada hep nokta saklanır).
**İkinci düzeltme:** Excel başlıkları artık büyük/küçük harf duyarsız ("litre" = "Litre") — elde tutulan
dosyalarda başlık farkı satırı sessizce reddediyordu.

**Yeni: Yakıt içe/dışa aktarımı** (İmport/Export ekranı, masaüstü). İki tür: **Yakıt Dağıtım** (araca yakıt
verme) + **Yakıt Depo Girişi** (satın alma). Gerçek dünya uyumu: yalnız **Araç + Litre zorunlu**; sayaç boş →
aracın mevcut sayacı (sayaç bozulmaz), fiyat boş → güncel depo fiyatı, personel/tarih boş → geçilir.
Araç **iç kod VEYA plaka** ile eşlenir (boşluk/harf duyarsız). Depo yetersizse **DryRun önceden uyarır**
(kaç litre eksik olduğunu söyler). Satırlar **tarihe göre** işlenir (sayaç zinciri doğru kurulsun).
**Aynı dosya iki kez aktarılırsa kayıt tekrarlanmaz** (deterministik operation_id). Test: 23 yeni.

### Firma "yerel sıfırlama" isteği (2026-07-16, ADR-084)
Sevgi A.Ş. bilgileri/adı web'den güncellendi; 2 yerel makine daha önce bu firmayla giriş yapmıştı.
**Teşhis:** firma ADI her çevrimiçi girişte zaten otomatik düzeliyordu; ama DİĞER alanlar (vergi/adres/
kota) hiç aynalanmıyordu → bu oturumda düzeltildi (`CompanySyncService.MirrorLocalAsync` artık TÜM alanları
aynalıyor). **Yeni özellik:** Firma Tanım listesinde "Yerel Sıfırlama İste" (turuncu ikon, süper-admin-only) —
firma sunucuda durur/erişim engellenmez, yalnız o firmanın makineleri bir sonraki çevrimiçi girişte yerel
kopyalarını BİR KEZ temizler ve sıfırdan yeniden doldurur. Makine o an kapalıysa istek sunucuda bekler,
makine aktif olunca (bugün/yarın fark etmez) algılanır. ADR-083'ten (kalıcı silme) farkı: YIKICI değil,
özel kod gerekmez, kendi firman için de kullanılabilir. Şema: Migration 045. Test: 7 yeni.

### Kullanıcı firması değiştirilemez — doğrulandı (2026-07-16)
Kullanıcı sordu: "kullanıcı oluşmuş ise süper admin dahil hiç kimse firmasını değiştirememeli — yapı böyle mi?"
Kod incelemesi: `users.company_id`'yi güncelleyen HİÇBİR UPDATE yok (7 UPDATE'te company_id yalnız WHERE
filtresinde), firma değiştiren API ucu yok, masaüstü senkronu `users` tablosuna hiç dokunmuyor. Tek istisna
(`AuthService.ImportRemoteUser`) firma DEĞİŞTİRMEZ — sunucudaki gerçeği yerele yansıtır. **Yapı doğru.**
6 yeni test (`UserCompanyImmutableTests`) bunu davranışsal olarak kilitler: şube atama/rol/aktif-pasif/
şifre/tüm-şubeler hiçbiri firmayı etkilemiyor + `UserService`'te "firma değiştir" imzalı metod yok.

### ⚠️ Kalıcı Silme ekranı (2026-07-16, ADR-083) — GERİ ALINAMAZ
**Ne işe yarar:** Firma Tanım firmayı *pasife alır*; bu yeni ekran firmayı ve TÜM verisini (kullanıcılar,
şubeler, malzeme, araç, stok, fotoğraflar, sunucu yedekleri) **kalıcı siler**. Temiz test ortamı içindir.

**Nasıl açılır:** Yönetim menüsü → **Kalıcı Silme** (yalnız web, yalnız süper admin). Ekran **özel kod** ile
açılır. Özel kod, süper adminin **ilk web girişinde** oluşturduğu, şifresinden AYRI bir sırdır; unutulursa
şifreyle yenisi belirlenir.

**Silme için gereken:** özel kod + şifre + firma adını birebir yazma. **Kendi firmanı silemezsin** (ADR-064/068
dersi: kilitlenirsin). Silinince geriye yalnız **künye** kalır; o firmanın makineleri bir sonraki girişte
eşitleme adımında künyeyi görüp **yerel veriyi siler ve login'e döner** → o firmayla artık girilemez.
Çevrimdışı makinede hiçbir şey silinmez (sunucu "silindi" demedikçe dokunulmaz).

**Masaüstünde:** yeni ekran YOK, login'de özel kod alanı YOK (kullanıcı kararı) — yalnız algılama var.

### Firma/şube karışmasını önleme — 3 faz (2026-07-16)
**Faz 1 — Şube ekranı:** firma kutusu "birden çok firma varsa" koşuluna bağlıydı + firma listesi hatası
sessizce yutuluyordu → süper adminde kutu HİÇ çıkmıyordu. Artık daima görünür, hata gösterilir ve
varsayılan **kendi firman** (alfabetik ilk firma değil). Masaüstü şube ekranına firma seçici eklendi (yoktu).

**Faz 2 — Aktif Firma (ADR: ekran-başı firma kutusu REDDEDİLDİ):** süper admin üst bardan firmayı değiştirir
(`/api/auth/select-company` → yeni jeton); tüm ekranlar o firmada çalışır, şube bağlamı sıfırlanır.
Gerekçe: CLAUDE.md §4 "firma kimliği yalnız güvenilir oturumdan gelir" — her ekrana firma kutusu koymak
bu kuralı deler ve riski 30 ekrana yayardı. Masaüstünde firma GİRİŞTE seçilir (yerel veri ona göre eşitlenir);
üst barda **aktif firma + çalışma şubesi rozeti** eklendi.

**Faz 3 — "Tüm Şubeler" koruması:** bu modda çalışma şubesi yoktur → stok hareketi şubesiz (`branch_id NULL`)
düşüyordu. Artık şube bazlı 7 ekranda (Malzemeler, Araçlar, Stok Giriş-Çıkış, Stok Sayım, Yakıt ×2, Bakım,
Muayene) **yazma engellenir**: uyarı penceresi çıkıp çıkış/giriş ile şube seçmesi istenir. **Okuma serbest.**
Ortak kod: `DepoWise.Web/Services/BranchGuard.cs` + `DepoWise.Desktop/BranchGuard.cs`. 4 yeni test.

### Kullanıcıda firma seçimi + Firma Tanım'da ilk şube (2026-07-16)
- **Kullanıcı Tanım:** firma seçme kutusu YALNIZ süper adminde; seçilen firmaya kullanıcı açılır.
  Firma değişince **şube listesi o firmaya göre yenilenir** (asıl kusur buydu: web'de kutu vardı ama
  şube listesi eski firmadan kalıyordu). Masaüstünde kutu hiç yoktu → eklendi (`FormBranches` ayrı liste).
  Personel bağlama yalnız KENDİ firmasında (personel listesi tenant'a kilitli) — başka firmada açıklama gösterilir.
- **Firma Tanım:** yeni firmada **"İlk Şube / Şantiye Adı" zorunlu**; firma ile birlikte o firmaya bağlı
  oluşturulur (şubesiz firmaya kullanıcı açılamıyordu). Düzenlemede alan gizli.
- 5 yeni tenant testi (`UserCompanySelectorTests`): başka firmaya kullanıcı · yabancı şube reddi ·
  admin'in firma seçememesi · şubesiz firma · firma+ilk şube akışı.

### QA alan doğrulamaları (2026-07-16)
Zorunlu: araç şantiye/şube + makul üretim yılı; yakıt/stok personel. Yumuşak uyarı (kullanıcı geçebilir):
plaka Türk biçimi (iş makinesi muaf), telefon biçimi, çok büyük sayı, muayene tarih mantığı. Sayaç kuralı
(düşük değer aracın KM'sini değiştirmez) zaten doğruydu. Web + masaüstü + API sınır katmanı; FieldChecks ortak.

### 17-maddelik istek — TAMAMLANDI (2026-07-15)
Tenant firma seçici · yetki ağacı tam gizleme · ilk-login şifre · bağlanacak kullanıcı (ad+şube) ·
seçili satır vurgusu · SignalR foto takılma düzeltmesi · araç foto silme (düzenleme modu) · tanım
tekilleştirme (dedup) + duplicate uyarısı + spinner · alt kategori aktif+bağlı+"+" · şablon fotoğrafları +
malzeme şablonu uyumlu araçlar · düzenlemeye giriş onayı · **temiz test ortamı** (sunucu+yerel sıfırlandı,
süper admin korundu).

### Bu oturumda (2026-07-15) tamamlananlar (17-maddelik istekten)
- **Tenant:** Şube ekranında firma seçici (süper admin tümü, diğerleri kendi firması); `/api/companies/options`.
- **Yetki ağacı:** yetkisiz/verilmeyecek kalemler kilit yerine TAMAMEN gizli; hedef-kullanıcı bazlı.
- **İlk giriş zorunlu şifre** (web+masaüstü Adım 4); Migration042.
- **"Bağlanacak kullanıcı"** yalnız Ad Soyad + şube.
- **Seçili satır** tema-uyumlu vurgu (CSS temeli).
- **KRİTİK:** Foto yüklerken ekran takılması → SignalR MaximumReceiveMessageSize 32KB→12MB.
- **Araç foto silme** yalnız düzenleme modunda.

### Bu oturumda yapılanlar (2026-07-14)

- **Makine Yedekleri ekranı** (süper admin): makine/firma/şube detayı + günlük yedekler + **aylık ZIP arşivi**.
  Masaüstü **her gün** yedek yükler; ay tamamlanınca günlükler tek ZIP'e alınır, hamlar silinir; arşivler
  **3 yıl** saklanır. **Disk koruması:** disk kritikleşirse en eski arşivler otomatik budanır (ADR-070 dersi).
- **Rol Yetki Kontrol ekranı** (süper admin): ekran × rol matrisi. Bir ekranı bir role kapatınca →
  yetki ağacında **görünmez**, grant **reddedilir**, verilmiş olsa bile **erişim kapanır** (Admin bypass'ı dahil).
  Süper admin muaf. Yapısal kilitler (süper-admin-only / admin-kısıtlı) değiştirilemez.
- **Kehribar menü teması:** web ve masaüstü üst bar + kenar menüye yarı şeffaf kehribar katman.
- Uygulama içi **logo boyutları** büyütüldü; masaüstü login "GİRİŞ YAP" yazısı ortalandı.

> **Bekleyen işleri her zaman [docs/YARIM_KALAN_ISLER.md](docs/YARIM_KALAN_ISLER.md)'den oku.**
> Kullanıcı "yarıda kalan işler ne?" diye sorduğunda bakılacak tek liste odur; her değişiklikte güncellenir.

### Bu oturumda yapılanlar (2026-07-12) — ADR-064 … ADR-074

**Kritik olaylar (ikisi de çözüldü, önlem alındı):**
- **ADR-064 — Süper admin kilitlenmesi:** Firma silme, o firmadaki *tüm* kullanıcıları pasife alıyordu; süper admin
  kendi firmasını silince sistemden tamamen kilitleniyordu ("kullanıcı adı veya parola hatalı"). Artık firma silme
  süper admini **asla** pasife almaz + sunucu açılışında pasif süper adminleri aktifleştiren **self-heal** var.
- **ADR-070 — TAM KESİNTİ: sunucu diski doldu.** `/data` (974 MB) %100 dolunca SQLite yazamadı → **login dahil tüm
  API 500**. Sebep: her masaüstü paketi ~85 MB ve eski paketler hiç temizlenmiyordu (11 paket = 892 MB).
  Eski paketler silindi (%100 → %36) + **otomatik saklama politikası** (en yeni 3 paket tutulur, `ReleaseStore.PruneOld`).
  ⚠️ **Disk dolması sessiz değil ÖLÜMCÜLdür.** Teşhis: `flyctl ssh console --config fly.toml -C "df -h /data"`.

**Özellik / hata işleri:**
- **ADR-067 — #6 Personel ekranı NİHAİ hâli (Fikir A):** personel + uygulama kullanıcısı **tek ekranda**
  ("Uygulama erişimi ver" → kullanıcı adı/şifre/rol; "Hesabı kaldır"). Koşullar: **☐ Saha personeli** kutucuğu ·
  hesap yoksa/açılmıyorsa **ve** kutucuk işaretsizse **uyarı penceresi** (işaretliyse hiç çıkmaz) ·
  **unvan sabit tanım + "+"** · mükerrer kişi uyarısı · bir personele tek hesap.
  *(Geçmiş: önce Fikir B — ayrı ekran — yapıldı, kullanıcı beğenmedi → A'ya dönüldü, koşullar korundu. ADR-065 geçersiz.)*
- **ADR-066 — Silinen şubeler her yerde listeleniyordu:** şubeler sunucu-otoriteli ama masaüstü yerel kopyası
  yalnız *upsert* ediliyordu → silinen şube yerelde kalıyordu. Artık her girişte sunucu şube listesi **aynalanır**.
- **ADR-068 — Firma silince 401 + firmalar yüklenmiyor:** süper admin **içinde çalıştığı** firmayı silince
  token'daki firma geçersiz kalıyor, sonraki her istek 401 dönüyordu. Artık silinmiş firmada **home firmaya düşer**
  (oturum yaşar); *hiç var olmayan* firmada fail-closed korunur.
- **ADR-069 — SİLMEDE WEB TAM OTORİTER:** web'de silinen kayıt makinelerin yerel DB'sinden de **düşer**
  (silme LWW'yi aşar) **ve** sunucuda silinen kayıt **cihaz push'uyla diriltilemez**. Silme dışındaki LWW korundu.
- **ADR-071/072 — Firmalar sunucu-otoriteli + OFFLINE-FIRST kuyruk:** masaüstünde eklenen/silinen firma web'e hiç
  ulaşmıyordu. Artık işlem **önce yerele** yazılır + **kuyruğa** (`sync_outbox`) alınır; internet gelince **sırayla**
  işlenir. Yeniden denemede **hata düşmez** (idempotent). **Eşitleme sırası: 1) firma → 2) sabit tanımlar → 3) iş kayıtları.**
- **ADR-073 — Kota "ONLINE":** inceleme sonucu **zaten kullanıcı bazında tekildi** (aynı kişi web+masaüstü = 1);
  düzeltilecek hata yoktu. Şart 4 testle sabitlendi + gerçek bir bellek sızıntısı giderildi.
- **ADR-074 — Marka logoları** (web + masaüstü): tam logonun opak beyaz zemini flood-fill ile şeffaflaştırıldı
  (kamyonun beyaz kabini korunarak), sembolden 7 boyutlu `.ico` üretildi, **`.exe` simgesi** (hiç ayarlı değildi) eklendi.
  **Kullanıcı isteği: logoların arkasında beyaz kutu OLMAYACAK — yalnız logo.**

> Daha eski oturumların ayrıntısı: `docs/DECISIONS.md` (ADR-056…063) ve `docs/PROJECT_STATE.md`.

---

## 3. SIRADAKI TEK IŞ

> **Şu an bekleyen iş YOK.** Büyük yetki/ekran promptu (Adım 1–7) kod + test (313/313) + **CANLIYA ALINDI**
> (2026-07-13): API + Web deploy (health/login 200), masaüstü **1.0.48** yayınlandı (sunucuda "en güncel").
> Kullanıcı komutu olmadan yeni faza/işe kendiliğinden başlama (CLAUDE.md §1).
>
> **Bu turda yapılanlar (Adım 1–7):** Sync kaldırıldı · Talep→Form/Onaylama · Kısıtlı Süper Admin + delegasyon +
> Firma Yetki Kontrol 3-düzey · Firma Tanım ayrı admin/personel + makine kotası · Yetki Şablonu firma-kapsamlı ·
> Malzeme şablonu + şablon-dışı uyarı · Kullanıcı-şube zorunluluğu (admin dahil) · yeni login tasarımı (fotoğraf zemini).

**Bu oturumda yapılanlar (2. prompt, ADR-076…082):** silinen makine firması/şubesi girişe sunulmuyor ·
makine yönetiminde firma değiştirme · canlı sunucu ekranında disk + paket silme · web logosu düzeltildi ·
ilk açılış tema varsayılanları · personel ekranı "mevcut kullanıcıyı bağla" · firma yetki kontrol global kilit.

**Kullanıcıdan onay/geri bildirim bekleyenler:**
- Yeni **Personel ekranını** (tek ekranda hesap açma + saha kutucuğu + unvan "+") canlıda gözden geçirmesi.
- **Logo yerleşimi**: arka plansız hâliyle beğendi mi? (Koyu temada logo lacivert ağırlıklı olduğu için kontrast
  düşebilir — kullanıcı bunu bilerek arka planı istemedi. Şikâyet gelirse koyu tema için açık renkli logo varyantı gerekir.)

**Yeni iş geldiğinde:** önce `docs/YARIM_KALAN_ISLER.md`'ye ekle, sonra uygula, bitince oraya "Tamamlananlar"a taşı.

---
## 4. AÇIK YAYIN ENGELLERI (genel kullanıcı yayını öncesi)

- **R10:** Kalan operasyonel modül ekranlarının UI bağlanması (Malzemeler bağlı, gerisi sırada).
- **R8/R9:** Web oturum kalıcılığı + masaüstü/web login akışı (büyük kısmı 05.07'de bağlandı).
- **R4/R7:** (ADR-057) PostgreSQL'e geçilmedi; gerçek sistem uçtan uca SQLite. Artık "engel" değil — PostgreSQL sadece gelecek bir seçenek (karar kullanıcıya bırakıldı).
- **R22:** Code-signing (imzasız sürümde şeffaf uyarı var — maliyet kararı bekliyor).

> Tam açık/kapalı liste: [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md).

---

## 5. Çalıştırma / Güvenli Komutlar

**Yeni/temiz PC'de ilk kurulum (araçlar):** git, GitHub CLI (`gh`), .NET 8 SDK, Node.js, flyctl gerekir.
Windows'ta hepsi winget ile: `winget install Git.Git GitHub.cli Microsoft.DotNet.SDK.8 OpenJS.NodeJS.LTS Fly-io.flyctl`.
Sonra `gh auth login` (GitHub), `flyctl auth login` (deploy için), `git clone https://github.com/osmanalpaslan/DepoWise`.
`OPENAI_API_KEY`, `DEPOWISE_ADMIN_*` gibi ortam değişkenleri makineye özeldir — yeni PC'de yeniden ayarlanır.

- Bu makinede COMODO yok (2026-07-09'da yeni PC'ye geçildi) — EXE/BAT doğrudan çalıştırma yasağı kalktı (ADR-056). `dotnet` ile çalıştırma yine de önerilir.
- Masaüstü (senin makinen): uygulamayı kapat → **"DepoWise (Gercek DB)"** kısayolundan aç.
- Geliştirme derleme: `dotnet build DepoWise.sln`
- Test: `dotnet test tests/DepoWise.Tests/DepoWise.Tests.csproj`
- Masaüstü çalıştır: `dotnet run --project src/DepoWise.Desktop`
- Web (Blazor, gerçek/aktif): `dotnet run --project src/DepoWise.Web`
- API (sunucu, yerel): `dotnet run --project src/DepoWise.Api`
- (`apps/web` eski Next.js denemesi — donmuş, kullanılmıyor; bkz. ADR-057)

### Canlıya alma (deploy) — doğrulanmış komutlar

```bash
flyctl deploy --config fly.toml     --ha=false   # API  → depowise-erp.fly.dev
flyctl deploy --config fly.web.toml --ha=false   # Web  → depowise-web.fly.dev
curl -s -o /dev/null -w "%{http_code}" https://depowise-erp.fly.dev/health   # 200 bekle
```
> **API'yi de deploy etmeyi unutma** eğer `src/DepoWise.Api`, `Infrastructure` ya da migration değiştiyse —
> yeni web eski API'ye çarparsa 404/500 alır.

### Masaüstü paketi yayınlama (sürüm artır!)

```bash
dotnet publish src/DepoWise.Desktop/DepoWise.Desktop.csproj -c Release -r win-x64 \
  --self-contained true -p:UseAppHost=true -p:Version=1.0.47 -o artifacts/rc/desktop-1.0.47
# PowerShell: Compress-Archive -Path "artifacts\rc\desktop-1.0.47\*" -DestinationPath "artifacts\rc\DepoWise-desktop-1.0.47.zip" -Force
node scripts/publish_release.mjs artifacts/rc/DepoWise-desktop-1.0.47.zip 1.0.47 "sürüm notu"
```
- Kimlik: `DEPOWISE_ADMIN_USER` / `DEPOWISE_ADMIN_PASS` **ortam değişkenlerinden** okunur (bu makinede kurulu).
- Script login olur, checksum'ı kendi hesaplar, yükler ve "en güncel sürüm" doğrulamasını yapar.
- Açık masaüstüler 60 sn içinde otomatik güncelleme uyarısı alır.
- Sunucu **en yeni 3 paketi** tutar (ADR-070); eskiler otomatik silinir.

### ⚠️ Sunucu diski (ADR-070 — tam kesinti yaşandı)

```bash
flyctl ssh console --config fly.toml -C "df -h /data"        # doluluk
flyctl logs --config fly.toml --no-tail | grep -i "disk is full"
```
Disk dolarsa SQLite yazamaz → **login dahil her uç 500 döner.** Çare: `/data/releases` altındaki eski
`.pkg` dosyalarını sil (en günceli koru).

---

## 6. Nereye Bakayım? (dosya haritası)

| İhtiyaç | Dosya |
|---|---|
| **Yarım kalan işler + testleri (sıradaki ne?)** | [docs/YARIM_KALAN_ISLER.md](docs/YARIM_KALAN_ISLER.md) |
| Ekranların çalışma mantığı + backlog (ortak defter) | [docs/PROJE_REHBERI.md](docs/PROJE_REHBERI.md) |
| Detaylı faz faz ne yapıldı | [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md) |
| Açık/kapalı bilinen sorunlar (R-numaraları) | [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) |
| Alınan teknik kararlar (ADR) | [docs/DECISIONS.md](docs/DECISIONS.md) |
| Test kanıtları | [docs/TEST_EVIDENCE.md](docs/TEST_EVIDENCE.md) |
| Bağlayıcı analiz (ürün sözleşmesi) | [docs/DEPOWISE_ANALYSIS.md](docs/DEPOWISE_ANALYSIS.md) |
| Ana kurallar (Claude nasıl çalışır) | [CLAUDE.md](CLAUDE.md) |
