# ERPNext (Frappe) ve Odoo Community — Alpnex için kaynak kodu incelenebilir referanslar

Erişim tarihi: 2026-10-12. Yöntem: GitHub kaynak dosyaları (raw) doğrudan indirildi ve alan/fonksiyon listeleri çıkarıldı; resmî belgeler (docs.frappe.io, odoo.com/documentation, odoo/documentation deposu) okundu.
Etiketler: **[KAYNAK]** = kaynak kodda doğrulandı; **[BELGE]** = resmî belgede doğrulandı; **(unverified)** = doğrulanamadı, eğitim bilgisine dayanıyor. Pazarlama sayfası kullanılmadı.
Kaynak dalları: ERPNext/Frappe/HRMS `develop` dalı; Odoo `20.0` dalı (deponun varsayılan dalı, aşağıya bakın). Alan adları dosyada birebir yazıldığı gibidir.

---

## 1. Depolar, lisanslar, bakım durumu, mimari

### Takeaway
Her iki proje de 2026-10-12 itibarıyla çok aktif. ERPNext GPL-3.0 (Frappe çatısı MIT); Odoo Community LGPL-3.0. GPL kodu Alpnex'e (kapalı kaynak .NET) **kopyalanamaz**; yalnız tasarım/iş akışı fikri olarak incelenmelidir. Odoo LGPL de pratikte aynı: kod taşınmaz, fikir alınır.

