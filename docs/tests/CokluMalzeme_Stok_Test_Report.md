# Test Raporu — Çok Malzemeli Stok İşlemi (İş #8)

Tarih: **2026-08-09** · Kapsam: **yalnız Giriş-Çıkış (Stok İşlemleri) ekranı** — CLAUDE.md §7.1
Migration: **YOK** · Production yazma/deploy: **YOK**

---

## 1. Başlangıç durumu (koddan doğrulandı)

İş #8 iki maddeden oluşuyordu:

| Madde | Durum |
|---|---|
| **P1-7** `BranchService` sürüm kontrolü | ✅ **İş #6'da yapıldı** — burada tekrar edilmedi |
| **P1-6** Giriş-Çıkış'ta çoklu malzeme | bu işin konusu |

Çoklu malzeme için mevcut durum:

- `StockService.ReceiveIn` / `IssueOut` **zaten** `IReadOnlyList<StockLine>` alıyordu (tek belge, N hareket).
- `StockService.Transfer` **tek malzemeydi**.
- API'nin üç ucu da (`/receive`, `/issue`, `/transfer`) **tek malzeme** alıyordu.
- Masaüstü ve web ekranı tek malzeme + tek miktar ile çalışıyordu.

Sonuç: 10 malzeme veren depocu **10 ayrı belge** açmak zorundaydı.

## 2. Yapılan

| Katman | Değişiklik |
|---|---|
| Servis | `Transfer` için **çok satırlı aşırı yükleme**; eski tek malzemeli imza korundu (ona yönlendirir) |
| API | `StockMoveDto` / `StockTransferDto` içine `Lines` eklendi; `StockLines(...)` ortak doğrulama |
| Masaüstü | `StockEntryView(Model)`: "+ Listeye Ekle" sepeti, satır listesi, "Çıkar" |
| Web | `Stock.razor`: aynı desen (Listeye Ekle + tablo + Çıkar) |
| Test | `MultiMaterialStockTests` (8) + `ApiMultiMaterialTests` (6) |

**Kapsam dışı bırakılan (bilinçli):** "Yeni Kayıt" (giriş) yolu — orada form bir **malzeme kartı**
oluşturur (kod, ad, birim, kategori…). Oraya sepet koymak ekranı yeniden tasarlamak olurdu; bu iş
"Giriş-Çıkış ekranında çoklu malzeme" idi, ekran yeniden tasarımı değil. Mevcut malzemeye giriş
yapılırken de tek malzeme akışı korundu.

## 3. Korunan davranışlar (geriye uyumluluk)

- **Eski API gövdesi** (`materialId` + `quantity`, `lines` yok) aynen çalışır → güncellenmemiş
  masaüstü paketleri bozulmaz. Testle doğrulandı.
- **Tek malzemeli transferde idempotency anahtarı DEĞİŞMEDİ** (`op:out` / `op:in`). Çok malzemede
  satır numarası eklenir (`op:0:out`). Böylece sürüm geçişinde bekleyen bir tekrar denemesi
  kopya hareket üretemez.
- Sepet boş bırakılıp tek malzeme + miktar yazılırsa ekran eskisi gibi davranır.

## 4. Veri bütünlüğü — asıl iddia

**Tek belge, tek transaction.** Bir satır bile başarısız olursa (ör. negatif stok) belgenin
**tamamı** geri alınır. Testler bunu doğrudan ölçüyor:

- `Cikista_BIR_satir_bile_yetersizse_TAMAMI_geri_alinir` → ilk malzemeden **de** düşülmemiş olmalı
- `Transferde_BIR_satir_bile_yetersizse_TAMAMI_geri_alinir` → hedef şubeye **hiçbir şey** geçmemeli
- `Cok_malzemeli_cikis_TEK_islemde_iptal_edilir` → iptal (ters kayıt) tüm satırları geri alır

Ayrıca **aynı malzeme iki kez eklenirse miktarlar toplanır** ve tek hareket yazılır (hem ekranda hem
API'de) — aksi halde bakiye doğru olur ama hareket defteri kullanıcıya kafa karıştırıcı görünürdü.

## 5. Test sonuçları

| Paket | Sonuç |
|---|---|
| `MultiMaterialStockTests` (servis) | **8 / 8** |
| `ApiMultiMaterialTests` (gerçek HTTP) | **6 / 6** |
| SQLite tam paket | **943 geçti / 0 başarısız / 31 atlandı** |
| `dotnet build DepoWise.sln` | **0 hata** |

## 6. Coverage Matrix (§7.13)

| Alan | Durum |
|---|---|
| Form Açıldı · Yeni Kayıt · Doğrulamalar · Hata Mesajları | ✅ |
| Database (tek belge · transaction · rollback · iptal · idempotency) | ✅ |
| UI / UX (sepet, özet metni, "çok büyük miktar" uyarısı sepeti de kapsıyor) | ✅ (masaüstü + web) |
| Yetki · Security | değişmedi (aynı `AccessControl` + şube kuralları) → kapsam dışı |
| Grid · Offline · Sync · Performans | değişmedi → kapsam dışı (§7.1) |

## 7. Bu sırada düzeltilen ikincil bulgu

"Miktar çok büyük görünüyor" uyarısı (madde 7) yalnız **formdaki** miktara bakıyordu. Sepet
kullanıldığında formdaki miktar 0 olduğu için uyarı **hiç tetiklenmezdi** → sepete konan hatalı
büyük miktar sessizce geçerdi. Artık sepetteki en büyük miktar da denetleniyor (iki platformda da).

## 8. Risk ve açık uçlar

- **Yayın gerekir:** web bu uçları uzak API'den çağırır → önce API, sonra web deploy.
- **Yeni Kayıt (giriş) hâlâ tek malzeme** — yukarıda gerekçesi yazılı. Kullanıcı isterse ayrı iş.
- Sepet **ekran** düzeyinde tutulur; sayfa yenilenirse kaybolur (taslak kaydı yok). Kayıt tek
  oturumda tamamlandığı için bilinçli olarak taslak altyapısı eklenmedi.
