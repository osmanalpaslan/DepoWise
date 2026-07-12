# Yarım Kalan İşler ve Testleri (Canlı Liste)

> **Bu dosya nedir?** "Yarıda kalan işlemler ne?" / "sırada ne var?" dediğinde bakılacak **tek liste**.
> Her işin **hangi aşamaları** kaldığını ve **hangi testlerin** yapılacağını gösterir. Teknik bilgi gerektirmez.
>
> **Nasıl güncel kalır?** Claude her anlamlı değişiklikten sonra bu dosyayı günceller (bir madde bitince
> "Tamamlananlar"a taşır, yeni iş çıkınca ekler). Özet burada; ayrıntı `docs/` ve `DEVAM.md`'de.
>
> Son güncelleme: **2026-07-12**

---

## A. Bekleyen İşler (sıradaki hata listesi — son promttan 2026-07-12)

**Şu an bekleyen iş YOK.** Son promttaki 7 maddenin TAMAMI yapıldı, test edildi (273/273 yeşil) ve
**CANLIYA ALINDI:** API + Web deploy (health 200) · masaüstü **1.0.47** yayınlandı (sunucuda "en güncel"
doğrulandı) · git push edildi. Ayrıntı §C.

---

## B. Onay / Aksiyon Bekleyenler (senden)

- **Personel ekranını gözden geçir** (canlıda): artık **"Mevcut kullanıcıyı bağla"** (hesap açma yok; ADR-081) +
  **☐ Saha personeli** + **unvan listesi "+"**. Beğendin mi, değişiklik ister misin?
- **Masaüstü:** açık makineler **1.0.47** güncelleme uyarısı alır; güncelleyip yeni ekranları gör.
- **QA raporu (2026-07-12):** proje geneli tarama → [docs/tests/PROJECT_QA_Report.md](docs/tests/PROJECT_QA_Report.md).
  **4 küçük iyileştirmenin TAMAMI uygulandı** (B1 login boş-alan mesajı · B2 Audit/QuotaMonitor/Developer sayfa-içi
  yetki guard'ı · B3 Inspection + StockCount özel testleri, 8 yeni test · B4 build uyarıları CS8604/MUD0002 temizlendi).
  Test **281/281 yeşil**. ⏳ **DEPLOY EDİLMEMİŞ WEB DEĞİŞİKLİĞİ VAR** (B1/B2/B4-web): kullanıcı kararı = *bir sonraki
  web işiyle birlikte* deploy edilecek. Sonraki Web deploy'unda bu değişiklikler de otomatik gider — unutma.

---

## C. Bu Oturumda Tamamlananlar (2026-07-12)

### 2. prompt (ADR-076…082) — CANLIYA ALINDI (test 273/273; API+Web deploy, masaüstü 1.0.47)

> **Not:** Bu 7 ADR'nin **commit mesajları ADR-075…081** etiketli; DECISIONS.md'de doğru sıra **ADR-076…082**
> (075 numarası zaten "logo arka plan" kararına aitti — birer kaydırma).

- ✅ **ADR-076 — Silinen makine firması/şubesi girişe sunulmaz** (server `ReadDeviceInfo` join'lerine
  `is_deleted=0` + masaüstü: makine firması geçerli firma listesinde yoksa sayılmaz). 2 test.
- ✅ **ADR-077 — Makine yönetiminde FİRMA değiştirme** (web, süper admin): `AssignCompany` (şube ataması
  otomatik kalkar) + `POST /api/machines/{id}/company` + web sütunu. 1 test.
- ✅ **ADR-078 — Canlı sunucu ekranı: disk (canlı) + paket silme**: `ReleaseStore.GetDiskInfo/ListPackages/Delete`,
  `/api/server/status` disk alanları, `GET/DELETE /api/releases/packages`, web gauge + paket tablosu.
- ✅ **ADR-079 — Web logosu** masaüstünün temiz şeffaf logosuna (`app-icon.png`) eşitlendi, arka plan yok.
- ✅ **ADR-080 — İlk açılış tema varsayılanları**: Masaüstü Fluent/Koyu/Kehribar, Web Koyu/Yumuşak/Kehribar.
- ✅ **ADR-081 — Personel ekranı: hesap AÇMA yerine mevcut kullanıcıyı BAĞLAMA** (web + masaüstü):
  `ListLinkableUsers` + `POST /api/personnel/{id}/link-user`. 2 test.
- ✅ **ADR-082 — Firma yetki kontrol: süper admin DİNAMİK global kilidi açıp kapatabilir**
  (`SetGlobalLocks`/`IsGlobalRestricted`, global app_settings, enforcement + web toggle). 1 test.

### 1. prompt (2026-07-12, ADR-064…074) — canlıda

- ✅ **KRİTİK süper admin kilitlenme (ADR-064)** — firma silme süper admini pasife almaz + açılışta self-heal + regresyon testi. Canlı API redeploy edildi.
- ✅ **#6 NİHAİ: Fikir A — tek ekran + koşullar (ADR-067)** — web + masaüstü:
  - **Personel ekranında hesap açma** ("Uygulama erişimi ver" → kullanıcı adı/şifre/rol) + "Hesabı kaldır".
  - **☐ Saha personeli** kutucuğu; hesap yoksa/açılmıyorsa + kutucuk işaretsizse **uyarı penceresi** (işaretliyse çıkmaz).
  - **Unvan sabit tanım listesi + "+"** ile yeni tanım ekleme · mükerrer kişi uyarısı · bir personele tek hesap.
  - Kullanıcılar ekranındaki "Personel seç (bağla)" + PERSONEL sütunu ikinci yol olarak duruyor.
  - *(Kısa geçmiş: önce B (ayrı ekran) yapıldı, beğenilmedi → A'ya dönüldü, koşullar korundu.)*
- ✅ **Silinen şubeler her yerde listeleniyordu (ADR-066)** — kök neden: şubeler sunucu-otoriteli ama masaüstü
  yerel kopyası sunucudan yalnız **upsert** ediliyordu; silinenler yerelde kalıyordu. Artık her girişte sunucu
  şube listesi **aynalanır** (sunucuda olmayan yerel şube pasife alınır). Regresyon testi eklendi.
- ✅ **Firma silince 401 + firmalar yüklenmiyordu (ADR-068)** — süper admin, **içinde çalıştığı** firmayı silince
  token'daki firma geçersiz kalıyor, sonraki **her istek 401** dönüyordu (liste yüklenmiyor, ekranda silinmiş firma
  kalıyor, tekrar silme 401). Artık: firma **silinmişse** süper admin **home firmasına düşer** (oturum yaşar);
  firma **hiç yoksa** (sahte id) fail-closed korunur.
- ✅ **SİLMEDE WEB TAM OTORİTER (ADR-069)** — web'de silinen kayıt **makinelerin yerel DB'sinden de düşer**
  (silme artık LWW'yi aşar) **ve** sunucuda silinen kayıt **cihaz push'uyla diriltilemez**. Silme dışındaki
  LWW davranışı korundu. Unvan tanımları (`personnel_titles`) senkron listesine eklendi. 3 yeni test.
- ✅ **Masaüstü firma ekle/sil web ile eşitlenmiyordu (ADR-071 + ADR-072)** — kök neden: masaüstü Firma Tanım **yalnız yerel
  DB'ye** yazıyordu ve firmalar iş senkronunda hiç yoktu → sunucuya ulaşmıyordu. Artık **firmalar sunucu-otoriteli**
  ve **OFFLINE-FIRST kuyruk** (ADR-072): işlem önce **yerele** yazılır + **kuyruğa** alınır, internet gelince
  **sırayla** işlenir. Yeniden denemede **hata düşmez** (idempotent: aynı işlem tekrar gelirse mükerrer kayıt/hata yok).
  Eşitleme sırası: **1) firma → 2) sabit tanımlar/lookup → 3) iş kayıtları** (paralel değil, sırayla).
