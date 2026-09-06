# ALPNEX — FAZ 4 FINAL QA RAPORU

**Tarih:** 2026-09-06 · **Kapsam:** FAZ 4.1–4.16 (kullanıcının 16 isteği) + tam regresyon
**Prompt:** `docs/project-control/FAZ_4_TEST_PROMPTU.md` (36 bölüm)
**Ortam:** İZOLE — `artifacts/f4-data` (API :5228) + web :5287 · **üretime hiç dokunulmadı**

---

## 1. Executive Summary

Kullanıcının 16 isteğinin tamamı uygulandı ve test edildi. **Tam regresyon 3729 testin 3681'i geçti,
0 başarısız, 48 atlandı** (atlananlar PostgreSQL bağlantısı gerektiren testlerdir — yerelde PG yok).

QA sırasında **4 gerçek kusur bulundu ve düzeltildi**; 3 bulgu bilinçli olarak kapsam dışı bırakılıp
raporlandı. Kritik (Critical) ve yüksek (High) seviyede açık kusur **yoktur**.

En önemli iki sonuç:

- **FAZ 4.1 (canlı veri hatası) kök nedeniyle kapatıldı.** Araç sayacı artık geçerli kayıtlardan
  türetilir; yanlış girilen sayaç düzeltilebiliyor. Bu, babanızın KAM-ME 059 aracında yaşadığı sorunun
  ta kendisiydi.
- **FAZ 4.4 uçtan uca kanıtlandı.** Gerçek bir senkron çakışması üretildi, kazanan/kaybeden gösterildi,
  üzerine yazılan sürüm geri getirildi ve kaydın gerçekten güncellendiği doğrulandı.

---

## 2. Test Environment

| Bileşen | Değer |
|---|---|
| API | `http://localhost:5228` (`api-f4`), veri dizini `artifacts/f4-data` |
| Web | `http://localhost:5287` (`web-f4`) |
| Masaüstü | `DEPOWISE_ENVIRONMENT=Faz4QA` → `%LOCALAPPDATA%\Alpnex\Data\Faz4QA\alpnex.db` |
| Veritabanı | SQLite (izole, tek kullanımlık) |
| Üretim | **HİÇ KULLANILMADI** — bağlantı açılmadı, sorgu atılmadı, deploy yapılmadı |
| Tohum parolası | Bu oturuma özel (`Faz4QA!…`) — canlı parola kullanılmadı |

**Test verisi (§8, deterministik):** `QA-A Ltd` + `QA-B Ltd` firmaları, her birinde bir şube;
`superadmin`, `qa001-admin`, `qa002-normal`, `qa003-kisitli`, `qa004-yetkisiz`, `qa005-bfirma`.
Kayıtlar `QA-ARAC-<n>`, `QA-NEG-<n>` gibi ayırt edilebilir adlarla üretildi.

---

## 3. Kullanılan Automation / MCP / Tools

- **Birim/entegrasyon:** `scripts/run_tests.ps1` (sistem geneli kilit + gerçek derleme çıkış kodu).
- **API:** Node tabanlı 38 kontrollü batarya (`qa_api.js`) — kimlik, tenant, yetki, negatif senaryolar.
- **Senkron çakışması:** `qa_conflict.js` — masaüstünün gönderdiği paketin **aynısı** kullanıldı.
- **Web:** yerleşik tarayıcı araçları (gerçek tıklama + DOM okuma).
- **Masaüstü:** Windows UI Automation (PowerShell).
- **MCP:** Context7 ve Playwright **kapalı tutuldu** (CLAUDE.md §7.5 gereği).

---

## 4. Web Coverage

| Alan | Sonuç |
|---|---|
| Giriş → parola değiştirme → şube seçimi → panel | ✅ gerçek tıklamalarla |
| Sistem Logu (`/audit`) — gün gruplaması, "NE DEĞİŞTİ" sütunu | ✅ |
| Kayıt geçmişi penceresi (per-record) | ✅ alan bazlı fark listesi |
| Senkron Çakışmaları (`/sync-conflicts`) | ✅ boş durum + gerçek çakışma |
| "Üzerine Yazılanı Kazanan Yap" | ✅ kayıt gerçekten güncellendi |
| Araç/Malzeme kartındaki "Geçmiş" düğmesi | ✅ (kod + yetki kapısı) |

