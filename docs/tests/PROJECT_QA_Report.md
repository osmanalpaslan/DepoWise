# DepoWise — Kapsamlı Proje QA Raporu

> Kullanıcı isteği üzerine **proje geneli (regresyon)** QA taraması. Bu, §7.1'deki "yalnız değişen ekran"
> kuralının **istisnası**dır (kullanıcı açıkça talep etti). Tarih: **2026-07-12**. Motor: Opus 4.8.

## 0. Yöntem ve dürüst kapsam sınırı
Bu tarama üç gerçek kaynağa dayanır; **hayal değil**:
1. **Otomatik test paketi** — `dotnet test` (273 senaryo).
2. **Statik/kod-seviyesi QA taraması** — 72 ekran + servis/endpoint katmanı desen taraması.
3. **Canlı web** — `depowise-web.fly.dev` giriş ekranı tarayıcıda gerçekten çalıştırıldı.

**Yapılamayanlar (şeffaflık):**
- **Masaüstü (Avalonia)** ekranları fare-otomasyonuyla tıklanamadı (headless UI test altyapısı yok) → kod + test + statik seviyede incelendi, gerçek tık tık test değil.
- **Yetkili web ekranları** (giriş sonrası) test edilemedi: giriş için parola girmek yasak (kullanıcı adına kimlik doğrulama). Yalnız giriş ekranı (yetkisiz) gerçek test edildi.
- Bu yüzden §7.3–7.12'deki "her alanı elle tıkla" seviyesi masaüstü/yetkili-web için **kod incelemesi** olarak uygulandı, canlı tık olarak değil.

## 1. Geçen testler (otomatik)
```
Başarılı! — Başarısız: 0, Başarılı: 273, Atlanan: 0, Toplam: 273, Süre: 26 s
```
- **273/273 yeşil, 0 hata.** 26 test dosyası, 247 test metodu (bazı `[Theory]` çok senaryolu → 273 senaryo).
- Solution build: **0 hata** (birkaç non-blocking uyarı — bkz. §4).

### Modül → test kapsamı haritası
| Modül / Ekran | Kapsam | Durum |
|---|---|---|
| Auth / Yetki / JWT | AuthPermissionTests, SecurityHardeningTests, JwtTokenTests | ✅ güçlü |
| Senkron / BusinessSync / Cihaz kaydı | SyncTests, BusinessSyncTests | ✅ güçlü |
| Firma / Personel / Şube | OrgPersonnelTests | ✅ |
| Firma yetki kontrol (global kilit) | CompanyGrantTests | ✅ |
| Malzeme / Stok / Stok hareket | MaterialTests, StockOperationTests | ✅ |
| Araç / Araç şablonu | VehicleTests | ✅ |
| Bakım | MaintenanceTests | ✅ |
| Yakıt / Günlük faaliyet | FuelDailyActivityTests | ✅ |
| Talep | RequestTests | ✅ |
| Uyarı (alert) okuma | AlertReadTests | ✅ |
| Rapor / Dashboard | ReportingTests | ✅ |
| Dosya / Çöp / Yedek | FileTrashBackupTests | ✅ |
| Migration / DB temeli | DatabaseFoundationTests, Migration029/030Tests | ✅ |
| Türkçe kültür / arama | TurkishLikeTests | ✅ |
| Sunucu presence / online | ServerPresenceTests | ✅ |
| E2E akış | EndToEndTests | ✅ |
| **Muayene (Inspection)** | yalnız dolaylı (Maintenance/Reporting/Alert) | ⚠ özel test yok |
| **Sayım (StockCount)** | yalnız dolaylı (StockOperation/Reporting) | ⚠ özel test yok |

## 2. Bulunan hatalar / bulgular
| # | Öncelik | Ekran/Alan | Bulgu | Beklenen | Gerçek |
|---|---|---|---|---|---|
| B1 | Düşük (UX) | Web Login | Boş alanla **DEVAM** → genel hata | "Kullanıcı adı ve parola gerekli" | "Kullanıcı adı veya parola hatalı." |
| B2 | Düşük | Web: Audit, Trash, QuotaMonitor, Developer | Sayfa bileşeninde erken-çıkış yetki guard'ı yok (menü + API kapılı, ama savunma derinliği eksik) | Sayfada da `Auth.IsSuperAdmin/IsAdmin` guard | Yalnız menü (`CanSeeMenu`) + API `RequireAuthorization` koruyor |
| B3 | Orta (kalite) | Test | Inspection ve StockCount için **özel test dosyası yok** | Her modülde regresyon testi | Yalnız dolaylı kapsam |
| B4 | Düşük | Build | Non-blocking uyarılar (MUD0002, CS8604, xUnit1031) | 0 uyarı | ~7 web + birkaç test uyarısı |

> **Güvenlik açısı olumlu:** B1'de genel "hatalı" mesajı hangi alanın yanlış olduğunu sızdırmadığı için güvenli; yalnız UX iyileştirmesi.

