# F — İŞ EMRİ · ANALİZ BELGESİ (F0 — karar öncesi)

> Tarih: **2026-08-28** · Durum: **ANALİZ TAMAM — PK-F ürün kararları bekleniyor** · Kod/migration YAZILMADI.
> Kararlar verilince: bu belge + kararlar → `F_ISEMRI_01.md` (uygulama kontrol belgesi) açılır.

## 1. Mevcut altyapı haritası (hedefli inceleme — tamamı bu oturumlarda kurulan/doğrulanan desenler)

| Altyapı | Yeniden kullanım | Mevcut tabloya dokunuş | Not |
|---|---|---|---|
| Personel / Araç / Ekipman | ✔ atama İLİŞKİ tablosuyla | **YOK** | Tek polymorphic atama tablosu (resource_type+resource_id — assignment/file_records emsali) |
| Proje/Şantiye/Saha (C) | ✔ | YOK | Kapsam anahtarı `branch_id`; proje şantiyeden TÜRETİLİR (C/P kararıyla tutarlı — bkz. PK-F5) |
| Malzeme/Stok | ✔ `IssueOutTx` (fatura/zimmet/satın alma emsali) | YOK | Tüketim = normal stok çıkışı `wo:<opId>` iziyle; idempotency STOK DEFTERİNDEN (STN3 deseni) |
| Zimmet (B) | Çakışma YOK | YOK | İş emri ataması ≠ zimmet (görev ↔ mülkiyet). İş emrinden zimmet tetiklenmez (ilk sürüm) |
| Bakım | BAĞ olarak ✔ | YOK | Mevcut ARAÇ bakım kaydı iş emrine dış-bağla bağlanır. Ekipman bakımı bugün İMKÂNSIZ (3 tabloda vehicle_id NOT NULL) — bkz. §7 |
| Satın Alma (P) | BAĞ olarak ✔ | YOK | Sipariş iş emrine dış-bağla iliştirilebilir (izlenebilirlik) |
| Maliyet Merkezi (D) | ✔ dış-bağ deseni | YOK | İş emri maliyet özeti D'nin YÖNTEMİYLE (C# decimal, tek kaynak: stok belgesi/bakım satırı) — D'nin kendisi değişmez |
| Evrak (A) | ✔ 1 harita satırı | YOK | `["work_order"]` → belge/foto/tutanak mevcut sistemle |
| Yetki | ✔ | YOK | Yeni `work_orders` modülü + iki kapı (tüketimde STOK kapısı da — STN5/ZMT9 emsali) + BranchAccess |
| Durum makinesi | Emsal ✔ (kopya değil) | YOK | `RequestOperationStateMachine` + `request_status_history` deseni; `RequestPriority` enum AYNEN yeniden kullanılır |
| Audit/Ekran logu/Trash/Excel/TRH-01 | ✔ | YOK | Standart satır eklemeleri (6 modülde kanıtlı desen) |
| Senkron | ✔ | YOK | FK sıralı yeni tablolar; kısmi paket analizi §6 |

**Sonuç: hiçbir mevcut tabloya ALTER dahi gerekmez; F tamamen yeni tablolar + mevcut kapı çağrılarıyla kurulur.**

## 2. Önerilen veri modeli (Migration079 — yalnız CREATE)

- **`work_orders`**: id · company_id · **wo_no** (firma içi benzersiz) · title · description ·
  **status** (`draft|assigned|in_progress|on_hold|completed|cancelled`) · **priority** (mevcut
  RequestPriority değerleri) · **branch_id** NULL (şantiye/saha — kapsam anahtarı) · cost_center_id NULL ·
  assignee_personnel_id NULL (ana sorumlu) · planned_start/planned_end/actual_start/actual_end NULL
  (İŞ GÜNÜ, ADR-162) · created_by · completed_by NULL · closing_note NULL · standart damgalar
  (created/updated/version/is_deleted). Gereksiz alan yok; hepsi (başlık+no hariç) opsiyonel.
- **`work_order_assignments`**: work_order_id · company_id · resource_type(`personnel|vehicle|equipment`) ·
  resource_id · note · damgalar. (Araç/ekipman/zimmet sistemlerine sıfır dokunuş.)
- **`work_order_links`**: work_order_id · company_id · entity_type(`stock_document|vehicle_maintenance|purchase_order`) ·
  entity_id · damgalar. (Tüketim belgeleri + bakım + sipariş bağları; maliyet özetinin kaynağı.)
- **`work_order_status_history`**: work_order_id · from_status · to_status · user_id · note · created_at.
  (Geçmiş DEFTERİ — request_status_history emsali; UPDATE ile durum ezilse bile kim/ne zaman izi kalır.)

## 3. Ekranlar (minimum)

