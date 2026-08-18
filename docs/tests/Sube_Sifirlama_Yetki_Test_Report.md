# Test Raporu — Şube Yapısı · Sıfırlama · Yerel Veri Sıfırlama Yetkisi

Tarih: **2026-08-18** · Kapsam (CLAUDE.md §7.1): **yalnız değiştirilen ekranlar/akışlar**
Analiz: [`docs/ANALIZ_SUBE_VE_SIFIRLAMA.md`](../ANALIZ_SUBE_VE_SIFIRLAMA.md)

## Kapsam

| Ekran / akış | Platform | Neden kapsamda |
|---|---|---|
| Şube / Şantiye Tanım | Web + Masaüstü | ŞB-01/02/03/04/06 |
| Şube aynası (senkron) | Masaüstü | ŞB-01 |
| Firma İş Verisini Sıfırla | Web (sunucu) | SIF-03 |
| Yerel sıfırlama uygulaması (giriş akışı) | Masaüstü | SIF-01 |
| **Yerel Veri Sıfırlama** (YENİ ekran) | Web | YET |
| İçe aktarım oturum kopyası | Web + Masaüstü | ŞB-04 turunda bulunan kapsam açığı |

Genel regresyon **istenmedi**; ancak kapsam davranışı değiştiği için **tam takım** koşturuldu.

## Sonuç

