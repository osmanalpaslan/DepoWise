# I — Bildirim Merkezi · ANALİZ RAPORU (kod yazılmadı)

> Tarih: **2026-08-28** · Roadmap: FAZ 4 / SIRA 9 (MASTER_ROADMAP §1 — "Uyarılar genişletmesi")
> Bu belge SALT ANALİZDİR: kod / migration / deploy / canlı veri değişikliği YOKTUR.
> Uygulama, kullanıcının PK-I kararlarından SONRA ayrı turda yapılır.

---

## 1. Mevcut altyapı (kod taraması, 2026-08-28) — yeniden kullanılacak parçalar

**Bildirim Merkezi'nin çekirdeği ZATEN VAR.** Roadmap'in "Uyarılar genişletmesi" tanımı birebir doğru:

| Parça | Ne yapıyor | Yeniden kullanım |
|---|---|---|
| `DashboardService.GetSummary` | 4 kaynaktan (bakım eşik %85/95/100 · muayene/sigorta ≤30 gün/geçmiş · düşük stok · yakıt %20) uyarıları **HER ÇAĞRIDA TÜRETİR** — DB'de uyarı satırı YOK | Yeni kaynaklar AYNI desenle eklenir |
| Kaynak başına `AccessControl.Can(...)` kapısı | Bakım yetkisi olmayan bakım uyarısı GÖRMEZ (yan kapı yok deseni MEVCUT) | Aynen |
| `DashboardAlert` | `Key = Kind\|EntityId\|Title` (kalıcı kimlik) + `Signature = Detail` (hal imzası) + `NavigateKey` (kaynağa git) + `IsCritical` | Aynen |
| `alert_reads` (Migration031) | Kullanıcı-bazlı OKUNDU işareti: `UNIQUE(user_id, alert_key)` + upsert; **imza değişirse (kötüleşme) okundu düşer, uyarı yeniden görünür** | Aynen — okundu/okunmadı modeli HAZIR |
| "Uyarılar" ekranı (web `Alerts.razor` /alerts + masaüstü `AlertsView`) | Kategori butonları (sayılı) + kritik ayrımı + tıkla→kaynağa git; ana ekranda okundu gizlenir, bu ekranda aktif olduğu sürece kalır | Ekran genişletilir, yenisi açılmaz |
| `alerts` ekran/modül kaydı | AppScreens'te mevcut (Both; menüde 🔔 Uyarılar) | Aynen |

