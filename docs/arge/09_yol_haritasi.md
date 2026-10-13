# 09 — Yol haritası (B fazları) ve ürün sahibinin seçim listesi (A6)

> Tarih: 2026-10-13 · Dal: `arge/2026-10-kapsamli` · Dayanak: `05_fark_analizi.md` (F-001…F-171).
> **Bu çalışmada canlıya yayın YOK** (TALEP_2026-10-12). Aşağıdaki işler dalda geliştirilip test ortamında doğrulanır;
> yayın ayrı ve kullanıcı kararıyla yapılır. "Onay gerekir" yazan işler (senkron protokolü, yayın/güncelleme mekanizması,
> yeni migration, iş kuralı) proje kuralı gereği ayrıca senin onayını ister — bu listede seçmen o onay yerine geçer.

---

# SEÇİM LİSTESİ — ürün sahibinin seçeceği maddeler

**Nasıl kullanılır:** A bölümünde her konu için bir seçenek işaretle (önerimiz yazılı; işaretlemezsen önerimizle ilerleriz).
B bölümünde yapılmasını istediğin maddeleri seç. "güvenlik/veri — önerimiz zorunlu" yazanlar seçilmese de yapılmalıdır;
yapılmazsa veri kaybı veya güvenlik açığı riski kalır.
Kısaltmalar: **Öncelik** P0 kritik · P1 yüksek · P2 orta · P3 düşük · **İş büyüklüğü** S ≤1 gün · M 2–5 gün · L 1–2 hafta · XL 2+ hafta.

## A) Senin kararın gereken konular

