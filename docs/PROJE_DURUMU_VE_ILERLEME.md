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
| FAZ 1 | Senkron optimizasyonu + parite (SNK-01…04, PRT-01, PRT-02) | 🔵 **AKTİF** — senkron (SNK-01…04) BİTTİ · **PRT-01 Grup 1 (stok) ✅** `8bf27cb` · sırada PRT-01 Grup 2 |
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
| ~~SNK-04~~ | ~~Günlük yedeği senkron turundan ayırma~~ | 1 | ❌ **ZATEN YAPILMIŞ / İPTAL** *(2026-08-10)* — saatlik koruma `b2604de` ile mevcut | — | — | ✅ | — | — | — |
| **PRT-01** | Tam ekran parite denetimi | 1 | 🔵 **DEVAM EDİYOR** — envanter + Grup 1 (stok) ✅, kalan 5 grup | P1 | — | ✅ | kısmi | ✅ 1057/1024/0/33 | `8bf27cb` |
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

## 6.3 ❌ `SNK-04` — ZATEN YAPILMIŞ / İPTAL (2026-08-10)

**Durum: `ZATEN YAPILMIŞ / İPTAL`** — planın istediği koruma kodda **zaten mevcuttu**.

**Plan ne diyordu:** *"`MaybeDailyBackupAsync` her 15 sn'de çalışıyor; saatte bir yeterli."*

**Koddan kanıt:**

| Kanıt | İçerik |
|---|---|
| `ShellViewModel.cs:410` | Metodun **İLK** satırı: `if ((DateTime.UtcNow - _lastBackupCheck).TotalHours < 1) return;` |
| `git log -S "_lastBackupCheck"` | **`b2604de` · 2026-07-11** — tek commit |
| `git log -S "MaybeDailyBackupAsync"` | **`b2604de` · 2026-07-11** — **aynı** commit |

Saatlik koruma, `MaybeDailyBackupAsync` metodunun **oluşturulduğu commit'ten beri** mevcut;
sonradan kaldırılıp geri konmamış. Plan **2026-08-10**'da yazıldı → **plan yazılmadan önce
zaten karşılanmıştı** (bir ay geriden geliyordu).

**Gerçek çalışma akışı — iki katmanlı koruma:**
1. **Saatlik kısıt** (satır 410): saatteki 240 tick'in **239'u** anında dönüyor.
2. **Günlük kısıt** (`hasToday`): bugün yedek varsa iş yapılmıyor.

15 sn'de gerçekten çalışan iş: **bir `DateTime` çıkarma + karşılaştırma**. Pahalı işler
(yetki kontrolü, `ListBackups()` disk taraması, yedekleme, buluta yükleme) **zaten saatlik
kısıtın arkasında**.

**Sonuç:** Kod değişikliği **yapılmadı** · yeni test **gerekmedi** ·
**`SNK-02` ve `SNK-03` davranışları değiştirilmedi** · migration/API/`.csproj`/bağımlılık yok.

*(Kapsam dışı, `SNK-04`'ün sonucu DEĞİL — analiz sırasında yolun üstünde görüldü, iş açılmadı:
bulut yüklemesi başarısız olursa istisna sessizce yutuluyor ve `hasToday` **yerel** yedeğe
baktığı için o gün tekrar denenmiyor.)*

---

## 7. SIRADAKİ İŞ

**⏳ KULLANICI KARARI BEKLİYOR — kod işi başlatılmadı.**

`KLT-01` kapandı; FAZ 0'ın **kod tarafı bitti**. Kalan iki FAZ 0 maddesi (`GUV-01`, `DOG-01`)
kullanıcı aksiyonudur, Claude tamamlayamaz.

**`SNK-01` analiz edildi ve İPTAL edildi (2026-08-10)** — koruma kodda zaten vardı (§13).
**`SNK-02` uygulandı ve kapandı (2026-08-10)** — bkz. §6.1 (HTTP QA doğrulama sınırı dahil).
**`SNK-03` uygulandı ve kapandı (2026-08-10)** — bkz. §6.2.

**`SNK-04` analiz edildi ve ZATEN YAPILMIŞ / İPTAL olarak kapatıldı (2026-08-10)** — bkz. §6.3.

