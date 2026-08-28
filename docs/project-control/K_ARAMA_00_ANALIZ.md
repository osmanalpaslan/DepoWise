# K — Global Arama · ANALİZ RAPORU (kod yazılmadı)

> Tarih: **2026-08-28** · Roadmap: FAZ 4 / SIRA 11 (MASTER_ROADMAP §1 — "Üst bar ortak özelliği, menü DEĞİL")
> Bu belge SALT ANALİZDİR: kod / migration / deploy / canlı veri değişikliği YOKTUR.
> ⚠️ Migration073..081 hâlâ yayında DEĞİL — K analizi ve uygulaması canlıya dokunmaz; K'nin kendisi için
> migration ÖNGÖRÜLMÜYOR (aşağıda §6).

---

## 1. Mevcut altyapı (kod taraması, 2026-08-28)

- **Her modülün kendi araması ZATEN VAR:** 20 serviste `search` parametreli liste
  (Material/Vehicle SearchGrid + WorkOrder/Announcement/Calendar/Document/Project/Equipment/CostCenter/
  Purchase/Request/Personnel/Party/Supplier…). Global Arama bunların ÜSTÜNE bir toplayıcıdır —
  hiçbirinin davranışına dokunulmaz.
- **Güvenlik desenleri hazır:** kaynak başına `Can(View)` kapısı + BranchAccess süzgeci + tenant
  (bildirim/takvim toplayıcılarında kanıtlı desen — yan kapı testleriyle).
- **Gezinme:** iki platformda ekrana gitme hazır (NavigateKey deseni). Masaüstünde 4 ekran
  (`IDeepLinkTarget`: Malzeme, Araç, Bakım, Stok Hareketleri) KAYDI da açabiliyor; web'de kayıt-açma
  altyapısı YOK (uyarılarda bugün de fark böyle — kabul edilmiş durum).
- **Üst bar:** çan (BLD-01) eklenirken iki kabuk da tanındı — arama kutusu aynı yere eklemeli girer.
- **Veri hacmi:** canlıda tek firma, kayıt sayıları küçük (binler mertebesi) → `LIKE` + firma filtresi +
  kaynak başına LIMIT fazlasıyla yeterli; **FTS/fuzzy/harici motor/paralel indeks GEREKSİZ** (kurulmayacak).

## 2. Önerilen mimari — BİRLEŞİK, TÜRETİLMİŞ SearchService (paralel gerçeklik YOK)

Tek `SearchService.Search(s, q)` (Infrastructure): kaynak başına KÜÇÜK, salt-okunur LIKE sorgusu
(`company_id=@c AND is_deleted=0 AND alan LIKE @q`, **LIMIT 5/kaynak**) → `SearchHit(Module, ModuleDisplay,
Id, Label, SubLabel, NavigateKey)` listesi. UNION'lu dev sorgu YERİNE kaynak başına ayrı sorgu — her
kaynağın kendi yetki kapısı/kapsam kuralı bozulmadan uygulanır, bir kaynağın yavaşlığı diğerini kilitlemez,
yeni kaynak eklemek 1 blok. Sonuç: **kategori gruplu** (Malzemeler, Araçlar, …), kategori içinde önce
"kodu/adı aramayla BAŞLAYAN" sonra "içeren" (iki geçişli basit sıralama — skor motoru yok).
5'ten fazla eşleşen kaynakta "daha fazlası için ekrana git" satırı.

- **Güvenlik (merkezî kapı):** her kaynak bloğu `Can(s, kaynakModülü, View)` ile sarılı (yetkisi olmayan
  kategori HİÇ SORGULANMAZ — sızma imkânsız); şube taşıyan kaynaklarda BranchAccess süzgeci
  (branchless görünür — sınıf kuralı); her sorgu company_id'li. Yeni `global_search` YETKİSİ GEREKMEZ —
  ayrı yetki eklemek güvenlik katmaz (içerik zaten kaynak yetkisinden süzülür) ve "aramada var,
  ekranda yok" tutarsızlığı doğururdu (PK-K5).
