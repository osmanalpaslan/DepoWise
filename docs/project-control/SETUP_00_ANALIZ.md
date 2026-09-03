# ALPNEX SETUP / BOOTSTRAPPER — ANALİZ (uygulama öncesi)

> Tarih: **2026-09-04** · Durum: **YALNIZ ANALİZ — kod değiştirilmedi, production'a dokunulmadı**
> Kural: bu belge onaylanmadan implementasyona geçilmez (kullanıcı talebi §25).

---

## A. MEVCUT SETUP

### A.1 Teknoloji ve konum

| | |
|---|---|
| Proje | `src/DepoWise.Setup/` — **3 dosya**: `Program.cs` (211 satır), `.csproj`, `Assets/app-logo.ico` |
| UI teknolojisi | **Windows Forms** (`UseWindowsForms=true`) — **Avalonia DEĞİL** |
| Hedef | `net8.0-windows` |
| Yayın | `-r win-x64 --self-contained true -p:PublishSingleFile=true`, sıkıştırma açık, native kütüphaneler exe'ye gömülü |
| **Ölçülen boyut** | **71.885.605 bayt (69 MB)** — bu oturumda yeniden yayınlanıp ölçüldü |
| Sunucu adresi | Derleme zamanında `AssemblyMetadata("ServerUrl")` ile gömülüyor (`-p:ServerUrl=...`), varsayılan `https://depowise-erp.fly.dev` |

### A.2 Mevcut akış

```
AlpnexSetup.exe
   → GET /api/releases/latest        (kimlik doğrulama YOK, açık uç)
   → JSON: version, downloadUrl, checksum, sizeBytes, minSupportedVersion, signed
   → GET <downloadUrl>               (zip, ~86 MB)
   → ZipFile ile klasöre aç          (zip-slip koruması VAR)
   → serverurl.txt yaz
   → masaüstü kısayolu oluştur (WScript.Shell)
   → MessageBox "tamamlandı"
```

### A.3 Dağıtım zinciri

```
dotnet publish → AlpnexSetup.exe
   → scripts/publish_setup.mjs  (süper admin girişi)
   → POST /api/setup
   → GET /api/setup/download    (yeni bilgisayarın indirdiği adres)
```

### A.4 Mevcut güncelleme sistemi (AYRI ve DAHA OLGUN)

Uygulama içi güncelleme yolu Setup'tan bağımsız ve **belirgin şekilde daha güvenli**:

- `UpdateService.RequireVerifiedPackage` → **fail-closed checksum** (boş checksum = kurulum YOK).
  Bu, 2026-08-26 denetiminde **bilinçli olarak kapatılmış bir açıktı (UPD-01)**.
- `UpdateInstaller` → staging → yedek → kur → başarısızsa **rollback** → sürümü yalnız başarıda yaz.
- Paket bütünlük guard'ı: zip içinde ana exe yoksa kurulum başlamaz.
- Güncelleme durumu **sabit yolda**: `%LOCALAPPDATA%\Alpnex\update\current.txt`

---

## A.5 TESPİT EDİLEN KUSURLAR (koddan doğrulandı, varsayım değil)

