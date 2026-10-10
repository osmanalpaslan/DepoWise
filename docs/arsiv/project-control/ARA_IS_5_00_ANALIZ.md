# ARA İŞ 5 — EKİP + HİYERARŞİ + ONAY

> Tarih: **2026-08-30** · Aşama: **AŞAMA 3 (roadmap'in kalan maddesi)**
> Durum: **FAZ 0 ✅ · FAZ 1 ✅ · FAZ 2 ✅ KARARLAR KESİNLEŞTİ (ADR-187) · FAZ 3 ⏸️ BAŞLATILMADI — "UYGULAMA BAŞLASIN" onayı bekleniyor**
> ⛔ **KOD YOK · MIGRATION084 YOK · TEST YOK · PRODUCTION'A BAĞLANILMADI (SELECT dahil) · DEPLOY YOK · COMMIT YOK**
> Bu belge ARA İŞ 3 · FIN-B1 · ARA İŞ 4'ü **açmaz**; ana roadmap sırası **değişmez**.

---

## FAZ 0 — Durum doğrulama

| Kontrol | Beklenen | Bulunan | Sonuç |
|---|---|---|---|
| HEAD | `492b14c` | **`492b14c`** | ✅ |
| origin/master | senkron | senkron | ✅ |
| Çalışma ağacı | temiz | temiz (yalnız kullanıcının 2 takip dışı dosyası) | ✅ |
| Migration azamisi | 083 | **`Migration083_CustomReports`** | ✅ |
| Migration084+ | yok | **0 dosya** | ✅ |
| Son ADR | ADR-186 | **ADR-186** | ✅ |
| Aktif ara iş | yok | yok | ✅ |
| Yayın havuzu | boş | boş | ✅ |
| Sıradaki iş | ARA İŞ 5 | `CURRENT_PHASE.md` satır 139 ile doğrulandı | ✅ |

⚠️ **Canlı şema 83 ve masaüstü 1.0.165** bilgisi ARA İŞ 4'ün yayın kaydından alınmıştır (ADR-186
yayın kaydı). **Bu turda production'a bağlanılmadı**, bu iki değer yeniden ölçülmedi.

---

## FAZ 1 — Mevcut sistem ve teknik analiz (dosya:satır kanıtlı)

### Kullanıcı / rol / yetki

- `users` temel şeması: `Migration001_CoreSchema.cs:57-66`. Sonradan eklenen sütunlar:
  `branch_id` (`Migration014_UserBranch.cs:19`) · `can_view_all_branches`
  (`Migration026_UserViewAllBranches.cs:18`) · **`personnel_id`**
  (`Migration033_UserPersonnelLink.cs:20`, benzersiz kısmi indeks satır 21) ·
  `must_change_password` (M042) · `special_code_hash` (M044).
- **`manager_id` / `parent_user_id` / `is_manager` YOK** (arama sonucu: her biri 0).
- Yetki: `user_permissions.module_key` **serbest metindir**
  (`Migration001_CoreSchema.cs:83`; benzersizlik `(user_id, module_key)` satır 93) → rapor/kayıt
  başına **dinamik yetki anahtarı migration'sız** mümkündür. Bu, ADR-186'da Custom Rapor için
  fiilen uygulanmış ve yayınlanmıştır.
- Ekran/yetki ağacı **statiktir**: `AppScreens.Sections / Groups / All`.

### Mevcut ekip altyapısı

**YOKTUR.** Şemada ekip varlığı bulunmamaktadır. Aramada çıkan `from_team_stock`
(`Migration059_MaintenanceTeamStock.cs:24`) bir **bakım stok bayrağıdır**; ekip varlığı değildir.
⇒ Ekip için **yeni tablo gerekir**.

### Mevcut hiyerarşi altyapısı

**Kullanıcı/personel hiyerarşisi YOKTUR.** Şemadaki tek self-reference'lar:
`branches.parent_id` (`Migration001_CoreSchema.cs:31,39`) ve
`material_categories.parent_id` (`Migration005_Materials.cs:24,28`) — ikisi de personel/ekip
hiyerarşisi **değildir**. ⇒ Hiyerarşi için **yeni yapı gerekir**.

### Mevcut onay altyapısı — VAR, ancak TEK modülde ve TEK adımlı

