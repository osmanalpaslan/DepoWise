# Rakipler sahayı parça parça kapsıyor, Alpnex birleştiriyor

**Erişim tarihi (tüm kaynaklar):** 2026-10-12 · **Kapsam:** açık web, üretici yardım merkezleri, GitHub kaynak kodu · **Hazırlayan:** araştırma notlarının sentezi (6 not dosyası)

Alpnex'in (DepoWise) tüm kapsamını tek başına karşılayan bir ürün yok. Karşılaştırılabilir ürünler dört kümeye ayrılıyor ve her biri Alpnex'in bir dilimini kapsıyor. Birinci küme Türk inşaat ERP'leri (Yapıtaşı, KonstraERP, Uyumsoft, Qera, DİA+Mavvo). Bunların ortak çekirdeği **hakediş, taşeron, sözleşme, bütçe-gerçekleşen maliyet ve puantaj**; iş makinesi, yakıt, zimmet ve çevrimdışı çalışma ya yok ya da ikincil [K-53][K-54][K-55][K-56]. İkinci küme ABD inşaat ERP'leri. Bunlar içinde yapıca en yakın örnek **Trimble Viewpoint Vista Equipment Management**: ekipman maliyeti ve iç kira geliri aynı ERP içinde işe (projeye) yansıyor [K-11]. Üçüncü küme ağır ekipman bakım ve filo yazılımları (HCSS, B2W, Tenna, Clue, Fleetio). Dördüncü küme alet ve zimmet yazılımları (ToolWatch, ShareMyToolbox, Hilti ON!Track) [K-01][K-05][K-26]. İncelenen hiçbir ürün **çevrimdışı çalışan bir masaüstü istemci** sunmuyor; çevrimdışı destek yalnız mobil veri toplama uygulamalarında var [K-03][K-41]. Bu, Alpnex'in en net farkı. Açık kaynak tarafında ERPNext (GPL-3.0), Odoo Community (LGPL-3.0), Snipe-IT, Atlas CMMS ve Fleetbase (AGPL) kaynak düzeyinde en öğretici veri modellerini sunuyor, ama **kodları kapalı kaynak Alpnex'e kopyalanamaz** [K-70][K-74][K-113][K-120][K-128]. Çevrimdışı eşitleme (senkron) için kanıtlanmış desen şu: sıralı kalıcı kuyruk (outbox), istemci başına işlem numarası, sunucunun son sözü söylemesi ve commit sırasına dayanan imleç (cursor). Alpnex'in hareket defteri, işlem kimliği (operation id) ve tekrar korumalı (idempotent) tasarımı bu desene uyuyor. Asıl riskler imlecin zaman damgasına dayanması ve kalıcı hatayla geçici hatanın ayrılmaması [K-142][K-144][K-147][K-149]. Raporun sonunda kaynaklı ve önceliklendirilmiş **45 eksik işlev/süreç adayı** var. En güçlü adaylar şunlar: makine iç kira birim fiyatıyla projeye maliyet aktarımı, iki adımlı (yolda/transit) depo transferi, zimmet kabul kaydı ve periyodik zimmet sayımı, sayaç bütünlüğü kuralları, kiralık makine sözleşmesi ve otomatik tahakkuk, mal kabul ve fatura eşleştirmesi, kayıt bazında etkinlik paneli ve e-İrsaliye uyumu.

---

## Okuma rehberi: etiketler, yöntem ve lisans uyarısı

Bu raporda her iddia kaynağın güvenilirlik düzeyiyle etiketlenmiştir. **[DOC]** resmî yardım merkezi veya belgede doğrulanan davranıştır. **[SRC]** kaynak kodda (GitHub) birebir okunan alan, fonksiyon veya durumdur. **[MKT]** üreticinin pazarlama sayfasındaki iddiadır ve bağımsız doğrulanmamıştır. **[SEC]** ikincil kaynaktır (Capterra, basın bülteni, forum, bayi sayfası). **[SNIP]** sayfanın kendisi açılamadığı için yalnız arama motoru özetinden alınmıştır ve güvenilirliği daha düşüktür. **(doğrulanmadı)** birincil kaynakla teyit edilemeyen bilgidir. Türk ürünlerinin neredeyse tamamı yalnız üretici sayfalarından incelenebildi. Bu yüzden Türk rakiplerin özellikleri büyük ölçüde [MKT] düzeyindedir ve bağımsız kullanıcı yorumu bulunamadı [K-53][K-54].

**Karşılaştırma kuralı.** "Rakipte var" demek "Alpnex'te olmalı" demek değildir. Son bölümdeki adaylar Alpnex'in iş modeline (Türk şantiye, çok şube, çevrimdışı masaüstü, saha operasyonu odağı) göre değerlendirilmiştir. Alpnex'in bu adaylardan hangilerine zaten sahip olduğu bu çalışmada kodda kontrol edilmedi; bu karşılaştırma sonraki aşamada yapılacak.

**Lisans uyarısı (kesin).** ERPNext ve Frappe HRMS **GPL-3.0** [K-70]; Odoo Community **LGPL-3.0** [K-74]; Snipe-IT, Atlas CMMS, Fleetbase ve Shelf.nu **AGPL-3.0** (Atlas ve Fleetbase ayrıca ticari lisanslı) [K-113][K-120][K-128][K-136]; Dolibarr **GPL-3.0** [K-133]. Bu kodlar Alpnex'in kapalı kaynak .NET koduna **kopyalanamaz, çevrilemez, uyarlanamaz**. Yalnız fikir, iş akışı ve veri modeli kavramı örnek alınabilir; uygulama sıfırdan yazılmalıdır. Frappe Framework'ün kendisi MIT lisanslıdır [K-72], ama ERPNext iş mantığı GPL'dir. MIT lisanslı InvenTree bu kuralın tek istisnası; onun bile özellikleri bu turda incelenmedi [K-141].

---

## Karşılaştırılabilir ürünler dört kümeye ayrılıyor, hiçbiri Alpnex'in tamamını kapsamıyor

Alpnex dört işi bir arada yapıyor: malzeme/şantiye deposu, araç ve iş makinesi filosu (bakım, yakıt, sayaç, kiralık), ön muhasebe (cari, fatura, kasa-banka, maliyet merkezi) ve iş akışı (talep, onay, iş emri, zimmet, evrak). Bu dörtlüyü birlikte sunan ürün bulunamadı. ABD'de bir müteahhit genellikle ERP ve bakım için Vista veya HCSS'yi, alet zimmeti için de ToolWatch'ı kullanıyor; ürünler birbirine entegrasyonla bağlanıyor [K-26][K-21][K-32]. Türkiye'de ise inşaat ERP'lerinin ağırlık merkezi hakediş ve maliyet. İş makinesi ve yakıt ya ERP'nin içinde ikincil bir modül olarak duruyor (Yapıtaşı, DİA+Mavvo) ya da telematik sağlayıcılara bırakılıyor [K-53][K-57][K-175]. Bu nedenle aşağıdaki tablo "ne kadar benziyor" sorusunu kümeler halinde yanıtlıyor.

| Ürün | Küme | Alpnex'e benzerlik | Neden karşılaştırılabilir | Kanıt düzeyi |
|---|---|---|---|---|
| Yapıtaşı ERP | Türk inşaat ERP | **Yüksek** | Ambar/depo transferi, satın alma onayı, makine-ekipman (günlük kullanım, yakıt, zimmet, kiralama) aynı üründe | [MKT] |
| DİA + Mavvo | Türk bulut ERP + inşaat çözümü | **Yüksek** | Şantiye deposu, makine envanteri, bakım takvimi, saat/yakıt analizi, mobil saha girişi | [MKT] |
| KonstraERP | Türk inşaat SaaS | Orta | Satın alma, onay merkezi, şantiye günlüğü, evrak; depo/makine yok | [MKT] |
| Uyumsoft şantiye programı | Türk inşaat | Orta | Şantiye deposu, satın alma, taşeron; makine/yakıt yok | [MKT] |
| Qera ERP | Türk inşaat | Orta | Şantiye satın alma, stok/WMS, maliyet; makine yok | [MKT] |
| Oska / AMP / e-HakedişNet | Türk hakediş programı | Düşük | Kamu ihalesi metraj-hakediş; operasyon yok | [MKT]/[SEC] |
| Trimble Viewpoint Vista | ABD inşaat ERP | **En yüksek (yapısal)** | Envanter + ekipman + satın alma + iş maliyeti + bordro tek ERP'de | [DOC] |
| Procore | ABD saha/proje platformu | Orta | Günlük kayıt, ekipman kaydı, izin şablonları, çevrimdışı mobil | [DOC] |
| CMiC, Spectrum, Foundation, Sage 300/Intacct | ABD inşaat muhasebe ERP | Orta | Talep onayı, ekipman maliyeti, raporlar | [DOC]/[SEC] |
| Dynamics 365 SCM | Genel ERP | Düşük-orta | Sayım (cycle count) deseni | [DOC] |
| HCSS Equipment360, B2W Maintain | Ağır ekipman bakım (CMMS) | Yüksek (bakım dilimi) | Sayaç bazlı periyodik bakım, iş emri, usta zamanı | [MKT]/[SNIP] |
| Tenna, Clue, EquipmentShare T3, Samsara | Telematik + bakım | Orta-yüksek | Sayaç içe aktarma, yakıt, kiralık, saha uygulaması | [MKT]/[DOC] |
| Fleetio | Genel filo bakım | Orta-yüksek | En ayrıntılı belgelenmiş sayaç ve yakıt kuralları | [DOC] |
| ToolWatch (AlignOps), ShareMyToolbox, Hilti ON!Track | Alet/zimmet | Yüksek (zimmet dilimi) | Çıkış/iade, transfer, talep onayı, sayım, sertifika | [MKT]/[DOC] |
| ERPNext, Odoo Community | Açık kaynak genel ERP | Orta (desen kaynağı) | Stok defteri, transit, satın alma zinciri, filo sözleşmesi, denetim | [SRC] |
| Snipe-IT, Atlas CMMS, Fleetbase, Dolibarr, Shelf.nu | Açık kaynak varlık/CMMS/filo | Orta (desen kaynağı) | Zimmet kabulü, sayaç tetikli bakım, yakıt, kontrol listesi | [SRC]/[MKT] |

Tablodan üç sonuç çıkıyor. Birincisi, Türkiye'de Alpnex'in doğrudan rakipleri **Yapıtaşı ve DİA+Mavvo**. Navappro'nun taraflı karşılaştırması da Yapıtaşı'nı iş makinesi kullanım takibiyle öne çıkarıyor [K-58]. İkincisi, yurtdışında yapısal örnek Vista'dır, ama Vista kurulu (on-premises) ya da barındırılan (hosted) bir ERP'dir ve saha verisini ayrı mobil uygulamalarla toplar [K-11][K-34]. Üçüncüsü, rakiplerin "çevrimdışı" dediği şey yalnız mobil formlardır. Procore bile çevrimdışıyken yalnız daha önce cihazda açılıp önbelleğe alınmış kayıtların görülebildiğini belgeliyor [K-41]. Alpnex'in tam yerel SQLite kopyasıyla çalışan masaüstü istemcisi bu açıdan rakiplerinden güçlü konumda.

---

## Ürün profilleri: modüller, ekranlar, alanlar ve akışlar

Her profil aynı sırayı izliyor: amaç, hedef kullanıcı, Alpnex'e benzerlik/fark, modüller, öne çıkan ekran/akışlar, teknik bulgular, örnek alınabilecekler, kaynaklar. Tüm erişim tarihleri 2026-10-12.

### Türkiye'deki inşaat ERP'leri hakedişte güçlü, makine ve depoda zayıf

**Yapıtaşı ERP**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | İnşaata özel uçtan uca ERP; orta-büyük müteahhitler [MKT] |
| Benzerlik / fark | Depo, satın alma ve makine-ekipman dilimleri Alpnex'le örtüşüyor. Alpnex'te olmayan hakediş, taşeron, işveren sözleşmesi ve tam muhasebe var. Çevrimdışı çalışma yalnız tek bir cümleyle ima ediliyor |
| Modüller | Lojistik ve Ambar/Depo (mal kabul, kritik stok, proje depoları arası transfer); Satınalma (talep, iş programına göre ihtiyaç planlama, teklif toplama/karşılaştırma, sipariş, onay akışı); Taşeron Yönetimi (sözleşme, avans, teminat, hakediş, kesinti, döviz bazlı sözleşme); İşveren Sözleşme ve Hakediş; Proje Personel Takibi (günlük puantaj, aktivite bazlı saat, fazla mesai); Makina Ekipman ve Demirbaş (günlük kullanım, tamir, yedek parça, amortisman, şantiyeler arası transfer, yakıt/yağ, zimmet durumu, kiralama, kullanım oranı); Muhasebe (mizan, bilanço, kur farkı) [MKT] |
| Öne çıkan akışlar | İrsaliye siparişe bağlanıyor; **fatura ↔ sipariş ↔ mal kabul üçlü eşleştirmesi**; BIM metrajı teklif modülüne aktarılıyor [MKT] |
| Teknik bulgular | IBM Collaboration Solutions platformu; "bağlantı geri geldiğinde veriyi senkronize eder" ifadesi var ama mekanizma anlatılmıyor; sayfada e-Fatura/e-İrsaliye geçmiyor [MKT] |
| Örnek alınabilecek | Üçlü eşleştirme, makine kullanım oranı raporu, döviz bazlı sözleşme |
| Kaynak | [K-53] |

**KonstraERP**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Bulut SaaS inşaat ERP'si; 1–200+ kullanıcılı firmalar [MKT] |
| Benzerlik / fark | Onay merkezi, sürümlü evrak, satın alma ve günlük şantiye kaydı ortak noktalar. **Depo, iş makinesi, yakıt ve çevrimdışı çalışma sayfada yok** (ayrı Hafriyat ürünü hariç) |
| Modüller (16) | Hakediş & İmalat; e-İhale & Sözleşme; Bütçe & Maliyet; Satınalma (tedarikçi performansı); Metraj & Keşif; Şantiye Günlüğü (mobil, foto, ekip puantajı); Personel & Bordro; Finans & Ticari (fatura, çek, banka); Onay Merkezi; Dokümanlar (sürümlü); Kalite Kontrol; İSG (eğitim, ramak kala, denetim); Satış Merkezi (daire/dükkan stoku); Mesajlaşma; Konstra Zeka (doğal dil sorgu, anomali); Kütüphane [MKT] |
| Öne çıkan akışlar | **Taşeron portalı** (taşeron kendi hakedişini, kesintisini, onay durumunu görür); Konstra Hafriyat: sefer takibi, kantar fişi, kamyoncu ödemesi ve **otomatik mazot kesintisi**; ÇŞB, İLBANK, DSİ ve KGM resmî birim fiyat kütüphanesi [MKT] |
| Teknik bulgular | Muhasebe için hazır konnektör yok; resmî defter Logo/Netsis/Mikro'da kalabilir; Excel şablonu ve **e-Fatura XML içe aktarma**; barındırma Frankfurt'ta. Fiyat: Starter 290$ + 18$/kullanıcı, Pro 690$ + 24$/kullanıcı, Business 1.490$ + 28$/kullanıcı (aylık, USD); 14 gün pilot [MKT] |
| Örnek alınabilecek | e-Fatura XML içe aktarma; "resmî muhasebe başka programda kalır" konumlaması; hafriyatta yakıt kesintisinin cariye yansıması |
| Kaynak | [K-54] |

**Uyumsoft "Şantiye ve İnşaat Takip Programı"**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Genel ERP üreticisinin inşaat paketi [MKT] |
| Benzerlik / fark | Şantiye deposu giriş/çıkışı ve talepten başlayan satın alma Alpnex'le ortak. Makine, yakıt, demirbaş ve mobil/çevrimdışı sayfada yok |
| Modüller | Depo, Satın Alma, Hakediş ve Taşeron, Taşeron puantajı ve ödemeleri, Alt Yüklenici, Şantiye Yönetimi (günlük operasyon, görev atama), Sözleşme, Maliyet, Müşteri (alıcı ödeme planı), İK (PDKS, avans, izin), **çoklu para birimi (USD/EUR/TL)**, bütçe-kâr analizi [MKT] |
| Teknik | e-Fatura/e-İrsaliye ayrı hizmet olarak sunuluyor; fiyat yok [MKT] |
| Kaynak | [K-55] |

