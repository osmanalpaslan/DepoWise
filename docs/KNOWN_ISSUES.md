# KNOWN ISSUES

> Son güncelleme: 2026-08-27 (TSN — tanım senkronu marka/üst kategori bağını siliyordu)

## ✅ KAPATILAN (2026-08-27 — kullanıcı bildirimi)

- **TSN — Tanım senkronu, araç modelinin markasını ve alt kategorinin üstünü SİLİYORDU.**
  `GET /api/lookups/sync` alan adlarını veritabanı sütunu olarak gönderir (`brand_id`, `parent_id`,
  `brand_type`); masaüstü camelCase arıyordu (`brandId`) ve `TryGetProperty` harf duyarlı olduğu için
  alanı hiç bulamayıp "boş geldi" sanarak sütunu `NULL`'a çekiyordu. `updated_at` "şimdi"
  damgalandığı için LWW yerel satırı yeni sayıyor, iş senkronu doğru değeri geri yazamıyor ve bir
  sonraki push boş değeri **sunucuya** taşıyordu. Görünen sonuç: **yeni eklenen araç modeli, markası
  seçildiğinde listede çıkmıyordu.** ADR-159.

  **Açık kalan (bilinçli):** hatadan ÖNCE açılmış modellerin `brand_id`'si sunucuda da kayıp olabilir;
  düzeltme bunları geri getirmez (sunucudaki değer zaten yok). Güvenli çözüm: o model yeniden eklenir —
  tekilleştirme ad+marka ikilisine baktığı için doğru yeni kayıt açılır ve **hiçbir şey silinmez**.
  Çıkarıma dayalı otomatik onarım, mevcut veriyi değiştireceği için YAPILMADI.


## ✅ Bu turda KAPATILAN (2026-08-26, altıncı tur — kullanıcı bildirimi)