**Eksik olanlar (I'nin işi):** üst barda çan/sayaç iki platformda YOK · evrak/iş emri/talep gibi yeni
modüllerin uyarı kaynağı YOK · "tümünü okundu yap" YOK · Uyarılar ekranında okundu/okunmadı görünümü YOK.

**Takvim ile sınır (madde 10):** Takvim = "ne zaman ne var" (tarih ekseni, nötr). Bildirim = "dikkat
gerektiren durum" (eşik/gecikme). İkisi AYNI kaynak verileri okur ama kopyalamaz; Bildirim Merkezi
Takvim'in yerine geçmez, Takvim'i değiştirmez.

## 2. Önerilen mimari — TÜRETİLMİŞ bildirim + okundu-işareti (mevcut modelin genişletilmesi)

**Madde 3'ün cevabı: bildirimler FİZİKSEL KAYIT OLMAMALI.** Gerekçe:

- **Tekilleştirme/idempotency (madde 14) kökten çözülür:** üretim yok → kopya diye bir şey OLAMAZ.
  Aynı olay her hesaplamada aynı `Key`'i verir; okundu upsert `UNIQUE(user_id, alert_key)` ile teklidir.
  Fiziksel tabloda ise her senkron turu/ekran açılışı için üretici + tekilleştirici + temizlik işi
  gerekirdi (en riskli model).
- Bildirim HEP GÜNCEL: kaynak düzelince (bakım yapıldı, stok doldu) bildirim kendiliğinden KAYBOLUR —
  fiziksel kayıtta "kapatma/temizleme" mantığı yazmak gerekirdi.
- Kullanıcı durumu (okundu) zaten ayrı küçük tabloda (`alert_reads`) ve MEVCUT; imza modeli
  "kötüleşince yeniden göster" davranışını bedavaya verir. Kapat/ertele v1'e GEREKMEZ (aşağıda §13).
- **Migration GEREKMEZ** (aşağıda §11).

Uygulama biçimi: `DashboardService.GetSummary` içindeki uyarı bloğu, yeni kaynak blokları EKLENEREK
genişletilir (mevcut 4 kaynak bloğuna dokunulmaz); `AlertKind` enum'una yeni değerler SONA eklenir
(mevcut değerler/serileştirme değişmez). Paralel "NotificationService" kurulmaz.

## 3. Bildirim kaynakları (madde 2) — envanter ve v1 önerisi

| Kaynak | Türetilebilir mi? | Sorgu/kural | v1 önerisi |
|---|---|---|---|
| Bakım · Muayene/Sigorta · Düşük stok · Yakıt | ✅ MEVCUT | — | Aynen kalır |
| **Evrak geçerlilik** | ✅ `file_records.valid_until` ≤30 gün / geçmiş (muayene sabitiyle aynı eşik) | `DocumentService.List` (iki kapı + kapsam İÇİNDE) | **EVET** |
| **Geciken iş emri** | ✅ `planned_end` geçmiş VE durum terminal değil | `WorkOrderService.List` (BranchAccess İÇİNDE) | **EVET** |
| **Bekleyen talep** | ✅ onay bekleyen talepler (sayısı dashboard'da ZATEN var — kalemleştirilir) | mevcut sorgu | **EVET** |
| Açık satın alma siparişi | ✅ ama "beklenen teslim tarihi" alanı YOK → yalnız "açık sipariş var" denebilir (gecikme hesaplanamaz) | — | Seçime bağlı (PK-I1) |
| Yaklaşan el ile takvim kaydı | ✅ ama Takvim zaten gösteriyor; madde 10 sınırını bulanıklaştırır | — | Seçime bağlı (PK-I1, önerim HAYIR) |
| Zimmet | ❌ vade/iade tarihi alanı YOK (Migration076) — tarih bazlı bildirim ÜRETİLEMEZ | — | KAPSAM DIŞI (alan eklemek ALTER ister — istenmez) |
| İş emri durum DEĞİŞİMİ ("X emri tamamlandı") | Olay bazlıdır — türetilmiş modelde "olay akışı" değil "durum" gösterilir | — | KAPSAM DIŞI (fiziksel kayıt/olay günlüğü gerektirir; ihtiyaç görülürse ayrı iş) |

## 4. Üretim mekanizması (madde 4)

**İstek anında hesaplama** (mevcut model): kullanıcı ekranı açınca / sayaç tazeleyince hesaplanır.
Gerçek zamanlı push YOK · periyodik sunucu job'ı YOK · kuyruk YOK (madde 13 — erken optimizasyon
kurulmaz; canlıda tek firma, veri küçük, mevcut GetAlerts zaten böyle çalışıyor ve sorunsuz).

## 5. Web / masaüstü / offline davranışı (madde 5)

- **Masaüstü:** bakım · muayene · stok · yakıt · iş emri · talep YEREL veriden → **çevrimdışı tam
  çalışır**. **Evrak** sunucu-otoritelidir → çevrimdışıyken evrak bildirimi ÜRETİLEMEZ (Takvim/Projeler
  emsali: sessiz atlama + istenirse "çevrimiçi gerekli" notu). Okundu işaretleri yerel SQLite'a yazılır.
- **Web:** tüm kaynaklar sunucuda; okundu işaretleri sunucu DB'sine.
- Üst bar sayacı iki platformda aynı kaynağı okur (aşağıda §8).

## 6. Senkron (madde 8)

**Bildirimlerin kendisi SENKRONLANMAZ** — senkronlanacak bildirim tablosu yoktur. Kaynak veriler zaten
taşınıyor → **her cihaz kendi bildirimlerini kendi verisinden üretir** (artı: çevrimdışı tutarlı, paket
büyümez, kopya riski sıfır; eksi: cihazlar kaynak verisi eşitlenene kadar farklı liste görebilir —
mevcut uyarı sistemi bugün de böyle ve sorun yaratmadı).
`alert_reads` BUGÜN senkronlanmıyor → okundu işareti CİHAZ-YERELDİR (web'de okunan masaüstünde okunmamış
görünür). Bunu değiştirmek `alert_reads`'e eklemeli ALTER (updated_at/version/is_deleted) + sync satırı
ister → **PK-I4**.

## 7. Yetki / BranchAccess / tenant (madde 6-7, 18)

- **Çift kapı mevcut desenle:** her kaynak bloğu `Can(s, kaynakModülü, View)` ile sarılı (bugün de öyle);
  yeni kaynaklarda ek güvence: evrak `DocumentService.List` (iki kapı + şube/proje kapsamı İÇİNDE),
  iş emri `WorkOrderService.List` (BranchAccess İÇİNDE) → **kapsam dışı şubenin iş emri/evrakı bildirim
  üzerinden SIZAMAZ** (Takvim'dekiyle aynı kanıt testleri yazılır).
- Bakım/muayene/stok/yakıt kaynakları BUGÜN firma-genelidir (araç/stokta şube kapsam boyutu mevcut
  sistemde böyle) — I bu davranışa DOKUNMAZ (değiştirmek ayrı iştir).
- **Tenant:** tüm kaynak sorguları company_id'li; `alert_reads` user_id'li (oturumdan) — mevcut.
- **Yetki modülü (madde 18):** mevcut **`alerts`** ekranı/modülü kullanılır — yeni `notifications`
  modülü GEREKMEZ. Ekranın kendisi içerik taşımaz; içerik kaynak-yetkisiyle süzülür (çift kapı asıl
  güvencedir). Yeni modül açmak yalnız yetki ağacına bir satır ekler, güvenlik katmaz → **PK-I3**.

## 8. UI önerisi (madde 9)

- **Üst barda çan 🔔 + okunmamış sayacı** — iki platformda (web MainLayout + masaüstü Shell üst barı;
  bugün ikisinde de YOK). Tıkla → Uyarılar ekranı. Sayaç = aktif VE okunmamış bildirim sayısı.
- **Uyarılar ekranı genişler** (yeni ekran AÇILMAZ): yeni kategoriler (Evrak · İş Emri · Talep) ·
  okundu/okunmadı görünür ayrımı + "okundu işaretle" · **"Tümünü okundu yap"** · mevcut "kaynağa git"
  korunur · "Tümü" görünümü (bugün kategori seçmek zorunlu).
- Kapat/ertele YOK (v1) — "okundu" ana ekran/sayaç gizlemesi yapar; kaynak düzelince bildirim zaten düşer.
- Öncelik (madde 15): mevcut **İKİLİ model (kritik/normal — `IsCritical`) yeterli**; üçlü seviye
  (bilgi/uyarı/kritik) v1'e alınmaz (yeni değer katmaz, tüm kaynaklara seviye atama işi doğurur).
- Kullanıcı tercihleri (madde 16): v1'e ALINMAZ — kategori filtre butonları ihtiyacı karşılıyor;
  tercih saklama yeni tablo/senkron işi doğurur.

## 9. Performans (madde 13)

Kaynak sorguları bugünkü dashboard'la aynı sınıfta (indeksli, firma-filtreli, küçük veri). Yeni üç
kaynak birer hafif SELECT. Sayaç için ayrı hafif uç (`/api/alerts/count` benzeri) — tam listeyi değil
yalnız sayıyı döndürür; web'de oturum başına bir kez + elle/yenilemede tazelenir (her sayfa geçişinde
yeniden HESAPLANMAZ). Cache/queue KURULMAZ; ölçülmeden indeks eklenmez (protokol §8).

## 10. Tekilleştirme / idempotency (madde 14)

Türetilmiş modelde üretim yok → kopya İMKANSIZ (yapısal). Okundu: `UNIQUE(user_id, alert_key)` +
`ON CONFLICT ... DO UPDATE` (mevcut) → tekrar işaretleme kopya satır üretmez. "Tümünü okundu yap" aynı
upsert'ün döngüsüdür. İmza modeli: hal kötüleşince okundu otomatik düşer (mevcut davranış korunur).

## 11. Migration gereksinimi (madde 12)

**PK-I4 = HAYIR (önerilen) ise: MIGRATION HİÇ GEREKMEZ — şema 80'de kalır.** Mevcut tablolara ALTER
gerekmez; yeni tablo gerekmez (`alert_reads` var, bildirimler türetilmiş).
PK-I4 = EVET ise: `alert_reads`'e yalnız EKLEMELİ kolonlar (updated_at/version/is_deleted — ADD COLUMN,
Migration074 emsali) + sync listesine 1 satır; bit-bit kanıt testleri standart.

