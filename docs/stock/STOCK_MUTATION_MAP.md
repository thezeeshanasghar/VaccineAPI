# STOCK MUTATION MAP (legacy code forensics)

Scope: every place in `Controllers/`, `Services/`, `helper/`, `Models/`, `Program.cs` (bin/obj/VaccineAPI.Tests/Migrations ignored; Tests only referenced when checking for callers) where inventory state (Stock rows, BrandAmount rows, InventoryTransaction ledger, Schedule stock-snapshot fields) can change or be recreated.
Method: grep + reading each site. All line numbers are from the worktree `VaccineAPI-stockrefactor`, branch `stock-integrity-refactor`, as read on 2026-09-30. Nothing here relies on memory notes.

Classification key
- THROUGH_SERVICE: the mutation is performed by `InventoryTransactionService` (rows for service-internal sites are marked "(service)": they ARE the service).
- BYPASS_DIRECT: a controller/helper/DB cascade mutates inventory state itself (or writes ledger rows / schedule stock fields itself).
- READ_ONLY: reads only.
- DEAD_CODE: no callers (grep-verified) or commented out.

Global facts verified by grep (these frame everything below)
- There is NO `_db.Stocks.Remove/RemoveRange` anywhere in C#. Stock rows are only ever created (7 `Stocks.Add` sites: 6 in service, 1 in BillController:496) or removed indirectly by FK cascade (Bill/Brand/Clinic/Doctor/User deletes) or nulled/orphaned.
- Only ONE `ExecuteSql*` exists: `InventoryBackfillController.cs:36` `DELETE FROM inventorytransactions`. No ExecuteUpdate/ExecuteDelete/FromSql anywhere.
- `InventoryTransaction` has NO foreign keys (Models/InventoryTransaction.cs has no navigation/ForeignKey), so no cascade ever touches the ledger; deleting Stock/Bill/Clinic/Brand leaves dangling ledger rows.
- `Stock.ClinicId` is written in exactly one place: `PostOpeningBalance` (Services/InventoryTransactionService.cs:299). Purchase (117-126), AdjustIncrease (246-255), TransferIn (429-438), recreated rows (506-515, 587-596) and SplitConsumed (BillController.cs:486-495) never set it, despite the model comment (Models/Stock.cs "Set on every insert (Purchase/TransferIn/AdjustIncrease/OpeningBalance)"). All FEFO queries therefore rely on the `s.ClinicId == null && s.Bill.ClinicId == clinicId` fallback.
- `Stock.BatchLot/Expiry/StockAmount/BrandId/BillId` are only ever set at row creation; never updated afterwards.
- `Clinic.StockPeriodStart` is never written in C# (grep `StockPeriodStart\s*=` finds none); it is SQL-managed.
- `Context.BumpConcurrencyTokens` (Models/Context.cs:22-31) increments `Stock.RowVersion` / `BrandAmount.RowVersion` on every Modified entry inside `SaveChanges`/`SaveChangesAsync` (overrides at 33-43).
- Inventory-affecting guards: `StockActionGuard.CheckStockAction` is applied to Bill Create/Update/AddPayment/Reverse, AdjustStock Create/Delete, DirectSale Create/Delete, StockTransfer Create/Delete. A caller with neither paId nor managerId (i.e. "doctor") is accepted with NO identity verification (helper/StockActionGuard.cs `if (!paId.HasValue && !managerId.HasValue) return (true, null)`). NOT guarded: Bill SplitConsumed, Stock opening-balance / reconcile, all InventoryBackfill endpoints, ClinicController delete, BrandController create/delete, BrandAmountController, CorrectBatch (identity only via DTO).

---------------------------------------------------------------------------

## 1. Mutation register

### 1a. Inside Services/InventoryTransactionService.cs (the intended single writer)

