# STK-12 — Masaüstünde "Tüm Şubeler" modunda stok işlemi

> **Durum:** ✅ TAMAMLANDI · **2026-09-04**
> **Kaynak:** TRF-01 analizinde bulundu, oraya **bilinçli olarak sıkıştırılmadı** (ADR-205) —
> transfer'e özel değil, Stok ekranının tamamını ilgilendiriyor.

---

## 1. Sorun

Aynı iş iki platformda farklı davranıyor:

| | Web | Masaüstü |
|---|---|---|
| "Tüm Şubeler" ile giriş | **İşlem YAPILABİLİR** — depo açıkça seçilmek şartıyla (STK-04) | **Hiçbir işlem yapılamaz** — `BranchGuard.RequireBranchAsync` Kaydet'in tamamını kapatıyor |

Masaüstü kullanıcısı, çok depolu bir firmada yönetici olarak girdiğinde **hiç stok işlemi
yapamıyor**; çıkıp tek bir şube seçerek yeniden girmek zorunda. Kullanıcı ağırlıklı olarak
masaüstünü kullandığı için bu, günlük işi doğrudan aksatan bir fark.

## 2. Neden bu koruma vardı ve neden kaldırılmıyor

`BranchGuard` bir **yetki** sınırı değil, **veri doğruluğu** korumasıdır (kendi açıklaması):
"Tüm Şubeler" modunda oturumun çalışma şubesi yoktur; kayıt açılırsa hareket **şubesiz** düşer ve
hangi şantiyeye ait olduğu kaybolur.

**Bu kaygı geçerlidir ve korunacaktır.** Web'in STK-04'te bulduğu çözüm, korumayı kaldırmak değil
**yerini değiştirmek**tir:

> ~~"Şube seçmeden hiçbir şey yapamazsın"~~ → **"İşlemin yazılacağı depoyu açıkça seç"**

Sonuç aynı: belirsiz (şubesiz) stok hareketi **oluşamaz**. Fark: kullanıcı çıkıp yeniden giriş
yapmak zorunda kalmaz.

## 3. Taklit edilecek desen — web'in STK-04'ü (ölçüldü)

`src/DepoWise.Web/Components/Pages/Stock.razor`:

| Parça | Nerede | Ne yapıyor |
|---|---|---|
| **Uyarı bandı** | `:22-28` | "Tüm Şubeler" modunda: *"Stok bir depoya ait olmalıdır — işlem yapmadan önce aşağıdan Depo / Şantiye seçin."* |
| **Çalışma lokasyonu** | `:313` `_workLocation` | Yalnız bu modda kullanılan, kullanıcının seçtiği depo |
| **Etkin lokasyon** | `:316` `EffectiveLocation` | `IsAllBranches() ? NullIfEmpty(_workLocation) : Auth.BranchId` — şubeli kullanıcıda oturum şubesi, **değiştirilemez** |
| **Kayıt kapısı** | `:513-517` | `EffectiveLocation is null` → *"Önce işlemin yapılacağı depoyu/şantiyeyi seçin."* ve kayıt **yapılmaz** |
| **Depo seçici** | `:167` | Yalnız "Tüm Şubeler" modunda görünür; şubeli kullanıcıda salt-okunur ad (`:171`) |
| **Bakiye** | `:455` | Lokasyon seçilmeden bakiye bile sorulmaz: *"Bakiye için önce depo seçin."* |

Kilit ayrıntı: `EffectiveLocation` **asla boş metin göndermez** ("Atanmamış" kovasına düşürmez) —
yeni kayıt belirsiz olamaz.

## 4. Analiz — masaüstü kapsamı ölçüldü

**Bulgu: bu iş UI katmanındadır, servis katmanına DOKUNULMAZ.** `StockService` lokasyonu zaten her
metotta **parametre** olarak alıyor (`ReceiveIn(..., branchId)`, `IssueOut(..., branchId)`,
`Transfer(..., fromBranchId, toBranchId)`, `Count(..., branchId)`), ve `EnforceOwnBranch`
`BranchScope.Active(s)` null olduğunda **engellemiyor**. Yani sunucu tarafı bu senaryoyu zaten
destekliyordu; kapı yalnızca masaüstü arayüzündeydi.