## 12. Test planı (madde 19 — kod yazılmadan)

`BildirimTests` (hedef ~14-16 senaryo): her yeni kaynak için üretim eşiği (eşik altı üretmez / yaklaşan /
geçmiş üretir) · geciken iş emrinde terminal durum üretmez · **yan kapı: kaynak modül yetkisi yoksa o
kategori sızmaz** (files/work_orders/requests tek tek) · **BranchAccess: kapsam dışı şubenin iş
emri/evrakı bildirime çıkmaz** · **tenant** · okundu işaretle→sayaç düşer→imza kötüleşince yeniden
görünür · tümünü-okundu · **idempotency: iki kez hesaplama aynı Key kümesi; okundu upsert kopya satır
üretmez** · **kaynak kayıtlar bit-bit değişmez (hesaplama salt-okunur)** · masaüstü offline: belge
servisi yokken evrak kategorisi sessiz boş · **senkron: kaynak veri taşınınca hedef cihaz AYNI bildirimi
kendisi üretir** (uçtan uca) · sayaç doğruluğu · mevcut 4 kaynağın davranışının DEĞİŞMEDİĞİ (regresyon:
dashboard/rapor test setleri). Migration çıkarsa bit-bit + statik kanıt standart.

## 13. Riskler ve kapsam dışı bırakılanlar

