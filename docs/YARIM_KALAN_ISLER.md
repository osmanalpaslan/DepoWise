# Yarım Kalan İşler ve Testleri (Canlı Liste)

> **Bu dosya nedir?** "Yarıda kalan işlemler ne?" / "sırada ne var?" dediğinde bakılacak **tek liste**.
> Her işin **hangi aşamaları** kaldığını ve **hangi testlerin** yapılacağını gösterir. Teknik bilgi gerektirmez.
>
> **Nasıl güncel kalır?** Claude her anlamlı değişiklikten sonra bu dosyayı günceller (bir madde bitince
> "Tamamlananlar"a taşır, yeni iş çıkınca ekler). Özet burada; ayrıntı `docs/` ve `DEVAM.md`'de.
>
> Son güncelleme: **2026-08-09**

---

## 📋 ONAYLI GELİŞTİRME SIRASI (kullanıcı kararı 2026-08-09, K1–K7)

Kaynak: [docs/KARAR_ANALIZI_K1_K7.md](KARAR_ANALIZI_K1_K7.md) ·
[docs/YARIM_ISLER_VE_EKRAN_STANDARDIZASYONU_ANALIZI.md](YARIM_ISLER_VE_EKRAN_STANDARDIZASYONU_ANALIZI.md)

| # | İş | Durum |
|---|---|---|
| 1 | **Yakıt kaydı iptali** | ✅ YAYINLANDI (masaüstü 1.0.131) |
| 2 | **Günlük Faaliyet → stok/bakım tutarlılığı** | ✅ YAYINLANDI (masaüstü 1.0.132) |
| 3 | **M-S1a `company_id` migration'ı** (çok-kiracı sızıntısı) | ✅ YAYINLANDI (masaüstü 1.0.133) — [sonuç raporu](MS1A_MIGRATION_SONRASI_RAPORU.md) |
| 3b | **Paket 1** — KD-1 (stok hareketleri 500) + firma izolasyonu T-1…T-6, Y-1, Y-2 + API çok-firmalı testler | ✅ YAYINLANDI (masaüstü 1.0.134) — [rapor](PAKET1_UYGULAMA_RAPORU.md) |
| 4 | Ortak düzenleme altyapısı + Personel/Talepler çift tık | ✅ kod tamam (yayın bekliyor) |
| 5 | Günlük Faaliyet + Bakım kaydı düzenleme | ✅ kod tamam (yayın bekliyor) |
| 6 | Düzenleme kilitleri (aynı kaydı iki kişi) | ✅ kod tamam (yayın bekliyor) |
| 7 | Excel içe aktarma → Web | ✅ kod tamam (yayın bekliyor) — [rapor](tests/ExcelIceAktarim_Web_Test_Report.md) |
| 8 | Çoklu malzeme + şube sürüm kontrolü | ✅ kod tamam (yayın bekliyor) — [rapor](tests/CokluMalzeme_Stok_Test_Report.md) |
| 9 | LookupBox ortak bileşeni | ✅ kod tamam (yayın bekliyor) — [rapor](tests/LookupBox_Ortak_Bilesen_Test_Report.md) |
| 10 | Kolon kataloğu → Alan/Kolon Yönetimi | ⏭️ SIRADAKİ |
| 11 | Faz S (senkron performansı) / FK / benzersizlik | bekliyor |

