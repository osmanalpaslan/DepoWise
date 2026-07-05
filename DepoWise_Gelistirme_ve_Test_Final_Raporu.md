# DepoWise — Geliştirme ve Test Final Raporu

Tarih: 05.07.2026 · Bu rapor ikinci bir Claude hesabının bağımsız analizi için hazırlanmıştır. Amaç: yapılan tüm değişiklikleri, testleri ve bilinen riskleri tek bir belgede toplamak; incelenecek noktaları ve şüpheli/eksik alanları açıkça işaretlemek.

## 0. İkinci analiz için okuma notu

Aşağıdaki her değişiklik "ne yapıldı + neden + nasıl doğrulandı + hangi riskler açık" formatında verildi. Bir denetçi olarak özellikle "Açık riskler / şüpheli noktalar" ve "Bağımsız doğrulama önerileri" başlıklarına odaklanılması önerilir. Kod değişiklikleri kaynak dosyalara **yazıldı ve derlendi**, ancak **canlıya deploy edilmedi** ve **git'e commit edilmedi** (kullanıcı commit'i kendi makinesinde yapacak — sandbox `.git`'i yönetemedi).

Proje: .NET 8 · Sunucu = ASP.NET Minimal API + SQLite (tek Fly makinesi) · Masaüstü = Avalonia/MVVM · Web admin = Blazor Server + MudBlazor. Hedef: 200-300 kullanıcı, sadece adminler web'e, tüm kullanıcılar masaüstünden sunucuya login.

## 1. Test ortamı ve doğrulama yöntemi

Derleme/test, izole bir Linux sandbox'ta .NET SDK 8.0.128 ile yapıldı (NuGet paketleri kullanıcının yerel önbelleğinden offline çözüldü — internet erişimi kısıtlı). Sonuçlar:

- API, Web, Desktop ve Test projeleri **Release** derlendi: 0 hata.
- **Tam test suit: 238/238 test geçti** (regresyon yok). Yeni eklenen 7 test dahil.
- Canlı web (depowise-web.fly.dev) superadmin oturumuyla manuel gezildi (18 ekran).

Önemli ortam kısıtı: sandbox'ın Windows klasörünü bağladığı dosya sistemi (virtiofs) bazı düzenlenmiş dosyaları kesik/eski gösterdi; bu dosyalar derleme kopyasına elle tam içerikle yeniden yazılarak doğrulandı. Bu, **kaynak dosyaların doğruluğunu etkilemez** (host'taki gerçek dosyalar tamdır) ama ikinci analizde dosya bütünlüğünün bir kez daha gözden geçirilmesi önerilir.

## 2. İlk turda uygulanan güvenlik/hata düzeltmeleri

