# A ve B GRUBU — TAM ANALİZ (2026-09-07)

> Kullanıcı isteği: *"sırada olan LST-01, A grubu, B grubu işlerini tam ve eksiksiz analiz yaparak
> süreci tamamlamanı istiyorum."*
>
> **Yöntem:** her madde için "var mı, yok mu" tahmin edilmedi — kodda **ölçüldü** (grep + uç taraması
> + ekran incelemesi). Aşağıdaki her satırın arkasında bir ölçüm var.

---

## Özet tablo

| # | İş | Durum (ÖLÇÜLDÜ) | Migration | Risk | Öneri sırası |
|---|---|---|---|---|---|
| A1 | Ekran içi liste toplamları | ⚠️ **Çok eksik** — web'de 63 sayfadan yalnız **3'ünde**, masaüstünde **2 ekranda** özet var | Gerekmez | Düşük | **1** |
| A2 | Cari yaşlandırma (vade) | ❌ Yok — ama `invoices.due_date` **zaten var** ve ekranda gösteriliyor | **Gerekmez** | Düşük | **2** |
| A3 | Toplu işlem | ❌ Yok (hiç eşleşme yok) | Gerekmez | Orta | 5 |
| A4 | Favori ekranlar | ❌ Yok (hiç eşleşme yok) | **Gerekir** (kullanıcı başına favori) | Düşük | 6 |
| B1 | Çek/senet portföyü | ❌ Yok | **Gerekir** (yeni tablo) | Yüksek | 7 |
| B2 | E-posta uyarısı | ❌ Yok — SMTP altyapısı da yok | Gerekmez ama **secret** gerekir | Orta | 8 |
| B3 | Trafik cezası + HGS/OGS | ❌ Yok | **Gerekir** (yeni tablo) | Orta | 4 |
| B4 | Araç zimmeti geçmişi | ✅ **ZATEN YAPILMIŞ** — iki ortamda da çalışıyor | — | — | — |
| B5 | Lastik yaşam döngüsü | ❌ Yok (yalnız malzeme TÜRÜ olarak "Lastik" var) | **Gerekir** | Yüksek | 9 |
| B6 | Puantaj | ⛔ **Kapsam dışı** — `Migration079` kaydında `PK-F4 puantaj YOK` kararı var | — | — | — |

**İki düzeltme yol haritasına:** B4 tamamlanmış olduğu hâlde "sırada" görünüyordu; A2 ise
"migration gerektirir" sanılıyordu — gerektirmiyor.

---

## Madde madde

