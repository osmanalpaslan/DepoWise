# A2b — Ekran, Alan, Düğme ve Kullanıcı Akışı Envanteri

> **Faz:** Ar-Ge A2b (salt okuma envanteri). **Tarih:** 2026-10-13. **Dal:** `arge/2026-10-kapsamli`.
> **Kapsam:** `AppScreens.cs` içindeki 73 ekran (59 iki platform, 3 yalnız masaüstü, 11 yalnız web) + bu ekranlardan açılan pencereler.
> **Yöntem:** Masaüstü `src/DepoWise.Desktop/Views/*.axaml` + `ViewModels/*.cs`, web `src/DepoWise.Web/Components/Pages/*.razor`
> dosyaları betikle tarandı (düğme metni, form alanı, zorunlu işaret, tablo başlığı, boş/hata paneli, kısayol, onay çağrısı),
> önemli noktalar elle doğrulandı. Kod değiştirilmedi, test/derleme çalıştırılmadı.
> **Not:** Bu belge "ne var" sorusunu cevaplar. Rakiplerle karşılaştırma (gap analysis — eksik analizi) sonraki fazdır;
> buradaki "sadeleştirme fikirleri" yalnız fikirdir, gereksinim değildir.

Kısaltmalar: **M** = masaüstü (Avalonia), **W** = web (Blazor), `*` = formda zorunlu işaretli alan,
**Onay** = `ConfirmService.AskAsync` (M) / `Dialog.Confirm` (W) onay penceresi, **VM** = ViewModel (ekran mantığı dosyası).

---

## 1. Tüm ekranlarda ortak yapı (çapraz desenler)