- Tablo: `material_requests` (`Migration010_Requests.cs:19-35`) — `approver_id` ·
  `status` (`draft|pending|approved|rejected|cancelled`) · `approved_by` · `approved_at`.
- Geçmiş: `request_status_history` (`Migration010_Requests.cs:51-58`) — `from_status` ·
  `to_status` · `by_user` · **`reason`**.
- Yetki modülü: **`request_approval`** (`RequestService.cs:65`; `AppModules.cs:70`). Talep formu
  Edit yetkisi **YETMEZ** (`RequestService.cs:254`).
- Aktör kapısı: `EnsureIsDesignatedApprover` (`RequestService.cs:277-300`) — talebi **yalnız formda
  seçilen "Onay Veren"** onaylar/reddeder · **firma admini ve süper admin istisnadır** · onay veren
  seçilmemişse **veya** personelin bağlı kullanıcı hesabı yoksa **eski davranış korunur**
  (geriye uyumluluk, `RequestService.cs:297`).
- **Onaycı PERSONEL'dir**; kullanıcı hesabına `users.personnel_id` ile çözülür
  (`RequestService.cs:284-288`).
- **Ret gerekçesi zorunludur** (`RequestService.cs:263`).
- **İş Emri ve Satın Alma'da onay katmanı YOKTUR** — `PurchaseOrderService.cs:390` açıkça
  *"otomatik — teknik sonuç, onay katmanı değil"* der. Bu ayrım korunacaktır.
- **Bildirim altyapısı hazırdır**: bekleyen talep `AlertKind.Request` olarak Uyarılar'a düşer
  (`DashboardService.cs:155`).

### Senkron / offline analizi

`BusinessSyncService.Tables` üyeliği (doğrudan sayım):

| Tablo | Senkronda | Anlamı |
|---|---|---|
| `personnel` | ✅ | Personel masaüstünde **çevrimdışı mevcuttur** |
| `material_requests` | ✅ | Talepler taşınır |
| `request_status_history` | ✅ | Onay geçmişi taşınır |
| `users` | ❌ | **Firmanın kullanıcı listesi masaüstünde YOKTUR** |
| `roles` | ❌ | — |
| `user_permissions` | ❌ | — |

**Sunucu-otoriteli yapılandırma aynası mevcuttur:** `/api/lookups/sync`
(`Program.cs:1569-1614`) — `branches` (**`parent_id` dâhil**) · `screenVisibility` ·
`menuLayoutScreens` · `menuLayoutGroups` taşır. Kod bu kanalın gerekçesini açıkça yazar:
*"bunlar iş verisi değil, SUNUCU OTORİTELİ YAPILANDIRMADIR — masaüstü bunları asla yazmaz,
çakışma/LWW sorusu doğmaz"* (`Program.cs:1600-1610`). Ekip/hiyerarşi için **olası emsaldir**
(karar noktası PK-EK-02).

**SNK-05 bağlayıcıdır:** onaylarda LWW **yasak**; online **ilk geçerli onay kazanır**.
Kilitler: `FinalStabilizasyonTests.FIN9_Snk05_Online_IlkOnay_Kazanir` ve
`FIN10_Snk05_Offline_LWW_Sozlesmesi`.

### Güvenlik ve BranchAccess

Mevcut kapılar: modül yetkisi (deny-by-default) · ayrı `request_approval` modülü · belirlenen
onaycı kapısı · admin istisnası · tenant (`company_id`) · `BranchAccess`. Yeni tabloların tamamı
`company_id` taşımalıdır. **`CompanyId` hiçbir yeni DTO'da istemciden alınmayacaktır**
(ADR-186 deseni: firma daima oturumdan çözülür).

### Eski istemci uyumluluğu

ARA İŞ 4'te **gerçek testlerle** kanıtlanan davranış geçerlidir: alıcının tanımadığı senkron
tablosu **sessizce atlanır** · istisna atılmaz · geçerli satırlar rollback olmaz · yerelde tablo
oluşturulmaz (`CustomRaporSenkronOnDogrulamaTests` ESK-01…05 ve `CustomRaporTests` CR33).
Mekanizma: `BusinessSyncService.ApplyCore` **alıcının kendi** `Tables` dizisini gezer (satır 915)
ve `TableExists` ikinci kapıdır (satır 919). ⇒ Yeni tablolar eski istemcide **görünmez** kalır.