| # | Kusur | Kanıt | Önem |
|---|---|---|---|
| **S1** | **Setup indirdiği paketi DOĞRULAMIYOR.** Sunucu SHA-256'yı veriyor (`Latest()` döndürüyor) ve yayında **64 hane hex zorunlu** (`ReleaseService.Publish`), ama `Program.cs` bu alanı hiç okumuyor. | `Program.cs:98-107` — yalnız `version` + `downloadUrl` okunuyor | 🔴 **KRİTİK** |
| **S2** | **Taze kurulumdan sonra aynı paket tekrar iniyor.** Setup `current.txt` yazmıyor → `UpdateService` onu `0.0.0` olarak oluşturuyor → `Check()` "güncelleme var" diyor → ilk açılışta ~86 MB tekrar iniyor ve yeniden kuruluyor. | `Program.cs` (yazılmıyor) + `UpdateService.cs:25,32,36-40` | 🔴 **YÜKSEK** |
| **S3** | `sizeBytes` sunucudan geliyor, **kontrol edilmiyor** (yarım indirme yakalanmaz) | `Program.cs:98-107` | 🟡 Orta |
| **S4** | **Yeniden deneme / kaldığı yerden devam YOK.** Tek `GetAsync`; bağlantı koparsa kurulum tamamen başarısız. Tek koruma 30 dakikalık timeout. | `Program.cs:136-170` | 🟡 Orta |
| **S5** | **Sistem ön-koşulu kontrolü YOK**: Windows sürümü, mimari, disk alanı, ağ, `minSupportedVersion` — hiçbiri bakılmıyor | `Program.cs` geneli | 🟡 Orta |
| **S6** | **`downloadUrl` şeması/host'u doğrulanmıyor.** Sunucu mutlak `http://...` dönerse olduğu gibi indirilir. Bugün sunucu göreli yol döndürüyor, ama savunma katmanı yok. | `Program.cs:101-103` | 🟡 Orta (S1 ile birleşince kritik) |
| **S7** | **Log dosyası YOK.** Hata yalnız `MessageBox`'ta; kullanıcı kapatınca kanıt kalmıyor. | `Program.cs:128-133` | 🟡 Orta |
| **S8** | **Kurulum düzeni tutarsız.** Varsayılan `%LOCALAPPDATA%\Alpnex\app`; "Gözat" ile `<seçilen>\Alpnex`. Güncelleme durumu ise **her zaman** `%LOCALAPPDATA%\Alpnex\update`. Farklı klasöre kurulursa ikisi ayrışır. | `Program.cs:52,57` + `DesktopServices.cs:205` | 🟡 Orta |
| **S9** | **Setup kendini güncelleyemiyor.** Yeni bootstrapper çıkarsa kullanıcı elle indirmeli. | — | 🟢 Düşük |
| **S10** | **`DepoWise.Desktop.csproj` yayın biçimini SABİTLEMİYOR.** `RuntimeIdentifier` / `SelfContained` / `PublishSingleFile` yok; kural yalnız komut satırında ve `CLAUDE.md`'de yaşıyor. Düz `dotnet publish` **bozuk (framework-dependent) paket** üretir. | `DepoWise.Desktop.csproj:2-10` | 🟡 Orta |

**S1 hakkında not:** Bu, uygulama içi güncelleyicide **bilinçli olarak kapatılan açığın kurulum tarafındaki eşi**. Aynı sınıf risk (indirilen ne ise onu aç ve çalıştır), aynı üründe bir kapı kilitli, diğeri açık.

---

## B. GERÇEK BAĞIMLILIK LİSTESİ (ampirik olarak doğrulandı)

> Yöntem: `artifacts/rc/desktop-1.0.171/` (253 dosya, 245 MB) içindeki **tüm** exe/dll'lerin
> import tabloları tarandı; kaynak kodda WebView2 araması yapıldı.

| Bileşen | Gerekli mi? | Kanıt | Not |
|---|---|---|---|
| **.NET Runtime** | ❌ **HAYIR** | Uygulama `--self-contained` yayınlanıyor; runtime pakette | — |
| **.NET Desktop Runtime** | ❌ **HAYIR** | Aynı | Avalonia WPF/WinForms kullanmaz |
| **WebView2** | ❌ **HAYIR** | Kaynakta `WebView` geçmiyor (0 eşleşme) | İleride gömülü tarayıcı gelirse gerekir |
| **Visual C++ Redistributable** | ❌ **HAYIR** | 253 dosyanın hiçbirinde `vcruntime140` / `msvcp140` importu **yok** | Native DLL'ler statik bağlı |
| **Universal C Runtime (UCRT)** | ⚠️ **İşletim sisteminde hazır** | `DepoWise.Desktop.exe` → `api-ms-win-crt-*.dll` | **Windows 10+ ile birlikte gelir**, ayrıca kurulmaz |
| Native DLL'ler | ✅ Pakette | `libSkiaSharp.dll`, `libHarfBuzzSharp.dll`, `av_libglesv2.dll` (ANGLE), `e_sqlite3.dll` | Zip içinde geliyor, kurulum gerektirmez |

### B.1 ⭐ EN ÖNEMLİ SONUÇ

**Bugün kurulması gereken HİÇBİR dış bağımlılık yok.**

Dolayısıyla klasik bir "Dependency Manager" **şu an var olmayan bir sorunu** çözer. Gerçek olan
şey bağımlılık değil, **sistem ön-koşulları**:

| Ön-koşul | Neden | Nasıl tespit edilir |
|---|---|---|
| Windows 10 (1607+) veya üzeri, x64 | .NET 8 asgarisi + UCRT'nin in-box olması | `Environment.OSVersion` / registry `CurrentBuild` |
| Mimari: x64 (ARM64'te emülasyon çalışır, x86 çalışmaz) | Paket `win-x64` | `RuntimeInformation.OSArchitecture` |
| Disk alanı ≈ 350 MB | 86 MB zip + 245 MB açılmış | `DriveInfo.AvailableFreeSpace` |
| Ağ erişimi (HTTPS) | Paket sunucudan iniyor | İlk istek |

**Tasarım kararı:** manifest tabanlı **çerçeve** kurulur (kullanıcının §7/§29 talebi), ama bugün
içi **ön-koşullarla** doldurulur; kurulabilir bağımlılık listesi bilinçli olarak **boş** başlar.
İleride WebView2 gibi bir ihtiyaç doğarsa **kod değil, manifest** güncellenir.

### B.2 Mirror / lisans notu

Bugün mirror'lanacak bir üçüncü taraf yükleyici olmadığı için **§9'daki lisans sorunu doğmuyor**.
İleride gerekirse: Microsoft'un yeniden dağıtılabilir bileşenleri (VC++ redist, WebView2 Evergreen
Bootstrapper) genelde yeniden dağıtıma izin verir, ancak **her biri kendi şartına göre ayrıca
değerlendirilmelidir** — varsayılan davranış **resmî URL**, mirror yalnız gerekirse ve onayla.

---

## C. ÖNERİLEN MİMARİ

Kullanıcının §5'teki modeli doğru; ampirik bulgulara göre sadeleştirilmiş hâli:

```
AlpnexSetup.exe (bootstrapper)
        │
        ├─(1) Ön-koşul kontrolü      OS · mimari · disk · ağ
        │       └─ eksikse → Ön-koşul ekranı (net mesaj + çözüm) → ÇIK
        │
        ├─(2) Manifest indir          GET /api/setup/manifest   (yeni uç)
        │       └─ yoksa → GERİ DÜŞ: GET /api/releases/latest  (mevcut uç)
        │
        ├─(3) Bağımlılık motoru       manifestteki liste (BUGÜN BOŞ)
        │       └─ eksik varsa → resmî URL → başarısızsa Alpnex mirror → başarısızsa MANUEL ekran
        │
        ├─(4) Paket indir             devam ettirilebilir + yeniden denemeli
        │
        ├─(5) DOĞRULA                 SHA-256 + boyut  ← FAIL-CLOSED
        │
        ├─(6) Kur                     staging → taşı → serverurl.txt → current.txt → kısayol
        │
        └─(7) Bitiş                   "Alpnex'i Başlat"
```

**Geriye dönük uyumluluk (kritik):** (2)'deki geri düşüş sayesinde **sunucuya manifest
yüklenmeden önce de** yeni Setup çalışır. Böylece Setup ve sunucu bağımsız yayınlanabilir.

### C.1 UI teknolojisi kararı — WinForms mı, Avalonia mı?

| | WinForms (mevcut) | Avalonia 12 (uygulamayla aynı) |
|---|---|---|
| Ölçülen/tahmini boyut | **69 MB** (ölçüldü) | Ölçülmeli — Avalonia+Skia yükü ~30 MB (sıkıştırılmamış), ama WinForms'un getirdiği **WindowsDesktop** çatısı düşer → **benzer bant** beklenir |
| Animasyon | Zayıf (timer + manuel çizim) | Güçlü (`Transitions`, `Animation`, stiller) |
| Tasarım dili | Ayrı | **Uygulamayla aynı** (Semi.Avalonia, Inter fontu, mevcut `Classes` stilleri) |
| Ekip bilgisi | Düşük (tek dosya) | **Yüksek** (tüm masaüstü Avalonia) |

**Öneri:** Avalonia'ya taşı — **ama Faz 1'de boyutu ÖLÇTÜKTEN sonra karar ver.** Boyut 85 MB'ı
aşarsa WinForms'ta kalıp görsel iyileştirme yapılır. "Avalonia daha ağır" varsayımı doğrulanmadı;
WinForms de kendi çatısını taşıyor.

