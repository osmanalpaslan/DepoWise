# SON UÇTAN UCA STABİLİZASYON VE YAYINA HAZIRLIK TURU — 2026-08-26 (üçüncü tur)

> Amaç: yeni özellik eklemek değil; çalışan hiçbir şeyi bozmadan, gözden kaçmış **gerçek** hataları
> bulup önce kanıtlamak, sonra en küçük güvenli düzeltmeyi yapmak ve regresyon olmadığını göstermek.
>
> Öncelik sırası: **KARARLILIK > GÜVENLİK > VERİ DOĞRULUĞU > PERFORMANS > TEST KAPSAMI > ARAYÜZ > YENİ ÖZELLİK**

---

## 1. Başlangıç baseline (yeniden ÖLÇÜLDÜ, önceki rapora güvenilmedi)

| Ölçüm | Sonuç | Önceki raporla |
|---|---|---|
| HEAD | `153e21a` = `origin/master`, ağaç temiz | ✔ aynı |
| Release derlemesi (API·Web·Desktop) | **0 hata** | ✔ aynı |
| Tam test | **2323 geçti · 0 başarısız · 37 atlandı** | ✔ **birebir aynı** |
| Migration | **1…72 kesintisiz, mükerrer yok, katalog 72/72** | ✔ aynı |
| Üretim | API v168 · Web v192 · şema 72 · `/health` 200 | ✔ aynı |

Ayrıca bağımsız doğrulananlar: her migration **tek transaction** içinde ve uygulanmış sürüm kontrolüyle
idempotent; üretimde **tek firma** var ve kimliği güvenli biçimde (onaltılık GUID).

**Önceki raporla fark bulunamadı.**

---

## 2. İncelenen kapsam

| Alan | Nasıl incelendi |
|---|---|
| API uçları | **301 uç** dengeli-parantez ayrıştırmasıyla tarandı; kimlik doğrulaması olmayan **15 uç** tek tek okundu |
| Tenant (firma) | İstekten firma/şube kimliği alan **47 uç** çıkarıldı; 35 aday elle doğrulandı + **yazma tarafı** için yeni süpürme yazıldı |
| IDOR | `{id}` alan **103 uç** tarandı; 10 aday işaretlendi, **10'u da** devredilen metotta tenant kapılı çıktı |
| SQL enjeksiyonu | Dinamik SQL üreten **40+** yer; sıralama/filtre kolonları beyaz listeden çözülüyor |
| Yol geçişi | `Path.Combine` kullanan **30 yer**; 4 gerçek açık bulundu (YOL-01) |
| Sır sızıntısı | Kayıtlı dosyalarda parola/jeton/bağlantı dizesi deseni — **temiz** |
| Yutulan istisna | Boş `catch` blokları (API 18 · Infra 16 · Web 70 · Desktop 190) tarandı; hepsi belgelenmiş/zararsız |
| Web hata dayanıklılığı | İstisna fırlatan **her** API çağrısı `try` içinde mi — tarandı (tek aday, iki çağıranda da korumalı) |
| Olay sızıntısı | `+=` / `-=` dengesi (31 abonelik); web **temiz**, masaüstünde 2 statik olay açıktı (MAS-01) |
| Transaction | **139 transaction açılışı** — dengesiz kalan yok |
| Idempotency | Kritik tabloların `operation_id` benzersiz indeksleri doğrulandı |
| Raporlar | 21 raporun her biri için firma filtresi + şube kapsamı + yetki kapısı kaynaktan çıkarıldı |
| Menü/Ekran | Kendini kilitleme koruması (MNU-B2) hem ekran görünürlüğünde hem menü düzeninde doğrulandı |
| Test kalitesi | **10 mutasyon** (kasten bozma) uygulandı ve kaynak her seferinde aynen geri alındı |

---

## 3. Bulunan gerçek hatalar — özet