- **MAS-04 — Liste tablolarında kolon adları, filtre kutuları ve veriler aynı hizada değildi.**
  Dört ayrı kusur vardı: (1) filtre hücresindeki dış boşluk kolonu genişletip kaymayı **biriktiriyordu**,
  (2) hücrelerin **üst sınırı yoktu** → uzun değer gövdedeki kolonu genişletiyordu, (3) üç ekranda
  başlık ile gövde **ayrı** yatay kayıyordu, (4) üç ekranda başlık ile veri satırının **kolon sayısı
  farklıydı** (Talepler'de başlık 5 / veri 7). **31 tablo ekranının tamamı** düzeltildi; kullanıcının
  seçimiyle sütun ayırıcı çizgileri eklendi. ADR-157.

  **Bilinçli istisnalar:** esnek (`*`) kolonlar ve `SharedSizeGroup` kullanan kolonlar (orada kolonu
  Avalonia zaten eşitler); yazı olmayan hücreler — buton · sayı kutusu · durum rozeti — çünkü sabit
  genişlik etiketi kırpardı. Bunların 5'i son kolondadır (kayma sonraki kolonlara yayılmaz),
  ikisi Talepler'in rozet kolonlarıdır.

## ✅ Bu turda KAPATILANLAR (2026-08-26, beşinci tur — kullanıcı bildirimi)

- **MAS-03 — Masaüstü Malzeme Giriş-Çıkış tablosu görülemiyordu.** Veri **geliyordu** ("19 hareket"
  sayacı doluydu); form `Auto` satırında tüm boyu aldığı için listeye ~50 px kalıyordu. Form artık
  kapsayıcı yüksekliğinin bir **oranıyla** sınırlı ve taşarsa kendi içinde kayıyor; liste satırının
  taban yüksekliği var. **API / veritabanı / senkron değişmedi.** ADR-155.
- **STK-11 — Malzeme Giriş-Çıkış formunda işlem tarihi yoktu.** Artık **"İşlem Tarihi"** alanı var
  (varsayılan bugün; geçmiş ve gelecek serbest). İşlem tarihi (`stock_documents.doc_date`) ile kayıt
  zamanı (`stock_movements.created_at` + audit) kesin ayrıldı; ekran, rapor ve Excel işlem tarihini
  gösterir. **Yeni migration açılmadı — şema 72'de kaldı.** ADR-156.

## ⚠️ Operasyonel riskler (canlı sistemi durdurabilir)

- **R31 — Depo bazlı stok (Migration064) deploy edilince stoğun neredeyse tamamı "ATANMAMIŞ" görünecek**
  (ADR-102, 11.08.2026 — **henüz deploy EDİLMEDİ**, dalda duruyor). Mevcut 667 stok hareketinin **666'sı
  lokasyonsuz** olduğu için bakiye **8953,3 birim** ATANMAMIŞ kovasına düşer. Bu bir **veri kaybı değildir**
  (toplam korunur, kanıtlandı) ama kullanıcı "stoğum kayboldu / hepsi boşta" diye algılayabilir.
  **Önlem:** deploy öncesi kullanıcıya anlatılmalı; dağıtım **KARAR-8** (`STK-08`) ile yapılacak.
  Veri uydurulmadı — hangi malın hangi depoda olduğu defterde yazmıyor, tahmin edilmeyecek.

- **R32 — Migration064 bakiyesi defterle uyuşmayan veritabanında BİLİNÇLİ olarak durur** (ADR-102).
  Fail-closed: sessizce yanlış stok göstermek yerine açık hata verir ve transaction geri alınır
  (kanıtlandı). **Sonuç:** böyle bir **masaüstü** veritabanı varsa uygulama güncellemesi başlamaz.
  **Çözüm yolu:** o makinede önce sunucu-otoriteli yeniden hesaplama (`RecomputeBalances`) çalıştırılmalı.
  Üretim PostgreSQL kopyasında uyuşmazlık **yok** (664 malzemede uyuşmayan 0).

- **R30 — Sunucu diski dolarsa TÜM API 500 döner** (ADR-070, 12.07'de **yaşandı**). Fly.io kalıcı diski
  `/data` ~**974 MB**; her masaüstü paketi ~**85 MB** → **~11 sürümlük tavan**. Disk dolunca SQLite hiçbir şey
  yazamaz, **login dahil her uç 500** verir (sessiz değil, ölümcül).
  **Önlem (uygulandı):** `ReleaseStore.PruneOld` → yayında en yeni **3 paket** tutulur, eskiler otomatik silinir.
  **Teşhis:** `flyctl ssh console --config fly.toml -C "df -h /data"`.
  **Kalan risk:** paket boyutu büyürse veya sürüm hızı artarsa `KeepCount` düşürülmeli ya da
  `fly volumes extend` ile disk büyütülmeli. Etki: **kritik**.





## 🟢 DÖRDÜNCÜ TURDA **KAPANAN** ESKİ MADDELER (2026-08-26)

- ✅ **MAK-01/b — makine aktivasyon modeli: ÇIKMAZ YOK (ölçüldü, ADR-154).** İki turdur "kullanıcı
  kararı" olarak duruyordu. Bu turda A–G senaryoları izole ortamda gerçek HTTP ile ölçüldü:
  kota doluyken bile yönetici bekleyen gerçek makineyi **onaylayabiliyor** (`ApproveDevice` kotaya
  bakmaz ve cihaz jetonu da üretir), ayrıca sahte kaydı iptal edince makine kendiliğinden açılıyor.
  Yani **iki bağımsız kurtarma yolu** var. Kalan risk yalnız zahmettir. **Karar listesinden çıkarıldı.**

- ✅ **Şube izolasyonu artık üretimde GÖZLEMLENEBİLİR.** Önceki turlarda üretimde **0 şube** vardı ve
  bu bir sınır olarak yazılmıştı. Bu turda salt-okunur kontrolde **9 şube** görüldü; **5'i "ANKARA
  GENEL MERKEZ" altında alt şantiye**. Yani üst/alt şube (ağaç) kod yolu artık CANLI. Tam da bu yüzden
  bu turda **SB-01** bulundu (bkz. ADR-151).

- ✅ **WEB-03 kapandı: teorik uyarı, gerçek hata değil.** İki bağımsız kanıt: (1) tablo satırları
  `Select(e => new Row{…})` ile kurulur → **null eleman üretemez**; (2) gerçek tarayıcıda başlık satırına
  ve hücrelerine tıklandı → olay **tetiklenmedi**, devre sağlam. Derleyici uyarısı (CS8604) MudBlazor'ın
  `Item` alanını nullable ilan etmesinden kaynaklanır. **Kod değiştirilmedi** (gereksiz değişiklik yok).

- ✅ **Sunucu kapalıyken ekranın boş kalması düzeltildi (BAG-01, ADR-153).** Artık anlaşılır bir
  "Sunucuya ulaşılamıyor" şeridi ve "Tekrar Dene" düğmesi görünür. Ağ hatası ile yetki hatası
  ayrıştırılır; oturum düşürülmez.

- ✅ **RPR-15 kapandı (ADR-150).** Yalnız bir "tutarsızlık" değil, **gerçek bir yetki açığıymış**:
  "Rol Yetki Kontrol" ile role KAPATILAN ekranın verisi rapordan okunabiliyordu.

---

## 🟡 DÖRDÜNCÜ TURDA AÇIK BIRAKILAN (2026-08-26)

- **YET-01 — işlevsiz iki yetki anahtarı (ANALİZ TAMAM, KARAR SİZDE).** `btn-logo` kodda **var olmayan**
  bir özelliği korur (logo değiştirme ucu/ekranı yok); `btn-reset-db` yalnız süper adminin geçebildiği
  bir işlemi korur → ikisi de gerçek bir kapıya bağlanamaz. **Silmenin teknik riski ölçüldü:**
  `user_button_permissions` düz metin anahtardır (**FK yok**), `CanGrantButtonKey` listeye bakmaz →
  listeden çıkarmak **migration gerektirmez**, mevcut satırlar yalnız işlevsiz kalır, çökme olmaz.
  Yine de yetki ağacından bir satır kaldırmak **ürün kararıdır**; sizin onayınız olmadan yapılmadı.

- **PostgreSQL dosya yedeği — kapsam dışı (değişmedi).** `pg_dump` sunucu imajında yok; sır + saklama
  alanı + operasyon gerektirir → **yeni özellik**. Bugün sağlayıcının sürekli yedeğine (PITR) dayanır.
  Uygulamanın yedek ekranı PostgreSQL'de anlaşılır mesajla durur (YED-01) — yanlış güven vermez.

- **TEKNİK BORÇ — iki küçük N+1.** `MaterialService` muadil listesi ve `MaterialTemplateService`
  uyumlu-araç doğrulaması kayıt başına döngüde tek satır sorgular. **Darboğaz DEĞİL**: ikisi de TEK
  kaydın küçük alt listesinde çalışır (kart açma/kaydetme), liste ekranlarında değil. Ölçüldü,
  müdahale edilmedi (gereksiz migration/refactor açılmadı).

- **ARC-01 — araç seçicisi firma geneli (ÜRÜN KARARI, karar sizde).** Rapor filtresi (RPR-04) ve araç
  LİSTE ekranı şube kapsamlıdır; ama diğer ekranlardaki **araç seçicileri** (`VehicleService.List`)
  bilinçli olarak firma genelidir ve 12'den fazla yerden çağrılır (içe aktarma servisleri tüm araçlara
  ihtiyaç duyar). **Kanıt iki yöne de çekiyor:** araçlar şubeler arası hareket eden varlıklardır
  (A şubesinde yakıt alan araç B şubesine ait olabilir), dolayısıyla operasyon seçicisinin geniş olması
  savunulabilir. Değiştirmek 12+ çağrı noktasını etkiler → **varsayımla dokunulmadı**.

## 🟡 SON STABİLİZASYON TURUNDA AÇIK BIRAKILAN (2026-08-26, üçüncü tur)

- **YET-01 — işlevsiz iki yetki anahtarı (analiz derinleştirildi, DEĞİŞTİRİLMEDİ).** `btn-logo`
  ("Firma Logosu Değiştir") **var olmayan bir özelliği** koruyor: kodda logo değiştirme ucu da,
  ekranı da YOK (logo statik marka varlığıdır). `btn-reset-db` ("Veritabanı Sıfırlama") ise yapısal
  olarak yalnız süper adminin geçebildiği bir işlemi korur — devredilse bile kullanılamaz. Yani ikisi de
  **gerçek bir kapıya bağlanamaz**. Silmek yetki ağacından düşürür ama verilmiş `user_buttons` satırlarını
  öksüz bırakır; üretimde bu satırların var olup olmadığı **doğrulanamadı** (canlı veritabanına okuma
  erişimi yok). **Karar kullanıcıya bırakıldı.** Etki: düşük (yanıltıcı).

- **MAK-01/b — makine aktivasyon modeli (yeniden analiz edildi, DEĞİŞTİRİLMEDİ).** Bu turda akışın
  tamamı okundu: masaüstü `MachineGate` ucu **giriş ekranından ÖNCE** çağırır ve `pending` durumu
  **girişi tamamen engeller**. Dolayısıyla "önce kimlik doğrula, sonra aktifleştir" modeli bir
  **kilitlenme** yaratır (giriş yapamayan makine aktifleşemez, aktifleşmeyen makine giriş yapamaz).
  Mevcut telafi doğru seviyededir: IP başına hız sınırı + yöneticinin Makine Yönetimi'nden sahte
  makineleri görüp iptal edebilmesi. Model değişikliği **kurulum akışını yeniden tasarlamayı** gerektirir
  → ürün kararı. ⚠️ Veri sızıntısı YOK; mevcut aktif makineler DÜŞMEZ.

- **RPR-15 — rapor yetkisi bazı raporlarda modül yetkisi istemiyor.** Muayene/Sigorta, Personel ve 6 ön
  muhasebe raporu, `reports` yetkisinin YANINDA ilgili ekranın yetkisini de ister (RPR-12). Stok, Yakıt,
  Araç, Bakım, Talep ve şablon raporlarında ise **yalnız `reports` yeterlidir**. Yani "Raporlar" yetkisi
  verilen kullanıcı, Stok ekranına yetkisi olmasa da stok hareketlerini raporda görebilir.
  **Bilinçli mi bilinmiyor.** Sıkılaştırmak, bugün rapor görebilen kullanıcıların erişimini KESERDİ →
  çalışan davranış değiştirilmedi. **Karar kullanıcıya bırakıldı.**

- **PRF-01/c — rapor yanıt boyutu (ölçüm bu turda yenilendi).** Üst sınır `reports.max_rows` ayarıyla
  **50.000**'dir (varsayılan; 1000'in altına inmez). Ölçümler final raporundadır. Sayfalı rapor API'si
  ileride gerekebilir; bu turda mimari değişiklik yapılmadı.

