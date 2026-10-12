# Türkiye inşaat ERP / şantiye yönetimi / hakediş yazılımları ve yerel beklentiler (erişim: 2026-10-12)

Kapsam notu: Kaynakların büyük kısmı üretici siteleri ve üretici blogları — yani özellik iddiaları "üretici beyanı"dır, bağımsız doğrulanmamıştır. Bağımsız kullanıcı yorumu / bağımsız karşılaştırma bulunamadı. Arama araç bütçesi (~20 çağrı) nedeniyle Logo, Netsis, Mikro, ETA, Canias, IAS, Workcube, Univera'nın resmi inşaat sayfaları doğrudan doğrulanamadı (Gaps bölümüne bakın).

## 1. Türkiye'de inşaat firmalarına şantiye/depo/ekipman/hakediş için hizmet veren ürünler ve özellikleri

### Takeaway
Türkiye pazarı üç katmanlı: (a) inşaata özel ERP'ler (Yapıtaşı ERP, KonstraERP, e-MOR, Navappro, Qera, Uyumsoft şantiye programı, DİA+Mavvo) — ortak çekirdek **hakediş (işveren + taşeron), sözleşme/teminat/avans/kesinti, bütçe-gerçekleşen maliyet, satınalma, şantiye ambarı, puantaj**; (b) genel ERP'ler (Logo, Netsis, Mikro) — e-dönüşüm ve muhasebe güçlü, inşaat derinliği özelleştirme/iş ortağı ile; (c) masaüstü hakediş/yaklaşık maliyet programları (Oska HakedişPlus+, AMP, e-HakedişNet) — kamu ihale (4734) odaklı metraj/keşif/hakediş. Alpnex'in güçlü olduğu şantiye-depo + filo/iş makinesi + zimmet + offline alanı rakiplerde ya hiç yok ya da ikincil; Alpnex'te ise **hakediş/taşeron/sözleşme/puantaj** hattı yok.

### Cited Findings

**Yapıtaşı ERP (inşaata özel ERP)**
- Modül/süreç adları: "Lojistik ve Ambar/Depo Yönetim Süreci" (mal kabul, stok, kritik stok, proje depoları arası transfer), "Şantiye Uygulama İzleme ve Raporlama Süreci", "Satınalma Yönetimi Modülü" (malzeme talebi, iş programına göre ihtiyaç planlama, teklif toplama/karşılaştırma, sipariş, onay akışı, uyarılar) — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- İrsaliye kayıtları siparişe bağlanıyor; fatura ↔ sipariş ↔ mal kabul eşleştirmesi (3'lü eşleştirme) — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- "Taşeron Yönetimi modülü": taşeron sözleşmesi, avans, teminat, hakediş, kesintiler; döviz bazlı sözleşmeler ve onay süreçleri (arama özeti) — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- "İşveren Sözleşme ve Hakediş Yönetim Süreci": işverene kesilen hakediş planı, avanslar — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- Metraj ayrı modül değil; BIM'den gelen metrajlar teklif/ihale modülüne aktarılabiliyor — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- "Proje Personel Takibi": günlük şantiye puantajı, aktivite bazlı saat, fazla mesai — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- "Makina Ekipman ve Demirbaş (Sabit Kıymet) Yönetim Süreci": günlük kullanım kaydı, tamir, yedek parça, amortisman, şantiyeler arası transfer; yakıt/yağ tüketimi bu modülde; demirbaş kartı, zimmet durumu, ekipman kiralama, kullanım oranı raporları — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- Muhasebe modülü (mizan, defterler, gelir tablosu, bilanço, dönem açma/kapama, kur farkı) — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- Teknoloji: IBM Collaboration Solutions platformu; "bağlantı geri geldiğinde veriyi senkronize eder" ifadesi var; mobil uygulama platformu adı geçiyor ama mobil özellikler tarif edilmiyor. e-Fatura/e-İrsaliye sayfada geçmiyor — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)