### ✅ FAZ 1 — senkron optimizasyonu (SNK-01…04) TAMAMLANDI
`SNK-01` ❌ · `SNK-02` ✅ · `SNK-03` ✅ · `SNK-04` ❌ — dört maddenin tamamı sonuçlandı.

### 🔵 `PRT-01` DEVAM EDİYOR — Grup 1 (stok) tamamlandı, commit **`8bf27cb`** (2026-08-10)

**Envanter (koddan):** Web 43 sayfa / 47 route · Masaüstü 38 menü hedefi. Web'de olup masaüstünde
olmayan **7 ekranın yedisi de `IsSuperAdmin`** kapılı → **kasıtlı**, kusur değil. Kolon kataloğu
**tek dosya** (web aynı dosyayı `Compile Include` ile derliyor) → kolon paritesi yapısal garanti.
Yetki modülleri 12 ekranın 11'inde birebir aynı.

**Grup 1 — Stok Giriş-Çıkış · Hareketler · Sayım:** 18 kategori karşılaştırıldı, 9 fark bulundu,
**6'sı giderildi** (`8bf27cb`, 6 dosya, +257/−27).

| Bulgu | Durum | Doğrulama düzeyi |
|---|---|---|
| **G1-01** web'de bakiye gösterilmiyordu | ✅ | **gerçek tarayıcı QA** — "Mevcut stok: 137.5" = API |
| **G1-03** sayımda fark=0 gönderilmiyordu | ✅ | **gerçek HTTP QA** — fark=0 raporda, adjustment yok |
| **G1-04** web'de alt kategori yoktu | ✅ | **gerçek tarayıcı QA** — kaskad + kayıtta alt ID |
| **G1-05(a)** web `operationId` göndermiyordu | ✅ | **gerçek HTTP QA** — aynı jeton, bakiye 1 kez düştü |
| **G1-07** hata sessizce boş liste görünüyordu | ✅ | **gerçek tarayıcı QA** — uyarı çıktı, sonra temizlendi |
| **G1-02** masaüstünde toplu sayım yoktu | ⚠️ | **Kod + servis/veri katmanı doğrulandı; masaüstü sepet UI davranışı GUI üzerinde GÖZLENEMEDİ** |

`StockService` / `ReportService` **değişmedi** · migration/`.csproj`/dependency/`tests` **yok** ·
API sözleşmesi yalnız **genişledi** (opsiyonel `OperationId`).

**⏳ Grup 1'den AÇIK KALANLAR:** `G1-06` (başarı mesajları, P3) · `G1-08` (son düzeltmeler listesi,
P3) · `G1-09` (Yön kolonu, P3 — değişiklik **önerilmedi**) · **hareketsiz belge idempotency boşluğu**
(tamamı fark=0 sayım `stock_movements` üretmediği için aynı jetonla ikinci belge oluşabilir —
`StockService` değişikliği ister, kapsam dışı) · **G1-02 GUI QA'nın 6 senaryosu**.

**Kalan gruplar:** 2b Şablonlar · 3 Bakım+Yakıt · 4 Talepler · 5 Araç/Muayene/Personel/
Günlük · 6 Yönetim ekranları.

### 🔵 `PRT-01` GRUP 2a — Malzemeler (2026-08-10): analiz TAMAM, `G2-04` uygulandı

**8 bulgu** çıkarıldı. Uygulama **aşama aşama** ve **her aşama ayrı onayla** yürüyor.

| Bulgu | Konu | Durum |
|---|---|---|
| `G2-04` | Hızlı düzenleme malzemenin **şablon bağını siliyordu** (web **ve** masaüstü) | ✅ **UYGULANDI** — ⚠️ **commit EDİLMEDİ**, çalışma ağacında duruyor |
| `G2-02` | Web ana düzenleme formu **düzenleme kilidi göndermiyor** | ✅ **UYGULANDI** — ⚠️ commit edilmedi |
| `G2-03` | `PUT /api/materials/{id}` **`equivalentIds`'i yok sayıyor** | ✅ **UYGULANDI** — ⚠️ commit edilmedi |
| `G2-01` | Web'de **tam düzenleme formuna giriş yolu yok** (muadil/uyumlu araç/foto web'den değiştirilemiyor) | ✅ **UYGULANDI** — ⚠️ commit edilmedi |
| `G2-05` | Masaüstünde **"Yalnız kritik"** filtresi yok (servis + testi hazır) | ⏳ onay bekliyor |
| `G2-06` | Kritik stok paneli çapraz eksik (P3) | ⏸️ **değişiklik önerilmedi** |
| `G2-07` | Düzenlemede boş "Tür" varsayılanı platformlar arası farklı | ⏳ **ürün kararı** (§11) |
| `G2-08` | `Materials.razor`'da ölü kod | 📝 **yalnız kayıt** (§12) — `_v`/`CS0169` kısmı `G2-02` ile **kapandı** |