| ID | Önem | Kısaca | Düzeltildi? | Test? | Üretime çıktı? |
|---|---|---|---|---|---|
| **YED-02** | **P1** | Sunucu yedek YÜKLEME ucu cihaz jetonunu **hiç doğrulamıyordu** | ✅ | ✅ 7 | ✅ |
| **YOL-01** | P2 | Firma/makine adı doğrulanmadan **dosya yoluna** giriyordu (4 yer) | ✅ | ✅ 11 | ✅ |
| **SNK-01** | P2 | Senkron yolu araç **sayacını geriye alabiliyordu** | ✅ | ✅ 5 | ✅ |
| **RPR-14** | P2 | 6 muhasebe raporu **firma seçimini yok sayıyordu** | ✅ | ✅ 5 | ✅ |
| **PRS-01** | P2 | Şube kapsamı **sayfalamadan sonra** uygulanıyordu | ✅ | ✅ 5 | ✅ |
| **YET-05** | P3 | "İptal / Ters Kayıt" arayüz kapısı sunucudan farklıydı | ✅ | ✅ 7 | ✅ |
| **MAS-01** | P3 | Masaüstü kabuğu çıkış→girişte serbest bırakılmıyordu | ✅ | ✅ 5 | ✅ |

Ayrıca **analiz edilip bilinçli olarak DEĞİŞTİRİLMEYENLER**: MAK-01/b (makine aktivasyon modeli),
YET-01 (işlevsiz iki yetki anahtarı), RPR-15 (rapor yetkisinin modül yetkisi istememesi),
PostgreSQL dosya yedeği. Gerekçeler §22 ve `KNOWN_ISSUES.md`'de.

---

## 4. Her hata için: kök neden · kanıt · düzeltme · test

### YED-02 — Sunucu yedek yükleme ucu kimliği doğrulamıyordu · **P1**

**Kök neden.** Uç yalnız şunu yapıyordu:
```csharp
if (DeviceToken(req) is null) return Results.Unauthorized();
```
`DeviceToken` sadece `Authorization: Bearer …` başlığını **ayrıştırır** — jetonu doğrulamaz. Kardeş uçlar
(`/sync/push`, `/sync/pull`) jetonu `SyncServer.AuthDevice` ile veritabanından doğrularken **burada o adım
yoktu**. Üstelik dosyanın yazılacağı **firma ve makine adı da istekten** (`form["company"]`) geliyordu.

**Kanıt.** `Bearer tamamen-uydurma-jeton` ile yapılan istek **200** döndü ve dosya diske yazıldı.
Gövde sınırı **1 GB**, hız sınırı yok, depo *"üzerine yazmaz / otomatik silmez"*.

