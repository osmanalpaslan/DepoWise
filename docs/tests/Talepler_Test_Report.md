# Talepler — Test Raporu (PRT-01 Grup 4)

> Kapsam: **yalnız değiştirilen ekranlar** (§7.1) — Talep Formu + Talep Onaylama (web + masaüstü).
> Talep Operasyonları ekranına **dokunulmadı** (`KLT-01a`'da sertleştirildi). Başka ekrana dokunulmadı,
> genel regresyon istenmedi. Tarih: **2026-08-10**. Motor: Opus 5. Commit: **`d5d601d`**.

## 0. Yapılanlar

| # | İş | Sonuç |
|---|---|---|
| **B-1** | Durum/arama/limit **sunucuya ulaşıyor** — `GET /api/requests` geriye uyumlu genişletildi | ✅ |
| **B-2** | **Öncelik seçici** web + masaüstü forma eklendi (ortak katalog, aynı seçenekler) | ✅ |
| **B-3** | API'nin boş **ret gerekçesini** `"Reddedildi"` yapması kaldırıldı → **400** | ✅ |
| **B-4** | **Gerçek iptal gerekçesi** (web + masaüstü + API); boş gerekçe **400** | ✅ |
| **B-5** | Web PDF logosu | ⚪ **UYGULANMADI — teknik borç** (bkz. §6) |
| **B-6** | Ölü **taslak** akışı temizlendi | ✅ |

**B-1'in gerçek kusuru:** uç parametresizdi (`List(s)`) → en yeni **200** kayıt dönüyor, web bunun
*içinde* istemci tarafında süzüyordu. 200'den fazla talebi olan firmada **eski talepler web'de hiç
bulunamıyordu**. Masaüstü aynı parametreleri servise zaten geçiyordu; eksik olan **yalnız HTTP hattıydı**.

**B-2'nin gerçek kusuru:** öncelik iki platformda da **gösteriliyor** ama hiçbirinden **seçilemiyordu**;
tüm talepler kalıcı olarak "Normal" kalıyordu. DB, servis ve testler hazırdı — eksik olan **UI + taşıma
zinciriydi**. `GetForEdit` önceliği döndürmediği için, seçici eklenince düzenlemede öncelik
**sıfırlanacaktı**; `RequestEditData.PriorityDb` eklenerek zincir tamamlandı (Grup 3'teki `Version`
taşıma zincirinin aynısı).

**B-3/B-4'ün gerçek kusuru:** API, servisin **zorunlu gerekçe** kuralını eziyor ve denetim kaydına
kullanıcının **yazmadığı** bir gerekçe yazıyordu (gerçek gerekçeden ayırt edilemez). Masaüstünde iptal,
**ret gerekçesi alanını ödünç alıyordu**: kullanıcı ret kutusuna bir şey yazmadıysa gerekçe `null`
gidiyor, yazdıysa RET için yazdığı metin iptale geçiyordu.

## 1. Otomatik testler

```
Başarılı! — Başarısız: 0, Başarılı: 1075, Atlanan: 33, Toplam: 1108
```

Taban 1090 → **1108 (+18)**. Solution build **0 hata**, değişen dosyalarda **yeni uyarı yok**.

| Yeni test dosyası | Adet | Kapsam |
|---|---|---|
| `ApiGroup4Tests` | 18 | **Gerçek HTTP hattı** — B-1: parametresiz eski çağrı · durum filtresi · geçersiz durum **400** · arama (açıklama + belge no) · limit + üst sınır · filtre+arama birlikte · **tenant izolasyonu**. B-2: öncelik kaydı · varsayılan Normal · **düzenlemede korunması**. B-3: boş **400** · boşluk **400** · gerçek gerekçe geçmişte. B-4: boş **400** · alansız **400** · gerçek gerekçe geçmişte · **başka firmanın talebi iptal edilemez (403)** |

> ⚠️ **Test düzeltmesi:** `B1_Tenant_Izolasyonu` ilk koşuda **403** ile düştü. **Üretim kodu doğruydu** —
> test, B firmasına A firmasının malzemesiyle talep açtırıyordu ve `EnsureMaterialOwned` haklı olarak
> reddetti. **Test** düzeltildi, üretim koduna dokunulmadı.

## 2. Senaryolar

| # | Senaryo | Beklenen | Sonuç |
|---|---|---|---|
| 1 | Parametresiz eski API çağrısı | Eskisi gibi çalışır | ✅ |
| 2 | 211 kayıt · hedef talep 200'lük sayfada | **Yok** (hatanın kanıtı) | ✅ doğrulandı |
| 3 | Aynı talep sunucu tarafı aramayla | **Bulunur** | ✅ `TLP-2017-0001` |
| 4 | Geçersiz durum parametresi | **400** (sessizce "draft"a düşmez) | ✅ |
| 5 | `limit=3` / `limit=999999` | 3 / üst sınıra çekilir | ✅ |
| 6 | Filtre + arama birlikte | Kesişim | ✅ |
| 7 | İki firma, aynı arama terimi | Herkes yalnız kendi firmasını görür | ✅ |
| 8 | Öncelik "Acil" ile talep | DB'ye `urgent` yazılır | ✅ (API) |
| 9 | Öncelik gönderilmezse | Normal kalır | ✅ |
| 10 | Öncelikli talebi düzenle + kaydet | Öncelik **korunur** | ✅ |
| 11 | Boş / boşluk **ret** gerekçesi | **400**, talep reddedilmez | ✅ |
| 12 | Gerçek ret gerekçesi | Geçmişte görünür; sahte `"Reddedildi"` **yok** | ✅ |
| 13 | Boş / alansız **iptal** gerekçesi | **400**, talep iptal edilmez | ✅ |
| 14 | Arayüzde boş iptal gerekçesi | Diyalog kapanmaz, **istek gitmez** | ✅ tarayıcı |
| 15 | Gerçek iptal gerekçesi | Veritabanına yazılır | ✅ tarayıcı |
| 16 | Başka firmanın talebini iptal | **403** | ✅ |
| 17 | Durum filtresinde "Taslak" | **Yok** | ✅ tarayıcı |

## 3. Coverage (§7.13)

| Alan | Durum |
|---|---|
| Form Açıldı · Yeni Kayıt · Düzenleme | ✅ öncelik alanı dahil (kaydetme zinciri API testiyle) |
| Silme | ⚪ talepte silme yolu yok — değişmedi |
| Arama · Filtre · Grid | ✅ **sunucu tarafına taşındı**, uçtan uca doğrulandı |
| Doğrulamalar | ✅ boş ret/iptal gerekçesi hem arayüzde hem API'de reddediliyor |
| Yetki | ⚪ değişmedi — `requests` / `request_approval` ayrımı ve "Onay Veren" kısıtı korunuyor |
| Hata Mesajları | ✅ 400'ler Türkçe; ⚠️ web'de ham JSON sarmalı sürüyor (`WEB-01`) |
| Database | ✅ öncelik, durum ve `request_status_history` gerekçeleri doğrulandı |
| Offline / Sync | ⚪ **dokunulmadı** — `material_requests` senkron davranışı değişmedi (bkz. B-7) |
| Performans | ⚪ süzme DB'ye taşındı; istemciye gereksiz veri taşınması azaldı |
| UI / UX | ✅ web gerçek tarayıcı; ⚠️ masaüstü görsel etkileşim **gözlemlenmedi** |
| Security | ✅ tenant izolasyonu filtre/arama ve iptal yollarında testli; parametreler bağlı (enjeksiyon yüzeyi açılmadı) |

## 4. Gerçek tarayıcı / HTTP QA (izole ortam)

Ayrı veritabanı + yalnız `127.0.0.1` (API `:5199`, web `:5299`). **Canlı sunucuya sıfır istek** —
izole API günlüğünde **281** istek, tamamı yerel.

| Doğrulama | Sonuç |
|---|---|
| 211 kayıt kuruldu; hedef talep varsayılan 200'lük sayfada | ✅ **yok** (hata kanıtlandı) |
| Tarayıcıda "IGNE" + Enter | ✅ `TLP-2017-0001` geldi (sayfa dışı kayıt) |
| Geçersiz durum → 400 · `limit=3` → 3 · `limit=999999` → sınırlandı | ✅ |
| Öncelik seçici ve seçenekleri (`Normal / Yüksek / Acil / Kritik`) | ✅ göründü, seçilebildi |
| İptal diyaloğu — boş gerekçe | ✅ diyalog kapanmadı, **istek gitmedi** (tek iptal kaydı) |
| İptal diyaloğu — gerçek gerekçe | ✅ `request_status_history`'de göründü; `"Kullanıcı iptali"` **0 kayıt** |
| Durum filtresinde "Taslak" | ✅ yok |

> **Süreç düzeltmesi (kayda geçsin):** İlk denemelerde arama çalışmıyor göründü. **Sebep üründe
> değildi** — web derlemesi, çalışan sürecin DLL'i kilitlemesi yüzünden **sessizce başarısız oluyordu**
> ve tarayıcı bayat ikiliye bakıyordu. Süreç durdurulup temiz derlendikten sonra doğrulandı.
> Ayrıca ara aşamada *"API günlüğünde parametreli çağrı yok"* denmişti; **bu çıkarım geçersizdi** —
> günlük `Request.Path` kullanır, sorgu dizesini yazmaz.

## 5. Doğrulanamayanlar

- ⚠️ **Avalonia GUI otomasyonu bu ortamda YOK — masaüstü GUI test EDİLMEDİ.** Masaüstü yalnız
  **kod + derleme + servis/HTTP testleri** düzeyinde doğrulandı. **Elle kontrol edilmeli:**
  ① talep formundaki yeni **Öncelik** açılır listesi (yerleşim, seçim, kaydetme),
  ② **Talebi İptal Et** akışındaki yeni gerekçe penceresi,
  ③ durum filtresinden "Taslak"ın kalkmış olması.
- ⚠️ **B-2'nin web formundan uçtan uca oluşturma turu tarayıcıda TAMAMLANAMADI** — MudBlazor otomatik
  tamamlamayla kalem ekleme sentetik etkileşimle oturmadı (form doğrulaması *"En az bir kalem ekleyin."*
  ile doğru davrandı). Seçicinin varlığı/seçenekleri/seçilebilirliği tarayıcıda doğrulandı; **kaydetme
  zinciri API testleriyle** (webin gönderdiği JSON gövdesinin aynısıyla) kanıtlandı.
  **"Tarayıcıda uçtan uca kaydedildi" DENMİYOR.**
- Boş iptal gerekçesinde *"İptal gerekçesi zorunlu."* metni tarayıcıda görünür olarak yakalanamadı;
  **davranış** (diyalog kapanmıyor, istek gitmiyor) doğrulandı.

## 6. Kapsam dışı bırakılanlar

| Konu | Neden |
|---|---|
| **B-5** web PDF logosu | **Güvenilir sunucu kaynağı YOK:** masaüstü logoyu `app_settings`'te (`requests.company_logo`) **yerel dosya yolu** olarak tutuyor · `app_settings` **senkron tablo listesinde değil** · sunucuda logo deposu/ucu yok. Kullanıcı kararı K6 gereği logo sistemi **icat edilmedi** → **teknik borç** |
| **B-7** senkron LWW'nin onay durumunu ezmesi | Düzeltme `BusinessSyncService`'in kolon yazma davranışında; tüm çakışma/yayılma mekanizmasını etkiler → **ayrı iş kalemi** (kullanıcı kararı K4). `ConflictTracked` davranışı da değiştirilmedi |
| **B-8** web'de `BranchScope`'un etkisiz olması | JWT şube taşımıyor, `PermissionSnapshot.ToSession()` `OperatingBranchId` kurmuyor → **10 servisteki** filtre web'de no-op. Talepler'e özgü değil → **ayrı iş kalemi** |
| `B-9` UPDATE `company_id` savunma derinliği · `B-10` `request_status_history` firma kolonu · `B-11` kalem notu · `B-12` detay uçlarında `is_deleted` · `B-13` doc-no yarışı hata davranışı | Teknik borç |
| `CreateIssueFromRequest` UI bağlantısı | Stok↔talep köprüsü — ayrı değerlendirme |
| Talep Operasyonları | `KLT-01a`'da sertleştirildi — tekrar ele alınmadı |

## 7. Riskler / notlar

- **API davranış değişikliği:** boş ret/iptal gerekçesi artık **400** döner (eskiden 200 + sahte gerekçe).
  Depodaki tek çağıran web talep ekranıdır ve artık her zaman gerçek gerekçe gönderir; masaüstü servisi
  doğrudan çağırır. Dış/doğrudan API çağrısı yapan bir istemci varsa etkilenir — bilinçli karar.
- **`Cancel` servis imzası DEĞİŞTİRİLMEDİ** (`reason = null` hâlâ geçerli): kullanıcı kararı gereği
  kontrol **uçta** yapılır, servis sözleşmesi korunur (masaüstü ve testler doğrudan çağırıyor).
- **Taslak durumu KALDIRILMADI** — DB'de ve durum makinesinde duruyor; yalnız ölü UI/komut temizlendi.
  Eski veya dış kaynaklı bir taslak kaydı "Tümü"de görünmeye ve "Taslak" olarak etiketlenmeye devam eder.
- **`DepoWise.Web.csproj`'a tek satır eklendi:** `RequestOperationStatus.cs` (saf katalog, dış bağımlılığı
  yok) web'e **link**lendi — projenin `ListColumns.cs` için zaten belgelediği istisna deseninin aynısı.
  Alternatifler Razor'da metin kopyalamak (katalog ikiye bölünürdü) veya yeni API ucu açmaktı.
- **Migration YOK, bağımlılık değişikliği YOK.** `priority` kolonu Migration060'ta zaten mevcuttu.
- `ConfirmDialog` / `ConfirmWindow` **değiştirilmedi** — çok sayıda çağıranı var, regresyon riski alınmadı.

## 8. Değişen dosyalar

**Servis / API:** `RequestService.cs` · `Program.cs`
**Web:** `Requests.razor` · `DepoWise.Web.csproj`
**Masaüstü:** `RequestsViewModel.cs` · `RequestsView.axaml`
**Test:** `ApiGroup4Tests.cs` *(yeni)*
