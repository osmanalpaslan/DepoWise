# CANLI KULLANICI TEST CHECKLIST — 2026-08-12

- **Sürüm:** Masaüstü `1.0.137` · API Fly `v151` · Web Fly `v177` · commit `ca8dce5`
- **Firma:** Oze İnşaat · 5 şube · 2.511 malzeme
- **Yedek:** `D:\AlpnexYedek\depowise_prod_2026-08-12_110953.dump` (380.617 bayt, 2026-08-12 11:10)

> ⚠️ **BU TURDA HİÇBİR STOK HAREKETİ KAYDETMEYİN.** Aşağıdaki adımlar "Kaydet"e basmadan
> yapılabilecek doğrulamalardır. Yazma gerektiren adımlar **[YAZMA]** ile işaretlidir ve
> yalnız siz karar verdikten sonra yapılmalıdır.

---

## ⛔ ÖNCE BUNU BİLİN — şubelerde stok YOK

Canlıda ölçüldü (salt-okunur):

| Lokasyon | Kalem | Toplam |
|---|---|---|
| ANKARA GENEL MERKEZ | 0 | 0 |
| DÜZCE | 0 | 0 |
| KARAMAN | 0 | 0 |
| NEVŞEHİR | 0 | 0 |
| TEST ŞANTİYE | 0 | 0 |
| **ATANMAMIŞ** | **676** | **8.971,30** |

Yani **stoğun tamamı hâlâ "Atanmamış" kovasında.** Şube ekranlarında boş liste görmeniz
**hata değil, verinin gerçek durumudur.** Çok şubeli senaryolar (Ankara 10 / Düzce 5 / Nevşehir 2)
şu anda **test edilemez** — önce şubelere stok girmesi gerekir (bkz. bölüm D).

---

## A) MASAÜSTÜ — ana kanal (ayrıntılı)

### A1. Güncelleme mekanizması ⭐ en kritik
| # | Adım | Beklenen |
|---|---|---|
| 1 | Eski sürümü (1.0.136 veya öncesi) açın | Alt köşede eski sürüm no |
| 2 | Açılışta bekleyin | **"Güncelleme Mevcut — 1.0.137"** penceresi çıkmalı |
| 3 | "**Ertele**"ye basın | Pencere kapanmalı, uygulama normal çalışmalı |
| 4 | Menüden Sürümler/Güncelleme ekranını açın | Mevcut 1.0.136 · Sunucu 1.0.137 |
| 5 | "**İndir ve Kur**" | Yüzde (0→100) ilerlemeli, donmamalı |
| 6 | Kurulum bitince | Uygulama yeniden başlamalı |
| 7 | Alt köşe | **"Sürüm 1.0.137"** |
| 8 | Menüler/ekranlar | Hepsi eskisi gibi açılmalı (bozulma yok) |

> Checksum doğrulaması otomatiktir: paket bozuksa kurulum **hiç başlamaz** ve eski sürüm kalır.
> Bunu elle bozarak test etmeyin.

### A2. Giriş ve şube seçimi
| # | Adım | Beklenen |
|---|---|---|
| 9 | Giriş ekranı | Kullanıcı adı + şifre alanları, "Web'e git" bağlantısı |
| 10 | Oze İnşaat kullanıcınızla girin | Şube seçim listesi gelmeli |
| 11 | Şube listesi | **5 şube**: ANKARA GENEL MERKEZ · DÜZCE · KARAMAN · NEVŞEHİR · TEST ŞANTİYE |
| 12 | **DÜZCE** seçip girin | Üst barda seçili şube görünmeli |
| 13 | Yanlış şifre deneyin | Anlaşılır hata, uygulama çökmemeli |