---

## D. SUNUCU / MANIFEST MİMARİSİ

### D.1 Yeni uç: `GET /api/setup/manifest` (kimlik doğrulama YOK — kurulum girişten önce çalışır)

```jsonc
{
  "manifestVersion": 1,
  "generatedAt": 1757000000000,
  "setup":       { "version": "1.1.0", "url": "/api/setup/download", "sha256": "…", "sizeBytes": 0 },
  "application": { "version": "1.0.171", "url": "/api/releases/1.0.171/download",
                   "sha256": "…", "sizeBytes": 90547562, "minSupportedVersion": "0.0.0" },
  "requirements": [
    { "id": "os",   "minBuild": 14393, "label": "Windows 10 (1607) veya üzeri" },
    { "id": "arch", "allowed": ["X64", "Arm64"], "label": "64-bit Windows" },
    { "id": "disk", "requiredBytes": 367001600, "label": "≈350 MB boş alan" }
  ],
  "dependencies": []          // BUGÜN BOŞ — gelecekteki bileşenler buraya
}
```

`dependencies[]` şeması (kullanıcının §7 modeli, gerçek yapıya uyarlanmış):

```jsonc
{
  "id": "webview2", "name": "Microsoft Edge WebView2", "required": true, "order": 10,
  "detect": { "method": "registry", "path": "HKLM\\SOFTWARE\\WOW6432Node\\Microsoft\\EdgeUpdate\\Clients\\{GUID}", "value": "pv", "minVersion": "1.0.0" },
  "officialUrl": "https://…", "fallbackUrl": "/api/setup/deps/webview2",
  "sha256": "…", "installerType": "exe", "silentArgs": "/silent /install",
  "requiresAdmin": false, "arch": "x64"
}
```

**Kilit ilke:** yeni bileşen eklemek = **manifeste satır eklemek**. Setup kodu değişmez.
`detect.method` sabit bir kümeyle sınırlıdır (`registry` · `file` · `command`) — manifestten
serbest komut çalıştırılmaz (uzaktan kod çalıştırma yolu açılmasın).

### D.2 Manifest üretimi

Manifest **elle yazılmaz**; `app_releases` tablosundan üretilir (checksum zaten orada ve 64-hane
zorunlu). Böylece manifest ile gerçek paket **ayrışamaz**.

### D.3 Yayın adımı (tasarlandı, UYGULANMADI)

Sunucu tarafı değişiklik yayın gerektirir → **`YAYINLA` yetkisi olmadan yapılmayacak.**
Geri düşüş (C.1) sayesinde Setup, manifest yayınlanmadan da çalışır.

---

## E. GÜVENLİK MODELİ

| Kontrol | Bugün | Hedef |
|---|---|---|
| HTTPS zorunluluğu | ❌ yok | ✅ `https` dışı şema **reddedilir** |
| Host allowlist | ❌ yok | ✅ yalnız gömülü `ServerUrl` host'u + manifestteki resmî URL'ler |
| SHA-256 | ❌ **doğrulanmıyor** | ✅ **fail-closed** (`UpdateService.RequireVerifiedPackage` deseniyle birebir) |
| Boyut kontrolü | ❌ yok | ✅ `sizeBytes` eşleşmeli |
| Zip-slip | ✅ var | ✅ korunur |
| Paket bütünlüğü | ❌ yok | ✅ zip içinde `DepoWise.Desktop.exe` yoksa kurulum yok |
| Authenticode | ⚠️ `signed` alanı var, kullanılmıyor | 🔵 Sertifika alınırsa: `WinVerifyTrust` + yayıncı adı gösterimi (roadmap) |
| Manifest güveni | — | ✅ HTTPS + şema doğrulama + bilinmeyen alanları yok say |
| Log gizliliği | — | ✅ Log **yalnız**: sürüm, URL host'u, boyut, hata kodu. **Parola/jeton/bağlantı dizesi ASLA** |

**Fail-closed ilkesi:** checksum yoksa/uyuşmuyorsa **kurulum yapılmaz**. Sunucu 64-hane hex'i
zorunlu kıldığı için bu değişiklik **mevcut hiçbir sürümü bozmaz** (doğrulandı).