**K1 — JWT anahtarı üretimde zorunlu.** `src/DepoWise.Api/Program.cs`: `DEPOWISE_JWT_KEY` yoksa Development dışında uygulama açılmıyor (eskiden bilinen bir dev anahtarına düşüyordu → herkes süper admin token'ı üretebilirdi). *Açık iş:* Fly'da `DEPOWISE_JWT_KEY` secret'ı set edilmeli, yoksa deploy sonrası API başlamaz (bilinçli davranış).

**K2 — Seed admin şifreleri.** `src/DepoWise.Api/ServerServices.cs`: `admin/admin123` ve `superadmin/superadmin` sabitleri kaldırıldı. Şifre env'den (`DEPOWISE_SEED_ADMIN_PASSWORD`, `DEPOWISE_SEED_SUPERADMIN_PASSWORD`) veya yoksa kriptografik rastgele üretilip **bir kez** konsola yazılıyor. *Açık iş:* canlıdaki mevcut superadmin/admin şifreleri hâlâ eski; elle değiştirilmeli (bu kod yalnız yeni kurulumları etkiler).

**K3 — Login rate limit.** Mevcut ama bağlanmamış `RateLimiter` `/api/auth/login` ve `/api/auth/sync-login` uçlarına IP bazlı bağlandı (30 istek/5 dk; NAT arkası ofisler için gevşek). *Şüpheli nokta:* limiter süreç-içi bellekte; tek Fly makinesinde sorun değil ama ölçeklenince paylaşılmaz.

**Y1 — reset-data üretimde kapalı.** Tüm firmaların iş verisini silen `/api/admin/reset-data` üretimde `DEPOWISE_ALLOW_RESET=1` olmadan 403.

**Y2 — 500 sızıntısı.** Global handler artık ham exception mesajını client'a döndürmüyor, jenerik mesaj + sunucu loguna yazıyor. *Açık iş:* yapısal loglama (ILogger) hâlâ yok; sadece Console.Error.

**K5 — Web F5/doğrudan URL bug'ı.** `MainLayout.razor`: oturum tarayıcı deposundan yüklenene kadar (`Auth.Loaded`) sayfa gövdesi render edilmiyor (spinner). Eskiden sayfalar token'sız API çağırıp yanlış "kayıt yok"/"yetkisiz" gösteriyordu. `Home.razor` uyarı widget'ı da ilk-render bağımlılığından kurtarıldı (sonsuz spinner düzeltildi).

**Y8 — Form autofill.** `Users.razor`: kullanıcı ve (dolaylı) şube formlarında `autocomplete=new-password` → tarayıcı artık kayıtlı superadmin kimliğini forma doldurmuyor.

## 3. İkinci turda eklenen geliştirmeler (kullanıcı onaylı)

### 3.1 business-push yetki + içerik doğrulaması (Y3)
Dosyalar: `src/DepoWise.Infrastructure/Sync/BusinessSyncService.cs`, `src/DepoWise.Api/Program.cs`.

- Her iş tablosu bir yetki modülüne eşlendi (TableModule). Yeni `Apply(SessionContext, payload)` overload'ı: kullanıcı ilgili modülde Create **veya** Edit yetkisine sahip değilse o tablonun tüm satırları uygulanmaz (Admin/SüperAdmin tam yetkili). Böylece en yetkisiz kullanıcının JWT'siyle tüm firma tablolarını ezmesi engellendi.
- Satır içerik doğrulaması: `stock_balances`, `stock_movements`, `fuel_*`, `materials` gibi tablolarda negatif miktar/tutar reddediliyor (sayı ve sayısal-string toleranslı).
- Eski `Apply(companyId, payload)` overload'ı testler için korundu.
- Testler: 3 yeni (yetkisiz modül atlanır / admin tam yazar / negatif bakiye reddedilir) + mevcut 6 = **9/9 geçti**.

**Açık riskler / şüpheli noktalar:**
- Yetkisiz tablo "sessizce atlanıyor" (hata değil). Masaüstü kullanıcısına "verinin bir kısmı gönderilemedi" sinyali gitmiyor — kullanıcı verisinin sunucuda göründüğünü sanabilir. İkinci analiz: bu davranış kabul edilebilir mi, yoksa kısmi-push kullanıcıya bildirilmeli mi?
- Doğrulama alan adları sabit listeyle (heuristik) eşleşiyor; şema alan adları farklıysa negatif kontrol atlanır. Şemadaki gerçek miktar/tutar kolonları ile bu liste karşılaştırılmalı.
- company_id satır-içi kontrolü kaldırıldı (UpsertRow zaten oturumdan zorluyor); bu bilinçli ama denetlenmeli.

### 3.2 JWT yenileme / kayan oturum (Y5)
Dosyalar: `src/DepoWise.Api/JwtTokens.cs`, `Program.cs`, `src/DepoWise.Desktop/ServerAuthClient.cs`, `BusinessSyncPushService.cs`.

- Sunucu: `POST /api/auth/refresh` (RequireAuthorization) → geçerli token'la aynı kullanıcı/firma için taze token; yetkiler token'dan değil DB'den yeniden kurulur.
- `JwtTokens.ExpiryHours=12` sabiti + `ReadExpiry` (doğrulamasız exp okuma).
- Masaüstü: token exp'i saklanıyor; `EnsureFreshTokenAsync` süreye <2 saat kalınca yeniliyor; 401'de `SessionExpired=true` (UI tekrar-girişe yönlendirebilir). `PushAsync` push öncesi yeniliyor.
- Testler: 4 yeni JwtToken testi (claim+süre, doğrulama, farklı-anahtar reddi, yenileme kimliği korur) — **geçti**.

**Açık riskler / şüpheli noktalar:**
- `SessionExpired` bayrağı üretildi ama **UI'da henüz tüketilmiyor** — kullanıcıya "tekrar giriş yapın" penceresi bağlanması gerekli (şu an sadece sinyal var). İkinci analiz: LoginWindow/Shell'e bu bayrağın bağlanması önerilir.
- Refresh, süresi dolmuş token'ı yenileyemez (tasarım gereği). Uygulama 12 saatten uzun kapalı/uykuda kalıp sonra ilk push denerse 401 alır → SessionExpired. Yani "en az 10 saatte bir aktif push" varsayımı var; periyodik push aralığı bu pencereye göre doğrulanmalı.
- Refresh endpoint'i HTTP düzeyinde (running server) test edilmedi; yalnız JwtTokens birim testleri var.

### 3.3 Updater yedek + rollback + bütünlük guard'ı (Y4)
Dosya: `src/DepoWise.Desktop/UpdateInstaller.cs`.

- Kurulum öncesi paket ana exe (`DepoWise.Desktop.exe`) içermiyorsa kurulum hiç başlatılmıyor (bütünlük guard).
- PowerShell yardımcısı: önce mevcut kurulumu `%LocalAppData%\DepoWise\backup`'a yedekler; yedek alınamazsa güncellemeyi başlatmaz. staging→install kopyalaması başarısızsa (robocopy≥8) yedekten geri alır ve **sürümü yazmaz** (bozuk/yarım güncelleme kalıcı olmaz). Yalnız başarıda current.txt yazılır. Checksum kontrolü korunuyor.

**Açık riskler / şüpheli noktalar:**
- Gerçek PowerShell yolu **Linux sandbox'ta çalıştırılamadı** → yalnızca derleme + kod incelemesi ile doğrulandı. Windows'ta gerçek bir başarısız-kopya senaryosuyla rollback'in çalıştığı **manuel/entegrasyon testiyle** doğrulanmalı. (Senkron model `UpdateService.ApplyUpdate` rollback'i mevcut testlerde kapsanıyor, ama gerçek yol ayrı.)
- Rollback `robocopy /E` ile yapılıyor; başarısız kopyanın eklediği YENİ fazla dosyaları silmiyor (yalnız eski dosyaları geri yazıyor). `/MIR` daha temiz olurdu ama kurulum dizininde risklidir; bilinçli olarak /E seçildi. Denetlenmeli.
- Checksum boşsa (release imzasız) doğrulama atlanıyor; imza zorunluluğu hâlâ yok (paket imzalama ayrı bir iş olarak açık).

