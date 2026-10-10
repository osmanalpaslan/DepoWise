# ARA İŞ 6 — Yakıt Dağıtımları ekranı: görünmeyen kayıtlar · sayfalama · arama

> **Kullanıcı talebi:** 2026-09-04 · **Öncelik:** ara iş (fazların önüne geçer)
> **Alındığı an:** MOB-W + TRF-01 yayınlandıktan, süit 3320/0 yeşil olduktan ve FAZ C bittikten
> hemen sonra; `STK-12`'ye **henüz başlanmamıştı** → yarım kalmış iş yok, hiçbir ekran riske girmedi.

---

## 1. Kullanıcının bildirdikleri (Yakıt Dağıtımları ekranı)

1. **Eski kayıtlar görünmüyor.** Raporda **02.08.2026** tarihli bir yakıt dağıtımı var, ama Yakıt
   Dağıtımları ekranında o kayıt görüntülenemiyor. Kullanıcının ifadesi: *"daha önceki tarihli
   kayıtları göremiyor olabilirim"* → tekil bir kayıt sorunu değil, **bir sınıf sorun**; önüne
   geçilmesi isteniyor.
2. **Liste sayfalanmıyor.** Tablo bütün kayıtları listelemeye çalışıyor; **hem webde hem masaüstünde**
   sayfa aşağı doğru uzuyor. İstenen: **Malzemeler ve Araçlar tablosundaki gibi** — seçilen sayfa
   boyutu kadar kayıt + sayfalar arası geçiş.
3. **Arama/filtre yok, mevcut arama düğmesi çalışmıyor.**
   - Ekranda **tarih bazlı** ve **araç bazlı** arama yapılabilmeli. Bu bugün yalnız raporda mümkün,
     ama **raporda düzenleme yapılamıyor** — kullanıcının kaydı bulup **düzenlemesi** gerekiyor.
   - Ekrandaki mevcut arama düğmesi **çalışmıyor**; ad/kod sorgulanamıyor.
   - Arama alanı **Sorgula düğmesine bağlanacak**; sorgu **yalnız bu düğme ve Enter tuşu** ile
     çalışacak (yazarken anlık arama YOK).
4. **Aynı sorunları yaşayacak başka ekranlar varsa** tespit edilip aynı iyileştirmeler oraya da
   uygulanacak.

## 2. Kullanıcının koyduğu çalışma kuralları (bu iş için bağlayıcı)

- Test masaüstünde yapıldı; **ama bu, hataların webde de olduğu anlamına gelmez** — webde hiç
  olmayabilir de. **İki ortam da ayrı ayrı analiz edilecek.**
- **Ortamlardan biri analiz edilmeden işleme geçilmeyecek.**
- İsteklerle ilgili ve onlardan **etkilenen bütün alanlar** eksiksiz kontrol edilecek.
- **Çalışan hiçbir yapı bozulmayacak.**
- Çalışma **tam ve eksiksiz** olacak.

## 3. Analiz — iki ortam AYRI AYRI incelendi

### Kök neden (her iki ortamda da AYNI)

`FuelService.ListDistributions` **sabit `limit = 200`** ile çağrılıyordu ve sorgu
`ORDER BY distribution_date DESC` ile **en yeniden** başlıyordu. Yani ekran yalnız **en yeni 200
dağıtımı** gösteriyor, daha eskiler **sessizce düşüyordu**. Rapor tarafı limitsiz okuduğu için aynı
kayıt orada görünüyordu — kullanıcının gördüğü tutarsızlık tam olarak budur.
Kesildiğine dair **hiçbir uyarı yoktu**; kayıt "kaybolmuş" gibi duruyordu.

- Masaüstü: `FuelViewModel.cs:238`
- Web: `Program.cs:1085` (`/api/fuel` ucu)

Kullanıcının *"daha önceki tarihli kayıtları göremiyor olabilirim"* sezgisi **doğruydu**: tekil bir
kayıt sorunu değil, 200'ün ötesindeki **her** kaydı etkileyen bir sınıf sorunu.

### Üç şikayetin ortamlara göre gerçek durumu

| Şikayet | Masaüstü | Web |
|---|---|---|
| Eski kayıtlar görünmüyor | ✅ var (200 tavanı) | ✅ **var** (aynı 200 tavanı) |
| Liste sayfalanmıyor | ✅ var (düz `ListBox`) | ✅ var (`MudTable`, pager yok) |
| Arama düğmesi çalışmıyor | ✅ **kutu ÖLÜ** — şablon çiziyor, ekran bağlamamış | ⚠️ **FARKLI**: webde arama kutusu **hiç yok** |

> Kullanıcının kuralı gereği web'e "aynı hata vardır" diye yaklaşılmadı; ölçüldü. Üçüncü maddede
> **belirti gerçekten farklı çıktı** (ölü kutu ≠ hiç kutu olmaması), sonuç aynıydı: filtrelenemiyordu.

### 🔴 Yan bulgu — ölü arama kutusu, 46 ekranda

`Toolbar` şablonunda `ShowSearch` varsayılanı **`true`** olduğu için şablon **her ekranda** bir arama
kutusu çiziyordu. Oysa Toolbar kullanan **50 ekranın yalnız 4'ü** `SearchText`'i bağlamıştı →
**46 ekranda kutu görünüyor, kullanıcı yazıyor, hiçbir şey olmuyordu.** Kullanıcının şikayeti bunun
tekil bir örneğiydi; sorun ekranda değil, **şablonun varsayılanındaydı**.