**`G2-04` doğrulaması:** Build 0 hata · **1058 test / 1025 başarılı / 0 başarısız / 33 atlanan**
(önceki taban 1057/1024/0/33 → **+1 test, sıfır kırılma**) · yeni uyarı yok.

### ✅ `G2-02` — web tam düzenleme formunda düzenleme kilidi (2026-08-10)
`Materials.razor`: ölü `_v` alanı (`CS0169`) **amacına uygun** kullanıldı (`int`→`long`), sürüm
`BeginEdit`'te okunup `PUT` gövdesinde gönderiliyor; **yeni kayıtta gönderilmiyor**. 409'da masaüstünün
kanıtlanmış deseni: **"Kaydı yenile" / "Formda kal"** — `Clear()` ve yönlendirme **çalışmıyor**.
`ApiClient`'a **dokunulmadı** (ham JSON `WEB-01`'in konusu, Seçenek A).
**+2 HTTP testi** (`ApiEditLockTests`) · **1060/1027/0/33** · uyarı **14 → 13** (`CS0169` giderildi).
**Tarayıcı QA:** A/B çakışması gerçek tarayıcıda koşuldu — 409 çıktı, A'nın yazdıkları korundu,
"Formda kal" formu kapatmadı, "Kaydı yenile" alanları güncel değerlerle doldurdu, sonrasında kaydetme
başarılı oldu. Sunucudaki kayıt hiçbir aşamada bayat veriyle **ezilmedi**.

### ✅ `G2-03` — `PUT` muadil uzlaştırması (2026-08-10)
**Yalnız `Program.cs` yetmedi:** `SetCompatibleVehicles`'ın **simetriği olan servis metodu eksikti**.
Eklenen `MaterialService.SetEquivalents` **tek transaction**: tüm hedefler önce doğrulanır (bir tanesi
bile yabancıysa **hiçbiri** yazılmaz), sonra bu malzemeye dokunan bağlar **iki yönde** silinip yeni
liste simetrik yazılır. `Program.cs`'e `is not null` koşullu tek çağrı — **`Update`'ten SONRA**, böylece
409'da muadillere hiç dokunulmaz (`G2-02` korunur).
**`null` ≠ `[]` semantiği** `VehicleIds` ile birebir aynı: `null` = dokunma (hızlı düzenleme pencereleri
bu alanı göndermez), `[]` = hepsini kaldır.
**+6 HTTP testi** (yeni `ApiMaterialEquivalentTests.cs`) · **1066/1033/0/33** · yeni uyarı yok.

### ✅ `G2-01` — web tam düzenleme formuna giriş yolu (2026-08-10)
Form kodda hep vardı, **onu açan kontrol yoktu** (`EditNav` ölüydü); üstelik hızlı düzenleme penceresi
kullanıcıyı **var olmayan** bir "Düzenle" düğmesine yönlendiriyordu.
- Hızlı düzenleme penceresine **"Tam Düzenleme"** düğmesi (`Auth.CanEdit` ile) → `fulledit` sonucu →
  mevcut `EditNav`. Yanıltıcı ipucu metni gerçeğe uyduruldu. Ekstra onay **yok** (kullanıcı zaten bastı).
- **YETKİ KAPISI DÜZELTİLDİ:** form iki yerde `CanCreate`'e bağlıydı → düzenleme yetkisi olup oluşturma
  yetkisi olmayan kullanıcı **bomboş sayfa** görüyordu. Artık **yeni kayıt = Create, düzenleme = Edit**
  (masaüstündeki `CanWrite`/`CanEdit` ayrımının aynısı).
