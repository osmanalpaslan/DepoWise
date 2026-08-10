# PROJE GELİŞTİRME DURUMU — DepoWise / Alpnex

> **Bu dosya nedir?** Projenin **kalıcı geliştirme hafızası**. Yeni bir oturum açıldığında
> *"neredeyiz, ne bitti, sırada ne var, hangi kararlar alındı"* sorularının cevabı buradadır.
>
> **Bu dosya yeni bir plan DEĞİLDİR.** İş ayrıntıları, faz içerikleri ve bağımlılık ağacı
> **[PROJE_GELISTIRME_PLANI.md](../PROJE_GELISTIRME_PLANI.md)** dosyasındadır ve **orası bağlayıcıdır**.
> Burada yalnız ilerleme ve karar geçmişi tutulur; plan içeriği kopyalanmaz.
>
> ⚠️ **Çelişki kuralı:** Bu dosya ile gerçek kod/Git durumu farklıysa **gerçek durum esastır**;
> fark raporlanır ve bu dosya düzeltilir.

---

## 🔻 YENİ OTURUM AÇILIŞ SIRASI

```
1. Bu dosyayı oku            → neredeyiz?
2. git status + git log      → gerçek durum bununla uyuşuyor mu?
3. Fark varsa               → GERÇEK durumu esas al, farkı raporla, bu dosyayı düzelt
4. Aktif işin gerektirdiği kadar çalış — genel analizi TEKRARLAMA
```

---

## 1. PROJENİN GENEL AMACI

Çok firmalı (multi-tenant) **depo / stok / araç / bakım / yakıt / günlük faaliyet** yönetim sistemi.

Başlangıçta tek kişinin (kullanıcının babasının) kullanımı için tasarlandı.
**Bugünkü hedef:** çok kullanıcılı, çok şubeli, web + masaüstü çalışan, **farklı firmalara
satılabilecek ticari ürün**.

**Kısıt:** Maddi imkânlar sınırlı → mevcut mimariyi koruyan, minimum maliyetli, büyümeyi
engellemeyen çözümler tercih edilir. Ücretli servis / SaaS / harici altyapı önerilmez.

---

## 2. ANA MİMARİ

```
   Masaüstü (Avalonia, .NET 8)          Web (Blazor Server, MudBlazor)
   YEREL SQLite — çevrimdışı çalışır     kendi iş mantığı YOK
            │                                      │
            └──── 15 sn periyodik senkron ─────────┤
                                                   │
                        ┌──────────────────────────▼─────┐
                        │  API — Fly.io, 249 uç          │
                        │  PostgreSQL (Neon)             │
                        └────────────────────────────────┘
                                     │
                   DepoWise.Infrastructure — İŞ KURALLARI TEK YERDE
                   (hem API hem masaüstü aynı servisleri çağırır)
```

**Hedef veri hiyerarşisi (KARAR-6 — henüz kodlanmadı, FAZ 4):**

```
Firma
└── Şube            (organizasyon birimi — kullanıcı/yetki/personel buraya bağlanır)
    └── Depo        (fiziksel stok konumu — AYRI warehouses tablosu)
        └── Stok
            └── Malzeme   (katalog FİRMA GENELİNDE ortak kalır)
```

**Korunacak değişmezler:** iş kuralları tek yerde · deny-by-default yetki · çevrimdışı masaüstü ·
operasyonel kayıtta fiziksel silme yok · çift lehçe (SQLite + PostgreSQL) test edilir.

---

## 3. ANA GELİŞTİRME YOL HARİTASI

Sıra **plan dosyasından** alınmıştır; burada yeni sıra üretilmez.