---

## 5. Desktop Coverage

| Alan | Sonuç |
|---|---|
| İzole ortamda açılış + sunucu girişi + makine/şube kurulumu | ✅ |
| **FAZ 4.11** — girişten sonra TAM EKRAN | ✅ `MainWindow` 1920×1009 |
| **FAZ 4.4** — "Senkron Çakışmaları" menü öğesi + yetki kapısı | ✅ görünür (admin) |
| **FAZ 4.12** — üst barda senkron göstergesi | ✅ "✓ Senkron" + "Eşitle" |
| Yeni pencerelerin İÇERİĞİ (görsel) | ⚠️ **doğrulanamadı** — makinenin etkileşimli masaüstü oturumu kilitli; ekran görüntüsü boş dönüyor ve sentetik klavye girdisi reddediliyor. **Ortam kısıtı, ürün bulgusu değil.** |

Pencerelerin doğruluğu şu üç yolla dolaylı olarak güvence altındadır: (1) derleme — Avalonia **derlenmiş
bağlamalar** (`x:DataType`) sayesinde hatalı bağlama derlemede patlar; (2) aynı içeriği besleyen
servislerin birim testleri (21 test); (3) **birebir aynı** içeriğin web'de uçtan uca doğrulanması.

---

## 6. API / Backend Coverage

38/38 kontrol geçti:

- Tokensiz ve geçersiz token ile 4 uç → **401**
- Firma sınırı: QA-A, QA-B verisini okuyamıyor (liste + tekil + log)
- `/api/audit/record` yetki kapısı (btn-screen-log + ekran View)
- `/api/sync/conflicts` yeni `sync_conflicts` kapısı; yetkisiz → **403**
- `promote-loser` yetkisiz → reddedildi
- `/api/vehicles/{id}/meter/recalc` çalışıyor; yetkisizde 403
- Günlük faaliyet: tarih aralığı + çoklu araç süzgeci; **SQL enjeksiyonu denemesi etkisiz**
- `/api/lookup-plus` aç/kapat; yetkisiz değiştiremiyor
- `/api/me/list-columns/*` yaz/oku; **kullanıcıya özel** (başkasına sızmıyor)

---

## 7. Database / Data Integrity

- Mükerrer iç kod ve mükerrer plaka **reddedildi** (gerçek çakışma denemesiyle görüldü).
- 🔴 **Bulgu ve düzeltme:** araç kaydı açarken **negatif sayaç** kabul ediliyordu (`-5000`).
  Kapatıldı; sıfır geçerli kaldı. Regresyon testi `SY9`.
- Çakışma çözümünde `id`/`company_id`/`version` **geri yazılmıyor** (CK11 ile kilitli) — kaydın başka
  firmaya taşınması ya da sürüm numarasının geri gitmesi imkânsız.
- Migration094 yalnız `ADD COLUMN`; backfill/UPDATE/DELETE **yok**.

---

## 8. Authentication / Authorization

