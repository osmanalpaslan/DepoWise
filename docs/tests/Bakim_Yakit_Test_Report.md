# Bakım + Yakıt — Test Raporu (PRT-01 Grup 3)

> Kapsam: **yalnız değiştirilen ekranlar** (§7.1) — Bakım Tanımları (web + masaüstü) ve Yakıt
> (Dağıtımlar + Depo Girişleri, web + masaüstü). Başka ekrana dokunulmadı, genel regresyon istenmedi.
> Tarih: **2026-08-10**. Motor: Opus 5.

## 0. Yapılanlar

| # | İş | Sonuç |
|---|---|---|
| **B-1** | Bakım tanımı **düzenleme kilidi** — sürüm liste → ekran → kayıt zinciri boyunca taşınıyor | ✅ web + masaüstü |
| **B-2** | Masaüstü bakım tanımı silme | ⚪ **ZATEN MEVCUT** — kod yazılmadı (bkz. §5) |
| **B-4** | **Gerçek iptal gerekçesi** — sabit `"Kullanıcı iptali"` kaldırıldı | ✅ yakıt, web + masaüstü + API |
| **B-5** | Web yakıt dağıtım listesinde **para birimi** gösterimi | ✅ |

**B-1'in gerçek kusuru:** `MaintenanceDefinitionService.Update` **zaten** `expectedVersion` parametresi
alıyordu ve `EditLockGuard` mekanizması eksiksizdi — ama sürüm **hiçbir yere taşınmıyordu**:
`List` sorgusu `version` kolonunu seçmiyor, `MaintenanceDefinitionRow` alanı taşımıyor, `MaintDefDto`'da
alan yok, iki platform da göndermiyordu. Sonuç: mekanizma vardı ama **hiç devreye girmiyordu** — iki
yönetici aynı tanımı düzenlerse ikincisi birincinin değişikliğini **sessizce eziyordu**.

**B-4'ün gerçek kapsamı:** Bakım iptalinde gerekçe **zaten** kullanıcıdan alınıyordu ve zorunluydu
(web `Maintenance.razor:572`, masaüstü `MaintenanceViewModel.cs:602`). Sabit metin **yalnız yakıtta** vardı.
Ayrıca API'deki `string.IsNullOrWhiteSpace(reason) ? "Kullanıcı iptali" : reason` yedeği kaldırıldı —
`FuelService` gerekçeyi zaten **zorunlu** tutuyordu, API bu kuralı eziyor ve denetim kaydına kullanıcının
**yazmadığı** bir gerekçe yazıyordu (gerçek gerekçeden ayırt edilemez).

## 1. Otomatik testler

```
Başarılı! — Başarısız: 0, Başarılı: 1057, Atlanan: 33, Toplam: 1090, Süre: 5 m 4 s
```

Taban 1084 → **1090** (+6 yeni test). Solution build **0 hata, yeni uyarı yok**.

| Yeni test dosyası | Adet | Kapsam |
|---|---|---|
| `MaintenanceDefinitionConcurrencyTests` | 4 | Servis katmanı: liste sürüm döndürür · bayat sürüm reddedilir + 1. kullanıcının değeri korunur · sürüm verilmezse kontrol yok (geriye uyumlu) · alt bakım tanımı da sürüm taşır |
| `ApiGroup3Tests` | 6 | **Gerçek HTTP hattı**: B-1 sürüm listede · bayat sürüm **409** + Türkçe mesaj + kayıt korunur · sürümsüz eski istemci **200** · B-4 boş gerekçe **400** + kayıt iptal olmaz · gerekçe alanı hiç yoksa **400** · gerçek gerekçe **200** |

## 2. Senaryolar