| Faz | İçerik | Durum |
|---|---|---|
| **FAZ 0** | Canlıya geçiş öncesi zorunlu düzeltmeler (GUV-01, DOG-01, MLZ-01, KLT-01) | 🔵 **AKTİF** |
| FAZ 1 | Senkron optimizasyonu + parite (SNK-01…04, PRT-01, PRT-02) | BEKLEMEDE |
| FAZ 2 | Yetki ağacı (YET-01 kapı → BRM-01, YTK-01…04) | BEKLEMEDE |
| FAZ 3 | Gerçek kayıt kilidi (KLT-02, KLT-03, KLT-04) | BEKLEMEDE |
| FAZ 4 | Depo bazlı stok (STK-01…07) | BEKLEMEDE |
| FAZ 4B | Depo transferi (TRF-01) | BEKLEMEDE |
| FAZ 5 | İş akışları (GNL-01/02, BKM-01…03) | BEKLEMEDE |
| FAZ 6 | Kalite/erteleme (GNC-01, LOG-01, RPR-01, TST-01, TMZ-01) | BEKLEMEDE |

---

## 4. İŞ DURUM TABLOSU

**Durumlar:** `BEKLEMEDE` · `ANALİZ BEKLİYOR` · `ANALİZ TAMAM` · `GELİŞTİRMEDE` · `TESTTE` ·
`TAMAMLANDI` · `ERTELENDİ` · `KARAR BEKLİYOR`

| ID | İş | Faz | Durum | Önc. | Bağımlılık | Analiz | Geliştirme | Test | Commit |
|---|---|---|---|---|---|---|---|---|---|
| GUV-01 | Süper admin parolası | 0 | KARAR BEKLİYOR | P0 | — | ✅ | kullanıcı | — | — |
| DOG-01 | Normal kullanıcı web girişi testi | 0 | BEKLEMEDE | P1 | — | ✅ | kullanıcı | — | — |
| **MLZ-01** | Malzeme silme koruması | 0 | **TAMAMLANDI** | P0 | — | ✅ | ✅ | ✅ 1025/992/0/33 | `b932f75` |
| **KLT-01c** | PermissionService concurrency | 0 | **TAMAMLANDI** | P1 | — | ✅ | ✅ | ✅ 1033/1000/0/33 | `18a21f8` |
| KLT-01a | RequestOperationsService | 0 | **ANALİZ BEKLİYOR** | P1 | — | ❌ | ❌ | ❌ | — |
| KLT-01e | Yakıt/stok regresyon testleri | 0 | BEKLEMEDE | P2 | — | ✅ | ❌ | ❌ | — |
| KLT-01b | LookupService.Rename | 0 | BEKLEMEDE | P2 | — | ✅ | ❌ | ❌ | — |
| KLT-01d | Şablon/Unvan/Firma servisleri | 0 | BEKLEMEDE | P2 | — | ✅ | ❌ | ❌ | — |
| SNK-01…04 | Senkron optimizasyonu | 1 | BEKLEMEDE | P1/P2 | — | kısmi | ❌ | ❌ | — |
| PRT-01 | Tam ekran parite denetimi | 1 | ANALİZ BEKLİYOR | P1 | — | ❌ | ❌ | ❌ | — |
| PRT-02 | Ekran adı eşleme | 1 | BEKLEMEDE | P2 | PRT-01 | ❌ | ❌ | ❌ | — |
| **YET-01** | **Yetki modeli KARARI** | 2 | **KARAR BEKLİYOR** | P1 | — | ✅ | ❌ | ❌ | — |
| TMZ-02 | BranchService + user_scopes | 2 | ERTELENDİ→YET-01 | P1 | YET-01 | ✅ | ❌ | ❌ | — |
| BRM-01 | Personel birimi | 2 | BEKLEMEDE | P1 | YET-01 | kısmi | ❌ | ❌ | — |
| YTK-01…04 | Approve/Cancel, kayıt tipi, UI | 2 | BEKLEMEDE | P1 | YET-01 | kısmi | ❌ | ❌ | — |
| KLT-02/03/04 | Gerçek kilit (lease/heartbeat) | 3 | BEKLEMEDE | P1 | KLT-01 | ✅ tasarım | ❌ | ❌ | — |
| STK-01…07 | Depo bazlı stok | 4 | BEKLEMEDE | P0/P1 | MLZ-01, KLT-01 | ✅ mimari | ❌ | ❌ | — |
| TRF-01 | Depo → depo transferi | 4B | BEKLEMEDE | P2 | STK-05 | ✅ | ❌ | ❌ | — |
| GNL-01/02 | Günlük faaliyet | 5 | BEKLEMEDE | P1/P2 | — / BRM-01 | kısmi | ❌ | ❌ | — |
| BKM-01…03 | Bakım onayı → stok | 5 | BEKLEMEDE | P1 | YTK-01, KARAR-4 | kısmi | ❌ | ❌ | — |
| GNC-01 | Otomatik güncelleme davranışı | 6 | BEKLEMEDE | P2 | — | ✅ | ❌ | ❌ | — |
| LOG-01 | Kullanıcı karar logu | 6 | BEKLEMEDE | P2 | BRM-01 | ✅ | ❌ | ❌ | — |
| RPR-01 | Rapor envanteri | 6 | ANALİZ BEKLİYOR | P2 | — | ❌ | ❌ | ❌ | — |
| TST-01 | 33 atlanan test | 6 | ANALİZ TAMAM | P2 | — | ✅ | ❌ | ❌ | — |
| TMZ-01 | ListColumns çift kopya | 6 | BEKLEMEDE | P2 | — | ✅ | ❌ | ❌ | — |