| ID | file:line | method | mutates | class | reached by |
|---|---|---|---|---|---|
| M-001 | Services/InventoryTransactionService.cs:32-53 (`_db.InventoryTransactions.Add(new InventoryTransaction` at :37) | `Log` (private) | INSERT ledger row (the only ledger writer in the service; append-only) | THROUGH_SERVICE (service) | every service method below |
| M-002 | :95-138 (`existingStock.Quantity += quantity` :105, `OriginalQuantity += quantity` :106, `IsClosed = false` :112, `_db.Stocks.Add(stock)` :127, `ba.Quantity += quantity` :132, Log :134) | `PostPurchaseLine` | Stock qty+OriginalQuantity, IsClosed reopen, Stock row create (no ClinicId), BrandAmount.Quantity, ledger Purchase | THROUGH_SERVICE (service) | POST /api/Bill (BillController.Create:206); PUT /api/Bill/{id} (Update:303). Merge branch does not update StockAmount (price edit lost) and adds to OriginalQuantity that ReverseBillLine never reduced (see 7) |
| M-003 | :141-155 (`ba.Quantity = Math.Max(0, ba.Quantity - stock.Quantity)` :144, `stock.Quantity = 0` :152, `stock.IsClosed = true` :153) | `ReverseBillLine` | BrandAmount.Quantity (floored), Stock qty->0, IsClosed, ledger BillEdit(-stock.Quantity). Does NOT touch OriginalQuantity | THROUGH_SERVICE (service) | PUT /api/Bill/{id} (Update:265) |
| M-004 | :162-170 | `LogSplitConsumed` | ledger only: SplitConsumed -consumed on old stock, +consumed on new stock; no qty change | THROUGH_SERVICE (service) | POST /api/Bill/{billId}/line/{stockId}/split-consumed (BillController:502) |
| M-005 | :177-183 | `LogBatchCorrection` | ledger only, QuantityDelta 0, ConsumesStock=false | THROUGH_SERVICE (service) | PATCH /api/Schedule/{id}/correct-batch (ScheduleController:5139) |
| M-006 | :186-200 (`ba.Quantity` :189, `stock.Quantity = 0` :197, `IsClosed = true` :198) | `ReverseBillStock` | same as M-003 with ledger BillReverse | THROUGH_SERVICE (service) | DELETE /api/Bill/{id}/reverse (BillController:567) |
| M-007 | :205-266 (`existingStock.Quantity += quantity` :231, `OriginalQuantity += quantity` :232, `IsClosed = false` :233, `_db.Stocks.Add(newStock)` :256, `ba.Quantity += quantity` :262) | `AdjustIncrease` | Stock qty/Original/IsClosed OR new Stock row anchored to latest non-XFER Bill; if no existing row AND no anchor bill => NO stock row but BrandAmount.Quantity still += (stockId null in ledger) ; ledger AdjustIncrease | THROUGH_SERVICE (service) | POST /api/AdjustStock Type=Increase (AdjustStockController:135) |
| M-008 | :273-316 (`existingStock.Quantity += quantity` :290, `OriginalQuantity` :291, `_db.Stocks.Add(newStock)` :306 with `ClinicId = clinicId` :299, `ba.Quantity += quantity` :311, `ba.NeedsReconcile = false` :312) | `PostOpeningBalance` | Stock qty/Original or new row (ONLY writer of Stock.ClinicId), BrandAmount qty + NeedsReconcile=false, ledger OpeningBalance | THROUGH_SERVICE (service) | POST /api/Stock/opening-balance (StockController:68) |
| M-009 | :319-344 (`stockRow.Quantity -= quantity` :338, `ba.Quantity = Math.Max(0, ...)` :339) | `AdjustLoss` | Stock qty (no IsClosed when it reaches 0), BrandAmount qty (floored), ledger AdjustLoss. Batch matched by BatchLot only (Expiry ignored) | THROUGH_SERVICE (service) | POST /api/AdjustStock Type=Loss (AdjustStockController:134) |
| M-010 | :347-399 (`ba.Quantity` :353/:354, `stockRow.Quantity -= restoreQty` :372, `OriginalQuantity -= restoreQty` :373, `stockRow.Quantity += Math.Abs(adjustment)` :390) | `ReverseAdjustment` | BrandAmount qty, Stock qty/Original (Increase undo) or qty (Loss undo, no reopen of IsClosed), ledger AdjustReverse (StockId may be null) | THROUGH_SERVICE (service) | DELETE /api/AdjustStock/{id} (AdjustStockController:212) |
| M-011 | :404-423 (`sourceStock.Quantity -= quantity` :407, `sourceBa.Quantity` :408, `IsClosed = true` :418) | `TransferOut` | source Stock qty, IsClosed at 0, source BrandAmount qty, ledger TransferOut | THROUGH_SERVICE (service) | POST /api/StockTransfer (StockTransferController:150) |
| M-012 | :425-468 (`_db.Stocks.Add(destStock)` :439, `_db.BrandAmounts.Add(destBa)` :459, `destBa.Quantity += quantity` :462) | `TransferIn` | dest Stock row create (BillId = XFER bill, no ClinicId), dest BrandAmount row create if missing (Quantity 0, SalePrice from source), dest BrandAmount qty, ledger TransferIn | THROUGH_SERVICE (service) | POST /api/StockTransfer (StockTransferController:151) |
| M-013 | :478-524 (`sourceBa.Quantity += quantity` :482, `sourceStock.Quantity += quantity` :493, `_db.Stocks.Add(recreated)` :516 with `OriginalQuantity = 0`) | `ReverseTransferOut` | source BrandAmount qty, source Stock qty (does NOT reopen IsClosed, matches by BatchLot only) or recreate Stock anchored to latest non-XFER bill, ledger TransferReverse | THROUGH_SERVICE (service) | DELETE /api/StockTransfer/{id} (StockTransferController:252) |
| M-014 | :526-534 (`destBa.Quantity = Math.Max(0, ...)` :530) | `ReverseTransferIn` | dest BrandAmount qty only; ledger TransferReverse with StockId = null (so dest Stock ledger sum is never reduced) | THROUGH_SERVICE (service) | DELETE /api/StockTransfer/{id} (StockTransferController:253) |
| M-015 | :537-556 (`sourceStock.Quantity -= quantity` :540, `sourceBa.Quantity` :541, `IsClosed = true` :551) | `SellDirect` | Stock qty/IsClosed, BrandAmount qty, ledger DirectSale | THROUGH_SERVICE (service) | POST /api/DirectSale (DirectSaleController:144) |
| M-016 | :559-605 (`sourceBa.Quantity += quantity` :563, `sourceStock.Quantity += quantity` :574, `_db.Stocks.Add(recreated)` :597, `OriginalQuantity = 0`) | `ReverseDirectSale` | BrandAmount qty, Stock qty (no IsClosed reopen: a closed batch restored to qty>0 stays invisible to FEFO `!s.IsClosed`), or recreated Stock row, ledger DirectSaleReverse | THROUGH_SERVICE (service) | DELETE /api/DirectSale/{id} (DirectSaleController:408) |
| M-017 | :610-646 | `Administer` (async) | BrandAmount qty, Stock qty/IsClosed, ledger. **No callers** (grep `\.Administer\(` finds none in Controllers/helper/Services) | DEAD_CODE | none |
| M-018 | :649-674 | `Unadminister` (async) | BrandAmount qty, Stock qty/IsClosed, ledger. **No callers** (grep `\.Unadminister\(` none) | DEAD_CODE | none |
| M-019 | :728-732 | `AdministerSync` 5-arg legacy overload | delegates to M-020. **No callers**; only the 8-arg overload is called (ScheduleController:890, :2743; Tests use 8-arg at StockWorld.cs:103) | DEAD_CODE | none |
| M-020 | :742-798 (`ba.Quantity -= 1` :759, `src.Quantity -= deduct` :768, `src.IsClosed = true` :777, `ba.Quantity = 0` :786, `ba.NeedsReconcile = true` :787) | `AdministerSync` 8-arg | FEFO deduct of 1 unit: Stock qty/IsClosed, BrandAmount qty (floored to 0), NeedsReconcile flag, ledger Administer rows (zero-delta row when non-consuming; StockId null row when unbatched). Returns consumed StockId (last batch only) | THROUGH_SERVICE (service) | PUT /api/Schedule/child-schedule (give), PUT /api/Schedule/update-bulk-injection (give) |
| M-021 | :806-864 (`ba.Quantity += 1` :832/:839, `giveRow.ReconciledByTransactionId = null` :833, `restoreStock.Quantity += 1` :850, `IsClosed = false` :851) | `UnadministerSync` | ungive: BrandAmount qty, Stock qty (+reopen), ledger Unadminister, clears ReconciledByTransactionId on the give row (UPDATE of an existing ledger row) | THROUGH_SERVICE (service) | PUT /api/Schedule/child-schedule (ungive, brand-change); DELETE /api/PAAssignment/{id}?mode=FullReset |
| M-022 | :869-917 (`ba.Quantity++` :889/:896, `ReconciledByTransactionId = null` :890, `restoreStock.Quantity++` :906, `IsClosed = false` :907) | `UnadministerBulkSync` | same as M-021 for bulk | THROUGH_SERVICE (service) | PUT /api/Schedule/update-bulk-injection (ungive, brand-change) |
| M-023 | :60-69 | `FefoFillCandidates` (private) | none: IQueryable of open, qty>0 stocks ordered by Expiry (nulls last) then Id | READ_ONLY | M-020, M-017 |
| M-024 | :75-78 | `HasFillableBatch` | none | READ_ONLY | ScheduleController:879, :2370 (unbatched-give prompt) |
| M-025 | :82-90 | `FefoRestoreCandidates` (private) | none: includes qty>=0 rows, closed or not | READ_ONLY | M-018, M-021, M-022 |
| M-026 | :704-724 | `ResolveGiveDecision` (static) | none (pure decision; uses `ClinicClock.TodayPkt()`) | READ_ONLY | ScheduleController:846, :995, :2366, :2694 |
| M-027 | :919-923 | `GetOrNoOpBrandAmount` (private) | none (returns null if BrandAmount missing => callers silently skip the counter) | READ_ONLY | M-002, M-003, M-006, M-010, M-013, M-014, M-016 |
| M-028 | Services/InventoryReconciliationService.cs:45-97 | `Verify` | none (class comment: "Never writes anything"). Note Verify compares against ALL-TIME ledger sums, unlike StockController.Reconcile which floors at StockPeriodStart | READ_ONLY | GET /api/InventoryBackfill/verify (:76); POST run (:47) |
| M-029 | Models/Context.cs:22-31 | `BumpConcurrencyTokens` | `Stock.RowVersion++`, `BrandAmount.RowVersion++` on every Modified entry (infra only, no quantity change) | BYPASS_DIRECT (benign infra; RowVersion only) | every SaveChanges |