| No | Konu (ne soruyoruz) | Seçenekler ve sonuçları | Önerimiz |
|---|---|---|---|
| K-01 | **Hakediş / taşeron** — Alpnex hakediş ve taşeron sözleşmesi yapsın mı? (F-140) | **a)** Kendimiz yapalım: çok büyük iş (aylar), uzman programlarla yarışırız. **b)** Yapmayalım; hakediş programı (Oska vb.) ve Logo/Mikro'ya veri aktaralım (Excel/XML): az iş, müşteri alıştığı programı kullanır. **c)** Şimdilik hiçbiri. | **b** — Alpnex'in gücü saha (depo, makine, yakıt, zimmet); rakipler orada zayıf. |
| K-02 | **Ön muhasebe ne kadar derin olsun?** Çek/senet, döviz kur farkı, e-Fatura dosyası içe alma, Logo/Mikro'ya aktarma (F-141) | **a)** Ön muhasebe kalsın, resmî defter Logo/Mikro'da; aktarım dosyası + e-Fatura XML içe alma ekleyelim. **b)** Çek/senet ve kur farkını da ekleyelim (büyük iş). **c)** Bugünkü hâli yeter. | **a** (çek/senet ancak müşteri isterse). |
| K-03 | **e-İrsaliye** (elektronik sevk irsaliyesi) gerekli mi? (F-142) | **a)** Önce mali müşavire sor: firma içi depo taşımasında zorunlu mu? **b)** Yalnız irsaliye no/tarih alanı + dışa aktarım (ucuz). **c)** Özel entegratör bağlantısı (pahalı, aylık ücret olabilir). | **a**, sonra **b**; **c** yalnız zorunluysa. |
| K-04 | **Onayda "son yazan kazanır" (LWW — last write wins)** — çevrimdışı çakışmada daha yeni "reddedildi", "onaylandı"yı eziyor; kuralımız bunu yasaklıyor (F-143) | **a)** Kuralı uygula: çakışmada ilk onay kazansın, diğeri çakışma listesine düşsün. **b)** Mevcut davranış kalsın, kurala "istisna" yazalım. | **a** — onay zaten çevrimiçi yapılıyor; değişiklik küçük, kural tutarlı olur. |
| K-05 | **Negatif stok istisnaları** — bakım malzemesi ve açılış stoğu eksiye düşebiliyor (F-144) | **a)** İkisini de kurala "bilinçli istisna" olarak yazalım (bugünkü çalışma bozulmaz). **b)** Bakımda da eksiyi yasakla (stok yoksa bakım kaydı girilemez). **c)** Yalnız açılış istisna kalsın. | **a** — sahada parça stoğa girmeden takılabiliyor; ama eksiye düşünce uyarı versin. |
| K-06 | **İki bilgisayar çevrimdışıyken aynı stoğu düşerse** ne olsun? (F-145, F-064) | **a)** Kabul et ama "eksi stok oluştu" uyarısı ve listesi üret (kimse kaydını kaybetmez). **b)** Sonradan gelen kaydı reddet (o kullanıcının kaydı geri döner). **c)** Bugünkü gibi sessiz kalsın. | **a**. |
| K-07 | **Yakıt / bakım / talep kayıtlarında çakışma** — iki yerde düzenlenen kayıtta son yazan kazanıyor (F-146) | **a)** Kuralı uygula: ezme yok, çakışma listesine düşsün, yönetici seçsin. **b)** Mevcut kalsın, istisna yaz. | **a**. |
| K-08 | **Web'de çift tıklama = çift kayıt** (yakıt/bakım) — bir test bunu "normal" sayıyor (F-147) | **a)** Düzelt: aynı işlem iki kez gelse de tek kayıt. **b)** Bırak. | **a** — güvenlik/veri, önerimiz zorunlu. |
| K-09 | **Fatura stoğa girsin mi?** "Stoğu etkilesin" varsayılan açık; aynı mal mal kabulle de girerse çift stok oluşur (F-148) | **a)** Kısa vade: siparişe bağlı malda varsayılan kapalı + çift giriş uyarısı; uzun vade: faturayı mal kabulden oluştur. **b)** Varsayılanı her zaman kapat. **c)** Bugünkü gibi kalsın. | **a**. |
| K-10 | **Düğme sırası** — formlarda "İptal … Kaydet" (Kaydet en sağda) (F-149) | **a)** Kaydet en sağda (bugünkü çoğunluk + web aynı). **b)** Windows kuralı: Kaydet solda. | **a** — kullanıcılar alışık; aykırı 3–4 ekran düzeltilir. |
| K-11 | **Ana düğme rengi** — sabit kehribar (turuncu-sarı) mı, kullanıcının seçtiği vurgu rengi mi? (F-150) | **a)** Sabit marka rengi kalsın. **b)** Kullanıcı rengini izlesin. | **a** (marka tutarlılığı); kullanıcı rengi yalnız menü/ikonlarda. |
| K-12 | **.NET 10'a geçiş zamanı** — .NET 8 güvenlik desteği **10 Kasım 2026**'da bitiyor (F-151) | **a)** Hemen başla: önce sunucu + web, sonra masaüstü; dalda hazır, yayın senin kararınla. **b)** Kasım sonrasına bırak (yamasız dönem). | **a**. |
| K-13 | **Kayıt onay penceresi** — fatura/tahsilat onay sormuyor, yakıt her kayıtta soruyor (F-152) | **a)** Para işlemlerinde her zaman sor; toplu girişte (yakıt tablosu) sonda tek onay. **b)** Her kayıtta sor. **c)** Hiçbirinde sorma. | **a**. |
| K-14 | **Şantiye personel puantajı** (kim kaç saat çalıştı) eklensin mi? Bordro hariç (F-153) | **a)** Şimdilik hayır. **b)** Makine-operatör saatiyle birlikte basit puantaj. | **a** (önce makine saatleri, F-086). |
| K-15 | **Giriş ekranında firma listesi herkese açık** (müşteri adları görünüyor) (F-154) | **a)** "Firma kodu" ile giriş, liste gizli. **b)** Bırak. | **a**. |
| K-16 | **GitHub deposu herkese açık** — iç belgeler ve müşteri adı görünüyor (F-155) | **a)** Özel yap (ücretsiz dakika yetiyorsa maliyet yok; önce ölçeriz). **b)** Açık kalsın, hassas belgeleri temizleyelim. | **a** — ödeme gerekirse önce sana sorarız. |
| K-17 | **Zorunlu güncelleme** — kullanıcı güncellemeyi sonsuza dek erteleyebiliyor (F-156) | **a)** En fazla 7 gün ertelensin, kritik güvenlik güncellemesi hemen zorunlu. **b)** Erteleme serbest kalsın. | **a**. |
| K-18 | **Hatırlatmalar nasıl gitsin?** (bakım, muayene, sigorta, kira bitişi — bilgisayar kapalıyken de) (F-157) | **a)** Uygulama içi bildirim (ücretsiz). **b)** + e-posta (SMTP — e-posta gönderim servisi, küçük ücret olabilir). | **a**, sonra istenirse **b**. |
| K-19 | **Kiralık makine bedeli** otomatik olarak kiralayan firmanın cari hesabına borç yazılsın mı? (F-158) | **a)** Hayır, yalnız maliyet raporu; borç fatura gelince. **b)** Evet, her dönem otomatik borç (fatura gelince eşleşir). | **a** (çift kayıt riski yok). |
| K-20 | **Güncelleme paketi imzalama + süper admin için iki adımlı giriş (MFA — çok adımlı doğrulama)** — yayın mekanizması değişikliği senin onayını ister (F-030) | **a)** Onaylıyorum: paketler imzalansın; telefonuna doğrulama uygulaması (ör. Google Authenticator) kurman gerekecek. **b)** Şimdi değil. | **a** — güvenlik/veri, önerimiz zorunlu. |
| K-21 | **Belge numarası biçimi** — iki bilgisayar internetsizken aynı numarayı (ör. fatura 42) veriyor, biri sessizce kayboluyor (F-190, F-172) | **a)** Numaraya bilgisayar kodu eklensin: `TLP-2026-A3-0042` (çakışmaz; numaralar tek sıra olmaz). **b)** İnternetsizken "geçici numara", sunucu kesin numarayı verir (çıktıda "geçici" yazar; çevrimdışı basılan belge sonra değişir). **c)** Bugünkü gibi (kayıp riski sürer). | **a** — basit, hiç kayıp yok; şube kodu da eklenebilir. |
| K-22 | **Aynı malzeme/araç kodu iki bilgisayarda açılırsa** ne olsun? (F-172) | **a)** İkinci kayıt "kod çakışması" listesine düşsün, kullanıcı yeni kod versin (veri kaybolmaz). **b)** Sistem otomatik ek koysun (`MLZ-100-2`). | **a** — mükerrer kart oluşmaz. |

