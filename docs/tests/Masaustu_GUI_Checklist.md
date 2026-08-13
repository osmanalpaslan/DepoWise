# Masaüstü GUI — Manuel Kullanıcı Test Listesi

**Durum: MANUEL TEST BEKLİYOR.** Avalonia penceresi otomasyon ortamında açılamıyor
(tarayıcı aracı yalnız web'e bağlanır). Bu liste **kullanıcı tarafından** uygulanacaktır.

**Web'de doğrulanmış olanlar** (masaüstünde de aynı servisleri kullanır): cari listesi ve bakiyeleri,
rapor sonuç tablosu, şube filtresi, tarih dönüşümü.

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