### A3. Malzemeler
| # | Adım | Beklenen |
|---|---|---|
| 14 | Malzemeler ekranı | Liste açılmalı, **~2.511** kayıt |
| 15 | Kod ile arama (ör. bir kodun ilk 4 hanesi) | Sonuç anında gelmeli |
| 16 | Ad ile arama, Türkçe karakter (İ, Ş, Ğ, Ü, Ö, Ç) | Büyük/küçük harf farkı sonucu bozmamalı |
| 17 | **Stok** kolonu | Çoğu malzemede dolu; bu **firma geneli** toplamdır |
| 18 | Kolon sıralama (Kod, Ad, Stok) | Doğru sıralamalı |
| 19 | Kolon filtreleri | Her kolonda "içerir" araması |
| 20 | **Excel'e Aktar** | Filtrelenmiş TÜM sonucu indirmeli (sayfa değil) |
| 21 | Sayfalama | İleri/geri gezinme |

### A4. Malzeme kartı + lokasyon kırılımı ⭐
| # | Adım | Beklenen |
|---|---|---|
| 22 | Stoğu olan bir malzemeye çift tıklayın | Kart açılmalı |
| 23 | **Stok** alanı | Firma geneli toplam |
| 24 | **Depo kırılımı** paneli | Tek satır: **"Atanmamış"** + miktar (şubelerde stok yok) |
| 25 | Kırılım toplamı = karttaki stok | **Eşit olmalı** |
| 26 | Kırılımda "Atanmamış" en **altta** | Gerçek depolar önce gösterilir kuralı |
| 27 | Kartı kapatın (kaydetmeden) | Değişiklik olmamalı |