- **WEB-03 — ÜRETİLEMEDİ (değiştirilmedi).** Derleyici üç ekranda (`Finance`, `Invoices`, `Parties`)
  `OnRowClick`'te `e.Item`'ın null olabileceğini uyarıyor (CS8604) ve Blazor'da olay işleyicisindeki bir
  istisna **devreyi düşürür**. Gerçek tarayıcıda tablo başlık satırına ve hücrelerine tıklandı → olay
  **tetiklenmedi**, devre sağlam kaldı, konsolda hata yok. Üretilemediği için **kod değiştirilmedi**
  (varsayımla düzeltme yapılmadı). Not olarak duruyor.

- **Sunucuya ulaşılamadığında ekran boş kalıyor (gözlem, hata değil).** API kapalıyken web **oturumu
  düşürmüyor** ve devre çökmüyor (§13 karşılandı) — ama menü daralıyor ve ekran neredeyse boş kalıyor;
  kullanıcıya "sunucuya ulaşılamıyor" diyen bir uyarı **yok**. Babanızın interneti kesilirse boş bir
  ekranla karşılaşır. Genel bir bağlantı uyarısı eklemek **yeni bir özelliktir** → kullanıcı kararı.

- **Şube izolasyonu üretimde hâlâ GÖZLEMLENEMEDİ.** Üretimde hiç şube tanımlı değil (0 şube). Kural izole
  ortamda kanıtlandı (ADR-142 matrisi + bu turda eklenen yazma süpürmesi). ⚠️ Bu turda tam da bu yüzden
  gizli kalmış bir hata bulundu (**PRS-01**, ADR-146): şube kapsamı sayfalamadan sonra uygulanıyordu.
  Şube tanımlandığında benzer "0 şubede görünmeyen" hatalar çıkabilir.

## 🟡 FİNAL AUDIT TURUNDA AÇIK BIRAKILAN (2026-08-26, ikinci tur)

- **MAK-01/b — makine aktivasyon modeli.** Anonim `/api/machines/register` (giriş ekranından ÖNCE
  çağrıldığı için anonim kalmak ZORUNDA) yeni makineyi kota dolana kadar kendiliğinden `active` yapar.
  Anonim bir çağıran bu yolla firmanın makine kotasını tüketebilir ve **yeni/yeniden kurulan** gerçek
  makine `pending` kalıp senkron yapamaz. ⚠️ Mevcut aktif makineler DÜŞMEZ ve **veri sızıntısı YOKTUR**
  (kayıt bir cihaz jetonu vermez). Bu turda IP başına hız sınırı kondu (ADR-140). **Modeli değiştirmek**
  — yeni makinenin ancak kimlik doğrulanmış girişten sonra aktifleşmesi — kurulum akışını etkiler →
  **kullanıcı kararı**. Bugünkü telafi: yönetici sahte makineleri Makine Yönetimi'nden görür ve iptal eder.

- **YET-01 — işlevsiz iki yetki anahtarı.** `btn-reset-db` (Veritabanı Sıfırlama) ve `btn-logo`
  (Firma Logosu Değiştir) yetki ağacında görünür ama kodda **hiçbir yerde kapı değildir**; yönetici
  yetki verdiğini sanır, hiçbir şey değişmez. Anahtarları silmek verilmiş kayıtları öksüz bırakacağı
  için dokunulmadı; testte bilinçli istisna olarak listelendi. **Etki:** düşük (yanıltıcı).