| # | Senaryo | Beklenen | Sonuç |
|---|---|---|---|
| 1 | Bakım tanımı listesi `version` döndürüyor mu | Alan mevcut, > 0 | ✅ `"version":1` |
| 2 | İki yönetici aynı tanımı düzenler, ikincisi kaydeder | **409** + veri korunur | ✅ 1. kullanıcının `15000` değeri kaldı |
| 3 | 409 mesajı kullanıcı diline uygun mu | Türkçe, anlaşılır | ✅ *"…bir başkası tarafından değiştirildi…"* |
| 4 | Sürüm göndermeyen eski istemci | **200**, kırılmaz | ✅ (geriye uyumluluk) |
| 5 | Yeni tanım kaydı (sürüm 0) | Kilit kontrolü yapılmaz | ✅ |
| 6 | Yakıt iptalinde **boş** gerekçe (arayüz) | Uyarı, **istek gönderilmez** | ✅ *"İptal gerekçesi zorunlu."*, denetim kaydı artmadı |
| 7 | Yakıt iptalinde **boş** gerekçe (doğrudan API) | **400** | ✅ `{"error":"İptal gerekçesi zorunlu."}` |
| 8 | Yakıt iptalinde gerçek gerekçe | Denetime yazılır | ✅ `{"reason":"Yanlis araca islendi"}` |
| 9 | İptalden vazgeçme | Hiçbir istek gitmez | ✅ |
| 10 | Web yakıt dağıtım listesinde para birimi | Masaüstüyle aynı biçim | ✅ `42.5 TRY` / `5100 TRY` |

## 3. Coverage (§7.13)

| Alan | Durum |
|---|---|
| Form Açıldı · Düzenleme | ✅ bakım tanımı formu (web gerçek tarayıcı, masaüstü kod+derleme) |
| Silme | ⚪ değişmedi — zaten mevcut ve yetki korumalı (`CanDelete`) |
| Doğrulamalar | ✅ boş gerekçe hem arayüzde hem API'de reddediliyor |
| Yetki | ⚪ değişmedi — `Require(Edit)` + `RequireButton(Reverse)` korunuyor |
| Hata Mesajları | ✅ 409 ve 400 Türkçe; ⚠️ web'de ham JSON sarmalı sürüyor (`WEB-01`) |
| Database | ✅ `version` artışı, denetim kaydı (`audit_logs.after_json`) doğrulandı |
| Offline / Sync | ⚪ dokunulmadı — `maintenance_definitions` senkron akışı değişmedi |
| Performans | ⚪ sorguya tek kolon (`version`) eklendi, ölçülebilir etki yok |
| UI / UX | ✅ web gerçek tarayıcı; ⚠️ masaüstü görsel etkileşim **gözlemlenmedi** |
| Security | ⚪ yetki modeli değişmedi; denetim kaydı bütünlüğü **iyileşti** |

## 4. Gerçek tarayıcı doğrulaması (izole ortam)

Ayrı veritabanı + yalnız `127.0.0.1` (API `:5199`, web `:5299`). **Canlı sunucuya sıfır istek** —
izole API günlüğünde 72 istek sayıldı, tamamı yerel.

| Doğrulama | Sonuç |
|---|---|
| Web bakım tanımı: başkası değiştirdikten sonra kaydet | ✅ 409 uyarısı ekrana geldi, veri ezilmedi |
| Web yakıt iptali: boş gerekçe | ✅ diyalog açık kaldı, *"İptal gerekçesi zorunlu."*, **istek gitmedi** |
| Web yakıt iptali: gerçek gerekçe | ✅ denetim kaydında gerekçe göründü |
| Web yakıt para birimi | ✅ `42.5 TRY` / `5100 TRY` |

## 5. Analiz düzeltmeleri — önceki raporlarda YANLIŞ çıkanlar

> Bu iki madde **sessizce değiştirilmedi**; kullanıcıya bildirildi ve kod yazılmadan önce doğrulandı.