- **Çöp Kutusu:** aranmaz önerisi — silinmiş kayıt yalnız Çöp Kutusu ekranında (PK-K4).
- **Evrak:** yalnız METADATA (başlık/tür/bağlı kayıt etiketi) — dosya İÇERİĞİ aranmaz (içerik araması
  OCR/metin çıkarma + indeks demek — v1'de yok, ileride ayrı iş; mimariye engel değil).
- **Duyuru/Takvim:** başlık(+not) metadata araması; duyuruda okuma-herkese kuralı zaten Can'de.

## 3. UI — üst bar arama kutusu (yeni menü YOK)

Web MainLayout + masaüstü MainWindow üst barına 🔍 kutu (çanın yanı). Enter/yazınca (Enter ile —
autocomplete/anlık öneri YOK, v1 sade) açılır SONUÇ PANELİ: kategori başlıkları + satırlar
(Label + SubLabel). Tıkla → ilgili EKRANA git; masaüstünde `IDeepLinkTarget` olan ekranlarda KAYIT da
açılır (uyarılardaki mevcut davranış — PK-K3). Esc/dışına tıkla kapanır. Ekran değil ortak özellik
olduğu için AppScreens'e ekran EKLENMEZ (parite sayıları değişmez); yetki ağacına modül EKLENMEZ (PK-K5).

## 4. Offline / senkron / parite

- **Masaüstü:** yerel kaynaklar (malzeme/araç/personel/ekipman/şube/cari/iş emri/sipariş/talep/duyuru/
  takvim/maliyet merkezi) ÇEVRİMDIŞI aranır (aynı SearchService yerel DB'de). **Proje + Evrak
  sunucu-otoriteli** → çevrimiçiyse `/api/search`'ten o iki kategori eklenir, çevrimdışıysa panelde
  "çevrimiçi gerekli" notu (Takvim/Bildirim'de kanıtlı desen).
- **Web:** `/api/search?q=` tek uç → aynı SearchService.
- **Parite:** iki platform AYNI servisi çağırır → sonuç kümesi birebir (tek fark: masaüstünün kayıt-açma
  derinliği, PK-K3). Senkron mimarisine SIFIR dokunuş — aranan veri zaten taşınıyor.

## 5. Performans

Kaynak başına LIMIT'li LIKE; sorgu YALNIZ Enter'da (her tuşta değil); boş/1 karakterlik arama reddedilir
(min 2). Mevcut indeksler (company_id bileşik indeksleri) yeterli — **ölçmeden yeni indeks EKLENMEZ**
(protokol §8). Cache/kuyruk/arka plan indeksleme YOK.

## 6. Migration gereksinimi

**GEREKMEZ — şema 81'de kalır.** Tablo yok, ALTER yok, indeks yok. (Yayın bekleyen 073..081 durumu
değişmez; K yalnız kod ekler.)

## 7. Test planı

`AramaTests` (~12): kaynak başına eşleşme (kod/ad; başlayan-önce sıralama) · min uzunluk · LIMIT+
"daha fazla" işareti · **yan kapı: kaynak modül yetkisi yoksa o kategori hiç dönmez** (kaynak kaynak) ·
**BranchAccess: kapsam dışı şubenin iş emri/duyurusu/kaydı sonuca sızmaz** · **tenant** ·
**silinmiş kayıt sonuçta yok** (PK-K4'e göre) · duyuru okuma-herkese kuralı · sonuçların salt-okunur
olduğu (kaynaklar bit-bit) · offline: sunucu-otoriteli kaynaklar belge servissiz sessiz boş ·
API ucu sözleşmesi · mevcut modül regresyonları (arama parametreli listeler değişmedi).

## 8. Riskler / maliyet / yeniden yazım

**Büyüklük: ORTA** (asıl iş iki platform üst-bar paneli + kaynak blokları + testler). Yeniden yazım
riski YOK: kaynak eklemek 1 blok; içerik araması/fuzzy ileride bu servisin ARKASINA eklenir (arayüz
değişmez); kayıt-açma derinliği ekran ekran IDeepLinkTarget ile eklemeli büyür.
Risk (düşük): üst bar kalabalığı (çan + arama) — dar pencerede sığdırma; mevcut MinWidth desenleri var.

## 9. Sonraki işlerle ilişki

L — Dashboard aramayı KULLANMAZ (bağımsız). Yayın: K migration'sız → yayın paketini büyütmez.
7b hâlâ serbest sırada.

---

## PK-K SORULARI — kullanıcı kararı bekleniyor

Karar bekleyen 5 soru ana rapordadır (PK-K1 kapsam · PK-K2 aranan alanlar · PK-K3 kayda-git derinliği ·
PK-K4 çöp kutusu · PK-K5 yetki modeli). Kararlar gelmeden UYGULAMA BAŞLAMAZ.