---

## 5. TAMAMLANAN İŞLER

| İş | Commit | Sonuç | Push |
|---|---|---|---|
| **MLZ-01** — Malzeme silmede stok/kullanım koruması | **`b932f75`** | Stoğu veya operasyonel geçmişi (hareket/bakım/talep/sayım) olan malzeme artık silinemiyor. Koruma tek serviste → web + masaüstü + doğrudan API birlikte korunuyor. Migration yok, +90/−0 satır. | ❌ |
| MLZ-01 plan/analiz dokümantasyonu | **`2ab4c71`** | Depo + yetki mimarisi analizi, KARAR-6, FAZ 4 yeniden yazımı | ❌ |
| KLT-01 kapsam analizi | **`d974e70`** | Planın 4 hedefinden 3'ünün yanlış olduğu tespiti | ❌ |
| **KLT-01c** — Yetki kaydetmede düzenleme kilidi | **`18a21f8`** | `users.version` jetonuyla koruma. İki yönetici çakışırsa ikincisi **409** alıyor, birincinin verdiği yetki silinmiyor, kısmi yazma olmuyor. 5 dosya + 1 yeni test (8 test). Migration yok. | ❌ |

**Bu plandan önce tamamlananlar:** Tasarım paketi (FAZ 1-9 web + M1-M5 masaüstü) — yayınlandı,
web canlı + masaüstü **1.0.136**. · Masaüstü vektör ikonları (M2.5) — ayrı dalda commit'li,
**görsel doğrulama bekliyor**, `master`'a alınmadı.

---

## 6. AKTİF İŞ

**ANA İŞ:** `KLT-01` — Eksik iyimser (optimistic) düzenleme kilitleri · FAZ 0
**BİTEN ALT İŞ:** `KLT-01c` ✅ **TAMAMLANDI ve COMMIT EDİLDİ** — `18a21f8` (2026-08-10)
**SIRADAKİ ALT İŞ:** `KLT-01a` — `RequestOperationsService` · ⏳ **DETAY ANALİZ AŞAMASINDA**

**Git dalı:** `feature/mlz-01-malzeme-silme-korumasi` · **Push:** ❌ (dal yerelde)

**KLT-01 alt iş durumu:**
| Alt iş | Durum |
|---|---|
| `KLT-01c` PermissionService | ✅ TAMAMLANDI (`18a21f8`) |
| `KLT-01a` RequestOperationsService | ⏳ detay analiz |
| `KLT-01e` Yakıt/stok regresyon testleri | BEKLEMEDE |
| `KLT-01b` LookupService.Rename | BEKLEMEDE |
| `KLT-01d` Şablon/Unvan/Firma | BEKLEMEDE |
| Web+masaüstü 409 kontrolü | BEKLEMEDE |
| Tam test + plan güncelleme | BEKLEMEDE |

