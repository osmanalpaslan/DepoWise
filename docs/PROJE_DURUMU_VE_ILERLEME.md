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
| **FAZ 0** | Canlıya geçiş öncesi zorunlu düzeltmeler (GUV-01, DOG-01, MLZ-01, KLT-01) | 🔵 **AKTİF** — kod işleri (MLZ-01 ✅, KLT-01 ✅) **bitti**; kalan iki madde **kullanıcı aksiyonu** (GUV-01, DOG-01) |
| FAZ 1 | Senkron optimizasyonu + parite (SNK-01…04, PRT-01, PRT-02) | 🔵 **AKTİF** — SNK-01 ❌ iptal · SNK-02 ✅ · SNK-03 ✅ · sırada SNK-04 |
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
| **KLT-01** | **Eksik iyimser düzenleme kilitleri (ANA İŞ)** | 0 | ✅ **TAMAMLANDI** *(2026-08-10 kapandı)* | P1 | — | ✅ | ✅ | ✅ 1057/1024/0/33 | 3 commit |
| **KLT-01c** | PermissionService concurrency | 0 | **TAMAMLANDI** | P1 | — | ✅ | ✅ | ✅ 1033/1000/0/33 | `18a21f8` |
| **KLT-01a** | RequestOperationsService | 0 | **TAMAMLANDI** | P1 | — | ✅ | ✅ | ✅ 1046/1013/0/33 | `ef905d6` |
| ~~KLT-01e~~ | ~~Yakıt/stok regresyon testleri~~ | 0 | ❌ **İPTAL** | — | — | ✅ | — | — | — |
| ~~KLT-01b~~ | ~~LookupService.Rename~~ | 0 | ❌ **İPTAL** | — | — | ✅ | — | — | — |
| **KLT-01d** | **`MaterialTemplateService.Update`** *(daraltıldı)* | 0 | **TAMAMLANDI** | P2 | — | ✅ | ✅ | ✅ 1057/1024/0/33 **+ gerçek HTTP/web QA** | `4f3524a` |
| ~~SNK-01~~ | ~~Değişiklik yoksa push yapma~~ | 1 | ❌ **İPTAL** *(2026-08-10)* — koruma zaten mevcut (`c8d3dc7`) | — | — | ✅ | — | — | — |
| **SNK-02** | Seçici kadans *(daraltıldı — 2a)* | 1 | ✅ **UYGULANDI / KOD DOĞRULANDI** — ⚠️ gerçek HTTP QA yapılamadı | P1 | — | ✅ | ✅ | ✅ 1057/1024/0/33 · ⚠️ kadans **ölçülmedi** | `0501729` |
| **SNK-03** | Hata halinde exponential backoff | 1 | ✅ **TAMAMLANDI / UYGULANDI** — ⚠️ çalışma zamanı QA yapılamadı | P1 | SNK-02 ✅ | ✅ | ✅ | ✅ 1057/1024/0/33 | *(bu commit)* |
| SNK-04 | Günlük yedeği senkron turundan ayırma | 1 | BEKLEMEDE — ⚠️ varsayım **doğrulanacak** (saatlik koruma zaten var olabilir) | P2 | — | kısmi | ❌ | ❌ | — |
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
| **WEB-01** | Web hata mesajlarında ham JSON *(yeni bulgu)* | — | **FAZLANMADI** | P2 | — | ✅ | ❌ | ❌ | — |

---

## 5. TAMAMLANAN İŞLER