## B) Yapılabilir maddeler

### Güvenlik & veri bütünlüğü
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-01 | Güncelleme paketlerini dijital imzala + süper admine MFA (F-030) | Şifre çalınsa bile kimse sahte güncelleme dağıtamaz | P0 | M | **güvenlik/veri — önerimiz zorunlu** |
| M-02 | Eşitlemede başka firmanın kaydını ezme açığını kapat (F-031) | Firmalar arası veri karışması imkânsız olur | P0 | S | **güvenlik/veri — önerimiz zorunlu** |
| M-03 | Eşitleme imleci: bilgisayar saatine bağlılığı kaldır + her turda tüm verinin yeniden inmesini düzelt (ikisi birlikte) (F-032, F-038, F-173) | Kayıt kaybolmaz; eşitleme trafiği yüzlerce kat azalır | P0 | M–L | **güvenlik/veri — önerimiz zorunlu** |
| M-78 | İnternetsiz iki bilgisayarın aynı numarayı vermesiyle kaybolan kayıtları önle (F-172, K-21, K-22) | Hiçbir fatura/belge sessizce kaybolmaz | P0 | M | **güvenlik/veri — önerimiz zorunlu** |
| M-04 | Web'de çift tıklamada çift yakıt/bakım kaydını engelle (F-033, F-147, F-044) | Maliyet ve stok iki kez düşmez | P0 | S | **güvenlik/veri — önerimiz zorunlu** |
| M-05 | Stok/cari/kasa hareketlerinin eşitlemeyle değiştirilmesini engelle (F-034) | Geçmiş hareketler iz bırakmadan değişemez | P0 | S–M | **güvenlik/veri — önerimiz zorunlu** |
| M-06 | Masaüstü işlem geçmişini (kim ne yaptı) sunucuya gönder (F-035) | Yönetici web'den her şeyi görür; bilgisayar bozulsa da kanıt kalır | P0 | M | **güvenlik/veri — önerimiz zorunlu** |
| M-07 | Veritabanı kütüphanesindeki bilinen güvenlik açığını kapat (F-036) | Bilinen açık kapanır | P0 | S | **güvenlik/veri — önerimiz zorunlu** |
| M-08 | Yedekleri şifrele, günlük ve otomatik al; geri dönüş provası (F-037, F-027) | En kötü durumda 1 günlük kayıp; yedek çalınsa okunamaz | P0 | S–M | **güvenlik/veri — önerimiz zorunlu** |
| M-09 | Şube yetkisi kaldırılan kullanıcının bilgisayarından o şubenin verisini temizle (F-040) | Yetkisi alınan kişi eski veriyi göremez | P1 | M | Önerilir |
| M-10 | Oturum iptali: şifre değişince eski oturumlar kapansın (F-066) | Çalınan oturum geçersiz olur | P1 | S–M | Önerilir |
| M-11 | Web girişinde bir kişinin hatası herkesi kilitlemesin (F-041, F-051) | Sabah toplu girişte kilitlenme olmaz | P1 | S | Önerilir |
| M-12 | Zorunlu güncelleme (min. sürüm) (F-042, K-17) | Çok eski sürümler yanlış veri yazamaz | P1 | M | Önerilir |
| M-13 | Küçük güvenlik düzeltmeleri: parola özeti, CORS, log maskeleme, firma listesi gizleme (F-107, F-110, F-050, F-049, F-154) | Saldırı yüzeyi daralır | P2 | M | Önerilir |
| M-14 | Excel içe aktarmayı geri alınabilir yap (F-063) | Yarım kalan aktarım tek tuşla temizlenir | P2 | M | Önerilir |

### Eşitleme & çevrimdışı
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-15 | Sunucunun reddettiği / gönderilemeyen kayıtları sakla, ekranda göster, düzeltince yeniden gönder (F-174, F-065) | "Kaydettim sanıyordum" kaybı biter | P0 | M | **güvenlik/veri — önerimiz zorunlu** |
| M-79 | "Yereli sıfırla" önce bekleyen kayıtları göndersin (F-175) | Sıfırlamada iş kaybolmaz | P0 | S | **güvenlik/veri — önerimiz zorunlu** |
| M-80 | Sunucu yedekten dönerse bilgisayarlar aradaki kayıtları yeniden göndersin (F-176) | Felakette masaüstündeki günler kaybolmaz | P0 | S–M | **güvenlik/veri — önerimiz zorunlu** |
| M-16 | Çevrimdışı eksi stok oluşunca uyarı ve liste (F-064, K-06) | Eksi stoğun kaynağı hemen bulunur | P1 | M | Önerilir |
| M-17 | İlk/tam eşitlemeyi parça parça, sıkıştırılmış ve kaldığı yerden sürdür (bugün 567 MB tek paket, sunucuda ~4,9 GB bellek ister) (F-039, F-180) | Sunucu çökmez; büyük firmada kurulum biter | P0 | M–L | **güvenlik/veri — önerimiz zorunlu** |
| M-18 | Yakıt/bakım/onay çakışmalarında ezme yerine çakışma listesi (F-143, F-146, K-04, K-07) | Düzeltilen kayıt sessizce eski hâline dönmez | P1 | M | Önerilir |
| M-19 | Yedekten geri dönerken gönderilmemiş kayıtları koru (F-043) | Geri yüklemede iş kaybı olmaz | P1 | M | Önerilir |
| M-20 | Yerel veritabanı bakımı + istatistik (ölçüldü: bazı sorgular 1,7 sn → anında) (F-068) | Hızlı kazanç, program yavaşlamaz | P1 | S | Önerilir |
| M-81 | Sonradan eşitlemeye eklenen tabloların eski kayıtlarını bir kez gönder (F-181) | Gözden kaçmış eski kayıtlar sunucuya ulaşır | P1 | S | Önerilir |
| M-82 | Aynı kaydın iki bilgisayarda ayrı girilmesine "olası mükerrer" uyarısı (F-183) | Çift fatura yakalanır | P2 | M | İsteğe bağlı |
| M-83 | Program/sunucu sürüm uyumu kontrolü + güncelleme öncesi otomatik yedek (F-184, F-185, F-182, F-186) | Sürüm farkında veri düşmez | P2 | S–M | Önerilir |