| Ölçüm | Değer |
|---|---|
| Tam test takımı | **2018 toplam · 1983 geçti · 0 başarısız · 35 atlandı** |
| Atlananlar | Bilinen PostgreSQL testleri (`ApiTestHost` `DEPOWISE_PG_URL`'i süreç genelinde null'lar — mevcut/kasıtlı davranış) |
| Bu turda eklenen test | **43** |
| Derleme | Masaüstü · Web · API — **0 hata** |
| Bulunan gerçek hata | **9** (analizde 16 bulgu; 9'u kod düzeltmesi gerektirdi) |

### Eklenen test dosyaları

| Dosya | Adet | Neyi kilitler |
|---|---|---|
| `BusinessResetCoverageTests.cs` | 5 | Sıfırlamada firma/şube/kullanıcı/rol korunur · sıfırlama SONRASI giriş yapılabilir · senkron-dışı tablolar temizlenir · öksüz çocuklar temizlenir · başka firmaya dokunulmaz · SIF-01 çağrı yeri (kaynak düzeyi) |
| `BranchHierarchyTests.cs` | 11 | Giriş listesi üst şube+tür taşır · ayna yerele yazar · alt şube önce gelse de çalışır · bilinmeyen üste bağ kurulmaz · ayna tekrarında kaybolmaz · döngü (doğrudan/derin) reddedilir · geçerli taşıma kabul · alt şubesi olan silinemez · silinmiş üst listede görünmez |
| `BranchParentScopeTests.cs` | 11 | Geçişli kapanış · düz yapıda null · döngüye dayanıklılık · üst/ana şube genişlemesi · yukarı-genişleme YOK · rapor toplama · izinli kümeyi aşamama · ağaçsız fail-safe · yazma yolu · devir tavanı |
| `TemplateSyncTests.cs` | 4 | Şablonlar senkron listesinde · modül eşlemesi (yetki kapısı) · FK sırası · uçtan uca taşıma |
| `ExplicitOnlyModuleTests.cs` | 12 | Katalog+ekran kaydı · firma admini örtük ALAMAZ · süper admin erişir · açıkça verilen erişir · SA/KSA verebilir · alan aşağıya verebilir · açık izni olmayan admin veremez · devir tavanı aşılamaz · Rol Yetki Kontrol kapatabilir · normal modül regresyonu |

## Coverage Matrix (CLAUDE.md §7.13)

| Madde | Durum | Not |
|---|---|---|
| Form Açıldı | ✅ | Şube formu + yeni Yerel Veri Sıfırlama ekranı derlendi/render edildi |
| Yeni Kayıt | ✅ | Şube oluşturma (üst şube + tür ile) — `BranchHierarchyTests` |
| Düzenleme | ✅ | Üst şube değiştirme, döngü reddi, geçerli taşıma |
| Silme | ✅ | Alt şubesi olan silinemez; araç/personel kuralı korundu |
| Arama | ⏸️ | Bu turda değişmedi (şube listesi arama mantığına dokunulmadı) |
| Filtre | ✅ | Rapor şube filtresi ağaca uyar (`Effective`) |
| Grid | ⏸️ | Liste sorgusu değişti (JOIN koşulu) ama grid davranışı değişmedi |
| Doğrulamalar | ✅ | Ad zorunlu · kendi üstü olamaz · döngü · başka firmaya ait şube |
| Yetki | ✅ | 12 test — açık-verilir katman, devir zinciri, Rol Yetki Kontrol |
| Hata Mesajları | ✅ | "kendi alt şubelerinden birinin altına taşınamaz" · "alt şube/şantiye bulunmaktadır" · yetkisiz ekran uyarısı |
| Database | ✅ | Öksüz satır temizliği · tenant izolasyonu · iki geçişli FK-güvenli yazma |
| Offline | ✅ | **SIF-01'in özü:** sıfırlama sonrası çevrimdışı giriş korunur (R1 testi giriş yapıyor) |
| Sync | ✅ | Şablon senkronu uçtan uca · şube aynası tekrar koşumu |
| Performans | ✅ | Ağaç oturumda **bir kez** yüklenir; şube sayısı onlarca mertebesinde. `Expand` alt şube yoksa yeni liste ÜRETMEZ (tahsis yok) |
| UI | ✅ | Yeni ekran MudBlazor deseninde; mevcut yönetim ekranlarıyla aynı grup/görünüm |
| UX | ✅ | Ekranda "sunucudaki veriye dokunulmaz" ve "programı tamamen kapattırın" uyarıları açıkça yazılı |
| Security | ✅ | Fail-closed kapsam · tenant zorlaması (`TenantAccessGuard`) · admin bypass kapatma · içe aktarım kapsam açığı kapatıldı |

## Güvenlik testleri (§7.12)

| Senaryo | Sonuç |
|---|---|
| Kapsam dışı şube elle istenirse (parametre manipülasyonu) | **Reddedilir** — `Effective` kesişimi; `Genisletme_Izinli_Kumeyi_Asamaz` |
| Alt şubeye yetkili kullanıcı üst şubeyi görebilir mi | **Hayır** — `AltSubeye_Yetkili_UstSubeyi_Goremez` |
| Firma admini yeni yetkiyi kendiliğinden alır mı | **Hayır** — `FirmaAdmini_Ortuk_ALAMAZ` |
| Yetkisi olmayan kullanıcı devredebilir mi | **Hayır** — `Yetkisiz_Personel_Veremez` |
| Devreden kendisinde olmayanı verebilir mi | **Hayır** — `Devir_Tavani_Kendi_Yetkisini_Asamaz` |
| Süper admin olmayan biri başka firmanın makinelerini sıfırlayabilir mi | **Hayır** — `TenantAccessGuard` hedefi oturumdan çözer |
| Sıfırlama başka firmanın verisine dokunur mu | **Hayır** — `Sifirlama_BaskaFirmanin_Verisine_Dokunmaz` |

## Riskler / açık kalanlar

1. **SIF-02 (açık, backlog'da).** Yerel sıfırlama kontrolü **yalnız giriş anında** çalışır. Program açıkken
   sıfırlama yapılırsa o makine eski verisini göndermeye devam eder. **Operasyonel önlem zorunlu:**
   sıfırlamadan önce tüm programlar tamamen kapatılmalı.
2. **ŞB-04 bir yetki genişlemesidir.** Üst şubeye yetkili kullanıcı artık alt şubelere yazabilir ve
   onları devredebilir. Canlıdaki mevcut kapsamlar gözden geçirilmeli (`YTK-07`).
3. **Dağıtım yapılmadı.** API + Web deploy edilmedi, masaüstü sürümü paketlenmedi. SIF-01 düzeltmesi
   masaüstü sürümünde olduğu için **sıfırlama işleminden ÖNCE** dağıtım gerekir.
4. **Ekranlarda ağaç görünümü yok** (`ŞB-07`). Davranış ağaca uyuyor ama listeler hâlâ düz.
5. **Ağaç tazeliği.** Şube ağacı oturum kurulurken bir kez yüklenir. Web/API tarafında yetki fotoğrafı
   **90 sn TTL** ile tazelendiği için yeni açılan bir alt şube en geç 90 saniyede kapsama girer.
   **Masaüstünde** ise oturum boyunca sabittir → yeni alt şube, kullanıcı çıkıp yeniden girene kadar
   kapsama girmez. Bu, mevcut `ScopeBranchIds` davranışıyla **aynıdır** (şube kapsamı zaten oturumda
   dondurulmuş durumdaydı) — yeni bir gerileme değildir, bilinen sınırdır.

## Çalıştırılan senaryo sayısı

Tam takım **2018** test (bu turda eklenen **43** dahil). Hiçbir test gevşetilmedi, devre dışı bırakılmadı
veya retry ile geçirilmedi. `AppScreensParityTests` içindeki iki beklenti **kasıtlı olarak** güncellendi —
bu testler menü kataloğunu kilitler ve yeni ekran eklendiğinde güncellenmesi tasarımın parçasıdır.