| İş | Commit | Sonuç | Push |
|---|---|---|---|
| **MLZ-01** — Malzeme silmede stok/kullanım koruması | **`b932f75`** | Stoğu veya operasyonel geçmişi (hareket/bakım/talep/sayım) olan malzeme artık silinemiyor. Koruma tek serviste → web + masaüstü + doğrudan API birlikte korunuyor. Migration yok, +90/−0 satır. | ❌ |
| MLZ-01 plan/analiz dokümantasyonu | **`2ab4c71`** | Depo + yetki mimarisi analizi, KARAR-6, FAZ 4 yeniden yazımı | ❌ |
| KLT-01 kapsam analizi | **`d974e70`** | Planın 4 hedefinden 3'ünün yanlış olduğu tespiti | ❌ |
| **KLT-01c** — Yetki kaydetmede düzenleme kilidi | **`18a21f8`** | `users.version` jetonuyla koruma. İki yönetici çakışırsa ikincisi **409** alıyor, birincinin verdiği yetki silinmiyor, kısmi yazma olmuyor. 5 dosya + 1 yeni test (8 test). Migration yok. | ❌ |
| **KLT-01a** — Gönderim bilgilerinde düzenleme kilidi | **`ef905d6`** | `UpdateShipmentInfo` üç alanı körlemesine yazıyordu → `material_requests.version` jetonu eklendi. `ChangeStatus` durum geçişine kontrol EKLENMEDİ (durum makinesi zaten koruyor; regresyon testi eklendi). `updateBranches:true` tek UPDATE olduğu için tamamı kontrole tabi. 4 dosya + 1 yeni test (13 test). Migration yok. | ❌ |
| **KLT-01d** — Şablon güncellemede düzenleme kilidi | **`4f3524a`** | `MaterialTemplateService.Update` 12 alanı körlemesine yazıyordu → `material_templates.version` jetonu eklendi. Çakışmada `tx.Commit()` çağrılmıyor → ne alanlar ne **audit kaydı** yazılıyor. 4 kod + 1 yeni test dosyası (11 test), +340/−12 satır. Kapsam daraltıldı: `PersonnelTitleService` ve `CompanyService` **çıkarıldı**. `material_templates` senkron listesinde **değil** → LWW politikasıyla çelişmiyor. Migration yok. | ❌ |
| ✅ **KLT-01 KAPANIŞI** | — | Ana iş **tamamlandı** (aşağıda §6). 3 alt iş bitti, 2'si gerekçeli iptal. Kapanış doğrulaması sırasında **hiçbir kod dosyası değiştirilmedi**. | ❌ |
| **SNK-03** — Hata halinde exponential backoff | *(bu commit)* | Geçici sunucu/ağ hatasında iş verisi senkron turu kademeli olarak seyreltiliyor (15→30→60→120→240→300 sn, ±%20 jitter, jitter dahil **300 sn asla aşılmaz**). Backoff **yalnız geçici** hatalarda: taşıma/ağ, zaman aşımı, 5xx, 429. **401/403/diğer 4xx ve JSON/veri hataları backoff tetiklemez.** Başarılı turda sıfırlanır. Kontrol `SyncGate`'ten **önce** → kapı tutulmaz, manuel "Eşitle" bypass eder. `authsig`/`machines/register`/`/health` kadansları **değişmedi**. 3 dosya, +109/−7. Migration/API/`.csproj`/yeni bağımlılık **yok**, `tests/` **değişmedi**. ⚠️ Çalışma zamanı/HTTP davranışı **gözlenmedi** (aşağıya bakınız). | ❌ |
| **SNK-02** — Seçici senkron kadansı *(daraltılmış 2a)* | **`0501729`** | Tek dosya (`ShellViewModel.cs`, +33/−3). Mevcut 15 sn'lik timer'a **tick sayacı** eklendi; gecikmeye dayanıklı iki uç 60 sn'ye alındı. **Yeni timer yok, aktivite takibi yok, `SyncGate` değişmedi, veri yolu (push/pull/watermark/LWW) değişmedi.** Migration yok. ⚠️ **Gerçek HTTP kadans ölçümü YAPILAMADI** (aşağıya bakınız). | ❌ |

**Bu plandan önce tamamlananlar:** Tasarım paketi (FAZ 1-9 web + M1-M5 masaüstü) — yayınlandı,
web canlı + masaüstü **1.0.136**. · Masaüstü vektör ikonları (M2.5) — ayrı dalda commit'li,
**görsel doğrulama bekliyor**, `master`'a alınmadı.

---

## 6. AKTİF İŞ

**ANA İŞ:** `KLT-01` — Eksik iyimser (optimistic) düzenleme kilitleri · FAZ 0
### ✅ **KLT-01 KAPANDI — 2026-08-10**

**Aktif kod işi YOK.** Sıradaki iş seçimi kullanıcı onayı bekliyor (§7).

**Git dalı:** `feature/mlz-01-malzeme-silme-korumasi` · **Push:** ❌ (dal yerelde)

**KLT-01 alt iş durumu (kapanış hâli):**
| Alt iş | Durum |
|---|---|
| `KLT-01c` PermissionService | ✅ TAMAMLANDI (`18a21f8`) |
| `KLT-01a` RequestOperationsService | ✅ TAMAMLANDI (`ef905d6`) |
| `KLT-01d` `MaterialTemplateService.Update` *(daraltıldı)* | ✅ TAMAMLANDI (`4f3524a`) — **otomatik test + gerçek HTTP/web QA** ile doğrulandı |
| `KLT-01e` Yakıt/stok regresyon testleri | ❌ **İPTAL EDİLDİ** (2026-08-10) — gerekçesi ortadan kalktı, aşağıya bakınız |
| `KLT-01b` LookupService.Rename | ❌ **İPTAL EDİLDİ** (2026-08-10) — LWW bu tablolarda mimari politika, aşağıya bakınız |
| Web + masaüstü 409 davranış kontrolü | ✅ TAMAMLANDI (2026-08-10) — aşağıya bakınız |

**KLT-01 kapanış ölçümü:** Build **0 hata** · Test **1057 toplam / 1024 başarılı / 0 başarısız /
33 atlanan** (atlananların tamamı `Postgres*`, ortam eksikliği — `TST-01`).
Üç alt işte de **migration gerekmedi** (`version` kolonları şemada zaten vardı).

#### 🔬 409 davranış doğrulaması (2026-08-10) — kod okumasıyla yetinilmedi

Temiz bir QA veritabanıyla yerel API + web ayağa kaldırıldı, tarayıcıdan giriş yapılıp
Malzeme Şablonları ekranında **gerçek çakışma** üretildi. Canlı veriye dokunulmadı.