- **"İş Emirleri"** ana liste (durum/öncelik/personel/şantiye filtreleri + arama + **Excel** — liste kuralı 2).
- **Tek güçlü DETAY** (ayrı ekran değil, listenin yan/alt paneli — Zimmet/Satın Alma düzeni):
  genel bilgiler + durum değiştirme + atamalar (personel/araç/ekipman) + **malzeme tüketimi**
  (IssueOut formu) + maliyet özeti + durum geçmişi. Belgeler Evrak ekranından bağlanır (detayda sayısı/kısayolu).
- Web + masaüstü parite; masaüstü YEREL (çevrimdışı — stok/zimmet gibi).
- **Mobil hazırlığı (§11):** API zaten kaynak-bazlı ve küçük gövdeli (durum değişikliği tek POST);
  liste DTO'ları kompakt; ileride responsive saha görünümü ek API istemez — bugünden ekstra iş GEREKMEZ.

## 4-5. Özellikler ve entegrasyonlar — kapsam ayrımı (§17)

**İlk sürümde KESİN:** iş emri CRUD + durum akışı + geçmiş defteri · personel/araç/ekipman atamaları ·
malzeme tüketimi (IssueOutTx, idempotent, maliyet merkezine otomatik bağ D deseniyle) · maliyet özeti
(malzeme + bağlı araç-bakım malzemesi; C# decimal) · Evrak bağı · Excel liste aktarımı · yetki+kapsam+senkron.
**Sonraki faza:** personel çalışma saati/puantaj · araç-ekipman kullanım metriği (sayaç) · Raporlar
menüsüne özel raporlar (liste+Excel ilk sürümde yeter) · zimmet tetikleme · alt/tekrarlayan iş emri ·
Takvim entegrasyonu (H fazının kendisi).
**Hiç yapılmamalı (bu ürün için):** çok seviyeli onay zinciri · SLA/eskalasyon motoru · vardiya planlama.

## 6. Senkron planı

Sıra (FK): `work_orders` → `work_order_assignments` → `work_order_links` → `work_order_status_history`,
listede **purchase_order_lines SONRASI** (kaynaklar: personnel/materials/equipment/cost_centers/
purchase_orders yukarıda; branches sunucu-otoriteli ayna — 3 modüldür aynı durum). TableModule → `work_orders`.
Kısmi paket: iş emri satırı stok belgesinden önce/sonra gelebilir — her ikisi idempotent, sonraki turda
tamamlanır (talep/stok ve satın almanın kanıtlı davranışı, STN11 emsali). LWW: başlık tanımsal (uygun);
tüketim zaten stok defterinde (LWW yasağı orada korunuyor); geçmiş append-only.

## 7. Bakım-ekipman ön işi (EKP PK-E2 notunun değerlendirmesi)

Teyit: bakım zincirinde `vehicle_id` 3 tabloda NOT NULL → **ekipman bakımı bugün yapılamaz**.
**Karar önerisi: F'yi BLOKLAMAZ, F'nin İÇİNE DE ALINMAZ.** Gerekçe: F ilk sürümü bakımı yalnız BAĞ olarak
kullanıyor (mevcut araç bakım kaydını iş emrine iliştirme) — ekipman bakımı olmadan tam çalışır.
Ekipman bakımı ayrı küçük iş olarak (bakım tablolarına eklemeli nullable equipment_id + servis dallanması
+ bakım ekranına hedef seçimi) F'DEN SONRA yapılabilir; yapılınca iş emri bağı otomatik kapsar
(entity_type zaten `vehicle_maintenance`). → PK-F9'da onayınıza sunuldu.

## 8. Test + veri güvenliği planı (§18)

`IsEmriTests`: CRUD+no benzersiz · durum geçiş matrisi + geçmiş defteri değişmezliği (bit-bit) ·
atama/çıkarma · **tüketim→stok düşer + `wo:` izi + İDEMPOTENT (çift stok yok)** · negatif stok kalkanı ·
**yetki: work_orders yok→ret; tüketimde STOK kapısı; yan kapı yok** · tenant · BranchAccess (okuma+yazma) ·
maliyet özeti doğru toplar (decimal, çift sayım yok) · senkron sıra/kapı + uçtan uca + tekrar-kopyasızlık ·
**Migration079 bit-bit + statik yalnız-CREATE**. Regresyon: stok/zimmet/bakım/araç/ekipman/satın alma/
maliyet/senkron hedefli paketleri (6 modülde kurulmuş filtreler). Canlıya sıfır yazma; deploy yok.

## 9. Uygulama fazları (§19)

F0 karar kilidi (bu belge + PK-F cevapları) → F1 Migration079 + kanıt testleri → F2 WorkOrderService +
API → F3 web ekranı → F4 masaüstü ekranı → F5 entegrasyon bağları (maliyet özeti/evrak/katalog satırları)
→ F6 senkron + IsEmriTests → F7 hedefli regresyon + 3 Release build → F8 kalıcı belge + roadmap + commit.
Her faz bir öncekini şart koşar; F1'e ancak PK-F cevaplarıyla girilir.

## 10. Kalıcı takip sistemi durumu (§13)

**MEVCUT ve çalışıyor** — yeni klasör yapısına gerek yok: `MASTER_ROADMAP.md` (sıra+durum+migrationlar+
kurallar) · modül kontrol belgeleri (PRJ/EVRAK/EKP/B_ZIMMET/D_MALIYET/P_SATINALMA — her biri "ne yaptık/
sınırlar/sonraki iş") · `docs/DECISIONS.md` (ADR-162..169) · `CURRENT_PHASE.md` (aktif durum).
"Sıradaki iş nedir?" sorusu bugün bu dosyalardan cevaplanabiliyor. F için aynı desen sürecek:
bu belge (F0) + kararlar sonrası `F_ISEMRI_01.md`.

## 11. ÜRÜN KARARLARI — cevaplarınız bekleniyor

**PK-F1 — Durum akışı ve kimler.** Öneri: `Taslak → Atandı → Devam Ediyor ⇄ Beklemede → Tamamlandı`,
her durumdan `İptal`. Kim ne yapar: oluşturma/atama = work_orders **Create/Edit** · başlatma/bekletme/
tamamlama = **Edit** · iptal = **Delete** yetkisi. AYRI onay katmanı YOK (talep zinciri onayı emsal —
orada da operasyon adımları modül yetkisiyle yürüyor). *Yanlış seçim etkisi:* onay katmanı sonradan
eklemek durum makinesine ekleme ister ama migration istemez (düşük risk).

**PK-F2 — Tamamlanan iş emri yeniden açılabilir mi?** Öneri: **HAYIR** — düzeltme yeni iş emriyle;
geçmiş defteri bozulmaz. Alternatif: yalnız admin "yeniden aç" (geçmişe iz düşerek). *Etki:* sonradan
açılabilir yapmak kolay; baştan serbest bırakmak disiplini bozar.

**PK-F3 — Malzeme tüketimi hangi kaynaktan?** Öneri: **depodan normal stok çıkışı** (IssueOut; depo
seçilir, negatif stok kalkanı çalışır, maliyet merkezi otomatik bağlanır). Zimmetten tüketim AYRI akış
olarak zaten var (Zimmet ekranı: kayıp/iade) — iki sistem karıştırılmaz. *Etki:* zimmet-tüketim köprüsü
istenirse sonra eklenir, model değişmez.

**PK-F4 — Personel çalışma saati/puantaj ilk sürümde var mı?** Öneri: **YOK** — yalnız atama listesi.
Saat kaydı ayrı büyük konu (puantaj backlog'da zaten var). *Etki:* sonradan work_order_assignments'a
eklemeli kolon/tabloyla gelir; migration küçük.

**PK-F5 — Proje ilişkisi.** Öneri: iş emri **yalnız şantiye/sahaya bağlanır** (branch_id); projesi
şantiyeden türetilir — C ve P'deki kararla tutarlı (üçlü gerçeklik yasağı). Alternatif: ayrıca project_id.
*Etki:* yanlış seçilirse (ikisi birden) aynı bilgi iki yerde tutulur ve raporlar çelişebilir.

**PK-F6 — İş emri numarası.** Öneri: **elle giriş + firma içi benzersiz** (sipariş no ile aynı desen).
Alternatif: otomatik numaralandırma (İE-2026-001). *Etki:* otomatik istenirse sonradan servis içinde
üretilebilir; şema değişmez.

**PK-F7 — Alt iş emri ve tekrarlayan (periyodik) iş emri?** Öneri: **İKİSİ DE İLK SÜRÜMDE YOK.**
Tekrarlayan işler H (Takvim) fazının doğal konusu. *Etki:* alt-iş-emri sonradan parent_id eklemeli
kolonuyla gelir (Projeler/branches emsali) — düşük risk.

**PK-F8 — Araç/ekipman KULLANIM kaydı (sayaç/saat) ilk sürümde var mı?** Öneri: **YOK** — yalnız atama.
Araç sayacı/yakıtı mevcut sistemlerden zaten akıyor. *Etki:* sonradan eklemeli.

**PK-F9 — Bakım-ekipman ön işi.** Öneri: **F'yi beklemez; F'den SONRA ayrı küçük iş** (§7 gerekçesi).
Alternatif: F'den önce yapılır (F 1-2 gün gecikir ama iş emri ekipman bakımını da bağlayabilir).
*Etki:* sonra yapılırsa hiçbir yeniden yazım gerekmez (bağ tipi hazır).

## 12. Roadmap işareti

MASTER_ROADMAP §1'de F: 🔵 **ANALİZ TAMAM — PK-F1..F9 kararları bekleniyor** (bu belgeye bağ).
Kararlar gelince F1 fazından uygulamaya geçilir.