- **YET-02/b — arayüzde iptal butonu kapısı tutarsız.** Yetki artık verilebilir (ADR-141), ama arayüz
  bunu tutarlı uygulamıyor: masaüstü Yakıt ekranı butonu gizliyor, masaüstü Stok ekranı ve web
  gizlemiyor → yetkisi olmayan kullanıcı butonu görüp hata alıyor. **Güvenlik açığı DEĞİL** (sunucu
  fail-closed). **Etki:** düşük (kullanıcı deneyimi).

- **PRF-01/c — rapor yanıt boyutu.** 50.000 satırda API yanıtı ~**6 MB** (sorgu 275 ms + serileştirme
  95 ms). Tarayıcı çizimi ADR-135 ile çözüldü; kalan yük **aktarım**dır. Sayfalı API ileride gerekebilir.

- **Şube izolasyonu üretimde GÖZLEMLENEMEDİ.** Üretimde hiç şube tanımlı değil (0 şube); kural izole
  ortamda 17 senaryoyla kanıtlandı (ADR-142). Şube tanımlandığında davranış testlerin gösterdiği gibi
  olacaktır, ama bu **canlı veriyle doğrulanmış değildir**.

## 🟡 Bu turda AÇIK BIRAKILAN — kullanıcı kararı gerekiyor (2026-08-26)

- **YED-01/b — PostgreSQL için DOSYA YEDEĞİ yok.** Sunucu yedekleme kodu SQLite'a özgüdür
  (`VACUUM INTO` + `PRAGMA integrity_check`). Üretim 2026-07-24'te PostgreSQL'e geçtiği için bu düğme
  çalışmıyordu; artık **anlaşılır bir mesajla ve hiçbir dosyaya dokunmadan** duruyor (ADR-136) —
  yani yanlış güven vermiyor. **Gerçek** bir dosya dökümü `pg_dump` ister; o araç sunucu konteynerinde
  yoktur ve uygulama içinde bir dökümcü yazmak **yeni bir özelliktir**.
  **Bugünkü koruma:** veritabanı sağlayıcısının sürekli yedeği (PITR). **Etki:** orta —
  "kendi elimde dosya olarak yedek" isteniyorsa ayrı bir iş olarak planlanmalıdır.

- **PRF-01/b — rapor tavanı 50.000 satır.** Ekrana çizilen satır artık sınırlı (ADR-135) ve sunucu
  sorgusu 50.000 satırda 287 ms. Kalan risk: sunucudan web'e taşınan veri hâlâ tüm sonuçtur.
  Sayfalı API (server-side pagination) ileride gerekebilir. **Etki:** düşük (bugün ölçülen değerlerle).

- **Satın Alma kategorisi boş.** `ReportCategory.Purchasing` etiketi vardır ama kodda **satın alma
  domaini yoktur** (yalnız talep durumu olarak "Satın Alma Sürecinde" geçer). Sahte ekran/rapor
  **üretilmedi**. Bu bir hata değil, **karar bekleyen bir özelliktir**.

- **TNT-04 — anonim uçlar firma/şube ADLARINI açar.** Giriş ekranı kullanıcıya firmasını ve şubesini
  seçtirmek zorunda olduğu için üç anonim uç vardır; hız sınırlıdır ve **veri döndürmez**. Ürün gereğidir.

## Çözüldü (12.07.2026)

- **Süper admin kilitlenmesi** (ADR-064): firma silme, süper admin dahil tüm kullanıcıları pasife alıyordu →
  süper admin kendi firmasını silince sistemden tamamen kilitleniyordu. Firma silme artık süper admini hariç
  tutar + sunucu açılışında **self-heal**. Regresyon testi var.
- **Firma silince 401 + firmalar yüklenmiyor** (ADR-068): süper admin içinde çalıştığı firmayı silince
  token'daki firma geçersiz kalıyordu → her istek 401. Artık home firmaya düşer (sahte firma id'de fail-closed).
- **Silinen şubeler her yerde listeleniyordu** (ADR-066): masaüstü yerel kopyası sunucudan yalnız upsert
  ediliyordu. Artık her girişte **aynalanır**.
- **Masaüstü firma ekle/sil web'e ulaşmıyordu** (ADR-071/072): firmalar iş senkronunda yoktu ve yalnız yerele
  yazılıyordu. Artık sunucu-otoriteli + **offline kuyruk** (idempotent, sıralı).
- **Webte silinen kayıt makinede kalıyordu** (ADR-069): LWW silmeyi eziyordu; ayrıca cihaz push'u sunucudaki
  silmeyi diriltiyordu. İkisi de kapatıldı.

## Açık

### ✅ SEC-03 — KAPANDI (2026-08-25, ADR-125): geliştirici modu yalnız süper adminde
Kaynak kodda sabit yazan geliştirici kodu, *Ayarlar › Geliştirici Modu* ekranını açabilen **herhangi bir
kullanıcıya** süper admin yetkisi veriyordu (depo herkese açık → kod da açık). Kapı artık tek otoritede:
`DeveloperMode.CanActivate/TryActivate` → **ham süper admin rolü**. `AccessControl.IsAdmin` bilinçli
KULLANILMADI (o, modun kendisini sayar → döngüsel yetki). Etkinleştirme · masaüstü gezinme · masaüstü menü ·
web sayfası · web menüsü · sunucu ucu — **hepsi** kapatıldı. 12 test; düzeltme geri alınınca 9'u kırılıyor.

> ℹ️ Kod hâlâ kaynakta sabittir ve depo herkese açıktır. Artık **tek başına yeterli değildir** (süper admin
> olmak da gerekir), ama kodu depodan çıkarmak isterseniz bu ayrı bir iştir.

### 🟠 PRF-01 (2026-08-25, ÖLÇÜLDÜ) — Stok Hareketleri raporu tek seferde 50.000 satıra kadar dönebilir
`ReportLimits.DefaultMaxRows = 50_000`. Ölçüm (3.000 malzeme · **20.000 hareket** · 8 şube, SQLite):
rapor **20.000 satırın tamamını** döndürüyor ve **125 ms** sürüyor. Ham SQL yalnız **6 ms** — yani maliyet
sorguda değil, satırların oluşturulup arayüze taşınmasında.