| Doğrulanan | Sonuç |
|---|---|
| Güncel sürümle PUT | HTTP **200** |
| Eski (stale) sürümle PUT | HTTP **409** + doğru Türkçe mesaj |
| Çakışan verinin ezilmesi | **Ezilmedi** — ilk yazanın verisi ve sürümü aynen korundu |
| Web formundaki kullanıcı girdisi | **Korundu** — form kapanmadı, alanlar duruyor, düzenleme modu sürüyor |
| Güncel sürümle tekrar kaydetme | **Başarılı** — kullanıcı kilitlenip kalmıyor |
| Sürüm göndermeyen eski istemci | HTTP **200** — geriye uyumluluk korunuyor |
| Başarılı kayıt akışı | Bozulmadı |

**Masaüstü:** Çakışmada `Clear()`/`Load()` çalışmıyor → form kapanmıyor, 12 alanlık girdi
korunuyor; mesaj anlaşılır. Masaüstü servisi **doğrudan** çağırdığı için 11 concurrency testi
masaüstünün gerçek yolunu test ediyor. ⚠️ **Canlı Avalonia arayüz koşusu YAPILMADI** — mevcut
araçlarla masaüstü penceresi sürülemiyor ve çalıştırmak kullanıcının **gerçek yerel
veritabanına** test verisi yazardı. İstenirse izole QA veritabanıyla ayrıca yapılabilir.

**Bu doğrulama sırasında hiçbir kod dosyası değiştirilmedi.**

**`KLT-01d` kapsam kararı (2026-08-10, kullanıcı onayı):**
| Servis | Karar | Gerekçe |
|---|---|---|
| `MaterialTemplateService.Update` | ✅ **KAPSAMDA** | 12 alan körlemesine yazılıyor; aynı **genel** şablonu iki firma yöneticisi eşzamanlı düzenleyebilir → gerçek LWW problemi |
| `PersonnelTitleService` | ❌ **ÇIKARILDI** | Gerçek bir Update/Rename yolu **yok**; mevcut `UPDATE` yalnız soft-delete ve `WHERE ... AND is_deleted=0` atomik CAS koruması **zaten var** |
| `CompanyService` | ❌ **ÇIKARILDI** | Optimistic kontrol yok ama yalnız **süper admin** erişimli; çakışma ihtimali çok düşük, faydaya göre gereksiz kapsam/karmaşıklık. Ayrı teknik borç **açılmadı**, yalnız analiz bulgusu olarak kayıtlı |

---

## 6.1 ✅ `SNK-02` — KAPANDI (2026-08-10)

**Durum: `UYGULANDI / KOD DOĞRULANDI — GERÇEK HTTP QA YAPILAMADI`**

**Uygulanan kapsam (kullanıcı kararı: daraltılmış **2a**):** mevcut 15 sn'lik timer korundu,
tick sayacı (`_tick % 4`) ile iki uç 60 sn'ye alındı.

| Uç | Kadans | Neden |
|---|---|---|
| `business-version` (+push/pull) | **15 sn** | ADR-099 "veri anlık görünmeli" kararı korunur |
| `authsig` | **15 sn** | Yetki/şifre değişikliği algılama gecikmesi artmasın |
| `machines/register` | **15 sn** | Makine iptali (revoked/pending) algılama gecikmesi artmasın (**2a**) |
| `/health` | **60 sn** | Yalnız bağlantı rozeti; veri akışı buna bağlı değil |
| `conflicts/unseen` | **60 sn** | Zaten çözülmüş çakışmaların bildirimi; aksiyon gerektirmez |

**Beklenen kazanç: TEORİK %30 daha az HTTP isteği** (20 → 14 istek/dk/makine).
⚠️ Bu rakam **hesaplanmıştır, ölçülmemiştir.**

**Doğrulananlar:** Build **0 hata** · Test **1057 toplam / 1024 başarılı / 0 başarısız / 33 atlanan**
(referansla birebir aynı, regresyon yok) · Migration **yok** · Değişen dosya **yalnız 1**
(`ShellViewModel.cs`, +33/−3) · Kadans mantığı **kod düzeyinde** doğrulandı.

**Korunanlar:** `SyncGate` davranışı · `WarnConflictsAsync`'in gating'i (dışarı taşınmadı,
parametreyle atlanıyor) · çağrı sırası · açılıştaki ilk bağlantı kontrolü · kapanış push'u ·
manuel "Eşitle" · offline→online toparlanma · push/pull/watermark/LWW veri yolu.

### ⚠️ DOĞRULAMA SINIRI — başarısızlık değil, ortam kısıtı

**Gerçek HTTP kadans ölçümü YAPILAMADI.** Sebep: kadansı çalıştıran `ShellViewModel`
**yalnız girişten sonra** başlıyor (`App.axaml.cs:97`); Avalonia giriş penceresi ise bu
geliştirme ortamından görüntülenemiyor (etkileşimli masaüstü oturumu yok). Uygulama başlıyor,
migration'ları çalıştırıyor, başlangıç logunu `ok=True` yazıyor — ama pencere açılmadığı için
giriş yapılamıyor ve zamanlayıcı hiç başlamıyor.