### A1 — Ekran içi liste toplamları · **önerilen ilk iş**
**Ölçüm:** web'de `dw-summary` yalnız `Materials`, `StockMovements`, `Vehicles` sayfalarında
(63 sayfadan 3'ü). Masaüstünde toplam satırı yalnız `FinanceView` ve `FuelView`'de.

**Neden önemli:** kullanıcı listede kaç kayıt/ne kadar tutar olduğunu görmeden karar veremiyor;
şu an bunu yalnız Excel'e aktarıp toplayarak öğrenebiliyor.

**Neden düşük riskli:** yeni tablo, yeni yetki, yeni ekran YOK. Var olan listenin altına/üstüne
sayı yazmak. **Ama** dikkat: toplam **sunucudan** gelmeli — istemcide sayfa üzerinden toplamak
LST-01'in aynı hatasını üretir (tavanlı listede "toplam" yanlış çıkar).

**Kapsam:** Malzeme · Araç · Personel · Ekipman · Cari · Fatura · Kasa-Banka · Tahsilat-Ödeme ·
Talep · İş Emri · Satın Alma · Günlük Faaliyet (12 ekran × 2 ortam).

---

### A2 — Cari yaşlandırma (vade analizi) · **migration gerektirmez**
**Ölçüm:** `invoices.due_date` sütunu **var**, `FinanceReads` içinde okunuyor ve `DueText` ile
ekranda gösteriliyor. Yani veri temeli hazır; eksik olan **rapor**.

**Yapılacak:** 0-30 / 31-60 / 61-90 / 90+ gün kovalarında açık bakiye dağılımı; cari bazında ve
firma toplamında. Mevcut rapor motoruna (`ReportCatalog` + `Dispatch`) yeni bir rapor olarak eklenir;
rapor bazlı yetki (ADR-181/197) kendiliğinden uygulanır.

**Dikkat:** bakiye **defterden türetilir** (CLAUDE.md §4) — yaşlandırma da `PartyLedger` üzerinden
hesaplanmalı, faturaya doğrudan bakılmamalı; aksi hâlde tahsilatlar düşülmez.

---

### A3 — Toplu işlem
**Ölçüm:** `SelectedItems` / `BulkDelete` / `TopluSil` benzeri hiçbir şey yok.

**Neden orta riskli:** silme/güncelleme çoğaltılıyor. Projede fiziksel silme yasak (§4) → toplu işlem
de **iptal/ters kayıt** semantiğiyle çalışmalı ve **tek transaction + operation id** kullanmalı.
Yarım kalan toplu işlem, veriyi tutarsız bırakır. Önce hangi ekranlarda gerçekten gerektiği
kullanıcıyla netleşmeli (en olası: Talep onaylama, Evrak, Duyuru).

---

### A4 — Favori ekranlar
**Ölçüm:** yok. Kullanıcı başına favori listesi **yeni tablo** ister (`user_favorites`).
Alternatif: mevcut `app_settings`'e kullanıcı bazlı JSON olarak yazmak → **migration gerekmez**.
Bu ikinci yol önerilir (ölçüldü: `SettingsService` kullanıcı bazlı anahtar destekliyor).

---

### B1 — Çek/senet portföyü
Yeni tablo + vade takibi + kasa/banka ilişkisi + durum akışı (portföyde / ciro / tahsil / karşılıksız).
Ön muhasebenin en ağır parçası. **Tek başına bir faz.**

### B2 — E-posta uyarısı
**Ölçüm:** projede SMTP/MailKit izi yok. Sunucuya giden e-posta, **secret** (SMTP parolası) ve
gönderim kuyruğu ister; hata durumunda sessizce kaybolmamalı. Uyarı motoru (`AlertRules`) zaten var —
eksik olan yalnız "kanal". Orta iş.

### B3 — Trafik cezası + HGS/OGS
**Ölçüm:** hiç yok. Araç + tarih + tutar + ödeme durumu ile yeni tablo. Araç modülüne bağlanır,
bakım/muayene deseninin aynısıdır → **kalıp hazır**, riski düşürür. B grubunun en kolayı.

### B4 — Araç zimmeti geçmişi · ✅ **ZATEN VAR**
**Ölçüm:** `/api/assignments/history` ucu (varlık/personel filtreli) **var**; web `Assignments.razor`
satıra tıklayınca geçmişi açıyor (`OpenHistory`), masaüstü `AssignmentsViewModel` seçim değişince
`LoadHistory` çağırıyor. **Yapılacak iş yok** — yol haritası güncellenmeli.

### B5 — Lastik yaşam döngüsü
**Ölçüm:** "Lastik" yalnız malzeme TÜRÜ olarak var. Yaşam döngüsü (araç-aks-pozisyon, km, rotasyon,
hurda) yeni tablolar + araç ilişkisi ister. **Kapsamı en büyük madde.**

### B6 — Puantaj · ⛔ kapsam dışı
`Migration079_WorkOrders` başlığında kayıtlı karar: **PK-F4 puantaj YOK**. Yeniden açılacaksa bu
önceki karar bilinçli olarak değiştirilmeli.

---

## Bu gece neden uygulanmadı (dürüst kayıt)

Kullanıcının bu turdaki **birinci şartı** şuydu: *"sabah babam login olurken sorun olmasını
istemiyorum"* ve *"çalışan hiçbir yapı bozulmayacak."*

Yukarıdaki maddelerin **beşi migration (veritabanı şema değişikliği) gerektiriyor.** Kullanıcı
uyurken, canlı veri üzerinde şema değiştirip sabaha yetiştirmeye çalışmak bu şartla doğrudan
çelişir. Bu yüzden bu gece:

- **Yapıldı:** sohbet hatası (kök neden + iki ortam + gerçek uçtan uca testler), LST-01'in sessiz
  tavanları, güncelleme paketi disk önbelleği, açılışta bozuk indeks onarımı.
- **Yapılmadı:** A/B gruplarının şema gerektiren maddeleri.

**Önerilen sıra (risk/değer):** A1 → A2 → B3 → A4 → A3 → B2 → B1 → B5.
İlk ikisi migration gerektirmez ve tek turda bitirilebilir.