### A5. Atanmamış Stok Dağıtımı — ⚠️ SADECE GÖRÜNTÜLEME
| # | Adım | Beklenen |
|---|---|---|
| 28 | Stok İşlemleri → Atanmamış Stok Dağıtımı | Ekran açılmalı |
| 29 | Liste üstündeki bilgi yazısı | **"676 kayıt bulundu."** ← H-1 düzeltmesinin kanıtı |
| 30 | Liste satır sayısı | **676** (500'de kesilmemeli) |
| 31 | Listeyi en alta kaydırın | Son kayda kadar inebilmeli |
| 32 | **Negatif miktarlı** bir satır bulun (66 tane var) | "Dağıtılacak" alanı **kapalı/girilemez** olmalı |
| 33 | Pozitif bir satırda "Tümü" düğmesi | Miktar alanını doldurmalı (**kaydetmez**) |
| 34 | Hedef depo listesi | Yalnız 5 gerçek şube; **"Atanmamış" seçenek OLMAMALI** |
| 35 | Arama kutusuna alfabenin sonundan bir kod yazın | Bulmalı (arama tüm listede çalışır) |
| 36 | **⛔ "Dağıtımı Kaydet"e BASMAYIN** | — |
| 37 | Ekrandan çıkın | Hiçbir kayıt oluşmamalı |

> Beklenen sayılar: **676 kayıt · 610 dağıtılabilir (pozitif, toplam 9.535,12) · 66 negatif (−563,82)**

### A6. Stok Hareketleri
| # | Adım | Beklenen |
|---|---|---|
| 38 | Stok Hareketleri ekranı | Liste açılmalı (~680 hareket) |
| 39 | **Lokasyon** filtresi → "Atanmamış" | Açılış hareketleri gelmeli |
| 40 | Lokasyon → ANKARA GENEL MERKEZ | **Boş liste** (doğru — orada hareket yok) |
| 41 | **Hareket Türü** → "Açılış" | Yalnız açılış kayıtları |
| 42 | Tarih aralığı verin | Filtre uygulanmalı |
| 43 | Serbest metin arama (malzeme kodu) | Bulmalı |
| 44 | Excel'e aktar | Ekrandaki filtrelerle aynı sonucu vermeli |

### A7. Stok Sayımı — ⚠️ kaydetmeden
| # | Adım | Beklenen |
|---|---|---|
| 45 | Stok Sayımı ekranı | Sayım lokasyonu seçimi gelmeli |
| 46 | Lokasyon = **DÜZCE** seçin, bir malzeme seçin | **Sistem stoğu = 0** (firma toplamı DEĞİL) ⭐ |
| 47 | Lokasyon = Atanmamış seçin, aynı malzeme | Sistem stoğu = o malzemenin atanmamış miktarı |
| 48 | **⛔ Kaydetmeyin** | — |

> 46. adım kritik: eskiden burada firma geneli toplam gösteriliyordu ve kullanıcı yanlış fark görüyordu.

### A8. Giriş / Çıkış / Transfer — ⚠️ yalnız doğrulama kapıları
| # | Adım | Beklenen |
|---|---|---|
| 49 | Malzeme Giriş-Çıkış → "Depo Çıkışı" → "Şube Dışı" | Kaynak = login şubeniz (DÜZCE), hedef seçimi |
| 50 | Hedef seçmeden Kaydet | **"Hedef şube seçin."** uyarısı ⭐ D-1 kapısı |
| 51 | Hedef = DÜZCE (kaynakla aynı) seçin | **"Hedef şube kendi şubenizden farklı olmalı"** |
| 52 | Hedef = ANKARA seçin, malzeme seçin, miktar 1 | Kaydete basmadan önce **onay penceresi** metnini okuyun |
| 53 | **⛔ Onayı iptal edin** | Hiçbir kayıt oluşmamalı |
| 54 | "Depo Çıkışı" → "Şube İçi", bir malzeme, miktar 1 | Kaydederseniz **"Bu şubede yeterli stok yok"** demeli (DÜZCE=0) |
| 55 | **⛔ Kaydetmeyin** (isterseniz 54'ü deneyip hatayı görün — yazma olmaz) | Hata mesajı, kayıt yok |

### A9. Raporlar
| # | Adım | Beklenen |
|---|---|---|
| 56 | Raporlar → Stok Durumu, filtresiz "Sorgula" | Malzeme başına **tek satır**, toplam **8.971,30** |
| 57 | Depo = ANKARA seçip sorgula | **Boş sonuç** (doğru) |
| 58 | Depo = "Atanmamış" seçip sorgula | **676 satır**, toplam 8.971,30 |
| 59 | Tüm depolar + Atanmamış seçip sorgula | Toplam yine 8.971,30 (kopma yok) ⭐ |
| 60 | Stok Hareketleri raporu | Filtreler ekrandakiyle aynı sonucu vermeli |
| 61 | Excel dışa aktarım | Ekrandaki sonucun aynısı |
| 62 | "Sorgula"ya basmadan rapor | Çalışmamalı (ağır rapor kuralı) |

### A10. Bakım / Yakıt / Günlük Faaliyet — ⚠️ kaydetmeden
| # | Adım | Beklenen |
|---|---|---|
| 63 | Bakım → yeni kayıt, malzeme ekleyin | **"Stok Lokasyonu"** seçimi görünmeli ⭐ |
| 64 | Lokasyon seçmeden bırakın | Uyarı ya da "Atanmamış" varsayılanı (sessiz şube ataması OLMAMALI) |
| 65 | "Bakım Ekibi Stoğundan" kutusu | İşaretlenince depo stoğu etkilenmeyeceği belirtilmeli |
| 66 | **⛔ Kaydetmeyin** | — |
| 67 | Yakıt ekranı | Açılmalı, araç/depo seçimleri gelmeli |
| 68 | Günlük Faaliyet ekranı | Açılmalı, listeler dolmalı |

### A11. Genel dayanıklılık
| # | Adım | Beklenen |
|---|---|---|
| 69 | İnterneti kapatıp uygulamayı kullanın | Çalışmaya devam etmeli (çevrimdışı) |
| 70 | İnterneti açın | Senkron kendiliğinden çalışmalı |
| 71 | Tüm menüleri sırayla açıp kapatın | Hiçbirinde çökme/boş ekran olmamalı |
| 72 | Pencereyi küçültüp büyütün | Yerleşim bozulmamalı |

---

## B) WEB — ikincil kanal (kısa regresyon)

| # | Adım | Beklenen |
|---|---|---|
| W1 | https://depowise-web.fly.dev açın | Giriş sayfası |
| W2 | Oze İnşaat ile girin | Ana ekran |
| W3 | Malzemeler | Liste + arama + Excel |
| W4 | Bir malzemeye tıklayın | Lokasyon kırılımı paneli (Atanmamış) |
| W5 | **Stok → Atanmamış Stok Dağıtımı** | **"676 kayıt bulundu."** + 676 satır ⭐ |
| W6 | Negatif satır | Miktar alanı kapalı |
| W7 | **⛔ Dağıtmayın** | — |
| W8 | Stok Hareketleri + lokasyon filtresi | Masaüstüyle **aynı** sonuç |
| W9 | Raporlar → Stok Durumu (filtresiz / Atanmamış) | Masaüstüyle aynı toplam |
| W10 | Çıkış yap / tekrar gir | Sorunsuz |

---

## C) ŞU ANDA GERÇEK VERİYLE TEST EDİLEBİLENLER

✅ Güncelleme mekanizması (1.0.136 → 1.0.137)
✅ Giriş, şube seçimi, yetki kapıları
✅ 2.511 malzemeyle liste/arama/filtre/Excel performansı
✅ **Atanmamış kovasının 676 kaydının tamamının erişilebilirliği (H-1)**
✅ Malzeme kartı lokasyon kırılımı ve toplamla tutarlılığı
✅ Sayım ekranının **lokasyon bazlı** sistem stoğu göstermesi
✅ **Transfer hedef kapısı (D-1)** — hedefsiz transfer reddi
✅ Şube bazlı çıkış engeli ("bu şubede stok yok")
✅ Raporların firma geneli ↔ lokasyon bazlı ayrımı
✅ Web/masaüstü sonuç paritesi
✅ Çevrimdışı çalışma + senkron

## D) GERÇEK VERİ OLMADAN DOĞRULANAMAYANLAR

Bunların hepsi **şubelerde stok bulunmasını** gerektirir; şu anda tüm şubeler 0:

❌ Aynı malzemenin Ankara 10 / Düzce 5 / Nevşehir 2 olarak **aynı anda** durması
❌ Bir şubede stok varken diğerinde 0 olması
❌ Şube stoğunun tüketilip **tam 0**'a düşmesi
❌ Şubeler arası transferin kaynağı azaltıp hedefi artırması (firma toplamı sabit)
❌ Bir şubeden çıkışın **diğer şubeleri etkilememesi**
❌ Bakım/yakıt/günlük faaliyet tüketiminin **doğru lokasyon** stoğunu düşürmesi
❌ Depo bazlı sayım farkının yalnız o lokasyona yazılması
❌ Eşzamanlı iki işlemde stoğun negatife düşmemesi

> Bunların tamamı **izole testlerde 37 senaryoyla doğrulandı** (bkz. `CokSubeliStok_Test_Report.md`),
> ama canlı veride kanıtlanmadı.

### Şubelere stok koymanın iki yolu (ikisi de sizin kararınız)

| Yol | Ne yapar | Not |
|---|---|---|
| **1. STK-08 dağıtımı** | Atanmamış'taki 610 kalemi seçtiğiniz şubelere taşır | Plan Excel'i doldurulmalı · **geri alınamaz** |
| **2. Depo Girişi** | Birkaç test malzemesine şube stoğu girer | Küçük ve kontrollü; ters kayıtla iptal edilebilir |

> Yalnız senaryo testi için **2. yol** daha güvenlidir: 2-3 malzemeye birkaç şubede stok girip
> tüm çok şubeli senaryoları gerçek arayüzden yaşarsınız, sonra ters kayıtla temizlersiniz.
> **Bunu ancak siz "yap" derseniz yaparım.**

---

## E) HATA BULURSANIZ — bana ne gönderin

1. **Hangi ekran** ve **hangi adım numarası**
2. **Ne bekliyordunuz / ne oldu**
3. Ekran görüntüsü
4. Hata mesajının **tam metni**
5. Masaüstü ise: `%LOCALAPPDATA%\DepoWise\` altındaki `update.log` (güncelleme sorunuysa)

Ben önce hatanın **arayüz mü, ViewModel mi, servis mi, API mi, sorgu mu** olduğunu ayırırım;
**gerçek hata mı yoksa mevcut verinin/iş kuralının sonucu mu** olduğunu belirtirim.
**Onayınız olmadan production'da hiçbir düzeltme yapmam.**