**İzole QA ortamı güvenli biçimde KURULDU** (ayrı build klasörü + `serverurl.txt`→localhost +
`DEPOWISE_ENVIRONMENT=QA-SNK02` ile ayrı veritabanı). **Canlı sunucuya QA sırasında 0 istek gitti**
(yerel API logunun tamamı tek satır ve o da kontrol amaçlı curl'dü). Gerçek veritabanı açılmadı,
gerçek build klasörüne `serverurl.txt` konulmadı, QA süreçleri kapatıldı.

**Bu nedenle HTTP kadansı "gerçek ortamda doğrulandı" olarak KAYDEDİLMEZ.**
Ölçülemeyenler: 15 sn veri duyarlılığının pratikte korunduğu · bağlantı göstergesi ·
açılıştaki ilk kontrol · manuel "Eşitle" · kapanış push'u.

**Nasıl tamamlanabilir:** Kullanıcı kendi oturumunda izole kurulumu çalıştırıp giriş yaparsa,
yerel API logundaki zaman damgalarından kadans ölçülebilir. Ayrı bir tur olarak ele alınacak.

---

## 6.2 ✅ `SNK-03` — KAPANDI (2026-08-10)

**Durum: `TAMAMLANDI / UYGULANDI`** · **Bağımlılık `SNK-02` karşılandı.**

Geçici sunucu/ağ hatasında iş verisi senkron turu (`business-version` + push + pull) kademeli
olarak seyreltilir. Karar: **B2 — sınıflandırmalı backoff** (kullanıcı kararı).

**Backoff yalnız GEÇİCİ hatalarda devreye girer:**

| Hata | Backoff |
|---|---|
| Taşıma/ağ/DNS/bağlantı | ✅ |
| Zaman aşımı | ✅ |
| HTTP 5xx | ✅ |
| HTTP 429 | ✅ |
| **HTTP 401 / 403 / diğer 4xx** | ❌ **tetiklemez** |
| **JSON / veri (deserialization) hataları** | ❌ **tetiklemez** |
| Z3 "sunucu satır atladı" durumu | ❌ tetiklemez (kendi retry'ı var) |

**Backoff dizisi:** `15 → 30 → 60 → 120 → 240 → 300 sn` · **±%20 jitter** ·
**jitter dahil mutlak maksimum 300 sn** (tavan jitter'dan sonra da uygulanır) ·
**başarılı senkron turundan sonra sıfırlanır** (en geç bir sonraki tick'te 15 sn kadansa dönülür).

**Mimari kurallar korundu:** Backoff kontrolü **`SyncGate`'ten ÖNCE** → bekleme sırasında kapı
tutulmaz · manuel "Eşitle" backoff'u **bypass eder** (ayrı yol) ve başarıda sıfırlar ·
login / import / personel bağlama / kapanış push'u backoff'a **tabi değil** ·
`authsig`, `machines/register`, `/health` ve `conflicts` kadansları **değiştirilmedi** (SNK-02 2a
kararı korundu) · yeni timer yok · `Task.Delay` yok · push/pull/watermark/LWW mantığı değişmedi.

**Değişen dosyalar (3):** `BusinessSyncPullService.cs` (`SyncFailureKind` + sınıflandırıcı +
`LastFailure`) · `BusinessSyncPushService.cs` (`LastFailure`; `LastPushFailed` ve Z3 ayrımı
korundu) · `ShellViewModel.cs` (backoff durumu + gate öncesi kontrol + reset).
Toplam **+109/−7**. **Migration / API / `.csproj` / yeni bağımlılık YOK. `tests/` değişmedi.**

**Build/test:** 0 hata · **1057 toplam / 1024 başarılı / 0 başarısız / 33 atlanan** (regresyon yok).

### ⚠️ DOĞRULAMA SINIRI

**Kod incelemesi + build/regresyon testleri ile doğrulandı; çalışma zamanı/HTTP davranışı
GUI/QA ortamı sınırı nedeniyle gözlenmedi.** (Sebep `SNK-02` §6.1 ile aynı: kadansı çalıştıran
`ShellViewModel` yalnız girişten sonra başlıyor, Avalonia giriş penceresi geliştirme ortamından
görüntülenemiyor; ayrıca `DepoWise.Desktop` test projesinde referanslı değil.)

---

## 7. SIRADAKİ İŞ

**⏳ KULLANICI KARARI BEKLİYOR — kod işi başlatılmadı.**

`KLT-01` kapandı; FAZ 0'ın **kod tarafı bitti**. Kalan iki FAZ 0 maddesi (`GUV-01`, `DOG-01`)
kullanıcı aksiyonudur, Claude tamamlayamaz.

**`SNK-01` analiz edildi ve İPTAL edildi (2026-08-10)** — koruma kodda zaten vardı (§13).
**`SNK-02` uygulandı ve kapandı (2026-08-10)** — bkz. §6.1 (HTTP QA doğrulama sınırı dahil).
**`SNK-03` uygulandı ve kapandı (2026-08-10)** — bkz. §6.2.

**Sıradaki aday: `SNK-04` — günlük yedeği senkron turundan ayırma.**
⚠️ Bu maddenin varsayımı **doğrulanmamıştır**: `SNK-01` analizi sırasında `MaybeDailyBackupAsync`
içinde **zaten saatlik kısıt** göründü — `SNK-01` gibi "zaten yapılmış" çıkabilir.

**Başlamadan önce gereken:** kullanıcı onayı + `SNK-04` için **detay analiz** (kapsam koddan
yeniden çıkarılmalı — plan varsayımları `KLT-01`'de üç, `SNK-01`'de bir kez yanlış çıktı; bkz.
§12.5 ve §13'ün altındaki kalıcı ders).

---

## 8. SONRAKİ AŞAMALAR

```
✅ KLT-01 KAPANDI (KLT-01c ✅ · KLT-01a ✅ · KLT-01d ✅ · KLT-01e ❌ · KLT-01b ❌)
   ↓
❌ SNK-01 İPTAL (koruma zaten vardı — c8d3dc7, 2026-07-19)
   ↓
✅ SNK-02 UYGULANDI (seçici kadans 2a) — ⚠️ gerçek HTTP QA yapılamadı (§6.1)
   ↓
✅ SNK-03 UYGULANDI (sınıflandırmalı backoff) — ⚠️ çalışma zamanı QA yapılamadı (§6.2)
   ↓
SNK-04  (senkron optimizasyonu)  ◄ SIRADAKİ ADAY: SNK-04 (varsayımı doğrulanacak)
   ↓
PRT-01/02  (parite denetimi)
   ↓
YET-01     (yetki modeli KARARI — FAZ 2'nin kapısı, TMZ-02 dahil)
```

**Sırayı etkilemeyen ama bekleyen ayrı kayıtlar:** `WEB-01` (§12), `GUV-01`/`DOG-01` (kullanıcı).

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
| TMZ-02 | İki `BranchService` (`Org` ölü, `Organization` aktif) + `user_scopes`'un üretimde **yazanı yok** → çoklu şube ataması arayüzden ulaşılamaz. **EK BULGU (2026-08-10, KLT-01d analizinde):** aynı desen `CompanyService` için de geçerli — `Org/CompanyService.cs` üretimde **hiç örneklenmiyor** (`src/` içinde tek referans yok), aktif olan `Organization/CompanyService.cs`. `Org/` klasöründe **başka ölü sınıflar da olabilir**; YET-01'de tüm klasörün kullanım haritası çıkarılmalı. Kodla dokunulmadı | **YET-01'e dahil** |
| **TMZ-03** | **Seed'de olmayan rol sabitleri** — aşağıda ayrıntı | **YET-01 kapsamı** · ⛔ şimdi dokunulmayacak |
| — | ~~Yakıt/stok korumaları test edilmiyor~~ | ❌ **GEÇERSİZ** — varsayım yanlıştı, test ediliyor (bkz. §13). `KLT-01e` iptal edildi |
| — | **Yakıt ↔ stok davranış tutarsızlığı:** çift iptalde yakıt **hata fırlatıyor**, stok **sessiz no-op**. İkisi de savunulabilir, ikisi de test edilmiş; kullanıcıya yansıyan sorun yok. **Kullanıcı kararı: değiştirilmeyecek**, yalnız gözlem olarak duruyor | — (dokunulmayacak) |
| — | `Postgres*` testleri yerelde **atlanıyor** → yakıt/stok eşzamanlılığının **PostgreSQL tarafı yerelde hiç koşmuyor** | **`TST-01`** kapsamı (KLT-01e ile çözülmeyecek) |
| **TNM-01** | **`LookupService.Rename` benzersizlik kontrolü YAPMIYOR** — `Add*` metotları `FindByName` ile tekilleştirme yapıyor, `Rename` yapmıyor. Var olan bir adla çakışan yeni ad verilebiliyor → **çift kayıt** oluşabiliyor. Veritabanında bu tablolar için benzersizlik indeksi de bulunamadı. **Concurrency değil, VALİDASYON boşluğu.** | ⏳ İleride değerlendirilecek — **KLT-01 kapsamı DIŞI**, kodla dokunulmadı |
| **TNM-02** | **`LookupService.Rename` ve `Delete` etkilenen satır sayısını kontrol etmiyor** — `ExecuteNonQuery()` dönüşü atılıyor. Olmayan / başka firmaya ait / silinmiş id gönderilirse işlem **sessizce başarılı** dönüyor ve **audit kaydı yine yazılıyor** → yanıltıcı denetim izi. Güvenlik açığı değil (tenant filtresi veriyi koruyor). | ⏳ İleride değerlendirilecek — **KLT-01 kapsamı DIŞI**, kodla dokunulmadı |
| — | `users.version` artık yetki değişiminde artıyor; ileride kullanıcı düzenlemesine kilit eklenirse **aynı jetonu paylaşacaklar** (doğru davranış ama YET-01'de teyit edilmeli) | YET-01 |
| **WEB-01** | **Web hata mesajlarında ham JSON gösteriliyor** — aşağıda ayrıntı | ⏳ **AYRI İŞ** — henüz fazlanmadı |
| — | **Senkron analiz gözlemleri B / C / D / E** (aynı-ms kaybı · saat geri alınması · masaüstü testsizliği · transaction'sız snapshot) — aşağıda ayrıntı. **İş açılmadı**, yalnız kayıt | ⏳ B ve C **veri kaybı** içeriyor → ileride ayrıca analiz |
| — | *(düşük öncelikli gözlem)* Web'de başarılı kayıttan sonra "Güncellendi." mesajı **hiç görünmüyor**: `ClearForm()` mesajı hemen siliyor. Kayıt gerçekten yapılıyor ve liste tazeleniyor, yani kullanıcı sonucu dolaylı görüyor. **KLT-01 öncesinden var, KLT-01d'nin sebep olduğu bir durum değil.** Ayrı iş açılması **önerilmiyor**; `WEB-01` ele alınırsa aynı dosyalara dokunulacağı için oraya iliştirilebilir | ⏳ gözlem — iş açılmadı |

### 🔎 Senkron analiz gözlemleri (2026-08-10, `SNK-01` analizinde bulundu)
*(**İş numarası verilmedi, teknik borç açılmadı, kodla dokunulmadı** — kullanıcı kararı.
Buradaki amaç yalnız **kaybolmamalarını** sağlamak. `SNK-01` kapsamı DIŞI; `SNK-01`'in
yaratacağı riskler DEĞİL — hepsi 2026-07-19 watermark tasarımından gelen mevcut durumdur.)*

| # | Gözlem | Öncelik | Neden kayda geçti |
|---|---|---|---|
| **B** | **Aynı milisaniye penceresinde sessiz senkron kaybı.** `PushAsync` önce `localV = CompanyVersion()` okuyor, sonra snapshot üretiyor, başarıda watermark'ı `localV` yapıyor. Delta filtresi **kesin `>`** (`{stamp} > @since`). `CompanyVersion` okunduktan **sonra tam aynı milisaniyede** yazılan satır: bu turda snapshot'a girmez, sonraki turda `localV <= pushWm` nedeniyle push atlanır, girse bile `> localV` filtresi eler → **sunucuya hiç ulaşmaz ve hiçbir uyarı çıkmaz.** Dar pencere; toplu içe aktarma gibi aynı milisaniyeye çok kayıt düşen işlerde olasılık artar | **ORTA** — potansiyel **veri kaybı** | Kullanıcı dosya/veri kaybına karşı hassas; sessiz olması en riskli yanı |
| **C** | **Sistem saati geri alınırsa push süresiz durur.** `updated_at` sistem saatinden üretiliyor. Saat geriye alınırsa yeni kayıtların damgası watermark'ın altında kalır → `localV <= pushWm` → push kalıcı olarak atlanır. Kullanıcıya **hiçbir uyarı gösterilmez** | **ORTA** — potansiyel **veri kaybı** | Aynı sebeple: sessiz ve kendiliğinden düzelmiyor |
| **D** | **Masaüstü senkron mantığı otomatik test kapsamı dışında.** `tests/DepoWise.Tests.csproj` yalnız Api / Infrastructure / Application / Domain referansı veriyor; **`DepoWise.Desktop` referansı YOK** → `localV <= pushWm` koruması, watermark ilerletme/geri tutma ve "poison" mantığı hiçbir testle kaplanmıyor. Test edilen yalnız Infrastructure tarafındaki `CompanyVersion`/`BuildSnapshot` | DÜŞÜK-ORTA | B ve C'nin fark edilmemiş olmasının yapısal sebebi |
| **E** | **`BuildSnapshot` transaction kullanmıyor** — 22 tablo tek tek okunuyor, tutarlı anlık görüntü yok. B'yi besleyen yapı | DÜŞÜK | B ile birlikte değerlendirilmeli |

**Karar (2026-08-10, kullanıcı):** Bu tur **iş açılmayacak, kodlanmayacak**. B ve C ileride
**ayrıca analiz edilecek** (veri kaybı içerdikleri için). D ve E onlarla birlikte değerlendirilir.

### 🔍 `WEB-01` — Web hata mesajlarında ham JSON gösterimi
*(2026-08-10'da `KLT-01` kapanış QA'sinde bulundu — **KLT-01 kapsamı DIŞI**, kodla dokunulmadı)*

**Doğrulanan durum (gerçek tarayıcı koşusuyla):** Kullanıcının ekranda gördüğü metin:

```
Hata 409: {"error":"Bu kayıt siz düzenlemeye başladıktan sonra bir başkası tarafından değiştirildi. ..."}
```

**Sebep:** `src/DepoWise.Web/Services/ApiClient.cs` içinde `PutAsync` ve `DeleteAsync`,
sunucunun `{"error":"..."}` gövdesini **ayrıştırmadan** doğrudan kullanıcı mesajına yapıştırıyor
(`$"Hata {kod}: {gövde}"`). Aynı dosyadaki **`UploadImportAsync` ise aynı gövdeyi doğru şekilde
ayrıştırıyor** (`TryGetProperty("error")`) → proje doğru deseni zaten biliyor, **7 yerde**
uygulanmamış.

**Önemli:** Bu **`KLT-01d` tarafından oluşturulmuş bir hata DEĞİLDİR.** Uygulama genelinde
önceden var olan bir **UX / hata gösterimi** problemidir; her ekrandaki her hata mesajını
(403, 400, 409, 500) etkiler. Mesaj **görünüyor ve içeriği doğru** — yalnız teknik gövdeyle sarılı.

**Neden ayrı iş:** Düzeltme `ApiClient`'ın ortak metotlarına dokunur → **tüm web ekranlarını**
etkiler. `KLT-01` kapsamına alınması dar kapsam kuralını bozardı.

**Kapsam (ileride):** 7 çağrı noktası · tek ortak ayrıştırma yardımcısı · beklenen etki tüm web.
**Öncelik önerisi:** P2 · **Bağımlılık:** yok · **Migration:** yok · **Masaüstü:** etkilenmiyor
(masaüstü servisleri doğrudan çağırır, HTTP gövdesi görmez).

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

## 12.5 KALICI ANALİZ KURALI — CONCURRENCY ADAYI DEĞERLENDİRME
*(2026-08-10'da eklendi. **Yalnız KLT-01 için değil**, ileride yapılacak tüm benzer analizlerde
geçerli genel yöntemdir.)*

> ### ⚠️ `version++` bulunması ve `expectedVersion` bulunmaması **TEK BAŞINA** concurrency açığı veya düzeltilmesi gereken iş anlamına GELMEZ.

Bu mekanik ölçüt KLT-01 sürecinde **üç kez** yanlış/eksik sonuç üretti (`KLT-01e`, `KLT-01b`, `KLT-01d`).
Bundan sonra her concurrency adayı **şu sırayla** değerlendirilecek:

| # | Soru | KLT-01'de kaçırılan gerçek örnek |
|---|---|---|
| 1 | **Gerçek bir Update/Edit yazma yolu var mı?** | Yakıt, stok belgeleri, muayene, personel unvanları → **düzenleme yolu hiç yoktu** |
| 2 | **Aynı kayda aynı anda kaç farklı aktör erişebiliyor?** | `CompanyService` → yalnız süper admin; kişisel şablon → yalnız sahibi |
| 3 | **Mevcut bir concurrency / CAS / transaction / state-machine koruması var mı?** | `ChangeStatus` → durum makinesi (`from==to → false`); yakıt iptali → `WHERE ... is_deleted=0` atomik CAS |
| 4 | **Mimari olarak LWW veya başka bir çakışma politikası BİLİNÇLİ seçilmiş mi?** | Lookup tabloları → `BusinessSyncService` LWW; `CLAUDE.md` §4 LWW yasağı **tanımları kapsamıyor** |
| 5 | **Offline / senkronizasyon davranışı nedir?** | Servise kilit eklemek senkron LWW'siyle **iki farklı çakışma politikası** yaratabilir |
| 6 | **Gerçek veri kaybı veya bütünlük etkisi nedir?** | Tek alanlık rename → yine geçerli bir değer; yetki kümesi → **tüm küme siliniyor** (güvenlik) |
| 7 | **Çakışma senaryosu gerçekçi mi?** | Satır içi anlık düzenleme (saniyeler) ↔ dakikalarca açık duran yetki ağacı |
| 8 | **Mevcut testler bu davranışı gerçekten kapsıyor mu?** | Test **adlarını ve içeriğini** oku — `grep "Concurrency\|version\|Lock"` yetmez (Türkçe adlar kaçar) |

**Ancak bu sekiz adımdan sonra** *"düzeltilmesi gereken concurrency açığı"* kararı verilir.

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
| 2026-08-10 | **KLT-01a** | **`ChangeStatus` zaten korumalı** — `BeginImmediate` + durumu transaction İÇİNDE okuma + durum makinesi (`CanTransition`, `from==to` → **false**). İki kullanıcı aynı geçişi yaparsa ikincisi reddediliyor. Gerçek açık **`UpdateShipmentInfo`**'da: durum makinesi yok, 3 alan **körlemesine** yazılıyor | ❌ *"RequestOperationsService'te ChangeStatus ve UpdateShipmentInfo sessizce eziyor"* — **ChangeStatus için YANLIŞTI** |
| 2026-08-10 | KLT-01a | Masaüstü bu ekranda servisi **DOĞRUDAN** çağırıyor (yerel SQLite, çevrimdışı çalışır) — KLT-01c'deki sunucu-otoriteli yetki ekranından **farklı**. Sürüm jetonu yerel kayıttan gelmeli | ❌ *"tüm ekranlar aynı yoldan geçer"* varsayımı — bu ekranda **farklı** |
| 2026-08-10 | **KLT-01d** | Üç servisin **yalnız biri** gerçek hedef çıktı. `PersonnelTitleService` → **Update/Rename metodu YOK** (tek `UPDATE` soft-delete, `AND is_deleted=0` atomik CAS'li). `CompanyService.Update` → 10 alan körlemesine ama modül `IsSuperAdminOnly` → **erişen 1-2 kişi**, çakışma gerçekçi değil. `MaterialTemplateService.Update` → 12 alan körlemesine; `EnsureManageable` kişisel şablonu **tek sahibine** kilitliyor → gerçek çakışma yalnız **genel şablon + iki admin** senaryosunda. **Kapsam daraltıldı: yalnız `MaterialTemplateService.Update`** | ⚠️ *"üçünde de version++ var, expectedVersion yok → üçü de düzeltilmeli"* — **ÜÇTE İKİSİ YANLIŞTI.** Ölçüt üç şeyi görmüyordu: (a) düzenleme yolu var mı, (b) mimari politika ne, (c) **kaç aktör erişebiliyor**. → §12.5 kalıcı kuralı bu yüzden eklendi |
| 2026-08-10 | **KLT-01b** | `LookupService.Rename` **gerçekten** körlemesine yazıyor (LWW) — mekanizma tespiti doğruydu. **AMA** bu tablolarda (`units`, `brands`, `suppliers`, `material_categories`, `vehicle_types/categories/models`) LWW **mimari olarak bilinçli seçilmiş politikadır**: `BusinessSyncService.cs:14` *"updated_at varsa LWW (yalnız daha yeni/eşit yazma uygulanır)"* + `Tables` listesinde *"ebeveyn lookup/tanımlar (LWW: web daha yeni düzenlediyse ezilmez)"*. `CLAUDE.md` §4 LWW'yi yalnız **stok/sayaç/yakıt/bakım/onay** için yasaklıyor — **tanımlar kapsam dışı**. → Kural ihlali değil, **İPTAL** | ⚠️ *"version artırılıyor + expectedVersion yok = düzeltilmeli"* — **ÖLÇÜT EKSİKTİ.** Bu mekanik ölçüt tek başına düzeltme gerektiren bir concurrency problemi anlamına **gelmez**; **mimari çakışma politikasının da incelenmesi gerekir.** KLT-01e'de bu ölçüt *"koruma var mı"* sorusunu kaçırmıştı; KLT-01b'de *"koruma **gerekli mi**"* sorusunu kaçırdı |
| 2026-08-10 | **KLT-01e** | Yakıt ve stok iptal/ters kayıt korumaları **zaten kapsamlı test ediliyor**: `FuelCancelTests` (14 test, çift iptal + yetki + tenant + negatif bakiye dahil), `StockConcurrencyTests` (11 test, CAS + retry + iptal), `StockOperationTests` (idempotent ikinci iptal, transfer geri alınamaz, eşzamanlı çıkış). **KLT-01e'nin gerekçesi ortadan kalktı → İPTAL EDİLDİ** | ❌ *"Yakıt/stok korumaları çalışıyor ama TEST EDİLMİYOR"* — **YANLIŞTI.** Sebep: tarama mekanikti (`grep "Concurrency\|version"`); testler Türkçe adlandırıldığı için (`Iptal_edilen_kayit_TEKRAR_IPTAL_EDILEMEZ`) aramaya takılmadı. **Ders: dosya adlarına değil, TEST ADLARINA bakılmalı.** |

| 2026-08-10 | **SNK-01** | **Planlanan koruma KODDA ZATEN VAR.** `BusinessSyncPushService.cs:55` → `if (localV <= pushWm) return;` — yerel değişiklik yoksa snapshot hiç üretilmiyor, push HTTP isteği hiç atılmıyor. Mekanizma **`c8d3dc7` commit'i ile 2026-07-19'da** eklenmiş; plan ise 2026-08-10'da yazıldı → plan **22 gün geriden** geliyordu. Yapılacak kod değişikliği **yok**, performans kazancı **sıfır**. Boştaki gerçek trafik (tick başına **5** istek: `/health`, `/api/machines/register`, `/api/me/authsig`, `/api/sync/business-version`, `/api/sync/conflicts/unseen`) push'tan değil **aralığın kendisinden** kaynaklanıyor → bu **`SNK-02`**'nin konusu. → **İPTAL** | ⚠️ *"Yerel değişiklik yoksa push HTTP isteği hiç yapılmasın"* (DURUM: BEKLEMEDE) — **ZATEN YAPILMIYORDU.** Planın *"15 sn'de ~5-6 istek, çoğu boşa"* tespiti **doğru**; ama önerdiği çare (push'u atla) **zaten uygulanmıştı** ve o 5 isteğin **hiçbirine** dokunmuyor. **Ders: bir plan maddesi, kodun mekanik olarak aranmasıyla değil, GERÇEK DAVRANIŞ ve ÇAĞRI AKIŞI (timer → hangi metotlar → hangi HTTP istekleri) baştan sona izlenerek değerlendirilmelidir.** Bu kez hata "koruma yok" sanmaktı; oysa koruma vardı ve `git log -S` ile tarihi bile bulunabiliyordu |

> **Kural:** Plan yanlış çıkarsa **sessizce düzeltilmez** — "önceki varsayım yanlıştı → kod
> incelemesi sonucu gerçek durum budur" biçiminde kaydedilir. Yukarıdaki tablo bunun kaydıdır.
>
> ⚠️ **Kalıcı ders (2026-08-10, dört kez tekrarlandıktan sonra — KLT-01e, KLT-01b, KLT-01d, SNK-01):**
> Bir plan maddesine başlamadan önce **"bu iş zaten yapılmış olabilir mi?"** sorusu sorulur.
> Doğrulama yöntemi: ilgili çağrı akışını uçtan uca izle (tetikleyici → metotlar → G/Ç) **ve**
> `git log -S "<ilgili kod parçası>"` ile kodun geçmişine bak. Plan tarihi, kodun o kısmının
> son değişim tarihinden **eski** olabilir.

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