### 1b. Controller / helper / cascade sites

| ID | file:line | method | mutates | class | reached by |
|---|---|---|---|---|---|
| M-030 | Controllers/BillController.cs:200 (`_db.Bills.Add(bill)`), :206 | `Create` | Bill row + Stock/BA/ledger via M-002 | THROUGH_SERVICE | POST /api/Bill (guarded: StockActionGuard StockPurchaseBills) |
| M-031 | BillController.cs:263-266 (ReverseBillLine loop), :278-291 (bill header fields incl. BillDate), :303 | `Update` | via M-003 then M-002; also mutates bill.BillDate/AwtPercent that feed ledger EventDate and payable | THROUGH_SERVICE | PUT /api/Bill/{id} (guarded) |
| M-032 | BillController.cs:483 (`Bills.Add(newBill)`), :486-495 new Stock (`Quantity = consumed`, `OriginalQuantity = consumed`), :496 `_db.Stocks.Add(newStock)`, :500 `stock.OriginalQuantity = stock.Quantity` | `SplitConsumed` | creates Bill + Stock row directly with live Quantity = consumed (units already consumed => phantom on-hand, FEFO-eligible, no BrandAmount change), rewrites OriginalQuantity of source row; ledger via M-004 only | BYPASS_DIRECT | POST /api/Bill/{billId}/line/{stockId}/split-consumed (NO guard) |
| M-033 | BillController.cs:567 | `Reverse` (stock part) | via M-006 | THROUGH_SERVICE | DELETE /api/Bill/{id}/reverse (guarded) |
| M-034 | BillController.cs:571 (`SupplierPayments.RemoveRange`), :573 (`_db.Bills.Remove(bill)`) | `Reverse` (row removal) | deletes Bill; `bill.Stocks` are loaded/tracked and Stock.BillId is `int?` (optional FK), so EF nulls BillId on tracked closed stocks (or DB cascade deletes them: snapshot in Migrations/ContextModelSnapshot.cs:973-979 says Cascade+Required, but the snapshot is stale InitialCreate 2025-03, prod schema unverified). Ledger rows keep dangling StockId/SourceId | BYPASS_DIRECT | DELETE /api/Bill/{id}/reverse |
| M-035 | Controllers/AdjustStockController.cs:130 (`AdjustStocks.Add`), :134, :135 | `Create` | AdjustStock audit row + M-007 / M-009 | THROUGH_SERVICE | POST /api/AdjustStock (guarded StockAdjust) |
| M-036 | AdjustStockController.cs:158 (`backlogRow.ReconciledByTransactionId = row.Id`), :162 (`_db.InventoryTransactions.Add(new InventoryTransaction` zero-delta AdjustIncrease row) | `Create` (ClearUnbatchedBacklog branch) | UPDATE of existing ledger rows + direct ledger INSERT outside the service | BYPASS_DIRECT | POST /api/AdjustStock Type=Increase with ClearUnbatchedBacklog=true |
| M-037 | AdjustStockController.cs:212, :214 (`AdjustStocks.Remove(row)`) | `Delete` | M-010 + audit row delete; does NOT clear `ReconciledByTransactionId` claimed by this adjustment (grep: no other reset besides M-021/M-022) | THROUGH_SERVICE | DELETE /api/AdjustStock/{id} (guarded) |
| M-038 | Controllers/StockTransferController.cs:102-117 (`Bills.Add` XFER bill), :130-142 (`StockTransfers.Add`), :150, :151 | `Create` | XFER Bill + StockTransfer rows + M-011 + M-012 | THROUGH_SERVICE | POST /api/StockTransfer (guarded StockTransfer) |
| M-039 | StockTransferController.cs:252, :253 | `Delete` (reversal) | M-013 + M-014 | THROUGH_SERVICE | DELETE /api/StockTransfer/{id} (guarded) |
| M-040 | StockTransferController.cs:264-269 (`destStock.Quantity = 0; destStock.IsClosed = true;` for all `Stocks.Where(s.BillId == billId)`) and :272 (`StockTransfers.RemoveRange`) | `Delete` | direct zeroing/closing of destination Stock rows with NO ledger row against those StockIds (ReverseTransferIn logs StockId null) => Stock-level drift; zeroes rows even if partially consumed at destination | BYPASS_DIRECT | DELETE /api/StockTransfer/{id} |
| M-041 | Controllers/DirectSaleController.cs:118-141 (`DirectSales.Add`), :144 | `Create` | DirectSale rows + M-015 | THROUGH_SERVICE | POST /api/DirectSale (guarded StockDirectSale) |
| M-042 | DirectSaleController.cs:408, :412 (`DirectSales.RemoveRange`) | `Delete` | M-016 + row delete | THROUGH_SERVICE | DELETE /api/DirectSale/{id} (guarded) |
| M-043 | Controllers/StockController.cs:68 | `PostOpeningBalance` | via M-008, loops lines; not idempotent (comment :45 "running it twice doubles stock") | THROUGH_SERVICE | POST /api/Stock/opening-balance (NO guard, no caller identity) |
| M-044 | StockController.cs:185 (`c.Quantity = flooredBal`), :189 (`c.NeedsReconcile = ...`) | `Reconcile` | BrandAmount.Quantity/NeedsReconcile overwritten from ledger sum (EventDate >= StockPeriodStart, floored at 0); Stock rows untouched; no ledger row written | BYPASS_DIRECT | POST /api/Stock/reconcile?clinicId=&brandId= (NO guard) |
| M-045 | StockController.cs:98-152 | `CheckIntegrity` | none | READ_ONLY | GET /api/Stock/integrity |
| M-046 | StockController.cs:204-226 | `GetBatchLots` | none: qty>0 rows for brand+clinic ordered by Expiry (NOTE: does not filter IsClosed, no Id tiebreak) | READ_ONLY | GET /api/Stock/batch-lots |
| M-047 | StockController.cs:228-1500 (sales-report 229, sales-summary 402, sales-report-log 433, sales-collection-report 467, items-report 778, stock-position-report 1215/1238, items-purchase-report 1343, items-supplier-report 1476) | report endpoints | none | READ_ONLY | GET /api/Stock/* |
| M-048 | Controllers/StockOverviewController.cs:74, :90, :164 (grep of write patterns finds none) | overview | none (reads `Σ Stock.Quantity` and `BrandAmount.SalePrice`) | READ_ONLY | GET /api/StockOverview/* |
| M-049 | Controllers/InventoryBackfillController.cs:36 (`_db.Database.ExecuteSqlRawAsync("DELETE FROM inventorytransactions")`) | `Run` | raw SQL wipe of the ENTIRE ledger, then rebuild | BYPASS_DIRECT | POST /api/InventoryBackfill/run (NO guard) |
| M-050 | InventoryBackfillController.cs:149 | `BackfillPurchases` | direct ledger INSERT (Purchase/TransferIn) per Stock row | BYPASS_DIRECT | POST /api/InventoryBackfill/run |
| M-051 | InventoryBackfillController.cs:189 | `BackfillAdjustments` | direct ledger INSERT (StockId null) | BYPASS_DIRECT | POST run |
| M-052 | InventoryBackfillController.cs:219 | `BackfillTransfers` | direct ledger INSERT TransferOut | BYPASS_DIRECT | POST run |
| M-053 | InventoryBackfillController.cs:245 | `BackfillDirectSales` | direct ledger INSERT DirectSale | BYPASS_DIRECT | POST run |
| M-054 | InventoryBackfillController.cs:317 | `BackfillAdministrations` | direct ledger INSERT Administer (StockId null) from Schedule rows | BYPASS_DIRECT | POST run |
| M-055 | InventoryBackfillController.cs:106 (`ba.Quantity = flooredCount`), :107 (`ba.NeedsReconcile`) | `CorrectDrift` | BrandAmount qty/NeedsReconcile from ALL-TIME ledger sum (differs from M-044 which floors at StockPeriodStart) | BYPASS_DIRECT | POST /api/InventoryBackfill/correct-drift (NO guard) |
| M-056 | InventoryBackfillController.cs:76-81 | `Verify` | none | READ_ONLY | GET /api/InventoryBackfill/verify |
| M-057 | Controllers/ClinicController.cs:73-81 (`_db.BrandAmounts.Add(ba)`, Quantity 0, SalePrice 0) | `Add` | creates one BrandAmount row per existing Brand for the new clinic | BYPASS_DIRECT | POST /api/Clinic |
| M-058 | ClinicController.cs:162 (`Bills.RemoveRange`), :165 (`AdjustStocks.RemoveRange`), :168 (`BrandAmounts.RemoveRange`), :175 (`Clinics.Remove`) | `Delete` | removes Bills (their Stocks by FK cascade/orphan), AdjustStocks, BrandAmounts of the clinic without any ledger reversal; Stock rows anchored by `Stock.ClinicId` (opening-balance rows) are not explicitly removed. Ledger rows remain | BYPASS_DIRECT | DELETE /api/Clinic/{id} (NO guard) |
| M-059 | ClinicController.cs:64, :113 (`dbClinic.MaintainInventory = ...`); DoctorController.cs:186, :315 (`AllowInventory = ...`) | `Add`/`Put`; Doctor create/update | config gates: when false, gives/ungives skip all inventory (`IsInventoryEnabledForActor`, ScheduleController:1712; `IsInventoryEnabledForDoctor`, PAAssignmentController:329). Toggling mid-life causes give/ungive asymmetry (drift) | BYPASS_DIRECT (flag only) | POST/PUT /api/Clinic, POST/PUT /api/Doctor |
| M-060 | ClinicController.cs:190-193 (`child.ClinicId = toClinicId`) | `TransferPatients` | moves patients between clinics; `Child.ClinicId` is the fallback clinic in `ResolveClinicIdForStock/Ungive`, so this silently changes which clinic's stock future gives/ungives hit | BYPASS_DIRECT (indirect) | POST /api/Clinic/{fromClinicId}/transfer/{toClinicId} |
| M-061 | Controllers/BrandController.cs:93 (`_db.BrandAmounts.AddRange(brandAmounts)`) | `Post` | creates BrandAmount (Qty 0) for every doctor x clinic | BYPASS_DIRECT | POST /api/Brand |
| M-062 | BrandController.cs:164 (`BrandAmounts.RemoveRange`), :165 (`Brands.Remove`) | `Delete` | deletes all BrandAmounts of the brand (live counters lost) and the Brand; Stocks of that brand removed by FK cascade (Stock.BrandId required); ledger rows dangle | BYPASS_DIRECT | DELETE /api/Brand/{id} (NO guard) |
| M-063 | Controllers/BrandAmountController.cs:45 (`_db.BrandAmounts.AddRange(toAdd)`) | `GetByDoctorClinic` | a GET that INSERTS BrandAmount rows (Qty 0) for any brand missing at doctor+clinic | BYPASS_DIRECT | GET /api/BrandAmount?doctorId=&clinicId= |
| M-064 | BrandAmountController.cs:85 (`ba.SalePrice = dto.SalePrice`) | `UpdateAmounts` | BrandAmount.SalePrice (price only); PA blocked only by self-declared `PaId` (:77-80) | BYPASS_DIRECT | PUT /api/BrandAmount |
| M-065 | Controllers/DoctorScheduleController.cs:122-130 (`new BrandAmount{... Quantity=0 ...}`, `_db.BrandAmounts.Add(brandAmount)`) | `Post` | inserts a BrandAmount (first Brand in table, online clinic) per posted DoctorSchedule with NO existence check => duplicate BrandAmount rows for same (Brand, Doctor, Clinic) | BYPASS_DIRECT | POST /api/DoctorSchedule |
| M-066 | Controllers/DoctorController.cs:215-220 (commented `ba.SalePrice/ba.Quantity/_db.BrandAmounts.Add(ba)`) | (commented block) | none | DEAD_CODE | none |
| M-067 | DoctorController.cs:461 (`Schedules.RemoveRange`), :465, :469 (`Clinics.RemoveRange`), :474 (`Doctors.Remove`) | `Delete` | deletes every child's Schedules (including given doses with Schedule.StockId) and all clinics; Bills/Stocks/BrandAmounts/AdjustStocks of those clinics go by FK cascade (unverified prod schema); no ledger reversal | BYPASS_DIRECT | DELETE /api/Doctor/{id} |
| M-068 | Controllers/UserController.cs:565 (`_db.Users.Remove(obj)`) | `Delete` | user delete; cascades to Doctor -> Clinic -> Bill -> Stock -> BrandAmount if the DB FKs are cascade (snapshot lines 842-848 show Doctor->User cascade); unverified | BYPASS_DIRECT (cascade) | DELETE /api/User/{id} |
| M-069 | Controllers/ChildController.cs:3886 (`_db.Schedules.RemoveRange(dbChild.Schedules)`), :3889 (`Childs.Remove`) | `Delete` | removes a patient's schedules including GIVEN doses; consumed stock is never restored, ledger Administer rows orphaned (only guard: PA same-day rule) | BYPASS_DIRECT | DELETE /api/Child/{id} |
| M-070 | Controllers/ScheduleController.cs:890 | `Update` single-give | via M-020 (`_inventory.AdministerSync`) inside its own `BeginTransaction`/`SaveChanges`/`Commit` block directly after the call (DbUpdateConcurrencyException handled) | THROUGH_SERVICE | PUT /api/Schedule/child-schedule |
| M-071 | ScheduleController.cs:720 | `Update` single-ungive | via M-021 (`UnadministerSync`) | THROUGH_SERVICE | PUT /api/Schedule/child-schedule (IsDone true->false) |
| M-072 | ScheduleController.cs:686-700 (`IsDone`, `GivenDate`, `BrandId = null` :697, `VaccineCost = null` :698) | `Update` single-ungive schedule reset | clears BrandId/VaccineCost but leaves Lot, Expiry, StockId, StockClinicId, Manufacturer, Route stale on the ungiven row | BYPASS_DIRECT | PUT /api/Schedule/child-schedule |
| M-073 | ScheduleController.cs:797 | `Update` true->true brand change | via M-021 then falls into a fresh give (M-070) | THROUGH_SERVICE | PUT /api/Schedule/child-schedule |
| M-074 | ScheduleController.cs:1003 (`dbSchedule.StockId = giveConsumedStockId`), :1004 (`ApplyStockSourceFields(... giveConsumedStockId, blankIfNoBatch)`) | `Update` HPV-branch early return | schedule.StockId/Lot/Expiry/VaccineCost/StockClinicId | BYPASS_DIRECT | PUT /api/Schedule/child-schedule (HPV dose 1, age>5475d) |
| M-075 | ScheduleController.cs:1200 (`ApplyStockSourceFields(dbSchedule, scheduleDTO, onlineStockClinicId)`) | `Update` general give path | stamps Lot/Expiry/VaccineCost/StockClinicId via the LEGACY overload (:1453 with consumedStockId=null -> :1490 -> :1527-1577), which picks a stock by operator-typed lot or `GetLatestStockByBrandAndClinic`, NOT the batch AdministerSync consumed. `giveConsumedStockId` is only ever assigned to `dbSchedule.StockId` at :1003 (HPV branch), so on the normal path Schedule.StockId is never set | BYPASS_DIRECT | PUT /api/Schedule/child-schedule |
| M-076 | ScheduleController.cs:1453-1490, :1493-1524, :1527-1577 (`Lot`, `Expiry`, `VaccineCost`, `StockClinicId`, `Manufacturer`, `Route`) | `ApplyStockSourceFields` (3 overloads) | Schedule stock-snapshot fields | BYPASS_DIRECT | callers M-074, M-075, M-080 |
| M-077 | ScheduleController.cs:2606 (`UnadministerBulkSync`, brand change) and :2667 (`UnadministerBulkSync`, ungive) | `UpdateBulkInjection` | via M-022. Uses `ResolveClinicIdForUngive(dbSchedule, ...)` (the primary schedule, :2577/:2628) rather than the per-item `schedule`, so clinic/lot resolution is by the wrong row. Ungive restores only when `scheduleDTO.ScheduleBrands.Count > 0` and a matching scheduleBrand exists (block starts :2540-2545), otherwise IsDone flips with no restore | THROUGH_SERVICE | PUT /api/Schedule/update-bulk-injection |
| M-078 | ScheduleController.cs:2743 (`AdministerSync`), :2747 (`schedule.StockId = bulkConsumedStockId`) | `UpdateBulkInjection` give | via M-020; StockId written directly | THROUGH_SERVICE | PUT /api/Schedule/update-bulk-injection |
| M-079 | ScheduleController.cs:2755 (`ApplyStockSourceFields(schedule, brandId, lot, expiry, clinic, bulkConsumedStockId, bulkBlankIfNoBatch)`) | `UpdateBulkInjection` | schedule Lot/Expiry/VaccineCost/StockClinicId | BYPASS_DIRECT | same |
| M-080 | ScheduleController.cs:5131 (`schedule.Lot = newLot`), :5132 (`Manufacturer`), :5133 (`schedule.Expiry = newExpiry`), :5139 (`LogBatchCorrection`) | `CorrectBatch` | edits certificate snapshot; StockId untouched; ledger zero-delta row via M-005 | BYPASS_DIRECT | PATCH /api/Schedule/{id}/correct-batch (no VerifyCaller; only `dto.CorrectByPaId` recorded) |
| M-081 | ScheduleController.cs:1219-1252 (`[HttpPatch("after-injection")]`) | `AfterInjection` | Weight/Height/Circle only | READ_ONLY (no inventory effect) | PATCH /api/Schedule/after-injection |
| M-082 | ScheduleController.cs:3738 (`_db.Schedules.RemoveRange(futureDoses)`), helper/InfiniteDoseCleanup.cs:46 | `Delete`, `RemoveExtraUndoneRows` | delete only `IsDone == false` rows => no inventory effect | READ_ONLY (no inventory effect) | DELETE /api/Schedule/{ChildId}/{DoseId}/{Date}; also called from ScheduleController and PAAssignmentController FullReset |
| M-083 | Controllers/PAAssignmentController.cs:252 (`_inventory.UnadministerSync`) | `DeleteAssignment` mode=FullReset | via M-021 for each given schedule of that PA | THROUGH_SERVICE | DELETE /api/PAAssignment/{id}?doctorId=&mode=FullReset (auth = `assignment.DoctorId != doctorId` only; no VerifyCaller) |
| M-084 | PAAssignmentController.cs:292-303 (`s.IsDone = false`, `GivenDate = null`, `BrandId = null`, `Amount = null`, ...) | `DeleteAssignment` schedule reset | schedule reset; Lot/Expiry/StockId/VaccineCost/StockClinicId are NOT cleared | BYPASS_DIRECT | same |
| M-085 | ScheduleController.cs:1283-1322 | `GetLatestStockByBrandAndClinic` | none. Ordering: unexpired-first by Expiry, then any dated, then newest bill; DOES NOT filter Quantity>0 or IsClosed | READ_ONLY | GetSingle (:165-170), ApplyStockSourceFields (:1569) |
| M-086 | ScheduleController.cs:1580-1623 | `ResolveClinicIdForStock` | none | READ_ONLY | :165, :427, :992, :1199, :1670, :2348, :2552, :2676, :5137 |
| M-087 | ScheduleController.cs:1626-1709 | `ResolveClinicIdForUngive` | none (reads Stock rows by brand+lot+expiry; when several clinics match, falls back to child clinic then actor clinic then first) | READ_ONLY | :563, :758, :2577, :2628 |
| M-088 | ScheduleController.cs:1712-1740 `IsInventoryEnabledForActor`, :1283ff `IsInventoryEnabledForClinic` | gates | none | READ_ONLY | give/ungive paths |
| M-089 | Controllers/ChildController.cs:5062, :5362 (`latestStockByBrand`), :123, :2936, :3319, :3381 (BrandAmounts lookups for price) | PDF/price helpers | none | READ_ONLY | GET card/invoice PDFs. (ChildController:3381 area also flips `Invoice.IsVoided`, not inventory.) |
| M-090 | Controllers/DashboardController.cs:278, :330, :416, :479; PnLController.cs:58, :142; PaCashHandoverController.cs | reports | none | READ_ONLY | GET dashboards/reports |
| M-091 | Controllers/ChildController.cs:4134-4137, :4200, :4230 (+ `InsertEpiHistoryDoses`) | registration | inserts Schedule rows with IsDone=true (EPI history) or false, BrandId null, no stock | READ_ONLY (no inventory effect) | POST /api/Child |
| M-092 | Controllers/DoseController.cs:232 (`Doses.Remove`), VaccineController.cs:137 (`Vaccines.Remove`), VaccineBrandController.cs:119 | deletes | Dose delete may cascade Schedules (snapshot 911-913, 922-924 Cascade) including given ones => stock never restored; Vaccine delete blocked while Doses exist | BYPASS_DIRECT (cascade, unverified prod FK) | DELETE /api/Dose/{id} |

---------------------------------------------------------------------------

## 2. (a) Public surface of the two services

### Services/InventoryTransactionService.cs (namespace VaccineAPI.Services; ctor `(Context db)`)

| public member | line | mutates | callers |
|---|---|---|---|
| `InventoryOperationResult` (Ok/Fail) | :7-16 | none (DTO) | AdjustIncrease, AdjustLoss, PostOpeningBalance, Administer |
| `PostPurchaseLine` | :95 | Stock qty/OriginalQuantity/IsClosed(reopen)/row create; BA qty; ledger Purchase; SaveChanges (mid-method) at :128 | BillController:206, :303 |
| `ReverseBillLine` | :141 | BA qty; Stock qty=0, IsClosed=true; ledger BillEdit | BillController:265 |
| `LogSplitConsumed` | :162 | ledger x2 (SplitConsumed) | BillController:502 |
| `LogBatchCorrection` | :177 | ledger x1 (delta 0) | ScheduleController:5139 |
| `ReverseBillStock` | :186 | BA qty; Stock qty=0, IsClosed=true; ledger BillReverse | BillController:567 |
| `AdjustIncrease` | :205 | Stock qty/Original/IsClosed or new Stock; BA qty; ledger AdjustIncrease; SaveChanges :257 | AdjustStockController:135 |
| `PostOpeningBalance` | :273 | Stock qty/Original or new Stock (+ClinicId); BA qty, NeedsReconcile=false; ledger OpeningBalance; SaveChanges :307 | StockController:68 |
| `AdjustLoss` | :319 | Stock qty; BA qty; ledger AdjustLoss | AdjustStockController:134 |
| `ReverseAdjustment` | :347 | BA qty; Stock qty/Original (Increase) or qty (Loss); ledger AdjustReverse | AdjustStockController:212 |
| `TransferOut` | :404 | Stock qty, IsClosed; BA qty; ledger TransferOut | StockTransferController:150 |
| `TransferIn` | :425 | new Stock; new BrandAmount if missing; BA qty; ledger TransferIn; SaveChanges :440, :460 | StockTransferController:151 |
| `ReverseTransferOut` | :478 | BA qty; Stock qty or recreated Stock; ledger TransferReverse; SaveChanges :517 | StockTransferController:252 |
| `ReverseTransferIn` | :526 | BA qty; ledger TransferReverse (StockId null) | StockTransferController:253 |
| `SellDirect` | :537 | Stock qty, IsClosed; BA qty; ledger DirectSale | DirectSaleController:144 |
| `ReverseDirectSale` | :559 | BA qty; Stock qty or recreated Stock; ledger DirectSaleReverse; SaveChanges :598 | DirectSaleController:408 |
| `Administer` (async) | :610 | BA qty, Stock qty/IsClosed, ledger | NONE (DEAD_CODE) |
| `Unadminister` (async) | :649 | BA qty, Stock qty/IsClosed, ledger | NONE (DEAD_CODE) |
| `GiveDecision` (nested class), `ResolveGiveDecision` (static) | :697, :704 | none | ScheduleController:846, :995, :2366, :2694 |
| `AdministerSync` (5-arg) | :728 | delegates | NONE (DEAD_CODE) |
| `AdministerSync` (8-arg, out stockId) | :742 | BA qty/NeedsReconcile; Stock qty/IsClosed; ledger Administer (incl. zero-delta and unbatched -1 rows) | ScheduleController:890, :2743 |
| `UnadministerSync` | :806 | BA qty; Stock qty/IsClosed(reopen); ledger Unadminister; updates existing ledger row's ReconciledByTransactionId | ScheduleController:720, :797; PAAssignmentController:252 |
| `UnadministerBulkSync` | :869 | same | ScheduleController:2606, :2667 |
| `HasFillableBatch` | :75 | none | ScheduleController:879, :2370 |

Private helpers: `Log` :32, `FefoFillCandidates` :60, `FefoRestoreCandidates` :82, `GetOrNoOpBrandAmount` :919. The service never calls `SaveChanges` at the end of a method; it calls it MID-method (lines 128, 257, 307, 440, 460, 517, 598) inside callers' open transactions to obtain new Ids.

### Services/InventoryReconciliationService.cs (ctor `(Context db)`)

| public member | line | mutates | callers |
|---|---|---|---|
| `Verify()` | :45 | none (reads all InventoryTransactions, Stocks, BrandAmounts; compares all-time) | InventoryBackfillController:47, :79, :90 |
| DTOs `StockDriftRow`, `BrandAmountDriftRow`, `ReconciliationReport` | :9-34 | none | same |

---------------------------------------------------------------------------

## 3. (b) Every controller endpoint that can change stock (or the inputs to stock)

Route base is `api/[controller]` for every controller listed.

| Endpoint | Controller.method (file:line) | Effect on inventory | Path | Auth |
|---|---|---|---|---|
| POST /api/Bill | BillController.Create (:137) | + Stock row, + BA qty, ledger Purchase | THROUGH_SERVICE (M-002) | StockActionGuard |
| PUT /api/Bill/{id} | BillController.Update (:225) | reverse all lines (Stock->0/closed, BA down) then re-post | THROUGH_SERVICE (M-003, M-002) | StockActionGuard |
| POST /api/Bill/{billId}/line/{stockId}/split-consumed | BillController.SplitConsumed (:438) | new Bill + new Stock(Quantity=consumed), OriginalQuantity rewrite, ledger x2 | BYPASS_DIRECT (M-032) | none |
| DELETE /api/Bill/{id}/reverse | BillController.Reverse (:526) | Stock->0/closed, BA down, Bill deleted | THROUGH_SERVICE + BYPASS (M-006, M-034) | StockActionGuard |
| POST /api/Bill/{id}/payment | BillController.AddPayment (:321) | none (payments only) | n/a | StockActionGuard |
| POST /api/AdjustStock | AdjustStockController.Create (:67) | Increase/Loss; optional direct ledger claim rows | THROUGH_SERVICE + BYPASS (M-007/M-009, M-036) | StockActionGuard |
| DELETE /api/AdjustStock/{id} | AdjustStockController.Delete (:192) | reverse adjustment | THROUGH_SERVICE (M-010) | StockActionGuard |
| POST /api/StockTransfer | StockTransferController.Create (:30) | TransferOut + TransferIn | THROUGH_SERVICE (M-011/M-012) | StockActionGuard |
| DELETE /api/StockTransfer/{id} | StockTransferController.Delete (Delete method after GetList, guard at :230) | ReverseTransferOut/In + direct dest Stock zero/close | THROUGH_SERVICE + BYPASS (M-013/M-014, M-040) | StockActionGuard |
| POST /api/DirectSale | DirectSaleController.Create (:31) | SellDirect | THROUGH_SERVICE (M-015) | StockActionGuard |
| DELETE /api/DirectSale/{id} | DirectSaleController.Delete (:375) | ReverseDirectSale | THROUGH_SERVICE (M-016) | StockActionGuard |
| POST /api/Stock/opening-balance | StockController.PostOpeningBalance (:47) | + Stock, + BA | THROUGH_SERVICE (M-008) | none |
| POST /api/Stock/reconcile | StockController.Reconcile (:155) | overwrite BA.Quantity/NeedsReconcile | BYPASS_DIRECT (M-044) | none |
| POST /api/InventoryBackfill/run | InventoryBackfillController.Run (:30) | wipes ledger via raw SQL, rebuilds it | BYPASS_DIRECT (M-049..M-054) | none |
| POST /api/InventoryBackfill/correct-drift | InventoryBackfillController.CorrectDrift (:88) | overwrite BA.Quantity/NeedsReconcile | BYPASS_DIRECT (M-055) | none |
| PUT /api/Schedule/child-schedule | ScheduleController.Update (:241; attribute :240) | give (FEFO deduct), ungive (restore), brand-change (restore + deduct), schedule stock fields | THROUGH_SERVICE (M-070/M-071/M-073) + BYPASS (M-072, M-074, M-075) | VerifyCaller + PA/Manager perms (ScheduleController:58-84) |
| PUT /api/Schedule/update-bulk-injection | ScheduleController.UpdateBulkInjection (:2101) | bulk give/ungive/brand-change | THROUGH_SERVICE (M-077/M-078) + BYPASS (M-079) | same |
| PATCH /api/Schedule/{id}/correct-batch | ScheduleController.CorrectBatch (:5114) | schedule Lot/Expiry/Manufacturer, ledger label row | BYPASS_DIRECT (M-080) | no VerifyCaller |
| DELETE /api/PAAssignment/{id}?mode=FullReset | PAAssignmentController.DeleteAssignment (:184) | ungive all PA-given schedules of that assignment (+ schedule resets) | THROUGH_SERVICE + BYPASS (M-083/M-084) | `assignment.DoctorId == doctorId` only |
| POST /api/Clinic | ClinicController.Add (:47) | creates BrandAmount rows | BYPASS_DIRECT (M-057) | none |
| DELETE /api/Clinic/{id} | ClinicController.Delete (:159) | deletes Bills/AdjustStocks/BrandAmounts; cascades | BYPASS_DIRECT (M-058) | none |
| PUT /api/Clinic/{id}, POST /api/Clinic | ClinicController.Put (:89) / Add | MaintainInventory flag (gates all give/ungive inventory) | BYPASS_DIRECT (M-059) | none |
| POST /api/Clinic/{from}/transfer/{to} | ClinicController.TransferPatients (:180) | changes Child.ClinicId (clinic resolution fallback) | BYPASS_DIRECT (M-060) | none |
| POST /api/Brand | BrandController.Post (:61) | creates BrandAmount rows for every doctor x clinic | BYPASS_DIRECT (M-061) | none |
| DELETE /api/Brand/{id} | BrandController.Delete (:153) | deletes BrandAmounts+Brand; Stock cascade | BYPASS_DIRECT (M-062) | none |
| GET /api/BrandAmount | BrandAmountController.GetByDoctorClinic (:20) | INSERTS missing BrandAmount rows | BYPASS_DIRECT (M-063) | none |
| PUT /api/BrandAmount | BrandAmountController.UpdateAmounts (:73) | SalePrice only | BYPASS_DIRECT (M-064) | self-declared PaId check |
| POST /api/DoctorSchedule | DoctorScheduleController.Post (:99) | INSERTS BrandAmount (Qty 0) with no dup check | BYPASS_DIRECT (M-065) | none |
| DELETE /api/Doctor/{id} | DoctorController.Delete (:443) | mass cascade | BYPASS_DIRECT (M-067) | none |
| PUT/POST /api/Doctor | DoctorController.cs:186, :315 | AllowInventory flag | BYPASS_DIRECT (M-059) | n/a |
| DELETE /api/User/{id} | UserController.Delete (:558) | possible cascade | BYPASS_DIRECT (M-068) | none |
| DELETE /api/Child/{id} | ChildController.Delete (:3860) | deletes given schedules without restore | BYPASS_DIRECT (M-069) | PA same-day rule only |
| DELETE /api/Dose/{id} | DoseController.Delete (:225) | possible schedule cascade | BYPASS_DIRECT (M-092) | none |

Verified NO inventory effect (checked by reading): Bill GET endpoints (GetAll/GetById/GetPayments/ConsumedCheck), DirectSale RecordPaymentMode/MarkDone/Confirm/GetPending/GetCompleted/GetList/GetPdf, StockTransfer GetList/GetPdf, AdjustStock GetAll, StockOverview, Dashboard, PnL, SupplierPayment (reads Stock.OriginalQuantity x StockAmount only), Schedule Delete (only IsDone==false rows), AfterInjection, ScheduleController reports/alerts, ChildController registration (EPI rows have no BrandId).

---------------------------------------------------------------------------

## 4. (c) FEFO / batch-selection code paths

| Code | file:line | selection rule | filters | consumers |
|---|---|---|---|---|
| `FefoFillCandidates` | Services/InventoryTransactionService.cs:60-69 | `Expiry` ascending, NULL expiry last, then `Id` | `BrandId`, `!IsClosed`, `Quantity > 0`, `ClinicId == clinic` OR (`ClinicId == null` AND `Bill.ClinicId == clinic`) | AdministerSync (:761), Administer (:621, dead), HasFillableBatch (:77) |
| `HasFillableBatch` | :75-78 | `.Any()` of the above | same | ScheduleController:879 (single give prompt), :2370 (bulk give prompt) |
| `FefoRestoreCandidates` | :82-90 | same ordering | `BrandId`, `Quantity >= 0` (closed allowed), clinic rule as above | UnadministerSync/BulkSync fallback (:846, :902), Unadminister (:656, dead) |
| Exact-batch restore | :843-844, :899-900 | `_db.Stocks.FirstOrDefault(s => s.Id == giveRow.StockId)` from the latest Administer ledger row of that schedule | none (no clinic check) | UnadministerSync/BulkSync |
| Ungive/give clinic resolution | ScheduleController.cs:1580-1623 (`ResolveClinicIdForStock`), :1626-1709 (`ResolveClinicIdForUngive`) | give: PA's `PaAccess.IsOnline` clinic, else doctor's `Clinic.IsOnline`, else PA online, else child clinic. ungive: clinic(s) of Stocks matching schedule's BrandId+Lot+Expiry, else child clinic, else actor clinic | see file | every give/ungive path |
| `GetLatestStockByBrandAndClinic` | ScheduleController.cs:1283-1322 | non-expired earliest Expiry, else any-dated earliest, else latest bill; NOT FEFO, no qty/closed filter | brand+clinic (same clinic rule) | `GetSingle` :165-170; legacy `ApplyStockSourceFields` :1569 (M-075 stamping) |
| `ApplyStockSourceFields` selected-lot query | ScheduleController.cs:1546-1567 | `BatchLot.Trim() == lot`, optional expiry date match, order by Expiry, Bill.BillDate, Id | brand+clinic; no qty/closed filter | legacy stamping (M-075) |
| `GetBatchLots` | StockController.cs:203-226 | `OrderBy(Expiry)` | brand, `Quantity > 0`, clinic rule; no `IsClosed` filter | GET /api/Stock/batch-lots (UI pickers) |
| Manual batch pickers (by BatchLot string) | AdjustStockController via `AdjustLoss` :327-332; StockTransferController.cs:75-80; DirectSaleController.cs:79-84; ReverseAdjustment :363-368, :382-387; ReverseTransferOut :484-488; ReverseDirectSale :565-569 | first row matching BrandId+BatchLot (Expiry NOT compared; `FirstOrDefault` without OrderBy) + clinic rule; Loss/Transfer/Sale add `Quantity > 0` | as stated | those endpoints |
| PostPurchaseLine merge lookup | :98-100 | BrandId + this BillId + BatchLot + Expiry (includes closed rows) | none | Bill Create/Update |
| AdjustIncrease/PostOpeningBalance merge lookup | :221-226 (clinic rule, any IsClosed), :282-285 (`ClinicId == clinicId` exact, `!IsClosed`) | BatchLot + Expiry identity | as stated | Adjust Increase / opening balance |
| Batch-card stamping for PDFs | ChildController.cs:5062-5072, :5362-5371 | per brand: rows with lot/expiry first, then newest Bill date, then Id | brand+clinic rule | card/certificate PDFs (READ_ONLY) |

---------------------------------------------------------------------------

## 5. (d) Summary counts

Counted from the section-1 tables (one row = one entry).

Total register entries: 92 (M-001..M-092).

| classification | all entries | inside InventoryTransactionService/Reconciliation/Context (M-001..M-029) | controller/helper/cascade sites (M-030..M-092) |
|---|---|---|---|
| THROUGH_SERVICE | 35 | 19 (the service's own mutating methods/sites) | 16 (controller call sites that delegate to the service) |
| BYPASS_DIRECT | 33 | 1 (Context.BumpConcurrencyTokens, RowVersion only, benign) | 32 |
| READ_ONLY | 20 | 6 | 14 |
| DEAD_CODE | 4 | 3 (Administer, Unadminister, 5-arg AdministerSync) | 1 (commented block DoctorController:215-220) |

Key ratio for the refactor: controller-side mutation sites that already go through the service = 16; controller-side sites that mutate inventory state themselves = 32 (of which the ones that change on-hand quantity, ledger, or BrandAmount counters/rows directly: M-032, M-034, M-036, M-040, M-044, M-049..M-055, M-057, M-058, M-061..M-065, M-067..M-069, M-092; schedule-snapshot-only: M-072, M-074..M-076, M-079, M-080, M-084; config/flag/indirect: M-059, M-060).

Distinct raw write primitives found by grep: `Stocks.Add` x7 (6 service, 1 BillController:496), `Stocks.Remove` x0, `BrandAmounts.Add/AddRange` x6 (service :459; ClinicController:81; BrandController:93; BrandAmountController:45; DoctorScheduleController:130; commented DoctorController:220), `BrandAmounts.RemoveRange` x2 (ClinicController:168, BrandController:164), `InventoryTransactions.Add` x7 (service :37; AdjustStockController:162; InventoryBackfillController:149,189,219,245,317), raw SQL x1 (InventoryBackfillController:36), direct Stock.Quantity/IsClosed writes outside the service x1 (StockTransferController:267-268), direct BrandAmount.Quantity writes outside the service x2 (StockController:185, InventoryBackfillController:106).

---------------------------------------------------------------------------

## 6. Observations worth a second look (all verified by reading the cited lines; not fixed here)

1. Bill edit inflates `OriginalQuantity` and ignores price edits on a retained line: `ReverseBillLine` (:152-153) zeroes Quantity but not OriginalQuantity; `PostPurchaseLine` then merges into the same closed row (`OriginalQuantity += quantity` :106) and does not assign StockAmount. Also re-creates consumed units (Quantity restored to the full line quantity).
2. `SplitConsumed` (BillController:486-500) gives the new row `Quantity = consumed` although those units are already consumed (phantom on-hand, FEFO-eligible; BrandAmount not changed; original row ledger goes -consumed without a matching live change => Stock-level drift).
3. Normal single give never sets `Schedule.StockId` and stamps Lot/Expiry/VaccineCost through the legacy selector (M-075); only the HPV early-return (:1003) and the bulk path (:2747) persist the consumed StockId. Ungive leaves StockId/Lot/Expiry/StockClinicId stale (M-072, M-084).
4. `ReverseDirectSale`, `ReverseTransferOut`, `ReverseAdjustment`(Loss) add quantity back without clearing `IsClosed`; FEFO filters `!IsClosed`, so restored units can be unreachable.
5. `StockTransferController.Delete` zeroes destination rows directly (M-040) with no matching ledger row; `ReverseTransferIn` logs StockId null.
6. `DoctorScheduleController.Post` (M-065) and `BrandAmountController.GetByDoctorClinic` (M-063) can create BrandAmount duplicates / rows on read; `InventoryTransactionService` looks BrandAmounts up with `FirstOrDefault` so a duplicate row splits the counter.
7. `InventoryBackfillController.Run` (M-049) and `correct-drift`/`Stock/reconcile` (M-055, M-044) are unauthenticated and destructive; the two reconcilers use different ledger windows (all-time vs since StockPeriodStart).
8. Bulk ungive resolves clinic/lot from `dbSchedule` (primary row) instead of the item's `schedule` (:2577, :2628) and skips restore silently when ScheduleBrands is empty.
9. Ledger rows are never deleted by normal code (only M-049), but the ledger has no FK: every hard delete (Clinic/Brand/Doctor/Bill/Child) leaves orphan ledger rows.


---

## AFTER THE REFACTOR (post-implementation status of the baseline above)

The register above is the BASELINE before the refactor. Current state of the BYPASS_DIRECT entries:

- Now routed through `InventoryTransactionService`: `SplitConsumed` (`SplitConsumedLine`), transfer delete (`CheckTransferReversible` + `ReverseTransfer`), backlog claim in `AdjustStockController` (replaced by `ClaimUnbatched`), `reconcile` and `correct-drift` (`RebuildBrandAmounts`), single/bulk ungive (`UnadministerCore`), all BrandAmount creators (`BrandAmountProvisioner.Ensure`).
- Removed: `InventoryBackfillController.Run` and its five backfill helpers (endpoint retired), `InventoryReconciliationService` (replaced by the read-only `InventoryAuditService`), the async `Administer`/`Unadminister` dead code.
- Blocked while stock or given doses exist (`InventoryDeleteGuard`): clinic, brand, doctor, user and dose deletes. Patient delete now voids that patient's pending doses.
- Guarded by `NoDirectStockMutationTests`: no other production file may write batch quantity, `OriginalQuantity`, `IsClosed`, the BrandAmount counter, the ledger, or batch/counter rows, and no raw SQL may touch the inventory tables.
- Label-only, not inventory: `CorrectBatch` (changes the dose's printed lot, writes a zero-delta ledger note through the service). It still has no caller check; that is an authorization gap, not a stock mutation.
