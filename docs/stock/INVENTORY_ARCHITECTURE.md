# Inventory Architecture — Stock Integrity Refactor (Agent 1 design)

> **Read the section "AS BUILT" at the end first.** It lists where the implementation differs from this design (names, routes, deferred parts). `DESIGN_REVIEW_RESOLUTIONS.md` records the review changes.

Branch `stock-integrity-refactor`. Design only; no production code changed.
Acceptance criteria: `VaccineAPI.Tests/Cases/FiveKnownFailureTests.cs` (5 tests, all currently red) + `InventoryInvariants.Check` (INV1..INV4).

Everything below was derived from the real code (file references given). Where the code contradicted the brief, that is called out in section 0.

---------------------------------------------------------------------------------------------------

## 0. Ground truth found in the code (facts the design depends on)

| # | Fact | Where |
|---|------|-------|
| F1 | The service never saves and never owns a transaction. Every controller hand-rolls `BeginTransactionAsync` + `SaveChanges`. Some early-`return Ok(false)` paths sit inside the `using tx` (safe, dispose = rollback), but ScheduleController does NOT use one tx. | `InventoryTransactionService.cs:18-26`, `ScheduleController.cs:724, 800, 898, 2805` |
| F2 | Give has two divergent implementations: `Administer` (async, brand-level BA check, `ba.Quantity -= 1` before FEFO) and `AdministerSync` (sync, clamps BA at 0, writes `-1` ledger row with `StockId = null`, sets `NeedsReconcile`). Same for ungive (3 copies). | `InventoryTransactionService.cs:610-674, 728-917` |
| F3 | Batch identity is a moving heuristic: reversals/loss/transfer/sale locate the batch with `BrandId + BatchLot + (ClinicId or Bill.ClinicId)` + `FirstOrDefault()`. Several bills can own rows with the same lot, so the wrong row can be picked; a lot that spans two rows cannot be sold beyond the first row. | `AdjustLoss:327`, `ReverseTransferOut:484`, `ReverseDirectSale:565`, controllers' `sourceStock` queries |
| F4 | `IsClosed` is written in 7 places, and read-restored in 4. `ReverseTransferOut`/`ReverseDirectSale` add quantity but never touch `IsClosed` (Case 5). | `InventoryTransactionService.cs:112,152,197,233,417,550,636,661,777,851,907` |
| F5 | `Stock.ClinicId` is set only by `PostOpeningBalance`. Purchases (`PostPurchaseLine`), `TransferIn`, `AdjustIncrease` leave it null and rely on `Bill.ClinicId`. Every query carries the `ClinicId == c OR (ClinicId == null AND Bill.ClinicId == c)` fallback copy-pasted ~15 times. | grep `s.ClinicId == null` |
| F6 | **Bill edit is reverse-all then re-post-all** (`BillController.Update` -> `ReverseBillLine` then `PostPurchaseLine`). `ReverseBillLine` logs `-stock.Quantity` (remaining only) and closes the row; `PostPurchaseLine` then finds the same-bill row and does `Quantity += qty; OriginalQuantity += qty`. Result for Case 1: Quantity 100 (should be 60), OriginalQuantity 200, ledger 100-40-60+100=100 (coincidentally equals Quantity, but the physical truth is wrong by the consumed 40). | `BillController.cs:227-240`, `PostPurchaseLine:98-113` |
| F7 | Single give commits stock in an interim tx at `ScheduleController.cs:898-912`; the "previous dose is not given" check is at `:1088-1100` and `GiveCount>=2` at `:1107` — both AFTER the commit (Case 2). The same pattern exists for ungive (`:724`) and true->true brand-change (`:800`): the old-brand reversal is committed before the new-brand give is validated, so a rejected brand-change leaves a given dose whose old-brand stock was already returned. **Not in the brief; same root cause as Case 2.** | `ScheduleController.cs:724, 800, 898` |
| F8 | Give-at-zero writes ledger `-1` with `StockId=null` and clamps BA at 0. The unbatched *ungive* writes ledger delta `0` while doing `ba.Quantity++` — so a legacy unbatched give+ungive leaves ledger at -1 forever while BA is +1. Legacy null-stock ledger sums are therefore not simply "count of unbatched gives". | `AdministerSync:781-797`, `UnadministerSync:830-837` |
| F9 | `SplitConsumed` creates a new Stock row with `Quantity = consumed` (not 0) and never touches BrandAmount: it manufactures `consumed` phantom units of stock (and INV2 breaks). **Not in the brief.** | `BillController.cs:432-443` |
| F10 | `StockTransferController.Delete`, `AdjustStockController.Delete`, `DirectSaleController.Delete`, `BillController.Reverse` HARD-delete the document although every model has a `Status` column ("Never hard-deleted; a reverse flips this"). No list endpoint filters `Status`. | controllers |
| F11 | `ReverseAdjustment` (Increase) silently does `Math.Min(adjustment, stockRow.Quantity)` — reverses a partial amount without telling anyone. `ReverseBillStock` (Bill.Reverse) logs `-Quantity` remainder and drops the consumed portion's cost history. | `InventoryTransactionService.cs:371, 186-200` |
| F12 | `/api/InventoryBackfill/run` executes raw `DELETE FROM inventorytransactions` and rebuilds a *guess* of history. `/api/stock/reconcile` and `/api/InventoryBackfill/correct-drift` overwrite `BrandAmount.Quantity` from the ledger floored at 0. Any of these run in production destroys or falsifies the authoritative record. | `InventoryBackfillController.cs:34-40`, `StockController.cs:154-200` |
| F13 | Patient/doctor delete (`ChildController:3886`, `DoctorController:461`) removes given `Schedule` rows without touching stock. The ledger `Administer` row stays (correct: the vial was physically used) but any pending unbatched record must be voided. | controllers |
| F14 | Prod DB: `lower_case_table_names=0`, EF lowercases every table name (`Context.OnModelCreating`). No EF migrations; no `EnableRetryOnFailure` (so user-controlled transactions are legal without `CreateExecutionStrategy`). Existing `unbatched_stock_reconciled_column.sql` uses `InventoryTransactions` (capitalised) — on prod that table name would not match; all new SQL must be lowercase. | `Context.cs:100-106`, `Program.cs:22` |
| F15 | Test rig: SQLite in-memory + `EnsureCreated`, production `Context`. So any new table/column/index/CHECK must exist in the EF model (`OnModelCreating`) or tests will not see it; MySQL-only features (`FOR UPDATE`, CHECK enforcement differences) must be guarded by `Database.IsMySql()`. `GiveViaService` calls `AdministerSync(ba, clinic, 9000+i, Today, null, true, "NORMAL", out _)` with **fake ScheduleIds** -> `UnbatchedUse.ScheduleId` must NOT have an FK to `schedules`. `w.Bills/Transfers/Sales/Adjusts/Schedules` construct controllers with `(Context, InventoryTransactionService)` -> those ctor signatures and `HasFillableBatch(brand, clinic)` must survive. | `StockWorld.cs` |

---------------------------------------------------------------------------------------------------

## 1. Target architecture

### 1.1 What is authoritative (decision (a))

```
 AUTHORITATIVE (truth)          inventorytransactions   append-only ledger, one row per stock movement
 MATERIALISED BATCH STATE       stocks.Quantity         = Σ ledger.QuantityDelta WHERE StockId = s.Id
                                stocks.OriginalQuantity = Σ receipt-class ledger deltas for that StockId
                                stocks.IsClosed         = (Quantity == 0)         (derived in ONE place)
 PROJECTION (rebuildable)       brandamounts.Quantity   = Σ stocks.Quantity over (BrandId, ClinicId)
                                brandamounts.NeedsReconcile = EXISTS pending unbatched use for (brand, clinic)
 OUTSIDE THE INVARIANTS         unbatcheduses (Pending)  a clinical fact "dose given, batch unknown"; NO quantity effect
```

* The ledger is truth; `Stock.Quantity` is the per-batch materialisation kept in the same DB transaction (needed for FEFO and row locking); `BrandAmount.Quantity` is a pure function of batch rows written by exactly one method.
* **Single writer**: `InventoryTransactionService` (keep the class name and constructor; tests construct it). Its only mutation primitive is the private `PostMovement`. No controller, no other service, no SQL script touches `stocks.Quantity/OriginalQuantity/IsClosed`, `brandamounts.Quantity`, or inserts into `inventorytransactions`. (Enforced by a grep-based unit test — see stage 8.)
* Receipt-class movements (change `OriginalQuantity`): `Purchase, TransferIn, OpeningBalance, AdjustIncrease, BillEdit, MigrationCorrection(receipt) and their reversals`. Consumption-class: everything else with a negative delta (`Administer, DirectSale, TransferOut, AdjustLoss, Wastage, Expiry`) and their reversals. `Consumed(s) = OriginalQuantity - Quantity` always.