- Kimlik doğrulama kapıları çalışıyor (401'ler).
- Giriş hız sınırlayıcı (rate limit) tetiklendi ve **beklendiği gibi** engelledi.
- ⚠️ **Bilinen orta seviye bulgu:** `mustChangePassword` yalnız arayüzde zorlanıyor; doğrudan API
  kullanan bir istemci parolasını değiştirmeden çalışabiliyor. **Kimlik doğrulama atlatması değil**
  (geçerli parola gerekiyor). Sunucuda zorlamak mevcut istemcilerin erişimini daraltacağı için
  **kullanıcı kararına bırakıldı** (ADR-226).

---

## 9. Tenant / Company Isolation

Sızıntı **yok**. QA-A yöneticisi QA-B'nin aracını okuyamıyor, listesinde göremiyor, kaydının logunu
açamıyor; QA-B'nin çakışmasını göremiyor/çözemiyor. Log ad çözümlemesi bile `company_id` ile sınırlı.

---

## 10. Role / Permission

- Üç yeni yetki deny-by-default: `btn-template-free-create`, `btn-link-user`, `btn-conflict-resolve`.
- Yeni ekran modülü `sync_conflicts` yetki ağacında; **UI ve API aynı kapıyı** uyguluyor.
- Çakışma çözümünde **iki kapı**: ekran View + düğme yetkisi (CK5 her iki yönü de test ediyor).

---

## 11. Field Permission

FAZ 3b/3c alan yetkisi mimarisi **değiştirilmedi**. FAZ 4.3 log görüntüleri hassas sütunları hiç
almıyor (`AuditFields.Gizli`) ve ham görüntü API yanıtına konmuyor (`JsonIgnore`) — testle kilitli
(LG3 + API bataryası "parola özeti yok" kontrolleri).

---

## 12. Stock / Transfer

Bu fazda stok mantığına dokunulmadı. İlgili süitler tam regresyonda geçti.
🔴 FAZ 4.1 sırasında **FAZ 3c'den kalan bir regresyon** bulundu ve düzeltildi: mal kabulde siparişte
yazılı fiyat, fiyatı göremeyen kullanıcıda `null`'lanıyordu (sessiz veri kaybı). Regresyon testi eklendi.

---

## 13. Offline / Sync

- Gerçek çakışma üretildi (`business-push` ile) → tespit, kazanan/kaybeden, alan farkı, geri getirme.
- Geri getirme `version+1` ve `updated_at=şimdi` ile yayılıyor → **özel senkron yolu açılmadı** (CK4).
- 🔴 **PostgreSQL bulgusu (kod okumasıyla):** görüntü değerleri metin saklanıyor; PG `bigint` kolona
  metin kabul etmez → üretimde çalışmazdı. Hedef kolon türüne göre bağlama eklendi. **SQLite bunu
  affettiği için testler yakalayamazdı.**

---

## 14. Reports / Export

Bu fazda rapor motoruna dokunulmadı; ilgili süitler tam regresyonda geçti.

---

## 15. 10k+ Performance

**Yapılmadı — bilinçli.** Bu fazın değişiklikleri liste sorgu planlarını değiştirmiyor. Tek performans
riski FAZ 4.3'ün her audit yazımına eklediği PK üzerinden `SELECT`'tir; bu, sınırlandırılmış tasarımla
karşılandı: log listesinde satır başına sorgu YOK, **tablo başına tek** sorgu var ve "öncesi" araması
60 ek sorguyla sınırlı. 3729 testlik tam koşu 31 dk sürdü (önceki koşularla aynı mertebede).

---

## 16. Visual QA

Web tarafında gerçek ekran görüntüleri alındı (log ekranı, çakışma ekranı). **Masaüstü görsel QA
yapılamadı** — makinenin etkileşimli oturumu kilitli (§5'teki not).

---

## 17. Accessibility

Yeni ekranlarda metinler seçilebilir (`SelectableTextBlock`), düğmelerin metin etiketleri var, kritik
bilgi yalnız renge bağlı değil (kazanan/kaybeden **yazıyla** belirtiliyor).

---

## 18. Bugs Found

| # | Önem | Bulgu | Nerede bulundu |
|---|---|---|---|
| 1 | **High** | Araç kaydında negatif sayaç kabul ediliyordu | API bataryası + log ekranı |
| 2 | **Medium** | Web'de "Kazanan Yap" yanlış onay metni gösteriyordu ("Kaydı iptal et…") | Web E2E |
| 3 | **Medium** | Kayıt logunda bağlantı alanları 32 haneli kimlik gösteriyordu | Web E2E |
| 4 | **Medium** | PostgreSQL'de çakışma geri yazımı tür hatası verirdi | Kod okuması |
| 5 | Low | `Durum: active`, `app_setting` gibi teknik değerler Türkçeleştirilmemişti | Web E2E |
| 6 | Low | Eksik zorunlu sorgu parametresi 400 yerine **500** dönüyor | API bataryası |
| 7 | Low | `mustChangePassword` sunucuda zorlanmıyor | Web E2E |
| 8 | Low | Test altyapısı: paralel koşuda `ClearAllPools()` yarışı | Tam regresyon |

---

## 19. Bugs Fixed

**1, 2, 3, 4, 5** düzeltildi (ayrıntı: ADR-226). Ayrıca FAZ 4 uygulaması sırasında bulunan
**FAZ 3c mal kabul fiyat regresyonu** düzeltildi ve testi eklendi.

---

## 20. Remaining Issues

**6 — 500 yerine 400:** ortak hata katmanı `BadHttpRequestException`'ı 400'e çevirmiyor. Son kullanıcı
etkisi yok (arayüz parametreyi daima gönderiyor). Düzeltmesi ortak hata katmanını değiştirir → ayrı iş.

**7 — `mustChangePassword` sunucuda zorlanmıyor:** düzeltmek mevcut istemcilerin erişimini daraltır →
**kullanıcı onayı gerekir** (geliştirme protokolü §10).

**8 — test altyapısı yarışı:** 175 test sınıfını ilgilendirir; ürün hatası değildir → ayrı iş.

---

## 21. Flaky Tests

Bir koşuda `VehicleGridTests.SearchGridAll` `ObjectDisposedException` ile düştü; **tek başına
çalıştırıldığında geçti** ve sonraki iki tam koşuda tekrar etmedi. Kök neden #8'dir.
**Retry ile gizlenmedi** — kaydedildi (§28).

---

## 22. Full Regression Result

```
Toplam 3729 · Geçti 3681 · BAŞARISIZ 0 · Atlandı 48 · Süre 31 dk 17 sn
```

Atlanan 48 test PostgreSQL bağlantısı ister (yerelde PG yok) — bu testler **her zaman** bu ortamda
atlanır, yeni bir durum değildir.

**Yol boyunca güncellenen 8 nöbetçi test** (hepsi gerekçesiyle, hiçbiri gevşetilmeden):
`FuelUpdateTests.YD2` (⭐ karar değişikliği), `PermissionScreenUxTests U3/U4`,
`TedarikciHizliEklemeTests TDR1/TDR6`, `MasaustuTasarimPaketiTests TSR2`,
`AppScreensParityTests S4/S14/S16`, `MenuIkonTests MIK1`, `MenuRenkTests RNK12`.

---

## 23. Coverage Matrix

| Boyut | Durum |
|---|---|
| Form açıldı · Yeni kayıt · Düzenleme · Silme/İptal | ✅ |
| Arama · Filtre · Grid | ✅ (tarih aralığı, çoklu araç, plaka) |
| Doğrulamalar | ✅ (negatif sayaç, mükerrer kod/plaka) |
| Yetki | ✅ (modül + düğme + iki kapı) |
| Hata mesajları | ✅ (açık metin, sessiz başarısızlık yok) |
| Database | ✅ (transaction, audit, migration) |
| Offline / Sync | ✅ (gerçek çakışma senaryosu) |
| Performans | ⚠️ ölçülmedi (gerekçesi §15) |
| UI | ✅ web · ⚠️ masaüstü görsel yapılamadı |
| UX | ✅ (yanlış onay metni bulundu ve düzeltildi) |
| Security | ✅ (tenant, yetki, SQL enjeksiyonu, XSS, parola sızıntısı) |

---

## 24. Final Acceptance Decision

| Kriter | Durum |
|---|---|
| Critical bug | **0** |
| High severity bug | **0** (bulunan 1 tanesi düzeltildi) |
| Tenant isolation problemi | **0** |
| Authorization bypass | **0** |
| Field permission leak | **0** |
| Kritik data integrity problemi | **0** (bulunan 1 tanesi düzeltildi) |
| Production riski | **0** (üretime hiç dokunulmadı) |
| Kritik sync data loss | **0** |
| Kritik regression | **0** |

Bilinen **orta/düşük** seviye açık maddeler: §20'de listelenen 3 madde + §21'deki test altyapısı yarışı.

---

### FINAL QA PASSED WITH KNOWN LOW/MEDIUM ISSUES

Kabul kriterlerinin tamamı (kritik/yüksek sıfır) sağlanmıştır. "PASSED" yerine bu ifade seçilmiştir,
çünkü §20'de **bilerek düzeltilmemiş** üç madde vardır: biri kullanıcı kararı gerektiriyor
(`mustChangePassword`), ikisi kapsam dışı ayrı iş (500/400 hata kodu, test altyapısı yarışı).
Bunların hiçbiri son kullanıcıyı etkileyen bir kusur değildir.

**Yayın için engel yoktur.**