### Performans
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-21 | Eşitlemede tüm stok defteri yerine yalnız değişenleri hesapla (bugün 12,1 sn + yazma kilidi) (F-108) | Kayıt sırasında "veritabanı kilitli" hatası olmaz | P1 | M | Önerilir |
| M-84 | Masaüstü eşitlemede sıkıştırma (gzip) kullanılsın (567 → 102 MB) (F-180) | Hızlı kazanç: 5 kat az trafik | P1 | S | Önerilir |
| M-85 | Malzeme/günlük faaliyet ekranı açılışındaki karesel yavaşlığı düzelt (40 bin satırda 23–50 sn) (F-177) | Ekran açılışı anında | P1 | S | Önerilir |
| M-22 | Liste sayfaları ve aramayı hızlandır (bugün 300 bin kayıtta sayfa 7–10 sn, arama 5–6 sn) (F-178, F-111, F-025) | Her sayfa anında açılır | P1 | M | Önerilir |
| M-23 | Sunucunun her yoklamada tüm tabloları taramasını ve gereksiz uyandırmayı bitir (5,1 sn/yoklama) (F-179, F-112, F-049) | Sunucu ve internet yükü düşer | P1 | S–M | Önerilir |

### İş akışları (stok / satınalma / talep)
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-24 | Fatura–mal kabul çift stok uyarısı (F-045, K-09) | Aynı mal iki kez stoğa girmez | P1 | S | Önerilir |
| M-25 | Faturayı mal kabulden oluştur + sipariş-kabul-fatura eşleştirme (F-072, F-059) | Fatura yeniden yazılmaz, fazla ödeme yakalanır | P1 | L | Önerilir |
| M-26 | Talepten tek tıkla "stoktan gönder" ya da "sipariş aç"; talep formunda mevcut stok (F-071) | Talep kalemi üç kez yazılmaz; merkezdeki mal tekrar alınmaz | P1 | L | Önerilir |
| M-27 | İki adımlı transfer: gönder → teslim al, "yolda" görünümü (F-070, F-058) | Şantiyeler arası kayıp görünür | P1 | L | Önerilir |
| M-28 | Sayım iyileştirme: kör sayım, taslak, fark onayı, masaüstünde tablo (F-075) | Sayım hızlı ve dürüst olur | P2 | M | Önerilir |
| M-29 | Sipariş ve iş emri numarası otomatik (F-073) | Numara çakışması biter | P2 | S | Önerilir |
| M-30 | Ay kapanışı: geçmiş tarihe kayıt kilidi (F-076) | Kapanmış ayın raporu değişmez | P2 | M | Önerilir |
| M-31 | Depo bazında asgari stok + satın alma öneri listesi (F-114) | Kritik malzeme bitmeden haber alırsın | P2 | M | İsteğe bağlı |
| M-32 | Mal kabulde irsaliye no + düzeltme izi (F-113) | Parçalı teslim izlenir; e-İrsaliye'ye hazırlık | P2 | S–M | Önerilir |
| M-33 | Tek onay ekranı, kalemleri görerek onay, toplu onay, tutar eşiği (F-115, F-092) | Onaycı tek yerden hızlı karar verir | P2 | M | Önerilir |