### 1.2 Movement types -> existing enum (`InventoryTransactionType`, values never reordered)

| Requested | Enum | Delta | Notes |
|-----------|------|-------|-------|
| Purchase | `Purchase` (0) | + | receipt |
| Give (FEFO) | `Administer` (12) | - | one row per batch drawn; claim of a pending use is also `Administer` (`UnbatchedUseId` set) |
| DirectSale | `DirectSale` (10) | - | |
| TransferOut / TransferIn | `TransferOut` (7) / `TransferIn` (8) | - / + | `TransferIn.CounterpartTransactionId = TransferOut row id` |
| AdjustIn / AdjustOut | `AdjustIncrease` (4) / `AdjustLoss` (5) | + / - | |
| Wastage / Expiry | **new** `Wastage` (19), `Expiry` (20) | - | write-off; backed by an `adjuststocks` row (Reason text) |
| Bill quantity edit | `BillEdit` (1) | ± | signed, same StockId; delta 0 rows = cost-only audit |
| Opening balance | `OpeningBalance` (16) | + | receipt |
| Reversal (any) | `BillReverse`(2) `TransferReverse`(9) `DirectSaleReverse`(11) `AdjustReverse`(6) `Unadminister`(13) | opposite | **must** carry `ReversesTransactionId` |
| Legacy neutralisation | `MigrationCorrection` (15) | ± | only from SQL phase B |

### 1.3 Pipeline diagram

```
 HTTP request (Bill/Transfer/Sale/Adjust/Give/Ungive/Claim/...)
        |
        v
 [Controller]  auth guard (StockActionGuard) + DTO shape checks + idempotency key lookup
        |
        v
 InventoryTransactionService.RunAtomic(scope => ...)         <-- ONE DB tx, opened here (or joined if ambient)
        |
        |  1. LOCK      SELECT ... FROM brandamounts WHERE (brand,doctor,clinic) FOR UPDATE   (ordered by brandId, clinicId)
        |               SELECT ... FROM stocks WHERE candidates FOR UPDATE                     (MySQL only; RowVersion is the fallback)
        |  2. PLAN      read-only: resolve batches (FEFO / by StockId / by ledger row), run ALL validations,
        |               produce List<PlannedMovement>.  Any rule failure => return Fail(msg). NOTHING has been mutated.
        |  3. APPLY     for each PlannedMovement -> PostMovement():
        |                    stock.Quantity += delta   (guard >= 0, else InventoryInvariantException)
        |                    stock.OriginalQuantity   (receipt-class only)
        |                    stock.IsClosed = (Quantity == 0)          <- the only writer of IsClosed
        |                    ledger row INSERT (ReversesTransactionId / CounterpartTransactionId / UnbatchedUseId)
        |  4. FLUSH     SaveChanges()  (stocks + ledger + documents)
        |  5. PROJECT   foreach touched (brand,clinic): ProjectBrandAmount()  -> brandamounts.Quantity = SUM(stocks.Quantity)
        |  6. ASSERT    AssertInvariants(touchedStockIds, touchedPairs)      (section 2.1) -> throws => rollback
        v
 COMMIT (all-or-nothing)      exception / Fail / DbUpdateConcurrencyException => ROLLBACK + ChangeTracker.Clear()
        |
        v
 +------------------+   +------------------------+   +-------------------------------+
 | ledger (truth)   |   | stocks (batch state)   |   | brandamounts (projection)     |
 +------------------+   +------------------------+   +-------------------------------+
                         unbatcheduses / idempotencykeys / documents change in the same tx
```

### 1.4 Class layout (partial class, new files; existing file becomes a thin shim)

```
Services/Inventory/InventoryTransactionService.cs          public façade (existing name; old public methods -> [Obsolete] shims)
Services/Inventory/InventoryService.Core.cs                RunAtomic, PostMovement, ProjectBrandAmount, AssertInvariants, locking
Services/Inventory/InventoryService.Queries.cs             BatchesAt(), UsableBatches(), ResolveClinic(), AllocateFromLot()
Services/Inventory/InventoryService.Receipts.cs            PostPurchase, EditBill, PostOpeningBalance, AdjustIn, TransferIn
Services/Inventory/InventoryService.Consumption.cs         Give, Ungive, SellDirect, TransferOut, AdjustOut, WriteOff
Services/Inventory/InventoryService.Reversals.cs           PlanReverse/ApplyReverse (generic), Reverse* document wrappers
Services/Inventory/InventoryService.Unbatched.cs           ClaimUnbatched, DismissUnbatched, VoidUnbatched..., Release
Services/Inventory/InventoryAuditService.cs                replaces InventoryReconciliationService (report; read-only)
Services/Inventory/IdempotencyService.cs                   key table helper
Models/UnbatchedUse.cs, Models/IdempotencyKey.cs           new entities
```

---------------------------------------------------------------------------------------------------

## 2. Design decisions

### (a) Invariant assertion inside every transaction

`RunAtomic` collects `touchedStockIds` and `touchedPairs` (brand, clinic) during `PostMovement`. Before commit:

```csharp
private void AssertInvariants(HashSet<int> stockIds, HashSet<(long brand,long clinic)> pairs)
{
    // I1 (delta form, ALWAYS on): what we changed on the batch == what we wrote to the ledger for it in this tx.
    //   tracked: foreach touched stock: (Quantity - originalValue(Quantity)) == Σ delta of ledger rows staged for that StockId.
    // I1 (absolute form, when Inventory:StrictMode=true): re-read from DB inside the tx
    //   SELECT s.Id, s.Quantity, COALESCE(SUM(t.QuantityDelta),0)
    //   FROM stocks s LEFT JOIN inventorytransactions t ON t.StockId = s.Id
    //   WHERE s.Id IN (@ids) GROUP BY s.Id, s.Quantity   -> every row Quantity == sum else InventoryInvariantException
    // I3-neg : Quantity >= 0 on every touched stock (also enforced in PostMovement before the write).
    // I8     : Quantity > 0  =>  IsClosed == false ; Quantity == 0 => IsClosed == true.
    // I2 (absolute, strict): brandamounts.Quantity == SELECT SUM(Quantity) FROM stocks WHERE BrandId=@b AND ClinicId=@c
    //   -- guaranteed because Project ran, asserted anyway (cheap).
    // I4 (strict): Σ ledger for (brand, clinic) == Σ stocks.Quantity for (brand, clinic)
    //   (this is exactly test INV4; it catches null-StockId ledger rows and cross-clinic writes).
}
```

Delta form is always on so legacy drift cannot block trading but no *new* code can add drift. Absolute forms are enabled by `appsettings: "Inventory": { "StrictMode": true }` — **true in tests, false in prod until SQL phase B has zeroed the legacy drift** (section k). Failure => `InventoryInvariantException` => rollback, `IsSuccess=false`, message "Stock integrity check failed; nothing was changed", logged with the movement plan.

`PostMovement` (private, the only primitive):

```csharp
private InventoryTransaction PostMovement(PlannedMovement m)   // m: StockId (nullable ONLY for MigrationCorrection), ClinicId, BrandId, DoctorId,
{                                                              //    Delta, Type, SourceId, EventDate, UnitCost, ReversesId?, CounterpartId?, UseId?, Actor
    var stock = m.StockId is int id ? LoadTrackedForUpdate(id) : null;      // never a lot heuristic
    if (stock != null)
    {
        var newQty = stock.Quantity + m.Delta;
        if (newQty < 0) throw new InventoryInvariantException($"batch {stock.Id} would go to {newQty}");
        stock.Quantity = newQty;
        if (IsReceiptClass(m.Type)) stock.OriginalQuantity += m.Delta;
        SyncClosed(stock);                                   // stock.IsClosed = stock.Quantity == 0   <- only place
        _touchedStocks.Add(stock.Id); _touchedPairs.Add((stock.BrandId, m.ClinicId));
    }
    var row = new InventoryTransaction { ... m ..., BatchLot = stock?.BatchLot, Expiry = stock?.Expiry };
    _db.InventoryTransactions.Add(row);
    return row;
}
```
`m.ClinicId` is asserted equal to `ResolveClinic(stock)`; a mismatch is a bug -> exception.

