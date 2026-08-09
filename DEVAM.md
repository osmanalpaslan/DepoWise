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

> 🗂️ **Çok görevli takip:** Aynı anda birden fazla iş yürütülüyor (PostgreSQL geçişi + babanın
> uygulamasına geliştirmeler). "Nerede kaldık / şu işe dön" için tek yer: **[docs/GOREV_PANOSU.md](docs/GOREV_PANOSU.md)**.
> 🔒 **Altın kural:** Babanın canlı gerçek verisine dokunulmaz — geçiş kopyayla, ayrı DB'de yapılır.

---

## 1. Bu proje nedir? (tek paragraf)

**DepoWise** — çok firmalı (multi-tenant) depo/stok/araç/bakım/yakıt yönetim sistemi.
Üç parça, tek beyin: **Masaüstü** (Windows/.NET 8 + Avalonia, yerel SQLite) + **Web** (Blazor Server/.NET,
MudBlazor, tarayıcı) + **API** (sunucu, Fly.io, SQLite). İş kuralları ve yetkiler API'de tek yerde. Detaylı
çalışma mantığı: [docs/PROJE_REHBERI.md](docs/PROJE_REHBERI.md) (ortak defterimiz).
> Not: `apps/web` (eski Next.js denemesi) 2026-06-27'den beri donmuş; aktif web `src/DepoWise.Web`'dir (ADR-057).

---

## 2. ŞU AN NEREDEYIM? (son güncelleme: 2026-08-09c — GÜNLÜK FAALİYET İPTALİ YAYINLANDI, masaüstü 1.0.132)

### ✅ İŞ 2 — GÜNLÜK FAALİYET İPTALİ YAYINLANDI (2026-08-09) — API + web + masaüstü **1.0.132**
Bir günlük faaliyet iptal edildiğinde **bağlı bakım kaydı ve malzeme çıkışları da aynı anda** iptal ediliyor;
malzemeler stoğa geri dönüyor. Analiz: [docs/IS2_GUNLUK_FAALIYET_TUTARLILIK_ANALIZI.md](docs/IS2_GUNLUK_FAALIYET_TUTARLILIK_ANALIZI.md)
- **Eski durum (sorun):** faaliyet siliniyor, bağlı bakım ve stoktan düşülen malzemeler öylece kalıyordu →
  stok ve bakım geçmişi gerçekle uyuşmuyordu.
- **Yeni durum:** hepsi **tek işlem**; bir adım bile başarısız olursa hiçbiri olmuyor (ya hepsi ya hiçbiri).
- **Buton "Sil" → "İptal Et".** Onay penceresi ne olacağını önceden yazar: bağlı bakım + kaç adet malzeme
  stoğa döneceği. **Araç sayacı geri alınmaz**, işlem geri alınamaz.
- İptal edilenler listede **varsayılan gizli**; "İptal edilenleri göster" kutusuyla üstü çizili görünür.
- "Bakım Ekibi Stoğundan Kullanıldı" satırları stoğa eklenmez (merkez depodan hiç düşülmemişti).
- Yetki: **yalnız Günlük Faaliyet silme/iptal yetkisi** yeterli; kontrol servis katmanında — arayüz değiştirilerek
  veya API doğrudan çağrılarak aşılamaz.
- **Migration YOK** (`is_deleted`/`is_cancelled`/`version` zaten vardı). Testler: **825 geçti / 0 başarısız**.
- Test raporu: [docs/tests/GunlukFaaliyet_Iptal_Test_Report.md](docs/tests/GunlukFaaliyet_Iptal_Test_Report.md)

---

### (önceki) YAKIT İPTALİ YAYINLANDI, masaüstü 1.0.131

### ✅ İŞ 1 — YAKIT KAYDI İPTALİ YAYINLANDI (2026-08-09) — API + web + masaüstü **1.0.131**
Yanlış girilen yakıt kaydı artık **iptal edilebiliyor**. Analiz: [docs/IS1_YAKIT_IPTALI_ANALIZI.md](docs/IS1_YAKIT_IPTALI_ANALIZI.md)
- **Nasıl çalışır:** kayıt silinmez, "iptal" işaretlenir → bakiyeden ve raporlardan otomatik çıkar, geçmişte iz kalır.
- **Araç sayacı GERİ ALINMAZ** (proje kuralı) — 10.500'e çıkmışsa 10.500'de kalır.
- **Depo girişi**, bakiyeyi eksiye düşürecekse iptal edilemez; "önce dağıtımları iptal edin" der.
- **Düzeltme:** iptal edilen dağıtımın başlangıç sayacı yeni kayda taşınır → rapor km'si bozulmaz.
- İptal edilenler varsayılan gizli; "İptal edilenleri göster" ile üstü işaretli görünür. İptal geri alınamaz.
- Yetki: mevcut **"Ters Kayıt"** yetkisi (yeni yetki yok). Formlardaki yanıltıcı "İptal" butonları **"Vazgeç"** oldu.
- **Migration YOK** (`is_deleted`/`prev_meter` zaten vardı). Testler: 811 geçti / 0 başarısız.

---

### (önceki) ŞUBE/ŞANTİYE ÇALIŞMASI YAYINLANDI, masaüstü 1.0.130

