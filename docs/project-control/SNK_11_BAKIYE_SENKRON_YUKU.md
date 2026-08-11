# SNK-11 — Türetilmiş bakiyenin senkron paketinden çıkarılması · ✅ TAMAMLANDI

> 2026-08-11 · Ön koşul: `STK-07` (senkron sertifikasyonu) ✅
> **Kapsam:** yalnız **senkron paketi**. Tablo kaldırılmadı, offline mimariye dokunulmadı.

---

## 1. ETKİ ANALİZİ (kod yazmadan önce)

| Soru | Bulgu |
|---|---|
| `stock_balances` nerede senkrona giriyor? | **Tek yer:** `BusinessSyncService.Tables` (satır 45) + yetki eşlemesi `TableModule` |
| Push'a nasıl giriyor? | `BuildSnapshot` `Tables` üzerinde döner → otomatik |
| Pull'da uygulanıyor mu? | **Hayır** — masaüstü zaten `excludeTables` ile hariç tutuyordu (`BusinessSyncPullService`) |
| Sunucu pakete güveniyor mu? | **Hayır** — push ucu hemen ardından `RecomputeBalances` çağırıyor (`Program.cs`) |
| Cursor/sürüm hesabına giriyor mu? | Evet, `CompanyVersion` `Tables` üzerinde döner → çıkarılınca bakiye değişimi artık sürüm bumplamaz (**istenen davranış**, Test D) |
| Conflict/idempotency etkileniyor mu? | **Hayır** — bunlar `stock_movements` ve `operation_id` üzerinden çalışır |
| Yerel (masaüstü) kullanım | **14 sorgu** (`StockService`, `StockBalanceWriter`, `MaterialService`) — **dokunulmadı** |
| Sunucu (PostgreSQL) kullanım | Rapor/dashboard/liste sorguları — **dokunulmadı** |

➡️ **Sonuç:** paketten çıkarmak **hiçbir okuma/yazma yolunu bozmuyor**; taşınan değer zaten kullanılmıyordu.

## 2. YAPILAN DEĞİŞİKLİK (tek dosya, iki satır)

`src/DepoWise.Infrastructure/Sync/BusinessSyncService.cs`
- `Tables` listesinden `"stock_balances"` **çıkarıldı** (gerekçe yorumda).
- `TableModule` içindeki `["stock_balances"] = "stock"` eşlemesi **kaldırıldı** (artık gereksiz).
- Sınıf açıklamasındaki "türetilmiş stock_balances de taşınır" ifadesi **düzeltildi**.

**Yapılmayanlar:** yeni protokol/tablo yok · `stock_movements` şeması aynı · cursor mantığı aynı ·
conflict çözümü aynı · offline mimari aynı · Migration064 aynı · UI değişikliği yok.

## 3. ⚠️ BULGU — bileşik PK'lı TEK senkron tablosu buydu

`stock_balances`, senkron listesindeki **tek** "PK'sı `id` olmayan" tabloydu. Çıkarılınca generic
upsert'in `DbIntrospect.PrimaryKey` yolu **senkronda** artık kullanılmıyor. Kod silinmedi (yerel
`StockBalanceWriter` aynı PK'ya dayanıyor) ve yetenek **ayrı bir testle** kilitlendi
(`SNK11_Bilesik_PK_Tespiti_Calismaya_Devam_Ediyor`).

## 4. DEĞİŞTİRİLEN MEVCUT TESTLER (gerekçeli — gevşetme DEĞİL)

| Eski test | Neden değişti | Yerine ne geldi |
|---|---|---|
| `Apply_IdOlmayanPk_StockBalances_Calisir` | "Bakiye taşınır ve uygulanır" davranışını kilitliyordu; bu davranış **bilinçli kaldırıldı** | `SNK11_Bakiye_Senkron_Paketinde_TASINMAZ_Hareketler_Tasinir` + `SNK11_Bilesik_PK_Tespiti_Calismaya_Devam_Ediyor` |
| `Apply_NegatifStokBakiyesi_Uygulanir` | Negatif **bakiye satırının** senkronda uygulanmasını test ediyordu | `SNK11_Negatif_Acilis_Defter_Uzerinden_Tasinir` — asıl garanti (devralınan eksik stok kaybolmaz) **defter** üzerinden kilitlendi (ADR-086) |
| `GeriCekme_HaricTutulanTablo_Uygulanmaz` | `stock_balances` ile kuruluydu → artık pakette olmadığı için test **anlamsız** (her zaman geçerdi) | Aynı mekanizma **`personnel`** ile yeniden kuruldu (gerçekten taşınan tablo) |

## 5. YENİ TESTLER — `SyncBalancePayloadTests` (7 senaryo)

1. Paket defteri taşır, bakiyeyi **taşımaz** (+ `Tables` listesi doğrulaması)
2. **Fayda:** 50 bakiye satırı varken pakette bakiye bölümü **yok**
3. (Test A) Normal senkron → sunucu doğru lokasyon kırılımını üretir
4. (Test C) Kasten bozuk bakiye (999) **sunucuya bulaşmaz**
5. (Test D) Yalnız bakiye değişirse paket taşımaz · **yerel okuma çalışmaya devam eder**
6. **Çevrimdışı akışların tamamı** çalışıyor: giriş · çıkış · ters kayıt · transfer · sayım ·
   STK-08 dağıtımı · bakiye kırılımı görüntüleme
7. (Test B + E) offline→online→offline→online: **kopya yok**, yakınsıyor

## 6. DOĞRULAMALAR

| Doğrulama | Sonuç |
|---|---|
| Build | **0 hata** |
| Test | **1325 · 1292 geçti · 0 kaldı · 33 atlandı** (taban 1317; **+7 yeni**, 3 mevcut test yeniden yazıldı) |
| SQLite | Tüm çevrimdışı akışlar yerel veritabanında koşuyor ✅ |
| **İzole PostgreSQL (üretim kopyası)** | Paket **1807,1 KB** · **663 hareket** taşınıyor · **663 bakiye satırı TAŞINMIYOR** ✅ |
| **Ölçülen fayda** | Taşınmayan bakiye verisi: **~86 KB** (663 satır) — her senkron turunda |

## 7. AÇIK RİSK (küçük, kayda geçti)
`CompanyVersion` artık bakiye satırlarının `updated_at`'ini saymıyor. Bir istemcinin elinde eskiden
bakiyeden gelmiş daha yüksek bir cursor varsa, ilk turda sunucu sürümü ondan küçük görünüp pull
atlanabilir. **Zararsız:** çekilecek yeni veri zaten yoktur ve ilk gerçek değişiklik sürümü yukarı taşır.

## 8. AÇIK KALAN İŞLER (silinmedi)
`BKM-04` · `RPR-01` · `STK-09` · `STK-10` · `STK-11 (veri artığı)` · `SNK-12` ✅ tamam