**Qera ERP — Şantiye Bazlı Maliyet ve Hakediş**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Proje ve şantiye bazında maliyet ve kârlılık [MKT] |
| Benzerlik / fark | Şantiye satın alma (talep, sipariş, teslim) ve stok/WMS (giriş-çıkış, sarf) ortak. Puantaj, makine ve yakıt yok |
| Modüller / akış | Proje bütçesi, iş kalemleri, gerçekleşen maliyet; hakedişin imalat ilerlemesine bağlanması, kesinti, avans mahsubu; taşeron sözleşme/hakediş; proje ve şantiye bazında nakit akışı/kârlılık [MKT] |
| Örnek alınabilecek | Şantiye bazında **bütçe-gerçekleşen** görünümü (Alpnex'teki maliyet merkezleriyle doğal bağ) |
| Kaynak | [K-56] |

**DİA (bulut ERP) + Mavvo inşaat çözümü**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Bulut ERP üzerinde iş ortağı dikey çözümü [MKT] |
| Benzerlik / fark | Alpnex'e Türkiye'de en yakın kapsam: şantiye depo giriş/çıkış, sarf, **düşük stok uyarısı**, günlük şantiye raporu; **makine envanteri** (model, seri no, ömür), şantiye bazlı tahsis, **bakım takvimi ve hatırlatma**, arıza kaydı, yedek parça stoku, **saat/yakıt/verimlilik analizi**; mobil saha veri girişi. Fark: puantaj-bordro, taşeron hakedişi ve vardiya planı da var [MKT] |
| Uyarı | Kaynak bir iş ortağı blog yazısı (30.11.2024 tarihli, başlıkta 2026); "hatasız" gibi ifadeler pazarlama |
| Kaynak | [K-57] |

**Navappro, Logo, Mikro, Netsis ve Parsek (genel ERP + inşaat uyarlaması)**

Navapp'ın kendi blogundaki karşılaştırma tabloyu üç gruba ayırıyor: sektöre özel (Yapıtaşı, e-MOR, Canias, KonstraERP), genel ERP + inşaat uyarlaması (Logo, Netsis, Mikro, DİA) ve platform + dikey çözüm (Business Central + Navappro) [K-58]. Aynı kaynağa göre Logo'da e-Fatura, e-Arşiv, e-Defter ve e-İrsaliye yerleşik, inşaat derinliği ise özelleştirme ile geliyor. Mikro'nun giriş maliyeti düşük ama inşaat derinliği sınırlı. Hiçbir ürün için çevrimdışı/mobil ve teminat mektubu değerlendirilmemiş [K-58]. Bu kaynak taraflı olduğu için yalnız pazar haritası olarak kullanılmalı. Logo 2026/03 faaliyet raporunda 230.000'in üzerinde müşteri ve 1.000'in üzerinde iş ortağı bildiriyor, ama Tiger için ayrı bir inşaat/hakediş modülü tanımlamıyor [K-61]. Parsek ERP, şantiye ve merkez depolarının barkodla izlendiğini söylüyor [SNIP] [K-60]. Logo, Netsis, Mikro, ETA, Canias, IAS, Workcube ve Univera'nın resmî inşaat sayfaları bu çalışmada doğrulanamadı (doğrulanmadı).

**Oska HakedişPlus+, AMP ve e-HakedişNet (hakediş ve yaklaşık maliyet programları)**

Oska'nın ürünleri şunlar: masaüstü HakedişPlus+ (yaklaşık maliyet, hakediş, kesin hesap), web tabanlı OSKABULUT, mal/hizmet alımı hakedişi için HM-Hakediş, kamu idareleri için e-DoğrudanTemin ve e-kikNet (4734 sayılı Kamu İhale Kanunu) [K-59]. Bu programlar kamu ihalesine yönelik metraj, keşif ve hakediş hesaplıyor. Saha operasyonu yok ve Alpnex'le benzerlikleri düşük. Önemleri şurada: Türk müteahhidin hakedişi zaten bu tür uzman programlarda yaptığını gösteriyorlar. Alpnex'in bu alanı **taklit etmek yerine bu programlarla birlikte çalışması** daha akılcı.

**Küçük yerel ürünler ve puantaj.** Sitabula, ŞantiyeX ve Axoft yalnız uygulama mağazası ve indirme sitesi kayıtlarından biliniyor (doğrulanmadı). Türkçe arayüzlü, iş makinesi çalışma saati, yakıt ve kiralık makine puantajına özel **bağımsız bir Türk ürünü aramalarda bulunamadı**. Bu işlev ya ERP'lerin içinde ya da Teltonika gibi telematik sağlayıcılarında [K-53][K-175]. Jibble (yabancı ama Türkçe), şantiye personeli için yüz tanıma veya NFC kiosk ile internetsiz giriş-çıkış alıp sonra eşitliyor [MKT] [K-62].

### ABD inşaat ERP'leri malzeme ve ekipmanı ayrı belgelere bölüyor

**Trimble Viewpoint Vista (IN, EM, PO, JC modülleri)**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Ağır inşaat ve işi kendi yapan (self-perform) müteahhitler için ERP [DOC] |
| Benzerlik / fark | Alpnex'in **en yakın yapısal karşılığı**: envanter, ekipman, satın alma, iş maliyeti ve bordro tek ERP'de. Fark: Vista'nın tam genel muhasebesi (GL) var, çevrimdışı masaüstü yok, saha verisi ayrı uygulamadan geliyor |
| Ekipman (EM) | **Gelir (iç kira) birim fiyatları** kategori × gelir kodu bazında tanımlanıyor (saatlik, günlük, haftalık, aylık, bekleme/standby). "Override Rates" kutusuyla ekipman bazında geçersiz kılınabiliyor; kategori fiyatı sonradan değişse de ekipman bazındaki özel fiyat korunuyor. Projeye özel fiyat şablonları da var [DOC] [K-11][K-12][K-13]. Kullanım bordro puantajından, **EM Usage Posting** ya da **Automatic Usage** ile işleniyor: tutar ekipmana gelir olarak alacaklanıyor, projeye doğrudan maliyet olarak borçlanıyor. Her ekipmanda bir **Usage Cost Type** (iş maliyeti türü) zorunlu [DOC] [K-14]. Ekipmana aylık genel gider dağıtımı (Cost Allocations), amortismanın GL'ye işlenmesi, **bileşen takibi** (motor, şanzıman), ana makineyle birlikte taşınan **ataşmanlar** ve **EM Location Transfer** (proje, atölye, saha arası) belgelenmiş [DOC] [K-11] |
| Sayaç | **EM Meter Readings** okumaları ekipman kartını ve geçmiş dosyasını (EMMR) güncelliyor; değişmemiş okuma girilse bile tarih kaydediliyor. Sayaç değişimi ekipman kartına giriliyor. Şirket parametresi, yakıt ve parça işlemlerinin sayacı güncelleyip güncellemeyeceğini belirliyor [DOC] [K-11] |
| Bakım | **EM Standard Maintenance Groups** "son yapılan" sayaç değerini tutuyor; bakımı gelen kalemlerden iş emri otomatik ya da elle üretiliyor [DOC] [K-11] |
| Satın alma (PO) | Yalnız "Receiving" kutusu işaretli sipariş satırı mal kabul görebiliyor. Kabul, sipariş kaleminin **dağıtım satırına** (PO Item Distribution) giriliyor; **PO Initialize Receipts** ile toplu kabul yapılabiliyor; işlenmiş kabuller düzeltilebiliyor veya silinebiliyor [DOC] [K-34] |
| Envanter (IN) | IN Locations, **IN Transfer Entry** (malzeme her iki depoda da tanımlı olmalı), IN Adjustment Entry (fire, hasar, hatalı kayıt), **IN Physical Count Worksheet** [SNIP] [K-36]. 30 standart rapor: Material Orders, Restocking, Value of Inventory at Month End, **Negative Price/Cost**, Monthly Usage vs Production, **Allocated Inventory**, Bill of Materials, Inventory History Drilldown vb. [DOC] [K-35] |
| Örnek alınabilecek | İç kira birim fiyat tablosu, ataşman ve bileşen, sipariş satırı bazında kabul, ay sonu stok değeri ve negatif maliyet istisna raporları |
| Kaynak | [K-11]–[K-14], [K-34]–[K-36] |

**Procore**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Proje yönetimi ve saha platformu; ERP değil [DOC] |
| Benzerlik / fark | Günlük kayıt (Daily Log) ve ekipman kaydı Alpnex'in günlük faaliyet ve filo modülleriyle örtüşüyor. Muhasebe ve stok yok |
| Ekran/alanlar | Daily Log'un ekipman bölümündeki alanlar: firma, ekipman, **Hours Operating** (çalışma saati), **Hours Idle** (rölanti/boşta saati), maliyet kodu, konum, kontrol kutusu ve saati, yorum. Ekipman timesheet'iyle eşitlenmesi için zorunlu alanların eşleşmesi ve kaydın "Complete" işaretlenmemiş olması gerekiyor [DOC] [K-39] |
| Ekipman aracı | Şirket kaydında sahip olunan ve kiralanan ekipman tutuluyor; kiralananlar tedarikçiye bağlanıyor. Durumlar özelleştirilebiliyor (bakım, kira, giriş/çıkış, iade çağrısı). Projelere toplu atama ve "Onsite" bayrağı var. Ağustos 2025'te bakım kayıtları eklendi. **QR kod (beta)** ile tanıma ve projeye giriş yapılabiliyor; ayrıca **çöp kutusu (recycle bin)** var [DOC] [K-40]. Ekipman timesheet'i kişiye bağlanmadan da oluşturulabiliyor [DOC] [K-52] |
| Satın alma durumları | "Partially Received" bütçede bekleyen değişiklik olarak, "Approved" taahhüt edilmiş maliyet olarak görünüyor; "Closed" durumunu proje ekibi tanımlıyor [DOC] [K-38] |
| Bildirim | Observations: durum değişince ve yorum gelince atanan kişiye, oluşturana ve dağıtım listesine e-posta gidiyor. Oluşturulunca otomatik gönderim yok, kullanıcının "gönder" demesi gerekiyor. **Gecikme e-postası yalnız atanana**, yalnız eylem gerektiren durumlarda gidiyor [DOC] [K-42] |
| Yetki | Her araç için None / Read Only / Standard / Admin; şirket ve proje **izin şablonları**; bunların üzerine "ayrıntılı izinler" (granular permissions); dizin kaydında rol bazlı ayrıcalıklar (ör. Accounting Approver); yayımlanmış bir **İzin Matrisi** [DOC] [K-43] |
| Çevrimdışı | Çevrimdışı yapılan işler bağlantı gelince eşitleniyor; Android'de bir kayıt ancak daha önce görüntülenip önbelleğe alındıysa çevrimdışı açılabiliyor [DOC] [K-41] |
| Örnek alınabilecek | Çalışma/rölanti saat ayrımı, QR ile giriş, şantiye bazlı izin şablonu, otomatik üretilen izin matrisi, bildirim kuralları |
| Kaynak | [K-38]–[K-43], [K-52] |

**CMiC**

Talep (requisition) modülü projeye, ekipman bakımına veya şirket ihtiyacına yönelik stoklu, stok dışı ve serbest metinli kalem istenmesine izin veriyor. Onaylanan talep ya envanter modülünde **stoktan çıkışa** alınıyor ya da **doğrudan sipariş (PO) üretiyor** [SNIP] [K-33]. Kılavuzun içindekiler bölümüne göre kurulum tabloları şunlar: Requisition Roles, Rules of Approver Setup, Approval Levels, Approvers, Approval Groups, Group Approvers. Ayrıca ayrı bir **Requisition Unapproval** (onayı geri alma) işlevi ve **Requisition Approver Query** (onaycı sorgusu) ekranı var. Eşik değerleri ve vekâlet ayrıntısı okunamadı (doğrulanmadı) [K-33]. Daily Journal kaydının durumu EMPTY / UNPOSTED / POSTED olabiliyor [DOC] [K-47]; bir müşteri kılavuzuna göre tarih en fazla 7 gün geriye ya da ileriye alınabiliyor [SEC] [K-48].

**Spectrum, Foundation, Sage 300 CRE ve Sage Intacct Construction**

Spectrum'un ekipman rapor kataloğu şunları içeriyor: Equipment Revenue, **Equipment Unit Cost**, **Equipment Utilization**, **Fuel & Oil**, **Fuel Utilization**, E/C G/L Distribution ve **Warranty Expiration** [DOC] [K-49]. Foundation'da bordro puantajından gelen ekipman maliyeti hem ekipman modülünü hem iş maliyetini güncelliyor; kullanıcı tanımlı iş maliyeti fiyatları var. Bir bayi listesi "owner rentals" ve "denetim izli iç yansıtma" bilgisini ekliyor [SEC] [K-50][K-51]. Sage 300 CRE kullanıcıları üçüncü taraf sistemlerden gelen ekipman saatlerini **CSV ile içe aktarıyor** [SEC] [K-51]. Sage Intacct'ta satın alma onayı yapılandırılabiliyor: hangi belge türü (teklif, sipariş, fatura) onaya girecek, kim onaylayacak (Project Manager, Warehouse gibi onaycı tipleri), hangi sırayla ve tutara bağlı mı [DOC] [K-44]. İkincil bir kaynak iki onay modelini ayırıyor. **Değer bazlı** modelde belge doğrudan tutara uygun seviyeye gidiyor; **kademeli** modelde eşiğe kadar her seviyeden geçiyor [SEC] [K-45]. Saha ürünüyle ERP birlikte kullanıldığında onay akışının yalnız birinde açık olması gerekiyor [DOC] [K-46].

**Dynamics 365 Supply Chain Management (sayım deseni)**

Döngüsel sayım (cycle counting) üç adımlı: sayım işi oluştur → mobil cihazda say → farkları çöz. İş, bir toplama (pick) sonrası eldeki miktar **eşiğin altına düşünce** otomatik doğuyor; ayrıca bir sayım planından (madde başına azami sayım sayısı, sayımlar arası gün) ya da elle oluşturulabiliyor. Sayım sırasında stok bloke edilmiyor. Cihaz **beklenen miktarı asla göstermiyor** (kör sayım). Çalışanın izinli sapma yüzdesini veya miktarını aşan sayım **"Pending review"** kuyruğuna düşüyor; amir onaylamazsa sayım reddediliyor [DOC] [K-37].

### Ağır ekipman ve filo yazılımları sayaca dayalı bakımda derinleşiyor

**HCSS Equipment360**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Müteahhitler için ağır ekipman bakım sistemi [MKT] |
| Benzerlik / fark | Sayaç ve takvim bazlı bakım, iş emri, parça Alpnex'le ortak. Yakıt ayrı üründe (FuelerPlus); muhasebe ve stok ERP'de değil |
| Modüller/akış | Bakım, telematik veya elle girilen sayaçla (saat/km) ya da tarihle planlanıyor. Maliyet kodu, parça talebi, belge ve kontrol listesi bakıma otomatik ekleniyor. **Uyarılar üç tıkla iş emrine dönüşüyor.** Uyarı türleri: sertifika ve ruhsat, iş emri bitiş tarihi, saha talebi, periyodik bakım, yeniden sipariş seviyesi, personel yeterliliği, garanti. Ustalar tablette iş emri görüp foto ve not ekliyor. **Usta zaman kartları** iş emrine işleniyor; harcırah, yol ve ekipman ücreti gibi ek ödemeler bordroya aktarılıyor. Kontroller (inspections) iş emrini otomatik doğuruyor [MKT] |
| Kullanıcı görüşü | Capterra'da 19 yorumla 4,1/5; fiyat/değer puanı 2,8/5 [SEC] |
| Kaynak | [K-05], [K-06] |

**Trimble B2W Maintain ve B2W Track**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Ağır ekipman bakımı (Maintain) ve saha günlükleri (Track) [MKT]/[SNIP] |
| Öne çıkan akışlar | Talep oluşturulurken sayaç okunuyor. Sayaç kategorisinde üç ayar var: **"Exclude from field logs"**, **"Exclude from work orders"** ve **"Require a meter reading to complete a work order"** (iş emrini kapatmak için sayaç zorunlu) [SNIP] [K-08]. **Bakım şablonları** (firma veya üretici programı) birçok makineye toplu uygulanıyor. Sonraki bakım noktası iki moddan biriyle hesaplanıyor: **Planned Completion** önceki aralığın tetiklendiği andan, **Actual Completion** işin gerçekten yapıldığı andan sayıyor [SNIP] [K-07][K-09]. Bir makinenin tüm işleri tek iş emrinde toplanabiliyor; görevler sürükle-bırak ile atanıyor. Panolarda planlanmamış bakım, süresi geçmiş iş emri ve zaman kartı durumu gösteriliyor [SNIP] [K-07]. Track'te personel ve ekipman saha günlüğünde birbirine bağlanıyor, operatör saati bağlı makineye kopyalanabiliyor; sahadan gelen ekipman/tamir talebi planlama ve bakım ekranlarına anında düşüyor [SNIP] [K-10] |
| Teknik | AEMP uyumlu telematik verisiyle bakım tetikleniyor [SNIP] [K-07] |
| Kaynak | [K-07]–[K-10] |

**Tenna**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | İnşaat varlık yönetimi: takip, bakım, kontrol, alet [MKT] |
| Modüller | Varlık takibi (saha, harita, coğrafi sınır/geofence, kullanım); bakım (periyodik bakım, iş emri, parça, usta zaman kartı, maliyet kodu); kontrol ve uyum (özel kontrol formları, DVIR, IFTA, ELD); alet takibi (TennaQR, TennaBLE); OEM telematik/AEMP ve ERP entegrasyonları; Asset Financials, Schedule, Load Tracking [MKT] [K-01] |
| Akışlar | Mobil uygulamada kontrol formları foto, not ve imzayla dolduruluyor; QR okutunca konum, durum, kondisyon veya atama güncelleniyor; **"Site transfer and reservation module"** var; veri **çevrimdışı** alınıp sonra eşitleniyor; iş emri ve bakım talebi **sesli notla** gönderilebiliyor [MKT] [K-03]. Periyodik bakım alarmı otomatik bakım talebi açabiliyor [MKT] [K-02] |
| Teknik | Asset API **ISO/TS 15143-3 (AEMP)** standardına uyuyor. Alınan alanlar: kimlik, marka, model, seri, VIN/PIN, çalışma ve rölanti saati, enlem/boylam, yakıt seviyesi ve tüketimi, DEF seviyesi, arıza kodları. Ortaklar: Caterpillar, John Deere, Komatsu, Volvo, JCB, Liebherr, Bobcat, Sunbelt Rentals vb. Veri yenileme sıklığı belirtilmemiş [MKT] [K-04]. Vista'ya iş emri, parça ve işçilik maliyeti gönderiyor [MKT] [K-32] |
| Kaynak | [K-01]–[K-04], [K-32] |

**Clue**

Clue'nun modülleri şunlar: varlık takibi, geofence, OEM/GPS telematik, bakım (periyodik bakım, iş emri, kontrol, parça stoku), yakıt, kullanım, **sevk (dispatch)**, **kiralama yönetimi**, filo ekonomisi, zaman kartı uygulaması, İSG ve yapay zekâ [MKT] [K-19]. Yakıt tarafında veri GPS/OEM'den ya da mobil yakıt raporu şablonundan geliyor ve **mobil rapor çevrimdışı çalışıyor**. PIUSI B.Smart pompa verisi 3 saatte bir içe aktarılıyor ve **kaynak tank ile tank dolumunu** kaydediyor. Raporlar şunları gösteriyor: varlık, gün ve proje bazında hacim/birim fiyat/maliyet, saat başına yakıt maliyeti, rölanti yakıtı (galon, oran, maliyet). Raporlar günlük, haftalık veya aylık e-postayla gönderilebiliyor. Otomatik hırsızlık alarmı yok; yakıt motor saatiyle elle karşılaştırılıyor [MKT] [K-20]. Vista'ya GPS sayaç okumaları ve iş emri bilgisi gönderiyor [MKT] [K-21].

**Fleetio (genel filo bakımı; en ayrıntılı resmî belge)**

| Başlık | Bulgu |
|---|---|
| Amaç / hedef | Genel filo bakım SaaS'ı [DOC] |
| Sayaç modeli | Her araçta **birincil sayaç** ve isteğe bağlı **ikincil sayaç** var (ör. vincin üst/alt ünitesi, kamyon üstündeki jeneratör). Birim mil, km veya saat. Her okuma **Meter History**'de saklanıyor ve CSV'ye aktarılabiliyor; yönetici için **toplu sayaç girişi** var. Bir aydan uzun süre güncellenmeyen okuma "Date of Last Update" ile vurgulanıyor, eksik okuma kırmızı görünüyor [DOC] [K-15] |
| Doğrulama kuralları | Yeni okuma geçmişteki hiçbir değerden küçük olamaz. Geriye tarihli okuma önceki ve sonraki okumaların arasında kalmalı. Sıra dışı okuma silinmek yerine **"Mark as Void"** (geçersiz say) ile doğrulamasız kaydediliyor [DOC] [K-15] |
| İş emri | İş emrinde başlangıç tarihi/sayacı ve tamamlanma tarihi/sayacı var. Tamamlanma değeri otomatik oluşan servis kaydını besliyor ve bekleyen bakım hatırlatmalarını kapatıyor. Arada kullanım olduysa başlangıç sayacı sıra dışı kalıyor; sistem güncelleme ya da Void öneriyor. İş emri bakım hatırlatmasından ve arıza kaydından (Issue) açılabiliyor [SNIP] [K-16] |
| Yakıt | Yakıt kartı işlemleri (WEX, FLEETCOR, Comdata) günlük içe aktarılıyor, VIN ve plakayla eşleniyor; işlem başına tüketim ve km başı maliyet hesaplanıyor [SNIP] [K-18]. **Auto Void Fuel Entry**: sıra dışı ya da hesaplanan standart sapmayı aşan yakıt kaydı otomatik geçersiz sayılıyor, kullanıcı bunu geri alabiliyor [SNIP] [K-17] |
| Örnek alınabilecek | Sayaç bütünlüğü kuralları, ikincil sayaç, eski sayaç vurgusu, istatistiksel yakıt geçersizleme |
| Kaynak | [K-15]–[K-18] |

**EquipmentShare T3 ve Samsara**

T3, EquipmentShare donanımıyla beslenen bir bulut platformu. Karma ekipman ve araçların konumunu, sağlığını ve kullanımını gösteriyor. T3 Fleet uygulaması EquipmentShare'den ve **dış tedarikçilerden kiralanan** makineleri birlikte yönetiyor [SEC] [K-22]. Üreticinin bildirdiği pilot sonuçlar: altı şantiyede 7.700 varlık, 162 bakım müdahalesi, bunların %72'si 4 saatten kısa sürede çözülmüş [SEC] [K-23]. Samsara, Caterpillar VisionLink'ten "Caterpillar AEMP 2.0" ile konum, motor saati, yakıt seviyesi, kilometre ve arıza kodu çekiyor [DOC] [K-24]. Bakım planı "tarihe göre", "kilometreye göre" ya da "motor saatine göre" kuruluyor [DOC] [K-25].

### Zimmet yazılımları çıkış, transfer, talep ve sayımı derinleştiriyor

**ToolWatch (AlignOps altında)**

ToolWatch şantiye ve depolar arasında aletleri anlık stok güncellemesiyle taşıyor. Alet odalarında (tool crib) **self-servis** hızlı giriş/çıkış var. **Otomatik toplama fişleri (pick tickets)** depo personelinin malzemeyi çekip şantiyeye göndermesini sağlıyor; ekipler mobil uygulamadan talep ve iade yapıyor. Kit, toplama fişi ve satın alma siparişi için şablonlar var. Düşük stok **otomatik satın alma siparişi ve mal kabul fişi** tetikliyor; satın alma talepleri web ve mobilde onaylanıyor. Her transfer, talep ve iade için **denetim izi (audit trail)** tutuluyor. Aletler projeye kullanıma göre, sabit ücretle ya da min/maks ücretle fatura ediliyor. Entegrasyonlar: CMiC, Procore, Sage 300, Vista. Ürün sayfası çevrimdışından söz etmiyor [MKT] [K-26]. ToolWatch.com artık AlignOps'a yönleniyor; "Align (ShareMyToolbox)" ilişkisi doğrulanmadı.

**ShareMyToolbox**

Telefon kamerası barkod/QR okuyucu olarak çalışıyor; giriş, çıkış ve transfer okutarak yapılıyor. **Ödünç alma talebi** akışı şöyle: kullanıcı "Borrow"a basıyor, yönetici ya da aletin şu anki sahibi onaylıyor, talep eden bilgilendiriliyor. **Sepet tarzı toplu çıkış** ve okutarak ya da gözle **alet sayımı (tool audit)** var [SNIP]/[MKT] [K-27].

**Hilti ON!Track**

Alerts bölümü şu uyarıları topluyor: varlık, miktar, servis ve sertifika uyarıları, saha talepleri ve **transfer onayları**. Sarf malzemesi için **asgari ve sıfır miktar eşikleri** tanımlanabiliyor [DOC] [K-28]. Ağ geçidi (gateway) bir varlığı kayıtlı yerinden farklı bir yerde algılarsa sistem **transfer öneriyor**; tamamlanan transfer, önceki "beklenmeyen çıkış" uyarısını kapatıyor [DOC] [K-29]. Sabit ağ geçitleri yaklaşık 5 dakikada bir güncelleniyor [DOC]/[SNIP] [K-31]. Eğitim ve sertifika yenileme, bakım ve kalibrasyon için önceden uyarı veriyor; sertifika yönetimi Lite, Pro ve Enterprise paketlerinin hepsinde var [DOC]/[MKT] [K-30].

---

## Açık kaynak projeler kanıtlanmış veri modelleri sunuyor ama kodları alınamaz

### Depo, lisans, bakım durumu ve mimari

| Proje | Depo | Lisans | Bakım (2026-10-12) | Mimari | Alpnex için değer |
|---|---|---|---|---|---|
| ERPNext | github.com/frappe/erpnext | **GPL-3.0** (Frappe çatısı MIT) | Son push 2026-10-12; son "latest" v15.122.0 (2026-10-07); v16 hattı paralel [SRC] [K-70][K-71][K-72] | Python; her iş nesnesi bir DocType JSON tanımı + aynı klasörde `.py` denetleyici; RQ kuyrukları `short/default/long`, tekilleştirme için `deduplicate` + `job_id` [SRC] [K-75][K-93] | **Çok yüksek** (stok defteri, geriye tarih, iptal/düzelt, izin, sürüm geçmişi) |
| Odoo Community | github.com/odoo/odoo | **LGPL-3.0** | Varsayılan dal `20.0`, son push 2026-10-12 [SRC] [K-73][K-74] | Python ORM; `_inherit` ile mixin (ör. ekipmana `mail.thread`, `mail.activity.mixin`) [SRC] [K-104] | **Çok yüksek** (transit lokasyon, sayım, yeniden sipariş, filo sözleşmesi, chatter, cron) |
| Snipe-IT | github.com/grokability/snipe-it | **AGPL-3.0** | ~15,0 bin yıldız; v8.8.0 (2026-09-30) [SRC] [K-113] | PHP/Laravel; SoftDeletes; şirket kapsamı (`CompanyableScope`); Laravel zamanlayıcı [SRC] [K-114][K-116] | **Yüksek** (zimmet kabulü, sayım tarihi, eylem günlüğü) |
| Atlas CMMS | github.com/Grashjs/cmms | **AGPL-3.0 veya ticari** (8 Ocak 2026'dan itibaren, INTELLOOP LLC); kodda lisans/yetki kontrolü var [SRC] [K-120] | ~821 yıldız; v1.9.0 (2026-09-22) [SRC] [K-119] | Java Spring + TypeScript; PostgreSQL, MinIO, JWT, Docker Compose [MKT] [K-119] | **Yüksek** (sayaç tetikleyici, iş emri zinciri, duruş) |
| Fleetbase FleetOps | github.com/fleetbase/fleetbase | **AGPL-3.0 + ticari (FCL)** | ~4,3 bin yıldız; v0.7.68 (2026-10-05) [SRC] [K-128] | PHP; her modelde `company_uuid`; `LogsActivity` [SRC] [K-169] | **Yüksek** (tarih/km/motor saati tek bakım planı, yakıt, kontrol listesi) |
| Dolibarr | github.com/Dolibarr/dolibarr | **GPL-3.0** | ~7,7 bin yıldız; 24.0.1 (2026-09-07) [SRC] [K-133] | PHP modüler | Orta (depo bazında uyarı seviyesi) |
| Shelf.nu | github.com/Shelf-nu/shelf.nu | AGPL-3.0 (mobil uygulama PolyForm Internal Use) | ~3,0 bin yıldız; 2.3.0 (2026-09-30) [MKT] [K-136] | React Router 7, Prisma, **Supabase PostgreSQL**, pg-boss iş kuyruğu, **Fly.io** | Orta (QR ile işlem, rezervasyon, kit; Alpnex'e benzer altyapı) |
| openMAINT | Tecnoteca/SourceForge | Eski kaynaklarda AGPL; 2.4 için doğrulanmadı [K-137] | 2.4 (30 Nisan 2025): SLA, adım süresi, talep edenin son onayı [K-137] | Java, Ext JS, PostgreSQL, JasperReports, BIM/GIS [MKT] [K-138] | Düşük (karmaşıklık fazla) |
| iDempiere / ADempiere / Tryton | GitHub | API'de NOASSERTION / GPL-2.0 / doğrulanmadı | Aktif push; ADempiere son sürüm 2023 [K-172][K-173][K-174] | Java / Python | Düşük (yalnız metaveri incelendi) |
| InvenTree | github.com/inventree/InvenTree | **MIT** | v1.5.7 (2026-10-10) [K-141] | Python | İncelenmedi (doğrulanmadı) |

**Avalonia'da olgun bir açık kaynak ERP yok.** "avalonia erp", "avalonia inventory", "avalonia pos" aramaları yalnız 0–2 yıldızlı hobi depoları getirdi [K-139]. Bakımı süren tek kayda değer Avalonia veri uygulaması Gnomeshade (kişisel finans, 35 yıldız, AGPL) [K-140]. Sonuç: Alpnex'in Avalonia + SQLite çevrimdışı + PostgreSQL eşitleme mimarisi açık kaynakta fiilen eşsiz. Açık kaynaktan öğrenilecek olan arayüz yığını değil, **alan (domain) modelleri**.

### ERPNext: hareket defteri Alpnex'in ilkesiyle bire bir örtüşüyor

ERPNext'te stok bakiyesi doğrudan değiştirilmiyor. Stoku etkileyen her belge **Stock Ledger Entry (SLE)** satırları üretiyor. Her satır işlem anındaki şu değerleri taşıyor: `qty_after_transaction` (işlem sonrası miktar), `valuation_rate`, `stock_value`, `stock_value_difference` ve FIFO kuyruğu (`stock_queue`). Kaynak belgeye `voucher_type` + `voucher_no` ile genel bir bağ kuruluyor [SRC] [K-76]. İptal edilen belgenin SLE'leri silinmiyor, `is_cancelled=1` olarak işaretleniyor. Bekleyen yeniden değerleme "In Progress" durumundaysa iptal engelleniyor [SRC] [K-77]. Alpnex ters kayıt kullanıyor. Ters kayıt yalnız ekleme yapan (append-only) bir yöntem olduğu için çevrimdışı eşitlemede daha güvenli; bu farkı değiştirmeye gerek yok.

En değerli desen **geriye tarihli kayıt sonrası yeniden hesaplama** (Repost Item Valuation). Geriye tarihli bir stok kaydı otomatik olarak bir repost belgesi açıyor. Belgenin durumu Queued / In Progress / Completed / Skipped / Failed / Cancelled olabiliyor; ayrıca hata günlüğü (`error_log`), ilerleme (`current_index`) ve ara durum dosyası tutuluyor [SRC] [K-79]. İş "long" kuyruğunda çalışıyor. Benzer işler tekilleştiriliyor. Zaman aşımı, kilitlenme (deadlock) veya sorgu zaman aşımı gibi **kurtarılabilir** hatalarda iş yeniden kuyruğa giriyor; diğer hatalarda "Failed" olup stok yöneticilerine bildirim gidiyor. Kapalı dönem kontrolü de yapılıyor [SRC] [K-79]. Zamanlayıcı repost işini saatlik, paralel repost'u 15 dakikada bir çalıştırıyor [SRC] [K-92]. Kullanıcı belgesi, geriye tarihli işlemde repost'un otomatik oluştuğunu ve "Start Reposting" ile elle de başlatılabildiğini doğruluyor [DOC] [K-80]. Alpnex'te çevrimdışı masaüstünden geç eşitlenen kayıtlar doğal olarak "geriye tarihli" düşer. Bu yüzden, hareket sonrası bakiye veya maliyet saklanıyorsa, durumlu bir yeniden hesaplama işi doğruluk açısından kritik.

Stok ayarlarındaki iki alan ucuz ama güçlü bir kontrol sağlıyor: `stock_frozen_upto` (stok dondurma tarihi) ve `role_allowed_to_create_edit_back_dated_transactions` (geriye tarihli kayıt girebilecek rol). Aynı ayarlarda `allow_negative_stock`, `auto_indent` (otomatik malzeme talebi) ve `enable_stock_reservation` (stok rezervasyonu) da var [SRC] [K-78]. Stock Entry'de `add_to_transit`, `outgoing_stock_entry` ve `per_transferred` alanları **iki adımlı transferi** taşıyor; `additional_costs` nakliye gibi ek maliyetleri giriş değerine ekliyor [SRC] [K-75]. Material Request'in türü (Purchase, Material Transfer, Material Issue, **Customer Provided** vb.) sonraki belgeyi belirliyor. Durumlar `per_ordered` / `per_received` yüzdeleriyle "Partially Ordered" ve "Partially Received" olarak kendiliğinden güncelleniyor. "Stopped" durumu ve yeniden siparişten doğduğunu gösteren `auto_created_via_reorder` bayrağı da var [SRC] [K-82]. "Customer Provided" türü, inşaattaki **idare malzemesi** (işverenin verdiği malzeme) kavramına benziyor. Yeniden sipariş işi günlük çalışıyor ve depo bazında yeniden sipariş seviyesine ve öngörülen miktara göre malzeme talebi açıyor [SRC] [K-81].

Varlık tarafında **Asset Repair** alanları şunlar: arıza tarihi, tamamlanma tarihi, **duruş süresi (downtime)**, yapılan işlemler, stoktan tüketilen parçalar, dış faturalar, toplam onarım maliyeti ve "maliyeti varlığa ekle" seçeneği [SRC] [K-84]. Bakım görevi yalnız **takvim periyodu** (günlük … 3 yıllık) ile çalışıyor; tür olarak "Preventive Maintenance" ve "Calibration" var, sertifika istenebiliyor [SRC] [K-83]. Sayaç veya motor saatine bağlı bakım yok. Vehicle kaydında sigorta şirketi, poliçe no ve poliçe başlangıç/bitiş tarihleri var [SRC] [K-85]. Vehicle Log (HRMS) ise basit: kilometre, yakıt miktarı, fiyat, tedarikçi [SRC] [K-111].

Frappe çekirdeğinde dört desen öne çıkıyor. İlki belge yaşam döngüsü: Taslak (0) → Onaylı (1) → İptal (2). Geçersiz geçişler engelleniyor. İptal edilmiş belgeden **amend** ile düzeltilmiş kopya üretiliyor ve ekler yeni sürüme kopyalanıyor [SRC] [K-86]. İkincisi iyimser kilitleme (optimistic locking): `check_if_latest` formun açıldığı andaki `modified` değeri değişmişse "belge siz açtıktan sonra değiştirildi, yenileyin" hatası veriyor [SRC] [K-86]. Üçüncüsü **Version**: alan ve alt tablo satırı farklarını otomatik kaydediyor, başka kullanıcı adına yapılan işlemi de (impersonator) not ediyor [SRC] [K-87]. Dördüncüsü üç katmanlı yetki. Rol izni satırı (DocPerm) ayrı ayrı haklar içeriyor: okuma, yazma, oluşturma, silme, **onaylama (submit), iptal, düzeltme (amend)**, rapor, **dışa aktarma, içe aktarma**, paylaşma, **yazdırma**, e-posta, **maske**. Bunların yanında `if_owner` (yalnız kendi kaydı) ve `permlevel` (alan grubu) var [SRC] [K-88]. User Permission ise kullanıcıyı belirli kayıt değerleriyle (ör. yalnız bir şantiye) sınırlıyor ve `is_default` ile o değeri formlarda varsayılan yapıyor [SRC] [K-89]. Bunlara ek olarak **Deleted Document** silinen belgenin tam JSON içeriğini saklıyor ve geri yüklemeye izin veriyor [SRC] [K-90]. **Auto Repeat** tekrarlayan belge üretiyor [SRC] [K-91]. Accounting Dimension ise maliyet merkezine ek, kullanıcı tanımlı bir analiz boyutu (ör. "Şantiye") ekliyor [SRC] [K-110].

### Odoo: transit lokasyon, sayım ve filo sözleşmesi en net modeller

Odoo stoku **lokasyonlar arası hareket** olarak modelliyor. Tedarikçi, müşteri, **envanter kaybı**, üretim ve **transit** de birer sanal lokasyon [SRC] [K-94]. Aynı şirketin depoları arasında `internal_transit_location` kullanılıyor; bir depo başka bir depodan beslenecek şekilde ayarlanabiliyor (`resupply_wh_ids`) [SRC] [K-96]. Sayım, stok satırındaki "Counted" alanına girilen değerle yapılıyor. Fark otomatik hesaplanıyor (fazla yeşil, eksik kırmızı), "Apply" ile fark hareketi üretiliyor ve lokasyon bazında **planlı sayım tarihi** (`inventory_date`) tutuluyor [SRC] [K-95] [DOC] [K-112]. Yeniden sipariş kuralı (`stock.warehouse.orderpoint`) şu alanları taşıyor: otomatik ya da manuel tetik, min/maks miktar, hesaplanan ama elle düzeltilebilen sipariş miktarı ve **`snoozed_until` (erteleme)** [SRC] [K-97].

Satın almada PO durumları şunlar: draft (teklif isteği/RFQ), sent, **to approve** (tutar eşikli ikinci onay), purchase, cancel. Ayrıca bir `locked` bayrağı var: **kilitli sipariş değiştirilemiyor**, önce kilidin açılması gerekiyor [SRC] [K-98]. Satırda teslim alınan, faturalanan ve faturalanacak miktarlar tutuluyor. Faturalanacak miktar, kontrol politikasına göre sipariş miktarından ya da teslim alınan miktardan hesaplanıyor [SRC] [K-99]. Belgeye göre **üçlü eşleştirme (3-way matching)**, ürünlerin bir kısmı ya da tamamı teslim alınmadan tedarikçi faturasının ödenmemesini sağlıyor; faturada "Should Be Paid" alanı var [DOC] [K-100].

Filo (Fleet) modülünde **sözleşme** modeli şunları içeriyor: sözleşme türü, tutar, başlangıç ve bitiş tarihi, kalan gün, tedarikçi, **tekrarlayan maliyet ve sıklığı** (günlük/haftalık/aylık/yıllık), dahil hizmetler ve durum (New/Running/Expired/Closed) [SRC] [K-101]. Varsayılan sözleşme türleri "Omnium" ve "Leasing". Zamanlanmış bir eylem sözleşme maliyetlerini sıklığa göre üretiyor; bitişten varsayılan **30 gün önce** uyarı veriyor [SRC] [K-101][K-103]. Araç kaydında şu alanlar var: `future_driver_id` ve `next_assignation_date` (planlı atama), kilometre değerini ayrı log tablosundan hesaplayan `odometer` alanı, araç değeri ve kalıntı değeri [SRC] [K-102]. 20.0 sürümünde ayrı bir yakıt kaydı modeli bulunmuyor [SRC] [K-105]. Bakım (Maintenance) modülünde talep türü düzeltici ya da önleyici olabiliyor. Talepler kanban aşamalarıyla ilerliyor. Tekrar "her N gün/hafta/ay/yıl, süresiz ya da bir tarihe kadar" kuralıyla tanımlanıyor; yani **takvim bazlı**. Ekipmanda **MTBF** (arızalar arası ortalama süre), **MTTR** (ortalama onarım süresi) ve tahmini sonraki arıza tarihi hesaplanıyor [SRC] [K-104] [DOC] [K-171]. Onarım (Repair) emrinde garanti bayrağı, stoktan parça hareketi ve **"parça hazır mı" durumu** var [SRC] [K-176].

Çekirdekte **chatter** öne çıkıyor. `mail.thread` alan değişikliklerini izliyor, yorum, takipçi ve ek ekliyor. `mail.activity.mixin` ise son tarihli **aktiviteler** (gecikmiş / bugün / planlı) sunuyor ve "benim aktivitelerim" listesi aranabiliyor [SRC] [K-106][K-107]. Güvenlik belgesine göre **global kurallar kesişir** (erişimi daraltır), **grup kuralları birleşir** (erişimi genişletir ama global sınırı aşamaz) [DOC] [K-108]. Zamanlayıcı (`ir.cron`) ardışık hata sayısını (`failure_count`) tutuyor ve başarıda sıfırlıyor; uzun süre üst üste başarısız olan işi **otomatik devre dışı bırakıyor**; uzun işler ilerleme bildiriyor [SRC] [K-109].

**İki açık kaynak ERP'de de sayaç (km/motor saati) bazlı periyodik bakım temel sürümde görülmedi ve yakıt kaydı basit.** Alpnex'in sayaç okumalı günlük faaliyetle çalışan periyodik bakımı ve yakıt sapma analizi bu iki projeye göre **farklılaştırıcı** [SRC] [K-83][K-104][K-105].

### Snipe-IT, Atlas CMMS ve Fleetbase: zimmet, iş emri ve bakım planı için hazır şablonlar

**Snipe-IT (zimmet ve sayım).** Zimmet (check-out) fonksiyonu beklenen iade tarihi ve "yerinde imza" bayrağı alıyor. Varlık bir kişiye, bir lokasyona ya da **başka bir varlığa** (ör. alet → araç) atanabiliyor [SRC] [K-114]. **CheckoutAcceptance** kaydı şu alanları tutuyor: kabul tarihi, ret tarihi, imza dosyası, not ve donmuş sözleşme metni (EULA) PDF'i. Kabul ve ret yalnız "her iki tarih de boşsa" koşuluyla kesinleşiyor; böylece iki kişinin aynı anda kabul etmesi gibi yarış durumları önleniyor. İmzalı PDF de üretiliyor [SRC] [K-115]. Sayım için her varlıkta `next_audit_date` tutuluyor. "Sayımı yaklaşan" (bugün + uyarı günü içinde) ve "sayımı gecikmiş" listeleri ayrıca "iadesi gecikmiş" listesi var [SRC] [K-114]. Günlük zamanlayıcı stok, süre bitişi, beklenen iade ve yaklaşan sayım uyarılarını çalıştırıyor, haftalık yedek alıyor [SRC] [K-116]. Sarf malzemesinde `min_amt` (asgari miktar) var [SRC] [K-118]. Varlık kodu yalnız **silinmemiş kayıtlar arasında** benzersiz olmak zorunda; geçmiş ekranları silinmiş kişi ve lokasyonları da çözüyor [SRC] [K-114]. Tek bir çok biçimli (polymorphic) **Actionlog** tablosu tüm geçmişi tutuyor: hedef, eylem türü, alan farkı (`log_meta`), IP, tarayıcı ve **işlemin kaynağı (`action_source`)** [SRC] [K-117].

**Atlas CMMS (iş emri zinciri).** WorkOrder, PreventiveMaintenance, Request ve WorkOrderMeterTrigger ortak bir **WorkOrderBase** şablonunu paylaşıyor. Şablonda bitiş tarihi, öncelik, tahmini süre, imza gerekliliği, ekip, atananlar ve varlık var [SRC] [K-121]. İş emri tamamlayanı, tamamlanma zamanını, **imzayı**, geri bildirimi, **ilk müdahale süresini** (`firstTimeToReact`) ve kaynağını (talep ya da periyodik bakım) tutuyor [SRC] [K-122]. Bakım talebinde iptal nedeni, **sesli not** (`audioDescription`) ve herkese açık bir talep portalı var [SRC] [K-123]. Sayaç modeli şu parçalardan oluşuyor: sayaç (birim, okuma sıklığı, okuyacak kullanıcılar), **okuma geçmişi** (zaman ve yazar bilgisiyle) ve **sayaç tetikleyici** (koşul, değer, bekleme) [SRC] [K-125]. Takvim planında `recurrenceBasedOn` alanı var; bu alan Fleetio'daki planlı/gerçekleşen ayrımına benziyor [SRC] [K-124]. Duruş kaydı (başlangıç + süre) varlık bazında duruş ve maliyet analizlerini besliyor [SRC] [K-126]. Parçada asgari miktar, barkod ve **`version` (iyimser kilit)** alanları var [SRC] [K-127].

**Fleetbase FleetOps (bakım planı, yakıt, kontrol).** **MaintenanceSchedule** tek bir kayıtta tarih, mesafe ve **motor saati** aralıklarını birlikte tutuyor. Son bakım değerlerini, önceden hesaplanmış **sonraki bakım tarihini, km'sini ve motor saatini**, **hatırlatma ofsetleri** dizisini ve varsayılan atanan/talimatları da saklıyor [SRC] [K-129]. Yapılan bakım kaydında bakım anındaki km ve motor saati, işçilik/parça/vergi/toplam maliyet ve ekler var [SRC] [K-130]. Yakıt kaydının alanları: sürücü, araç, kaydeden, km, konum, tutar, hacim, birim, durum. Yakıt kartı içe aktarımı için ayrı bağlantı/çalıştırma/işlem modelleri var [SRC] [K-131]. **InspectionSubmission** (kontrol listesi sonucu) toplam ve başarısız madde sayısını, imzayı, km/motor saatini tutuyor ve **arıza (Issue) ile iş emrine bağlanıyor** [SRC] [K-132].

**Dolibarr ve Shelf.nu.** Dolibarr'da uyarı eşiği (`seuil_stock_alerte`) ve istenen stok (`desiredstock`) **ürün × depo** bazında ayrıca tutulabiliyor [SRC] [K-134]. Çekirdek Asset modülü amortisman odaklı; saha ekipmanı özellikleri ücretli üçüncü taraf eklentilerden geliyor [K-135]. Shelf.nu'nun README'si şu özellikleri sayıyor: QR okutarak görüntüleme, zimmet ve arıza bildirimi; **çifte rezervasyonu engelleyen** rezervasyon; kit; toplu tarayıcı; bakım, kalibrasyon ve garanti hatırlatmaları [MKT] [K-136].

---

## Rakiplerdeki ortak akışlar beş zincirde toplanıyor

Profiller yan yana konunca, ürünlerden bağımsız tekrar eden beş iş akışı zinciri ortaya çıkıyor. Alpnex'in ekranları sonraki aşamada bu zincirlerle karşılaştırılabilir.

**Sayaç → bakım → iş emri zinciri.** En olgun ürünlerde sayaç kendi geçmiş tablosunda tutuluyor ve kartta yalnız son değer görünüyor (Odoo kilometre logu, Vista EMMR, Atlas Reading) [K-102][K-11][K-125]. Okumalar katı kurallarla korunuyor: geriye gitmeme, geriye tarihli okumanın sıraya oturması, silme yerine geçersiz sayma, eski sayacın vurgulanması [K-15]. Bakım planı tarih, km ve saati tek kayıtta birleştiriyor ve sonraki bakım değerlerini önceden hesaplıyor [K-129]. Uyarı ya da saha arıza bildirimi birkaç tıkla iş emrine dönüşüyor [K-05][K-02]. İş emri sayaç girilmeden kapatılamıyor [K-08]. Kapanış servis kaydını oluşturup hatırlatmayı sıfırlıyor [K-16]. Bu zincirin ucunda duruş süresi ve MTBF/MTTR raporları var [K-84][K-104][K-126].

**Ekipman kullanımı → proje maliyeti zinciri.** Vista, Foundation ve Procore aynı fikri farklı derinlikte uyguluyor: makine saatleri bir **iç kira birim fiyatıyla** projeye maliyet olarak yansıyor, ekipman de gelir kazanıyor [K-11][K-14][K-50][K-39]. Procore'un çalışma/rölanti ayrımı ayrı bekleme fiyatını mümkün kılıyor [K-39]. Spectrum'un raporları da bu zincirin çıktısı: kullanım oranı, birim maliyet, yakıt ve garanti [K-49]. Türkiye'de Yapıtaşı "kullanım oranı" raporuyla bu yöne işaret ediyor [K-53].

**Talep → sipariş → kabul → fatura zinciri.** CMiC'te talep, stoktan karşılama ile satın alma arasında dallanıyor [K-33]. ERPNext'te zincir boyunca yüzde bazlı durumlar kendiliğinden güncelleniyor [K-82]. Vista'da kabul sipariş satırı düzeyinde yapılıyor ve toplu kabul mümkün [K-34]. Odoo'da faturalanabilir miktar teslim alınanla sınırlanıyor ve onaylı sipariş kilitleniyor [K-98][K-99][K-100]. Yapıtaşı aynı üçlü eşleştirmeyi Türk pazarında sunuyor [K-53].

**Depo → şantiye malzeme ve zimmet zinciri.** Transfer iki adımlı: gönderilen mal teslim alınana kadar "yolda" görünüyor [K-94][K-75]. Sayım kör yapılıyor, toleransı aşan fark amir onayına gidiyor [K-37]. Sayım farkı ve fire ayrı bir "kayıp" lokasyonuna yazılıyor [K-94]. Zimmette çıkış imzalı kabulle kesinleşiyor, iade tarihi ve periyodik sayım takip ediliyor [K-115][K-114]. Talep ve ödünç alma onaya bağlı [K-27]. Düşük stok satın almayı tetikliyor [K-26].

**Kayıt etrafındaki iş birliği zinciri.** Odoo'nun chatter'ı, Frappe'nin Version'ı ve Snipe-IT'nin Actionlog'u aynı ihtiyaca cevap veriyor: her kaydın altında kim neyi ne zaman değiştirdi, kim ne yorum yaptı, hangi ek var, sıradaki iş ne ve ne zaman [K-106][K-107][K-87][K-117]. Bildirim kuralları gürültüyü azaltacak şekilde tasarlanıyor; gecikme uyarısı örneğin yalnız atanana gidiyor [K-42]. Onay akışında tutar eşikli seviyeler, onaycı grupları ve onayı geri alma belgelenmiş [K-33][K-44].

**Arka plan teknik süreçleri.** Rakiplerdeki baskın arka plan süreci **telematik sayaç içe aktarımı**. OEM beslemeleri ya da ISO 15143-3 (AEMP) Asset API saat, konum, yakıt ve arıza kodlarını taşıyor ve bakımı otomatik tetikliyor [K-04][K-24]. Hiçbir üretici yoklama sıklığını ya da telematik okumayı elle girilen okumayla nasıl tekilleştirdiğini belgelemiyor. Diğer arka plan süreçleri şunlar: günlük yakıt kartı içe aktarımı [K-18], ağ geçidiyle otomatik konum ve transfer önerisi [K-29], ERP'ye maliyet eşitlemesi [K-32] ve günlük uyarı işleri [K-116][K-91]. Açık kaynak tarafında zamanlayıcıların olgunluk işaretleri şunlar: tekilleştirme [K-93], hata sayacı ve otomatik devre dışı bırakma [K-109], kurtarılabilir hata ile kalıcı hatanın ayrılması [K-79].

---

## Çevrimdışı eşitlemede kanıtlanmış desen Alpnex'inkiyle örtüşüyor, riskler ayrıntıda

### Olgun sistemlerin ortak deseni beş parçadan oluşuyor

Birinci parça, istemcide **sıralı ve kalıcı bir değişiklik kuyruğu** (outbox). PowerSync istemci yazmalarını "sıralı bir mutasyon günlüğünde" tutuyor ve backend'e sırayla boşaltıyor. Bir işlem ancak backend başarılı yanıt verince kuyruktan siliniyor; bu yüzden backend aynı işlemi birden fazla kez alabiliyor. İşlemler **tekrar korumalı (idempotent)** olmak zorunda ve her biri istemci başına artan bir işlem numarası taşıyor [K-142][K-143]. İkinci parça, sunucunun her istemci için "son uygulanan işlem no"yu tutması. Replicache bu sayede her değişikliği sunucuda **tam bir kez** uygulamayı hedefliyor. Sunucu yetkili; istemcinin sonucu yalnız tahmin [K-144]. Üçüncü parça, **sunucunun verdiği opak imleç** (cursor/checkpoint) ile delta çekme. Replicache'te bu bir "cookie". CouchDB'de artan bir sıra numarası; checkpoint her toplu işlemden (batch) sonra iki tarafa da yazılıyor ve hata sonrası "son başarılı noktadan" devam ediliyor [K-144][K-147]. Dördüncü parça, **silmelerin işaret olarak taşınması**. CouchDB değişiklik akışında `deleted:true` gönderiyor; Dotmim.Sync "uzakta silinmiş, yerelde var" gibi silmeye özgü çakışma türlerini ayrıca tanımlıyor [K-147][K-151]. Beşinci parça, **kalıcı hata ile geçici hatanın ayrılması**. CouchDB'ye göre zaman aşımları yeniden denenmeli, ama 401, 403, 409 ve 412 yanıtları tekrarlanmamalı [K-147]. AWS Builders' Library de şunları öneriyor: yalnız idempotent çağrıları yeniden deneme, üstel bekleme (backoff) ile birlikte **rastgele sapma (jitter)**, sınırlı deneme sayısı ve yeniden denemenin tek katmanda yapılması (3 deneme × 5 katman = 243 kat yük) [K-155].

Alpnex'in işlem kimliği + transaction + idempotency + hareket defteri tasarımı bu desenin alana özgü bir uygulaması. CRDT gerekmiyor. Ink & Switch'in kendisi de aynı alana yapılan eşzamanlı yazmayı CRDT'nin otomatik çözemediğini ve deneysel CRDT araçlarını üretimde kanıtlanmış ürünlerin yerine henüz önermediğini yazıyor [K-154]. Stok ≥ 0 gibi kuralların sunucu doğrulaması gerektirdiği ise bir çıkarım.

### Dört somut risk kodda denetlenmeli

**1. İmleç zaman damgasına dayanıyorsa satır kaçabilir.** Datasync Toolkit'in delta token'ı en büyük `UpdatedAt` değeri, ve SQLite'ın `DateTimeOffset`'i varsayılanda saniye hassasiyetinde sakladığı uyarısı var [K-145]. PowerSync saat kaymasının zaman damgasını güvenilmez yapabileceğini, kritik veri için sıra numarası kullanılmasını yazıyor [K-143]. PostgreSQL sıra numaraları (sequence) işlemsel değil: iptal edilen işlemde değer geri alınmıyor ve boşluk kalıyor [DOC] [K-148]. Topluluk kaynakları şu tuzağı anlatıyor: uzun süren bir transaction'ın satırları başlangıç zamanıyla damgalanıyor, imleç geçtikten sonra görünür oluyor ve **"bu satırlar hiç eşitlenmiyor"**. Önerilen çözümler imleci en eski açık transaction'ın gerisinde tutmak ya da bir örtüşme penceresiyle idempotent upsert yapmak. Fivetran'ın bağlayıcıları da uzun transaction bitene kadar bekliyor [SEC] [K-149][K-150]. Alpnex'teki 60 saniyelik güvenlik turu kaçan satırı ancak imleç geride bırakılıyorsa yakalar.

**2. Başarısız işlem kuyruğu kilitleyebilir.** Belgelenmiş sistemler kalıcı hatayı (doğrulama, çakışma) "başarısız/reddedildi" olarak ayırıyor ve kullanıcıya gösteriyor; geçici hatayı (ağ, 5xx, zaman aşımı) bekleme + jitter ile tekrarlıyor [K-147][K-155]. Datasync başarısız işlemleri "failed" işaretleyip sunucu yanıtını saklıyor. Paralel gönderim açıkken işlemlerin sunucuda **sırasız** uygulanabileceğini de uyarıyor [K-145]. Bu nedenle aynı stok kalemine ya da aynı kayda ait işlemler sıralı gönderilmeli.

**3. Reddedilen hareketin kullanıcıya görünmesi.** PowerSync stokta miktarın **delta (hareket)** olarak gönderilmesini ve stoku negatife düşürecek değişikliğin `insufficient_stock` gibi bir nedenle reddedilmesini öneriyor. Reddi görünür kılmak için iki desen veriyor: "Benim sürümüm / Sunucu sürümü" seçimli çakışma tablosu ve `pending/applied/failed` durumlu değişiklik günlüğü (hata mesajı + yeniden dene düğmesi) [K-143]. ElectricSQL, reddedilen yazmada tüm yerel durumu silmenin naif olduğunu; yalnız **nedensel olarak bağlı** yazmaların geri alınması ya da işaretlenmesi gerektiğini söylüyor [K-153]. Örnek: aynı malın sonraki çıkışı ya da transferin karşı ayağı. Dynamics 365 Field Service çakışmayı alan düzeyinde değil **tablo (kayıt) düzeyinde** algılıyor. Varsayılan ayarda çevrimdışı cihaz kazanıyor ve dispeçerin değişikliğini eziyor; yöneticiler geçmiş hataları "Sync Errors" ekranında görüyor [DOC] [K-152]. Bu, Alpnex'in "operasyonel kayıtta LWW yasak" kuralının neden doğru olduğunu gösteren somut bir karşı örnek.

**4. Yetki veya şube kapsamı değişince yerel veri temizlenmeli.** D365 Field Service, eşitleme sonrası çevrimdışı profil filtresine artık uymayan kayıtları cihazdan kaldırıyor; kullanıcı böyle bir kaydı açarsa "Record Not Found" görüyor [DOC] [K-152]. Alpnex'te firma/şube filtresi bu profilin karşılığı. Kullanıcının şube yetkisi daraldığında eski verinin yerel SQLite'ta kalıp kalmadığı **güvenlik açısından** denetlenmeli.

### Büyük ilk eşitleme sayfa sayfa ve kaldığı yerden devam etmeli

CouchDB değişiklikleri küçük parçalar halinde okuyup toplu gönderiyor; checkpoint'i yalnız toplu commit'ten sonra yazıyor [K-147]. Datasync sayfaları sırayla istiyor ve her sayfadan sonra delta token'ı kalıcı olarak güncelliyor. Yeniden eşitleme için bekleyen işlemleri ve token'ları silip baştan çekmeyi öneriyor [K-145]. D365 ilk eşitlemede ve uzun aradan sonraki büyük eşitlemede kullanıcıya bildirim gösteriyor. "Offline Status" sayfası tabloların son güncelleme zamanını, cihaz durumu sayfası son eşitlemenin ayrıntısını gösteriyor [K-152]. Tablo başına 200–300 bin satır için bunun anlamı şu: sayfa sayfa çekme, her sayfadan sonra imleci kalıcı yazma ve ilerleme göstergesi. Yarıda kopan bir kurulum baştan başlamamalı. MongoDB Atlas Device Sync'in Eylül 2024'te kullanımdan kaldırılıp 30 Eylül 2025'te kapanması [K-156], kritik eşitleme mantığını kapalı bir üçüncü taraf servise bağlamanın riskini gösteriyor. Alpnex'in kendi API'si bu riski taşımıyor. Microsoft Datasync Community Toolkit Avalonia'yı ve PostgreSQL/SQLite'ı destekliyor, ama güncel sürümü .NET 10 istiyor [K-146]. Alpnex .NET 8'de olduğu için doğrudan benimsemek sürüm yükseltmesi gerektirir; bu yüzden toolkit ancak referans olarak değerli.

### 200–300 bin satır küçük-orta ölçek; asıl risk sorgu biçimi

SQLite belgesine göre **kapsayan indeks (covering index)** tabloya hiç bakmadan yanıt veriyor ve yaklaşık 2 kat hız kazandırıyor. Bileşik indekste ilk sütun eşitlikle kullanılırsa sonraki sütun zaten sıralı geliyor ve ORDER BY ayrı bir sıralama gerektirmiyor. SQLite AND koşulları için birden fazla indeksi kesiştirmiyor [DOC] [K-157]. Firma (tenant) filtresinin her indeksin ilk sütunu olması bu kuraldan çıkan bir sonuç. `PRAGMA optimize`, uzun ömürlü bağlantıda açılışta ve periyodik olarak çalıştırılmalı [DOC] [K-158]. WAL dosyası bazı durumlarda sınırsız büyüyebiliyor: her zaman en az bir okuyucu aktifse checkpoint tamamlanamıyor ve WAL büyüdükçe okuma yavaşlıyor. Çözüm boşta `wal_checkpoint(TRUNCATE)` çalıştırmak ve dönüş değerini kaydetmek [DOC] [K-159][K-158]. WAL modunda `synchronous=NORMAL` bozulmaya karşı güvenli, ama güç kesintisinde son commit kaybolabiliyor [DOC] [K-158]. `%arama%` biçimindeki LIKE araması için SQLite'ta **FTS5 trigram** indeksi kullanılabiliyor (3 karakterden kısa aramalar eşleşmiyor) [DOC] [K-160]; PostgreSQL'de **pg_trgm GIN** indeksi var [DOC] [K-161]. OFFSET sayfalama atlanan her satırı okuyup atıyor. Keyset sayfalama `(tarih, id) > (...)` ile doğrudan konuma atlıyor; CockroachDB keyset'i "hızlı, önerilen", OFFSET'i "yavaş, önerilmeyen" olarak etiketliyor [K-162][K-163]. Supabase, kalıcı sunucular için doğrudan bağlantı ya da session pooler öneriyor. Transaction pooler (6543) hazırlanmış ifade (prepared statement), `LISTEN/NOTIFY` ve `SET` desteklemiyor [DOC] [K-164].

Avalonia'da sanallaştırma yalnız yüksekliği sınırlanmış panelde çalışıyor; liste bir StackPanel içine konursa sanallaştırma kapanıyor. Resmî performans kılavuzu, düzenleme gerekmiyorsa DataGrid'den kaçınmayı ve büyük veri için TreeDataGrid'i öneriyor (TreeDataGrid'in lisans durumu değişken) [DOC] [K-165]. ScrollViewer içindeki ItemsRepeater'ın bağlı listenin tüm öğelerini baştan okuduğu bildirilmiş [SEC] [K-166]. Arayüz sanallaştırması yalnız görsel kontrolleri azaltıyor; 300 bin satırı belleğe yüklemek hâlâ pahalı. Bu yüzden veri de sayfa sayfa (keyset) yüklenmeli.

Yerel yedekte açık bir SQLite dosyasını düz kopyalamak yerine **Online Backup API** ya da `VACUUM INTO` kullanılmalı [DOC] [K-167]. Bütünlük için düzenli `quick_check` (O(N)), daha seyrek `integrity_check` (O(N log N)) çalıştırılmalı. İkisi de yabancı anahtarı denetlemediği için ayrıca `foreign_key_check` gerekiyor [DOC] [K-158]. Gönderilmemiş işlem varken yerel veriyi sıfırlamak veri kaybı demek. Bu yüzden kurtarmada tercih "sunucudan yeniden ilk eşitleme + yerel bekleyen işlemleri koru" olmalı (çıkarım). Çevrimdışı istemciler varken şema geçişi (migration) için birincil kaynak bulunamadı. Alpnex'in "yalnız eklemeli migration" kuralı bu boşlukta mantıklı bir varsayım, ama sürüm uyumluluk penceresi kaynakla doğrulanmadı.

---

## Türk mevzuatı iki somut beklenti doğuruyor: e-İrsaliye ve periyodik kontrol

**e-İrsaliye.** 509 Sıra No.lu VUK Genel Tebliği e-belge düzenlemelerini birleştirdi. 535 Sıra No.lu tebliğle demir-çelik faaliyeti şartsız kapsama alındı ve ciro şartı **10 milyon TL** oldu [K-69][K-63]. Bir danışmanlık kaynağına göre 2023 ve sonrasında 10 milyon TL hasılatı aşan mükellefler 1 Temmuz 2024'e kadar geçiş yapmalıydı (2026 eşiği resmî sayfadan doğrulanmadı) [SEC] [K-65]. Zorunlu mükellef, istisnalar dışında, alıcı kayıtlı olmasa bile **tüm sevk irsaliyelerini** e-İrsaliye olarak düzenlemek zorunda [K-64]. Alpnex'in "şantiyeye sevk / depolar arası transfer" hareketleri bu yüzden vergi açısından sevk irsaliyesi gerektirebilir. Firmanın kendi depoları arasındaki taşımada irsaliye zorunluluğunun tam metni resmî kaynaktan teyit edilmedi (doğrulanmadı).

**Periyodik kontrol.** İş Ekipmanlarının Kullanımında Sağlık ve Güvenlik Şartları Yönetmeliği'ne (RG 25.04.2013) göre periyodik kontrol yalnız Ek-III tablolarındaki ekipman için zorunlu ve yetkili kişilerce yapılıyor [K-66][K-67]. Ek-III Tablo-2 kaldırma ve iletme ekipmanlarını kapsıyor; 2017 değişikliği vinçler için ASME B30.17 atfı ekledi [K-68]. Aralık 2025 değişikliğiyle işveren ile yetkili kişi arasında, kontrolden en geç 1 gün önce, İSG-KATİP'teki örneğe uygun sözleşme şartı geldi [K-66]. Kesin kontrol periyotları resmî metinden doğrulanamadı.

**Yerel ticari beklentiler.** Ürün sayfalarında doğrulanan beklentiler şunlar: çoklu döviz ve döviz bazlı sözleşme, kur farkı [K-55][K-53]; çek ve banka takibi, e-Fatura XML içe aktarma, resmî defterin Logo/Netsis/Mikro'da kalabilmesi [K-54]; avans, kesinti ve teminat [K-56][K-53]. KDV tevkifatı, cari ekstre ve senet takibi için ürün belgesi bulunamadı (doğrulanmadı). Araç muayene, trafik sigortası ve kasko tarih takibi Türk filo yazılımlarında olağan bir beklenti olarak görülüyor, ama bu çalışmada bir Türk ürün kaynağıyla doğrulanmadı. Benzer alanlar ERPNext Vehicle'da (poliçe başlangıç/bitiş) ve Odoo Fleet sözleşmesinde ("Omnium") var [K-85][K-103].

---

## Alpnex'te muhtemelen eksik olabilecek işlev/süreç adayları

**Kapsam notu.** Bu tablo bir **aday listesidir**. Alpnex'te bu işlevlerin olup olmadığı kodda kontrol edilmedi; mevcut sistemle karşılaştırma sonraki aşamada yapılacak. Bir rakipte bulunması gereklilik anlamına gelmez. Öncelik, Alpnex'in iş modeline (Türk şantiye operasyonu, çok şube, çevrimdışı masaüstü + web, ön muhasebe) uygunluğa ve maliyet/fayda tahminine göre verilmiş bir **ön önceliktir**. Mükerrer adaylar birleştirildi. "Not" sütunu lisans, kanıt düzeyi ya da mimari uyarıları içerir.

| # | Aday | İlgili modül | Kaynak(lar) | Alpnex için gerekçe / uygunluk | Ön öncelik | Not |
|---|---|---|---|---|---|---|
| 1 | **Makine iç kira birim fiyatı → projeye maliyet aktarımı**: kategori × zaman birimi (saat/gün/ay/bekleme) fiyatı, ekipman ve proje bazında geçersiz kılma, kullanımda ekipmana gelir + projeye maliyet | Filo, Günlük faaliyet, Maliyet merkezi, Projeler | [K-11][K-12][K-13][K-14][K-50][K-53] | Günlük faaliyetteki saatler ve kiralık fiyatlar zaten var; şantiye bazında gerçek makine maliyetinin en kısa yolu. Rakiplerde belgelenmiş en güçlü yapısal fark | **Yüksek** | Vista [DOC]; ön muhasebe kapsamında "basit iç yansıtma kaydı" yeterli olabilir |
| 2 | **Çalışma / rölanti (boşta) saati ayrımı** ve buna bağlı bekleme ücreti | Günlük faaliyet, Raporlar | [K-39][K-04][K-20] | Kiralık makinede bekleme ücreti ve kullanım oranı raporu için temel veri | Yüksek | Procore [DOC]; telematik rölanti saatini de taşıyor |
| 3 | **Sayaç bütünlüğü kuralları**: geriye gitmeme, geriye tarihli okumanın sıraya oturması, sıra dışı okumayı silmek yerine "geçersiz say", güncellenmeyen sayacı vurgulama, sayaç geçmişi + CSV | Günlük faaliyet/sayaç, Bakım | [K-15][K-16][K-11][K-125] | Periyodik bakım uyarılarının doğruluğu sayaca bağlı; çevrimdışı geç eşitlenen okumalar sıra dışı düşebilir | **Yüksek** | Alpnex kurallarında "sayaç geriye gitme" kritik test alanı; void ve eski sayaç vurgusu ayrıca kontrol edilmeli |
| 4 | **İkincil sayaç** (vinç üst/alt ünite, kamyon + jeneratör/PTO); sayaç değişimi kaydı | Filo, Sayaç | [K-15][K-11] | Türk şantiyelerinde kamyon üstü vinç ve pompa yaygın | Orta | Fleetio [DOC] |
| 5 | **"İş emrini kapatmak için sayaç zorunlu" ayarı**; kapanışın bakım hatırlatmasını sıfırlaması; yakıt/parça kaydının sayacı güncellemesi (ayarlı) | Bakım, İş emri | [K-08][K-16][K-11] | Sonraki bakım noktası kapanış sayacından hesaplanıyor; eksik sayaç yanlış uyarı üretir | **Yüksek** | B2W [SNIP], Vista [DOC] |
| 6 | **Tek bakım planında tarih + km + motor saati**, önceden hesaplanmış "sonraki bakım" alanları, **hatırlatma ofsetleri**, **planlanan / gerçekleşen tamamlamaya göre** sonraki nokta, **bakım şablonunu birçok makineye toplu uygulama** | Bakım tanımları | [K-129][K-07][K-09][K-124][K-25] | Önceden hesaplanmış alanlar çevrimdışı SQLite'ta ucuz sorgu sağlar; mevcut "bakım tanımları" ile doğal uyum | **Yüksek** | Fleetbase AGPL: yalnız model fikri |
| 7 | **Uyarı / arıza bildirimi / kontrol bulgusu → birkaç tıkla iş emri**; talepte foto ve **sesli not**; iş emrinin talep ya da plana bağlanması | Bakım, İş emri, Talep | [K-05][K-02][K-03][K-123][K-122][K-16] | Saha personelinin yazma zorluğu; uyarı ile iş emri arasındaki boşluğu kapatır | **Yüksek** | Atlas AGPL: yalnız fikir |
| 8 | **Günlük kontrol listesi (inspection)**: madde bazında sonuç, başarısız madde → arıza → iş emri, imza, km/saat | Filo, Bakım | [K-132][K-03][K-05][K-39] | İş makinesi günlük kontrolü İSG açısından da değerli | Orta-Yüksek | |
| 9 | **İş emrinde usta işçiliği/zamanı**, maliyet kırılımı (işçilik/parça/vergi/toplam), kapanış imzası, ilk müdahale süresi | İş emri, Personel | [K-05][K-130][K-122][K-127] | Onarımın gerçek maliyeti ve ekip performansı | Orta | |
| 10 | **Duruş süresi (downtime)**, arıza ve tamamlanma tarihi ayrımı, **MTBF/MTTR**, tahmini sonraki arıza | Bakım, Raporlar | [K-84][K-104][K-126] | Mevcut bakım verisinden hesaplanabilen ucuz göstergeler | Orta | |
| 11 | **Kiralık ekipman sözleşmesi**: kiralayan cari, başlangıç/bitiş, tekrarlayan bedel + sıklık, dahil hizmetler, **otomatik maliyet tahakkuku**, bitişten X gün önce uyarı; kiralık makine puantajı (saat × fiyat → kiraya veren cariye fatura/hakediş) | Filo (kiralık), Cari, Fatura | [K-101][K-103][K-53][K-22][K-19] | Türk şantiyelerinde kiralık makine çok yaygın; puantajı ayrı bir Türk ürününde bulunamadı | **Yüksek** | Odoo LGPL: kod değil model |
| 12 | **Zimmet kabul kaydı**: imza (yerinde ya da sonra), donmuş zimmet formu PDF'i, bekliyor/kabul/ret durumları, yarışa dayanıklı kesinleştirme (koşullu güncelleme) | Zimmet, Evrak | [K-115][K-114] | Alet kaybı anlaşmazlıklarında kanıt; koşullu güncelleme çevrimdışı eşitlemede doğal çakışma kuralı | **Yüksek** | Snipe-IT AGPL; KVKK açısından imza saklama ayrıca değerlendirilmeli (kaynak yok) |
| 13 | **Beklenen iade tarihi ve gecikmiş iade listesi**; **periyodik zimmet sayımı** (sonraki sayım tarihi, "yaklaşan/gecikmiş", günlük bildirim) | Zimmet | [K-114][K-116][K-27] | Doğrudan inşaat alet kaybını hedefliyor | **Yüksek** | |
| 14 | **Zimmet talebi / ödünç alma onayı**, sepet tarzı toplu çıkış, **kit şablonları**, okutarak sayım; kişiye, lokasyona ya da **başka varlığa** (alet → araç) zimmet | Zimmet, Ekipman | [K-27][K-26][K-114][K-136] | Mevcut zimmet modülünün akış derinliği | Orta-Yüksek | |
| 15 | **İki adımlı transfer ve "yolda (transit)" depo**: gönder → teslim al, eksik/hasarlı teslim farkı; transfere **nakliye ek maliyeti** | Stok, Depo transferi | [K-94][K-96][K-75] | Şantiyeler arası nakliyede kayıp görünürlüğü; hareket defteri ilkesine tam uyumlu (sanal depo) | **Yüksek** | |
| 16 | **Geriye tarih kontrolü**: stok dondurma tarihi + geriye tarih girebilecek rol; geç eşitlenen kayıtlar için **durumlu yeniden hesaplama işi** (kuyrukta/çalışıyor/bitti/hata, hata günlüğü, tekilleştirme) | Stok, Senkron, Ön muhasebe | [K-78][K-79][K-80][K-92] | Çevrimdışı masaüstü kayıtları doğal olarak geriye tarihli düşer; hareket sonrası bakiye/maliyet saklanıyorsa doğruluk için kritik | **Yüksek** | ERPNext GPL: yalnız tasarım |
| 17 | **Sayım iyileştirmeleri**: kör sayım, kullanıcı/rol bazında tolerans, tolerans dışı farkta amir onayı, eşik ya da "son sayımdan N gün" ile otomatik sayım görevi, lokasyon bazında planlı sayım tarihi, sayım farkı/fire için **"envanter kaybı" sanal deposu** | Stok sayımı | [K-37][K-95][K-112][K-94] | Şantiye depolarında periyodik sayım ve hesap verebilirlik | Orta-Yüksek | |
| 18 | **Depo bazında min / istenen stok** ve **yeniden sipariş öneri listesi** (otomatik sipariş değil), erteleme, manuel tetik; düşük stoktan satın alma talebi | Stok, Satın alma talebi | [K-134][K-97][K-81][K-26][K-28][K-57] | Şantiye gerçekliği: öneri + insan kararı | Orta-Yüksek | |
| 19 | **Talepte "stoktan karşıla (transfer/çıkış) ya da satın al" dallanması**; talepte yüzde bazlı durum (kısmen sipariş edildi / kısmen teslim alındı), "Durduruldu" | Talep, Satın alma | [K-33][K-82] | Merkez depoda olan malzemenin gereksiz satın alınmasını önler | Orta-Yüksek | |
| 20 | **Sipariş satırı bazında mal kabul**, "mal kabul gerekli" bayrağı, toplu kabul, "kısmen teslim alındı" durumu, işlenmiş kabulü denetim iziyle düzeltme | Satın alma | [K-34][K-38][K-99] | Şantiyede parçalı teslim yaygın | Yüksek | |
| 21 | **Üçlü eşleştirme**: fatura miktarı ≤ teslim alınan − önceden faturalanan; aşarsa uyarı/"ödenmeli mi" bayrağı; **onaylı siparişi kilitleme** | Fatura, Satın alma, Ödeme | [K-100][K-99][K-98][K-53] | İrsaliyesiz ya da eksik teslimli faturalara karşı koruma; Türk rakip Yapıtaşı'nda var | **Yüksek** | |
| 22 | **Onay iyileştirmeleri**: tutar eşikli seviyeler (değer bazlı / kademeli), onaycı grupları, **onayı geri alma** (denetim kaydıyla), "bende bekleyen onaylar" ekranı, fatura onayı | Talep + onay, Fatura | [K-44][K-45][K-33][K-98] | Mevcut onay zincirinin olgunluğu | Orta | Vekâlet/izin yerine onaycı hiçbir üründe bulunamadı |
| 23 | **Kayıt bazında etkinlik paneli (chatter)**: alan değişiklik geçmişi + yorum/@bahsetme + ekler + son tarihli **aktivite**; tüm uyarıları toplayan "Yapılacaklarım" listesi | Tüm kayıtlar, Denetim kaydı, Evrak | [K-106][K-107][K-87][K-117] | Denetim kaydı ve evrak altyapısı varsa büyük ölçüde görünüm işi; kullanıcı değeri yüksek | **Yüksek** | |
| 24 | **Bildirim kuralları**: durum değişimi/yorumda oluşturana, atanana ve dağıtım listesine; gecikme uyarısı yalnız atanana ve eylem gerektiren durumda; tarih bazlı günlük uyarılar | Bildirimler | [K-42][K-91][K-116] | Uyarı yorgunluğunu azaltır | Orta | |
| 25 | **İptal + düzelt (amend)**: ters kayıtla iptal edilen belgenin orijinale bağlı düzeltilmiş kopyası, ekler taşınır | Tüm belgeler | [K-86] | "Fiziksel silme yok, ters kayıt" ilkesinin doğal eklentisi | Orta | |
| 26 | **İyimser kilitleme**: form açıldıktan sonra kayıt değiştiyse uyarı (`modified`/`version`) | Tüm formlar (web + masaüstü) | [K-86][K-127] | Web ve masaüstünde aynı kaydın eşzamanlı düzenlenmesi | Orta-Yüksek | Ana veride sürümlü LWW ile ilişkisi kontrol edilmeli |
| 27 | **İzin kontrol listesi**: onayla / iptal / düzelt / dışa aktar / yazdır / içe aktar ayrı izinler; "yalnız kendi kaydı"; hassas alan **maskeleme**; kullanıcıya varsayılan şube; şantiye bazlı izin şablonu; **koddan otomatik üretilen izin matrisi** | Rol/yetki | [K-88][K-89][K-43][K-108] | Alpnex alan düzeyinde yetkiye sahip; bu liste eksik kenar durumları yakalamak için | Orta | |
| 28 | **Günlük faaliyet "kesinleştir/işle" durumu** ve **geriye tarih penceresi** (ör. ±7 gün) | Günlük faaliyet | [K-47][K-48] | Geç saha düzeltmelerini sınırlar; maliyet aktarımıyla birlikte anlamlı | Orta | Swinerton [SEC] |
| 29 | **QR kodla ekipman kartı açma ve şantiyeye giriş/çıkış**; ekipman/alet etiketleri | Ekipman, Zimmet, Filo | [K-40][K-03][K-27][K-136] | Sahada hızlı tanıma | Orta | Masaüstünde etiket basımı, web/mobilde okutma |
| 30 | **Şantiyeden ekipman talebi ve rezervasyon**, çifte rezervasyon engeli, sevk panosu, planlı atama ("şu tarihten itibaren") | Filo, Projeler | [K-03][K-10][K-102][K-136][K-19] | Merkezi filonun şantiyeler arası planlaması | Orta | |
| 31 | **Ataşmanların ana makineyle birlikte taşınması**; **bileşen takibi** (motor, şanzıman) | Ekipman, Filo | [K-11] | Ekskavatör + kırıcı gibi; büyük onarım geçmişi | Orta / Düşük-Orta | |
| 32 | **Sertifika, eğitim ve kalibrasyon süreleri**; **yasal periyodik kontrol kaydı** (ekipman, son kontrol, yetkili kişi/firma, rapor dosyası, sonraki tarih, İSG-KATİP sözleşmesi, uyarı) | Ekipman, Personel, Bakım, Evrak | [K-30][K-05][K-83][K-66][K-67][K-68] | Vinç, forklift ve platform tutan Türk firmaları için doğrudan mevzuat ilişkisi | **Orta-Yüksek** | Kesin periyotlar doğrulanmadı; operatör belgesi (MYK vb.) doğrulanmadı |
| 33 | **Araç sigorta / kasko / muayene / poliçe tarihleri** ve süre bitiş uyarıları; garanti bitiş listesi | Filo, Evrak | [K-85][K-103][K-49] | Türk filo pratiğinde olağan beklenti | Orta | Türk ürün kaynağıyla doğrulanmadı |
| 34 | **Yakıt iyileştirmeleri**: istatistiksel sapmayla otomatik "geçersiz say" (geri alınabilir), saat başı yakıt maliyeti, rölanti yakıtı görünümü, planlı e-posta yakıt raporu | Yakıt | [K-17][K-20] | Mevcut sapma uyarılarını tamamlar | Orta | |
| 35 | **CSV/Excel ile makine saati ve yakıt içe aktarma** (önizleme + doğrulama); ileride **telematik sayaç içe aktarma (ISO 15143-3 / AEMP)** | Günlük faaliyet, Yakıt, Entegrasyon | [K-51][K-04][K-24][K-07] | CSV hemen değerli. Telematik büyük filolarda elle girişi kaldırır, ama sunucu tarafı yoklama gerektirir | CSV: Orta · Telematik: Düşük (stratejik) | Türk filolarda AEMP aboneliği yaygınlığı doğrulanmadı |
| 36 | **Ek raporlar**: ekipman kullanım oranı (çalışma/rölanti/boşta), ekipman birim maliyeti (toplam maliyet ÷ saat), ay sonu depo stok değeri, kullanım vs üretim, negatif fiyat/maliyet istisnası, ayrılmış vs serbest stok, planlı e-posta raporları | Raporlar | [K-49][K-35][K-20][K-53] | ~30 raporluk mevcut setin sektöre özgü tamamlayıcıları | Orta | |
| 37 | **Stok rezervasyonu / ayrılmış stok** (iş emri ya da talep için) | Stok | [K-78][K-35] | Bakım parçası ve kritik malzeme için | Düşük-Orta | Çevrimdışında garanti verilemez (bkz. #41) |
| 38 | **Proje bazlı bütçe vs gerçekleşen maliyet** | Projeler, Maliyet merkezi | [K-56][K-55][K-54] | Türk inşaat ERP'lerinin ortak paydası; maliyet merkezleri zaten var | Orta-Yüksek | |
| 39 | **Şantiye personel puantajı** (günlük, aktivite bazlı saat, fazla mesai; isteğe bağlı çevrimdışı kiosk) | Personel | [K-53][K-54][K-57][K-62] | Personel ve günlük faaliyet zaten var; makine-operatör saat bağı (B2W) ile birlikte değerli | Orta | Bordro kapsam dışı tutulabilir |
| 40 | **Taşeron sözleşmesi, taşeron hakedişi, avans/kesinti/teminat; taşeron portalı** | Cari, Projeler (yeni alan) | [K-53][K-54][K-55][K-56] | Türk "inşaat ERP" beklentisinin çekirdeği; ama Alpnex'in saha operasyonu konumlamasının dışında. Alternatif: hakediş programı ve Logo/Mikro ile birlikte çalışma | Orta (stratejik karar) | Ürün konumlama kararı gerektirir |
| 41 | **e-İrsaliye uyumu**: sevk/transfer hareketinde irsaliye no/tarih alanı ve UBL dışa aktarım ya da özel entegratör bağlantısı; **e-Fatura XML içe aktarma**; Logo/Mikro'ya Excel/XML aktarım | Depo transferi, Fatura, Entegrasyon | [K-63][K-64][K-65][K-69][K-54] | Zorunlu mükellef müşteriler için sevk irsaliyesi yasal gereklilik olabilir | **Yüksek** (doğrulama gerekli) | Firma içi depo taşımasında irsaliye zorunluluğu ve 2026 eşiği doğrulanmadı |
| 42 | **Döviz cari ve kur farkı, çek/senet portföyü, teminat takibi** | Ön muhasebe | [K-55][K-53][K-54] | Türk ön muhasebe beklentisi | Orta | Alpnex'te varlığı kodda kontrol edilmeli |
| 43 | **İdare (işveren) malzemesi** ayrımı | Stok, Talep | [K-82] | İşveren tarafından verilen malzemenin ayrı izlenmesi | Düşük-Orta | ERPNext "Customer Provided" benzeri |
| 44 | **Silinen ana verinin tam anlık görüntüsü + geri yükleme**; benzersiz kodların yalnız silinmemişler arasında zorunlu tutulması | Çöp kutusu, Ana veri | [K-90][K-114][K-40] | Mevcut çöp kutusunu ana veri için tamamlar | Düşük-Orta | |
| 45 | **Dayanıklı zamanlanmış işler**: ardışık hata sayacı, sürekli başarısız işi otomatik durdurma, ilerleme bildirimi, aynı işin çift kuyruklanmasını engelleme | Arka plan işleri (API) | [K-109][K-93][K-79] | Uyarı, tahakkuk ve yeniden hesaplama işleri arttıkça gerekli | Orta | |
| 46 | **Eşitleme imleci (cursor)**: commit sırasına dayalı imleç ya da örtüşme penceresi + idempotent upsert; SQLite tarih hassasiyeti | Senkron | [K-143][K-145][K-148][K-149][K-150] | Uzun transaction'da satır kaçırma riski; 60 sn tur ancak imleç geride ise yakalar | **Yüksek (denetim)** | Kodda doğrulanmalı |
| 47 | **Kalıcı/geçici hata ayrımı**: 4xx doğrulama/çakışma = "reddedildi", ağ/5xx = bekleme + jitter ile tekrar; başarısız işlemin kuyruğu kilitlememesi; aynı kayda/kaleme ait işlemlerin sıralı gönderimi | Senkron | [K-147][K-155][K-145] | Tek bir hatalı işlemin tüm eşitlemeyi durdurmasını önler | **Yüksek (denetim)** | |
| 48 | **Masaüstünde kayıt bazında eşitleme durumu** (bekliyor / uygulandı / reddedildi + neden + yeniden dene/düzelt), reddedilen hareketin bağımlıları için politika, yönetici **eşitleme hata listesi** ve cihaz durumu | Senkron, Arayüz | [K-143][K-153][K-152][K-168] | Web'deki çakışma ekranının masaüstü karşılığı; çevrimdışı negatif stok reddinin kullanıcıya görünmesi | **Yüksek** | ECOSIRE [MKT] |
| 49 | **Yetki/şube kapsamı değişince yerel SQLite'tan kapsam dışı verinin temizlenmesi** | Senkron, Güvenlik | [K-152] | Veri sızıntısı riski | **Yüksek (denetim)** | |
| 50 | **İlk eşitlemede sayfa sayfa çekme, her sayfadan sonra kalıcı imleç, kaldığı yerden devam, ilerleme göstergesi**; tablo bazında son eşitleme zamanı ekranı | Senkron, Kurulum | [K-147][K-145][K-152] | 200–300 bin satır/tablo ölçeğinde kurulum sağlamlığı | Orta-Yüksek | |
| 51 | **Liste performansı**: keyset sayfalama, firma-önce bileşik/kapsayan indeks, `%arama%` için FTS5 trigram / pg_trgm, `PRAGMA optimize`, sanallaştırmayı bozan panel düzenlerinin taranması | Masaüstü listeler, API | [K-157][K-158][K-160][K-161][K-162][K-163][K-165][K-166] | Büyük veride son sayfaların ve aramanın yavaşlamasını önler | Orta | Ölçmeden indeks değişikliği yapılmamalı |
| 52 | **Yerel DB sağlığı**: WAL boyutu izleme + boşta `wal_checkpoint(TRUNCATE)`, Online Backup API / `VACUUM INTO` ile yedek, açılışta/haftalık `quick_check` + `foreign_key_check`, sunucuda "bakiye = hareket toplamı" mutabakatı | Masaüstü altyapı | [K-159][K-158][K-167] | Bozulma ve WAL şişmesine karşı; yedekten dönüşte bekleyen işlemler korunmalı | Orta | Mutabakat sorgusu çıkarım |

Tablo 52 satır; senkron ve performansla ilgili teknik denetim maddeleri (#46–#52) işlev adaylarından ayrı tutuldu. Yalnız işlev/süreç adayları sayıldığında 45 aday kalıyor. Düşük öncelikli ve bilinçli olarak tabloya alınmayan fikirler şunlar: BLE/ağ geçidi ile otomatik konum ve transfer önerisi (donanım gerektiriyor) [K-29][K-31], dolum pompası otomasyonundan içe aktarma (Türkiye'de yaygınlığı doğrulanmadı) [K-20], konut satış modülü [K-54], resmî birim fiyat kütüphanesi ve metraj (uzman hakediş programlarının alanı) [K-54][K-59], İSG/kalite modülleri (ramak kala, uygunsuzluk) [K-54], BIM/GIS (openMAINT) [K-138].

---

## Sonuç

Bu araştırmanın değiştirdiği temel algı şu: Alpnex'in rakibi tek bir ürün değil, **müşterinin yan yana kullandığı ürün kombinasyonu**. ABD'de bu Vista + HCSS + ToolWatch; Türkiye'de Logo/Mikro + bir hakediş programı + Excel. Türk inşaat ERP'leri hakediş ve maliyette derin, ama makine, yakıt, zimmet ve çevrimdışı alanında boşluk bırakıyor. Alpnex'in saha operasyonuna odaklı konumu bu boşluğa oturuyor. Bu yüzden en yüksek getirili adımlar yeni bir dikey alan (hakediş) açmak değil. Asıl kazanç mevcut modüller arasındaki **bağları** kurmakta: günlük faaliyet saatini iç kira fiyatıyla projeye maliyete, sayacı bakım ve iş emrine, transferi irsaliyeye ve transit depoya, zimmeti imzalı kabule ve periyodik sayıma, siparişi kabul ve fatura eşleştirmesine bağlamak. Rakiplerin değerini oluşturan tam olarak bu bağlar. Alpnex'te modüller zaten var; eksik olan büyük ölçüde aralarındaki otomatik akış. Hakediş ve taşeron için doğru soru "eklemeli miyiz?" değil, "hangi hakediş programı ve muhasebe yazılımıyla birlikte çalışmalıyız?" olmalı.

İkinci sonuç teknik. Alpnex'in çevrimdışı masaüstü mimarisi pazarda gerçek bir fark, ama farkın değeri **güvenilirliğe** bağlı. D365'in varsayılan ayarda dispeçer verisini ezmesi ve Atlas Device Sync'in kapanması gösteriyor ki bu alanda hata ve bağımlılık pahalı. Belgelenmiş desenler Alpnex'in temel kararlarını doğruluyor: operasyonel kayıtta LWW yasağı, delta hareketler, kendi API'si, sunucunun son sözü söylemesi. Kalan riskler imleç semantiği, kalıcı hata ayrımı, masaüstünde reddin görünürlüğü ve yetki daralınca yerel veri temizliği. Bunlar yeni işlevlerden önce kodda doğrulanmalı. Çünkü geç eşitlenen ve "geriye tarihli" düşen kayıtlar, önerilen maliyet aktarımı, yeniden hesaplama ve sayaç bütünlüğü adaylarının hepsinin doğruluğunu doğrudan etkiliyor.

---

## Kaynakça

Tüm kaynaklara erişim tarihi: **2026-10-12**. Etiketler: [DOC] resmî belge, [SRC] kaynak kod, [MKT] pazarlama, [SEC] ikincil, [SNIP] arama özeti.

**Ekipman, filo, bakım ve zimmet ürünleri**
- [K-01] Tenna ana sayfa — https://www.tenna.com/ — Tenna modülleri (takip, bakım, kontrol, alet, entegrasyon) [MKT]
- [K-02] Tenna Preventive Maintenance — https://www.tenna.com/construction-equipment-maintenance/preventive-maintenance/ — kullanım/zaman tetikli bakım, alarmdan otomatik talep [MKT]
- [K-03] Tenna Equipment Management App — https://www.tenna.com/construction-asset-management/equipment-management-app/ — mobil kontrol, QR, transfer/rezervasyon, çevrimdışı, sesli not [MKT]
- [K-04] Tenna OEM Telematics & AEMP — https://www.tenna.com/integrated-construction-software/oem-telematics-aemp-integrations/ — ISO/TS 15143-3 alanları, OEM ortakları [MKT]
- [K-05] HCSS Equipment360 — https://hcss.com/equipment360 — sayaç/tarih bazlı bakım, uyarıdan üç tıkla iş emri, usta zaman kartı, sertifika uyarıları [MKT]
- [K-06] Capterra: Equipment360 — https://www.capterra.ca/software/140169/equipment360 — 4,1/5 (19 yorum), fiyat/değer 2,8 [SEC]
- [K-07] Trimble B2W Maintain — https://www.trimble.com/products/b2w-software/maintain-equipment-maintenance — bakım şablonu, sürükle-bırak, panolar, AEMP [MKT]/[SNIP]
- [K-08] B2W Maintain Feature Overview 2023 (PDF) — https://www.b2wsoftware.com/wp-content/uploads/B2W-Maintain-FeatureOverview_April2023.pdf — "iş emrini kapatmak için sayaç zorunlu" ayarı [SNIP]
- [K-09] Trimble PM planning blog — https://www.trimble.com/en/blog/construction/article/preventive-maintenance-planning-scheduling — Planned vs Actual Completion [SNIP]
- [K-10] Trimble B2W Track — https://www.trimble.com/en/products/b2w-software/track — saha günlüğü, operatör-makine saat bağı, saha talebi [SNIP]
- [K-11] Trimble Vista: Equipment Management — https://help.trimble.com/doc/vista/vista/job-resources/equipment-management — EM fiyatları, kullanım işleme, sayaç, bakım grupları, bileşen, ataşman, transfer, amortisman [DOC]
- [K-12] Vista: EM Revenue Rates by Category — https://help.trimble.com/en/vista/vista/job-resources/equipment-management/revenuecosts/revenuecost-setup/about-the-em-revenue-rates-by-category-form — kategori × gelir kodu fiyatı [DOC]
- [K-13] Vista: EM Revenue Rates by Equipment — https://help.trimble.com/en/vista/vista/job-resources/equipment-management/revenuecosts/revenuecost-setup/about-the-em-revenue-rates-by-equipment-form — ekipman bazında geçersiz kılma [DOC]
- [K-14] Vista: Set Up and Process Equipment Revenue — https://help.trimble.com/zh-tw/vista/vista/job-resources/equipment-management/revenuecosts/revenuecost-setup/set-up-and-process-equipment-revenue — Usage Cost Type, gelir/maliyet işleme [DOC]
- [K-15] Fleetio: Meter overview — https://help.fleetio.com/en_US/meter-overview — birincil/ikincil sayaç, doğrulama kuralları, Mark as Void, sayaç geçmişi [DOC]
- [K-16] Fleetio: Work Order Meter Validation / Add Work Orders — https://help.fleetio.com/en_US/work-order-meter-validation ; https://help.fleetio.com/en_US/add-work-orders — iş emri başlangıç/kapanış sayacı, servis kaydı, hatırlatmadan iş emri [SNIP]
- [K-17] Fleetio: Auto Void Fuel Entry — https://help.fleetio.com/en_US/auto-void-fuel-entry — standart sapmayla otomatik geçersiz sayma [SNIP]
- [K-18] Fleetio: WEX fuel card integration — https://help.fleetio.com/en_US/fuel/wex-fuel-card-integration — günlük yakıt kartı içe aktarma [SNIP]
- [K-19] Clue ana sayfa — https://getclue.com/ — modüller, sevk, kiralama yönetimi [MKT]
- [K-20] Clue Fuel Management — https://getclue.com/solutions/fuel-management-software — çevrimdışı yakıt raporu, PIUSI içe aktarma, tank dolumu, rölanti yakıtı, e-posta raporları [MKT]
- [K-21] Clue–Vista entegrasyonu — https://getclue.com/integrations/clue-vista — GPS sayaç ve iş emri eşitlemesi [MKT]
- [K-22] Construction Dive: EquipmentShare T3 — https://www.constructiondive.com/press-release/20211021-equipmentshare-launches-t3-the-operating-system-for-construction-to-enhan/ — T3 Fleet ve dış kiralık makine [SEC]
- [K-23] Stevie Awards: EquipmentShare (PDF) — https://blog.stevieawards.com/hubfs/sate25%20GOLD%20PDFs/Judge-5999-EquipmentShare.pdf — 7.700 varlık, %72 < 4 saat (üretici beyanı) [SEC]
- [K-24] Samsara KB: Caterpillar VisionLink — https://kb.samsara.com/hc/en-us/articles/360041465972-Integrate-with-Caterpillar-VisionLink — AEMP 2.0 alanları [DOC]
- [K-25] Samsara KB: Create PM Schedule — https://kb.samsara.com/hc/en-us/articles/4405865435789-Create-a-New-Preventative-Maintenance-Schedule — tarih/km/motor saati planı [DOC]
- [K-26] ToolWatch (AlignOps) — https://alignops.com/toolwatch — toplama fişi, kit, düşük stoktan sipariş, onay, denetim izi, projeye faturalama [MKT]
- [K-27] ShareMyToolbox FAQ — https://www.sharemytoolbox.com/faq/ — okutarak çıkış, ödünç alma onayı, sepet, sayım [SNIP]/[MKT]
- [K-28] Hilti ON!Track: web platform navigation — https://help.ontrack3.hilti.com/hc/en-us/articles/34399866911889-How-to-navigate-in-the-web-platform — uyarı türleri, transfer onayı, min/sıfır eşik [DOC]
- [K-29] Hilti ON!Track: suggested transfers — https://help.ontrack3.hilti.com/hc/en-us/articles/34399871008785-How-suggested-transfers-functionality-works — transfer önerisi [DOC]
- [K-30] Hilti ON!Track Services Description 2024 (PDF) — https://assets.hilti.com/api/public/content/5052838-original?v=E4_IL_Services_Description_2024.pdf — paketler, sertifika yönetimi [DOC]
- [K-31] Hilti ON!Track: PAT overview — https://help.ontrack3.hilti.com/hc/en-us/articles/34399852984081-Proactive-Asset-Tracking-PAT-Overview — ağ geçidi, ~5 dk güncelleme [DOC]/[SNIP]
- [K-32] Tenna basın bülteni: Vista iş emri entegrasyonu — https://www.tenna.com/press_releases/work-order-integration-viewpoint-vista/ — iş emri/parça/işçilik maliyeti aktarımı [MKT]

**Global inşaat ERP'leri**
- [K-33] CMiC Requisitions v10x (PDF) — https://docs.cmicglobal.com/portal/Content/Resources/PDFs/v10x/RQv10_x_ADF.pdf — talep → stoktan çıkış ya da sipariş; onay seviyeleri/grupları, onayı geri alma [SNIP]/[DOC]
- [K-34] Vista: Enter a Receipt in PO Receipts Entry — https://help.trimble.com/en/vista/vista/costs-and-contracts/purchase-order/receiving/enter-a-new-receipt — satır bazında kabul, toplu kabul [DOC]
- [K-35] Vista: Inventory Reports — https://help.trimble.com/en/vista/vista/reports-catalog/inventory-reports — 30 envanter raporu [DOC]
- [K-36] Viewpoint Vista Inventory — https://help.viewpoint.com/vista/vista/job-resources/inventory — transfer, düzeltme, sayım çalışma sayfası [SNIP]
- [K-37] MS Learn: Cycle counting — https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/cycle-counting — eşik/plan tetiği, kör sayım, tolerans ve amir onayı [DOC]
- [K-38] Procore FAQ: default commitment statuses — https://support.procore.com/faq/what-are-the-default-commitment-statuses-in-procore — Partially Received, Approved, Closed [DOC]
- [K-39] Procore: Add Equipment Entries to a Daily Log (iOS) — https://v2.support.procore.com/product-manuals/daily-log-ios/tutorials/add-equipment-entries-to-a-daily-log-ios/ — çalışma/rölanti saati, maliyet kodu [DOC]
- [K-40] Procore: About the Equipment Tool — https://support.procore.com/products/online/user-guide/company-level/equipment/tutorials/about-the-equipment-tool — kiralık/sahip olunan, QR, bakım kaydı, çöp kutusu [DOC]
- [K-41] Procore: Daily Log permissions (Android) — https://support.procore.com/procore-mobile-android/user-guide/daily-log-android/permissions — çevrimdışı sınırları [DOC]
- [K-42] Procore FAQ: Observations notifications — https://support.procore.com/faq/who-gets-notifications-from-observations — bildirim kuralları [DOC]
- [K-43] Procore FAQ: permissions — https://v2.support.procore.com/en-ca/faq-what-are-permissions-in-procore-and-how-do-they-work — izin seviyeleri, şablonlar, matris [DOC]
- [K-44] Sage Intacct: About purchasing approvals — https://www-p02.intacct.com/ia/docs/en_US/help_action/Purchasing/Setting_up_Purchasing/Approvals/about-purchasing-approvals.htm — yapılandırılabilir onay [DOC]
- [K-45] Greytrix: Sage Intacct approvals — https://www.greytrix.com/blogs/sageintacct/wp-json/wp/v2/posts/8745 — değer bazlı / kademeli onay [SEC]
- [K-46] Sage Intacct: Integration tips for Sage Construction Management — https://www-p02.intacct.com/ia/docs/en_US/help_action/Construction/Sage_Construction_Management/Tips-integrate-SageConstrMgt.htm — tek onay sahibi; Equipment maliyet türü [DOC]
- [K-47] CMiC: Daily Journal — https://docs.cmicglobal.com/portal/Content/E_Reference_Material/CMiC_Field/Reference/Site_Management/Daily_Journal.htm — EMPTY/UNPOSTED/POSTED [DOC]
- [K-48] Swinerton müşteri kılavuzu — https://support.swinerton.com/support/solutions/articles/1000052774 — ±7 gün tarih penceresi [SEC]
- [K-49] Trimble Spectrum: Equipment Control reports — https://help.trimble.com/es-mx/spectrum/spectrum/equipment/equipment-control/spectrum-menus/reports-overview — kullanım, birim maliyet, yakıt, garanti raporları [DOC]
- [K-50] SoftwareOne: Foundation equipment management — https://platform.softwareone.com/product/construction-equipment-management/PCP-0244-1758 — iç yansıtma, denetim izi [SEC]
- [K-51] Sage City forum (Sage 300 CRE Job Cost) — https://www.sagecity.com/us/sage_construction_and_real_estate/f/sage-300-construction-and-real-estate/tags/processing/Sage%2b300%2bCRE/Job%2bCost — CSV ile ekipman saati içe aktarma [SEC]; ayrıca Foundation Heavy Highway — https://www.foundationsoft.com/heavy-highway/ [MKT]
- [K-52] Procore Equipment release notes — https://support.procore.com/products/online/user-guide/project-level/equipment/release-notes — kişiye bağlı olmayan ekipman timesheet'i [DOC]

**Türkiye ürünleri ve mevzuat**
- [K-53] Yapıtaşı ERP — https://www.yapitasierp.com/en/pages/yapitasi_erp — modüller, üçlü eşleştirme, makine-ekipman, taşeron [MKT]
- [K-54] KonstraERP — https://www.konstraerp.com/ — 16 modül, taşeron portalı, Hafriyat, fiyatlar, e-Fatura XML [MKT]
- [K-55] Uyumsoft Şantiye ve İnşaat Takip Programı — https://www.uyumsoft.com/santiye-insaat-takip-programi — modüller, çoklu döviz [MKT]
- [K-56] Qera ERP — https://qera.com.tr/santiye-bazli-maliyet-ve-hakedis-programi/ — bütçe-gerçekleşen, hakediş, stok [MKT]
- [K-57] Mavvo Blog: İnşaat ERP (DİA) — https://mavvo.com.tr/blog/insaat-erp/ — makine envanteri, bakım takvimi, saat/yakıt analizi [MKT]
- [K-58] Navapp: İnşaat ERP karşılaştırma — https://www.navapp.com.tr/blog/insaat-erp-programlari-karsilastirma — pazar haritası (taraflı) [SEC]
- [K-59] Oska Yazılım — https://www.oska.com.tr — HakedişPlus+, OSKABULUT, e-kikNet [MKT]
- [K-60] Parsek blog — https://parsekpro.com/blog/insaat-erp-nedir-santiye-yonetiminde-dijitallesme — barkodlu şantiye deposu [SNIP]
- [K-61] Logo Faaliyet Raporu 31 Mart 2026 (PDF) — https://cdn.logo.com.tr/Files/logocomtr/Uploads/Documents/Faaliyet-Raporu-31-Mart-2026.pdf — müşteri/iş ortağı sayısı [DOC]
- [K-62] Jibble: İnşaat zaman saati — https://www.jibble.io/tr/insaat-zaman-saati — çevrimdışı puantaj kiosku [MKT]
- [K-63] GİB 535 Sıra No.lu VUK Genel Tebliği bilgi notu (PDF) — https://gib.gov.tr/sites/default/files/fileadmin/mevzuatek/mevzuatbilginotu/535_serno_vukgenteb_abn.pdf — demir-çelik, 10 milyon TL eşik [DOC]
- [K-64] Alomaliye: e-İrsaliye kapsamı — https://www.alomaliye.com/2020/06/15/e-irsaliye-uygulamasinin-kapsami-genisliyor/ — tüm sevk irsaliyeleri [SEC]
- [K-65] Paraşüt: e-İrsaliye başvurusu — https://www.parasut.com/blog/e-irsaliye-basvurusu-nasil-yapilir — 1 Temmuz 2024 geçiş [SEC]
- [K-66] ÇSGB duyurusu 24.12.2025 — https://csgb.gov.tr/isggm/duyurular/24122025/ — periyodik kontrol değişikliği, İSG-KATİP sözleşmesi [DOC]
- [K-67] mevzuat.gov.tr: İş Ekipmanları Yönetmeliği — https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=18318&MevzuatTur=7&MevzuatTertip=5 — Ek-III kapsamı [DOC]
- [K-68] Alomaliye 2017: İş ekipmanları değişikliği — https://www.alomaliye.com/2017/04/24/is-ekipmanlarinin-kullaniminda-saglik-ve-guvenlik/ — kaldırma ekipmanı, ASME B30.17 [SEC]
- [K-69] Andersen bülteni: e-İrsaliyeye geçiş (PDF) — https://tr.andersen.com/tr/images/pdf/2020-42%20e-irsal%C4%B1ye-uygulamas%C4%B1na-gec%C4%B1s.pdf — 509 Sıra No.lu tebliğ kapsamı [SEC]
- [K-175] Teltonika: ağır araç yönetimi — https://www.teltonika-gps.com/tr/use-cases/agriculture/management-of-heavy-duty-vehicles-with-ble-accessories — iş makinesi takibinin telematik sağlayıcıda olması [MKT]

**ERPNext / Frappe**
- [K-70] GitHub API: frappe/erpnext — https://api.github.com/repos/frappe/erpnext — GPL-3.0, son push, varsayılan dal [SRC]
- [K-71] ERPNext Releases — https://github.com/frappe/erpnext/releases — v15.122.0, v16 hattı [SRC]
- [K-72] Frappe LICENSE — https://github.com/frappe/frappe/blob/develop/LICENSE — MIT [SRC]
- [K-75] stock_entry.json — https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_entry/stock_entry.json — transit, ek maliyet, geriye tarih [SRC]
- [K-76] stock_ledger_entry.json — https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_ledger_entry/stock_ledger_entry.json — hareket defteri alanları [SRC]
- [K-77] stock_ledger.py — https://github.com/frappe/erpnext/blob/develop/erpnext/stock/stock_ledger.py — iptal işareti, değerleme, negatif stok [SRC]
- [K-78] stock_settings.json — https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_settings/stock_settings.json — dondurma tarihi, geriye tarih rolü, rezervasyon [SRC]
- [K-79] repost_item_valuation.py (+ .json) — https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/repost_item_valuation/repost_item_valuation.py — durumlu yeniden hesaplama, kurtarılabilir hata [SRC]
- [K-80] docs.frappe.io: Repost Item Valuation — https://docs.frappe.io/erpnext/user/manual/en/repost-item-valuation — kullanıcı belgesi [DOC]
- [K-81] reorder_item.py — https://github.com/frappe/erpnext/blob/develop/erpnext/stock/reorder_item.py — günlük yeniden sipariş [SRC]
- [K-82] material_request.json — https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/material_request/material_request.json — talep türleri, yüzde durumlar, Customer Provided [SRC]
- [K-83] asset_maintenance_task.json — https://github.com/frappe/erpnext/blob/develop/erpnext/assets/doctype/asset_maintenance_task/asset_maintenance_task.json — takvim periyodu, kalibrasyon, sertifika [SRC]
- [K-84] asset_repair.json — https://github.com/frappe/erpnext/blob/develop/erpnext/assets/doctype/asset_repair/asset_repair.json — duruş, parça tüketimi, maliyet [SRC]
- [K-85] vehicle.json — https://github.com/frappe/erpnext/blob/develop/erpnext/setup/doctype/vehicle/vehicle.json — sigorta/poliçe alanları [SRC]
- [K-86] frappe/model/document.py — https://github.com/frappe/frappe/blob/develop/frappe/model/document.py — docstatus, amend, iyimser kilit [SRC]
- [K-87] version.py — https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/version/version.py — alan farkı geçmişi [SRC]
- [K-88] docperm.json — https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/docperm/docperm.json — izin türleri [SRC]
- [K-89] user_permission.json — https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/user_permission/user_permission.json — kayıt değeri bazlı kısıt [SRC]
- [K-90] deleted_document.json — https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/deleted_document/deleted_document.json — silinen belge anlık görüntüsü [SRC]
- [K-91] frappe/hooks.py (+ auto_repeat.json) — https://github.com/frappe/frappe/blob/develop/frappe/hooks.py ; https://github.com/frappe/frappe/blob/develop/frappe/automation/doctype/auto_repeat/auto_repeat.json — zamanlayıcı türleri, tarih uyarıları, tekrarlayan belge [SRC]
- [K-92] erpnext/hooks.py — https://github.com/frappe/erpnext/blob/develop/erpnext/hooks.py — repost ve bakım zamanlamaları [SRC]
- [K-93] background_jobs.py — https://github.com/frappe/frappe/blob/develop/frappe/utils/background_jobs.py — RQ kuyrukları, deduplicate [SRC]
- [K-110] accounting_dimension.json — https://github.com/frappe/erpnext/blob/develop/erpnext/accounts/doctype/accounting_dimension/accounting_dimension.json — kullanıcı tanımlı analiz boyutu [SRC]
- [K-111] hrms vehicle_log.json — https://github.com/frappe/hrms/blob/develop/hrms/hr/doctype/vehicle_log/vehicle_log.json — araç kaydı [SRC]

**Odoo**
- [K-73] GitHub API: odoo/odoo — https://api.github.com/repos/odoo/odoo — varsayılan dal 20.0, son push [SRC]
- [K-74] Odoo LICENSE — https://github.com/odoo/odoo/blob/master/LICENSE — LGPL-3.0 [SRC]
- [K-94] stock_location.py — https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_location.py — lokasyon türleri (transit, envanter kaybı) [SRC]
- [K-95] stock_quant.py — https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_quant.py — sayım alanları, planlı sayım tarihi [SRC]
- [K-96] stock_warehouse.py — https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_warehouse.py — depo besleme, transit lokasyon [SRC]
- [K-97] stock_orderpoint.py — https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_orderpoint.py — min/maks, erteleme, manuel tetik [SRC]
- [K-98] purchase_order.py — https://github.com/odoo/odoo/blob/20.0/addons/purchase/models/purchase_order.py — PO durumları, kilit, eşik onayı [SRC]
- [K-99] purchase_order_line.py — https://github.com/odoo/odoo/blob/20.0/addons/purchase/models/purchase_order_line.py — teslim/fatura miktarları [SRC]
- [K-100] Odoo docs: control_bills.rst — https://github.com/odoo/documentation/blob/19.0/content/applications/inventory_and_mrp/purchase/manage_deals/control_bills.rst — 3-way matching [DOC]
- [K-101] fleet_vehicle_log_contract.py — https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle_log_contract.py — sözleşme, tekrarlayan maliyet, 30 gün uyarı [SRC]
- [K-102] fleet_vehicle.py — https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle.py — planlı atama, kilometre logu [SRC]
- [K-103] fleet_data.xml — https://github.com/odoo/odoo/blob/20.0/addons/fleet/data/fleet_data.xml — Omnium/Leasing, maliyet üretim eylemi [SRC]
- [K-104] maintenance.py — https://github.com/odoo/odoo/blob/20.0/addons/maintenance/models/maintenance.py — takvim tekrarı, MTBF/MTTR [SRC]
- [K-105] fleet/models dizini (GitHub API) — https://api.github.com/repos/odoo/odoo/contents/addons/fleet/models?ref=20.0 — yakıt modeli yok [SRC]
- [K-176] repair.py — https://github.com/odoo/odoo/blob/20.0/addons/repair/models/repair.py — garanti, parça hazır durumu [SRC]
- [K-106] mail_thread.py — https://github.com/odoo/odoo/blob/20.0/addons/mail/models/mail_thread.py — alan izleme, mesaj [SRC]
- [K-107] mail_activity_mixin.py — https://github.com/odoo/odoo/blob/20.0/addons/mail/models/mail_activity_mixin.py — aktiviteler [SRC]
- [K-108] Odoo docs: security.rst — https://github.com/odoo/documentation/blob/19.0/content/developer/reference/backend/security.rst — global/grup kuralları [DOC]
- [K-109] ir_cron.py — https://github.com/odoo/odoo/blob/20.0/odoo/addons/base/models/ir_cron.py — hata sayacı, otomatik devre dışı [SRC]
- [K-112] Odoo docs: count_products.rst — https://github.com/odoo/documentation/blob/19.0/content/applications/inventory_and_mrp/inventory/warehouses_storage/inventory_management/count_products.rst — sayım ekranı [DOC]
- [K-171] Odoo 19.0: Maintenance requests — https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/maintenance/maintenance_requests.html — talep aşamaları [DOC]

**Diğer açık kaynak projeler**
- [K-113] Snipe-IT GitHub — https://github.com/grokability/snipe-it — AGPL-3.0, sürüm [SRC]
- [K-114] Snipe-IT Asset.php — https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php — zimmet, sayım tarihleri, çok biçimli atama, soft delete [SRC]
- [K-115] Snipe-IT CheckoutAcceptance.php — https://github.com/grokability/snipe-it/blob/master/app/Models/CheckoutAcceptance.php — imzalı kabul, yarış koruması [SRC]
- [K-116] Snipe-IT Kernel.php — https://github.com/grokability/snipe-it/blob/master/app/Console/Kernel.php — günlük uyarı işleri, haftalık yedek [SRC]
- [K-117] Snipe-IT Actionlog.php — https://github.com/grokability/snipe-it/blob/master/app/Models/Actionlog.php — eylem günlüğü, işlem kaynağı [SRC]
- [K-118] Snipe-IT Consumable.php — https://github.com/grokability/snipe-it/blob/master/app/Models/Consumable.php — asgari miktar [SRC]
- [K-119] Atlas CMMS GitHub (README) — https://github.com/Grashjs/cmms — özellikler, yığın [MKT]/[SRC]
- [K-120] Atlas CMMS COMMERCIAL_LICENSE — https://github.com/Grashjs/cmms/blob/main/COMMERCIAL_LICENSE.MD — çift lisans [SRC]
- [K-121] Atlas WorkOrderBase.java — https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/abstracts/WorkOrderBase.java — ortak iş emri şablonu [SRC]
- [K-122] Atlas WorkOrder.java — https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/WorkOrder.java — imza, ilk müdahale süresi [SRC]
- [K-123] Atlas Request.java — https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Request.java — sesli not, talep portalı [SRC]
- [K-124] Atlas Schedule.java — https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Schedule.java — takvim tekrarı [SRC]
- [K-125] Atlas Meter / Reading / WorkOrderMeterTrigger — https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/WorkOrderMeterTrigger.java ; https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Reading.java — sayaç tetikleyici, okuma geçmişi [SRC]
- [K-126] Atlas AssetDowntime.java — https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/AssetDowntime.java — duruş [SRC]
- [K-127] Atlas Part.java — https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Part.java — asgari miktar, version kilidi [SRC]
- [K-128] Fleetbase GitHub — https://github.com/fleetbase/fleetbase — AGPL + FCL, sürüm [SRC]/[MKT]
- [K-129] Fleetbase MaintenanceSchedule.php — https://github.com/fleetbase/fleetops/blob/main/server/src/Models/MaintenanceSchedule.php — tarih/km/motor saati planı [SRC]
- [K-130] Fleetbase Maintenance.php — https://github.com/fleetbase/fleetops/blob/main/server/src/Models/Maintenance.php — maliyet kırılımı [SRC]
- [K-131] Fleetbase FuelReport.php — https://github.com/fleetbase/fleetops/blob/main/server/src/Models/FuelReport.php — yakıt alanları [SRC]
- [K-132] Fleetbase InspectionSubmission.php — https://github.com/fleetbase/fleetops/blob/main/server/src/Models/InspectionSubmission.php — kontrol → arıza → iş emri [SRC]
- [K-133] Dolibarr GitHub — https://github.com/Dolibarr/dolibarr — GPL-3.0 [SRC]
- [K-134] Dolibarr productstockentrepot.class.php — https://github.com/Dolibarr/dolibarr/blob/develop/htdocs/product/stock/class/productstockentrepot.class.php — depo bazında uyarı/istenen stok [SRC]
- [K-135] Dolibarr wiki: Field Interventions — https://wiki.dolibarr.org/index.php/Field_Interventions_for_Dolibarr — ücretli saha eklentileri [SEC]
- [K-136] Shelf.nu GitHub — https://github.com/Shelf-nu/shelf.nu — QR, rezervasyon, kit, yığın [MKT]
- [K-137] openMAINT 2.4 duyurusu — https://www.openmaint.org/en/resources/news/openmaint-2-4-release-now-available — sürüm içeriği [MKT]
- [K-138] openMAINT features — https://www.openmaint.org/en/product/features — modüller, yığın [MKT]
- [K-139] GitHub arama: avalonia erp — https://api.github.com/search/repositories?q=avalonia%20erp&sort=stars — olgun Avalonia ERP yokluğu [SRC]
- [K-140] Gnomeshade — https://github.com/VMelnalksnis/Gnomeshade — tek bakımlı Avalonia veri uygulaması [SRC]
- [K-141] InvenTree — https://github.com/inventree/InvenTree — MIT, sürüm [SRC]
- [K-169] Fleetbase FleetOps models dizini — https://github.com/fleetbase/fleetops/tree/main/server/src/Models — model listesi, company_uuid [SRC]
- [K-172] iDempiere GitHub — https://github.com/idempiere/idempiere — metaveri [SRC]
- [K-173] ADempiere GitHub — https://github.com/adempiere/adempiere — GPL-2.0, son sürüm 2023 [SRC]
- [K-174] Tryton stock docs — https://docs.tryton.org/latest/modules-stock/index.html — stok kavramları [DOC]

**Çevrimdışı senkron, performans ve yedek**
- [K-142] PowerSync: Handling Update Conflicts — https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts — sıralı kuyruk, idempotency, işlem ID [DOC]
- [K-143] PowerSync: Custom Conflict Resolution — https://docs.powersync.com/usage/lifecycle-maintenance/handling-update-conflicts/custom-conflict-resolution — saat kayması, delta stok, red ve durum gösterimi [DOC]
- [K-144] Replicache: How it works — https://doc.replicache.dev/concepts/how-it-works — tam bir kez uygulama, cookie, rebase, poke [DOC]
- [K-145] Datasync Community Toolkit: client in-depth — https://communitytoolkit.github.io/Datasync/in-depth/client/ — operations queue, delta token, sırasızlık uyarısı [DOC]
- [K-146] Datasync Community Toolkit ana sayfa — https://communitytoolkit.github.io/Datasync/ — .NET 10 gereksinimi, Avalonia desteği [DOC]
- [K-147] CouchDB Replication Protocol — https://docs.couchdb.org/en/stable/replication/protocol.html — checkpoint, tombstone, tekrarlanmayacak hata kodları [DOC]
- [K-148] PostgreSQL: Sequence Functions — https://www.postgresql.org/docs/current/functions-sequence.html — sequence boşlukları [DOC]
- [K-149] pgsql-general: xmin/watermark tartışmaları — https://postgrespro.com/list/thread-id/1506035 ; https://postgrespro.com/list/thread-id/1531732 — uzun transaction'da kaçan satır [SEC]
- [K-150] Fivetran: query-based / XMIN connector — https://fivetran.com/docs/connectors/databases/postgresql/troubleshooting/query-based-connector-not-syncing ; https://fivetran.com/docs/connectors/databases/postgresql/troubleshooting/xmin-connector-not-syncing — uzun transaction beklemesi [DOC]
- [K-151] Dotmim.Sync: Conflicts — https://dotmimsync.readthedocs.io/Conflict.html — silmeye özgü çakışmalar, ServerWins [DOC]
- [K-152] D365 Field Service: offline data sync — https://learn.microsoft.com/en-us/dynamics365/field-service/mobile/offline-data-sync — tablo düzeyi çakışma, kapsam dışı kayıt temizliği, durum ekranları [DOC]
- [K-153] ElectricSQL: Writes guide — https://electric.ax/docs/guides/writes — yazma desenleri, nedensel geri alma [DOC]
- [K-154] Ink & Switch: Local-first software — https://www.inkandswitch.com/essay/local-first/ — CRDT sınırları [SEC]
- [K-155] AWS Builders' Library: Timeouts, retries and backoff with jitter — https://builder.aws.com/content/3EumjoZascWd1oZiEgL8ORlv3qE/timeouts-retries-and-backoff-with-jitter — yeniden deneme ilkeleri [DOC]
- [K-156] MongoDB forum / docs: Atlas Device Sync EOL — https://mongodb.com/community/forums/t/atlas-device-sync-end-of-life-and-deprecation/296687 ; https://mongodb.com/docs/atlas/app-services/sync/device-sync-deprecation — kapanış tarihleri [DOC]/[SEC]
- [K-157] SQLite: Query Planning — https://www.sqlite.org/queryplanner.html — kapsayan/bileşik indeks [DOC]
- [K-158] SQLite: PRAGMA — https://www.sqlite.org/pragma.html — optimize, synchronous, checkpoint, integrity/quick/foreign_key_check [DOC]
- [K-159] SQLite: Write-Ahead Logging — https://www.sqlite.org/wal.html — WAL büyümesi, ağ sürücüsü uyarısı [DOC]
- [K-160] SQLite: FTS5 — https://www.sqlite.org/fts5.html — trigram, rebuild/optimize [DOC]
- [K-161] PostgreSQL: pg_trgm — https://www.postgresql.org/docs/current/pgtrgm.html — trigram indeks [DOC]
- [K-162] Vlad Mihalcea: SQL Seek/Keyset Pagination — https://vladmihalcea.com/sql-seek-keyset-pagination — keyset vs OFFSET [SEC]
- [K-163] CockroachDB: Pagination — https://www.cockroachlabs.com/docs/stable/pagination.md — keyset önerisi [DOC]
- [K-164] Supabase: Connecting to Postgres — https://supabase.com/docs/guides/database/connecting-to-postgres — doğrudan/session/transaction pooler [DOC]
- [K-165] Avalonia: Performance optimization / Improving performance — https://docs.avaloniaui.net/docs/app-development/performance ; https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance — sanallaştırma, DataGrid/TreeDataGrid [DOC]
- [K-166] Avalonia issue #6904 / discussion #15600 — https://github.com/AvaloniaUI/Avalonia/issues/6904 ; https://github.com/AvaloniaUI/Avalonia/discussions/15600 — ItemsRepeater tüm öğeleri okuma [SEC]
- [K-167] SQLite: Online Backup API — https://www.sqlite.org/backup.html — tutarlı yedek, VACUUM INTO [DOC]
- [K-168] ECOSIRE: ERPNext offline POS — https://ecosire.com/apps/erpnext/erpnext-offline-pos — idempotency anahtarı, inceleme kuyruğu [MKT]

**Bilinen boşluklar (kaynakla kapatılamayanlar):** HCSS, B2W, Tenna ve Clue'nun alan düzeyinde resmî kılavuzları; Logo/Netsis/Mikro/ETA/Canias'ın resmî inşaat sayfaları; 2026 e-İrsaliye eşiği ve firma içi taşımada irsaliye zorunluluğunun tam metni; Ek-III periyotları; KDV tevkifatı ve cari ekstre beklentisinin ürün belgesi; çevrimdışı istemciler varken şema geçişi için birincil kaynak; onaycı vekâleti; kiralama ayrıntıları (iade/off-rent); telematik yoklama sıklığı ve tekilleştirme. Bu maddeler (doğrulanmadı) olarak değerlendirilmelidir.