---

## 7. SIRADAKİ İŞ

**`KLT-01a` — `RequestOperationsService` (`ChangeStatus`, `UpdateShipmentInfo`)**

⏳ **DETAY ANALİZ BEKLENİYOR.** Kullanıcı analiz promptu verecek; kodlamaya doğrudan başlanmaz.

---

## 8. SONRAKİ AŞAMALAR

```
KLT-01a  → KLT-01e → KLT-01b → KLT-01d → 409 davranış kontrolü → tam test + plan güncelleme
   ↓
SNK-01…04  (senkron optimizasyonu — en yüksek getiri/maliyet oranlı iş)
   ↓
PRT-01/02  (parite denetimi)
   ↓
YET-01     (yetki modeli KARARI — FAZ 2'nin kapısı, TMZ-02 dahil)
```

---

## 9. MİMARİ KARARLAR

| ID | Karar | Neden | Etkilenen | Uygulama aşaması | Durum |
|---|---|---|---|---|---|
| KARAR-1 | Malzeme kataloğu firma genelinde kalır, fiziksel stok **depo** bazlı olur | Standart ERP deseni; katalogu bölmek raporları ve muadil eşleştirmeyi bozar | STK-01…07, MLZ-01 | FAZ 4 | ✅ VERİLDİ |
| KARAR-2 | Stok geçişi fazlara bölünür, canlı veri korunur | Tek seferde uygulanamayacak kadar riskli | STK-01…07 | FAZ 4 | ✅ VERİLDİ |
| KARAR-3 | Kayıt kilidi **kiralama (lease)** tabanlı gerçek kilit | Soft-warning reddedildi; kiralama stale-lock sorununu ortadan kaldırır. **Sınır:** çevrimdışı masaüstü kilitlenemez (fiziksel sınır) | KLT-02/03/04 | FAZ 3 | ✅ VERİLDİ |
| KARAR-4 | Bakımda negatif stok ↔ onay akışı çelişkisi | — | BKM-02, BKM-03 | FAZ 5 | ⏳ **AÇIK** |
| KARAR-5 | Queue / Redis / WebSocket / ücretli monitoring **kurulmayacak** | Gerçek ihtiyaç yok, mevcut yük uzak | Y-1, Y-5, SNK-* | — | ✅ VERİLDİ |
| KARAR-6 | Depo = **ayrı `warehouses` tablosu**; `branches` genişletilmez; her şubeye 1 varsayılan depo | `branches.kind` hiçbir sorguda süzülmüyor → 12+ nokta sessizce bozulurdu | FAZ 4 tamamı | FAZ 4 | ✅ VERİLDİ |
| KARAR-7 | Yetki kümesi eşzamanlılık jetonu = **`users.version`** | Kolon zaten vardı → migration yok; "sil+yaz" deseninde satır sürümü kümeyi koruyamaz | KLT-01c | FAZ 0 | ✅ UYGULANDI |

**Öneri aşamasında (KESİN KARAR DEĞİL):** Yetki modeli için *rol tabanı + kullanıcı override*
(`role_permissions` + `user_permissions`). **YET-01'de karara bağlanacak** — kesin karar gibi
uygulanmayacak.

---

## 10. HENÜZ UYGULANMAMASI GEREKEN KONULAR

- ❌ **Depo kodlaması** — mimari karar var (KARAR-6) ama FAZ 4'e ait. KLT-01 içine depo kodu sokulmaz.
- ❌ **Depo bazlı yetki** — P3, FAZ 4 sonrası.
- ❌ **Yetki refactor'ı** — `YET-01` kesinleşmeden `role_permissions` eklenmez, `YTK-01…04` başlamaz.
- ❌ **`TMZ-02` düzeltmesi** — `Org.BranchService` yalnız "ölü kod" diye **silinmez**; YET-01 kapsamı.
- ❌ **Bakım onay sistemi** — `YTK-01` ve KARAR-4'e bağlı.
- ❌ **Gerçek kayıt kilidi (lease/heartbeat)** — `KLT-02/03/04`, FAZ 3. `KLT-01` ile karıştırılmaz.
- ❌ **`material_requests.warehouse_id`** — bu alan **depo değil, personel (depo sorumlusu)** tutuyor.
  Depo mimarisinde **kullanılmayacak**.
