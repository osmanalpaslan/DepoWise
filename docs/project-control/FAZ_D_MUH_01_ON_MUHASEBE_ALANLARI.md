# FAZ D — `MUH-01`: ön muhasebe alan hazırlığı

> **Durum:** 🔵 SÜRÜYOR · başlangıç **2026-09-04**
> **İş tanımı (yol haritası):** para hareketi doğuran her kayda **cari + maliyet merkezi + belge**
> alanları (malzeme alışı, yakıt, bakım, şantiye gideri).

---

## 1. Önce ölçüldü — üç eksenin durumu birbirinden ÇOK farklı çıktı

Yol haritası üç ekseni tek cümlede topluyor, ama bugünkü gerçek üçünde de ayrı:

| Eksen | Bugünkü durum | Kalan iş |
|---|---|---|
| **Maliyet merkezi** | Mimari karar zaten verilmiş ve uygulanmış: kolon değil **dış bağ tablosu** (`cost_center_links`, ADR-168). "Alan eklemek" burada **yanlış** olurdu | Kolon değil **kapsam**: bağlanabilir kayıt türleri eksikti |
| **Belge** | Stok belgesinde tam (`invoice_no` · `order_slip_no` · `credit_slip_no`), yakıt depo girişinde kısmen | Yakıt dağıtımı ve bakımlarda belge no yok |
| **Cari** | `parties` + `party_ledger` var (ADR-…/M066) ama para doğuran kayıtlar ona **bağlanmıyor**; yakıt/satınalma eski `suppliers` tablosunu kullanıyor | En büyük boşluk — kendi adımında ele alınacak |

**Önemli mimari kısıt (M066'da yazılı, korunuyor):** *"Mevcut `suppliers` DOKUNULMAZ; veri TAŞINMAZ.
İsteğe bağlı `parties.supplier_id` ile EŞLEME kurulur."* Cari adımı bu kurala uyacak — `suppliers`
alanları değiştirilmeyecek, yanına opsiyonel cari bağı gelecek.

---

## 2. `MUH-01a` — maliyet merkezi kapsamı ✅ TAMAM (2026-09-04, ADR-210)

### 🔴 Bulunan tuzak

`POST /api/equipment-maintenance` ucu maliyet merkezi bağını **yazmaya çalışıyordu**
(`svc.CostCenters.Link(s, "equipment_maintenance", …)`), ama `equipment_maintenance` tipi
`CostCenterService.Entities` sözlüğünde **yoktu** → `Link` `ArgumentException` atar.

Çağrı `try/catch` içinde de değildi (masaüstü aynı çağrıyı sarmalıyor). Sonuç şu olurdu:

> Bakım **kaydedilir**, sonra uç **hata döner** → kullanıcı "kaydedilmedi" sanıp tekrar dener →
> **mükerrer bakım kaydı**.

**Bugüne kadar tetiklenmedi**, çünkü hiçbir arayüz bu alanı göndermiyordu. Yani yaşayan bir hata
değil, **ilk kullanan arayüzde patlayacak bir tuzaktı** — ve bu iş tam olarak o arayüzü ekliyordu.

### Yapılanlar

| # | İş | Not |
|---|---|---|
| 1 | `equipment_maintenance` kapsam sözlüğüne eklendi | Kapsam kolonu kardeşiyle **aynı** (`vehicle_maintenance` gibi boş) — amaç davranış değiştirmek değil, eksik tipi kapsama almaktı |
| 2 | **Özet raporuna** eklendi | Bağı yazmak yetmez: rapora düşmezse kullanıcı merkezi seçer, maliyeti hiçbir yerde göremez. Araç bakımıyla **aynı kategoride** toplanır ("Bakım Malzemesi") ki tek satır olsun |
| 3 | Masaüstü: Ekipman sekmesine **Maliyet Merkezi** alanı | Araç sekmesindekinden **ayrı** alan — ortak alan, araç için seçilen merkezin ekipman kaydına sessizce yapışması olurdu |
| 4 | Web: aynı alan, aynı ayrım | Sunucu DTO'su (`CostCenterId`) **zaten hazırdı**, gönderen arayüz yoktu |
| 5 | Testler: `MLY12` · `MLY13` · `MLY14` | Sırasıyla: bağ kurulabiliyor + tek-merkez kuralı · özete düşüyor · **kapsam sözlüğü hâlâ kapalı** |

`MLY14` bilinçli: kapsam listesi genişledi ama **açılmadı**. Aksi hâlde "listeye ekleyerek düzeltme"
alışkanlığı kapıyı tümden açardı.

### Yan düzeltme (davranış değişmedi)

`CostCenterService`'teki açıklama *"yakıt/bakım tablolarında şemada branch yok"* diyordu — bu
**yanlıştı**: `Migration027` o tabloların hepsine `op_branch_id` ekledi. Kolon **var**, burada
bilinçli olarak **kullanılmıyor**. Yalnız gerekçe düzeltildi.

**Migration GEREKMEDİ.** Doğrulama: ilgili 97 test **97/97** · masaüstü + web build 0 hata.

---

## 3. `MUH-01b` — belge alanları ✅ TAMAM (2026-09-04, ADR-211)

`Migration089_DocumentFields` — üç tabloya opsiyonel `invoice_no TEXT NULL`:
`fuel_distributions` (irsaliye/fiş no) · `vehicle_maintenances` ve `equipment_maintenances`
(fatura / servis fişi no). **Yalnız ekleme**; `NOT NULL` yok, backfill yok.

| # | İş | Durum |
|---|---|---|
| 1 | Migration089 (3 sütun, idempotent, iki lehçe) | ✅ |
| 2 | Servis katmanı: DTO + INSERT + okuma yolları (yakıt için İKİ okuma yolu) | ✅ |
| 3 | API DTO ve uçları (4 bakım çağrı noktası + 2 yakıt ucu) | ✅ |
| 4 | Masaüstü: 3 form alanı + yakıt listesine **BELGE NO** kolonu | ✅ |
| 5 | Web: 3 form alanı | ✅ |
| 6 | **Aranabilirlik** — belge no yakıt serbest metin aramasına dâhil | ✅ |
| 7 | `BelgeAlanlariTests` BLG1–BLG8 | ✅ |

**Senkron için ek iş gerekmedi:** `BusinessSyncService` `SELECT *` ile taşır ve uygularken gelen
kolonları tablonun gerçek kolonlarıyla kesişime sokar → yeni sütun kendiliğinden akar, eski istemci
sessizce yok sayar.

**Eskimiş iki test doğrultuldu (susturulmadı):** EM01/EM03 şema 85 kurup bugünkü servisi çağırıyordu;
bu bileşim gerçekte oluşamaz (istemci kendi kataloğunu açılışta uygular). Kayıt artık dönemin
şemasıyla SQL ile atılıyor. Sonuç daha güçlü — ayrıntı ADR-211.

## 4. `MUH-01c` — cari bağı · ⏳ SIRADA

Para doğuran kayıtların `parties`'e bağlanması. `suppliers` **değiştirilmeyecek** (M066 kuralı);
opsiyonel `party_id` yanına gelecek. En geniş adım — kendi tasarım turunu hak ediyor.