---

## F. UI / UX TASARIMI

### F.1 Yapı — sihirbaz değil, tek pencere

Modern kurulumcular (VS Code, Slack, Tailscale, JetBrains Toolbox) **Next/Next/Finish'i terk etti**.
Hedef akış: **aç → ilerleme → bitti**. Kurulum klasörü "Gelişmiş" altına gizlenir.

Pencere: **sabit ~640×480**, yeniden boyutlandırılamaz, büyütülemez, ortalanmış.

### F.2 Ekranlar

```
1) AÇILIŞ (≈900 ms)          Alpnex logosu, yumuşak fade+scale → ana ekrana geçer

2) HAZIRLIK
   ALPNEX                                          sürüm 1.0.171
   Sisteminiz kontrol ediliyor…
     ✓ Windows 11 (derleme 26200)
     ✓ 64-bit
     ✓ 12,4 GB boş alan
     ✓ Sunucuya erişim
                                        [ Gelişmiş ]      [ Kur ]

3) İLERLEME  (belirsiz "marquee" YOK — gerçek yüzde)
   Alpnex kuruluyor
   ██████████████░░░░░░  %72
   İndiriliyor…  62 / 86 MB  ·  4,1 MB/sn  ·  ~6 sn
                                                      [ İptal ]

   Aşama etiketi sırayla: İndiriliyor… → Doğrulanıyor… → Kuruluyor… → Son kontroller…

4) HATA (en çok atlanan, güveni en çok etkileyen ekran)
   Kurulum tamamlanamadı
   İndirilen dosya doğrulanamadı. Bu genellikle bağlantının
   yarıda kesilmesinden olur. Bilgisayarınızda hiçbir değişiklik yapılmadı.
     [ Yeniden Dene ]   [ Ayrıntılar ▾ ]   [ Kapat ]
   Ayrıntılar → hata kodu + log yolu (kopyalanabilir)

5) BİTTİ
   ALPNEX HAZIR              Sürüm 1.0.171 kuruldu.
                                        [ Alpnex'i Başlat ]
```

**Kasıtlı olarak YAPILMAYACAKLAR:** anket, bülten, ek yazılım teklifi, zorunlu yeniden başlatma,
ikinci bir sözleşme ekranı.

**Güven sinyalleri (ucuz, etkili):** ilk ekranda yayıncı adı + kurulacak sürüm + hedef klasör.
Kod imzalama alınırsa sertifika sahibi de gösterilir.

**Erişilebilirlik:** Enter = birincil eylem, Esc = onaylı iptal, gerçek sekme sırası, görünür odak
halkası. İptal dürüst olmalı: ya gerçekten geri alır ya "hiçbir değişiklik yapılmadı" der.

### F.3 Tasarım sistemi

Ayrı bir `Tokens.axaml` (`ResourceDictionary`): marka rampası, nötr rampa, semantik renkler
(başarı/uyarı/hata), 4-8-12-16-24-32 boşluk skalası, 3 kademeli tipografi, 2 köşe yarıçapı.
**Her görünüm yalnız `{DynamicResource}` kullanır, gömülü hex YOK.**

İkonlar: 6–10 glif yeterli (onay, uyarı, hata, klasör, kalkan, chevron, kapat). **NuGet paketi
eklemeden**, SVG `d` verisi tek bir `Icons.axaml` içine `StreamGeometry` olarak gömülür — tek
dosya kurulumcuda boyut ve lisans yüzeyi açılmaz. Kaynak: Lucide (ISC) veya Material Design
Icons (Apache-2.0); `THIRD-PARTY-NOTICES`'a tek satır atıf.

---

## G. ANİMASYON PLANI

`alpnex-arayuz-hareket` becerisine uygun, **bütçesi bilinçli olarak küçük**:

| Yer | Hareket | Süre |
|---|---|---|
| Açılış | logo fade + hafif scale (0.96→1.0) | ≤ 900 ms, atlanabilir |
| Ekran geçişi | çapraz solma | 180 ms ease-out |
| İlerleme çubuğu | değere **yumuşak interpolasyon** (zıplamaz) | 150 ms |
| Ön-koşul ✓ işaretleri | sırayla 60 ms gecikmeli görünme | toplam < 400 ms |
| Hata ekranı | animasyon **YOK** — anında görünür | 0 |