**Denenen ve ELENEN çözüm:** `stock_movements(company_id, created_at)` indeksi eklenip ölçüldü → sorgu
planı `SCAN` yerine `SEARCH`e döndü **ama rapor süresi değişmedi** (125 → 123 ms). Yani "eksik indeks"
bu raporun darboğazı DEĞİLDİR. Ölçüm yapılmasaydı gereksiz bir migration açılacaktı.

**Bugünkü risk düşük:** üretimde 663 hareket var ve ekran ucu (`/api/stock/movements`) zaten **1000
satırla** sınırlı. Risk yalnız RAPOR yolundadır ve hareket sayısıyla birlikte büyür.
**İzleme eşiği:** hareket sayısı ~20.000'i geçtiğinde rapora sayfalama / SQL tavanı eklenmelidir.

**🟢 2026-08-25 güncellemesi — RPR-07 bu riski en sık karşılaşılan hâlinde ÇÖZDÜ.** 30.000 hareketli
ölçümde depo personelinin raporu **196 ms → 28 ms**, dönen satır **30.000 → 3.000**. Çünkü Operasyon
Raporları artık yalnız çalışma şubesini kapsıyor. Kalan risk: **yönetici** raporlarında (tüm şubeler)
hareket sayısı çok büyürse. Tavan hâlâ 50.000 satırdır.

### 🟡 UPD-01 (2026-08-25) — Güncelleme checksum kontrolü boş değerde atlanıyor
`UpdateInstaller.InstallAndRestart`: `if (!string.IsNullOrWhiteSpace(expectedSha) && !VerifyChecksum(...))`
→ checksum BOŞ gelirse doğrulama **hiç yapılmaz**. Bugün ulaşılabilir değil: sunucu `ReleaseService.Publish`
içinde 64 haneli hex checksum'ı **zorunlu** tutuyor. Fail-closed'a çevirmek tek satır, ama eski bir
`app_releases` satırının checksum'ı boşsa güncelleme **durur** → çalışan bir yolu bozma riski var.
Bu yüzden değiştirilmedi; önce canlıdaki `app_releases` satırlarının checksum'ı kontrol edilmeli.

### 🔵 RPR-08 (2026-08-25, KULLANICI KARARI) — eksik olabilecek raporlar
Katalogda **19 rapor** var. Şu üç konuda rapor **yoktur** ve bu tur kapsamına ALINMADI (yeni özellik;
kolon/filtre kararı kullanıcıya aittir):
- **Muayene / Sigorta raporu** — modülün ekranı ve uyarıları var, raporu yok.
- **Personel raporu** — ekranı ve Excel dışa aktarımı var, raporu yok.
- **`Purchasing` (Satın Alma) kategorisi** — `ReportCategory` enum'unda tanımlı ama **hiçbir rapor
  kullanmıyor**; boş bir kategori olarak duruyor.

Bunların "menü/route/isim sorunu" OLMADIĞI doğrulandı: katalogda kaydı yok, başka bir ekran aynı işlevi
görmüyor. Eklenmeleri = katalog satırı + `ReportService` metodu + testler.

### 🔵 TNT-04 (2026-08-25, bilgi) — Anonim uçlar firma ve şube ADLARINI açar
`/api/public/companies` ve `/api/public/branches` kimlik doğrulamasız çalışır (masaüstü giriş ekranı
listeleri buradan doldurur) ve hız sınırı (`publicLimiter`, 120/dk/IP) dışında koruma yoktur. Veri
sızıntısı **firma/şube ADIYLA sınırlıdır** — iş verisi dönmez. Girişten önce listeyi göstermek ürün
gereği olduğu için değiştirilmedi; kayıt olarak duruyor.
- **R34 (12.08.2026) — ✅ KÖK NEDEN BULUNDU VE DÜZELTİLDİ (kapatıldı):** tam test takımında ara sıra
  `SyncBalancePayloadTests.Yalniz_Bakiye_Degisirse_Sunucu_Etkilenmez_Yerel_Calismaya_Devam_Eder`
  kırılıyordu. Neden **üretim kodu değil, TESTİN KENDİSİYDİ**: `Assert.DoesNotContain("777", Snapshot())`
  senkron paketinin TAMAMINDA ham `"777"` metnini arıyordu; pakette rastgele üretilen GUID'lerden biri
  `777` dizisini içerdiğinde test sebepsiz kırılıyordu (yakalanan örnek: `…0077788757fd6`).
  **Düzeltme gevşetme DEĞİL, keskinleştirme:** artık paketin `tables` bölümünde `stock_balances`
  tablosunun HİÇ olmadığı (asıl sözleşme) + `"quantity":"777"` alanının bulunmadığı doğrulanıyor.
  Retry/skip **kullanılmadı** (proje kuralı). Bu kırılganlık STK-10b-4 ile ilgisizdi, önceden vardı.
- **R33 (YENİ 12.08.2026, `RPR-02`):** **Web'de rapor isteği, giriş ekranında seçilen ŞUBEYİ taşımıyor.**
  JWT yalnız kullanıcı+firma bilgisini taşır; `AuthService.CreateSessionForUser` oturuma
  `OperatingBranchId` **atamaz** (tek istisna: içe-aktarma ucu, formdan `branchId` alır). Sonuç:
  `ReportScope.Effective` → `BranchScope.Active(s)` **null** döner ve web raporları **firma geneli**
  çalışır; şube daralması yalnız kullanıcı **açıkça** şube seçtiğinde (`branchIds`) olur.
  **Masaüstü etkilenmiyor** — orada oturum şubesi gerçekten dolu ve daraltma testli.
  **Etki:** orta — bu bir tenant (firma) sızıntısı DEĞİL; firma içi şube görünürlüğü beklenenden geniş.
  Tüm raporları etkileyen **mevcut** mimari; STK-10a/10b artımları getirmedi. STK-10b-3'te tespit
  edildi ve kasten düzeltilmedi (kapsam dışı). Kayıt: `STK_10_HAREKET_RAPORU_PLANI.md` §23.5.
