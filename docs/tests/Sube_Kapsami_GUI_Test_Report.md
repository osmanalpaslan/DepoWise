# Şube Kapsamı — Masaüstü GUI Test Raporu (CLAUDE.md §7.14)

**Tarih:** 2026-08-13 · **Kapsam:** masaüstü giriş akışı + ön muhasebe ekranları + Raporlar + Yetkiler
**Yöntem:** Windows UI Automation ile **gerçek kullanıcı etkileşimi** (yazma, tıklama, gerçek fare olayları, ekran görüntüsü)
**Ortam:** tamamen izole — yerel API (127.0.0.1:5099, SQLite) + `DEPOWISE_ENVIRONMENT=GuiTest` masaüstü veritabanı
**Üretim:** hiçbir bağlantı/işlem yapılmadı (bkz. "Üretim dokunulmazlığı")

Ayrıntılı 28 maddelik sonuç tablosu: [`Masaustu_GUI_Checklist.md`](Masaustu_GUI_Checklist.md)

---

## 1. Özet

| | |
|---|---|
| Koşturulan GUI maddesi | **22 / 28** |
| Geçen | **22** |
| Başarısız | **0** |
| Koşturulmayan | 6 (gerekçeleri checklist'te tek tek yazılı) |
| Bulunan gerçek ürün hatası | **6** (hepsi düzeltildi) |
| Eklenen regresyon testi | **15** (`BranchScopeDesktopTransferTests` 8 · `PartyLedgerBranchTests` 6 · `BranchScopeUiContractTests.U17` 1) |

## 2. Bulunan hatalar (öncelik sırası)

### 🔴 GUI-01 — Şube kapsamı masaüstünde FİİLEN YOKTU
- **Tekrar üretme:** `admin`e A+B kapsamı ata → masaüstünde giriş yap → şube listesinde `Sube C` görünüyor → seç → giriş **başarılı**, makine C'ye bağlanıyor.
- **Beklenen:** yalnız A ve B listelenir; C seçilemez.
- **Kök neden (iki katmanlı):**
  1. `RemoteUserBundle` firma/kullanıcı/rol/yetki taşıyor ama **`user_scopes` taşımıyordu** → yerel DB'de kapsam satırı hiç oluşmuyordu.
  2. `AuthService.Login` oturuma **`ScopeBranchIds`/`HomeBranchId` koymuyordu** (web/API `PermissionSnapshot.ToSession()` üzerinden alıyordu; masaüstü doğrudan `Login` çağırıyor).
  Sonuç: `BranchAccess.Allowed` bir sonraki basamağa düşüp admin'i **kısıtsız** sayıyordu.
- **Risk:** yetki modeli web'de uygulanıp masaüstünde uygulanmıyor (CLAUDE.md §5 ihlali). Veri sızıntısı olmadı çünkü servis kesişimi fail-closed; ama kullanıcı yetkisiz şubede çalışıyor gibi görünüyordu.
- **Düzeltme:** paket + import + `Login` + giriş listesi kırpması + **API tarafında `/api/auth/login` 403 kapısı** (UI güvenlik kapısı değildir).
- **Test:** `BranchScopeDesktopTransferTests` D1–D8.

### 🔴 GUI-02 — Elle cari hareketi ŞUBESİZ kaydediliyordu
- **Tekrar üretme:** Şube A oturumunda cari açılış hareketi gir → Şube B oturumuna geç → **aynı bakiye B'de de görünüyor**.
- **Kök neden:** `PartyLedgerService.Add` `BranchAccess.Resolve` çağırmıyordu; ne masaüstü ne web `BranchId` gönderiyordu → `branch_id = NULL`. Şubesiz satır okuma filtresinde bilerek herkese görünür (`OR branch_id IS NULL`).
- **Risk:** şube bazlı ön muhasebenin temel vaadi (hareketler şube bazlıdır) delinmişti; ekstre, bakiye ve **altı raporun tamamı** etkileniyordu.
- **Ek açık (GUI-02b):** `Reverse` karşı kaydı da şubesiz yazıyor ve **aslın şubesi için kapsam kontrolü yapmıyordu**.
- **Düzeltme:** `Add` artık `Resolve`'dan geçer (fatura/tahsilat ile aynı kapı); `Reverse` aslın şubesini taşır + `Require` uygular.
- **Test:** `PartyLedgerBranchTests` L1–L6.

### 🟠 GUI-03 — "Tüm yetkili şubeler" etiketi ile veri çelişiyordu
- Seçim temizlenince etiket A+B vaat ederken yalnız çalışma şubesi geliyordu (B'de 2200 yerine 700).
- **Kök neden:** seçici boşken `null` gönderiyordu; `Effective = İZİNLİ ∩ (İSTENEN ?? OTURUM ?? İZİNLİ)` `OTURUM` basamağına düşüyordu.
- **Düzeltme:** boş seçimde **yetkili şubelerin tamamı açıkça istenir** (masaüstü `BranchScopeSelector.Filter` + web `BranchPicker.Csv`). Formül değişmedi, kapsam genişlemedi.
- **Test:** `BranchScopeUiContractTests.U17`.

### 🟠 GUI-04 — Rapor şube filtresinde yetkisiz şube listeleniyordu
- **Düzeltme:** masaüstü `ReportsViewModel.LoadBranches` ve web'i besleyen `/api/reports/scope` artık `BranchAccess.Allowed` ile kırpar.

### 🟠 GUI-05 — "Şube Kapsamı" bölümü sessizce kayboluyordu
- Web'de oluşturulmuş kullanıcı seçilince panel hiç görünmüyordu; sebebi de hiçbir yerde yazmıyordu.
- **Kök neden:** kullanıcı listesi ve yetkiler SUNUCUDAN, kapsam ise YEREL DB'den okunuyordu. Masaüstünün yerel DB'sinde yalnız o makinede giriş yapmış kullanıcılar bulunur → `EnsureUserOwned` hata veriyor, hata `Status`'a yazılıp hemen ardından çalışan yetki yüklemesince **eziliyordu**.
- **Düzeltme:** kapsam da sunucudan okunur/kaydedilir (`OrgServerClient.GetBranchScopeAsync` / `SaveBranchScopeAsync`, yetkilerle aynı desen); çevrimdışıysa yerele düşer. Ayrıca hata artık panelde **görünür** (`ScopeError`) ve hata varken düzenleme açılmaz.

## 3. Doğrulanan davranışlar (hata bulunmayanlar)

Tarih doğruluğu (13.08.2026, gün kayması yok, bugünün kaydı raporda) · fatura numarası üretimi + KDV ·
açık fatura kapatma · ters kayıt gerekçe zorunluluğu ve kayıt silmeme · A/B/A+B tam toplam ·
cari + şube kesişimi · çoklu seçimin korunması (`_suppress`/`SyncPicks`) · G3 satır seçimi ·
çevrimdışı çalışma ve kapsamın genişlememesi · deny-by-default (buton yetkisi olmayan kullanıcıda şube seçici yok).

## 4. Performans (gözlem)

Ekran açılışları ve sorgular 3 sn altında yanıtladı; rapor sorguları anında döndü (izole veri kümesi küçük).
Ölçülebilir yük testi bu turun kapsamı değildir.

## 5. Coverage Matrix (CLAUDE.md §7.13)

| Form Açıldı | Yeni Kayıt | Düzenleme | Silme | Arama | Filtre | Grid | Doğrulamalar | Yetki | Hata Mesajları | Database | Offline | Sync | Performans | UI | UX | Security |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| ✅ | ✅ | ✅ (ters kayıt) | ⏸️ (fiziksel silme yok) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⏸️ | ⚪ | ✅ | ✅ | ✅ |

## 6. Üretim dokunulmazlığı

INSERT 0 · UPDATE 0 · DELETE 0 · DDL 0 · Migration 0 · Deploy 0 · Publish 0 · Paket 0 · **Üretim bağlantısı 0**.
`DEPOWISE_PG_URL` hiç tanımlanmadı. `src/DepoWise.Api/data/depowise-server.db` **açılmadı**.
`%LOCALAPPDATA%\Alpnex\Data\Development\alpnex.db` değişmedi (10.387.456 bayt · 12.08.2026 14:21 — tur başı ile aynı).
Ortak önbellek dosyaları (`lastuser/machine_status/machine_branch`) yedeklendi ve **md5 eşleşmesiyle** geri yüklendi.
`serverurl.txt` silindi → masaüstü yine üretim varsayılanına bakıyor.