**Yasak:** belirsiz "marquee" ilerleme (ucuz yazılım sinyali #1), dönen logo, parçacık efekti,
ilerleme çubuğunda parlama animasyonu. **smooth > flashy.**

---

## H. MCP / SKILL DEĞERLENDİRMESİ

### H.1 Mevcut durum doğru — değişiklik gerekmiyor

| Araç | Durum | Karar |
|---|---|---|
| Serena | ✅ açık, salt-okuma (15 araç, yazma/kabuk yok) | **Koru** |
| `frontend-design` | ✅ açık | **Koru** — bu iş için doğrudan uygun |
| `alpnex-arayuz-hareket` | ✅ açık | **Koru** |
| Playwright | ⛔ kapalı | **Kapalı kalsın** — masaüstü penceresini göremez, tarayıcı yok |
| Context7 | ⛔ kapalı | **Kapalı kalsın** (§22/§23 talimatı) — ama gerekçe düzeltildi, bkz. H.3 |

### H.2 ⭐ Zaten sahip olunan, fark edilmemiş yetenek

Bu oturumda **`design` eklentisi yüklü**: `design-critique`, `design-system`, `design-handoff`,
`ux-copy`, `accessibility-review`. Yani "tasarım sistemi üretme" ve "tasarım eleştirisi" için
**yeni bir şey kurmaya gerek yok**. (Eklentinin Figma/Notion/Slack MCP'leri OAuth ister ve bu iş
için gereksiz — **kapalı bırakılmalı**.)

### H.3 Context7 — önceki raporun DÜZELTMESİ

Kullanıcının talimatıyla (§23) CVE iddiası birincil kaynaklardan doğrulandı. **Önceki rapor
üç noktada yanlıştı:**

1. **CVSS 9.0 eksik bilgi.** Puanı NVD değil, CVE'yi kaydeden **VulnCheck** verdi; aynı kuruluşun
   CVSS 4.0 puanı **6.4 (orta)**. NVD durumu hâlâ "Received" — analiz etmemiş.
2. **"Yama belirsiz" yanlış.** Açık **2026-02-23'te sunucu tarafında kapatıldı** (üretici + açığı
   bulan Noma Security doğruluyor). CVE'deki "2.1.2 ve öncesi" bir istemci yaması değil, **düzeltme
   tarihinin işareti**.
3. **Mimari akıl yürütme tersti.** Açık Upstash'in **sunucusundaydı** → düzeltme de sunucuda.
   Yapılandırdığımız **uzak uç, yamalı olan taraftır**; yerel npx istemcisi daha güvenli değildir.

Ayrıca önceki raporun "herkese açık depoda gerçek kimlik bilgisi var" gerekçesi de **yanlıştı**:
`.env.test.local` **hiç commit edilmemiş**, `.gitignore`'da, git geçmişinde yok (doğrulandı).

**Kalan gerçek risk:** saldırganın Context7'ye **sahte kütüphane kaydedip** kullanıcının onu
sorgulaması gerekiyor. Tanınmış kütüphanelerde (MudBlazor, Avalonia, Npgsql) yol yok.
**Karar:** §22/§23 gereği **açılmadı**; açma kararı kullanıcıya bırakıldı.

### H.4 Araştırılan ve REDDEDİLEN araçlar

| Araç | Neden reddedildi |
|---|---|
| coolors-mcp / color-palette-mcp / design-token-bridge-mcp | 3–40 yıldızlı hobi projeleri; çıktıları **CSS/Tailwind** — XAML'de kullanılamaz. Renk rampası/tip skalası zaten sıfır maliyetle hesaplanabiliyor. |
| color-scheme-mcp | Marka renklerinizi **üçüncü taraf API'ye** gönderiyor; karşılığı basit aritmetik |
| Zafiro.Avalonia.Mcp | 4 yıldız, taze v2.0 kırılması; **uygulamanın içine** teşhis/uzaktan-kontrol yüzeyi derletiyor — imzalı bir kurulumcuda kabul edilemez |
| AvaloniaUI.MCP (decriptor) | Resmî ücretsiz sunucuyu tekrarlıyor + **.NET 9 SDK** istiyor |
| Avalonia DevTools MCP | Teknik olarak en iyisi, ama **€299/yıl/kişi**; ücretsiz katman artık ticari kullanıma kapalı |