- **R5:** Web ve masaüstü health şu an DB'ye fiilen bağlanmıyor (web config-kontrolü, masaüstü yerel SQLite write/read). Gerçek PostgreSQL bağlantı health'i Faz 02'de eklenecek. Etki: düşük.
- **R6:** `dotnet test` çıktısında MSBuild "MSB4011 Directory.Build.props ikinci kez içe aktarıldı" benzeri bilgi mesajı görülebilir; build/test sonucunu etkilemiyor. Etki: kozmetik.
- **R2:** Üretim hosting, object storage, e-posta ve code-signing sağlayıcıları maliyet değerlendirmesi yapılmadan seçilmeyecek. Etki: yayın (Faz 15-17) öncesi.
- **R3:** Otomatik döviz kuru kaynağı kesinleşmedi; manuel kur + tarihçe güvenli fallback olarak tasarlanacak. Etki: para/maliyet modülleri (Faz 06+).
- **R4:** (Güncellendi 09.07.2026, ADR-057) Gerçek/canlı sunucu (`depowise-erp.fly.dev`) **SQLite** kullanıyor (`depowise-server.db`, Fly.io kalıcı disk). PostgreSQL'e hiç geçilmedi; `apps/web/drizzle` altında üretilmiş migration SQL'i **kullanılmıyor/donmuş**. PostgreSQL'e geçiş artık aktif bir plan değil, kullanıcı karar verirse ele alınacak bir gelecek seçenek. Etki: düşük (mevcut SQLite tek-dosya/tek-disk mimarisi çok şirketli kullanım için şimdilik yeterli; çok yüksek eşzamanlı yazma/ölçek ihtiyacı doğarsa yeniden değerlendirilir).
- **R7:** (Güncellendi 09.07.2026) PostgreSQL üretime hiç alınmadığı için "PG ↔ SQLite şema eşitliği" konusu şu an geçerli değil — tek gerçek şema SQLite (`MigrationRunner`/`IMigration`). `apps/web/drizzle` donmuş, aktif bakımı yok. Etki: düşük (drift riski yok, çünkü ikinci bir canlı şema yok).
- **R23:** `npm audit`: 9 advisory (1 high @eslint/plugin-kit, moderate esbuild/drizzle-kit, postcss/next) — tümü **dev/build araçları**, üretim runtime'ında yok. `npm audit fix --force` breaking (next downgrade) olduğu için uygulanmadı; lock dosyası commit'li, periyodik izlenecek. Etki: düşük (runtime maruziyeti yok).
- **R22:** Code-signing (imzalı dağıtım) henüz yapılmadı; maliyetli kalem, yayın öncesi karara bırakıldı. İmzasız sürümde updater kullanıcıya şeffaf uyarı verir (signedWarning). Etki: orta (yayın öncesi).
- **R21:** UpdateService dosya tabanlı kurulum/rollback mantığı + testleri hazır; gerçek HTTP indirme transport, masaüstü güncelleme UI ekranı (yüzde göstergesi) ve canlı uygulama dosyalarının değişimi henüz bağlanmadı. Etki: orta.
- **R20:** SyncServer push'ta `accepted` işlemler şu an `sync_inbox` + `server_changes` feed'ine yazılıyor; gerçek iş tablolarına apply (upsert) iş-servisleriyle bağlanacak. Idempotency/doğrulama/conflict çekirdeği hazır. Etki: orta.
- **R19:** Sync HTTP transport katmanı (push/pull endpoint'leri), DPAPI `ISecretProtector` gerçek implementasyonu, retry/backoff ve 0-100 non-blocking ilerleme UI henüz yok (servis mantığı + testler hazır). Etki: orta.
- **R17:** İçe aktarım şu an yalnız malzeme seti (dry-run+commit). Araç/diğer setler aynı desenle (`ImportRow`/dry-run) eklenecek. Ayrıca commit'te mevcut kod "updated" sayılıyor ama alanlar güncellenmiyor (idempotent no-op); gerçek güncelleme akışı sonra. Etki: orta.
- **R16:** Talep PDF binary üretimi şu an yalnız .NET (QuestPDF). Web tarafı aynı `RequestPdfModel`'i kullanıyor ama binary render hattı (ör. server-side PDF lib) henüz eklenmedi. Etki: düşük (web PDF sonraki bir adımda).
- **R15:** Günlük faaliyet bakımında `MaintenanceService.Save` ve `daily_activities` insert ayrı transaction'larda (MaintenanceService kendi tx'ini commit eder). Her ikisi de idempotent → retry ile tutarlı; nadir partial-fail penceresinde bakım kaydı oluşup faaliyet referansı eksik kalabilir (retry düzeltir). İleride tek tx'e alınabilir. Etki: düşük.
- **R14:** `MaintenanceService.GetAlerts` GROUP BY + MAX(created_at) ile en-son bakımı seçerken SQLite bare-column davranışına dayanıyor; aynı created_at'te tie belirsiz olabilir (testlerde saat ilerletilerek garanti). İleride pencere fonksiyonu/alt sorgu ile sağlamlaştırılabilir. Etki: düşük.
- **R13:** Stok bakiyesi material-global (şube bazlı değil); transfer net-zero. Şube bazlı bakiye + şube negatif kontrolü sonraki fazda. Etki: orta (çok şubeli stok ayrımı henüz yok).
- **R11:** `material_compatible_vehicles.vehicle_id` şu an FK'siz serbest metin (vehicles tablosu Faz 08). Faz 08'de FK + referans bütünlüğü eklenecek. Etki: düşük (geçici).
- **~~R10~~ (KAPANDI 11.07.2026):** Operasyonel + yönetim modül ekranları BAĞLANDI. Masaüstü: her menü anahtarı gerçek bir ViewModel'e yönleniyor (ShellViewModel switch tamam; PlaceholderViewModel yalnız tanımsız anahtar için fallback). Web: 34 Blazor sayfası. GUI'nin gerçek kullanımda test edilmesi kullanıcıya kaldı (birim/entegrasyon iş mantığı testlerle kapalı).
- **R9:** Masaüstü shell şu an **preview admin oturumu** ile menüyü gösteriyor (login akışı Faz 05). Yetki mantığı testlerle doğrulandı; gerçek oturum + firma override tema Faz 05'te bağlanacak. Etki: orta (UI önizleme).
- **R8:** Web `getServerSession` henüz oturum çözmüyor (imzalı cookie + DB session lookup Faz 05'e bırakıldı); şu an fail-closed null döner → `/api/v1/me` daima 401. Davranış güvenli; işlevsel oturum web tarafında Faz 05'te bağlanacak. Etki: orta.

## Kapatılan
- **R18:** Foto optimizasyonu yapıldı — `ImageOptimizer` (SkiaSharp, ücretsiz; ImageSharp lisans maliyeti yerine): en uzun kenar >1600px küçültme + JPEG Q82; çözülemezse orijinal (graceful). Fly Linux native asset doğrulandı. Test: `ImageOptimizerTests`.
- **R12:** LIKE araması artık Türkçe duyarsız — `SqliteConnectionFactory` `like()`'ı `SqlLikeTr` ile override eder (İ/ı/ş/ç/ğ/ü/ö). Tüm sorgular otomatik faydalanır. Test: `TurkishLikeTests`.
- Büyük tek prompt yerine faz bazlı çalışma paketi oluşturuldu.
- Proje adı ve dosyalar DepoWise olarak standartlaştırıldı.
- CLAUDE.md ↔ V6 analiz çelişki taraması yapıldı; çelişki yok (Faz 00).
- COMODO güvenli çalıştırma zinciri (hook + UseAppHost=false + mutlak DB yolu) doğrulandı (Faz 00).
- R1 (kaynak kod yoktu): Faz 01'de çözüm iskeleti kuruldu, baseline build+test+web build yeşil.
- `next` CVE-2025-66478: 15.5.19 yamalı sürüme yükseltilerek kapatıldı (Faz 01).

## 05.07.2026 — Açık kalan bilinen sorunlar (canlı test + inceleme)
- Sync üretim yolu LWW'li tek yönlü snapshot (`business-push`); operation-id'li `/sync/push` masaüstünce kullanılmıyor. `stock_balances` LWW satırı olarak taşınıyor; iş verisi pull edilmiyor (2. makine senaryosunda veri ezilir/görünmez). Çok makineli kullanım öncesi çözülmeli.
- (ÇÖZÜLDÜ 05.07.2026 ADR-053) business-push artık modül-bazlı yetki + negatif değer doğrulaması yapıyor.
- (ÇÖZÜLDÜ 05.07.2026 ADR-054) JWT refresh eklendi; kayan oturum + SessionExpired sinyali.
- (ÇÖZÜLDÜ 05.07.2026 ADR-055) Updater artık yedekliyor + başarısızlıkta rollback yapıyor + bütünlük guard./gerçek PS yolu Windows entegrasyon testi bekliyor.
- (ÇÖZÜLDÜ 05.07.2026) Çöp Kutusu web API'si eklendi: `POST /api/trash` + `/api/trash/restore` (parola ile yeniden doğrulama), web `Trash.razor` (/trash). `soon/about` hâlâ placeholder.
- (ÇÖZÜLDÜ 05.07.2026) Server-status bellek grafiği min-max normalize edildi (artık hep %100 değil).
- (ÇÖZÜLDÜ 05.07.2026) SessionExpired UI'ya bağlandı: masaüstü oturum düşünce dialog + tekrar giriş (`ShellViewModel.OnSessionExpired`).
- Sunucuda ILogger yok; ~40 boş catch bloğu gözlemlenebilirliği düşürüyor (500 loglaması eklendi, gerisi açık). *(orta öncelik — launch için kabul edilebilir)*
- Güvenlik sertleştirme adayları (bu turda dokunulmadı, ayrı inceleme): CORS AllowAnyOrigin (Blazor Server side-call olduğundan tarayıcıdan kullanılmıyor, düşük risk), `/api/machines/register` anonim, `serverurl.txt` düz metin, 1 GB gövde limiti.

## 2026-08-13 — Masaüstü GUI doğrulama turunda AÇILAN konular

**Kapatılanlar (bu turda düzeltildi, regresyon testi eklendi):** GUI-01 masaüstünde şube kapsamının hiç
uygulanmaması · GUI-02 elle cari hareketinin şubesiz yazılması · GUI-02b ters kaydın şubesiz + kapsam
kontrolsüz olması · GUI-03 "tüm yetkili şubeler" etiketi ile verinin çelişmesi · GUI-04 rapor şube
filtresinde yetkisiz şubenin listelenmesi · GUI-05 "Şube Kapsamı" bölümünün sessizce kaybolması.
Ayrıntı: [`docs/tests/Sube_Kapsami_GUI_Test_Report.md`](tests/Sube_Kapsami_GUI_Test_Report.md).

**AÇIK — veri geçişi kararı (kullanıcıya sorulacak):** GUI-02 düzeltmesi yalnız bundan sonra girilecek
hareketleri şubeye bağlar. Canlıda daha önce elle girilmiş cari hareketler `branch_id = NULL` olabilir;
şubesiz satır tasarım gereği HER şubede görünür. Yayın öncesi canlı veride şubesiz hareket sayılmalı,
varsa toplu şube ataması kullanıcı onayıyla yapılmalıdır. **Bu tur canlı veriye bakmadı.**

**AÇIK — masaüstü GUI'de koşturulamayan 3 madde:** negatif stok kapısı (izole ortamda malzeme kurulmadı) ·
idempotency ikinci gönderim (kayıttan sonra form kapanıyor) · senkron şube izolasyonu (iki makine gerekir).
Üçü de otomatik testlerle örtülüdür ama **GUI kanıtı yoktur**.

**AÇIK — masaüstü Yetkiler ekranı ile yerel veritabanı ilişkisi:** kullanıcı listesi ve yetkiler
sunucudan gelir; masaüstünün yerel veritabanında ise yalnız o makinede giriş yapmış kullanıcılar bulunur.
GUI-05 ile kapsam okuma/yazma sunucuya taşındı, ama **çevrimdışıyken** web'de oluşturulmuş bir kullanıcının
kapsamı hâlâ yerelden okunamaz (panelde sebep yazar). Kalıcı çözüm kullanıcı aynalaması olurdu — mimari
karar gerektirir, bu turda yapılmadı.

## 2026-08-18 — ŞUBE / SIFIRLAMA / YETKİ TURUNDA KAPATILANLAR

Aşağıdakiler bu turda **düzeltildi**; kayıt olarak duruyor (tekrar ederse aynı yerlere bakılır).
Ayrıntı: [`docs/ANALIZ_SUBE_VE_SIFIRLAMA.md`](ANALIZ_SUBE_VE_SIFIRLAMA.md)

- **SIF-01 (kritik, kapatıldı)** — masaüstü, sunucudan gelen "yerelini sıfırla" isteğini uygularken
  ADR-083'ün TAM SİLME fonksiyonunu çağırıyordu → yerel `users` satırı silindiği için o makinede
  **çevrimdışı giriş imkânsız** hâle geliyordu. Çağrı yeri artık kaynak düzeyinde testle kilitli
  (`BusinessResetCoverageTests.LoginEkraniDogruFonksiyonuCagirir`).
- **SIF-03 (kapatıldı)** — silme kapsamı senkron sözleşmesinden okunuyordu; ortak liste
  `BusinessDataExtras` ile ayrıldı.
- **ŞB-01 (kapatıldı)** — şube aynası `kind`/`parent_id` taşımıyordu.
- **ŞB-04 (davranış değişikliği)** — üst şube artık **işlevsel**: kapsam alt şubelere yayılır,
  rapor üst şube seçilince altları toplar. ⚠️ Bu bir **yetki genişlemesidir**: üst şubeye yetkili
  kullanıcı artık alt şubelere de **yazabilir** ve alt şubeleri **devredebilir**. Ağacı yöneten
  admindir; mevcut kullanıcı kapsamları gözden geçirilmelidir.
- **İçe aktarım kapsam açığı (kapatıldı)** — içe aktarım oturum kopyası şube kapsamını taşımıyordu →
  kapsam dışı şubeye kayıt basılabiliyordu (web + masaüstü).

### ⚠️ Bu turda AÇIK KALAN
- **✅ SIF-02 — KAPANDI (2026-08-25, ADR-124).** Yerel sıfırlama kontrolü artık periyodik eşitleme
  turunda da çalışır: tur, **gönderimden ÖNCE** sunucuda bekleyen istek var mı diye sorar; varsa durur,
  kullanıcıya ne olduğunu anlatır ve oturumu güvenle kapatır (sıfırlama yine tek yerde — giriş akışında
  — uygulanır). Çevrimdışıyken bayrak açılmaz → internet kesikken uygulama kendini kilitlemez.
  Üç testle kilitli (SIF-02a/b/c). Operasyonel önlem ("önce programları kapatın") artık zorunlu değildir.

## 2026-08-18 — MENÜ / EKRAN YÖNETİMİ TURUNDA KAPATILANLAR

- **MNU-B1 — "Masaüstü" kutusu gerçek masaüstü makinelerde ETKİSİZDİ.** (ADR-110) 🔴
  G5 ile 2026-08-12'de eklenen ekran platform ayarı sunucu veritabanına yazılıyor ama masaüstüne
  **hiçbir yoldan inmiyordu** (`screen_platform_visibility` ne `BusinessSyncService.Tables` listesinde
  ne de `/api/lookups/sync` yanıtındaydı; masaüstü ayarı kendi yerel SQLite'ından okuyor). Sonuç:
  yönetici bir ekranı masaüstünde kapattığını sanıyordu, ekran açık kalmaya devam ediyordu.
  **Çözüm:** tanım (lookup) senkronuna üç bölüm eklendi (platform + menü düzeni). Yazma **replace**
  semantiğiyle (kaldırılan ayar yerelde de düşsün). Çevrimdışı davranış korundu.

- **MNU-B2 — süper admin kendini KALICI olarak kilitleyebiliyordu.** (ADR-111) 🔴
  Yönetim ekranı web'de kapatılabiliyordu; kapatıldığı anda menüden düşüyor, route koruması adresi elle
  yazmayı da engelliyor ve masaüstü karşılığı olmadığı için **geri alacak arayüz kalmıyordu**.
  **Çözüm:** `AppScreens.Protected` (`screen_visibility`, `users`, `permissions`) — tüm platformlarda
  birden kapatılamaz. Tek platformda kapatma serbest (kurtarma yolu kalır).

- **Tek platformlu 14 ekran yönetim listesinden sessizce düşüyordu.**
  Birleşik bayrak maskesi `HasFlag(Desktop|Web)` "ikisinde de olan" anlamına geldiği için yalnız web ya
  da yalnız masaüstünde bulunan ekranlar (Kota İzleme, Malzeme Şablonları, Yedek Yönetimi…) listede
  görünmüyordu. Maske testi `(& != 0)` olarak düzeltildi; regresyon testi eklendi.

- **Arayüz: taşınan satırdan sonra yanlış grup görünüyordu.** Blazor öğeleri konuma göre yeniden
  kullandığı için kullanıcının elle değiştirdiği `<select>` değeri bir alttaki satıra yapışıyordu.
  Satırlara `@key` eklendi.

- **Arayüz: reddedilen değişiklikten sonra onay kutusu yanlış durumda kalıyordu.** Kullanıcı ekranı
  kapattığını sanabilirdi. Her yeniden yüklemede satırların yeniden oluşmasını sağlayan sürüm anahtarı
  eklendi.

### ⚠️ Bu turda AÇIK KALAN

- **Ekran adı değişikliği yalnız MENÜ etiketini değiştirir.** Ekranın kendi sayfa başlığı (ör.
  "Yakıt — Yakıt Dağıtımları") ilgili bileşenin içindedir ve değişmez. Tasarım gereğidir; istenirse
  sayfa başlıklarını da katalogdan besleyen ayrı bir iş açılabilir.
- **Yetkisiz kullanıcıyla GUI testi yapılmadı** (ayrı personel hesabı oluşturulmadı). Sunucu tarafı
  otomatik testler ve tokensiz `curl` ile doğrulandı (üç uç da 401) — yani güvenlik kanıtlanmıştır,
  eksik olan yalnız arayüz üzerinden tekrar gösterimidir.
- **1280px genişlikte İŞLEM (sıra taşıma) kolonu yatay kaydırma gerektiriyor.** Tablo 10 kolonlu ve
  `overflow-x:auto` ile korunuyor; dar ekranda kullanıcı sağa kaydırmalı.