### ✅ 3b — Paket 1: KD-1 + firma izolasyonu (2026-08-09, masaüstü 1.0.134)
Sunucudaki **Stok Hareketleri** listesi açılmıyordu (3 uç 500 veriyordu — `rowid` PostgreSQL'de yok);
düzeltildi. Ayrıca **8 firma izolasyonu açığı** kapatıldı (T-1…T-6, Y-1, Y-2) — hepsi servis katmanında,
çünkü masaüstü aynı metotları doğrudan çağırıyor. Gerçek HTTP hattı üzerinden **çok-firmalı test paketi**
eklendi (bu sınıf hatayı bundan sonra otomatik yakalar). PostgreSQL'deki flaky (kararsız) test de çözüldü.
**Migration YOK.** Testler: SQLite 866/0 · PostgreSQL **35/0/0 atlandı**.
[Uygulama raporu](PAKET1_UYGULAMA_RAPORU.md) · [plan](PAKET1_UYGULAMA_PLANI.md)

**Sonradan çıkan bulgular (backlog'a eklendi, 2026-08-09):**
- **P2 — Web'de lookup alanlarında arama yok.** Masaüstü İş #9'da ortak `LookupBox`'a geçirildi;
  web aynı alanlarda `MudSelect` (aramasız) kullanıyor. Web'in kendi aranabilir deseni zaten var
  (`Stock.razor` → `MudAutocomplete`); 18+ kontrolün dönüştürülmesi ayrı iş.
- **P3 — `LookupBox`'ta "seçimi temizle" yok.** Opsiyonel alanda seçim geri alınamıyor.
  Eski `ComboBox`ta da alınamıyordu → regresyon değil.

**Kapsam dışı bırakılanlar (hâlâ açık):** Y-3 (latent, API ucu yok) · Y-4/Y-5 (ölü kod) ·
Y-6 (`/api/materials` N+1 performans) · **D-1: `CLAUDE.md` satır 53-54 yanlış** ("sunucu SQLite" diyor,
gerçekte PostgreSQL) · M-S1b (`request_status_history` + 5 tablo firma kolonu, **migration gerektirir**) ·
M-S1d (eşitlemede üst kayıt firma doğrulaması).

---

### ✅ 3 — M-S1a firma izolasyonu (2026-08-09, masaüstü 1.0.133)
`material_request_items` + `maintenance_materials` tablolarına **firma kolonu** eklendi (NOT NULL, varsayılan yok).
Canlıda 2 kalem doğru firmaya taşındı; silinen/kaybolan kayıt 0, çözülemeyen 0, yanlış firma 0, şema 61→62.
Eşitleme paketi artık yalnız kendi firmasının satırlarını taşıyor (canlıda doğrulandı).
Geri dönüş noktası: Neon `pre-ms1a` dalı duruyor.
[Ön rapor](MS1A_PRE_MIGRATION_RAPORU.md) · [Sonuç raporu](MS1A_MIGRATION_SONRASI_RAPORU.md)

**KAPSAM DIŞI, AÇIK KALDI (KD-1):** sunucuda `/api/stock` ve `/api/stock/movements` **500** veriyor —
sıralamada SQLite'a özel `rowid` kullanılıyor, PostgreSQL'de yok. 2026-08-05'ten beri var, M-S1a ile ilgisiz.
Ayrıca açık: **M-S1b** (`request_status_history`, `maintenance_definition_vehicles` firma kolonu) ·
**M-S1c** (yeni tabloda firma kolonu unutulmasın kontrolü) · **M-S1d** (eşitlemede üst kayıt firma doğrulaması).

---

### ✅ 2 — Günlük Faaliyet iptali (2026-08-09, masaüstü 1.0.132)
Faaliyet iptal edilince **bağlı bakım + malzeme çıkışları da aynı tek işlemde** iptal olur, malzemeler stoğa
döner; bir adım başarısız olursa hiçbiri olmaz. "Sil" → **"İptal Et"**; onay penceresi etkiyi önceden yazar.
İptal edilenler varsayılan gizli. Araç sayacı geri alınmaz, işlem geri alınamaz. Yetki servis katmanında.
**Migration YOK.** Test: 825 geçti / 0 başarısız ·
[test raporu](tests/GunlukFaaliyet_Iptal_Test_Report.md)

**Bu iş sırasında bulunan ve düzeltilen ek hata:** `/api/daily/grid` ucu "İptal edilenleri göster" bayrağını
iletmiyordu (web'de kutu işe yaramıyordu) — API'ye eklendi, Excel dışa aktarımı da ekranla aynı kümeyi verir.

---

## 🔴 CANLI VERİ MODU AÇIK (2026-08-08)

**Baban gerçek veri girmeye başladı** (kullanıcı `mustafa.alpaslan`, şube **Karaman**). Bundan sonra her işte:
geri alınamaz işlem (silme, sıfırlama, veri taşıyan migration, toplu güncelleme) **açık onay olmadan yapılmaz**;
şema değişikliği veri taşıma/yedek planı olmadan girişilmez. Testler yalnız yerel/ayrı test veritabanında koşar.

---

## ⏳ Talep Faz 3 — KULLANICI ONAYI BEKLİYOR (2026-08-08)

Faz 3 (talep karşılama + gerçek stok hareketleri) öncesi **yalnız analiz** yapıldı; kod/migration/deploy YOK.
Rapor: **[docs/FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md](FAZ3_ONCESI_KARAR_VE_RISK_ANALIZI.md)**.

**Güncelleme (2026-08-08):** 15 maddenin **13'ü onaylandı**. Faz 3-Ön uygulama planı hazırlandı:
**[docs/FAZ3_ON_UYGULAMA_PLANI.md](FAZ3_ON_UYGULAMA_PLANI.md)** (9 başlık: plan · dosyalar · migration ·
transaction sınırı · CAS/retry · yetki noktaları · test · transfer iptali yetki analizi · company_id risk analizi).
Kalan iki karar: **K-1 transfer iptali politikası (P-1/P-2/P-3)** ve **K-2 M-S1 migration zamanlaması**.

Onay gelince önerilen sıra:
1. **Faz 3-Ön** — PostgreSQL eşzamanlılık (oversell) düzeltmesi: `stock_balances` üzerinde iyimser CAS +
   sınırlı tekrar. **Migration yok**, SQLite davranışı değişmez.
2. **Faz 3a** — `request_fulfillments` tablosu (M-062) + servis + senkron listesine ekleme.
3. **Faz 3b/3c** — masaüstü ve web karşılama ekranı. 4. **Faz 3d** — şube transferiyle karşılama + iptal.

Analizden çıkan, Faz 3'ten **bağımsız** iki iş (ayrıca onay bekliyor):
- **Faz S — Senkron performansı:** 22 sorgulu sürüm hesabı → tek sorgu; her push'ta tüm defterden bakiye
  yeniden hesabı → yalnız etkilenen malzemeler; push sonrası "yankı pull"un kesilmesi; uyarlanabilir tur aralığı.
- **M-S1 — Çok-kiracı sızıntısı:** `material_request_items` ve `maintenance_materials` tablolarında `company_id`
  olmadığı için senkron çekmede firma filtresi uygulanmıyor (bugün tek firma olduğu için zarar yok; ikinci
  firmada gerçek sızıntı olur).

---

## ✅ Masaüstü ekran düzeltmeleri (2026-08-08) — TAMAMLANDI + KULLANICI DOĞRULADI

Kullanıcı testinde bildirilen 5 madde; **1.0.125 ile sütun sorunu da dâhil hepsi çözüldü ("sorun düzelmiş")**.
1. **Sütun daraltma/genişletme** — kök neden: başlık/filtre/gövde üç ayrı Grid genişliği `SharedSizeGroup` ile
   pazarlık ediyordu; paylaşılan ölçü büyümeyi anında, **küçülmeyi ancak liste yeniden kurulunca** uyguluyordu
   (kullanıcının gördüğü "~10 sn" = 15 sn'lik eşitleme turu). Çözüm: Raporlar tablosundaki ilke — SharedSizeGroup
   kaldırıldı, her hücrede genişlik doğrudan sabitlendi (Araçlar/Malzemeler/Günlük Faaliyet). 1.0.124 yetmedi,
   **1.0.125** çözdü. (İlk turda ayrıca `*` esnek kolon → paylaşımlı ölçü ve SortHeader tek-kaynak/koordinat düzeltmesi.)
2. **Eşitleme ekranı bozmasın** — detay paneli/form açıkken liste yeniden kurulmuyor; yenileme bekletilip
   kapanınca sessizce uygulanıyor (açık detay artık kaybolmuyor).
3. **Tema** — masaüstünden "Semi (Modern)" kaldırıldı (web'e dokunulmadı).
4. **Muadil malzeme** — sonuç ve seçilenler "KOD — Ad" gösteriyor (arama zaten kod+ad idi).
5. **Malzeme şablonu** — forma şablon seçici geri eklendi + masaüstüne **Malzeme Şablonları** yönetim ekranı
   (Malzemeler menüsü; mevcut `material_templates` yetkisi). Altyapı zaten duruyordu, yalnız UI eksikti.

Test 709/0. Web/API değişmedi. Yayın: **masaüstü 1.0.125**.

---

## ✅ Raporların tek tek yeniden tasarımı (ortak standart) — DEVAM EDEN GÖREV

Ortak standart: Birim 1-4 mimarisi + Araç Raporu'nda oturan biçim (NumCell HAM değer + görüntü), pinned TotalRow,
InfoNote, km/saat duyarlılığı, tam filo, tek-geçiş (N+1 yok), web+masaüstü parite. Her rapor kendi iş mantığıyla
ayrı analiz edilir (hesap kopyalanmaz). Sıra: **ANALİZ → KAPSAM → GELİŞTİRME → TEST → COMMIT → DEPLOY.**

- ✅ **Araç Raporu** (2026-08-08) — CANLI (API+web+masaüstü 1.0.119).
- ✅ **Yakıt Tüketim** (2026-08-08) — CANLI (API+web+masaüstü **1.0.120**). Tam filo, km/saat duyarlı, 13 kolon
  (Şube/İç Kod/Plaka/Araç Adı/Türü/Sayaç Birimi/İşlem/Mesafe/Litre/Ort. Tüketim/Ort. Fiyat/Toplam/Birim Maliyet),
  ağırlıklı ort. fiyat, akıllı toplam (km↔saat karışımında mesafe/ort./birim boş), Araç+Araç Türü filtreleri,
  web özeti çift-sayım bug'ı giderildi. Para birimi: ortak kur dönüşümü yok → mevcut davranış + InfoNote notu.
  Test 668/0 (+17). **Not:** ortak kur dönüşümü ileride gerekirse ayrı iş (Money USD/EUR var ama çevrim altyapısı yok).
- ✅ **Bakım Raporu** (2026-08-08) — CANLI (API+web+masaüstü **1.0.121**). Detay (her bakım bir satır), 12 kolon,
  işlenen şube (op_branch_id), km/saat sayaç, malzeme maliyeti + kalem sayısı (derived-table, correlated subquery yok),
  yeni **Bakım Tanımı + Teknisyen** filtreleri (uçtan uca) + Araç + Araç Türü. Pinned toplam (kayıt+kalem+maliyet;
  sayaç toplanmaz). İptaller hariç. İşçilik maliyeti alanı YOK → yalnız malzeme (ileride ayrı iş). Test 682/0 (+14).
- ✅ **Depo Girişi** (2026-08-08) — CANLI (API+web+masaüstü **1.0.122**). 8 kolon (Şube/Tarih/Tedarikçi/Litre/
  Birim Fiyat/Tutar/Fatura No/Para Birimi), yeni **Tedarikçi** filtresi (uçtan uca), pinned toplam (litre+tutar+
  ağırlıklı ort. birim fiyat). Para birimi: kur dönüşümü yok → mevcut davranış + Para Birimi kolonu + InfoNote. Test 692/0 (+10).
- ✅ **Talep Raporu** (2026-08-08) — CANLI (API+web+masaüstü **1.0.123**). 8 kolon (Şube/Belge No/Tarih/Talep Eden/
  Onaylayan/Durum/Kalem Sayısı/Açıklama), yeni **Durum + Talep Eden** filtreleri (uçtan uca), pinned toplam
  (talep + kalem sayısı), kalem = satır adedi, red/iptal listede kalır. `RequestStatusOptions` = durum tek kaynağı.
  Correlated subquery kaldırıldı (derived-table). Test 709/0 (+17).
- ⏳ **Sıradaki rapor:** kullanıcı seçecek (aday: Stok Sayım / Stok Durumu — standart raporlarda kalan son ikisi;
  ardından yönetici raporları). Önce ANALİZ.
  **İleride ayrı iş adayı:** bakım işçilik/servis maliyeti (şema + form), çoklu para birimi kur dönüşümü,
  **"Alan/Kolon Yönetimi" ekranı** (tüm raporlar standarda geçtikten sonra + özel-alan özelliğiyle birlikte).

---

## ✅ Rapor ortak mimarisi — Birim 4: Ortak tablo bileşeni (2026-08-07) — TAMAMLANDI (yayına hazır)

Genel amaçlı, yeniden kullanılabilir tablo bileşeni (web `DwDataGrid` + masaüstü `GridController`/`DataGridView`):
kolon-altı filtre (Excel-benzeri), başlık-tık sıralama, sürükle-genişlik, gizleme/yeniden sıralama + **kullanıcı-
bazlı kolon tercihi** (sıra/genişlik/gizli aktif; **pinned + varsayılan sıralama altyapıda hazır**, UI kapalı).
Ekran açılışında **tek sorgu** (`ListPrefs.GetAll`, Migration058). Filtre/sıralama istemcide (tekrar sorgu yok;
çekirdek `GridDataView`). **Yalnız Raporlar'a uygulandı; diğer ekranlar dokunulmadı.** Build 0 hata, test 633/0
(+17). Görsel doğrulama masaüstünde **1.0.112'de kullanıcıyla** (Avalonia önizlemesi yok).

**Sıradaki (ayrı görev):** Raporlar tek tek yeniden tasarlanacak — **önce Araç Raporu** (kullanıcı onayıyla).

---

## ✅ Fotoğraf biçim uyarısı + detay paneli oto-kapanma (2026-07-25) — DÜZELTİLDİ (masaüstü paket + web canlı)

**1 — Fotoğraf biçim uyarısı:** Sunucu yalnız **JPEG/PNG** kabul ediyordu (magic-byte doğrulaması) ama dosya
seçici webp/bmp'ye de izin veriyordu → kullanıcı bunları seçince kaydederken şifreli/sessiz hata alıyordu.
**Masaüstü:** `PhotoPickHelper` — seçilen dosyalar EKLENİRKEN doğrulanır; desteklenmeyen biçimde dosya adı +
nedeniyle uyarı penceresi ("Desteklenen biçimler: JPEG (.jpg, .jpeg), PNG (.png)"), yalnız geçerli dosyalar
forma eklenir. Dosya seçici filtresi de gerçek desteklenen biçimlere daraltıldı. **Web** (aynı sorun, platform
önceliği kuralı gereği kontrol edildi ve düzeltildi): `InputFile accept` daraltıldı + `OnFiles` uzantı bazlı
ön-kontrol yapıp reddedilenler için `MudAlert` uyarısı gösterir (Materials.razor + Vehicles.razor).

**2 — Detay paneli oto-kapanma (yalnız masaüstü):** Periyodik eşitleme yenilemesi (~15sn, `RefreshData→Load`)
listeyi `Items.Clear()` ile sıfırdan kuruyordu → seçili kayıt kayboluyordu → detay paneli kapanıyordu. Artık
`Load()` seçili kaydın kimliğini saklayıp yeniden kurulan listede tekrar seçiyor → panel açık kalır (yalnız
kayıt gerçekten kalktıysa/silindiyse kapanır). Materials + Vehicles ViewModel. Web'de eşdeğer sorun YOK (web'de
periyodik oto-yenileme mimarisi yok, detay ayrı modal).

**Doğrulama:** Backend değişmedi (591 test yeşil, regresyon yok). UI değişiklikleri (masaüstü Avalonia + web
InputFile akışı) bu ortamda tıklanarak test edilemedi — masaüstü test edilemez, web'de login formuna şifre
girme yasak. **Kullanıcı canlıda doğrulamalı.** Masaüstü paket bekliyor (rol-seçici düzeltmesiyle birlikte gidecek).

---

## ✅ Şube-bazlı veri filtreleme + şifre/görünürlük (2026-07-25) — DÜZELTİLDİ (1.0.91 paketiyle gidecek)

**1 & 2 (şifre sıfırlama + kullanıcı görünürlük):** Zaten 1.0.91 kodunda (sunucu-tabanlı Kullanıcı/Yetki ekranları,
şifre sıfırla sunucuya yazıyor → login'de must_change → yeni şifre; liste herkese açık, düzenleme/sıfırlama admin).

**3 & 4 (şube filtreleme) — YENİ:** Uygulama veriyi yazarken şubeyi kaydediyordu ama OKURKEN filtrelemiyordu.
Yeni `BranchScope` (Application/Security): belirli şubeyle girişte (`OperatingBranchId` dolu) veri o şubeye
filtrelenir; "Tüm Şubeler" (null) → hepsi; şubesi olmayan ESKİ kayıtlar gizlenmez (veri kaybı yok); admin dahil
herkes seçili şubeye göre. Uygulandı: **araç grid, günlük faaliyet (liste+grid), yakıt dağıtım/depo, bakım kaydı,
talep, stok hareketi** + **NORMAL raporlar** (Genel/Yakıt/Bakım/Depo/Talep). **Yönetici raporları FİLTRESİZ**
(tüm şubeler). Malzeme firma-geneli (şube yok) → filtrelenmez. Servis katmanında → masaüstü (OperatingBranchId
dolu) düzelir, web değişmez. 2 test (BranchScopeTests). 591 test (580 SQLite yeşil).
Not: Personel listesi kendi mevcut şube-kapsamını kullanıyor (dokunulmadı); web şube bağlamı ileride.
**Masaüstü 1.0.91 YAYINLANDI (2026-07-25)** — sunucu latest=1.0.91. Commit `b24efc3`.

---

## ✅ Masaüstü Kullanıcı/Yetki ekranları sunucu-tabanlı (2026-07-25) — DÜZELTİLDİ (paket bekliyor)

**Sorun:** Masaüstü Kullanıcı Tanım + Yetkiler ekranları yalnız YEREL DB okuyordu; kullanıcılar sunucu-otoriteli
ve masaüstüne çekilmiyordu → (1) başka yerde oluşturulan (babanın) kullanıcı masaüstünde görünmüyordu, (2) yetki
güncellemesi yerelde kalıp hedefe ulaşmıyordu / görünmeyen kullanıcı seçilemiyordu.

**Çözüm:** Her iki ekran ÇEVRİMİÇİYKEN **sunucu-tabanlı** (`OrgServerClient`): kullanıcı listesi + roller + yetkiler
sunucudan çekilir; yetki kaydı / rol / şifre-sıfırla / aktif-pasif / sil / şube-atama / Tüm-Şubeler sunucuya yazılır
(hedef kullanıcıya ulaşır). Çevrimdışı → yerel salt-okuma + "çevrimiçi gerektirir" uyarısı. Admin hedefte matris
tam-işaretli+salt-okunur (task 1) korunur. Uç şekilleri canlı doğrulandı (`/api/users`, `/roles`, `/permissions`).
589 test. **1.0.91 paketiyle yayınlanacak.**

---

## ✅ Çıkış hızı + Şube/Kullanıcı kayıt kaybı (2026-07-25) — DÜZELTİLDİ (masaüstü; paket bekliyor)

**Sorun 1 — Çıkış çok yavaş:** Kapanışta bekleyen veriyi göndermek için 10 sn'ye kadar bekleniyordu →
**2 sn**'ye indirildi (MainWindow.OnClosing). Gönderilemeyen veri sonraki girişte zaten push edilir (kayıp yok).

**Sorun 2 — Şube/Kullanıcı re-login'de kayboluyordu (VERİ KAYBI):** Kök neden: şube+kullanıcı SUNUCU-OTORİTELİ
(iş senkronuna dahil değil; kod/şifre/hash taşır) ve her girişte sunucudan aynalanıyor → masaüstünde YALNIZ
yerele yazılan kayıt sonraki girişte siliniyordu. **Çözüm:** masaüstü ÇEVRİMİÇİYKEN şube/kullanıcı oluşturma/
düzenleme/silmeyi doğrudan **SUNUCU API'sine** yapar (yeni `OrgServerClient`) → sunucu-otoriteli olur, aynalama
(`BranchMirror`) korur; kullanıcı yerele sunucu id'siyle işlenir (`UserService.ImportServerUser`, çift kayıt yok).
Çevrimdışıysa "bu işlem çevrimiçi gerektirir" uyarısı. 4 test (ImportServerUser). 589 test (578 SQLite yeşil).
Not: CompaniesViewModel'deki firma-kurulum şubesi kapsam dışı (süper admin akışı).
**Masaüstü 1.0.90 YAYINLANDI (2026-07-25)** — sunucu latest=1.0.90. Commit `a78046e`.

---

## ✅ Yetki ekranı + Kullanıcı görünürlük/şifre sıfırlama (2026-07-25) — 3 işten 1-2 TAMAM (web+API canlı; masaüstü paketi bekliyor)

**İş 1 — Yetki ekranı önceden-işaretli:** Admin/Süper Admin hedef seçilince matris artık TAM işaretli + salt-okunur
+ bilgi notu (admin granular yetki tutmaz, bypass ile hepsine erişir; önceden BOŞ açılıyordu → "yetkisi yok" izlenimi).
Staff hedefte mevcut davranış (yükleme backend testiyle kanıtlı). Web + masaüstü. Commit `9c886f3`.

**İş 2 — Kullanıcı görünürlük + şifre:** (a) Kullanıcı listesi TÜM oturum sahiplerine açık; Personel SINIRLI görür
(rol gizli), düzenleme/şifre yalnız admin. Menüde "Kullanıcı Tanım" herkese görünür (Yetkiler/Şablonlar admin-gated).
(b) Şifre kullanıcı tanımından DEĞİŞTİRİLMEZ; yerine **Şifre Sıfırla** — geçici şifre = kullanıcı adı, kullanıcı
ilk girişte kendi şifresini belirler (must_change). Web + masaüstü + API (`/api/users/{id}/reset-password`). 3 test.

**İş 3 — Masaüstü otomatik güncelleme akışı:** TAMAM (kodda). Otomatik AÇIKken login sonrası **eşitleme ekranında**
(ana pencere açılmadan) en son paket SESSİZCE indirilir → **Kur / Ertele**. Kur→kurar+yeniden başlatır; Ertele→uygulama
açılır, **10 dk** sonra tekrar sorulur (indirilen paket saklanır, tekrar inmez). Onay vermeden **kapatmaya çalışırsa
zorla kur** (MainWindow kilidi). Yarım kalan kurulum: sürüm hâlâ eskiyse sonraki girişte akış yeniden indirir+kurar
(InstallAndRestart staging'i sıfırdan açar + backup/rollback → baştan sağlam). Otomatik KAPALIYKEN eski davranış
(Dashboard'da manuel buton). Ortak durum: yeni `AutoUpdateService` (eşitleme ekranı + ShellViewModel timer +
kapatma-kilidi aynı paketi paylaşır). Masaüstü-only → **1.0.89 YAYINLANDI (2026-07-25)** (İş 1+2 masaüstü karşılıklarıyla birlikte; sunucu latest=1.0.89). Commit `5f8db8b`.

---

## ✅ Durum Rapor + Rapor Excel Dışa Aktarma (2026-07-25) — CANLI + DOĞRULANDI (web+API); masaüstü paketi bekliyor

**Ne yapıldı:** Yönetici raporları altına **Durum Rapor** (şube bazlı sayısal özet: Araç şablonlu/şablon-dışı;
Personel/Bakım/Yakıt/Talep/Günlük toplamları; Malzeme firma-geneli tek satır — şubesi yok; tarih filtreli).
Raporlar + Yönetici Raporları ekranlarına **Excel'e Aktar** — **iki ayrı özel yetki** (`btn-export-reports` /
`btn-export-mgr-reports`), deny-by-default; yetki yoksa "yetkiniz yok" uyarısı (UI + API fail-closed).

**Kapsam:** ReportService.StatusReport · API `/api/reports/{type}/export` + `status` tipi · web Reports.razor
(sekme+buton) · masaüstü ReportsViewModel/ReportsView · AppModules (2 özel buton). PG-güvenlik: tüm rapor
sayımları `CAST(... AS INTEGER)` (MaterialsByTemplate/Fuel/Requests COUNT dahil — PG'de int8→int4).

**Kanıt:** 585 test (574 SQLite yeşil + 11 gerçek PG). PG uçtan-uca testi **gerçek Neon PG'de geçti**.
Canlı prod (salt-okuma, veri değişmedi): Durum Rapor **200** (Firma Geneli 2459 malzeme + şube kırılımı),
materials-template **200**, status/export **200** (geçerli xlsx). Commit `af11ba0`.

**Kalan:** Masaüstü paket (Durum Rapor + Excel export kodda hazır, 1.0.88'de değil) → sıradaki sürümde yayınlanacak.

---

## ✅ Düzenleme kilidi — KAPSAM TAMAMLANDI (İş #6, 2026-08-09; yayın bekliyor)

**Kapsanan ekranlar:** Malzemeler · Araçlar · Personel · Bakım Tanımları *(2026-07-22)* ·
**Talepler** · **Şube/Şantiye** *(İş #6, 2026-08-09)* · Günlük Faaliyet + Bakım kaydı metadata'sı
*(İş #5, 2026-08-09)* — hepsi masaüstü + web + API.

**İş #6'da bulunan açık:** Talepler ve Şube/Şantiye'de `version` sütunu vardı ve her kaydetmede
ilerliyordu ama **hiç kontrol edilmiyordu** → iki kişi aynı talebi/şubeyi düzenlediğinde ikincisi
birincisini sessizce eziyordu. Mevcut `EditLockGuard` deseni bu iki servise de uygulandı; **yeni
mekanizma yazılmadı, migration gerekmedi** (sütunlar zaten vardı).

**KAPSAM DIŞI (kasıtlı):** Yakıt kayıtları ve Günlük Faaliyet/Bakım'ın **stok + sayaç** alanları
düzenlenemez — bunlar §4 gereği ekle-only defter kayıtlarıdır (iptal + yeniden gir). Değiştirilebilen
alanlar (açıklama, operatör, teknisyen, süre) İş #5'te kilit ile birlikte açıldı.

**Canlı kanıt:** Malzeme/Araç/Personel için güncel sürümle PUT **200**, eski sürümle PUT **409**,
ilk verinin ezilmediği doğrulandı (geçici test kayıtlarıyla, sonra silindi).

---

## (eski kayıt) Düzenleme kilidi — Malzeme aşaması

**Sorun:** `version` yazılıyordu ama hiç kontrol edilmiyordu → iki kişi (ya da iki makine) aynı kaydı
düzenlerse ikincisi birincisini **sessizce eziyordu**.

**Karar:** gerçek "kilit" değil, **sürüm karşılaştırması**. Sunucu tabanlı kilit çevrimdışı makinede
işlemez ve program çökerse kayıt kilitli kalır; DepoWise çevrimdışı çalışabilmeli. Sürüm kontrolü
çevrimdışı dahil her zaman çalışır ve asıl zararı (sessiz üzerine yazma) önler.

- Malzeme: masaüstü ana ekran + çift-tık hızlı düzenle + web dialog + API (409 Conflict). ✓
- Kullanıcıya sorulur: **"Kaydı yenile"** / **"Formda kal"** — yazdıkları kaybolmaz.
- Sürüm gönderilmezse eski davranış sürer (geriye uyumlu; çalışan çağrılar bozulmadı).
- Canlı kanıt: eski sürümle kaydetme **409**, ilk verinin **ezilmediği** doğrulandı.

**Kalan:** Araçlar · Personel · Günlük Faaliyet · Yakıt · Bakım ekranlarına aynı desen (her biri:
detay kaydına `version`, formda tutma, `expectedVersion` ile kaydetme, 409/uyarı).

---

## ✅ Eşitleme çekirdeği Z1–Z5 + QA (2026-07-22) — TAMAMLANDI (API canlıda)

- **Z1** tek eşitleme kapısı (`SyncGate`) — 6 giriş noktası artık aynı anda çalışamaz.
- **Z3** atlanan kayıtlar otomatik yeniden denenir; 5 denemede çözülmezse "poison" + kalıcı uyarı
  (rozet artık kaybolmuyor).
- **Z5** üst barda daima görünür, tıklanabilir senkron rozeti + durum paneli.
- **§7 QA motoru yeniden aktif** (kullanıcı isteği) — bkz. CLAUDE.md §7 ve yeni §7.0 token disiplini.
- **QA'de gerçek hata bulundu ve düzeltildi (B-1):** stok hareket defteri (`stock_movements`)
  `updated_at` taşımadığı için delta filtresine hiç girmiyordu → her eşitlemede tüm defter aktarılıyor,
  ayrıca yeni hareket firma sürümünü yükseltmediği için **karşı makine çekmiyordu**. Canlı ölçüm:
  delta **663 → 0 satır**. Detay: `docs/tests/Esitleme_Test_Report.md`.
- Testler: **563/563**. Canlı QA: **7/7** (`node tools/qa/live-sync-check.mjs`).

**Kalan (ertelendi, kritik değil):** `server_seq` (saatten bağımsız sıra), ledger `op_id` idempotency,
yakıt/bakımda LWW kaldırılması, snapshot sayfalama.
**Kullanıcı aksiyonu:** masaüstü **1.0.86** paketi hazır ama yüklenemedi — süper admin parolası
değiştiği için yayınlamayı kullanıcı çalıştırmalı (bkz. DEVAM.md).

---

## 🔴 KRİTİK: Senkron donma + sessiz başarısız push (2026-07-19, ADR-090) — TAMAMLANDI (canlıda)

Test 530/530. Baba dosyası içeri alındıktan sonra web'e ulaşmamıştı. Canlı sunucu doğrulandı: firmada 0
malzeme/0 araç. Kök neden: (1) senkron ağır iş arayüz iş parçacığında çalışıyordu → "donma" şikayeti;
(2) 30sn zaman aşımı büyüyen veride aşılıyor, sessizce yutuluyordu → veri sonsuza kadar ulaşmıyordu.

- Düzeltme: Task.Run + 120sn zaman aşımı + "Eşitle" butonunda görünür hata. Masaüstü **1.0.69**.
- **Kullanıcı aksiyonu gerekli:** baba makinesini 1.0.69'a güncelleyip "Eşitle"ye bassın (ya da normal giriş) —
  geçmiş içe aktarılan veri o an push edilecek.
- Detay: `docs/DECISIONS.md` ADR-090.

---

## ⏳ 12 maddelik yeni istek listesi (2026-07-19) — SÜRÜYOR

Kullanıcı: "sıradaki maddeleri yap en son test edeceğim." Opus 4.8. Durum:

1. ✅ Senkron donma/başarısız push (yukarıda, ADR-090).
2. ✅ Günlük Faaliyet'e 3 yeni kayıt tipi: **İlave Yağ, İlave Filtre, Tamir** — Bakım ile aynı alanlar, yalnız
   bakım tanımı/alt bakım YOK. Web+masaüstü (ADR-091). Yan bulgu: masaüstü/sunucu servis başlatma sırası
   kusuru (Bakım kaydında null-referans riski) da düzeltildi. **Masaüstü 1.0.70'de canlı.**
3. ✅ Çift-tık ile ayrı pencerede Düzelt/Kaydet/Sil (tek tık = mevcut detay paneli korunur) — **Malzemeler +
   Araçlar** (web+masaüstü, ADR-096). Foto/muadil/uyumlu-araç ve sayaç korunur. Kullanıcı "bu ikisinden başla,
   sonrasını kendin belirle" dedi → diğer ekranlar (Günlük Faaliyet/Personel/Stok…) istenirse aynı desenle
   eklenir. **⚠️ Görsel test kullanıcıda** (Avalonia + web giriş bu ortamda çalıştırılamadı).
4. ✅ Malzeme kategorilerinde (ve tüm tanımlarda) fazla boşluk normalize (Migration050).
5. ✅ Tanım Düzenle'de **kilitli/sabit tanımlar** (ADR-092) — her tanım satırı admin tarafından tek tek
   kilitlenebilir (silinemez/düzenlenemez), yeni tanım eklemek ("+") kilitten bağımsız her zaman açık.
   Hiçbir mevcut tanım otomatik kilitlenmedi — **hangi tanımların kilitleneceği kullanıcının/admin'in kararı**
   (ekrandan kilit ikonuyla yapılır).
6. ✅ Semi Modern arama kutusu → Fluent Classic ile aynı tasarım (ADR-093) — köşe/geçiş normalizasyonu
   Search'e de eklendi. **⚠️ Kullanıcı kontrolü gerekli** (bu ortamda Avalonia görsel test edilemedi).
7. ✅ Kural: yeni filtrelenebilir alan eklerken gerekli adımlar → `.claude/rules/list-screens.md`.
8. ✅ Günlük Faaliyet ekranına ADR-087/088/089 grid deseni (filtre+sayfalama+sıralama) — ADR-094.
9. ✅ "Excel'e Aktar" butonu: Malzemeler+Araçlar+Günlük Faaliyet'te TAMAM (web+masaüstü).
10. **Muhtemelen ÇÖZÜLDÜ** farklı makine aynı şube senkron sorunu — madde 1 ile AYNI kök neden (push hiç
    ulaşmıyordu). Kullanıcı 1.0.69+ ile test edip doğrulamalı.
11. ✅ Yeni form kutuları odaklanmadan da arka plandan görünür ayrılsın (ADR-093) — web+masaüstü.
    **⚠️ Kullanıcı kontrolü gerekli** (giriş gerektiren ekranlar bu ortamda test edilemedi).

Detay ve mimari notlar (yapılanlar): `docs/DECISIONS.md` ADR-094 (madde 8/9) + ADR-092 (madde 5) + ADR-093
(madde 6/11) + ADR-091 (madde 2) + ADR-090 (madde 1) + ADR-089 (madde 4/7/9 altyapısı).

**Bu 12 maddelik listede kalan tek iş:** madde 3 (çift-tık ile ayrı pencerede Düzenle/Kaydet/Sil) — kapsam
büyük ("bütün kayıtlara"), hangi ekranlar öncelikli netleştirilmeli.

---

## ✅ 7 maddelik liste paketi (2026-07-18, ADR-089) — TAMAMLANDI (canlıda, masaüstü görsel doğrulama bekliyor)

Test **523/523**. Kullanıcının 7 isteği (2600+ kayıtla çalışırken):
1. Sayfa boyutu varsayılan 25 (kişiye özel). 2. Sayfa no + kayıt bilgisi üstte-solda. 3. Excel-benzeri grid
(taşma yok + sürüklenebilir kolon genişliği, kişiye özel). 4. Tanım düzenleme (rename yetkiye açıldı +
masaüstü satır-içi). 5. Başlıkla sıralama (metin Türkçe A→Z/Z→A, sayısal küçük→büyük). 6. Yeni tanım 50 kar.
7. İçe aktarımda "Tür" harf duyarsız kanonik eşleme + mevcut veriyi düzelt (Migration048).

- **TAMAM + canlıda:** infra (Migration048/049, GridQuery sıralama, TRNOCASE Türkçe collation, MaterialType,
  LookupService 50-kar/rename) + API + **web** (Materials/Vehicles: 25, sıralama, üstte-sol sayfalama,
  Excel-grid) + tanım düzenleme (web+masaüstü Tanımlar ekranı).
- **Masaüstü — TÜMÜ 1.0.68'de canlı:** #1 (sayfa boyutu 25 + kişisel hatırlama), #4 (Tanımlar ekranı satır-içi
  düzenleme), #6 (50 kar), #7 (Tür kanonik + Migration048), #2 (sayfalama üstte-sola), #5 (başlığa tıklayınca
  sırala — yeni `SortHeader` + `IListGridViewModel` arayüzü), #3 (Excel-benzeri yatay kaydırma + sürüklenebilir
  kolon genişliği, kişiye özel kalıcı).
- **⚠️ KRİTİK — kullanıcı doğrulaması gerekli:** bu ortamda Avalonia'yı çalıştırıp tıklama/sürükleme testi
  yapacak araç yok; yalnız **temiz derleme** ile güvence alındı (görsel test YAPILAMADI). Kullanıcı 1.0.68'i
  açıp Malzemeler/Araçlar listesinde: (1) sayfalama tablonun üstünde-solunda mı, (2) başlığa tıklayınca sıralanıyor
  mu (3. tıkta kapanıyor mu), (3) pencereyi küçültünce kolonlar taşmadan kayıyor mu, (4) başlığın sağ kenarından
  sürükleyip kolon genişletebiliyor mu — kontrol etmeli. Sorun çıkarsa bildirsin, hemen düzeltilir.
- **KALAN (kullanıcı):** baba dosyasında para birimi "TL" → "TRY" (Excel'de düzelt).
- Detay: `docs/DECISIONS.md` ADR-089.

---

## ✅ Sayısal kolon filtresi: tam-sayı/karşılaştırma/aralık (2026-07-18, ADR-088) — TAMAMLANDI (canlıda)

Test **509/509** (11 yeni). API+Web deploy edildi, masaüstü **1.0.66** yayınlandı. Kullanıcı ADR-087'nin
filtresini denerken fark etti: "stokta sadece 5 olanları listelemek istiyorum ama bütün içinde 5 olan
malzemeler listeleniyor."

- Malzemede Birim Fiyat/Min Stok/Stok, Araçta Üretim Yılı/Sayaç artık **sayısal** filtre: `5` TAM eşleşir
  (içermez), `>5`/`<5`/`>=5`/`<=5` karşılaştırma, `5-10` aralık (negatif sınır destekli).
- Tanınmayan söz dizimi eski "içerir" davranışına düşer — filtre kutusu asla sessizce boş kalmaz.
- Metin kolonları (Kod/Ad/Marka…) DEĞİŞMEDİ. UI'da söz dizimi ipucu eklendi.
- Detay: `docs/DECISIONS.md` ADR-088.
- **Not:** Bu iş için tarayıcı üzerinden canlı doğrulama YAPILAMADI — kimlik bilgilerini otomatik forma
  girmek güvenlik politikası tarafından engellendi (parola girişi otomasyonu yasak). Doğrulama tamamen
  **509/509 birim testiyle** yapıldı (SearchGrid'e karşı gerçek SQL sorguları). Kullanıcı canlıda kendi
  girişiyle görsel kontrol etmek isterse önerilir.

---

## ✅ Malzeme/Araç Listesi — kolon filtre + sayfalama + kişisel kolon seçimi (2026-07-17, ADR-087) — TAMAMLANDI (canlıda)

Test **497/497** (24 yeni). API+Web deploy edildi, masaüstü **1.0.65** yayınlandı. Kullanıcı 2600+ satır
içeri aldıktan sonra fark etti: liste ekranları da 200 satır sınırına dayanıyordu. İstek: sütun bazlı
filtre (içerir + başlangıca göre) + sayfa boyutu + numaralı sayfalama + sağ tık "Kolon Ayarla" (kişiye
özel, farklı kullanıcıda görünmez).

- Yeni `SearchGrid` uçları (gerçek `COUNT`+`LIMIT/OFFSET`) — eski hızlı-arama uçları dokunulmadı.
- Kolon kataloğu = form alanları (fotoğraf hariç); kolon tercihi KİŞİSEL (Migration 047).
- Web + masaüstü ikisinde de filtre kutuları + sayfalama + kolon seçici.
- Detay: `docs/DECISIONS.md` ADR-087.
- **⚠️ Masaüstü UI görsel doğrulanamadı** (Avalonia'yı bu ortamda çalıştırıp tıklama testi yapacak araç yok)
  — temiz derleme + backend testleriyle güvence alındı. Web tarayıcıda uçtan uca doğrulandı.
- **Kalan:** kullanıcı gerçek makinede 1.0.65'i denesin (masaüstü UI ilk gerçek testi).

---

## ✅ Açılış stoğu NEGATİF olabilir (2026-07-17, ADR-086) — TAMAMLANDI (canlıda)

Test **473/473** (6 yeni). API+Web deploy edildi, masaüstü **1.0.64** yayınlandı. Babanın dosyasında 63
satırda negatif Açılış Stok vardı; içe aktarım reddediyordu.
Kullanıcı: "eksi stok kontrolünü kaldıralım; devralan firmalar mevcut stoklarını girebilsin."

- **Yalnız BAŞLANGIÇ stoğu** negatif olabilir (içe aktarım + web/masaüstü form + API). **Operasyonel çıkış
  negatif-bakiye engeli KORUNUR.** Fiyat/Min Stok yine negatif olamaz.
- Ledger temiz: negatif açılış = `stock_movements` pozitif miktar + direction=−1; yalnız türetilmiş bakiye eksi.
- Detay: `docs/DECISIONS.md` ADR-086.
- **⚠️ Babanın dosyasında 2. engel (bu iş kapsamı DIŞINDA):** para birimi her satırda "TL" — sistem TRY/USD/EUR
  bekler. Excel'de TL→TRY yapılmalı (ya da ayrı talep gelirse otomatik eşleme eklenir).
- **Kalan:** API+Web deploy, masaüstü 1.0.64 yayını.

---

## ✅ Makine "tanım sıfırlama" (2026-07-17) — TAMAMLANDI (canlıda, DESKTOP-SIKIB3U testi bekleniyor)

Test **467/467** (8 yeni, `MachineResetTests`). API+Web deploy edildi, masaüstü **1.0.63** yayınlandı.
Kullanıcı: babasının makinesi test firmasıyla giriş yapmıştı, asıl firmayla giremedi sandı → "makine
tanımını sıfırlayan buton + login sonrası otomatik algılama" istedi.

- **Makine Yönetimi'nde (süper admin) "Tanımı Sıfırla" butonu:** makineyi TÜM firmalardan koparır
  (iş verisi ETKİLENMEZ, özel kod GEREKMEZ). Şema: Migration 046 (`machine_resets`).
- **Masaüstü algılama:** girişten sonra eşitleme adımında (purge/yerel-sıfırlamadan ÖNCE) künyeyi görür →
  yerel makine-firma/şube önbelleğini temizler → **girişi iptal eder, login ekranına döner**.
- Sonraki giriş yapan kullanıcı makineyi kendi firması/şubesiyle yeniden tanımlar (mevcut "ilk kurulum" akışı).
- Detay: `docs/DECISIONS.md` ADR-085.
- **Kalan:** API+Web deploy, masaüstü yeni sürüm yayını (1.0.63), canlıda gerçek doğrulama.

---

## ✅ Personel içe aktarımı (2026-07-16) — TAMAMLANDI

Test **459/459** (34 yeni). Kullanıcı: "toplu personel listesini içeri almak istiyorum; saha personeli
veya kullanıcı ise sütunda nasıl belirtmem gerek?"

**Şablon (7 sütun, formla birebir):** Ad Soyad* · Unvan · Telefon · Şube · Aktif · Saha Personeli · Kullanıcı Adı

- **Saha Personeli = Evet** → uygulamaya girmez. **Kullanıcı Adı** → MEVCUT hesabı bağlar.
  **İkisi birbirini dışlar** (birlikte yazılırsa satır reddedilir).
- ⚠️ **İçe aktarım hesap AÇMAZ** — hesap Kullanıcılar ekranından açılır, burada yalnız bağlanır.
- Evet/Hayır esnek (Evet/E/Var/X/1/true · Hayır/H/Yok/0/false); tanınmayan değer reddedilir.
- Mükerrer anahtarı: **normalize ad**. Aynı isimli iki farklı kişi varsa ikincisi atlanır (raporlanır).
- **🔴 KUSUR:** Personel + Malzeme **DIŞA aktarımı** 200 satırla sınırlıydı (`MaxLimit=200`) → düzeltildi
  (`AllPages` keyset imleci).

---

## ✅ Şablonlar tam alan + "Arızalı" + 200 satır sınırı kusuru (2026-07-16) — TAMAMLANDI

Test **425/425** (48 yeni). Kullanıcı: "şablonlarda formdaki her alan olmalı; tanım ekleme, import'ta
otomatik oluşsun; babamın dosyası ~2600 satır, altında test yapma."

- **🔴 KUSUR (hacim testi yakaladı):** `VehicleService.List` varsayılanı 200, `PageRequest.MaxLimit` 200.
  →200'den fazla araç/malzemede **bakım/muayene/yakıt aktarımı 201+ araçları "bulunamadı" diye reddediyor**,
  araç/malzeme **mükerrer kontrolünü kaçırıp kopya oluşturuyordu**. Dünkü yakıt import'unda da vardı.
  Düzeltildi + 3 regresyon testi (250 kayıtla).
- **Şablonlar = form** (fotoğraf hariç): Araç 4→15, Malzeme 6→15, Bakım +2, Muayene +2 sütun.
- **Otomatik tanım oluşturma** (`ImportLookupResolver`, önbellekli) + **"oluşturulan tanımlar" raporu**.
- **"Arızalı" durumu** + ortak `VehicleStatus` kaynağı. **Yan kusur:** Arızalı durum notu serviste
  sessizce siliniyordu → düzeltildi. Masaüstü durum kutusu artık Türkçe.
- **Bakım ekranında araç durumu** (web+masaüstü) + `POST /api/vehicles/{id}/status`.

### Kullanıcı kararıyla ŞABLON DIŞI bırakılanlar
- **Bakım şablonunda "Araç Durumu" YOK** — kullanıcı bunu yalnız bakım EKRANINA istedi (toplu durum
  değişikliği Araç şablonunun "Durum" sütunuyla yapılır).
- **Bakım şablonunda malzeme satırları YOK** — stoktan düşürür; Excel'den toplu stok hareketi istenmiyor.

---

## ✅ Yakıt içe aktarımı + import kusurları (2026-07-16) — TAMAMLANDI

Test **377/377**. Kullanıcı: "babam Excel'de yakıt tutuyor, içeri almam lazım ama alanlar eksik;
bütün içe/dışa aktarma sürecini kontrol et, veriler sıkıntı olmadan lazım."

- **⚠️ BULUNAN KUSUR (10 kat sessiz bozulma):** malzeme import'u `Money.Parse` kullanıyordu → Türk Excel'inin
  `"12,5"` değeri **125** oluyordu (virgül binlik ayırıcı sayılıyordu). Fiyat ve min-stok 10 kat şişiyordu,
  hata da vermiyordu. **Düzeltildi + 6 regresyon testi.** (`Money.Parse` değiştirilmedi — DB okuması için doğru.)
- **⚠️ İkinci kusur:** Excel başlıkları harf-duyarlıydı → elde yazılmış "litre" başlığı "Litre" ile eşleşmiyor,
  satır "zorunlu alan boş" diye sessizce reddediliyordu. Artık **harf duyarsız**.
- **Yeni: Yakıt Dağıtım + Yakıt Depo Girişi** içe/dışa aktarımı. Yalnız **Araç + Litre zorunlu**; eksik alanlar
  makul varsayılana düşer. Araç **iç kod veya plaka** ile eşlenir. Depo yetersizse **önceden** uyarılır.
  **Aynı dosya iki kez aktarılırsa tekrarlanmaz.**

### Kalan (kullanıcı bildirecek)
- Yakıt dışında başka bir Excel türü çıkarsa kullanıcı haber verecek ("farklı bir şey çıkarsa bilgi veririm").
- **Araç import'u şubeyi ATAMIYOR** (canlı ekranda şube zorunlu, import'ta boş kalıyor) — kullanıcı isterse eklenir.
- İmport/Export ekranı **yalnız masaüstünde** var; web'de yok — kullanıcı isterse eklenir.

---

## ✅ Firma "yerel sıfırlama" isteği (2026-07-16, ADR-084) — TAMAMLANDI

Test **354/354**. Şema **Migration 045**. Kalıcı Silme'den (ADR-083) FARKI: **YIKICI DEĞİL**.

- **Firma Tanım** listesine "Yerel Sıfırlama İste" butonu (turuncu ikon, süper-admin-only): o firmanın
  TÜM makineleri bir sonraki çevrimiçi girişte yerel kopyalarını **bir kez** temizler, sıfırdan yeniden
  doldurur. **Firma sunucuda durur, erişim engellenmez** — özel kod gerekmez.
- Makine o an kapalı/çevrimdışı olsa da istek sunucuda **bekler**; makine aktif olup çevrimiçi giriş
  yaptığında algılanır (bekleme süresi sınırsız).
- **Yan düzeltme (aynı kökten):** firma bilgisi güncellenince yalnız İSİM yerel makinelere yansıyordu;
  diğer alanlar (vergi/adres/kota) hiç aynalanmıyordu → artık `CompanySyncService.MirrorLocalAsync` TÜM
  alanları aynalıyor (bu düzeltme olmadan yeni özellik, sıfırlama sonrası bu alanları boş bırakırdı).

---

## ✅ Kalıcı Silme ekranı + özel kod (2026-07-16, ADR-083) — TAMAMLANDI

Test **341/341**. Şema **Migration 044**. ⚠️ **GERİ ALINAMAZ** — `CLAUDE.md` §4'ün bilinçli istisnası.

- **Kalıcı Silme** (web, süper-admin-only): firma + TÜM verisi (fotoğraf/yedek dahil) fiziksel silinir.
  Firma Tanım *pasife alır*; bu ekran *siler*. Kilit: **özel kod + şifre + firma adını birebir yazma**.
- **Özel kod:** süper adminin ilk web girişinde oluşturulur, hash'lenir; unutulursa şifreyle yenilenir.
  Kod yoksa ekran açılmaz (fail-closed). Diğer rollerin giriş akışı değişmez.
- **Kendi firmanı silemezsin** (ADR-064/068: kilitlenme + 401 dersi) — hem serviste hem ekranda engelli.
- **Masaüstü:** yeni ekran/alan YOK; eşitleme adımı künyeyi görüp yerel veriyi siler → login'e döner.
  Çevrimdışıysa yerel veriye DOKUNULMAZ (fail-safe).

---

## ✅ Firma/şube karışmasını önleme — Faz 1-2-3 (2026-07-16) — TAMAMLANDI

Test **332/332**. Kullanıcı şikâyeti: "süper adminken şube ekranında firma kutusu yok; birden çok firma
olacak, hiçbir tanım karışmamalı."

- **Faz 1 — Şube ekranı firma kutusu:** kutu `_companies.Count > 1` koşuluna bağlıydı **ve** firma listesi
  hatası sessizce yutuluyordu → süper adminde kutu hiç görünmüyordu. Artık daima görünür + hata gösterilir.
  Varsayılan **kendi firman** (eskiden alfabetik ilk firma → yanlış firmaya şube açma riski). Masaüstüne de eklendi.
- **Faz 2 — Aktif Firma seçici (üst bar, web):** `/api/auth/select-company` ile oturum firması değişir → tüm
  ekranlar o firmada çalışır. **Ekran-başı firma kutusu bilinçli olarak REDDEDİLDİ** (CLAUDE.md §4: firma
  kimliği yalnız güvenilir oturumdan gelir; 30 ekrana kutu = risk + maliyet). Masaüstünde firma girişte seçilir;
  üst barda aktif firma + çalışma şubesi rozeti eklendi.
- **Faz 3 — "Tüm Şubeler" koruması:** bu modda şube bazlı 7 ekranda yazma engellenir (uyarı penceresi →
  çıkış/giriş ile şube seç). Okuma serbest. Gerçek kusur: bu modda stok hareketi `branch_id NULL` düşüyordu.

---

## ✅ Kullanıcıda firma seçimi + Firma Tanım'da ilk şube (2026-07-16) — TAMAMLANDI

**Şu an bekleyen iş YOK.** Test **328/328** yeşil (5 yeni tenant testi).

- **Kullanıcı Tanım — firma seçme kutusu (yalnız süper admin):** seçilen firmaya kullanıcı açılır.
  Web'de kutu vardı ama **şube listesi eski firmadan kalıyordu** (yanlış firmaya şube atama riski) → firma
  değişince şube listesi yenileniyor. **Masaüstünde kutu hiç yoktu** → eklendi. Personel bağlama yalnız
  kendi firmasında (personel listesi tenant'a kilitli); başka firmada açıklama gösterilir.
- **Firma Tanım — "İlk Şube / Şantiye Adı" (yeni firmada zorunlu):** firma ile birlikte o firmaya bağlı
  şube oluşur. Sebep: şubesiz firmaya kullanıcı açılamıyordu (çıkmaz sokak). Düzenlemede alan gizli.
- Şube açılamazsa firma kaydı **durur**, kullanıcıya açıkça söylenir (elle ekleyebilir).

---

## A. Bekleyen İşler — BÜYÜK YETKİ/EKRAN PROMPTU (2026-07-12)

Kullanıcı ~16 maddelik büyük bir yetki+ekran revizyonu verdi. **Adım adım, test edilebilir dilimler**
halinde uygulanıyor (her dilim: build + ilgili test + commit + push). Motor: **Opus 4.8** (güvenlik/rol/tenant).

### ✅ Adım 1 — Yetki ağacı temeli (TAMAMLANDI, test 283/283, DEPLOY EDİLMEDİ)
- ✅ **Sync yetkisi kaldırıldı** (ölü madde; eşitleme cihaz-token bazlı, her kullanıcıda zaten aktif). Kullanıcı onayıyla.
- ✅ **Talep ikiye bölündü:** `requests` = **Talep Formu**, yeni `request_approval` = **Talep Onaylama**
  (ayrı ekran+yetki). Onay/ret artık `request_approval` Edit ister. `btn-approve` kaldırıldı + **Migration035**
  mevcut yetkileri yeni modüle taşıdı. Web+masaüstü onay butonu yeni yetkiye bağlandı (eski UI/servis mismatch giderildi).
- ✅ **Özel işlem yetkileri ağacın içinde** listeleniyor (PermMatrix tek-onaylı satırlar; web). Masaüstü zaten aynı panelde.
- ✅ **Eksik ekran denetimi:** tüm operasyonel ekranlar ağaçta; eksik yok (`company-permissions`/`developer`/`trash` gerekçeli hariç).
- 📄 Rapor: [docs/tests/Yetki_Agaci_Test_Report.md](docs/tests/Yetki_Agaci_Test_Report.md).

### ✅ Adım 2 — Yeni ara rol + delegasyon tavanı + süper-admin-only reorg (TAMAMLANDI, test 294/294, DEPLOY EDİLMEDİ)
- ✅ **"Kısıtlı Süper Admin"** rolü (admin ile süper admin arası); admin bypass'ı yok; yalnız süper admin atar (Migration036).
- ✅ Süper-admin-only ekranlar (Kota, Canlı Sunucu, Yedekler, Makine, Güncelleme, Firma Tanım) yalnız süper adminde;
  süper admin **Kısıtlı Süper Admin'e** devredebilir. **Kota İzleme** süper-admin-only oldu.
- ✅ **Delegasyon tavanı + ağaç görünürlüğü:** aktör yalnız kendi verebileceği yetkileri görür; veremeyeceği ağaçta yok.
- ✅ Firma Yetki Kontrol modeli **Serbest / Admin / Süper Admin** (Global kilit kaldırıldı; Migration037).
- ✅ Admin'e yükseltme uyarısında **sebep ekranlar madde madde** listeleniyor (web + masaüstü).
- 📄 Rapor: [docs/tests/Yetki_Rol_Delegasyon_Test_Report.md](docs/tests/Yetki_Rol_Delegasyon_Test_Report.md).

### ✅ Adım 3 — Firma Tanım: ayrı admin/personel kotası + makine kotası (TAMAMLANDI, test 298/298, DEPLOY EDİLMEDİ)
- ✅ `max_admins` (admin) + `max_users` (normal/personel) AYRI; **%20 admin kuralı kaldırıldı** (Migration038).
- ✅ **Makine kotası** (`machine_quota`) Firma Tanım ekranında (web + masaüstü). Kota enforcement + QuotaMonitor güncellendi.
- 📄 Rapor: [docs/tests/Firma_Tanim_Kota_Test_Report.md](docs/tests/Firma_Tanim_Kota_Test_Report.md).
### ✅ Adım 4 — Yetki Şablonu: firma seçimi + tüm firmalar + firma-bazlı görünürlük (TAMAMLANDI, test 302/302, DEPLOY EDİLMEDİ)
- ✅ `scope_all` kolonu (Migration039); şablon bir firmaya veya Tüm Firmalar'a. Ağaç seçilen firmanın admine açık modülleri.
- ✅ `ListForUserCreation`: kullanıcı-oluşturma yetkili aktör kendi firması + tüm-firma şablonlarını görür (tenant izolasyonu).
- ✅ Web firma seçici + kapsam sütunu; Users ekranı şablon listesi `for-user` (web + masaüstü).
- 📄 Rapor: [docs/tests/Yetki_Sablonu_Test_Report.md](docs/tests/Yetki_Sablonu_Test_Report.md).
### ✅ Adım 5 — Malzeme yeni-kayıt şablonu + şablon-dışı uyarı (TAMAMLANDI, test 307/307, DEPLOY EDİLMEDİ)
- ✅ `material_templates` tablosu + servis + modül + web yönetim ekranı (Malzeme menüsü); malzeme create'te şablon seçici.
- ✅ Görünürlük **oluşturana göre** (kullanıcı onayı): admin=global, diğerinin şablonu yalnız kendisine (araç dahil; Migration040).
- ✅ Şablon-dışı kayıtta uyarı ("Ana Yetkiliye Bilgi verilmelidir! Şablon dışı kayıt girmektesiniz!") — malzeme + araç, web + masaüstü.
- ⚠️ Masaüstü Malzeme Şablonları YÖNETİM ekranı (Avalonia) eklenmedi (web'den yönetilir); masaüstünde seçim+uyarı çalışır.
- 📄 Rapor: [docs/tests/Malzeme_Sablonu_Test_Report.md](docs/tests/Malzeme_Sablonu_Test_Report.md).
### ✅ Adım 6 — Kullanıcı oluştururken şube zorunlu (TAMAMLANDI, test 312/312, DEPLOY EDİLMEDİ)
- ✅ **Admin dahil** tüm firma kullanıcılarında **şube/şantiye zorunlu**; muaf yalnız Süper/Kısıtlı Süper Admin. Admin firmanın herhangi bir şubesiyle geçer.
- ✅ Şube yoksa engelle + yönlendirme mesajı; şube firmaya ait/geçerli olmalı. Web'de zorunlu alan + şube-yok uyarısı.
- ✅ Enforcement oluşturma-akışı sınırında (API + masaüstü VM); mevcut testler bozulmadı (`ValidateBranchForNewUser`).
- 📄 Rapor: [docs/tests/Kullanici_Sube_Zorunlulugu_Test_Report.md](docs/tests/Kullanici_Sube_Zorunlulugu_Test_Report.md).

### ✅ Adım 7 (SON) — Login ekranları yeni tasarım (TAMAMLANDI, build 0 hata, DEPLOY EDİLMEDİ)
- ✅ Web + masaüstü login: koyu lacivert tema + **kurumsal iş-makineleri silüet zemini** (yarı şeffaf) + logo + ikonlu inputlar + amber DEVAM.
- ✅ Web: parola göster/gizle; Masaüstü: Beni hatırla + "veya" ayıracı + **Web'de Giriş Yap** (yeni OpenWeb komutu). Çok-adımlı akışlar korundu.
- ✅ Arka plan `login-bg.png` SkiaSharp ile üretildi (depo/vinç/kamyon/ekskavatör). Not: lisanslı stok foto indirilemedi → özel vektör sahne; telifli foto verilirse tek dosya değişimiyle takılır.

---

## ✅ BÜYÜK YETKİ/EKRAN PROMPTU TAMAMLANDI + CANLIYA ALINDI (2026-07-13)
**Şu an bekleyen iş YOK.** Adım 1–7 kod + test (313/313) + **deploy** tamamlandı:
- **API** (`depowise-erp`) deploy → health **200** (Migration 035→040 sunucuda uygulandı).
- **Web** (`depowise-web`) deploy → login **200** (yeni tasarım + fotoğraf zemini canlı).
- **Masaüstü 1.0.48** yayınlandı (sunucuda "en güncel" doğrulandı) — açık makineler otomatik güncelleme uyarısı alır.

### Açıklanan (işlem yapılmadı):
- **Fly.io ölçekleme:** personal/kullanım-bazlı hesapta makine/RAM/disk **üçü de ücretli**; bedava maksimum yok;
  disk küçültülemez (geri alınamaz maliyet). Kullanıcı kuralı gereği **hiçbir değişiklik yapılmadı**.

> ⏳ **DEPLOY EDİLMEMİŞ WEB DEĞİŞİKLİĞİ VAR** (Adım 1 web + eski B1/B2/B4): kullanıcı kararı = *sonraki web
> işiyle birlikte* deploy edilecek. Sonraki Web deploy'unda otomatik gider — unutma. **API değişikliği de var**
> (AppModules/RequestService/Migration035) → API'yi de deploy et.

---

## B. Onay / Aksiyon Bekleyenler (senden)

- **Personel ekranını gözden geçir** (canlıda): artık **"Mevcut kullanıcıyı bağla"** (hesap açma yok; ADR-081) +
  **☐ Saha personeli** + **unvan listesi "+"**. Beğendin mi, değişiklik ister misin?
- **Masaüstü:** açık makineler **1.0.47** güncelleme uyarısı alır; güncelleyip yeni ekranları gör.
- **QA raporu (2026-07-12):** proje geneli tarama → [docs/tests/PROJECT_QA_Report.md](docs/tests/PROJECT_QA_Report.md).
  **4 küçük iyileştirmenin TAMAMI uygulandı** (B1 login boş-alan mesajı · B2 Audit/QuotaMonitor/Developer sayfa-içi
  yetki guard'ı · B3 Inspection + StockCount özel testleri, 8 yeni test · B4 build uyarıları CS8604/MUD0002 temizlendi).
  Test **281/281 yeşil**. ⏳ **DEPLOY EDİLMEMİŞ WEB DEĞİŞİKLİĞİ VAR** (B1/B2/B4-web): kullanıcı kararı = *bir sonraki
  web işiyle birlikte* deploy edilecek. Sonraki Web deploy'unda bu değişiklikler de otomatik gider — unutma.

---

## C. Bu Oturumda Tamamlananlar (2026-07-12)

### 2. prompt (ADR-076…082) — CANLIYA ALINDI (test 273/273; API+Web deploy, masaüstü 1.0.47)

> **Not:** Bu 7 ADR'nin **commit mesajları ADR-075…081** etiketli; DECISIONS.md'de doğru sıra **ADR-076…082**
> (075 numarası zaten "logo arka plan" kararına aitti — birer kaydırma).

- ✅ **ADR-076 — Silinen makine firması/şubesi girişe sunulmaz** (server `ReadDeviceInfo` join'lerine
  `is_deleted=0` + masaüstü: makine firması geçerli firma listesinde yoksa sayılmaz). 2 test.
- ✅ **ADR-077 — Makine yönetiminde FİRMA değiştirme** (web, süper admin): `AssignCompany` (şube ataması
  otomatik kalkar) + `POST /api/machines/{id}/company` + web sütunu. 1 test.
- ✅ **ADR-078 — Canlı sunucu ekranı: disk (canlı) + paket silme**: `ReleaseStore.GetDiskInfo/ListPackages/Delete`,
  `/api/server/status` disk alanları, `GET/DELETE /api/releases/packages`, web gauge + paket tablosu.
- ✅ **ADR-079 — Web logosu** masaüstünün temiz şeffaf logosuna (`app-icon.png`) eşitlendi, arka plan yok.
- ✅ **ADR-080 — İlk açılış tema varsayılanları**: Masaüstü Fluent/Koyu/Kehribar, Web Koyu/Yumuşak/Kehribar.
- ✅ **ADR-081 — Personel ekranı: hesap AÇMA yerine mevcut kullanıcıyı BAĞLAMA** (web + masaüstü):
  `ListLinkableUsers` + `POST /api/personnel/{id}/link-user`. 2 test.
- ✅ **ADR-082 — Firma yetki kontrol: süper admin DİNAMİK global kilidi açıp kapatabilir**
  (`SetGlobalLocks`/`IsGlobalRestricted`, global app_settings, enforcement + web toggle). 1 test.

### 1. prompt (2026-07-12, ADR-064…074) — canlıda

- ✅ **KRİTİK süper admin kilitlenme (ADR-064)** — firma silme süper admini pasife almaz + açılışta self-heal + regresyon testi. Canlı API redeploy edildi.
- ✅ **#6 NİHAİ: Fikir A — tek ekran + koşullar (ADR-067)** — web + masaüstü:
  - **Personel ekranında hesap açma** ("Uygulama erişimi ver" → kullanıcı adı/şifre/rol) + "Hesabı kaldır".
  - **☐ Saha personeli** kutucuğu; hesap yoksa/açılmıyorsa + kutucuk işaretsizse **uyarı penceresi** (işaretliyse çıkmaz).
  - **Unvan sabit tanım listesi + "+"** ile yeni tanım ekleme · mükerrer kişi uyarısı · bir personele tek hesap.
  - Kullanıcılar ekranındaki "Personel seç (bağla)" + PERSONEL sütunu ikinci yol olarak duruyor.
  - *(Kısa geçmiş: önce B (ayrı ekran) yapıldı, beğenilmedi → A'ya dönüldü, koşullar korundu.)*
- ✅ **Silinen şubeler her yerde listeleniyordu (ADR-066)** — kök neden: şubeler sunucu-otoriteli ama masaüstü
  yerel kopyası sunucudan yalnız **upsert** ediliyordu; silinenler yerelde kalıyordu. Artık her girişte sunucu
  şube listesi **aynalanır** (sunucuda olmayan yerel şube pasife alınır). Regresyon testi eklendi.
- ✅ **Firma silince 401 + firmalar yüklenmiyordu (ADR-068)** — süper admin, **içinde çalıştığı** firmayı silince
  token'daki firma geçersiz kalıyor, sonraki **her istek 401** dönüyordu (liste yüklenmiyor, ekranda silinmiş firma
  kalıyor, tekrar silme 401). Artık: firma **silinmişse** süper admin **home firmasına düşer** (oturum yaşar);
  firma **hiç yoksa** (sahte id) fail-closed korunur.
- ✅ **SİLMEDE WEB TAM OTORİTER (ADR-069)** — web'de silinen kayıt **makinelerin yerel DB'sinden de düşer**
  (silme artık LWW'yi aşar) **ve** sunucuda silinen kayıt **cihaz push'uyla diriltilemez**. Silme dışındaki
  LWW davranışı korundu. Unvan tanımları (`personnel_titles`) senkron listesine eklendi. 3 yeni test.
- ✅ **Masaüstü firma ekle/sil web ile eşitlenmiyordu (ADR-071 + ADR-072)** — kök neden: masaüstü Firma Tanım **yalnız yerel
  DB'ye** yazıyordu ve firmalar iş senkronunda hiç yoktu → sunucuya ulaşmıyordu. Artık **firmalar sunucu-otoriteli**
  ve **OFFLINE-FIRST kuyruk** (ADR-072): işlem önce **yerele** yazılır + **kuyruğa** alınır, internet gelince
  **sırayla** işlenir. Yeniden denemede **hata düşmez** (idempotent: aynı işlem tekrar gelirse mükerrer kayıt/hata yok).
  Eşitleme sırası: **1) firma → 2) sabit tanımlar/lookup → 3) iş kayıtları** (paralel değil, sırayla).
- ✅ **Kota İzleme "ONLINE" dedup (ADR-073)** — inceleme sonucu: sayım **zaten kullanıcı bazında tekildi**
  (ilk günden beri `userId` anahtarlı), aynı kişi iki platformdan girse **1** sayılıyordu; düzeltilecek hata yoktu.
  Yapılanlar: şart **4 testle sabitlendi** (regresyon) + gerçek bir kusur giderildi (eski kayıtlar sözlükten hiç
  silinmiyordu → bellek sızıntısı). **Not:** ekranda 2 gördüysen ya iki **farklı kullanıcı** online'dı ya da
  **"AKTİF"** sütunu (aktif kullanıcı sayısı) ile **"ONLINE"** karıştı — tekrarlarsa hangi kullanıcılarla olduğunu bildir.
- ✅ **Marka logoları eklendi (ADR-074)** — web + masaüstü. Tam logonun **opak beyaz zemini şeffaflaştırıldı**
  (flood-fill: kamyonun beyaz kabini/yol çizgileri korunarak), sembolden **7 boyutlu `.ico`** üretildi.
  **`.exe` simgesi hiç ayarlı değildi** (varsayılan .NET ikonu çıkıyordu) → düzeltildi. Favicon + giriş ekranları +
  üst bar/kenar çubuğu bağlandı. Kalite korundu (hiç büyütme yok, kayıpsız PNG).
- ⚠️ **KRİTİK OLAY — sunucu diski doldu (ADR-070):** `/data` (974 MB) %100 doldu → SQLite yazamadı →
  **login dahil tüm API 500** (tam kesinti). Sebep: her masaüstü paketi ~85 MB ve eski paketler hiç
  temizlenmiyordu (11 paket = 892 MB). Eski paketler silindi (disk %100 → %17) ve **otomatik saklama
  politikası** eklendi (en yeni 3 paket tutulur). Hafızaya kaydedildi.
- ✅ **CANLIYA ALINDI (12.07):** API + Web yayında (health 200). **Masaüstü 1.0.46 YAYINLANDI.**
  Yayın sırasında **süper admin canlı girişi doğrulandı** → ADR-064 tümüyle kapandı. Test **267/267**.

> Önceki oturumlarda tamamlananların tam listesi: `DEVAM.md` §2 ve `docs/DECISIONS.md` (ADR-062/063).