### Filo / bakım / yakıt
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-34 | Toplu yakıt dağıtımı (tablo, sonda tek onay) (F-074, K-13) | 25 araçlık dağıtım dakikalar yerine saniyeler | P1 | M | Önerilir |
| M-35 | Araç kartından "Arıza bildir" → bakım/iş emri; uyarıdan iş başlatma (F-077) | Arıza unutulmaz, arıza süresi ölçülür | P1 | M | Önerilir |
| M-36 | Sayaç kuralları: hatalı okumayı "geçersiz say", eski sayaç uyarısı, sayaç geçmişi (F-078) | Bakım uyarıları doğru çıkar | P2 | M | Önerilir |
| M-37 | Bakım planı: tarih + km + saat birlikte; kapanışta sayaç zorunlu (F-079) | Hiçbir bakım kaçmaz | P2 | M | Önerilir |
| M-38 | Bakım durumu (arızalı → bekliyor → tamam) + duruş süresi raporu (F-080) | Makine ne kadar yattı görürsün | P2 | M | İsteğe bağlı |
| M-39 | Kiralık makine sözleşmesi: kiralayan cari, dönem maliyeti, bitiş uyarısı (F-084, K-19) | Kira maliyeti kendiliğinden izlenir | P2 | L | Önerilir |
| M-40 | Makine iç kira fiyatıyla şantiye maliyeti (F-085, F-086) | Her şantiyenin gerçek makine maliyeti | P2 | L | İsteğe bağlı (K-02'ye bağlı) |
| M-41 | Yasal periyodik kontrol (vinç, forklift) alanları ve uyarı (F-116) | Denetimde belge hazır | P2 | M | Önerilir |
| M-42 | Bilgisayar kapalıyken de bakım/muayene/sigorta hatırlatması (F-069, K-18) | Sigorta/muayene kaçmaz | P2 | M | Önerilir |
| M-43 | Günlük makine kontrol listesi (F-087) | İSG açısından kayıt | P3 | M | İsteğe bağlı |
| M-44 | Yakıtta şüpheli girişleri otomatik işaretleme (F-117) | Hatalı litre raporu bozmaz | P3 | M | İsteğe bağlı |
| M-45 | Ataşman/ikincil sayaç, ekipman rezervasyonu (F-089, F-088) | Büyük filoda planlama | P3 | L | Önerilmez (şimdilik) |

### Zimmet / ekipman
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-46 | Zimmette iade tarihi, gecikme uyarısı, personel kartında zimmetler (F-081) | Alet kaybı azalır; ayrılan personelin zimmeti görünür | P1 | M | Önerilir |
| M-47 | Kişi bazlı imzalı zimmet tutanağı (F-082) | "Almadım" itirazına kanıt | P2 | M | Önerilir |
| M-48 | Ekipman taşıma hareketi ve konum geçmişi (F-058) | Ekipman nerede, ne zaman taşındı belli | P2 | M | Önerilir (M-27 ile) |
| M-49 | Formda QR okutarak araç/alet seçme (F-139, F-026) | Sahada hızlı ve hatasız giriş | P3 | M | İsteğe bağlı |
| M-50 | Zimmet talebi ve kit (set) şablonu (F-083) | Toplu alet teslimi | P3 | M | İsteğe bağlı |

### Ön muhasebe
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-51 | Fatura/tahsilat kaydında onay (F-053, K-13) | Para işleminde yanlış kayıt azalır | P2 | S | Önerilir |
| M-52 | Logo/Mikro'ya aktarım dosyası + e-Fatura XML içe alma (F-141, K-02) | Muhasebeye çift giriş biter | P2 | L | Önerilir (K-02=a ise) |
| M-53 | Tedarikçiyi tek yerde tut (Tanımlar ↔ Cari birleşimi) (F-059) | Mükerrer tedarikçi olmaz | P2 | M | Önerilir |

### Raporlama & liste standartları
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-54 | Her listede arama, sıralama, Excel, Yazdır, tek tip sayfa (F-057) | Her ekranda aynı araçları bulursun | P2 | L | Önerilir |
| M-55 | Web ↔ masaüstü eksiklerini kapat (Yazdır, QR, zorunlu alan, geçmiş) (F-054, F-055, F-056) | İki ortam gerçekten aynı | P2 | M | Önerilir |
| M-56 | Kayıt ekranında Geçmiş · Belgeler · Hareketler sekmesi; satırdan kaynağa git (F-090) | "Bu stok neden 12?" tek ekranda | P2 | L | Önerilir |
| M-57 | Yeni raporlar: makine kullanım oranı, birim maliyet, ay sonu stok değeri, eksi stok (F-091) | Yönetici kararları kolaylaşır | P2 | M | İsteğe bağlı |
| M-58 | Rapor kayıtlı görünüm + PDF (F-122) | Sık raporu tek tıkla | P3 | M | İsteğe bağlı |

### Tasarım sistemi & ekranlar
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-59 | Renk/yazı düzeni: okunaklı yazı, açık tema kontrastı, tek font (F-125, F-061, K-11) | Göz yormayan, okunur ekran | P2 | S | Önerilir |
| M-60 | Ortak ekran parçaları: başlık, filtre çubuğu, seçim çubuğu, tek sayfalama, sağ panel (F-126) | Her ekran aynı düzende | P2 | M | Önerilir |
| M-61 | Düğme sırası/tek ana düğme kuralı (F-060, K-10) | Yanlış düğmeye basma azalır | P2 | S | Önerilir |
| M-62 | Klavye kısayolları, yükleniyor göstergesi, "kaydedildi" bildirimi, ekran okuyucu etiketleri (F-093, F-094, F-095, F-096) | Hızlı ve anlaşılır kullanım | P2 | M | Önerilir |
| M-63 | İşlem ekranlarını yenile: Giriş-Çıkış, Zimmet, Sayım, Yakıt (F-128) | Form küçük, liste büyük; hızlı giriş | P2 | L | Önerilir |
| M-64 | Liste ekranlarını yenile: Malzeme, Araç, Cari, Talep, Fatura, Rapor (F-129) | Modern ve tutarlı ekranlar | P2 | XL | Önerilir |
| M-65 | Tanım ekranları, ana ekran; "sık" görünüm seçeneği (F-130, F-127) | Küçük iyileştirmeler | P3 | M | İsteğe bağlı |

### Test & kalite
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-66 | Testlerin çökmeyi "geçti" saymasını ve atlanan testleri gizlemesini düzelt (F-046, F-047) | "Testler geçti" gerçekten güvenilir olur | P1 | S | Önerilir |
| M-67 | Tüm API uçlarına otomatik yetki taraması (F-018) | Yeni ekranda yetki unutulursa hemen yakalanır | P1 | M | Önerilir |
| M-68 | Çevrimdışı uçtan uca test: iki bilgisayar + sunucu (F-019, F-145) | Ürünün ana özelliği kanıtlanır | P1 | M | Önerilir |
| M-69 | Web'de kayıt akışlarını tarayıcıyla test (yerel ortamda) (F-022) | Web form hataları yayından önce yakalanır | P2 | L | Önerilir |
| M-70 | Masaüstü/web/rapor sonuçları aynı mı testi; gerçek boyutta migration provası; ekran testlerine eşik (F-021, F-020, F-023, F-024) | Sürprizsiz yayın | P2 | M–L | Önerilir |

### Teknik borç / altyapı
| No | Madde | Kazancın | Öncelik | İş | Öneri |
|---|---|---|---|---|---|
| M-71 | .NET 10'a geçiş (K-12) (F-151) | Güvenlik yamaları sürer | P1 | M–L | **Önerilir (son tarih 10.11.2026)** |
| M-72 | Sunucu sağlık kontrolü + disk dolma alarmı (F-067) | Kilitlenmede otomatik toparlanır | P1 | S | Önerilir |
| M-73 | Kullanılmayan paket/kod temizliği (F-100–F-105) | Bakım ve güvenlik yükü azalır | P3 | S–M | Önerilir |
| M-74 | Avalonia 12.1 yükseltmesi (F-131) | Güncel arayüz altyapısı | P2 | M | Önerilir (.NET 10'dan sonra) |
| M-75 | Büyük dosyaları bölme, tipli web-API bağlantısı (F-132–F-136) | Geliştirme güvenliği artar | P3 | L–XL | İsteğe bağlı |
| M-76 | Belgeleri güncelle (F-062) | Doğru bilgiyle geliştirme | P3 | S | Önerilir |
| M-77 | Hakediş, puantaj, telematik, bütçe (F-140, F-153, F-164, F-098) | — | P3 | XL | Önerilmez (şimdilik; K-01/K-14 kararına bağlı) |

---

# Yol haritası (B fazları)

**Sıralama ilkesi:** önce test güvencesi → P0 güvenlik/veri → .NET 10 (son tarih) → senkron sağlamlığı → iş akışı boşlukları →
tasarım sistemi F0 + ekranlar → isteğe bağlı. Her faz ayrı commit grubu; her alt faz sonunda ilgili testler, büyük işte
tam takım + arayüz testleri + iki ortam denetçisi. **Hiçbir fazda yayın yok**; geri dönüş noktası = alt faz başlangıç commit'i
(dal üzerinde `git revert`), migration'lar yalnız eklemeli olduğu için şema geri alma gerektirmez.

| Faz | Kapsam (F-id) | Bağımlılık | Teslimat | Eklenecek / koşulacak testler | Kabul kriteri | Geri dönüş | Efor | Riskler |
|---|---|---|---|---|---|---|---|---|
| **B0 — Kararlar** | K-01…K-20; F-143–F-158 | Ürün sahibi seçimi | ADR'ler; §3'e istisna/kural metni; FIN10, UUA6, bakım-negatif testlerinin yeni sözleşmesi | — | Her K için seçim kayıtlı | — | S | Karar gecikirse B1.4/B3 bekler |
| **B1 — P0 güvenlik, veri ve senkron bütünlüğü** | | | | | | | **~5 hafta** | |
| B1.0 Test güvencesi | F-046, F-047, F-018 (başlangıç) | — | `IsDenied` 500'süz; atlanan test görünür; uç kataloğu taraması iskeleti | Ölçüm koşusu: 500 alan testler listelenir | Tam takım yeşil, `NotExecuted`=0 | commit | S–M | Gizli çökmeler ortaya çıkar (iyi ama iş çıkarır) |
| B1.1 Paket açığı | F-036 | — | SQLitePCLRaw yamalı sürüm sabitlendi; kontrol listesine vuln taraması | Tam takım + masaüstü açılış | `--vulnerable` temiz | csproj revert | S | Yerel SQLite davranış farkı |
| B1.2 Yedek | F-037, F-027 | GitHub Actions sırları | Günlük şifreli yedek iş akışı; Supabase PITR/Fly anlık görüntü doğrulama notu | Geri yükleme provası şifreli dosyadan | RPO ≤1 gün belgeli | iş akışını kapat | S–M | Anahtar kaybı → anahtar ayrı saklanır |
| B1.3 Senkron tenant kapısı | F-031 | B1.0 | Upsert'te firma eşitliği + red sayımı | Önce kırmızı kanıt testi, sonra yeşil; PG + SQLite | Başka firma id'siyle push reddedilir | commit | S | Eski hatalı satırlar varsa red artar (log izlenir) |
| B1.4 Web idempotency | F-033, F-147, F-044 | K-08 | Web formları op id üretir; sunucu "yoksa üret"; maliyet merkezi aynı transaction | Aynı op id iki POST → tek kayıt (yakıt, depo, bakım, ekipman bakımı, günlük, açılış); UUA6 yeni sözleşme | Çift kayıt yok; eski istemci çalışır | commit | S–M | Web DTO uyumu |
| B1.5 **Senkron bütünlüğü (A3b)** | F-032 + F-038 + F-173 (tek iş birimi), F-172, F-174/F-065, F-175, F-176, F-181, F-039 | Onay (senkron + eklemeli migration); K-21, K-22 | Sunucu atamalı sıra (`server_seq`) + örtüşme + imleç ataması **birlikte**; makine önekli numara + kod çakışma kaydı; red listesi + ekran + yeniden gönderim; sıfırlamadan önce push; sunucu veri dönemi (epoch); sayfalı + gzip'li tam eşitleme | İki çevrimdışı cihaz: aynı numara, geri damgalı satır, saat geri/ileri; poison sonrası yeniden gönderim; geri yükleme provası; 300k satır kurulum + kesinti sonrası devam; API bellek tepesi | Hiçbir senaryoda satır sessizce kaybolmaz; boş turda çekilen satır 0; tam paket bellek <400 MB | Alt adım commit'leri; yeni kolonlar eklemeli, kullanılmazsa zararsız | L (~2 hafta) | **F-173'ü tek başına düzeltmek veri kaybı başlatır**; eski istemci uyumu; tüm makinelerde bir kez tam gönderim gerekebilir |
| B1.6 Defter ve audit | F-034, F-035 | Onay (senkron) | Defter tablolarında kolon izin listesi; `audit_logs` yalnız-ekle push | Miktar değiştiren push reddi; masaüstü audit web'de görünür | İz bırakmadan değişiklik imkânsız | commit | M | Audit hacmi → A3b trafik ölçümü |
| B1.7 Güncelleme imzası + MFA | F-030 | K-20 onayı | Ed25519 imza + masaüstü doğrulama; süper admin TOTP; yayın ucuna ek sır | İmzasız/yanlış imza reddi; eski sürümden geçiş provası (test ortamı) | Sahte paket kurulamaz | Doğrulama bayrağı ile geçiş paketi | M | Anahtar yönetimi; geçiş sürümü sırası kritik |
| B1.8 Ölçülmüş hızlı kazançlar | F-068, F-180, F-177 | — | `ANALYZE`/optimize; gzip; SearchGridAll tek geçiş | A3b §6 ölçüm betiği önce/sonra | 1,7 sn → ~0; 567 → ~102 MB; filtre seçenekleri <1 sn | commit | S (~2 gün) | Düşük |
| **B2 — .NET 10 geçişi** | F-151 (+ Npgsql/JwtBearer/SignalR 10) | K-12; B1.1 | Dalda net10: önce Api + Web (Docker imajları), sonra Desktop + Setup | Tam takım (PG dahil), arayüz testleri, kurulum + otomatik güncelleme provası (8→10 istemci), Playwright yerel | Tüm testler yeşil; self-contained paket kurulur | Dal; TFM revert | M–L (~1,5–2 hafta) | Avalonia/QuestPDF/SkiaSharp uyumu; son tarih 2026-11-10 — yayın kararı kullanıcıda |
| **B3 — Senkron sağlamlığı, performans ve operasyon** | F-064, F-040, F-043, F-108, F-178, F-179, F-111, F-112, F-182–F-186, F-189, F-143/F-146 (karara göre), F-145, F-042, F-066, F-041, F-051, F-067, F-052, F-109 | B1, B0 | Eksi stok uyarısı; kapsam temizliği; artımlı bakiye; liste/arama hızlandırma (ölçerek indeks); firma sürüm sayacı; çakışma kaydı yakıt/bakım/fatura/kasa; zorunlu güncelleme + şema el sıkışması; jeton iptali; sağlık kontrolü | İki cihaz oversell; yakıt/bakım/onay çakışma testleri; çevrimdışı uçtan uca (F-019); A3b §6 ölçüm seti SQLite + PG | Hiçbir çakışma sessiz ezilmez; 300k'da ilk sayfa <150 ms, son sayfa <300 ms, CompanyVersion <5 ms | Alt faz commit'leri | L–XL (~4 hafta) | Protokol değişiklikleri onay ister; eski istemci uyumu |
| **B4 — Test ve kalite (B1–B3'e paralel)** | F-018, F-019, F-020, F-021, F-022, F-023, F-024, F-028, F-138 | B1.0 | Uç kataloğu taraması tam; uçtan uca çevrimdışı; sonuç paritesi; yerel web yazma e2e; migration provası; UI eşikleri | Kendileri | Kritik §3 kuralları negatif senaryoyla kilitli (A4 matrisi TAM) | — | L | Test süresi uzar → "kapsamlı" kategorisi |
| **B5 — İş akışları: stok / satınalma / talep** | F-045, F-072, F-059, F-071, F-070, F-058, F-075, F-073, F-076, F-114, F-113, F-115, F-092, F-053, F-063 | B1.4, K-09, K-13 | Çift stok uyarısı → mal kabulden fatura; talep dallanması; iki adımlı transfer + yolda; sayım; otomatik numara; dönem kilidi; onay merkezi | Her akış: stok defteri, negatif stok, idempotency, iptal, çevrimdışı, eski şema testleri; iki ortam | Senaryo 1, 4, 5, 7 (A2b) adım sayısı azalır, çift giriş yok | Özellik başına commit; yeni tablolar eklemeli | XL (~6 hafta) | Stok defteri — kapsamlı test şart; migration onayı |
| **B6 — Filo, bakım, yakıt, zimmet** | F-074, F-077, F-078, F-079, F-080, F-081, F-082, F-084, F-116, F-069, F-085, F-086 (K-02'ye göre), F-139 | B5 (F-070), K-18, K-19 | Toplu yakıt; arıza→iş emri; sayaç kuralları; bakım planı; zimmet iade/tutanak; kiralık sözleşme; periyodik kontrol; sunucu hatırlatmaları | Sayaç geri gitme, yakıt kuralları, zimmet eşzamanlılık, hatırlatma tekilliği | Senaryo 2, 3, 6 sadeleşir | Özellik başına | XL (~6 hafta) | Sayaç/yakıt iş kuralları — yalnız ekleme |
| **B7 — Raporlama, liste standardı, kayıt merkezli görünüm** | F-057, F-054, F-055, F-056, F-090, F-091, F-122, F-026 | B8 F0 bileşenleri (liste standardı için) | Her tabloda aynı araçlar; parite kapanır; kayıt sekmeleri; yeni raporlar | Ekran × özellik matrisi testi; rapor = ekran = Excel paritesi | İki ortam denetçisi temiz | Ekran başına | L–XL | "Tüm tablolara" kuralı geniş — matris testi ile izlenir |
| **B8 — Tasarım sistemi F0 + ekranlar** | F0: F-125, F-126, F-060, F-061, F-093–F-096, F-127; ekranlar: F-128, F-129, F-130 | K-10, K-11; B2 (Avalonia 12.1 isteniyorsa F-131) | Token/kontrast; ortak bileşenler; kısayol; toast; yükleniyor; erişilebilirlik; Şablon A–D ekran dönüşümleri (web eşleniğiyle) | Görsel tarama (iki tema), kontrast testi, ikon-Name statik testi, ilgili E2E | A5 §3.5 erişilebilirlik minimumları; ekran başına görsel onay | Ekran başına commit | F0 ~2–3 hafta + ekranlar ~6–8 hafta + web | En çok kullanılan ekranlar (Malzeme, Giriş-Çıkış) — iş kuralı değişmez |
| **B9 — Teknik borç ve isteğe bağlı** | F-100–F-106, F-131, F-132–F-137, F-048, F-049, F-050, F-062, F-107, F-110, F-111, F-112, F-117–F-121, F-083, F-087–F-089, F-097 | B1–B8 | Temizlik, Avalonia 12.1, API bölme, tipli istemci, belgeler | Tam takım; uç sayısı önce/sonra aynı | Davranış değişmez | commit | XL | Geniş refactor — küçük adımlarla |
| **B10 — Stratejik (karara bağlı)** | F-140, F-141, F-142, F-153, F-098, F-164 | K-01, K-02, K-03, K-14 | Hakediş/muhasebe aktarımı, e-İrsaliye alanı/entegratör, puantaj | Aktarım dosyası doğrulama | Seçilen kapsam | — | M–XL | Dış sistem/maliyet; mali müşavir teyidi |
| **Z — Nihai doğrulama** | Tümü | B1–B8 | `11_kalan_riskler.md`, `12_yayin_oncesi_kontrol.md` | Tam takım + arayüz + yerel e2e + iki ortam denetçisi | Kontrol listesi eksiksiz; yayın kararı kullanıcıda | — | M | — |

## A3b bulguları (entegre edildi — 2026-10-13)

> `07_veri_senkron_performans.md` işlendi (`05_fark_analizi.md` §9, F-172…F-190). Değişenler:
> - **Senkron bütünlüğü P0 fazına (B1.5) taşındı:** numara çakışması (F-172), imleç + tam paket (F-032/F-038/F-173 birlikte),
>   reddedilen satırlar (F-174/F-065), "Yereli sıfırla" (F-175), sunucu geri yükleme sonrası gönderim (F-176), 567 MB tam paket (F-039).
> - **Ölçülmüş hızlı kazançlar B1.8'e alındı** (F-068 ANALYZE, F-180 gzip, F-177 SearchGridAll).
> - **Performans P1 maddeleri B3'te** (F-108 12,1 sn bakiye + kilit, F-178 sayfa başına sayım 7–10 sn, F-179 CompanyVersion 5,1 sn, F-111 arama).
> - Doğrulanan riskler: çevrimdışı çift çıkış → sessiz negatif bakiye (F-064), yetki daralınca yerel veri (F-040), ilk eşitleme devam edemiyor (F-039).
> - Seçim listesine eklenen kararlar: K-21 (belge no biçimi), K-22 (kod çakışması); maddeler: M-78…M-85.
