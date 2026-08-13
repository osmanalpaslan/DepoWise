# Masaüstü GUI — Otomatik Test Sonuçları

**Durum (2026-08-13): 28 maddenin 22'si GERÇEK GUI ETKİLEŞİMİYLE KOŞTURULDU ve GEÇTİ.**
Kalan 6 madde aşağıda tek tek gerekçesiyle işaretlidir. **Hiçbir madde API/servis testiyle "GUI geçti" sayılmamıştır.**

## Nasıl koşturuldu

Windows'un yerleşik **UI Automation** arayüzü Avalonia 12 penceresini sürüyor — ek paket/framework YOK.
Etkileşimler gerçek: `ValuePattern.SetValue` ile yazma, `InvokePattern.Invoke` ve **gerçek fare olayları**
(`mouse_event`) ile tıklama, `Graphics.CopyFromScreen` ile ekran görüntüsü.

```powershell
Add-Type -AssemblyName UIAutomationClient; Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win  = $root.FindAll('Children', TrueCondition) | ? { $_.Current.ProcessId -eq $pid }
# okuma : $win.FindAll('Descendants', TrueCondition)
# yazma : $el.GetCurrentPattern([ValuePattern]::Pattern).SetValue('...')
# tıklama: $el.GetCurrentPattern([InvokePattern]::Pattern).Invoke()   (veya gerçek mouse_event)
```

### İzole ortam (üretime HİÇ dokunulmadı)

| Parça | Ayar |
|---|---|
| Sunucu | `DEPOWISE_SERVER_DATA=<scratch>/apidata` · `http://127.0.0.1:5099` · **SQLite** (PG bağlantısı yok) |
| Masaüstü veri | `DEPOWISE_ENVIRONMENT=GuiTest` → `%LOCALAPPDATA%\Alpnex\Data\GuiTest\alpnex.db` |
| Sunucu adresi | `bin\...\serverurl.txt` = yerel API (**test sonunda SİLİNDİ** → üretim varsayılanına dönüldü) |
| Ortak önbellek | `%LOCALAPPDATA%\Alpnex\{lastuser,machine_status,machine_branch}.txt` yedeklendi ve **md5 doğrulamasıyla geri yüklendi** |
| Test verisi | Yalnız izole DB'de; **hepsi uygulamanın kendi ekranlarından/REST uçlarından** oluşturuldu (elle INSERT yok) |

Kullanıcılar: `admin` (kapsam **A+B**), `depo1` (kapsam **A**), `superadmin` (kısıtsız). `Sube C` kimseye verilmedi.

---

## Sonuçlar

