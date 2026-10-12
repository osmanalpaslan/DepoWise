# Global construction ERP / construction management software: workflows relevant to Alpnex (site depot + heavy equipment + light accounting)

Access date: 2026-10-12. Open web only. Primary sources were vendor help centers: help.trimble.com / help.viewpoint.com (Vista, Spectrum), support.procore.com, learn.microsoft.com, intacct.com docs and docs.cmicglobal.com. Third-party pages (cleverence, followupcrm, greytrix, softwareconnect, forconstructionpros, softwareone marketplace) are secondary and labelled as such. Statements marked "(unverified)" were not confirmed by a primary source in this session.

**Comparability (why these products).**
- **Trimble Viewpoint Vista** is the closest functional analogue to Alpnex. It is an on-premises/hosted ERP for heavy-civil and self-performing contractors, with separate modules for Inventory (IN), Equipment Management (EM), Purchase Order (PO), Job Cost (JC) and Payroll.
- **Procore** is a project-management and field platform, not an ERP. It is relevant only for field capture (Daily Log), the equipment registry, permission templates and observation/inspection workflows.
- **CMiC**, **Foundation**, **Spectrum** and **Sage 300 CRE / Intacct Construction** are US construction accounting ERPs with equipment-costing modules.
- **Dynamics 365** (Project Operations, Supply Chain) is a generic ERP. It is relevant for inventory/cycle-count patterns.
- **Not researched (out of tool budget):** InEight, RIB iTWO and Jonas. See the Gaps sections.

---

## 1. Material request → requisition → PO → receiving → issue to job; valuation; transfers; counts; reorder

### Takeaway
Mature construction ERPs split the material chain into separate documents/forms, and each has its own approval and posting step:
- **Requisition**, with approval levels and approval groups. Free-form or non-stock items are allowed.
- **Conversion** of the approved requisition into a PO, or into an inventory issue from stock.
- **PO receipt** against the PO *line distribution*. Bulk-receipt forms exist, and posted receipts can still be edited or deleted.
- **Inventory module**: locations, transfers between locations, adjustments, a physical-count worksheet, and material orders/allocations to jobs and equipment.

Cycle counting in D365 is threshold- or plan-driven, uses blind mobile counts, and sends differences that exceed tolerance to a supervisor-review queue. Alpnex has most document types already. The candidate gaps are:
- receipt at PO-line-distribution level, with a "receiving required" flag per PO line;
- requisition → "issue from stock OR create PO" branching;
- counts that are blind, tolerance-based and approved by a supervisor;
- threshold-triggered counts;
- material orders / allocated inventory (reservation).

### Cited Findings

