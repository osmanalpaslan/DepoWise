# Open-source asset management, CMMS, fleet and small-business ERP references for Alpnex (excluding ERPNext/Odoo)

Access date: 2026-10-12. Method: GitHub REST API metadata (stars, license, last push, latest release) fetched live; source files read directly from raw.githubusercontent.com; official docs/README where reachable. "Verified-in-source" = read in the code itself; "README/marketing" = only claimed in README or product page. Stars are a rough popularity signal only.

## 1. Project profiles: repo, license, activity, purpose, modules

### Takeaway
Snipe-IT (tool/asset custody), Atlas CMMS (maintenance work orders + meter/time triggers) and Fleetbase FleetOps (vehicles, fuel, maintenance schedules by km/engine-hours) are the three most relevant, actively maintained, source-inspectable references for Alpnex. Dolibarr is useful for per-warehouse stock alert levels and intervention statuses. Shelf.nu is a modern UX reference. Tryton, openMAINT and iDempiere/ADempiere matter less. No serious open-source Avalonia ERP/inventory app exists.

### Cited Findings

**Repository metadata (GitHub API, fetched 2026-10-12)**
- Snipe-IT (grokability/snipe-it): ~15.0k stars, AGPL-3.0, PHP (Laravel). Last push 2026-10-12. Latest release v8.8.0 on 2026-09-30. — [GitHub](https://github.com/grokability/snipe-it), [API](https://api.github.com/repos/grokability/snipe-it)
- Atlas CMMS (Grashjs/cmms): ~821 stars, AGPL-3.0 (dual-licensed, see below). Repo language TypeScript, with a Java Spring backend under `api/src/main/java/com/grash`. Last push 2026-10-05. Latest release v1.9.0 on 2026-09-22. — [GitHub](https://github.com/Grashjs/cmms), [API](https://api.github.com/repos/Grashjs/cmms)
- Fleetbase (fleetbase/fleetbase): ~4.3k stars, AGPL-3.0 plus a commercial "Fleetbase Commercial License (FCL)". Last push 2026-10-12. Latest release v0.7.68 on 2026-10-05. — [GitHub](https://github.com/fleetbase/fleetbase)
- Dolibarr (Dolibarr/dolibarr): ~7.7k stars, GPL-3.0, PHP. Last push 2026-10-12. Latest release 24.0.1 on 2026-09-07. — [GitHub](https://github.com/Dolibarr/dolibarr)
- Shelf.nu (Shelf-nu/shelf.nu): ~3.0k stars, TypeScript. Last push 2026-10-12. Latest release shelf@2.3.0 on 2026-09-30. GitHub's API reports the license as NOASSERTION. The README says AGPL-3.0, except `apps/companion/` (the mobile app), which uses PolyForm Internal Use 1.0.0. — [GitHub](https://github.com/Shelf-nu/shelf.nu)
- Tryton (tryton/tryton mirror): ~229 stars, Python. Last push 2026-10-11. No GitHub releases; the license is not reported by the API. GPL-3.0 per general knowledge (unverified here). — [GitHub](https://github.com/tryton/tryton)
- iDempiere: ~667 stars, Java, license NOASSERTION in the API (GPL-2.0 per general knowledge, unverified). Last push 2026-10-12. — [GitHub](https://github.com/idempiere/idempiere)
- ADempiere: ~888 stars, GPL-2.0. Its last GitHub release, 3.9.4.001, dates from 2023-12-20, although the last push was 2026-10-10. — [GitHub](https://github.com/adempiere/adempiere)
- InvenTree (an extra candidate: parts/stock inventory): ~7.7k stars, MIT, Python. Last push 2026-10-12. Latest release 1.5.7 on 2026-10-10. Its features were not inspected in this pass (unverified). — [GitHub](https://github.com/inventree/InvenTree)
- openMAINT: no GitHub repo found under `openmaint/openmaint` (API returned "Not Found"). It is distributed via Tecnoteca and SourceForge. openMAINT 2.4 was released on 30 April 2025 on the CMDBuild 4.0 core. 2.4 added extended SLA configuration, per-step execution-time tracking, and a final requester-approval step in corrective maintenance. — [openMAINT news](https://www.openmaint.org/en/resources/news/openmaint-2-4-release-now-available), [Tecnoteca news](https://www.tecnoteca.com/en/company/news/openmaint-2-4-release-now-available)
- openMAINT license: older (2016) CMDBuild newsletters state AGPL. The license for 2.4 specifically was not confirmed. — [CMDBuild newsletter](https://www.cmdbuild.org/en/reference/newsletter/cmdbuild-newsletter-n.-52)

**Snipe-IT: purpose and modules (verified-in-source)**
- `app/Models` contains: Asset, AssetModel, Accessory/AccessoryCheckout, Consumable/ConsumableAssignment, Component/ComponentAssignment, License/LicenseSeat, PredefinedKit, Statuslabel, CustomField/CustomFieldset, Depreciation, Maintenance/MaintenanceType, CheckoutAcceptance, CheckoutRequest, Actionlog, Company, Location, Department, Supplier, Manufacturer, Labels, Import, ReportTemplate, Ldap/SCIM/SAML, Order/OrderItem, and SyncAdapterConfig/SyncAdapterInstance. — [snipe-it app/Models](https://github.com/grokability/snipe-it/tree/master/app/Models)
- Snipe-IT is an IT and physical asset register with check-in/check-out (custody). Serialized assets, quantity-tracked accessories, consumables and components are separate item types.

**Atlas CMMS: purpose and modules**
- The README calls it a "self-hosted maintenance management system designed for both web and mobile platforms" and "Jira for technicians". — [README](https://github.com/Grashjs/cmms)
- README (marketing) feature list: work orders with time logging and priorities; automation of work orders with triggers; downtime and reliability analytics; inventory with stock alerts; purchase orders and approvals; custom roles; workflows; locations; service requests.
- Domain entities (verified-in-source, `api/.../model`): Asset, AssetDowntime, Meter, MeterCategory, Reading, PreventiveMaintenance, Schedule, WorkOrder, WorkOrderHistory, WorkOrderMeterTrigger, Request, RequestPortal, Part, PartQuantity, PartTransaction, MultiParts, PurchaseOrder, Labor, AdditionalCost, Checklist/Task/TaskOption, CustomField/CustomFieldValue, Workflow/WorkflowAction/WorkflowCondition, Team, Role, Vendor, Customer, FloorPlan, Notification, WebhookEndpoint, ShiftConfiguration, Company. — [GitHub tree](https://github.com/Grashjs/cmms/tree/main/api/src/main/java/com/grash/model)
- Dual license (verified): the user must choose AGPLv3 or a purchased Commercial License from INTELLOOP LLC, effective 8 January 2026. The code contains license/entitlement validation (LicenseService, Keygen DTOs) and a `useLicenseEntitlement` hook. Some features are therefore likely gated by a paid entitlement (which ones is unverified). — [COMMERCIAL_LICENSE.MD](https://github.com/Grashjs/cmms/blob/main/COMMERCIAL_LICENSE.MD)
- Stack: PostgreSQL, MinIO (file storage), JWT, SMTP/SendGrid email, and Docker Compose. — [README](https://github.com/Grashjs/cmms)

**Fleetbase FleetOps**
- README (marketing): Fleet-Ops covers orders, drivers, vehicles, live tracking, route optimization and maintenance. Telematics connects GPS devices. The Pallet WMS is "in development, targeting Q4 2026". There is an open-source Navigator driver app (iOS/Android) with proof of delivery. Modules install as extensions. — [README](https://github.com/fleetbase/fleetbase)
- FleetOps server models (verified-in-source): Vehicle, Driver, Fleet, FleetVehicle, Equipment, Asset, Part, FuelReport, FuelProviderConnection/SyncRun/Transaction, Issue, Maintenance, MaintenanceSchedule, WorkOrder, Warranty, InspectionForm/InspectionItemResult/InspectionSubmission, Telematic, Device/DeviceEvent, Sensor, Trailer, Vendor/VendorPersonnel. — [fleetops repo](https://github.com/fleetbase/fleetops/tree/main/server/src/Models)

**Dolibarr**
- `htdocs` (verified) contains these module folders: `asset`, `bom`, `mrp`, `workstation`, `resource`, `fichinter` (interventions), `product` (incl. stock), `reception`, `expedition`, `ticket`, `holiday`, `knowledgemanagement`. — [Dolibarr htdocs](https://github.com/Dolibarr/dolibarr/tree/develop/htdocs)
- The Asset module classes (verified) are: asset, assetmodel, assetdepreciationoptions, assetaccountancycodes and assetaccountancycodesfiscal. That points to an accounting/depreciation-oriented fixed-asset module, not field custody (inference from class names). — [asset/class](https://github.com/Dolibarr/dolibarr/tree/develop/htdocs/asset/class)
- Equipment sheets with mileage and serial number, technician planning and customer e-signature on interventions come from third-party paid modules (KSIDol "Field Interventions", K.S.I "SERVICEAPRESVENTE"), not core. — [Field Interventions wiki](https://wiki.dolibarr.org/index.php/Field_Interventions_for_Dolibarr), [After-sales wiki](https://wiki.dolibarr.org/index.php/After-Sales_Service_Workshop_%26_On-site_Interventions)

**Shelf.nu (README)**
- Features: QR asset tags (scan to view, check out or report), bookings with checkout and return dates and double-booking prevention, custody, hierarchical locations with GPS, kits, custom fields, categories/tags/saved filters, a bulk scanner, spreadsheet import/export, reminders (maintenance, calibration, warranty), notes and activity log, and Owner/Admin/Base/Self Service roles plus workspaces. — [README](https://github.com/Shelf-nu/shelf.nu)
- Stack: React Router 7, React 19, Prisma 6, PostgreSQL via Supabase, Supabase Auth, pg-boss for jobs, Nodemailer, Vitest and Playwright. The main deployment target is Fly.io. — [README](https://github.com/Shelf-nu/shelf.nu)

**openMAINT (product page)**
- A Property & Facility Management system on CMDBuild. Areas: space and asset inventory, maintenance, logistics, economic management, energy, GIS/BIM. It has a visual workflow engine (Tecnoteca RIVER / Enhydra Shark) and a task manager. Stack: Java, Ext JS, PostgreSQL, JasperReports, Alfresco/CMIS, PostGIS/GeoServer, BIMServer. Cluster and multitenant setups are claimed. — [openMAINT features](https://www.openmaint.org/en/product/features)

**Tryton**
- The Stock module docs list these design concepts: Location, Location Lead Time, Move, Shipment, Inventory, Period, Period Cache, Product Quantities by Warehouse, and Cost Price Revision. The detail pages for location types and move states were not retrievable (404). — [Tryton stock docs](https://docs.tryton.org/latest/modules-stock/index.html)

**Avalonia business/data-heavy desktop apps (GitHub search, 2026-10-12)**
- The searches "avalonia erp", "avalonia inventory", "avalonia pos" and "avalonia accounting" returned only tiny or hobby repos, mostly 0–2 stars. Examples: maxBernd661/AvaloniaERP (2 stars), Mr-Tomate/ComercialPro-ERP (0), nightness/TemPOS.Avalonia (1), Cadlaxa/Office-Inventory-System (0). — [GitHub search API](https://api.github.com/search/repositories?q=avalonia%20erp&sort=stars)
- The only maintained Avalonia data app of note: VMelnalksnis/Gnomeshade, a self-hosted personal finance tracker (35 stars, AGPL-3.0, last push 2026-07-18). — [Gnomeshade](https://github.com/VMelnalksnis/Gnomeshade)

### Inferences
- No open-source Avalonia ERP is mature enough to copy architecture from. Alpnex's Avalonia + SQLite offline + PostgreSQL sync design is effectively unique in open source; the useful references are domain models, not UI stacks.
- Atlas CMMS and Fleetbase are dual-licensed (AGPL + commercial). Copying their code into a closed product is not allowed. Studying their data models and patterns is fine. Snipe-IT is AGPL and Dolibarr is GPL, so the same caution applies.
- Dolibarr's core Asset module is depreciation/accounting oriented and does not fit "zimmet" (personnel custody). Its field-equipment features come from paid third-party add-ons.

### Gaps
- openMAINT 2.4 license text and current repo location were not verified (the SourceForge page did not render).
- iDempiere/ADempiere were reviewed by metadata only. Their asset and maintenance modules were not inspected.
- Which Atlas CMMS features are gated by the commercial entitlement was not determined.
- Tryton location/move detail pages returned 404. Tryton has no dedicated maintenance module per my knowledge (unverified).

## 2. Workflows: check-out/in, acceptance, audits, maintenance, PM triggers, meters, downtime, stock alerts, labels, import, notifications

### Takeaway
The best concrete workflow references are:
- Snipe-IT: check-out with an expected return date, a race-safe acceptance/decline record with signature and EULA PDF, and audit due/overdue windows.
- Atlas CMMS: a request leads to a work order, which is completed with parts, labor, costs, signature and history; preventive maintenance has calendar schedules plus meter triggers based on readings.
- Fleetbase: one maintenance schedule driven by date, distance or engine hours, with next-due fields and reminder offsets; plus fuel reports and inspection checklists.

### Cited Findings

**Snipe-IT check-out/check-in and acceptance (verified-in-source)**
- `Asset.checkOut($target, $admin, $checkout_at, $expected_checkin, $note, $name, $location, bool $signInPlace)` supports an expected return date and an in-place signature flag. — [Asset.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php)
- Assets can be assigned polymorphically: `assigned_type` must be User, Location or Asset (an asset can be checked out to another asset, e.g. a tool to a vehicle). — [Asset.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php)
- Asset fields include `asset_tag` (unique among non-deleted rows: rule `unique_undeleted`), `requestable`, `last_checkout`, `expected_checkin`, `byod`, `last_audit_date`, `next_audit_date`, `rtd_location` (default return location) and `eol` via the model. — [Asset.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php)
- CheckoutAcceptance has `accepted_at`, `declined_at`, `signature_filename`, `note` and `stored_eula_file`:
  - `isPending()` is true when both dates are null.
  - `accept($signature_filename, $eula, $filename, $note)` and `decline(...)` finalize only `whereNull('accepted_at')->whereNull('declined_at')`, and return false if another request already finalized it. This guards against double-accept races.
  - `generateAcceptancePdf()` produces a signed PDF.
  - Scopes: `pending`, `declined`, `forUser`.
  — [CheckoutAcceptance.php](https://github.com/grokability/snipe-it/blob/master/app/Models/CheckoutAcceptance.php)
- A default EULA text is configurable in Settings (`default_eula_text`). — [Setting.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Setting.php)
- The `CheckoutRequest` model and the `requestable` flag support user-initiated requests for assets. — [app/Models](https://github.com/grokability/snipe-it/tree/master/app/Models)

**Snipe-IT audits (verified-in-source)**
- `scopeDueForAudit` selects assets whose `next_audit_date` is between today and today + `settings.audit_warning_days`, excluding archived ones.
- `scopeOverdueForAudit` selects `next_audit_date < today`.
- There are also `scopeDueOrOverdueForAudit` and `scopeDueOrOverdueForCheckin` (overdue returns).
- An `audits()` relation stores audit history.
- The global audit interval setting name was not seen (unverified).
— [Asset.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php)
- When `alerts_enabled` is on, the scheduler runs `snipeit:inventory-alerts`, `snipeit:expiring-alerts`, `snipeit:expected-checkin` and `snipeit:upcoming-audits` daily, plus `snipeit:backup` weekly. — [app/Console/Kernel.php](https://github.com/grokability/snipe-it/blob/master/app/Console/Kernel.php)

**Snipe-IT consumables and maintenance (verified-in-source)**
- Consumable has `qty` and `min_amt`. Remaining stock is `qty - consumables_users_count`, and `percentRemaining()` is computed from it. The low-stock alert comes from the daily inventory-alerts command. — [Consumable.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Consumable.php)
- Maintenance is polymorphic (`item_type`/`item_id`, migrated from legacy `asset_id`). Fields: `maintenance_type_id` (required), `name`, `is_warranty`, `start_date`, `expected_completion_date`, `completed_at` (before now), `cost`, `supplier_id`, `responsible_party_id`, `completed_by`, `url`, `notes`. It is a maintenance log, not a preventive-maintenance engine. — [Maintenance.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Maintenance.php)

**Atlas CMMS (verified-in-source)**
- `WorkOrderBase` (shared by WorkOrder, PreventiveMaintenance, Request and WorkOrderMeterTrigger): `dueDate`, `priority`, `estimatedDuration`, `estimatedStartDate`, `title`, `description`, `requiredSignature`, `image`, `category`, `location`, `team`, `primaryUser`, `assignedTo` (many), `customers`, `files`, `asset`. — [WorkOrderBase.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/abstracts/WorkOrderBase.java)
- WorkOrder adds:
  - completion: `status` (default OPEN), `completedBy`, `completedOn`, `signature`, `feedback`, `firstTimeToReact` (a response-time KPI), `archived`
  - origin: `parentRequest`, `parentPreventiveMaintenance`
  - extra data: `customFieldValues`
  — [WorkOrder.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/WorkOrder.java)
- Request (maintenance request): `cancelled`, `cancellationReason`, `audioDescription` (voice note file), a link to the created `workOrder`, `requestPortal` (a public request portal), `contact`. — [Request.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Request.java)
- Time-based preventive maintenance:
  - PreventiveMaintenance has a one-to-one Schedule.
  - Schedule fields: `startsOn`, `endsOn`, `frequency`, `recurrenceType` (default DAILY), `recurrenceBasedOn` (default SCHEDULED_DATE; the alternative presumably bases the next date on completion date — unverified), `daysOfWeek`, `dueDateDelay`, `disabled`.
  - Background jobs `WorkOrderCreationJob` and `PreventiveMaintenanceNotificationJob` generate the work orders and notifications.
  — [Schedule.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Schedule.java), [job folder](https://github.com/Grashjs/cmms/tree/main/api/src/main/java/com/grash/job)
- Meter-based triggers: `Meter` has `name`, `unit`, `updateFrequency`, `meterCategory`, `asset`, `location` and `users` (assigned readers). `Reading` has a `value` and a `meter` and extends Audit, so it carries a timestamp and author. `WorkOrderMeterTrigger` has `recurrent`, `triggerCondition` (enum), `value`, `waitBefore` and `meter`, plus all WorkOrderBase template fields. — [Meter.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Meter.java), [Reading.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Reading.java), [WorkOrderMeterTrigger.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/WorkOrderMeterTrigger.java)
- Downtime: `AssetDowntime` has `asset`, `startsOn` and `duration`. Analytics DTOs include DowntimesByAsset, DowntimesByDate, DowntimesMeantimeByDate and DowntimesAndCostsByAsset. — [AssetDowntime.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/AssetDowntime.java), [analytics dto](https://github.com/Grashjs/cmms/tree/main/api/src/main/java/com/grash/dto/analytics/assets)
- Parts:
  - Part: `quantity`, `minQuantity` (low-stock threshold), `barcode`, `unit`, `cost`, `nonStock`, `area`, `version` (optimistic locking), plus links to assets, vendors and preventiveMaintenances.
  - `PartQuantity` links a part and quantity to a WorkOrder or a PurchaseOrder.
  - `PartTransaction` records part movement against a work order.
  - Labor and AdditionalCost models exist for labor and other costs.
  — [Part.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Part.java), [PartQuantity.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/PartQuantity.java), [PartTransaction.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/PartTransaction.java)
- History: `WorkOrderHistory` has `name`, `user` and `workOrder`, and extends Audit. — [WorkOrderHistory.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/WorkOrderHistory.java)
- Import: import DTOs exist for meters and preventive maintenance (MeterImportDTO, PreventiveMaintenanceImportDTO). — [dto/imports](https://github.com/Grashjs/cmms/tree/main/api/src/main/java/com/grash/dto/imports)
- Email notifications and email invitations are toggled with `ENABLE_EMAIL_NOTIFICATIONS` and `INVITATION_VIA_EMAIL`. — [README](https://github.com/Grashjs/cmms)

**Fleetbase FleetOps (verified-in-source)**
- MaintenanceSchedule fields:
  - target: `subject_type`/`subject_uuid` (polymorphic: vehicle, equipment…)
  - interval: `interval_method`, `interval_type`, `interval_value`, `interval_unit`, `interval_distance`, `interval_engine_hours`
  - last service: `last_service_odometer`, `last_service_engine_hours`, `last_service_date`
  - next due: `next_due_date`, `next_due_odometer`, `next_due_engine_hours`
  - defaults for the generated job: `default_priority`, `default_assignee_type`/`uuid`, `instructions`
  - other: `reminder_offsets` (array), `status`
  - It uses an activity log (LogsActivity trait).
  — [MaintenanceSchedule.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/MaintenanceSchedule.php)
- Maintenance (the performed record) fields:
  - links: `work_order_uuid`, polymorphic `maintainable_type`/`uuid`
  - classification: `type`, `status`, `priority`
  - timing: `scheduled_at`, `started_at`, `completed_at`
  - readings at service: `odometer`, `engine_hours`
  - who did it: `performed_by_type`/`uuid`
  - costs: `line_items`, `labor_cost`, `parts_cost`, `tax`, `total_cost`, `currency`
  - other: `attachments`
  - computed values: `is_overdue`, `days_until_due`, `duration_hours`, `cost_breakdown`
  — [Maintenance.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/Maintenance.php)
- FuelReport fields: `driver_uuid`, `vehicle_uuid`, `reported_by_uuid`, `odometer`, `location` (spatial point), `amount`, `currency`, `volume`, `metric_unit`, `status`, `meta`. Separate FuelProviderConnection/SyncRun/Transaction models handle fuel-card imports. — [FuelReport.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/FuelReport.php)
- InspectionSubmission (pre-trip or periodic inspection checklist) fields: `inspection_form_uuid`, `vehicle_uuid`, `driver_uuid`, `result`, `odometer`, `engine_hours`, `total_items`, `failed_items`, `started_at`/`submitted_at`/`resolved_at`, `signature`, `attachments`, `location`. It links to `issue_uuid` and `work_order_uuid`, so a failed inspection can raise an issue or work order. — [InspectionSubmission.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/InspectionSubmission.php)
- Issue (defect report) fields: `reported_by_uuid`, `assigned_to_uuid`, `vehicle_uuid`, `driver_uuid`, `category`, `type`, `priority`, `status`, `tags`, `location`, `resolved_at`. — [Issue.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/Issue.php)

**Dolibarr (verified-in-source)**
- Product has a global `seuil_stock_alerte` (alert threshold) and `desiredstock`. `ProductStockEntrepot` repeats `seuil_stock_alerte` and `desiredstock` per product × warehouse (`fk_product`, `fk_entrepot`), so alert and replenishment levels can differ by depot. `status_batch` enables lot/serial tracking. — [product.class.php](https://github.com/Dolibarr/dolibarr/blob/develop/htdocs/product/class/product.class.php), [productstockentrepot.class.php](https://github.com/Dolibarr/dolibarr/blob/develop/htdocs/product/stock/class/productstockentrepot.class.php)
- An intervention (Fichinter) has statuses DRAFT 0, VALIDATED 1, BILLED 2, CLOSED 3, CANCELED 9. It also has `dateo`/`datee` (start/end), `duration`, `fk_contrat` and `fk_project`. — [fichinter.class.php](https://github.com/Dolibarr/dolibarr/blob/develop/htdocs/product/../fichinter/class/fichinter.class.php)

**Shelf.nu (README, not source-verified)**
- QR scan lets anyone view, check out or report an asset. A bulk scanner assigns custody, changes location or adds to a booking. Bookings prevent double-booking. Kits bundle assets. Reminders cover maintenance, calibration and warranty. — [README](https://github.com/Shelf-nu/shelf.nu)

### Inferences
- **Zimmet.** The Snipe-IT acceptance model maps directly onto Alpnex "zimmet": create the acceptance row at checkout and keep it pending until the employee signs, on the tablet or desktop (sign-in-place) or later. Store the signature file and a frozen EULA/zimmet-form PDF. Finalize with conditional updates (`WHERE accepted_at IS NULL AND declined_at IS NULL`). That is also a natural conflict-safe rule for offline sync.
- **Periodic tool counts ("zimmet sayımı").** Snipe-IT's `next_audit_date` + `audit_warning_days` + daily job gives a simple pattern: per-item next verification date, "due soon" and "overdue" lists, and a daily notification.
- **Maintenance scheduling.** Fleetbase's MaintenanceSchedule is the closest fit for vehicles and heavy equipment. One record holds date, distance and engine-hour intervals, last-service values and precomputed next-due values, which makes alert queries cheap and works offline in SQLite. Atlas's Meter/Reading/MeterTrigger split is cleaner if Alpnex wants generic meters (km, hours, cycles) with reading history.
- **Fuel.** Fleetbase FuelReport (odometer + volume + amount + reporter + location) is a minimal fuel-entry schema. The odometer value can also feed meter readings.
- **Work orders.** Atlas's chain (Request → WorkOrder with `parentRequest` and `parentPreventiveMaintenance`; PartQuantity/PartTransaction; Labor; signature; firstTimeToReact) is a good blueprint for "arıza bildirimi → iş emri → kapanış" (fault report → work order → close-out) with spare parts consumed from depot stock.
- **Stock.** Dolibarr's per-warehouse alert and desired levels are the right granularity for Alpnex's per-site depots.

### Gaps
- Snipe-IT's CSV import field mapping, label/QR template system (`app/Models/Labels`) and bulk checkout UI were not inspected in detail.
- Atlas CMMS `WorkOrderMeterTriggerCondition` enum values (presumably MORE_THAN/LESS_THAN, unverified) and `RecurrenceBasedOn` values were not read.
- Fleetbase's computation of `next_due_*` (which job or observer) was not inspected.
- Snipe-IT doc pages (snipe-it.readme.io /docs/auditing, /docs/checkinout) returned 404, so UI-level descriptions come from source only.

## 3. Technical design: audit log, soft delete, permissions, scheduler, multi-tenancy, stack

### Takeaway
All reviewed systems use:
- a central activity/audit log with polymorphic subject and target
- a company/tenant scope on every row
- scheduled daily jobs for alerts
Snipe-IT shows soft delete with uniqueness scoped to non-deleted rows; Atlas shows a shared "work-order base" template inherited by work orders, requests, PM and triggers. None is offline-first.

### Cited Findings
- **Snipe-IT Actionlog** (one polymorphic table for all history: `item_type`, `target_type`, `action_type`, `note`, `log_meta` (field diff), `action_date`, `stored_eula`, `filename`, `remote_ip`, `user_agent`, `action_source`). Methods: `logaction()` and `logUploadDelete()`. — [Actionlog.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Actionlog.php)
- **Snipe-IT soft delete:** Asset uses the `SoftDeletes` trait. The asset_tag rule `unique_undeleted:assets,asset_tag` allows reuse only after deletion. Relations use `withTrashed()` so history still resolves deleted users and locations. — [Asset.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php)
- **Snipe-IT multi-company:** `CompanyableScope`/`CompanyableChildScope` traits and ICompanyableChild scope queries by company. Groups, SCIM, LDAP and SAML handle identity. — [app/Models](https://github.com/grokability/snipe-it/tree/master/app/Models)
- **Snipe-IT scheduler:** Laravel scheduler with daily alerts, weekly backup and the `snipeit:pull-inventory`/`push-inventory` sync adapters run `withoutOverlapping()`. — [Kernel.php](https://github.com/grokability/snipe-it/blob/master/app/Console/Kernel.php)
- **Atlas CMMS:** entities extend `Audit` or `CompanyAudit` (company-scoped and audited base classes). Part has a `version` field. Roles are customizable (README), and there are Workflow/WorkflowCondition/WorkflowAction entities (rule automation), WebhookEndpoint, ApiKey and push-notification tokens. — [model folder](https://github.com/Grashjs/cmms/tree/main/api/src/main/java/com/grash/model), [README](https://github.com/Grashjs/cmms)
- **Atlas CMMS data flags:** `isDemo` appears on many entities, and `DeleteDemoCompaniesJob` purges demo data. — [job folder](https://github.com/Grashjs/cmms/tree/main/api/src/main/java/com/grash/job)
- **Fleetbase:** every FleetOps model carries `company_uuid` (multi-tenant). Maintenance, MaintenanceSchedule, Issue and InspectionSubmission use the `LogsActivity` trait; FuelReport does not. None of the five inspected models use SoftDeletes (whether a base model handles deletion was not checked). There are `created_by_uuid`/`updated_by_uuid` columns. — [fleetops Models](https://github.com/fleetbase/fleetops/tree/main/server/src/Models)
- **Shelf.nu:** pg-boss (a PostgreSQL-backed job queue) for background jobs. Roles: Owner/Admin/Base/Self Service. Workspaces are separate inventories. — [README](https://github.com/Shelf-nu/shelf.nu)
- **openMAINT:** a workflow engine with a visual editor, a task manager for automation, and claimed cluster/multitenant support. — [openMAINT features](https://www.openmaint.org/en/product/features)

### Inferences
- **Audit log.** Alpnex could keep one ActionLog-style table: entity type/id, target type/id, action, JSON diff, source (desktop/web/API/sync), device/IP, timestamp. This also helps sync conflict forensics; Snipe-IT's `action_source` is a direct analogue.
- **Soft delete.** Uniqueness of asset codes and plates should apply only among non-deleted rows, and history views should include deleted references.
- **Shared base entity.** A WorkOrderBase-like template shared by fault report, work order and maintenance plan avoids duplicated fields. The plan acts as a template that spawns work orders.
- **Offline.** None of these projects solve offline desktop sync. Alpnex's SQLite↔PostgreSQL sync remains its own design. Atlas's `version` column (optimistic locking) is the only directly reusable concurrency idea seen.

### Gaps
- Snipe-IT's permission matrix (`permissions` JSON on groups/users) was not read in source this pass (unverified details).
- No restore UI or soft-delete strategy was verified for Atlas CMMS or Fleetbase.

## 4. Patterns worth adopting in Alpnex (prioritized)

### Takeaway
High-value, low-risk adoptions:
1. a zimmet acceptance record with signature, PDF and pending/declined states
2. per-item next-audit dates with due/overdue windows
3. one maintenance schedule per asset covering date, km and engine hours with next-due fields and reminder offsets
4. a request → work order → completion chain with parts consumption and labor
5. per-depot min/desired stock
6. a polymorphic activity log with source
7. inspection checklists that raise faults

### Cited Findings
- Zimmet acceptance: Snipe-IT CheckoutAcceptance (accept/decline with signature, EULA PDF, race-safe finalize). — [CheckoutAcceptance.php](https://github.com/grokability/snipe-it/blob/master/app/Models/CheckoutAcceptance.php)
- Expected return and overdue checkin: `expected_checkin` + `scopeDueOrOverdueForCheckin` + the daily `snipeit:expected-checkin` command. — [Asset.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php), [Kernel.php](https://github.com/grokability/snipe-it/blob/master/app/Console/Kernel.php)
- Assign to person, location or another asset: the polymorphic `assigned_type`. — [Asset.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Asset.php)
- Separate item classes for serialized assets, accessories (quantity, returnable), consumables (quantity, not returned, `min_amt`) and components (installed into an asset). — [app/Models](https://github.com/grokability/snipe-it/tree/master/app/Models), [Consumable.php](https://github.com/grokability/snipe-it/blob/master/app/Models/Consumable.php)
- Multi-unit preventive maintenance schedule (date, distance, engine hours, reminder offsets, default assignee and instructions). — [MaintenanceSchedule.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/MaintenanceSchedule.php)
- Maintenance cost breakdown (labor, parts, tax, total) and odometer/engine hours at service time. — [Maintenance.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/Maintenance.php)
- Meter reading history and meter-based triggers. — [Reading.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Reading.java), [WorkOrderMeterTrigger.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/WorkOrderMeterTrigger.java)
- Downtime records feeding MTTR/downtime reports. — [AssetDowntime.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/AssetDowntime.java)
- Request → work order linkage, signature at completion and a first-response KPI. — [WorkOrder.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/WorkOrder.java), [Request.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Request.java)
- Inspection checklist results (total and failed items, signature) linked to issue and work order. — [InspectionSubmission.php](https://github.com/fleetbase/fleetops/blob/main/server/src/Models/InspectionSubmission.php)
- Per-warehouse alert and desired stock. — [productstockentrepot.class.php](https://github.com/Dolibarr/dolibarr/blob/develop/htdocs/product/stock/class/productstockentrepot.class.php)
- QR scan-to-act (view, check out, report) and a bulk scanner for custody and location changes. — [Shelf.nu README](https://github.com/Shelf-nu/shelf.nu)
- Voice-note attachment on requests (useful for field staff with low literacy or typing difficulty). — [Request.java](https://github.com/Grashjs/cmms/blob/main/api/src/main/java/com/grash/model/Request.java)

### Inferences
- **Priority for Alpnex:**
  - (a) zimmet acceptance + overdue return + periodic count — directly addresses construction tool loss
  - (b) Fleetbase-style km/hour/date maintenance schedule with precomputed next-due fields — simple SQLite queries offline
  - (c) Atlas-style work order chain with part consumption posted as depot stock movements
  - (d) inspection checklist → fault → work order for heavy equipment daily checks
  - (e) per-depot min stock alerts
- **Avoid copying:**
  - openMAINT's BIM/GIS/workflow-engine complexity and iDempiere's generic dictionary-driven UI are oversized for the target users.
  - Dolibarr's Asset module is accounting/depreciation, not custody.
- **License caution:** the patterns can be reimplemented, but AGPL/GPL code must not be copied into Alpnex's proprietary code.

### Gaps
- No evidence on how these products handle Turkish-specific needs (e-İrsaliye, KVKK consent for signatures). This is out of scope for these projects.
- User-experience quality (screens, buttons) was not assessed visually. Screen-level claims come from README text and model fields only.