## 4. Manuel canlı web testi bulguları (deploy öncesi durum)

18 ekran menüden gezildi; formlar/listeler çalışıyor. Tanım ekranında ekle/sil CRUD canlı doğrulandı. Bakım ve Uyarılar ekranları gerçek test verisiyle doğru çalışıyor. Konsolda JS hatası yok. Deploy öncesi tespit edilen (ve kodda düzeltilen) canlı hatalar: F5/doğrudan URL (K5), form autofill (Y8), ana ekran uyarı spinner'ı. Kozmetik açık: server-status bellek grafiği barları hep %100 (normalize hatası).

## 5. Hâlâ açık / bu turda dokunulmayan konular (ikinci analiz için öncelik adayları)

1. **Sync tek yönlü + LWW.** En büyük mimari açık: masaüstü iş verisini sunucudan **geri çekmiyor** (yalnız 8 tanım tablosu pull ediliyor); bir makinedeki stok hareketi diğerinde görünmüyor. `stock_balances` LWW satırı olarak taşınıyor → aynı firmada 2. makine bakiyeyi ezebilir. Bu turda business-push **güvenliği** sertleştirildi ama **çift-yönlü birleşme** yapılmadı. Çok makineli kullanım öncesi çözülmeli.
2. **PostgreSQL geçişi + yedekleme.** Tek Fly makinesi + SQLite; 200-300 kullanıcıda yazma darboğazı ve tek-nokta-arıza riski. Volume yedek/snapshot planı doğrulanmalı.
3. **Yapısal loglama (ILogger) yok.** ~40 boş catch bloğu gözlemlenebilirliği düşürüyor.
4. **`SessionExpired` UI'ya bağlanmadı** (3.2'de belirtildi).
5. **Web E2E testi yok** (K5 bu yüzden erken yakalanamamıştı).
6. **Eksik ekranlar:** `soon/about`, `soon/trash` placeholder; TrashService API ucu yok.
7. **`/api/machines/register` anonim**, `serverurl.txt` düz metin/kullanıcı-yazılabilir, CORS `AllowAnyOrigin`, 1 GB istek gövdesi tüm uçlarda — sertleştirme adayları.

## 6. Bağımsız doğrulama önerileri (ikinci Claude'a)

- **Dosya bütünlüğü:** virtiofs kesme sorunu nedeniyle değiştirilen 6 kaynak + 2 test dosyasının host'taki tam ve derlenebilir olduğunu bir kez daha `dotnet build` ile teyit et.
- **business-push doğrulama listesi:** `NonNegativeFields` içindeki alan adlarını gerçek DB şemasındaki miktar/tutar kolonlarıyla karşılaştır; kaçan alan var mı?
- **Yetki eşlemesi:** `TableModule` sözlüğündeki her tablo→modül eşlemesinin `AppModules` anahtarlarıyla ve masaüstü yetki modeliyle tutarlı olduğunu doğrula.
- **JWT refresh penceresi:** periyodik push aralığı ile 12 saat/2 saat marj uyumlu mu? Uygulama uzun uykudan sonra doğru şekilde SessionExpired'e düşüp UI'ya yansıyor mu (UI bağlantısı eklenince)?
- **Updater rollback:** Windows'ta staging→install kopyasını yapay olarak kilitleyip (dosya açık tutarak) rollback'in eski sürümü geri getirdiğini ve current.txt'in yazılmadığını doğrula.
- **Deploy sırası:** `DEPOWISE_JWT_KEY` set edilmeden deploy edilirse API açılmayacak — deploy prosedürünün bunu içerdiğini teyit et.

## 7. Değişen dosyalar (özet)

Kod: `Program.cs`, `ServerServices.cs`, `JwtTokens.cs` (Api); `BusinessSyncService.cs` (Infrastructure); `ServerAuthClient.cs`, `BusinessSyncPushService.cs`, `UpdateInstaller.cs` (Desktop); `MainLayout.razor`, `Home.razor`, `Users.razor` (Web).
Test: `BusinessSyncTests.cs` (+3), `JwtTokenTests.cs` (+4, yeni), `DepoWise.Tests.csproj` (Api referansı eklendi).
Belge: `DECISIONS.md` (ADR 051-055), `KNOWN_ISSUES.md`, `TEST_EVIDENCE.md`, `PROJECT_STATE.md`.

Durum: Tümü Release derlendi, 238/238 test geçti. Deploy ve git commit kullanıcı tarafında yapılacak.