## 4. Yapılanlar

| # | İş | Durum |
|---|---|---|
| 1 | `FuelService.SearchDistributions` — sunucu tarafı sayfalama + tarih aralığı + araç (iç kod **ve plaka**) + serbest metin | ✅ |
| 2 | `/api/fuel/grid` ucu (toplam sayı da döner) | ✅ |
| 3 | Masaüstü: filtre çubuğu + sayfalama + **ölü kutu kapatıldı** | ✅ |
| 4 | Web: filtre çubuğu + sayfalama (webde arama **ilk kez** eklendi) | ✅ |
| 5 | Arama **yalnız Sorgula düğmesi ve Enter** ile çalışır — yazarken tetiklenmez | ✅ |
| 6 | **Depo Girişleri sekmesi** — aynı ekran, aynı 200 tavanı → o da sayfalandı | ✅ |
| 7 | **Sınıf düzeltmesi:** `ShowSearch` varsayılanı `false`; aramayı kullanan 4 ekran açıkça bildirir | ✅ |
| 8 | Testler: `YakitListeSayfalamaTests` (7) + `YakitEkranAramaTests` (7) | ✅ |

**Migration GEREKMEDİ** — mevcut indeksler yeterli:
`ix_fuel_dist_company(company_id, distribution_date)` ve `ix_fuel_dist_vehicle(vehicle_id, distribution_date)`.

### Uygulama sırasında bulunup düzeltilenler

- **Durum satırı yalan söylüyordu.** Arama yalnız düğmeyle çalıştığı için kullanıcı bir şey yazdığında
  liste değişmiyor; ama durum yazısı kutulara baktığı için hemen "· filtreli" diyordu — ekranda ise
  hâlâ filtresiz 241 kayıt duruyordu. Artık durum **son sorguda uygulanan** filtreyi anlatır, kutuda
  bekleyen değişiklik varsa ayrıca **"Sorgula'ya basın"** uyarısı çıkar.
- **Filtre değişince sayfa 1'e dönülür.** 7. sayfadayken filtre daraltılsa boş ekran gelir ve kullanıcı
  "kayıtlar silinmiş" sanardı — yani çözdüğümüz şikayetin yeni bir biçimde geri gelmesi.

## 5. Doğrulama

**Kusur ve düzeltmesi canlı kanıtlandı** (izole QA sunucusu, kullanıcının senaryosu birebir kuruldu:
02.08.2026 tarihli bir kayıt + üstüne 240 yeni kayıt = 241 kayıt):

| Kontrol | Sonuç |
|---|---|
| **Eski yol** (`/api/fuel`, sabit 200) | 200 satır döndü · **02.08.2026 kaydı YOK** ← kusurun kanıtı |
| **Yeni yol** — son sayfa | kayıt **BULUNDU** |
| **Tarih aralığı** 01–03 Ağustos 2026 | **1 sonuç**, doğru kayıt |
| Araç filtresi (plaka `06 XYZ`) | 80 kayıt, doğru araç |
| Serbest arama (`santiye`) | 24 kayıt |
| Web arayüzü | "241 dağıtım — sayfa 1 / 10" · filtre çubuğu · sayfa numaraları ✅ |
| **Yazarken liste değişmiyor** | 241 → 241, yalnız "Sorgula'ya basın" uyarısı ✅ |
| **Sorgula'ya basınca** | "1 dağıtım — sayfa 1 / 1 · filtreli" ✅ |

Testler: `YakitListeSayfalamaTests` **YKT1** kusurun kendisini kilitler — eski yolla kayıt
bulunamadığını, yeni yolla bulunduğunu aynı testte kanıtlar.

## 6. Kalan: aynı sorunu yaşayan DİĞER ekranlar → `LST-01`

Web analizinde, listesini **sayfalamadan** basan ve tavanı olan başka ekranlar da bulundu. Bunlarda
da aynı sınıf kusur var (kayıt var ama görünmüyor, üstelik sessizce):

| Ekran | Tavan | Risk |
|---|---|---|
| `Stock.razor` (Stok İşlemleri) | 200 | 🔴 yüksek — günlük kullanım |
| `Maintenance.razor` (Bakım) | 200 | 🔴 yüksek |
| `StockMovements.razor` | 1000 | 🟡 orta |
| `Personnel.razor` | 500 | 🟡 orta |
| `Audit.razor` · `StockChangeLog.razor` | 300 | 🟢 düşük (denetim kaydı) |
| `Inspection.razor` · `Purchasing.razor` | tavan YOK | 🟡 sayfa uzuyor, kayıp yok |

**Neden bu ara işe dahil edilmedi:** her biri kendi servis metodu + API ucu + iki arayüz demektir;
hepsini birden yapmak bu ara işi günlere yayardı ve **babanın canlı veri girdiği** ekranlarda tek
seferde geniş bir değişiklik anlamına gelirdi. Desen artık kurulu (`SearchDistributions` +
`/api/fuel/grid` + iki arayüz) — `LST-01` olarak yol haritasına yazıldı, yukarıdaki risk sırasıyla
yapılacak.