### H.5 Değerlendirmeye değer bulunan tek MCP

**Avalonia Build MCP** — `https://docs-mcp.avaloniaui.net/mcp` (uç **doğrulandı: HTTP 200**)

| | |
|---|---|
| Sahibi / ücret | Avalonia resmî · **ücretsiz**, kayıt/anahtar yok |
| Ne yapar | Güncel Avalonia 12 dokümanı + API araması (5 araç) |
| Neden değerli | Eğitim verisinden gelen **eski/yanlış XAML**'i keser — elle installer XAML'i yazarken gerçek bir hata kaynağı |
| Bağlam maliyeti | Düşük (~5 araç şeması; gecikmeli yükleme ile oturum başı ~120 token) |
| Risk | **Uzak sunucu** — sorgular Avalonia'nın sunucusuna gider (kaynak kod değil, doküman sorgusu) |
| Çakışma | Serena ile yok (o **sizin** kodunuzu okur, bu Avalonia'nın **dokümanını**) |

**Öneri:** Avalonia'ya taşınmaya karar verilirse **Faz 7'de geçici olarak** açılsın, iş bitince
kapansın. Kalıcı açık tutmaya gerek yok. *Uzak sorgu istenmiyorsa alternatif:* aracın
`get_avalonia_expert_rules` çıktısı **bir kez** alınıp proje kuralı olarak kaydedilir, sunucu hiç
bağlı tutulmaz — değerin çoğu sıfır kalıcı maliyetle elde edilir.

### H.6 Görsel geri bildirim boşluğu — MCP değil, betik

Yerleşik tarayıcı araçları **masaüstü penceresini göremez** (tarayıcı/DOM yok). Ama Claude yerel
PNG **okuyabiliyor**. Yani eksik halka sadece "pencereyi PNG'ye çeken bir şey":

- **Faz 1–6:** ~15 satırlık PowerShell ekran yakalama betiği (scratchpad'e PNG) → Claude okur ve
  `design-critique` ile eleştirir. **Kalıcı maliyet sıfır, MCP yok.**
- **Faz 7 sonrası:** Avalonia'ya taşındıysa `RenderTargetBitmap` ile hata/ilerleme gibi durumlar
  **yeniden üretmeye gerek kalmadan** yakalanabilir.

### H.7 Kod değil, kütüphane önerisi

**`Irihi.Ursa` 2.2.0** — MIT, Avalonia **12.0.2** bağımlılığı (12.0.4 ile uyumlu, doğrulandı),
159K indirme, **Semi.Avalonia ile aynı ekip** (`irihitech`), `Irihi.Ursa.Themes.Semi` ile doğrudan
eşleşiyor. **MCP değil, NuGet paketi → bağlam maliyeti sıfır.**

⚠️ **Ama kurulumcuya EKLENMEMELİ:** tek dosya self-contained yayınlandığı için her NuGet
referansı kullanıcının güvenmeden önce indirdiği boyutu şişirir. Ana uygulamada değerlendirilsin;
kurulumcuda 2-3 kontrol için elle XAML yazmak daha doğru.

### H.8 Görev bazlı otomatik açma/kapama (§24)

**Araştırıldı: Claude Code'da desteklenen mekanizma `.claude/settings.local.json` içindeki
`enabledMcpjsonServers` / `disabledMcpjsonServers` listeleridir ve değişiklik oturum yeniden
başlatması gerektirir.** Görev tipine göre **otomatik** açıp kapatan bir mekanizma **YOK** —
uydurulmayacak (kullanıcı talimatı). Bu yüzden öneri: kalıcı olarak yalnız Serena açık; tasarım
MCP'si gerekirse **elle** açılıp iş bitince kapatılır. Beceriler (`frontend-design`,
`alpnex-arayuz-hareket`, `design-*`) zaten **çağrıldıklarında** yüklenir → sürekli maliyet yok.

---

## I. RİSKLER

| # | Risk | Olasılık | Etki | Azaltma |
|---|---|---|---|---|
| R1 | Checksum fail-closed yapılınca eski/bozuk kayıtlı bir sürüm kurulamaz hale gelir | **Düşük** | Yüksek | Sunucu yayında 64-hane hex'i **zaten zorunlu kılıyor** (doğrulandı) → mevcut kayıtlar geçerli |
| R2 | Avalonia'ya taşıma bootstrapper'ı büyütür | Orta | Orta | **Faz 1'de ölç**, 85 MB eşiği aşılırsa WinForms'ta kal |
| R3 | Yeni `/api/setup/manifest` ucu yayınlanmadan Setup dağıtılırsa çalışmaz | Orta | Yüksek | **Geri düşüş zorunlu** (C.1): manifest yoksa `/api/releases/latest` kullanılır |
| R4 | `current.txt` yazımı güncelleme akışını bozar | Düşük | Yüksek | `UpdateInstaller` ile **aynı yolu ve biçimi** kullan; izole testle doğrula |
| R5 | Kurulumcu testleri gerçek indirme gerektirir | Yüksek | Düşük | HTTP katmanı arayüzle soyutlanır, testlerde sahte sunucu; **gerçek sunucuya yazma yok** |
| R6 | Tasarım işi kapsamı büyütür, mevcut sistemleri etkiler | Orta | Orta | Kapsam **yalnız `src/DepoWise.Setup/`**; masaüstü/web/API'ye dokunulmaz (tek istisna: S10 için csproj'a yayın sabitleme, ayrı onayla) |
| R7 | Kod imzalama sertifikası yok → SmartScreen uyarısı | **Yüksek** | Orta | Bu işin kapsamı dışında; roadmap'e alındı. Yayıncı adı + sürüm gösterimi kısmi güven sağlar |