- Sayfa başlığı düzenlemede "Malzeme — Yeni Kayıt" yazıyordu → **"Malzeme — Düzenle"**.

**⚠️ Uygulama sırasında çıkan ZORUNLU düzeltme (planda yoktu):** `/materials` ile `/materials/new`
**aynı bileşene** bağlı (`@page "/materials"` + `@page "/materials/{Section}"`). Blazor bileşeni
yeniden oluşturmadığı için `OnInitializedAsync` **tekrar çalışmıyordu** → form **boş açıldı**.
`EditNav` ve kayıt sonrası listeye dönüş `forceLoad: true` ile tam sayfa yüklemesine çevrildi.
İkincisi **önceden var olan ama ulaşılamayan** bir kusuru da kapatıyor: kaydettikten sonra liste
dönen göstergede kalırdı.

**Test:** yeni otomatik test **yazılmadı** (değişiklik UI/gezinme + yetki görünürlüğü katmanında;
API/servis davranışı değişmedi). Regresyon: **1066/1033/0/33 — değişmedi**, build 0 hata / 13 uyarı.
**Tarayıcı QA (izole, `127.0.0.1`, canlıya sıfır istek), GERÇEK KULLANICI YETKİLERİYLE:**

| Kullanıcı | Beklenen | Gözlenen |
|---|---|---|
| **admin** (tüm yetkiler) | düğme görünür, form dolu açılır | ✅ `Kapat / Sil / Tam Düzenleme / Düzelt`; form doldu, başlık "Malzeme — Düzenle"; kaydetme sonrası **listeye döndü ve liste render oldu** |
| **qa_edit** (View+**Edit**, Create YOK) | düğme görünür, form **dolu açılır** | ✅ `Kapat / Tam Düzenleme / Düzelt` (**Sil yok** — Delete yetkisi yok); form kod/ad/muadille **doldu**. *Düzeltmeden önce burada boş sayfa gelirdi.* |
| **qa_edit** → `/materials/new` (yeni kayıt) | form **açılmamalı** | ✅ Kaydet düğmesi yok, form yok |
| **qa_view** (yalnız View) | düzenleme düğmesi **görünmemeli** | ✅ pencerede **yalnız "Kapat"**; URL ile `?edit=` zorlansa da form **açılmadı** |
**İzole gerçek HTTP QA (web):** aynı `PUT` iki gövde şekliyle koşuldu — `templateId` **gönderilince
bağ korundu**, gönderilmeyince `null`'a düştü (kusur birebir üretildi). 18 isteğin tamamı
`127.0.0.1`'e gitti, **canlıya tek istek gitmedi**.
⚠️ **Masaüstü:** *"Kod + servis/test doğrulandı; masaüstü GUI davranışı gözlenemedi"* — `G1-02`'deki
Avalonia GUI otomasyon sınırı sürüyor.

**Silme derin denetimi:** malzeme silme koruması **gerçek ve tek noktalı** (`MaterialService.Delete`);
UI gizleme değil **veri katmanı** koruması; elle API çağrısı atlatamaz. Yakıt tabloları ve
`daily_activities` `material_id` **taşımıyor** → o taraf için kontrol gerekmiyor.
⚠️ Silme SOFT olduğu için **FK hiç devreye girmez** → tek güvence `GuardDeletable` (bkz. `MLZ-01-DEPO`).

---