- **Risk (düşük):** `AlertKind`'a değer eklerken mevcut serileştirme — değerler SONA eklenir, mevcutlar
  değişmez; parite/dashboard testleri kilitler. Üst bar iki platformda ortak kabuk dosyalarına dokunur
  (MainLayout/Shell) — küçük, eklemeli diff.
- **Kapsam dışı (v1):** e-posta/SMS/WhatsApp/push (madde 17 — türetilmiş model ileride bir "gönderici"
  eklenmesine engel değil: gönderici aynı üretici fonksiyonu okur, ayrı iş) · ertele/kapat · kullanıcı
  tercihleri · üçlü öncelik · olay-bazlı bildirim (durum değişim akışı) · zimmet (vade alanı yok) ·
  eşik ayarları (30 gün sabit, muayene emsali) · bakım/stok kaynaklarının şube-kapsamlı hale getirilmesi
  (mevcut davranış korunur).

## 14. Uygulama fazları ve büyüklük

**Tahmini büyüklük: ORTA** (migration yok/en fazla 1 eklemeli; asıl iş UI + testler).
I1 kaynak genişletmesi (GetSummary'ye 3 blok + AlertKind) → I2 sayaç ucu + web çan/sayaç + Alerts.razor
genişletme → I3 masaüstü çan/sayaç + AlertsView genişletme → I4 (yalnız PK-I4=EVET ise) okundu senkronu →
I5 BildirimTests + hedefli regresyon + 3 Release build → I6 belge (I_BILDIRIM_01.md) + ADR + roadmap +
commit/push. **Deploy YOK.**

## 15. Sonraki roadmap işleri

I sonrası: **J — Duyuru** (FAZ 4/SIRA 10) · 7b Bakım-Ekipman genişletmesi hâlâ serbest sırada ·
yayın bekleyenler: Migration073..080 (+I'de çıkarsa 081).

---

## PK-I SORULARI — kullanıcı kararı bekleniyor

Karar bekleyen 4 soru ana rapordadır (PK-I1 v1 kaynak seti · PK-I2 UI kapsamı · PK-I3 yetki modülü ·
PK-I4 okundu senkronu). Kararlar gelmeden UYGULAMA BAŞLAMAZ.