- ✅ **Kota İzleme "ONLINE" dedup (ADR-073)** — inceleme sonucu: sayım **zaten kullanıcı bazında tekildi**
  (ilk günden beri `userId` anahtarlı), aynı kişi iki platformdan girse **1** sayılıyordu; düzeltilecek hata yoktu.
  Yapılanlar: şart **4 testle sabitlendi** (regresyon) + gerçek bir kusur giderildi (eski kayıtlar sözlükten hiç
  silinmiyordu → bellek sızıntısı). **Not:** ekranda 2 gördüysen ya iki **farklı kullanıcı** online'dı ya da
  **"AKTİF"** sütunu (aktif kullanıcı sayısı) ile **"ONLINE"** karıştı — tekrarlarsa hangi kullanıcılarla olduğunu bildir.
- ✅ **Marka logoları eklendi (ADR-074)** — web + masaüstü. Tam logonun **opak beyaz zemini şeffaflaştırıldı**
  (flood-fill: kamyonun beyaz kabini/yol çizgileri korunarak), sembolden **7 boyutlu `.ico`** üretildi.
  **`.exe` simgesi hiç ayarlı değildi** (varsayılan .NET ikonu çıkıyordu) → düzeltildi. Favicon + giriş ekranları +
  üst bar/kenar çubuğu bağlandı. Kalite korundu (hiç büyütme yok, kayıpsız PNG).
- ⚠️ **KRİTİK OLAY — sunucu diski doldu (ADR-070):** `/data` (974 MB) %100 doldu → SQLite yazamadı →
  **login dahil tüm API 500** (tam kesinti). Sebep: her masaüstü paketi ~85 MB ve eski paketler hiç
  temizlenmiyordu (11 paket = 892 MB). Eski paketler silindi (disk %100 → %17) ve **otomatik saklama
  politikası** eklendi (en yeni 3 paket tutulur). Hafızaya kaydedildi.
- ✅ **CANLIYA ALINDI (12.07):** API + Web yayında (health 200). **Masaüstü 1.0.46 YAYINLANDI.**
  Yayın sırasında **süper admin canlı girişi doğrulandı** → ADR-064 tümüyle kapandı. Test **267/267**.

> Önceki oturumlarda tamamlananların tam listesi: `DEVAM.md` §2 ve `docs/DECISIONS.md` (ADR-062/063).