**CMiC requisitions**
- The requisition module can request inventory, non-stock and free-form items for a job, for equipment maintenance or for other company needs. An approved request can be picked up for issue in the Inventory module, or it can generate a PO directly — [CMiC docs, via search snippet](https://docs.cmicglobal.com/portal/Content/Resources/PDFs/v10x/RQv10_x_ADF.pdf)
- The CMiC Requisitions manual (v10x) is structured as follows ([CMiC RQ v10x PDF outline](https://docs.cmicglobal.com/portal/Content/Resources/PDFs/v10x/RQv10_x_ADF.pdf)):
  - Requisition Entry (Header, Details, "Entering a Free-Form Item");
  - Requisition Approval, and a separate **Requisition Unapproval**;
  - Create Purchase Order From Requisition;
  - Requisition Query and **Requisition Approver Query**;
  - setup tables: User ID Cross-Reference, Requisition Roles, Rules of Approver Setup, Approval Levels, Approvers, Approval Groups, Group Approvers Maintenance, Approval Group Members, Non-Stock Items, Vendor Items.

  The body text could not be extracted, so thresholds and delegation details are unverified.

**Vista PO receiving**
- Only a PO item line flagged **"Receiving"** (a checkbox on the Info tab of PO Purchase Order Entry) can be received — [Trimble Vista: Enter a Receipt in PO Receipts Entry](https://help.trimble.com/en/vista/vista/costs-and-contracts/purchase-order/receiving/enter-a-new-receipt)
- **PO Receipts Entry** is used to enter individual receipts and to edit or delete receipts that were already posted. **PO Initialize Receipts** enters large numbers of receipts in bulk. Receipts are entered against the PO item *lines* (PO Item Distribution), not against the PO item header — [same source](https://help.trimble.com/en/vista/vista/costs-and-contracts/purchase-order/receiving/enter-a-new-receipt)
- Trimble's separate **Viewpoint Field Management** app has a PO Receiving module that opens a receiving batch, validates it and posts it. Notes entered in the field appear on the Notes tab of PO Receipts Entry in Vista — [Trimble: Receive a PO (Field Management)](https://help.trimble.com/en/viewpoint-field-management/viewpoint-field-management/field-management/purchase-orders-field-management/receive-a-po)
- The Vista "About Receiving in the PO Module" page returned HTTP 404 on 2026-10-12. As a result, the receipt-expensing option, 3-way match rules and backorder handling are **unverified** for Vista — [404 URL](https://help.trimble.com/en/vista/vista/costs-and-contracts/purchase-order/receiving/about-receiving-in-the-po-module)
- A generic description of 3-way match comes from Deltek Vision, a different product, so it is not Vista behavior. A PO and invoice must also match a receiving record before an AP voucher is created, and 3-way match is mandatory for inventory items — [Deltek Vision help](https://help.deltek.com/product/Vision/7.6/ap_Matching_Methods_for_AP_Vouchers_Created_from_Purchase_Orders.html)

**Procore commitments (PO)**
- Default PO statuses include **"Partially Received"** (partially fulfilled by the contract company), shown under *Pending Cost Changes* in the budget. **"Approved"** moves the amount to *Committed Costs*. **"Closed"** is defined by the project team, for example when the PO is complete and invoiced — [Procore FAQ: default commitment statuses](https://support.procore.com/faq/what-are-the-default-commitment-statuses-in-procore)
- Users commonly tick the **"Executed"** box when moving a PO to Approved/Complete — [Procore: Create Purchase Orders](https://support.procore.com/products/online/user-guide/project-level/commitments/tutorials/create-purchase-orders)

**Vista Inventory (IN)**
- IN tracks all stocked materials, integrates with other accounting modules, tracks inventory sold to customers, jobs and **equipment**, and handles transfers between locations — [Trimble Vista: Inventory Reports](https://help.trimble.com/en/vista/vista/reports-catalog/inventory-reports)
- Locations are set up on **IN Locations**. Transfers are entered in **IN Transfer Entry**, and the material must be stocked at *both* the transferring and the destination location. **IN Adjustment Entry** covers write-offs, damage and mis-postings. The **IN Physical Count Worksheet** resolves differences between the physical and the computer count — [Viewpoint Vista Inventory (search snippet)](https://help.viewpoint.com/vista/vista/job-resources/inventory) (direct fetch failed: expired certificate)
- Vista IN has 30 standard reports ([Trimble Vista: Inventory Reports](https://help.trimble.com/en/vista/vista/reports-catalog/inventory-reports)):
  - **Material orders:** IN Material Orders, IN MO Worksheet.
  - **Reorder and valuation:** IN Restocking Report, IN Value of Inventory, IN Value of Inventory at Month End, IN Negative Price/Cost Report.
  - **Counts:** IN Physical Count, IN Physical Count Worksheet.
  - **Reconciliation and usage:** IN Monthly Reconciliation Detail/Report, IN Monthly Usage Vs. Production.
  - **Allocations and BOM:** IN Allocated Inventory, IN Bill of Materials, IN Bill of Materials Overrides.
  - **Current stock and history:** IN Current Inventory Drilldown, IN Inventory History Drilldown, IN Inventory Totals By Matl And Location, IN Inventory Transaction Detail.
  - **Locations:** IN Location Groups, IN Location Category / Company Overrides, IN Plant Summary.
  - **Setup and supplier:** IN Supplier Listing.
- The Vista costing method (average/FIFO/LIFO) is **not stated** on the pages retrieved — [same source](https://help.trimble.com/en/vista/vista/reports-catalog/inventory-reports)
- Viewpoint ProContractor's Materials (Inventory) tasks include "Create a Warehouse Transfer Entry" and "Count Inventory" — [Viewpoint ProContractor: Materials (Inventory) Tasks](https://help.viewpoint.com/procontractor/procontractor/accounting/accounting/materials-inventory-tasks)

**Dynamics 365 (Project Operations / Finance and Supply Chain Management)**
- A project PO can generate **project item requirements**, and each PO line stays linked to its requirement. This requires the "Create Item requirements" and "Item consumption" parameters. Project transactions generally use **weighted-average** costing, and an inventory recalculation is needed after PO adjustments — [MS Learn: PO for stocked items with item requirements](https://learn.microsoft.com/zh-cn/dynamics365/project-operations/prod-pma/stkprojectcostwithtemrequirements)
- For procurement-category PO lines, a separate parameter lets items be invoiced to the project before the vendor invoice arrives (Finance 10.0.44+) — [MS Learn: item requirements for procurement categories](https://learn.microsoft.com/lt-lt/dynamics365/project-operations/prod-pma/create-item-requirements-procurement-categories)
- Cycle counting in D365 Supply Chain Management has three steps: create counting work → count (on a mobile device) → resolve differences ([MS Learn: Cycle counting](https://learn.microsoft.com/en-us/dynamics365/supply-chain/warehousing/cycle-counting)):
  - Work is created automatically by a **threshold**: when on-hand quantity falls below the threshold after a pick, count work is created. It can also come from a **cycle counting plan** (batch job; maximum counts per item; days between counts), or be created manually by item or by location.
  - Stock is **not blocked** while counting work is open.
  - Mobile modes are user-directed, system-directed, grouping and **spot counting**.
  - The device **never shows the expected quantity** (blind count). A "Number of attempts" setting allows recounts.
  - Counts outside a worker's maximum percentage or quantity deviation go to **Pending review**. A supervisor approves the difference; if it is not approved, the count is rejected.

### Inferences
- Alpnex's "count" module would benefit from three changes:
  - a blind-count option (hide the system quantity);
  - a per-user or per-role tolerance (percentage or quantity), so that only out-of-tolerance differences need a supervisor's approval;
  - count tasks triggered automatically by a minimum threshold or by "N days since last count" per depot.
- Vista's rule that a transfer requires the material to be stocked at both locations suggests a material-by-depot master with site-specific settings: min/max, preferred supplier, bin location.
- Receipts at PO-line level, plus a bulk-receipt screen and the ability to edit or reverse posted receipts with an audit trail, are standard. For partial deliveries Alpnex should show ordered, received and remaining quantities per line, and a PO status "Partially received".
- Requisition → "fulfil from stock (issue/transfer) or buy (PO)" is a documented branch in CMiC. Alpnex's purchase request flow could add a "Fulfil from depot stock" button, which creates a transfer or issue instead of a PO.

### Gaps
- Vista's receipt-expensing option and its AP 3-way match configuration (the help page returned 404).
- Valuation methods in Vista, CMiC and Spectrum were not found.
- The in-transit state for site-to-site transfers was not confirmed for any construction product. D365 transfer orders have in-transit warehouses, but this was not fetched (unverified).
- Reorder-point / auto-PO generation was found only as a report name ("IN Restocking Report"); the mechanism is unverified.
- CMiC approval thresholds and delegation: the PDF body could not be decoded.

---

## 2. Job / phase / cost-code structure; charging equipment usage, fuel and materials to jobs (internal rental rates)

### Takeaway
The dominant pattern is the Vista EM model:
- Equipment has **revenue rates per category × revenue code** (hourly, daily, weekly, monthly, standby), with **per-equipment overrides** and **job-specific rate templates**.
- Usage is posted from payroll timecards, from EM Usage Posting or from **Automatic Usage**.
- The posted amount is **credited to the equipment as revenue and debited to the job as cost** (internal rental), using a **Usage Cost Type** on the equipment.
- Fuel, parts and cost adjustments post to equipment and can update meters.
- Overhead is allocated to equipment monthly.
- Depreciation posts to the GL.

Procore's equipment hours flow from Timesheets and the Daily Log into the Budget by cost code.

### Cited Findings

**Vista Equipment Management (EM)** — [Trimble Vista: Equipment Management](https://help.trimble.com/doc/vista/vista/job-resources/equipment-management) unless noted.
- Usage rates (hourly, daily, weekly, standby) are assigned to equipment, and usage can be charged to **jobs, other equipment or overhead**. Usage is posted through **Payroll timecards** or through the **EM Usage Posting** and **Automatic Usage** programs. Revenue rates are set by category, can be overridden per equipment, and job templates can hold special rates.
- The posted dollars are credited to the equipment as income, and the job takes a direct cost of the same amount — [search snippet of Trimble EM revenue pages](https://help.trimble.com/en/vista/vista/job-resources/equipment-management/revenuecosts)
- A revenue rate should exist for every category × revenue code combination used. Revenue codes are created for each billing time increment (hourly, daily, monthly, hourly standby) — [About the EM Revenue Rates by Category Form](https://help.trimble.com/en/vista/vista/job-resources/equipment-management/revenuecosts/revenuecost-setup/about-the-em-revenue-rates-by-category-form)
- **EM Revenue Rates by Equipment** has an "Override Rates" checkbox. A later change to the category's standard rate does not affect the equipment-level overrides — [About the EM Revenue Rates by Equipment Form](https://help.trimble.com/en/vista/vista/job-resources/equipment-management/revenuecosts/revenuecost-setup/about-the-em-revenue-rates-by-equipment-form)
- Each equipment record needs a **Usage Cost Type**, the Job Cost cost type that usage cost interfaces to — [Set Up and Process Equipment Revenue](https://help.trimble.com/zh-tw/vista/vista/job-resources/equipment-management/revenuecosts/revenuecost-setup/set-up-and-process-equipment-revenue)
- Other documented EM functions:
  - **Overhead / cost allocations** to equipment, typically monthly.
  - **Revenue breakdown codes** to categorize equipment revenue.
  - **Usage posting** from EM, Material Sales (MS) and Payroll (PR).
  - **Fuel posting**, which enables metrics such as miles per gallon.
  - **Parts and cost adjustments.**
  - **EM Company Parameters**, which control whether usage, cost adjustment, parts and fuel postings update the odometer and hour meters.
  - **EM Meter Readings**, which keep a history file (EMMR). Entering an unchanged reading still records the date. A meter replacement is entered on the equipment record.
  - **Component tracking** (engines, transmissions; location and cost).
  - **Equipment attachments** (bucket, trailer) that move with the parent unit.
  - **EM Location Transfer** of equipment and tools between jobs, shops and yards.
  - **EM Depreciation Processing**, which posts to the GL.

**Procore**
- Daily Log equipment entries capture ([Procore: Add Equipment Entries to a Daily Log (iOS)](https://v2.support.procore.com/product-manuals/daily-log-ios/tutorials/add-equipment-entries-to-a-daily-log-ios/)):
  - company, equipment, **Hours Operating**, **Hours Idle**, **cost code**, location, an inspection checkbox with inspection time, and comments.
  - Equipment must first exist in the project Equipment tool.
  - To sync with Equipment Timesheets, the required fields in the Daily Log Equipment fieldset must match the Timesheets fieldset, and the log must not be marked Complete.
- Equipment time entered in Timesheets flows into the **Budget** to track equipment cost. Additional equipment costs can be tracked on **T&M Tickets** — [Procore: About the Equipment Tool](https://support.procore.com/products/online/user-guide/company-level/equipment/tutorials/about-the-equipment-tool)

**Foundation Software** (vendor marketing and a reseller listing)
- Equipment costs entered, for example from payroll timecards, automatically update both the equipment module and job costing — [Foundation article PDF](https://www.foundationsoft.com/wp-content/uploads/articles/equipment-costing-key-to-better-bidding-and-higher-profits.pdf)
- Mileage and equipment hours are captured with **user-defined job cost rates** — [Foundation: Heavy Highway](https://www.foundationsoft.com/heavy-highway/)
- The module covers operating hours, fuel, repairs, depreciation, internal charges, job revenue, **owner rentals and internal chargebacks with audit trails** — [SoftwareOne marketplace listing (secondary)](https://platform.softwareone.com/product/construction-equipment-management/PCP-0244-1758)

**Sage 300 CRE** (secondary sources only)
- A Sage City forum user allocates equipment to jobs with **Revenue in EQ**. Usage hours are imported by **CSV** from third-party software, and all EQ cost appears in Job Cost reports — [Sage City forum, Sage 300 CRE Job Cost tag](https://www.sagecity.com/us/sage_construction_and_real_estate/f/sage-300-construction-and-real-estate/tags/processing/Sage%2b300%2bCRE/Job%2bCost)

**Spectrum**
- The Equipment reports include ([Trimble Spectrum: Equipment Control reports overview](https://help.trimble.com/es-mx/spectrum/spectrum/equipment/equipment-control/spectrum-menus/reports-overview)):
  - Equipment Revenue Report, Equipment Unit Cost Report, **Equipment Utilization Report**;
  - **Fuel & Oil Report**, **Fuel Utilization Report**;
  - E/C G/L Distribution Report;
  - **Warranty Expiration Report**.

**Sage Intacct Construction**
- Cost types are mapped from the resource types Labor, Materials, Subcontract, **Equipment** and Other (Sage Construction Management integration) — [Intacct: Integration tips for Sage Construction Management](https://www-p02.intacct.com/ia/docs/en_US/help_action/Construction/Sage_Construction_Management/Tips-integrate-SageConstrMgt.htm)

### Inferences
- Alpnex has fleet, daily activity, fuel and pre-accounting modules. The single biggest structural gap is likely an **internal equipment rate table** that turns daily-activity hours into a job/project cost. The documented Vista pattern is a set of rules:
  - category × time unit, with an equipment-level override and a project-level override;
  - a standby/idle rate;
  - a posting that credits the equipment's cost center and charges the project's cost center.

  Procore's "Hours Operating / Hours Idle" split supports separate working and idle rates.
- Fuel and parts postings that also update the hour meter (configurable), and a meter-reading history that records the date even when the reading is unchanged, are cheap but valuable features.
- Equipment "attachments that move with the parent", component tracking, and equipment location transfers between sites and yards are documented in Vista. They map naturally onto Alpnex's branches and depots.
- A monthly overhead allocation to equipment (insurance, depreciation) is needed if cost per hour is to be realistic. For a "light accounting" product this could be an optional batch job.

### Gaps
- Spectrum's and CMiC's exact rate-table structure was not found in primary documentation.
- The Sage 300 CRE Equipment Cost (EQ) module configuration was not verified with official Sage documentation.
- The phase/cost-code hierarchy (phase → cost type) in Vista JC was not retrieved in this session (unverified).
- InEight, RIB iTWO and Jonas were not researched.

---

## 3. Approval workflows, notifications, audit trail, attachments

### Takeaway
- Sage Intacct documents configurable purchasing approvals: which transaction types (quotes, orders, invoices), who approves (including Project Manager and Warehouse approver types), the sequence, and amount-based levels.
- CMiC models approval rules, levels, groups and **unapproval** as setup tables.
- Procore shows a notification design worth copying:
  - status-change and comment emails to the assignee, creator and distribution list;
  - an explicit "send" after creation;
  - overdue emails only to the assignee, and only in actionable statuses;
  - granular permissions that control who may assign whom.

### Cited Findings
- **Sage Intacct purchasing approvals:**
  - The organization decides which purchasing transactions need approval, who approves, the order of approval, and whether approvals are amount-based.
  - The approvable types are Quotes, Orders and Invoices.
  - The approver user types include Project Manager, Payment Approver and Warehouse.

  [Intacct: About purchasing approvals](https://www-p02.intacct.com/ia/docs/en_US/help_action/Purchasing/Setting_up_Purchasing/Approvals/about-purchasing-approvals.htm)
- **Value-based vs progressive approval** (example thresholds 5,000 / 25,000 are illustrative):
  - In value-based approval, the request goes straight to the level that matches the amount.
  - In progressive approval, it passes through every level up to the threshold.

  [Greytrix blog (secondary)](https://www.greytrix.com/blogs/sageintacct/wp-json/wp/v2/posts/8745)
- If Sage Construction Management's approval workflow is used, the approval workflow in Sage Intacct Construction must be disabled. This means there is a single owner of approvals between the field and the ERP — [Intacct: Integration tips](https://www-p02.intacct.com/ia/docs/en_US/help_action/Construction/Sage_Construction_Management/Tips-integrate-SageConstrMgt.htm)
- The **CMiC requisition approval** setup includes Rules of Approver Setup, Approval Levels, Approvers, Approval Groups, Group Approvers Maintenance and Approval Group Members, plus a **Requisition Unapproval** function and a **Requisition Approver Query** — [CMiC RQ v10x PDF outline](https://docs.cmicglobal.com/portal/Content/Resources/PDFs/v10x/RQv10_x_ADF.pdf)
- **Procore Observations:**
  - Statuses: Initiated → Ready for Review → Closed / Not Accepted.
  - Status-change and comment emails go to the assignee, creator and distribution list.
  - Notifications are **not** sent automatically on creation; the user must send them.
  - Overdue emails go only to the assignee, and only while the status is Initiated or Not Accepted.
  - The granular permission "Can Assign Standard Users to Observations" controls assignment.

  [Procore FAQ: who gets notifications from observations](https://support.procore.com/faq/who-gets-notifications-from-observations); [Procore customer SOP mirror](https://support.procore.com/tc/KIER_Construction/Procore_Process_Database/KIER___Procore%3A_Observations)
- **Procore Change Events:**
  - Configure Settings has "Budget ROM" options for in-scope, out-of-scope and TBD changes. The basis can be Latest Cost, Latest Price or None.
  - Creating a budget change automatically creates a linked change event.

  [Procore: About change events](https://v2.support.procore.com/es-es/process-guides/about-change-events/)
- The **Procore Equipment** registry has a **recycle bin** for recovering deleted records — [Procore: About the Equipment Tool](https://support.procore.com/products/online/user-guide/company-level/equipment/tutorials/about-the-equipment-tool)
- Daily Log photos and files can be attached to the Equipment, Deliveries, Inspections, Accidents and Manpower sections (Android) — [Procore: Attach Files to a Daily Log (Android)](https://v2.support.procore.com/es-es/product-manuals/daily-log-android/tutorials/attach-files-to-a-daily-log-android/)
- **Vista Workflow module** checklists and processing logs are viewed with the WF Checklist Template form. Without the module, the WF Templates report can be used — [Trimble Vista: Equipment Management](https://help.trimble.com/doc/vista/vista/job-resources/equipment-management)

### Inferences
- Alpnex already has a purchase-request approval chain. The documented extras worth considering are:
  - amount-based levels, with a choice between value-based (jump to a level) and progressive (pass through each level) modes;
  - approver *groups* (any member can approve);
  - an explicit **unapprove** action with an audit entry;
  - an "Approvals waiting on me / approver query" screen;
  - approval of invoices, not only of requests and POs.
- Notification rules in Procore's style are a low-cost improvement: notify on status change or comment to the creator, assignee and distribution list, and send overdue reminders only for actionable statuses.

### Gaps
- Delegation and out-of-office substitution of approvers were not found for any product.
- Mobile approval UX was not verified in primary documentation for Vista, CMiC or Sage.
- Audit-trail granularity (field-level history) was not verified for any ERP in this session.

---

## 4. Field capture: daily log, timesheets, equipment timecards, offline mobile, photos

### Takeaway
- **Procore** has a field Daily Log with an equipment section (operating vs idle hours, cost code, inspection), photo attachments per section, and mobile apps that cache data and sync offline work after reconnection. It also offers equipment QR-code check-in (beta) and equipment timesheets that do not require a linked person.
- **CMiC** has a Daily Journal with a posted/unposted status and a limited back-dating window.
- **Vista** relies on Payroll timecards or EM usage posting. Trimble's separate Field Management app feeds PO receipts.

### Cited Findings
- **Procore Daily Log, Equipment section:** Hours Operating, Hours Idle, cost code, location, inspection checkbox and time, and comments. On mobile, entries are created from the Equipment section or from quick-create — [Procore: Add Equipment Entries (iOS)](https://v2.support.procore.com/product-manuals/daily-log-ios/tutorials/add-equipment-entries-to-a-daily-log-ios/); [Procore web: create equipment entries](https://support.procore.com/products/online/user-guide/project-level/daily-log/tutorials/create-equipment-entries)
- **Procore offline:**
  - Tasks performed offline sync once the connection is re-established.
  - On Android, an item can be viewed or edited offline only if it was previously viewed and cached on the device.
  - The offline actions listed differ between iOS and Android.

  [Procore: Daily Log user permissions (Android)](https://support.procore.com/procore-mobile-android/user-guide/daily-log-android/permissions). A trade press claim of "instant sync" is broader than the official documentation — [ForConstructionPros](https://www.forconstructionpros.com/blogs/construction-toolbox/blog/12018576/forconstructionproscom-app-of-the-week-procore-construction-management-software)
- **Procore Equipment tool** ([Procore: About the Equipment Tool](https://support.procore.com/products/online/user-guide/company-level/equipment/tutorials/about-the-equipment-tool)):
  - a company registry of owned or rented equipment, with rented equipment linked to a vendor from the Directory;
  - custom statuses (maintenance, rental, check-in/out, call-off), editable inline;
  - bulk assignment to projects and an "Onsite" flag;
  - **maintenance logs** (added August 2025);
  - **QR codes (beta)** to identify equipment or check it into a project;
  - check-in from the iOS and Android apps;
  - equipment inspections via the Inspections tool;
  - a recycle bin.
- Equipment timesheets can be created without linking equipment to a person; this was initially web-only — [Procore Equipment release notes](https://support.procore.com/products/online/user-guide/project-level/equipment/release-notes)
- **CMiC Daily Journal:**
  - It records the day's job-site activities.
  - Swinerton's internal guide says the date can be back-dated or forward-dated by up to 7 days.
  - The status is EMPTY, UNPOSTED or POSTED depending on whether units are posted.

  [CMiC: Daily Journal](https://docs.cmicglobal.com/portal/Content/E_Reference_Material/CMiC_Field/Reference/Site_Management/Daily_Journal.htm); [Swinerton support (customer guide, secondary)](https://support.swinerton.com/support/solutions/articles/1000052774)
- The CMiC app is marketed as working "with or without internet" for daily journals (weather, deliveries, safety, labor, equipment). Which features work offline is **unverified** — [ForConstructionPros: CMiC app](https://www.forconstructionpros.com/construction-technology/apps/blog/20990556/cmic-app-streamline-collaboration-project-management-with-or-without-internet)

### Inferences
- The following Alpnex additions are well-precedented:
  - equipment QR codes, scanned to open the equipment card or to check equipment in or out of a site;
  - an idle vs operating hours split in daily activity;
  - a daily-log "post" status that locks the entries;
  - a configurable back-dating window, for example ±7 days, to stop late edits of field data.
- Procore's "only previously cached items are available offline" limitation is a useful benchmark. Alpnex's offline desktop can be positioned as stronger, but its documentation should state clearly what is and is not available offline.

### Gaps
- Offline behavior of Vista, Spectrum or Foundation mobile apps was not verified.
- Photo GPS and timestamp stamping was not verified for any product.

---

## 5. Permissions model, multi-company

### Takeaway
Procore uses per-tool general levels (None / Read Only / Standard / Admin) assigned through **company and project permission templates**. On top of these it layers **granular permissions** for specific actions, plus a small set of **role-based privileges** set on the directory record, such as Accounting Approver for ERP integration. A Permissions Matrix documents what each task requires.

### Cited Findings
- Permissions exist at the company-tool and project-tool level and are granted through company and project permission templates. The tool levels are ([Procore FAQ: permissions](https://v2.support.procore.com/en-ca/faq-what-are-permissions-in-procore-and-how-do-they-work)):
  - **None:** the tool is hidden.
  - **Read Only.**
  - **Standard:** view and interact, but no configuration.
  - **Admin.**
- Granular permissions add specific capabilities to Read Only or Standard users. Role-based privileges, for example "Accounting Approver" for ERP integration, are set on the Company Directory record and not in templates. A Permissions Matrix lists what each task requires — [same source](https://v2.support.procore.com/en-ca/faq-what-are-permissions-in-procore-and-how-do-they-work)
- Editing templates needs Admin on the Directory tool, or the granular permission "Manage Permission Templates (Assignable Only)". Global templates apply across multiple projects — [FollowUpCRM blog (secondary)](https://www.followupcrm.com/blog/how-to-change-project-permissions-in-procore)
- Vista IN has company-level overrides per location (IN Location Company Overrides), which indicates a multi-company inventory configuration — [Trimble Vista: Inventory Reports](https://help.trimble.com/en/vista/vista/reports-catalog/inventory-reports)

### Inferences
- Alpnex already has field-level permissions, which is finer-grained than Procore's documented model. Two things are worth adopting:
  - **project/site-scoped templates**: the same user with a different template per site or depot;
  - a published **permissions matrix**, which can be generated automatically from code, showing which permission each action needs.

### Gaps
- The multi-company / intercompany models of Vista, CMiC and Intacct were not retrieved (unverified).

---

## 6. Reporting: dashboards, KPIs, scheduled emails, export

### Takeaway
Report catalogs consistently include several construction-specific reports that are relevant to Alpnex. Dashboard and scheduled-email details were not verified in primary sources.
- **Equipment:** utilization, unit cost, revenue, fuel and oil, fuel utilization, warranty expiration (Spectrum).
- **Inventory:** value at month end, monthly reconciliation, usage vs production, allocated inventory, negative price/cost exceptions, restocking (Vista IN).

### Cited Findings
- Spectrum Equipment Control reports: Equipment Revenue, Equipment Unit Cost, Equipment Utilization, Fuel & Oil, Fuel Utilization, E/C G/L Distribution, Warranty Expiration — [Trimble Spectrum reports overview](https://help.trimble.com/es-mx/spectrum/spectrum/equipment/equipment-control/spectrum-menus/reports-overview)
- Vista IN report catalog (30 reports; see section 1) — [Trimble Vista: Inventory Reports](https://help.trimble.com/en/vista/vista/reports-catalog/inventory-reports)
- A Vista PO Receipts Detail report exists — [Trimble Vista: PO Receipts Detail](https://help.trimble.com/doc/vista/vista/reports-catalog/purchase-order-reports/po-receipts-detail)
- Foundation's job costing page refers to cash flow, overhead allocation and over/under-billing (WIP) reports (marketing) — [Foundation: Construction Job Costing](https://foundationsoft.com/?p=1555)
- A Foundation user review says custom reports are "very limited" (secondary) — [Capterra MX: Foundation](https://www.capterra.mx/software/2032/foundation)
- Procore budget views show Committed Costs vs Pending Cost Changes, driven by PO status — [Procore FAQ: commitment statuses](https://support.procore.com/faq/what-are-the-default-commitment-statuses-in-procore)

### Inferences
- Candidate report additions for Alpnex:
  - equipment utilization: working, idle and available hours per period;
  - fuel per hour or per km by equipment, with anomaly flags;
  - equipment unit cost, i.e. total cost ÷ hours;
  - stock value at month end per depot;
  - "usage vs production";
  - a negative-cost/price exception report;
  - allocated (reserved) vs free stock;
  - warranty and insurance expiry lists.

### Gaps
- Scheduled report emails, dashboards and cost-to-complete KPIs were not verified in primary documentation for any product in this session.

---

## 7. Data and integration: APIs, imports, telematics

### Takeaway
Only limited primary evidence was collected. Patterns observed:
- CSV import of equipment hours from third-party systems into Sage 300 CRE EQ (forum);
- Procore ERP-integration syncing of billed-rate job cost transactions from Sage 300 CRE;
- Procore API fields for change events (third-party mirror);
- Procore's warning that enabling its new Equipment tool can disable existing third-party integrations that relied on the old Admin equipment list.

### Cited Findings
- Procore can import billed-rate job cost transactions from Sage 300 CRE, using the "Billed Amount" value — [Procore: Enable ERP Billed Rate Transaction Syncing for Sage 300 CRE](https://v2.support.procore.com/en-au/product-manuals/sage-300-cre/tutorials/enable-erp-billed-rate-transaction-syncing-for-sage-300-cre/)
- Equipment usage hours are imported by CSV into Sage 300 CRE EQ (user report) — [Sage City forum](https://www.sagecity.com/us/sage_construction_and_real_estate/f/sage-300-construction-and-real-estate/tags/processing/Sage%2b300%2bCRE/Job%2bCost)
- Enabling the Procore Equipment tool on a project can disable existing third-party integrations that depend on equipment listed in the project Admin tool — [Procore: About the Equipment Tool (search snippet)](https://support.procore.com/products/online/user-guide/company-level/equipment/tutorials/about-the-equipment-tool)
- Clue (a telematics and equipment platform) markets an integration with Vista — [Clue: Vista integration (vendor page)](https://getclue.com/integrations/clue-vista)
- Procore change event API fields (estimated budget impact, calculation strategy, transfer-from WBS code) come from a third-party mirror and are **unverified** — [withone.ai mirror](https://www.withone.ai/knowledge/procore/conn_mod_def%3A%3AGLdXnxSMBhA%3A%3AcqMNwjABSi6D8pkK5I7t3w/md)

### Inferences
- For Alpnex, three features would match the market's practice of importing equipment hours from telematics and CSV:
  - a **CSV/Excel import of equipment hours and fuel** with validation and a preview;
  - an import of telematics hour-meter readings;
  - meter-reading history.

### Gaps
- Official API documentation (Vista/Trimble App Xchange, CMiC API, Intacct Web Services, Procore REST) was not fetched in this session (unverified).
- Telematics standards such as AEMP/ISO 15143-3 support per product were not verified.
- **InEight, RIB iTWO, Jonas Construction** and **Dynamics 365 Business Central** construction add-ons were not researched within the tool budget.