---

## J. UYGULAMA AŞAMALARI

> Her faz **bağımsız derlenip test edilebilir**; her fazdan sonra durulabilir.

| Faz | İçerik | Dokunulan | Risk |
|---|---|---|---|
| **1** | **Ölçüm + iskele.** Avalonia bootstrapper prototipi yayınlanıp **boyut ölçülür** → UI teknolojisi kararı. Ekran yakalama betiği. | scratchpad | Yok |
| **2** | **🔴 S1 + S2 + S3 düzeltmesi (mevcut WinForms üzerinde).** Checksum fail-closed, boyut kontrolü, `current.txt` yazımı, HTTPS+host allowlist. **En yüksek değer, en düşük risk.** | `Setup/Program.cs` | Düşük |
| **3** | **Ön-koşul motoru** (OS/mimari/disk/ağ) + net hata ekranı | Setup | Düşük |
| **4** | **İndirme yöneticisi**: yeniden deneme, kaldığı yerden devam, iptal, gerçek ilerleme | Setup | Orta |
| **5** | **Manifest istemcisi** + geri düşüş (sunucu değişikliği YOK) | Setup | Düşük |
| **6** | **Bağımlılık motoru** (manifest tabanlı, bugün boş liste) + manuel kurulum ekranı | Setup | Orta |
| **7** | **Modern UI** (karar 1'e göre) + `Tokens.axaml` + `Icons.axaml` | Setup | Orta |
| **8** | **Animasyon** (G bölümü bütçesiyle) | Setup | Düşük |
| **9** | **Hata/log/kurtarma** — log dosyası (gizli veri YOK) | Setup | Düşük |
| **10** | **Testler** (§21 matrisi) + tam süit regresyonu | tests/ | Düşük |
| **11** | **Paketleme doğrulaması** + S10 (csproj yayın sabitleme, **ayrı onay**) | csproj | Düşük |
| **12** | *(YAYINLA yetkisi gerekir)* Sunucu manifest ucu + dağıtım | API | — |

**Öneri:** Faz 2 tek başına bile bugünkü en büyük güvenlik açığını kapatır ve ~86 MB gereksiz
indirmeyi yok eder. Tasarım işinden **önce** yapılmalı.

---

## PRODUCTION DURUMU

**PRODUCTION'A DOKUNULMADI.** Bu analiz boyunca: deploy yok, migration yok, sunucuya yükleme yok,
üretim veritabanına yazma yok. Yapılan tek ağ işlemi: `/api/setup/download` başlık kontrolü
(salt-okuma) ve Avalonia doküman ucunun erişilebilirlik kontrolü.