### 1.1 Kabuk (shell — ana pencere) ve gezinme
| Konu | Masaüstü | Web | Kanıt |
|---|---|---|---|
| Menü | Sol menü, bölümler (Malzeme-Stok, Operasyon, Finans, Raporlar, Kurumsal, Sistem) → grup → ekran. Menü içinde "Ekran ara…" kutusu | Sol menü aynı katalogdan (`AppScreens.GroupsFor`) | `AppScreens.cs:101-129`, `MainWindow.axaml` |
| Global arama (global search) | Üst barda "Ara… (Enter)" kutusu, **Ctrl+K** odaklar. 12 kaynak: malzeme, araç, personel, ekipman, şube, cari, tedarikçi, maliyet merkezi, iş emri, satın alma, talep, takvim. Tek tam eşleşmede (barkod/QR okutma) kayıt doğrudan açılır. 1300 px altı pencerede kutu gizlenir | Üst barda arama var (aynı servis) | `MainWindow.axaml.cs:15-31`, `ShellViewModel.cs:1761-1783`, `SearchService.cs:59-72`, `MainWindow.axaml:334-341` |
| Arama sonucundan kayda gitme | Kayıt **yalnız** "derin bağlantı" (deep link — doğrudan kayda gitme) destekleyen ekranlarda açılır: Malzemeler, Araçlar, Bakım, Stok Hareketleri. İş emri, satın alma, talep, cari vb. sonuçlarında yalnız ekran açılır, kayıt seçilmez | — | `ShellViewModel.cs:1730-1734`, `IDeepLinkTarget` uygulayan VM'ler |
| Kayıt geçmişi (record history) | Kabuk menüsü "Seçili Kaydın Geçmişi" → `RecordHistoryWindow`. Yalnız 7 ekranda: Malzeme, Araç, Ekipman, Personel, Yakıt, Bakım, Talep (`IKayitLoguKaynagi`) | Üst bar "Ekran araçları" menüsü → "Kayıt Geçmişi — <ekran>" : **ekranın tüm kayıtlarının** günlüğü; satırdan kaydın kendi geçmişi açılır. 28 ekran kapsanır (stok, fatura, cari, satın alma, iş emri dahil) | `MainWindow.axaml:436`, `ShellViewModel.cs:137,1918-1927`; `MainLayout.razor:107-112,775-805` |
| Onay penceresi | `ConfirmService.AskAsync` (kaydet/sil/iptal), gerekçe isteyen `AskReasonAsync` / `ReasonWindow` | `DialogExtensions.Confirm / ConfirmEdit / ConfirmDelete / ConfirmCancelRecord` | `ConfirmService.cs:9-14`, `Services/DialogExtensions.cs:14-29` |
| Boş / hata / yükleniyor | Ortak `StatePanel` (Mode=Empty/Error + "Tekrar Dene"). Yükleniyor göstergesi çoğu ekranda yok (VM'de `IsLoading` yalnız Uyarılar, Ana Ekran, Giriş) | `MudProgress`/`_loading` hemen her sayfada; boş metin sayfaya göre değişir | betik çıktısı (STATE/loading sayaçları) |
| Durum satırı | Çoğu ekranın altında `Status` metni (ör. "12 kayıt") | Snackbar (alt bildirim) | `PurchasingView.axaml:173` |
| Sohbet (chat) | Kabukta mesajlaşma paneli ("Mesaj yazın…", "Kullanıcı ara…") — kayda bağlı değil | `ChatDock` | `MainWindow.axaml` |
| Yetki modeli | Modül başına Görüntüle/Oluştur/Düzenle/Sil (`PermissionAction`), ek düğme anahtarları: `btn-approve, btn-backdate, btn-branch-select, btn-reverse, btn-restore, btn-screen-log, btn-export-reports, btn-export-mgr-reports, btn-add-lookup, btn-link-user, btn-conflict-resolve, btn-template-free-create, btn-reset-db, btn-logo`; korumalı alan (field) izinleri `fld_` | aynı | `AppModules.cs:4-10`, `FieldAccess.cs:48-62` |

### 1.2 Liste özellikleri matrisi (hangi ekranda ne var)
| Özellik | Masaüstünde olan ekranlar | Web'de olan ekranlar | Yorum |
|---|---|---|---|
| Başlığa tıkla sırala (sort) | Malzemeler, Araçlar (+Kiralık), Günlük Faaliyet, Raporlar | Aynı 4 ekran (Raporlar `DwDataGrid`) | Diğer ~40 listede sıralama yok |
| Kolon seç/genişlet (column picker) | Malzemeler, Araçlar, Günlük Faaliyet | Aynı + Raporlar | |
| Sayfalama (paging) | Malzeme, Araç, Günlük, Yakıt (2 sekme), Bakım kayıtları, Giriş-Çıkış hareketleri, Fatura, Cari, Tahsilat/Ödeme | Aynı ekranlar + Stok Dağıtımı | Diğer listeler tümünü yükler |
| Kolon altı filtre | Malzeme, Araç, Günlük | Aynı + Raporlar | |
| Excel'e Aktar | Duyuru, Zimmet, Takvim, Maliyet Merk., Günlük, Ekipman, Muayene, Malzeme, Personel, Satın Alma, Raporlar, Araç, İş Emri (+Excel Merkezi) | Aynı 13 ekran | **Yakıt, Bakım, Giriş-Çıkış, Stok Hareketleri, Talepler, Fatura, Cari, Kasa/Banka, Tahsilat, Sistem Logu'nda yok** (yalnız Raporlar üzerinden) |
| Yazdır (PDF) | Duyuru, Zimmet, Takvim, Maliyet Merk., Günlük, Ekipman, Malzeme, Personel, Satın Alma, Araç, İş Emri | Duyuru, Zimmet, Takvim, Maliyet Merk., Ekipman, **Muayene** | Web'de **Malzeme, Araç, Günlük, Personel, Satın Alma, İş Emri** Yazdır yok; masaüstünde **Muayene** Yazdır yok (bkz. §3.1) |
| QR etiketi | Malzeme, Araç, Ekipman | Malzeme, Araç (Ekipman web'de yok) | `EquipmentView.axaml:27` |
| Toplu işlem (bulk) | Yok denecek kadar az: Zimmet "toplu teslim" listesi, Stok Dağıtımı "Tümü", Yetkiler "Tümünü Seç", Raporlarda çoklu filtre seçimi | Aynı + Firma Yetki Paketi "Tümü açık/kapalı" | Çoklu satır seçip onaylama/silme/durum değiştirme **hiçbir listede yok** |
| Fotoğraf | Malzeme, Araç, Malzeme/Araç şablonu | aynı | |
| Belge eki (attachment) | Merkezi **Evrak** ekranı (Bağlı Kayıt Türü + Bağlı Kayıt). Kayıt ekranlarının detayında ekli belgeler **görünmez** | aynı | `DocumentsView.axaml:35-39` |
| Kayıt üzerinde yorum (comment) | Yok (sohbet var ama kayda bağlı değil) | Yok | |

### 1.3 Klavye kısayolları (keyboard shortcuts)
- **Ctrl+K**: global arama (yalnız masaüstü, `MainWindow.axaml.cs:21`).
- **Enter**: arama/sorgu kutularında çalıştırır (Yakıt `FuelView.axaml:230,243`, Stok Hareketleri, Malzeme/Araç/Şablon filtreleri, Bakım).
- **Escape**: Malzeme, Araç, Şablon, Bakım formlarında "Vazgeç".
- Kaydet için **Ctrl+S**, yeni kayıt için **Ctrl+N**, satır gezinme kısayolu **yok** (betikte hiçbir görünümde bulunmadı).

---

## 2. Ekran envanteri (menü grubuna göre)

Her ekran için: **Amaç** · **M liste** (kolonlar / filtre-arama / sıralama-sayfa) · **M düğmeler** · **M form** · **M detay ve satır işlemleri** ·
**Durumlar / onay** · **W farkı** · **Yetki / ilişki** · **Gözlem** (kanıt).

### 2.0 Ana Ekran ve Uyarılar
| Ekran | İçerik |
|---|---|
| **Ana Ekran (Dashboard)** (M `DashboardView`, W `Home.razor` `/`) | Amaç: günün özeti. Bölümler: ÖZET, KRİTİK UYARILAR, BUGÜNÜN TAKVİMİ, AKTİF DUYURULAR; "okundu", "Güncellemeyi İndir ve Kur", "Kontrol Et". W: senkron çakışma kartında "Gizle", "Kurulum Aracını İndir". Gözlem: menüde ekran değil, kabuk düğmesi ("Ana Ekran"). |
| **Uyarılar** `alerts` (M `AlertsView`, W `/alerts`) | Amaç: kritik stok, bakım, muayene, kira bitişi vb. uyarı kutusu. M: kategori seçimi (`SelectCategory`), "Yenile", "Okundu işaretle", tümünü okundu; uyarıya tıkla → ilgili kayda gider (`OpenAlert`). W: "Yenile", "Tümünü Okundu Yap". Yetki: herkese açık (`WebPermOverride ""`). Gözlem: arama/filtre yok; uyarıdan iş başlatma (ör. "bakım kaydı aç", "talep oluştur") düğmesi yok — yalnız kayda gitme. |

### 2.1 Malzemeler (bölüm: Malzeme ve Stok)
| Ekran | İçerik |
|---|---|
| **Malzeme Listesi** `materials.list` (M `MaterialsView` 855 satır + VM 1098; W `Materials.razor`) | **Amaç:** malzeme kartı (stok kalemi) tanımı ve stok görünümü. **Liste:** 15 kolon kataloğu (Kod, Ad, Tür, Kategori, Birim, Marka, Tedarikçi, Birim Fiyat, Para, Min Stok, Stok, Durum, Açıklama, Uyumlu Araçlar, Muadil); varsayılan 8 (`ListColumns.cs:45-69`). Kolon altı filtre + "Filtrele/Temizle", "Yalnız kritik", başlık sıralama, sayfalama (‹ ›), kolon seçici. **Düğmeler:** Excel'e Aktar, Yazdır, QR Etiketi, Yeni (`AddButtonText`). **Form:** Şablon, Kod*, Ad*, Tür, Kategori (+ yerinde yeni), Alt Kategori (+), Birim* (+), Marka (+), Tedarikçi (+), Uyumlu Araçlar (çoklu, Tümünü Seç), Muadil Malzeme, Birim Fiyat, Min Stok, Açılış Stok, Açıklama, Foto Ekle. **Detay paneli:** FOTOĞRAFLAR, Düzenle, Sil, MUADİLLER, UYUMLU ARAÇLAR, DEPO KIRILIMI, İŞLEM GEÇMİŞİ (`MaterialsView.axaml:715-827`); hızlı düzenleme penceresi `MaterialQuickEditWindow` ("Talep Oluştur", Düzelt, Sil). **Durum:** "Liste yüklenemedi"/"Kayıt yok"; Onay: kaydet/sil/foto sil. **W farkı:** aynı alanlar + "Açılış Deposu *" ayrı alan, "Muadil Ekle", "Talep Oluştur"; **Yazdır yok** (`Materials.razor:326` yalnız Excel). Kayıt düzenleme `MaterialEditDialog` içinde geçmişli. **Yetki:** `materials` (+`fld_` korumalı fiyat alanları). **Gözlem:** form, liste ve detay aynı ekranda; 5 ayrı "+ yerinde tanım ekle" mini formu formu uzatıyor (`MaterialsView.axaml:97-172`). |
| **Yeni Kayıt** `materials.new` | Aynı ekranın form modu (menüden doğrudan boş form). Ayrı ekran değil. |
| **Giriş-Çıkış** `stock.entry` (M `StockEntryView` + VM 962; W `Stock.razor` `/stock`) | **Amaç:** stoğa giriş, depo çıkışı (şube içi), şube dışı transfer. **Form:** Kayıt Tipi ("Yeni Kayıt" / "Depo Çıkışı"), Çıkış Türü ("Şube İçi"/"Şube Dışı" = transfer) (`StockEntryViewModel.cs:56-59`), İşlem Tarihi (geri tarih yetkili), Maliyet Merkezi, Miktar*, Birim Fiyat, Depo/Şantiye*, Hedef Şube*, Teslim Eden/Alan (Personel), Araç, Malzeme seçici ya da **yerinde yeni malzeme** (Kod*, Ad*, Birim*, Kategori…, 5 adet "+ yeni tanım"), Fatura/İrsaliye No, Sipariş Fişi No, Veresiye Fişi No, Açıklama. Çoklu çıkış satırı ("+ Listeye Ekle", "Çıkar"). **Liste (alt):** son hareketler — TARİH, TİP, KOD, MALZEME, YÖN, MİKTAR, B.FİYAT, AÇIKLAMA; yalnız "◀ Önceki / Sonraki ▶" sayfa; satırda "Düzenle", "İptal" (ters kayıt). **Onay:** 8 noktada. **W farkı:** "Atanmamış Stok Dağıtımı" düğmesi bu sayfada (menüde değil); personel alanı web'de zorunlu (`Teslim Eden / Alan (Personel) *`), masaüstünde zorunlu işareti yok. **Yetki:** `stock` (+`btn-backdate`, `btn-reverse`). **Gözlem:** alttaki hareket listesinde arama/filtre yok — filtre için ayrı "Stok Hareketleri" ekranına geçmek gerekir; Excel/Yazdır yok; "Yeni Kayıt" tipi içinde yeni malzeme kartı açılabilmesi mükerrer kart riski (`StockEntryView.axaml:187-200`). |
| **Stok Hareketleri** `stock.movements` (M `StockMovementsView`; W `/stock/movements`) | Amaç: salt okuma hareket defteri. Filtre: Başlangıç, Bitiş, Ara (kod, malzeme, not, belge no), Depo/Şantiye; "Filtrele/Temizle" (Enter çalışır). Kolonlar: TARİH, TİP, KOD, MALZEME, YÖN, MİKTAR, BİRİM, DEPO/ŞANTİYE, NOT/BELGE, DURUM. Derin bağlantı hedefi. Gözlem: **Excel/Yazdır, sıralama, sayfalama yok**; satırdan kaynak belgeye (fatura, sipariş, bakım) gitme yok. |
| **Stok Sayım** `stock.count` (M `StockCountView`; W `/stock/count`) | **Amaç:** sayım farkını düzeltme hareketi olarak işlemek. **M akışı:** malzeme ara → Sistem Stoğu (bu depo) görünür → Sayılan Miktar* → Gerekçe → İşlem Tarihi → "+ Listeye Ekle" → "Sayımı Kaydet". Tüm Şubeler modunda "Sayılan Depo*". Geçmiş tablo: TARİH, YÖN, MİKTAR, GEREKÇE. **W akışı farklı:** depo seçilince **depodaki tüm malzemeler tablo olarak** gelir (KOD, MALZEME, SİSTEM STOĞU, SAYILAN, FARK), "Sayım Gerekçesi *", "Sayımı Tamamla" (`StockCount.razor:36-75`). **Gözlem:** M'de satır satır ekleme (uzun sayım için yavaş); iki tarafta da sistem stoğu sayana görünüyor (kör sayım — blind count — yok); "Gerekçe" M'de zorunlu işaretsiz ama VM zorunlu tutuyor (`StockCountView.axaml:75` ↔ `StockCountViewModel.cs:263`); sayım taslağı/onay adımı yok, tek seferde işlenir. |
| **Malzeme Şablonları** `material_templates` (yalnız M menüde; W sayfa var ama menüde yok) | Amaç: sık kullanılan malzeme kartı kalıbı. Form: Şablon Adı*, Örnek Kod, Tür, Kategori, Birim, Marka, Tedarikçi, Min Stok, Birim Fiyat, Para Birimi, Açıklama, Uyumlu Araçlar, Foto. Liste araması "Şablon ara (ad/kod)". Gözlem: web'de menüden erişilemiyor (`AppScreens.cs` yorum satırı). |
| **Atanmamış Stok Dağıtımı** `stock.distribute` (M menüde; W `Stock.razor` içinden) | Amaç: şubesi belirsiz eski stoğu depolara dağıtmak. Liste: KOD, MALZEME, ATANMAMIŞ, DAĞITILACAK, KALAN; "Tümü" (kalanı doldur), Hedef Depo*, İşlem Tarihi, "Dağıtımı Kaydet" (Onay). |

### 2.2 Araçlar (bölüm: Operasyon)
| Ekran | İçerik |
|---|---|
| **Araç Listesi** `vehicles.list` (M `VehiclesView` 1010 satır + VM 1142; W `Vehicles.razor`) | **Amaç:** araç/iş makinesi kartı. **Liste:** 14 kolon kataloğu (İç Kod, Plaka, Üretim Yılı, Sayaç, Durum, Durum Notu, Araç Tipi, Kategori, Marka, Model, Şube/Şantiye, Sürücü, Şase No, Motor No), varsayılan 5 (`ListColumns.cs:113-135`) + BAKIM/MUAYENE göstergesi; kolon filtresi, sıralama, sayfalama, kolon seçici. **Düğmeler:** Excel'e Aktar, Yazdır, Sayaçları Onar, QR Etiketi, Yeni. **Form:** Şablon, İç Kod*, Plaka, Üretim Yılı, Durum, Sayaç, Birim, Durum Açıklaması, Makine Tipi(+), Kategori(+), Marka(+), Model(+), Şantiye/Şube, Sürücü(+), Şasi No, Motor No, Foto. **Detay:** FOTOĞRAFLAR, Düzenle, Sil ve sekmeler **Genel / Uyumlu Malzemeler / Muayene-Sigorta / Bakım Takibi / İşlem Geçmişi** (`VehiclesView.axaml:830-978`); hızlı pencere `VehicleQuickEditWindow` (aynı sekmeler + Fotoğraflar). **Onay:** 12 nokta. **W farkı:** "Şantiye / Şube *" web'de zorunlu, masaüstünde değil; **Yazdır yok** (`Vehicles.razor:295`); "Kolonlar" düğmesi. **Yetki:** `vehicles`. **Gözlem:** detayda bakım/muayene **görülür ama oradan yeni bakım/muayene/yakıt kaydı açılamaz** — ilgili ekrana gidilip araç yeniden seçilir; sayaç geçmişi ayrı sekme değil (Yakıt/Günlük/Bakım kayıtlarına dağılmış). |
| **Yeni Araç Ekle** `vehicles.new` | Aynı ekranın form modu. |
| **Kiralık Araçlar** `vehicles.rental` | Aynı ekran, kiralık filtresi + 4 ek kolon (Kiralayan Firma, Kira Başlangıç, Kira Bitiş, Yerine Geldiği Araç). Form ek alanları: Kiralayan Firma*, Kira Başlangıç*, Kira Bitiş, Kira Bedeli, Bedel Birimi, "yerine geldi" onay kutusu → Yerine Gelinen Araç*, Değişim Nedeni (`VehiclesView.axaml:87-115`). Detayda "Kiralamayı Bitir". |
| **Şablonlar** `vehicle_templates` (M `VehicleTemplatesView`; W `/vehicle-templates`) | Amaç: araç genel tanımı. Form: Ad*, Örnek İç Kod, Üretim Yılı, Makine Tipi/Kategori/Marka/Model (+ yerinde ekle), Uyumlu Malzemeler (çoklu). Liste: AD, ÖRNEK KOD, TİP, MARKA/MODEL, UYUMLU MALZEMELER; arama "Şablon ara…". |
| **Muayene / Sigorta** `inspection` (M `InspectionView`; W `/inspection`) | Amaç: muayene, sigorta, kasko vb. belge tarih takibi. Form: Araç*, Belge Tipi, Son Tarih, Sonraki Tarih, Yer/Kurum, Sonuç, Erteleme Tarihi* (erteleme seçilirse), Açıklama. Liste: ARAÇ, BELGE, SON, SONRAKİ, YER, DURUM; satırda "İptal Et". Düğmeler: Excel'e Aktar, Yenile. **W farkı:** web'de "Yazdır" var (`Inspection.razor:71`), masaüstünde yok. **Gözlem:** liste araması/filtresi yok (yalnız Yenile); poliçe/muayene belgesi eki için Evrak ekranına gitmek gerekir. |

### 2.3 Ekipman, Zimmet, Satın Alma, İş Emirleri, Takvim (Operasyon)
| Ekran | İçerik |
|---|---|
| **Ekipman Listesi** `equipment` (M `EquipmentView`; W `/equipment`) | Amaç: araç dışı demirbaş/ekipman kartı (jeneratör, kırıcı ucu…). Arama "kod / ad / seri / tür / şube". Liste: KOD, AD, TÜR, DURUM, ŞUBE/ŞANTİYE, SERİ NO (W'de +KONUM). Form: Ekipman Kodu*, Ad*, Ekipman Türü, Durum, Şube/Şantiye/Saha, Seri No, Konum, Durum Notu, Açıklama. Düğmeler: Excel, Yazdır, QR Etiketi (W'de QR yok), Düzenle, Sil. **Gözlem:** şantiyeler arası taşıma için ayrı akış yok — **kartın Şube alanını düzenlemek** gerekir (`EquipmentView.axaml:53`, `EquipmentViewModel.cs:204,225`); taşıma tarihi, gönderen/alan, yolda durumu tutulmaz (yalnız genel denetim günlüğü). Bakım kayıtları Bakım ekranının "Ekipman Bakımları" sekmesinde. |
| **Zimmet** `assignments` (M `AssignmentsView`; W `/assignments`) | Amaç: personele malzeme/ekipman teslimi ve iadesi. Form: İşlem (Teslim / İade / Hasarlı İade / Devir / Kayıp — `AssignmentsViewModel.cs:50`), Varlık Türü (Malzeme/Ekipman), Varlık*, Personel*, Alan Personel* (Devir'de), Depo/Şube, Miktar, İşlem Tarihi, Açıklama; çoklu kalem "+ Listeye Ekle" → "Kaydet" (`AssignmentsView.axaml:70,111`). Liste "kimde ne var": PERSONEL, VARLIK, SON İŞLEM…; sağda ZİMMET GEÇMİŞİ (`:154`). Excel, Yazdır. **Gözlem:** beklenen iade tarihi, imza/teslim tutanağı, durum (hasar) fotoğrafı alanı yok; gecikmiş iade uyarısı yok. |
| **Satın Alma** `purchasing` (M `PurchasingView`; W `/purchasing`) | Amaç: sipariş → mal kabul → stok. Toolbar: arama "no / tedarikçi / talep / depo", durum filtresi, Ara/Yenile, Excel, Yazdır, "Yeni Sipariş". Form: Sipariş No*, Tedarikçi, Bağlı Talep (onaylı talepten satır kopyalar), Teslim Deposu, Sipariş Tarihi (geri tarih yetkili), Maliyet Merkezi, Açıklama; satırlar: Malzeme, Miktar, Birim Fiyat, Sil, "Satır Ekle"; "Sipariş Oluştur". Liste: NO, TARİH, TEDARİKÇİ, DEPO, DURUM, TOPLAM (fiyat yetkisine bağlı). Detay: "Mal Kabul" (satır başına kısmi miktar), "İptal Et". **W farkı:** Yazdır yok (`Purchasing.razor:102`), web listede TALEP kolonu var. **Gözlem:** Sipariş No elle yazılıyor (otomatik numara yok); sipariş için onay adımı ekranda görünmüyor (Onaylamalarım'a düşebilir); mal kabulde irsaliye no/belge alanı yok; **faturayla bağ yok** (bkz. Senaryo 7). |
| **İş Emirleri** `work_orders` (M `WorkOrdersView`; W `/work-orders`) | Amaç: şantiyede yapılacak işin planı, atama, malzeme tüketimi, maliyet. Form: İş Emri No*, Başlık*, Öncelik, Şantiye/Saha (tüketim bu depodan düşer), Sorumlu, Maliyet Merkezi, Plan Başlangıç/Bitiş, Açıklama. Liste: NO, BAŞLIK, DURUM, ÖNCELİK, ŞANTİYE. Detay: durum değiştir, ATAMALAR ("Ata", "Kaldır"), MALZEME TÜKETİMİ ("Tüket"), MALİYET ÖZETİ, İLİŞKİLİ KAYITLAR, GEÇMİŞ (`WorkOrdersView.axaml:159-200`). **W farkı:** Yazdır yok. **Gözlem:** iş emrinden satın alma talebi veya bakım kaydı açma yok; araç arızasından iş emri üretme yok. |
| **Takvim** `calendar` (M `CalendarView`; W `/calendar`) | Amaç: ay görünümü + ajanda; türetilmiş (bakım/muayene/kira) ve elle girilen olaylar. Düğmeler: ◀ ▶ Bugün, şantiye filtresi, arama, Excel, Yazdır, "Yeni Takvim Kaydı". Form: Başlık*, Başlangıç*, Bitiş, Şantiye, Sorumlu Personel, İş Emri Bağı, Açıklama. W: "Kayda Git", "Tüm ayı göster". |

### 2.4 Günlük Faaliyet, Bakım, Yakıt (Operasyon)
| Ekran | İçerik |
|---|---|
| **Günlük Faaliyet Girişi** `daily_activity` (M `DailyActivityView` 563 + VM 826; W `/daily`) | **Amaç:** bir günde yapılan işler tek ekranda. **Form tek, "Kayıt Tipi"ne göre değişir** (`DailyActivityView.axaml:104-113`): Hareket, Transfer (araç başka şantiyeye — kaynak/hedef şube), Bakım (bakım tanımı*, alt bakım, teknisyen, sayaç, malzemenin çekildiği depo, malzemeler → stoktan düşer), Depo Çıkışı (Çıkış Türü, Malzeme*, Çıkacak Miktar*, Hedef Şube*, Teslim Alan). Ortak: Araç (çoklu), Operatör/Şoför, Süre (gün), Tarih, Açıklama. **Liste:** 9 kolon (Tarih, Kayıt Tipi, Araç, Rota, Personel, Süre, Malzeme Miktarı, Kullanılan Malzemeler, Açıklama), kolon filtresi, sıralama, sayfalama, "Tarihe Göre Getir", Excel, Yazdır. Satırda "Düzenle" (yalnız açıklama/meta), "İptal Et" (`:540-544`). **W farkı:** Yazdır yok (`Daily.razor:179`); "İptal edilenleri göster" anahtarı web'de. **Gözlem:** Bakım ve Depo Çıkışı **üç ayrı yerden** girilebiliyor (Günlük, Bakım, Giriş-Çıkış) → kullanıcı nereye gireceğini karıştırabilir; kayıt düzeltmesi "iptal + yeniden giriş". |
| **Bakım Tanımları Girişi** `maintenance.defs` (M `MaintenanceView` sekme 1; W `/maintenance/defs`) | Amaç: periyodik bakım planı (Tanım Adı*, Periyot, Birim (km/saat/gün), Açıklama, İlişkili Araçlar çoklu + alt bakım tanımları). Liste: BAKIM TANIMI, PERİYOT, ALT BAKIM TANIMLARI; Ekle/Düzenle/Sil. |
| **Araç Bakımları Girişi** `maintenance.records` (M sekme 2-4; W `/maintenance/records`) | **Form:** Araç*, Bakım Tanımı*, Alt Bakım(+), Teknisyen(+ yeni personel), Sayaç, Tarih, Maliyet Merkezi, Fatura/Servis Fişi No, Dış Servis (Cari), **Araç Durumu (değiştir)** + açıklama (`MaintenanceView.axaml:254`), Malzemenin çekildiği depo, Malzemeler (stoktan düşülür, `:286`). **Liste:** ARAÇ, PLAKA, BAKIM, YAPILMA, SONRAKİ, DURUM, KULLANILAN MALZEMELER; filtre Başlangıç/Bitiş/Ara; "◀ Önceki/Sonraki ▶". Satır: "Bilgileri Düzenle" (meta), "Bakımı İptal Et" + gerekçe (`:482`). Sekmeler: **Ekipman Bakımları** (`:497`), **Periyodik Bakım Uyarıları** (ARAÇ KODU, İLERLEME, TÜKETİLEN/PERİYOT; `:583`). **W farkı:** aynı içerik `/maintenance/{Section}` ile 4 bölüm. **Gözlem:** Excel/Yazdır yok; "arıza bildirimi → bakım bekliyor → tamamlandı" gibi bir **iş akışı/durum yok** — bakım kaydı iş bittikten sonra tek seferde girilir; parça stokta yoksa talep açma düğmesi yok. |
| **Yakıt Dağıtımları** `fuel.dist` (M `FuelView` sekme 1; W `/fuel/dist`) | **Form:** Araç*, Önceki Sayaç (salt), Güncel Sayaç*, Sayaç Farkı (salt), İşlem Tarihi, Maliyet Merkezi, Fatura/İrsaliye No, Litre*, Birim Fiyat, Toplam, Yakıtı Veren (hatırlanır), Yakıtı Alan. Kayıtta Onay + gerekirse sayaç/litre uyarıları (`FuelViewModel.cs:533-562`). **Liste:** ARAÇ, SAYAÇ, LİTRE, SAYAÇ FARKI, ORT. TÜKETİM, BİRİM FİYAT, TOPLAM, TARİH; filtre araç/tarih/ara (Enter), sayfa. Satırda Düzenle, "Kaydı İptal Et". **W farkı:** Yakıtı Veren* web'de zorunlu, "Sayfa boyutu" seçimi, "İptal edilenleri göster". **Gözlem:** her araç için ayrı form + ayrı onay → günlük 20-40 araçlık dağıtım çok tıklamalı; Excel yok. |
| **Depo Girişleri** `fuel.depot` (sekme 2) | Yakıt tankına alım: Birim Fiyat*, Tedarikçi(+), litre, fatura no, tarih; liste: TARİH, BELGE NO, LİTRE…, DURUM; "Kaydı İptal Et". Gözlem: alış faturası (Ön Muhasebe) ile bağ yok. |
| **Özet** `fuel.summary` (sekme 3) | Haftalar/Günler kırılımı, kalan (alınan − dağıtılan). Salt okuma. |

### 2.5 Talepler
| Ekran | İçerik |
|---|---|
| **Talep Formu** `requests.form` (M `RequestsView` sekme 1; W `/requests`) | **Amaç:** şantiyeden malzeme talebi. Form: Şube/Şantiye*, Tarih, Öncelik, Talep Eden(+), Depo Sorumlusu(+), Onay Veren(+), Açıklama; kalem: Malzeme ara, Miktar, Araçlar (çoklu) → "Kalemi Ekle"; "Talebi Oluştur" (`RequestsView.axaml:196`). Liste: BELGE NO, TARİH, KALEM, AÇIKLAMA, ÖNCELİK, DURUM, OPERASYON; arama "Belge no / açıklama". Detay: KALEMLER, DURUM GEÇMİŞİ, Düzenle, PDF, Ekonomik PDF, İptal Et (`:278-287`). **W farkı:** aynı, "Talebi İptal Et" yalnız taslak/bekleyen (`Requests.razor:157`). **Gözlem:** talep kalemi stokta var mı bilgisi formda görünmüyor (Malzemeler'e gidip bakmak gerekiyor); talepten doğrudan "sipariş oluştur" düğmesi yok — Satın Alma'da talep seçiliyor. |
| **Talep Onaylama** `requests.approve` (M `RequestsView` sekme 2 "Talep Onaylama (Bekleyen)", `:304-306`; W `/requests/approve`) | Bekleyen talepler; Onayla / Reddet (Ret Gerekçesi). Yetki: `request_approval` (+`btn-approve`). |
| **Onaylamalarım** `approvals` (M `ApprovalsView`; W `/approvals`) | Kullanıcıya düşen onay adımları (talep + satın alma). Kolonlar W: Süreç, Belge/Sipariş No, Tarih, Adım, İşlem. Onayla / Reddet (gerekçe "Örn: Bütçe uygun değil"). **Gözlem:** onay **iki ayrı ekranda** (Talep Onaylama sekmesi ve Onaylamalarım) → hangisinin asıl olduğu belirsiz; belge detayı (kalemler) bu listede görünmüyor, karar için başka ekrana geçmek gerekebilir; toplu onay yok. |
| **Talep Operasyonları** `request_ops` (M `RequestOperationsView`; W `/request-operations`) | Onaylı talebin depo/satın alma sürecini izleme: liste BELGE NO, TARİH, TALEP EDEN/ŞUBE, ÖNCELİK, OPERASYON DURUMU, GÖNDERİM; detay KALEMLER, GÖNDERİM BİLGİLERİ (Gönderen Şube, Gönderilecek Şube, Operasyon Notu), DURUM DEĞİŞTİR (Yeni Durum), İŞLEM GEÇMİŞİ. **Gözlem (kanıtlı sınır):** "kısmi karşılama, talebin bölünmesi, teslim alan, dosya eki, bildirim ve **otomatik stok hareketleri** bu fazda yok" (`RequestOperationsViewModel.cs:43-49`) → durum "gönderildi" yapılsa bile stok çıkışı Giriş-Çıkış ekranında **ayrıca** girilmeli. |

### 2.6 Ön Muhasebe (bölüm: Finans)
| Ekran | İçerik |
|---|---|
| **Cari Listesi / Yeni Cari** `accounting.parties(.new)` (M `PartiesView`; W `/parties`) | **Amaç:** tedarikçi/müşteri cari kartı ve borç-alacak hareketi. Liste: KOD, ÜNVAN, TİP, VERGİ/TCKN, BAKİYE, DURUM (W'de +TELEFON); arama "Kod / ünvan / vergi no / telefon", Şube (çoklu), tip, "Yalnız aktif"; ◀ ▶ sayfa. Form: Cari Kodu*, Ünvan*, Cari Tipi*, Vergi Dairesi, VKN (10 hane), TCKN (11 hane), Telefon, E-posta, Adres, İl, İlçe, Not, **Tedarikçi Eşlemesi** (Tanımlar'daki tedarikçiyle bağ). Detay: GENEL BİLGİLER, FİNANSAL ÖZET, hareketler (TARİH, TÜR, AÇIKLAMA, BORÇ, ALACAK), "Hareket Ekle" (Belge Türü*, Yön*, Tutar*, Tarih, Vade, Belge No, Açıklama), "Seçili Hareketi Ters Çevir" + gerekçe, Aktif/Pasif, Sil. **Gözlem:** "Tedarikçi" iki yerde yaşıyor (Tanımlar > Tedarikçi ve Cari kartı) ve elle eşleniyor → mükerrer/eşlenmemiş tedarikçi riski; Excel yok (ekstre Raporlar'da). |
| **Fatura Listesi / Yeni Fatura** `accounting.invoices(.new)` (M `InvoicesView`; W `/invoices`) | Liste: FATURA NO, TÜR, CARİ, TARİH, TUTAR, DURUM (W +VADE); arama "Fatura no / karşı belge no / cari", Şube çoklu, ◀ ▶. Form: Fatura Türü* (Alış/Satış), Cari*, Depo/Şantiye (stok hareketinin deposu), Karşı Taraf Belge No, Fatura Tarihi*, Vade, Not, **"Stoğu etkilesin" (varsayılan açık)** (`InvoicesView.axaml:157`, `InvoiceService.cs:64`, W `Invoices.razor:245,387`); satırlar: Malzeme (hizmet satırında boş), Açıklama, Miktar, Birim Fiyat, İskonto %, KDV %, Tevkifat %. "İptal Et" → İptal Gerekçesi* → ters stok + ters cari. **Gözlem:** sipariş/mal kabul ile **bağ alanı yok** (`InvoiceService.cs` içinde sipariş referansı geçmiyor) → aynı malın hem Satın Alma > Mal Kabul hem Alış Faturası ile **iki kez stoğa girme riski**; fatura PDF/görsel eki için Evrak'a gitmek gerekir; Excel yok. |
| **Kasa / Banka / Yeni Hesap** `accounting.finance(.new)` (M `FinanceView`; W `/finance`) | Hesap listesi: KOD, HESAP ADI, TÜR, BAKİYE, DURUM (W +ŞUBE). Form: Hesap Kodu*, Hesap Adı*, Hesap Türü*, Şube, Banka Adı/Şubesi, Hesap No, IBAN, Not (W +Varsayılan hesap, Aktif). Hareketler: TARİH, TÜR/AÇIKLAMA, GİRİŞ, ÇIKIŞ; "Hareket Ekle" (Tür*, Tutar*, Tarih*, Açıklama), "İç Transfer" (Kaynak*, Hedef*), "Ters Kayıt" + gerekçe. |
| **Tahsilat / Ödeme** `accounting.payments` (M `PaymentsView`; W `/payments`) | Liste: TARİH, TÜR, CARİ, HESAP, GİRİŞ, ÇIKIŞ (W +YÖNTEM). Form: İşlem Türü*, Cari*, Kasa/Banka*, Tutar*, Tarih*, Ödeme Yöntemi, Belge No, Referans, Açıklama; "Tutarı Faturalardan Doldur" (W'de açık fatura tablosu: FATURA NO, VADE, KALAN, KAPATILACAK). Ters Kayıt + gerekçe. **Gözlem:** M'de "Kaydet" onay penceresi açmadan yazıyor (`PaymentsViewModel.cs:287` `Save` ve `:363` `ReverseTxn` içinde `ConfirmService` yok; ters kayıt paneldeki "Ters Kaydı Onayla" düğmesiyle). Aynı durum Fatura kaydında (`InvoicesViewModel.cs:338`; onay yalnız iptalde `:222`). Diğer ekranların çoğu kaydetmeden önce onay sorduğu için **tutarsız**; `IslemOnaylariTests` yalnız Düzenle/Sil/İptal adlarını denetliyor, "Save" adını değil (`tests/DepoWise.Tests/IslemOnaylariTests.cs:40-66`). |
| **Maliyet Merkezleri** `cost_centers` (M `CostCentersView`; W `/cost-centers`) | Form: Kod, Ad*, Durum, Açıklama. Liste: KOD, AD, DURUM; sağda MALİYET ÖZETİ (Başlangıç/Bitiş + "Sorgula"; W: MERKEZ, KALEM, TUTAR, KAYIT). Excel, Yazdır. Maliyet merkezi seçimi stok, yakıt, bakım, satın alma, iş emri formlarında. |

### 2.7 Raporlar
| Ekran | İçerik |
|---|---|
| **Operasyon Raporları** `reports` / **Yönetici Raporları** `reports.manager` (M `ReportsView` + VM 843; W `/reports`, `/reports/manager`) | **Amaç:** 30 hazır rapor (`ReportCatalog.cs:193+`): Araç, Araç-Günlük, Stok Durumu, Stok Hareketleri (+Günlük), Depo Çıkışları, Stok Sayım, Yakıt Tüketim (+Günlük), Yakıt Depo Girişi, Bakım, Talep, Muayene/Sigorta, Kiralık Araç Maliyeti, Personel, Malzeme/Araç şablonlu-şablon dışı (4), Durum, Cari Ekstre, Cari Bakiye, Fatura Özeti, Açık Faturalar/Vade, Yaşlandırma, Tahsilat/Ödeme Özeti, Kasa/Banka Özeti, Maliyet Merkezi Özeti, Günlük Faaliyet Detay/Dönem. **Filtreler (rapora göre görünür):** Başlangıç/Bitiş, Şube (çoklu), Araç Türü, Araç (çoklu + Tümünü Seç), Bakım Tanımı, Teknisyen, Tedarikçi, Talep Eden, Teslim Alan, Depo, Ara, Hareket Türü, Kayıt Tipi, Malzeme (çoklu), Cari, Durum, Sıralama. "Sorgula", "Excel'e Aktar" (`btn-export-reports` / `btn-export-mgr-reports`). Sonuç tablosu sıralanabilir, kolon filtreli (W `DwDataGrid`). Operasyon = yalnız çalışma şubesi; Yönetici = izinli şubeler + şube seçici. **Gözlem:** rapor satırından ilgili kayda/ekrana gitme yok; rapor sonucu için Yazdır/PDF yok (yalnız Excel); kayıtlı filtre (favori rapor) yok — her açılışta filtre yeniden kurulur (Rapor Tasarımcısı ayrı). |
| **Rapor Tasarımcısı** `reports.designer` (M `CustomReportsView`; W `/reports/designer`) | Kullanıcı tanımlı rapor: Rapor Adı, Veri Kaynağı, kolon seçimi, filtre değeri; Yeni / Kaydet / Sil / Yenile. Rapor başına erişim dinamik anahtarla. |

### 2.8 Şube ve Personel, Kullanıcı Yönetimi (bölüm: Kurumsal)
| Ekran | İçerik |
|---|---|
| **Şube / Şantiye** `branches` (M `BranchesView`; W `/branches`) | Form: Ad*, Tür, Şube Kodu, Şube şifresi, Üst Şube/Şantiye (W +Firma*). Liste: AD, TÜR, ÜST ŞUBE, KULLANICI (W +KOD, ŞİFRE); sağda ŞUBEDEKİ KULLANICILAR. Düzenle/Sil (Onay). Gözlem: arama yok. |
| **Projeler** `projects` (M `ProjectsView`; W `/projects`) | Form: Proje Adı*, Durum, Başlangıç, Bitiş, Bağlı Şantiye, Sorumlu Personel, Konum/Adres, Açıklama. Liste: PROJE, DURUM, ŞANTİYE, SORUMLU, BAŞLANGIÇ, BİTİŞ. Arama var. Yetki `branches`. Gözlem: projeye bağlı maliyet/iş emri özeti yok (Maliyet Merkezleri ayrı). |
| **Personel Girişi** `personnel` (M `PersonnelView`; W `/personnel`) | Form: Ad Soyad*, Unvan (+ yeni unvan), Telefon, Şube/Şantiye, Aktif, Saha personeli, Bağlanacak Kullanıcı* (`btn-link-user`), "Bağı kaldır". Liste: AD SOYAD, UNVAN, ERİŞİM, TELEFON, DURUM; arama "ad / unvan / telefon". Excel, Yazdır. **W farkı:** Yazdır yok (`Personnel.razor:126`). Gözlem: personelin zimmetleri ve ehliyet/belge geçerliliği kartta görünmüyor (Zimmet ve Evrak ayrı ekran). |
| **Kullanıcı Tanım** `users` (M `UsersView`; W `/users`) | Form: Kullanıcı Adı*, Şifre*, Ad Soyad, E-posta, Cep, Unvan, Not, Firma*, Şube, Personel bağla, Rol, Yetki Şablonu (süper admin). Liste: KULLANICI, AD SOYAD, PERSONEL, ROLLER, ŞUBE, DURUM. Detay: Şube Ata, Tüm Şubeler Yetkisi, Bilgileri Düzenle, Şifre Sıfırla, Aktif/Pasif, Kullanıcıyı Sil, Rolü Kaydet. W: "Şube / Şantiye *" zorunlu. Korumalı ekran. Gözlem: liste araması yok. |
| **Ekipler** `teams` (M `TeamsView` **salt okunur**; W `/teams` tam düzenleme) | W: Ekip Adı, Ekip Yöneticisi, Aktif, Üye Ekle/Çıkar, Kaydet, Sil. M yalnız "Yenile" (ADR-187 gereği bilinçli; `TeamsViewModel.cs` başlık yorumu). |
| **Yetkiler** `permissions` (M `PermissionsView` tek ekran + VM 1011; W `/permissions`) | Kullanıcı seç → modül/ekran ağacı (Görüntüle/Oluştur/Düzenle/Sil) + KORUMALI ALANLAR; "Şablondan doldur", "Ekran / alan ara…", grup başına Tümünü Seç/Temizle, Yetki Özeti, Yetkileri Sıfırla, Şube Kapsamını Kaydet. W: aynı + "YAPABİLDİKLERİ", "Ne olur?" açıklama kolonu. Korumalı ekran. |
| **Yetki Şablonları** `permission_templates` | Şablon Adı*, Rol (ops.), modül × 4 işlem; Şablonu Kaydet / Sil (W +Firma*). |

### 2.9 Evrak, Duyurular, Denetim
| Ekran | İçerik |
|---|---|
| **Evrak / Belgeler** `documents` (M `DocumentsView`; W `/documents`) | Form: Belge Başlığı*, Belge Türü (serbest metin), **Bağlı Kayıt Türü + Bağlı Kayıt** (`DocumentsView.axaml:35-39`), Geçerlilik Başlangıç/Bitiş, Dosya (≤7 MB), Açıklama. Liste: BAŞLIK, TÜR, BAĞLI KAYIT, GEÇERLİLİK, YÜKLENME (W +DOSYA, BOYUT); arama. İndir, Düzenle, Sil. Yetki `files`. **Gözlem:** belge, bağlı olduğu kaydın kendi ekranında görünmez (araç kartından "belgeleri" açma yok); belge türü serbest metin → raporlanması zor. |
| **Duyurular** `announcements` | Form: Başlık*, Önem, Hedef Şube, Yayın Başlangıcı/Bitişi, Metin. Liste + Excel/Yazdır. Okuma herkese açık. |
| **Sistem Logu** `audit` (M `AuditLogView`; W `/audit`) | Filtre: Başlangıç, Bitiş, Kayıt Sayısı (W +Kayıt (ekran), Kullanıcı). Kolonlar M: TARİH, KULLANICI, KAYIT, İŞLEM; W +NE DEĞİŞTİ ve satırda "Geçmiş". **Gözlem:** M'de kullanıcı/ekran filtresi ve alan farkı (diff) kolonu yok → web'den zayıf; iki tarafta Excel yok. |
| **Stok Değişiklik Kaydı** `stock_change_log` | TARİH, KULLANICI, MALZEME, ESKİ, YENİ, FARK, SONUÇ, UYARI; tarih + kayıt sayısı filtresi (W +Kullanıcı, Sonuç). |
| **Senkron Çakışmaları** `sync_conflicts` (W sayfa; M `SyncConflictsWindow` kabuk menüsünden) | Üzerine yazılan değişiklikler; "Üzerine Yazılanı Kazanan Yap" (`btn-conflict-resolve`), "Kaydın Geçmişi", "Gizle". |

### 2.10 Sistem Yönetimi (süper admin ağırlıklı)
| Ekran | Platform | Özet |
|---|---|---|
| Firma Tanım `companies` | M+W | Firma Adı*, Yetkili, kotalar (admin/personel/makine), vergi, iletişim, İlk Şube Adı*, Türü; Aktife Al. |
| Güncelleme Yönetimi `releases` | M+W | Sürüm*, Min. Desteklenen, Boyut, İmzalı, Notlar, Checksum; Yayınla / Bu Paketi Kur (M). W sade (Sürüm, Notlar). |
| Makine Yönetimi `machines` | M+W | M: MAKİNE, ŞUBE, DURUM, SON GÖRÜLME; kota, Aktifleştir/Pasife Al/Sil. W daha zengin: bağlantı, il, IPv4/IPv6, firma değiştir, "Kayıtsız Makineleri Getir", "Tanımı Sıfırla". |
| Makine Yedekleri, Canlı Sunucu, Kota İzleme, Firma Yetki Paketi, Kalıcı Silme, Firma İş Verisini Sıfırla, Yerel Veri Sıfırlama, Menü/Ekran Yönetimi | yalnız W | Sunucu işlemleri. Kalıcı Silme/Sıfırlama: firma + şifre + özel kod (çift doğrulama). Menü/Ekran Yönetimi: sıra, ad, üst grup, platform görünürlüğü; korumalı ekran. |
| Sunucu Yedekleri `server_backups` | M+W | Başlangıç/Bitiş, Listele, Seçili Aralığı Sil. |
| Yedek Yönetimi `backup` | yalnız W (M'de görünüm dosyası duruyor, menüde yok) | Yedek Al, İndir. |
| Çöp Kutusu `trash` | M+W | TÜR, KAYIT, SİLİNME; M "Çöp Kutusunu Aç" (kilit), W "Parolanız"; Geri Yükle. Gözlem: arama/filtre yok. |

### 2.11 Ayarlar
| Ekran | Platform | Özet |
|---|---|---|
| Tanım Düzenle `definitions` | M `SettingsView` (açılır bölümler: her tanım listesi + Malzeme Alt Kategori + Araç Modelleri; Ekle/Kaydet/Sil/Kilitle) · W `/definitions` (`DefEditor`) | Birim, kategori, marka, tedarikçi, makine tipi, unvan… tek yerde. **Gözlem:** aynı tanımlar 10+ formda "+" ile yerinde de eklenebiliyor (`btn-add-lookup`) → yazım farkıyla mükerrer tanım riski (ör. "Adet"/"adet"). |
| Alan Ayarları `field_settings` | M+W | Firma bazlı alan zorunluluğu (Alan, Zorunlu, Durum). Gözlem: M ve W formlarındaki farklı zorunlu işaretleri (bkz. §3) bu ayardan mı yoksa sabit koddan mı geliyor, ayrıca doğrulanmalı. |
| Excel Merkezi `import` (W) / `import_export` (M) | M: DIŞA AKTAR / İÇE AKTAR (Şube + şube şifresi). W: Kaynak, Şube, "Örnek Şablon İndir", **"Ön Kontrol (yazmaz)"**, "İçe Aktar", hata tablosu (Satır, Sebep). | M'de ayrı "ön kontrol" düğmesi yok. |
| Geliştirici Modu, Tema, Hakkında, Güncelleme Notları | M+W | Tema: renk/stil, "Sesi Dene". Geliştirici kodu (süper admin). |
| Bileşen Galerisi (M), Giriş penceresi | — | Geliştirici aracı; giriş (kullanıcı adı, şifre, ilk şifre belirleme, şube seçimi). |

---

## 3. Çapraz gözlemler (kanıtlı)

### 3.1 Masaüstü ↔ web farkları (parite — eşitlik)
| # | Fark | Kanıt | Etki |
|---|---|---|---|
| P1 | Web'de **Yazdır** yok: Malzeme, Araç, Günlük Faaliyet, Personel, Satın Alma, İş Emri (masaüstünde var) | `Materials.razor:326`, `Vehicles.razor:295`, `Daily.razor:179`, `Personnel.razor:126`, `Purchasing.razor:102`, `WorkOrders.razor:95` (yalnız Excel) | Web kullanıcısı imzalı çıktı alamaz |
| P2 | Masaüstünde **Muayene/Sigorta Yazdır** yok (web'de var) | `InspectionView.axaml` (Yazdır yok) ↔ `Inspection.razor:71` | Ters yönde eksik |
| P3 | **Stok Sayım** akışı farklı: M satır satır ekleme, W depodaki tüm malzemeleri tablo olarak getirir | `StockCountView.axaml:60-80` ↔ `StockCount.razor:36-75` | Aynı iş iki platformda farklı öğrenilir |
| P4 | **Zorunlu alan işaretleri** farklı: Araç formunda "Şantiye/Şube" W'de `*`, M'de değil; Giriş-Çıkış'ta "Teslim Eden/Alan (Personel)" W'de `*`, M'de değil; Yakıt "Yakıtı Veren" W'de `*`, M'de değil; Sayım gerekçesi W'de `*`, M'de işaretsiz ama zorunlu | betik çıktısı; `StockCountView.axaml:75` ↔ `StockCountViewModel.cs:263` | Kullanıcı kaydette hata görür; Alan Ayarları ile ilişkisi doğrulanmalı |
| P5 | **Kayıt geçmişi** modeli farklı: M seçili kaydın geçmişi (7 ekran), W ekranın tüm kayıt günlüğü (28 ekran) | `ShellViewModel.cs:137` ↔ `MainLayout.razor:775-782` | M'de Satın Alma, Fatura, Cari, Stok, Zimmet, İş Emri için kabuk menüsünden geçmiş yok (bazılarında ekran içi geçmiş bölümü var) |
| P6 | **Sistem Logu** M'de kullanıcı/ekran filtresi ve "Ne değişti" kolonu yok | `AuditLogView` ↔ `Audit.razor` | Denetim masaüstünde zayıf |
| P7 | **Ekipler** M'de salt okunur | `TeamsViewModel.cs` (ADR-187) | Bilinçli; belgelenmiş |
| P8 | **QR etiketi** Ekipman'da yalnız M | `EquipmentView.axaml:27` | Web'den etiket basılamaz |
| P9 | **Excel Merkezi**: W'de "Ön Kontrol (yazmaz)" adımı var, M'de yok | `ImportExcel.razor` ↔ `ImportExportView` | M'de hatalı dosya doğrudan işlenmeye çalışılır |
| P10 | **Makine Yönetimi** W çok daha zengin (IP, il, firma değiştirme, kayıtsız makineler) | `Machines.razor` ↔ `MachineManagementView` | Süper admin işi, düşük öncelik |

### 3.2 Kullanılabilirlik gözlemleri
| # | Gözlem | Kanıt |
|---|---|---|
| G1 | **Sıralama** yalnız 4 listede; ~40 listede başlığa tıklayınca sıralama yok | `IListGridViewModel` uygulayanlar: Materials, Vehicles, DailyActivity (+Reports) |
| G2 | **Toplu işlem** yok: çoklu satır seçip onaylama, iptal, durum değiştirme, etiket basma, Excel'e yalnız seçileni aktarma yok | betik `bulk` sayaçları; ApprovalsView tek tek Onayla/Reddet (`ApprovalsView.axaml:21`) |
| G3 | **Excel/Yazdır** en çok iş girilen ekranlarda yok: Yakıt, Bakım, Giriş-Çıkış, Stok Hareketleri, Talepler, Fatura, Cari, Kasa, Tahsilat, Sistem Logu (dolaylı yol: Raporlar) | §1.2 matrisi |
| G4 | **Giriş-Çıkış ekranındaki hareket listesinde arama/filtre yok**; filtre için Stok Hareketleri'ne geçmek gerekiyor | `StockEntryView.axaml:334-376` (yalnız ◀ ▶) |
| G5 | **Aynı iş birden çok ekranda**: depo çıkışı (Giriş-Çıkış, Günlük Faaliyet, Bakım malzemesi, İş Emri "Tüket", Zimmet teslimi); bakım (Bakım, Günlük Faaliyet "Bakım" tipi); onay (Talep Onaylama sekmesi, Onaylamalarım) | `DailyActivityViewModel.cs:23-26`, `StockEntryViewModel.cs:56-58`, `WorkOrdersView.axaml:169`, `RequestsView.axaml:304`, `ApprovalsView` |
| G6 | **Yerinde tanım ekleme ("+")** 10+ formda → mükerrer kategori/marka/birim/tedarikçi riski | `MaterialsView.axaml:97-172`, `StockEntryView.axaml:206-291`, `VehiclesView.axaml:169-250` |
| G7 | **Kayıttan kayda geçiş eksik**: araç detayında bakım/muayene görünür ama "yeni bakım/yakıt" açılamaz; rapor satırından kayda gidilemez; arama sonucu yalnız 4 ekranda kaydı açar | `VehiclesView.axaml:830-978`, `ShellViewModel.cs:1733` |
| G8 | **Belge eki merkezi**: Evrak ekranında, kayıt ekranında görünmez; fatura/irsaliye/poliçe/arıza fotoğrafı kayıtla birlikte izlenemez | `DocumentsView.axaml:35-39` |
| G9 | **Kayıt üzerinde yorum/not akışı yok** (sohbet var ama kayda bağlı değil) | `MainWindow.axaml` sohbet paneli |
| G10 | **Numara elle**: Sipariş No*, İş Emri No* elle yazılıyor | `PurchasingView.axaml` "Sipariş No" `IsRequired`, `WorkOrdersView` "İş Emri No*" |
| G11 | **Kaydet onayı tutarsız**: çoğu ekran soruyor, Fatura ve Tahsilat/Ödeme sormuyor; Yakıt her kayıtta soruyor (yoğun girişte yorucu) | `PaymentsViewModel.cs:287`, `InvoicesViewModel.cs:338`, `FuelViewModel.cs:562` |
| G12 | **Yükleniyor göstergesi** masaüstünde çoğu ekranda yok (liste boşken "Kayıt yok" ile karışabilir) | VM `IsLoading` yalnız Alerts/Dashboard/Login |
| G13 | **Kısayol** az: Ctrl+K dışında kaydet/yeni/ara kısayolu yok | §1.3 |
| G14 | **Uyarılardan iş başlatılamıyor** (yalnız kayda gitme) | `AlertsViewModel` komutları: Load, SelectCategory, OpenAlert, MarkRead, MarkAllRead |
| G15 | **Liste araması olmayan ekranlar**: Uyarılar, Muayene, Şube, Kullanıcılar, Çöp Kutusu, Sistem Logu (M) | betik `SEARCH/WM` boş |

---

## 4. Kullanıcı senaryoları (gerçek ekranlarla adım adım)

> Her senaryoda: **adımlar** (ekran → işlem), **yeniden giriş** (aynı bilginin tekrar yazıldığı yer), **hata riski**, **sadeleştirme fikri** (yalnız fikir).

### Senaryo 1 — Malzeme talebi → onay → satın alma → mal kabul → şantiyeye çıkış
| Adım | Ekran | İşlem |
|---|---|---|
| 1 | Talepler › Talep Formu | Şube/Şantiye*, Talep Eden, Öncelik; her kalem için malzeme ara + miktar + "Kalemi Ekle"; "Talebi Oluştur" |
| 2 | Talepler › Talep Onaylama **veya** Onaylamalarım | Onaylayan bekleyenleri görür, Onayla/Reddet (gerekçe) |
| 3 | (Stokta var mı?) Malzemeler › detay "DEPO KIRILIMI" | Talep ekranında stok görünmediği için ayrı ekranda bakılır |
| 4 | Satın Alma › Yeni Sipariş | Sipariş No* (elle), Tedarikçi, **Bağlı Talep** seçilir → satırlar kopyalanır, Teslim Deposu, Maliyet Merkezi; "Sipariş Oluştur" |
| 5 | (Gerekirse) Onaylamalarım | Sipariş onay adımı |
| 6 | Satın Alma › sipariş seç › "Mal Kabul" | Satır başına gelen miktar (kısmi olabilir) → stok teslim deposuna girer |
| 7 | Ön Muhasebe › Yeni Fatura | Alış faturası: Cari, Depo, satırlar (malzeme+miktar+fiyat+KDV) **yeniden yazılır**; "Stoğu etkilesin" kapatılmazsa stok **ikinci kez** girer |
| 8 | Talep Operasyonları | Durumu "gönderildi" vb. güncelle, gönderen/gönderilecek şube — stok hareketi üretmez (`RequestOperationsViewModel.cs:48-49`) |
| 9 | Malzemeler › Giriş-Çıkış › Depo Çıkışı (Şube Dışı) | Malzeme, miktar, kaynak depo, hedef şantiye, teslim alan — talep kalemleri **üçüncü kez** yazılır |

- **Yeniden giriş:** talep kalemleri → (sipariş otomatik) → fatura satırları elle → çıkış satırları elle. Tedarikçi hem Tanımlar'da hem Cari'de.
- **Hata riski:** çift stok girişi (adım 6 + 7); talep durumu ile gerçek stok çıkışı ayrışabilir; onay iki ekranda; sipariş no çakışması.
- **Ekran sayısı:** 6–7 farklı ekran, en az 9 geçiş.
- **Sadeleştirme fikirleri:** talep formunda kalem başına mevcut stok; onaylı talepten tek tıkla "stoktan karşıla / sipariş oluştur" dalı; faturayı siparişten/mal kabulden oluşturma ve stok etkisini mal kabulden alma; operasyon durumu "gönderildi" iken çıkış hareketini aynı adımda üretme; otomatik belge numarası.

### Senaryo 2 — Araç arızası → parçalı bakım
| Adım | Ekran | İşlem |
|---|---|---|
| 1 | Araçlar › Araç Listesi › Düzenle | Durum = "Arızalı" + Durum Açıklaması (arıza bildirimi için ayrı kayıt yok; `VehicleStatus.cs:22-27`) |
| 2 | (Parça var mı?) Araç detayı › Uyumlu Malzemeler / Malzemeler › Depo Kırılımı | Uyumlu parçaları ve depodaki stoğu kontrol |
| 3 | (Yoksa) Talepler › Talep Formu → Senaryo 1 zinciri | Parça talebi; arıza kaydıyla bağ kurulmaz |
| 4 | Bakım Takibi › Araç Bakımları › Yeni | Araç* (yeniden seçilir), Bakım Tanımı*, Alt Bakım, Teknisyen, Sayaç, Tarih, Maliyet Merkezi, Dış Servis (Cari), Fatura/Servis Fişi No, **Araç Durumu (değiştir) = Aktif**, depo + malzemeler (stoktan düşer) |
| 5 | (Dış servis faturası) Ön Muhasebe › Yeni Fatura | Hizmet satırı, "Stoğu etkilesin" kapalı; bakım kaydıyla bağ yok |
| 6 | (Fotoğraf/servis fişi) Evrak › Yeni Belge | Bağlı Kayıt Türü = araç/bakım seçilerek ayrı yüklenir |

- **Yeniden giriş:** araç 3–4 kez seçilir; servis fişi no hem bakımda hem faturada.
- **Hata riski:** arıza anı ile bakım tarihi arasında kayıt yok (arıza süresi ölçülemez); durumu "Aktif"e çevirmeyi unutmak; parçanın yanlış depodan düşmesi.
- **Sadeleştirme fikirleri:** araç detayından "Arıza bildir" → açık bakım/iş emri taslağı; bakım kaydında "eksik parça için talep aç"; servis faturasını bakım kaydından açma; belge ekini bakım formunda gösterme.

### Senaryo 3 — Günlük yakıt dağıtımı (ör. 25 araç)
| Adım | Ekran | İşlem |
|---|---|---|
| 1 | Yakıt › Depo Girişleri (gerekiyorsa) | Tanker alımı: litre, birim fiyat*, tedarikçi, fatura no |
| 2 | Yakıt › Yakıt Dağıtımları › Yeni Dağıtım | Araç* (ara), Güncel Sayaç*, Litre*, Yakıtı Veren (hatırlanır), Yakıtı Alan, tarih, maliyet merkezi |
| 3 | Kaydet | Onay penceresi + (gerekirse) sayaç geri/büyük fark ve büyük litre uyarıları (`FuelViewModel.cs:533-562`) |
| 4 | 2–3'ü 25 kez tekrarla | Form her kayıttan sonra temizlenir |
| 5 | Yakıt › Özet / Raporlar › Yakıt Tüketim | Kontrol; Excel yalnız rapordan |

- **Yeniden giriş:** tarih, maliyet merkezi, veren kişi her kayıtta (veren hatırlanıyor, diğerleri doğrulanmalı).
- **Hata riski:** yanlış araca yazma (iptal + yeniden giriş gerekir), sayaç yazım hatası (uyarı var), yorgunlukla onayı körlemesine geçme.
- **Tıklama yükü:** araç başına ~6–8 etkileşim + 1 onay → 25 araçta 150–200 etkileşim.
- **Sadeleştirme fikirleri:** tablo biçiminde toplu dağıtım (bir satır = bir araç, sonda tek onay); barkod/QR ile araç seçimi (global aramada var, formda yok); tarih/merkez/veren için "oturum varsayılanı"; Excel'den içe aktarma (Excel Merkezi'nde yakıt içe aktarma servisi var: `FuelImportService.cs`).

### Senaryo 4 — Ekipmanın (ve aracın) şantiyeler arası transferi
| Adım | Ekran | İşlem |
|---|---|---|
| 1a | **Ekipman:** Ekipman Listesi › Düzenle | "Şube / Şantiye / Saha" alanını yeni şantiye yap, Kaydet (`EquipmentViewModel.cs:204,225`) |
| 1b | **Araç:** Günlük Faaliyet › Kayıt Tipi = Transfer | Araç, Kaynak Şube, Hedef Şube, Operatör, Tarih (`DailyActivityViewModel.cs:23-24`, araç otomatik pasife) |
| 1c | **Malzeme:** Giriş-Çıkış › Depo Çıkışı › Şube Dışı | Malzeme, miktar, hedef şube |
| 2 | (Personelde zimmetliyse) Zimmet › Devir | Alan Personel* |
| 3 | Evrak (sevk irsaliyesi) | Ayrı belge yükleme |

- **Yeniden giriş:** üç varlık türü için üç farklı ekran ve üç farklı form deseni.
- **Hata riski:** ekipmanda taşıma tarihi, gönderen/alan, "yolda" durumu tutulmuyor → anlık konum sorgusu güvenilmez; geçmiş yalnız genel denetim günlüğünde; araç transferinin "pasif" yan etkisi beklenmedik olabilir.
- **Sadeleştirme fikirleri:** tek "Transfer" belgesi (araç + ekipman + malzeme satırları), gönder → teslim al iki adımı, ekipman kartında konum geçmişi sekmesi.

### Senaryo 5 — Ay sonu stok sayımı (bir depo, ~300 kalem)
| Adım | Ekran | İşlem |
|---|---|---|
| 1 | (Hazırlık) Raporlar › Stok Durumu (depo filtresi) › Excel | Sayım listesi çıktısı (sayım ekranında "liste yazdır" yok) |
| 2 | Sahada kâğıtla sayım | — |
| 3M | **Masaüstü** Stok Sayım | Her kalem için: malzeme ara → sayılan miktar → "+ Listeye Ekle" (300 kez) → Gerekçe → "Sayımı Kaydet" |
| 3W | **Web** Stok Sayım | Depo seç → tüm kalemler tabloda → SAYILAN kolonuna yaz → Gerekçe* → "Sayımı Tamamla" |
| 4 | Raporlar › Stok Sayım / Stok Değişiklik Kaydı | Farkların kontrolü |

- **Yeniden giriş:** kâğıttan ekrana; masaüstünde her kalem için arama.
- **Hata riski:** sistem stoğu sayana görünüyor (kör sayım yok) → sayının sisteme "uydurulması"; uzun oturumda kaydetmeden kapanırsa kayıp (taslak yok); fark için amir onayı yok; sayım sırasında başka çıkış girilirse fark kayar (sayım dondurma yok).
- **Sadeleştirme fikirleri:** masaüstüne web'deki tablo görünümü; sayım taslağı (kaydet-devam et); sistem stoğunu gizleme seçeneği; tolerans üstü farkı onaya gönderme; sayım listesini yazdırma.

### Senaryo 6 — Alet/ekipman zimmeti ve iadesi
| Adım | Ekran | İşlem |
|---|---|---|
| 1 | Zimmet › İşlem = Teslim | Varlık Türü, Varlık*, Personel*, Depo, Miktar, Tarih; birden çok kalem "+ Listeye Ekle" → Kaydet (Onay) |
| 2 | (İmzalı tutanak) Zimmet › Yazdır | Liste PDF'i (kişi bazlı tutanak değil — liste çıktısı) |
| 3 | İade: Zimmet › İşlem = İade / Hasarlı İade / Kayıp | Varlık ve personel yeniden seçilir |
| 4 | (Hasarlıysa) Bakım › Ekipman Bakımları ve/veya Ekipman › Durum | Ayrı ekranlarda durum ve bakım |
| 5 | Personelin üzerindekiler | Zimmet listesi "kimde ne var" (personel kartında görünmez) |

- **Hata riski:** beklenen iade tarihi yok → gecikme uyarısı üretilemez; hasarlı iade ekipman durumunu otomatik değiştiriyor mu belirsiz (doğrulanmalı); işten ayrılan personelin açık zimmeti personel kartında görünmez.
- **Sadeleştirme fikirleri:** iade tarihi + uyarı; kişi bazlı teslim tutanağı (imza alanlı); personel kartında "Zimmetleri" sekmesi; QR okutarak hızlı iade.

### Senaryo 7 — Tedarikçi faturasının stoğa etkisi
| Adım | Ekran | İşlem |
|---|---|---|
| 1 | Ön Muhasebe › Cari Listesi | Tedarikçi carisi yoksa aç; Tanımlar'daki tedarikçiyle "Tedarikçi Eşlemesi" |
| 2 | Ön Muhasebe › Yeni Fatura | Fatura Türü = Alış, Cari*, Depo/Şantiye, Karşı Taraf Belge No, tarih/vade, satırlar (malzeme, miktar, fiyat, iskonto, KDV, tevkifat), "Stoğu etkilesin" |
| 3 | Kaydet | **Onay sorulmadan** stok girişi + cari borç oluşur (`InvoicesViewModel.cs:338`) |
| 4 | Hata varsa | "İptal Et" + gerekçe → ters stok + ters cari; yeniden giriş |
| 5 | Tahsilat / Ödeme | Ödeme: "Tutarı Faturalardan Doldur" |

- **Hata riski:** aynı mal Satın Alma › Mal Kabul ile de girildiyse **çift stok**; malzeme seçilmeyen satır stok etkilemez ama kullanıcı fark etmeyebilir; Giriş-Çıkış ekranındaki "Fatura / İrsaliye No" alanı ile Fatura kaydı birbirine bağlı değil (aynı belge iki yerde).
- **Sadeleştirme fikirleri:** faturayı mal kabulden üret (stok etkisi kabulde, fatura yalnız fiyat/cari); fatura–sipariş–kabul üçlü eşleştirme (three-way match — üçlü eşleştirme) uyarısı; "Stoğu etkilesin" için bağlama göre varsayılan.

### Senaryo 8 — Bir kaydın geçmişini bulma (ör. "bu malzemenin stoğu neden 12?")
| Adım | Masaüstü | Web |
|---|---|---|
| 1 | Malzemeler › kaydı seç › detay "İŞLEM GEÇMİŞİ" ve "DEPO KIRILIMI" | Malzemeler › düzenleme penceresinde geçmiş |
| 2 | Kabuk menüsü › "Seçili Kaydın Geçmişi" (kart alanı değişiklikleri) | Üst bar › Ekran araçları › "Kayıt Geçmişi — Malzemeler" → satırdan kaydın geçmişi |
| 3 | Stok Hareketleri › Ara = kod, tarih aralığı | aynı |
| 4 | Hareketin kaynağı (fatura mı, mal kabul mü, bakım mı?) için NOT/BELGE kolonunu okuyup ilgili ekrana elle gitmek | aynı |
| 5 | Stok Değişiklik Kaydı / Sistem Logu (M'de kullanıcı filtresi yok) | Sistem Logu (kullanıcı + ekran filtresi, "Ne değişti") |

- **Hata riski / zorluk:** geçmiş 3–5 ayrı yerde; hareketten kaynak belgeye tıklama yok; Satın Alma, Fatura, Zimmet gibi ekranlarda masaüstü kabuk menüsü "Seçili Kaydın Geçmişi"ni sunmuyor.
- **Sadeleştirme fikirleri:** her kayıtta tek "Geçmiş" sekmesi (alan değişikliği + bağlı hareketler + belgeler + yorumlar — rakiplerdeki "chatter" deseni, sektör araştırması "Kayıt etrafındaki iş birliği zinciri"); hareket satırından kaynak belgeye bağlantı.

---

## 5. Sonraki faza aktarılacak özet

1. **Akış kopuklukları** (en büyük kazanç alanı): talep → sipariş → kabul → fatura → çıkış zincirinde belge bağları eksik (Senaryo 1, 7); çift stok riski somut.
2. **Aynı işin çok giriş noktası:** depo çıkışı 5, bakım 2, onay 2 ekranda (G5).
3. **Toplu giriş eksikliği:** yakıt, sayım (masaüstü), onay — en sık işlerde tek tek form (Senaryo 3, 5; G2).
4. **Kayıt merkezli görünüm eksikliği:** geçmiş, belge, yorum, bağlı hareket tek yerde değil (G7–G9, Senaryo 8).
5. **Parite farkları** (P1–P10): çoğu küçük ve kapatılabilir; zorunlu alan ve Stok Sayım farkı kullanıcıyı en çok etkileyenler.
6. **Liste standardı:** sıralama/Excel/Yazdır/arama ekranlara göre değişiyor (§1.2) — "tablo istekleri her tabloya" kuralıyla çelişen ortak bir liste standardı ihtiyacı.

> Bu belgedeki "fikirler" gereksinim değildir; önceliklendirme ve rakip karşılaştırması sonraki fazda (eksik analizi) yapılacaktır.
