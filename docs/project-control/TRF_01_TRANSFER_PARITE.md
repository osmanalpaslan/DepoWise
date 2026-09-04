# TRF-01 — Depo→depo transfer: UI paritesi + bakiyeye yansıma doğrulaması

> **FAZ C'nin kalan TEK işi.** Bu bittiğinde FAZ C (depo bazlı stok) tamamlanır.
> Analiz: 2026-09-04 · Durum: 🔵 ANALİZ TAMAM, uygulama sırada

---

## 1. Çıkış noktası — kod zaten var

Yol haritasının kendi ifadesi: *"Transfer kodu zaten var — UI paritesi + bakiyeye yansıma
doğrulaması"*. Analiz bunu doğruladı: **servis katmanı olgun**, eksik olan arayüz tarafı.

**Servis (`StockService.Transfer`) — sağlam:**
- Çok malzemeli, **tek transaction** (`BeginImmediate`) — bir satır patlarsa tamamı geri alınır
- **Idempotent**: aynı `operationId` ile ikinci çağrı yeni hareket üretmez, mevcut belgeyi döner
- **Çift katmanlı negatif stok koruması**: defter bakiyesi kontrolü + `ApplyDelta(allowNegative:false)`
- Kaynak boş olamaz · hedef boş olamaz ("Atanmamış"a transfer yasak) · kaynak = hedef yasak
- Şubeli kullanıcı yalnız kendi şubesinden çıkış yapabilir (`EnforceOwnBranch`)

**Bakiyeye yansıma — doğru çalışıyor:** kaynak için `-1`, hedef için `+1` iki satır, ikisi de
**ortak yazıcı** `StockBalanceWriter.ApplyDelta` üzerinden. Firma toplamı değişmez, lokasyon
kırılımı doğru güncellenir. Transfer bilinçli olarak **geri alınamaz** (`CanReverse` dışlıyor).

## 2. Bulunan gerçek kusur — 🔴 MALİYET MERKEZİ SESSİZCE KAYBOLUYOR

**Doğrulanmış:** "Maliyet Merkezi" alanı her iki platformda da **işlem türünden bağımsız** görünür —
görünürlüğü yalnız yetkiye bağlıdır (`cost_centers` / Edit). Yani kullanıcı **transfer yaparken de
bu alanı doldurabiliyor**, ama:

- Web `Stock.razor`: `costCenterId` **yalnız çıkış** gövdesinde gönderiliyor, transfer gövdesinde yok
- Masaüstü `StockEntryViewModel`: `BaglaMaliyetMerkezi` **yalnız** IssueOut dalında çağrılıyor
- API `StockTransferDto`: alanın kendisi **hiç yok**

Sonuç: kullanıcı bir alan dolduruyor, kaydettiğinde hiçbir yere yazılmıyor ve **uyarı da almıyor**.
Bu bir parite farkı değil, **iki platformda birden var olan ortak kusurdur**.

### Karar: alan transferde GİZLENİR (kaydedilmez)

Gerekçe — depo→depo transfer bir **maliyet olayı değildir**: malzeme tüketilmez, yalnız yer değiştirir.
Maliyet, malzeme şantiyede *kullanıldığında* (çıkış) doğar; zaten `costCenterId` orada çalışıyor.
Transferi bir maliyet merkezine yazmak muhasebe açısından yanıltıcı olurdu ve şantiye maliyet
dağıtımının kuralları henüz kararlaştırılmadı (yol haritası: **`MUH-04` gider dağıtımı**).

Alanı "çalışır hâle getirmek" yerine gizlemenin nedeni: bugün alan **hiçbir şey yapmıyor**, dolayısıyla
gizlemek hiçbir işlevi kaldırmaz — ama kullanıcının "kaydedildi" sanmasını önler. Sessizce yutulan
bir giriş, hiç olmayan bir alandan daha kötüdür. İleride transferlerin maliyetlendirilmesi istenirse
doğru yer `MUH-04`'tür.

## 3. Paritede gerçek farklar

| # | Fark | Web | Masaüstü | Karar |
|---|---|---|---|---|
| 1 | **"Tüm Şubeler" modunda işlem** | STK-04 ile **açık** — depo açıkça seçilirse işlem yapılabilir (`Stock.razor:22,296,491`) | `BranchGuard.RequireBranchAsync` **tüm kaydı engelliyor** (`StockEntryViewModel.cs:370`) | **Masaüstü web'e hizalanacak** — en büyük fark |
| 2 | Kaynak depo alanı | Yöneticide seçilebilir liste, şubelide salt-okunur | **Her koşulda** salt-okunur | #1 ile birlikte çözülür |
| 3 | Hedef listesi | Kaynağı listeden **dışlıyor** (`Stock.razor:299`) | Tüm şubeler listeleniyor; aynı şube seçilirse hata **ancak Kaydet'te** çıkıyor | Masaüstü web'e hizalanacak |
| 4 | Onay metni | "hedef şubeye" — **hangi depo olduğu yazmıyor** | Hedef adını yazıyor (`:468`) | **Web masaüstüne hizalanacak** (bu maddede masaüstü daha iyi) |
| 5 | Maliyet merkezi | Transferde gönderilmiyor | Transferde bağlanmıyor | Bkz. §2 — ikisinde de gizlenecek |

**Kusur OLMAYAN bulgu:** `AppScreens`'te `stock.distribute` `Platforms = D` görünüyor ama web'de
sayfa açılıyor. Bu **bilinçli**: kod yorumunda *"web'de Stok İşlemleri ekranından açılır, menüde
listelenmez"* yazıyor ve canlı denemede sayfa **engellenmeden açıldı**. Katalog yanlış değil,
"web menüsünde listeleme" kararı. Dokunulmayacak.

## 4. Test durumu

Transfer servis düzeyinde **iyi test edilmiş**: 21 dosyada 68 `Transfer(` çağrısı
(`MultiBranchStockScenarioTests` ~22 · `StockLocationTests` ~19 · `StockOperationTests` ~17 ·
`StockDistributeTests` ~17 · `BranchScopeDesktopTransferTests` ~8 …).

**Eksik olan:** transfere özel **UI parite testi yok**. TRF-01'in asıl katkısı bu olacak —
`RPR-01`'in rapor filtreleri için yaptığının aynısı, transfer ekranı için.

## 5. Yapılacaklar

1. Maliyet merkezi alanı transferde gizlenir (web + masaüstü) — §2
2. Masaüstünde "Tüm Şubeler" modunda işlem, **depo açıkça seçilmek şartıyla** açılır (web'in STK-04
   kuralı birebir) — kaynak depo seçilebilir listeye döner
3. Masaüstünde hedef listesinden kaynak depo dışlanır
4. Web onay metnine hedef deponun **adı** yazılır
5. **UI parite testi** eklenir: iki platformun transfer ekranı aynı alan/kural kümesini taşır