- **B-2 (masaüstü tanım silme) — "eksik" denmişti, GERÇEKTE MEVCUT.** Koddan doğrulandı:
  `MaintenanceViewModel.RequestDeleteDef` (yetki kontrolü `CanDelete` + onay penceresi + hata yakalama),
  görünümde `MaintenanceView.axaml:141` `IsVisible="{Binding CanDelete}"` ile bağlı **"Sil"** butonu ve
  ayrıca alt bakım silme (`DeleteSubDefCommand`, satır 126). **Kod yazılmadı** — çalışan davranış
  yeniden yazılmadı.
- **B-4 (iptal gerekçesi) — "bakım + yakıt" denmişti, GERÇEKTE yalnız YAKIT.** Bakım iptalinde gerekçe
  iki platformda da zaten zorunluydu. B-4 yakıtla sınırlı uygulandı.

## 6. Kapsam dışı bırakılanlar (teknik borç)

| Konu | Neden kapsam dışı |
|---|---|
| `WEB-01` — web'de `Hata 409: {"error":"…"}` ham JSON gösterimi | **Grup 3'e özgü değil.** `ApiClient.cs` içinde 5 biçimlendirme noktası, **36 web bileşenini** besliyor. Düzeltme tüm web ekranlarını etkiler → dar kapsam kuralını bozar. §12 `WEB-01` altında zaten kayıtlı |
| `/api/maintenance/cancel` ve `/api/stock/reverse` uçlarındaki aynı `"Kullanıcı iptali"` yedeği | **Başka modüller** (bakım kaydı iptali, stok ters kaydı). Grup 3 yakıt kapsamındaydı; bu iki uca dokunmak kapsam genişletmesi olurdu |
| Diğer ekranlarda sabit gerekçe: `StockEntryViewModel.cs:508`, `Stock.razor:498`, `Requests.razor:318` | Stok ve Talepler ekranları — Grup 3 dışı |
| `B-6` … `B-10` (Grup 3 analizinde açılan diğer maddeler) | Kullanıcı kararı **K5 = B** ile kapsam `B-1 + B-2 + B-4 + B-5` olarak sabitlendi |

## 7. Riskler / notlar

- ⚠️ **Masaüstü GUI etkileşimi gözlemlenmedi** — Avalonia için GUI otomasyonu bu ortamda yok.
  Masaüstü doğrulaması **kod + derleme + servis düzeyi testler** ile sınırlıdır; yeni `ReasonWindow`
  penceresinin görsel davranışı (odak, Esc, buton yerleşimi) **elle bakılmalıdır**. Test edilmiş gibi
  raporlanmadı.
- **API davranış değişikliği:** boş gerekçeyle yakıt iptali artık **400** döner (eskiden 200 + sahte
  gerekçe). Depodaki **tek çağıran** web yakıt ekranıdır ve o artık her zaman gerçek gerekçe gönderir;
  masaüstü servisi doğrudan çağırır (HTTP'ye çıkmaz). Dış/doğrudan API çağrısı yapan bir istemci varsa
  etkilenir — bilinçli karar.
- **Migration YOK, bağımlılık değişikliği YOK.** `version` kolonu `maintenance_definitions` tablosunda
  zaten mevcuttu; yalnız `SELECT` listesine eklendi.
- `ConfirmWindow` (masaüstü) ve `ConfirmDialog` (web) **değiştirilmedi** — çok sayıda çağıranı var,
  regresyon riski alınmadı; gerekçe pencereleri **ayrı bileşen** olarak eklendi.

## 8. Değişen dosyalar

**Servis / API:** `MaintenanceDefinitionService.cs` · `Program.cs`
**Web:** `Maintenance.razor` · `Fuel.razor` · `DialogExtensions.cs` · `ReasonInputDialog.razor` *(yeni)*
**Masaüstü:** `MaintenanceViewModel.cs` · `FuelViewModel.cs` · `ConfirmService.cs` ·
`ReasonWindow.axaml` + `.axaml.cs` *(yeni)*
**Test:** `MaintenanceDefinitionConcurrencyTests.cs` *(yeni)* · `ApiGroup3Tests.cs` *(yeni)*
