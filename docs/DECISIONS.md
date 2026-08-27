# DECISIONS

## ADR-000 - V6 başlangıç kararları
- Web: Next.js + TypeScript strict + Drizzle + PostgreSQL.
- Masaüstü: .NET 8 + Avalonia + MVVM + Dapper + SQLite.
- Web çevrimiçi merkez; masaüstü offline-first.
- Stok hareket defteri ana kaynak; kritik operasyonlarda LWW kullanılmaz.
- Fotoğraf için file_records + storage provider; DB base64 varsayılan değildir.
- Geliştirme makinesinde dotnet host ve mutlak LocalAppData DB yolu zorunludur.

Fazlar ilerledikçe yeni kararlar tarih, bağlam, karar, alternatifler ve sonuç formatında eklenir.

---

### ADR-108 — "Açık-verilir" yetki katmanı + Yerel Veri Sıfırlama modülü (18.08.2026)
- **Bağlam:** Kullanıcı, makinelerin yerel verisini sıfırlama isteğinin **yetki ağacında bir menü maddesi**
  olmasını ve **Süper Admin veya Kısıtlı Süper Admin** verdiğinde **alan kişinin de alt rollerine
  verebilmesini** istedi. Mevcut modelde bu ifade edilemiyordu: yalnız iki uç vardı — `IsSuperAdminOnly`
  (hiç devredilemez) veya normal modül (firma adminine **admin bypass** ile örtük açık). Ayrıca düğme
  `Companies.razor` (süper-admin-only ekran) içine gömülüydü ve `CompanyLocalResetService.RequestReset`
  sert biçimde `IsSuperAdmin` istiyordu → yetki verilse bile kişi düğmeye ULAŞAMAZDI.
- **Karar:** Üçüncü katman eklendi — **`AppModules.IsExplicitOnly`**: *devredilebilir ama asla örtük
  verilmeyen*. Bu modüllerde (1) `AccessControl.Can` içindeki admin bypass GEÇERSİZ, (2) devretme yetkisi
  **Süper Admin | Kısıtlı Süper Admin | yetkiyi açıkça almış olan**, (3) "ilk admin her şeyi verebilir"
  kestirmesi UYGULANMAZ. İlk üyesi `local_reset` — kendi web ekranıyla (`/local-reset`).
  Sunucu kapısı `IsSuperAdmin` yerine modül yetkisine bağlandı; hedef firma `TenantAccessGuard` ile
  oturumdan çözülür (süper admin dışında kimse başka firmanın makinelerini sıfırlayamaz).
- **Alternatif (elendi):** Özel buton (`SpecialButtons`) yapmak. Elendi çünkü (a) özel butonlarda da admin
  bypass var, (b) `RoleGrantService` matrisi yalnız MODÜLLERİ kapsıyor → rol bazlı yasak konulamazdı,
  (c) düğmenin bulunduğu ekran yine devredilemez kalırdı.
- **Sonuç:** Zincir kullanıcının istediği gibi: SA/KSA → Admin → Personel; her kademe yalnız kendisinde
  olanı verir. Modül `Rol Yetki Kontrol` matrisine normal modül gibi girer. 12 test.
- **Kapsam dışı:** Ekran YALNIZ web'dedir (kardeşleri Kalıcı Silme / Firma İş Verisini Sıfırla gibi);
  masaüstünde karşılığı yoktur. Yetki ağacında ise iki platformda da görünür ve verilebilir.

### ADR-107 — Üst şube işlevsel hâle getirildi (18.08.2026)
- **Bağlam:** `branches.parent_id` ilk günden (Migration001) beri şemada vardı ama kod tabanında YALNIZ
  saklanıp gösteriliyordu: `BranchAccess`, raporlar ve hiçbir filtre onu okumuyordu. "Üst Şube" alanı
  fiilen bir etiketti — Merkez'e yetkili kullanıcı altındaki şantiyeleri göremiyor, Merkez seçilince
  rapor altları toplamıyordu. Kullanıcı bunun çalışmasını istedi.
- **Karar:** `BranchTree.LoadDescendants` firma başına geçişli kapanış (üst şube → tüm alt şubeleri)
  üretir; oturum kurulurken bir kez yüklenir (masaüstü Login + web/API snapshot). `BranchAccess.Expand`
  hem İZİNLİ hem İSTENEN şube kümesini genişletir. İkinci bir kapsam mantığı KURULMADI — tek otorite
  yine `BranchAccess`.
- **İki kural korundu:** **fail-closed** (genişletme izinli kümeyi aşamaz; yukarı/kardeşe genişleme yok)
  ve **fail-safe** (ağaç yüklenmemişse davranış ADR-107 öncesiyle birebir aynı).
- **Sonuç / KABUL EDİLEN GENİŞLEME:** Üst şubeye yetkili kullanıcı artık alt şubelere **yazabilir** ve
  alt şubeleri **devredebilir**. Bu hiyerarşinin kasıtlı anlamıdır; ağacı yöneten admindir. Canlıdaki
  mevcut kapsamlar gözden geçirilmelidir (`YTK-07` backlog maddesi).
