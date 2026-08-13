# Masaüstü GUI — Manuel Kullanıcı Test Listesi

**Durum: MANUEL TEST BEKLİYOR — ancak OTOMASYON YOLU AÇILDI.**

## 2026-08-13 — MASAÜSTÜ GUI OTOMASYONU MÜMKÜN (önceki tespit düzeltildi)

Önceki turlarda "Avalonia penceresiyle etkileşim kurulamıyor" denmişti. **Bu yanlıştı.**
Windows'un yerleşik **UI Automation** arayüzü Avalonia 12 penceresini görüyor ve sürebiliyor —
ek paket, framework veya ücretli araç GEREKMİYOR.

### Kanıtlanan yetenekler (gerçek etkileşim, DOM/JS hilesi değil)

| Yetenek | Kanıt |
|---|---|
| Pencere bulma | `Alpnex — Giriş \| class=LoginWindow` |
| Eleman ağacını okuma | 22 eleman; Edit/Button/CheckBox/Text tipleriyle |
| Metin alanına yazma | `ValuePattern.SetValue('admin')` → alan gerçekten doldu |
| Butona basma | `InvokePattern.Invoke()` → uygulama tepki verdi |
| Uygulama yanıtını okuma | *"Kullanıcı adı veya parola hatalı."* mesajı okundu |

### Kullanılan yöntem (tekrar edilebilir)

```powershell
Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win  = $root.FindFirst('Children', <ClassName = LoginWindow / MainWindow>)
# okuma:  $win.FindAll('Descendants', TrueCondition)
# yazma:  $el.GetCurrentPattern([ValuePattern]::Pattern).SetValue('...')
# tıklama:$el.GetCurrentPattern([InvokePattern]::Pattern).Invoke()
```

Uygulamayı izole çalıştırma:
- `DEPOWISE_ENVIRONMENT=GuiTest` → veritabanı `%LOCALAPPDATA%\Alpnex\Data\GuiTest\alpnex.db`
- `bin\...\serverurl.txt` içine yerel API adresi yazılırsa masaüstü **production yerine yerel sunucuya** bağlanır
  (⚠️ test bitince bu dosya SİLİNMELİDİR — aksi hâlde uygulama yanlış sunucuya bakar)

### Neden 28 madde yine de tamamlanamadı

**Giriş yapılamadı.** İzole `GuiTest` veritabanında kullanıcı yok (masaüstünde seed admin
oluşturulmuyor) ve "Web'de Giriş Yap" yolu makine kaydı/ek adım istiyor. Production sunucusuna
bağlanmak yasak olduğu için giriş sonrası ekranlara (madde 3–28) ulaşılamadı.

**Bir sonraki turda çözülebilir:** izole API'de makine kaydı/onayı tamamlanır ya da masaüstü
girişinin yerel kullanıcı ile çalıştığı senaryo kurulur; ardından bu 28 madde **otomatik**
koşturulabilir.

### Şu an doğrulanmış maddeler

- **Madde 1 (kısmi):** uygulama açılıyor, veritabanı kuruluyor (`startup.log ok=True`, WAL 2,5 MB),
  45 sn ayakta, çökme yok, kullanıcının `Development` verisine dokunulmuyor.
- **Madde 1 (GUI):** giriş ekranı render oluyor, alanlar yazılabiliyor, buton çalışıyor,
  **hatalı parolada doğru hata mesajı gösteriliyor** ← gerçek GUI davranışı.

**2–28 arası maddeler hâlâ MANUEL TEST BEKLİYOR.**

## Web'de zaten doğrulanmış olanlar
Masaüstü aynı servis katmanını kullandığı için şunlar web GUI'sinde gerçek veriyle kanıtlandı:
cari listesi ve bakiyeleri · **altı ön muhasebe raporunun tamamı** · şube filtresi ·
**cari + şube kesişimi** · tarih dönüşümü. Bu, masaüstü UI katmanının doğrulandığı anlamına **gelmez**.