⚠️ Bu turda **yeni test yazılmadı/çalıştırılmadı**; yalnız mevcut kanıt kullanılmıştır.

### Performans riskleri (yapısal — production ölçümü YAPILMADI)

Hiyerarşik zincir çözümlemede çok adımlı/recursive sorgu · onay zinciri kurarken N+1 riski ·
ekip üyeliği büyürse senkron paket artışı · "sıradaki onaycı ben miyim" süzgecinin Uyarılar
üretiminde tekrarlanması.
**Production ölçümü gerektiren maddeler FAZ 3 / yayın öncesine bırakılmıştır.**

---


## FAZ 2 — KESİNLEŞEN KARARLAR (2026-08-30, ADR-187)

> **FAZ 2 TAMAMLANDI.** Aşağıdaki 17 madde kullanıcı tarafından **açıkça seçilmiştir** ve
> bağlayıcıdır. Bunlar artık "öneri" değil **kesinleşmiş karardır**.
> ⛔ **FAZ 3 BAŞLAMADI.**

### Ana kararlar (PK-EK-01…07)

| PK | Konu | **KESİN KARAR** |
|---|---|---|
| **PK-EK-01** | Onay zinciri kapsamı | **C — Malzeme Talebi + Satın Alma** (İş Emri **kapsam dışı**) |
| **PK-EK-02** | Hiyerarşi tabanı | **B — Kullanıcı tabanlı + `/api/lookups/sync` aynası** |
| **PK-EK-03** | Zincir saklama | **B — Ayrı `approval_instance` / `approval_step`** |
| **PK-EK-04** | Zincir anlık görüntüsü | **A — Süreç başlarken dondurulur (snapshot)** |
| **PK-EK-05** | Çevrimdışı onay | **A — Yalnız çevrimiçi** |
| **PK-EK-06** | Fazlama | **A — 3 alt faz: (1) Ekip tanımı → (2) Onay zinciri motoru → (3) Onaylamalarım** |
| **PK-EK-07** | Ekip yetkisi | **B — Mevcut Kullanıcılar (`users`) modülüne bağlanır** (yeni `teams` modülü YOK) |

### İş kuralları (1…10)

| # | Kural | **KESİN KARAR** |
|---|---|---|
| 1 | Çoklu ekip üyeliği | **Evet** — model **çoka-çok** üyeliği desteklemelidir |
| 2 | Hiyerarşi derinliği | **N seviye, N = 4** (sınırsız hiyerarşi yok) |
| 3 | Zincir zorunluluğu | **Opsiyonel** — zincir yoksa mevcut tek-adımlı davranış korunur |
| 4 | Reddedilen talebin yeniden gönderimi | **Hayır** (`rejected → pending` akışı yok) |
| 5 | Self-approval | **Yalnız admin** |
| 6 | Ekip yöneticisi yetkisi | **İkisi de** — üye ekler/çıkarır **ve** onay verir |
| 7 | Ekipler arası görünürlük | **Evet** (gereksiz izolasyon eklenmeyecek) |
| 8 | Ekip kapsamı | **Firma bazlı** (`company_id`; şube bazlı model yok, `BranchAccess` genişletilmeyecek) |
| 9 | Çevrimdışı onay yasağı | **Kesin yasak** — ürün davranışı aynen: *"çevrimdışıyken onay ekranından onay vermeye çalışırsa hem engellenmeli hem uyarı mesajı verilmeli; sadece çevrimiçi onay verilebilir"* |
| 10 | Ret gerekçesi görünürlüğü | **Herkes** — bugünkü davranış korunur, API daraltması yapılmayacak |

### Kararların doğrudan sonucu olan kapsam notları

1. **Satın Alma'ya onay katmanı EKLENECEKTİR** (PK-EK-01=C) — artık **kapsam içidir**, FAZ 3
   tasarımında ele alınacaktır. Bugün `PurchaseOrderService.cs:390` "onay katmanı değil" demektedir;
   bu sınır kullanıcı kararıyla açılmıştır.
2. **İş Emri onay zinciri KAPSAM DIŞIDIR.**
3. Hiyerarşi **kullanıcı tabanlıdır**, ancak **`users` tablosuna hiyerarşi sütunu EKLENMEYECEKTİR**;
   ayrı yapı `users`'a referans verecektir.