**Etki.** Disk dolduğunda **tüm API 500 döner** — bu daha önce yaşandı (ADR-070). Yani kimliği olmayan bir
çağıran sistemi durdurabilirdi. Ayrıca sahte yedekler süper adminin ekranında gerçek firmanın yedeği gibi
görünürdü. ⚠️ **Veri sızıntısı yoktu** (uç yalnız yazar; okuma uçları SEC-04'te kapatılmıştı).

**Düzeltme.** Kimlik gerçekten doğrulanır: geçerli **JWT oturumu** *veya* geçerli **cihaz senkron jetonu**.
Firma artık **formdan değil kimlikten** alınır. İkinci katman: IP başına hız sınırı (60/saat, NAT dostu).
Masaüstünün bugünkü akışı değişmedi — zaten oturum jetonu ve kendi firmasını gönderiyordu.

**Testler (7).** Uydurma jeton reddi · **diske yazılmadığının** doğrulanması · jetonsuz istek ·
geçerli oturumla yüklemenin ÇALIŞMASI · başka firmanın klasörüne yazılamaması · süper admin listesi ·
yedek silmenin kök dışına çıkamaması.

---

### YOL-01 — Firma/makine adı dosya yoluna doğrudan giriyordu · **P2**

**Kök neden.** Firma kimliği `POST /api/companies` gövdesinden geliyor ve **hiç doğrulanmıyordu**
(masaüstünün çevrimdışı ürettiği kimliği korumak için bilinçli olarak serbest bırakılmıştı). Aynı değer
sonra dosya yoluna giriyordu — **dört yerde**:

1. `purge-company` → `Path.Combine(dataDir, sub, companyId)` + **özyinelemeli silme**
2. `reset-company-business` → aynı desen
3. `BackupStore.DeleteRange` → `DELETE /api/backups?company=…` (firma adı **istekten**)
4. `MachineBackupArchiver.ResolveArchive` → dosya adı korunuyordu, **firma/makine adı korunmuyordu**

**Etki.** Kimlik `".."` olsaydı (1)/(2)'de silinecek klasör **veri kökünün kendisi** olurdu → bütün
firmaların fotoğrafları, makine yedekleri, yayın paketleri ve SQLite'a düşülmüşse veritabanı birlikte
giderdi. (3)'te taranan klasör yedeklerin ÜST klasörü olur, tarih aralığındaki **yayın paketleri** ve
**veritabanı yedekleri** silinirdi. Süper admin gerekir — ama silmeyi yapan kişi **tek bir firmayı**
sildiğini sanır.

**Düzeltme — iki katman.** (a) Firma kimliği yalnız harf/rakam/`-`/`_` içerebilir. (b) Yol
`SafePath.UnderRoot` ile çözülür; taban klasörün altında değilse **hiçbir şey yapılmaz**.

> Taban, son parça HARİÇ tüm parçalardır. Yalnız "kökün altında" demek YETMEZ: `kök/files/../ust`
> kökün altındadır ama `files`'tan çıkmıştır. **`SafePath`'in ilk sürümü tam bu inceliği kaçırıyordu
> ve kendi testim yakaladı.**

**Testler (11).** Kök dışına çıkan 5 kimlik · normal kimlikler · boş kimlik · HTTP üzerinden 5 yol
karakterli kimlik reddi · çevrimdışı GUID'in çalışmaya devam etmesi · kimliksiz oluşturma · silmenin
kök dışına çıkamaması.

---

### SNK-01 — Senkron yolu araç sayacını geriye alabiliyordu · **P2**

**Kök neden.** Mimari kural (CLAUDE.md §4): *"Stok, sayaç, yakıt, bakım ve onayda LWW yasaktır."*
Doğrudan yol buna uyuyordu (`VehicleService.SetMeter` → `MeterBackwardException`). Ama
`POST /api/sync/business-push` araç satırını **düz LWW ile** upsert ediyor, `current_meter` için hiçbir
kontrol yapmıyordu.

**Kanıt (izole sunucuda gerçek istek).**
```
ÖNCE : {"kod":"AR-MERKEZ","sayac":1000}
push : 200 {"upserted":1,"skipped":0,"errors":[]}
SONRA: {"sayac":10}
```
Hata yok, çakışma yok — **sessiz**.

**Etki.** Sayaç, yakıt tüketimi (km/saat başına) ve **bakım periyodu** hesaplarının girdisidir. Geriye
giden sayaç yanlış tüketim raporu üretir ve **bakım uyarılarının kaçırılmasına** yol açar. Çevrimdışı
çalışmış, yerel sayacı eski kalmış bir masaüstü bunu farkında olmadan tetikleyebilir.

**Düzeltme (yeni kural YOK).** Senkron yolunda da **mevcut** `MeterRule.ShouldAdvance` uygulanır: gelen
büyükse ilerler, küçükse **dokunulmaz**. Satır reddedilmez — diğer alanlar normal uygulanır, meşru
düzenlemeler (plaka, durum) kaybolmaz. Yalnız **istemci → sunucu** yönünde.

**Aynı sınıftan başka boşluk var mı:** tarandı — stok (`quantity` pozitif + sunucu bakiyeyi hareketlerden
yeniden hesaplar), yakıt (litre/fiyat/tutar), onay (durum beyaz listesi) zaten korunuyordu. **Sayaç tek
boşluktu.**

**Testler (5).** Geriye gitme engeli · diğer alanların yine uygulanması · ileri gitmenin çalışması ·
aynı değer · sayaç alanı hiç gönderilmediğinde mevcut değerin korunması.

---

### RPR-14 — Muhasebe raporları firma seçimini yok sayıyordu · **P2**

**Kök neden.** Rapor ekranı süper adminde **"Firma (Süper Admin)"** seçicisi gösterir ve seçileni her
istekte `companyId` olarak gönderir. 15 rapor bunu `ReportGate.ResolveCompany` ile çözerken, **6 ön
muhasebe raporu** alanı hiç okumuyor, doğrudan `s.CompanyId` kullanıyordu.

**Etki.** Süper admin **B firmasını** seçtiğinde rapor **A firmasının** mali verisini getiriyor, ekranda
B seçili görünüyordu → **yanlış firmanın rakamları**. Sessiz: hata yok, boş sonuç yok, yalnız yanlış veri.

⚠️ Tenant açığı DEĞİL — yön ters: uç istenen firmayı kullanmak yerine kendi firmasına düşüyordu.

**Düzeltme.** 6 metotta diğer 15 raporla **aynı** çözüm. Süper admin olmayan için davranış değişmez
(`ResolveCompany` yabancı firmada 403, boş/kendi firmasında oturum firması). Masaüstü `CompanyId`
göndermediği için masaüstü davranışı **birebir aynı** kalır.

**Testler (5).** Seçilen firmanın verisi · başka firma seçilince diğerinin verisinin gelmemesi · ikinci
rapor türü · firma gönderilmezse oturum firması · yetkisiz kullanıcının yabancı firma isteyememesi.

---

### PRS-01 — Şube kapsamı sayfalamadan sonra uygulanıyordu · **P2**

**Kök neden.** `PersonnelService.List` veritabanından `LIMIT n+1` satır çekiyor, sonra **bellekte** kapsam
dışı şubeleri eliyor, ve "sonraki sayfa" imlecini **eleme sonrası** sayıya bakarak üretiyordu.

**Etki.** Bir sayfa kapsam dışı kayıtlarla dolduğunda kullanıcı **boş liste** görür **ve** imleç
üretilmediği için **sonraki sayfaya hiç geçemez** → tek şubeye yetkili kullanıcı kendi şubesindeki
personeli göremeyebilir. Güvenlik açığı değil (fazla değil, EKSİK gösterme) ama gerçek bir veri
görünürlüğü hatası. **Üretimde 0 şube olduğu için bugüne dek görülmedi.**

**Düzeltme.** Filtre SQL'e taşındı (araç listesindeki mevcut desenin aynısı). Görünen küme birebir aynı.

> **Kendi testim önce dişsizdi:** sıralama `created_at DESC` olduğu için kapsam içi kayıt zaten ilk
> sayfaya düşüyordu. Kasten bozma denemesi bunu ortaya çıkardı; kurgu düzeltildi ve ancak ondan sonra
> gerçek kırmızı→yeşil elde edildi.

**Testler (5).** Sayfalamada kaybolmama · ilk sayfanın boş dönmemesi · kapsam dışının hâlâ gizli olması ·
şubesiz kaydın görünür kalması · adminin tüm şubeleri görmesi.

---

### YET-05 — "İptal / Ters Kayıt" arayüz kapısı sunucudan farklıydı · **P3**

**Sunucu kuralı:** `stock.Edit` **ve** `btn-reverse`.
**Arayüzler:** masaüstü Stok yalnız `stock.Delete` (buton kontrolü yok); web Stok `stock.Delete` + buton.

**İki yönlü sonuç.** (a) Yöneticinin `stock.Edit`+`btn-reverse` verdiği kullanıcı butonu **hiçbir
platformda göremiyordu** — verilen yetki kullanılamıyordu (YET-02 ile yetki verilebilir hâle gelince bu
boşluk görünür oldu). (b) Yalnız `stock.Delete`'i olan kullanıcı butonu görüp tıklayınca hata alıyordu.

⚠️ **Güvenlik açığı değildi** — sunucu her iki durumda da doğru davranıyordu.

**Düzeltme.** Yalnız arayüz eşitlendi (masaüstü Stok, masaüstü Yakıt, web Stok). **Sunucu kuralına
dokunulmadı** — sunucu tek otorite olarak kalır.

**Testler (7).** 3 sunucu kuralı kilidi + 3 arayüz kaynak kilidi + kilidin kendini sınaması.

---

### MAS-01 — Masaüstü kabuğu çıkış→girişte serbest bırakılmıyordu · **P3**

**Kök neden.** Her girişte **yeni** bir `ShellViewModel` oluşur; eskisi iki **statik** olaya abone
kalıyordu (`DeveloperMode.Changed`, `ServerAuthClient.SessionExpiredRaised`) ve `_updateTimer` hiç
durdurulmuyordu.

**Etki.** Aynı uygulama oturumunda her çıkış→giriş bir kabuk daha biriktirir → dakikada N kez güncelleme
kontrolü, yeni sürüm çıktığında birden çok "güncelleme mevcut" penceresi, çıkışta geliştirici modu
kapanırken **kapanmış pencerelerin** işleyicilerinin de çağrılması, sürekli artan bellek.

**Düzeltme.** `ShellViewModel.Release()` (idempotent) + `App.ShowLogin()` yeni kabuk oluşturmadan önce
eskisini bırakır.

**Testler (5).** İki statik aboneliğin çözülmesi · iki zamanlayıcının durdurulması · çıkış akışının
kabuğu bırakması · kilidin kendini sınaması.

---

## 5. Kasten bozularak doğrulanan testler (mutasyon turu)

Her mutasyondan sonra kaynak **aynen geri alındı** (her koşuda doğrulandı).

| # | Mutasyon | Sonuç |
|---|---|---|
| M1 | Rapor şube filtresi kaldırıldı (fail-open) | ✅ 9 test kırıldı |
| M3 | Güncelleme checksum kontrolü kapatıldı | ⚠️ **eşdeğer mutasyon** — kod iki katmanlı fail-closed |
| M3b | Gerçek UPD-01 öncesi davranış (`return;`) | ✅ 3 test kırıldı |
| M4 | Senkron idempotency (inbox) kapatıldı | ✅ kırıldı |
| M5 | Rapor Excel buton yetkisi kaldırıldı | ✅ kırıldı |
| M6 | Stok ters kayıt buton kapısı kaldırıldı | ❌ **fark edilmedi** → gerçek test zayıflığı |
| M6b | Aynı mutasyon, test düzeltildikten sonra | ✅ kırıldı |
| M7 | TNT-05 şube aidiyeti kapısı kaldırıldı | ✅ kırıldı |
| M9 | Tenant kapısı istenen firmayı koşulsuz kabul | ✅ 5 test kırıldı |
| M10 | YED-02 kimlik doğrulaması geri alındı | ✅ 3 test kırıldı |
| M11 | YOL-01 yedek silme koruması kaldırıldı | ✅ kırıldı |
| M12 | RPR-14 firma çözümü geri alındı | ✅ 2 test kırıldı |

**Bulunan iki şey:**
- **M3 bir test boşluğu DEĞİLDİ** — boş checksum kontrolü kapatılsa bile ikinci kontrol yine reddediyor.
  Yani güncelleme kapısı **iki katmanlı**. Gerçek eski davranışla tekrarlanınca test kırıldı.
- **M6 gerçek bir zayıflıktı:** testim yalnız `ForbiddenException` bekliyordu; buton kapısı kaldırılınca
  "Belge bulunamadı" da **aynı türden** istisna fırlattığı için test yine geçiyordu. Test artık istisna
  **mesajını** da sınıyor. → **Ders: aynı istisna türüyle biten iki farklı yol, testi sessizce dişsiz bırakır.**

---

## 6. Tenant (firma) güvenliği

| Kontrol | Sonuç |
|---|---|
| Okuma süpürmesi (mevcut, 13 senaryo) | ✅ hepsi geçti |
| **Yazma süpürmesi (bu turda YENİ, 13 senaryo)** | ✅ hepsi geçti — **veritabanı satırına bakılarak** |
| IDOR (`{id}` alan 103 uç) | ✅ 10 aday elle doğrulandı, hepsi tenant kapılı |
| Anonim yazma | ✅ reddediliyor |

Yazma süpürmesi HTTP durumuna DEĞİL, **B firmasının satırının değişip değişmediğine** bakar: başka
firmaya şube açma, kota değiştirme, makine pasife alma/silme/şube-firma değiştirme, yerel sıfırlama
isteği, yabancı şubeye araç/personel yazma, firma ve rol yetki matrisleri, anonim yazma.

---

## 7. Şube güvenliği

| Kontrol | Sonuç |
|---|---|
| İzole matris (A1/A2 + B1, 25 senaryo) | ✅ |
| Gerçek tarayıcıda iki şubeli tur | ✅ (aşağıda) |
| **PRS-01** | ⚠️ **bu turda bulundu** — 0 şube olduğu için gizli kalmıştı |

> ⚠️ **Üretimde hâlâ hiç şube tanımlı değil (0 şube).** Şube davranışı **canlı veriyle
> gözlemlenemedi**. PRS-01 tam da bu yüzden bugüne dek görünmemişti; şube tanımlandığında benzer
> "0 şubede görünmeyen" hatalar çıkabilir.

---

## 8. Raporlar 21/21

| Ölçüm | Sonuç |
|---|---|
| Katalog | **21 rapor**, hepsi `Run` tarafından tanınıyor (mevcut test kilidi) |
| Yetki kapısı | 21/21 |
| Firma filtresi | 21/21 |
| Şube kapsamı | **19/21** uygular; **Stok Durumu** ve **Stok Sayım** bilinçli olarak fiziksel depo mantığıyla çalışır (kanıtlanmış tasarım — bu turda DOKUNULMADI) |
| Malzeme raporları | Şube sütunu **yok** (firma geneli ortak katalog) → filtrelenecek bir şey yok |
| Test kapsamı | 21 rapor anahtarının **hepsi** en az bir test dosyasında geçiyor |
| Filtre açılır listeleri | Şube · araç · personel kapsamla kırpılıyor (gerçek tarayıcıda doğrulandı) |

**Kişisel veri:** Personel raporu `personnel`, Muayene/Sigorta `inspection`, 6 muhasebe raporu ilgili
modül yetkisini **ayrıca** ister. Diğer raporlarda yalnız `reports` yeterlidir → bkz. **RPR-15** (§22).

---

## 9. Web / Masaüstü paritesi

- Rapor motoru **ortak koddadır**; tarih kuralı tek kaynaktan gelir (parite testleriyle kilitli).
- YET-05 düzeltmesi **iki platformda birlikte** yapıldı (masaüstü önce, web hemen ardından).
- RPR-14 ortak kodda düzeltildi; masaüstü `CompanyId` göndermediği için masaüstü davranışı **değişmedi**.
- SNK-01 sunucu tarafındadır → iki platform da aynı korumayı alır.

---

## 10. Senkron / çevrimdışı

| Kontrol | Sonuç |
|---|---|
| Mevcut senkron testleri | **148 geçti · 1 atlandı** |
| Idempotency indeksleri | Kritik tabloların hepsinde `operation_id` benzersiz |
| Tek transaction + rollback | ✅ (2026-07-19 düzeltmesi yerinde) |
| İstemcinin sunucu yanıtını okuması | ✅ (Z2/Z3 yerinde — sorun varsa imleç ilerlemez) |
| Kritik değişmezler | Stok ✅ · Yakıt ✅ · Onay ✅ · **Sayaç ❌ → SNK-01 ile kapatıldı** |

---

## 11. Güncelleme / yayın bütünlüğü

Boş · null · boşluk · yanlış checksum, yarım paket, geçersiz sürüm — **hepsi reddediliyor**; doğru
checksum geçiyor; kurulum hatasında eski sürüme dönülüyor. Mutasyonla doğrulandı (M3b).

---

## 12. Menü / Ekran yönetimi

Kendini kilitleme koruması (MNU-B2) hem ekran platform görünürlüğünde hem menü düzeninde uygulanıyor;
süper admin rol-kapatmadan **muaf** → hiçbir yoldan kilitlenme yok. Mevcut **109 test** yeşil.

---

## 13. Performans (ölçüldü — izole yerel sunucu)

**Stok Hareketleri raporu**

| Veritabanındaki satır | Sorgu+yanıt | Yanıt boyutu | Dönen satır |
|---|---|---|---|
| 50.000 | **329 ms** | 6,55 MB | 50.000 |
| 100.000 | **641 ms** | 6,35 MB | **50.000 (üst sınır doğru uygulandı)** |

**Diğer raporlar (aynı veriyle):** Stok Durumu 10 ms · Araç 5 ms · Bakım 4 ms.
**Excel dışa aktarma:** 50.000 satır → **4,3 sn**, 2,2 MB.

**Yorum.** Üst sınır (`reports.max_rows`, varsayılan 50.000) **çalışıyor**: veri iki katına çıksa da
dönen satır sayısı sabit kalıyor. Sunucu tarafı hızlı; kalan yük **aktarım** (6,5 MB) ve **Excel üretimi**
(4,3 sn). Bu turda mimari değişiklik (sayfalı rapor API'si) **yapılmadı** — bkz. §22.

---

## 14. Gerçek tarayıcı (GUI) turu — izole, iki şubeli

| Senaryo | Sonuç |
|---|---|
| İlk giriş şifre belirleme akışı | ✅ çalışıyor |
| Depo kullanıcısının şube listesi | ✅ **yalnız MERKEZ** (SANTIYE-2 yok) |
| Adminin şube listesi | ✅ Tüm Şubeler + MERKEZ + SANTIYE-2 |
| Yönetici (A1+A2 kapsamlı) şube listesi | ✅ **yalnız iki yetkili şube**, "Tüm Şubeler" yok |
| Operasyon rapor ekranı | ✅ **10 rapor**, şube seçici **yok**, "Sorgula" kapısı var |
| Yönetici rapor ekranı (admin) | ✅ **21 rapor**, şube seçici **var**, "Excel'e Aktar" **var** |
| `/reports/manager` adresle (depo kullanıcısı) | ✅ **engellendi** |
| `/reports/manager` adresle (personel rolü yönetici) | ✅ **engellendi** (ekran admin yetkisi ister) |
| Araç filtresi açılır listesi | ✅ **yalnız kendi şubesinin plakası** |
| Muayene/Sigorta raporu | ✅ 2 satır, **yalnız MERKEZ**; durum kuralı doğru; tarih taşınmıyor |
| Personel raporu | ✅ 1 satır, **yalnız MERKEZ** |
| Durum Rapor (admin) | ✅ **iki şube birlikte**, sayılar doğru |
| **Sunucu kapalıyken ekran açma** | ✅ **oturum düşmedi**, devre çökmedi |
| Ağ | ~160 istek, **tek 404** ve o da elle yazdığım olmayan `/logout` adresi |
| Konsol | Ürün kaynaklı hata **yok** |

**Araştırılıp DOĞRULANAMAYAN (WEB-03).** Derleyici üç ekranda `OnRowClick`'te `e.Item`'ın null
olabileceği uyarısını veriyor (CS8604) ve Blazor'da olay işleyicisindeki istisna devreyi düşürür. Gerçek
tarayıcıda başlık satırına ve hücrelerine tıklandı — **olay tetiklenmedi, devre sağlam kaldı**.
Üretilemediği için **değiştirilmedi**; varsayımla düzeltme yapılmadı.

---

## 15. İzole masaüstü turu

| Kontrol | Sonuç |
|---|---|
| Ortam | `DEPOWISE_ENVIRONMENT=IzoleDenetim` → ayrı klasör |
| Sunucu adresi | `serverurl.txt` ile **yerel** sunucuya yönlendirildi (üretim adresi geçersiz kılındı) |
| Açılış | `host=dotnet` · `journal=wal` · `fk=True` · `writeRead=True` · `ok=True` · hata yok |
| Veritabanı | `…\Alpnex\Data\IzoleDenetim\alpnex.db` — **sıfırdan oluştu** |
| Migration | **72/72 uygulandı**, şema sürümü 72, **79 tablo** |
| Üretim | **Bağlanmadı** — üretim veri klasörüne ve üretim sunucusuna dokunulmadı |

> ⚠️ **Sınır:** Avalonia arayüzü bu ortamda otomatize edilemiyor; **ekran içi tıklama akışları
> sürülemedi**. Açılış, izolasyon, migration, veritabanı sağlığı ve sunucu yönlendirmesi doğrulandı.

---

## 16. Yedekleme (analiz — kapsam dışı bırakıldı)

- Uygulamanın yedek ekranı PostgreSQL'de **anlaşılır bir mesajla durur** (YED-01, yerinde) ve geri
  yükleme **hiçbir dosyaya dokunmadan** durdurulur → kullanıcı çalışmayan bir özelliğe güvenmez.
- Üretim yedeği bugün sağlayıcının **sürekli yedeğine (PITR)** dayanır. Bu, kaza/bozulma senaryosunu
  karşılar; **sağlayıcı hesabının kaybı** veya uzun süreli arşiv için yeterli değildir.
- Gerçek bir PostgreSQL dosya yedeği `pg_dump` ister (sunucu imajında yok), bir sır ve saklama alanı
  gerektirir → **yeni özellik + operasyon işi**. Bu turda **kapsam dışı**; üretim bağlantı bilgisi
  **istenmedi ve okunmaya çalışılmadı**.

---

## 17. Migration / veritabanı

**1…72 kesintisiz · mükerrer yok · katalog 72/72 · her biri tek transaction · uygulanmış sürüm kontrolü
ile idempotent.** **Yeni migration açılmadı**; üretim şeması **72**'de kaldı. Bu turdaki hiçbir düzeltme
şema değişikliği gerektirmedi.

---

*(Test sayıları, yayın sürümleri ve yayın sonrası sağlık kontrolleri §18–§21'de; kalan konular §22'de.)*