---
| # | Yapılacak | Beklenen sonuç | Başarısızsa bakılacak yer |
|---|---|---|---|
| 1 | Uygulamayı aç, giriş yap | Giriş ekranı açılır; ilk girişte parola değiştirme istenir | `AuthService`, `LoginViewModel` |
| 2 | Şube seçim ekranı | **Yalnız yetkili şubeler** + "Tüm Şubeler" | `ScopeResolver`, `BranchService.List` |
| 3 | Ön Muhasebe → Cari Hesaplar | Liste açılır; **şube seçici görünür** (çok şubeliyse) | `BranchScopeSelector.IsVisible` |
| 4 | Yeni cari oluştur | Kayıt oluşur, listede görünür | `PartiesViewModel.SaveParty` |
| 5 | Cari hareket ekle (açılış) | **Tarih doğru gün** (bir gün kaymaz) | `FieldChecks.ToUnixMs` |
| 6 | Fatura → Yeni Fatura | Şube listesi **yalnız yetkili**; varsayılan = çalışma şubesi | `ActiveWriteBranchId` |
| 7 | Alış faturası kes | Fatura no üretilir; cari ve stok etkilenir | `InvoiceService.Create` |
| 8 | Satış faturası (stok yokken) | **Reddedilir** ("yeterli stok yok") — doğru davranış | `StockService` negatif stok kapısı |
| 9 | Tahsilat/Ödeme ekranı | Cari + kasa seçilir; açık fatura listelenir | `PaymentsViewModel` |
| 10 | Ödeme kaydet | Fatura kalanı azalır, kasa ve cari bakiyesi değişir | `FinanceService.Add` |
| 11 | Aynı işlemi tekrar kaydet | **Mükerrer kayıt oluşmaz** (operation_id) | `Migration068` tekil indeks |
| 12 | Ters kayıt (gerekçeli) | Kayıt silinmez; karşı kayıt yazılır, kalan geri artar | `FinanceService.Reverse` |
| 13 | Şube seçici — tek şube işaretle | Yalnız o şubenin verisi | `BranchScope.Filter` |
| 14 | Şube seçici — **iki şube işaretle** | **Seçim silinmez**, birleşik veri gelir | `_suppress` / `SyncPicks` (geri besleme düzeltmesi) |
| 15 | Seçimi temizle | "Tüm yetkili şubeler" davranışı | `SelectionText` |
| 16 | Yetkisiz şubeyi ara | **Listede hiç yok** | `BranchAccess.Allowed` kesişimi |
| 17 | Raporlar → Cari Bakiye Özeti | Sonuç tablosu ve TOPLAM satırı gelir | `AccountingReports.Balances` |
| 18 | **Tarih vermeden Sorgula** | **Patlamaz**; bugünün kayıtları **dahil** | `ToUnixMs(endOfDay)` |
| 19 | Rapor: A / B / A+B | A+B = A + B (tam toplam) | `ReportScope.BranchSql` |
| 20 | Rapor: cari seçici + şube birlikte | Kesişim uygulanır | `AccountingReports` |
| 21 | Altı ön muhasebe raporunu tek tek çalıştır | Hepsi sonuç üretir | `ReportService.Dispatch` |
| 22 | Yetkiler → Şube Kapsamı | **Yalnız devredebildiğiniz şubeler** listelenir | `PermissionService.GetBranchScope` |
| 23 | Kendi kapsamınızı değiştirmeyi deneyin | **Engellenir** (açık hata) | `SaveBranchScope` self kontrolü |
| 24 | Yetkisiz şubeyi devretmeyi deneyin | **Reddedilir** (sessiz kırpma yok) | `BranchAccess.RequireGrantable` |
| 25 | Kapsam değiştir → çıkış → yeniden giriş | **Yeni kapsam uygulanır** | `PermissionSnapshotCache.InvalidateUser` |
| 26 | Çevrimdışı (ağı kes) çalış | Ekranlar çalışır; kapsam **genişlemez** | `BranchScopeSelector` (oturumdan türer) |
| 27 | Ağı aç → senkron | Yalnız izinli şubelerin verisi iner/gider | `BusinessSyncService` şube izolasyonu |
| 28 | Tabloda metne tıkla | **Satır seçilir** (G3 davranışı) | `TableRowSelect` |

---

## Bulunursa
Her hata için: ekran · adım · beklenen · gerçekleşen · ekran görüntüsü.