### (b) BrandAmount projection + single clinic resolution

* `ProjectBrandAmount(long brandId, long clinicId)` is the only writer of `brandamounts.Quantity/NeedsReconcile`:
  ```csharp
  var doctorId = ClinicOwnerDoctor(clinicId);                         // Clinic.DoctorId
  var sum      = BatchesAt(brandId, clinicId).Sum(s => s.Quantity);   // DB query AFTER the FLUSH so it includes this tx's stock writes
  var ba       = LockedBrandAmount(brandId, doctorId, clinicId) ?? CreateBrandAmount(...);   // SalePrice copied from the doctor's other clinic row for the brand, else 0
  ba.Quantity = sum;  ba.NeedsReconcile = PendingUnbatchedExists(brandId, clinicId);
  ```
  No `Math.Max(0, ...)` clamps anywhere; a negative sum is impossible because `stocks.Quantity >= 0` is asserted per batch.
* Clinic resolution — one helper, one query shape:
  ```csharp
  public static long ResolveClinic(Stock s) => s.ClinicId ?? s.Bill?.ClinicId ?? 0;            // transitional
  public IQueryable<Stock> BatchesAt(long brandId, long clinicId) =>
      _db.Stocks.Where(s => s.BrandId == brandId &&
          (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)));
  ```
  All ~15 copies of the OR-expression are deleted; the OR arm is dropped in SQL phase C once `ClinicId` is NOT NULL. **Every insert from now on sets `Stock.ClinicId`** (purchase, TransferIn, AdjustIn, opening, split, recreated). Backfill SQL is in section (k) A5. Stocks with `ClinicId IS NULL AND BillId IS NULL` (or bill missing) are reported as ORPHAN_BATCH and are excluded from projection until a human assigns a clinic.
* Doctor scope: batches have no `DoctorId`; clinic -> `Clinic.DoctorId` is the owner. `BrandAmount` uniqueness `(BrandId, DoctorId, ClinicId)` (unique index, phase A6).

### (c) `IsClosed` derived from `Quantity` in a single place

* Only `SyncClosed(Stock)` writes it; it is called only by `PostMovement` and by the constructor helper `NewBatch(...)`. The 7 existing `IsClosed = ...` writers are deleted. `Stock.IsClosed` stays a column (FEFO queries and the VacDoc DTOs read it) but is never an input: FEFO filters on `Quantity > 0` (and expiry), not on `IsClosed`.
* DB backstop (phase C): `CHECK (Quantity >= 0)` and `CHECK ((Quantity = 0 AND IsClosed = 1) OR (Quantity > 0 AND IsClosed = 0))`, also added via `HasCheckConstraint` in `OnModelCreating` so SQLite tests get them.

### (d) Bill edit model (`InventoryService.EditBill`)