**Sıradaki aday: `G2-05` (masaüstüne "Yalnız kritik" filtresi) — sonra `G2-07` karar kapısı.**
*(`G2-01` ✅ tamamlandı; Grup 2a'nın kalan tek kod işi `G2-05`.)*
Sıra gerekçesi: `G2-01` (formun kapısını açmak) **en sonda**, çünkü `G2-02` ve `G2-03` çözülmeden
form açılırsa bugün gizli olan iki sessiz hata kullanıcıya açılır.
Ardından **Grup 2b (Şablonlar)** — **henüz analiz edilmedi**, ayrı analiz aşaması olarak yürütülecek.

**Başlamadan önce gereken:** her aşama için ayrı kullanıcı onayı (kapsam koddan doğrulanır —
plan varsayımları `KLT-01`'de üç, `SNK-01` ve `SNK-04`'te birer kez yanlış çıktı;
bkz. §12.5 ve §13'ün altındaki kalıcı ders).

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
❌ SNK-04 ZATEN YAPILMIŞ (saatlik koruma b2604de ile mevcut, 2026-07-11)
   ↓
✅ FAZ 1 — SENKRON OPTİMİZASYONU (SNK-01…04) TAMAMLANDI
   ↓
🔵 PRT-01 Grup 1 (stok) ✅ 8bf27cb — kalan 5 grup  ◄ SIRADAKİ: Grup 2 (Malzemeler)
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

### 🗂️ Uzun vadeli gereksinimler — "şimdi DEĞİL, ama kayıtta" (2026-08-10 gözden geçirmesi)

Kullanıcının 2026-08-10'da ayrıntılandırdığı ürün hedefleri **tek tek plana bağlandı**. Hiçbiri bu
turda kodlanmadı. Aşağıdaki tablo *"bu konu unutuldu mu?"* sorusunun kalıcı cevabıdır:

| Kullanıcı gereksinimi | Nerede kayıtlı | Faz | Durum |
|---|---|---|---|
| Masaüstü **sürekli aktif bağlantı** | `KARAR-5` + plan §7 `Y-5` | — | ❌ **Bilinçli olarak YAPILMAYACAK** — analizde gereksiz bulundu; yerine `SNK-02`/`SNK-03` (seçici kadans + backoff) ✅ uygulandı |
| **Kayıt kilitleme** (A düzenlerken B giremesin, kilit sahibinin adı görünsün) | plan §5 `KLT-02` / `KLT-03` / `KLT-04` | 3 | ⏳ Şema hazır tasarlandı: `record_locks` + `acquire`/`heartbeat`/`release`; **süresi geçen kilit otomatik düşer** (logout/çökme/bağlantı kopması senaryosu KARŞILANIYOR) |
| **Kapsamlı yetki ağacı** (ekran + işlem bazlı) | `YET-01` (kapı) → `YTK-01…04` | 2 | ⏳ **Baştan yazılmayacak** — `Approve`/`Report` için enum değil **modül** eklenir (`request_approval` deseni, Migration035) |
| **Tüm kullanıcılar web'e login** | H-3 + `DOG-01` | 0 | ⏳ **Kodda rol kısıtı BULUNAMADI** → muhtemelen "giriş engeli" değil, deny-by-default yüzünden **boş menü**. `DOG-01` bunu gerçek kullanıcıyla doğrulayacak; **yetki açığı oluşturacak değişiklik YAPILMADI** |
| **Birim / personel yapısı** (`Firma→Şube→Birim→Kullanıcı`) | `BRM-01` | 2 | ⏳ Bugün `personnel.title` var, **birim yok** |
| **Bakım → onay → stok düşümü** | `BKM-01` / `BKM-02` / `BKM-03` + `KARAR-4` | 5 | ⏳ Bugün bakımda onay **yok**. Hazır desen: `material_requests` durum makinesi (Draft→Pending→…) aynı şekilde kullanılabilir |
| **Günlük Faaliyet kayıt tipleri** (yönetilebilir + yetkilendirilebilir) | **`GNL-03` 🆕** → `YTK-02` → `GNL-02` | 2 | ⏳ **YENİ EKSİK BULUNDU:** `activity_type` bugün **sabit metin** (`maintenance\|movement`); tip listesi olmadan yetki verilemez |
| **Mükerrer kayıt uyarısı** ("Kaydı Görüntüle" / "Yine de Devam Et" + tekrar tetiklememe + log) | `GNL-01` | 5 | ⏳ Zaten ayrıntılı planlı: **sunucu taraflı** kontrol, `allowDuplicate` bayrağı, UNIQUE kısıtı KONULMAZ |
| **Ayrıntılı audit log** (önceki/yeni değer, uyarı, verilen cevap) | `LOG-01` + **`LOG-02` 🆕** | 6 | ⏳ **YENİ:** `audit_logs.before_json/after_json` **şemada var ama doldurulmuyor** → ucuz kazanım |
| **Şube bazlı malzeme silme izolasyonu** | **`KARAR-7` 🆕** (plan §14) + `MLZ-01-DEPO`/`STK-05` | 4 kapısı | ⏳ **KULLANICI KARARI GEREKİYOR** — `KARAR-1` ("katalog firma geneli") ile **çelişiyor** |
| **Otomatik güncelleme** (sormadan indir/kur, yalnız yeniden başlatmayı sor + Ertele) | `GNC-01` + [UPDATE_CONTRACT.md](UPDATE_CONTRACT.md) | 6 | ⏳ Altyapı büyük ölçüde **var** (indirme+checksum+yedek+rollback+yüzde). Eksik: çalışan ikilinin fiziksel değişimi + "Ertele/hatırlat" akışı. **Code-signing** ücretli kalem (#3) |
| **Performans / ölçeklenme** | H-9 + **`PRF-01` 🆕** + `Y-1`, `Y-6` | 6 | ⏳ **YENİ:** darboğaz haritası yazılı değildi. `PRF-01` **ücretsizdir** (ölçüm+belge, kod yok) |
| **Yatırım sonrası profesyonel altyapı** | plan §7 `Y-1…Y-7` + [MALIYET_KALEMLERI.md](MALIYET_KALEMLERI.md) | — | ⏳ Para kalemleri tek dosyada; **fiyat uydurulmuyor** |
| **Çöp/ölü kod temizliği** | H-12 · `TMZ-01`/`TMZ-02`/`TMZ-03` · `G2-08` | 6 | ⏳ **Refactor uğruna çalışan sistem bozulmaz.** Riskliyse dokunulmaz, **kayıt altına alınır** |

---

## 11. BEKLEYEN KULLANICI KARARLARI

**İşletme / iş kuralı kararları (kullanıcıya ait):**
| Konu | Soru |
|---|---|
| KARAR-4 | Bakımda "negatif stok serbest" kuralı, "stok düşümü onaya bağlı" akışıyla çelişiyor. Onay beklerken stok düşmeyecekse negatif stok serbestliği ne anlama gelecek? |
| **KARAR-7** 🆕 | **Malzeme KARTI firma geneli mi kalsın (bugünkü `KARAR-1`), yoksa şube bazlı mı olsun?** Şube bazlı olursa aynı malzemenin firmada birden çok kartı oluşur → rapor, muadil, talep/bakım eşleşmesi ve stok toplamları kökten etkilenir. **`STK-01` başlamadan önce karara bağlanmalı.** |
| **G2-07** 🆕 | Düzenlemede türü boş olan eski kayıt açılınca varsayılan ne olsun — masaüstündeki **"Diğer"** mi, web'deki **"Yedek Parça"** mı? (İkisi bugün farklı; kaydedilince veriye yazılıyor.) |
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
| **G2-08** | **`Materials.razor`'da ölü kod** (2026-08-10, PRT-01 Grup 2a): `_v` alanı (derleyici **`CS0169`** ile zaten uyarıyor), `DeleteSelected`, `DeletePhoto`, `OpenDetail`/`_detailPhotos`, `ApplyTemplate`/`_templates` — hiçbiri markup'tan çağrılmıyor (yan detay paneli ve şablon seçici kaldırılınca kalmışlar). **Kullanılmama nedeni doğrulandı.** ⚠️ `OpenDetail` **`OpenQuickEdit` içinden çağrılıyor** → tamamen ölü DEĞİL; kör silme yapılmamalı | 📝 **YALNIZ KAYIT** — `G2-01` bu dosyaya zaten dokunacak; temizlik o iş sırasında **aynı dosyada** değerlendirilir. Tek başına refactor açılmayacak (H-12) |
| **ARC-01** | **`Vehicles.razor`'da `EditNav` ÖLÜ — araç tam düzenleme formuna web'den ULAŞILAMIYOR** (2026-08-10, `G2-01` analizinde bulundu). Malzemedeki `G2-01` kusurunun **birebir aynısı**: metot tanımlı ([Vehicles.razor:276](../src/DepoWise.Web/Components/Pages/Vehicles.razor:276)) ama hiçbir markup'a bağlı değil; listede yalnız çift-tık hızlı düzenleme var. Araç formunun yetki kapısının da `CanCreate`'e bağlı olup olmadığı **kontrol edilmedi** | ⏳ **AÇIK** — **`PRT-01` Grup 5 (Araç)** kapsamında ele alınacak. `G2-01`'de bilerek **dokunulmadı** (kullanıcı kararı). Çözüm deseni hazır: `G2-01`'in aynısı |
| **MUA-01** | **Muadil: TRANSİTİF gösterim ↔ DOĞRUDAN yazım uyuşmazlığı** (2026-08-10, `G2-03` analizinde bulundu). `GetEquivalentGroup` **BFS ile transitif** çalışır (A↔B, B↔C ⇒ grup(A)={B,C}), `GetDetail` bu transitif grubu "Muadiller" olarak gösterir. Yazma tarafı ise **doğrudan** çift üzerinde çalışır. Sonuç: A'nın listesinden **yalnızca transitif bağlı** bir malzeme çıkarılırsa silinecek doğrudan satır yoktur → **kullanıcıya "silinmiyor" gibi görünür**. Aynı sınır **masaüstü uzlaştırmasında da vardır** (yeni değil). Ayrıca web tam formu transitif grubu geri gönderdiği için kaydetme, transitif bağları **doğrudan satıra dönüştürür** (graf yoğunlaşır — zararsız ama davranış değişikliği) | ⏳ **ÜRÜN KARARI GEREKİR:** "muadil" bir **grup (transitif)** mu, malzeme başına **liste** mi? `G2-03`'te davranış **bilerek DEĞİŞTİRİLMEDİ**, yalnız kayda alındı |
| **MUA-02** | **`EnsureOwned` silinmiş malzemeyi kabul ediyor** — `SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c` (**`is_deleted` filtresi YOK**). Aynı dosyadaki `EnsureVehicleOwned` ise `is_deleted=0` kontrol ediyor → **asimetri**. Etki: soft-silinmiş bir malzeme muadil olarak eklenebilir; `GetDetail` gösterirken siliyor ama BFS silinmiş kaydın **üzerinden geçmeye devam ediyor** | 📝 **YALNIZ KAYIT** — `G2-03`'te davranış **değiştirilmedi** (yeni davranış icat edilmedi). Düzeltilecekse muadil/silme davranışıyla birlikte ele alınmalı (`MLZ-01` ailesi) |
| **AUD-01** | **`audit_logs.before_json` / `after_json` kolonları var ama neredeyse hiç doldurulmuyor** — `AuditWriter` destekliyor, çağıranların hemen hepsi `null` geçiyor (`AfterJson` yalnız `FileService` + `MaintenanceService`; `BeforeJson` **hiçbir yerde**). Sonuç: bugün "bu kayıtta ne değişti?" sorusu **cevaplanamıyor**. Ayrıca **Audit görüntüleme ekranı yalnız web'de var** (`Audit.razor`), masaüstünde yok | ⏳ **`LOG-02`** olarak plana eklendi (§6) · masaüstü eksiği `PRT-01` **Grup 6**'da denetlenecek |
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
| 2026-08-10 | **SNK-04** | **Planlanan koruma KODDA ZATEN VAR — ikinci kez.** `ShellViewModel.cs:410` → `if ((DateTime.UtcNow - _lastBackupCheck).TotalHours < 1) return;` metodun **İLK** satırı. Saatlik kısıt, `MaybeDailyBackupAsync`'in **oluşturulduğu commit'te** eklenmiş: **`b2604de` · 2026-07-11**; plan 2026-08-10'da yazıldı → plan **bir ay geriden** geliyordu. 15 sn'de gerçekten çalışan iş yalnız bir `DateTime` çıkarma+karşılaştırma; pahalı işler (yetki kontrolü, `ListBackups()` disk taraması, yedekleme, buluta yükleme) **zaten saatlik kısıtın arkasında**. Ayrıca ikinci katman günlük kısıt (`hasToday`) var. **Kod değişikliği yapılmadı, yeni test gerekmedi, SNK-02 ve SNK-03 davranışları değiştirilmedi.** → **ZATEN YAPILMIŞ / İPTAL** | ⚠️ *"`MaybeDailyBackupAsync` her 15 sn'de çalışıyor; saatte bir yeterli"* (DURUM: BEKLEMEDE) — **ZATEN SAATTE BİR ÇALIŞIYORDU.** Metot her tick'te **çağrılıyor** ama ilk satırında dönüyor; "çağrılıyor" ile "iş yapıyor" karıştırılmış. **Ders (SNK-01 ile aynı, ikinci kez doğrulandı): bir plan maddesi, çağrı akışı uçtan uca izlenerek ve `git log -S` ile kod geçmişine bakılarak değerlendirilmelidir.** |

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
| 2026-08-10 | **`PRT-01` Grup 1 (stok) kapandı** (`8bf27cb`) — 6 bulgu giderildi; `G1-02` masaüstü GUI QA yapılamadı, açıkça öyle kaydedildi. Doküman kapanışı `7bf4afa`. |
| 2026-08-10 | **`PRT-01` Grup 2a (Malzemeler) analizi** — 8 bulgu (`G2-01…G2-08`). Silme koruması derinlemesine denetlendi: **tek noktalı, veri katmanında, elle API çağrısı atlatamaz**; yakıt tabloları `material_id` taşımıyor. **`G2-04` uygulandı** (şablon bağı korunuyor), izole gerçek HTTP QA ile doğrulandı; masaüstü GUI gözlenemedi. **Commit edilmedi.** |
| 2026-08-10 | **`G2-02` + `G2-03` uygulandı.** `G2-02`: web tam formu artık `version` gönderiyor, 409'da "Kaydı yenile/Formda kal" (masaüstü deseni), ölü `_v` alanı amacına uygun kullanıldı → `CS0169` giderildi. `G2-03`: **yalnız `Program.cs` yetmedi** — `MaterialService.SetEquivalents` (tek transaction, `null`≠`[]`, çift yönlü, hepsi-veya-hiçbiri) eklendi. **1066/1033/0/33** (+8 test). İkisi de izole gerçek HTTP + tarayıcı QA ile doğrulandı. **Commit edilmedi.** Yeni teknik borç: **`MUA-01`** (muadil transitif↔doğrudan uyuşmazlığı — ürün kararı), **`MUA-02`** (`EnsureOwned` silinmiş malzemeyi kabul ediyor). İkisinde de **davranış bilerek değiştirilmedi**. |
| 2026-08-10 | **Grup 2a kod commit'i `ffbb995`** (G2-04 + G2-02 + G2-03, 11 dosya). Ardından **`G2-01` uygulandı** (commit edilmedi): "Tam Düzenleme" giriş yolu + **yetki kapısı düzeltmesi** (yeni kayıt=Create, düzenleme=Edit) + başlık. Uygulamada planda olmayan **zorunlu** bir düzeltme çıktı: `/materials` ↔ `/materials/new` aynı bileşen olduğu için `OnInitializedAsync` tekrar çalışmıyordu → `forceLoad` gerekti (kayıt sonrası liste dönüşündeki gizli kusuru da kapattı). Yetki ayrımı **gerçek kullanıcı yetkileriyle** tarayıcıda doğrulandı (admin / edit-only / view-only). Yeni teknik borç: **`ARC-01`** — `Vehicles.razor`'da **aynı ölü `EditNav`**, Grup 5'e bırakıldı. |
| 2026-08-10 | **Uzun vadeli gereksinim gözden geçirmesi (kullanıcının 17 maddesi).** Çoğunun **zaten `H-1…H-12` altında planlı** olduğu doğrulandı → mükerrer iş açılmadı. Gerçekten eksik çıkan **dört** konu eklendi: **`GNL-03`** (kayıt tipi kataloğu — `YTK-02`'nin önkoşulu), **`LOG-02`** (audit önceki/yeni değer), **`PRF-01`** (ölçek darboğaz haritası, ücretsiz), **`PRT-01` Grup 2b (Şablonlar)** ayrı analiz aşaması olarak işaretlendi. **`KARAR-7`** açıldı: şube bazlı malzeme silme isteği **`KARAR-1` ile çelişiyor** → kullanıcı kararı bekliyor. `Y-6`/`Y-7` ve maliyet kalemleri #9/#10 eklendi. **Hiç kod yazılmadı.** |