- ❌ **Kuyruk / WebSocket / harici altyapı** — KARAR-5.

---

## 11. BEKLEYEN KULLANICI KARARLARI

**İşletme / iş kuralı kararları (kullanıcıya ait):**
| Konu | Soru |
|---|---|
| KARAR-4 | Bakımda "negatif stok serbest" kuralı, "stok düşümü onaya bağlı" akışıyla çelişiyor. Onay beklerken stok düşmeyecekse negatif stok serbestliği ne anlama gelecek? |
| YET-01 | Rol değiştiğinde o roldeki herkesin yetkisi otomatik değişsin mi, yoksa yetkiler kişiye özel mi kalsın? |

**Kullanıcı aksiyonu bekleyenler (kod işi değil):**
- `GUV-01` — süper admin parolası ⚠️ **acil** (zayıf parola canlıda çalıştığı doğrulandı)
- `DOG-01` — normal kullanıcıyla web girişi testi
- Masaüstü vektör ikonlarının görsel kontrolü

---

## 12. TEKNİK BORÇLAR

| ID | Borç | Durum |
|---|---|---|
| TMZ-01 | `ListColumns` iki kopya (`Application/Ui` + `Web/Services`) — biri unutulursa ekran sessizce bozulur | P2, FAZ 6 |
| TMZ-02 | İki `BranchService` (`Org` ölü, `Organization` aktif) + `user_scopes`'un üretimde **yazanı yok** → çoklu şube ataması arayüzden ulaşılamaz | **YET-01'e dahil** |
| **TMZ-03** | **Seed'de olmayan rol sabitleri** — aşağıda ayrıntı | **YET-01 kapsamı** · ⛔ şimdi dokunulmayacak |
| — | Yakıt/stok mevcut concurrency korumaları **çalışıyor ama test edilmiyor** | `KLT-01e` |
| — | `users.version` artık yetki değişiminde artıyor; ileride kullanıcı düzenlemesine kilit eklenirse **aynı jetonu paylaşacaklar** (doğru davranış ama YET-01'de teyit edilmeli) | YET-01 |

### 🔍 `TMZ-03` — Seed'de bulunmayan rol sabitleri
*(2026-08-10'da `KLT-01c` testi yazılırken tesadüfen bulundu — kullanıcı kararı: **şimdilik olduğu gibi bırak**)*

**Doğrulanan durum (koddan):**
- `RoleKeys` içinde **8 sabit** tanımlı: `SuperAdmin`, `RestrictedSuperAdmin`, `CompanyAdmin`,
  `Staff`, `Warehouse`, `Manager`, `Operation`, `ReadOnly`.
- `RoleKeys.Seed` içinde yalnız **4'ü** var: `SuperAdmin`, `RestrictedSuperAdmin`,
  `CompanyAdmin`, `Staff` (Migration002 bunları `roles` tablosuna yazar).
- Sonuç: **`Warehouse`, `Manager`, `Operation`, `ReadOnly`** rollerinin veritabanında karşılığı
  **oluşmuyor**; bu rollerle kullanıcı oluşturulmak istenirse
  `InvalidOperationException: "Rol bulunamadı: role-warehouse"` alınıyor.

**⛔ ŞU AŞAMADA YAPILMAYACAKLAR (kullanıcı talimatı):**
- Seed'e bu roller **eklenmeyecek**
- Rol yetkisi **tanımlanmayacak**
- Migration **oluşturulmayacak**
- `RoleKeys` **değiştirilmeyecek**
- Rollerin **isimlerinden hareketle anlam veya yetki varsayımı yapılmayacak**

**YET-01 analizinde cevaplanacak sorular:**
1. Bu dört sabit kod içinde **nerelerde referanslanıyor**? (hiç kullanılmıyor mu, yoksa bir yerde
   bekleniyor mu?)
2. **Geçmiş/amaçlanan kullanımları** neydi? (yarım kalmış bir tasarım mı, terk edilmiş mi?)
3. Gerçekten **korunması gereken roller mi**, yoksa temizlenmeli mi?

> ⚠️ **Özel uyarı — `Warehouse` rolü depo mimarisiyle OTOMATİK İLİŞKİLENDİRİLMEYECEK.**
> Bizim mimaride **Depo = fiziksel stok konumudur** (Firma → Şube → **Depo** → Stok → Malzeme).
> `Warehouse` rolünün gerçekten *"depo sorumlusu"* anlamına gelip gelmediği **ayrıca koddan
> doğrulanmalıdır**. İsim benzerliği kanıt değildir.
> (Aynı tuzağa daha önce düşüldü: `material_requests.warehouse_id` alanının depo sandığımız hâlde
> **personel** tuttuğu ortaya çıkmıştı — bkz. §13.)

---

## 13. SON ANALİZ BULGULARI

| Tarih | İş | Bulgu | Önceki varsayım |
|---|---|---|---|
| 2026-08-09 | Genel | Stok **firma geneli** — `stock_balances` PK yalnız `material_id`, şube/depo boyutu yok | — |
| 2026-08-09 | Genel | Malzeme silmede **hiçbir koruma yok** | — |
| 2026-08-10 | Depo | `branches.kind` **hiçbir sorguda süzülmüyor** (12+ nokta) | ❌ *"kind='warehouse' ile depo eklenebilir"* — **YANLIŞTI** |
| 2026-08-10 | Depo | `stock_balances` bir **önbellek** (ledger'dan türetilir) → defterden yeniden hesaplanabilir, göç ucuzlar | ❌ *"riskli veri dönüşümü gerekir"* — **YANLIŞTI** |
| 2026-08-10 | Depo | Şubeler arası transfer **zaten çalışıyor**; eksik olan yalnız bakiye önbelleğinde konum boyutu | ❌ *"transfer sıfırdan yazılacak"* — **YANLIŞTI** |
| 2026-08-10 | Depo | `material_requests.warehouse_id` **personel** tutuyor, depo değil | ❌ *"kullanılmayan depo alanı, maliyeti düşürür"* — **YANLIŞTI** |
| 2026-08-10 | Yetki | `role_permissions` **YOK** — yetki kullanıcı bazlı; roller yetki taşımıyor; şablonlar **kopya** | ❌ *"rol → modül → işlem zinciri var"* — **YANLIŞTI** |
| 2026-08-10 | Yetki | Web'de **rol bazlı giriş kısıtı YOK** — `Guard()` yalnız oturum kontrol ediyor | ❌ *"sadece admin web'e girebiliyor"* — **DOĞRULANAMADI** |
| 2026-08-10 | KLT-01 | Yakıt, stok belgeleri, muayenede **düzenleme yolu hiç yok**; mevcut iptal/ters kayıt korumaları çalışıyor. Gerçek açık `PermissionService.SaveForUser`'daydı | ❌ *"4 serviste edit-lock eksik"* — **3'Ü YANLIŞTI** |
| 2026-08-10 | KLT-01c | `users.version` şemada var, **hiç artırılmıyor**, okuyucusu yok, senkron upsert'i dokunmuyor → jeton olarak güvenle benimsendi | — |
| 2026-08-10 | TMZ-03 | `RoleKeys`'te 8 sabit var, `RoleKeys.Seed`'de yalnız 4'ü → `Warehouse`/`Manager`/`Operation`/`ReadOnly` veritabanında yok. **Kullanıcı kararı: olduğu gibi bırakılacak, YET-01'de analiz edilecek.** `Warehouse` rolü depo mimarisiyle **otomatik ilişkilendirilmeyecek** | — |

> **Kural:** Plan yanlış çıkarsa **sessizce düzeltilmez** — "önceki varsayım yanlıştı → kod
> incelemesi sonucu gerçek durum budur" biçiminde kaydedilir. Yukarıdaki tablo bunun kaydıdır.

---

## 14. SON YAPILAN İŞLEM

**2026-08-10** — `KLT-01c` geliştirildi, test edildi ve **commit edildi (`18a21f8`)**:
`PermissionService.SaveForUser` artık `users.version` jetonuyla korunuyor. İki yönetici aynı
kullanıcının yetkisini düzenlerse ikincisi **409** alıyor, birincinin verdiği yetki silinmiyor,
kısmi yazma olmuyor. 5 dosya + 1 yeni test dosyası (8 test). Migration yok.
**Commit: `18a21f8`.** Push yapılmadı.

Ayrıca bu dosya (proje hafızası) oluşturuldu ve eski takip dosyaları işaretçi hâline getirildi.

---

## 15. SIRADAKİ CLAUDE CODE İŞLEMİ

1. **Önce:** Kullanıcının `KLT-01c` commit kararını al.
2. **Sonra:** `KLT-01a` için **detay analiz promptunu** bekle — kendiliğinden analiz veya
   kodlama başlatma.
3. Analizde `RequestOperationsService.ChangeStatus` / `UpdateShipmentInfo` zincirini
   (servis → API → web ekranı → masaüstü ViewModel → testler → yetki/şube kapsamı) koddan incele.
4. Analiz sonucu raporla → kullanıcı kararı → geliştirme → test → commit → **bu dosyayı güncelle**.

---

## EK — DOSYA HARİTASI

| Dosya | Amaç |
|---|---|
| **`docs/PROJE_DURUMU_VE_ILERLEME.md`** (bu dosya) | **Proje hafızası / ilerleme — TEK GERÇEK KAYNAK** |
| `PROJE_GELISTIRME_PLANI.md` | **Ana plan** — iş ID'leri, fazlar, bağımlılıklar (**BAĞLAYICI**) |
| `PROJE_GENEL_DURUM_ANALIZI.md` | Mevcut durumun koddan doğrulanmış fotoğrafı (2026-08-09) |
| `DEVAM.md` | ⤴ İşaretçi — geçmiş kayıt olarak korunuyor |
| `docs/YARIM_KALAN_ISLER.md` | ⤴ İşaretçi — geçmiş kayıt olarak korunuyor |
| `docs/GOREV_PANOSU.md` | ⤴ İşaretçi — geçmiş kayıt olarak korunuyor |
| `docs/SECURITY_CREDENTIAL_ROTATION_PLAN.md` | 🔒 **Kullanıcıya ait — dokunulmaz, stage/commit edilmez** |

**Neden eski dosyalar silinmedi:** `CLAUDE.md` §0 oturum akışını `DEVAM.md` ve
`YARIM_KALAN_ISLER.md` üzerine kurmuş (silmek projenin kendi kuralını kırar) · `DEVAM.md`'de
başka yerde olmayan 1399 satırlık geçmiş var · silme geri alınması zor, işaretçi aynı sonucu
veriyor.

---

## GÜNCELLEME KAYDI

| Tarih | Ne oldu |
|---|---|
| 2026-08-10 | Dosya oluşturuldu ve tam yapıya genişletildi. MLZ-01 (tamamlandı), KLT-01c (test geçti, commit bekliyor), A-1…A-4 analizleri, KARAR-1…7, teknik borçlar ve yanlış çıkan varsayımlar kaydedildi. Eski üç takip dosyası işaretçi yapıldı. |
| 2026-08-10 | `TMZ-03` kaydedildi (seed'de olmayan rol sabitleri) — kullanıcı talimatıyla **dokunulmadan** YET-01'e bırakıldı; `Warehouse` rolü için "depo ile otomatik ilişkilendirme yasağı" uyarısı eklendi. Kod değişikliği yok. |