**Etkilenen iki ekran** (`BranchGuard.RequireBranchAsync` çağrısı ile kilitliydi):

| Ekran | Dosya | Eski davranış |
|---|---|---|
| Malzeme Giriş-Çıkış | `StockEntryViewModel.cs` | Kaydet tümden kapalı |
| Stok Sayım | `StockCountViewModel.cs:205` | Kaydet tümden kapalı; ayrıca `CountLocationId` şubesizken **"Atanmamış"** kovasına düşüyordu |

**Kapsam dışı bırakılanlar (bilinçli):** Yakıt · Bakım · Muayene · Malzemeler · Araçlar ekranları
"Tüm Şubeler" modunda hâlâ işlem yapmaz — **ama bu bir parite farkı DEĞİL**: web de bu ekranlarda
aynı bandı gösterip işlemi kapatıyor (`Fuel.razor:16`, `Maintenance.razor:22`, `Inspection.razor:15`,
`Materials.razor:33`, `Vehicles.razor:30` → `BranchGuard.Banner`). İki platform bu ekranlarda zaten
aynı. STK-12 yalnız **gerçek farkı** kapatır.

## 5. Yapılanlar

| # | İş | Durum |
|---|---|---|
| 1 | `StockEntryViewModel`: `IsAllBranches` · `Branches` · `CalismaDeposu` · `EtkinLokasyon` (**nullable**) · `EtkinLokasyonAdi` · `TumSubelerUyarisi` | ✅ |
| 2 | Giriş-Çıkış kayıt kapısı: `BranchGuard` yerine `EtkinLokasyon is null` → *"Önce işlemin yapılacağı depoyu/şantiyeyi seçin."* | ✅ |
| 3 | Üç işlem yolu da (`ReceiveIn` · `IssueOut` · `Transfer`) etkin lokasyondan besleniyor | ✅ |
| 4 | Transfer: hedef listesinden dışlanan depo ve onay metnindeki kaynak adı da etkin lokasyondan | ✅ |
| 5 | `StockCountViewModel`: aynı desen; `CountLocationId` artık **nullable** — "Atanmamış" kovasına asla düşmez | ✅ |
| 6 | Sayımda depo değişince **sepet temizlenir** (sistem stokları eski depoya aitti) + kullanıcı bilgilendirilir | ✅ |
| 7 | Depo seçilmeden **bakiye okunmaz** (firma geneli toplamı göstermek kullanıcıyı yanıltırdı) | ✅ |
| 8 | İki XAML: uyarı bandı + zorunlu `Depo / Şantiye` seçici (yalnız bu modda); şubeli kullanıcıda alan **salt-okunur** kalır | ✅ |
| 9 | `TumSubelerStokPariteTests` (6 test) — kapıyı ve yönlendirmeyi kilitler | ✅ |
| 10 | `TransferPariteTests` TRP2/TRP3 etkin lokasyona göre güncellendi; `AllBranchesGuardTests` açıklaması düzeltildi | ✅ |

**Migration GEREKMEDİ** — servis sözleşmesi ve şema değişmedi, yalnız arayüz katmanı.

### En kritik ayrıntı

`EtkinLokasyon` / `CountLocationId` **asla boş metin göndermez**. Eskiden sayım ekranı şubesizken
`StockBalanceWriter.Unassigned` ("Atanmamış") değerini yazıyordu; kapı kaldırılıp bu davranış
bıraksaydı **belirsiz stok hareketleri sessizce üretilirdi** — düzeltmeye çalıştığımız sorunun
daha kötü bir hâli. Tip `string?` yapıldı ki derleyici bu yolu kapalı tutsun.

## 6. Doğrulama

| Kontrol | Sonuç |
|---|---|
| Masaüstü derleme | ✅ 0 hata |
| STK-12 + transfer + guard + tasarım nöbetleri (95 test) | ✅ 95/95 |
| Tam süit | ✅ (aşağıdaki ADR kaydına bak) |