Signature:
```csharp
OpResult<BillEditResult> EditBill(Bill bill, IReadOnlyList<BillLineEdit> lines /* int? StockId, long BrandId, string BatchLot, DateTime? Expiry, int Quantity, decimal UnitPrice */,
                                  decimal awtPercent, DateTime billDate, bool releaseClaims, MovementContext ctx);
```
`BillLineDTO` gains optional `int? StockId` (VacDoc's `BillLineDetailDTO` already returns `StockId`, so the edit form can echo it). If absent (old client) the server matches each dto line to an existing `bill.Stocks` row deterministically by `(BrandId, BatchLot, Expiry)` — first unmatched; the result is identical to id matching. **Never** reverse-all/re-post-all.

Per matched pair (stock S, line L), `consumed = S.OriginalQuantity - S.Quantity`, `delta = L.Quantity - S.OriginalQuantity`, `newCost = round(L.UnitPrice * (1 + awt/100), 4)`:

| Change | Rule | Ledger |
|--------|------|--------|
| Nothing but price / AWT / header | `S.StockAmount = newCost` only. Snapshots already taken (`Schedule.VaccineCost`, `DirectSale.PurchasePricePerUnit`, old ledger `UnitCost`) are untouched. | one zero-delta `BillEdit` audit row (`ConsumesStock=false`, carries new UnitCost) — not a movement |
| Quantity change | Guard `S.Quantity + delta >= 0` (i.e. `L.Quantity >= consumed`), else FAIL "Line X: cannot reduce to {q}; {consumed} units already used/sold/transferred." | ONE signed `BillEdit` row `delta` on `S.Id`; `OriginalQuantity += delta` (set, never accumulated); `Quantity += delta` |
| Lot / expiry change | allowed only if `consumed == 0` and no live claims/uses: update labels in place; else FAIL ("lot change would disagree with issued certificates; use split-consumed / dose batch correction"). | zero-delta `BatchCorrection`-style audit row |
| Brand change | allowed only if `consumed == 0` and no claims: explicit relabel = `BillEdit -Quantity` on old batch + new batch of new brand `BillEdit +Quantity` (two rows, net zero per clinic, each brand's projection refreshed). Else FAIL. | 2 rows |
| Line removed | allowed only if `consumed == 0` and no live claims (or `releaseClaims=true`, which auto-releases claims, section (e)); else FAIL with existing text about split-consumed. Removal = `BillEdit -Quantity`, batch closed by `SyncClosed`, not deleted. | 1 row |
| Line added | new `Stock` (`ClinicId = bill.ClinicId`, `BillId`) + `Purchase` row | 1 row |

Duplicate lines in the DTO with identical brand+lot+expiry are merged in PLAN (as `PostPurchaseLine` does today), never after posting. The whole edit is one `RunAtomic`; failure of any line => no change to any line (I3).

Case 1 trace: 100 posted, 40 given -> S.Quantity 60, Original 100. Edit price 10->12, qty 100: `delta = 0` -> only `StockAmount=12` + zero-delta audit row. Assert: Quantity 60, OriginalQuantity 100, BA 60, IsClosed false, ledger sum 60. GREEN.

`SplitConsumed` (F9) is re-implemented as a net-zero relabel (4 zero-net rows, S' created with `Quantity = 0`, see section 6, stage 10); until fixed the endpoint returns a "temporarily disabled" failure (decision needed — conflict list #12).

### (e) Generic reversal

Ledger column `ReversesTransactionId BIGINT NULL` + **unique index** (one reversal per original row, DB-enforced -> I5 and no double reversal). `CounterpartTransactionId BIGINT NULL` links a `TransferIn` to its `TransferOut`.

```csharp
ReversalPlan PlanReverse(IReadOnlyList<InventoryTransaction> originals, ReverseOptions o)   // o: ReleaseClaims, Actor, EventDate
OpResult    ApplyReverse(ReversalPlan plan, InventoryTransactionType reverseType, long sourceId)
OpResult    ReverseDocument(InventoryTransactionType docType, long docId, ReverseOptions o)  // finds originals = ledger rows WHERE SourceType=docType AND SourceId=docId AND ReversesTransactionId IS NULL AND NOT EXISTS reversal
```
Validation rules (all in PLAN, before any mutation):

1. An original that is itself a reversal cannot be reversed (re-do the business action instead). An original already reversed -> "already reversed" (also the unique index).
2. **Reversing a consumption** (`delta < 0`: sale, give, loss, transfer-out, write-off): always allowed. Posts `+|delta|` on the SAME `StockId` (never a lot search); batch reopens because `IsClosed` is derived. Same clinic as the original row (`ClinicId` is copied).
3. **Reversing a receipt** (`delta > 0`: purchase, bill-edit+, transfer-in, adjust-in, opening balance) — **I6**: let `stock = S(original.StockId)`, `claims = live UnbatchedUse claims on S from the same source document`, `releasable = Σ claims`. Require `S.Quantity + releasable >= original.delta`. Else FAIL listing `consumed by: n gives, m sales, k transfers, j write-offs` (computed from ledger rows on S that are not the receipt and not reversed). It may **never** create the missing units elsewhere.
4. `releasable` claims are released only when `o.ReleaseClaims` is true (the doctor said yes): each claim is reversed (`Unadminister` with `ReversesTransactionId = claim row`), the `UnbatchedUse` returns to `Pending`, `Schedule.StockId/Lot/Expiry/VaccineCost` are cleared. Only claims are auto-releasable; real gives/sales are never silently undone.
5. Reversals are dependency-ordered inside one tx: release claims -> reverse receipts -> project.

Document wrappers:

| Action | Originals | Rule |
|--------|-----------|------|
| **Sale delete** (`DirectSale`) | its `DirectSale` rows | consumption reversal (rule 2): +qty to each original `StockId`; `IsClosed` derived. Sale row `Status=1`. **Case 5 GREEN.** |
| **Transfer delete** | all `TransferOut` + `TransferIn` rows with `SourceId in (transfer ids of the XFER bill)` | pair each `TransferIn` to its `TransferOut` via `CounterpartTransactionId`. For each pair: destination batch `D` must satisfy `D.Quantity == in.delta` (net-untouched; give+ungive nets to zero so it passes) after releasing claims. **If any destination batch is partly consumed: REJECT whole delete, nothing changes**, message: `"{c} of {q} units of {brand} lot {lot} were already used/sold at {destClinic}; only {r} remain. Reverse those first, or use 'Return remaining {r} units' instead."` Optional separate operation `ReturnTransferRemainder(transferId)` = a new normal transfer (dest -> source) for the unconsumed `D.Quantity`, own document & ledger, original stays Active, never silent. Then source batch: `+out.delta` on the original source `StockId`. **Case 4 GREEN** (reject; A=0, B=6). |
| **Purchase reverse** (`Bill.Reverse`) | `Purchase` + `BillEdit` rows per stock | rule 3 per stock: reject when any line is consumed (today it silently drops the remainder and loses cost history). Bill `Status=1`, rows kept. |
| **Adjustment delete** | `AdjustIncrease`/`AdjustLoss` rows of that adjust id | Increase: rule 3 (reject if consumed; no `Math.Min` partial). Loss/Wastage/Expiry: rule 2 (restore). Adjust row `Status=1`. Legacy null-`StockId` adjust rows: FAIL with "legacy adjustment, ask support" — none remain after phase B. |
| **Ungive** | the un-reversed `Administer` row(s) for the ScheduleId | rule 2 on the recorded `StockId`+`ClinicId`; **no lot heuristics** (`ResolveClinicIdForUngive` is replaced by `giveRow.ClinicId`, falling back to `Schedule.StockClinicId`, and only for legacy doses with neither to the old lot heuristic, logged). |

### (f) Give / ungive atomicity

* One implementation: `Give(GiveRequest)` / `Ungive(scheduleId, ...)` synchronous (the schedule controller is synchronous; async controllers simply call the sync method — one code path instead of today's Administer/AdministerSync duplication). `AdministerSync`, `UnadministerSync`, `UnadministerBulkSync`, `Administer`, `Unadminister` become `[Obsolete]` shims delegating to it (tests and `w.GiveViaService` call `AdministerSync(...)`).
* **Wrapper pattern** (least invasive change to a 5288-line controller): rename `Update` -> `UpdateCore` and add
  ```csharp
  [HttpPut("child-schedule")]
  public Response<ScheduleDTO> Update(ScheduleDTO dto) =>
      _inventory.RunAtomic(() => UpdateCore(dto), r => r.IsSuccess);   // rollback on !IsSuccess (incl. Warning), on exception, on concurrency
  ```
  Same wrapper for `UpdateBulkInjection`. Inside `UpdateCore` the three interim `using (var tx = _db.Database.BeginTransaction()) { SaveChanges; Commit }` blocks (`:724, :800, :898`) are removed (EF forbids nested `BeginTransaction`; the ambient tx replaces them) — stock is only *staged* there; the existing final `SaveChanges()` calls (`:1009, :1213`) are inside the same tx and `RunAtomic` commits after the invariant assertion. Result: dose-2-without-dose-1 rejection at `:1098` rolls back a staged deduction that never reached the DB (Case 2 GREEN), and a rejected true->true brand change no longer strands the old brand's reversal (F7).
* On rollback `RunAtomic` calls `_db.ChangeTracker.Clear()` so a controller that continues to use the context (e.g. to build the error message) never sees half-applied entities.
* Validation before mutation: the give's own rules (permission, MinAge, MinGap, MaxAge, previous-dose, GiveCount, future-date, decision model) all execute before `Give()`. Where reordering is impractical, atomicity (rollback) is the guarantee; the minimum required move is "call `Give()` after the previous-dose and GiveCount checks" (they sit at `:1088-1109`, `Give` currently at `:890`) — a pure code move recommended in stage 5 in addition to the wrapper.
* Ungive mirrors the give's ledger row: reads the **un-reversed** `Administer` row (`SourceId = scheduleId`, latest), uses its `StockId/ClinicId/DoctorId`, posts `Unadminister` with `ReversesTransactionId`. Pending/unbatched handled by the use's `Status` (section 3).
* Bulk give (several doses of one brand in one request): `Give` filters candidates on the *tracked* quantity (`.ToList().Where(s => s.Quantity > 0)`) so the second dose sees the first dose's staged deduction (the SQL filter sees only committed values — the existing code relied on `Math.Min(src.Quantity, remaining)` accidentally).

### (g) FEFO and expired stock

```csharp
IQueryable<Stock> UsableBatches(long brandId, long clinicId, DateTime onDate) =>
    BatchesAt(brandId, clinicId)
      .Where(s => s.Quantity > 0 && (s.Expiry == null || s.Expiry.Value.Date >= onDate.Date))     // I7 (IsClosed is NOT consulted)
      .OrderBy(s => s.Expiry.HasValue ? 0 : 1).ThenBy(s => s.Expiry).ThenBy(s => s.Id);
public bool HasFillableBatch(long brandId, long clinicId, DateTime? onDate = null)                // test signature preserved
```
* `onDate` = the **give date** (a backdated late-recording uses its own date; "expired at the give date").
* Expired stock (`Expiry < today AND Quantity > 0`) is never given, sold or transferred. It stays visible: Stock Overview marks it `Expired`, audit category `EXPIRED_WITH_STOCK`, and the doctor writes it off with `WriteOff(stockId, qty, reason: Expiry|Wastage)` (new endpoint `POST /api/stock/writeoff`, backed by an `adjuststocks` row `Reason="Expired"`; ledger `Expiry`/`Wastage`, delta `-qty`, batch closes via `SyncClosed`). `AdjustLoss` remains the general manual out.
* If only expired stock exists, a give behaves like "no usable batch": the existing prompt/`ConfirmUnbatchedGive` path applies and the dose is recorded as a **Pending unbatched use** — with the message "Only expired stock on hand (n units, lot X, expired D)". Stock is not silently consumed.
* Lot-scoped operations (DirectSale, TransferOut, AdjustLoss/write-off): `AllocateFromLot(brandId, clinicId, lot, expiry?, qty)` allocates FEFO across ALL open rows of that lot (fixes F3 "first row only"), one ledger row per batch; sale/transfer additionally reject expired batches (write-off is allowed on expired).

### (h) Idempotency

Chosen mechanism: generic table `idempotencykeys` (unique `(DoctorId, ClientRequestId)`) instead of a column on each document, because `StockTransfer` and `DirectSale` create **N rows per request** (one per item, sharing the XFER/SALE bill), so a per-row unique column cannot express "this request".

Flow (in `RunAtomic`, first statement of the tx): insert key row (`Operation`, `RequestHash = SHA256(canonical DTO)`), flush. Unique violation => rollback, load existing row:
* same hash + `ResponseJson` present -> return the stored response verbatim plus `"IsDuplicate": true` (no second document, no second movement);
* same key, different hash -> `{IsSuccess:false, Message:"This request id was already used for a different request"}`;
* no `ResponseJson` yet (first request still committing): the second insert blocks on the unique index until the first commits, then falls into case 1.
On success the controller stores the serialized response into the key row before commit. Applies to: `Bill Create`, `StockTransfer Create`, `DirectSale Create`, `AdjustStock Create`, `Opening balance`, `Claim`, `WriteOff`, and optionally give/ungive. DTOs gain optional `string? ClientRequestId` (absent = current behaviour, no idempotency — old VacDoc builds keep working; VacDoc generates one uuid per form open). Auto-generated numbers (BILL-/XFER-/SALE-) are allocated inside the tx after `SELECT ... FROM doctors WHERE Id=@d FOR UPDATE` to serialise per doctor.

### (i) Concurrency

* Existing `[ConcurrencyCheck] RowVersion` on `Stock` and `BrandAmount` + `Context.BumpConcurrencyTokens` stay (they are correct and give free protection).
* Pessimistic lock in MySQL (guarded by `Database.IsMySql()`; SQLite tests rely on RowVersion): every operation first locks the `brandamounts` row for each `(brand, doctor, clinic)` it touches, ordered by `(brandId, clinicId)`, then the candidate `stocks` rows ordered by `Id` (`FromSqlInterpolated("SELECT * FROM stocks WHERE ... ORDER BY Id FOR UPDATE")`). Consistent ordering => no deadlocks between two-clinic transfers.
* `DbUpdateConcurrencyException` / MySQL 1213 (deadlock) / 1062 on `brandamounts`: `RunAtomic` rolls back, clears the tracker and **re-executes `work` once** (PLAN re-reads fresh data; idempotency key makes the retry safe). Second failure -> `Fail("Inventory was updated by another action just now. Please retry.")` (the exact string the client already handles).
* Uniqueness: `brandamounts (BrandId, DoctorId, ClinicId)` unique; `inventorytransactions (ReversesTransactionId)` unique; `unbatcheduses (ActiveScheduleKey)` unique; `idempotencykeys (DoctorId, ClientRequestId)` unique. `stocks`: optional unique `(BillId, BrandId, BatchLot, Expiry)` only after a duplicate scan (phase C; batches from different bills legitimately share lot/expiry, so no global lot uniqueness).
* Isolation: leave MySQL default REPEATABLE READ; correctness comes from the locks + RowVersion, not from SERIALIZABLE.

### (j) Reconciliation report + neutralising the wipe

`InventoryAuditService.Run(doctorId, clinicId?, brandId?)` (read-only, replaces `InventoryReconciliationService.Verify`), endpoints `GET /api/InventoryAudit/report`, `/report.csv`; `StockController.CheckIntegrity` and `InventoryBackfill/verify` delegate to it (JSON contracts of those two keep their old fields plus a new `Findings` array). Every finding row: `{Category, Severity, DoctorId, ClinicId, BrandId, StockId?, StoredQty, LedgerQty, BrandAmountQty, Diff, Detail, SuggestedAction}`.

| Category | Definition |
|----------|-----------|
| NEGATIVE_STOCK | `stocks.Quantity < 0` or `brandamounts.Quantity < 0` |
| ORPHAN_BATCH | `ClinicId IS NULL AND (BillId IS NULL OR bill missing)`, or clinic not owned by the doctor |
| CLOSED_WITH_STOCK | `IsClosed = 1 AND Quantity > 0`  (I8) |
| OPEN_EMPTY | `IsClosed = 0 AND Quantity = 0` |
| STOCK_WITHOUT_LEDGER | `Quantity > 0` and zero ledger rows for that StockId |
| LEDGER_WITHOUT_STOCK | ledger `StockId IS NULL AND QuantityDelta <> 0`, or StockId pointing to a missing stock |
| LEGACY_UNBATCHED_GIVE | `Administer, StockId NULL, ConsumesStock=1, delta<0` not compensated and with no `unbatcheduses` row (F8) |
| BATCH_DRIFT | `Quantity <> Σ ledger(StockId)` (I1) |
| BRANDAMOUNT_DRIFT | `brandamounts.Quantity <> Σ stocks.Quantity` per (brand, clinic) (I2) |
| CLINIC_LEDGER_DRIFT | `Σ ledger(brand, clinic) <> Σ stocks(brand, clinic)` (INV4) |
| RECEIPT_ACCUMULATION | `OriginalQuantity <> Σ receipt-class ledger rows` (Case 1 legacy) |
| TRANSFER_INCONSISTENT | Active transfer whose `TransferOut` sum <> `-Quantity`, or missing/unequal `TransferIn`, or `TransferIn` not paired |
| REVERSAL_WRONG | reversal row with `delta <> -original.delta`, wrong StockId/ClinicId, missing `ReversesTransactionId` (legacy reversal types), or original reversed twice |
| DUPLICATE_MOVEMENT | >1 row per `(SourceType, SourceId, StockId)` for non-repeatable types |
| EXPIRED_WITH_STOCK | `Quantity > 0 AND Expiry < today` |
| PENDING_UNBATCHED | pending uses per (clinic, brand), oldest age; escalates > 7 days |
| BA_ZERO_PRICE | brandamount created by projection with `SalePrice = 0` |

Neutralising the destructive endpoints:
* `POST /api/InventoryBackfill/run`: returns `{IsSuccess:false, Message:"Disabled: the ledger is authoritative and append-only."}` unless **all** of: `Inventory:AllowBackfillWipe=true` in config, query `?confirm=WIPE-LEDGER`, and `SELECT COUNT(*) FROM inventorytransactions = 0` (i.e. only ever usable on an empty ledger, e.g. a dev DB). The raw `DELETE` line is guarded by the same count check inside the tx.
* `POST /api/InventoryBackfill/correct-drift` and `POST /api/stock/reconcile`: no longer write from the ledger; they call `RebuildProjection(clinicId, brandId?)` = `ProjectBrandAmount` from batch rows (safe, idempotent) and return the audit findings that need human correction.
* Ledger corrections are never edits: they are `MigrationCorrection` rows written by SQL phase B or by `InventoryService.PostCorrection(...)` (doctor-approved, reason required, audited).

### (k) SQL migrations (hand-written, MySQL, lowercase, backup first)

Ordering (no EF migrations; VaccineAPI auto-deploys on push to `staging`): **additive SQL first, then code, then data corrections, then strict mode, then constraints.** Old code tolerates every Phase-A change (new columns are nullable/defaulted and unknown to old EF models; new tables are ignored; the unique index on `brandamounts` never fires with today's code because only `TransferIn` inserts BA rows and only when none exist). To avoid the "silent 500 after deploy" trap (memory: `feedback_pending_sql_after_deploy`), the new code runs a `SchemaGuard` at startup: reads `information_schema.columns` for the required objects and, if missing, makes inventory writes return `IsSuccess=false "Database update not applied: <object>"` instead of an EF exception.

```
STEP 0  BACKUP    mysqldump --single-transaction vaccineapi stocks brandamounts inventorytransactions schedules bills
                  stocktransfers directsales adjuststocks > inv_pre_refactor_YYYYMMDD.sql        (db_backup.sh pattern)
                  plus in-DB copies (fast rollback):
                  CREATE TABLE stocks_bak_20260930           AS SELECT * FROM stocks;
                  CREATE TABLE brandamounts_bak_20260930     AS SELECT * FROM brandamounts;
                  CREATE TABLE inventorytransactions_bak_20260930 AS SELECT * FROM inventorytransactions;
PHASE A   additive, safe with the OLD code, run BEFORE deploy
  A1  ALTER TABLE inventorytransactions
        ADD COLUMN ReversesTransactionId     BIGINT NULL,
        ADD COLUMN CounterpartTransactionId  BIGINT NULL,
        ADD COLUMN UnbatchedUseId            BIGINT NULL,
        ADD UNIQUE KEY ux_invtx_reverses (ReversesTransactionId),          -- all NULL today; NULLs are not unique-checked
        ADD KEY ix_invtx_stock (StockId),
        ADD KEY ix_invtx_brand_clinic_date (BrandId, ClinicId, EventDate),
        ADD KEY ix_invtx_source (SourceType, SourceId);
  A2  CREATE TABLE unbatcheduses (            -- see DDL in section 3.1
  A3  CREATE TABLE idempotencykeys (          -- see DDL in section 2(h)
  A4  ALTER TABLE stocks ADD KEY ix_stocks_brand_clinic (BrandId, ClinicId, IsClosed, Expiry);
  A5  UPDATE stocks s JOIN bills b ON b.Id = s.BillId
         SET s.ClinicId = b.ClinicId
       WHERE s.ClinicId IS NULL;                 -- identical to what old code already resolves at read time
      SELECT COUNT(*) FROM stocks WHERE ClinicId IS NULL;         -- remainder = ORPHAN_BATCH, list, do not guess
  A6  -- precondition, must return 0 rows, else merge duplicates by hand first:
      SELECT BrandId, DoctorId, ClinicId, COUNT(*) c FROM brandamounts GROUP BY BrandId, DoctorId, ClinicId HAVING c > 1;
      ALTER TABLE brandamounts ADD UNIQUE KEY ux_brandamounts (BrandId, DoctorId, ClinicId);
  (models already declare bills/stocktransfers/directsales/adjuststocks.Status, so those columns must already exist — verify with information_schema before deploy)

DEPLOY    push to staging with Inventory:StrictMode=false. New code writes the new way; delta-form assertion only.

PHASE B   data corrections — generated FROM the audit report, REVIEWED WITH THE USER, run in one transaction per clinic
  B1  derived flag:       UPDATE stocks SET IsClosed = (Quantity = 0) WHERE Quantity >= 0 AND IsClosed <> (Quantity = 0);
                          -- negative-quantity rows are NOT touched here; listed for a physical recount
  B2  legacy unbatched gives -> pending uses (only doses still given):
        INSERT INTO unbatcheduses (ScheduleId, DoctorId, ClinicId, BrandId, UseDate, RecordedAt, RecordedByPaId, Status, ActiveScheduleKey, LegacyLedgerId, RowVersion)
        SELECT t.SourceId, t.DoctorId, t.ClinicId, t.BrandId, t.EventDate, t.CreatedAt, t.CreatedByPaId, 0, t.SourceId, t.Id, 0
          FROM inventorytransactions t JOIN schedules sc ON sc.Id = t.SourceId AND sc.IsDone = 1 AND sc.StockId IS NULL
         WHERE t.SourceType = 12 AND t.StockId IS NULL AND t.ConsumesStock = 1 AND t.QuantityDelta < 0
           AND NOT EXISTS (SELECT 1 FROM unbatcheduses u WHERE u.LegacyLedgerId = t.Id);
        -- rows previously "claimed" by ReconciledByTransactionId: stock was NEVER deducted for them -> also seeded as Pending
        -- (flagged in ResolvedReason='legacy: previously stamped as absorbed'); user decision, conflict list #5.
  B3  neutralise the unbatched ledger so I4 holds: ONE MigrationCorrection (15) row per (doctor, clinic, brand) with
        QuantityDelta = -(SELECT SUM(QuantityDelta) FROM inventorytransactions WHERE StockId IS NULL AND ConsumesStock=1 AND <legacy types>)
        StockId NULL, DecisionReason 'legacy unbatched neutralisation', EventDate = migration date.
      Positive null-stock rows (AdjustIncrease without anchor bill, recreated reversals): scripted pair per row =
        new batch (ClinicId, lot/expiry from the row) + MigrationCorrection(-q, StockId NULL) + MigrationCorrection(+q, new StockId).
  B4  per-batch drift: for every BATCH_DRIFT row the doctor confirms by PHYSICAL COUNT:
        INSERT INTO inventorytransactions (DoctorId, ClinicId, BrandId, StockId, BatchLot, Expiry, QuantityDelta, UnitCost, SourceType, SourceId,
                                           CreatedAt, EventDate, ConsumesStock, DecisionReason)
        SELECT ..., (s.Quantity - COALESCE(SUM(t.QuantityDelta),0)), s.StockAmount, 15, s.Id, NOW(), CURDATE(), 0, 'drift correction to counted qty' ...
      (physical count wins; OriginalQuantity re-derived: UPDATE stocks SET OriginalQuantity = <Σ receipt-class ledger rows>)
  B5  BrandAmount rebuilt through the app: POST /api/stock/reconcile (now = ProjectBrandAmount); verify audit report = 0 findings except PENDING_UNBATCHED / EXPIRED.
  Flip Inventory:StrictMode=true (appsettings; no deploy of code needed).
PHASE C   constraints, one release later, after 0 violations for 7 days
  C1  ALTER TABLE stocks MODIFY ClinicId BIGINT NOT NULL;      (drops the Bill fallback arm in BatchesAt)
  C2  ALTER TABLE stocks ADD CONSTRAINT ck_stocks_qty CHECK (Quantity >= 0),
                         ADD CONSTRAINT ck_stocks_closed CHECK ((Quantity = 0 AND IsClosed = 1) OR (Quantity > 0 AND IsClosed = 0));
  C3  ALTER TABLE brandamounts ADD CONSTRAINT ck_ba_qty CHECK (Quantity >= 0);
  C4  (optional) unique (BillId, BrandId, BatchLot, Expiry) on stocks after duplicate scan.
ROLLBACK  code: redeploy previous build (Phase A is ignored by it). Data: restore *_bak_20260930 tables (phase B only).
```
Enum ints used above: `Administer=12, MigrationCorrection=15` (see `InventoryTransaction.cs`), `Wastage=19, Expiry=20` appended.

### (l) Staged implementation order and test mapping

Each stage ends with `dotnet test` and the InventoryInvariants check; stages keep old public method names as shims so VacDoc's JSON contract never changes mid-way.

| Stage | Scope | Turns green |
|-------|-------|-------------|
| 1 | Foundation, no behaviour change to gives: `PostMovement`, `SyncClosed`, `BatchesAt`/`ResolveClinic`, `ProjectBrandAmount`, `RunAtomic`, `AssertInvariants` (strict on in tests), model additions (`ReversesTransactionId`, `CounterpartTransactionId`, `UnbatchedUseId`, `UnbatchedUse`, `IdempotencyKey`, `Wastage/Expiry`), every insert sets `Stock.ClinicId`. Existing methods reimplemented on top of `PostMovement`. | — (existing behaviour preserved) |
| 2 | Generic reversal engine + `ReverseDocument`; rewrite `DirectSale.Delete` (soft, `Status=1`, list filters). | **Case 5** |
| 3 | Transfer as paired movements (`CounterpartTransactionId`), `TransferIn` batch gets `ClinicId`; transfer delete rule + reject message; optional `ReturnTransferRemainder`. Purchase reverse and adjust delete on the same engine. | **Case 4** |
| 4 | `EditBill` by StockId (+ optional `BillLineDTO.StockId`), cost-only path, guards. | **Case 1** |
| 5 | Unbatched pending model: `Give` writes `UnbatchedUse` (no ledger/BA/stock effect), `AdministerSync` shim, `ClaimUnbatched`/`Dismiss`/`Void`, claim prompt in purchase/transfer-in/adjust-in, ungive by Status. | **Case 3** |
| 6 | Give/ungive atomicity: `RunAtomic` wrapper on `Update` and `UpdateBulkInjection`, remove interim txs, move `Give()` after previous-dose/GiveCount checks, ungive via recorded row. | **Case 2** |
| 7 | Idempotency (`idempotencykeys`, DTO `ClientRequestId`), concurrency retry, MySQL `FOR UPDATE` locking, numbering lock. | (extra tests: duplicate POST, concurrent give) |
| 8 | `InventoryAuditService`, endpoints, neutralise wipe/`correct-drift`/`reconcile`; grep test "only InventoryTransactionService writes stocks.Quantity / brandamounts.Quantity / inventorytransactions". | — |
| 9 | FEFO expiry filter + `WriteOff` endpoint + Stock Overview expired marker. | (extra tests) |
| 10 | SQL Phase A -> deploy -> B -> strict -> C (section k). `SplitConsumed` fix (F9). | — |

Endpoints whose JSON contract for VacDoc is **unchanged** (same routes, same `IsSuccess/Message/ResponseData` shapes; new fields are additive/optional): `POST/PUT/GET /api/bill`, `GET /api/bill/{id}`, `POST /api/bill/{id}/payment`, `GET .../consumed-check`, `POST /api/StockTransfer`, `GET /api/StockTransfer`, `POST /api/DirectSale`, `GET /api/DirectSale`, `PATCH DirectSale/by-bill/*`, `POST /api/adjuststock`, `GET /api/adjuststock`, `PUT /api/Schedule/child-schedule`, `PUT .../update-bulk-injection`, `GET /api/stock/batch-lots`, `POST /api/stock/opening-balance`, all stock reports and Stock Overview. `Response.Message` for the interim prompts ("No stock batch found for this vaccine...") keeps working.
Changed/new: `DELETE StockTransfer/{id}`, `DELETE adjuststock/{id}`, `DELETE bill/{id}/reverse` may now return `IsSuccess=false` with a reason (previously always succeeded); responses gain optional `IsDuplicate`; new `GET/POST /api/stock/unbatched*`, `POST /api/stock/writeoff`, `GET /api/InventoryAudit/report`; `StockController.Reconcile` and `InventoryBackfill` behaviours per (j).

---------------------------------------------------------------------------------------------------

## 3. Unbatched (pending) use model — concrete

Evaluated and **adopted** (it is the only design that keeps I1/I2/I4 true at every instant while still letting a PA/doctor record a real dose). Adversarial points folded in: state is driven by `UnbatchedUse.Status` (never by lot/expiry heuristics), every transition is a conditional update, ungive/claim/void are transactional with the ledger, and Dismiss is doctor-only and audited.

### 3.1 Schema

```sql
CREATE TABLE unbatcheduses (
  Id                     BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
  ScheduleId             BIGINT       NOT NULL,                -- NO FK (tests use fake ids; deleted patients keep history)
  DoctorId               BIGINT       NOT NULL,
  ClinicId               BIGINT       NOT NULL,                -- inventory clinic the dose was given from
  BrandId                BIGINT       NOT NULL,
  UseDate                DATETIME(6)  NOT NULL,                -- the give date
  RecordedAt             DATETIME(6)  NOT NULL,
  RecordedByPaId         BIGINT       NULL,
  RecordedByManagerId    BIGINT       NULL,
  Status                 INT          NOT NULL DEFAULT 0,      -- 0 Pending, 1 Claimed, 2 Dismissed, 3 Voided
  ActiveScheduleKey      BIGINT       NULL,                    -- = ScheduleId while Status IN (0,1), else NULL; UNIQUE => one live use per schedule
  ClaimedStockId         INT          NULL,
  ClaimLedgerId          BIGINT       NULL,                    -- the Administer(-1) row posted by the claim
  ClaimSourceType        INT          NULL,                    -- Purchase/TransferIn/AdjustIncrease/BillEdit/OpeningBalance
  ClaimSourceId          BIGINT       NULL,                    -- bill / transfer / adjust id -> release on its reversal
  ClaimedAt              DATETIME(6)  NULL,
  ClaimedByUserId        BIGINT       NULL,
  ResolvedReason         VARCHAR(500) NULL,                    -- dismiss / void reason
  ResolvedByUserId       BIGINT       NULL,
  ResolvedAt             DATETIME(6)  NULL,
  LegacyLedgerId         BIGINT       NULL,
  RowVersion             INT          NOT NULL DEFAULT 0,
  UNIQUE KEY ux_unbatcheduses_active (ActiveScheduleKey),
  KEY ix_unbatcheduses_pool (BrandId, ClinicId, Status, UseDate),
  KEY ix_unbatcheduses_source (ClaimSourceType, ClaimSourceId),
  KEY ix_unbatcheduses_schedule (ScheduleId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
```
EF: `enum UnbatchedUseStatus { Pending=0, Claimed=1, Dismissed=2, Voided=3 }`, entity `UnbatchedUse` -> table `unbatcheduses`; `[ConcurrencyCheck] RowVersion` bumped in `Context.BumpConcurrencyTokens`.

### 3.2 State machine (all transitions inside `RunAtomic`, guarded `WHERE Status = @expected`)

```
                    Give (no usable batch, ConfirmUnbatchedGive)
                                   |
                                   v
          +----------------- PENDING (0) -----------------+
          | ClaimUnbatched(stockId)   | Dismiss(doctor,reason)   | Ungive / brand change / patient delete
          v                           v                          v
     CLAIMED (1)                 DISMISSED (2)               VOIDED (3)
   ledger Administer -1 on S      no stock effect,           no stock effect
   Schedule.StockId/Lot/Expiry/   dose becomes a             ActiveScheduleKey = NULL
   VaccineCost stamped            non-consuming fact
          |
          | Ungive               -> reverse claim row (Unadminister, ReversesTransactionId) -> VOIDED
          | Release (reversal of the claiming purchase/transfer/adjust, or user "un-claim") -> Unadminister -> back to PENDING
```
Illegal transitions (e.g. claim a Voided use, dismiss a Claimed use) return `Fail` — the conditional `Status` check is what makes a double-click / concurrent claim harmless.

### 3.3 Operations (signatures)

```csharp
GiveResult    Give(GiveRequest r);   // r: DoctorId, ClinicId, BrandId, ScheduleId, GivenDate, ConsumesStock, DecisionReason, Actor, AllowPending
    // consumesStock=false -> zero-delta Administer audit row (unchanged behaviour: OHF / pre-period / historical)
    // usable batch        -> Administer(-1) on the FEFO batch; GiveResult.StockId set
    // none + AllowPending -> INSERT unbatcheduses(Pending); NO ledger row, NO stock, NO BA change; GiveResult.PendingUseId set;
    //                        BrandAmount.NeedsReconcile projected true; Schedule.Lot/Expiry blank, VaccineCost null (as today for give-at-zero)
    // none + !AllowPending -> Fail("No stock batch found ...")  (controller keeps its existing ConfirmUnbatchedGive prompt)
OpResult<ClaimResult> ClaimUnbatched(ClaimRequest r);
    // r: DoctorId, ClinicId, BrandId, StockId, IReadOnlyList<long>? UseIds (null = oldest first), int? Max, ClaimSourceType, ClaimSourceId, ActorUserId
    // PLAN: batch belongs to (brand, clinic); uses Pending & same (brand, clinic); n = min(selected, batch.Quantity)  -> PARTIAL CLAIM when batch < pending
    //       batch not expired at UseDate; warn (not fail) if batch receipt date > UseDate.
    // APPLY per use: Administer(-1) on StockId, EventDate = max(UseDate, batch receipt date, clinic StockPeriodStart), UnbatchedUseId = use.Id;
    //        use -> Claimed (ClaimLedgerId, ClaimSourceType/Id); schedule.StockId, Lot, Expiry, VaccineCost, StockClinicId stamped.
    //        remaining uses stay Pending. Projection refreshed once at the end.
OpResult      DismissUnbatched(long useId, long doctorUserId, string reason);   // doctor only (CallerGuard: caller.UserId == doctor.UserId); reason mandatory; Pending -> Dismissed
OpResult      VoidUnbatchedForSchedule(long scheduleId, string reason);          // ungive / brand change / patient or doctor delete hook (ChildController:3886, DoctorController:461)
OpResult      ReleaseClaims(ClaimSourceType t, long sourceId);                   // used by reversal rule 4
int           PendingCount(long brandId, long clinicId);
```

### 3.4 Prompts and endpoints

* Create paths for Purchase (`BillController.Create`), TransferIn (`StockTransferController.Create`, evaluated at the *destination* clinic) and AdjustIncrease/opening balance return, when `PendingCount > 0` and no decision was supplied: `IsSuccess=false`, `Message="You have {n} dose(s) recorded as given with no batch. Include this batch details and deduct the units from this bill, as they have already been used?"`, `ResponseData={ RequiresClaimDecision: true, PendingCount: n }`. New optional DTO field `bool? ClaimPending` (`AdjustStockCreateDTO.ClearUnbatchedBacklog` is kept as an alias: `true` = claim, `false` = No, leave pending). Yes = `ClaimUnbatched` inside the same tx as the receipt (all or nothing). No = the document posts normally and the uses stay Pending.
* `GET /api/stock/unbatched?doctorId&clinicId&brandId&status` (list with age, patient, PA); `POST /api/stock/unbatched/claim` (claim into an existing batch any time); `POST /api/stock/unbatched/dismiss` (doctor only).
* Permissions: claim needs the same permission as the receipt document (`StockPurchaseBills` / `StockTransfer` / `StockAdjust`) or Doctor; Dismiss is Doctor only; Void is system-driven.
* Ungive uses the use's Status (section 2(e) last row); `UnadministerSync`'s `giveRow.StockId == null` heuristic and `ReconciledByTransactionId` stamping are retired (the column stays for history).

---------------------------------------------------------------------------------------------------

## 4. Case-by-case acceptance mapping

| Test | Why it fails today | Design element that fixes it | Stage |
|------|--------------------|------------------------------|-------|
| Case1 price-only bill edit | F6 reverse-all + re-post accumulates | `EditBill` match by StockId, `delta = 0` => cost-only; `OriginalQuantity` never accumulated | 4 |
| Case2 rejected give moves nothing | F7 interim commit before later validations | `RunAtomic` wrapper around `UpdateCore`, interim txs removed, staged-only stock | 6 |
| Case3 give-with-no-stock drift | F8 -1 ledger row, `StockId=null`, BA clamp | give-with-no-batch is a `UnbatchedUse(Pending)`; ledger/BA/stock untouched; `AdministerSync` shim honours it (fake ScheduleIds OK: no FK) | 5 |
| Case4 transfer delete after consumption | full quantity blindly returned | transfer pair via `CounterpartTransactionId`; dest untouched rule; reject with message | 3 |
| Case5 reversed sale closed batch | quantity restored, `IsClosed` untouched | reversal on same StockId + `SyncClosed` derivation | 2 |

---------------------------------------------------------------------------------------------------

## 5. Places where this design CONFLICTS with / changes current business behaviour (STOP-AND-CONFIRM list)

1. **Expired stock is no longer given, sold or transferred** (today FEFO ignores expiry and will issue it). A dose with only expired stock on hand becomes a pending unbatched record; the doctor must write the expired units off (`Expiry` movement). Confirm: is "expired at give date" the right test, and is month-end expiry treated as valid through its last day (`Expiry.Date >= giveDate.Date`)?
2. **Transfer delete is rejected** when any destination batch was partly used/sold (today it "succeeds" and inflates the source). Offer of "Return remaining units" instead. Transfers/sales/adjusts/bills become **soft-reversed** (`Status=1`, rows kept, list endpoints filter them out) instead of hard-deleted; anything else reading those tables directly must filter `Status`.
3. **Bill reverse / bill line removal / adjustment-increase delete are rejected when any of the batch was consumed** (today: silently drops the remainder, loses the consumed cost history, or reverses a `Math.Min` partial amount). Doctor must ungive/split or ask for a write-off.
4. **Give with no batch no longer writes a ledger `-1` row and no longer touches BrandAmount** (today it writes the row and clamps BA at 0). `NeedsReconcile` now means "pending uses exist". The dose's cost (`Schedule.VaccineCost`, P&L, invoices) is unknown until claimed; lot/expiry stay blank (as today).
5. **The existing "clear this backlog with this purchase?" flow is replaced by the claim prompt** (needs a VacDoc change for the structured `RequiresClaimDecision` response; the old message text still works). Claiming *deducts* units from the new batch (today the "clear" only stamped a flag and left the batch inflated). Legacy rows already stamped `ReconciledByTransactionId` never had stock deducted — phase B seeds them as Pending again: **user must decide** if those should instead be treated as already settled.
6. **AdjustIncrease / opening balance / transfer-in always create their own batch row** (no merge into an existing same-lot row): more Stock rows; Stock Overview already groups by lot+expiry so screens are unchanged.
7. **Lot-scoped sale/transfer/loss allocate across all open rows of a lot** (today the first row only, so a two-row lot could not be sold beyond row 1). Behavioural improvement, but the chosen batch/cost of a sale can differ.
8. **`StockController.Reconcile` and `InventoryBackfill.correct-drift` no longer overwrite BrandAmount from the ledger floored at 0**; they rebuild it from batch rows. `InventoryBackfill/run` is disabled on a non-empty ledger.
9. **Legacy data correction (phase B) changes numbers users see**: BrandAmount rows inflated by old Case-3 drift will drop to physical batch totals; `MigrationCorrection` ledger rows appear on the migration date in ledger-derived reports. Needs a physical-count sign-off per clinic before running.
10. **Ungive of a legacy dose with no ledger row and no `Schedule.StockId`** (given before the ledger existed) no longer donates a unit to "some" FEFO batch; it restores nothing and warns. Today it silently inflates a batch.
11. **Duplicate create requests (same `ClientRequestId`) now return the first response with `IsDuplicate:true`** instead of creating a second document; old clients without the id are unaffected.
12. **`SplitConsumed` (F9) currently manufactures phantom stock**; the fix removes those phantom units from batch quantity and Stock Overview for any brand that ever used split-consumed. Decision needed whether to disable the endpoint until phase B audit is run.
13. **Patient/doctor delete does not restore stock for given doses** (unchanged, and the ledger keeps the deduction — correct); pending uses of that patient are voided. Confirm that is the intended rule.
14. **Claim event date** is `max(UseDate, batch receipt date, StockPeriodStart)` so closed historical periods are not rewritten; confirm vs. using the dose date.
15. **Permissions**: Dismiss is doctor-only; claim requires the receipt document's permission. Confirm whether Managers may claim.
16. **A new BrandAmount row may be auto-created by the projection** (price copied from the doctor's other clinic, else 0 and reported as `BA_ZERO_PRICE`) — today a purchase at a clinic with no BrandAmount row silently updates nothing and drifts.

---------------------------------------------------------------------------------------------------

## 6. Open technical risks / notes for the implementer

* `RunAtomic` must join, not nest, when the controller already opened a transaction (`_db.Database.CurrentTransaction != null`) — the controllers' existing `using var tx` blocks can be deleted incrementally; both forms work during the transition.
* Two `SaveChanges()` per operation (stock+ledger flush, then projection) is intentional: `ProjectBrandAmount` reads batch sums from the DB.
* `InventoryInvariants` in tests requires per-batch ledger sum == quantity for *every* batch, so StrictMode must be on for the test host; production keeps it off until phase B (delta form still guarantees no new drift).
* Strict-mode absolute I2 uses `BatchesAt` (transitional clinic fallback) exactly like the test's `ClinicOf`, so purchase/transfer batches with null `ClinicId` are counted consistently either way.
* Ledger stays append-only: nothing in the app UPDATEs or DELETEs `inventorytransactions` after this refactor (the legacy `ReconciledByTransactionId` mutation in `UnadministerSync` is removed).
* Recommend a fifth test file after stage 6: property-style random sequences (purchase/edit/give/ungive/sale/transfer/reverse/claim) asserting `InventoryInvariants.AssertClean` after every step.


---------------------------------------------------------------------------------------------------

## AS BUILT (where the code differs from the design above)

| Design says | Code does |
|---|---|
| `Inventory:StrictMode` | Config keys are `Inventory:StrictInvariants` and `Inventory:ExcludeExpiredFromFefo` (`Program.cs`). Strict is on in tests, off by default in production until legacy drift is corrected. |
| `PostMovement`, `SyncClosed`, `RunAtomic`, partial classes under `Services/Inventory/` | One file, `Services/InventoryTransactionService.cs`. The single writers are `ApplyDelta` (batch quantity, `OriginalQuantity`, `IsClosed`), `ProjectTouched` (BrandAmount) and `Log` (ledger). Atomicity comes from the controller's transaction plus `SaveAndAssert`; `ScheduleController.InOneTransaction` wraps give/ungive/bulk give. |
| `[Obsolete]` shims and a unified `Give(GiveRequest)` | The existing method names (`AdministerSync`, `UnadministerSync`, ...) remain the public API. |
| Locking with `SELECT ... FOR UPDATE`, `Database.IsMySql()` guards | Not built. Concurrency relies on the `RowVersion` tokens (Stock, BrandAmount, UnbatchedUse) and the unique indexes. MySQL row-lock behaviour is unproven (tests run on SQLite). |
| `InventoryAuditService` under `Services/Inventory/`, `GET /api/InventoryAudit/report`, CSV export | `Services/InventoryAuditService.cs`, `GET /api/InventoryAudit`. No CSV export. |
| `WriteOff(stockId, qty, reason)` at `POST /api/stock/writeoff` | `POST /api/adjuststock/writeoff` (`AdjustStockController.WriteOff`) with `Wastage` / `Expiry` ledger types. Deleting the adjustment restores the same batch. No Stock Overview button yet (needs a mock). |
| Pending-dose routes under `/api/stock/unbatched*` | `UnbatchedUseController`: `GET /api/UnbatchedUse`, `GET /api/UnbatchedUse/count`, `POST /api/UnbatchedUse/claim`, `POST /api/UnbatchedUse/{id}/dismiss` (doctor session required for the last two). |
| Idempotency keys unique on (doctor, request id) | Unique on (doctor, endpoint, request id). |
| Soft-reversed documents (`Status = 1`) | Deferred. Documents keep their current delete behaviour; reversal rows in the ledger still reference the original movement. |
| Phase B data corrections (legacy drift, legacy claimed doses) | Not part of this work. The audit reports them; corrections need a physical count per clinic. |
| Nothing in the design | Added after the red-team review: a runtime single-writer check in `Context.SaveChanges` (`Inventory:EnforceSingleWriter`), a refusal of bill reversal while any unit of the bill is used, `HasLiveGive` so an ungive works even if inventory was switched off after the give, per-doctor scoping of sale deletes, and `OriginalQuantity >= Quantity` kept by `ApplyDelta`. |
| Unique index on `brandamounts(brand, doctor, clinic)` | In the EF model; in the SQL file it is commented out until the duplicate check returns zero rows. |
| `Stock.ClinicId` always set | Set on every batch the service creates. Existing rows are backfilled by step 4 of `stock_integrity_phase_a.sql`. The `Bill.ClinicId` fallback in queries is kept for rows not yet backfilled. |