4. `users` masaüstünde senkronlu olmadığından çevrimdışı hiyerarşi **görünürlüğü**
   `/api/lookups/sync` sunucu-otoriteli aynası ile sağlanacaktır.
5. **Yeni `teams` yetki modülü OLUŞTURULMAYACAKTIR**; ekip yönetimi `users` yetki kapsamındadır.
6. Çoklu ekip üyeliği → **çoka-çok** model.
7. Hiyerarşi azami **4 seviye**; **döngü engelleme doğrulaması FAZ 3'te zorunludur**.
8. Onay zinciri **snapshot** olarak süreç başlangıcında dondurulur.
9. Çevrimdışı onay kesin yasak: **UI'da engelleme + uyarı mesajı**, ayrıca **servis/API seviyesinde
   güvenlik kapısı** FAZ 3 tasarımında korunacaktır.
10. **SNK-05 LWW yasağı korunacaktır**; onay sunucu-otoriteli çevrimiçi akışta kalır.
11. **Mevcut Malzeme Talebi tek-adımlı onay davranışı BOZULMAYACAKTIR.**
12. Ret sonrası yeniden gönderim **yoktur**.
13. Self-approval **yalnız admin**.
14. Ekip yöneticisi **hem üye yönetir hem onay verir**.
15. Ekipler arası görünürlük **açıktır**.
16. Ekipler **firma bazlıdır**; `company_id` zorunluluğu korunur.

### Bu turda yapılmayanlar (kanıtlı)

- **Migration084 OLUŞTURULMADI** — katalog azamisi **83**, canlı şema **83**.
- **`src/` değişmedi · `tests/` değişmedi** (0 dosya).
- **Production'a bağlanılmadı** — SELECT dâhil hiçbir sorgu çalıştırılmadı.
- **Commit/push yapılmadı.**
- FAZ 1 bulguları **silinmedi/değiştirilmedi**; yalnız FAZ 2 bölümü eklendi.

---

## FAZ 3 ön koşulları

1. Veri modelinin kesinleşmesi: ekip (çoka-çok üyelik) · kullanıcı hiyerarşisi (ayrı tablo, `users`
   referanslı, azami 4 seviye, döngü engelli) · `approval_instance` / `approval_step` · Satın Alma
   onay bağlantısı. Tümü `company_id` taşır.
2. Migration **084** — karar verilmiş olsa da **FAZ 3'te** oluşturulacaktır.
3. Senkron tasarımı: hangi tablolar `BusinessSyncService.Tables` içine girecek, hangileri
   `/api/lookups/sync` aynasına bağlanacak (PK-EK-02=B gereği hiyerarşi ayna tarafındadır).
4. Güvenlik: çevrimdışı onay engeli (UI + servis/API) · self-approval kapısı (yalnız admin) ·
   döngü engeli · `CompanyId` daima oturumdan.
5. Geriye uyumluluk: zinciri olmayan mevcut talepler bugünkü tek-adımlı akışla çalışmaya devam eder.

**FAZ 3 yalnızca kullanıcı tarafından açıkça "UYGULAMA BAŞLASIN" onayı verildiğinde başlatılacaktır.**

---

## FAZ 3 — §9'un 6 AÇIK NOKTASI KESİNLEŞTİ (2026-08-30, ADR-188)

> FAZ 3 planlama turunda §9 altında "kararı bekleniyor" diye işaretlenen 6 nokta **kapanmıştır**.
> Bunlar ADR-187'nin 17 kararına **EK**tir; ADR-187 değişmedi ve beklemeye alınmadı.