- **Kapsam dışı:** Ekranlar hâlâ düz liste gösteriyor (ağaç görünümü `ŞB-07` olarak backlog'a alındı).

### ADR-106 — Sıfırlama kapsamı senkron sözleşmesinden AYRILDI (18.08.2026)
- **Bağlam:** Hem sunucudaki "Firma İş Verisini Sıfırla" hem masaüstündeki yerel temizlik, silinecek
  tabloları `BusinessSyncService.Tables` listesinden okuyordu. O liste **senkron sözleşmesidir**
  (taşınacak tablolar), silinecekler değil. Farkta kalan tablolar temizlikte atlanıyordu.
- **Karar:** Silme kapsamı ayrı bir katmana alındı — `BusinessDataExtras`: (1) `company_id` taşıyan ama
  senkronda taşınmayan 7 tablo (bakiye, muayene, sayaç, stok log, dosya, iki şablon), (2) `company_id`
  taşımayan 8 satır/bağlantı tablosu. İkinciler **öksüz ölçütüyle** silinir (ebeveyni artık yok) —
  bu ölçüt firma-güvenlidir, başka firmanın ebeveyni durduğu sürece çocuğuna dokunulmaz.
- **Ayrıca:** Masaüstündeki yerel sıfırlama, ADR-084'ün sözüne aykırı biçimde ADR-083'ün TAM SİLME
  fonksiyonunu çağırıyordu (firma+kullanıcı+şube+yetki siliniyordu) → o makinede çevrimdışı giriş
  imkânsız hâle geliyordu. Doğru fonksiyona çevrildi; çağrı yeri kaynak düzeyinde testle kilitlendi.
- **Sonuç:** Kullanıcının şartı ("şubeler ve kullanıcılar silinmesin") artık iki platformda da geçerli.

### ADR-105 — Hareket filtrelerinin TEK SQL kaynağı + B-1 sunucuya indi (12.08.2026)
- **Bağlam:** Stok hareket defteri İKİ yerden sorgulanıyordu: `ReportService.StockMovements`
  (rapor + XLSX) ve `StockService.SearchMovements` (Stok Hareketleri EKRANI, web + masaüstü).
  Filtre mantığı iki yerde ayrı yazılıydı → ekran ile raporun **sessizce farklı sonuç** vermesi
  mümkündü. Üstelik web ekranı lokasyon süzmesini, sunucudan gelen **limitli** listenin üzerinde
  **istemcide** yapıyordu (**B-1**): seçilen depoya ait hareket ilk N kaydın dışındaysa kullanıcı
  onu hiç göremiyor ve eksikliği fark edemiyordu.
- **Karar:** Lokasyon · hareket türü · arama · malzeme filtrelerinin WHERE'i **tek** üreteçten gelir:
  `DepoWise.Infrastructure.Materials.StockMovementFilterSql`. Rapor ve ekran bu sınıfı çağırır;
  ikinci bir hareket sorgulama mimarisi kurulmaz. Ekran ucu (`/api/stock/movements`) filtreleri
  **tekrarlanabilir sorgu parametresi** olarak alır (`?location=…&type=…&material=…`); rapor
  sözleşmesiyle aynı anlam: parametre yoksa filtre yok, **boş değer** (`?location=`) 📦 Atanmamış.
  Masaüstü ekranına da (parite eksiğiydi) lokasyon filtresi eklendi.
- **Sıra (her iki çağıranda aynı):** firma → `BranchScope` (kapsam, DIŞ SINIR) → tarih →
  lokasyon/tür/arama/malzeme → `ORDER BY created_at DESC, tie DESC` → `LIMIT`.
  Hiçbir filtre `OR` ile kapsamı genişletemez.
- **Alternatifler:** (a) ekranı doğrudan rapor tablosuna (`TableModel`) çevirmek — reddedildi:
  ekranın kolonları/UX'i (yön, birim fiyat, iptal durumu, belge alanları) rapordan zengin, gereksiz
  yeniden tasarım olurdu. (b) İstemci süzmesini korumak ve limiti büyütmek — reddedildi: hatayı
  gizler, ölçekle geri döner.
- **Sonuç:** Ekran = rapor = XLSX aynı satır kümesini üretir (testle kilitlendi). B-1'in eski
  davranışı, aynı veri üzerinde kaydı KAYBETTİĞİ gösterilerek regresyon testine dönüştürüldü.
- **Kapsam dışı (bilinçli):** `STK-B2` (aramanın `stock_documents.note`'u kapsaması) ve
  `RPR-02`/R33 (web isteğinin oturum şubesini taşımaması) bu ADR'de **çözülmedi**.

### ADR-104 — STK-10 arama filtresi kataloğa girer (KARAR-10) (11.08.2026)

- **Bağlam:** "Stok Hareketleri" ekranındaki **"Ara (kod, malzeme, not, belge no)"** kutusunun rapor
  kataloğunda karşılığı yoktu. STK-10 ekranı rapor altyapısına bağlarken üç seçenek vardı:
  (A) arama ekranda kalsın raporda olmasın · (B) kataloğa `Search` filtresi olarak girsin ·
  (C) kaldırılıp yerine Malzeme filtresi geçsin.
- **Karar (KARAR-10 = B, kullanıcı):** Arama **kataloğa gerçek bir `ReportFilters.Search` filtresi**
  olarak girer. Ekranda kalıp export dışında bırakılmaz → **ekran ve XLSX aynı filtrelenmiş kümeyi**
  üretir. Mevcut arama anlamı (kod + ad + not + belge no) **korunur**; Malzeme filtresi Search'ün
  YERİNE geçmez, ikisi birlikte bulunur. Yeni arama mimarisi icat edilmez — mevcut SQL koşulu taşınır.
- **Gerekçe:** (A) STK-10'un asıl amacını (ekran = export) kırardı. (C) kullanıcıdan mevcut bir
  yeteneği (not/belge no araması) alırdı. (B) 6 katman daha kablolama maliyeti getirir ama RPR-01
  koruma testi bunu zaten zorunlu kılıyor ve katman atlamayı imkânsızlaştırıyor.
- **Sonuç:** STK-10 filtreleri `Date | Location | Search | Material | MovementType`.
  `Search` **skaler** (`string?`), diğerleri liste. Plan + kabul kriterleri:
  [`project-control/STK_10_HAREKET_RAPORU_PLANI.md`](project-control/STK_10_HAREKET_RAPORU_PLANI.md).
- **Bağlı bulgu:** `STK-B1` STK-10'un **adım 0**'ı oldu — `movement_type` üretimde **8 değer** üretiyor,
  3'ü kullanıcıya ham İngilizce görünüyor (`reverse`, `usage`, `usage_reverse`) ve Web ile masaüstünün
  etiket haritaları **ıraksamış** (`adjustment`: "Düzeltme" ↔ "Sayım Düzeltme"). Tek doğru kaynak
  (`MovementTypeOptions`) kurulacak; mevcut kayıtların `movement_type` DEĞERLERİ değişmeyecek.
- **Kapsam dışı:** STK-11 (float artığı) bu işte çözülmez · migration açılmaz · senkron protokolü değişmez.

### ADR-103 — Bakım malzemesinin çıktığı depo (KARAR-9) (11.08.2026, KRİTİK)

- **Bağlam:** `MaintenanceService` stok yazarken lokasyonu **sabit** olarak boş yazıyordu:
  hareket defterine `branch_id = NULL`, bakiyeye `StockBalanceWriter.Unassigned`. Sonuç: her bakım
  tüketimi **ATANMAMIŞ** kovasına düşüyordu. `STK-08` geçmiş atanmamış stoğu temizleme aracını verdi,
  ama bu yol **yenisini üretmeye devam ediyordu**.
  Analiz: [`project-control/BKM_04_LOKASYON_ANALIZI.md`](project-control/BKM_04_LOKASYON_ANALIZI.md)
- **Analizin belirleyici bulguları:**
  - `vehicle_maintenances.op_branch_id` bağımsız bir alan **değil** — `s.OperatingBranchId`'nin kopyası.
    Yani "bakımın şubesi" ile "kullanıcının şubesi" **aynı veridir**; ayrı seçenek olarak değerlendirilemez.
  - **API oturumu `OperatingBranchId`'yi HİÇ set etmiyor** (tek istisna Excel içe aktarım) → bugün
    Web'den girilen her bakımın `op_branch_id`'si NULL. Web'de bu alandan lokasyon türetmek hiçbir
    şeyi düzeltmezdi.
  - **İki bakım ekranı da "Tüm Şubeler" modunda kaydetmeyi zaten engelliyor** (`RequireBranchAsync`)
    → kaydet anında somut bir şube her zaman var (masaüstünde oturumda, Web'de `Auth.BranchId`).
- **Karar (KARAR-9, kullanıcı):** Bakımda kullanılan malzemenin hangi depodan çıktığı **işleme göre
  değişir**. Bu yüzden:
  1. Bakım formunda **"Malzemenin çekildiği depo"** alanı bulunur.
  2. Varsayılan = kullanıcının aktif/oturum şubesi.
  3. Kullanıcı **kendi firmasına ait aktif** başka bir depo/şantiye seçebilir.
  4. Seçim yapılmazsa varsayılan depodan düşer; açıkça farklı depo seçilirse **o depodan** düşer.
  5. **"Atanmamış" yeni yazma hedefi olarak seçim listesinde SUNULMAZ.**
  6. Firmada hiç uygun depo yoksa bakım kaydı stok yüzünden **engellenmez** — hareket ATANMAMIŞ
     olarak devam eder (2026-08-06 kararı korunur).
  7. Yabancı firma / bilinmeyen / pasif lokasyon **kabul edilmez** → mevcut `EnsureLocationOwned`
     deseni, **servis katmanında** (masaüstü çevrimdışı yolu da korunsun diye).
- **Kullanılmayacaklar (açık yasak):** `vehicles.branch_id` stok lokasyonu belirlemek için
  KULLANILMAZ (araç şantiyede olabilir ama parça merkez depodan gelmiş olabilir → sessiz yanlış stok).
  `op_branch_id` ile stok lokasyonu **karıştırılmaz**; bakım raporundaki "Şube" mevcut anlamını korur
  (kaydı işleyen şube), stok lokasyonu ayrı kavram kalır (STK-06 ile kurulan ayrım).
- **⚠️ En kritik kural — sessiz yönlendirme YASAK:** Kullanıcı depo seçimini değiştirdiğinde bu
  gerçekten stok hareketine yansımalıdır. Sessizce kullanıcının şubesine dönmek, aracın şubesini
  kullanmak, `op_branch_id` üzerinden yeniden hesaplamak veya başka lokasyona yönlendirmek yasaktır.
  (Aynı hata sınıfı `STK-08`'de bulunmuştu: `EnforceOwnBranch` boş kaynağı sessizce kullanıcının
  şubesine çeviriyordu.)
- **⚠️ İPTAL SİMETRİSİ:** Ters hareketin lokasyonu iptal anındaki oturumdan **yeniden hesaplanmaz**;
  **orijinal stok hareketinin `branch_id` değeri okunur** ve ters kayıt aynı lokasyona uygulanır.
  (Depo A'dan düşen 5, kullanıcı Depo B ile giriş yapmış olsa bile Depo A'ya döner.)
- **Kapsam dışı:** Geçmişte oluşmuş ATANMAMIŞ bakım tüketimleri **taşınmaz/tahmin edilmez**; mevcut
  stoklar yeniden dağıtılmaz; KARAR-8 kapsamındaki stoklara dokunulmaz. BKM-04 yalnız **yeni** bakım
  tüketim akışını ele alır.
- **Migration:** GEREKMEZ — `stock_movements.branch_id` ve `stock_balances.location_id` zaten var.
  Yeni tablo/kolon/senkron protokolü açılmaz; `stock_movements` senkronda olduğu için lokasyon
  kolon-kesişimiyle kendiliğinden taşınır. SNK-11'de çıkarılan `stock_balances` senkronu geri gelmez.
- **Geriye dönük uyum:** `branchId` API'de **opsiyonel**; göndermeyen eski istemci bugünkü davranışta
  (ATANMAMIŞ) kalır ve kırılmaz. Yeni Web ve yeni masaüstü lokasyonu gönderir.

### ADR-102 — Stok bakiyesi DEPO BAZLI oldu (KARAR-7=A) (11.08.2026, TAMAM — KRİTİK)

- **Bağlam:** `stock_movements` baştan beri lokasyon taşıyordu (`branch_id`, transferde `branch_from_id`),
  ama `stock_balances` birincil anahtarı `(material_id)` idi → firma başına TEK bakiye. Sonuç: transfer
  bakiyede **görünmüyordu** ("net bakiye değişmez") ve "hangi depoda ne kadar var?" sorusunun cevabı yoktu.
  Bu, projenin 1 numaralı mimari borcuydu; ön muhasebe ve şantiye maliyeti buna bağlı.
- **Karar (KARAR-7 = A, kullanıcı):** **Malzeme kartı FİRMA GENELİ kalır** (ortak katalog), **stok DEPO
  BAZLI** olur. `stock_balances` anahtarı `(company_id, material_id, location_id)`; `location_id` =
  `branches.id`, **`''` = ATANMAMIŞ** (PostgreSQL'de PK kolonu NULL olamaz; boş metin açık, sorgulanabilir
  ve ekranda gösterilebilir bir kovadır). Ayrı bir "depo" tablosu AÇILMADI — lokasyon = şube/şantiye.
- **Veri:** Yeni bakiyeler **hareket defterinden** yeniden hesaplanır (`Migration064`). Eski tek-satır
  bakiyeler lokasyonlara **dağıtılmaz/tahmin edilmez** — veri uydurmak yasak (kullanıcı kuralı). Lokasyonu
  bilinmeyen geçmiş ATANMAMIŞ kovasında kalır; kullanıcı sonradan transferle taşır (KARAR-8 / `STK-08`).
- **Hassasiyet:** Toplama **C#'ta `decimal`** ile yapılır; SQL `SUM` yazma yollarında KULLANILMAZ
  (miktar TEXT içinde decimal tutulur, SQLite'ta sayısal toplama kayan noktaya düşer).
  Görüntüleme toplamı için `SqlDialect.StockTotalSubquery` **kanonik metin** üretir (iki lehçede aynı çıktı).
- **Fail-closed migration:** Yazmadan önce her malzeme için `Σ(yeni lokasyon bakiyeleri) == eski bakiye`
  karşılaştırılır; tek bir fark bile varsa istisna fırlatılır ve runner transaction'ı geri alır.
  Sessiz bozulma yerine açık durma tercih edildi. **Sonuç:** bakiyesi defterle uyuşmayan bir veritabanında
  güncelleme başlamaz — önce `RecomputeBalances` gerekir. Üretim kopyasında uyuşmazlık YOK.
- **Atomiklik:** Migration ve 16 çağrı noktasının dönüşümü **aynı iş biriminde** verildi. Yalnız migration
  açılsaydı stok değerleri **sessizce yanlış** görünürdü — en tehlikeli hata türü.
- **Senkron:** **0 değişiklik** gerekti. `DbIntrospect.PrimaryKey` bileşik anahtarı sırayla okuyor,
  `BusinessSyncService` `ON CONFLICT` hedefini ondan kuruyor → üç kolonlu anahtar otomatik üretiliyor.
  `stock_movements` şeması değişmedi → push/pull/idempotency aynen çalışır.
  **Masaüstü çevrimdışı mimarisi korundu** (SQLite kalır; aynı migration kataloğu yürür).
- **Alternatifler:** (a) ayrı `stock_locations` tablosu — gereksiz ikinci kavram, şube zaten var;
  (b) bakiyeyi kaldırıp her okumada defteri toplamak — 667 hareketle bugün ucuz ama büyümede yavaş,
  ayrıca CAS koruması kaybolurdu; (c) `DISTINCT` ile satır çoğaltmayı gizlemek — **reddedildi**
  (hatayı gizler, toplamı bozardı).
- **Kanıt:** İzole PG kopyası 667 hareket · 664 → 665 bakiye · **uyuşmayan 0** · toplam korundu ·
  dolu SQLite v63→v64 kayıpsız · doğrulama kapısı durdurup geri aldı · liste 2459 satır = malzeme sayısı
  (çoğaltma yok) · test 1223/1190/0/33. Rapor: `docs/tests/Stok_Lokasyon_Test_Report.md`.
- **Yan bulgu (düzeltildi):** Sayım, sistem miktarını firma genelinden okuyup düzeltmeyi şubeye yazıyordu.

---

### ADR-101 — Delta eşitleme: yalnız değişen kayıtlar (zaman aşımı çözümü) (19.07.2026, TAMAM — KRİTİK)

- **Bağlam:** Kullanıcı: "DESKTOP-SIKIB3U makinede eşitleme zaman aşımına uğruyor." Tanı: firmada 2508 malzeme
  var; her push/pull TAM snapshot gönderiyordu (server'da zaten olsa bile) → 120sn'yi aşıyordu. Uygulama her
  açılışta da her şeyi baştan gönderiyordu.
- **Karar (ADR-099'un tamamlayıcısı):** `BuildSnapshot(companyId, machineId, sinceVersion)` — sinceVersion>0 ise
  yalnız `updated_at > sinceVersion` satırlar. Masaüstü her tick (~15 sn): sunucu sürümünü (`business-version`)
  ve yerel sürümü (`CompanyVersion`) alır; **PUSH DELTA** = yerel > sunucu ise yalnız yeni satırları gönderir
  (server'da olanı tekrar göndermez); **PULL DELTA** = `business-pull?since=X` ile yalnız yeni satırları çeker.
  Pull imleci KALICI (`SettingsService: sync_pull_cursor`) → yeniden açılışta her şeyi baştan çekmez. Zaman
  aşımı 120→**300sn** (ilk/tam eşitleme büyük olabilir). Giriş de delta push kullanır; **manuel "Eşitle" TAM**
  (uzlaştırma) kalır.
- **Soft-delete deltada:** silme `is_deleted=1 + updated_at=now` → delta'ya girer, silme yayılır.
- **Bilinen sınır:** iki makine eşzamanlı yazarken, sunucu sürümünden ESKİ ama henüz push edilmemiş bir satır
  delta'da atlanabilir (nadir); manuel "Eşitle" (tam) bunu uzlaştırır. Gerçek per-cihaz push-cursor sunucu
  tarafı ileride eklenebilir.
- Test: `Delta_Snapshot_YalnizDegisenleri_Icerir_CompanyVersion_MaxDoner` + 34 senkron testi (35/35). **1.0.82.**

---

### ADR-099 — Duyarlı eşitleme: ucuz sürüm kontrolü + açık ekran otomatik yenileme (19.07.2026, TAMAM)

- **Bağlam:** Kullanıcı: "otomatik eşitleme 3 dakikada bir değil anlık, herhangi bir kayıt değiştiğinde
  yapılmalı; kayıtlar hem web hem uygulamada anlık görünmeli." + canlı tanı: OZE'de sunucuda 2508 malzeme VAR
  (push çalışmış) ama diğer makinede görünmüyordu → açık ekran pull sonrası kendini yenilemiyordu + 3 dk gecikme.
- **Karar (delta'sız duyarlılık):** Delta senkron (yalnız değişen satır) büyük iş, ayrı yapılacak (kullanıcı
  onayı: önce anlık). Bu adımda **ucuz sürüm kontrolü**: `BusinessSyncService.CompanyVersion` = firmanın tüm iş
  tablolarındaki max(updated_at) (tek sayı). Masaüstü artık her **15 sn** (eski 180 sn yerine): (1) yerel sürüm
  arttıysa push, (2) sunucu sürümü (`GET /api/sync/business-version`) arttıysa pull. Sürüm değişmediyse tam
  snapshot AKTARILMAZ → sık yoklama ucuz, bant israfı yok. Pull veri getirince **açık liste ekranı kendini
  yeniler** (`IRefreshable` — Malzemeler/Araçlar/Günlük Faaliyet/Stok) → kullanıcı gidip dönmek zorunda kalmaz.
- **Web:** Web zaten her yüklemede sunucudan CANLI okur (bayatlamaz); masaüstü push edince (artık ~15 sn'de)
  web bir sonraki gezinme/yenilemede görür. Web'de otomatik canlı-güncelleme (polling/SignalR) sonraki adım.
- **Kalan (bu maddeden):** Gerçek DELTA (yalnız değişen kayıt aktarımı) + web canlı-güncelleme. Şu anki tam-
  snapshot yaklaşımı KORUNDU ama yalnız değişince tetikleniyor.
- Test: 34 senkron testi yeşil (554/554 genel). **Masaüstü 1.0.78.**

---

### ADR-098 — 8 maddelik masaüstü-öncelikli istek paketi (19.07.2026, 7/8 TAMAM canlı)

Kullanıcı masaüstünde test edip 8 madde + platform-öncelik kuralı verdi (`.claude/rules/platform-priority.md`:
masaüstü önce, web eksik bırakma). Durum (554/554 test, API+Web+masaüstü 1.0.77 canlı):
- ✅ **Günlük Faaliyet — Arıza Açıklaması:** İlave Yağ/Filtre/Tamir'de "Açıklama" → "Arıza Açıklaması" (aynı
  description alanı, şema değişmez). Web+masaüstü.
- ✅ **Enter ile filtreleme:** masaüstü Malzeme/Araç/Günlük Faaliyet filtre kutularına Enter KeyBinding (web
  zaten OnKeyUp ile yapıyordu).
- ✅ **Fluent klasik menü rengi:** menü grup/ekran isimleri artık `Brand.OnSurface.Brush` (menü yüzeyi rengi)
  kullanır → koyu menüde siyah/görünmez sorunu bitti, Semi ile aynı görünür. YALNIZ menü. (Fluent/Semi
  masaüstü temaları — web MudBlazor teması etkilenmez.)
- ✅ **Yakıt dağıtımı "Yakıtı Alan":** yeni `recipient_personnel_id` (Migration052) + "Yakıtı Veren"den ayrı
  form alanı. Web+masaüstü.
- ✅ **Talep formu PDF logosu büyütüldü** (110×48 → 170×72) + **Ekonomik PDF'e logo eklendi** (ortak servis →
  web+masaüstü).
- ✅ **Araç ekranı sayfalama:** sayfa numaraları/bilgisi tablonun ALTINDA, solda (web+masaüstü). ⚠️ Malzemeler
  hâlâ ÜSTTE — kullanıcı yalnız aracı belirtti; istenirse Malzemeler de alta alınır (tutarlılık).
- ⏳ **Giriş-Çıkış çoklu malzeme (depo çıkışı):** YAPILMADI — §4 stok değişmezlerine (negatif stok/idempotency)
  dokunduğundan, aceleye getirmeden ayrı ve dikkatli yapılacak (çoklu satır UI + satır-başı doğrulama).

**Web eşitleme sorunu (kullanıcı bildirimi):** Ayrı bir web hatası DEĞİL — web doğrudan sunucudan okur; canlı
tekrar doğrulandı, OZE'de sunucuda hâlâ 0 malzeme (makine A henüz push etmemiş). Makine A 1.0.77 ile online
olunca ADR-097 otomatik push'u veriyi gönderir → web + makine B görür. Web→masaüstü yönü: masaüstü pull'u
(giriş + ~3dk) web değişikliklerini çeker.

---

### ADR-097 — Eşitleme kök neden: içe aktarım push etmiyordu + kullanıcılar makineler arası görünmüyordu (19.07.2026, TAMAM — KRİTİK)

- **Bağlam:** Kullanıcı: "Bir firmanın şubesine toplu kayıt içeri aldım; aynı şubeye farklı makine/kullanıcı ile
  login oldum ama veriler eşitlenmedi." + "Personel'de kullanıcı bağlamak istedim, kullanıcı (Mustafa Alpaslan,
  Oze/Karaman) listelenmedi."
- **CANLI TANI (salt-okunur, superadmin):** OZE GRUP firmasında sunucuda **0 malzeme, 0 araç** ama 2 şube,
  1 personel, 2 kullanıcı (mustafa.alpaslan dâhil) VAR. → İçe aktarılan iş verisi sunucuya HİÇ ulaşmamış;
  Mustafa kullanıcısı sunucuda var ama masaüstünün yerel `users` tablosunda yok.
- **KÖK NEDEN 1 (iş verisi):** İçe aktarım yalnız YERELE yazıyordu; push yalnız giriş / "Eşitle" / periyodik
  (~3dk) tetikleniyordu. Kullanıcı içe aktarıp makineyi kapatınca/değiştirince veri sunucuya ulaşmıyordu.
  **Düzeltme:** içe aktarım biter bitmez `BusinessSyncPushService.PushAsync()` çağrılır ve sonuç kullanıcıya
  GÖSTERİLİR ("✔ gönderildi" / "⚠️ gönderilemedi — Eşitle'ye basın"). Sessiz başarısızlık yok.
- **KÖK NEDEN 2 (kullanıcı görünürlüğü):** Kullanıcılar iş senkronunda YOK (yalnız giriş yapan kullanıcının
  kendi kaydı yerele iner — §4 kullanıcı/firma değişmezliği bilinçli). Personel ekranı bağlanabilir kullanıcıları
  YEREL `users`'tan okuyordu → başka makinede/web'de oluşturulmuş kullanıcı listelenmiyordu. Ayrıca bağ
  (`users.personnel_id`) sunucu-otoriteli ve masaüstünden push EDİLMEZ. **Düzeltme:** (a) personel ekranı
  çevrimiçiyken bağlanabilir kullanıcıları SUNUCUDAN çeker (`ServerUserClient.GetLinkableUsersAsync` →
  /api/personnel/linkable-users; çevrimdışı → yerel liste); (b) bağlama işlemi çevrimiçiyken SUNUCUDA yapılır
  (önce personel push edilir, sonra `ServerUserClient.LinkUserAsync` → bağ tüm makinelere ulaşır). Yerel
  `users` tablosuna DOKUNULMAZ (immutability korunur).
- **Web:** Bu iki sorun web'de YOK — web zaten sunucu-tarafı (import ekranı yok; personel bağlama zaten API'den
  okur/yazar). Platform-öncelik kuralı gereği kontrol edildi, web'de değişiklik gerekmedi.
- **OTOMATİK GÖNDERİM (kullanıcı sorusu 2026-07-19: "butona basmadan da gitsin"):** Yapı zaten otomatiktir —
  push+pull şu anlarda KENDİLİĞİNDEN olur: (1) her GİRİŞTE, (2) uygulama açıkken her **~3 dakikada**, (3) içe
  aktarım biter bitmez (yeni). "Eşitle" butonu yalnız ANINDA/elle tetikleme içindir, zorunlu değildir. **Kapatılan
  boşluk:** veriyi girip 3 dk dolmadan uygulamayı KAPATIRSAN/ÇIKIŞ yaparsan o an gitmiyordu → artık **kapanışta ve
  çıkışta** son bir push yapılır (en fazla 10 sn sınırlı bekleme; çevrimdışıysa anında geçer). Böylece hiçbir
  senaryoda veri yerelde takılı kalmaz.
- **⚠️ Mevcut takılı veri:** Bu düzeltme YENİ içe aktarımlar için. Makine A'da hâlihazırda takılı kalan veri için
  kullanıcı: A'yı güncel sürüme al → ONLINE olduğunda ~3 dk içinde (ya da hemen "Eşitle" ile) 2600+ kayıt sunucuya
  gider → sonra B girişte/otomatik görür.
- **Test:** Bu iş UI+HTTP akışı (birim test kapsamaz); derleme temiz, canlı tanı ile kök neden doğrulandı.
  Kullanıcı makine A→"Eşitle"→makine B ile uçtan uca doğrulamalı.

---

### ADR-096 — Çift-tık "hızlı düzenle" penceresi: Malzemeler + Araçlar (19.07.2026, TAMAM)

- **Bağlam:** Kullanıcı: "Çift sol tık yaptığımda uygulama içi ayrı bir pencerede düzenleme/kaydetme/silme
  yapacağım bir pencere açılsın… düzelt, kaydet ve sil butonları olmalı; düzelt butonuna tıklanınca düzeltme
  yapılabilmeli." Tek tık davranışı (detay paneli) KORUNUR. Kapsam: "Malzemeler ve Araçlar'dan başla."
- **Karar:** Her iki ekranda kayda ÇİFT TIKLAYINCA ayrı bir pencere açılır; alanlar "Düzelt"e basılana kadar
  SALT-OKUNUR (web: MudField; masaüstü: kontroller IsEnabled=false). "Düzelt" → düzenlenebilir; "Kaydet" →
  günceller ve kapatır; "Sil" → siler ve kapatır. Kapanınca liste + açık detay paneli tazelenir.
- **VERİ KORUMA (kritik tasarım):** Hızlı pencere yalnız çekirdek alanları düzenler. Malzemede fotoğraf/muadil/
  uyumlu araçlar DEĞİŞMEZ — PUT'a `vehicleIds=null` gönderilir (sunucu mevcut listeyi korur), muadiller PUT'a
  hiç dokunmaz. Araçta iç kod ve SAYAÇ değişmez (sayaç zaten `UpdateVehicle`'da yok — §4 sayaç geri gitme
  koruması); fotoğraflar korunur. Böylece hızlı düzenleme yanlışlıkla ilişki/foto SİLMEZ.
- **Web:** `MaterialEditDialog.razor` + `VehicleEditDialog.razor` (MudDialog), satırda `@ondblclick`. İzinler:
  Düzelt yalnız `CanEdit`, Sil yalnız `CanDelete`. Araç markası değişince modeller kademeli yenilenir.
- **Masaüstü:** `MaterialQuickEditWindow` + `VehicleQuickEditWindow` — **kod-arkası** Window (ColumnPickerWindow
  ile aynı desen, DataContext bağlaması YOK → düşük runtime-binding riski). `QuickEditService` MainWindow
  üzerinde modal açar. Satır `DoubleTapped` → `QuickEditSelectedCommand`. Silme: iç içe modal açmamak için
  İKİ AŞAMALI (ilk tık uyarır, ikinci siler).
- **⚠️ Doğrulama:** Web derleme temiz; masaüstü derleme temiz. Bu ortamda Avalonia çalıştırılamadığı ve web
  giriş formu otomasyonu güvenlik politikasınca engellendiği için **görsel/uçtan-uca test kullanıcıya kalıyor**
  (554/554 birim test yeşil — bu iş UI olduğundan birim testi kapsamaz). Kullanıcı canlıda: çift tık pencereyi
  açıyor mu, Düzelt alanları açıyor mu, Kaydet/Sil çalışıyor mu — kontrol etmeli.
- **Sonraki:** Diğer liste ekranlarına (Günlük Faaliyet, Personel, Stok vb.) aynı desen istenirse eklenecek
  (kullanıcı "sonrasını kendin belirle" dedi — önce bu ikisi referans).

---

### ADR-095 — Opus 4.8 gözden geçirme: ADR-090…094 denetimi + sabit-tanım yarış düzeltmesi (19.07.2026, TAMAM)

- **Bağlam:** Kullanıcı: "buraya kadar farkında olmadan sonnet 5 ile yapmışız... opus 4.8 e aldım, proje
  bittikten sonra analiz et ve düzeltilmesi gereken şeyler var ise düzelt." → bu oturumdaki tüm iş (ADR-090…094)
  Opus 4.8 ile satır satır yeniden denetlendi (tenant/permission/senkron/idempotency/web-masaüstü ayna).
- **Denetim sonucu (TEMİZ bulunanlar):**
  - **ADR-090 (senkron):** `Task.Run` + 120sn zaman aşımı + görünür hata doğru; devam UI thread'inde çözülür,
    `LastPushFailed` yarışsız. Sorun yok.
  - **ADR-091 servis başlatma sırası:** `DesktopServices` ve `ServerServices` ikisinde de Maintenance/
    MaintenanceDefs → DailyActivity sırası doğru düzeltilmiş. Sorun yok.
  - **ADR-092 kilit + Migration051:** `is_locked` iş-senkronundaki 8 lookup tablosuna eklendi; `BusinessSync.
    UpsertRow` JSON∩yerel-kolon KESİŞİMİ aldığından ESKİ istemci yeni kolonu sessizce yok sayar, YENİ istemci
    varsayılan 0 alır — çift yönlü senkron güvenli. Kilit LWW ile makineler arası doğru yayılır. Tenant/izin
    doğru (SetLocked yalnız admin; RequireNotLocked firma-kapsamlı). Sorun yok.
  - **ADR-094 Günlük Faaliyet grid:** SearchGrid SQL parametreli/injection-güvenli, tarih sıralaması
    (`date_raw`), operatör param eşlemesi (`operator`→`operatorText`), web/masaüstü kolon aynası tutarlı;
    kaldırılan eski filtre state'ine sarkan referans YOK. Sorun yok. (Küçük kozmetik: "Süre" kolonu metinsel
    sıralanır — sayısal değil; nadir kullanıldığından reader-index riskini almamak için DEĞİŞTİRİLMEDİ.)
- **DÜZELTİLEN (1 gerçek, düşük öncelikli bulgu):** `EnsureExtraDefinition` (ADR-091) "önce SELECT, yoksa
  Create" ADIMLARI ATOMİK DEĞİLDİ ve `MaintenanceDefinitionService.Create` tekilleştirmez → AYNI firmada AYNI
  türün İLK kaydını iki kullanıcı SUNUCUDA eşzamanlı girerse İKİ görünmez sabit tanım oluşabilirdi (masaüstü
  tek-kullanıcı → etkilenmez; stok/ledger bozulmaz — yalnız çift gizli tanım). **Çözüm:** tek
  `INSERT ... SELECT ... WHERE NOT EXISTS` (SQLite yazarları seri hale getirir → yarışsız). İzin zaten
  `_maintenance.Save` (maintenance/Create) ile korunduğundan bypass güvenli. Test: 1 yeni (539→554 arası,
  `SaveExtraActivity_AyniAdliTanimVarsa_YenidenKullanir_CiftOlusmaz`). 554/554.
- **Yayın:** API redeploy (sunucu yarışı bu düzeltmeden faydalanır). Masaüstü YENİDEN YAYINLANMADI — yarıştan
  etkilenmiyor (tek-kullanıcı); düzeltme bir sonraki masaüstü işiyle (çift-tık pencere) birlikte paketlenecek.

---

### ADR-093 — Form kutuları odaklanmadan da görünür + Semi Modern arama kutusu (19.07.2026, TAMAM)

- **Bağlam:** Kullanıcı: "Yeni formlarında bulunan kutuların üstüne tıklama yapılmadığında da görünür
  şekilde olsun. tıklamadığımda arka plan ile aynı renkte olduğu için karıştırabiliyorum." + "Semi modernde
  bulunan arama kutusu tasarımını Fluent klasikteki arama kutusu tasarımı ile aynı yap."
- **Kök neden (masaüstü):** `TextBox.Field`/`ComboBox.Field`/`NumericUpDown.Field`/`DatePicker.Field`
  zemini `SurfaceBrush` idi — bu ekranlardaki KART/PANEL zemini de (`Border.Panel`) AYNI `SurfaceBrush`;
  alan kenarlığı (`BorderSubtleBrush`) açık temada çok soluk → beyaz kutu beyaz panelde neredeyse kayboluyordu.
  **Düzeltme:** zemin `SurfaceElevatedBrush`'a alındı (arama kutusunun zaten kullandığı desenle aynı) —
  panelden ayırt edilir hâle geldi, hem açık hem koyu temada.
- **Kök neden (web):** MudBlazor `.mud-input-outlined-border` yalnız `:hover`/`.mud-focused` durumunda
  belirgin kenarlık alıyordu; **varsayılan (odaksız) durumda** kenarlık MudBlazor'un soluk öntanımlı çizgi
  rengine düşüyordu. **Düzeltme:** `app.css`'te varsayılan duruma `border-color: color-mix(... text-primary
  32% ...)` eklendi — tema-bağımsız (CSS değişkeni otomatik light/dark'a göre çözülür).
- **Arama kutusu (Semi/Fluent parity):** `TextBox.Search` temel stili iki temada da PAYLAŞILIYORDU ama
  "modernizasyon katmanı" (yumuşak köşe geçişi) yalnız `TextBox.Field`'a uygulanmıştı, Search'e değil —
  bu eksiklik giderildi (aynı geçiş/köşe normalizasyonu Search'e de eklendi, tema-bağımsız).
- **⚠️ Doğrulama sınırı:** Bu ortamda Avalonia çalıştırılamıyor (masaüstü UI görsel test edilemedi, yalnız
  temiz derleme ile güvence alındı) ve web "yeni kayıt" formları girişe ihtiyaç duyduğu için tarayıcı
  otomasyonuyla (kimlik girişi güvenlik politikasınca yasak) doğrulanamadı. Kullanıcı canlıda kontrol etmeli;
  Semi Modern'in arama kutusunda HALA fark varsa (Semi.Avalonia'nın kendi TextBox şablonu Fluent'ten temel
  şablon parçaları bakımından farklı olabilir) ekran görüntüsüyle bildirilirse tam eşleşme için ek dokunuş yapılır.

---

### ADR-094 — Günlük Faaliyet ekranına liste deseni: filtre+sayfalama+sıralama+Excel (19.07.2026, TAMAM)

- **Bağlam:** Kullanıcı: "Günlük faaliyet ekranına da Detay filtreleme işlemi için araçlar ve malzemeler
  ekranına yaptığımız geliştirmeyi yapacağız." (madde 15, ayrıca madde 16'nın bu ekrana yayılması).
- **Karar:** Malzemeler/Araçlar'daki ADR-087/088/089 deseninin BİREBİR AYNISI: `DailyActivityListColumns`
  (Application + Web ayna) · `DailyActivityService.SearchGrid/SearchGridAll/ToTableModel` · API `GET
  /api/daily/grid` + `/api/daily/grid/export` · Web (`Daily.razor`) kolon filtreleri + üstte-sol sayfalama +
  başlığa tıklayınca sıralama + "Excel'e Aktar" · Masaüstü (`DailyActivityViewModel`/`View`) aynı desen
  (`IListGridViewModel`, `SortHeader`, kişiye özel kolon/genişlik/sayfa-boyutu).
- **"Tarih" bilinçli olarak filtre kutusu YOK** — yalnız başlığa tıklayarak sıralanır (ham `activity_date`
  üzerinden, `GridQuery.ColumnKind.Numeric` + `RawAlias`). Sebep: biçimlendirilmiş "gg.aa.yyyy" metninde
  "içerir" araması hem yanıltıcı olur (örn. "07" günü de ayı da yakalar) hem de doğru kronolojik sıralama
  için ayrıca ham sütun gerekirdi — bu tur kapsamı dışına alındı.
- **Varsayılan sıra** (kullanıcı başlığa tıklamadıysa): en yeni faaliyet üstte — mevcut `List()` davranışıyla
  AYNI. Malzemeler/Araçlar'daki "filtrelerin doldurulma sırası" önceliği bu ekranda UYGULANMAZ (kronolojik
  günlük ekranı için tarih her zaman kazanır — bilinçli sapma, dokümante edildi).
- Eski basit dropdown filtre (Tümü/Hareket-Transfer/Bakım/İlave...) + gün seçici KALDIRILDI; yerine "Kayıt
  Tipi" kolonunun serbest metin filtresi geldi (daha esnek: "İlave" yazınca 3 türü birden yakalar, "Tamir"
  yazınca yalnız onu).
- **Test:** 9 yeni (`DailyActivityGridTests`) — tip/araç/rota/personel/açıklama filtresi · varsayılan tarih
  sırası · başlığa tıklayınca araca göre sıralama · SearchGridAll tüm sayfaları dolaşır · Excel tablo modeli ·
  tenant izolasyonu. 553/553. **Canlıya alındı:** API+Web deploy, masaüstü **1.0.72** yayınlandı (sunucuda
  "en güncel" doğrulandı).
- **⚠️ Doğrulama sınırı:** Web tarayıcı otomasyonuyla giriş gerektirdiği için (kimlik girişi yasak) uçtan uca
  test edilemedi; masaüstü Avalonia bu ortamda çalıştırılamıyor. Backend tam test edildi (553/553); UI
  yalnız temiz derleme ile güvence alındı.

---

### ADR-092 — Tanım Düzenle: "sabit tanım" (kilit) desteği (19.07.2026, TAMAM)

- **Bağlam:** Kullanıcı: "Tanım düzenle alanında hem + butonu alanları hem de + butonu olmayan sabit
  tanımlar alanları olmalı. sabit tanımları silme ve düzenleme işlemi yapamasınlar ama yeni tanım
  ekleyebilsinler." — hangi tanımların "sabit" olacağı belgede yok (gerçek ürün belirsizliği); en güvenli
  ve tersine çevrilebilir yorum uygulandı: KATEGORİ bazında değil, TEK TEK SATIR bazında kilit.
- **Karar:** `LookupService`'in yönettiği 8 tanım tablosuna (`material_categories, brands, units, suppliers,
  vehicle_types, vehicle_categories, vehicle_models, branches`) `is_locked` kolonu eklendi (Migration051,
  varsayılan 0 — HİÇBİR mevcut satır otomatik kilitlenmedi). Yalnız **admin** (firma admin/süper admin,
  `AccessControl.IsAdmin`) bir satırı kilitleyip açabilir (`LookupService.SetLocked`, `PUT
  /api/lookups/{table}/{id}/lock`). Kilitli satır `Rename`/`Delete`'te reddedilir (`ArgumentException`,
  servis seviyesinde — API/UI'dan bağımsız korunur). **"+" ile yeni tanım ekleme kilitten TAMAMEN bağımsız
  her zaman açık** — kullanıcının "yeni tanım ekleyebilsinler" şartı budur.
- **UI:** Web (`DefEditor.razor`) kilitli satırda kalem/sil ikonu yerine kilit ikonu gösterir; admin'e ayrıca
  kilitle/kilit-aç ikon butonu görünür. Masaüstü (`SettingsView.axaml`/`LookupSectionViewModel`) aynı desen:
  Kaydet/Sil butonları kilitliyken devre dışı, admin'e "Kilitle"/"Kilidi Aç" butonu.
- **Kullanıcı onayı gerekli:** Bu tamamen GENERİK bir kilit ARACI — hangi tanımların fiilen kilitlenmesi
  gerektiği kullanıcının/admin'in kendi kararı (örn. "Yedek Parça" kategorisi hep sabit kalsın gibi bir
  isteği varsa admin ekrandan kilitler). Kod hiçbir tanımı kendiliğinden kilitlemedi.
- **Test:** 5 yeni (`LookupDedupTests`) — kilitli rename/delete reddi · kilit açılınca serbest · kilitliyken
  yeni tanım eklenebilir · admin olmayan kilit değiştiremez. 544/544.

---

### ADR-091 — Günlük Faaliyet: "İlave Yağ/İlave Filtre/Tamir" + masaüstü Bakım null-referans kusuru (19.07.2026, TAMAM)

- **Bağlan:** Kullanıcı: "Günlük Faaliyet kayıt tipine 3 yeni kayıt tipi eklenecek... Bakım ile aynı olacak
  sadece bakım tanımı ve alt bakım olmayacak. diğer bütün alanlar olması gerekir."
- **Karar:** 3 yeni tür (`extra_oil`/`extra_filter`/`repair`) ortak `MaintenanceService`'i kullanır (sayaç +
  malzeme stok düşümü dahil — Bakım ile TAM AYNI mekanizma); her tür firma başına OTOMATİK oluşan sabit bir
  `maintenance_definitions` satırına (IntervalValue=0 → asla vade uyarısı üretmez) bağlanır — kullanıcı bunu
  hiç görmez/seçmez. Web (`Daily.razor`) + masaüstü (`DailyActivityViewModel`) ikisinde de "Kayıt Tipi"
  listesine eklendi; form alanları Bakım ile PAYLAŞILIR (Teknisyen/KM/Saat/Malzeme), yalnız Bakım
  Tanımı/Alt Bakım bu 3 türde gizlenir.
- **Yan bulgu (gerçek, önceden fark edilmemiş kusur):** `DesktopServices.Initialize()`, `DailyActivityService`'i
  `Maintenance`/`MaintenanceDefs` ATANMADAN ÖNCE oluşturuyordu — `readonly` alan kalıcı olarak `null` kalıyordu.
  Masaüstünün Günlük Faaliyet ekranından "Bakım" kaydı kaydedilirken bu YOLDA hiç kullanılmamış olmalı
  (aksi halde NullReferenceException verirdi) — muhtemelen kullanıcılar Bakım'ı hep ayrı Bakım ekranından
  giriyordu. Sıra düzeltildi. **Sunucu tarafında (`ServerServices`) AYNI kusur `MaintenanceDefinitions` için
  vardı** (o da `DailyActivity`'den SONRA oluşturuluyordu) — o da düzeltildi.
- **Test:** `DailyActivityExtraTests` (9) — 3 tür de kayıt oluşturur · malzeme stoktan düşer (Bakım ile aynı) ·
  aynı tür ikinci kayıt AYNI sabit tanımı kullanır (ikinci tanım oluşmaz) · operation_id idempotent · geçersiz
  tür reddedilir · 3 tür 3 ayrı tanım · periyot=0 asla Kritik/Gecikti seviyesi üretmez. 539/539.
- **Masaüstü 1.0.70'de canlı.**

---

### ADR-090 — Senkron donma + sessiz başarısız push kök neden düzeltmesi (19.07.2026, TAMAM — KRİTİK)

- **Bağlam:** Kullanıcı iki ayrı şikayet bildirdi: (1) "dün babamın kayıtlarını içeri almıştım, araçlar ve
  malzemeler web ile eşitlenmemiş... aynı kullanıcı ve aynı şube ile web'e login oldum ama veriler gelmedi."
  (2) "menüler arasında masaüstü uygulama geçiş yaparken takılabiliyor veya çok kısa donabiliyor... sunucu
  kaynaklı olduğunu sanıyorum."
- **Teşhis (canlı sunucu doğrulandı):** Süper admin API token ile babanın firmasına (OZE GRUP İNŞAAT) geçilip
  `/api/materials/grid` ve `/api/vehicles/grid` sorgulandı → **`totalCount: 0`** — içe alınan veri sunucuya
  HİÇ ULAŞMAMIŞ. Kod incelemesi iki birleşik kök neden ortaya çıkardı:
  1. `BusinessSyncPushService.PushAsync`/`PullService.PullAsync` SENKRON ağır işi (`BuildSnapshot` — binlerce
     satırı okuyan ADO.NET döngüsü; `ApplyPull` — JSON parse + upsert döngüsü) `Task.Run` OLMADAN çalıştırıyordu.
     `ShellViewModel`'in periyodik zamanlayıcısı (`_connTimer.Tick`, 30sn'de bir; push/pull'u 180sn'de bir
     tetikler) ve "Eşitle" butonu bu ağır işi ARAYÜZ İŞ PARÇACIĞININ DEVAMI olarak çalıştırıyordu → 2600+
     kayıtlı firmada bu senkron iş **arayüzü donduruyordu** ("sunucu kaynaklı" sanılan şey aslında istemci
     tarafı iş parçacığı bloklanmasıydı — sunucu yanıt süresi de payda ama asıl sebep bu değildi).
  2. `HttpClient.Timeout = 30sn` — büyüyen snapshot (malzeme+araç+stok hareketleri) bu süreyi aşınca push/pull
     `TaskCanceledException` fırlatıyordu; `catch {}` bunu SESSİZCE yutuyordu → kullanıcı hiçbir hata görmüyordu,
     veri sonsuza kadar sunucuya ulaşmıyordu (her periyodik denemede AYNI zaman aşımı tekrarlanıyordu).
- **Karar:**
  1. `BuildSnapshot`/`ApplyPull`'ın senkron kısmı `Task.Run` ile arka plana alındı — kim çağırırsa çağırsın
     (zamanlayıcı, Eşitle butonu, girişteki fire-and-forget) arayüz artık bloklanmaz.
  2. HttpClient timeout 30sn → **120sn** (push VE pull).
  3. `BusinessSyncPushService.LastPushFailed` eklendi; "Eşitle" butonunun sonuç mesajı artık push başarısızsa
     bunu AYRI/doğru bir mesajla gösterir ("Eşitleme tamamlandı" yanıltıcı görünmesin).
- **Bu, ADR-089'daki senkron/donma bulgusuyla AYNI kök nedendir** — birden fazla makinenin aynı şube verisini
  görememesi de (kullanıcının 3. şikayeti) muhtemelen AYNI push-zaman-aşımı sorunundan kaynaklanıyordu: makine
  A'nın verisi sunucuya hiç ulaşmadığından makine B'nin pull'u doğal olarak boş dönüyordu.
- **YIKICI DEĞİL, veri kaybı YOK:** yerel (masaüstü) veri hep durdu; yalnız sunucuya ULAŞMIYORDU. Düzeltme
  sonrası kullanıcının babasının makinesi güncellensin + "Eşitle"ye basılsın (ya da normal login/periyodik
  döngü) → geçmişte içeri alınan veri artık push edilir.
- **Test:** Bu spesifik ağ/UI-thread davranışı masaüstü entegrasyon testiyle DOĞRULANAMAZ (gerçek HTTP + gerçek
  Avalonia Dispatcher gerektirir); güvence kod incelemesi + canlı sunucu doğrulaması (önce/sonra `totalCount`)
  ile sağlandı. 530/530 (ilgisiz testler regresyon göstermedi).
- **Masaüstü 1.0.69'da canlı.**

---

### ADR-089 — Liste geliştirmeleri paketi: Tür eşleme, tanım düzenleme, 50-kar, sayfa boyutu, sıralama, Excel-grid (18.07.2026)

Kullanıcının 7 maddelik toplu isteği (2600+ kayıtla çalışırken fark ettikleri). Durum: infra+API+web TAMAM
ve canlıda; **masaüstü UI kısmı ayrı adımda** (bu ortamda Avalonia görsel doğrulanamıyor → dikkatli, build-doğrulamalı).

- **#7 "Tür" harf-duyarsız kanonik eşleme:** malzeme "Tür" bir tanım/lookup DEĞİL, serbest metindir → içe
  aktarımda "YEDEK PARÇA" kanonik "Yedek Parça" ile eşleşmiyordu (diğer tanımlar `ImportLookupResolver`'da
  zaten harf duyarsız). Çözüm: `MaterialType.Normalize` (Application/Ui) + `MaterialService.Create/Update`
  yazarken normalize + **Migration048** mevcut yanlış-harfli veriyi bir kez düzeltir (C# ile — SQLite upper()
  Türkçe'yi çeviremez). Bilinmeyen tür serbest kalır.
- **#4 Tanım düzenleme:** `LookupService.Rename` vardı ama API'de YALNIZ süper admine kapalıydı; "definitions/
  Edit" yetkisine açıldı (Ekle/Sil ile aynı model). Web DefEditor edit'i herkese gösterir (sunucu yetkiyi
  zorlar); masaüstü Tanımlar ekranına satır-içi düzenleme (`LookupRowViewModel` + Rename) eklendi.
- **#6 50 karakter sınırı:** yeni tanım + rename adında (LookupService + PersonnelTitle sunucu tarafı + UI MaxLength).
- **#1 Sayfa boyutu:** varsayılan 50→**25**; kullanıcı değiştirirse KİŞİYE ÖZEL hatırlanır (Migration049
  `page_size` sütunu + `/api/me/list-prefs`).
- **#5 Başlıkla sıralama:** kolon başlığına tıkla → metin A→Z/Z→A, SAYISAL küçük→büyük/büyük→küçük (ham değeri
  CAST). `GridQuery` + `SearchGrid`'e sort/desc; API grid uçlarına parametre. Türkçe sıralama için
  `SqliteConnectionFactory`'ye **TRNOCASE** collation (SQLite NOCASE yalnız ASCII'ydi → "Çınar" "Zeytin"den
  sonra geliyordu; artık Ç, C'den sonra).
- **#3 Excel-benzeri grid + kolon genişliği:** web'de MudTable → sabit-düzen HTML tablo + yatay kaydırma
  (pencere küçülünce taşma/kayma YOK) + CSS `resize` ile sürüklenebilir başlık + "Genişlikleri kaydet"
  (JS `dwReadColWidths` → `/api/me/list-prefs/.../widths`, Migration049 `widths_json`, KİŞİYE ÖZEL — kullanıcı
  onayı).
- **Test:** MaterialTests(+4 Tür) · LookupDedupTests(+2 50-kar) · MaterialGridTests(+2 sıralama) ·
  VehicleGridTests · UserListPreferenceTests(+3 sayfa/genişlik). 523/523.
- **Masaüstü UI (#2/#5/#3) — build-doğrulamalı, ayrı adımda TAMAMLANDI:**
  - **#2** sayfalama (numaralı sayfa + kayıt bilgisi) tablonun üstüne, en sola taşındı (eski alt-sağ konum kaldırıldı).
  - **#5** başlığa tıklayınca sıralama: `SortHeader` (Grid alt sınıfı, YENİ ControlTheme GEREKMEZ — Button'ın
    zaten var olan "Ghost" stiliyle çalışır, kasıtlı düşük-risk tasarım) + `IListGridViewModel` arayüzü
    (MaterialsViewModel/VehiclesViewModel ortak sözleşmesi: SortState, SortByCommand, ColWidths metotları) —
    3. tıkta sıralama kapanır (doğal sıra).
  - **#3** Excel-benzeri: tablo `ScrollViewer` (yatay kaydırma, `HorizontalAlignment="Left"` ile doğal genişlik
    ölçülür → pencere küçülünce artık TAŞMA/KAYMA olmaz) içine alındı; başlık hücrelerine 6px sürükleme
    tutamağı eklendi (`SortHeader`'ın kendi `Border` grip'i) — genişlik `UserListPreferenceService` (ADR-089
    üstteki bölüm) ile KİŞİYE ÖZEL kalıcı. SharedSizeGroup mekanizması BOZULMADI (header/satır senkronu hâlâ
    ondan); sürükleme yalnız o ölçümün ALT SINIRINI (MinWidth) büyütür — küçültme, satırın kendi doğal içerik
    genişliğinin altına inemez (WPF/Avalonia SharedSizeGroup'un bilinen bir sınırlaması; kabul edilen ödün).
  - **⚠️ Görsel doğrulama YAPILAMADI** (bu ortamda Avalonia'yı çalıştırıp tıklama/sürükleme testi yapacak
    araç yok) — yalnız temiz derleme ile güvence alındı. Kullanıcının canlı ortamda görsel onayı gerekir.

---

### ADR-088 — Sayısal kolon filtresi: tam-sayı/karşılaştırma/aralık (18.07.2026, TAMAM — infra+web+masaüstü)

- **Bağlam:** ADR-087'nin filtre motoru (`GridQuery`) HER kolonda "içerir" (`LIKE '%terim%'`) arıyordu.
  Kullanıcı: "stokta sadece 5 olanları listelemek istiyorum ama bütün içinde 5 olan malzemeler listeleniyor" —
  "5" yazınca 15/25/50/0.5 de eşleşiyordu (sayısal kolonda "içerir" anlamsız/yanıltıcı).
- **Karar:** `ListColumn`'a `IsNumeric` bayrağı eklendi; Malzemede **Birim Fiyat/Min Stok/Stok**, Araçta
  **Üretim Yılı/Sayaç** artık sayısal işaretli. `GridQuery.Build`, `ColumnKind.Numeric` işaretli bir kolon
  için filtre metnini SIRAYLA dener: (1) karşılaştırma `>5`/`<5`/`>=5`/`<=5`, (2) aralık `5-10` (iki uca dahil;
  negatif sınır destekli: `-9--5`), (3) tam sayı `5` (artık 15/25/50'yi YAKALAMAZ). Hiçbiri uymazsa (kullanıcı
  sayısal olmayan bir şey yazdıysa) eski "içerir" davranışına DÜŞER — filtre kutusu asla sessizce hiçbir şey
  yapmaz, davranış her zaman öngörülebilir.
- **Ham kolon karşılaştırması:** sayısal kolonlarda biçimlendirilmiş metin (`stock_text`="5.00") değil, HAM
  decimal-string alan (`stock_raw`) `CAST(... AS REAL)` ile karşılaştırılır — ondalık/virgül/negatif doğru
  çalışsın diye. Fiyat/Min Stok negatif olamaz ama **Stok** olabilir (bkz. ADR-086) — negatif tam sayı/aralık
  sınırı bu yüzden desteklenir.
- **Kapsam dışı bırakılmadı, GENİŞLETİLMEDİ:** metin kolonları (Kod/Ad/Marka/Kategori…) davranışı AYNI kaldı
  ("içerir" + "başlangıca göre" öncelik) — bu ADR yalnız sayısal kolonları etkiler.
- **UI:** Web + Masaüstü filtre kutularına ipucu/araç-ipucu eklendi ("Tam sayı: 5 · Karşılaştırma: >5 <5
  >=5 <=5 · Aralık: 5-10") — kullanıcı söz dizimini ezberlemek zorunda kalmasın.
- **Test:** `MaterialGridTests` (+8) + `VehicleGridTests` (+3) — tam sayı artık içermez, negatif açılış stoğu
  tam eşleşir, karşılaştırma operatörleri (`Theory`), aralık iki uca dahil, ondalık virgül, tanınmayan söz
  dizimi "içerir"e düşer. 509/509.

---

### ADR-087 — Malzeme/Araç Listesi: kolon bazlı filtre + sayfalama + kişisel kolon seçimi (17.07.2026, TAMAM — infra+API+web+masaüstü)

- **Bağlam:** Kullanıcı, malzeme dosyasını (2507 satır) düzeltip içeri aldıktan sonra fark etti: "2600 üstünde
  kayıt olduğu için geliştirme gerekli." İstek üç parça: (1) sütun bazlı filtreler ("içerir" + "başlangıca
  göre" arama), (2) sayfa boyutu seçimi + numaralı (1,2,3…) sayfalama, (3) — soru sorulup netleştirilince —
  hangi kolonların gösterileceğini sağ tık → "Kolonları Ayarla" ile seçebilme, **kişiye özel** (kullanıcı: "bu
  ayar işlemleri her kullanıcıya özel olsun, farklı kullanıcıda görünmesin").
- **Gizli kusur ortaya çıktı:** Malzeme/Araç LİSTE EKRANLARI da (import/export'tan bağımsız olarak)
  `MaterialService.List`/`VehicleService.List`'in **200 satır varsayılanına** dayanıyordu — 2600+ kayıtlı bir
  firmada liste ekranı sessizce yalnız ilk 200'ü gösteriyordu. Yeni `SearchGrid` uçları bunu ATLAR (gerçek
  `COUNT(*)` + `LIMIT/OFFSET`); eski `List(search)` uçları DOKUNULMADAN kaldı (Stok/Talep/Bakım gibi ekranlardaki
  hızlı-arama seçiciler onu kullanır).
- **Kolon kataloğu — TEK KAYNAK:** `DepoWise.Application/Ui/ListColumns.cs` (`MaterialListColumns`,
  `VehicleListColumns` — anahtar+etiket+varsayılan-görünür listesi). Web'in Application'a referansı olmadığından
  aynı liste `DepoWise.Web/Services/ListColumns.cs`'te AYNADIR (VehicleStatus ile aynı ikiz-dosya deseni).
  Kapsam = yeni kayıt formundaki HER alan, fotoğraf HARİÇ (kullanıcı isteği); "Açılış Stok" da BİLİNÇLİ OLARAK
  yok (kartın kalıcı alanı değil, yalnız kayıt anındaki bir hareket) — "Şablon" alanı da yok (form doldurma
  kolaylığı, kalıcı alan değil — malzeme içe aktarımındaki "Şablon" istisnasıyla AYNI gerekçe).
- **Sorgu motoru — `GridQuery` (Infrastructure/Database, paylaşılan):** her filtre alanı "içerir" (`LIKE
  '%terim%'`) arar; birden çok filtre aktifken "başlangıca göre" önceliği DETERMİNİSTİK sırayla uygulanır
  (kataloğun sabit sırasına göre, hangi kutunun önce doldurulduğuna bakılmaksızın). Hesaplanan/join'lenmiş
  kolonlar (stok bakiyesi, durum etiketi, uyumlu araç listesi gibi) SQL WHERE'de doğrudan kullanılamadığından
  (`SELECT * FROM (iç sorgu) t WHERE ...`) derived-table sarma deseni kullanılır — ham VE hesaplanan HER kolon
  aynı filtre/sıralama mantığından geçer. `MaterialService.SearchGrid` / `VehicleService.SearchGrid` bu deseni
  kullanır; `GridResult<T>` (Items+TotalCount+Page+PageSize+TotalPages) numaralı sayfalamayı besler.
- **Kolon tercihi — KİŞİSEL (Migration 047, `user_list_preferences`):** anahtar `(user_id, list_key)` — FİRMA
  değil, doğrudan kullanıcı (aynı firmadaki iki kullanıcı bile birbirinin seçimini görmez). Web: sunucu tarafında
  (`GET/POST /api/me/list-columns/{listKey}`, oturumdan user_id zorlanır). Masaüstü: KENDİ yerel SQLite'ında
  (aynı migration, ayrı anlam — dual-schema deseni ama bu kez "sunucu/yerel" değil "web/masaüstü" ayrımı; iki
  taraf SENKRONLANMAZ, kasıtlı — bir kullanıcının web'deki kolon seçimi masaüstünü etkilemez, ekranlar farklı
  kolon setleri sunabilir).
- **UI:** Web (MudBlazor) — her görünür kolon için `MudTextField` filtre kutusu + `MudPagination` (native
  numaralı sayfalama) + sağ-tık (`@oncontextmenu`) açılan `ColumnPickerDialog`. Masaüstü (Avalonia) — MudTable
  yok; kolon görünürlüğü SABİT XAML kolonları + yeni `Conv.ColumnVisible` converter (Auto+SharedSizeGroup kolon,
  görünmeyince 0'a çöker) ile çözüldü; sayfalama Prev/Next + numaralı buton `ItemsControl`; kolon seçici
  `ColumnPickerWindow` (ConfirmWindow ile AYNI modal desen). `MaterialRow`/`VehicleRow` eski 8-parametreli
  çağrılarla (Muadil Malzeme seçici) GERİYE UYUMLU — yeni alanlar varsayılan değerli, sonuna eklendi.
- **Test:** `MaterialGridTests` (12) + `VehicleGridTests` (7) + `UserListPreferenceTests` (5) — içerir arama,
  başlangıca göre öncelik, büyük/küçük harf duyarsız, birden çok filtre birleşimi, join'li/hesaplanan kolon
  filtresi, sayfalama (toplam/sayfa sayısı/tekrarsız/sınır kırpma), tenant izolasyonu, kişisel tercih izolasyonu.
  497/497.
- **⚠️ Masaüstü UI görsel olarak doğrulanamadı** (bu ortamda Avalonia masaüstü uygulamasını çalıştırıp
  etkileşimli test edecek bir araç yok) — yalnız temiz derleme + backend testleriyle güvence alındı. Web tarafı
  gerçek tarayıcıda uçtan uca doğrulandı (filtre/sayfalama/kolon seçimi/kalıcılık).

---

### ADR-086 — Açılış stoğu NEGATİF olabilir (17.07.2026, TAMAM — infra+API+web+masaüstü)

> ⚠️ Bu, `CLAUDE.md` §4 "negatif stok" değişmezinin BİLİNÇLİ ve SINIRLI bir yorumudur. Kullanıcının açık
> talebi (§1) bu satırın üstündedir; karar burada kayıt altına alınmıştır.

- **Bağlam:** Kullanıcının babasının gerçek malzeme dosyasında (2507 satır) 63 satırda **Açılış Stok negatif**
  (örn. −59, −1, −78). İçe aktarım bunları reddediyordu. Kullanıcı: "eksi stok kontrolünü kaldıralım.
  sonradan projemizi satın alan firmalar mevcut stoklarını ekleyebilirler." — yani sistemi devralan bir firma
  mevcut/eksik başlangıç stoğunu OLDUĞU GİBİ girebilmeli.
- **KAPSAM — yalnız BAŞLANGIÇ değeri gevşetildi; operasyonel koruma AYNEN korunur:**
  - **Gevşetilen:** açılış/ilk stok girişi (`OpeningStockService.RecordOpening`, malzeme içe aktarımı,
    web + masaüstü malzeme formu, `POST /api/materials`). Artık negatif açılış kabul edilir; yalnız **sıfır**
    reddedilir (anlamsız hareket).
  - **KORUNAN (dokunulmadı):** operasyonel ÇIKIŞ'ın negatif-bakiye engeli (`StockService.ApplyDelta`,
    `allowNegative:false`) — bir çıkış bakiyeyi eksiye DÜŞÜREMEZ. Bu §4'ün asıl koruduğu kuraldır.
  - Fiyat ve Min Stok negatif OLAMAZ (eşik/tutar anlamsız) — yalnız STOK MİKTARI negatif olabilir.
- **Ledger sözleşmesi korunur (kritik tasarım kararı):** negatif açılış, `stock_movements`'a **quantity DAİMA
  pozitif + direction=−1** olarak yazılır (ör. −9 → dir=−1, qty=9). Neden: (1) senkron içerik doğrulaması
  (`BusinessSyncService`) `stock_movements.quantity` negatifse satırı reddeder → hareket düzeyi kalkanı
  KORUNUR; (2) `RecomputeBalances` = Σ(yön×miktar) doğru kalır (−1×9 = −9). Türetilmiş **bakiye**
  (`stock_balances`) negatif olabilir → o alan senkron negatif-kalkanından ÇIKARILDI.
- **Bozuk-veri koruması nasıl sürüyor:** bakiye türetilmiştir; sunucu her push sonrası
  `RecomputeBalances` ile bakiyeyi hareketlerden yeniden hesaplar (otoriteli). Ham negatif `quantity` yalnız
  bozuk/kötü niyetli snapshot'tan gelebilir ve hâlâ reddedilir (`Apply_NegatifHareketMiktari_Reddedilir`).
- **Test:** `MaterialTests` (+3: negatif açılış yön/miktar & bakiye · sıfır reddedilir · RecomputeBalances
  round-trip) · `ImportFullFieldsTests` (+2: negatif açılış kabul & bakiye · negatif fiyat reddedilir) ·
  `BusinessSyncTests` (negatif BAKİYE artık uygulanır; negatif HAREKET miktarı hâlâ reddedilir). 473/473.
- **NOT (kapsam dışı, kullanıcıya bildirildi):** babanın dosyasındaki 2. sorun — her satırda para birimi
  "TL" yazılı (sistem TRY/USD/EUR bekler). Bu içe aktarım için hâlâ engel; kullanıcı Excel'de TL→TRY
  yapmalı (veya ayrı bir talep gelirse TL→TRY otomatik eşlemesi eklenir).

---

### ADR-085 — Makine "tanım sıfırlama" (17.07.2026, TAMAM — API+web+masaüstü)

- **Bağlam:** Kullanıcının babası bir makinede (DESKTOP-SIKIB3U, süper admin makinesi) önce bir "test
  firması" ile giriş yapmıştı; sonra aynı makinede **asıl firma** ile giriş yapamadığını düşündü. Kullanıcı
  istek: "makine yönetimi ekranına makine tanımı sıfırlama butonu oluştursak ve loginden sonra gelen ekranda
  eşitleme yaptıktan sonra kendini login ekranına yönlendirse. sonra ilk girilen kullanıcı ile firma makine
  tanımı tanımlansın."
- **Teşhis:** `sync_devices` zaten `(company_id, device_name)` çiftiyle anahtarlanır — aynı fiziksel makine
  birden çok firmada bağımsız satıra sahip olabilir; bu yüzden farklı firmayla giriş kendiliğinden ayrı bir
  satır açar. Asıl ihtiyaç, kullanıcının tarif ettiği **elle "tanımı temizle" düğmesi** — makineyi TÜM
  firmalardan tamamen koparıp "ilk kurulum" durumuna döndürmek (örn. bir makineyi bir müşteriden alıp
  başkasına devretmek, ya da kota/karışıklık şüphesinde temiz başlangıç). ADR-084 (firma yerel sıfırlama)
  ile KARIŞTIRILMAMALI: o firma verisini sıfırlar, bu makinenin firma/şube AİDİYETİNİ sıfırlar.
- **Karar:** `machine_resets` (Migration **046**), `company_local_resets`(ADR-084) ile AYNI iki-anlamlı
  desen ama **makine adıyla** anahtarlanır (firma ile DEĞİL) — çünkü sıfırlama isteği fiziksel makineye
  aittir, hangi firmayla giriş yapılırsa yapılsın algılanmalıdır:
  1. Süper admin Makine Yönetimi'nde bir satırın **"Tanımı Sıfırla"** butonuna basar → o makine adına ait
     **TÜM firmalardaki** `sync_devices` satırları silinir (`MachineResetService.RequestReset`) + künye yazılır.
  2. Masaüstü, girişten sonra eşitleme adımında (`LoginViewModel.FinalizeLoginAsync`, purge/yerel-sıfırlama
     kontrollerinden ÖNCE) künyeyi görür → `DesktopServices.MachineCompanyId/BranchId` + `MachineGate`
     önbellek dosyalarını (`machine_status.txt`/`machine_branch.txt`) temizler → **girişi iptal eder ve
     login ekranına döner** (`Back()`).
  3. Sonraki girişte makine "ilk kurulum" durumundadır (`MachineBranchId` boş) → giriş yapan **ilk
     kullanıcı** (süper admin değilse) mevcut "İlk Kurulum" onay akışıyla makineyi kendi firması/şubesiyle
     yeniden tanımlar; süper admin için de "makine firması" kısayolu (UseMachineCompany) temiz başlar.
- **ADR-084'ten kasıtlı FARKI — GİRİŞİ DURDURUR:** yerel sıfırlama girişe izin verip devam eder (veri
  sıfırdan yeniden dolar); makine sıfırlaması **durdurur** — çünkü sıfırlama sonrası makinenin hangi
  firmaya ait olduğu belirsizdir, o firmanın verisiyle devam etmek yanlış olur.
- **YIKICI DEĞİL:** iş verisi (malzeme/araç/stok/personel…) hiç etkilenmez; yalnız "bu makine hangi
  firmaya ait" bilgisi silinir. ADR-083'teki (kalıcı firma silme) ile karıştırılmamalı; özel kod GEREKMEZ.
- **Künye SİLİNMEZ:** çevrimdışı bir makine haftalar sonra açılsa bile isteği görüp bir kez uygular (ADR-083/
  084 ile aynı fail-safe ilkesi — çevrimdışıyken hiçbir şey silinmez).
- **Test:** `MachineResetTests` (8) — istek durumda görünüyor · tekrar istek zamanı güncelliyor · süper admin
  olmayan bırakamıyor · boş makine adı reddediliyor · **TÜM firmalardaki kayıtlar silinir** · başka makine
  etkilenmez · sıfırlama sonrası aynı makine adıyla farklı firmaya yeniden kayıt çalışıyor.

---

### ADR-084 — Firma "yerel sıfırlama" isteği (16.07.2026, TAMAM — API+web+masaüstü)

- **Bağlam:** Kullanıcı bir firmanın (Sevgi A.Ş.) bilgilerini/adını web'den güncelledi; bu firmayla 2 yerel
  makinede daha önce giriş yapılmıştı. "Bu bir soruna yol açar mı, ve bu firmanın TÜM yerel kayıtlarını
  (o makineler o an kapalı olsa bile) bir sonraki girişte bir kerelik temizleyecek bir yapı istiyorum" dedi.
- **Teşhis (rename'in etkisi):** Kod incelemesi iki ayrı davranış ortaya çıkardı:
  1. Firma **adı** her çevrimiçi girişte otomatik düzeliyordu (`CompanySyncService.MirrorLocalAsync`,
     `ON CONFLICT DO UPDATE SET name=...`) — sorun yoktu.
  2. **Diğer alanlar** (vergi no/dairesi, adres, telefon, e-posta, yetkili, kotalar) hiç aynalanmıyordu —
     yalnız `id` ve `name` okunup yazılıyordu. Web'de bunlar değişince yerel makinelerde **sonsuza kadar
     eski** kalıyordu. Bu, gerçek (küçük ama gerçek) bir kusurdu; **aynı oturumda düzeltildi** (aşağıya bkz).
- **Karar — iki parça:**
  1. **`MirrorLocalAsync` tüm alanları aynalar** artık (tax_no/tax_office/address/phone/email/
     authorized_person/max_users/max_admins/machine_quota) — yalnız isim değil. Bu düzeltme olmadan,
     aşağıdaki yeni özellik firma satırını sıfırladıktan sonra bu alanları **NULL/0** bırakırdı (eskiden
     "bayat" olan alanlar daha da kötüleşirdi) — bu yüzden ikisi birlikte yapıldı.
  2. **Yeni "Yerel Sıfırlama" isteği** (`company_local_resets`, Migration **045**) — ADR-083'ten (kalıcı
     silme) KASITLI olarak FARKLI bir mekanizma: firma **sunucuda durur**, erişim **engellenmez**; yalnız
     o firmanın makineleri bir sonraki **çevrimiçi** girişte kendi yerel kopyalarını **bir kez** temizleyip
     yeni-makine-ilk-girişiyle aynı yoldan sıfırdan yeniden doldurur.
- **Aynı tablo, iki anlam (server ↔ masaüstü):** `company_local_resets` şeması sunucuda VE her masaüstünün
  kendi yerel SQLite dosyasında **aynıdır** ama farklı yorumlanır: sunucuda "en son istenen zaman", her
  makinede "BU makinenin en son UYGULADIĞI zaman". Karşılaştırma `sunucu > yerel` ise wipe uygulanır ve
  yerel satır sunucunun zamanına eşitlenir — böylece istek **tam bir kez** uygulanır, tekrar tekrar değil.
- **"Makine o an kapalı olabilir" şartı:** İstek EPHEMERAL bir sinyal değil, sunucuda KALICI bir satırdır
  (silinene kadar durur). Makine hangi zaman çevrimiçi girişe geçerse (bugün, yarın, ay sonra) o zaman
  algılanır ve uygulanır — bekleme süresi sınırsızdır.
- **Sıra kritik (ADR-083 ile birebir aynı ilke):** kontrol, çevrimdışı kuyruk/push'tan ÖNCE çalışır — aksi
  halde makine, henüz temizlenmemiş eski veriyi sunucuya geri gönderirdi.
- **Silme mantığı ADR-083'teki `LocalPurgeService.PurgeLocalCompany` ile AYNIDIR** (kod tekrarı yok) — tek
  fark, bu akışta **giriş engellenmez**; wipe sonrası normal senkron adımları (mirror/pull) devam eder.
- **Kapsam dışı (ADR-083 ile aynı kullanıcı kararı):** masaüstünde yeni ekran yok; buton yalnız **web**
  Firma Tanım listesinde ("Yerel Sıfırlama İste" ikonu, süper-admin-only). Özel kod GEREKMEZ (bu, ADR-083'ün
  aksine YIKICI/erişim-engelleyici değildir — sunucu verisi hiç etkilenmez).
- **Test:** `CompanyLocalResetTests` (7) — istek durumda görünüyor · tekrar istek zamanı güncelliyor ·
  süper admin olmayan bırakamıyor · olmayan firma reddediliyor · kendi firman İÇİN de istek bırakılabiliyor
  (ADR-083'ten farkı) · başka firmaya sızmıyor.

---

### ADR-083 — Firma KALICI silme + "özel kod" (16.07.2026, TAMAM — API+web+masaüstü)

> ⚠️ **Bu ADR, `CLAUDE.md` §4'ün "Operasyonel kaydı fiziksel silme; iptal/ters kayıt ve audit kullan"
> kuralının BİLİNÇLİ ve SINIRLI bir istisnasıdır.** `CLAUDE.md` §1 gereği kullanıcının açık talebi bu
> dosyanın üstündedir; karar burada kayıt altına alınmıştır.

- **Bağlam:** Kullanıcı sistemi gerçek verilerle uçtan uca test etmek istiyor ve bunun için bir firmanın tüm
  kayıtlarını hem sunucudan hem makinelerden **tamamen** silebilmesi gerekiyor. Mevcut Firma Tanım ekranı
  firmayı yalnız **pasife alır** (soft delete) — veri diskte ve makinelerde durmaya devam eder, temiz test
  ortamı kurulamaz.
- **Karar:** Yeni **"Kalıcı Silme"** ekranı (yalnız **web**, `purge_company`, süper-admin-only, devredilemez).
  Seçilen firmanın tüm satırları `company_id` üzerinden fiziksel silinir; fotoğraflar (`files/{id}`) ve makine
  yedekleri (`backups/{id}`) diskten silinir. **Kapsam yalnız FİRMA bazlıdır** — normal iş akışlarında silme
  YASAK olmaya devam eder (iptal/ters kayıt + audit).
- **Kilit (çok katmanlı, fail-closed):** süper admin **+ özel kod + şifre + firma adını birebir yazma**.
  - **Özel kod:** şifreden AYRI bir sır; yalnız süper adminde vardır, ilk **web** girişinde oluşturulur,
    `users.special_code_hash`'te **hash**'lenir. Unutulursa süper admin **şifresiyle** yenisi belirlenir
    (ekran kalıcı kilitlenmesin — kullanıcı kararı). Kod yoksa doğrulama **daima false** (kodsuz ekran açılmaz).
  - **Kendi firmanı silmek YASAK:** ADR-064'te kendi firmasını silen süper admin sistemden kilitlendi,
    ADR-068'de oturumu 401'e düştü. Kalıcı silmede telafisi YOK → hem serviste hem ekranda engellenir.
- **Künye (tombstone) — `company_purges`:** silme sonrası kalan tek iz. Purge sırasında **asla silinmez**.
  Masaüstü giriş sonrası eşitleme adımında `/api/sync/purge-status` ile bunu sorar; "silinmiş" ise **yerel
  veriyi temizler ve login'e döner**. Künye olmasaydı çevrimdışı bir makine kendi kopyasını sunucuya geri
  push edip **veriyi diriltirdi**.
- **Sıra kritik:** masaüstünde purge kontrolü, çevrimdışı kuyruk (`sync_outbox`) sunucuya **işlenmeden ÖNCE**
  çalışır — aksi halde makine silinmiş firmanın kayıtlarını geri gönderir.
- **Fail-safe:** sunucuya erişilemezse (çevrimdışı, `null`) yerel veriye **DOKUNULMAZ**. Silme yalnız sunucu
  açıkça "silindi" dediğinde uygulanır — "cevap alamadım" yerel veri silme gerekçesi değildir.
- **Korunanlar:** `schema_migrations`, `sqlite_sequence`, `company_purges` ve sistem rolleri
  (`roles.company_id IS NULL` = tüm firmalar) — aksi halde purge'den sonra hiçbir firmada rol atanamazdı.
- **Kapsam dışı (kullanıcı kararı):** masaüstünde **yeni ekran yok** ve **login'de özel kod alanı yok**;
  masaüstü yalnız silmeyi algılar. Silme işlemi web'den yapılır.
- **Şema:** Migration **044** (`users.special_code_hash` + `company_purges`).
- **Test:** `CompanyPurgeTests` (9) — kendi firması silinemez · süper admin olmayan silemez · firma+verisi gider
  ve künye kalır · silinen firmanın kullanıcısı giriş yapamaz · **başka firmaya dokunmaz** · sistem rolleri
  korunur · künye yalnız silinmiş firmada döner · özel kod fail-closed/kısa kod reddi/rol kısıtı.

---

## Faz 00 kararları (2026-06-26)

### ADR-001 — Çözüm/klasör düzeni
- **Bağlam:** Boş repo; web + masaüstü + ortak sözleşme bir arada.
- **Karar:** `src/DepoWise.Desktop` (Avalonia UI), `src/DepoWise.*` katman projeleri (Domain/Application/Infrastructure), `web/` (Next.js), `docs/`, `artifacts/`. Tek `.sln` masaüstü tarafını toplar.
- **Alternatif:** Tek monolit proje — reddedildi (test izolasyonu ve katman ayrımı zorlaşır).
- **Sonuç:** Faz 01'de iskelet bu düzene göre kurulacak.

### ADR-002 — Masaüstü mimarisi
- **Karar:** .NET 8, Avalonia, MVVM (CommunityToolkit.Mvvm), Dapper, SQLite. UI thread'de DB/ağ yok; Dapper parametreli; transaction tek connection üzerinde.
- **Gerekçe:** Analiz §3 ve `.claude/rules/desktop.md` ile birebir.

### ADR-003 — Yerel DB yolu ve bağlantısı
- **Karar:** SQLite mutlak yol `%LOCALAPPDATA%\DepoWise\Data\<environment>\depowise.db`. Connection: `Cache=Private`, WAL, `foreign_keys=ON`, `busy_timeout=5000`. Açılışta host/DB-yolu/journal_mode/health loglanır.
- **Gerekçe:** COMODO sandbox'ın sanal-DB tuzağını önler (relative path yasak).

### ADR-004 — COMODO güvenli çalıştırma
- **Karar:** Debug'da `UseAppHost=false`. Uygulama yalnız `dotnet build` + `dotnet run/--project` veya `dotnet <dll>` ile çalışır. Proje `.exe`/`.bat` ASLA çalıştırılmaz; PreToolUse hook bunu zorlar.
- **Sonuç:** Doğrulandı (hook + Directory.Build.props mevcut ve tutarlı).

### ADR-005 — Merkezi veri ve API
- **Karar:** PostgreSQL + Drizzle + migration; API `/api/v1`, ortak hata modeli + correlation id + OpenAPI sözleşmesi. `company_id` yalnız server session'dan; payload'dan tenant kabul edilmez (fail-closed).
- **Not:** Üretim PG sağlayıcısı tek markaya bağlanmaz (KNOWN_ISSUES).

### ADR-006 — Kritik operasyon bütünlüğü
- **Karar:** Stok/sayaç/yakıt/bakım/onay işlemlerinde LWW yasak; `operation_id` ile idempotency + transaction + audit/outbox tek transaction. Operasyonel kayıt fiziksel silinmez (iptal/ters kayıt). Stok hareket defteri tek doğru kaynak.
- **Gerekçe:** Analiz §7 ve §11 kabul testleri.

### ADR-007 — Para, zaman, kimlik, dosya
- **Karar:** Para `decimal` + `currency_code`, kur snapshot; zaman merkezi UTC / sözleşmede Unix ms; ana kayıtlar UUID/ULID, kullanıcı belge no ayrı; fotoğraf `file_records` metadata + storage provider (DB base64 değil).
- **Gerekçe:** Analiz §7, §6.16.

---

## Faz 01 kararları (2026-06-26)

### ADR-008 — Çözüm yerleşimi ve hedef framework
- **Karar:** `src/DepoWise.{Domain,Application,Infrastructure,Desktop}` + `tests/DepoWise.Tests` + `apps/web`. Tüm .NET projeleri **net8.0** (Avalonia template'in ürettiği net10.0 hedefi düşürüldü; SDK 8.0.422).
- **Gerekçe:** CLAUDE.md .NET 8 değişmezi; katmanlı bağımlılık Domain←Application←Infrastructure←Desktop/Tests.

### ADR-009 — Ortak sözleşmelerin iki platformda eşlenmesi
- **Karar:** Hata modeli (`ApiError`+`ErrorCodes`), keyset pagination (`PageRequest`/`PagedResult`), zaman (UTC + Unix ms) ve correlation_id hem .NET (`Application/Common`) hem web (`lib/contracts.ts`) tarafında **birebir aynı kodlar/biçimle** tanımlandı. OpenAPI bu sözleşmeyi `apps/web/docs/openapi.yaml`'de belgeliyor.
- **Gerekçe:** Analiz §3/§5 fonksiyonel eşitlik; tek doğru sözleşme.

### ADR-010 — Config fail-closed
- **Karar:** Web `loadConfig()` zod ile doğrular; **Production**'da `DATABASE_URL`/`SESSION_SECRET` eksikse `ok=false` (health 503). Geliştirmede uyarı niteliğinde. Sırlar yalnız environment'tan.
- **Gerekçe:** Analiz §9 (başlangıçta eksik/zayıf sır fail-closed).

### ADR-011 — Güvenlik yükseltmesi (tedarik zinciri)
- **Bağlam:** `next@15.1.6` CVE-2025-66478 açığı içeriyordu.
- **Karar:** Yamalı `next@^15.5.19`'a yükseltildi (eslint-config-next eşlendi). "Gereksiz yükseltme yapma" kuralının istisnası: kritik güvenlik açığı (analiz §9 tedarik zinciri).
- **Sonuç:** Yükseltme sonrası typecheck/build yeşil.

---

## Faz 02 kararları (2026-06-26)

### ADR-012 — Migration stratejisi
- **Karar:** Yerel SQLite için kod tabanlı sürümlü migration (`IMigration`/`MigrationRunner`, `schema_migrations` izleme tablosu, her migration tek transaction, idempotent). Merkezi PostgreSQL için Drizzle Kit ile üretilen SQL migration dosyaları (`apps/web/drizzle`).
- **Gerekçe:** İki platform farklı motorlar; ortak şema kavramı korunur, her motor kendi migration aracını kullanır.

### ADR-013 — Standart kolon sözleşmesi
- **Karar:** Tüm operasyonel tablolar `id` (UUID/ULID, TEXT/text), `company_id`, `created_at`/`updated_at` (INTEGER/bigint Unix ms), `version` (optimistic concurrency), uygun olduğunda `is_deleted`. Para alanları decimal-as-TEXT (SQLite) / numeric (PG) + `currency_code`.
- **Gerekçe:** Analiz §7; tenant + soft-delete + concurrency + zaman tutarlılığı tek desende.

### ADR-014 — Tenant izolasyonu fail-closed
- **Karar:** `company_id` `TenantContext`/`TenantGuard` ile yalnız güvenilir bağlamdan; boşsa exception. Tüm okuma/yazma sorguları `TenantSql.ScopePredicate` kullanır. Regresyon: tenant izolasyon + başka-firma-silemez testleri.
- **Gerekçe:** Analiz §9; tenant kontrolü UI'a bırakılmaz.

### ADR-015 — Keyset pagination + soft-delete + audit
- **Karar:** Sayfalama keyset (created_at DESC, id DESC) + opak `Cursor`; toplam sayı zorunlu değil. Silme = `is_deleted=1` + version+1 (fiziksel silme yok). Kritik mutasyonlar `AuditWriter` ile aynı transaction'da audit yazar.
- **Gerekçe:** Analiz §7 (keyset kararlı sıralama), §2/§7 (silme yerine soft-delete/ters kayıt), §9 (audit).

---

## Faz 03 kararları (2026-06-26)

### ADR-016 — Parola hash algoritması (parite)
- **Karar:** PBKDF2-HMAC-SHA256, 100k iter, 16B salt, 32B hash; biçim `pbkdf2$sha256$<iter>$<saltB64>$<hashB64>`. Hem .NET (`Rfc2898DeriveBytes.Pbkdf2`) hem web (`node:crypto.pbkdf2`) aynı biçim → enroll/sync sırasında karşılıklı doğrulanabilir.
- **Alternatif:** BCrypt — reddedildi (iki platformda harici bağımlılık + parite zorluğu); PBKDF2 her iki runtime'da yerleşik.
- **Sonuç:** Parite testle doğrulandı (.NET + node:test).

### ADR-017 — Deny-by-default erişim kontrolü
- **Karar:** `AccessControl` UI ve API'de aynı sonucu üretir; izin kaydı yoksa erişim yok. Süper Admin/Firma Admini bypass. Dashboard/About herkese açık (yalnız okuma). Özel buton/alan da deny-by-default. API sınırında `Require*` → `ForbiddenException` (403).
- **Gerekçe:** Analiz §5/§9; yetki yalnız UI'a bırakılmaz.

### ADR-018 — Tenant kaynağı ve yetki yükseltme koruması
- **Karar:** `company_id` yalnız `SessionContext`'ten; istek payload'ındaki farklı company_id (süper admin değilse) 403. Firma Admini firma değiştiremez (foreign company → reddedilir, sessizce rescope EDİLMEZ). `RoleAssignmentGuard`: admin olmayan admin/süper-admin rolü atayamaz; süper admin yalnız süper admin tarafından oluşturulur.
- **Gerekçe:** Analiz §4/§9; tenant sızıntısı ve privilege escalation fail-closed.

### ADR-019 — Web içi TS import uzantıları (.ts)
- **Karar:** `lib/security` içi göreli importlar `.ts` uzantılı + `allowImportingTsExtensions`. Böylece aynı kaynak hem Next bundler ile derlenir hem de `node --test` (Node 24 type-stripping) ile harici test koşusunda çalışır.
- **Gerekçe:** Web için hafif birim test koşusu (ek bağımlılık olmadan) sağlanır.

---

## Faz 04 kararları (2026-06-27)

### ADR-020 — Ortak UI mantığı platform-bağımsız
- **Karar:** Menü, doğrulama (tarih/numerik), çoklu seçim ve alan görünürlüğü saf mantık olarak iki tarafta da yazıldı (`Application/Ui/*` ve `apps/web/src/lib/ui/*`), aynı kabul senaryolarıyla test edildi. Avalonia/React yalnız bu mantığı bağlar.
- **Gerekçe:** Analiz §5; web ve masaüstü fonksiyonel eşitlik tek kaynaktan.

### ADR-021 — Tarih ve arama davranışı
- **Karar:** Tarih GG/AA/YYYY KESİN biçim + gerçek takvim doğrulaması (.NET `TryParseExact None`; web Date.UTC geri-doğrulama). Aranabilir çoklu seçim Türkçe büyük/küçük harf duyarsız (.NET tr-TR `CompareInfo`; web `toLocaleLowerCase('tr')`); arama seçimi korur; "tümünü seç" yalnız filtre sonucunu ekler.
- **Gerekçe:** Analiz §5; CLAUDE.md Türkçe duyarsız arama standardı.

### ADR-022 — Merkezi tema/branding (sabit değil)
- **Karar:** Renk ve marka metinleri ekrana sabit yazılmaz. `app_settings` (Migration003, global/firma override) → `ThemeTokens`/`BrandingSettings`. Masaüstü `ThemeApplier` ile `Brand.*` DynamicResource; web CSS değişkenleri (`--brand-*`) kök `:root`/layout'tan. Ayar değişiklikleri audit'lenir.
- **Gerekçe:** Kullanıcı talimatı + analiz §5 (tema merkezi yönetilebilir).

---

## Faz 05 kararları (2026-06-27)

### ADR-023 — Firma yönetimi yalnız Süper Admin; tenant fail-closed
- **Karar:** Firma oluşturma/listeleme `CompanyService` ile yalnız Süper Admin; Firma Admini yalnız kendi firmasını görür, `EnsureAccess` başka firmaya erişimi 403'ler. Tüm org servisleri `company_id`'yi session'dan alır.
- **Gerekçe:** Analiz §4; normal admin firma sınırını aşamaz.

### ADR-024 — Kullanıcı şube kapsamı (user_scopes)
- **Karar:** `user_scopes` ile kullanıcı bazlı şube kapsamı. `ScopeResolver`: açık scope öncelikli; yoksa admin → tüm firma şubeleri, admin-olmayan kapsamsız → boş. Şube/personel seçim listeleri ve yazma `EnsureBranchAllowed` ile kapsam dışına taşamaz. Web `lib/org/scope.ts` aynı kararı saf fonksiyonla aynalar.
- **Gerekçe:** Analiz §5/§6.2 (seçim listeleri yalnız kullanıcı kapsamını getirir).

---

## Faz 06 kararları (2026-06-27)

### ADR-025 — Para ve stok temsili
- **Karar:** Para/miktar SQLite'ta TEXT (invariant decimal) + `currency_code`; .NET `Money` ve web `money.ts` ile taşınır. Float YOK. Desteklenen: TRY (baz) / USD / EUR. İşlem anı kuru `stock_movements.fx_rate` snapshot; manuel kur `fx_rates`.
- **Gerekçe:** Analiz §7 (decimal + currency, kur snapshot).

### ADR-026 — Stok hareket defteri ana kaynak; açılış stoğu hareket olarak
- **Karar:** `stock_movements` ana kaynak, `stock_balances` cache (yalnız ledger'la aynı transaction'da güncellenir). Açılış stoğu kart alanı DEĞİL `OpeningStockService` ile 'opening' hareketi; `operation_id` ile idempotent. Doğrudan bakiye set eden API yok.
- **Gerekçe:** Analiz §7/§2; bu fazda bakiye doğrudan değiştirilmez (Faz 07 diğer hareket tipleri).

### ADR-027 — Muadil ve uyumlu araç ilişkileri
- **Karar:** Muadil simetrik (servis çift yön yazar) + self-FK CHECK + döngü güvenli BFS grup çözümü. Uyumlu araç çoklu seçim `material_compatible_vehicles` (vehicle_id FK Faz 08'e ertelendi). Araç→uyumlu malzeme sorgusu güncel stoğu (stock_balances join) gösterir.
- **Gerekçe:** Analiz §6.5; çift yönlü, döngü güvenli ilişki.

---

## Faz 07 kararları (2026-06-27)

### ADR-028 — Stok işlemleri concurrency: IMMEDIATE transaction
- **Karar:** Tüm bakiye değiştiren akışlar `BeginTransaction(deferred: false)` (BEGIN IMMEDIATE) ile yazma kilidini baştan alır → eş zamanlı çıkışlar serialize olur; ikinci işlem güncel bakiyeyi okuyup negatif guard'a takılır. Negatif düşüş `NegativeStockException` + rollback.
- **Alternatif:** Koşullu UPDATE (quantity TEXT karşılaştırması zor) — reddedildi. IMMEDIATE + busy_timeout yeterli ve sade.
- **Kanıt:** `EsZamanli_IkiCikis_NegatifStokOlusturamaz` (Parallel.For).

### ADR-029 — Belge/hareket modeli ve iptal = ters kayıt
- **Karar:** `stock_documents` (in/out/transfer/count) + hareketler belgeye bağlı; doc_no otomatik (PREFIX-YYYY-NNNN). Transfer kaynak çıkış + hedef giriş aynı group_id'de atomik. İptal hareketi FİZİKSEL SİLMEZ: ters hareket üretir, orijinali is_reversed=1 işaretler, belge cancelled. operation_id ile tüm akışlar idempotent.
- **Gerekçe:** Analiz §7 (silme yerine ters kayıt, idempotency, transaction).

### ADR-030 — Bakiye material-global (şube bazlı ertelendi)
- **Karar:** `stock_balances` material düzeyinde tek bakiye; transfer toplam stoğu değiştirmez (net-zero), hareketlerde from/to şube kayıtlı. Şube bazlı bakiye/negatif kontrolü sonraki bir fazda eklenecek (R13).
- **Gerekçe:** Faz 06 şemasını bozmadan ilerlemek; MVP için yeterli, kayıt izi şube bilgisini taşıyor.

---

## Faz 08 kararları (2026-06-27)

### ADR-031 — Sayaç geriye gitmeme + iki yöntem
- **Karar:** `MeterRule` ortak (web+masaüstü). `SetMeter` (doğrudan form düzenleme) geriye gidişi `MeterBackwardException` ile reddeder. `AdvanceMeter` (bakım/yakıt) ileri-only: yeni>mevcut ise ilerletir+loglar, değilse no-op (geçmiş tarihli düşük okumayı ENGELLEMEZ). Her ilerleme `vehicle_meter_logs`'a (old,new,source) yazılır. Güncellemeler IMMEDIATE transaction.
- **Gerekçe:** Analiz §7; kullanıcı talimatı "sayaç geriye düşmesin + tüm değişimler loglansın".

### ADR-032 — Şablondan doldurma (kullanıcı değeri öncelikli) + malzeme kopyalama
- **Karar:** Araç oluştururken `TemplateId` varsa boş alanlar şablondan doldurulur (`?? ` ile; kullanıcı girdisi ezilmez). Şablonun uyumlu malzemeleri yeni aracın `material_compatible_vehicles` kayıtlarına AYNI transaction'da kopyalanır (INSERT OR IGNORE). Otomatik iç kod önek+en büyük no+1 (genişlik korunur).
- **Gerekçe:** Analiz §6.7; AlpDepo deseni, kontrollü doldurma.

---

## Faz 09 kararları (2026-06-27)

### ADR-033 — Bakım atomik akışı + tek stok düşümü
- **Karar:** `MaintenanceService.Save` IMMEDIATE transaction'da: bakım kaydı + her malzeme için TEK 'usage' hareketi (negatif guard, fiyat snapshot `maintenance_materials.unit_price`) + sayaç ileri (AdvanceMeter mantığı) + sonraki hedef + audit. operation_id idempotent (ikinci çağrı çift düşmez). İptal: 'usage_reverse' +1 ile stok geri, kayıt is_cancelled (fiziksel silme yok), idempotent.
- **Gerekçe:** Analiz §7 (tek transaction, tek düşüm, ters kayıt, idempotency).

### ADR-034 — Uyarı eşikleri ve döngü
- **Karar:** `AlertRules` (web+masaüstü): progress=tüketilen/interval; <0.85 Normal, [0.85,0.95) Approaching, [0.95,1.0) Critical, ≥1.0 Overdue. Tüketilen km/saat = current_meter − performed; gün = now − performed_date. Uyarı her (araç,tanım) için EN SON non-cancelled bakımdan hesaplanır → yeni bakım girilince otomatik temizlenir.
- **Gerekçe:** Kullanıcı talimatı + analiz §6.8.

---

## Faz 10 kararları (2026-06-27)

### ADR-035 — Yakıt dağıtımı atomik + fiyat snapshot
- **Karar:** `FuelService.Distribute` IMMEDIATE transaction'da: depo bakiye yeterlilik kontrolü + dağıtım (birim fiyat **snapshot**; verilmezse güncel=son depo fiyatı) + araç sayacı ileri (MeterRule) + meter log + audit; operation_id idempotent. Depo bakiyesi = Σgiriş − Σdağıtım (tüm zamanlar). Güncel fiyat değişimi geçmiş dağıtımları ETKİLEMEZ.
- **Gerekçe:** Analiz §7 (tarihsel maliyet snapshot, sayaç bütünlüğü, transaction).

### ADR-036 — Günlük Faaliyet bakım = tek kayıt (çift düşüm yok)
- **Karar:** `DailyActivityService.SaveMaintenanceActivity` ortak `MaintenanceService.Save`'i çağırır (tek `vehicle_maintenances` + tek stok düşümü). `daily_activities` yalnız `maintenance_id` referansı + `stock_processed=1` tutar; burada stok DÜŞMEZ. Böylece kayıt hem Bakım Takibi hem Günlük Faaliyet ekranında görünür, veri tek.
- **Gerekçe:** Kullanıcı talimatı + analiz §6.11 (tek kayıt prensibi).

---

## Faz 11 kararları (2026-06-27)

### ADR-037 — Talep durum makinesi + onay stok düşürmez
- **Karar:** `RequestStatusMachine` (web+masaüstü) geçişleri kısıtlar: draft→pending→approved/rejected/cancelled; approved/rejected/cancelled terminal. Çift onay/yetkisiz/geçersiz geçiş fail-closed. Onay/ret approve butonu + requests edit yetkisi ister; tenant ownership zorunlu. **Onay stok bakiyesini DEĞİŞTİRMEZ.** Stok yalnız `CreateIssueFromRequest` ile (onaylı talep → açık `StockService.IssueOut`). Belge no TLP-YYYY-NNNN tenant/yıl benzersiz.
- **Gerekçe:** Analiz §6.12/§7; kullanıcı talimatı (onay stok düşürmez, stok yalnız gerçek çıkış/teslim).

### ADR-038 — PDF üretimi (QuestPDF)
- **Karar:** Masaüstü/Infrastructure PDF QuestPDF Community ile (`IRequestPdfService`/`RequestPdfService`), `RequestPdfModel` ortak veri modeli; Türkçe karakter korunur. Web tarafı aynı modeli kullanır; binary render hattı sonraya bırakıldı (R16).
- **Gerekçe:** Analiz §6.12 (PDF çıktısı); .NET'te yerleşik, lisans Community.

---

## Faz 12 kararları (2026-06-27)

### ADR-039 — Rapor kapısı + tenant/firma filtresi
- **Karar:** `ReportGate.EnsureRunnable` ağır raporu `Executed=false` iken çalıştırmaz (kullanıcı Sorgula/Filtrele'de Executed=true yapar). Raporlar tenant + "reports" permission fail-closed. Firma filtresi yalnız Süper Admin'e görünür (`ShowCompanyFilter`); hedef firma `TenantAccessGuard.ResolveCompanyId` ile çözülür (normal admin başka firma isteyemez). Web `lib/reports/gate.ts` aynı.
- **Gerekçe:** Analiz §6.14/§7 (ağır rapor manuel tetik, tenant sızıntısı yok).

### ADR-040 — Excel export (ClosedXML) + import dry-run politikası
- **Karar:** `TableModel` → `.xlsx` ClosedXML ile (sayısal hücreler sayı). İçe aktarım: örnek başlık + ön kontrol + **dry-run (DB'ye yazmaz)** + satır bazlı hata (ilk 15) + commit. Politika: **satır bazlı** (bir hatalı satır diğerlerini bozmaz), commit `MaterialService.Create` ile iş kurallarını atlamaz (tenant/permission/kod benzersiz/currency). Web `lib/reports/import.ts` aynı doğrulama.
- **Gerekçe:** Analiz §6.15; kullanıcı talimatı (örnek dosya + ön kontrol + satır hata + dry-run).

---

## Faz 13 kararları (2026-06-27)

### ADR-041 — Dosya güvenliği + ayrık dosya kaydı (base64 yok)
- **Karar:** `FileValidation` ortak: ≤7MB, izinli MIME (jpeg/png), **magic-byte** ile gerçek tip (uzantı/declared MIME'a güvenmez; sahte içerik + MIME-içerik uyuşmazlığı reddi), güvenli ad. Fotoğraflar `IFileStorageProvider` (yerel disk; swappable) ile saklanır; operasyonel tabloya **base64 yazılmaz** — yalnız `file_records` metadata (provider/key/mime/size/sha256). Storage kök içine sınırlı (path traversal koruması). Web `lib/files/validation.ts` aynı.
- **Gerekçe:** Analiz §6.16/§9.

### ADR-042 — Çöp Kutusu + yedekleme
- **Karar:** `TrashService` yalnız master-data soft-delete kayıtlarını listeler/geri yükler; özel buton (RestoreTrash) + **yeniden doğrulama (reauth)** + tenant fail-closed. Operasyonel kayıtlar çöp kutusunda DEĞİL (iptal/ters kayıt). `BackupService`: `VACUUM INTO` tutarlı yedek, 30 gün retention, `PRAGMA integrity_check`, geri yükleme admin+reauth ve `SqliteConnection.ClearAllPools()` ile dosya kilidi olmadan.
- **Gerekçe:** Analiz §6.17-6.18/§9; gerçek geri yükleme + bütünlük kanıtı.

---

## Faz 14 kararları (2026-06-27)

### ADR-043 — Offline write + outbox atomik; idempotent push
- **Karar:** Yerel write ve `sync_outbox` AYNI SQLite transaction (`OutboxWriter.Enqueue`); operation_id + payload_hash + base_version taşınır; rollback hiçbirini bırakmaz. Push'ta operation_id `sync_inbox` ile idempotent (ikinci ulaşım → already_applied; çift kayıt yok). Offline veri yeniden açılışta kalıcı.
- **Gerekçe:** Analiz §8 (yerel+outbox tek transaction, idempotent retry).

### ADR-044 — Kritik işlemlerde LWW yasak; sunucu otoriteli + conflict
- **Karar:** Kritik entity'lerde (stok/sayaç/yakıt/bakım/onay) basit LWW YOK: sunucu doğrulaması zorunlu (validator yoksa/red ise rejected + `sync_conflicts`). Düşük-riskli kart alanlarında base_version uyuşmazlığı → conflict (kör overwrite yok). Pull seq cursor; bozuk sayfada rollback + cursor sabit. Cihaz: tek-kullanımlık 10 dk enrollment anahtarı + master onay + token (hash saklı); pending/revoked cihaz push/pull'da 403.
- **Gerekçe:** Analiz §8-9; kullanıcı talimatı (LWW yok, operation_id + sunucu doğrulaması zorunlu).

---

## Faz 15 kararları (2026-06-27)

### ADR-045 — Sürüm yönetimi + güncelleme yaşam döngüsü
- **Karar:** `ReleaseService` (yalnız Süper Admin) `app_releases` yayınlar (SemVer benzersiz + 64-hex checksum + min_supported + signed). `UpdateService`: `Check` (güncelleme/min-supported/imzasız uyarı), `VerifyChecksum` ile **bozuk paket kurulmaz** (hiçbir değişiklik), `ApplyUpdate` 0-100 yüzde + hata logu, **başarısız kurulumda yedekten rollback** (eski sürüm açılır). Web `lib/update/update.ts` aynı SemVer/checksum/kontrol mantığı.
- **Gerekçe:** Analiz §6.19; kullanıcı talimatı (checksum, yüzde, hata kaydı, rollback).

### ADR-046 — COMODO güvenli çalıştırma kanıtı (sürdürülüyor)
- **Karar:** Geliştirme makinesinde proje EXE/BAT çalıştırılmaz; yalnız `dotnet` host. Hook `comodo_guard.ps1` .bat + imzasız `DepoWise*.exe`'yi engeller; Debug `UseAppHost=false`. Gerçek DB mutlak `%LOCALAPPDATA%\DepoWise\Data\<env>\depowise.db`; açılışta host/yol/WAL/health loglanır. Kapat-aç sonrası veri **aynı DB'de kalır** (testle kanıt; `ClearAllPools` ile kilit yok). Code-signing maliyetli kalem → yayın öncesi karara bırakıldı; imzasız sürümde kullanıcıya şeffaf uyarı.
- **Gerekçe:** CLAUDE.md §0/§6; kullanıcı talimatı + analiz §10.

---

## Faz 16 kararları (2026-06-27)

### ADR-047 — Güvenlik başlıkları + CSRF + rate limit + redaction
- **Karar:** Web başlıkları `next.config.mjs` (CSP/nosniff/X-Frame DENY+frame-ancestors none/Referrer/Permissions; HSTS yalnız Production). CSRF double-submit sabit-zaman doğrulama (fail-closed). `RateLimiter` (login 5/5dk, sync 60/dk, admin 30/dk) iki platformda. `LogRedactor`/`redact` ham secret/PII (password/token/secret/authorization/connstr/session/Bearer) maskeler. Sırlar koda yazılmaz; başlangıçta eksik sır fail-closed.
- **Gerekçe:** Analiz §9; kullanıcı talimatı (fail-closed, sır koda yazma).

### ADR-048 — Token rotasyonu + dependency advisory politikası
- **Karar:** Cihaz token rotasyonu (`RotateDeviceToken`) eski token'ı anında geçersiz kılar; revoke push/pull'da 403. `npm audit` açıkları yalnız **dev/build araçlarında** (eslint/drizzle-kit→esbuild/next→postcss) — runtime maruziyeti yok; `--force` breaking olduğu için uygulanmadı, R23'te izlenir. Code-signing/pentest/MFA maliyetli kalemler `SECURITY.md`'de yayın-öncesi/sonrası karara bırakıldı (temel güvenlikten ayrı).
- **Gerekçe:** Analiz §9 (tedarik zinciri, rotasyon); CLAUDE.md (gereksiz upgrade yapma).

---

## Faz 17 kararları (2026-06-27)

### ADR-049 — Yayın adayı kapsamı: backend RC, UI yayın-engeli
- **Karar:** DepoWise 1.0.0-rc; **backend/iş mantığı + sözleşmeler + testler yayın adayı olgunluğunda** (187 .NET + 66 web test, uçtan uca akış dahil). Genel kullanıcı yayını için UI ekran bağlama (R10), web login akışı (R8/R9) ve canlı PostgreSQL migration (R4/R7) **yayın engeli** olarak kayda geçti; test edilmeyen UI "tamamlandı" sayılmadı (analiz §14 dürüst tamamlanma tanımı).
- **Gerekçe:** Analiz §11-14; kullanıcı talimatı (test edilmemiş işlemi tamamlandı işaretleme).

### ADR-050 — Release candidate checksum yayın akışı
- **Karar:** RC artefaktı Release publish + zip; kimliği SHA-256 ile sabitlenir (`RELEASE_CANDIDATE.md`). Üretim dağıtımında bu checksum `ReleaseService.Publish` ile yayınlanır; updater indirme sonrası doğrular (bozuk paket kurulmaz). artefaktlar git'e dahil edilmez (`.gitignore artifacts/`).
- **Gerekçe:** Analiz §6.19; izlenebilir/yeniden üretilebilir yayın.

### ADR-051 — Yayın güvenliği sertleştirmesi (05.07.2026)
- **Karar:** (1) JWT anahtarı üretimde zorunlu; `DEPOWISE_JWT_KEY` yoksa API açılmaz (dev fallback yalnız Development). (2) Seed admin/superadmin şifreleri sabit değil: env (`DEPOWISE_SEED_ADMIN_PASSWORD` / `DEPOWISE_SEED_SUPERADMIN_PASSWORD`) veya rastgele üretilip bir kez konsola yazılır. (3) `/api/admin/reset-data` üretimde `DEPOWISE_ALLOW_RESET=1` olmadan 403. (4) `/api/auth/login` ve `/api/auth/sync-login` IP bazlı 30 istek/5 dk sınırlı (RateLimiter ilk kez bağlandı; NAT arkası ofisler için gevşek pencere). (5) 500 hatalarında ham exception mesajı client'a dönmez; konsol loguna yazılır.
- **Gerekçe:** Canlı test + kod incelemesi bulguları (bilinen dev anahtarı + bilinen seed şifreyle tam ele geçirme; brute-force sınırsızdı).

### ADR-052 — Web oturum geri yükleme kapısı (05.07.2026)
- **Karar:** MainLayout, oturum ProtectedLocalStorage'dan geri yüklenene kadar `@Body` render etmez (spinner). `Auth.Loaded` artık restore tamamlanınca set edilir.
- **Gerekçe:** F5/doğrudan URL'de sayfalar token'sız API çağrısı yapıp yanlış "kayıt yok"/"yalnız süper admin" gösteriyordu (canlıda doğrulandı: /users, /server-status, /definitions).

### ADR-053 — business-push yetki + içerik doğrulaması (05.07.2026)
- **Karar:** `BusinessSyncService.Apply(SessionContext, payload)` overload'ı eklendi. (1) Yetki: her iş tablosu bir yetki modülüne eşlendi (TableModule); kullanıcı ilgili modülde Create VEYA Edit yetkisi yoksa o tablonun tüm satırları UYGULANMAZ (hata değil, sessiz atla + errors'a not). Admin/SüperAdmin tam yetkili. (2) İçerik: NonNegativeFields ile stok/yakıt/tutar alanları negatifse satır reddedilir (sayı ve sayısal-string toleranslı). company_id zaten UpsertRow'da oturumdan zorlandığı için ayrıca kontrol edilmez. Endpoint `Apply(s, ...)` çağırıyor. Eski `Apply(companyId, ...)` overload'ı korundu (yetkisiz, testler için).
- **Gerekçe:** Y3 — en yetkisiz kullanıcının JWT'siyle firmanın tüm tablolarını ezmesi / negatif stok yazması engellendi. 3 yeni test (yetkisiz modül atlama, admin tam yazma, negatif bakiye reddi) + mevcut 6 BusinessSync testi geçti.

### ADR-054 — JWT yenileme (kayan oturum) (05.07.2026)
- **Karar:** Sunucuya `POST /api/auth/refresh` eklendi (RequireAuthorization; geçerli token → aynı kullanıcı/firma için taze token, yetkiler DB'den). `JwtTokens.ExpiryHours=12` sabiti + `ReadExpiry`. Masaüstü `ServerAuthClient`: token exp'i saklanır (TokenExpiresUtc), `EnsureFreshTokenAsync` süreye <2 saat kalınca yeniler; 401'de `SessionExpired=true` (UI tekrar-girişe yönlendirebilir). `BusinessSyncPushService.PushAsync` push öncesi token yeniler, 401'de bir kez daha dener.
- **Gerekçe:** Y5 — 12 saatten uzun masaüstü oturumda sync sessizce duruyordu; artık kayan oturum + açık sinyal. 4 yeni JwtToken testi (claim/süre, doğrulama, farklı-anahtar reddi, yenileme kimliği korur).

### ADR-055 — Updater yedek + rollback + bütünlük guard'ı (05.07.2026)
- **Karar:** `UpdateInstaller`: (1) kurulum öncesi paket ana exe içermiyorsa kurulum hiç başlatılmaz (bütünlük guard). (2) PowerShell yardımcısı önce mevcut kurulumu `backup` dizinine yedekler; yedek alınamazsa güncelleme başlatılmaz. (3) staging→install kopyalaması başarısızsa (robocopy>=8) yedekten geri alınır ve sürüm YAZILMAZ (bozuk/yarım güncelleme kalıcı olmaz). (4) yalnız başarıda current.txt yazılır. Checksum kontrolü korunur.
- **Gerekçe:** Y4 — eski yardımcı başarısız kopyada bile sürümü yazıp exe'yi başlatıyor, yedek almıyordu. NOT: gerçek PS yolu Windows entegrasyon testi gerektirir; senkron ApplyUpdate rollback'i (UpdateService) mevcut testlerde kapsanıyor.

> **NUMARA NOTU (ADR-076…082):** Aşağıdaki 7 ADR'nin **commit mesajları ADR-075…081** etiketlendi; ancak
> ADR-075 numarası zaten yukarıdaki "logo arka plan" kararına aitti → DECISIONS.md'de doğru sıra **076-082**
> (commit'ler birer eksik: commit-075 = ADR-076, …, commit-081 = ADR-082). Git history yeniden yazılmadı.

### ADR-076 — Silinen makine firması/şubesi girişe makine bilgisi olarak SUNULMAZ (12.07.2026) [commit: ADR-075]
- **Bağlam:** Süper admin, makinenin atanmış firmasını silince tekrar login'de "Makine firması ile giriş
  (silinmiş firma)" çıkıyor ve ona giriş yapılabiliyordu. Kök neden: `EnrollmentService.ReadDeviceInfo`
  join'leri `is_deleted` filtrelemiyordu → silinmiş firma/şube adı-id'si makine bilgisi olarak dönüyordu.
- **Karar:** (server) `ReadDeviceInfo` join'lerine `AND is_deleted=0`; silinmişse NULL döner. (masaüstü)
  `SetupSuperAdminStep2Async`: makine firması geçerli firma listesinde yoksa makine firması/şubesi sayılmaz
  (liste hiç yüklenemediyse dokunulmaz). 2 regresyon testi (SyncTests).
- **Kural:** Makineye hangi firma+şube atandıysa makine firması **odur**; silinmiş/atanmamış firma seçenek değildir.

### ADR-077 — Makine yönetiminde FİRMA değiştirme (web, süper admin) (12.07.2026) [commit: ADR-076]
- **Karar:** `EnrollmentService.AssignCompany(s, deviceId, companyId)` — yalnız süper admin (çapraz-firma);
  hedef firma var+silinmemiş olmalı; **şube ataması otomatik kalkar** (şube eski firmaya aitti). API:
  `POST /api/machines/{id}/company`. Web `Machines.razor`: süper admine "Firma (değiştir)" seçim sütunu + onaylı taşıma.
- **Kapsam:** Masaüstü makine ekranı zaten şube/firma değiştirme içermiyor (yalnız kota/aktif/sil) → dokunulmadı;
  kullanıcının "sadece şube değiştirebiliyorum" dediği ekran web'di. 1 regresyon testi.

### ADR-078 — Canlı sunucu ekranı: disk kapasitesi (canlı) + güncelleme paketi manuel silme (12.07.2026) [commit: ADR-077]
- **Karar:** `ReleaseStore`: `GetDiskInfo` (DriveInfo ile `/data` doluluk), `ListPackages`, `Delete`.
  `/api/server/status`'a disk alanları (diskPercent/Free/Used/packages) — 3 sn'de bir canlı. Yeni uçlar:
  `GET /api/releases/packages` + `DELETE /api/releases/packages/{version}` (süper admin; **en güncel sürüm silinemez**).
  Web `ServerStatus.razor`: canlı disk göstergesi (gauge + spark + %85 kritik uyarı) + KPI + paket tablosu (onaylı silme).
- **Gerekçe:** ADR-070'teki disk-dolması tam kesintisine karşı süper adminin diski canlı görüp eski paketi elle temizlemesi.

### ADR-079 — Web logosu masaüstünün temiz şeffaf logosuna eşitlendi (arka plan yok) (12.07.2026) [commit: ADR-078]
- **Bağlam:** Web `logo.png`'de flood-fill şeffaflık "Depo" harflerinin içine sızmıştı (dama deseni görünüyordu)
  + fazladan slogan vardı. Masaüstü login'de "tam olmuş" logo `Assets/app-icon.png` (şeffaf, arka plansız).
- **Karar:** `app-icon.png` → `wwwroot/logo.png` olarak kopyalandı (birebir). Login + üst bar CSS zaten şeffaf.
  Kullanıcının verdiği kaynak `masaüstü uygulama simge logosu.png` (2.2 MB, opak turuncu zeminli işlenmemiş orijinal)
  yerine, zaten şeffaf/işlenmiş masaüstü asset'i tercih edildi ("arka plan olmasın" garantisi).

### ADR-080 — İlk açılış tema varsayılanları (12.07.2026) [commit: ADR-079]
- **Karar (kayıt yoksa uygulanan varsayılan; kullanıcı değiştirince kaydı ezer):** Masaüstü **Fluent / Koyu / Kehribar**
  (`ThemeService`: accent varsayılanı blue→amber; mod Dark, stil fluent zaten hedefti). Web **Koyu / Yumuşak / Kehribar**
  (server `/api/me/theme` + ApiClient fallback + `ThemeState`: color→amber, style→soft; mode dark zaten).

### ADR-081 — Personel ekranı: hesap AÇMA yerine MEVCUT kullanıcıyı BAĞLAMA (12.07.2026) [commit: ADR-080]
- **Kullanıcı talimatı:** Personel ekranında kullanıcı **açma** alanı değil, personele **mevcut kullanıcıyı bağlama** alanı olmalı.
- **Karar:** ADR-067'deki inline "hesap aç" (kullanıcı adı/şifre/rol) alanı kaldırıldı; yerine **bağlanabilir
  (henüz bir personele bağlı olmayan, süper-admin olmayan) mevcut kullanıcı** seçimi geldi (web + masaüstü).
  `UserService.ListLinkableUsers`; `GET /api/personnel/linkable-users` + `POST /api/personnel/{id}/link-user`
  (mevcut `LinkPersonnel` kullanılır). Hesaplar artık yalnız "Kullanıcılar" ekranında açılır. 2 regresyon testi.
- **Not:** "Saha personeli" kutucuğu + bağlanmadıysa uyarı koşulu korundu (bağlama üzerinden). Eski `/account` (hesap aç) ucu kaldı ama kullanılmıyor.

### ADR-082 — Firma yetki kontrol: süper admin DİNAMİK global kilidi açıp kapatabilir (12.07.2026) [commit: ADR-081]
- **Bağlam:** "Global kilit" salt derleme-zamanı sabitiydi (`AppModules.IsAdminRestricted`) ve UI'da salt-okunurdu.
- **Karar:** İki katman: (1) **SABİT** kilit (IsAdminRestricted — değiştirilemez), (2) süper adminin yönettiği
  **DİNAMİK** global kilit (tüm firmalar). Dinamik kilit **migration'sız**, global `app_settings` satırında saklanır
  (`company_id NULL`, key=`global_grant_limits`). `CompanyGrantService.SetGlobalLocks`/`IsGlobalRestricted`;
  `GetControl` satırına `GlobalHardLocked` alanı. Enforcement `PermissionService.SaveForUser`'a `IsGlobalRestricted`
  eklendi (alt role verilemez). API: `POST /api/global-permissions` (süper admin). Web: "Global kilit" toggle
  (sabit olanlar "sabit" rozetiyle salt-okunur), Save hem firma hem global kilidi kaydeder. 1 regresyon testi.

### ADR-074 — Marka logoları web + masaüstüne eklendi (kalite korunarak) (12.07.2026)
- **Kaynak:** `Desktop\Logo Dosyalarım` — iki dosya, ikisi de **1536×1024**:
  - `Web +Uygulama içi Logo.png` — **istifli tam logo** (görsel + "DepoWise" + slogan). Arka planı **opak beyaz**di.
  - `masaüstü uygulama simge logosu.png` — **sembol** (yazısız değil, kısa marka; **şeffaf**, A=0).
- **İşleme (kalite korunur — yalnız küçültme, HighQualityBicubic, kayıpsız PNG; hiç büyütme yok):**
  1. **Tam logo şeffaflaştırıldı:** dış beyaz zemin **kenarlardan flood-fill** ile alfa=0 yapıldı. Basit "beyazı sil" yapılsaydı **kamyonun beyaz kabini ve yol çizgileri delinirdi**; flood-fill yalnız *dıştan erişilebilen* beyazı siler → iç beyazlar korundu (görsel doğrulandı). Kenar yumuşatma için eşik gradyanı (190–232) → halo yok. Sonra içerik sınırına kırpıldı: **1040×841**.
  2. **Sembol:** alfa sınırına kırpıldı (748×538) → **kare** tuvale ortalandı (%6 boşluk) → 838×838 → 16/24/32/48/64/128/256 px üretildi → **7 boyutlu `.ico`** (PNG gömülü, Vista+ standardı).
- **Yerleşim:**
  - Masaüstü: `Assets/logo.png` (tam logo), `Assets/app-icon-256.png` (sembol), `Assets/app-logo.ico` (pencere ikonu).
  - **`.exe` simgesi:** csproj'da `<ApplicationIcon>` **hiç ayarlı değildi** → exe varsayılan .NET ikonuyla çıkıyordu. Eklendi; gömülü olduğu doğrulandı. Kullanılmayan `avalonia-logo.ico` (şablon artığı) silindi.
  - Web: `wwwroot/logo.png`, `favicon.png` (256), `favicon.ico` (çok boyutlu) + `apple-touch-icon`.
- **Ölçek kararı:** Tam logo **istifli** (1040×841) → 30 px yükseklikte **okunmaz**. Bu yüzden dar alanlarda (masaüstü kenar çubuğu, web üst barı) **sembol** kullanılır; tam logo yalnız **giriş ekranlarında** (geniş, açık zemin) gösterilir.
- ⚠️ **GÜNCELLENDİ → bkz. ADR-075.** Bu ADR'de başlangıçta logoların arkasına "beyaz yuvarlak kutu" konmuştu (koyu temada lacivert logo kaybolmasın diye). **Kullanıcı bunu reddetti; arka plan KALDIRILDI.** Aşağıdaki ADR-075 bağlayıcıdır.

### ADR-075 — Logoların arkasında ARKA PLAN OLMAYACAK (yalnız logo) (12.07.2026)
- **Kullanıcı talimatı (bağlayıcı):** *"logo ve uygulama içine beyaz arka plan ekleyerek logoları uygulamışsın. arka plan olmamalı sadece logo olmalı."*
- **Karar:** Logo/sembol **hiçbir yerde** arka plan kutusuna sarılmaz. Şeffaf PNG **doğrudan** kullanılır. Kaldırıldığı 5 yer: masaüstü **LoginWindow**, masaüstü **MainWindow** (daraltılmış + açık kenar çubuğu), web **MainLayout** üst barı, web **Login** kartı.
- **Neden not düşülüyor:** ADR-074'te (kendi kararımla) beyaz kutu eklenmişti; belgede öyle kalırsa **sonraki oturumlar bunu geri koyar**. Bu ADR onu geçersiz kılar.
- **Bilinen ödünleşim (kullanıcı bilerek kabul etti):** Logo **lacivert ağırlıklı** olduğundan **koyu temada** kontrastı düşebilir (sarı/beyaz kısımlar görünür kalır). Kullanıcı şikâyet ederse çözüm **arka plan eklemek DEĞİL**, koyu tema için **açık renkli logo varyantı** üretmektir.
- **Masaüstü giriş ekranı** ayrıca tam logo yerine **sembol logosunu** kullanır (kullanıcı isteği); yüksek çözünürlük için `Assets/app-icon.png` (838×838) eklendi, kullanılmayan masaüstü `logo.png` kaldırıldı.

### ADR-073 — Kota İzleme "ONLINE": zaten kullanıcı-bazlı tekildi; testle sabitlendi + bellek sızıntısı düzeltildi (12.07.2026)
- **Talep (kullanıcı):** "Kota izleme ekranındaki online kolonunda aynı kullanıcı hem web'ten hem masaüstünden login olmuşsa **1 online** görünmeli; anlık login durumunu değil **kullanıcı** online durumunu almalı."
- **İnceleme sonucu (önemli):** Bu davranış **zaten doğruydu**. `ServerPresence` sözlüğü **ilk yazıldığı günden beri `userId` ile anahtarlı** (`_seen[userId] = …`, commit `03b4709`, #4 özelliği). Aynı kullanıcının ikinci platformu **yeni kayıt açmaz, mevcut kaydı tazeler** → çift sayım mimari olarak imkânsız. JWT `sub` claim'i her iki platformda da aynı `userId`'dir (tek token üretici). Yani düzeltilecek bir sayım hatası **yoktu**.
- **Yapılanlar (gerçek katkı):**
  1. **Kanıt/regresyon:** `ServerPresenceTests` (4 test) — aynı kullanıcı iki platformdan → **1**; farklı kullanıcılar → ayrı; 5 dk penceresi dışındaki düşer; **aynı kullanıcı iki farklı firmada bile tek kişi** sayılır (süper admin firma seçimi senaryosu). Şart artık koda çivilendi.
  2. **Gerçek kusur düzeltildi:** Pencere dışında kalan kayıtlar sözlükten **hiç silinmiyordu** (süresiz büyüme = bellek sızıntısı). `Prune()` eklendi; okuma sırasında eski kayıtlar düşürülür.
  3. `ServerPresence` test edilebilir hâle getirildi (`nowMs` enjekte edilebilir saat, `ResetForTests`).
- **Kullanıcıya not:** Ekranda 2 görülmüşse muhtemelen (a) **farklı iki kullanıcı** online'dı, ya da (b) **"AKTİF"** sütunu (firmadaki aktif kullanıcı sayısı) ile **"ONLINE"** karıştırıldı. Tekrar görülürse hangi kullanıcılarla olduğu bilgisiyle bildirilmeli.

### ADR-072 — Firma işlemleri OFFLINE-FIRST: yerele yaz + kuyruk, internet gelince SIRAYLA eşitle (12.07.2026)
- **Bağlam:** ADR-071 firma işlemlerini **çevrimiçi zorunlu** yapmıştı. Kullanıcı bunu reddetti: *"İnternete bağlanana kadar işlemleri yerel DB'ye yazsın, bağlanınca sırasıyla eşitlemeye başlasın. Ama eşitleme sırasında kayıtlar hataya düşmemeli. Önce sabit tanımlar ve hataya düşürebilecek tanımlar eşitlenmeli, sonra diğer kayıtlar."*
- **Karar (offline-first + kuyruk):**
  - Firma ekle/güncelle/sil/aktifleştir **ÖNCE YEREL DB'ye** yazılır (çevrimdışı tam çalışır), sonra **`sync_outbox`** tablosuna kuyruklanır (mevcut `OutboxWriter` — tanımlıydı ama hiç kullanılmıyordu; artık bağlandı).
  - İnternet gelince kuyruk **FIFO** (oluşturulma sırası) işlenir → aynı firmanın `create → update → delete` sırası korunur. Bir işlem kalıcı hata (4xx) verirse **sonrakiler işlenmez** (sıra bozulmasın); 5xx/ağ hatası **geçici** sayılır, kuyrukta kalır ve tekrar denenir.
- **"Hataya düşmemeli" şartı → İDEMPOTENCY (kritik):** Yeniden denemede aynı işlem birden çok kez gelebilir. Sunucu tarafı idempotent yapıldı:
  - `CompanyService.Create(s, dto, explicitId)`: masaüstünün **çevrimdışı ürettiği id** ile oluşturur (yerel ↔ sunucu id'leri eşleşir) ve `ON CONFLICT(id) DO UPDATE` ile **tekrar gelirse hata vermez**. API `NewCompanyDto.Id` alanı eklendi (web'den gelen istekte null → sunucu id üretir).
  - `Delete` / `Update` / `Reactivate`: "0 satır etkilendi" artık hata değil — kayıt **zaten o durumdaysa** sessizce başarılı. Yalnız firma **hiç yoksa** hata (fail-closed korunur).
- **SENKRON SIRASI (kullanıcının şartı — önce hataya düşürebilecek tanımlar):**
  1. **Firma kuyruğu** (en üst ebeveyn; olmadan diğer kayıtlar FK/tenant hatası verir)
  2. **Tanımlar/lookup** (`LookupSyncService`)
  3. **İş verisi** push→pull (`BusinessSyncService.Tables` **zaten FK-güvenli sırada**: units/suppliers/brands/kategoriler… → personel/malzeme/araç/stok…)
  Bunlar eskiden **paralel** başlatılıyordu (iş kaydı, ebeveyn tanımı gelmeden gidip hata verebilirdi) → artık **sırayla `await`** edilir.
- **Veri kaybı koruması:** `MirrorLocalAsync` kuyrukta **bekleyen işlem varken çalışmaz** — yoksa henüz gönderilmemiş yerel firma "sunucuda yok" sanılıp silinirdi.
- **UI:** Kuyrukta iş varsa kullanıcıya bildirilir: *"N işlem çevrimdışı kuyrukta — internet gelince eşitlenecek."*
- **Test:** `Firma_Kuyruk_TekrarGonderiminde_HataVermez_IDEMPOTENT` (aynı create/delete/reactivate iki kez → hata yok, mükerrer kayıt yok; olmayan firmada fail-closed). Suit **263/263**.

### ADR-071 — Masaüstü firma ekle/sil web ile eşitlenmiyordu → FİRMALAR SUNUCU-OTORİTELİ (12.07.2026)
- **Belirti (kullanıcı):** "Masaüstü firma tanım ekranından eklediğim/sildiğim firma verileri web ile zaman geçse de hâlâ eşitlenmemiş."
- **Kök neden:** Masaüstü `CompaniesViewModel` **yalnız YEREL DB'ye** yazıyordu (`DesktopServices.Companies` = yerel `CompanyService`). Firmalar iş senkronu tablo listesinde de **yok** (`BusinessSyncService.Tables` içinde `companies` bulunmuyor) → masaüstünde yapılan firma değişikliği sunucuya **hiçbir yoldan** ulaşmıyordu. Aynı şekilde web'de eklenen/silinen firma da masaüstüne inmiyordu.
- **Karar (kullanıcının "web tam otoriter" kuralı):** Firmalar **sunucu-otoriteli** yapıldı — şubelerdeki (ADR-066) modelin aynısı:
  - Yeni `CompanySyncService` (masaüstü): **ekle / güncelle / sil / aktifleştir** doğrudan **sunucu API'sine** gider (`/api/companies…`, JWT ile). **Çevrimiçi zorunlu** — çevrimdışıysa net mesaj (`OfflineException`), sessizce yerele yazıp sapma üretmez.
  - `MirrorLocalAsync()`: sunucudaki firma listesi yerele **aynalanır**; sunucuda **artık olmayan** yerel firmalar **pasife alınır**. Girişte (`FinalizeLoginAsync`), ekran açılışında ve "Yenile"de çalışır.
- **Sonuç:** Masaüstü ↔ web firma verisi birebir aynı. Yerel `CompanyService.Create/Delete` artık masaüstü UI'dan çağrılmıyor (sunucu tarafında API'nin kullandığı servis olarak kalır).
- **Test:** Build 0 hata, suit 262/262. (Ağ bağımlı akış olduğu için birim test yerine sunucu API'si + aynalama mantığı üzerinden doğrulanır; şube aynalama testi aynı deseni kapsar.)

### ADR-070 — TAM KESİNTİ: sunucu diski doldu (güncelleme paketleri) → saklama politikası (12.07.2026)
- **Olay:** 1.0.41 yayınlanırken önce yükleme, sonra **login bile 500** vermeye başladı. Log: `SQLite Error 13: 'database or disk is full'`. Fly.io kalıcı diski (`/data`, **974 MB**) **%100 dolmuştu** → SQLite hiçbir şey yazamıyor → **tüm API çöküyor** (login dahil). Kod hatası DEĞİL, operasyonel kapasite hatası.
- **Kök neden:** Her masaüstü paketi **~85 MB** ve `/data/releases` altında **hiç temizlenmiyordu**. 11 paket birikmişti (1.0.31…1.0.41) = **892 MB**. Sunucu DB'si yalnızca 1 MB. Güncelleyici **daima en son sürümü** indirdiği için eski paketler tamamen ölü ağırlıktı.
- **Acil müdahale:** Eski paketler silindi (en güncel 1.0.40 korunarak) + yarım kalmış bozuk 1.0.41 paketi silindi → disk **%100 → %17** (756 MB boş). Canlı düzeldi, 1.0.41 yeniden yayınlandı (checksum `2825aa71…`).
- **Kalıcı çözüm:** `ReleaseStore.SaveAsync` artık her yayından sonra `PruneOld()` çağırır: **en yeni `KeepCount=3` paket dışındakiler otomatik silinir** (geri dönüş ihtimaline karşı 3 tutulur). Temizlik hatası yayını bozmaz (sessiz geçilir).
- **Ders / gelecek:** ~1 GB disk + 85 MB paket = **~11 sürümlük tavan**. Paket boyutu self-contained olduğu için büyük. İleride paket boyutu artarsa veya sürüm hızı artarsa `KeepCount` düşürülmeli ya da disk büyütülmeli (`fly volumes extend`). Disk dolması **sessiz değil, ölümcül** bir arızadır: SQLite yazamaz → her uç 500.

### ADR-069 — SİLMEDE WEB (SUNUCU) TAM OTORİTER: silinen kayıt makinelerin yerel DB'sinden de düşer (12.07.2026)
- **Talep (kullanıcı):** "Web'te bir kayıt silindiyse ilgili şubenin makinesindeki yerel DB'de de silinsin. **Web tam otoriter olacak.**"
- **Mevcut durum / bulunan iki açık:**
  1. **Diriliş (pull):** Geri-çekmede `UpsertRow` **LWW** uyguluyordu (`excluded.updated_at >= tablo.updated_at`). Makinede kayıt web'deki silmeden SONRA düzenlenmişse (yerel `updated_at` daha büyük), gelen `is_deleted=1` **atlanıyor** ve kayıt yerelde canlı kalıyordu.
  2. **Diriliş (push):** Masaüstü girişte **önce PUSH sonra PULL** yapıyor. Makine, web'de silinmiş kaydı `is_deleted=0` + daha yeni `updated_at` ile push edince **sunucuda kayıt diriliyor**, ardından pull ile TÜM makinelere geri yayılıyordu. (Bu, tek başına (1)'i düzeltmeyi de boşa çıkarırdı.)
- **Karar (iki yönlü, simetrik):**
  - **PULL (`ApplyPull`, `serverAuthoritativeDeletes`):** Sunucudan gelen satır `is_deleted=1` ise **LWW koşulu uygulanmaz** → silme **her zaman kazanır**, yereldeki daha yeni düzenleme silmeyi engelleyemez.
  - **PUSH (`Apply`, `protectServerDeletes`):** Sunucuda `is_deleted=1` olan kayıt, cihazın `is_deleted=0` satırıyla **geri getirilemez** (`NOT (tablo.is_deleted=1 AND excluded.is_deleted=0)`). Kaydı geri getirmenin tek yolu **web'den** yeniden aktifleştirmektir.
  - **Silme dışındaki alanlarda LWW aynen korunur** (yerelde yapılmış yeni düzenleme, sunucunun eski sürümüyle ezilmez) — karşı-kontrol testiyle sabitlendi.
- **Ek:** `personnel_titles` (unvan sabit tanımları) senkron tablo listesine + `TableModule` (yetki eşlemesi, `personnel`) eklendi — yeni tablo hiç senkronlanmıyordu.
- **Kapsam notu:** `branches`/`companies` iş senkronunda değildir (web-otoriteli); şube silme yansıması ADR-066'da ayrıca çözüldü.
- **Test (3 yeni):** `Webte_Silinen_Kayit_Yerelde_De_Silinir_SUNUCU_OTORITER` · `Sunucuda_Silinen_Kayit_Cihaz_Pushuyla_Diriltilemez` · `GeriCekmede_SilinmemisKayitta_LWW_Korunur`. Suit **262/262**.

### ADR-068 — Firma silince 401 + liste yüklenmiyor: süper admin oturumu öksüz kalıyordu (12.07.2026)
- **Belirti (kullanıcı):** "Firma listesinde silinmiş firma listelenmeye devam ediyordu, tekrar sildim → **401 Unauthorized**; ayrıca firmalar hiç yüklenmiyor."
- **Kök neden:** Süper admin bir firmayı **seçip o firmanın bağlamında** çalışabiliyor (ADR-058, JWT company claim = seçilen firma). O firmayı **silince** token'daki firma geçersiz hâle geliyor. `AuthService.CreateSessionForUser` çapraz-firma dalında `CompanyExists` false görüp **null** dönüyordu → `Session(ctx)` null → **her istek 401**. Sonuç zinciri: silme başarılı olur (o an oturum geçerli) → liste yenileme isteği 401 → **UI'da eski/silinmiş firma görünmeye devam eder** → tekrar silmeye basınca 401 → sonrasında hiçbir şey yüklenmez. (Liste sorgusu zaten `is_deleted=0` filtreliydi; hata orada değildi.)
- **Karar:** Çapraz-firma dalında **"silinmiş firma"** ile **"hiç var olmamış firma"** ayrıldı:
  - Firma **kaydı hiç yoksa** (uydurma/sahte id) → `null` (fail-closed **korunur**; `SuperAdmin_OlmayanFirmada_Oturum_Acamaz` testi hâlâ geçer).
  - Firma **var ama silinmişse** → süper admin **kendi (home) firmasına düşürülür**, oturum yaşar. Süper admin platform sahibidir; hiçbir işlem onu kilitleyemez (ADR-064 ile aynı ilke).
- **Test:** `SuperAdmin_CalistigiFirmayiSilince_Oturum_Dusmez_401_Vermez` (seç → sil → oturum yaşar, home'a düşer, liste yüklenir ve silinen firma listede yoktur). Suit **259/259**.

### ADR-067 — #6 NİHAİ: **Fikir A** (tek ekran), B'nin koşulları korunarak (12.07.2026)
- **Bağlam:** ADR-065 ile Fikir B uygulandı (Personel/Kullanıcılar ayrı; hesap açma Kullanıcılar'a taşındı). Kullanıcı canlıda gördükten sonra **ayrı ekran yapısını beğenmedi** ve **Fikir A'ya dönülmesini** istedi: *"A'yı yapalım... ama koşullar aynı kalsın."*
- **Karar (A + B'de eklenen koşulların TAMAMI korunur):**
  - **Personel ekranında hesap açma GERİ GELDİ:** "Uygulama erişimi ver" anahtarı → kullanıcı adı / şifre / rol; hesap aynı formda açılır ve personele bağlanır (`POST /api/personnel/{id}/account`). Admin **"Hesabı kaldır"** ile bağı çözebilir.
  - **Korunanlar:** `☐ Saha personeli` kutucuğu · hesap yoksa/açılmıyorsa **ve** kutucuk işaretli değilse **uyarı penceresi** (kutucuk işaretliyse koşul hiç çalışmaz) · mükerrer kişi uyarısı · **unvan sabit tanım + "+"** · bir personele **tek** hesap.
  - **Çelişki önleme:** "Saha personeli" işaretlenirse hesap açma anahtarı otomatik kapanır ve gizlenir (kişi uygulamaya girmeyecek).
  - **Kullanıcılar ekranındaki "Personel seç (bağla)" KALDI** — kaldırmak gerekmedi; ikinci (isteğe bağlı) yol olarak duruyor, A'yı bozmuyor. PERSONEL sütunu da kalır.
- **Veri katmanı değişmedi** (Migration033/034 aynen geçerli): `users.personnel_id`, `personnel.is_field_staff`, `personnel_titles`. Yalnız UI/akış değişti → geri alınabilir.
- **Test:** 258/258. **Kapsam:** web + masaüstü.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). ADR-065'in yerini alır (B artık geçerli değil); ADR-063/064'teki A ise koşulsuz sürümdü — bu ADR "A + koşullar" nihai hâlidir.

### ADR-066 — Silinen şubeler masaüstünde listelenmeye devam ediyordu (12.07.2026)
- **Belirti:** Web'de silinen şube, masaüstünde **tüm şube alanlarında** (personel, kullanıcı, stok, araç…) görünmeye devam ediyordu.
- **Kök neden:** Sunucu/web tarafındaki TÜM şube okuma sorguları zaten `is_deleted=0` filtreliydi (hata orada değildi). Şubeler **sunucu-otoriteli** (`BusinessSyncService.Tables` içinde YOK — iş senkronuna dahil değil). Masaüstünün yerel şube kopyası ise sunucudan **yalnız UPSERT** ediliyordu (`LoginViewModel`), üstelik bu yalnız **süper admin firma seçimi** yolunda çağrılıyordu. Sunucuda silinen şube yerelde `is_deleted=0` olarak kalıyor, hiçbir zaman düşmüyordu.
- **Karar:** Şube aynalama `MirrorServerBranchesLocalAsync` metoduna çıkarıldı ve **her girişte** (`FinalizeLoginAsync`, tüm kullanıcılar) çağrılıyor. Sunucudan gelenler upsert edilir; **sunucunun listesinde ARTIK OLMAYAN yerel şubeler pasife alınır** (`is_deleted=1`). Çevrimdışıysa hiçbir şey yapılmaz (yereldekiyle devam — offline-first korunur).
- **Test:** `OrgPersonnelTests.Sube_Silinince_HicbirListede_Gorunmez` (liste + `ScopeResolver.AllowedBranchIds`). Suit 258/258.
- **Gerekçe:** Şube tek otoriteye (sunucu) bağlı olduğundan yerel kopya birebir ayna olmalı; yalnız-upsert modeli silmeyi hiç yansıtmıyordu.

### ADR-065 — #6 revizyon: Fikir A → **Fikir B** + saha personeli kutucuğu + unvan sabit tanım (12.07.2026)
- **Bağlam:** #6 (Personel+Kullanıcı birleştirme) ADR-063/064'te **Fikir A** ("tek Çalışan kaydı, aynı ekranda hesap açma") olarak uygulanmıştı. Kullanıcı 12.07'de **Fikir B'yi seçtiğini** belirtti (belgede A yazılıydı — çelişki kullanıcının son açık talebi lehine çözüldü, CLAUDE.md §1).
- **Karar (Fikir B + kullanıcının eklemeleri):**
  - **Personel** ve **Kullanıcılar** ekranları **ayrı** kalır. Personel ekranındaki hesap açma (kullanıcı adı/şifre/rol) ve "hesap bağını kaldır" **kaldırıldı**.
  - **Kullanıcılar** formuna **"Personel seç (bağla)"** eklendi → `users.personnel_id` (Migration033 zaten vardı). Yalnız **hesabı olmayan** personeller listelenir; bir personele **tek** hesap (mevcut kısmi tekil index korur). Kullanıcı listesine **PERSONEL** sütunu.
  - **`personnel.is_field_staff`** ("Saha personeli" kutucuğu, Migration034). Kaydederken **hesap bağlı değil VE kutucuk işaretli değilse** uyarı penceresi çıkar; **kutucuk işaretliyse koşul hiç çalışmaz** (kullanıcının açık talebi). Onaylanırsa kutucuk işaretlenir → tekrar sorulmaz.
  - **`personnel_titles`** tablosu (Migration034): **unvan sabit tanım** listesi (firma bazlı) + **"+"** ile yeni tanım. `personnel.title` serbest metin olarak kalır (geçmiş bozulmaz); migration mevcut unvanları tanım listesine taşır. Mükerrer kontrolü **tr-TR CompareInfo** ile yapılır — SQLite `LOWER()` Türkçe harfleri (Ş/İ/Ğ) küçültmediği için SQL'de değil C#'ta.
- **Kapsam:** Ortak ekran → **web + masaüstü** ikisinde de uygulandı. Diğer ekranlar bozulmadı.
- **Gerekçe/sonuç:** Kullanıcının son açık talebi. Küçük, geri alınabilir değişiklikler; veri kaybı yok (hesap-personel bağı ve unvan metinleri korunur). Test **257/257** (+4 yeni: saha kutucuğu, unvan mükerrer/tenant, kullanıcı-personel bağı).
- **Not:** Fikir A taslağı (`docs/mockups/calisan-yonetimi-A.html`) tarihsel kayıt olarak kalır.

### ADR-064 — Çalışan Yönetimi masaüstü (Faz4) + KRİTİK: süper admin kilitlenme düzeltmesi (12.07.2026)
- **Çalışan Yönetimi (Faz4, masaüstü):** Masaüstü Personel ekranı web (Faz3) ile eşitlendi — erişim rozeti (Saha/Kullanıcı/Admin), mükerrer kişi uyarısı, aynı formda "Uygulama erişimi ver" (kullanıcı adı/şifre/rol), saha-personeli onayı, hesap bağını kaldır (admin). Tek servis (`CompanyService`/`UserService`) iki platformca paylaşıldığı için iş kuralı tek yerde.
- **KRİTİK hata (kök neden):** `CompanyService.Delete` firma silinince o firmadaki TÜM aktif kullanıcıları `is_active=0` yapıyordu. Süper admin **kendi home firmasını** silince kendini pasife alıp sistemden tamamen kilitliyordu → login "Kullanıcı adı veya parola hatalı" (login `is_active=1` arar). Sunucu restart'ı kurtarmıyordu (seed yalnız süper admin YOKSA çalışır).
- **Karar / önlemler:** (1) `CompanyService.Delete` deaktivasyonu süper admin kullanıcılarını **hariç tutar** (`AND id NOT IN (…role-super-admin…)`). (2) `ServerServices.EnsureSeedAdmins` her açılışta pasif süper adminleri `is_active=1` yapan **self-heal** içerir → canlı kilit bir API redeploy ile açılır. (3) Regresyon testi `OrgPersonnelTests.Firma_Silme_SuperAdmini_PasifeAlmaz` (silme sonrası login başarılı). Hafıza notu: `superadmin-lockout-company-delete`.
- **Gerekçe:** Süper admin platform sahibidir; hiçbir operasyon onu kilide düşürememeli. Küçük, geri alınabilir SQL değişikliği; normal kullanıcı davranışı korunur. Test 253/253.
- **Açık takip:** Canlı süper admin kilidi ancak **API (`depowise-erp`) yeniden yayınlanınca** açılır (self-heal). Deploy kullanıcı onayı/flyctl gerektirir.

### ADR-063 — Güncelleme penceresi (Ertele/Yeniden Başlat, tek pencere) + Firma Yetki Kontrol yeni tasarım + Çalışan Yönetimi taslağı (11.07.2026)
- **(C) Yeniden başlatma onayı:** Eskiden indirme sonrası pencere iki "Tamam" butonuyla çıkıp ne olursa olsun yeniden başlatıyordu. Artık ayrı onay: **"Şimdi Yeniden Başlat" / "10 Dakika Ertele"**; her erteleme 10 dk ve pencerede yazılı. İndirilen paket (`_pendingBytes`) saklanır → erteleyince tekrar inmez.
- **(D) Biriken bildirimler:** Aynı anda **tek** güncelleme penceresi (`_availableWindow` guard + `_updateBusy` kritik bölüm). Pencere açıkken yeni paket çıkarsa **yeni pencere açılmaz**, açık pencerenin mesajı `ConfirmWindow.SetMessage` ile güncellenir. Snooze 10 dk (`_updateSnoozeUntilUtc`); kontrol aralığı 10 dk → **1 dk**.
- **#5 Firma Yetki Kontrol yeni tasarım (web):** Kullanıcı taslağı beğendi (`docs/mockups/firma-yetki-v2.html`) → `CompanyPermissions.razor` yeniden yazıldı: özet kutular, ekran arama, istemci-tarafı gruplama, 3 durumlu kontrol (Serbest/Yalnız Admin/🔒 kilit), grup-başı "tümünü serbest", değişiklik sayacı + yapışkan kaydet. **API sözleşmesi korundu** (`restrictedKeys`). Web-only ekran.
- **#6 Çalışan Yönetimi (Personel+Kullanıcı birleşik) — TASLAK, uygulanmadı:** Fikir A seçildi; `docs/mockups/calisan-yonetimi-A.html`. Kullanıcı kuralları: (1) mükerrer kişi (farklı şubede ad+telefon) uyarısı + birleştir/farklı-kişi; (2) bir personele **tek** kullanıcı; (3) yanlış bağ düzeltmesi yalnız Admin+; (4) kullanıcı seçilmezse "saha personeli mi?" onayı. Onay sonrası web+masaüstü uygulanacak. Detay: `docs/ONERILER_YETKI_PERSONEL.md`.
- **Gerekçe:** Kullanıcının açık talepleri. C/D/#5 uygulandı; #6 onay bekliyor.

### ADR-062 — Firma yeniden-aktifleştirme + sunucu izleme (CPU/RAM/online) + otomatik yedek + yetki/personel önerileri (11.07.2026)
- **#1 Firma yeniden-aktifleştirme (sözleşme yenileme):** `CompanyService.ListDeleted` + `Reactivate` — pasife alınan firma geri gelir, silme sırasında pasife alınan kullanıcılar (`is_active=0`) tekrar aktifleşir. `GET /api/companies/deleted`, `POST /api/companies/{id}/reactivate` (yalnız süper admin). Web `Companies.razor` "Pasif Firmalar" bölümü + masaüstü `CompaniesView` Expander (ortak `CompanyService` → iki platform).
- **#3 Canlı sunucu CPU/RAM:** `/api/server/status` yeni alanlar: `cpuPercent` (poll'lar arası `TotalProcessorTime` delta / duvar-saati / çekirdek), `memPercent` + `memLimitMb` (GC `TotalAvailableMemoryBytes` = cgroup limiti; yoksa 256MB), `usersOnline`. Web `ServerStatus.razor` animasyonlu gauge + sparkline (eşik renkleri %60/%85). Web-only ekran.
- **#4 Online kullanıcı:** `ServerPresence` (bellek-içi, son 5 dk; tek sunucu → kalıcı depo yok, ücretsiz); auth sonrası middleware `Touch(userId, companyId)`. `/api/quota-monitor` firma başına `onlineCount/onlineText`. Web `QuotaMonitor.razor` ONLINE sütunu. Web-only ekran.
- **#2 Otomatik yedek:** Gerçek durum: sunucuda otomatik yedek YOK; masaüstü elle yüklüyordu (koddaki "günlük otomatik" yorumu asılsızdı). Çözüm: `ShellViewModel.MaybeDailyBackupAsync` — bugün yerel yedek yoksa günde 1 kez `BackupService.Backup()` (VACUUM INTO + 30 gün rotasyon) + sunucu adresi tanımlıysa yükler. Web+masaüstü yedek ekranlarına "bu ekran nasıl dolar" bilgi paneli.
- **#5/#6 (ÖNERİ, uygulanmadı):** Firma Yetki Kontrol yeniden tasarım görsel taslağı (`docs/mockups/firma-yetki-v2.html`) + Personel/Yetki birleştirme fikirleri → `docs/ONERILER_YETKI_PERSONEL.md`. Kullanıcı onayı bekliyor.
- **Kılavuz:** `docs/KULLANIM_KILAVUZU.md` oluşturuldu; her değişiklikte güncellenecek.
- **Gerekçe:** Kullanıcının açık talepleri. #5/#6 onay-öncesi (kullanıcı "önce fikir/xml sun" dedi). Test: +1 (`Firma_YenidenAktiflestirme_KullanicilariGeriAktifEder`).

### ADR-061 — Makine şubesi ilk-kurulum oto-atama (onaylı) + firma silme kullanıcı koruması (11.07.2026)
- **Makine ilk-kurulum (ADR-059'u revize eder):** Önce "makineye şube atanmamışsa personel girişi ENGELLENİR, admin web'den atamalı" idi. Kullanıcı isteğiyle değişti: makinenin şubesi henüz YOKSA, **ilk giriş yapan kullanıcı** (çevrimiçi) şube seçer → **onay penceresi** ("bu makine [firma]/[şube] için tanımlanacak, onaylıyor musunuz?") → onaylarsa `POST /api/machines/self-assign` ile makinenin şubesi kullanıcının şubesine tanımlanır. `EnrollmentService.SelfAssignBranchIfUnset` yalnız `branch_id IS NULL` iken atar (zaten atanmışsa DOKUNMAZ → admin ataması otoriter kalır). Admin web'den her zaman değiştirebilir (AssignBranch). Çevrimdışı ilk kurulum yapılamaz (sunucu gerekir → bilgilendirme). Makine şubesi zaten varsa: eski davranış (çevrimdışı oto-giriş; çevrimiçi seçim + farklı-şube uyarısı).
- **Makine yönetimi ekranı (#2):** firma→şube seçimi + "Kayıtsız Makineler" (şubesiz, süper admin için firma bağımsız); her sorgu yalnız ilgili kümeyi çeker (menü açılışında tüm makineleri çekmez). `ListDevices(companyFilter, branchFilter, unassignedOnly)`; `/api/machines?companyId&branchId&unassigned`.
- **Firma silme (#1):** Önce "bağlı kullanıcılar var, önce silin" hatası veriyordu. Artık firma silinince bağlı kullanıcılar **pasife alınır** (`is_active=0`, `is_deleted=0` → korunur, kaybolmaz); yanlışlıkla silinirse veri durur, firma geri gelince aktifleştirilebilir.
- **Gerekçe:** Kullanıcının açık talebi. Suit 250/250 (+3 test: firma-silme-pasif, makine-filtre, ilk-kurulum self-assign). Masaüstü değişiklikleri yeni paketle (1.0.36) görünür.

### ADR-060 — Masaüstü süper admin girişi: firma+şube seçimi / makine firması-şubesi (10.07.2026)
- **Bağlam:** Masaüstünde süper admin kendi firmasına (DEPOWISE) giriyordu → web'de yönettiği firmanın (ör. Oze Group) şubelerini göremiyordu. Kullanıcı isteği: "süper adminin firması olmaz, bütün firmalara erişebilir. Login 2. aşamada 'makine firması ile giriş' + 'makine şubesi ile giriş' kutucukları olsun; işaretliyse makineye tanımlı firma+şube ile gir, değilse firma+şube seç; ve hiçbir koşul süper admini durdurmasın."
- **Karar:**
  - Sunucu: makine kayıt/heartbeat yanıtı artık makinenin **firmasını** da döner (`RegisterResult.CompanyId/Name`; `ReadDeviceInfo` companies join). Masaüstü bunu önbelleğe alır (çevrimdışı için).
  - Masaüstü `LoginViewModel`: süper admin ADIM 2 = iki kutucuk (**Makine firması ile giriş**, **Makine şubesi ile giriş**; makine firması/şubesi varsa varsayılan işaretli) + firma ComboBox (işaretsizken) + şube ComboBox. Süper admin **hiçbir koşulda engellenmez** (şube seçilmese bile → Tüm Şubeler).
  - Seçilen firma süper adminin kendi firması değilse: firma + şubeleri **yerel DB'ye upsert** edilir ve `AuthService.CreateSessionForUser` ile **çapraz-firma oturumu** kurulur (bu, masaüstü `AuthService`'inin ADR-057'deki süper admin çapraz-firma yeteneğini kullanır).
  - Normal (süper olmayan) kullanıcı akışı ADR-059'daki gibi kalır (makine/kullanıcı şubesi zorunlu, çevrimdışı oto-giriş).
- **Bilinen sınır:** Seçilen başka firmanın **operasyonel verisi** (stok/araç/bakım…) yerelde yoksa o ekranlar boş olabilir — bu akış yalnız firma+şube **tanımlarını** yerele senkronlar; iş verisi senkronu ayrı bir konu. Gerçek çok-firmalı kullanımda test edilmeli.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). Suit 244/244, masaüstü açılış smoke-test OK; GUI login akışı gerçek makinede doğrulanmalı. Görünürlük: yeni masaüstü paketi (1.0.35) veya dev kısayolu (güncel DLL).

### ADR-059 — Admin-tanımlı makine şubesi + IP'den il (10.07.2026, TAMAM — sunucu+web+masaüstü)
- **Bağlam:** Kullanıcı isteği: makinenin şubesi artık "ilk giriş yapanın şubesi" (yerel) değil, **admin'in web'den atadığı** şube olsun (otoriter). Ana sayfa bu şubeyi göstersin; farklı şube personeli girip işlem yaparsa "kayıtlar makine şubesine yazılmaz" uyarısı; internet yoksa makinenin şubesine otomatik giriş; kullanıcıya VEYA makineye şube tanımlı değilse giriş engellensin. Makine atama ekranı IP'den il gösterip tanımayı kolaylaştırsın.
- **Karar (Adım 1 — sunucu + web, TAMAM):**
  - `EnrollmentService.AssignBranch(admin, deviceId, branchId)`: admin makineye şube atar (tenant kontrollü; yalnız admin; boş→kaldırır). Yeni uç `POST /api/machines/{id}/branch`.
  - `RegisterSelf` **artık login şubesini `branch_id`'ye YAZMAZ** — şube yalnız admin atar (otoriter). Yeni makine şubesiz gelir. Kayıt/heartbeat yanıtı atanan şubeyi (id+ad) döndürür → masaüstü önbelleği için.
  - `/api/machines` yanıtına `branchId` + `province` (IP'den il) eklendi. `GeoIp`: best-effort ip-api.com, bellek-önbellekli, isteği bloklamaz, özel IP/başarısızlıkta boş.
  - Web makine ekranı: her satırda **şube atama açılır-listesi** + **İl** sütunu.
  - Test: 2 yeni (atama otoriter+login ezmez, yalnız admin+geçerli şube). Suit 243/243. Yerel e2e + canlı deploy doğrulandı.
- **Karar (Adım 2 — masaüstü, TAMAM):** (1) Kullanıcı şubesi (`users.branch_id`) artık sunucudan masaüstüne senkron olur (`RemoteUserBundle.BranchId` + `ExportForSync` + `ImportRemoteUser`; sync-login yanıtı e2e doğrulandı). (2) `MachineGate` makinenin admin-atanmış şubesini de getirir/önbelleğe alır (`machine_branch.txt`) ve login şubesini artık göndermez. (3) `LoginViewModel` ADIM 1'de: makineye şube yoksa → "makineye şube tanımlanmamış" (giriş yok); kullanıcıya şube yoksa (ve Tüm Şubeler yetkisi yoksa) → giriş yok; **internet yoksa makine şubesine otomatik giriş** (seçim yok); internet varsa şube seçimi (varsayılan = kullanıcının şubesi). Farklı-şube uyarısı artık admin-atanmış makine şubesine göre ("kayıtlar makine şubesine yazılmayacak"). Eski yerel "ilk giriş şubesi" mantığı kaldırıldı. (4) `DashboardViewModel` ana sayfada MAKİNE şubesini gösterir (çalışma şubesi farklıysa parantezde); heartbeat makine şubesini güncel tutar. Süper admin makine/kullanıcı şube kısıtlarından muaf. Suit 243/243 (+1 senkron testi); masaüstü açılış smoke-test OK. **Görünürlük: yeni masaüstü paketi (1.0.35) yayınlanınca.** NOT: masaüstü GUI login akışı (çevrimdışı oto-giriş, engel mesajları) gerçek çok-makineli ortamda kullanıcı testiyle doğrulanmalı.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). Additive + geriye dönük uyumlu sunucu değişikliği; op_branch_id (kullanıcının çalışma şubesi) mantığı korunur.

### ADR-058 — Çok firmalı süper admin girişi + zorunlu şube + Tüm Şubeler (09.07.2026)
- **Bağlam:** Kullanıcı talebi: (1) web'de şube seçmeden giriş yapılabiliyordu → engellenmeli; (2) süper admin girişte FİRMA + şube seçip o firmayı yönetmeli; (3) admin kendi firmasının bir şubesini seçmeli (zorunlu); (4) "Tüm Şubeler" seçeneği admin + süper admin'de daima açık olmalı (rapor için); (5) bir firma personeli başka firmanın kaydını görmemeli.
- **Karar:**
  - (5) zaten sağlanıyor: `TenantAccessGuard` (payload firma reddi + `EnsureOwnership` fail-closed), testlerle kanıtlı. Ek iş yok.
  - (2) **Çapraz-firma süper admin oturumu:** `AuthService.CreateSessionForUser` süper admin'in kendi (home) firması olmayan var olan bir firma için de oturum kurmasına izin verir (süper admin değilse null → fail-closed). Yeni uç `POST /api/auth/select-company` (yalnız süper admin) seçilen firma için YENİ JWT (company claim = seçilen firma) + o firmanın şubelerini döner. Böylece süper admin, operasyonel/veri uçlarında (şube/malzeme/stok/araç… — `s.CompanyId` ile kapsamlanan "Pattern B") seçtiği firma olarak çalışır. Uçtan uca doğrulandı: seçilen firmada oluşturulan şube yalnız o firmada görünür.
  - Not (Pattern A): `IsSuperAdmin ? tüm firmalar : kendi` mantığı taşıyan platform ekranları (kullanıcı listesi, firma listesi, makineler) süper admin'e çapraz kalmaya devam eder — bu kasıtlı platform gözetimi, sızıntı değil.
  - (1)(3) **Şube zorunlu:** web login'de şube seçilmeden giriş engellendi (masaüstünde zaten zorunluydu).
  - (4) **Tüm Şubeler:** sunucu login yanıtı + masaüstü, `canViewAllBranches = flag || IsCompanyAdmin || IsSuperAdmin` olarak hesaplar; enforcement de bu efektif değere göre.
- **Kapsam (ADR-058 kararı):** Süper admin FİRMA seçimi **yalnız web**. Masaüstünde yapılmadı çünkü masaüstü çevrimdışı-öncelikli ve yerel SQLite **tek firmaya** ait (senkronla gelen); seçilen başka firmanın verisi yerelde olmadığından anlamlı değil. Masaüstünde yalnız "Tüm Şubeler admin/süper admin" + (zaten var olan) şube-zorunlu geçerli.
- **Gerekçe:** Kullanıcının son açık talebi (CLAUDE.md §1). Küçük, geri alınabilir sunucu değişikliği (mevcut normal-kullanıcı davranışı birebir korunur; yalnız süper admin için yeni yetenek). 3 yeni güvenlik testi + tam suit 241/241 yeşil + canlı-benzeri yerel e2e.

### ADR-057 — Gerçek mimari kaydı: Web=Blazor, sunucu DB=SQLite (09.07.2026)
- **Bağlam:** `CLAUDE.md`/`DECISIONS.md` (ADR-000/005) web tarafını Next.js+Drizzle+PostgreSQL olarak
  tanımlıyordu. Commit geçmişi incelendiğinde: `apps/web` (Next.js) son kez 2026-06-27'de değişmiş (0 commit
  son 2 haftada); `src/DepoWise.Web` (Blazor Server, MudBlazor) 2026-07-02'den beri 56 commit almış ve
  canlıda (`depowise-web.fly.dev`) çalışan gerçek uygulama bu. Ayrıca `src/DepoWise.Api`/`Infrastructure`
  yalnız `Microsoft.Data.Sqlite` referans ediyor (Npgsql/PostgreSQL sürücüsü hiç eklenmemiş);
  `ServerServices.cs` sunucu DB'sini `depowise-server.db` (SQLite, Fly.io kalıcı disk `/data`) olarak açıyor.
  PostgreSQL/Drizzle hiç üretime alınmadı (R4/R7'de zaten "uygulanmadı" olarak işaretliydi, ama CLAUDE.md
  hâlâ PostgreSQL'i "değişmez mimari" gibi gösteriyordu — çelişki).
- **Karar:** Dokümanlar gerçeğe uydurulur: **Web = Blazor Server (`src/DepoWise.Web`)**, **API/sunucu DB =
  SQLite** (`depowise-server.db`). `apps/web` kod tabanında kalır ama **donmuş/referans** olarak işaretlenir;
  üzerinde aktif geliştirme yapılmaz. PostgreSQL'e geçiş (R4/R7) bir **gelecek karar** olarak açık kalır —
  şu an iptal edilmiyor, sadece "yapılıyor" değil "yapılmadı ve planlanmıyor (henüz)" olarak netleştirilir.
- **Kapsam dışı / karar verilmedi:** PostgreSQL'e geçilip geçilmeyeceği, `apps/web`'in silinip silinmeyeceği.
  Bunlar kullanıcı talimatı bekliyor; bu ADR yalnız **mevcut durumu doğru kaydetmek** içindir.
- **Gerekçe:** CLAUDE.md §1 "çelişkide kararı DECISIONS.md'ye yaz" kuralı; kod/dokuman tutarlılığı, gelecekte
  yanlış yönlendirme riski (ör. Next.js'e zaman harcamak veya PostgreSQL varmış gibi davranmak).

### ADR-056 — COMODO kısıtlaması kaldırıldı, yeni PC (09.07.2026)
- **Bağlam:** Kullanıcı bilgisayarını formatladı ve geliştirmeyi COMODO'nun kurulu olmadığı farklı bir PC'ye taşıdı. COMODO'nun Auto-Containment özelliği imzasız EXE/BAT'ı sanal alanda çalıştırıp sahte/boş bir DB'ye yazdırdığı için (bkz. `docs/COMODO_RUNBOOK.md`) bu kısıtlama konulmuştu; yeni makinede COMODO yok.
- **Karar:** `.claude/hooks/comodo_guard.ps1`'i tetikleyen PreToolUse hook `.claude/settings.json`'dan kaldırıldı. `CLAUDE.md` §6, `DEVAM.md` §5 ve `BASLAMA_REHBERI.md` güncellendi: proje EXE/BAT artık doğrudan çalıştırılabilir. `dotnet build`/`dotnet run` yine de önerilen yöntem olarak kaldı (alışkanlık/tutarlılık, zorunluluk değil).
- **Kapsam dışı:** `Directory.Build.props`'taki `UseAppHost=false` ayarına dokunulmadı (ayrı bir build/paketleme kararı; gerekirse ileride ayrıca değerlendirilir). SQLite mutlak DB yolu, WAL, Cache=Private kuralları COMODO'dan bağımsız olduğu için aynen korundu.
- **Geri alma:** İleride tekrar bir COMODO'lu makinede geliştirme yapılırsa `docs/COMODO_RUNBOOK.md`'deki adımlarla hook ve kısıtlamalar geri eklenmelidir.

### ADR-109 — Menü / Ekran Yönetimi: platform yönetimi genişletildi, ayrı ekran açılmadı (18.08.2026)
- **Bağlam:** Kullanıcı web'e özel bir "Menü / Ekran Yönetimi" ekranı istedi: ekran sırası, üst menü
  ataması, üst menü adı/sırası, platform seçimi (Web / Masaüstü / İkisi / Hiçbiri), menüde aktif-pasif
  ve görünen ekran adı. Envanter çıkarıldığında **istenen 7 maddeden 2'sinin (platform + aktif/pasif)
  G5 ile 2026-08-12'de zaten yapılmış** olduğu görüldü: `AppScreens` kataloğu, `ScreenVisibility`
  çözümleyicisi, `Migration065`, `ScreenVisibilityService`, `/api/screens/visibility` uçları ve
  `/screen-visibility` ekranı mevcuttu. Eksik olan yalnız **menü düzeni**: ad · üst menü · sıra.
- **Karar 1 (tek ekran):** Ayrı bir `/menu-management` ekranı AÇILMADI. Mevcut `/screen-visibility`
  genişletildi ve adı **"Menü / Ekran Yönetimi"** oldu. Gerekçe: iki ekran da AYNI 59 ekranı yönetirdi;
  aynı listeyi iki yerden yönetmek kullanıcı isteğinin kendi §2 kuralına ("aynı ekran iki farklı yerde
  tanımlanmasın") aykırı olurdu. **Route (`screen-visibility`), ekran anahtarı (`screen_visibility`) ve
  yetki modülü DEĞİŞMEDİ** — yalnız görünen ad değişti, hiçbir referans kırılmadı.
- **Karar 2 (grup kimliği):** `AppScreenGroup.Title` **değişmez sistem anahtarı** kabul edildi; kullanıcının
  verdiği ad ayrı alanda (`title_override`) tutulur. Böylece katalogda **tek satır bile değişmeden**
  "grup adı değişsin ama anahtar sabit kalsın" isteği karşılandı. Alternatif (gruplara ayrı `Key` alanı
  eklemek) 17 grup + 59 ekran + iki menü + ikon eşlemesini dolaşan bir refactor gerektirirdi — reddedildi.
- **Karar 3 (grup görünürlüğü ayrı alan DEĞİL):** Bir grubu gizlemek = içindeki ekranları o platformda
  kapatmak. Menüler zaten "görünür ekranı kalmayan grubu" göstermiyor. İkinci bir gizleme yolu açmak aynı
  sonucu iki farklı kurala bağlardı.
- **Karar 4 (kaydetme):** Düzen değişiklikleri **Düzenle → Kaydet** ile toplu/atomik yazılır (tek
  transaction, tam durum). Platform kutuları ise mevcut anında-kaydeden akışta bırakıldı: yıkıcı oldukları
  için kendi onay pencereleri var ve bu akış zaten denenmiş durumda.
- **Yeni yapı:** `Migration070_MenuLayout` (`screen_menu_layout`, `menu_group_layout`), `MenuLayout`
  (saf çözümleyici, web projesine de linklendi), `MenuLayoutService` (`ScreenVisibilityService` deseni:
  TTL önbellek + yazmada düşürme + audit), `/api/screens/layout/manage` · `/api/screens/layout` ·
  `/api/screens/layout/reset`. **Satır yoksa katalog varsayılanı** → migration sonrası menü birebir aynı.
- **Yetki:** Yeni bir authorization mekanizması kurulmadı — platform yönetimiyle **aynı modül**
  (`screen_visibility`, `AppModules.IsSuperAdminOnly`). Kontrol servis katmanındadır; arayüzde gizlemek
  güvenlik sayılmadı (`ApiMenuLayoutTests` uçları doğrudan çağırarak kilitler).
- **Kapsam dışı:** Sürükle-bırak (yukarı/aşağı düğmeleri tercih edildi — §7 "gösterişli ama kırılgan
  drag-drop kurma"), menü versiyonlama, ekranların fiziksel silinmesi.

### ADR-110 — Menü düzeni ve platform ayarı masaüstüne tanım senkronuyla iner (18.08.2026)
- **Bağlam (MNU-B1, gerçek hata):** G5'in "Masaüstü" kutusu **gerçek masaüstü makinelerde hiçbir etki
  yapmıyordu.** `screen_platform_visibility` ne `BusinessSyncService.Tables` listesinde ne de
  `/api/lookups/sync` yanıtındaydı; masaüstü ise ayarı **kendi yerel SQLite'ından** okuyor
  (`DesktopServices.Factory`) → tablo daima boş → katalog varsayılanı geçerli kalıyordu.
- **Karar:** Ayarlar **tanım (lookup) senkronuyla** iner: `/api/lookups/sync` yanıtına `screenVisibility`,
  `menuLayoutScreens`, `menuLayoutGroups` bölümleri eklendi; `LookupSyncService` bunları yerele yazar.
  **İş senkronu (`BusinessSyncService`) kullanılmadı:** bunlar iş verisi değil **sunucu otoriteli
  yapılandırmadır** — masaüstü asla yazmaz, çakışma/LWW sorusu doğmaz, `version`/`is_deleted` kolonları
  yoktur. Yeni bir senkron protokolü kurulmadı.
- **Yazma biçimi:** upsert değil **replace** (firma bazlı sil-yaz). Gerekçe: sunucuda KALDIRILAN bir ayar
  yerelde de düşmeli; upsert olsaydı bir kez kapatılan ekran bir daha açılamazdı.
- **Çevrimdışı korunumu:** Alan yanıtta hiç yoksa (eski sunucu) yerele dokunulmaz. Sunucuya hiç
  ulaşılamazsa `PullAsync` zaten sessizce atlar → masaüstü **en son inen ayarla çevrimdışı çalışmaya
  devam eder**, hiç inmediyse katalog varsayılanı geçerlidir. Masaüstünün açılışta sunucuya bağlanma
  zorunluluğu **getirilmedi**.

### ADR-111 — Kritik ekranlar tüm platformlarda birden kapatılamaz (18.08.2026)
- **Bağlam (MNU-B2, gerçek hata):** `ScreenVisibilityService.Set` yalnız "bu ekran o platformda katalogda
  var mı" diye bakıyordu. Süper admin **"Menü / Ekran Yönetimi"** ekranını web'de kapatabiliyordu; kapattığı
  anda ekran menüden düşüyor, `MainLayout` route koruması adresi elle yazmayı da engelliyor ve ekranın
  masaüstü karşılığı olmadığı için **ayarı geri alacak hiçbir arayüz kalmıyordu** (kurtarma yalnız
  veritabanına elle müdahale). Aynı sınıf risk `users` ve `permissions` için de vardı.
- **Karar:** `AppScreens.Protected` = { `screen_visibility`, `users`, `permissions` }. Bu ekranlar
  **hepsi kapalı** hâline getirilemez. Kural **dar** tutuldu: tek platformda kapatmak serbesttir (diğer
  platform kurtarma yolu olarak kalır). Liste keyfî değildir — her üçü de koddan kanıtlanan bir kilitlenme
  üretiyordu; başka ekran korumalı ilan edilmedi.

### ADR-112 — Menüye ÜÇÜNCÜ seviye: ÜST GRUP (19.08.2026)
- **Bağlam:** Kullanıcı kalabalıklaşan menüyü toparlamak için üst menüleri de gruplamak istedi
  ("üst menüleri de bir üst menü oluşturup ekleyebileceğim bir yapı"). Menü bugüne kadar iki
  seviyeliydi: ÜST MENÜ → EKRAN.
- **Karar:** Üçüncü seviye eklendi — **ÜST GRUP → ÜST MENÜ → EKRAN**. Yönetim yine
  **Menü / Ekran Yönetimi** ekranından yapılır; ayrı ekran açılmadı (kullanıcı isteği).
- **Yeni tablo AÇILMADI:** üst grup da bir menü düğümüdür ve mevcut `menu_group_layout` tablosunda
  `section:` önekli anahtarla saklanır. Tek eklenen alan `parent_group_key` (Migration071,
  `ALTER TABLE ADD COLUMN`, nullable). Böylece sıralama · ad değiştirme · audit · senkron · önbellek
  yolları TEK kod üzerinden yürür.
- **`MenuLayout.Build` DEĞİŞTİRİLMEDİ (kritik):** üstüne `BuildTree` eklendi. Mevcut çözümleyici
  davranışı ve onu kilitleyen `AppScreensParityTests.S17` aynen duruyor → sıralama/ad mantığında
  sıfır regresyon riski.
- **Geri uyumluluk garantisi:** üst grup tanımlanmadığı sürece ağaç, bugünkü düz menünün BİREBİR
  karşılığıdır (her grup kendi düğümü, tek elemanlı). Gerçek GUI'de doğrulandı: kayıt yokken web ve
  masaüstü menüleri değişmedi.
- **Masaüstü ikon rayı ve grup şablonu KORUNDU:** ray düz `Groups` listesinden beslenmeye devam
  ediyor; XAML'de mevcut grup şablonuna dokunulmadı, yalnız dışına bir seviye sarmalandı.
- **Sıralama:** üst grup, İLK ÜYESİNİN bulunduğu yerde açılır. İkinci bir sıralama alanı doğmaz;
  yönetici grupları taşıdıkça üst grup da onlarla birlikte yer değiştirir.
- **Fail-closed kurallar:** üst grup başka üst grubun altına konulamaz (menü ikiden fazla derinleşmez) ·
  üst menü yalnız üst gruba bağlanabilir (grup içinde grup yok) · var olmayan üst gruba bağlanamaz ·
  kendine bağlanamaz. Ekranlar üst gruba DOĞRUDAN bağlanamaz (arayüzde listelenmez).
- **Yetim koruması:** üst grup elle silinse bile ona bağlı üst menüler kaybolmaz, sessizce en üst
  seviyeye döner.
- **Kapsam dışı:** dört ve daha fazla seviye, sürükle-bırak, üst gruba ikon seçimi.

## ADR-113 — Menü: altında ekran/üst menü kalmayan tanım SAKLANMAZ (2026-08-19)
- **Bağlam:** Kullanıcı nihai menü şemasını iletti ve ek kural koydu: *"altında ekran olmayan menü ve
  üst menü kalacak olursa eğer tanımı sil."* Şema uygulandığında üç katalog grubu (Personel · Yönetim ·
  İmport / Export) boş kaldı. Bu gruplar menüde zaten görünmüyordu (boş grup çizilmez) ama firma
  kaydında (`menu_group_layout`) boş tanım olarak duruyorlardı.
- **Karar:** `MenuLayoutService.Save` artık **altında ekran kalmayan üst menünün** ve **altında üst menü
  kalmayan ÜST GRUBUN** kaydını yazmaz. Arayüz tam durumu (boş grup dahil) göndermeye devam eder;
  eleme tek yerde, sunucuda yapılır → web · masaüstü · toplu betik aynı kuralı alır.
- **Katalog tanımı SİLİNMEZ:** `AppScreens.Groups` programda durur. Bir ekran tekrar o gruba taşınırsa
  grup kendiliğinden geri gelir; yönetim ekranında "0 ekran" satırı olarak görünmeye devam eder ki
  yönetici oraya taşıma yapabilsin. Yani silinen şey **firmaya ait boş tercih kaydıdır**, program
  kataloğu değil.
- **Ekran kaybı riski yok:** eleme yalnız GRUP kayıtlarını etkiler; ekran satırları ve yetim-ekran
  doğrulaması aynen çalışır (var olmayan gruba taşıma hâlâ reddedilir).
- **Testler:** `MenuSectionTests` S18 (boş grup yazılmaz, 58 ekran yerinde durur) · S19 (altı boş üst
  grup yazılmaz) · S20 (dolu üst grup korunur).

## ADR-114 — Nihai menü şeması artık PROJENİN VARSAYILANI (2026-08-19)
- **Bağlam:** Şema önce yalnız firma bazlı kayıt olarak uygulanmıştı. Kullanıcı: *"son attığım şemayı
  firmaya özel göndermedim; son şema projenin varsayılan menü şeması olmalı."*
- **Karar:** Şema `AppScreens` kataloğuna taşındı. Artık **hiçbir firma kaydı olmadan** menü üç seviyeli
  ve şemadaki düzende çıkar; yeni açılan her firma da bu menüyle başlar.
- **Katalog üç seviyeli oldu (additive):** yeni `AppScreens.Sections` listesi (6 üst grup) ve
  `AppScreenGroup.Section` alanı. Anahtar biçimi firma bazlı üst gruplarla **AYNIDIR** (`section:*`) →
  katalog varsayılanı ile firma tercihi tek kod yolunda buluşur, ikinci bir mekanizma doğmaz.
- **Çözümleyici kuralı:** `MenuLayout.SectionKeyOf` artık **satır varsa satır, satır yoksa katalog**
  der. Bir firma grubu en üst seviyeye taşıdığında `Save` satırı YAZAR (parent=null) — aksi hâlde
  katalog varsayılanı sessizce geri gelirdi.
- **Değişen gruplar:** `Personel` + `Yönetim` → **Şube ve Personel** · `Yönetim` (log) → **Denetim** ·
  `Yönetim` (yedek) → **Yedekleme** · `Kullanıcı` → **Kullanıcı Yönetimi** · `Raporlar` →
  **Operasyon Raporları** · `İmport / Export` → **Ayarlar** altında *Excel'e Aktarım*.
- **EKRAN KAYBI YOK — kanıtlı:** masaüstü menü bağlantısı **47 → 47**, web **55 → 55**. Sayılar
  S13/S14 testlerinde kilitli.
- **Referans testleri BİLİNÇLİ güncellendi:** S13/S14 taşıma-öncesi menüyü kilitliyordu; varsayılan menü
  kasten değiştiği için beklenen değerler yeniden yazıldı (gevşetilmedi — sıra, anahtar ve toplam sayı
  hâlâ birebir doğrulanıyor). Yeni **S14b** varsayılan üst grup haritasını ayrıca kilitler.
- **Firma kayıtları temizlendi:** şema artık varsayılan olduğu için firmaların düzen kayıtları
  `/api/screens/layout/reset` ile kaldırıldı → aynı menü, sıfır kayıt.
- **Masaüstü yeni sürüm gerektirir:** katalog masaüstü uygulamasına derlenir; eski sürüm eski
  varsayılanı gösterirdi.

## ADR-115 — Yetki ekranları: C turu düzeltmeleri (2026-08-19)
- **Bağlam:** Kullanıcı üç somut şikâyet iletti (ikon rayı yer kaplıyor · Yetkiler ekranında düzenleme
  butonu yok · Rol Yetki Kontrol web'de sürekli yükleniyor) ve yetki mimarisi için analiz istedi.
  Sunulan üç yoldan **"önce C (hatalar), sonra A (yapı)"** sıralaması onaylandı. Bu ADR **C turudur**.
- **İkon rayı KALDIRILDI (masaüstü):** `MainWindow.axaml` sütun 0'daki 56 px dikey şerit gitti.
  Taşıdığı her şeyin karşılığı menü panelinde zaten vardı (Ana Ekran · gruplar · kullanıcı) ve üst
  bardaki daralt/genişlet düğmesi YERİNDE → menüsüz kalma durumu yok. **Web'de karşılığı yoktu**
  (kontrol edildi), bu yüzden web tarafında değişiklik gerekmedi. Ölü kalan `SelectGroup` komutu ve
  `NavGroupVm.PrimaryKey` temizlendi.
- **DÜZENLE → KAYDET akışı (iki ortam):** Yetkiler ekranı artık **salt-okunur açılır**. Verilmiş
  yetkiler görünür ama tıklanamaz; "Düzenle" kilidi açar, "Vazgeç" sunucudan taze yükler, "Kaydet"
  tek seferde yazar. ⭐ **Düzenlemeye geçmek hiçbir yetkiyi silmez** — yalnız bayrak çevrilir
  (test U4 bunu kilitler).
- **ROL aynı ekranda:** Rol seçimi Yetkiler ekranına eklendi (eskiden Kullanıcı Tanım'daydı).
  Rol yetki tavanını belirlediği için **kaydetmede ÖNCE rol yazılır**, sonra ağaç yeni role göre
  yeniden yüklenir. **Kendi rolünü değiştirmek engellidir** (kilitlenme koruması).
- **Sonsuz yükleme düzeltildi:** Rol/Firma Yetki Kontrol ekranları yükleme hatasında dönen tekerlekte
  kalıyor ve hatayı hiç göstermiyordu (hata metni tablo dalının içindeydi). Artık hata **her durumda
  ekranda** ve "Yeniden dene" düğmesiyle birlikte. Sunucu tarafı sağlamdı: uç 107 ms'de 200 dönüyor.
- **⭐ Gerçek arayüz turunda BULUNAN hata (YET-C4):** `/permissions` yetkisiz açıldığında
  `OnInitializedAsync` içindeki korumasız çağrı 401 alıp **Blazor devresini tamamen düşürüyordu**
  (bembeyaz ekran, "bağlantı kesildi"). Aynı desen `PermissionTemplates` ve `Users` ekranlarında da
  vardı. Üçü de korumaya alındı; düzeltme gerçek arayüzde doğrulandı (artık istisna yok).
- **Kapsam dışı (A turuna kaldı):** dört ekranın ikiye indirilmesi ve rol tavanının firma bazlı
  yapılması. Bu ADR yalnız hataları ve eksik akışı kapatır; ekran sayısı DEĞİŞMEDİ.

## ADR-116 — Yetki mimarisi A turu: rol tavanı firma bazlı + ekranlar birleşti (2026-08-19)
- **Bağlam:** Kullanıcı yetki yapısını karmaşık buldu ve sunulan üç yoldan **A**'yı seçti; sıralama
  "önce C (hatalar), sonra A (yapı)" olarak onaylandı. Bu ADR **A turudur**.
- **A1 — ROL TAVANI ARTIK FİRMA BAZLI (Migration 072).** `role_grant_limits` tablosunda firma kolonu
  YOKTU: tablo **platform geneliydi** ve kaydetme `DELETE FROM role_grant_limits;` ile tabloyu komple
  siliyordu → bir firmada yapılan tek değişiklik **bütün firmaları** etkiliyordu. Tabloya `company_id`
  eklendi, benzersizlik `(company_id, role_key, module_key)` oldu.
  **Veri kaybı yok:** mevcut ortak satırların her biri **her firmaya kopyalandı** (kullanıcı kararı) →
  yükseltme öncesi ve sonrası her firmanın gördüğü kısıt AYNIDIR. Migration doğrulama kapısı taşır:
  `yeni == eski × firmaSayısı` tutmazsa istisna atar ve hiçbir şey yazılmaz.
  Okuma yolu (`BlockedForRoles` / `BlockedForUser`) artık **firma parametresi zorunlu** alır — oturum
  kurulurken de firma süzgeci uygulanır.
- **A2 — İKİ EKRAN TEK EKRAN OLDU.** "Firma Yetki Kontrol" + "Rol Yetki Kontrol" → **Firma Yetki Paketi**
  (route `company-permissions` KORUNDU) · iki sekme: *Ekran paketi* ve *Rol tavanı* · **tek firma
  seçicisi** ikisini birden yükler. İkisi de aynı soruyu ("bu ekran kime verilebilir?") farklı
  eksenlerden soruyordu; artık ikisi de firma bazlı.
  `role_permissions` **EKRANI** katalogdan kalktı; **MODÜL anahtarı korundu** (yetki ağacı ve rol
  kısıtları onu kullanmaya devam ediyor). Eski sayfa dosyası silindi → tek giriş noktası.
- **A3 — ŞABLON KISAYOLU (kapsam bilinçli olarak daraltıldı).** Yetkiler ekranına **"Şablondan doldur"**
  eklendi (web + masaüstü): şablon **yalnız kutuları doldurur, sunucuya yazmaz** — kararı "Kaydet" verir.
  **Yetki Şablonları ekranı KALDI.** Öneride "şablonlar ayrı ekran olmaktan çıksın" denmişti; uygulamada
  şablonlar *kalıcı nesnelerdir* (oluşturma/silme/firma kapsamı) ve tam yönetimi bir açılır pencereye
  gömmek hem kullanımı hem riski kötüleştirirdi. Sonuç: **4 ekran → 3 ekran + kısayol**; kullanıcının
  şikâyet ettiği iki çakışan tavan ekranı birleşti.
- **Değişmeyenler (bilinçli):** yetki zincirinin anlamı, API sözleşmeleri (yalnız `companyId` eklendi),
  `AppModules` kataloğu, şablon uçları ve yetki ağacının kendisi.

## ADR-117 — Giriş · makine · eşitleme: saha bulgusu ve düzeltmeler (2026-08-19)
- **Bağlam:** Kullanıcı makineleri önce sıfırladı, sonra sildi. Ardından (1) babası internet varken
  giriş yapamadı, (2) kendi makinesinde **şube seçim ekranı hiç gelmedi** — makinenin önbellekteki
  eski şubesine sessizce girildi, (3) silinmiş test kayıtlarına ait "6 kayıt gönderilemiyor" uyarısı
  temizlenemiyordu.
- **ORTAK KÖK NEDEN — "sunucuya ulaşılamadı" ile "çevrimdışıyım" aynı sayılıyordu.** Giriş yolundaki
  iki ağ çağrısının zaman aşımı **6 sn** (`MachineGate`) ve **10 sn** (`ServerAuthClient`) idi. Sunucu
  veritabanı boşta uyuduğu için günün ilk isteği bu süreyi aşabiliyor; istek düşünce uygulama kendini
  **çevrimdışı** sayıyordu. Kanıt: makinenin yerel önbellek dosyaları (`machine_branch.txt`,
  `machine_status.txt`) **7 gündür güncellenmemişti** — yani `/api/machines/register` bir haftadır hiç
  başarılı olmamıştı; oysa uç ölçüldüğünde **200 / 1,4 sn** dönüyor.
- **B1:** süreler **20 sn / 25 sn**, `MachineGate` başarısız ilk denemeden sonra **bir kez daha** dener.
  Ayrıca sunucu kimliği doğruladıktan SONRA yapılan **yerel aynalama** adımları kendi `try/catch`ine
  alındı: yerel bir yazma hatası artık "çevrimdışı" saymıyor (eskiden tek dış `catch` her hatayı ağ
  hatası gibi ele alıyordu).
- **B2 — sessiz oto-şube girişi KALDIRILDI.** Çevrimdışıyken makinenin önbellekteki şubesine
  doğrudan giriliyor, şube adımı hiç gösterilmiyordu. Kullanıcı hangi şubeye girdiğini görmüyor ve
  kayıtlar yanlış şubeye yazılabiliyordu. Artık şube adımı **daima** gösterilir; makine şubesi yalnız
  ön seçimdir ve durum sarı bir bilgi kutusuyla açıkça yazılır.
- **B3 — makine şubesi yokken giriş kilitlenmiyor.** Makine silinip yeniden kaydolduğunda şubesi boş
  olur; sunucuya o an ulaşılamıyorsa kullanıcı uygulamaya **hiç** giremiyordu. Artık kullanıcının kendi
  şubesi biliniyorsa giriş açılır, makine ataması çevrimiçi olununca yapılır. Şubesi de yoksa giriş
  yine engellenir (fail-closed korunur).
- **B4 — eşitleme uyarısı temizlenebilir.** "Poison" durumu 5 denemeden sonra kurulur ve o satırlar bir
  daha gönderilmez; geriye yalnız uyarı kalır ve **temizlemenin hiçbir yolu yoktu** (firma/yerel
  sıfırlama bile temizlemiyordu). Artık Senkron Durumu panelinde **"Uyarıyı Temizle"** var (hiçbir veri
  silmez, hiçbir şey göndermez) ve **her sıfırlama yolu** eşitleme defterini (`poison`/`stuck`/
  `watermark`) sıfırlar.
- **B5 — YETKİ TAMAMEN SÜPER ADMİNİN ELİNDE (kullanıcı kararı).** Süper admin artık:
  Rol tavanı matrisinde **yapısal kilitlere takılmaz** (kilit yalnız başlangıç önerisidir) ·
  süper-admin-only ekranları **istediği role verebilir** · verdiği izin **çalışma zamanında da geçerlidir**
  (`AccessControl`: açıkça verilmişse erişilir). **Deny-by-default bozulmadı:** admin bypass'ı hâlâ
  geçersiz, alt roller için kurallar aynen sürer ve bu izinleri yalnız süper admin yazabilir.
- **Kapsam dışı (ayrıca raporlandı):** eşitleme kuyruğundaki kalıcı hataların (yinelenen anahtar / ebeveyni
  silinmiş satır) kaynağında çözülmesi — bu tur yalnız kullanıcıyı kilitleyen uyarıyı temizlenebilir yaptı.

## ADR-118 — Kalıcı eşitleme hataları kuyruğu kilitlemez (2026-08-19)
- **Bağlam:** ADR-117'de kullanıcıyı kilitleyen uyarı temizlenebilir yapılmıştı ama **kaynak** duruyordu:
  bir satır hiçbir denemede başarılı olamayacak olsa bile "atlandı" sayılıyor, gönderim damgası
  ilerlemiyor ve 5 turdan sonra kalıcı uyarı bırakılıyordu. Sahadaki 6 kayıt tam olarak buydu
  (1 yinelenen kategori + 4 ebeveyni silinmiş şablon satırı + 1 ebeveyni silinmiş bakım malzemesi).
- **KALICI / GEÇİCİ AYRIMI:** `ApplyResult` artık `PermanentSkipped` taşır. Kalıcı sayılanlar:
  şube kapsamı dışı · yetki yok · ebeveyn başka firmada · **ebeveyn sunucuda hiç yok** · doğrulama
  reddi · yabancı anahtar (23503) ve benzersizlik (23505) ihlalleri. Bunların hepsi **deterministiktir**;
  tekrar denemek aynı sonucu verir.
- **ÖKSÜZ ÇOCUK ÖN KONTROLÜ:** yeni `OrphanCheckedChildren` haritası ile ebeveyni bulunmayan çocuk satır
  **veritabanına hiç gönderilmez**. Eskiden doğrudan INSERT ediliyor, PostgreSQL'de yabancı anahtar hatası
  tüm transaction'ı bozduğu için satır-başı savepoint kurtarma yoluna düşülüyordu — hem yavaş hem her
  turda tekrar eden bir hata. Kapsam: `vehicle_template_materials` · `maintenance_materials` ·
  `material_request_items` · `stock_count_lines` · `request_status_history` · `material_equivalents` ·
  `material_compatible_vehicles` · `maintenance_definition_vehicles`.
  ⚠️ Bu, `CompanyScopedChildren`'dan FARKLIDIR: orası "ebeveyn bu firmada mı" (tenant kapısı), burası
  "ebeveyn hiç var mı" sorusunu sorar.
- **İSTEMCİ KARARI:** `Retryable = Skipped − PermanentSkipped`. "Sorun var" kararı artık YALNIZ
  `Retryable`'a bakar → kalıcı atlananlar gönderim damgasını durdurmaz, kuyruk kilitlenmez, kalıcı uyarı
  oluşmaz. Kalıcı atlananlar yine **log'a** yazılır (iz kaybolmaz).
- **İKİ YÖNLÜ SÜRÜM UYUMU:** eski istemci yeni alanı yok sayar (bugünkü davranış) · yeni istemci eski
  sunucuda alanı bulamaz → 0 kalır (bugünkü davranış). Sessiz veri kaybı üretmez.
- **⚠️ ÖZ-DENETİMDE BULUNAN HATA (yayından ÖNCE):** PostgreSQL kurtarma yolunda (bir satır patlayınca
  tablo geri alınıp satırlar BAŞTAN uygulanır) kalıcı sayaç sıfırlanmıyordu → aynı satırlar iki kez
  sayılıyor, `PermanentSkipped > Skipped` olabiliyor ve istemci "yeniden denenecek satır yok" sonucuna
  varıp **gerçekten yeniden denenmesi gereken satırları sessizce düşürebiliyordu (veri kaybı)**.
  Üç sayaç artık birlikte sıfırlanır; P4 testi bunu kilitler.
- **VERİ KAYBI YOK:** kalıcı atlanan satırlar zaten hiçbir zaman uygulanamayacak satırlardır
  (ebeveyni olmayan çocuk / mevcut doğal anahtar). Geçerli veride davranış birebir aynıdır — P2 testi
  ebeveyni olan satırın normal uygulandığını kilitler.

## ADR-119 — Makine Yönetimi: şube filtresi listelemeyi kilitliyordu (2026-08-20)
- **Bağlam:** Kullanıcı "webte makine yönetimi ekranında makineleri listeleyemiyorum" dedi ve sorunun
  sunucudan mı koddan mı geldiğini sordu.
- **SUNUCU SAĞLAM (ölçüldü):** `/api/machines` → **200 · 405 ms · 2 makine**.
  Firma süzgeciyle de doğru çalışıyor (`?companyId=…` → 2 makine).
- **KÖK NEDEN — EKRANDA:** "Sorgula" düğmesi `Disabled="@(string.IsNullOrEmpty(_branchId))"` ile
  **ŞUBE seçilene kadar kapalıydı**. Süper admin firmayı seçse bile düğme gri kalıyordu; şube seçse
  bile **yalnız o şubenin** makineleri geliyordu. Firmanın **9 şubesi** var ve iki makine iki farklı
  şubede (DÜZCE · TEST ŞANTİYE) → doğru şubeyi bilmeden makineyi bulmak pratikte imkânsızdı.
  "Kayıtsız Makineler" görünümü de yardımcı olmuyordu: her iki makinenin de şubesi ATANMIŞ olduğu için
  o liste boş dönüyor.
- **Düzeltme:** şube artık **isteğe bağlı**. Süper adminde düğme yalnız FİRMA seçimini bekler; şube boş
  bırakılırsa firmanın **tüm makineleri** listelenir (API `branchId` olmadan zaten bunu yapıyordu —
  sunucu değişmedi). Ekran metni ve "sonuç yok" mesajı bu davranışı açıkça anlatır.
- **Yanında düzeltilen:** ekranın `OnInitializedAsync` çağrıları korumasızdı (YET-C4 ile aynı sınıf):
  sunucu 401/500 dönerse Blazor devresi tamamen düşüp bembeyaz ekran bırakabilirdi. Korumaya alındı.
- **Test:** `PermissionScreenUxTests.U10` — düğme şubeye değil firmaya bakar, açıklama metni yerinde,
  ilk yükleme korumalı.

## ADR-120 — Giriş: varsayılan şube kullanıcının kendi şubesi + makine şubesi işareti (2026-08-20)
- **İstek (kullanıcı):** giriş ekranında şube kutusu **kullanıcının kendi şubesiyle** açılsın; isteyen
  seçimi değiştirip makine şubesine ya da başka bir şubeye girebilsin; listede makine şubesini belirten
  bir **simge** şube adının başında olsun.
- **Varsayılan seçim:** zaten kullanıcının kendi şubesiydi; niyet koda açık yazıldı ve **L6 testiyle
  kilitlendi** (sıra bozulursa kullanıcı her girişte yanlış şubeyle başlar). Kullanıcının şubesi listede
  yoksa (kapsam dışı / tanımsız) makinenin şubesine düşülür — bu yedek davranış korundu.
- **Makine şubesi işareti:** `LoginBranch` kaydına yalnız-görüntü amaçlı `IsMachineBranch` eklendi ve
  `ToString()` işaretli şubenin adının başına **🖥** koyuyor. Desen "🌐 Tüm Şubeler" ile aynıdır.
- **İşaretleme SIRASI önemlidir:** liste kurulup **kapsamla kırpıldıktan SONRA** işaretlenir
  (`FilterBranchesByScope()` → `MarkMachineBranch()`). L7 testi bu sırayı kilitler.
- **HİÇBİR MANTIK DEĞİŞMEDİ:** işaret yalnız görüntüdür. Şube kimliği (`Id`), yetki/kapsam kırpması,
  şube şifresi kuralı (`ShowBranchPassword`), "bu makinenin şubesi → şifre gerekmez" davranışı ve
  süper admin akışı aynen korundu. Sunucuya dokunulmadı.
- **Ekranda tek satır açıklama:** listede makine şubesi varsa "Varsayılan olarak kendi şubeniz
  seçilidir. 🖥 işaretli şube bu makinenin şubesidir." yazısı görünür.

## ADR-121 — Uçtan uca denetim: bağlantı tablolarında firma sınırı (2026-08-25)
- **Nasıl bulundu:** senkron gönderiminde (push) `company_id` kolonu OLMAYAN tablolar tek tek incelendi;
  ardından **açığı üreten testler önce yazıldı** (`TenantLinkTableTests`, 4/4 kırıldı), sonra düzeltildi.
- **TNT-01 (kritik, YAZMA sızıntısı):** `vehicle_template_materials` firma kapısı listesinde
  (`CompanyScopedChildren`) **hiç yoktu**. A firmasının makinesi, gönderdiği pakete B firmasının şablon
  kimliğini yazarak **B'nin araç şablonuna malzeme satırı ekleyebiliyordu**. Tablo listeye eklendi;
  kardeş tablolar zaten oradaydı, ikinci bir mekanizma kurulmadı.
- **TNT-02 (OKUMA sızıntısı):** kapı yalnız **ebeveyn** ucunu doğruluyordu. `material_equivalents`
  satırında `material_id` kendi firmasınınken `equivalent_material_id` **başka firmanın** malzemesi
  olabiliyordu ve malzeme kartı o muadili **kod + adıyla** gösteriyordu. Yeni `CrossCompanyRefs` kapısı
  bağlantının **karşı ucunu** da doğrular (muadil · uyumlu araç · bakım tanımı aracı · şablon malzemesi ·
  talep kalemi · bakım malzemesi · sayım satırı).
- **Kural bilinçli olarak DAR:** satır yalnız referans edilen kayıt **VAR ve başka firmaya ait** olduğunda
  reddedilir. Kayıt sunucuda henüz yoksa karar verilmez — delta senkronunda eş kayıt aynı pakette
  gelmemiş olabilir; meşru akış kırılmasın (öksüzlüğü `ParentExists` zaten ele alıyor).
- **Sıra:** öksüz kontrolü firma kapısından ÖNCEye alındı. İkisi de REDDEDER; sıra yalnız kullanıcıya
  giden mesajı belirler ("kayıt sunucuda yok" ≠ "başka firmada").
- **Performans:** ikincil referans kontrolü tablo ömürlü bir bellek kullanır (aynı malzeme bir sayım
  belgesinde onlarca satırda geçer) → satır başına ek sorgu YOK.
- **Okuma savunması:** `MaterialService.GetDetail` muadilleri artık firma filtresiyle okur
  (`GetEquivalentGroup(materialId, companyId)`). Aynı savunma malzeme LİSTESİ sorgusunda zaten vardı;
  kart bundan yoksundu. Veritabanında eski/bozuk bir satır olsa bile kart sızdırmaz (TNT-03).
- **SEC-02:** `VehicleService.MeterHistory` oturum almadan ve firma filtresi olmadan yazılmıştı; oturum
  zorunlu hâle getirildi ve sorguya `company_id` eklendi.

## ADR-122 — Uçtan uca denetim: üç raporda şube kapsamı eksikti (2026-08-25)
- **Bağlam:** DEN-E1/E2 turunda (2026-08-18) Stok Durumu ve Şube Bazlı Özet düzeltilmişti. Denetimde
  **aynı eksiğin üç raporda daha** durduğu görüldü. Testler önce yazıldı (4/4 kırıldı), sonra düzeltildi.
- **RPR-01/02 — Araç (Şablonlu / Şablon Dışı):** rapor **şube kolonunu gösteriyor** ama kapsamı
  uygulamıyordu → tek şubeye yetkili kullanıcı tüm firmanın araçlarını ve **plakalarını** görüyordu.
  `ReportScope.BranchSql(s, req, "v.branch_id")` eklendi (kardeş raporlarla aynı kalıp).
- **RPR-03 — Stok Sayım:** `req.LocationIds` **aynen** kullanılıyordu → (a) filtre boşken tüm şubelerin
  sayımları görünüyor, (b) isteğe başka şubenin depo kimliği yazılırsa o depo okunuyordu (parametre
  manipülasyonu, fail-open). DEN-E2'deki kalıbın birebir aynısı uygulandı: izinli ∩ istenen, boş
  kesişimde **boş sonuç** (fail-closed), ATANMAMIŞ kovası gizlenmez.
- **Sınırsız kullanıcıda (admin / tüm şubeler) davranış değişmedi** — üç rapor için de ayrı test var.

## ADR-123 — WEB-01: korumasız ilk yükleme Blazor devresini düşürüyordu (2026-08-25)
- **Sorun:** Blazor Server'da `OnInitializedAsync` içinde yakalanmayan istisna yalnız ekranı bozmaz,
  **kullanıcının devresini tamamen düşürür** (bembeyaz ekran + "bağlantı kesildi"). 401 (oturum düştü)
  ya da 500 (ör. sunucu diski doldu — R30) bunu tetikler.
- **Bulunanlar:** Stok Sayım · Stok Dağıtım · Stok Hareketleri. Üçü de ortak lokasyon önbelleğini
  (`LocationOptions` → `/api/branches`) **korumasız** çağırıyordu. Aynı hata YET-C4'te dört ekranda
  düzeltilmişti; bu üçü atlanmıştı.
- **Kalıcı çözüm:** `WebCircuitGuardTests` — sayfa kaynaklarını tarayıp "ilk yüklemede try DIŞINDA
  istisna fırlatabilen çağrı" arar. Ad çözümlemesi **dosya-içidir** (iki sayfadaki aynı adlı metot
  karışmaz). Hata yutan yardımcılar (ör. `OptionsAsync`) serbesttir → kural gereksiz sıkı değildir.
- **Testin kendisi doğrulandı:** düzeltme geri alınıp koşuldu → test **kırıldı**; geri konunca **geçti**.
  (İlk sürüm çok satırlı imzaları göremediği için "her zaman yeşil" bir kabuktu; bu deneme yakaladı.)

## ADR-124 — SIF-02: açık oturumda sıfırlama isteği artık algılanıyor (2026-08-25)
- **Sorun (backlog'da A önceliğiyle bekliyordu):** ADR-084 "yerelini sıfırla" isteği **yalnız giriş
  anında** kontrol ediliyordu. Program açıkken sıfırlama yapılırsa 15 saniyelik eşitleme turu dönmeye ve
  **az önce silinen veriyi sunucuya geri göndermeye** devam ediyordu → sıfırlama fiilen geri alınıyordu.
  Bugüne kadarki önlem yalnız operasyoneldi ("önce tüm programları kapatın").
- **Çözüm:** periyodik tur, **`SyncGate`'ten ve PUSH'tan ÖNCE** bekleyen sıfırlama isteğini sorar
  (yavaş kadans, 60 sn — çakışma bildirimiyle aynı grup). İstek varsa tur durur, kullanıcıya ne olduğu
  anlatılır ve oturum güvenle kapatılır. Desen "makine pasife alındı" akışının aynısıdır.
- **Sıfırlama YİNE tek yerde uygulanır** (giriş akışı) — burada veri silinmez, yalnız gönderim durdurulur.
- **Çevrimdışı fail-safe:** uç erişilemezse bayrak açılmaz → internet kesikken uygulama kendini
  kilitlemez (çevrimdışı çalışma bu ürünün temel özelliğidir). Üç testle kilitlendi (SIF-02a/b/c).
- **Senkron protokolü DEĞİŞMEDİ:** yeni uç yok, sunucu davranışı aynı; yalnız istemci mevcut
  `/api/sync/local-reset-status` ucunu bir de tur başında soruyor.

## ADR-125 — SEC-03: Geliştirici modu yalnız süper admine açılır (2026-08-25)
- **Açık:** masaüstünde *Ayarlar › Geliştirici Modu* ekranını açabilen **herhangi bir kullanıcı**
  (yalnız `settings` görüntüleme yetkisi yetiyordu) kaynak kodda **sabit** yazan kodu girerek
  `DeveloperMode.IsActive`'i açabiliyordu. Bu bayrak `AccessControl`'ün **her** kararında süper admin
  gibi davranır → o oturumda tüm ekranlar/işlemler açılır ve yazılan veri **eşitlemeyle sunucuya gider**.
  Depo herkese açık olduğu için "kodu kimse bilmez" varsayımı da geçersizdi.
- **Kapı (tek otorite):** `DeveloperMode.CanActivate/TryActivate` → **ham** `SessionContext.IsSuperAdmin`.
  `AccessControl.IsAdmin` **bilinçli kullanılmadı**: o metot `IsActive`'i de sayar; mod bir kez açıldığında
  kapı kendi kendini açık tutardı (**döngüsel yetki**). Kısıtlı süper admin de alamaz (devredilemez yetki).
- **Katmanların tamamı** kapatıldı — UI gizleme tek başına güvenlik değildir: etkinleştirme · masaüstü
  **gezinme** (Navigate) · masaüstü **menü** · web sayfası · web menüsü · **sunucu ucu**
  (`POST /api/settings/developer` artık süper admin ister; eskiden firma admini yetiyordu).
- **Masaüstü menüsü artık katalogdaki sözde-anahtarları uyguluyor** (`@super`/`@admin`/`@superr`).
  Bu kural web'de vardı, masaüstünde YOKTU → aynı ekran web'de gizli, masaüstünde açıktı.
- **Kanıt:** 12 test. Düzeltme geçici geri alınıp koşuldu → **9/12 kırıldı**; geri konunca 12/12 geçti.

## ADR-126 — RPR-06: Masaüstü raporlarında bitiş gününün tamamı düşüyordu (2026-08-25)
- **Hata (veri doğruluğu):** Avalonia `DatePicker` seçilen günü **gece yarısı** verir; SQL koşulu
  `tarih <= @to` olduğu için **bitiş gününün tamamı** rapordan düşüyordu. "01.08 – 25.08" raporunda
  25.08'de girilen hiçbir kayıt görünmüyordu. Ayrıca yerel saat dilimi yorumu sınırları **3 saat** kaydırıyordu.
- Web aynı hatayı **2026-08-13'te** düzeltmişti; masaüstü atlanmıştı → **aynı filtre iki platformda
  farklı sonuç** veriyordu.
- **Çözüm:** `Application/Reports/ReportDateRange` — tek kural (gün bileşeni + `Kind` nötrleme + UTC +
  gerekirse gün sonu). Web'in `FieldChecks.ToUnixMs`'i ile **birebir aynı**; parite testle kilitli.
- **Kanıt:** 12 test — hatanın etkisi **gerçek rapor üzerinden**, gün sonu, aynı gün aralığı, saat dilimi,
  artık yıl, web≡masaüstü parite, kaynak kilidi.

## ADR-127 — RPR-04: Rapor filtre listeleri şube kapsamsızdı (2026-08-25)
- **Sızıntı:** `/api/reports/scope` **şube** listesini kapsamla kırpıyordu (GUI-04) ama **araç** ve
  **personel** listelerini kırpmıyordu → tek şubeye yetkili depo personeli, rapor filtresini açtığında
  firmanın **bütün araç plakalarını** ve **bütün personel adlarını** görüyordu. Masaüstünde de aynıydı.
- **Çözüm:** `VehicleService.ListForReportFilter` + `LookupService.ListPersonnelForReportFilter` (yeni,
  additive). **Web ve masaüstü AYNI metodu çağırır** → ayrışamaz.
- **Paylaşılan `List()` / `ListPersonnel()` metotlarına DOKUNULMADI** — 20'den fazla tüketicisi var ve
  içe aktarma servisleri kod/ad çözerken **tüm** listeye muhtaç. Onları daraltmak çalışan akışları kırardı.
- Yeni metotların kapısı **`reports`** yetkisidir (`vehicles`/`definitions` değil) — ilk sürümde yanlış
  modül istendi, **test yakaladı**; erişim davranışı değişmedi, yalnız kapsam eklendi.

## ADR-128 — RPR-07: Operasyon / Yönetici rapor ekranları gerçekten ayrıldı (2026-08-25)
- **Durum:** iki menü girişi **aynı route** ve **aynı gezinme anahtarını** kullanıyordu → tek ekran;
  ayrım yalnız web menüsündeki görünürlük kapısıydı. Raporu **çalıştırmak** hiçbir yerde ayrılmıyordu
  (yalnız Excel yetkisi ayrılıyordu) → ayrım fiilen **kozmetikti**.
- **Yeni davranış (ayrım İŞLEVSEL ve ŞUBE KAPSAMINDA):**
  - `/reports` · `reports` → **Operasyon**: yalnız **çalışma şubesi** (girişte seçilen şube), şube seçici
    YOK, yalnız `Standard` raporlar.
  - `/reports/manager` · `reports:manager` → **Yönetici**: izinli şubeler + (yetkisi varsa) seçici, tüm
    raporlar, menüde `@admin` (artık iki platformda da).
- **R33 kapandı:** `ReportReqDto.OperatingBranchId` eklendi. Sunucu `BranchAccess.Require` ile **doğrular**
  (kapsam dışı → 403) ve oturum **kopyasına** yazar; `BranchAccess` kesişimi zaten uygulanır →
  **kapsamı genişletemez**. Alan gönderilmezse davranış eskisiyle **birebir aynı**. Desen içe-aktarma
  ucundan alındı; **ikinci bir kapsam mekanizması kurulmadı**. Export aynı kapsamdan geçer.
- **Yönetici raporu kapısı** `ReportService.Run` içinde — tek nokta, iki platform + API. Gerekçe: yönetici
  raporları oturumun çalışma şubesini **bilinçli olarak** yok sayar (`BranchScopeTests` ile kilitli ürün
  kararı) → "yalnız giriş yapılan şube" kuralı orada **sağlanamaz**.
  ⚠️ **Davranış değişikliği:** yönetici olmayan kullanıcı 5 yönetici raporunu artık çalıştıramaz.
  Web menüsü bunu zaten `@admin` ile ima ediyordu ve Excel yetkisi de ayrıydı.
- **Ekran anahtarları değişmedi** (`reports` / `reports.manager`) → firmaların kayıtlı menü düzeni ve
  platform görünürlük satırları **aynen** çalışır. Kod kopyalanmadı: tek bileşen, kip route'tan gelir.
- **Yan fayda (ölçüldü):** 30.000 hareketli veride depo personelinin raporu **196 ms → 28 ms**, satır
  sayısı 30.000 → 3.000. Kapsam daraltması aynı zamanda PRF-01'in en sık karşılaşılan hâlini çözer.

## ADR-129 — SEC-04: Makine yedek listesi firma sınırını uygulamıyordu (2026-08-25)
- `GET /api/backups` yalnız "giriş yapılmış mı" diye bakıyordu; **firma parametresi istekten geliyor ve
  doğrulanmıyordu** → herhangi bir kullanıcı başka firmanın makine adlarını, yedek dosya adlarını,
  boyutlarını ve tarihlerini listeleyebiliyordu.
- Kardeş uç (`/api/machine-backups/download`) bu iki kontrolü zaten doğru yapıyordu; **eksik olan buydu**.
  Düzeltme kardeş ucun deseninin aynısıdır. `DELETE` zaten süper admin istiyordu.
- **Kanıt:** 3 test; düzeltme geçici kaldırılıp koşuldu → **2/3 kırıldı**.

## ADR-130 — UPD-01: boş checksum güncelleme doğrulamasını ATLIYORDU (2026-08-26)
- **Durum:** masaüstü kurulumcusu `if (!IsNullOrWhiteSpace(expectedSha) && !VerifyChecksum(...)) throw;`
  yazıyordu. Sunucudan **boş** checksum gelirse koşul kısa devre yapıyor ve **doğrulama tamamen atlanıyordu**:
  inen zip açılıp uygulamanın kurulum dizinine kopyalanıyor, uygulama yeniden başlatılıyordu.
- **Neden ciddi:** bu, güncelleme yolunu "sunucudan ne gelirse onu çalıştır"a çevirir. Bozuk/yarım indirme,
  hatalı bir sürüm kaydı ya da araya giren bir aktör aynı kapıdan geçer.
- **Karar:** tek kapı `UpdateService.RequireVerifiedPackage` — **fail-closed**. Checksum yoksa "doğrulama yok"
  değil **"kurulum yok"** demektir. Sunucu tarafı yayında zaten 64 hane hex zorunlu kılıyordu; eksik olan
  istemcinin sunucu cevabına **koşulsuz güvenmesiydi**.
- **Doğru checksum'da davranış DEĞİŞMEDİ** (1.0.149 dahil tüm gerçek paketlerde checksum doludur).
- **Kanıt:** 7 test (boş/null/boşluk/yanlış/yarım-inen + doğru checksum kilidi + kurulumcunun kapıyı
  çağırdığını doğrulayan kaynak kilidi) ve sürüm karşılaştırma kilidi.

## ADR-131 — RPR-08 DENENDİ ve GERİ ALINDI: stok raporları çalışma şubesiyle daralmaz (2026-08-26)
- **Gözlenen "tutarsızlık":** 14 operasyon raporundan 12'si `ReportScope` (İZİNLİ ∩ **ÇALIŞMA ŞUBESİ**)
  kullanırken **Stok Durumu** ve **Stok Sayım** `BranchAccess.Allowed` kullanıyor — yani oturumun giriş
  şubesini yok sayıyor.
- **Denendi:** `Effective`'e çevrildi → **mevcut bir test kırıldı** (`MaintenanceStockLocationTests`).
- **İnceleme sonucu:** tutarsızlık **bilinçlidir**. Bu iki raporun filtre boyutu **şube değil, stoğun
  FİZİKSEL YERİDİR** (depo/şantiye). Kullanıcı Depo A'da çalışırken Depo B'den malzeme çekebilir
  (bakım stok lokasyonu, STK-04/05/06). Çalışma şubesini oraya uygulamak, ürünün **desteklediği** akışı kırardı.
- **Karar:** değişiklik **geri alındı**; gerekçe koda ve teste kalıcı yazıldı. `Allowed` yine gerçek bir
  güvenlik kapısıdır (yetkisiz depo istenemez, fail-closed) — uygulanmayan şey **görünüm tercihidir**.
- **Kilit:** karar iki yönden test edildi (yetki uygulanır / meşru akış kırılmaz).

## ADR-132 — RPR-09: operasyon ekranında elle şube listesi geçmez (2026-08-26)
- **Durum:** operasyon rapor ekranında şube seçici yoktur; ama sunucu gövdedeki `branchIds`'i "şube seçme"
  özel butonu olan kullanıcılar için uyguluyor ve bu liste `BranchAccess.Effective` sözleşmesi gereği
  **çalışma şubesinin YERİNE** geçiyordu.
- **Sızıntı YOKTU** (yetki kesişimi korunuyordu); kırılan şey "operasyon raporu yalnız giriş yapılan şubeyi
  gösterir" güvencesinin **koşulsuz** olmasıydı.
- **Karar:** istek `operatingBranchId` taşıyorsa (yalnız operasyon ekranı gönderir) `branchIds` **yok sayılır**.
  Yönetici ekranı ve masaüstü davranışı değişmedi.
- **Kanıt:** R25/R26 düzeltme geri alınınca kırılıyor; R27 (yönetici kipi kilidi) her iki durumda geçiyor.
  Export kapsamı **Excel içeriği açılarak** ölçüldü.

## ADR-133 — RPR-12: rapor listesi = kullanıcının çalıştırabildikleri (2026-08-26)
- **Durum:** bazı raporlar başka bir ekranın verisini gösterir ve servisleri O ekranın iznini ister
  (Cari Ekstre → `parties`, Fatura → `invoices`, Kasa/Banka → `finance`). Katalog bunu bilmediği için
  liste, izni olmayan kullanıcıya da gösteriyor ve kullanıcı **Sorgula'ya basınca 403** alıyordu.
- **Karar:** `ReportDescriptor.RequiredModule` (opsiyonel, sona eklendi → geriye uyumlu). Web ve masaüstü
  listeleri **aynı** süzmeyi uygular. Servis kapısı yerinde durur — bu yalnız **görünürlüktür**.
- **Yeni Personel raporu** kişisel veri (ad, telefon, kullanıcı adı) gösterdiği için `personnel` iznini,
  Muayene/Sigorta raporu `inspection` iznini **ayrıca** ister.
- **Kanıt:** katalog anahtarlarının gerçekliği + katalog-servis kapısı tutarlılığı testle kilitlendi;
  gerçek arayüzde depo personeli 14 rapor, admin 21 rapor gördü.

## ADR-134 — RPR-13: tarih alanı önceki rapordan taşınmaz (2026-08-26)
- **Gerçek arayüz turunda bulundu:** tarih ZORUNLU bir rapordan (varsayılan "Bu Ay") tarih zorunlu OLMAYAN
  bir rapora geçince alanlar dolu kalıyor ve yeni raporu **sessizce daraltıyordu**.
- **Etkilenen üç rapor:** Muayene/Sigorta (gelecek aydaki sigorta belgesi görünmüyordu → "sigorta kaydım yok"),
  **Cari Bakiye Özeti** ve **Kasa/Banka Özeti** (bakiye yalnız o ayın hareketiyle hesaplanıp **toplam bakiye**
  sanılabiliyordu — para raporunda sessiz yanlış okuma).
- **Karar:** yalnız `RequiresDate=false` bir rapora geçilirken tarihler temizlenir. Tarih ZORUNLU raporların
  davranışı **hiç değişmedi** (kullanıcının girdiği aralık korunur).

## ADR-135 — PRF-01: rapor ekranında çizim sınırı (sanallaştırma DEĞİL) (2026-08-26)
- **Ölçüm (gerçek tarayıcı):** 20.000 satırlık rapor **36.959 ms** ve **260.729 DOM düğümü**; aynı sorgu
  sunucuda **162 ms**. Darboğaz sorgu değil **çizim**. Rapor tavanı 50.000 olduğu için en kötü hâlde
  tarayıcı fiilen kilitleniyordu.
- **Karar:** `DwDataGrid.MaxRender` (opsiyonel, varsayılan **sınırsız** → bu bileşeni kullanan diğer ekranlar
  etkilenmez); **yalnız rapor ekranı** 1.000 uygular.
- **Neden `Virtualize` değil:** tablo sabit yükseklikli bir kaydırma kabında değil; sanallaştırma bu
  bileşeni kullanan **tüm** ekranların görünümünü değiştirirdi.
- **Sonuç:** 36.959 ms → **378 ms**, 260.729 → **13.746** düğüm. Filtre/sıralama/toplam **tüm satırlarda**
  çalışmaya devam eder (20.000 satırda 15.000'inci satır kolon filtresiyle bulundu) ve **Excel eksiksizdir**.
  Kırpma kullanıcıya **açıkça bildirilir** — sessiz kırpma yoktur.

## ADR-136 — YED-01: sunucu yedeği PostgreSQL'de çalışmıyordu (2026-08-26)
- **Durum:** `BackupService` tek-dosya kopyası alır (`VACUUM INTO`) ve bütünlüğü `PRAGMA integrity_check`
  ile doğrular; ikisi de **SQLite'a özgüdür**. Sunucu 2026-07-24'te PostgreSQL'e taşındığından beri
  üretimde "Yedek Al" **ham veritabanı hatasıyla** düşüyordu.
- **Daha tehlikelisi `Restore`:** yedek dosyasını `_factory.DatabasePath` üzerine kopyalar; PostgreSQL'de
  bu değer `"(postgres)"` sabitidir → yol anlamsız ve **yıkıcıydı**.
- **Karar:** her iki yol da **dosyaya dokunmadan**, anlaşılır bir mesajla durur. Geri yüklemede kapı
  yetkiden hemen sonra çalışır. Masaüstü (SQLite) davranışı **hiç değişmedi**.
- **Kapsam dışı bırakılan (kullanıcı kararı gerekiyor):** PostgreSQL için gerçek dosya dökümü `pg_dump`
  ister; o araç sunucu konteynerinde **yoktur** ve uygulama içinde dökümcü yazmak **yeni bir özelliktir**.
  Bugün üretim yedeği sağlayıcının sürekli yedeğine (PITR) dayanır.
- **Kanıt:** 4 test — ikisi **gerçek PostgreSQL** üzerinde koşturuldu; ham SQL hata kodunun kullanıcıya
  sızmadığı ve hiçbir dosya oluşmadığı da doğrulandı.

## ADR-137 — RPR-10/11: eksik iki rapor tamamlandı (2026-08-26)
- **Muayene/Sigorta** ve **Personel Listesi** raporları yoktu; oysa **veri modeli, servisi ve ekranı** vardı.
- **İş kuralı uydurulmadı:** kolonlar mevcut ekranlardan birebir alındı, durum eşiği ekranın kullandığı
  **aynı sabitten** okunur (`InspectionService.ApproachingDays = 30`), "Erişim" rozeti Personel ekranıyla
  aynı kuraldır.
- **Ekranın yapmadığı tek ek:** şube kapsamı (diğer operasyon raporlarıyla aynı kalıp).
- **"Satın Alma" kategorisi doldurulmadı:** kodda satın alma **domaini yok** (yalnız talep durumu olarak
  "Satın Alma Sürecinde" geçiyor). Sahte ekran üretilmedi → **kullanıcı kararı**.
- Katalog 19 → **21**.

## ADR-138 — TNT-05: şube kimliği firma aidiyeti doğrulanmıyordu (2026-08-26)
- **Durum:** rapor ucu, istekle gelen `operatingBranchId`'yi `BranchAccess.Require` ile doğruluyordu.
  Ama `BranchAccess` yalnız OTURUM üzerinden çalışır ve **veritabanını bilmez**: sınırsız (admin) bir
  kullanıcıda izinli küme `null` olduğu için **herhangi bir şube kimliği** — başka firmanınki dahil —
  "kapsam içi" sayılıyordu. Sunucu 403 yerine **200 (boş rapor)** dönüyordu.
- **Veri sızıntısı YOKTU:** rapor sorguları ayrıca `company_id` ile filtreler ve yazma yolları
  `EnsureBranchOwned` ile şube sahipliğini veritabanından doğrular. Kırılan şey **kapının kendisiydi**
  (fail-open) ve ADR-128'de yazılı "kapsam dışı → 403" sözleşmesiydi.
- **Karar:** `BranchService.BelongsToCompany` eklendi ve kapının **önüne** kondu. Oturumla yapılamayan
  tek kontrol (kimliğin firmaya aidiyeti) veritabanından yapılır; `BranchAccess` semantiği DEĞİŞMEDİ.
- **Kanıt:** `M15` testi, kapı devre dışı bırakılınca **kırılıyor**; açıkken geçiyor.

## ADR-139 — SIF-03: sıfırlama bildirimi sessizce yutuluyordu (2026-08-26)
- **Durum:** `/api/admin/reset-company-business` önce sunucudaki iş verisini **siliyor**, sonra makinelere
  "yerel kopyanı temizle" isteği bırakıyordu — ve ikinci adım **boş bir catch** ile yutuluyordu.
- **Neden ciddi:** ikinci adım başarısız olursa sunucu boşalmış ama masaüstleri bunu **hiç öğrenmemiş**
  olur; bir sonraki gönderimde silinen veriyi geri yüklerler. Bu, SIF-02'de kapatılan "silinen veri geri
  geliyor" hatasının aynısıdır. Üstelik yanıt yine `ok: true` dönüyordu.
- **Karar:** sıra **tersine** çevrildi — önce bildirim, sonra silme. Bildirim **yıkıcı değildir**
  ("yereli temizle + sunucudan yeniden çek"), bu yüzden silme sonradan başarısız olsa bile veri kaybı
  olmaz. Bildirim başarısız olursa **hiçbir şey silinmez** ve kullanıcı hatayı görür.
- **Kanıt:** kaynak kilidi + kuralın kendisini sınayan öz-doğrulama testi (ilk sürüm bir YORUM satırındaki
  aynı metinden başlayıp yanlış bloğu ölçüyordu; kasten bozma denemesiyle yakalandı, çapa kesinleştirildi).

## ADR-140 — MAK-01: anonim kalması ZORUNLU uçlarda hız sınırı yoktu (2026-08-26)
- Üç uç kimlik doğrulaması **isteyemez** (hepsi kimlik bilgisi oluşmadan önce çağrılır):
  `/api/machines/register` (masaüstü makine kapısı, giriş ekranından önce) · `/api/setup/download`
  (yeni bilgisayara kurulum aracı) · `/api/releases/{v}/download` (kurulum + otomatik güncelleme; jeton
  göndermez). `/sync/enroll` de sınırsızdı.
- **Ölçülen durum:** anonim çağıran, firmanın **makine kotasını** tüketebiliyor — yeni kayıt
  `ActiveCount < quota` olduğu sürece kendiliğinden `active` oluyor; kota dolunca **gerçek** makine
  `pending` kalıyor ve senkron yapamıyor. Firma kimlikleri `/api/public/companies` ile herkese açık.
  ⚠️ **Veri sızıntısı yok** (kayıt cihaz jetonu vermez; `/sync/push` ayrıca doğrular) ve **mevcut aktif
  makineler düşürülmez** — yalnız yeni makine etkilenir. İndirme uçlarında ise ~86 MB paket sınırsız kez
  çekilebiliyordu (tek küçük makinede bant genişliği/CPU).
- **Karar:** mevcut `RateLimiter` ile IP başına sınır kondu (makine kaydı 30/5dk · indirme 30/10dk ·
  enrollment giriş limiti). Sınırlar meşru kullanımın **çok üstünde**: ortak IP arkasındaki (NAT) bir
  ofiste 5 makine bile etkilenmez.
- **Kapsam dışı bırakılan (kullanıcı kararı):** aktivasyon MODELİNİ değiştirmek (yeni makinelerin ancak
  kimlik doğrulanmış girişten sonra aktifleşmesi) masaüstü kurulum akışını değiştirir; bu turda bilinçli
  olarak yapılmadı. Bugünkü telafi: yönetici sahte makineleri Makine Yönetimi'nden görüp iptal edebilir.
- **Yan bulgu:** `RateLimiter`'ın durum sözlüğü **sınırsız büyüyordu** (IP başına kalıcı satır; sunucu
  bellek sınırı 207 MB). Eşik aşılınca **penceresi dolmuş** satırlar atılır; karar mantığı değişmedi.

## ADR-141 — YET-02: "iptal / ters kayıt" yetkisi ağaçta yoktu (2026-08-26)
- **Durum:** `btn-reverse` üç gerçek işlemin kapısıydı (stok belgesi ters kaydı, yakıt depo girişi iptali,
  yakıt dağıtımı iptali) ama `SpecialButtons.All` listesinde **yoktu**. Sonuç: yetki yalnız **admin
  bypass'ıyla** geçilebiliyordu — firma yöneticisi bunu kimseye **veremiyor**, kullanıcı
  "Yetki yok: buton btn-reverse" hatasında kilitleniyordu ve yöneticinin çözebileceği bir yol yoktu.
- **Karar:** listeye eklendi. Bu kimseye yetki **vermez** (deny-by-default sürer); yalnız yöneticinin
  bilinçli olarak verebilmesini sağlar. Admin davranışı değişmedi.
- **YET-01 (raporlandı, dokunulmadı):** `btn-reset-db` ve `btn-logo` ağaçta görünür ama kodda **hiçbir
  yerde kapı değildir** → yönetici yetki verdiğini sanır, hiçbir şey değişmez. Anahtarları silmek
  verilmiş kayıtları öksüz bırakacağı için bu turda dokunulmadı; test içinde **bilinçli istisna** olarak
  listelendi ki YENİ bir işlevsiz buton sessizce eklenemesin.
- **Kalan (kullanıcı kararı):** arayüz `btn-reverse` üzerinden **tutarlı** kapı uygulamıyor — masaüstü
  Yakıt ekranı uyguluyor, Stok ekranı ve web uygulamıyor; bu yüzden yetkisi olmayan kullanıcı butonu
  görüp hata alıyor. Güvenlik açığı DEĞİL (sunucu fail-closed), arayüz tutarlılığıdır.

## ADR-142 — Şube izolasyonu üretim verisiyle DOĞRULANAMAZ: izole matris kuruldu (2026-08-26)
- **Sınır:** üretim veritabanında bugün **hiç şube tanımlı değil** (0 şube). Dolayısıyla şube izolasyonu
  canlı veriyle gözlemlenemez ve "üretimde çalışıyor" **denemez**.
- **Karar:** kural izole ortamda kanıtlanır — `FİRMA A (ŞUBE A1, A2)` + `FİRMA B (ŞUBE B1)` kurgusu,
  3 rapor × (görme / görmeme / seçememe / elle yazsa da geçememe) + kapsam listeleri + **Excel içeriği
  açılarak** dışa aktarma kapsamı + yönetici raporu kapısı. Toplam 25 senaryo.
- Ayrıca gerçek tarayıcıda iki şubeli bir kurulumla doğrulandı: depo personelinin giriş şube listesi
  yalnız kendi şubesi; raporlar yalnız kendi şubesinin verisi; yönetici ekranı adresle açılamıyor.

## ADR-143 — YED-02: sunucu yedek YÜKLEME ucu kimliği doğrulamıyordu (2026-08-26, üçüncü tur)
- **Durum:** `POST /api/backups` yalnız `if (DeviceToken(req) is null) return Unauthorized();` yapıyordu.
  `DeviceToken` ise sadece `Authorization: Bearer …` başlığını **AYRIŞTIRIR** — jetonu doğrulamaz.
  Kardeş uçlar (`/sync/push`, `/sync/pull`) jetonu `SyncServer.AuthDevice` ile veritabanından doğrularken
  burada o adım YOKTU. Üstelik dosyanın yazılacağı **firma ve makine adı da istekten** geliyordu.
- **Etki:** internetteki herhangi biri, uydurma bir jetonla, istediği firmanın klasörüne **1 GB'a kadar**
  dosya yükleyebiliyordu; depo "üzerine yazmaz / otomatik silmez" ve hız sınırı yoktu. Disk dolduğunda
  **TÜM API 500 döner** (ADR-070'te bir kez yaşandı) → kimliksiz bir çağıran sistemi durdurabilirdi.
  Ayrıca sahte yedekler süper adminin "Makine Yedekleri" ekranında gerçek firmanın yedeği gibi görünürdü.
  ⚠️ **Veri sızıntısı YOKTU** — uç yalnız yazar; listeleme/indirme uçları SEC-04'te kapatılmıştı.
- **Karar:** kimlik gerçekten doğrulanır — geçerli **JWT oturumu** (masaüstünün bugün gönderdiği şey) VEYA
  geçerli **cihaz senkron jetonu** (`SyncServer.CompanyForDevice`, `AuthDevice`'ın fırlatmayan sürümü).
  **Firma artık formdan değil KİMLİKTEN** alınır. İkinci katman olarak IP başına hız sınırı (60/saat) kondu;
  sınır, ortak IP arkasındaki (NAT) kalabalık bir ofis takılmasın diye bilerek yüksektir.
- **Meşru akış DEĞİŞMEDİ:** masaüstü zaten kendi firmasının kimliğini ve oturum jetonunu gönderiyordu.
- **Kanıt:** düzeltme kasten geri alındığında testler kırılıyor (mutasyon M10 → 3 başarısız).

## ADR-144 — YOL-01: firma/makine adı dosya yoluna doğrudan giriyordu (2026-08-26, üçüncü tur)
- **Durum:** firma kimliği `POST /api/companies` gövdesinden geliyor ve **hiç doğrulanmıyordu** (masaüstünün
  çevrimdışı ürettiği kimliği korumak için bilinçli olarak serbest bırakılmıştı). Aynı değer sonra dosya
  yoluna giriyordu. Dört yer bulundu:
  1. `purge-company` → `Path.Combine(dataDir, sub, companyId)` + **özyinelemeli silme**,
  2. `reset-company-business` → aynı desen,
  3. `BackupStore.DeleteRange` → `DELETE /api/backups?company=…` (firma adı **istekten**),
  4. `MachineBackupArchiver.ResolveArchive` → dosya adı korunuyordu ama **firma/makine adı korunmuyordu**.
- **Etki:** kimlik `".."` olsaydı (1) ve (2)'de silinecek klasör **veri kökünün kendisi** olurdu → bütün
  firmaların fotoğrafları, makine yedekleri, yayın paketleri ve SQLite'a düşülmüşse veritabanı birlikte
  giderdi. (3)'te taranan klasör yedeklerin ÜST klasörü olurdu → tarih aralığındaki **yayın paketleri ve
  veritabanı yedekleri** silinirdi. Süper admin gerektirir, ama silmeyi yapan kişi **tek bir firmayı**
  sildiğini sanır — klasik "kandırılmış vekil" (confused deputy).
- **Not:** aynı depoda DOĞRU desen zaten vardı (`LocalFileStorageProvider` hem karakter temizliği hem
  "kökün altında mı" kontrolü yapar); bu dört çağrı o korumayı kullanmıyordu.
- **Karar — iki katman:**
  1. **Giriş:** firma kimliği yalnız harf/rakam/`-`/`_` içerebilir (`SafePath.IsSafeId`). Üretimdeki tek
     firma kimliği onaltılık bir GUID'dir ve masaüstünün ürettiği kimlikler de öyledir → davranış değişmez.
  2. **İşlem:** yol `SafePath.UnderRoot` ile çözülür; **taban klasörün altında değilse hiçbir şey yapılmaz**
     (fail-closed). Taban, son parça HARİÇ tüm parçalardır — yalnız "kökün altında" demek yetmez:
     `kök/files/../ust` kökün altındadır ama `files`'tan çıkmıştır.
- **Kanıt:** `SafePath`'in ilk sürümü tam bu inceliği kaçırıyordu ve kendi testim yakaladı.

## ADR-145 — YET-05: "iptal / ters kayıt" ARAYÜZ kapısı sunucudan farklıydı (2026-08-26, üçüncü tur)
- **Sunucu kuralı** (`StockService.ReverseDocument`): `stock.Edit` **ve** `btn-reverse`.
- **Arayüzlerin sorduğu:** masaüstü Stok → yalnız `stock.Delete` (buton kontrolü YOK); web Stok →
  `stock.Delete` + `btn-reverse`. Yakıt ekranlarında web doğruydu, masaüstü modül kontrolünü sormuyordu.
- **İki yönlü sonuç:** (a) yöneticinin `stock.Edit`+`btn-reverse` verdiği kullanıcı butonu **hiçbir
  platformda göremiyordu** (verilen yetki kullanılamıyor — YET-02 ile yetki verilebilir hâle gelince görünür
  oldu); (b) yalnız `stock.Delete`'i olan kullanıcı butonu görüp tıklayınca hata alıyordu.
- ⚠️ **Güvenlik açığı DEĞİLDİ** — sunucu her iki durumda da doğru davranıyordu (fail-closed).
- **Karar:** yalnız ARAYÜZ eşitlendi (masaüstü Stok, masaüstü Yakıt, web Stok). **Sunucu kuralına
  DOKUNULMADI** — sunucu tek otorite olarak kalır.

## ADR-146 — PRS-01: şube kapsamı SAYFALAMADAN SONRA uygulanıyordu (2026-08-26, üçüncü tur)
- **Durum:** `PersonnelService.List` veritabanından `LIMIT n+1` satır çekiyor, sonra **bellekte** kapsam
  dışı şubeleri eliyor, ve "sonraki sayfa" imlecini eleme SONRASI sayıya bakarak üretiyordu.
- **Etki:** bir sayfa kapsam dışı kayıtlarla dolduğunda kullanıcı **boş liste** görür ve imleç
  üretilmediği için **sonraki sayfaya hiç geçemez** → tek şubeye yetkili kullanıcı kendi şubesindeki
  personeli göremeyebilir. Güvenlik açığı DEĞİL (fazla değil, EKSİK gösterme) ama gerçek bir veri
  görünürlüğü hatası. Üretimde henüz hiç şube tanımlı olmadığı için (0 şube) bugüne dek görülmedi.
- **Karar:** filtre SQL'e taşındı (araç listesindeki mevcut desenin aynısı). **Görünen küme birebir aynı:**
  admin sınırsız · şubesiz kayıt herkese görünür · kapsam boşsa yalnız şubesiz kayıtlar. Kapsam kaynağı
  (`ScopeResolver`) korundu.
- **Aynı kalıp başka yerde var mı:** tarandı — yalnız bir yer daha bellekte eliyor (`StatusReport`'un şube
  listesi) ve o **sayfalı değil**, dolayısıyla etkilenmiyor.
- **Kanıt:** ilk yazdığım test DİŞSİZDİ (sıralama `created_at DESC` olduğu için kapsam içi kayıt zaten ilk
  sayfaya düşüyordu); kasten bozma denemesi bunu ortaya çıkardı, kurgu düzeltildi, sonra kırmızı→yeşil.

## ADR-147 — MAS-01: çıkış→giriş döngüsünde masaüstü kabuğu serbest bırakılmıyordu (2026-08-26)
- **Durum:** her girişte YENİ bir `ShellViewModel` oluşur; eskisi iki **statik** olaya abone kalıyordu
  (`DeveloperMode.Changed`, `ServerAuthClient.SessionExpiredRaised`) ve `_updateTimer` hiç durdurulmuyordu.
- **Etki:** aynı uygulama oturumunda her çıkış→giriş bir kabuk daha biriktirir → dakikada N kez güncelleme
  kontrolü, yeni sürüm çıktığında birden çok "güncelleme mevcut" penceresi, çıkışta geliştirici modu
  kapanırken KAPANMIŞ pencerelerin işleyicilerinin de çağrılması ve sürekli artan bellek.
- **Karar:** `ShellViewModel.Release()` eklendi (zamanlayıcıları durdurur, iki statik aboneliği çözer;
  idempotent) ve `App.ShowLogin()` yeni kabuk oluşturulmadan önce eskisini bırakır.
- **Test:** `ShellViewModel` Avalonia ve `DesktopServices` olmadan örneklenemediği için kural **yapısal**
  olarak kilitlendi (kaynak kilidi) + izole masaüstü turunda çıkış→giriş denendi.

## ADR-148 — SNK-01: senkron yolu araç sayacını GERİYE alabiliyordu (2026-08-26, üçüncü tur)
- **Kural** (CLAUDE.md §4): *"Stok, sayaç, yakıt, bakım ve onayda LWW yasaktır."* Doğrudan yol buna
  uyuyordu (`VehicleService.SetMeter` → `MeterBackwardException`, tek doğru kaynak `MeterRule`).
- **Durum:** `POST /api/sync/business-push` araç satırını **düz LWW ile** upsert ediyor, `current_meter`
  için hiçbir kontrol yapmıyordu. **Gerçek istekle doğrulandı** (izole sunucu): sunucudaki sayaç
  **1000 iken 10'a düştü**, yanıt `{"upserted":1,"skipped":0,"errors":[]}` — tamamen **sessiz**.
- **Neden ciddi:** sayaç, yakıt tüketimi (km/saat başına) ve **bakım periyodu** hesaplarının girdisidir.
  Geriye giden sayaç yanlış tüketim raporu üretir ve **bakım uyarılarının kaçırılmasına** yol açar.
  Çevrimdışı çalışmış, yerel sayacı eski kalmış bir masaüstü bunu farkında olmadan tetikleyebilir.
- **Karar:** senkron yolunda da **mevcut** kural uygulanır (`MeterRule.ShouldAdvance`): gelen büyükse
  ilerler, küçükse **dokunulmaz**. Satır REDDEDİLMEZ — diğer alanlar normal uygulanır, yani meşru
  düzenlemeler (plaka, durum vb.) kaybolmaz. Yeni bir kural/kavram eklenmedi.
- **Kapsam:** yalnız **istemci → sunucu** yönü. Sunucu → masaüstü (pull) yönü sunucu-otoriteldir ve
  bilinçli olarak değiştirilmedi.
- **Aynı sınıftan başka boşluk var mı:** tarandı — stok (`quantity` pozitif + sunucu bakiyeyi
  hareketlerden yeniden hesaplar), yakıt (litre/fiyat/tutar negatif olamaz), onay (durum beyaz listesi)
  zaten korunuyordu. **Sayaç tek boşluktu.**

## ADR-149 — Test kalitesi: mutasyon (kasten bozma) turu (2026-08-26, üçüncü tur)
- Testlerin gerçekten "diş" taşıyıp taşımadığını ölçmek için **10 mutasyon** uygulandı: rapor şube filtresi,
  tenant kapısı, güncelleme checksum'ı, senkron idempotency, rapor Excel buton yetkisi, stok ters kayıt
  buton kapısı, TNT-05 şube aidiyeti, YED-02 kimlik doğrulaması. Her mutasyondan sonra kaynak AYNEN geri alındı.
- **Sonuç 8/8 yakalandı** — ama iki tanesi ancak düzeltmeden sonra:
  - **M3** ilk hâli "eşdeğer mutasyon"du: boş checksum kontrolü kapatılsa bile ikinci kontrol
    (`VerifyChecksum(content, null)`) yine fail-closed davranıyor. Gerçek UPD-01 öncesi davranış
    (`return;`) ile tekrarlandı → **kırıldı**. Yani kod iki katmanlı korumalı.
  - **M6** gerçek bir test zayıflığıydı: `YET05b` yalnız `ForbiddenException` bekliyordu; buton kapısı
    kaldırılınca "Belge bulunamadı" da **aynı türden** istisna fırlattığı için test yine geçiyordu.
    Test artık istisna MESAJINI de sınıyor → mutasyon yakalanıyor.
- **Ders (kayda geçti):** "aynı istisna türü" ile biten iki farklı yol, bir testi sessizce dişsiz bırakır.

## ADR-150 — RPR-15: role KAPATILAN ekranın verisi rapordan okunabiliyordu (2026-08-26, dördüncü tur)
- **İhlal edilen güvence:** `RoleGrantService` sözleşmesi, süper adminin bir ROLE kapattığı modül için
  *"oturum yüklenirken izin satırı DÜŞÜRÜLÜR → **admin bypass'ı dahil API/UI erişimi kapanır**"* der.
- **Durum:** rapor kapısı yalnız `reports` modülünü soruyordu; raporun OKUDUĞU ekranın kapalı olup
  olmadığına BAKMIYORDU. Süper admin "Stok" ekranını Personel rolüne kapatsa bile, o roldeki kullanıcı
  **Stok Hareketleri raporunu çalıştırıp aynı veriyi satır satır okuyabiliyordu** — Excel'e de aktarabiliyordu.
  ⚠️ Tenant/şube açığı DEĞİL (firma ve şube kapsamı doğru); ihlal edilen **rol bazlı ekran kapatma**dır.
- **Karar — kural bilinçli olarak DAR:** kataloğa `DataModule` ("raporun okuduğu ekran") eklendi ve
  **yalnız AÇIKÇA KAPATILMIŞ** modülün verisi engellenir. Bu raporlarda ekranın TAM iznini istemek,
  bugün yalnız "Raporlar" yetkisi verilmiş kullanıcıların erişimini **keserdi** → çalışan davranış
  korunmuştur. Kapatma yoksa hiçbir şey değişmez.
- **Tek nokta:** kapı `ReportService.Run` içindedir (yönetici kapısıyla aynı yer) → masaüstü, web, API ve
  Excel dışa aktarma birlikte korunur. Rapor LİSTESİ iki yerde süzülür (web katalog ucu + masaüstü
  `ReportsViewModel`); ikisi de güncellendi ve **parite testle kilitlendi**.
- **Kapsam:** 21 raporun 8'i zaten tam modül izni istiyordu (RPR-12), 12'sine `DataModule` verildi.
  `status` (Durum Rapor) bilinçli istisnadır: çapraz-modül sayısal özettir, tek bir "veri evi" yoktur.
- **Kanıt:** düzeltme kasten geri alındığında 4 test kırılıyor (mutasyon N1).

## ADR-151 — SB-01: şube AĞACI iki kapsam otoritesinde farklı uygulanıyordu (2026-08-26, dördüncü tur)
- **Ürün kuralı (ŞB-04):** "Üst şubeye yetkili kullanıcı alt şubeleri de görsün." `BranchAccess.Expand`
  bunu uyguluyordu (araç, rapor, stok hareketi hep o yoldan geçer).
- **Durum:** projede **İKİNCİ** bir kapsam otoritesi var — `ScopeResolver` — ve o `user_scopes` satırlarını
  olduğu gibi döndürüyor, ağacı **hiç genişletmiyordu**. Canlı kullanıcısı `PersonnelService`'tir.
  Sonuç: üst şubeye yetkili kullanıcı alt şantiyenin **araçlarını görüyor ama personelini görmüyor**,
  ve o şantiyeye **personel ekleyemiyordu** ("şube kapsam dışı").
- **Neden şimdi bulundu:** üretimde önceki turlarda **0 şube** vardı. Bu turda salt-okunur kontrolde
  **9 şube** görüldü ve bunların **5'i "ANKARA GENEL MERKEZ" altında alt şantiye** → hiyerarşi kod yolu
  artık CANLI. Eski turların "şube davranışı gözlemlenemiyor" varsayımı **geçersizdir**.
- **Karar:** `ScopeResolver` de `BranchTree.LoadDescendants` ile genişletir → iki otorite AYNI cevabı verir.
  Yeni kural YOK; ŞB-04'ün kararı ikinci yerde de uygulanır. Genişleme yalnız **aşağı** doğrudur:
  kardeş şube ve üst şube kapsama girmez, kapsamsız kullanıcı boş kalır, admin davranışı değişmez.
- **Kanıt:** ilk yazdığım test **aşırı genişletmeyi yakalayamıyordu** (kurguda ikinci bir üst şube yoktu);
  kasten bozma denemesi bunu ortaya çıkardı, kurguya kapsam dışı bir ALT şube eklendi, sonra iki mutasyon
  da yakalandı (N8, N9).

## ADR-152 — MAS-02: sayfa değişince masaüstü zamanlayıcısı birikiyordu (2026-08-26, dördüncü tur)
- **Durum:** `ShellViewModel.Navigate` her gezinmede YENİ bir sayfa ViewModel'i oluşturur ve eskisini
  yalnız referanstan düşürür. `DashboardViewModel` 60 saniyelik bir `DispatcherTimer` başlatır ve onu
  **hiçbir yerde durdurmuyordu**; çalışan zamanlayıcı kendi işleyicisini (dolayısıyla ViewModel'i)
  canlı tutar. "Ana Ekran ↔ başka ekran" arasında N kez gidip gelen kullanıcıda **N zamanlayıcı** birikir
  ve her biri **dakikada bir güncelleme sunucusuna ağ isteği** atar. Bellek de sürekli büyür.
- **MAS-01 ile aynı sınıf** (orada çıkış→giriş döngüsü). Bu yüzden yalnız yama yapılmadı, **genel kurala**
  dönüştürüldü: *zamanlayıcı başlatan her masaüstü ViewModel'i `IDisposable` uygular ve durdurur; kabuk
  açık sayfa değişince onu bırakır* (`OnCurrentPageChanging`). Kural mimari testle taranır → YENİ
  ekranlarda tekrarlanamaz.
- Bugün yalnız Dashboard etkilenir (tek zamanlayıcı orada); diğer sayfalar `IDisposable` olmadığı için
  davranışları değişmez.

## ADR-153 — BAG-01: sunucuya ulaşılamadığında kullanıcıya sebep söylenmiyordu (2026-08-26)
- **Durum:** API kapalıyken web **oturumu düşürmüyordu** (doğru) ama ekran neredeyse boş kalıyor, menü
  varsayılana düşüyor ve **hiçbir açıklama görünmüyordu** → "uygulama bozuldu" algısı. Gerçek tarayıcıda
  gözlendi (üçüncü tur).
- **Karar — en küçük çözüm:** tüm web istekleri zaten tek bir `_http.SendAsync` çağrısından geçiyordu;
  yalnız o çağrı bir sarmalayıcıya alındı. Karar mantığı (`BaglantiIzleyici`) Application katmanındadır
  çünkü web projesi ortak dosyaların aynasını derlediği için test projesine referans **verilemez**
  (denendi → mevcut 4 testte tür çakışması → geri alındı). Böylece riskli kısım — **ağ hatası ile yetki
  hatasının ayrımı** — gerçekten test edilir.
- **Sınır:** yalnız TAŞIMA katmanı hatası (bağlantı yok / zaman aşımı) "ulaşılamıyor" sayılır. Sunucudan
  bir yanıt geldiyse — **401/403/404/500 dahil** — bağlantı vardır. Oturum yönetimine **dokunulmaz**;
  hiçbir yerde çıkış yaptırılmaz. Olay yalnız DEĞİŞİMDE tetiklenir.
- ⚠️ `TaskCanceledException` neden güvenle "zaman aşımı" sayılıyor: `ApiClient` hiçbir isteğe
  `CancellationToken` geçirmez (doğrulandı) → kullanıcının sayfadan ayrılması yanlış uyarı üretemez.
- Arayüz: `MainLayout`'ta uyarı şeridi + "Tekrar Dene". Abonelik `Dispose`'ta çözülür (MAS-01 dersi).

## ADR-154 — MAK-01 KAPANDI: aktivasyon modeli çıkmaz yaratmıyor (2026-08-26, dördüncü tur)
- Önceki iki turda MAK-01 "model değişikliği **kullanıcı kararı**" olarak bırakılmıştı. Bu turda iddia
  senaryo senaryo **ölçüldü** (izole ortam, gerçek HTTP):
  - **A** gerçek makine kurulur → `active` ✅
  - **B** sahte kayıtlar kotayı doldurur → yönetici sahteyi iptal edince gerçek makine açılır ✅
  - **C** kota DOLUYKEN yönetici bekleyen gerçek makineyi **onaylayabilir** (`ApproveDevice` kotaya BAKMAZ)
    ve onay cihaz jetonu da üretir → makine gerçekten çalışır ✅
  - **D** aynı makine tekrar kurulur → yeni satır açılmaz, kota tüketilmez ✅
  - **E** iptal edilmiş makine kendiliğinden aktifleşemez ✅
  - **G** A firmasının cihaz jetonu B firmasının verisini çekemez (içerik kontrolüyle) ✅
- **Sonuç:** kalıcı bir kilitlenme YOKTUR; iki bağımsız kurtarma yolu vardır. Kalan risk yalnız
  **zahmet**tir (yönetici sahte kaydı görüp temizler) ve IP hız sınırıyla sınırlandırılmıştır.
  **Model değiştirilmedi** ve artık "karar bekleyen" listesinden çıkarılmıştır.
- **F (internet yok)** masaüstü `MachineGate` önbelleğindedir; Avalonia arayüzü otomatize edilemediği için
  **test edilmedi** — uydurma test yazılmadı.

---

## ADR-155 — MAS-03: masaüstü Malzeme Giriş-Çıkış tablosu görülemiyordu (2026-08-26, beşinci tur)

**Kullanıcı bildirimi (canlı kullanım).** "Masaüstünde Malzeme Giriş/Çıkış ekranındaki kayıtları
inceleyemiyorum; tablo çok minimal kalıyor. Web'de aynı kayıtları görebiliyorum."

**Kök neden — VERİ SORUNU DEĞİL, kanıtlandı.** Web ve masaüstü **aynı** veriyi **aynı** metottan
alıyor: web `GET /api/stock` → `svc.Stock.RecentMovements(s)`; masaüstü doğrudan
`DesktopServices.Stock.RecentMovements(_session)` (limit 200, aynı şube kapsamı). Ekranın alt
köşesindeki sayaç `Movements.Count`'tan gelir ve kullanıcının görüntüsünde **"19 hareket"**
yazıyordu → koleksiyon **doluydu**. Sorun yerleşimdeydi: kök `Grid`
`RowDefinitions="Auto,Auto,*,Auto"`; form **Auto** satırındaydı ve istediği boyu alıyordu
(44 form alanı + 130 px arama paneli + 180 px sepet + 44 px not ≈ 700 px), listeye (`*`) yalnız
artan ~50 px kalıyordu.

**Karar.** Form, kapsayıcının yüksekliğinin bir **oranıyla** sınırlanır (sabit piksel DEĞİL) ve
taşarsa kendi içinde kayar; liste satırı `*` kalır ve bir **taban yükseklik** alır. Böylece pencere
büyüyünce tablo da büyür, küçülünce tablo yok olmaz.

**Neden sabit piksel değil.** "Formu 420 px'e sabitle" iki yönde de kırılırdı: 768 px ekranda
satırlar taşar, 1440 px ekranda form gereksiz kırpılırdı.

**Test edilebilirlik.** Avalonia arayüzü bu projede otomatize edilemiyor. Karar mantığı saf bir
fonksiyon olarak `DepoWise.Application.Ui.FormListeOrani`'ye taşındı → gerçek sayılarla sınanır;
görünümün o kararı uyguladığı XAML üzerinde mimari testlerle kilitlendi. Sahte GUI testi
üretilmedi. (Aynı yaklaşım BAG-01'de `BaglantiIzleyici` ile kullanılmıştı.)

**Dokunulmayanlar.** API, veritabanı, senkron, `RecentMovements` sorgusu, ekranın işlevi ve
mevcut tasarımı değişmedi.

---

## ADR-156 — STK-11: işlem tarihi ile kayıt zamanı ayrıldı, MIGRATION AÇILMADI (2026-08-26, beşinci tur)

**Kullanıcı isteği.** "Malzeme Giriş/Çıkış formunda tarih alanı yok. Bugün 26.08 iken 25.08 tarihli
giriş, ya da 30.08 tarihli planlanmış hareket girebilmeliyim — ama kaydı bugün attığım belli olsun."

**Mevcut şema analizi (yeni kolon AÇILMADI).** İhtiyaç duyulan ayrım şemada **zaten vardı**:
`stock_documents` tablosunda `doc_date` (iş günü) ve `created_at` (kayıt zamanı) **ayrı** sütunlar
(Migration006, 2026-07). Dahası `StockService`'in tüm giriş noktaları — `ReceiveIn` · `IssueOut` ·
`Transfer` · `Count` — baştan beri opsiyonel bir `docDate` parametresi alıyor ve
`RunDocumentInTx` şunu yapıyordu:

```
var now  = _clock.UtcNow...;   // gerçek kayıt zamanı
var date = docDate ?? now;     // iş günü
InsertDocument(..., docNo, date, ..., now, ...);   // doc_date = date, created_at = now
AuditWriter.Write(..., _clock);                    // audit DAİMA gerçek saat
```

Yani **eksik olan yalnız arayüz ve API alanıydı**. Şema **72'de kaldı**; yeni migration yok.

**Neden `stock_movements`'a kolon eklenmedi.** Hareket satırının belgesi vardır (`document_id`);
iş günü belgenin niteliğidir ve projedeki her alan zaten bu kalıbı kullanır
(`vehicle_maintenances.performed_date`, `fuel_depot_entries.entry_date`,
`material_requests.request_date`, `daily_activities.activity_date`). İkinci bir tarih sütunu
açmak aynı bilgiyi iki yerde tutup ayrışma riski üretirdi.

**Değişen.** (1) Masaüstü ve web formuna **"İşlem Tarihi"** alanı — varsayılan **bugün**, geçmiş ve
gelecek **serbest** (üst sınır yok). (2) API DTO'larına opsiyonel `DocDate` (Unix ms) — göndermeyen
eski istemci için davranış birebir aynı. (3) Hareket ekranı ve Stok Hareketleri raporu artık
**işlem tarihini** gösterir ve ona göre süzer; ifade tek kaynaktan gelir:
`StockMovementFilterSql.IslemTarihiSql = COALESCE(d.doc_date, sm.created_at)`.

**Geçmiş veri güvende.** Bu tur öncesi **hiçbir** çağıran `docDate` göndermiyordu → mevcut tüm
satırlarda `doc_date == created_at` (aynı `now` değişkeni). `COALESCE` ifadesi bu yüzden geçmiş
kayıtların görünümünü **hiç değiştirmez**.

**Bilinçli olarak DEĞİŞTİRİLMEYENLER.**
- **Sıralama** `ORDER BY sm.created_at DESC` kaldı: kullanıcı geri tarihli bir kayıt girdiğinde onu
  listenin **en üstünde** görsün. İşlem tarihine göre sıralamak, az önce kaydedilen satırı listenin
  ortasına düşürüp "kaydedilmedi mi?" izlenimi verirdi.
- **Stok muhasebesi:** ileri tarihli hareket bakiyeyi **beklemeden** etkiler — mevcut iş kuralı budur
  ve bu turda değiştirilmedi (test `IST13` bunu kilitler, ileride sessizce değişirse uyarır).
- **`StockMovementRow.CreatedAt` alan adı:** artık işlem tarihini taşıyor ama adı korundu — web
  tablosu JSON'daki `createdAt`/`dateText` anahtarlarını okur; yeniden adlandırmak yayındaki
  istemcilerle sözleşmeyi kırardı. Anlam, kaydın üstünde açıkça belgelendi.

**Senkron.** `stock_documents` senkron tablo listesindedir ve paket `SELECT *` ile üretilir →
`doc_date` ve `created_at` olduğu gibi taşınır. Senkron zamanı işlem tarihinin yerine geçmez;
aynı paketin tekrar uygulanması tarihleri değiştirmez (testler `IST14`, `IST15`).

---

## ADR-157 — MAS-04: liste tablolarında kolon hizası (2026-08-26, altıncı tur)

**Kullanıcı bildirimi (canlı kullanım, ekran görüntüsüyle).** "Kolon adları ve aynı kolondaki filtre
hücreleri, tablo başlıkları ile aynı hizada olmalı; diğer verilerin kısımlarına taşmamalı. Bir Excel
tablosu gibi olmalı. Sütunlar kendi sınırlarını korumalı."

### Kök neden — TEK bir sebep değil, dört ayrı kusur

1. **Biriken kayma (asıl şikâyet).** Başlık, filtre ve veri satırları aynı genişliği okuyordu
   (`ColWidths`, `MinWidth = MaxWidth`), ama filtre hücresinde ayrıca `Margin="4,0"` vardı.
   `Margin` sabitlenmiş genişliğin **DIŞINA** eklenir → filtre kolonu `W+8`, diğerleri `W`.
   Fark her kolonda birikiyordu (4. kolonda ~30 px). Aynı kusur Raporlar ekranının ortak
   tablosunda `Margin="2,0"` olarak da vardı (+4 px/kolon).
2. **Üst sınırsız hücreler.** Birçok ekranda hücrelerde yalnız `MinWidth` vardı. Uzun bir değer
   GÖVDEDEKİ kolonu genişletiyor, başlık sabit kalıyordu.
3. **Ayrı yatay kaydırma.** Stok Hareketleri / Stok Değişiklik Kaydı / Denetim Kaydı'nda başlık
   Border'ı ile gövde ListBox'ı ayrı kayıyordu → yana kaydırınca hiza tamamen kopuyordu.
4. **Başlık/gövde kolon sayısı uyuşmazlığı.** Talepler'de başlık **5**, veri satırı **7** kolondu
   (iki durum rozetinin başlığı hiç yoktu). Yakıt (iki tablo) ve Muayene'de de birer kolon eksikti.

### Kararlar

- **`Margin` → `Padding`.** İç boşluk `MinWidth = MaxWidth` sınırının İÇİNDE kalır; görsel boşluk
  korunur ama kolon büyümez. 35 filtre hücresi + ortak rapor tablosu.
- **Her düz yazı hücresine üst sınır** (`MaxWidth = MinWidth`) + `TextTrimming` + tam değeri gösteren
  ipucu balonu. Böylece "sütun kendi sınırını korur" ve taşma yerine "…" olur.
- **Esnek (`*`) kolonlara ve `SharedSizeGroup` kullanan kolonlara DOKUNULMADI.** İlkinde yer varken
  yazıyı kesmek yanlış olurdu; ikincisinde kolonları zaten Avalonia eşitliyor.
- **Yazı olmayan hücrelere (buton · sayı kutusu · durum rozeti) sabit genişlik VERİLMEDİ** — etiketi
  kırpardı. Bunların çoğu son kolondadır, yani kayma sonraki kolonlara yayılmaz.
- **Başlık ve gövde tek yatay kaydırıcıyı paylaşır** (Malzemeler'de kanıtlanmış desen üç ekrana daha
  taşındı).
- **Eksik başlık kolonları tamamlandı**: Talepler → ÖNCELİK / DURUM / OPERASYON; Yakıt (×2) ve
  Muayene → eksik son kolon.
- **Başlık yazısı ile veri yazısı aynı noktadan başlar**: başlık düğmesinin kenarlığı (1 px) ve sol
  iç boşluğu (2 px) sıfırlandı. Kolon ayırıcısı artık ayrı çizildiği için başlığın kendi kutu
  çizgisine gerek kalmadı.

### `ColumnRules` — sütun ayırıcı çizgileri (kullanıcının seçimi)

Ayırıcılar **konum hesaplamaz**: her çizgi ilgili kolonun İÇİNE `HorizontalAlignment="Right"` ile
eklenir, yerini `Grid`'in kendisi belirler. Bu yüzden kolon sürüklenince, kolon gizlenince veya tablo
yana kaydırılınca çizgi kendiliğinden doğru kalır — **senkronu bozulabilecek ikinci bir konum kaynağı
yoktur**. Gizli kolonun çizgisi de gizlenir (yoksa 0 px'lik kolon 1 px kalıntı çizgi gösterirdi).
Çizgi `IsHitTestVisible="False"`'dır: satır seçimi ve metin kopyalama bozulmaz.

Çizgiler yerleşim turu bittikten sonra kuyruğa alınarak eklenir ve ekleme `try/catch` içindedir:
bu özellik **tamamen görseldir**, beklenmedik bir durumda çizgi çizilmemesi kabul edilebilir ama
çalışan bir ekranın çökmesi kabul edilemez. Burada iş mantığı yoktur, dolayısıyla gizlenen bir
veri/işlem hatası olamaz.

### Kapsam

**31 tablo ekranının tamamı.** Kullanıcı "bunun gibi bütün tablo ve ekranlarda" dediği için düzeltme
yalnız şikâyet edilen ekranla sınırlandırılmadı.

### Web

**Değiştirilmedi.** Web gerçek bir HTML `<table>` kullanır; başlık, filtre ve veri hücreleri aynı
tablonun içindedir ve tarayıcı kolonları kendisi hizalar. Orada bu kusur yoktur.

---

## ADR-158 — M6/M7 masaüstü tasarım paketi: vektör ikon seti + tablo başlığı (2026-08-27)

**Karar.** Kullanıcının Claude Code tasarım aracıyla hazırladığı paket uygulandı. Kapsam **yalnız
masaüstü** (kullanıcının açık talimatı); web'de tek satır değişmedi. CLAUDE.md §4 gereği web ve
masaüstü **işlevsel** olarak eşit kalır — piksel eşitliği zaten zorunlu değildir.

- **M6.** `Themes/Icons.axaml` (38 vektör ikon). 17 menü grubu + 6 üst grup ikon aldı; ana ekranda
  5 özet kartı, uyarı satırları, "kategori seçin" ipucu ve sürüm kartı ikonlandı; 7 emoji buton
  vektöre çevrildi. Katalogdaki emoji alanı (`AppScreens.DesktopIcon`, `NavGroupVm.Icon`)
  **silinmedi**: web ve `MenuLayout` onu okur, aynı zamanda geri dönüş yoludur. İkon bulunamazsa
  ilgili öğe ikonsuz çizilir — hiçbir durumda çökmez.
- **M7.** Başlık bandı marka rengine döndü (38 başlık birden), filtre satırı kendi sınıfına ayrıldı
  (`Border.TableFilterRow`), kolon-başı filtre kutuları 8 px dikdörtgene geçti (`TextBox.CellFilter`),
  dolu filtre ve sıralanan kolon aksan rengiyle vurgulanıyor.

**Paketin atladığı eksik.** Tasarım paketi "3 filtre satırı var" demişti; kaynak taramasında ortak
tablo kontrolünde (`Controls/DataGridView.axaml`) **dördüncüsü** bulundu. Kapsama alınmasaydı rapor
ekranlarında filtre bandı başlık rengine bürünürdü.

**Değişmeyenler.** Sıralama/sürükleme mantığı, genişliğin tek kaynağı (`ColWidths`), filtre mantığı,
`GridController`, `ColumnRules`, yetki kapıları, veri akışı, yatay boşluk 12 (hizanın kaynağı).

**Bilinçli bırakılan.** Başlık hücreleri `Button.Ghost` zeminini taşıdığı için kehribar bantta "çip"
gibi okunuyor. Kullanıcıya gösterildi; düz bant istenirse tek satır stille kapatılır.

---

## ADR-159 — TSN: tanım senkronu marka/üst/tür alanlarını NULL'a çekiyordu (2026-08-27)

**Kullanıcının bildirdiği.** *"Yeni araç kayıt formunda model alanında yeni kayıt oluşturuyorum ama
farklı bir kayıt gireceğim zaman daha önce eklemiş olduğum model listelenmiyor."*

**Kök neden (gerçek HTTP hattı üzerinde kanıtlandı — `TanimSenkronuAnahtarTests`).**
`GET /api/lookups/sync` satırları `Dictionary<string, object?>` döndürür ve sözlük **anahtarları
veritabanı sütun adlarıdır**: `brand_id`, `parent_id`, `brand_type`. ASP.NET Core'un web
varsayılanları *özellik* adlarını camelCase'e çevirir ama **sözlük anahtarlarına dokunmaz**
(`DictionaryKeyPolicy` ayarlı değildir). Masaüstündeki `LookupSyncService` ise camelCase arıyordu
(`brandId` / `parentId` / `brandType`) ve `JsonElement.TryGetProperty` **büyük-küçük harf
duyarlıdır** → alan hiç bulunamıyor, "boş geldi" sanılıyor ve senkron
`UPDATE … SET brand_id=NULL` ile sütunu **siliyordu**.

**Neden kalıcı oldu.** Aynı `UPDATE` `updated_at`'i "şimdi" olarak damgalıyor → LWW gereği yerel
satır sunucudakinden yeni sayılıyor → iş senkronu (`BusinessSyncService.ApplyPull`) doğru değeri
geri yazamıyor; dahası bir sonraki push NULL değeri **sunucuya da** taşıyor.

**Neden bugüne kadar görülmedi.** `ListBrands` `(brand_type=@t OR brand_type IS NULL)` ile NULL'a
toleranslı — marka kaybolmuyor, yalnız iki listede birden görünüyor. `ListVehicleModels`'ta böyle bir
tolerans yok (`AND brand_id=@b`), bu yüzden hata orada gözle görülür oldu.

**Düzeltme.** Yeni `DepoWise.Application.Common.JsonAlan.AlanOku` alanı **yazımdan bağımsız** okur
(`brand_id` → `brandId` → `BrandId` → alt çizgi/harf büyüklüğü yok sayılarak tarama).
`LookupSyncService` artık JSON adı değil **veritabanı sütun adı** verir. Tolerans bilinçlidir:
masaüstü ve sunucu ayrı yayınlanır; sahada eski sunucu + yeni istemci karışımı olabilir.

**Sunucu sözleşmesi DEĞİŞTİRİLMEDİ.** API'nin gönderdiği ad aynı kaldı → API deploy'u gerekmedi,
canlı sözleşme bozulmadı.

**Kapsam dışı bırakılan (kullanıcı onayı gerekir).** Hata öncesinde açılmış model kayıtlarının
`brand_id`'si sunucuda da NULL olabilir; düzeltme bunları **geri getirmez** (sunucudaki değer zaten
kayıp). Güvenli çözüm: kullanıcı o modeli yeniden ekler — `LookupService.Insert` ad+marka ikilisine
göre tekilleştirdiği için NULL markalı eski satırla çakışmaz, doğru yeni kayıt açılır ve **hiçbir şey
silinmez**. Çıkarıma dayalı otomatik onarım (araç kayıtlarından markayı türetme) mevcut veriyi
değiştireceği için yapılmadı.

---

## ADR-160 — RPR-V: çekme sonrası stok bakiyesi hesaplanmıyordu (2026-08-27)

**Kullanıcının bildirdiği.** *"Giriş-Çıkış ekranından bir sürü depo girişi yaptım ama depo girişi
raporunda hiçbiri listelenmiyor."* Talep: tüm raporların kapsamlı analizi.

**Bulunan üç kusur** (üçü de gerçek HTTP/servis yolu üzerinde yeniden üretildi):

### 1) Bakiye çok makinede SIFIR kalıyordu — asıl kusur

`stock_balances` **türetilmiş** veridir ve SNK-11 ile senkron paketinden çıkarılmıştır (otoriter
kaynak `stock_movements` defteridir; sunucu her push sonrası kendi tarafında yeniden hesaplar —
`Program.cs` → `RecomputeBalances`). **Çekme tarafında karşılığı yoktu.** Sonuç: başka bir makinede
ya da web'de girilen hareketler cihaza iniyor, "Stok Hareketleri" ekranında ve raporunda görünüyor,
fakat **bakiye 0 kalıyordu**.

Kullanıcıya yansıması: **Stok Durumu raporu sıfır · malzeme listesinin STOK kolonu 0 · düşük stok
uyarıları çalışmıyor.**

**Düzeltme.** `BusinessSyncService.ApplyPull` — çekilen verinin yerele indiği TEK nokta — uygulama
sonrası bakiyeyi defterden yeniden hesaplar (yalnız satır uygulandıysa). Hesap idempotenttir,
operasyonel hiçbir kayda dokunmaz ve sunucunun push sonrası yaptığının birebir aynısıdır → iki taraf
aynı değeri üretir. Ters kayıtlar (iptal) defterde olduğu için otomatik düşülür.

### 2) "Depo Girişi" raporunun adı yanıltıyordu

O rapor yalnız `fuel_depot_entries` okur — yani **yakıt** deposuna alınan yakıttır. Kullanıcı MALZEME
deposuna giriş yapıp bu rapora baktı ve boş buldu. Uygulamanın geri kalanı (Excel sayfa adı,
İçe/Dışa Aktarım ekranı, Yakıt ekranı) zaten **"Yakıt Depo Girişi"** diyordu; tutarsız olan yalnız
katalogdu. Ad düzeltildi, açıklama malzeme girişlerinin **«Stok Hareketleri»** raporunda olduğunu
artık söylüyor.

### 3) Sonraki tarihi girilmemiş muayene/sigorta belgesi raporda hiç görünmüyordu

Rapor `vi.next_date` üzerinden süzüyor; SQL'de `NULL` karşılaştırması daima false döner. "Sonraki
tarih" ekranda İSTEĞE BAĞLI olduğu için böyle bir belge **hiçbir tarih aralığında** listelenmiyordu.
Raporun kendi sıralaması (`ORDER BY (next_date IS NULL), …`) ve durum hesabı (`next is null → Normal`)
bu satırların var olmasını zaten bekliyordu — eksik olan tek şey süzgecin NULL'a izin vermesiydi.
Yeni `DateFilterNullable` yalnız bu raporda kullanılır; tarihi olan kayıtlarda davranış AYNIDIR.

### Kapsamlı tarama — kalan 21 rapor temiz

`RaporKapsamliTaramaTests`: tek firmaya her modülden birer normal kayıt girilir ve **katalogdaki HER
rapor** çalıştırılıp en az bir satır döndürdüğü, kolonlarının dolu olduğu ve satır/kolon sayısının
uyuştuğu doğrulanır. Kataloğa yeni rapor eklenirse test onu **otomatik kapsar**: ya veri üretilip
listelenmeli, ya da muafiyet listesine **gerekçesiyle** yazılmalı → "sessizce boş rapor" eklenemez.

**Ölçüm:** 21 rapor · 64 tarama testi · yalnız 1 kusur (madde 3) bulundu; kalanı doğru çalışıyor.

### İncelenip DEĞİŞTİRİLMEYENLER
- **Stok Sayım raporunda şube süzgeci yok** — sayım belgesi zaten şube bazlıdır ve rapor belgeden
  gelir; kapsam değişikliği ayrı bir karardır, bu turda dokunulmadı.
- **Ön muhasebe raporlarında tarih süzgeci SQL'de yok** — katalog da bu raporlarda Date filtresi
  tanımlamıyor (`ReportFilterParityTests` bunu zaten kilitliyor). Tutarlı; değiştirilmedi.

---

## ADR-161 — Web "Aurora Cam v4" tasarım paketi (2026-08-27)

**Karar.** Kullanıcının tasarım aracıyla hazırladığı web paketi uygulandı. Kapsam **yalnız web**;
`src/DepoWise.Desktop/` içinde **sıfır diff** (doğrulandı).

### Uygulananlar
| Adım | Ne yapıldı |
|---|---|
| 1 | `/api/materials/grid` yanıtına **eklemeli** `summary` (kritik/kategori/stok değeri) + web `ApiClient.GridSummary` (savunmacı okuma) |
| 2 | `app.css` sonuna **§16** (Aurora Cam kabuk + Komuta tablo dili) ve **§17** (ZB-1…ZB-10 ekran desenleri) |
| 3 | `MainLayout` kullanıcı rozetinde baş harf avatarı |
| 4 | Ana ekran · **Malzemeler** (özet şeridi, Yeni Malzeme, kritik satır, stok barı) · **Araçlar** (Yeni Araç, özet, bakım/muayene rozeti) · **Günlük Faaliyet** · **Stok Hareketleri** (özet + tür rozeti) · **Soon** (boş durum) · **Çöp Kutusu** (tür rozeti) · **47 ekranda ZB-1 başlık tipografisi** |

**Neden bu kadarı yeterli:** §16+§17 ortak sınıflardan geçtiği için **44 ekranın tamamı** markup'a
dokunulmadan yeni dili giyer (kehribar başlık bandı, çökük filtre satırı, zebra/hover, kompakt
sayfalama, degradeli birincil buton, camsı üst bar, hap menü). E1–E6'daki kalan maddeler bu dilin
üzerine ince ayardır.

### Pakette bulunan ve UYARLANAN nokta (paketin kendi 5. kuralı gereği)
Paketin §17 bloğu **`.dw-badge` TABANINI yeniden tanımlıyordu.** Projede §9.4'te zaten olgun bir rozet
sistemi var (`.dw-badge` + `-ok/-warn/-error/-muted`) ve Araç Listesi durum kolonu onu kullanıyor.
Paketin tanımı sonradan geldiği için mevcut rozetleri **eziyordu**: sabit `21px` yükseklik kayboluyor,
punto `.7rem→11px`, kalınlık `600→700` → tablo hücresinde büyüyüp hizadan çıkıyordu. Taban §9.4'te
bırakıldı; paketin kısa adları (`.ok/.warn/.err/.mut`) mevcut renklere **takma ad** olarak bağlandı.

Çakışan diğer 13 seçici tek tek incelendi ve **kasıtlı** oldukları doğrulandı (üst bar, menü,
sayfalama, birincil buton, diyalog, sekme, kullanıcı rozeti). `.dw-grid tbody td:first-child`'da v3'ün
**sabitlenmiş (sticky) ilk kolon** kuralı korunuyor — v4 yalnız renk/kalınlık yazıyor.

### Bilinçli UYGULANMAYANLAR (gerekçeli)
- **Günlük Faaliyet — tarih aralığı + "bugün/bu hafta/bu ay" kısayolları.** Ekranda tarih aralığı
  filtresi YOK (tarih kolonu bilinçli filtresiz) ve paket "sunucuya yeni parametre gerekmez" diyor →
  var olmayan bir filtreyi doldurmak mümkün değil. Uydurulmadı.
- **Araçlar — "bakımı geciken" / "muayenesi yaklaşan" ayrımı.** `/api/vehicles/alerts` yalnız
  "Bakım"/"Muayene" etiketi döner; GECİKEN ile YAKLAŞAN ayrımı veride yoktur. Paketin "yeni eşik
  uydurma" kuralı gereği ayrım icat edilmedi; tek seviye "uyarı" rozeti ve dürüst kutu adları kullanıldı.
- **Kota Ekranı — kullanım mini-barı.** Kota verisi sunucudan hazır METİN olarak gelir ("3/10");
  yüzde için metin ayrıştırmak kırılgan olurdu. Mevcut renkli çipler doluluk bilgisini zaten veriyor.
- **Talepler/Kota vb. — MudChip → `.dw-badge` dönüşümü.** Bu ekranlar rozet İŞLEVİNİ zaten `MudChip`
  ile karşılıyor; dönüştürmek görsel tercih uğruna regresyon riski demekti. Paketin 5. kuralı
  ("deseni gerçek yapıya uyarla") uygulandı.

### Doğrulama
Üç Release derleme 0 hata · masaüstünde sıfır diff · `app.css` tarayıcıda ayrıştı (308 kural, yeni
desenler canlı) · kimlik gerektirmeyen rotalar 200 ve Blazor hata kutusu yok · tam test seti.

**Görsel doğrulamanın sınırı (dürüstçe):** web'e giriş yapılamadığı için kimlik arkasındaki ekranlar
gözle tek tek denetlenemedi. Bu yüzden markup değişiklikleri bilinçli olarak DAR tutuldu ve ekran
yapısını yeniden kuran maddeler yerine sınıf/eklemeli değişiklikler tercih edildi.

---

## ADR-162 — İŞLEM TARİHİ ile KAYIT ANI ayrıldı (TRH-01)
**Tarih:** 2026-08-27 · **Durum:** Kabul · **Kaynak:** kullanıcı isteği
> *"Depo girişlerinde tarih alanı olmadığını fark ettim… log tarihi ve kayıt tarihi ayrı olmalı. Log
> üzerinden gerçekten kaydı ne zaman eklediğini görebilmeliyiz, ama tarih iş gereği ileri veya geri
> tarihli olabilir."*

### Karar
İki zaman kavramı **kalıcı olarak ayrıldı** ve tüm kayıt ekranlarında aynı biçimde uygulanır:

| Kavram | Alan | Kim yazar | Değişebilir mi |
|---|---|---|---|
| **İş günü (işlem tarihi)** | `doc_date` · `entry_date` · `distribution_date` · `performed_date` | kullanıcı (varsayılan: bugün) | **evet** — yetkiliyse geri/ileri alınır |
| **Kayıt anı** | `created_at` | **daima gerçek saat** (`IClock`) | **hayır** |

Raporlar **iş gününe** göre süzer (raporların bel kemiği); log/denetim izi **kayıt anını** gösterir.

### Yetki — tek kapı sunucuda
Yeni özel buton: **`btn-backdate` — "Geri / İleri Tarihli İşlem"** (Yetki Ağacı'nda). Tek boğaz noktası
`DateEntryPolicy.Uygula(session, istenenTarih)`:
- yetki yoksa istenen tarih **sessizce "şimdi"ye normalleştirilir** (istisna atılmaz),
- yetki varsa aynen kullanılır.

**Neden istisna değil normalleştirme:** istemcinin yerel saati ile sunucunun UTC'si arasındaki fark,
kullanıcının hiç dokunmadığı "bugün" değerini bile birkaç saatliğine "geçmiş" yapabiliyor. İstisna
atmak, yetkisiz kullanıcının **meşru aynı-gün kaydını** rastgele reddederdi. Normalleştirme yine
**kapalı-güvenli**dir: yetkisiz kullanıcı tarihi ASLA kaydıramaz.

Kapı UI'da değil **serviste**: `StockService.RunDocument` (giriş/çıkış/transfer/sayım/dağıtım) ve
`FuelService`'in iki inserti buradan geçer → API, masaüstü ve web aynı kuralı paylaşır.

### Uygulama
- Masaüstü: eksik olan ekranlara tarih alanı eklendi (Sayım, Dağıtım, Yakıt-Depo, Yakıt-Dağıtım).
  Tüm tarih alanları artık **kehribar** (`DateFieldBackgroundBrush`/`DateFieldBorderBrush`, iki temada
  da) — kullanıcı isteği: "farkedilmesi kolay olsun".
- Web: aynı ekranlarda `MudDatePicker`; yetki yoksa alan **kilitli** görünür.
- `IslemTarihiTests` (15 test) kuralı kilitliyor: TRH9 raporun iş gününe göre süzdüğünü,
  TRH11 logun gerçek kayıt anını gösterdiğini kanıtlar.

---

## ADR-163 — Ekran Araçları menüsü ve ekrana özel kayıt geçmişi (LOG-01)
**Tarih:** 2026-08-27 · **Durum:** Kabul · **Kaynak:** kullanıcı isteği
> *"Her ekrana özel log butonu olmalı. Bu log butonu ve farklı özellik butonlarını listeleyeceğimiz ana
> bir buton yapıp log butonunu içine koyulmalı. Ekran bilgileri butonlarını da her ekrana özel buralara
> taşımalısın."*

### Karar
Üst barda tek bir **"Ekran"** (masaüstü) / **⋯ araçlar** (web) menüsü var; aktif ekrana ÖZEL işlemler
buradadır. İlk iki madde: **Kayıt Geçmişi — <ekran adı>** ve **Ekran Bilgisi**. Kullanıcı menüsünde tek
başına duran "Ekran Bilgisi" maddeleri buraya taşındı. Menü ileride eklenecek ekran-özel işlemler için
tek toplanma noktasıdır.

### İki kapılı yetki (yan kapı kapalı)
Yeni özel buton: **`btn-screen-log` — "Ekran Kayıt Geçmişi (Log)"**. `AuditLogService.ForModule`
**iki** kontrol yapar:
1. `btn-screen-log` yetkisi,
2. **ekranın kendi modülünde `View` izni.**

İkincisi olmadan log, yetki sisteminde yan kapı olurdu: göremediğin ekranın geçmişinden veri sızardı.

### Kapsam — sızıntısız
`ScreenAuditMap` modül → varlık tipi eşlemesi, koddaki **gerçek `AuditEntry` çağrılarından** türetildi.
Eşlemesi olmayan modül **boş** döner; sessizce "tüm log"a düşmez. `LOG9` testi eşlemedeki her varlık
tipinin kodda gerçekten yazıldığını doğrular (yoksa kullanıcı sonsuza dek boş log görürdü).

### Gösterilen zaman
Log **`created_at`** gösterir → ADR-162 ile birlikte: işlem tarihi geri alınmış olsa bile kaydın
gerçekten ne zaman girildiği görünür. `LOG7` bunu kilitler.

### Uç nokta
`GET /api/audit/screen?module=…&from=…&to=…&limit=200` (kimlik zorunlu; kapılar serviste).

---

## ADR-164 — Proje / Şantiye Yönetimi (+ Saha türü) — PRJ-01
**Tarih:** 2026-08-27 · **Durum:** Kabul · **Kaynak:** yol haritası FAZ 1/SIRA 1 + kullanıcı ürün kararları PK-C1..C4

### Karar
- **Veri modeli:** yeni `projects` + `project_branches` (ilişki) tabloları — Migration073, **yalnız CREATE**;
  mevcut hiçbir tabloya (branches dahil) dokunulmadı, hareket tablolarına `project_id` EKLENMEDİ.
  PK-C1 gereği model ÇOKLU şantiyeye hazır; ilk sürüm UI'ı tek şantiye bağlar (tek→çok = yalnız UI işi).
- **Saha (PK-C2):** `branches.kind` üçüncü değer `field` ("Saha"); ayrı tablo/kapsam sistemi YOK.
- **Yetki (PK-C4):** ayrı kapı YOK — `branches` modülü + `BranchAccess` kapsamı (okuma VE yazma yolunda;
  kapsam dışı şantiyenin projesi görünmez, düzenlenemez, ona bağ kurulamaz — fail-closed).
- **Sunucu-otoriteli** (şubeler deseni): masaüstü CRUD çevrimiçi API'yle; **BusinessSync değişikliği YOK**
  (ebeveyn `branches` pakette olmadığından teknik olarak da tek doğru yol).
- Silme: soft delete + audit + Çöp Kutusu; firma toplu silme içgözlemle otomatik kapsar.

### Canlı veri güvenliği kanıtı
PRJ13: şema v72'de canlı benzeri veri + yalnız Migration073 → tüm mevcut satırlar bit-bit AYNI, yeni
tablolar boş. PRJ14: migration kaynağında ALTER/UPDATE/DELETE/DROP/INSERT bulunmadığı statik olarak
kilitli. Migration canlıya BU TURDA UYGULANMADI; deploy anında koşar → yayın onayı = migration onayı.

Ayrıntılı kalıcı kayıt: `docs/project-control/PRJ_01_PROJE_SANTIYE.md`.

---

## ADR-165 — Evrak / Belge Yönetimi — EVR-01
**Tarih:** 2026-08-28 · **Durum:** Kabul · **Kaynak:** yol haritası FAZ 1/SIRA 2

### Karar
- **Mevcut `file_records` YENİDEN kullanıldı** (ikinci belge tablosu YOK): belgeler `kind='document'`.
  Migration074 yalnız eklemeli meta kolonları + indeks; mevcut satırlara sıfır dokunuş (EVR11/12 kanıtlı).
- **Sunucu-otoriteli:** analiz KANITLADI ki dosya ikilisi bugün senkronda hiç taşınmıyor (file_records
  BusinessSync'te yok; masaüstü fotoğrafı yalnız kendi diskinde). Belgeler "her yerden erişilsin" gereğiyle
  sunucuda tek kopya tutulur; iki platform aynı /api/documents uçlarını çağırır. Masaüstü çevrimdışıyken
  evrak işlemi yapılamaz (anlaşılır uyarı). Senkron protokolüne ve fotoğraf davranışına DOKUNULMADI.
- **İki kapılı yetki:** `files` modülü + bağlı kaydın modülü; şube/proje belgelerinde BranchAccess kapsamı.
  Merkezi ekran yetki sisteminde yan kapı DEĞİLDİR (EVR6/7/8 kilitli).
- **Doğrulama:** `DocumentValidation` — magic-byte (PDF/Office/görsel) + uzantı-içerik tutarlılığı +
  7 MB (fotoğrafla aynı sınır). Sahte uzantı/izinsiz tür/boyut aşımı reddedilir (EVR4).
- **Yan düzeltme:** `FileService.GetPhotos/DeletePhoto`'ya `kind='photo'` koşulu — belge fotoğraf
  galerisine sızmasın, fotoğraf ucundan iki kapı atlanarak silinemesin (EVR10 bu hatayı yakaladı).

Bilinçli ALINMAYAN kararlar (ürün): belge türü sabit listesi · sürümleme · geçerlilik uyarısı ·
çöp kutusundan geri getirme. Ayrıntı: `docs/project-control/EVRAK_01_EVRAK_BELGE_YONETIMI.md`.

---

## ADR-166 — Varlık / Ekipman Yönetimi — EKP-01
**Tarih:** 2026-08-28 · **Durum:** Kabul · **Kaynak:** yol haritası FAZ 1/SIRA 3 + kullanıcı kararları PK-E1..E3

### Karar
- **PK-E1 — AYRI `equipment` tablosu** (vehicles genelleştirilMEDİ): "vehicle" 93 dosyada; tür filtresini
  her araç sorgusuna eklemek canlı sistemde sessiz-bozulma riskiydi. Hiçbir araç kaydı taşınmadı;
  EKP12 araç şemalarında ekipman izi olmadığını kilitler.
- **PK-E2** bakım entegrasyonu ilk sürümde YOK (F öncesi ayrı küçük iş: bakım tablolarına eklemeli
  equipment_id — tek sistem, kopya yok) · **PK-E3** yakıt/muayene ekipmana uygulanmaz.
- Migration075 yalnız CREATE (equipment_types + equipment) — EKP10/11 kanıtlı; canlıya uygulanmadı.
- **Yerel + senkronlu** (araç deseni): BusinessSync.Tables'a FK sıralı eklendi; push kapısı equipment
  modülü. EKP9: uçtan uca taşıma + idempotent tekrar + firma karışmazlığı.
- Yeni `equipment` yetki modülü (deny-by-default) + BranchAccess kapsamı (EKP5/6) + soft delete/Çöp
  Kutusu + ekran logu + Tanımlar'da "Ekipman — Türler" + Excel dışa aktarımı (liste kuralı 2) +
  Evrak'a "Ekipman" bağlı kayıt türü.

Ayrıntı: `docs/project-control/EKP_01_VARLIK_EKIPMAN.md`.
