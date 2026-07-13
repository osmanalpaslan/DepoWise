# Yarım Kalan İşler ve Testleri (Canlı Liste)

> **Bu dosya nedir?** "Yarıda kalan işlemler ne?" / "sırada ne var?" dediğinde bakılacak **tek liste**.
> Her işin **hangi aşamaları** kaldığını ve **hangi testlerin** yapılacağını gösterir. Teknik bilgi gerektirmez.
>
> **Nasıl güncel kalır?** Claude her anlamlı değişiklikten sonra bu dosyayı günceller (bir madde bitince
> "Tamamlananlar"a taşır, yeni iş çıkınca ekler). Özet burada; ayrıntı `docs/` ve `DEVAM.md`'de.
>
> Son güncelleme: **2026-07-12**

---

## A. Bekleyen İşler — BÜYÜK YETKİ/EKRAN PROMPTU (2026-07-12)

Kullanıcı ~16 maddelik büyük bir yetki+ekran revizyonu verdi. **Adım adım, test edilebilir dilimler**
halinde uygulanıyor (her dilim: build + ilgili test + commit + push). Motor: **Opus 4.8** (güvenlik/rol/tenant).

### ✅ Adım 1 — Yetki ağacı temeli (TAMAMLANDI, test 283/283, DEPLOY EDİLMEDİ)
- ✅ **Sync yetkisi kaldırıldı** (ölü madde; eşitleme cihaz-token bazlı, her kullanıcıda zaten aktif). Kullanıcı onayıyla.
- ✅ **Talep ikiye bölündü:** `requests` = **Talep Formu**, yeni `request_approval` = **Talep Onaylama**
  (ayrı ekran+yetki). Onay/ret artık `request_approval` Edit ister. `btn-approve` kaldırıldı + **Migration035**
  mevcut yetkileri yeni modüle taşıdı. Web+masaüstü onay butonu yeni yetkiye bağlandı (eski UI/servis mismatch giderildi).
- ✅ **Özel işlem yetkileri ağacın içinde** listeleniyor (PermMatrix tek-onaylı satırlar; web). Masaüstü zaten aynı panelde.
- ✅ **Eksik ekran denetimi:** tüm operasyonel ekranlar ağaçta; eksik yok (`company-permissions`/`developer`/`trash` gerekçeli hariç).
- 📄 Rapor: [docs/tests/Yetki_Agaci_Test_Report.md](docs/tests/Yetki_Agaci_Test_Report.md).

### ✅ Adım 2 — Yeni ara rol + delegasyon tavanı + süper-admin-only reorg (TAMAMLANDI, test 294/294, DEPLOY EDİLMEDİ)
- ✅ **"Kısıtlı Süper Admin"** rolü (admin ile süper admin arası); admin bypass'ı yok; yalnız süper admin atar (Migration036).
- ✅ Süper-admin-only ekranlar (Kota, Canlı Sunucu, Yedekler, Makine, Güncelleme, Firma Tanım) yalnız süper adminde;
  süper admin **Kısıtlı Süper Admin'e** devredebilir. **Kota İzleme** süper-admin-only oldu.
- ✅ **Delegasyon tavanı + ağaç görünürlüğü:** aktör yalnız kendi verebileceği yetkileri görür; veremeyeceği ağaçta yok.
- ✅ Firma Yetki Kontrol modeli **Serbest / Admin / Süper Admin** (Global kilit kaldırıldı; Migration037).
- ✅ Admin'e yükseltme uyarısında **sebep ekranlar madde madde** listeleniyor (web + masaüstü).
- 📄 Rapor: [docs/tests/Yetki_Rol_Delegasyon_Test_Report.md](docs/tests/Yetki_Rol_Delegasyon_Test_Report.md).

### ✅ Adım 3 — Firma Tanım: ayrı admin/personel kotası + makine kotası (TAMAMLANDI, test 298/298, DEPLOY EDİLMEDİ)
- ✅ `max_admins` (admin) + `max_users` (normal/personel) AYRI; **%20 admin kuralı kaldırıldı** (Migration038).
- ✅ **Makine kotası** (`machine_quota`) Firma Tanım ekranında (web + masaüstü). Kota enforcement + QuotaMonitor güncellendi.
- 📄 Rapor: [docs/tests/Firma_Tanim_Kota_Test_Report.md](docs/tests/Firma_Tanim_Kota_Test_Report.md).
### ✅ Adım 4 — Yetki Şablonu: firma seçimi + tüm firmalar + firma-bazlı görünürlük (TAMAMLANDI, test 302/302, DEPLOY EDİLMEDİ)
- ✅ `scope_all` kolonu (Migration039); şablon bir firmaya veya Tüm Firmalar'a. Ağaç seçilen firmanın admine açık modülleri.
- ✅ `ListForUserCreation`: kullanıcı-oluşturma yetkili aktör kendi firması + tüm-firma şablonlarını görür (tenant izolasyonu).
- ✅ Web firma seçici + kapsam sütunu; Users ekranı şablon listesi `for-user` (web + masaüstü).
- 📄 Rapor: [docs/tests/Yetki_Sablonu_Test_Report.md](docs/tests/Yetki_Sablonu_Test_Report.md).
### ✅ Adım 5 — Malzeme yeni-kayıt şablonu + şablon-dışı uyarı (TAMAMLANDI, test 307/307, DEPLOY EDİLMEDİ)
- ✅ `material_templates` tablosu + servis + modül + web yönetim ekranı (Malzeme menüsü); malzeme create'te şablon seçici.
- ✅ Görünürlük **oluşturana göre** (kullanıcı onayı): admin=global, diğerinin şablonu yalnız kendisine (araç dahil; Migration040).
- ✅ Şablon-dışı kayıtta uyarı ("Ana Yetkiliye Bilgi verilmelidir! Şablon dışı kayıt girmektesiniz!") — malzeme + araç, web + masaüstü.
- ⚠️ Masaüstü Malzeme Şablonları YÖNETİM ekranı (Avalonia) eklenmedi (web'den yönetilir); masaüstünde seçim+uyarı çalışır.
- 📄 Rapor: [docs/tests/Malzeme_Sablonu_Test_Report.md](docs/tests/Malzeme_Sablonu_Test_Report.md).
### ⏳ Adım 6 — Kullanıcı oluştururken **şube zorunlu** (süper admin hariç); şube yoksa engelle + uyarı.
### ⏳ Adım 7 — UI: **logo/tema renk uyumu** (web + masaüstü login).

### Açıklanan (işlem yapılmadı):
- **Fly.io ölçekleme:** personal/kullanım-bazlı hesapta makine/RAM/disk **üçü de ücretli**; bedava maksimum yok;
  disk küçültülemez (geri alınamaz maliyet). Kullanıcı kuralı gereği **hiçbir değişiklik yapılmadı**.

> ⏳ **DEPLOY EDİLMEMİŞ WEB DEĞİŞİKLİĞİ VAR** (Adım 1 web + eski B1/B2/B4): kullanıcı kararı = *sonraki web
> işiyle birlikte* deploy edilecek. Sonraki Web deploy'unda otomatik gider — unutma. **API değişikliği de var**
> (AppModules/RequestService/Migration035) → API'yi de deploy et.

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