### Cited Findings
- ERPNext deposu: github.com/frappe/erpnext; GitHub API lisansı `GPL-3.0`, varsayılan dal `develop`, son push 2026-10-12T16:26Z; son "latest" sürüm `v15.122.0` (2026-10-07). Sürüm sayfasında paralel v16 hattı da görülüyor (`v16.50.0` 06 Eki, `v16.37.0` 30 Eyl) — [GitHub API repos/frappe/erpnext](https://api.github.com/repos/frappe/erpnext); [Releases](https://github.com/frappe/erpnext/releases)
- ERPNext `license.txt` dosyası "GNU GENERAL PUBLIC LICENSE Version 3" ile başlıyor — [license.txt](https://github.com/frappe/erpnext/blob/develop/license.txt)
- Frappe Framework `LICENSE` = MIT License ("Copyright (c) 2016-2021 Frappe Technologies") — [frappe/LICENSE](https://github.com/frappe/frappe/blob/develop/LICENSE)
- Frappe son sürümleri: `v16.51.1` (2026-10-11), `v16.51.0` (2026-10-07), `v16.50.0` (2026-10-06) — [GitHub API frappe releases](https://api.github.com/repos/frappe/frappe/releases)
- Frappe HRMS (Vehicle Log burada) lisansı `GPL-3.0`, son push 2026-10-12 — [GitHub API repos/frappe/hrms](https://api.github.com/repos/frappe/hrms)
- Odoo deposu: github.com/odoo/odoo; varsayılan dal `20.0`, son push 2026-10-12T17:33Z; dallar arasında `saas-19.1…saas-19.4` var (yani Odoo 20 kararlı sürümü yayımlanmış görünüyor) — [GitHub API repos/odoo/odoo](https://api.github.com/repos/odoo/odoo); [branches](https://api.github.com/repos/odoo/odoo/branches)
- Odoo `LICENSE`: "Odoo is published under the GNU LESSER GENERAL PUBLIC LICENSE, Version 3 (LGPLv3)" — [odoo/LICENSE](https://github.com/odoo/odoo/blob/master/LICENSE)
- Odoo README: "a suite of web based open source business apps"; uygulamalar tek başına çalışabilir, birlikte kurulunca tam ERP olur — [github.com/odoo/odoo](https://github.com/odoo/odoo)
- Mimari (Frappe): her iş nesnesi bir "DocType" JSON tanımıdır (alanlar, `is_submittable`, `track_changes`, `autoname`); iş mantığı aynı klasördeki `.py` denetleyicide. Örnek: `erpnext/stock/doctype/stock_entry/stock_entry.json` — [stock_entry.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_entry/stock_entry.json)
- Mimari (Frappe arka plan işleri): RQ kuyrukları `short`, `default`, `long` (long zaman aşımı 1500 sn); `enqueue(..., enqueue_after_commit, job_id, deduplicate)` parametreleri — "deduplicate: do not re-queue job if it's already queued, requires job_id" — [frappe/utils/background_jobs.py](https://github.com/frappe/frappe/blob/develop/frappe/utils/background_jobs.py)
- Mimari (Odoo): Python ORM modelleri (`_name`, `_inherit` ile miras/mixin), ör. `maintenance.equipment` → `_inherit = ['mail.thread', 'mail.activity.mixin', 'maintenance.mixin']` — [addons/maintenance/models/maintenance.py](https://github.com/odoo/odoo/blob/20.0/addons/maintenance/models/maintenance.py)

### Inferences
- Frappe'nin "her şey meta veridir (DocType JSON)" yaklaşımı, Alpnex'in rapor tasarımcısı/özel alan ihtiyaçları için ilham verir; ancak Alpnex'in sıkı tipli .NET modeliyle birebir uyumlu değildir.
- Her iki projenin de çok sık sürüm çıkarması (ERPNext'te haftada birkaç yama) olgunluğu gösterir; fakat Alpnex için "referans tasarım" değeri, sürüm hızından bağımsızdır.

### Gaps
- Odoo 20'nin resmî yayın tarihi ve sürüm notları okunmadı (yalnız dal varlığı görüldü). Belgelerde 19.0 sayfaları kullanıldı; 20.0 belgeleri kontrol edilmedi.
- ERPNext v15 mi v16 mı "önerilen kararlı" — GitHub "Latest" etiketi v15.122.0'da; v16 durumu doğrulanmadı.

---

## 2. ERPNext Stok: Stock Entry türleri, Stock Ledger Entry, değerleme, geriye tarihli kayıt (Repost), sayım, seri/parti, yeniden sipariş, Putaway

### Takeaway
ERPNext'te stok bakiyesi doğrudan düzenlenmez; her stok etkili belge "Stock Ledger Entry" (SLE) satırları üretir ve her satır o anki `qty_after_transaction`, `valuation_rate`, `stock_value`, `stock_queue` değerlerini taşır. İptal edilen belgenin SLE'leri silinmez, `is_cancelled=1` işaretlenir. Geriye tarihli bir kayıt sonrasındaki tüm SLE'ler "Repost Item Valuation" belgesiyle arka planda yeniden hesaplanır. Alpnex'in "hareket defteri tek doğruluk kaynağı" ilkesiyle bire bir örtüşür ve en değerli referanstır.

### Cited Findings
- **Stock Entry Type** amaçları (`purpose`): Material Issue, Material Receipt, Material Transfer, Material Transfer for Manufacture, Material Consumption for Manufacture (kesik görünüyor, "Material Consumption for M…") vb.; ek bayraklar `add_to_transit`, `is_standard` — [stock_entry_type.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_entry_type/stock_entry_type.json)
- **Stock Entry** alanları: `stock_entry_type`, `purpose`, `from_warehouse`, `to_warehouse`, `items` (Stock Entry Detail), `additional_costs` (Landed Cost Taxes and Charges) + `total_additional_costs`, `total_incoming_value/total_outgoing_value/value_difference`, `add_to_transit`, `outgoing_stock_entry` (transit girişinin bağlandığı çıkış), `per_transferred`, `apply_putaway_rule`, `is_return`, `project`, `cost_center`, `asset_repair` (onarımda parça tüketimi), `set_posting_time` (geriye tarih), `scan_barcode`, `amended_from`; `is_submittable=1`, `track_changes=1`; butonlar `get_items`, `get_stock_and_rate` — [stock_entry.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_entry/stock_entry.json)
- **Stock Ledger Entry** alanları: `item_code`, `warehouse`, `posting_date/posting_time/posting_datetime`, `voucher_type` + `voucher_no` (Dynamic Link — kaynak belgeye genel bağ), `voucher_detail_no`, `actual_qty`, `incoming_rate`, `outgoing_rate`, `qty_after_transaction`, `valuation_rate`, `stock_value`, `stock_value_difference`, `stock_queue` (FIFO kuyruğu metin olarak), `project`, `is_cancelled`, `recalculate_rate`, `serial_and_batch_bundle`, `is_adjustment_entry`; adlandırma `MAT-SLE-.YYYY.-.#####` — [stock_ledger_entry.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_ledger_entry/stock_ledger_entry.json)
- **İptal davranışı**: `set_as_cancel()` SLE'leri silmez, `is_cancelled=1`, `modified`, `modified_by` günceller; `validate_cancellation()` — kaynak belgenin repost'u "In Progress" ise iptali engeller ("Cannot cancel the transaction. Reposting of item valuation on submission is not completed yet."), "Queued" ise repost'u "Skipped" yapar — [erpnext/stock/stock_ledger.py](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/stock_ledger.py)
- **Değerleme**: `stock_ledger.py` `FIFOValuation`, `LIFOValuation` sınıflarını içe aktarır; "Moving Average" dalları var; `NegativeStockError` ve "Insufficient Stock" hatası; `allow_negative_stock` ayarı — [stock_ledger.py](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/stock_ledger.py)
- **Stock Settings** alanları (seçme): `valuation_method`, `allow_negative_stock`, `auto_indent` (otomatik malzeme talebi), `reorder_email_notify`, `stock_frozen_upto`, `stock_frozen_upto_days`, `stock_auth_role`, `role_allowed_to_create_edit_back_dated_transactions`, `validate_material_transfer_warehouses`, `enable_stock_reservation`, `over_delivery_receipt_allowance`, `mr_qty_allowance` — [stock_settings.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_settings/stock_settings.json)
- **Repost Item Valuation** alanları: `based_on` (Transaction | Item and Warehouse), `voucher_type/voucher_no`, `item_code`, `warehouse`, `posting_date/time`, `status` (Queued | In Progress | Completed | Skipped | Failed | Cancelled), `error_log`, `current_index`, `total_reposting_count`, `recreate_stock_ledgers`, `repost_only_accounting_ledgers`, `reposting_data_file` (ara durum dosyası) — [repost_item_valuation.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/repost_item_valuation/repost_item_valuation.json)
- **Repost işleyişi**: `repost(doc)` → durum "In Progress" + commit; `repost_sl_entries`, `repost_gl_entries`; hata olursa rollback; `RecoverableErrors = (JobTimeoutException, QueryDeadlockError, QueryTimeoutError)` olursa tekrar "In Progress" (yeniden kuyruğa), diğerlerinde "Failed" + `notify_error_to_stock_managers`; `deduplicate_similar_repost`, `skip_reposts_covered_by_dependents`; kapalı dönem doğrulamaları `validate_period_closing_voucher`, `validate_accounts_freeze`; iş `queue="long"` ile kuyruklanır — [repost_item_valuation.py](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/repost_item_valuation/repost_item_valuation.py)
- Zamanlayıcı: `repost_entries` "hourly_maintenance"; `run_parallel_reposting` her 15 dk (cron `0/15 * * * *`); haftalık `repost_incorrect_valuation_entries` — [erpnext/hooks.py](https://github.com/frappe/erpnext/blob/develop/erpnext/hooks.py)
- Belge: "Backdated stock transactions" kaydedildiğinde otomatik repost oluşur; manuel olarak "Start Reposting" ile de başlatılır; Stock Ledger Variance raporu yanlış kayıtları bulmaya yardım eder; kapalı mali yılda kaçınılmalı — [docs.frappe.io Repost Item Valuation](https://docs.frappe.io/erpnext/user/manual/en/repost-item-valuation)
- **Stock Reconciliation** (sayım/açılış) denetleyicisi mevcut (65 KB) — [stock_reconciliation.py](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_reconciliation/stock_reconciliation.py). Ayrıntılı akışı bu oturumda okunmadı → aşağıda Gaps.
- **Yeniden sipariş (reorder)**: günlük `erpnext.stock.reorder_item.reorder_item` (daily_maintenance); `auto_indent` açıksa çalışır; öğe-depo bazında `warehouse_reorder_level` ve `material_request_type` okunur, öngörülen miktara (`get_item_warehouse_projected_qty`) göre Material Request oluşturur; `reorder_email_notify` açıksa e-posta; hatalar `notify_errors` — [erpnext/stock/reorder_item.py](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/reorder_item.py); Material Request'te `auto_created_via_reorder` bayrağı — [material_request.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/material_request/material_request.json)
- **Putaway Rule**: `item_code`, `warehouse`, `capacity`, `stock_capacity`, `priority`, `uom`, `disable` — ürün için depo kapasitesine/önceliğe göre yerleştirme — [putaway_rule.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/putaway_rule/putaway_rule.json)
- Seri/parti: SLE'de `serial_and_batch_bundle`, `has_batch_no`, `has_serial_no` — seri/parti bilgisi ayrı "Serial and Batch Bundle" belgesinde tutuluyor — [stock_ledger_entry.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_ledger_entry/stock_ledger_entry.json)

### Inferences
- Alpnex ile doğrudan örtüşen tasarım: (a) her stok satırında "işlem sonrası bakiye" ve "işlem sonrası değer" saklamak (bakiyeyi tekrar toplamadan raporlama), (b) `voucher_type+voucher_no` ile hareketin kaynağına genel bağ, (c) iptalde silme yerine işaretleme. Alpnex "ters kayıt" kullanıyor; ERPNext ise orijinali `is_cancelled` ile işaretleyip bakiyeyi yeniden hesaplıyor — iki yaklaşım farklı; Alpnex'in çevrimdışı eşitlemesinde ters kayıt daha güvenli (append-only), bu yüzden değiştirmeye gerek yok.
- Geriye tarihli kayıtta "sonraki hareketlerin bakiyesini yeniden hesapla" kuyruğu (durumlu, hata günlüklü, tekilleştirmeli) Alpnex için önemli: masaüstü çevrimdışı çalışıp geç eşitlendiğinde kayıtlar doğal olarak "geriye tarihli" düşer. Alpnex'te hareket sonrası bakiye/maliyet saklanıyorsa böyle bir "yeniden hesaplama işi" gerekir.
- "Stok dondurma tarihi" (`stock_frozen_upto`) + yalnız belirli rolün geriye tarih girebilmesi (`role_allowed_to_create_edit_back_dated_transactions`) Alpnex'e doğrudan uyarlanabilir ucuz ve güçlü bir kontroldür.
- Transfer için `add_to_transit` + `outgoing_stock_entry` + `per_transferred`: iki adımlı (gönderildi → teslim alındı) depolar arası transfer. Şantiyeler arası nakliyede "yolda" stok görünürlüğü sağlar.
- Ek maliyet (`additional_costs`) ile nakliye bedelini transfer/giriş değerine eklemek, inşaat malzemesinde (nakliye pahalı) değerli olabilir.

### Gaps
- Stock Reconciliation iş akışı (fark hesaplama, açılış stoku modu) ayrıntılı okunmadı.
- Değerleme yöntemi seçiminin öğe bazında mı şirket bazında mı yapıldığı bu oturumda dosyadan teyit edilmedi (Item'de `valuation_method` alanı olduğu bilinir — unverified).

---

## 3. ERPNext Satın alma zinciri: Material Request → PO → Purchase Receipt → Purchase Invoice

### Takeaway
Material Request bir "talep" belgesidir ve türü (Purchase, Material Transfer, Material Issue, Manufacture, Subcontracting, Customer Provided) sonraki belgeyi belirler; durum, yüzde alanlarla (`per_ordered`, `per_received`) zincir boyunca otomatik güncellenir.

### Cited Findings
- Material Request alanları: `material_request_type` (Purchase | Material Transfer | Material Issue | Manufacture | Subcontracting | Customer Provided), `status` (Draft | Submitted | Stopped | Cancelled | Pending | Partially Ordered | Partially Received | Ordered | Issued | Transferred | Received…), `per_ordered`, `per_received`, `transfer_status` (Not Started | In Transit | Completed), `schedule_date`, `set_warehouse`, `set_from_warehouse`, `job_card`, `work_order`, `auto_created_via_reorder`, `amended_from`; submittable — [material_request.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/material_request/material_request.json)
- Stock Entry, `purchase_order`, `purchase_receipt_no`, `subcontracting_order` bağlantı alanlarını taşır (zincirde geri izleme) — [stock_entry.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_entry/stock_entry.json)
- Günlük işler arasında `update_invoice_status` (fatura gecikme durumları) ve `supplier_scorecard.refresh_scorecards` var — [erpnext/hooks.py](https://github.com/frappe/erpnext/blob/develop/erpnext/hooks.py)

### Inferences
- Alpnex'in satın alma talebi ekranına "kısmen sipariş edildi / kısmen teslim alındı" yüzdeleri ve "Durduruldu (Stopped)" durumu eklenmesi takip kolaylığı sağlar. Tek talep hem satın almaya hem depodan transfere dönüşebilmeli (ERPNext'te tür seçimiyle).
- `Customer Provided` (müşterinin/işverenin sağladığı malzeme) türü, inşaatta "idare malzemesi" kavramına benziyor — Alpnex için incelemeye değer (existence ≠ requirement).

### Gaps
- Purchase Order / Purchase Receipt / Purchase Invoice JSON'ları indirilmedi; `per_billed`, `per_received` alanları ve "Update Stock" seçeneği (faturadan doğrudan stok girişi) bu oturumda kaynaktan doğrulanmadı (unverified).
- ERPNext'te üç yönlü eşleştirme (PO–irsaliye–fatura) ayarı doğrulanmadı.

---

## 4. ERPNext Varlıklar (Asset Movement, Maintenance, Repair, amortisman) ve Araç (Vehicle, Vehicle Log)

### Takeaway
ERPNext'te ağır ekipman için en uygun karşılık "Asset" modülüdür: hareket (lokasyon/sorumlu değişimi), periyodik bakım görevleri (sonraki tarih, gecikme durumu) ve onarım (duruş süresi, maliyet, stoktan parça tüketimi). Araç/yakıt tarafı (Vehicle Log) ise oldukça basittir.

### Cited Findings
- **Asset Movement**: `purpose` (Issue | Receipt | Transfer | Transfer and Issue), `transaction_date`, `reference_doctype/reference_name`, `assets` tablosu; submittable — [asset_movement.json](https://github.com/frappe/erpnext/blob/develop/erpnext/assets/doctype/asset_movement/asset_movement.json)
- **Asset Maintenance**: `asset_name`, `maintenance_team`, `maintenance_manager`, `asset_maintenance_tasks` tablosu — [asset_maintenance.json](https://github.com/frappe/erpnext/blob/develop/erpnext/assets/doctype/asset_maintenance/asset_maintenance.json)
- **Asset Maintenance Task**: `maintenance_type` (Preventive Maintenance | Calibration), `maintenance_status` (Planned | Overdue | Cancelled), `periodicity` (Daily | Weekly | Monthly | Quarterly | Half-yearly | Yearly | 2 Yearly | 3 Yearly), `start_date`, `end_date`, `next_due_date`, `last_completion_date`, `assign_to` (User), `certificate_required` — [asset_maintenance_task.json](https://github.com/frappe/erpnext/blob/develop/erpnext/assets/doctype/asset_maintenance_task/asset_maintenance_task.json)
- Günlük işler: `asset.update_maintenance_status`, `asset_maintenance_log.update_asset_maintenance_log_status`, `depreciation.post_depreciation_entries` — [erpnext/hooks.py](https://github.com/frappe/erpnext/blob/develop/erpnext/hooks.py)
- **Asset Repair**: `failure_date`, `completion_date`, `repair_status` (Pending | Completed | Cancelled), `description`, `actions_performed`, `downtime`, `repair_cost`, `stock_items` (Asset Repair Consumed Item — stoktan tüketilen parçalar), `invoices` (Asset Repair Purchase Invoice), `consumed_items_cost`, `total_repair_cost`, `capitalize_repair_cost`, `increase_in_asset_life`, `cost_center`, `project`; submittable — [asset_repair.json](https://github.com/frappe/erpnext/blob/develop/erpnext/assets/doctype/asset_repair/asset_repair.json)
- **Vehicle** (ERPNext setup): `license_plate` (ad), `make`, `model`, `last_odometer`, `acquisition_date`, `chassis_no`, `vehicle_value`, `employee`, `insurance_company`, `policy_no`, `start_date`, `end_date`, `fuel_type` (Petrol | Diesel | Natural Gas | Electric), `uom`, `carbon_check_date` — [vehicle.json](https://github.com/frappe/erpnext/blob/develop/erpnext/setup/doctype/vehicle/vehicle.json)
- **Vehicle Log** (HRMS): `license_plate`, `employee`, `date`, `odometer`, `last_odometer`, `fuel_qty`, `price`, `supplier`, `invoice`, `service_detail` (Vehicle Service tablosu); submittable — [hrms/.../vehicle_log.json](https://github.com/frappe/hrms/blob/develop/hrms/hr/doctype/vehicle_log/vehicle_log.json)

### Inferences
- Asset Repair'in "duruş süresi + stoktan parça tüketimi + dış fatura + maliyetin varlığa eklenip eklenmeyeceği" yapısı, Alpnex bakım modülündeki "parça tüketimi" ile karşılaştırılmalı; özellikle **duruş süresi (downtime)** ve **arıza tarihi / tamamlanma tarihi** ayrımı makine verimliliği raporu için değerli.
- Bakım görevinde `next_due_date` + `last_completion_date` + günlük "Overdue" güncellemesi, Alpnex'in periyodik bakım uyarılarıyla aynı mantık; ERPNext yalnız **takvim periyodu** destekler — sayaç/saat bazlı (motor saati) bakım yok. Alpnex'in sayaç okumalı günlük faaliyet verisi burada ERPNext'ten **üstün** olabilir.
- Vehicle'da kiralık araç / kira sözleşmesi kavramı yok (Odoo Fleet'te var, bkz. §7).

### Gaps
- Amortisman (Asset Depreciation Schedule) ayrıntısı okunmadı; Alpnex ön muhasebe kapsamında öncelikli görünmüyor.
- Vehicle Log'un submit sonrası `Vehicle.last_odometer` güncelleyip güncellemediği .py dosyasından doğrulanmadı (unverified).

---

## 5. ERPNext/Frappe çekirdek: belge yaşam döngüsü (docstatus/amend), Version/denetim, izinler, Workflow, Notification, Auto Repeat, silinen belgeler, zamanlayıcı, eşzamanlılık

### Takeaway
Frappe'nin en kopyalanmaya değer çekirdek kalıpları: (1) docstatus 0→1→2 durum makinesi ve iptal edilmiş belgeden "amend" ile yeni sürüm, (2) her kayıtta otomatik alan bazlı fark (Version), (3) rol × izin seviyesi (permlevel) × kayıt değeri (User Permission) üç katmanlı yetki, (4) silinen belgenin JSON'unun "Deleted Document"te saklanıp geri yüklenebilmesi, (5) `modified` zaman damgasıyla iyimser kilitleme.

### Cited Findings
- **Durum geçişleri**: `check_docstatus_transition` — "Cannot change docstatus from 0 (Draft) to 2 (Cancelled)", "Cannot change docstatus from 1 (Submitted) to 0 (Draft)", "Cannot change docstatus of non submittable doctype" — [frappe/model/document.py](https://github.com/frappe/frappe/blob/develop/frappe/model/document.py)
- **Amend**: `validate_amended_from` — `amended_from` belgesinin docstatus'u 2 (iptal) değilse hata; `copy_attachments_from_amended_from` ekleri yeni sürüme kopyalar; Version farkı amend'de eski belgeye göre alınır (`save_version` içinde `amended_from`) — [document.py](https://github.com/frappe/frappe/blob/develop/frappe/model/document.py)
- **İyimser eşzamanlılık**: `check_if_latest` — veritabanındaki `modified` ile formun açıldığı andaki `modified` farklıysa `TimestampMismatchError`: "has been modified after you have opened it … Please refresh to get the latest document." Ayrıca `lock(timeout)` ve `for_update` seçenekleri var — [document.py](https://github.com/frappe/frappe/blob/develop/frappe/model/document.py)
- **Version (denetim)**: `save_version` yalnız DocType'ta `track_changes` açıksa çalışır; `get_diff(old, new, …)` alan ve alt tablo satırı farklarını üretir; `set_impersonator` (başka kullanıcı adına işlem) kaydedilir; HTML diff üretimi — [frappe/core/doctype/version/version.py](https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/version/version.py)
- **DocPerm** (rol izni satırı): `role`, `if_owner`, `permlevel`, `read`, `write`, `create`, `delete`, `submit`, `cancel`, `amend`, `report`, `export`, `import`, `share`, `print`, `email`, `select`, `mask` — [docperm.json](https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/docperm/docperm.json)
- **User Permission** (kayıt değeri bazlı kısıt): `user`, `allow` (DocType), `for_value`, `is_default`, `apply_to_all_doctypes`, `applicable_for`, `hide_descendants` — [user_permission.json](https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/user_permission/user_permission.json)
- **Workflow**: `document_type`, `is_active`, `override_status`, `send_email_alert`, `enable_action_confirmation`, `conditions`, `states` (Workflow Document State), `transitions` (Workflow Transition), `workflow_state_field` — [workflow.json](https://github.com/frappe/frappe/blob/develop/frappe/workflow/doctype/workflow/workflow.json)
- **Auto Repeat**: `reference_doctype/reference_document`, `frequency` (Daily…Yearly), `repeat_on_day`, `repeat_on_last_day`, `next_schedule_date`, `submit_on_creation`, `notify_by_email`, `template`, `print_format`, `assignee`, `status` (Active | Disabled | Completed); günlük `make_auto_repeat_entry` — [auto_repeat.json](https://github.com/frappe/frappe/blob/develop/frappe/automation/doctype/auto_repeat/auto_repeat.json); [frappe/hooks.py](https://github.com/frappe/frappe/blob/develop/frappe/hooks.py)
- **Notification**: zamanlayıcıda 5 dakikada bir `trigger_offset_alerts`, günlük `trigger_daily_alerts` (tarih bazlı uyarılar) — [frappe/hooks.py](https://github.com/frappe/frappe/blob/develop/frappe/hooks.py); şema dosyası — [notification.json](https://github.com/frappe/frappe/blob/develop/frappe/email/doctype/notification/notification.json)
- **Deleted Document** (geri dönüşüm kutusu benzeri): `deleted_doctype`, `deleted_name`, `data` (Code — belgenin tam içeriği), `restored`, `new_name` — [deleted_document.json](https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/deleted_document/deleted_document.json)
- **Zamanlayıcı türleri**: `cron`, `all`, `hourly`, `hourly_maintenance` ("roughly once an hour but don't align with wall-clock time"), `daily`, `daily_long`, `daily_maintenance`, `weekly`, `monthly_long`; örnekler: `auto_email_report.send_daily`, `log_settings.run_log_clean_up`, `document_follow.send_daily_updates`, `reminder.send_reminders` — [frappe/hooks.py](https://github.com/frappe/frappe/blob/develop/frappe/hooks.py)
- **Muhasebe boyutu**: Accounting Dimension `label`, `fieldname`, `document_type`, `disabled`, `dimension_defaults` — maliyet merkezine ek, kullanıcı tanımlı analiz boyutu (ör. "Şantiye") — [accounting_dimension.json](https://github.com/frappe/erpnext/blob/develop/erpnext/accounts/doctype/accounting_dimension/accounting_dimension.json)

### Inferences
- **Amend kalıbı** Alpnex'in "operasyonel kayıt silinmez, ters kayıt yapılır" ilkesine çok uyar: "İptal et + düzeltilmiş kopya oluştur (orijinale bağlı)" kullanıcıya tek tuşla sunulabilir; ekler otomatik taşınır; Version farkı orijinale göre hesaplanır.
- **İzin eksikleri olası**: Alpnex'te "submit/cancel/amend" ayrı yetki mi, "yalnız kendi kaydı" (`if_owner`) var mı, alan grubu (permlevel) gizleme ve "export/print/import" ayrı yetkiler var mı — bunlar kontrol listesi olarak kullanılabilir. User Permission (kullanıcı yalnız şu şantiye/depoyu görür, `is_default` ile varsayılan doldurma) Alpnex'in şube modeliyle doğrudan eşleşir.
- `mask` izni (hassas alanı maskeli gösterme) yeni ve ilginç — maaş/fiyat gibi alanlar için düşünülebilir.
- Deleted Document'in "tam JSON yedeği + geri yükle" yaklaşımı, Alpnex'in soft-delete geri dönüşüm kutusuna alternatif değil ama ana veri (cari kart, malzeme kartı) gibi soft-delete edilmeyen türler için güvenlik ağı olabilir.
- `modified` zaman damgasıyla çakışma tespiti (iki kullanıcı aynı formu düzenlerse) — Alpnex web+masaüstü eşzamanlı düzenlemede aynı korumaya sahip mi kontrol edilmeli.

### Gaps
- Amend edilen belgenin adlandırması ("-1" eki) belge sayfası HTTP 500 verdiği için doğrulanamadı (unverified) — [docs.frappe.io docstatus](https://docs.frappe.io/framework/user/en/basics/doctypes/docstatus) erişilemedi.
- Report Builder, Script/Query Report, Print Format (Jinja) ve Data Import aracı bu oturumda kaynaktan incelenmedi (varlıkları genel bilgi — unverified).
- **Çevrimdışı POS**: ERPNext'in eski çevrimdışı POS'unun v13'te kaldırılıp çevrimiçi "Point of Sale" sayfasına geçildiği bilinir ama bu oturumda doğrulanmadı (unverified). `pos_invoice.json`'da "offline" içeren alan bulunamadı.

---

## 6. Odoo Envanter: stock.move / stock.quant / stock.picking, lokasyon türleri, transit, depo rotaları, sayım, yeniden sipariş kuralları, değerleme

### Takeaway
Odoo'da stok "lokasyonlar arası hareket" olarak modellenir: tedarikçi, müşteri, envanter kaybı, üretim ve **transit** de birer (sanal) lokasyondur; böylece her hareket çift taraflı (kaynak→hedef) olur ve bakiye `stock.quant` tablosunda tutulur. Sayım, quant üzerinde "Counted" alanı doldurulup "Apply" ile fark hareketi üretilerek yapılır.

### Cited Findings
- **Lokasyon türü** (`usage`): supplier (Vendor), view (Virtual), internal, customer, inventory (Inventory Loss), production, transit — [addons/stock/models/stock_location.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_location.py)
- **stock.move durumları**: draft (New), waiting (Waiting Another Move), confirmed (Waiting), partially_available, assigned (Available), done, cancel — [stock_move.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_move.py)
- **stock.picking durumları**: draft, waiting, confirmed, assigned (Ready), done, cancel; `tracking=True` (chatter'da izlenir) — [stock_picking.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_picking.py)
- **Sayım (Inventory adjustment)**: `stock.quant` üzerinde `inventory_quantity`, `inventory_diff_quantity`, `inventory_date` ("Scheduled"; internal ve transit lokasyonlar için lokasyonun `_get_next_inventory_date()`'inden hesaplanır), `inventory_quantity_set`; `_set_inventory_quantity` "Inverse method to create stock move"; sayım hareketleri `is_inventory = True` — [stock_quant.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_quant.py)
- Belge: "Counted" sütunu, "Difference" (otomatik; fazla yeşil, eksik kırmızı), "Accounting Date", "Apply All" — [odoo/documentation count_products.rst (19.0)](https://github.com/odoo/documentation/blob/19.0/content/applications/inventory_and_mrp/inventory/warehouses_storage/inventory_management/count_products.rst)
- **Depo adımları ve tedarik**: `stock.warehouse` → `reception_steps`, `delivery_steps`, `resupply_wh_ids` (bu depoyu başka depodan besle → otomatik tedarik rotaları `create_resupply_routes`); aynı şirket depoları arasında `internal_transit_location`, şirketler arası `external_transit_location` kullanılır — [stock_warehouse.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_warehouse.py)
- **Yeniden sipariş kuralı** (`stock.warehouse.orderpoint`): `trigger` (auto/manual), `product_min_qty`, `product_max_qty`, `route_id`/`effective_route_id`, `qty_to_order` (hesaplanan + elle düzeltilebilir), `snoozed_until` ("Hidden until next scheduler"), `action_replenish`, `action_replenish_auto` — [stock_orderpoint.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_orderpoint.py)
- **Değerleme**: `stock_account` `cost_method` seçimine FIFO ekler ("First In First Out (FIFO)"); yöntem ürün kategorisinden ya da şirketten gelir (`category.property_cost_method if category else self.env.company.cost_method`); 20.0'da `product_value.py` diye yeni bir model dosyası var — [addons/stock_account/models/product.py](https://github.com/odoo/odoo/blob/20.0/addons/stock_account/models/product.py); [stock_account/models dizini](https://github.com/odoo/odoo/tree/20.0/addons/stock_account/models)

### Inferences
- **Transit lokasyon** Alpnex'in depolar arası transferi için en net modeldir: "Gönder" adımı malı kaynak depodan "Yolda" lokasyonuna, "Teslim al" adımı yoldan hedef depoya taşır; eksik/hasarlı teslim fark olarak görünür. Alpnex hareket defterinde ayrı bir sanal "transit" depo ile bu çok az değişiklikle uygulanabilir.
- "Envanter kaybı" sanal lokasyonu: sayım farkları ve fire, "kayıp" hesabına giden ayrı bir hareket olur — Alpnex'in "doğrudan bakiye düzenleme yok" kuralını korurken sayım farkını açık kaydetmenin temiz yolu.
- Lokasyon bazında **planlı sayım tarihi** (`inventory_date`) — şantiye depolarında periyodik sayım hatırlatması için uyarlanabilir.
- Yeniden sipariş kuralında `snoozed_until` (erteleme) ve `trigger=manual` (öneri olarak listele, otomatik sipariş açma) şantiye gerçekliğine uygun: Alpnex'te min stok uyarısının "öneri listesi + erteleme" biçiminde sunulması önerilir.

### Gaps
- Odoo 20'de değerleme altyapısının (stock.valuation.layer → product_value?) nasıl değiştiği ayrıntılı okunmadı; 20.0'da `stock_valuation_layer.py` dosyası listede görülmedi — yapısal bir değişiklik olabilir (unverified).
- Lot/seri numarası (`stock.lot`) ayrıntısı okunmadı.

---

## 7. Odoo Satın alma (RFQ → PO → Receipt → Bill, 3-way matching)

### Takeaway
Odoo 20'de PO durumları sadeleşmiş: draft (RFQ) → sent → to approve → purchase → cancel; ayrıca `locked` bayrağı. Fatura kontrolü satır bazında "sipariş edilen" ya da "teslim alınan" miktara göre yapılır; 3-way matching ayarı, teslim alınmamış ürünün faturasının ödenmesini engeller.

### Cited Findings
- PO `state`: draft (RFQ), sent (RFQ Sent), to approve, purchase (Purchase Order), cancel; `locked` ("Locked Purchase Orders cannot be modified"), `lock_confirmed_po` (şirket ayarı `po_lock`); `invoice_status` (no | to invoice | invoiced), `receipt_status` (pending | partial | full); `button_confirm`, `button_approve` (`_approval_allowed` — çift onay eşiği), `button_cancel` (kilitliyse "You must first unlock them"), `button_draft`, `button_lock` — [addons/purchase/models/purchase_order.py](https://github.com/odoo/odoo/blob/20.0/addons/purchase/models/purchase_order.py)
- PO satırı: `qty_received` (manuel ya da stok hareketinden), `qty_invoiced` ("Billed Qty"), `qty_to_invoice` — `purchase_method == 'purchase'` ise sipariş miktarı − faturalanan, değilse teslim alınan − faturalanan — [purchase_order_line.py](https://github.com/odoo/odoo/blob/20.0/addons/purchase/models/purchase_order_line.py)
- Belge: "On ordered quantities" / "On received quantities" kontrol politikası; "The 3-way matching feature ensures vendor bills are only paid once some, or all, of the products included in the PO have been received"; faturada "Should Be Paid" alanı — [odoo/documentation control_bills.rst](https://github.com/odoo/documentation/blob/19.0/content/applications/inventory_and_mrp/purchase/manage_deals/control_bills.rst)

### Inferences
- Alpnex'in "stok etkili fatura" yapısına eklenebilecek en somut kontrol: fatura satırı miktarı ≤ teslim alınan − önceden faturalanan; aşarsa uyarı/"ödenmeli mi" bayrağı. Şantiyelerde irsaliyesiz/eksik teslimli faturalar yaygın olduğu için yüksek değerli.
- Onaylanmış siparişi "kilitle" (değişiklik için önce kilidi aç) — denetim izini temiz tutar.
- Tutar eşiğine bağlı ikinci onay (`to approve`) Alpnex'in satın alma talebi onayına benzer; PO seviyesinde de eşik onayı eksikse düşünülmeli.

### Gaps
- Odoo 20'de eski "done" PO durumunun kaldırılıp `locked` boolean'a dönüştüğü görülüyor; sürüm notundan teyit edilmedi.

---

## 8. Odoo Fleet (araçlar, sözleşme/leasing, kilometre, servis, yakıt, maliyet)

### Takeaway
Odoo Fleet kiralık/leasing araç takibi için iyi bir referanstır: sözleşme kaydı (başlangıç/bitiş, tekrarlayan maliyet ve sıklığı, durum), süresi yaklaşan sözleşme uyarısı (varsayılan 30 gün) ve sözleşme maliyetini periyodik üreten zamanlanmış eylem. Ayrı bir "yakıt kaydı" modeli 20.0'da yok; yakıt servis türü olarak kaydedilir (bkz. Gaps).

### Cited Findings
- `fleet.vehicle` alanları (seçme): `driver_id`, `future_driver_id`, `next_assignation_date`, `manager_id`, `model_id/brand_id`, `acquisition_date`, `write_off_date`, `state_id`, `odometer` (hesaplanan "Last Odometer", ters yazımlı), `odometer_unit`, `fuel_type`, `contract_renewal_due_soon`, `contract_renewal_overdue`, `contract_state`, `car_value`, `net_car_value`, `residual_value`, `plan_to_change_vehicle`, `service_activity`, sayaçlar `contract_count/service_count/odometer_count/history_count` — [addons/fleet/models/fleet_vehicle.py](https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle.py)
- Fleet model dosyaları: `fleet_vehicle_assignation_log.py` (sürücü geçmişi), `fleet_vehicle_log_contract.py`, `fleet_vehicle_log_services.py`, `fleet_vehicle_odometer.py`, `fleet_service_type.py`, `fleet_vehicle_state.py`, `mail_activity_type.py` — **fuel log modeli listede yok** — [fleet/models dizini (GitHub API)](https://api.github.com/repos/odoo/odoo/contents/addons/fleet/models?ref=20.0)
- `fleet.vehicle.log.contract`: `cost_subtype_id` (Contract Type), `amount`, `start_date`, `expiration_date`, `days_left`, `expires_today`, `insurer_id` (Vendor), `ins_ref`, `state` (futur=New | open=Running | expired | done | closed), `cost_generated` (Recurring Cost), `cost_frequency` (no | daily | weekly | monthly | yearly), `service_ids` (dahil hizmetler); `scheduler_manage_contract_expiration` — `hr_fleet.delay_alert_contract` parametresi varsayılan 30 gün — [fleet_vehicle_log_contract.py](https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle_log_contract.py)
- Varsayılan sözleşme türleri "Omnium" ve "Leasing" (category=contract); zamanlanmış eylem "Fleet: Generate contracts costs based on costs frequency"; araç durumları "New Request, To Order, Registered, Downgraded" — [addons/fleet/data/fleet_data.xml](https://github.com/odoo/odoo/blob/20.0/addons/fleet/data/fleet_data.xml)
- `fleet.vehicle.log.services`: `vehicle_id`, `amount`, `description`, `odometer_id` ("Odometer measure of the vehicle at the moment of this log"), `date_from/date_to`, `vendor_id`, `inv_ref`, `purchaser_id` (Driver), `service_type_id`, `state` (new | running | done | cancelled — kanban stage) — [fleet_vehicle_log_services.py](https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle_log_services.py)
- `fleet.vehicle.odometer`: `date`, `value` (aggregator=max), `vehicle_id`, `driver_id` — [fleet_vehicle_odometer.py](https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle_odometer.py)

### Inferences
- Alpnex "kiralık araç" desteği için Odoo'nun sözleşme modeli doğrudan kontrol listesidir: kiralayan firma, başlangıç/bitiş, tekrarlayan bedel + sıklık, dahil hizmetler, bitişe X gün kala uyarı, ve bedeli periyodik olarak maliyet kaydına dönüştüren zamanlanmış iş. Alpnex'te kira bedelinin otomatik tahakkuku yoksa eklenmeye aday.
- "Gelecek sürücü / şu tarihten itibaren müsait" alanları, ekipmanın şantiyeler arası planlı atamasına uyarlanabilir.
- Odoo'da kilometre ayrı log tablosunda tutulur ve araçtaki değer bu logdan hesaplanır (ters yazımla log ekler) — Alpnex'in günlük faaliyet sayaç okumalarının "tek doğruluk kaynağı" olması ile aynı ilke.
- Fleet'te yakıt tüketimi analizi (L/100 km, L/saat) yok gibi görünüyor; Alpnex'in yakıt modülü burada rakipten güçlü olabilir.

### Gaps
- Odoo 13'te ayrı olan fuel log'un sonraki sürümlerde servis kayıtlarına birleştirildiği bilinir; 20.0'da model dosyası olmadığı doğrulandı ama birleşme gerekçesi/sürümü doğrulanmadı (unverified).
- Fleet maliyet raporlarının (analiz görünümü) içeriği incelenmedi.

---

## 9. Odoo Maintenance ve Repair

### Takeaway
Odoo Maintenance: ekipman + bakım talebi (kanban aşamalı), düzeltici/önleyici tür, önleyici talepte "her N gün/hafta/ay/yıl tekrarla, sonsuz ya da tarihe kadar", ve ekipmanda MTBF/MTTR/tahmini sonraki arıza istatistikleri. Repair: onarım emri, parça hareketleri stoktan, garanti bayrağı, parça hazır olma durumu.

### Cited Findings
- `maintenance.request`: `maintenance_type` (corrective | preventive), `schedule_date`, `stage_id` (maintenance.stage, tracking), `recurring_maintenance`, `repeat_interval` (≥1 kısıtı), `repeat_unit` (day | week | month | year), `repeat_type` (forever | until), `repeat_until`; tamamlanınca bir sonraki talep `schedule_date + relativedelta(repeat_unit × repeat_interval)` ile oluşturulur; `_inherit = ['mail.thread', 'mail.activity.mixin']` — [addons/maintenance/models/maintenance.py](https://github.com/odoo/odoo/blob/20.0/addons/maintenance/models/maintenance.py)
- `maintenance.mixin`: `effective_date`, `maintenance_team_id`, `technician_user_id`, `maintenance_count`, `maintenance_open_count`, `expected_mtbf`, `mtbf` ("computed based on done corrective"), `mttr`, `estimated_next_failure`, `latest_failure_date` — [maintenance.py](https://github.com/odoo/odoo/blob/20.0/addons/maintenance/models/maintenance.py)
- Belge: talepler "New Request" aşamasında başlar; başarılı onarım "Repaired", başarısız "Scrap" aşamasına taşınır; öncelik yüksek olan üstte; ekipman için talep açmak için erişim/takipçi olmak gerekir; "Block Workcenter" — [odoo.com 19.0 Maintenance requests](https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/maintenance/maintenance_requests.html)
- Belge (takvim): önleyici talepte "Repeat Every" alanı — [odoo/documentation maintenance_calendar.rst](https://github.com/odoo/documentation/blob/19.0/content/applications/inventory_and_mrp/maintenance/maintenance_calendar.rst)
- `repair.order` durumları: draft (New), confirmed, done, cancel; `under_warranty`, `lot_id`, `picking_type_id`, `location_id`, `parts_location_id`, `recycle_location_id`, `move_ids` (parça stok hareketleri), `parts_availability` / `parts_availability_state` — [addons/repair/models/repair.py](https://github.com/odoo/odoo/blob/20.0/addons/repair/models/repair.py)

### Inferences
- **MTBF/MTTR** ve "tahmini sonraki arıza", Alpnex'in ağır ekipman raporlarına eklenebilecek, mevcut veriden (arıza/onarım tarihleri) hesaplanabilen ucuz ama etkili göstergeler.
- Bakım talebinin **kanban** (sürükle-bırak aşama) görünümü saha ekipleri için anlaşılır; Alpnex iş emirleri/bakım listesine "aşama panosu" görünümü eklenebilir.
- Odoo'da da tekrar **takvim bazlı**; sayaç (km/motor saati) bazlı tekrar Community Maintenance'ta görülmedi — Alpnex'in sayaç bazlı periyodik bakım uyarıları farklılaştırıcıdır.
- Repair'deki "parça hazır mı" durumu, iş emri açılmadan önce stok kontrolü için iyi bir UX fikri.

### Gaps
- Sayaç bazlı önleyici bakımın Odoo Enterprise'ta olup olmadığı doğrulanmadı.

---

## 10. Odoo çekirdek: chatter (mesaj/aktivite/ek), erişim hakları ve kayıt kuralları, çoklu şirket, zamanlanmış eylemler (ir.cron), eşzamanlılık

### Takeaway
Odoo'nun "chatter"ı (mail.thread + mail.activity.mixin) her kayda alan değişiklik izleme, yorum, takipçi, ek ve son tarihli "aktivite" (yapılacak iş) ekler; Alpnex için en çok kullanıcı değeri üretecek kalıp budur. Yetki: grup → model erişimi (CRUD) + kayıt kuralları (domain); global kurallar kesişir, grup kuralları birleşir. ir.cron hata sayacıyla kendini devre dışı bırakabilen dayanıklı zamanlayıcıdır.

### Cited Findings
- `mail.thread`: alan izleme altyapısı (`_track_get_fields`, `_track_prepare`, `_track_discard`; alanlarda `tracking=True`), `message_post`, `message_subscribe` — [addons/mail/models/mail_thread.py](https://github.com/odoo/odoo/blob/20.0/addons/mail/models/mail_thread.py)
- `mail.activity.mixin`: `activity_ids`, `activity_state` (overdue | today | planned), `activity_date_deadline`, `my_activity_date_deadline` (aranabilir), `activity_schedule` — [mail_activity_mixin.py](https://github.com/odoo/odoo/blob/20.0/addons/mail/models/mail_activity_mixin.py)
- Ekipman, bakım talebi, picking gibi modeller chatter mixin'lerini miras alır; ör. `maintenance.equipment` ve `maintenance.request` — [maintenance.py](https://github.com/odoo/odoo/blob/20.0/addons/maintenance/models/maintenance.py); PO `state` `tracking=True` — [purchase_order.py](https://github.com/odoo/odoo/blob/20.0/addons/purchase/models/purchase_order.py)
- Güvenlik belgesi: güvenlik mekanizmaları gruplara bağlanır ("a user belongs to any number of groups"); `implied_ids` ile grup kalıtımı; "Global rules *intersect* … adding global rules always restricts access further"; "Group rules *unify* … can expand access, but not beyond the bounds defined by global rules"; uyarı: birden çok global kural tüm erişimi kaldırabilir — [odoo/documentation security.rst (19.0)](https://github.com/odoo/documentation/blob/19.0/content/developer/reference/backend/security.rst)
- Çoklu şirket: alanlarda `check_company=True` kullanımı (ör. `fleet.vehicle.log.contract.vehicle_id`) — [fleet_vehicle_log_contract.py](https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle_log_contract.py)
- **ir.cron**: `interval_number`, `interval_type`, `nextcall`, `lastcall`, `priority`, `failure_count` ("number of consecutive failures … automatically reset on success"), `first_failure_date`, `user_id` (Scheduler User); `MAX_FAIL_TIME = timedelta(hours=5)` ve `MIN_FAILURE_COUNT_BEFORE_DEACTIVATION` ile sürekli başarısız işi devre dışı bırakma; iş seçimi `ORDER BY failure_count, priority, id`; uzun işler için `_notify_progress(done, remaining)` — [odoo/addons/base/models/ir_cron.py](https://github.com/odoo/odoo/blob/20.0/odoo/addons/base/models/ir_cron.py)
- ORM: `_log_access` (create_uid/create_date/write_uid/write_date sütunları), `_active_name` (arşivleme / soft-delete için `active` alanı; arama varsayılanda arşivlileri dışlar) — [odoo/orm/models.py](https://github.com/odoo/odoo/blob/20.0/odoo/orm/models.py)

### Inferences
- **Chatter benzeri kayıt geçmişi paneli** Alpnex için en yüksek getirili eklentidir: her kayıt (araç, iş emri, satın alma talebi, fatura) altında (1) izlenen alan değişiklikleri, (2) kullanıcı yorumları/@bahsetme, (3) ekler, (4) son tarihli "aktivite" (ör. "Sigorta yenile — 15 Kasım, atanan: X"). Alpnex'te audit log ve belgeler zaten var; bunları kayıt bazında tek akışta göstermek çoğunlukla bir görünüm işidir.
- "Aktivite" (overdue/today/planned) kavramı, Alpnex'teki dağınık uyarıları (bakım, sigorta, sözleşme bitişi, onay bekleyen talep) tek "Yapılacaklarım" listesinde toplamaya uygun.
- ir.cron'un **hata sayacı + otomatik devre dışı + ilerleme bildirimi** kalıbı, Alpnex'in bulut eşitleme/arka plan işleri için sağlam bir referans.
- Kayıt kuralı modeli (global ∩, grup ∪) Alpnex'in şube bazlı veri görünürlüğünü belgelemek ve test etmek için iyi bir zihinsel model.

### Gaps
- Odoo'da `ir.rule` dosyası 20.0'da `odoo/addons/base/models/` altında bulunamadı (konumu değişmiş); kaynak yerine belge kullanıldı.
- Eski sürümlerdeki `_check_concurrency` / `__last_update` iyimser kilit mekanizmasının 20.0 `models.py`'de karşılığı bulunamadı; Odoo'nun formlarda eşzamanlı düzenleme çakışmasını nasıl ele aldığı doğrulanmadı (unverified).
- İçe/dışa aktarma (import/export) aracı bu oturumda incelenmedi.

---

## 11. Alpnex için benimsenmeye değer somut kalıplar (öncelik sırası, gerekçeli)

### Takeaway
Varlık ≠ gereklilik. Aşağıdakiler, Alpnex'in mevcut ilkeleriyle (hareket defteri tek kaynak, silme yok/ters kayıt, çok şubeli, çevrimdışı) çelişmeyen ve kaynakta doğrulanmış kalıplardır.

### Cited Findings
- Geriye tarihli kayıtta sonraki hareketleri yeniden hesaplayan durumlu kuyruk (Queued/In Progress/Completed/Skipped/Failed, tekilleştirme, kurtarılabilir hata ayrımı) — [ERPNext repost_item_valuation.py](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/repost_item_valuation/repost_item_valuation.py)
- Stok dondurma tarihi + geriye tarih yetkili rolü — [ERPNext stock_settings.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_settings/stock_settings.json)
- Transit lokasyon / iki adımlı transfer — [Odoo stock_location.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_location.py); [ERPNext stock_entry.json](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/doctype/stock_entry/stock_entry.json)
- Draft→Submitted→Cancelled + Amend — [Frappe document.py](https://github.com/frappe/frappe/blob/develop/frappe/model/document.py)
- Chatter + aktivite — [Odoo mail_thread.py](https://github.com/odoo/odoo/blob/20.0/addons/mail/models/mail_thread.py); [mail_activity_mixin.py](https://github.com/odoo/odoo/blob/20.0/addons/mail/models/mail_activity_mixin.py)
- Yeniden sipariş önerisi (min/max, erteleme, manuel tetik) — [Odoo stock_orderpoint.py](https://github.com/odoo/odoo/blob/20.0/addons/stock/models/stock_orderpoint.py); [ERPNext reorder_item.py](https://github.com/frappe/erpnext/blob/develop/erpnext/stock/reorder_item.py)
- 3-way matching — [Odoo control_bills.rst](https://github.com/odoo/documentation/blob/19.0/content/applications/inventory_and_mrp/purchase/manage_deals/control_bills.rst)
- Kiralık araç sözleşmesi + tekrarlayan maliyet + bitiş uyarısı — [Odoo fleet_vehicle_log_contract.py](https://github.com/odoo/odoo/blob/20.0/addons/fleet/models/fleet_vehicle_log_contract.py)
- Onarımda duruş süresi + parça tüketimi + maliyet — [ERPNext asset_repair.json](https://github.com/frappe/erpnext/blob/develop/erpnext/assets/doctype/asset_repair/asset_repair.json); MTBF/MTTR — [Odoo maintenance.py](https://github.com/odoo/odoo/blob/20.0/addons/maintenance/models/maintenance.py)
- Dayanıklı zamanlayıcı (hata sayacı, otomatik devre dışı) — [Odoo ir_cron.py](https://github.com/odoo/odoo/blob/20.0/odoo/addons/base/models/ir_cron.py); RQ kuyrukları + deduplicate — [Frappe background_jobs.py](https://github.com/frappe/frappe/blob/develop/frappe/utils/background_jobs.py)
- Satır/rol/kayıt-değeri izinleri (submit/cancel/amend/export/print/mask, if_owner, permlevel, User Permission) — [Frappe docperm.json](https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/docperm/docperm.json); [user_permission.json](https://github.com/frappe/frappe/blob/develop/frappe/core/doctype/user_permission/user_permission.json)

### Inferences
Önerilen öncelik (Alpnex'in mevcut durumu bu oturumda kodda kontrol edilmedi; her madde için "Alpnex'te zaten var mı" doğrulanmalı):
1. **Transit (yolda) depo ile iki adımlı transfer** — şantiyeler arası nakliyede kayıp/eksik teslim görünürlüğü; hareket defteri ilkesine tam uyumlu. Yüksek değer, orta iş.
2. **Geriye tarih kontrolü**: stok dondurma tarihi + yetkili rol; çevrimdışı geç eşitlenen kayıtlar için "sonraki bakiye/maliyet yeniden hesaplama" arka plan işi (durum + hata günlüğü). Doğruluk için kritik.
3. **Kayıt bazında etkinlik paneli (chatter)**: değişiklik geçmişi + yorum + ek + son tarihli aktivite; "Yapılacaklarım" listesi. Yüksek kullanıcı değeri; audit log ve belge altyapısı varsa düşük-orta iş.
4. **İptal + Düzelt (amend)**: ters kayıt ile iptal edilen belgenin tek tuşla düzeltilmiş kopyası (orijinale bağ, ekler taşınır). Mevcut ters kayıt ilkesine doğal eklenti.
5. **3-way matching / fatura miktarı ≤ teslim alınan** kontrolü ve satın alma talebinde yüzde bazlı durum (kısmen sipariş/teslim).
6. **Kiralık ekipman sözleşmesi**: kira bedeli + sıklık + otomatik maliyet tahakkuku + bitiş uyarısı.
7. **Min/max yeniden sipariş önerisi** (otomatik sipariş değil, öneri listesi + erteleme).
8. **Bakım göstergeleri**: duruş süresi, MTBF/MTTR; bakım/iş emri kanban görünümü.
9. **Zamanlanmış işlerde** hata sayacı/otomatik devre dışı/ilerleme bildirimi; aynı işin çift kuyruklanmasını engelleme (deduplicate).
10. **İzin kontrol listesi**: onayla/iptal/düzelt, dışa aktar/yazdır ayrı yetkiler; "yalnız kendi kaydı"; hassas alan maskeleme; kullanıcıya varsayılan şube.
- Alpnex'in rakiplerden **güçlü** görünebileceği noktalar: sayaç (km/motor saati) bazlı periyodik bakım ve yakıt tüketim analizi — her iki açık kaynak ERP'nin Community/temel sürümünde takvim bazlı bakım ve basit yakıt kaydı görüldü.

### Gaps
- Alpnex'in bu kalıplardan hangilerine zaten sahip olduğu bu araştırma kapsamında kod tabanında kontrol edilmedi.
- ERPNext Report Builder/Query Report/Print Format, Data Import; Odoo import/export ve rapor (QWeb) altyapısı kaynak düzeyinde incelenmedi.
- Her iki ürünün çevrimdışı (offline) çalışma yeteneği doğrulanmadı; genel bilgiye göre ikisi de esas olarak çevrimiçi web uygulamasıdır, Odoo PoS sınırlı çevrimdışı çalışır (unverified). Bu, Alpnex'in çevrimdışı masaüstü + eşitleme mimarisinin farklılaştırıcı olduğunu düşündürür ama kanıtlanmadı.