| # | Yapılacak | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Uygulamayı aç, giriş yap | ✅ GEÇTİ | Giriş ekranı render, alanlar yazılabiliyor, DEVAM çalışıyor, hatalı parolada doğru mesaj; başarılı girişte `MainWindow` açılıyor |
| 2 | Şube seçim ekranı — yalnız yetkili şubeler | ✅ GEÇTİ (**hata bulundu ve düzeltildi**) | Önce A+B+C listeleniyordu → **GUI-01**. Düzeltme sonrası yalnız `Tüm Şubeler`, `Sube A`, `Sube B` |
| 3 | Ön Muhasebe → Cari Hesaplar, şube seçici | ✅ GEÇTİ | Liste açıldı; şube seçici A ve B ile göründü |
| 4 | Yeni cari oluştur | ✅ GEÇTİ | `CARI-1 / Ortak Cari` oluştu, listede göründü (`Sayfa 1/1 · 1 cari`) |
| 5 | Cari hareket ekle (açılış) — tarih doğru gün | ✅ GEÇTİ | 1500 Borç kaydedildi, **13.08.2026** (gün kayması yok), bakiye `1500 (B)` |
| 6 | Yeni Fatura — şube listesi yalnız yetkili | ✅ GEÇTİ | Cari listesi geldi; şube filtresi A/B |
| 7 | Alış faturası kes | ✅ GEÇTİ | `A00000001`, 2000 + KDV 400 = **2400**, tarih 13.08.2026, cari etkilendi |
| 8 | Satış faturası (stok yokken) reddedilir | ⏸️ KOŞTURULMADI | İzole ortamda malzeme/stok kartı yok; **kapsam dışı** tutuldu (negatif stok kapısı `StockService` testleriyle örtülü) |
| 9 | Tahsilat/Ödeme — açık fatura listelenir | ✅ GEÇTİ | Cari + kasa seçildi, `A00000001 — 2400.00 TRY` açık fatura olarak listelendi |
| 10 | Ödeme kaydet | ✅ GEÇTİ | 2400 ödeme kaydı (`BranchId = Sube A`), kasa ve cari etkilendi |
| 11 | Aynı işlemi tekrar kaydet — mükerrer yok | ⏸️ GUI'DE TETİKLENEMEDİ | Kayıttan sonra form kapanıyor; aynı `operation_id` ile ikinci gönderim GUI'den üretilemiyor. Otomatik testlerle örtülü |
| 12 | Ters kayıt (gerekçeli) | ✅ GEÇTİ | Gerekçesiz onay **"İptal gerekçesi zorunlu."** ile reddedildi; gerekçeyle karşı kayıt yazıldı (kayıt SİLİNMEDİ, 2 işlem) |
| 13 | Şube seçici — tek şube | ✅ GEÇTİ | Yalnız B seçili → 700; yalnız A → 1500 |
| 14 | Şube seçici — **iki şube** (geri besleme düzeltmesi) | ✅ GEÇTİ | A+B ikisi de **seçili kaldı**, veri **2200** (birleşik). A kaldırıldı → yalnız B kaldı |
| 15 | Seçimi temizle | ✅ GEÇTİ (**hata bulundu ve düzeltildi**) | Etiket "Tüm yetkili şubeler" derken yalnız çalışma şubesi geliyordu → **GUI-03**. Düzeltme sonrası **2200** |
| 16 | Yetkisiz şubeyi ara | ✅ GEÇTİ | `Sube C` hiçbir seçicide yok: giriş ekranı, ön muhasebe seçicisi, **rapor filtresi** (GUI-04), yetki devir listesi |
| 17 | Raporlar → Cari Bakiye Özeti | ✅ GEÇTİ | 1 satır + **TOPLAM** satırı |
| 18 | **Tarih vermeden Sorgula** | ✅ GEÇTİ | Varsayılan 1–13 Ağustos; **bugün (13.08.2026) oluşturulan kayıt rapora DAHİL**; patlama yok |
| 19 | Rapor / liste: A / B / A+B | ✅ GEÇTİ | A=1500, B=700, **A+B=2200** (tam toplam) |
| 20 | Cari seçici + şube birlikte | ✅ GEÇTİ | Cari seçili + `Sube B` → **0 satır**; cari seçili + `Sube A` → **1 satır** (kesişim) |
| 21 | Altı ön muhasebe raporu tek tek | ✅ GEÇTİ | Cari Ekstre 2 · Cari Bakiye Özeti 1 · Fatura Özeti 1 · Açık Faturalar/Vade 1 · Tahsilat/Ödeme Özeti 1 · Kasa/Banka Özeti 1 — hepsi hatasız |
| 22 | Yetkiler → Şube Kapsamı | ✅ GEÇTİ (**hata bulundu ve düzeltildi**) | Web'de oluşturulan kullanıcıda panel **sessizce kayboluyordu** → **GUI-05**. Düzeltme sonrası `depo1` için panel açılıyor |
| 23 | Kendi kapsamını değiştirmeyi dene | ✅ GEÇTİ | **"Kendi şube kapsamınızı değiştiremezsiniz. Bunu başka bir yetkili yapmalıdır."** + düzenleme kontrolleri gizli |
| 24 | Yetkisiz şubeyi devretmeyi dene | ✅ GEÇTİ | Devredilebilir listede yalnız **A ve B** var; `Sube C` **listede bile yok** (servis kapısı `RequireGrantable` ayrıca duruyor) |
| 25 | Kapsam değiştir → çıkış → yeniden giriş | ✅ GEÇTİ | `depo1` kapsamı A → **A+B** yapıldı, kaydedildi; yeniden girişte şube listesi **A ve B** oldu |
| 26 | Çevrimdışı (ağı kes) çalış | ✅ GEÇTİ | API durduruldu; Cari/Fatura ekranları açıldı, çalışma şubesi `Sube A` sabit, **kapsam genişlemedi** |
| 27 | Ağı aç → senkron (şube izolasyonu) | ⏸️ GUI'DE KOŞTURULMADI | İki makineli kurulum gerekir; tek makinede GUI'den doğrulanamaz. `BusinessSyncService` testleriyle örtülü |
| 28 | Tabloda metne tıkla → satır seçilir | ✅ GEÇTİ | `CARI-1` metnine **gerçek fare tıklaması** → satır seçili (G3 `TableRowSelect`) |