**KonstraERP (bulut SaaS, inşaata özel)**
- 16 modül: Hakediş & İmalat; e-İhale & Sözleşme; Bütçe & Maliyet; Satınalma (talep, sipariş, tedarikçi performansı); Metraj & Keşif (mahal bazlı metraj cetvelleri, keşif özeti, birim fiyat); Şantiye Günlüğü (mobil günlük, fotoğraf, ekip puantajı); Personel & Bordro; Finans & Ticari (fatura, çek, banka); Onay Merkezi; Dokümanlar (sürümlü); Kalite Kontrol (denetim, uygunsuzluk); İSG (eğitim, ramak kala, denetim); Satış Merkezi (daire/dükkan stoku, satış sözleşmesi); Mesajlaşma; Konstra Zeka (doğal dil sorgu, anomali); Kütüphane — [KonstraERP](https://www.konstraerp.com/)
- Taşeron portalı: taşeron kendi hakedişini, kesintilerini, onay durumunu görür — [KonstraERP](https://www.konstraerp.com/)
- Ayrı ürün "Konstra Hafriyat": sefer takibi, kantar fişi, kamyoncu ödemesi ve otomatik mazot kesintisi — [KonstraERP](https://www.konstraerp.com/)
- Birim fiyat kütüphanesi: ÇŞB, İLBANK, DSİ, KGM resmi birim fiyatları — [KonstraERP](https://www.konstraerp.com/)
- Muhasebe entegrasyonu: hazır konnektör yok; resmi defter Logo/Netsis/Mikro'da kalabilir; Excel şablonu ve e-Fatura XML içe aktarma — [KonstraERP](https://www.konstraerp.com/)
- Depo, iş makinesi, yakıt (Hafriyat dışında), offline sayfada yok — [KonstraERP](https://www.konstraerp.com/)
- Fiyat (USD/ay, liste): Starter 290$ taban + 18$/kullanıcı (≤10 kullanıcı, 1 proje); Pro 690$ + 24$/kullanıcı (≤50, 5 proje); Business 1.490$ + 28$/kullanıcı (≤200, sınırsız proje); Enterprise teklif; yıllıkta %20 indirim; TL ödeme TCMB kuruyla; barındırma Frankfurt; 14 gün kartsız pilot — [KonstraERP](https://www.konstraerp.com/)

**Uyumsoft — "Şantiye ve İnşaat Takip Programı"**
- Özellikler: Depo Yönetimi (şantiye deposuna giren/çıkan malzeme), Satın Alma Yönetimi (malzeme/hizmet talebinden itibaren), Hakediş ve Taşeron Yönetimi, Taşeron Takibi (taşeron ekiplerinin puantajı ve ödemeleri), Alt Yüklenici Takibi, Şantiye Yönetimi (günlük operasyon, görev atama), Sözleşme Yönetimi, Maliyet Yönetimi, Müşteri Yönetimi (alıcı ödeme planları), İK (PDKS, avans, izin), çoklu para birimi, bütçe/kâr analizi — [Uyumsoft](https://www.uyumsoft.com/santiye-insaat-takip-programi)
- e-Fatura/e-İrsaliye bu programın özelliği olarak değil, sitede ayrı hizmet olarak duruyor; iş makinesi/yakıt/demirbaş/mobil-offline sayfada yok; fiyat yok ("Demo Talep Edin") — [Uyumsoft](https://www.uyumsoft.com/santiye-insaat-takip-programi)
- Arama özeti: şantiye deposundaki demir/çimento gibi malzemelerin stok hareketleri ve Liox ERP entegrasyonu (arama motoru özeti; sayfa çekiminde Liox ifadesi teyit edilmedi) — [Uyumsoft](https://www.uyumsoft.com/santiye-insaat-takip-programi)

**Qera ERP — "Şantiye Bazlı Maliyet ve Hakediş Programı"**
- Proje bütçesi/iş kalemleri/gerçekleşen maliyet; hakedişin imalat ilerlemesine bağlanması, kesinti, avans mahsubu, ödeme; taşeron sözleşme/hakediş; şantiye satınalma talebi-sipariş-teslim; Stok & Depo/WMS (şantiye giriş-çıkış, sarf); finans, muhasebe, iş zekası (proje ve şantiye bazında nakit akışı/kârlılık). Puantaj, iş makinesi, yakıt, inşaata özel e-fatura/mobil sayfada yok; fiyat yok — [Qera ERP](https://qera.com.tr/santiye-bazli-maliyet-ve-hakedis-programi/)

**DİA (bulut ERP) + Mavvo (iş ortağı) inşaat çözümü**
- Belgelenen: taşeron iş ilerleme raporu girilerek hakediş hesabı, proje bazlı ödeme planı, onay bildirimleri; şantiye depo giriş/çıkış, sarf, düşük stok uyarısı, günlük şantiye raporu; taşeron fatura/ödeme/sözleşme; "Puantaj ve Bordro Yönetimi", vardiya planı; makine envanteri (model, seri no, ömür), şantiye bazlı tahsis, bakım takvimi/hatırlatma, arıza kaydı, yedek parça stoku, saat/yakıt/verimlilik analizi; mobil saha veri girişi. "Hatasız" vb. ifadeler pazarlama. Makale 30.11.2024 tarihli (başlıkta 2026) — [Mavvo Blog](https://mavvo.com.tr/blog/insaat-erp/)

**Navappro (Microsoft Dynamics 365 Business Central üzerine dikey çözüm) ve bir üretici karşılaştırması**
- Navapp'ın kendi blogu (taraflı): sektöre özel — Yapıtaşı ERP, e-MOR, Canias, KonstraERP, Factorbooks; genel ERP + inşaat uyarlaması — Logo, Netsis, Mikro, DİA; platform + dikey — BC + Navappro — [Navapp karşılaştırma](https://www.navapp.com.tr/blog/insaat-erp-programlari-karsilastirma)
- Aynı kaynağa göre: Logo'da e-Fatura/e-Arşiv/e-Defter/e-İrsaliye yerleşik, inşaat özelleştirme ile; Mikro düşük giriş maliyeti ama sınırlı inşaat derinliği; e-MOR hakediş, taşeron, ambar, bordro, ekipman ve faaliyet bazlı maliyet; Yapıtaşı'nda iş makinesi kullanım takibi; hiçbir ürün için offline/mobil ve teminat mektubu değerlendirilmemiş; fiyat verilmemiş — [Navapp karşılaştırma](https://www.navapp.com.tr/blog/insaat-erp-programlari-karsilastirma)
- Navappro: hakediş, sözleşme, teminat, avans, kesinti yönetimi (arama özeti) — [Navapp inşaat ERP](https://www.navapp.com.tr/blog/insaat-erp-yazilimi)

**Parsek ERP** — malzeme talebi, satınalma siparişi, tedarikçi süreçleri; şantiye ve merkez depolarının barkodla izlenmesi (arama özeti, sayfa çekilmedi) — [Parsek blog](https://parsekpro.com/blog/insaat-erp-nedir-santiye-yonetiminde-dijitallesme)

**Oska Yazılım (hakediş / yaklaşık maliyet programları)**
- HakedişPlus+ (masaüstü: yaklaşık maliyet, hakediş, kesin hesap; Server sürümü, öğrenci lisansı), OSKABULUT (web: BirimFiyatlar, MaliyetBulut, HakedişBulut), BirimFiyat (mobil), HM-Hakediş (mal/hizmet alımı hakedişi: temizlik, yemek, güvenlik, akaryakıt vb.), e-DoğrudanTemin ve e-kikNet (4734 kamu ihale/doğrudan temin, kamu idareleri için), YMH yönetici konsolu. Fiyat yok; paket alana en az 365 gün bakım/destek/yeni sürüm; Windows 8/10/11 — [Oska](https://www.oska.com.tr)
- 2008 tarihli Oska sunumu: 18 yıllık deneyim, 3.000+ kullanıcı (eski veri) — [EMO eki PDF](https://www.emo.org.tr/ekler/b771bf001f38848_ek.pdf?tipi=2)
- e-HakedişNet: ~70.000 poz ve birim fiyat analizi; maliyet analizi, nakliye hesapları, fiyat farkı, kesin hesap (indirme sitesi listesi, ikincil) — [Tamindir e-HakedişNet](https://www.tamindir.com/indir/e-hakedisnet/)
- AMP Hakediş ve Yaklaşık Maliyet: metraj, demir/profil metrajı, nakliye metrajı (ikincil) — [Tamindir AMP](https://www.tamindir.com/indir/amp-hakedis-ve-yaklasik-maliyet-programi/)
- Yaklaşık maliyet adımları: metraj cetvelleri, birim fiyat analizleri, yüzde hesapları, fiyat farkı ağırlık oranları; Excel'de zor (üretici yazısı) — [hakedis.org](https://www.hakedis.org/yaklasik-maliyet-hakedis-hesaplari-icin-program-kullaniminin-avantajlari/)

**Küçük/yeni mobil ürünler (yalnız mağaza kayıtları, doğrulanması sınırlı)**
- Sitabula: "Şantiye Yönetim Platformu", metraj→hakediş hattını Excel'den çıkarma, mobilden anlık saha kaydı (arama özeti; sitabula.com DNS çözülmedi) — [App Store Sitabula](https://apps.apple.com/pk/app/sitabula/id6757933988)
- ŞantiyeX: taşeron/usta/tedarikçi kayıtları, ödeme hatırlatmaları, aşama bazlı ilerleme (arama özeti) — [App Store ŞantiyeX](https://apps.apple.com/mx/app/santiyex/id6743945925)
- Axoft Proje Yönetim Programı: şantiyelere giden/kalan malzeme, depo hareket kayıtları (indirme sitesi) — [Tamindir Axoft](https://www.tamindir.com/indir/axoft-proje-yonetim-programi/amp/)

**Genel ERP'ler**
- Logo: 1.000+ iş ortağı, 230.000+ müşteri (2026/03 faaliyet raporu); sektörler arasında inşaat sayılıyor ama Tiger için ayrı inşaat/hakediş modülü kaynakta tanımlanmıyor — [Logo Faaliyet Raporu 31 Mart 2026](https://cdn.logo.com.tr/Files/logocomtr/Uploads/Documents/Faaliyet-Raporu-31-Mart-2026.pdf); [techcareer Logo profili](https://www.techcareer.net/sirketler/logo-yazilim)
- Mikro: inşaat hakediş modülü kaynağı bulunamadı; şantiye muhasebecisi ilanlarında Mikro kullanımı isteniyor (dolaylı: şantiyelerde muhasebe tarafında yaygın) — [Kariyer.net ilan](https://www.kariyer.net/is-ilani/gonuc-insaat-santiye-muhasebecisi-4511465)
- TMB "İnşaat Sektöründe ERP Kullanımı" raporu (2015): inşaatın parçalı yapısı sektöre özel çözüm ihtiyacı doğuruyor — [TMB raporu](https://www.tmb.org.tr/uploads/publications/6065b68d4e2c483e72ff8fe5/1617278600878-erp-icg-r-1.pdf)

**İş makinesi / puantaj**
- Türkçe, iş makinesi çalışma saati + yakıt + kiralık makine puantajına özel bağımsız bir Türk ürünü aramalarda bulunamadı; bu işlev ERP'lerin içinde (Yapıtaşı, DİA/Mavvo) ya da telematik/GPS sağlayıcılarında (Teltonika vb.) — [Teltonika ağır araç](https://www.teltonika-gps.com/tr/use-cases/agriculture/management-of-heavy-duty-vehicles-with-ble-accessories); [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- Jibble (yabancı, Türkçe): şantiye personel puantajı, yüz tanıma/NFC kiosk, internetsiz giriş-çıkış ve sonra senkron — [Jibble inşaat zaman saati](https://www.jibble.io/tr/insaat-zaman-saati)

### Inferences
- Türk inşaat ERP'lerinin "olmazsa olmaz" ortak paydası **hakediş** (işveren hakedişi + taşeron hakedişi, avans mahsubu, kesinti, teminat, fiyat farkı) ve **bütçe/gerçekleşen maliyet**. Alpnex'te bu yok; Alpnex'i "inşaat ERP" diye konumlamak bu beklentiyi doğurur. Alternatif konumlama: "şantiye depo + ekipman/filo operasyon ERP'si" ve hakedişi muhasebe/hakediş programına bırakmak (KonstraERP'nin Logo/Netsis/Mikro'yu resmi defter için bırakması gibi).
- Rakiplerin çoğunda **iş makinesi/filo, yakıt, zimmet ve offline** zayıf veya yok (KonstraERP, Uyumsoft, Qera sayfalarında yok). Bu Alpnex'in farklılaştırıcısı; Yapıtaşı ve DİA/Mavvo bu alanda en yakın rakipler.
- Alpnex'e eklenmesi en yüksek değerli yerel işlevler (öncelik tahmini): (1) taşeron sözleşmesi + taşeron hakedişi/kesinti (en azından taşeron cari + iş ilerleme), (2) şantiye/personel puantajı (Alpnex'te personel ve günlük aktivite zaten var), (3) proje bazlı bütçe vs gerçekleşen maliyet (maliyet merkezleri zaten var), (4) iş makinesi puantajı (kiralık makine saat × birim fiyat → kiraya veren cariye hakediş/fatura), (5) taşeron portalı. Metraj/keşif/resmi birim fiyat kütüphanesi (ÇŞB vb.) uzman hakediş programlarının alanı — Alpnex için düşük öncelik.
- Fiyat referansı: KonstraERP Pro ~690$/ay taban — SaaS inşaat yazılımlarında USD bazlı fiyatlama görülüyor.

### Gaps
- Logo (Tiger/j-guar/Netsis), Mikro, ETA, Canias, IAS, Workcube, Univera, Nebim'in resmi inşaat/şantiye sayfaları bu çalışmada doğrulanamadı (doğrulanmadı / unverified). Logo'nun inşaat derinliği muhtemelen iş ortağı dikey çözümleriyle — kaynak yalnız Navapp blogu (taraflı).
- e-MOR'un güncel modülleri için resmi kaynak bulunamadı (yalnız 2013 akademik bildiri ve Navapp blogu) — [AB13 bildirisi](https://www.ab.org.tr/ab13/kitap/baloglu_AB13.pdf).
- "Şantiye Defteri", "Planet", "Proda", "Teklifim" adlı ürünler aramalarda çıkmadı (doğrulanmadı).
- Hiçbir rakip için offline çalışma kapasitesi açıkça belgelenmemiş (Yapıtaşı'nın "bağlantı geri gelince senkron" ifadesi tek ipucu).
- Bağımsız kullanıcı yorumu/pazar payı verisi bulunamadı.

## 2. Türk mevzuat/entegrasyon beklentileri (e-belge, irsaliye, İSG periyodik kontrol, araç)

### Takeaway
e-İrsaliye, belirli ciro eşiğini aşan e-Fatura mükellefleri ve belirli sektörler için zorunlu; zorunlu mükellef tüm sevk irsaliyelerini (alıcı kayıtlı olmasa bile) e-İrsaliye düzenlemek zorunda — bu, Alpnex'teki depolar arası transfer/şantiyeye sevk akışını doğrudan ilgilendirir. Kaldırma ve iletme ekipmanları (vinç vb.) İş Ekipmanları Yönetmeliği Ek-III kapsamında periyodik kontrole tabi; Aralık 2025'te değişiklik yapıldı. Kesin süre değerleri bu çalışmada resmi metinden doğrulanamadı.

### Cited Findings
- 509 Sıra No.lu VUK Genel Tebliği (RG 19.10.2019, 30923) e-belge düzenlemelerini birleştirdi; e-İrsaliye zorunluluğu ilk aşamada enerji, petrokimya, maden, şeker, gübre, demir-çelik ve 2018+ brüt satış hasılatı 25 milyon TL ve üzeri mükellefler için — [Andersen bülteni PDF](https://tr.andersen.com/tr/images/pdf/2020-42%20e-irsal%C4%B1ye-uygulamas%C4%B1na-gec%C4%B1s.pdf); [Alomaliye](https://www.alomaliye.com/2020/07/17/e-irsaliye-kilavuzu-guncellendi/)
- 535 Sıra No.lu VUK Genel Tebliği bilgi notu: demir-çelik faaliyeti şartsız dahil, ciro şartı 10 milyon TL olarak belirlendi — [GİB 535 bilgi notu PDF](https://gib.gov.tr/sites/default/files/fileadmin/mevzuatek/mevzuatbilginotu/535_serno_vukgenteb_abn.pdf)
- Danışmanlık kaynağına göre 2023+ dönemlerde 10 milyon TL hasılat aşan mükellefler 1 Temmuz 2024'e kadar e-İrsaliyeye geçmeli (ikincil kaynak; 2026 güncel eşik doğrulanmadı) — [Paraşüt e-İrsaliye başvuru](https://www.parasut.com/blog/e-irsaliye-basvurusu-nasil-yapilir)
- Zorunlu mükellefler, tebliğin istisnaları dışında sevk irsaliyelerini e-İrsaliye olarak düzenlemek zorunda; matbu irsaliye istisnalar dışında kullanılamaz; zorunlu kullanıcı alıcı kayıtlı olmasa bile tüm sevk irsaliyelerini e-İrsaliye düzenler — [Alomaliye](https://www.alomaliye.com/2020/06/15/e-irsaliye-uygulamasinin-kapsami-genisliyor/); [Paraşüt SSS](https://www.parasut.com/blog/e-irsaliye-hakkinda-sikca-sorulan-sorular)
- GİB 2021 duyurusu: 526 Sıra No.lu tebliğ yalnız ciro şartının ne zaman sağlandığını netleştirdi — [GİB node PDF](https://gib.gov.tr/node/151354/pdf)
- İş Ekipmanlarının Kullanımında Sağlık ve Güvenlik Şartları Yönetmeliği (RG 25.04.2013, 28628): periyodik kontrol yalnız Ek-III tablolarındaki ekipmanlar için zorunlu; kontroller yetkili kişilerce — [ÇSGB duyurusu 24.12.2025](https://csgb.gov.tr/isggm/duyurular/24122025/); [mevzuat.gov.tr](https://www.mevzuat.gov.tr/mevzuat?MevzuatNo=18318&MevzuatTur=7&MevzuatTertip=5)
- Ek-III 2.2.3 Tablo-2 kaldırma ve iletme ekipmanlarının periyodik kontrol süre/kriterlerini içerir; 2017 değişikliği vinçler için ASME B30.17 atfı ekledi — [Alomaliye 2017](https://www.alomaliye.com/2017/04/24/is-ekipmanlarinin-kullaniminda-saglik-ve-guvenlik/)
- Aralık 2025 değişikliği: işveren ile yetkili kişi arasında periyodik kontrolden en geç 1 gün önce İSG-KATİP'teki örneğe uygun sözleşme; Ek-III dışı ekipmanların gönüllü kontrol raporlarında şekil/sözleşme şartı aranmaz — [ÇSGB duyurusu](https://csgb.gov.tr/isggm/duyurular/24122025/); [Alomaliye 23.12.2025](https://www.alomaliye.com/2025/12/23/is-ekipmanlarinin-kullaniminda-saglik-ve-guvenlik-sartlari-degisiklik-23-12-2025/)
- Rakip ürünlerde İSG modülü örneği: KonstraERP "İSG" (eğitim, ramak kala, denetim), "Kalite Kontrol" — [KonstraERP](https://www.konstraerp.com/)

### Inferences
- Alpnex'in "şantiyeye sevk / depolar arası transfer" hareketleri vergi açısından sevk irsaliyesi gerektirebilir; e-İrsaliye zorunlusu bir müşteri için bu hareketin özel entegratör üzerinden e-İrsaliye'ye dönüşmesi (ya da en azından irsaliye no/tarihi alanı ve XML/UBL dışa aktarımı) beklenir. Bu, Alpnex için yüksek öncelikli bir yerel eksik adayı (Alpnex'te e-belge entegrasyonu olup olmadığı bu çalışmada incelenmedi — kod tarafında doğrulanmalı).
- Alpnex'in bakım + filo modülüne "periyodik kontrol" kayıt tipi (ekipman, son kontrol tarihi, yetkili kişi/firma, rapor dosyası, sonraki tarih, uyarı) eklemek düşük maliyetli ve Türk mevzuatıyla doğrudan ilişkili bir özellik; vinç/forklift/platform gibi kaldırma ekipmanı tutan firmalar için anlamlı.
- Araç muayene, trafik sigortası, kasko tarihleri takibi Türk filo yazılımlarında olağan beklenti — ancak bu çalışmada resmi/ürün kaynağıyla doğrulanmadı.

### Gaps
- GİB'in güncel (2026) e-İrsaliye/e-Fatura/e-Arşiv/e-Defter ciro eşikleri resmi sayfadan teyit edilemedi (ebelge.gib.gov.tr sayfası 404 verdi). Özel entegratör listesi ve entegrasyon yöntemleri (GİB portal / özel entegratör / doğrudan entegrasyon) resmi kaynaktan çekilemedi.
- Ek-III Tablo-2'deki kaldırma ekipmanı kontrol periyotları (ör. "en fazla yılda bir") resmi PDF metni çözümlenemediği için doğrulanmadı.
- Takograf, araç muayene periyotları, demirbaş amortisman oranları bu çalışmada araştırılamadı (doğrulanmadı).
- Kendi depoları/şantiyeleri arasında mal taşımada irsaliye zorunluluğunun tam metni (VUK 230 vb.) resmi kaynaktan teyit edilmedi.

## 3. Türk kullanıcıların bu tür yazılımdan tipik beklentileri (KDV/tevkifat, cari ekstre, çek/senet, döviz, format)

### Takeaway
Ürün sayfalarında doğrulanan yerel beklentiler: **çoklu döviz / döviz bazlı sözleşme ve kur farkı**, **çek takibi**, **avans/teminat/kesinti**, **onay akışları**, **resmi birim fiyat kütüphaneleri**, **e-Fatura XML içe aktarımı** ve **Logo/Netsis/Mikro ile birlikte çalışma**. KDV tevkifatı ve cari ekstre gibi kalemler bu çalışmada ürün belgesiyle doğrulanamadı.

### Cited Findings
- Çoklu para birimi (USD, EUR, TL) — [Uyumsoft](https://www.uyumsoft.com/santiye-insaat-takip-programi)
- Döviz bazlı taşeron sözleşmeleri; muhasebede kur farkı işlemleri — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- Finans & Ticari modülünde çek ve banka hesapları; e-Fatura XML içe aktarma; Logo/Netsis/Mikro resmi defter için yerinde kalabiliyor — [KonstraERP](https://www.konstraerp.com/)
- Merkezi onay (hakediş ve satınalma onayları) — [KonstraERP](https://www.konstraerp.com/); satınalma onay akışı — [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- Avans mahsubu, kesinti, teminat — [Qera ERP](https://qera.com.tr/santiye-bazli-maliyet-ve-hakedis-programi/); [Yapıtaşı ERP](https://www.yapitasierp.com/en/pages/yapitasi_erp)
- Resmi birim fiyatlar (ÇŞB, İLBANK, DSİ, KGM) ve 4734 uyumu — [KonstraERP](https://www.konstraerp.com/); [Oska](https://www.oska.com.tr)
- Konut satışı yapan müteahhitler için daire/dükkan stoku ve alıcı ödeme planları — [KonstraERP](https://www.konstraerp.com/); [Uyumsoft](https://www.uyumsoft.com/santiye-insaat-takip-programi)
- PDKS ve bordro/avans — [Uyumsoft](https://www.uyumsoft.com/santiye-insaat-takip-programi); [Mavvo Blog](https://mavvo.com.tr/blog/insaat-erp/)

### Inferences
- Alpnex'in ön muhasebesi (cari, fatura, kasa/banka, tahsilat/ödeme) için Türk kullanıcı beklentisi: çek/senet portföyü, döviz cari + kur farkı, avans/teminat; bunların Alpnex'te olup olmadığı kodda kontrol edilmeli.
- Rakiplerin çoğu resmi muhasebeyi Logo/Netsis/Mikro'ya bırakıyor; Alpnex için de "Logo/Mikro'ya aktarım (Excel/XML)" pratik bir köprü olur.
- Konut satış modülü (daire stoku, alıcı ödeme planı) yap-sat müteahhitler için var ama Alpnex'in saha-operasyon odağı için düşük öncelik.

### Gaps
- KDV tevkifatı (inşaat işlerinde ve taşeron faturalarında yaygın), cari ekstre, senet takibi, Türk sayı/tarih biçimi hakkında ürün belgesi bu çalışmada doğrulanamadı (doğrulanmadı / unverified).
- Teminat mektubu takibini açıkça belgeleyen ürün sayfası bulunmadı (Navappro için yalnız arama özeti "teminat").