| # | Konu | **KESİN KARAR** |
|---|---|---|
| 1 | Satın Alma onayı neyi engeller | Onay tamamlanmadan **mal kabul (`Receive`) YAPILAMAZ**. Kapı UI + **servis/API** (yalnız buton gizlemek yetersiz). İptal siparişte mevcut engel aynen. |
| 2 | PO onay durumu nerede | **`purchase_orders.status` DEĞİŞMEZ** (`open\|closed\|cancelled`). Onay `approval_instance`/`approval_step`te. `Receive` kontrolü **atomik/yarış-güvenli**. |
| 3 | PO'da onaycı kim | Ayrı `approver_user_id` **YOK**; zincir **kullanıcı hiyerarşisinden**. **Ekip lideri otomatik onaycı DEĞİL.** Snapshot sonrası değişiklik süreci etkilemez. |
| 4 | Satın Alma'da zincir | **Opsiyonel.** Zincir yok → mal kabul serbest. Zincir başlatıldı → tamamlanmadan mal kabul yok. |
| 5 | Ekip ↔ zincir | Kaynak **USER HİYERARŞİSİ**. Ekip zincir oluşturmaz; gruplama + üye yönetimi + görünürlük. Azami derinlik **4**. |
| 6 | Çevrimdışı PO | PO çevrimdışı oluşturulabilir; **onay asla** (kuyruk yok, `sync_outbox`'a onay yazılmaz). Zinciri aktif PO'da **çevrimdışı mal kabul de yok**. |

## FAZ 3 / ALT FAZ 1 — EKİP TANIMI ✅ UYGULANDI (2026-08-30)

**Migration084_Teams** — `teams` + `team_members`. `branch_id` YOK (İK-8) · `users`'a ALTER YOK
(PK-EK-02) · backfill YOK · aktif üyelik benzersizliği **kısmi indeks** ile (İK-1 çoklu üyelik serbest,
aynı ekibe çift üyelik yasak, yumuşak silinen yeniden eklenebilir).

**FK kararı (kanıtlı teknik gerekçe).** `lead_user_id`/`user_id` için `users`'a **FK verilmedi**:
`users` masaüstüne senkronlanmaz ve aynada da yoktur (yerel `users` tablosuna hiçbir yazım yok) →
FK verilseydi ekip aynası masaüstüne inerken `foreign_keys=ON` altında **FK ihlaliyle kırardı**.
Bütünlük **sunucu servisinde** zorlanır. Migration081/083 içtihadı.

**Servis** `TeamService` — yetki modülü **`users`** (PK-EK-07=B). İK-6: ekip lideri **kendi ekibinin**
üyelerini yönetir (ekip oluşturma/silme hakkı vermez, başka ekibe geçmez). **Lider gerçekten üye
olmalı**; lider çıkarılırsa liderlik temizlenir.

**API** `/api/teams`, `/api/teams/{id}/members`, `/api/users/{id}/teams` — DTO'larda **`companyId` YOK**;
firma daima oturumdan (IDOR testlerle kilitli).

**Ayna** `teams`/`teamMembers` → `/api/lookups/sync`. **`BusinessSyncService.Tables`'a EKLENMEDİ.**
Masaüstü tüketicisi **replace** semantiği + sunucu kimliği koruma + **tablo yoksa sessizce atlama**
(eski istemci bozulmaz).

**Ekran** `AppScreens`'e `teams` (**ModuleKey = `users`**). Web `/teams` tam CRUD; **masaüstü SALT
OKUNUR** (ekip verisi sunucu otoriteli olduğu için masaüstünden yazılmaz).

**ALT FAZ 2 sınırı korundu:** `user_hierarchy` / `approval_instance` / `approval_step` **oluşturulmadı**
(test EK03 kilitler).

**Bu turda yapılmayanlar:** production'a **hiçbir erişim yok** (SELECT dâhil) · deploy/release **yok** ·
commit/push **yok** · güvenlik guard'ları **gevşetilmedi** · test **skip edilmedi**.

## FAZ 3 / ALT FAZ 2 — HİYERARŞİ + ONAY ZİNCİRİ ✅ UYGULANDI (2026-08-30, ADR-189)

**Migration085_ApprovalChain** — `user_hierarchy` + `approval_instance` + `approval_step`.
Mevcut tablolara **ALTER YOK** (`users`, `material_requests`, `purchase_orders` dokunulmadı;
`purchase_orders.status` sözleşmesi **korundu**) · **backfill YOK** → hiyerarşi tanımlanmadıkça
davranış **birebir aynı**.

**Hiyerarşi** — İK-2 = **4 düğüm** (`A→B→C→D` geçerli, `+E` geçersiz) → en çok **3 onaycı**.
Derinlik **yukarı + aşağı** birlikte ölçülür; döngü hem yazımda hem çözümlemede engellenir;
zincir çözümleme **tek sorgu** (N+1 yok). Yetki: **`users`**.

**Onay motoru** — TEK motor, iki varlık (İş Emri kapsam dışı, kapalı liste ile kilitli).
**Snapshot**: adım sahipleri süreç başında sabitlenir, sonradan hiyerarşi değişse de **değişmez**.
Kapılar: tenant → süreç açık → **mevcut** modül yetkisi → **snapshot adım sahipliği** → sıra →
**self-approval yalnız admin**. Eşzamanlılık: aynı adıma iki onaydan **yalnız biri** geçer.

**Malzeme Talebi** — zincir **yoksa** bugünkü tek-adımlı akış birebir; **varsa** eski tek-adımlı yol
**kapalı** (bypass kapısı). Reddedilen sonrası adımlar `skipped` (silinmez). İK-4 zaten durum
makinesinde kilitliydi, testle sabitlendi.

**Satın Alma** — zincir **sunucuda** hiyerarşiden kurulur (istemciden onaycı alınmaz).
**Onaysız `Receive()` reddedilir**; kapı `Receive`'ın transaction'ında, stok hareketinden önce →
**eski istemci bypass edemez**. `status` sözleşmesi değişmedi.

**Çevrimdışı onay** — onay tabloları **hiçbir senkron yolunda değil**; motor **yalnız sunucuda**.
Masaüstü onayı artık yerele yazmaz, `OnlineApprovalClient` ile sunucuya gider; çevrimdışıysa
**uyarı verir ve hiçbir şey yazmaz** (`sync_outbox`'a onay kaydı düşmez — testle kanıtlı).

**Kapsam sınırı** — ALT FAZ 3 **"Onaylamalarım" ekranı YAPILMADI** (yalnız `/api/approvals/mine`
servis sözleşmesi); `AppScreens`'e yeni ekran eklenmedi.

**Bu turda yapılmayanlar:** production erişimi **yok** (SELECT dâhil) · deploy/release **yok** ·
commit/push **yok** · guard **gevşetilmedi** · test **skip edilmedi**.

## FAZ 3 / ALT FAZ 3 — ONAYLAMALARIM ✅ UYGULANDI (2026-08-30, ADR-190) — **ARA İŞ 5 TAMAMLANDI**

**Migration YOK.** `Migration086 oluşturulmadı` — kanıt: gerekli tüm alanlar mevcut şemada
(`approval_step.step_no`, toplam adım aynı tablodan sayılıyor, `material_requests.doc_no` /
`purchase_orders.order_no`, `request_date` / `order_date`). Katalog azamisi **85** kaldı.

**Veri kaynağı.** `ApprovalService.MyPending` yeniden yazıldı → kullanıcıya düşen ve **sırası gelmiş**
adımlar **TEK sorguda**. ⚠️ **Bulunan gerçek sorun:** önceki sürümde satır başına `IsCurrent` çağrısı
vardı — **N+1**. Düzeltildi; `SayanFabrika` ile **sorgu sayan test** eklendi (5 satır → **1 komut**).
Uçta kullanıcı/firma parametresi **yok** → başkasının kuyruğu istenemez.

**Ekran.** `AppScreens`'e `approvals` (ModuleKey **`request_approval`** — yeni modül YOK), grup
"Talepler", rota `/approvals`. Parite kilitleri gevşetilmedi, ekran bilinçli kaydedildi
(masaüstü 58→59, web 65→66). **Listede görünmek onaylama yetkisi değildir** — karar mevcut kapılardan
geçer (`request_approval` / `purchasing`).

**Masaüstü + web.** Masaüstü yerel onay tablolarına dokunmaz; liste ve karar sunucudan
(`OnlineApprovalClient`). **Çevrimdışı: liste gelmez, karar verilemez, uyarı gösterilir, hiçbir yerel
kayıt / `sync_outbox` kaydı oluşmaz.** Web `/approvals` (MudBlazor, ProjectReference YOK) aynı uçlarda.
Ret gerekçesi iki platformda da zorunlu; gerekçe görünür kalır (İK-10).

**Eşzamanlılık.** UI'da kilit yok; aynı adıma ikinci karar sunucudaki atomik geçişte reddedilir.

**Bu turda yapılmayanlar:** production erişimi **yok** · deploy/release **yok** · commit/push **yok** ·
guard **gevşetilmedi** · test **skip edilmedi** · yeni migration **yok**.