**Özet: 22 GEÇTİ · 0 BAŞARISIZ · 6 koşturulmadı (3'ü kapsam dışı/altyapı gerektiriyor, 3'ü GUI'den tetiklenemiyor).**

---

## Bu turda bulunan GERÇEK ÜRÜN HATALARI (hepsi düzeltildi + regresyon testi eklendi)

| Kod | Hata | Kök neden | Etki |
|---|---|---|---|
| **GUI-01** | Kapsamı A+B olan kullanıcı **yetkisiz Şube C'yi görüp o şubeye GİRİŞ yapabiliyordu** | Kullanıcı paketi (`RemoteUserBundle`) `user_scopes` taşımıyordu **ve** `AuthService.Login` oturuma `ScopeBranchIds` koymuyordu → masaüstünde kapsam **fiilen yoktu**, admin kısıtsız sayılıyordu | Şube kapsamı web'de uygulanıyor, masaüstünde uygulanmıyordu. Makine yetkisiz şubeye bağlanıyordu |
| **GUI-02** | Elle girilen cari hareket **şubesiz** (`branch_id = NULL`) kaydediliyordu | Masaüstü/web `BranchId` göndermiyor, `Add` `BranchAccess.Resolve` çağırmıyordu | Şubesiz satır "her şubeye ait" sayılır → A'nın açılış bakiyesi **B'nin ekstresinde ve raporunda** görünüyordu |
| **GUI-02b** | Ters kayıt şubesiz yazılıyor, **yetkisiz şubenin hareketi iptal edilebiliyordu** | `Reverse` aslın şubesini okumuyor, kapsam kapısı yok | Defterde yanlış şube; kapsam dışı iptal |
| **GUI-03** | Seçim temizlenince etiket **"Tüm yetkili şubeler"** derken yalnız çalışma şubesi geliyordu | Seçici boşken `null` gönderiyordu; `Effective` formülü `OTURUM` basamağına düşüyordu | Etiket ile veri çelişiyordu (B'de 2200 yerine 700) |
| **GUI-04** | **Rapor** şube filtresinde yetkisiz `Sube C` listeleniyordu | Rapor ekranı `BranchAccess.Allowed` ile kırpmıyordu (masaüstü + `/api/reports/scope`) | Kullanıcı yetkisiz şube seçip sebebi anlaşılmayan boş rapor alıyordu |
| **GUI-05** | Web'de oluşturulan kullanıcıda **"Şube Kapsamı" bölümü sessizce kayboluyordu** | Kullanıcı listesi ve yetkiler SUNUCUDAN, kapsam ise YEREL DB'den okunuyordu; yerelde olmayan kullanıcıda hata `Status`'a yazılıp hemen eziliyordu | Yönetici kapsamı masaüstünden yönetemiyor, sebebini de göremiyordu |

**Düzeltmelerde ikinci bir kapsam mantığı üretilmedi** — hepsi mevcut tek otorite `BranchAccess` üzerinden yürür.

## GUI'de test EDİLMEYEN, açıkça belirtilmesi gerekenler

- **Madde 8** (negatif stok kapısı): izole ortamda malzeme kartı/stok kurulmadı.
- **Madde 11** (idempotency): kayıttan sonra form kapandığı için aynı `operation_id` GUI'den ikinci kez gönderilemiyor.
- **Madde 27** (senkron şube izolasyonu): iki makine gerekir.
- **İlk giriş parola değiştirme ekranı (Adım 4)**: tohum parolalar REST üzerinden değiştirildiği için masaüstündeki
  şifre-belirleme ekranı bu turda AÇILMADI.
- **`depo1` çoklu şube seçicisi**: `Şube Seçimi (Çok Şubeli Görüntüleme)` buton yetkisi verilmediği için seçici
  görünmedi — bu **doğru** deny-by-default davranıştır, eksik değildir.

## Web'de daha önce doğrulananlar
Cari listesi ve bakiyeleri · altı ön muhasebe raporunun tamamı · şube filtresi · cari + şube kesişimi · tarih dönüşümü.
Bu, masaüstü UI katmanının doğrulandığı anlamına gelmez — yukarıdaki tablo masaüstünün kendi kanıtıdır.

## Bulunursa
Her hata için: ekran · adım · beklenen · gerçekleşen · ekran görüntüsü.