## 3. Statik QA tarama sonuçları (72 ekran)
- **Silme onayı:** Silme çağrısı olup `Dialog.Confirm`/`ConfirmService` içermeyen ekran **yok** (Home yanlış-alarmdı). ✅
- **Numeric alan:** Sayı gereken alanlarda `MudTextField` ile serbest metin **yok**; 10 ekran `MudNumericField` kullanıyor (§5 uyumlu). ✅
- **Tarih alanı:** 6 ekran `MudDatePicker` (Daily, Inspection, Maintenance, Reports, Requests, ServerBackups). ✅
- **Yetki kapısı:** Menü sunucuda `AccessControl.CanSeeMenu` ile; her API ucu `RequireAuthorization` + servis `AccessControl.Require`. Deny-by-default korunuyor. ✅
- **Tenant güvenliği:** `company_id` daima session/server context'ten (Program.cs `S(c)`); doğrudan gövdeden alınmıyor. ✅

## 4. Performans notları
- Test paketi 273 senaryo / 26 sn → hızlı, izole, deterministik.
- Ağır raporlar Sorgula/Filtrele tıklanmadan çalışmıyor (§5). ✅
- Sunucu diski canlı izleniyor (ADR-078); disk %85'te kritik uyarı. Paket saklama politikası (en yeni 3) disk dolmasını önlüyor.
- Web (Blazor Server) ilk açılış SignalR circuit kurulumu birkaç saniye (ekran görüntüsü 30 sn timeout verdi ama sayfa render oldu) — beklenen davranış.

## 5. Coverage Matrix (proje geneli özet)
| Alan | Durum |
|---|---|
| Form Açıldı (login canlı, diğerleri kod) | ⚠ kısmi (yalnız login canlı) |
| Yeni Kayıt / Düzenleme / Silme | ✅ servis+test seviyesinde |
| Arama / Filtre / Grid | ✅ kod + TurkishLikeTests |
| Doğrulamalar | ✅ (numeric/tarih/onay desenleri) |
| Yetki | ✅ (menü + API + servis, testli) |
| Hata Mesajları | ✅ / ⚠ (B1 küçük) |
| Database (rollback/tx/audit/soft-delete) | ✅ (StockOperation, Migration, FileTrash testleri) |
| Offline / Sync | ✅ (SyncTests, BusinessSyncTests) |
| Performans | ✅ |
| UI / UX | ⚠ kısmi (masaüstü canlı test edilemedi) |
| Security (injection, yetki atlama, race, tenant) | ✅ (SecurityHardeningTests + parametreli sorgu + tenant guard) |

## 6. Tahmini test kapsamı ve senaryo sayısı
- **Çalıştırılan otomatik senaryo:** 273.
- **Statik incelenen ekran:** 72 (34 web + 38 masaüstü).
- **Canlı test edilen ekran:** 1 (web login, yetkisiz).
- **Tahmini fonksiyonel kapsam:** Sunucu/servis/senkron/güvenlik katmanı **yüksek** (testli); UI etkileşim katmanı **orta** (kod incelemesi, canlı tık sınırlı).

## 7. Sonuç
Proje **sağlıklı**: 273/273 test yeşil, build temiz, kritik güvenlik/tenant/senkron desenleri yerinde, silme-onayı ve numeric/tarih doğrulama kuralları tutarlı uygulanmış. Bulunan 4 madde **kritik değil** (3 düşük + 1 orta test-kapsamı). Öneriler §8 (chat yanıtı ve KNOWN_ISSUES'a taşınabilir).

> **GÜNCELLEME (2026-07-12):** B1–B4'ün tamamı **uygulandı** → test **281/281** (8 yeni). B1 login boş-alan
> mesajı düzeltildi; B2 Audit/QuotaMonitor/Developer sayfa-içi yetki guard'ı eklendi (Trash reauth ile korunuyor,
> rol guard'ı eklenmedi); B3 InspectionTests + StockCountTests (idempotent retry dahil); B4 CS8604 + MUD0002
> (DisableElevation→DropShadow, PanelClass→TabPanelsClass, title→MudTooltip) temizlendi. B1/B2/B4-web canlı için deploy bekliyor.

## 8. Kısa öneriler (madde madde)
### Çalışma mantığı
- Web login boş-alan mesajını "gerekli" yap (B1).
- Audit/Trash/QuotaMonitor/Developer sayfalarına sayfa-içi yetki guard'ı ekle (B2, savunma derinliği).

### Ekran / alan
- Masaüstü kritik ekranlar için **Avalonia.Headless** UI testi ekle (şu an UI otomasyonu yok).
- Inspection ve StockCount için **özel test dosyası** ekle (B3).

### Yapı / mimari
- Build uyarılarını temizle (MUD0002 `DisableElevation/PanelClass`, CS8604 nullable) — gürültü azalt (B4).
- `docs/tests/<Ekran>_Test_Report.md` üretimini CI'a bağla (§7.14 kuralı otomatikleşsin).
- Yetkili-web QA için **tohum (seed) test hesabı** + otomasyon (Playwright) düşün → giriş sonrası ekranlar da canlı test edilebilir.