### ✅ ŞUBE / ŞANTİYE ÇALIŞMASI YAYINLANDI (2026-08-09) — API + web + masaüstü **1.0.130**
Şube/Şantiye tanımı artık **yalnız Şube/Şantiye Tanımları ekranından** oluşturulabiliyor.
Raporlar: [denetim](docs/SUBE_SANTIYE_ALANLARI_DENETIM_RAPORU.md) · [plan](docs/SUBE_SANTIYE_ALANLARI_UYGULAMA_PLANI.md)
- **Yetki açığı kapatıldı:** "Tanımlar" (definitions) yetkisiyle şube ekleme/yeniden adlandırma/**silme**
  yapılabiliyordu; oysa `branches` modülü admin-kısıtlı. Kilit artık **servis katmanında**
  (`LookupService.EnsureWritableTable`) → arayüz atlansa bile olmuyor.
- **Kapatılan 6 oluşturma yolu:** masaüstü Araçlar/Talepler/Tanım Düzenle, web Araçlar/Talepler, Excel içe aktarma.
- **Excel:** tanınmayan şube/şantiye artık **satır hatası** (önizlemede satır no + değer); otomatik kayıt yok.
  Diğer tanım türlerinin otomatik oluşturması aynen duruyor.
- **`kind` filtresi EKLENMEDİ** (bilinçli): canlıda 94/94 araç ve 6/6 kullanıcı `site`'a bağlı; "Şube→branch"
  filtresi şantiyeleri listelerden düşürüp **transferi bozardı**. Onun yerine 15 alanda etiket "Şube / Şantiye".
- **Senkron riski kapandı:** masaüstünde şube oluşturulamadığı için "yerel şube → push edilmiyor → bağlı araç
  FK'den reddediliyor" zinciri artık tetiklenemez (testle doğrulanmıştı).
- **Migration YOK**, şema değişmedi, canlı veri değişmedi (deploy öncesi/sonrası sayımlar aynı).

---

### (önceki) FAZ 3-ÖN YAYINLANDI: API + web canlıda

### ✅ FAZ 3-ÖN YAYINLANDI (2026-08-08) — stok eşzamanlılık düzeltmesi canlıda
Sunucuda (PostgreSQL) iki kullanıcının aynı anda stok çıkışı yapması hâlinde oluşabilen **fazla satış
(oversell)** ve **bakiye kaybı** riski kapatıldı. Rapor: [docs/FAZ3_ON_DEPLOY_SONRASI_RAPORU.md](docs/FAZ3_ON_DEPLOY_SONRASI_RAPORU.md)
- **API `depowise-erp` + web `depowise-web` yayında**, health 200, loglar temiz. **Migration YOK, şema değişmedi.**
- Bakiye yazımı tek ortak sınıfa (`StockBalanceWriter`) toplandı; `StockService`, `MaintenanceService` ve
  `OpeningStockService` aynı korumayı kullanıyor. En fazla 3 tekrar, 10–40 ms bekleme.
- Test bulgusu: **belge numarası (doc_no) tahsisinde de aynı yarış vardı** — o da tekrar edilebilir yarış
  olarak ele alındı (kullanıcı kararı S1).
- Canlı veri kontrolü (salt-okuma): **2463/2463 bakiye-defter tutarlı**, yarım/yetim/tutarsız kayıt 0,
  çapraz firma sızıntısı 0. Deploy öncesi/sonrası sayımlar **birebir aynı** → yayın veriye dokunmadı.
- ✅ **Masaüstü 1.0.129 YAYINLANDI** — sunucudaki güncel sürüm 1.0.129; paket boyutu ve SHA-256 sağlaması
  yerel dosyayla birebir eşleşiyor. Yayın sonrası stok sayımları değişmedi (667 hareket / 2 belge).
- ⏸️ **M-S1a `company_id` migration'ı ve Faz 3 BAŞLAMADI** — ayrı onay bekliyor.

---

### (önceki) Faz 3 öncesi karar/risk analizi

### 🧭 FAZ 3 ÖNCESİ KARAR VE RİSK ANALİZİ (2026-08-08, Opus 5) · **KOD YAZILMADI**
Kullanıcı isteğiyle Faz 3'e (talep karşılama + gerçek stok hareketleri) başlamadan önce **yalnız analiz**
yapıldı: [docs/FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md](docs/FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md).
- **En kritik bulgu:** `BeginImmediate` yalnız **SQLite'ta** serialize eder; **PostgreSQL'de koruma yoktur** →
  eşzamanlı iki çıkışta **oversell + bakiye kaybı** mümkün. Önerilen çözüm: `stock_balances` üzerinde
  **iyimser CAS + sınırlı tekrar** (şema değişmez, iki veritabanında aynı davranış).
- Karşılama kaydı ile stok hareketi bugün **aynı transaction'da olamıyor** → `StockService`'e iç giriş noktası.
- Yeni `request_fulfillments` tablosu **senkron listesine eklenmezse** masaüstü verisi sunucuya ulaşmaz.
- Senkron darboğazları ölçüldü (22 sorgulu sürüm hesabı, her push'ta tüm defterden bakiye hesabı, yankı pull).
- **Ek bulgu:** `material_request_items` / `maintenance_materials` tablolarında `company_id` yok →
  senkron çekmede firma filtresi uygulanmıyor (ikinci firmada gerçek sızıntı riski).
- **15 maddenin 13'ü ONAYLANDI** (2026-08-08). Ardından **Faz 3-Ön uygulama planı** hazırlandı:
  [docs/FAZ3_ON_UYGULAMA_PLANI.md](docs/FAZ3_ON_UYGULAMA_PLANI.md).
  - Bakiye yazımı **tek ortak sınıfa** (`StockBalanceWriter`) taşınacak → `StockService` + `MaintenanceService`
    aynı güvenlik mantığını kullanacak. **Migration YOK**, ekran/yetki değişikliği YOK.
  - `btn-reverse` yetkisi incelendi: tüm kodda **tek kullanım** (`StockService.ReverseDocument`) →
    ne fazla geniş ne yetersiz; karşılama iptali için uygun.
  - **Kalan 2 karar:** K-1 transfer iptali politikası (P-1/P-2/P-3) · K-2 `company_id` migration'ı (M-S1) ne zaman.
  - Yeni bulgu: `company_id`siz iki tabloda sızıntı **çift yönlü** — okuma (pull filtresiz) + **yazma**
    (`UpsertRow` tenant zorlamasını yalnız `company_id` olan tabloda yapıyor).



### 🆕 TALEP OPERASYONLARI — 5 FAZLI PROJE (2026-08-08, Opus 5) · **FAZ 1 BİTTİ**
Talep modülü gerçek ERP iş akışına dönüştürülüyor. Kullanıcı onayıyla **5 faza** bölündü; bir faz bitmeden
diğerine geçilmiyor. **Faz 1 = temel** (ekran ve geçiş kuralları Faz 2'de).
- **Migration060** (additive, canlı-veri güvenli): `material_requests.operation_status` (NULL'a izinli,
  varsayılan YOK) + `priority` (DEFAULT 'normal'); `request_status_history.kind` (DEFAULT 'approval').
  **Geri-doldurma (kullanıcı kararı B):** YALNIZ `approved` talepler `pending_ops`; taslak/beklemede/
  reddedildi/iptal kayıtlar NULL kalır → ekranda "—". **Onay durumu ile operasyon durumu AYRI.**
- **13 operasyon durumu + öncelik:** isim/sıra kullanıcı şartnamesinden BİREBİR (projede tanımı yoktu — arandı).
  Etiket + RENK anahtarı tek ortak kaynakta (`RequestOperationStatusInfo`) → masaüstü rozeti ve web MudBlazor
  rengi aynı mantıktan beslenir. **Geçiş kuralları (matris) Faz 1'de YOK — Faz 2 başında onaylatılacak.**
- **Onay veren kısıtı:** talebi yalnız formda seçilen Onay Veren (users.personnel_id bağı) onaylar/reddeder;
  firma admini + süper admin istisna; onay veren seçilmemişse eski davranış (geriye uyumluluk).
- **Yetkiler:** `request_ops`, `request_ops_warehouse`, `request_ops_purchase` (deny-by-default).
- **UI:** Talep Formu'nda (masaüstü + web) Öncelik · Onay Durumu · Operasyon Durumu kolonları, renkli rozet.
- +18 test, tüm paket **743/0** (11 PG atlandı). ✅ **YAYINLANDI:** API (health 200, Migration060 canlıda —
  `/api/requests` 200 yeni kolonları okuyor, dbSize 14.6 MB veri sağlam) + web (200) + masaüstü **1.0.127**.
### ✅ FAZ 2 BİTTİ (2026-08-08) — Talep Operasyonları ekranı + geçiş matrisi (Migration061)
- **Geçiş matrisi KULLANICI ONAYLI.** Kullanıcı düzeltmesi uygulandı: **"Teslim Edildi → Kısmen Karşılandı"
  KALDIRILDI** (Faz 3'te miktar bazlı ele alınacak). Elden teslim (Depodan/Yola Çıktı → Teslim Edildi) ve
  kaynak değişikliği geçişleri korundu; Tamamlandı/İptal terminal; terminal olmayan her durumdan İptal'e geçilir.
- **Yetki (onaylı):** `request_ops` (ekran + genel adımlar) · `request_ops_purchase` (satın alma adımları) ·
  `request_ops_warehouse` (depo/sevkiyat/teslim) · firma admini + süper admin bypass.
- **Migration061** (additive, NULL'a izinli, geri-doldurma YOK): `ops_from_branch_id`, `ops_to_branch_id`,
  `ops_note` + `request_status_history.op_branch_id`.
- **Güvenlik:** işlemin yapıldığı şube (`op_branch_id`) **istemciden alınmaz**, oturumun çalışma şubesinden
  yazılır; gönderen/gönderilecek şube firmaya aitlik doğrulamasından geçer. Operasyon geçmişi `kind='operation'`
  ile onay geçmişinden ayrı; hiçbir kayıt silinmez.
- **Ekranlar:** masaüstü "Talep Operasyonları" (liste + işlem paneli + geçmiş, menüde Talepler altında) ve
  web `/request-operations`. Durum listesi **iki platformda da sunucudan** gelir (matris kopyalanmaz).
- **FAZ 2 SINIRI korundu:** kısmi miktar, alternatif malzeme, bölme, satın alma detayları, teslim alan/şekli,
  dosya eki, bildirim ve **otomatik stok hareketleri YOK** (test: stok değişmiyor).
- +24 test, tüm paket **767/0**. ✅ **YAYINLANDI:** API (health 200; `/api/request-ops` 200 → Migration061 canlıda;
  dbSize 14.6 MB veri sağlam) + web (200) + masaüstü **1.0.128**.
- **SIRADAKİ: FAZ 3** — kısmi karşılama (miktar), alternatif malzeme, talebin bölünmesi + otomatik stok hareketleri.

## (önceki) 2026-08-08k — "Bakım Ekibi Stoğundan Kullanıldı" seçeneği

### 🆕 Bakım malzemesi: "Bakım Ekibi Stoğundan Kullanıldı" (2026-08-08, Opus 5) — Migration059
İhtiyaç: bazı malzemeler daha önce bakım ekibine teslim edilmiştir; bakım kaydına girmeli ama merkez depo
stoğundan TEKRAR düşülmemelidir. Malzeme satırında onay kutusu: **"Bakım Ekibi Stoğundan Kullanıldı"** +
açıklama **"İşaretlenirse merkez depo stoklarından düşülmez."**
- **Kilit bulgu:** Araç Bakımları, Günlük Faaliyet (Bakım) ve İlave Yağ/Filtre/Tamir **AYNI ortak
  `MaintenanceService.Save`**'i kullanıyor → iş mantığı TEK yerde değişti, üç akış birden kapsandı.
- **Davranış:** işaretliyse `stock_balances` düşümü ve `stock_movements` tüketim hareketi YAPILMAZ; bağ kaydı +
  fiyat snapshot YAZILIR (**maliyete dâhil kalır**). **İptalde** işaretli satır ters hareket üretmez (stok şişmez).
  İşaretsiz = eski davranış (regresyon testleriyle korundu).
- **Migration059:** `maintenance_materials.from_team_stock BIGINT NOT NULL DEFAULT 0` — additive, mevcut satırlar 0,
  SQLite+PostgreSQL ortak. Denetim: işaretlenen malzemeler audit kaydına yazılır (kullanıcı+zaman oradan gelir).
- **Web:** işaretli satır "stok yetersiz" uyarısına girmez; bakım geçmişinde "Bakım ekibi stoğu" rozeti.
  **Günlük Faaliyet'teki "Depo Çıkışı" akışına DOKUNULMADI** (ayrı StockService yolu).
- +16 test (MaintenanceTeamStockTests: bakiye/defter/maliyet/iptal/karışık satır/3 ekstra tür/geriye uyumluluk).
  Tüm paket **725/0** (11 PG atlandı). ✅ **YAYINLANDI:** API (health 200, **migration canlıda uygulandı**,
  okuma uçları 200, dbSize 14.6 MB — veri sağlam, log'da hata yok) + web (200) + masaüstü **1.0.126** (d03890…).

## (önceki) 2026-08-08j — masaüstü ekran düzeltmeleri; rapor işi DURAKLATILDI

### 🆕 Masaüstü ekran düzeltmeleri (2026-08-08, Opus 5) — 5 madde
Kullanıcı rapor serisini geçici olarak durdurdu (kalan: Stok Sayım + Stok Durumu) ve masaüstünde tespit ettiği
sorunları iletti. Yapılanlar:
1. **Sütun genişliği hizalama (KÖK NEDEN BULUNDU):** başlık/filtre/gövde grid'lerinde 2. kolon `Width="*"`
   (esnek) tanımlıydı → üç satır esnek payı ayrı hesaplıyor, sürükleyince hizalar kayıyor, ancak liste yeniden
   kurulunca (eşitleme sonrası) düzeliyordu. `*` kolonu `Auto + SharedSizeGroup="c2"` yapıldı ve o kolona da
   Min/Max ColWidths bağlandı (Araçlar/Malzemeler/Günlük Faaliyet). Ayrıca **SortHeader** artık kendi MinWidth'ini
   değiştirmiyor (tek kaynak = VM.ColWidths) ve sürükleme koordinatını PENCEREYE göre ölçüyor (eskiden kendi
   üzerinden ölçtüğü için sola çekerken sıçrama/geri dönme oluyordu). Kalıcılık zaten yoktu → her login standart.
2. **Eşitleme ekranı bozmasın:** liste ekranlarında `RefreshData` artık detay paneli/kayıt formu AÇIKKEN listeyi
   yeniden kurmuyor; yenileme bekletiliyor ve panel/form kapanınca sessizce uygulanıyor (açık detay kaybolmuyor).
3. **Tema:** masaüstünden "Semi (Modern)" görünümü kaldırıldı; eski kayıt otomatik Fluent'e düşüyor. Web'e dokunulmadı.
4. **Muadil malzeme:** arama zaten kod+ad üzerinden çalışıyordu; listede yalnız ad göründüğü için "kod aramıyor"
   sanılıyordu → hem sonuçlar hem seçilenler artık "KOD — Ad" gösteriyor.
5. **Malzeme şablonu geri geldi:** altyapı duruyordu (tablo/servis/yetki/raporlar), yalnız UI kaldırılmıştı.
   Malzeme formuna şablon seçici geri eklendi (+ "Temizle") ve masaüstüne **Malzeme Şablonları** yönetim ekranı
   yapıldı (Malzemeler menüsü; liste/ekle/düzenle/sil; mevcut `material_templates` yetkisiyle, deny-by-default).
- Build 0 hata, test **709/0** (11 PG atlandı) — regresyon yok. Web/API DEĞİŞMEDİ (yalnız masaüstü).
- ✅ **YAYINLANDI: masaüstü 1.0.124** (checksum 2c7395…). Web/API deploy GEREKMEDİ (değişmediler).
- 🔁 **2. DÜZELTME — 1.0.125 (2026-08-08):** 1.0.124 sorunu ÇÖZMEDİ (kullanıcı: "hâlâ aynı, ~10 sn sürüyor").
  GERÇEK kök neden: başlık/filtre/gövde ÜÇ AYRI Grid genişliği **SharedSizeGroup** ile pazarlık ediyordu;
  paylaşılan ölçü BÜYÜMEYİ anında, KÜÇÜLMEYİ ancak liste yeniden kurulunca uyguluyor → kullanıcının gördüğü
  "10 saniye" = 15 sn'lik eşitleme turunun listeyi yeniden kurması. Raporlar tablosu (DataGridView) bu sorunu
  yaşamıyor çünkü her hücreye DOĞRUDAN `Width` veriyor, SharedSizeGroup kullanmıyor → aynı ilkeye geçildi:
  3 ekranda tüm `SharedSizeGroup="cN"` kaldırıldı (kolonlar Auto + hücrelerde Min=Max pin). Ayrıca Günlük
  Faaliyet'te filtre satırındaki TARİH yer tutucusu eklendi, ROTA filtresine pin verildi ve ROTA gövde
  hücresindeki sabit `MinWidth=170` kaldırıldı (o kolon 170'in altına inemiyordu). Sürükleme akıcılığı için
  aynı piksel değerinde yeniden çizim yapılmıyor. **1.0.125 (checksum 32184c…)**. Test 709/0.
  ✅ **KULLANICI DOĞRULADI (2026-08-08): "sorun düzelmiş"** — 5 maddenin tamamı kapandı.

### 🔴 CANLI VERİ MODU AÇIK (2026-08-08)
Baban **gerçek veri girmeye başladı** (kullanıcı `mustafa.alpaslan`, şube **Karaman**). Bundan sonra geri
alınamaz her işlem (silme, sıfırlama, veri taşıyan migration, toplu güncelleme) **açık onay olmadan yapılmaz**;
şema değişikliği veri taşıma/yedek planı olmadan girişilmez. Testler yalnız yerel/ayrı test DB'sinde koşar.
- ⚠️ **Görsel doğrulama kullanıcıda** (Avalonia bu ortamda çalıştırılamıyor): özellikle 1. maddedeki sütun
  sürükleme davranışı ve yeni şablon ekranı 1.0.124'te denenmeli.


### 🆕 Talep Raporu yeniden tasarım (2026-08-08, Opus 5) — ortak standart
Kullanıcı isteği. Her talep tek satır (belge listesi). **8 kolon:** Şube · Belge No · Tarih · Talep Eden ·
Onaylayan · Durum · Kalem Sayısı · Açıklama. Kalem = `material_request_items` SATIR adedi (miktar toplamı değil).
Reddedilen/iptal talepler **listede kalır** (Durum filtresiyle daraltılır). Bu raporda para/araç olmadığı için
₺/km/saat standartları uygulanmadı (kullanıcı kararı).
- **Yeni filtreler (uçtan uca):** Durum (çoklu, sabit liste) + Talep Eden (çoklu, mevcut personel listesi) —
  `ReportRequest.RequesterIds/Statuses`, `ReportFilters.Requester|Status`, API DTO + scope (`requestStatuses`;
  Talep Eden ayrı sorgu ÇEKMEZ, mevcut personel listesini kullanır) + katalog bayrakları + masaüstü/web picker.
- **`RequestStatusOptions`** (Application) = durum listesi + Türkçe etiket için TEK doğru kaynak; hem filtre
  listesi hem rapor etiketi (eski `StatusTr` buna delege edildi) → iki platform aynı değerleri kullanır.
- **Performans:** correlated subquery KALDIRILDI → kalem sayısı `request_id` bazında derived-table'da sayılıp
  1:1 LEFT JOIN. N+1 yok. Varsayılan sıralama: Şube → Tarih (yeni önce). Migration YOK.
- **Toplam (pinned):** talep sayısı (TOPLAM etiketinde) + toplam kalem; diğer kolonlar boş.
- Değişen: ReportModels (ReportRequest+2), ReportCatalog (ReportFilters+2, RequestStatusOptions, requests tanımı+
  InfoNote), ReportService.Requests (yeniden yazım), Program.cs (DTO+scope+katalog), ReportsViewModel +
  ReportsView.axaml, Reports.razor. Build 0 hata, test **709/0** (11 PG atlandı) — +17 RequestReportTests.
  ✅ **YAYINLANDI (2026-08-08):** API (`depowise-erp`, health 200 — migration YOK) + web (`depowise-web`, 200) +
  masaüstü **1.0.123** (checksum 514b23…). PG doğrulama: canlı /api/reports/requests 200 (8 kolon) + durum filtreli
  çağrı 200 + scope 200 (requestStatuses: Taslak/Beklemede/Onaylı/Reddedildi/İptal).


### 🆕 Depo Girişi raporu yeniden tasarım (2026-08-08, Opus 4.8) — ortak standart
Kullanıcı isteği. Depoya alınan yakıt alım kayıtları; her giriş tek satır. **8 kolon:** Şube (işlenen/op_branch_id) ·
Tarih · Tedarikçi · Litre · Birim Fiyat · Tutar · Fatura No · Para Birimi. Tutar = litre × birim fiyat.
- **Yeni filtre (uçtan uca):** Tedarikçi — `ReportRequest.SupplierIds`, `ReportFilters.Supplier`, API DTO + scope
  (`suppliers`) + katalog bayrağı + masaüstü/web picker. Ayrıca Tarih + Şube (yetkili/fail-closed).
- **Toplam (pinned):** litre + tutar toplanır; birim fiyat = ağırlıklı ort. (toplam tutar ÷ toplam litre). Filtre/sıralama dışı.
- **Para birimi:** ortak kur dönüşümü yok → mevcut davranış korundu, Para Birimi kolonu bilgi amaçlı + InfoNote notu.
- **Performans:** tek tablo + tedarikçi/şube 1:1 LEFT JOIN (N+1 yok). Varsayılan sıralama: Şube → Tarih (yeni önce).
- Değişen: ReportModels (ReportRequest+1), ReportCatalog (ReportFilters+1, fuel-depot tanımı+InfoNote),
  ReportService.FuelDepot (yeniden yazım), Program.cs (DTO+scope suppliers+katalog), ReportsViewModel + ReportsView.axaml,
  Reports.razor. Build 0 hata, test **692/0** (11 PG atlandı) — +10 FuelDepotReportTests, ReportingTests kolon güncellendi.
  ✅ **YAYINLANDI (2026-08-08):** API (`depowise-erp`, health 200 — migration YOK) + web (`depowise-web`, 200) +
  masaüstü **1.0.122** (checksum 98657b…). PG doğrulama: canlı /api/reports/fuel-depot 200 + scope 200 (suppliers geldi).


### 🆕 Bakım Raporu yeniden tasarım (2026-08-08, Opus 4.8) — ortak standart
Kullanıcı isteği. Her bakım kaydı TEK satır (detay). **12 kolon:** Şube (işlenen/op_branch_id) · Tarih · Araç İç Kod ·
Plaka · Araç Adı · Araç Türü · Bakım · Alt Bakım · Sayaç (km/saat duyarlı) · Teknisyen · Malzeme Kalem Sayısı ·
Malzeme Maliyeti. Maliyet yalnız malzeme (işçilik alanı YOK — kullanıcı kararı, ileride ayrı iş).
- **Yeni filtreler (uçtan uca):** Bakım Tanımı + Teknisyen — `ReportRequest.MaintenanceDefIds/TechnicianIds`,
  `ReportFilters.MaintenanceDef|Technician`, API DTO + scope (`maintenanceDefs`/`technicians`) + katalog bayrakları,
  masaüstü + web picker'ları. Ayrıca Araç + Araç Türü (mevcut) + Tarih + Şube (yetkili/fail-closed).
- **Performans:** correlated subquery KALDIRILDI → malzeme maliyeti + kalem sayısı `maintenance_id` bazında tek
  derived-table'da toplanıp bakıma 1:1 LEFT JOIN. N+1 yok. PG+SQLite ortak sözdizimi; migration YOK.
- **Toplam (pinned, "A"):** kayıt sayısı (TOPLAM etiketinde) + malzeme kalem toplamı + malzeme maliyeti toplamı;
  Sayaç toplanmaz (km↔saat karışımı). İptal (is_cancelled) kayıtlar hariç. Varsayılan sıralama: Şube → Tarih (yeni önce).
- Değişen: ReportModels (ReportRequest+2), ReportCatalog (ReportFilters+2, maintenance tanımı+InfoNote),
  ReportService.Maintenance (tam yeniden yazım), Program.cs (DTO+scope+katalog), ReportsViewModel + ReportsView.axaml,
  Reports.razor. Build 0 hata, test **682/0** (11 PG atlandı) — +14 MaintenanceReportTests, ReportingTests kolon güncellendi.
  ✅ **YAYINLANDI (2026-08-08):** API (`depowise-erp`, health 200 — migration YOK) + web (`depowise-web`, 200) +
  masaüstü **1.0.121** (checksum 550068…). PG doğrulama: canlı /api/reports/maintenance 200 + scope 200 (teknisyen geldi);
  derived-table GetDouble deseni satırlı çalışan Araç Raporu ile aynı. Görsel doğrulama kullanıcıda.


### 🆕 Yakıt Tüketim raporu yeniden tasarım (2026-08-08, Opus 4.8) — Araç Raporu standardı
Kullanıcı isteği. Mevcut "Yakıt Tüketim" raporu (ayrı yeni rapor DEĞİL) Araç Raporu standardına taşındı: tam filo
(yakıt almayan araç da 0/"-"), sayaç birimine (km/saat) duyarlı, tek-geçiş (N+1 yok). **Kolonlar (13):** Şube ·
Araç İç Kod · Plaka · Araç Adı · Araç Türü · Sayaç Birimi · İşlem Sayısı · Mesafe · Litre · Ort. Tüketim ·
Ort. Yakıt Fiyatı (ağırlıklı) · Toplam Yakıt Maliyeti · Birim Başına Maliyet.
- **Filtreler:** Tarih (zorunlu, Bu Ay) + Şube (yetkili/fail-closed, mevcut btn-branch-select) + Araç (çoklu+arama+
  Tümünü Seç/Kaldır) + Araç Türü (çoklu, SQL'de gerçekten uygulanır). Web+masaüstü katalog-sürümlü → filtre UI otomatik.
- **Toplam (akıllı, "A" seçeneği):** İşlem/Litre/Toplam Maliyet/Ort. Fiyat hep toplanır; Mesafe/Ort. Tüketim/Birim
  Maliyet yalnız tüm satırlar aynı birimdeyse (km↔saat karışımında boş — yanlış birimli toplam üretilmez).
- **Para birimi:** sistemde ortak kur dönüşümü YOK → tutarlar işlem para biriminde toplanır (mevcut davranış korundu),
  durum InfoNote'ta belirtildi (yeni varsayım uydurulmadı).
- **Web özeti çift sayım BUG'ı:** eski satır-içi "TOPLAM" kaldırıldı, pinned TotalRow ayrı → BuildSummary artık
  yalnız veri satırlarını toplar (regresyon testi eklendi). **Masaüstü grafik:** Litre kolonu artık BAŞLIĞA göre
  hedefleniyor (eski sabit r[3] index'i kaldırıldı), NumCell HAM değeri okunuyor.
- Değişen: ReportCatalog (fuel: Vehicle|VehicleType + InfoNote), ReportService.FuelConsumption (tam yeniden yazım),
  ReportsViewModel (grafik başlık-hedefli + NumCell). API/DwDataGrid/Reports.razor değişmedi (katalog-sürümlü + genel).
- Build 0 hata, test **668/0** (11 PG atlandı) — +17 yeni FuelConsumptionTests (KM/Saat/yakıtsız/eksik sayaç/sıfıra
  bölme/ağırlıklı fiyat/araç-tür-şube filtre/yetkisiz şube fail-closed/akıllı toplam/TotalRow ayrımı/çift-sayım/NumCell).
  ✅ **YAYINLANDI (2026-08-08):** API (`depowise-erp`, health 200 — migration YOK, şema değişmedi) + web
  (`depowise-web`, 200) + masaüstü **1.0.120** (checksum 3f671fdf…). Görsel doğrulama kullanıcıda (web canlı + 1.0.120).


### 🆕 Araç Raporu 5 UX revizesi (2026-08-08, Opus 4.8) — hesaplama DEĞİŞMEDİ, yalnız sunum
Kullanıcı isteği. Kritik kısıt: **sayısal değer HAM kalır, biçim yalnız görüntüde** (Birim 4 sayısal filtre/sıralama
bozulmasın). Çözüm: ortak tablo hücresi artık **`GridCell (Text görüntü + Num ham değer)`** — sıralama/filtre/
karşılaştırma/aralık HAM `Num` üzerinden, render `Text`. Rapor sayısal hücreleri **`NumCell(Value, Display)`**
üretir (₺ 12.345,67 · 125,50 L · 1.250 km · 125,5 Saat · ₺/km|₺/Saat); boş → görüntüde **"-"**, değer 0.
- **Toplam satırı:** genel amaçlı **pinned** (TableModel.TotalRow) → ortak tabloda altta SABİT, kolon-hizalı,
  görsel ayrı (kalın+vurgu), **filtre/sıralama DIŞI**. Rapora özel değil (her rapor kullanabilir).
- **Varsayılan sıralama:** SQL `ORDER BY Şube, Araç Adı` (kullanıcı sonra istediği gibi değiştirir).
- **Bilgi satırı:** `ReportDescriptor.InfoNote` (genel amaçlı, katalog-sürümlü) — web+masaüstü üstte gösterir.
- Değişen: GridDataView (GridCell), ReportModels (NumCell + TableModel.Numeric/TotalRow), ReportCatalog (InfoNote),
  ReportService.VehicleReport (biçim/sıralama/total), ExcelExport (NumCell sayısal + total), API (serialize {n,t}+
  numeric+totalRow+infoNote), DwDataGrid + GridController + DataGridView + ReportsView(M) + Reports.razor(web).
- Build 0 hata, test **651/0** (+ GridDataView GridCell'e taşındı; VehicleReport NumCell/TotalRow; yeni görünüm/boş testleri).
  12 senaryonun tümü (para/litre/km/saat sıralama, > < >= <= , aralık, "-" bozmaz, toplam filtre/sıralama dışı) yeşil.
  ✅ **YAYINLANDI (2026-08-08):** API (`depowise-erp`, health 200 — migration yok) + web (`depowise-web`, 200) +
  masaüstü **1.0.119** (checksum a39e98d…). Görsel doğrulama kullanıcıda (web canlı + masaüstü 1.0.119).



### 🆕 Sabit-tanım (lookup) alanlarında SOL-TIK açılır liste + sayfalama (2026-08-08, Opus 4.8) — PİLOT: Malzemeler
Kullanıcı isteği (Prompt 1): masaüstünde sabit-tanım alanları (Kategori/Birim/Marka/Tedarikçi…) sol-tıkta liste
açsın (ilk 25), aramada da 25/sayfa + altta ‹Önceki/Sonraki›+"Sayfa X/Y", tekrar tıkta kapansın (aç-kapa döngüsü,
yalnız alan içinde). Mevcut `AutoCompleteBox` bunu desteklemiyordu → **yeni `LookupBox` kontrolü**.
- **Yeni:** `DepoWise.Application/Ui/LookupPaging.cs` (saf filtre+25'lik sayfalama, Türkçe-doğru; 7 test) +
  `DepoWise.Desktop/Controls/LookupBox.cs` (kod-tabanlı; Border alan + Flyout liste + arama + önceki/sonraki;
  tık-toggle double-dismiss korumalı) + `Border.LookupField` stili (ComboBox.Field ile eş görünüm).
- **Uygulama:** YALNIZ Malzemeler ekranındaki 5 lookup (Kategori/Alt Kategori/Birim/Marka/Tedarikçi). Diğer ~24
  ekran DOKUNULMADI (kullanıcı kararı: önce 1 ekran pilot, onaydan sonra tümüne yayılacak).
- Build 0 hata, test **649/0** (+7 LookupPaging). Malzemeler pilotu 1.0.115'te yayınlandı.
- ✅ **GENİŞLİK DÜZELTİLDİ + TÜM EKRANLARA YAYILDI (2026-08-08b):** Kullanıcı geri bildirimi: işlev doğru, tek
  sorun açılır listenin alandan dar olması. Kök neden: Avalonia FlyoutPresenter varsayılan MaxWidth'i → düzeltme:
  presenter kısıtı/padding kaldırıldı (`FlyoutPresenter.dw-lookup-presenter`), açılır liste kod'da TAM alan
  genişliğine sabitlendi. Ardından `AutoCompleteBox` lookup'ları **10 ekranda** LookupBox'a geçirildi:
  Materials, Inspection, Fuel, StockEntry, Requests, DailyActivity, Maintenance, Users, Personnel, Settings,
  Vehicles (~30 alan). Gösterim alanı: VehicleListRow→Display, PersonnelRecord→FullName, diğerleri→Name.
  **İstisna:** VehicleQuickEditWindow diyalogundaki `DriverBox` code-behind ile yönetildiğinden AutoCompleteBox
  kaldı (ana Araçlar formundaki Sürücü dönüştü). ComboBox alanlara dokunulmadı (zaten tıkta açılır). Build 0 hata,
  test 649/0. ✅ **YAYINLANDI: masaüstü 1.0.116** (web/API değişmedi).
- ✅ **ARAÇLAR EKRANI DÜZELTİLDİ (2026-08-08c):** Kullanıcı: Araçlar'da yalnız Sürücü doğru; diğer tanım alanları
  farklı (arama yok). Kök neden: Araçlar bu alanlar için `AutoCompleteBox` değil **ComboBox** kullanıyordu
  (arama/sayfalama yok), bu yüzden ilk yayılımda atlanmıştı. Düzeltme: Araçlar formundaki 6 tanım ComboBox'ı
  (Şablon, Makine Tipi, Kategori, Marka, Model, Şantiye/Şube) LookupBox'a çevrildi → hepsi sol-tık liste + arama +
  sayfalama, Sürücü ile aynı. Durum/Birim/Sayfa-boyutu sabit enum → ComboBox bırakıldı. Build 0 hata. ✅ **YAYINLANDI:
  masaüstü 1.0.117.**
- ✅ **Bakım Takibi + Günlük Faaliyet "Bakım Tanımı / Alt Bakım" (2026-08-08d):** kullanıcı onayıyla bu 4 ComboBox
  da LookupBox'a çevrildi (aranabilir + sayfalı). Böylece TÜM tanım alanları tutarlı. Build 0 hata. ✅ **YAYINLANDI:
  masaüstü 1.0.118.** ⏭️ Sonra: rapor süreçlerine dönüş.



### 🐞 Sütun genişliği KALICILIĞI KALDIRILDI + web resize DÜZELTİLDİ (2026-08-08, Opus 4.8)
Kullanıcı bildirdi: masaüstünde otomatik "son genişliği hatırla" ayarı hatalı (eşitleme öncesi bozuk tepki,
sonra kısmen düzeliyor); **web'de sütun hiç genişletilemiyor.**
- **Kök neden (web):** `.dw-grid thead th { resize: horizontal }` — CSS `resize` **table-cell** (`<th>`) öğelerinde
  tarayıcılarca YOK SAYILIR → web'de hiç çalışmıyordu. **Çözüm:** her başlığa `.dw-col-grip` tutamağı + App.razor'da
  **delege edilmiş JS pointer** ile canlı sürükle-genişlet (`table-layout:fixed` → kolon takip eder). Oturum-içi.
- **Masaüstü:** genişlik **persistence kaldırıldı** (SaveWidths DB yazımı + init GetWidths yüklemesi silindi) →
  her login **standart genişlik** (DefaultColWidths); oturum içinde grip ile donmadan resize (DB/sync çakışması yok).
- **Kapsam:** Malzemeler/Araçlar/Günlük (eski desen) + Raporlar (Birim 4 grid) — web+masaüstü. Genişlik artık
  hiçbir yerde kaydedilmiyor (sıra/seçim/sıralama tercihleri korunuyor). Servis/şema/endpoint dokunulmadı
  (GetWidths/SaveWidths kod var ama kullanılmıyor). Build 0 hata, test **642/0** (regresyon yok). Görsel doğrulama
  kullanıcıda (masaüstü Avalonia + web canlı). ✅ **YAYINLANDI (2026-08-08):** web (`depowise-web`, 200 — API
  değişmedi, migration yok) + masaüstü **1.0.114** (sunucuda en güncel, checksum b374673…). ⏭️ Sıradaki: **Prompt 1**
  (sabit-tanım alanlarında sol-tık liste + sayfalama).



### 🆕 Rapor altyapısı standartlaştırma (2026-08-07, Opus 4.8) — SÜRÜYOR
Ham + fazlar: [docs/gelen-gorevler/2026-08-07_rapor-mimarisi.md](docs/gelen-gorevler/2026-08-07_rapor-mimarisi.md).
Yalnız ORTAK MİMARİ; rapor hesaplamaları bu fazda değişmez (raporlar sonra tek tek, önce Araç Raporu).
- ✅ **Birim 1 — Backend temel:** `ReportCatalog`/`ReportDescriptor` (12 rapor tek kaynak, + `ReportCategory`
  kategorileri + UI-dönük Description), `ReportLimits` (maks-kayıt, Ayarlar'dan), genel `btn-branch-select`
  yetkisi (Yetki Ağacına otomatik; admin bypass; sunucu zorlar), `ReportScope` (**ölü şube filtresi non-breaking
  düzeltildi** — boş=oturum, yetkili+açık=honor), `ReportService.Run` (katalog dispatch + **Bu Ay** tarih
  varsayılanı + maks-kayıt). `/api/reports/catalog`.
- ✅ **Birim 2 — Web ekran:** Reports.razor katalog-sürümlü (sabit dizi kalktı), kategori-gruplu seçici, dinamik
  filtre (tarih yalnız UsesDate, şube seçici yalnız yetki+UsesBranch), **Stok Sayım paritesi kapandı**, yükleniyor.
- ✅ **Birim 3 — Masaüstü ekran:** ReportsView/VM katalog ComboBox, dinamik tarih görünürlüğü, yetkili şube
  seçici (checkbox çoklu), Bu Ay varsayılanı, ortak `ReportService.Run`. Build 0 hata, test 616/0.
- ✅ **Birim 1-3 YAYINLANDI (2026-08-07):** API (`depowise-erp`) + web (`depowise-web`) + masaüstü **1.0.111**.
- ✅ **Birim 4 — Ortak tablo bileşeni (GENEL AMAÇLI, rapora özel değil):** Herhangi bir listeleme ekranında
  yeniden kullanılabilir tablo. **Kişisel kolon tercihi** (sıra+seçim+genişlik AKTİF; **pinned + varsayılan
  sıralama altyapıda hazır**, UI'da henüz kapalı) — ekran açılışında **TEK sorgu** (`ListPrefs.GetAll`;
  Migration058 `pinned_json`/`sort_json`). **Kolon-altı filtre** (Excel-benzeri: metin=içerir, sayısal=tam/
  karşılaştırma/aralık), **başlık-tık sıralama**, **sürükleyerek genişlik**, **gizleme/yeniden sıralama**.
  Filtre/sıralama İSTEMCİDE (in-memory, tekrar sorgu yok) — çekirdek ortak `GridDataView` (test edilebilir).
  **Web:** `DwDataGrid.razor` (mevcut `dw-grid` tasarımı) → Reports.razor. **Masaüstü:** `GridController` +
  `DataGridView` kontrolü → ReportsView. **Yalnız Raporlar'a uygulandı; diğer ekranlara dokunulmadı.** Build
  0 hata, test **633/0** (+17: 5 tercih + 12 grid davranış). Görsel doğrulama kullanıcıda (Avalonia önizlemesi yok).
- ✅ **Birim 4 YAYINLANDI (2026-08-07):** API (`depowise-erp`, health 200, **Migration058 canlı Neon PG'de** —
  additive pinned_json/sort_json; `.../sort` 401=var) + web (`depowise-web`, 200) + masaüstü **1.0.112** (sunucuda
  en güncel, checksum 4930517B...). Baba bir sonraki girişte 1.0.112'ye güncellenir; görsel doğrulama birlikte yapılacak.
- ✅ **ARAÇ RAPORU YENİDEN TASARLANDI (2026-08-07, Opus 4.8) — "Genel Rapor"un YERİNE.** Araç başına tek satır,
  14 kolon: yakıt (litre/ort.fiyat/maliyet/tüketim) + bakım malzeme + **doğrudan parça** (stock_documents.vehicle_id
  üzerinden bakım-dışı stok çıkışı) + Toplam + **Birim Başına Maliyet**; **meter_unit-bilinçli** (km/saat — saat
  makinelerinde hesap saatte). **N+1 KALDIRILDI:** korelasyonlu alt-sorgu → 3 türetilmiş-tablo (yakıt/bakım/parça)
  LEFT JOIN, tek geçiş, fan-out yok. Filtreler: Tarih + Şube(yetkili) + **Araç(çoklu, arama+Tümünü Seç/Kaldır)** +
  **Araç Türü**. Sonda TOPLAM özeti. Çıktı 2-haneye yuvarlanır (web+masaüstü birebir aynı; toplamlar ham).
  Genişletilebilir (sigorta/kasko/lastik/amortisman = +1 derived-table +1 kolon). Build 0 hata, test **642/0**
  (+9 VehicleReportTests: km/saat/yakıtsız/bakımsız/yalnız-parça/toplam/filtreler/tarih-elenme). Görsel doğrulama
  masaüstünde kullanıcıda (Avalonia önizlemesi yok). Analiz: [docs/gelen-gorevler/2026-08-07_arac-raporu-analiz.md](docs/gelen-gorevler/2026-08-07_arac-raporu-analiz.md).
  ✅ **YAYINLANDI (2026-08-07):** API (`depowise-erp`, health 200 — **migration yok**, şema değişmedi) + web
  (`depowise-web`, 200) + masaüstü **1.0.113** (sunucuda en güncel, checksum 663feff…). Baba sonraki girişte alır.
  ⏭️ Sıradaki: kalan raporlar tek tek aynı standarda (kullanıcı onayıyla).
  Bulgular: gerçek "araç maliyet raporu" `general`'e dağılmış; en yakın odur. Eksikler: km/**saat** başına maliyet
  yok, `meter_unit` (km/saat) dikkate alınmıyor (saat makinelerinde yanlış etiket), ort. yakıt fiyatı yok, **doğrudan
  stok çıkışı parçaları** (stock_documents.vehicle_id) maliyete girmiyor, bakım **işçilik** alanı yok, araç filtresi yok.
  Performans: `general`'de **korelasyonlu alt-sorgu (N+1)** → derived-table LEFT JOIN önerildi; sayısal-TEXT CAST'ler.
  ⏭️ Sıradaki: **kullanıcıyla nihai tasarım** (5 açık karar: işçilik/sigorta/amortisman alanı, doğrudan parça, KM ölçümü,
  yeni rapor mu general'in yerine mi) → onay sonrası geliştirme.



### 🆕 Depo Çıkışı yeniden düzenleme (2026-08-07, Opus 4.8) — masaüstü + web, yayın bekliyor
Ham + analiz: [docs/gelen-gorevler/2026-08-07_depo-cikisi-sube-ici-disi.md](docs/gelen-gorevler/2026-08-07_depo-cikisi-sube-ici-disi.md).
- **Birim 1 — Giriş-Çıkış:** "Transfer" üst tipi kaldırıldı → **Depo Çıkışı → Çıkış Türü** (Şube İçi=çıkış /
  Şube Dışı=transfer). Seçime göre alanlar dinamik gizlenir/gösterilir. Ortak servis StockService değişmedi.
- **Birim 2 — Günlük Faaliyet:** yeni **Depo Çıkışı** kayıt tipi (araç "Transfer"i ayrı kalır); AYNI ortak
  servis (`StockService`/`/api/stock/issue|transfer`). Çıkış stok defterine yazılır (Stok Hareketleri'nde görünür).
- Build 0 hata, test 608/0. **API/şema DEĞİŞMEDİ.** ✅ **YAYINLANDI (2026-08-07):** web (`depowise-web`) deploy
  (200) + masaüstü **1.0.110** (`/api/releases/latest`=1.0.110, checksum B71CFC...). 1.0.110 filtre düzeltmesini
  DE içerir. Makineler bir sonraki girişte 1.0.110'a güncellenir. Kullanıcı canlıda test edecek.

### 🐞 Masaüstü filtre satırı SOL yerleşim hatası düzeltildi (2026-08-07, Opus 4.8) — 1.0.110'a girecek

### 🐞 Masaüstü filtre satırı "sola kayma" hatası DÜZELTİLDİ (2026-08-07, Opus 4.8)
Kullanıcı bildirdi: Malzeme/Araç/Günlük liste ekranlarında başlık-altı filtre kutuları tablonun SOLUNA dikey
şerit gibi geliyordu (header'ın altında değil). **Kök neden:** üç ekranda da (Materials/Vehicles/DailyActivity)
filtre satırı `Border`'ında `DockPanel.Dock="Top"` EKSİKTİ → Avalonia DockPanel varsayılanı `Dock="Left"` olduğu
için filtre bloğu sola doklanıyordu (header'da Dock=Top vardı, veri ListBox'ı son çocuk olarak dolduruyordu).
**Düzeltme:** üç filtre Border'ına `DockPanel.Dock="Top"` eklendi (3 satır) → filtre satırı artık header'ın
HEMEN ALTINDA, aynı SharedSizeGroup sütunlarıyla hizalı. Yalnız yerleşim; filtreleme mantığı/event'ler/sayfalama/
sıralama/kolon gizleme/yatay kaydırma DEĞİŞMEDİ. Web zaten `<thead><tr>` ile doğru (hata masaüstüne özgüydü).
Sayfalama zaten tam (sayfa boyutu + Önceki/Sonraki + tıklanabilir sayfa no + Excel) — referanstaki özellikler mevcut,
eksik buton yok. Build 0 hata. **Görsel doğrulama kullanıcıda** (Avalonia önizlemesi yok) → masaüstü republish gerekir.

### 2. paket CANLIDA (masaüstü 1.0.109)

### 🆕 2. paket (2026-08-06/07) — Çeşitli Modüllerde İyileştirme (8 birim, sürüyor)
Ham prompt + sıralama: **[docs/gelen-gorevler/2026-08-06b_cesitli-modul-iyilestirmeleri.md](docs/gelen-gorevler/2026-08-06b_cesitli-modul-iyilestirmeleri.md)**.
Birim birim, masaüstü önce → web ardından; her birim sonunda kullanıcı onayıyla sıradakine geçiliyor.
- ✅ **Birim 1 — Düzenleme-ekranı boş-alan hataları (Opus 4.8):** 1.7 (malzeme düzenlemede Kategori/Alt Kategori
  boş gelme) kök nedeni bulunup düzeltildi — masaüstü ana form artık web/QuickEdit ile aynı "ebeveyn tara"
  mantığını kullanıyor. 1.6 (yetki ağacı boş gelme) canlı API'de ampirik test edildi — KOD HATASI DEĞİL çıktı,
  dokunulmadı. 5.1 (Bakım Teknisyen seçimi bazen kayboluyor) kullanıcı kararıyla ERTELENDİ.
- ✅ **Birim 2 — Yakıt tutarlılık (Sonnet 5):** masaüstü Depo Girişleri Tedarikçi alanına "+" (Malzemeler ile
  aynı desen) + "Yakıtı Veren/Alan" personel alanları aranabilir oldu. Web zaten doğruydu (ortak `LookupSelect`).
- ✅ **Birim 3 — Ortak seçim alanı davranışı (Sonnet 5, TAMAMLANDI):** TÜM seçim alanlarında (madde 3) aynı
  kural: tıklanınca en fazla 25 kayıt, arama başlayınca sınır kalkar + Türkçe karakter-doğru. Çekirdek mantık
  `DepoWise.Application/Ui/Validation.cs` → `SelectionSearch` (8 yeni test). Masaüstü: 11 ekran / ~27 alan
  `AutoCompleteBox.AsyncPopulator`'a geçirildi (yeni `SearchPopulator.For<T>`) — yol boyu bulunan Avalonia
  kısıtı: `ItemsSource` tamamen kaldırılınca derlenmiş-binding denetleyicisi `ValueMemberBinding`/`ItemTemplate`
  öğe tipini çıkaramıyor (bir alanda derleme hatası verdi, "Name" adlı diğerlerinde VM'in kendi alakasız `Name`
  property'siyle SESSİZCE yanlış eşleşiyordu) → çözüm: `ItemsSource` KORUNDU (yalnız tip-çıkarımı için),
  `AsyncPopulator` ek olarak eklendi (çalışma zamanında filtrelemeyi tamamen devralıyor). Web: ortak
  `LookupSelect.razor` (14+ ekran) + ayrıca `LookupSelect` KULLANMAYAN 9 doğrudan-sunucu-arama yeri (Daily/
  Requests/Maintenance/Inspection/Fuel/Materials/Stock.razor) bulunup aynı 25-sınırı eklendi. Build 0 hata,
  test **598/0** (591→598, +8 SelectionSearchTests). ⚠️ Bakım Teknisyen alanı da bu birimde AsyncPopulator'a
  geçti (tutarlılık gereği) — 5.1'e dönüldüğünde bunun bug'ı etkileyip etkilemediği yeniden test edilmeli.
  **Görsel doğrulama yapılamadı** (Avalonia önizlemesi yok) — kullanıcı testi gerekiyor.
- ✅ **Birim 4 — Giriş/Çıkış'ta mevcut malzemeye giriş (Sonnet 5, TAMAMLANDI, 2026-08-07):** "Yeni Kayıt"
  modunda opsiyonel malzeme seçici eklendi (masaüstü+web) — mevcut malzeme seçilince Kod/Ad/Tür/Birim/
  Kategori/Alt Kategori/Marka kilitlenip malzemeden doldurulur, Tedarikçi/Birim Fiyat/Fatura-Fiş-İrsaliye/
  Açıklama aktif kalır. Kullanıcı kararı (2026-08-07): Tedarikçi değişirse malzeme kartı güncellenir (şema
  değişikliği yok). **API `/api/stock/receive` ucuna yeni opsiyonel `MaterialId` alanı eklendi — servis
  değişikliği, API deploy GEREKİYOR, henüz yapılmadı.** Build 0 hata, test 598/0.
- ✅ **Birim 5 — Sistem Logu filtreleri (Sonnet 5, TAMAMLANDI, 2026-08-07):** Tarih Aralığı (Başlangıç/Bitiş)
  + Kayıt Sayısı seçimi (100/300/500/1000/2000/5000) eklendi (masaüstü+web) — filtreleme SUNUCU tarafında,
  kayıt sayısı performans için 5000'de sıkıştırılır (kullanıcı ne seçerse seçsin sorgu sınırsız kalmaz).
  `/api/audit` ucuna `from`/`to`/`limit` query parametresi eklendi (geriye uyumlu). Build 0 hata, test 603/0
  (+5 yeni AuditLogTests). **API deploy gerekiyor** (Birim 4'ün endpoint değişikliğiyle birlikte bekliyor).
- ✅ **Birim 6 — Bakım "+ Personel" butonu (Sonnet 5, TAMAMLANDI, 2026-08-07):** Bakım Takibi Teknisyen
  alanı yanına "+" (masaüstü+web) — eklenen kişi otomatik "Saha Personeli" işaretlenir (Personeller
  modülündeki mevcut yapı yeniden kullanıldı). Build 0 hata, test 603/0.
- ✅ **Birim 7 — Malzeme stok alanı + uyarı + log ekranı + yetki (Opus 4.8, TAMAMLANDI, 2026-08-07):** En
  büyük birim, 3 alt-commite bölündü. Malzeme düzenlemede (masaüstü çift-tık QuickEdit + web MaterialEditDialog)
  "Mevcut Stok" artık düzenlenebilir (yalnız stok yetkisiyle). Doğrudan değişimde **güçlü uyarı** + Devam/Vazgeç;
  Devam → stok mimariye uygun SAYIM/DÜZELTME hareketiyle güncellenir (doğrudan bakiye yazımı YOK) + loglanır,
  Vazgeç → yalnız loglanır. Yeni **"Stok Değişiklik Kaydı"** ekranı (masaüstü + web, Tarih Aralığı/kayıt sayısı
  filtreleri) + yeni yetki (Yetki Ağacına otomatik eklendi). Yeni tablo (Migration057) + 5 test. Build 0 hata,
  test 608/0. **Migration + servis/endpoint değişikliği → API deploy GEREKİR** (Birim 4/5 ile birlikte bekliyor).
- ✅ **Birim 8 — Bakım negatif stok davranışı (Opus 4.8, TAMAMLANDI, 2026-08-07):** Bakım Takibi'nde yetersiz
  stok artık ENGELLENMEZ (kayıt oluşur, stok eksiye düşebilir). Eksik varsa uyarı + (talep yetkisi varsa)
  "Taslak Talep Oluştur ve Devam Et" / "Talepsiz Devam Et" — iki yol da bakım kaydını sürdürür (iş akışı
  kesilmez). Backend `MaintenanceService.Save` allowNegative:true; eski "engelle+rollback" testi yeni davranışa
  güncellendi. Günlük Faaliyet İlave-işlemleri de aynı mekanizmayı kullandığından tutarlı. Build 0 hata, test 608/0.
- 🏁 **8 BİRİMLİK ÇEŞİTLİ-MODÜL PAKETİ TAMAMLANDI VE YAYINLANDI (2026-08-07).**
  - ✅ **API** (`depowise-erp`) deploy — **Migration057 canlı PostgreSQL'de çalıştı** (yeni stock_change_logs
    tablosu; additive, mevcut veriye dokunmadı). Yeni uçlar canlıda doğrulandı (`/api/stock/change-log` 401 =
    route var; health 200).
  - ✅ **Web** (`depowise-web`) deploy — home 200.
  - ✅ **Masaüstü 1.0.109** yayınlandı (`scripts/publish_release.mjs`); sunucu `/api/releases/latest` artık
    1.0.109 döndürüyor (doğrulandı, checksum 089F78...). AlpnexSetup.exe yeniden yayınlanmadı (gerek yok — kurulum
    sunucudan en güncel sürümü indirir). Makineler bir sonraki girişte 1.0.109'a güncellenir.
  - Masaüstü UI değişiklikleri (stok düzenleme, uyarılar, yeni ekranlar, arama davranışı) **kullanıcı kendi
    makinesinde görsel test etmeli** (bu ortamda Avalonia önizlemesi yok).
  - Kalan: **5.1 (Bakım teknisyen seçim kaybı)** — kullanıcı KENDİ testinden sonra bildirecek (2 gündür test
    edilmemişti; atılan kodlarla düzelmiş olabilir). Gündeme kullanıcı getirene kadar açılmayacak.

### 🆕 Önceki görev paketi (2026-08-06, TAMAMLANDI+YAYINLANDI) — Giriş/Çıkış-Transfer + Tablo/Filtre (5 birim)
Kullanıcı uzun bir prompt iletti; ham hali + sıralama: **[docs/gelen-gorevler/2026-08-06_giris-cikis-transfer-tablo-filtre.md](docs/gelen-gorevler/2026-08-06_giris-cikis-transfer-tablo-filtre.md)**.
Birim birim, masaüstü önce → web ardından ilerleniyor.
- ✅ **Birim 1 — Şube mantığı + Transfer bütünlüğü (2026-08-06):** işlem/kaynak şube artık **login şube**
  (masaüstü+web salt-okunur; kullanıcı yalnız transfer HEDEFİNİ seçer). Giriş'in şubesiz kaydolma hatası kapandı.
  **Transfer geri ALINAMAZ** (sunucu reddeder + "İptal" butonu gizli). Testler: per-branch transfer bakiyesi +
  transfer-geri-alma reddi.
- ✅ **Birim 2 — İşlem Geçmişi sekmesi + detay (2026-08-06, Sonnet 5):** Malzeme bilgi paneline "İŞLEM GEÇMİŞİ"
  bölümü (tüm stok hareketleri, cap 100) — masaüstü+web. Araç için YENİ `VehicleService.RecentHistory` (şube
  transferi artık "X Şubesinden Y Şubesine transfer edildi." metniyle audit'e yazılıyor + sayaç/genel güncelleme
  olayları) + mevcut Günlük Faaliyet hareketleri birleşik gösteriliyor (masaüstü "Araç Hareketleri" sekmesi
  "İşlem Geçmişi" oldu; web'de bu veri daha önce çekilip hiç gösterilmiyordu, artık gösteriliyor). Çift-tık/tıkla
  → salt-okunur detay penceresi + "Kaydı Görüntüle" (malzeme → Stok Hareketleri'ne kod ile arama; araç → Günlük
  Faaliyet). Tüm paket **590/0** (11 PG atlandı). **Yayınlanmadı** (yeni API ucu `/api/vehicles/{id}/history`
  deploy gerektirir). Not: web Araçlar'da Uyumlu Malzemeler/Muayene-Sigorta/Bakım sekmeleri hâlâ render
  edilmiyor (ayrı, önceden var olan eksik — bu işin kapsamı dışı, ilerde ele alınabilir).
- ✅ **Birim 3 — Tablo hücre davranışı (2026-08-06, Sonnet 5):** kök neden — Malzemeler/Araçlar/Günlük Faaliyet
  tablolarında (`SortHeader`+`ColWidths`+`SharedSizeGroup` deseni) satır hücrelerinin ÜST SINIRI yoktu; uzun
  içerikli tek satır bile sütunu küçültülemez yapıyordu ("önce büyütülüyor sonra küçültülemiyor") ve
  `TextTrimming` hiç tetiklenmiyordu (Auto sütun sonsuz genişlikle ölçülür). Fix: her satır hücresi artık
  header'la AYNI `ColWidths` kaynağına (Min=Max) bağlı — sürükleyince satırlar ANINDA küçülür/büyür, ellipsis
  gerçekten çalışır, eksik yerlere tooltip eklendi. 3 ekran de düzeltildi. Web tabloları bu Avalonia'ya özgü
  hatayı yaşamıyor (zaten `overflow-x:auto`) — web hücre inceltmesi Birim 4'e bırakıldı. Build 0 hata, test
  paketi 590/0 (UI-only). **Görsel doğrulama yapılamadı** (Avalonia önizlemesi yok) — kullanıcı testi gerekiyor.
- ✅ **Birim 4 — Başlık-altı filtre satırı (2026-08-06, Sonnet 5):** aynı 3 ekran (Malzemeler/Araçlar/Günlük
  Faaliyet, masaüstü+web — diğer liste ekranlarında kolon-filtre yok, kapsam dışı). Filtre kutuları önceden
  tablonun ÜSTÜNDE ayrı bir satırdı (sütunla hizasız); artık header'ın HEMEN ALTINDA, sütunla piksel piksel
  hizalı. Masaüstü: `FilterFieldsByKey`+yeni `Conv.FilterItem` converter + `ContentControl`/`DataTemplate` ile
  aynı `SharedSizeGroup`'a bağlı filtre satırı. Web: `<thead>` içine ikinci `<tr>` (`table-layout:fixed`
  otomatik hizalıyor) + yeni `.dw-filter-th` CSS. Filtreleme ALGORİTMASI hiç değişmedi, yalnız konum. **Canlı
  tarayıcıda doğrulandı** (yerel dev sunucu + gerçek API, test hesabıyla): Malzemeler'de 15/15 sütun header ile
  piksel piksel hizalı, filtre gerçekten çalışıyor (2 kayıt → 1'e düştü "TEST1" ile), konsol hatası yok. Build
  0 hata, test paketi 590/0.
- ✅ **Birim 5 — "+" seçim pencerelerinde arama standardı (2026-08-06, Sonnet 5) — PAKET TAMAMLANDI (5/5).**
  Masaüstü: 6 ekranda (StockEntryView×6, DailyActivityView×2, PersonnelView, UsersView, SettingsView, FuelView)
  arama İÇERMEYEN 12 lookup `ComboBox`'ı (Şube/Kategori/Birim/Marka/Tedarikçi), Personel/Araç seçicileriyle AYNI
  kanıtlanmış `AutoCompleteBox`'a yükseltildi — artık tüm büyüyebilir listeler arama-yazılabilir. Web: ortak
  `LookupSelect.razor` (14+ ekran) zaten arama içeriyordu ama **Türkçe karakter hatası** vardı — bağımsız bir
  C# betiğiyle KANITLANDI: `"İSTANBUL".Contains("istanbul", OrdinalIgnoreCase)` → **False** (hatalı!). Yeni
  `FieldChecks.TrCompare` (tek ortak kaynak, tr-TR culture) ile düzeltildi — TEK dosya değişikliği 14+ ekranı
  düzeltti. Build 0 hata, test paketi 590/0.
- 🏁 **5 BİRİMLİK PAKET TAMAMLANDI VE YAYINLANDI (2026-08-06).**
  - ✅ API (`depowise-erp.fly.dev`) deploy edildi + canlıda sağlık kontrolü yapıldı (yeni `/api/vehicles/{id}/history`
    ucu canlıda kayıtlı doğrulandı).
  - ✅ Web (`depowise-web.fly.dev`) deploy edildi + **canlı tarayıcıda test hesabıyla doğrulandı**: Malzemeler
    ekranında Birim 4'ün filtre satırı 15/15 sütunla piksel piksel hizalı çalışıyor.
  - ✅ Masaüstü **1.0.108** yayınlandı (`dotnet publish ... -r win-x64 --self-contained -o artifacts/rc/desktop-1.0.108`
    → zip → `scripts/publish_release.mjs`); sunucu `/api/releases/latest` artık 1.0.108 döndürüyor (doğrulandı).
    AlpnexSetup.exe yeniden yayınlanmadı (gerek yok — kurulum sırasında zaten sunucudan "en güncel" sürümü indiriyor).
  - Masaüstü UI değişiklikleri (Birim 1/3/5 — şube/transfer, sütun küçültme, yeni arama kutuları) **kullanıcı
    tarafından kendi makinesinde görsel olarak test edilmeli** (bu ortamda Avalonia önizlemesi yok); mevcut
    kurulumlar otomatik güncelleyici üzerinden 1.0.108'i alacak.

### 🐞 Kullanıcı hata/iyileştirme listesi (2026-08-05) — 11 madde, sürüyor
Kullanıcı masaüstünde test edip 11 maddelik liste verdi (her biri masaüstü+web). **Kural (hafızaya alındı):**
bir iyileştirme bildirildiğinde HER ZAMAN iki ortam da kontrol edilir.
- ✅ **1-2 Ekran ayrımı:** masaüstü Malzeme+Araç "Yeni kayıt" — ShowAdd açıkken liste/filtre/sayfalama gizli,
  sadece form. Web zaten route ile ayrıydı.
- ✅ **3 Malzeme şablonu kaldırıldı** (form alanı + "şablon dışı" uyarısı + web nav link); Araç şablonları kaldı.
- ✅ **4 Araç plakası benzersiz** (VehicleService Create+Update, firma bazında, silinen hariç, YEREL-yumuşak
  → offline çakışma sistemi çökertmez) + test. **5 Malzeme kodu / araç iç kodu** zaten benzersizdi (doğrulandı).
- ✅ **7 Tarih seçiciler Türkçe** (tr-TR kültür; sayı biçimi invariant/nokta bırakıldı → sayı girişi değişmez)
  — masaüstü + web Program.cs.
- 🔍 **6 kategori/alt-kategori:** malzeme formu mantığı DOĞRU çıktı (kategori=üst-seviye, alt=filtreli, ekleme
  parent_id doğru). Asıl eksik Tanımlar ekranında (madde 10 ile birleşiyor) — alt kategori yönetimi yok.
  Kullanıcıya karışmayı NEREDE gördüğü soruldu.
- ✅ **8(a) GÜVENLİK:** stok çıkış+transfer şube-yetki — şubeye bağlı kullanıcı yalnız KENDİ şubesinden;
  Tüm Şubeler/admin (null) her şubeden. Ortak StockService (masaüstü+web) + test. ⏳ **8(b)** "stok yoksa
  engelle" = per-branch stok (Tema B, büyük canlı-veri işi) — AYRI/kopyayla.
- ✅ **9** Stok formu kişi/araç etiket ayrımı: "Teslim Eden/Alan (Personel)" + "Transfer Edilen/Kullanılan
  Araç" (alanlar zaten ayrıydı; etiket netleşti). Masaüstü+web.
- ✅ **11 Stok Hareketleri ayrı ekran:** StockService.SearchMovements (tarih aralığı + metin araması) +
  /api/stock/movements + web StockMovements.razor (/stock/movements) + masaüstü StockMovementsView/VM + nav
  (her iki tarafta "Giriş-Çıkış" altında). Salt-okunur.
- ✅ **6 TAM:** Kategori/alt-kategori karışması (kutu `List` ile tümünü gösteriyordu) HER YERDE düzeltildi:
  web ana form (kutu üst-seviye + edit-load parent-çözümleme), web MaterialEditDialog (çift-tık, full split),
  masaüstü ana form (zaten doğru), masaüstü hızlı-düzenle (full split). Tüm giriş/düzenleme yolları kategori+alt
  ayrımını doğru yapıyor; mevcut değer parent-taramayla çözülür; kaydet = altKategori ?? kategori. Araçta alt yok.
- ✅ **10 TAM:** Part A alt kategori YÖNETİMİ (web SubCategoryEditor + masaüstü SubCategorySection: kategori seç
  → alt kategorilerini ekle/düzenle/sil) + Part B ekran-bazlı gruplama (web zaten gruplu, masaüstü önekli). CRUD
  `/api/materials/subcategories` + `/api/lookups/material_categories`. Web canlı; **masaüstü republish gerekir**
  (1.0.106 bundan önceydi).
- ✅ **8(b) per-branch stok BİTTİ:** çıkış/transferde o ŞUBENİN defter bakiyesi yetersizse NegativeStockException
  (şema DEĞİŞMEDİ — şube bakiyesi hareketten anlık; sayım/ters hariç; NULL şube firma-genelile). 2 test + tüm
  paket 587/0. → **11 maddenin HEPSİ BİTTİ.**
- ✅ **YAYIN TAM (2026-08-05):** **API `depowise-erp` DEPLOY** edildi (servis/endpoint değişiklikleri —plaka,
  şube güvenliği, per-branch stok, /api/stock/movements— web'de canlı; bkz. hafıza [[web-servis-degisikligi-api-deploy]]),
  **web `depowise-web` deploy**, **masaüstü 1.0.107 + AlpnexSetup.exe** yayınlandı. Baban 1.0.107'ye güncellenir.
- ✅ **MASAÜSTÜ 1.0.106 YAYINLANDI** (2026-08-05): biriken 10 madde (ekran ayrımı, şablon kaldırma, plaka
  benzersiz, tarih Türkçe, şube-çıkış güvenliği, kişi/araç etiket, Stok Hareketleri ekranı, kategori/alt split)
  masaüstüne çıktı + AlpnexSetup.exe güncellendi. Sunucu en güncel=1.0.106. Web zaten canlı. Kullanıcı test
  ETMEDEN devam edecek (kota endişesi) → kalan: 10 Tanımlar ekranı, 8b per-branch stok (Tema B).


### 🎯 AURORA 2. TUR — "neden sadece login değişti" KÖK NEDEN BULUNDU + DÜZELTİLDİ (2026-08-05 akşam)
İlk turda arayüzün sadece giriş ekranı değişmişti. Tasarım ekibi **"Tasarım Final.zip"** paketiyle nedeni
teşhis etti; iki kök neden vardı, ikisi de düzeltildi ve **web canlıya alındı**:
- **WEB — stil yükleme sırası:** `app.css` MudBlazor'dan ÖNCE yükleniyordu → tüm `.mud-*` kurallarımız
  eziliyordu; yalnız kendi iç stilini taşıyan Login değişebiliyordu. **Düzeltme:** `App.razor`'da app.css artık
  MudBlazor'dan SONRA yükleniyor → **40 ekran birden Aurora'ya döndü.** + app.css v2 (form bölümleme katmanı)
  + ölü şablon CSS temizliği. Canlı doğrulandı (HTTP 200, sıra doğru, sekme "Giriş — Alpnex", konsol temiz).
- **MASAÜSTÜ — sabit gri butonlar:** `Components.axaml`'de İptal/Temizle/Filtrele butonları sabit gri hex
  (#475569/#5B6473) ile boyalıydı; tablo zebrası (#0AFFFFFF) açık temada görünmüyordu → palet Aurora olsa bile
  gri kalıyorlardı. **Düzeltme:** hepsi tema token'ına bağlandı + App.axaml aktif menü kehribar gradyanı +
  DashboardView emoji→ikon/KPI düzeni. **Masaüstü 1.0.99 → 1.0.100 YAYINLANDI** (kullanıcı onayı "olmuş
  yayınla", 2026-08-05) → babanın makinesi otomatik güncellenir, veritabanı korunur.

### 🖼️ YENİ LOGO tüm ikonlara uygulandı (2026-08-05) — depo+kamyon+ekskavatör
Kullanıcının hazırladığı yeni Alpnex logosu (şeffaf zeminli) her yere uygulandı: **web** logo.png (512px) +
favicon.png/ico (canlı), **masaüstü** app-icon.png (giriş) + app-logo.ico (görev çubuğu/exe), **kurulum aracı**
exe simgesi (Setup csproj'a `ApplicationIcon` eklendi). Masaüstü **1.0.100** paketinde + **AlpnexSetup.exe**
yeniden derlenip sunucuya yüklendi (`/api/setup/download` HTTP 200). Yeni yardımcı: `scripts/publish_setup.mjs`.
Temiz kurulum artık yeni ikonu gösterir.
- **Aşama 2 (kısmen yapıldı, 2026-08-05):** WEB §1 bağlam satırı (firma + süper admin işareti) 14 çekirdek
  ekrana eklendi + **canlı** (Vehicles/Materials/Personnel/Requests/Fuel/Maintenance/Inspection/Stock/
  StockCount/Daily/Branches/Companies/Users/Definitions). MASAÜSTÜ ThemeSettingsView emoji→PathIcon.
- **Aşama 2 TAM (görsel sweep):** WEB §1 bağlam satırı **30 ekrana** eklendi + canlı (yalnız Reports/Soon/Theme
  gerekçeli atlandı). MASAÜSTÜ: ThemeSettingsView emoji→PathIcon; **§0 Classes atama zaten hazırdı** — uygulama
  tasarım sistemi Classes'larıyla kurulmuş (ana view'lar %100 sınıflı), Stage 1 teması hepsini Aurora yapıyor.
- **Aşama 3 API TAM (canlı):** A1 criticalOnly (materials grid/export), A2 dashboard summary, A3
  /api/materials/{id}/movements — hepsi mevcut ve canlı (önceki oturumda yapıldı).
- **KALAN (opsiyonel/ertelenen):** web §2 tek-birincil-buton (bölümler ayrı → riskli), Aşama 4 S1/S2 malzeme
  kartı bölümlü form+değişiklik sayacı, S3 kritik stok paneli, malzeme "Son Hareketler" paneli (A3 UI bağlama)
  — bunlar görsel-sweep değil, işlevsel eklemeler (çalışan forma müdahale → dikkatli, ayrı iş).
### 🧾 Son Hareketler paneli + masaüstü 1.0.101 (2026-08-05, kullanıcı seçimi)
Malzeme kartına (hem masaüstü `MaterialQuickEditWindow` hem web `Materials.razor` düzenleme formu) **"Son
Hareketler"** paneli eklendi: o malzemenin son 10 stok hareketi (giriş yeşil / çıkış kırmızı, tarih + belge no).
Salt-okunur; backend (A3) zaten vardı; ViewModel'e dokunulmadı (masaüstü kod-arkası, web @code helper).
**Masaüstü 1.0.101** (Tema emoji + Son Hareketler). Sonra **S3 + S2 de eklendi → 1.0.102 YAYINLANDI:**
- **S3 kritik stok paneli:** malzeme kartında stok ≤ minimum ise kırmızı "Kritik seviye altında" uyarısı +
  **Talep Oluştur** (eksik kadar TASLAK talep; gönderim değil, "requests" Create yetkisi ister). Web + masaüstü.
- **S2 değişiklik sayacı:** kaydedilmemiş alan sayısı rozeti + İptal'de "değişiklikler kaybolur" onayı. Web + masaüstü.
- ViewModel'e / kaydetme mantığına dokunulmadı (masaüstü kod-arkası, web @code/markup). Sunucu en güncel=1.0.102.
- **Malzeme kartı işlevsel eklemeleri TAM** (Son Hareketler + Kritik panel + Değişiklik sayacı).
- **Web §2 (opsiyonel) YAPILDI — GERİ ALINABİLİR:** ikincil butonlar (Filtrele ×3, Ekle ×2) Filled→Outlined;
  asıl Kaydet/Oluştur kehribar, Sil kırmızı, satır-içi butonlar dokunulmadı. **Geri alma:** `git revert 537ae14`
  ya da `scratchpad/s2_backup/*.orig` geri kopyala. Canlı.

### 🟨 Kare logo + %14 kenar kırpma → ikon kareyi dolduruyor + 1.0.105 (2026-08-05)
Kullanıcı "Kare Logo.png" verdi ama içeriği yine yatay (≈1.5:1) çıktı. Onayıyla **%14 kenar kırpma**
uygulandı → kare ikonlar (favicon + exe/görev çubuğu + setup) kareyi ~%92 doldurur (kamyon merkezde);
geniş uygulama-içi logo (giriş başlığı) tam kalır. Kaynak: `scratchpad/kare_source.png`. **Masaüstü 1.0.105
YAYINLANDI + AlpnexSetup.exe yeniden yüklendi.** Sunucu güncel=1.0.105.

### 🔍 Logo ikonu büyütüldü + 1.0.103/1.0.104 (2026-08-05, ara adımlar)
Kullanıcı bildirimi: uygulama simgesi diğer ikonların yanında küçük kalıyor. Neden: kaynak PNG'de logonun
etrafında %23–33 şeffaf boşluk vardı → kare ikonda küçük duruyordu. **Çözüm:** şeffaf kenarlar kırpıldı
(749×471 içerik), logo kareyi ~%94 dolduruyor. Web favicon/logo (canlı), masaüstü app-icon/app-logo.ico,
**masaüstü 1.0.103 YAYINLANDI**, **AlpnexSetup.exe yeniden yüklendi** (yeni ikon). Sunucu güncel=1.0.103.

### 🎨 AURORA arayüz yenilemesi CANLIDA (2026-08-05, masaüstü **1.0.98**) — marka Alpnex
Ayrı Claude (tasarım) hesabından gelen "Aurora" paketi (koyu tema + kehribar #F5A623 + indigo ışıma, Plus
Jakarta Sans) 3 aşamada uygulandı; **her yer Alpnex** (eski marka yok), `ui/aurora` dalı → master'a birleşti.
- **Aşama 1 (API, canlı):** A1 `criticalOnly` (malzeme grid+export), A2 `/api/dashboard` `summary`, A3
  `/api/materials/{id}/movements`. Hepsi geriye-uyumlu (yeni alan/param). ⚠️ **Yol boyu bulunan+düzeltilen
  CANLI HATA:** `/api/dashboard` PostgreSQL'de 500 veriyordu (madde-4 GetAlerts ikinci sorgusu aynı bağlantıda
  → "command in progress"; SQLite gizliyordu, web'de catch yutuyordu). Ayrı bağlantı + PG e2e testi.
- **Aşama 3a (Web, canlı):** app.css + MainLayout BuildTheme (Aurora palet, varsayılan kehribar) + App.razor
  (Plus Jakarta Sans) → tüm web Aurora. Login/Home/Reports Aurora düzenleri (işlevsel `@code` korundu:
  giriş adımları, uyarı kategorileri, export). Malzeme listesinde "Yalnız kritik" geçişi (A1 bağlama).
- **Aşama 3b (Masaüstü, 1.0.98):** Palette+Scales.axaml Aurora (kaynak anahtarları birebir → tüm ekranlar
  otomatik) + App.axaml kehribar overlay + LoginWindow Aurora (zemin/odak halkası/gradyan buton/ışıma).
  ViewModel'lere dokunulmadı.
- **Atlanan/ertelenen:** S6 içe-aktar (web'de yok), S1/S2 malzeme kartı bölümlü form+sabit çubuk (çalışan
  forma yapısal müdahale → güvenlik için ertelendi; tema zaten biçimlendiriyor), DashboardView emoji→ikon +
  KPI mikro-düzeni (tema Aurora yapıyor; ince rötuş ertelendi). S4 foto + S5 menü arama zaten vardı.
- Kaynak paket scratchpad'de; repoya eski-marka metniyle KONULMADI.

### 🏷️ MARKA DEĞİŞTİ: DepoWise → **Alpnex** (2026-07-26, masaüstü **1.0.97** CANLI)
Proje adı hukuken başkasına ait olduğu için marka **Alpnex** oldu. Baba tüm veriyi (yerel+sunucu) sıfırladı
→ yerel klasör adı güvenle değişti (taşıma gerekmedi). **Seçenek A uygulandı:**
- **DEĞİŞTİ:** görünür isimler (web PageTitle/başlık, masaüstü pencere başlıkları+üst bar, Kurulum "Alpnex
  Kurulum" + Alpnex.lnk), merkezî marka (`BrandingSettings.Default`=Alpnex), yerel klasör/DB
  (`%LOCALAPPDATA%\Alpnex\...\alpnex.db`, update/logs/machine/branding + Belgeler\Alpnex_Yedekler),
  **logolar** (web `wwwroot` + masaüstü `Assets`; yeni şeffaf logodan üretildi).
- **KALDI (kasıtlı, A):** iç kod adı `DepoWise.*` namespace/assembly + **exe `DepoWise.Desktop.exe`**
  (kullanıcı görmez) + **Fly altyapısı** (`depowise-erp`/`depowise-web` app adları, URL, Neon `depowise_prod`,
  secret `DEPOWISE_*`) + varsayılan firma-id `"DEPOWISE"` (iç kimlik).
- **KAPSAM DIŞI:** `login-bg.png` / `login-hero.png` (yeni arka plan görseli verilmedi → dokunulmadı).
- Test 583/594 (yol/marka assert'leri Alpnex'e güncellendi). Web canlı doğrulandı (sekme "Giriş — Alpnex").
- ⚠️ **Kurulum notu:** klasör adı değiştiği için **en temiz yol yeni Kurulum aracıyla SIFIRDAN kurmak**
  (Alpnex klasörü + Alpnex.lnk). Eski `%LOCALAPPDATA%\DepoWise\` + eski kısayol elle silinebilir. (Oto-güncelleme
  de çalışır ama klasör geçişi nedeniyle bir kez fazladan güncelleme turu olabilir.)
- **Yerel temizleme aracı:** `tools/Alpnex-Yerel-Veri-Temizle.bat` (+`.ps1`) — hem yeni Alpnex hem eski
  DepoWise yerel klasörlerini + eski `DepoWise.lnk` kısayolunu temizler (onaylı, sunucuya dokunmaz).
- **Kurulum exe'si tamamen Alpnex:** indirilen dosya adı artık **`AlpnexSetup.exe`** (API + AssemblyName), Alpnex
  davranışıyla derlendi → `%LOCALAPPDATA%\Alpnex\app`'e kurar + Alpnex.lnk üretir. Native kütüphaneler exe'ye gömülü
  (`IncludeNativeLibrariesForSelfExtract`) → tek dosya çalışır. Eski `DepoWiseSetup.exe` sunucu diskinden silindi.
- Envanter/analiz: [docs/REBRAND_ANALIZI.md](docs/REBRAND_ANALIZI.md).

### 🔐 İçe/dışa aktarım yetki ayrımı (2026-07-26, masaüstü **1.0.96** CANLI)
- `import_export` artık yalnız **İÇE AKTARIM**; **`export`** ayrı modül (Migration056: mevcut import_export
  sahiplerine export otomatik verildi — kimse sessizce kaybetmesin). Deny-by-default.
- Masaüstü + web: yetkisi olmayan kullanıcı için menü (import VEYA export ile görünür) ve **liste Excel
  butonları** (Malzeme/Araç/Günlük) "yetkiniz yok" uyarısı verir + işlem engellenir. API export uçları
  `Require(export)` → 403. Reports export kendi özel-buton yetkisinde (dokunulmadı).
- API+Web deploy (Migration056 canlı PG'de), masaüstü 1.0.96. Test 583/594.
- **Karaman veri notu (Tema B için):** firma OZE, KARAMAN şubesi var; test kullanıcısı test.personel / TEST
  ŞANTİYE. Beklenen: ortak malzeme listesi HER şubede; **stok şube-bazlı** → TEST ŞANTİYE'de stok 0, mevcut
  stok Karaman'da; başka şubede manuel giriş olmadan otomatik stok gelmez.

### 📥 İçe aktarımda zorunlu şube seçimi (2026-07-26, masaüstü 1.0.95)
- İçe aktarım ekranında **"Şube (zorunlu)"** seçici: **"Tüm Şubeler"** (firma geneli) + firmanın şubeleri.
  **Seçim yapılmadan import ENGELLENİR.** Seçilen şube oturum kopyasıyla (OperatingBranchId override) tüm
  import'lara geçer (yakıt/bakım/günlük/stok op_branch_id; araç/personel satırında Şube boşsa bu şubeye düşer).
  Seçilen hedef, çalışma şubesinden **farklıysa onay uyarısı** çıkar. Import masaüstüne özel (web'de import yok).
- ⏳ **Bekleyen (TEMA B — canlı veri):** babanın şubesi **Karaman**; mevcut kayıtları Karaman'a atama +
  **şube-bazlı stok** (stok_balances `material_id`→`material_id+şube`). Canlı stok defteri işi → verinin
  KOPYASINDA test edilip öyle canlıya alınacak. Karaman kararı alındı, uygulama onay + kopya-test bekliyor.

### 🔁 Malzeme modeli DEĞİŞTİ + Yedek yetkisi (2026-07-26, masaüstü 1.0.94)
- **Malzeme = ortak firma-geneli katalog** (kullanıcı kararı): madde 1'in şube-liste filtresi **geri alındı**;
  malzeme tüm şubelerde aynı görünür. **Ayrım STOK'a taşınacak** → **şube-bazlı stok** ayrı, büyük, canlı-defter
  işi olarak **PLANLANDI, henüz yapılmadı** (bkz. aşağıdaki "Sıradaki tek iş" ve karar notu). `materials.branch_id`
  kolonu duruyor (zararsız köken etiketi).
- **Yedek Yönetimi** masaüstünden kaldırıldı (web-only); web'de yalnız **süper + kısıtlı süper admin** görür
  (API `/me/menu` → `isRestrictedSuperAdmin`; NavMenu `@superr`; Backup.razor deny-by-default). Geri yükleme
  süreci korumalı süper-admin ekranı olarak sonra tasarlanacak (canlıyı doğrudan ezmeyen, doğrulama-kopyalı).
- API+Web deploy, masaüstü 1.0.94 yayında. Test 582/593.

### 🆕 4 maddelik istek TAMAM + CANLIYA ALINDI (2026-07-26, Opus 4.8) — masaüstü **1.0.92 YAYINDA** + API/Web deploy
Kullanıcının 4 isteği yapıldı ve **yayınlandı**: **API deploy** (Migration055 canlı PG'de, health 200) +
**Web deploy** (depowise-web, 200) + **masaüstü 1.0.92** (sunucuda "en güncel = 1.0.92", checksum `1a04091f…`, 85.2 MB).
Bu paket **birikeni** kapsar: rol atama güvenliği (eski 1.0.92 planı) + foto biçim uyarısı + detay paneli oto-kapanma + madde 1-4.
1. **Malzemeler şube-bazlı** (Madde 1, commit f625f65): `materials.branch_id` (Migration055) + `BranchScope`
   ile seçili şubeye filtre. Şubesiz eski kayıtlar HER şubede görünür (babanın canlı verisi gizlenmez).
   Malzeme kodu benzersizliği firma-geneli kaldı (canlı veride riskli index değişimi yapılmadı).
2. **Aranabilir alanlar** (Madde 2, commit 5449da2): masaüstü Kategori/Alt Kategori/Birim/Marka + tüm
   personel/sürücü açılırları `AutoCompleteBox` (metinle ara). Web zaten aranabilir (`LookupSelect`).
3. **Muadil malzeme köprüsü** (Madde 3, commit 769f211): malzeme detay panelinde muadiller tıklanınca
   ilgili malzemenin detayını açar (masaüstü). Web'de malzeme detay paneli yok → yalnız masaüstü.
4. **Uyarılar kategori butonları + bakım bug** (Madde 4, bu commit): Ana ekran + Uyarılar ekranında
   Malzeme/Bakım/Sigorta-Muayene/Yakıt **sayılı butonlar** (tıkla→filtrele, tekrar tıkla→Tümü); masaüstü+web.
   **Bug düzeltildi:** araca ATANIP hiç yapılmamış bakım tanımı uyarı vermiyordu → artık "İlk bakım yapılmadı"
   (Overdue) uyarısı çıkar. **Test 582/593 yeşil** (+1 yeni bakım testi).
> ✅ CANLIDA: API (Migration055 → canlı PG'de `materials.branch_id` var) + Web + masaüstü **1.0.93**.
> Makineler bir sonraki girişte 1.0.93'ü indirir. Web (Alerts/Home kategori butonları) depowise-web'de yayında.
> **1.0.93 (2026-07-26):** Uyarılar ana ekranda+Uyarılar ekranında ilk açılışta LİSTELENMEZ — yalnız kategori
> butonları+sayıları görünür; liste ancak ilgili butona tıklanınca gelir (tekrar tıkla → gizle). Masaüstü+web.

### 🟢 Tek bakışta güncel durum

| Ne | Durum |
|---|---|
| **PostgreSQL geçişi** | ✅✅ **CANLIYA ALINDI (2026-07-24)** — **sunucu (`depowise-erp`) + web PostgreSQL'de** (Neon `depowise_prod`). Masaüstü SQLite'ta kaldı (eşitleme API üzerinden PG'ye yazar). Gerçek verinin KOPYASIYLA prova edildi, canlıya alındı; eski SQLite yedekte (`/data/depowise-server.db`, el değmedi). Geri dönüş: `flyctl secrets unset DEPOWISE_PG_URL`. Detay: [docs/GOREV_PANOSU.md](docs/GOREV_PANOSU.md) Görev A. |
| **Testler** | **591 test** (580 SQLite yeşil + 11 gerçek Neon PG; `dotnet test`) + canlı eşitleme QA **7/7** |
| **1.0.91 (2026-07-25)** | **Şifre sıfırlama + kullanıcı görünürlük** sunucu-tabanlı (masaüstü çevrimiçiyken sunucudan okur/yazar → değişiklik hedefe ulaşır). **Şube-bazlı veri filtreleme:** belirli şubeyle girişte veri o şubeye filtrelenir ("Tüm Şubeler"→hepsi; şubesiz eski kayıtlar korunur); araç/günlük/yakıt/bakım/talep/stok + NORMAL raporlar. Yönetici raporları filtresiz (tüm şubeler). |
| **Son iki düzeltme (2026-07-25, 1.0.90)** | **Çıkış hızı:** kapanış push beklemesi 10sn→2sn. **Şube/Kullanıcı veri kaybı:** sunucu-otoriteli olduklarından her girişte aynalanıp siliniyorlardı → artık masaüstü çevrimiçiyken create/update/delete'i doğrudan SUNUCU API'sine yapar (`OrgServerClient`), kullanıcı yerele sunucu id'siyle işlenir (`ImportServerUser`); çevrimdışı → uyarı. |
| **Son 3 iş (2026-07-25)** | **1) Yetki ekranı:** admin/süper admin hedef → matris TAM işaretli + salt-okunur + bilgi (boş açılma sorunu bitti). **2) Kullanıcı:** liste herkese açık (Personel sınırlı, rol gizli), düzenleme admin; şifre tanımdan değişmez → **Şifre Sıfırla** (geçici=kullanıcı adı, ilk girişte kendi belirler). **3) Masaüstü oto-güncelleme:** oto açıkken eşitleme ekranında sessiz indir→Kur/Ertele (10 dk), onaysız kapatınca zorla kur, yarım kurulum self-heal (`AutoUpdateService`). Hepsi web+API canlı; masaüstü **1.0.89**'da. |
| **Yeni özellik** | **Durum Rapor + Rapor Excel dışa aktarma (2026-07-25)**: Yönetici raporları altına **Durum Rapor** — şube bazlı SAYISAL özet (Araç şablonlu/şablon-dışı; Personel/Bakım/Yakıt/Talep/Günlük toplamları; Malzeme firma-geneli tek satır çünkü şubesi yok), tarih filtreli. Ayrıca Raporlar + Yönetici Raporları ekranlarına **Excel'e Aktar** butonu — **iki ayrı özel yetki** (Rapor / Yönetici Rapor); yetki yoksa "yetkiniz yok" uyarısı (deny-by-default, UI+API). PG-güvenlik: tüm rapor sayımları `CAST(... AS INTEGER)`. Önceki: Yönetici raporları şablonlu/şablon-dışı + şablona bağlama (Migration054). |
| **Şema** | Migration **054** (materials.template_id). **Durum Rapor için yeni migration YOK** — mevcut kolonlar (branch_id, op_branch_id, template_id, created_at). |
| **API (sunucu)** | `depowise-erp.fly.dev` — **canlı** (PostgreSQL), health 200 · yeni: `/api/reports/{type}/export` + `status` rapor tipi |
| **Web** | `depowise-web.fly.dev` — **canlı** · yeni: Durum Rapor sekmesi + Excel'e Aktar (yetki kapılı) |
| **Masaüstü** | **1.0.91 YAYINDA** — 1.0.90'ın tümü + şifre sıfırlama/kullanıcı görünürlük (sunucu-tabanlı) + şube-bazlı veri filtreleme (Raporlar mevcut şube, Yönetici raporları tüm şubeler). Güncelleme: makine yalnız EN SON tam paketi indirir/kurar. |
| **Git** | temiz + `origin/master` ile senkron |
| **Bekleyen iş** | **Senkron çekirdeği ✓ · Düzenleme kilidi ✓ · 1.0.87 yayında ✓.** Sıradaki: giriş hız sınırı kararı (ortak ofis IP) · Giriş-Çıkış çoklu malzeme · makine bazlı güncelleme yetkisi · Yedek ekranları |

## 🔄 FORMAT SONRASI — BURADAN DEVAM ET (2026-07-22)

**PC formatlandı.** Kurulum: Git · **.NET 8 SDK** · flyctl · (VS Code/Claude Code) →
`git clone https://github.com/osmanalpaslan/DepoWise` → `flyctl auth login` → bana "devam" de.

### Sunucu durumu (ÖNEMLİ)
- Sunucu **fabrika ayarına sıfırlandı** (boş DB, eski veri yok). Firma/malzeme/araç **sıfırdan** kurulacak.
- **Süper admin giriş: `superadmin` / `DepoWise-2026`** → ilk girişte **şifreyi değiştir**.
- Fly secret'ları ayarlı: `DEPOWISE_SEED_ADMIN_PASSWORD` / `DEPOWISE_SEED_SUPERADMIN_PASSWORD` = `DepoWise-2026`
  (boş DB'de seed bu şifreyi kullanır; yoksa RASTGELE şifre üretip loga yazar — eski kafa karışıklığının sebebi buydu).

### Eşitlemede yapılanlar (canlı, masaüstü 1.0.85)
- **Z2** — push yanıtı (`upserted/skipped/errors`) okunuyor; `sync.log` + üst barda uyarı rozeti.
- **Z4** — delta kök neden: push artık **sunucu global max** yerine **makinenin kendi kalıcı watermark**'ını kullanır
  (`sync_push_watermark`) → başka kaydın zaman damgası yüzünden atlama imkânsız.
- **"Firma İş Verisini Sıfırla"** ekranı (web, süper admin): firma/şube/kullanıcı KALIR, yalnız iş verisi silinir.

### Senkron çekirdeği TAMAMLANDI (2026-07-22, masaüstü **1.0.86**)
1. **Z1 ✓** — `SyncGate` (tek SemaphoreSlim): 6 giriş noktası (giriş senkronu, tick, manuel Eşitle,
   Yereli Sıfırla, çıkış push'u, kapanış push'u) tek kapıdan. Reset↔tick yarışı bitti.
   Çıkış/kapanışta push atlanır ama **çıkış/kapanış daima yapılır**.
2. **Z3 ✓** — retry: sunucu bazı satırları uygulamazsa **watermark İLERLEMEZ** → sonraki turda otomatik
   yeniden denenir. 5 denemeden sonra **poison**: watermark ilerler (kuyruk kilitlenmez) + **kalıcı uyarı**.
   Sayaç/poison `SettingsService`'te kalıcı. Rozet artık sorun sürerken **kaybolmuyor**.
3. **Z5 ✓** — üst barda **daima görünür tıklanabilir rozet** ("✓ Senkron" / uyarı) → **Senkron Durumu** paneli:
   son başarılı push/pull zamanı, bekleyen/yeniden deneme, gönderilemeyen adet + sebep, `sync.log` yolu.

### QA yeniden aktif + eşitlemede gerçek hata bulundu (2026-07-22)
- **CLAUDE.md §7 (Ekran QA Motoru) yeniden yürürlükte** (senin isteğin). Yeni §7.0: QA israfa dönüşmesin —
  yalnız değiştirilen ekran, rapor dosyaya, yanıta kısa özet. Yeni §7.0.1: canlı testlerde **yalnız**
  `.env.test.local` içindeki test hesabı kullanılır (gerçek yönetici hesapları test edilmez).
- **Bulunan hata (düzeltildi):** stok hareket defteri `updated_at` taşımadığı için delta filtresine hiç
  girmiyordu → (a) her eşitlemede TÜM defter aktarılıyordu, (b) yeni hareket firma sürümünü yükseltmediği
  için **karşı makine çekmiyordu**. Damga artık `updated_at` yoksa `created_at`. Canlı: delta 663 → **0 satır**.
  Makine başına tek seferlik tam gönderim (`WatermarkEpoch`) ile eski watermark tuzağı da kapatıldı.
- **Testler 563/563**, canlı QA **7/7**. API canlıya alındı. Rapor: `docs/tests/Esitleme_Test_Report.md`.
- Canlı QA'yi istediğin an tekrar koşabilirsin: `node tools/qa/live-sync-check.mjs`

### Düzenleme kilidi — TAMAM (2026-07-22, API+web canlıda; masaüstü paket bekliyor)
Aynı kaydı iki kişi/iki makine düzenlerse ikincisi birincisini **sessizce eziyordu** (`version` yazılıyor
ama kontrol edilmiyordu). Artık kaydederken kayıt arada değiştiyse **üzerine yazmaz**, sorar:
**"Kaydı yenile"** / **"Formda kal"** (yazdıkların kaybolmaz).
- Gerçek kilit DEĞİL, sürüm karşılaştırması — çünkü sunucu kilidi **çevrimdışı çalışmaz** ve program
  çökerse kayıt kilitli kalırdı. Sürüm kontrolü çevrimdışı dahil her zaman çalışır.
- **Kapsanan ekranlar: Malzemeler · Araçlar · Personel · Bakım Tanımları** (masaüstü + web + API).
- **Kapsam dışı (kasıtlı):** Günlük Faaliyet, Yakıt, Bakım *kayıtları* zaten düzenlenemiyor (ekle-only
  defter kayıtları: oluşturulur, iptal/silinir; alanları hiç güncellenmez) → üzerine yazılacak şey yok.
- Canlı kanıt: her üçü için eski sürümle kaydetme **409**, ilk verinin ezilmediği doğrulandı (test kayıtları silindi).

### Çok makineli simülasyon + ölçek testi (2026-07-22) — masaüstü **1.0.87 YAYINDA**
10 sanal makine/kullanıcı, 3 şube, bütün ekranlarda eş zamanlı gerçekçi kullanım (yerel sunucu, boş DB).
Rapor: `docs/tests/Cok_Makineli_Simulasyon_Raporu.md` · Araç: `tools/qa/multi-machine-sim.mjs`
- **Düzenleme kilidi kanıtlandı:** 10 makine aynı sürümü aynı anda yazdı → **tam 1 kazanan, 9 × 409**.
- Mükerrer kod, negatif stok, tenant sızıntısı: hepsi doğru engellendi. Son koşu: **545 istek, 0 mantık hatası**.
- **Bulunan hata (düzeltildi):** stokta olmayan miktarı çıkarınca **500 "beklenmeyen hata"** dönüyordu.
  Kural doğruydu ama `NegativeStockException`/`MeterBackwardException` tanınmıyordu → artık **400 + gerçek mesaj**.
- **Açık bulgu (senin kararın):** giriş sınırı **IP başına 30/5dk**. Tek ofis internetinin arkasındaki 30+
  kişi vardiya başında birlikte girerse tıkanır. 500 kullanıcı hedefinde mutlaka değişmeli.
- **Ölçek:** okuma ~6.000 istek/sn (200 eşzamanlıda p95 51 ms), yazma ~**500/sn**'de düzleşiyor (SQLite tek
  yazıcı). 500 kullanıcı ≈ 50–100 istek/sn → **ham hız sorun değil**; duvar SQLite tek-yazıcı + tek makine
  + snapshot sayfalamasının olmaması. Ölçümler geliştirme PC'sinde/küçük veriyle alındı.

### Bilinen açıklar / kurallar
- ⚠️ **Aynı veriyi İKİ makinede import etme!** Her import farklı ID üretir → makineler birbirine oturmaz
  (araç/tanım FK'leri kırılır). **Tek makinede import et, diğeri eşitlemeyle çeksin.**
- Ertelenen: `server_seq` (saat-bağımsız pull sırası), ledger `op_id` idempotency, yakıt/bakımın LWW'den çıkarılması,
  snapshot sayfalama, **makine bazlı güncelleme yetkisi** (istendi, başlanmadı — `/api/releases/latest` makineyi
  tanımıyor, küçük bir masaüstü değişikliği gerekir).
- Araç import başlıkları birebir olmalı: **`İç Kod`**, **`Durum`**, **`Şantiye / Şube`** (boşluklu).
- Windows **Smart App Control** kapatıldı (açıkken git push + Avalonia derlemesi engelleniyordu).

### 🛡️ Senkron güvenilirlik planı — GPT ile mutabık, mimari DONDURULDU (2026-07-19)
Kök sorun: aynı firma+şubede iki masaüstü birbirini "zaman zaman" göremiyor. Kök neden (kanıtlandı):
delta watermark = tüm tabloların TEK global `max(updated_at)` + `updated_at` her makinenin KENDİ saatiyle →
gönderici ve alıcı atlaması. Çekirdek adımlar: **Z1** tek sync motoru+mutex · **Z2** push sonucunu oku
(sessiz başarısızlık bitsin) · **Z3** reset=sunucudan tam yenile (hard-delete yok) · **Z4** delta kök neden
(gerçekten gönderilmemiş/eksik kayıtları taşı; full-push/since=0 YASAK) · **Z5** basit sync durumu.
- **Z2 TAMAM (1.0.85):** push yanıtı (`upserted/skipped/errors`) artık okunuyor; `sync.log`; üst barda uyarı
  rozeti + manuel "Eşitle" diyaloğunda atlanan kayıt detayı. Canlı kanıt: HTTP 200 ama `{skipped:1,errors:[...]}`
  dönen "sessiz başarısızlık" senaryosu artık GÖRÜNÜR.
- **Z4 TAMAM (1.0.85) — DELTA KÖK NEDEN:** push artık "since = SUNUCU global max" DEĞİL, her makinenin KENDİ
  **kalıcı watermark**'ını (`sync_push_watermark`, SettingsService) kullanıyor. Böylece başka bir tablonun/
  makinenin yüksek zaman damgası, bu makinenin kendi kaydını atlatamaz (94-araç bug'ının kökü). since=0 yalnız
  ilk kurulumda; sürekli full push/resend YOK; watermark yalnız BAŞARILI push'ta ilerler (başarısızda tekrar
  denenir). Dosyalar: `BusinessSyncPushService.cs` (watermark), `ShellViewModel.cs`/`LoginViewModel.cs` (çağrı).
  Kanıt: `BusinessSyncTests.Z4_...` testi — eski (since=globalmax) kaydı ATLIYOR, yeni (watermark) GÖNDERİYOR,
  tekrar göndermiyor. **İki-makine (SIKIB3U↔8KN8USG) 6-senaryo testi kullanıcı tarafından yayından sonra yapılacak.**
  Sıradaki çekirdek: Z1 (tek mutex) · Z3 (reset=tam yenile) · Z5 (durum paneli).

### 🔧 Eşitleme kök düzeltme (2026-07-19) — "araçlar sunucuya ulaşmıyordu"
**Belirti:** Büyük firmada (2508 malzeme) push zaman aşımına uğruyor; araçlar sunucuya HİÇ ulaşmıyordu
(canlı kontrol: sunucuda 2508 malzeme, 0 araç). **Kök neden:** Sunucuda `ApplyCore` upsert döngüsü
transaction'sızdı → her satır ayrı commit (fsync) → 2508+ kayıt dakikalarca sürüyor → 120s'de yarıda kesiliyor
(malzemeler yazıldı, araçlar yazılamadı). Delta-push da araçları atlıyordu (updated_at ≤ sunucu sürümü).
**Düzeltme:** (1) `ApplyCore` tek `BEGIN/COMMIT` içinde → 1 commit, hızlı, atomik (yarıda kalma imkânsız).
(2) Girişte TAM push geri geldi (uzlaştırma: sunucuda eksik satır varsa tamamlar; artık hızlı olduğu için
zaman aşımı yok). Rutin push (ShellViewModel timer) DELTA kalır.

**✅ DOĞRULANDI + ÇÖZÜLDÜ (2026-07-19):** Kök neden canlı kanıtlandı — SIKIB3U yerelinde **94 araç VARDI**
(veri kaybı YOK), sunucuda 0. Düzeltilmiş sunucuya araçlar tek tek gönderildiğinde `upserted:94, skipped:0,
errors:[]` → sunucu tarafı kusursuz; sorun eski transaction'sız apply'ın büyük push'u (2508 malzeme+94 araç)
120s'de yarıda kesmesiydi (malzemeler FK sırasında önce → yazıldı; araçlar sıraya gelmeden koptu). 94 araç
sunucuya yüklendi (canlı doğrulama: /api/vehicles = 94 görünür). **Kullanıcının sorusu (süper admin çok-firma
yereli tetikler mi?): HAYIR** — push `company_id`'ye göre süzülüyor, çapraz-firma sızıntısı yok. **Baba makinesi
(8KN8USG) + web:** ~15 sn'de otomatik çeker (veya "Eşitle"/yenile). Her iki makineyi 1.0.84'e güncelle → tekrarı önlenir.

### 8 maddelik masaüstü-öncelikli paket (2026-07-19, ADR-098) — 7/8 canlı
Arıza Açıklaması · Enter ile filtre · Fluent menü rengi · Yakıtı Alan (Migration052) · PDF logolar (talep formu
büyük + ekonomik) · araç sayfalama alta. **Kalan (1):** Giriş-Çıkış çoklu malzeme. **Yeni kural:**
`.claude/rules/platform-priority.md`. **Web eşitleme sorunu = ADR-097 ile aynı kök neden** (sunucu boş, makine A
push edince gelir — ayrı web hatası yok). Detay: `docs/DECISIONS.md` ADR-098.

### Çift-tık "hızlı düzenle" penceresi (2026-07-19, ADR-096)
Malzemeler + Araçlar'da kayda çift tıklayınca ayrı pencerede Düzelt/Kaydet/Sil (tek tık detay panelini korur).
Web (MudDialog) + masaüstü (kod-arkası Window). Fotoğraf/muadil/uyumlu araç ve sayaç KORUNUR (hızlı pencere
silmez). Web canlı; masaüstü 1.0.73'te canlı. **⚠️ Görsel/uçtan-uca test kullanıcıya** (bu ortamda Avalonia +
web giriş formu test edilemedi). Detay: `docs/DECISIONS.md` ADR-096.

### Opus 4.8 gözden geçirmesi (2026-07-19, ADR-095)
Kullanıcı isteğiyle bu oturumdaki tüm iş (ADR-090…094) Opus 4.8 ile satır satır denetlendi (tenant/izin/
senkron/idempotency/web-masaüstü ayna). **Tek gerçek bulgu:** `EnsureExtraDefinition` atomik değildi →
eşzamanlı sunucu isteğinde çift gizli sabit tanım riski (masaüstü tek-kullanıcı, etkilenmez). Tek
`INSERT…SELECT WHERE NOT EXISTS` ile yarışsız yapıldı; API redeploy edildi (554/554). Diğer her şey TEMİZ.
Detay: `docs/DECISIONS.md` ADR-095.

### Günlük Faaliyet: "İlave Yağ / İlave Filtre / Tamir" (2026-07-19, ADR-091)
Bakım ile AYNI mekanizma (sayaç + malzeme stok düşümü dahil), yalnız Bakım Tanımı/Alt Bakım kullanıcıya
hiç sorulmaz — her tür firma başına otomatik oluşan sabit bir tanıma bağlanır. Web+masaüstü Kayıt Tipi
listesine eklendi. **Yan bulgu:** masaüstünde (ve sunucuda) servis başlatma sırası kusuru bulundu —
`DailyActivityService`, `Maintenance`/`MaintenanceDefs` atanmadan ÖNCE oluşturuluyordu (readonly alan kalıcı
`null` kalıyordu) → düzeltildi. Detay: `docs/DECISIONS.md` ADR-091. **Masaüstü 1.0.70'de canlı.**

### 🔴 KRİTİK: Senkron donma + sessiz başarısız push düzeltildi (2026-07-19, ADR-090)
Baba dosyasını içeri aldıktan sonra veri web'e ULAŞMAMIŞTI. Canlı sunucu doğrulandı: **OZE GRUP firmasında
0 malzeme, 0 araç** — push hiç başarılı olmamış. Kök neden: (1) senkron ağır işi (BuildSnapshot/ApplyPull)
Task.Run OLMADAN arayüz iş parçacığında çalışıyordu → "menüler arası donma" şikayetinin asıl sebebi budur
("sunucu kaynaklı" değil, istemci iş parçacığı bloklanması); (2) 30sn HttpClient zaman aşımı büyüyen veride
(2600+ kayıt) aşılıyor, `catch{}` bunu sessizce yutuyordu → veri SONSUZA KADAR sunucuya ulaşmıyordu, hata da
görünmüyordu. Düzeltme: ağır iş `Task.Run`'a alındı (arayüz artık donmaz) + zaman aşımı 120sn'e çıkarıldı +
"Eşitle" butonu artık başarısızlığı doğru gösteriyor. **Masaüstü 1.0.69'da canlı. Baba makinesini güncelleyip
"Eşitle"ye basması (veya normal girişi) gerekiyor** — geçmiş içe aktarılan veri o an push edilecek. Detay:
`docs/DECISIONS.md` ADR-090.

### 12 maddelik yeni istek listesi (2026-07-19) — sürüyor
Kullanıcı 12 madde verdi (Opus 4.8, "en son test edeceğim"). Durum:
- ✅ **Senkron donma/başarısız push** (yukarıda, ADR-090, KRİTİK+canlı).
- ✅ **Tanım adlarında fazla boşluk** normalize (Migration050 + Insert/Rename + import eşleştirme).
- ✅ **"Excel'e Aktar" butonu** Malzemeler+Araçlar'da (web+masaüstü) — aktif filtreyle TÜM sonuçları indirir.
- ✅ **Kural dosyası**: `.claude/rules/list-screens.md` (yeni filtrelenebilir alan + Excel export standardı).
- ✅ **Günlük Faaliyet'e 3 yeni tip** (İlave Yağ/İlave Filtre/Tamir) — ADR-091, masaüstü 1.0.70'de canlı.
- ✅ **Tanım Düzenle'de kilitli/sabit tanım** (ADR-092) + **form kutuları odaksız görünür + Semi arama
  kutusu Fluent ile aynı** (ADR-093) + **Günlük Faaliyet'e filtre+sayfalama+sıralama+Excel grid deseni**
  (ADR-094, madde 8/9 tamam) — masaüstü 1.0.72'de canlı. ⚠️ Görsel doğrulama kullanıcıda (bu ortamda
  Avalonia/giriş gerektiren web ekranları test edilemedi).
- ✅ **Çift-tık ayrı pencerede Düzelt/Kaydet/Sil** — Malzemeler + Araçlar (web+masaüstü, ADR-096, 1.0.73).
- ⏳ **Kalan (yalnız kullanıcı doğrulaması, geliştirme değil):** farklı makine aynı şube senkron doğrulaması
  (ADR-090 ile çözülmüş OLABİLİR, kullanıcı 1.0.69+ ile test etmeli) · ADR-096 çift-tık pencere görsel testi ·
  ADR-092/093/094 masaüstü görsel testleri.
Detay: `docs/YARIM_KALAN_ISLER.md`.

### 7 maddelik liste geliştirmeleri paketi (2026-07-18, ADR-089)
Kullanıcı 2600+ kayıtla çalışırken 7 istek verdi. **Web + backend TAMAM ve canlıda; masaüstü UI sürüyor.**
1. Sayfa boyutu varsayılan **25** (kişiye özel hatırlanır). 2. Sayfa numaraları + kayıt bilgisi tablonun
**üstünde-solunda**. 3. **Excel-benzeri grid**: pencere küçülünce taşma/kayma yok (yatay kaydırma) +
sürüklenebilir kolon genişliği (kişiye özel kalıcı). 4. **Tanım düzenleme** (rename artık definitions/Edit
yetkisiyle, süper-admin kısıtı kalktı; masaüstünde satır-içi düzenleme). 5. **Başlığa tıklayınca sıralama**
(metin A→Z/Z→A Türkçe; sayısal küçük→büyük). 6. Yeni tanım/rename **50 karakter** sınırı. 7. İçe aktarımda
**"Tür" harf duyarsız kanonik eşleme** ("YEDEK PARÇA"→"Yedek Parça") + Migration048 mevcut veriyi düzeltir.
Detay: `docs/DECISIONS.md` ADR-089. Test: 523/523. **Masaüstü — TÜMÜ 1.0.68'de canlı:** #1 (sayfa boyutu 25+
hatırlama), #4 (tanım düzenleme), #6 (50 kar), #7 (Tür), #2 (sayfalama üstte-sola taşındı), #5 (başlığa
tıklayınca sırala — yeni `SortHeader` + `IListGridViewModel`), #3 (Excel-benzeri: yatay kaydırma + sürüklenebilir
kolon genişliği, kişiye özel kalıcı). **⚠️ Görsel doğrulama yapılamadı** (Avalonia bu ortamda çalıştırılamıyor) —
yalnız temiz derleme ile güvence alındı; kullanıcının canlı ortamda gözden geçirmesi gerekiyor.

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

> **AKTİF: M-S1a — ONAYIN BEKLENİYOR.** `material_request_items` + `maintenance_materials` tablolarına
> firma kolonu: **kod ve testler hazır, canlıya UYGULANMADI.** Ön rapor:
> [docs/MS1A_PRE_MIGRATION_RAPORU.md](docs/MS1A_PRE_MIGRATION_RAPORU.md).
> ⚠️ **API yayınlamak = canlı migration** (API açılışta migration çalıştırır) → onayın gelmeden yayınlanmayacak.
> Canlıda taşınacak satır: 2 (ikisi de Oze İnşaat), çözülemeyen: 0. Testler: 14/14 SQLite, 6/6 PostgreSQL, takım 839/0.
> Sonrası: 4) ortak düzenleme altyapısı + Personel/Talepler çift tık · 5) Günlük Faaliyet + Bakım düzenleme ·
> 6) düzenleme kilitleri · 7) Excel → Web · 8) çoklu malzeme + şube sürüm kontrolü · 9) LookupBox ·
> 10) kolon kataloğu → Alan/Kolon Yönetimi · 11) Faz S / FK / benzersizlik.
> Sıra kaynağı: [docs/KARAR_ANALIZI_K1_K7.md](docs/KARAR_ANALIZI_K1_K7.md) ·
> [docs/YARIM_ISLER_VE_EKRAN_STANDARDIZASYONU_ANALIZI.md](docs/YARIM_ISLER_VE_EKRAN_STANDARDIZASYONU_ANALIZI.md)
>
> ---
>
> **(Geçmiş bağlam) Talep Faz 3 ONAY BEKLİYOR:** [docs/FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md](docs/FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md)
> sonundaki **15 maddelik onay listesi** cevaplanmadan Faz 3 kodlamasına başlanmaz. Önerilen sıra:
> **Faz 3-Ön** (PostgreSQL eşzamanlılık düzeltmesi, migration yok) → Faz 3a (migration + servis) →
> 3b masaüstü → 3c web → 3d transfer/iptal.
>
> ---
>
> **(Geçmiş bağlam) PostgreSQL geçişi (Görev A):** Sunucu KODU artık uçtan uca PG-hazır ve 579 test yeşil.
> **Kod tarafında açık iş kalmadı.** Sıradaki tek şey **canlı geçiş** ve bu **senin onayınla** başlar
> (üretim + altın kural): Fly API'yi Neon bölgesinde çalıştır → babanın verisinin **KOPYASIYLA** prova →
> sağlamsa yeni makineleri yönlendir; eski SQLite sunucusu yedekte kalır. Hazır olduğunda "canlı geçişe
> başla" de. Ayrıntı ve nerede kaldık: [docs/GOREV_PANOSU.md](docs/GOREV_PANOSU.md).
>
> ---
>
> **(Geçmiş bağlam — masaüstü işleri)** Büyük yetki/ekran promptu (Adım 1–7) kod + test (313/313) + **CANLIYA ALINDI**
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
