# Root cause

## The defect
The same quantity lived in three places and every transaction updated all three by hand:

| Store | Used for |
|---|---|
| `Stock.Quantity` (per batch) | FEFO and batch pickers |
| `BrandAmount.Quantity` (per brand and clinic) | the Overview total and the give gate |
| `InventoryTransaction` (ledger) | history and reports |

About fifteen code paths each wrote two or three of them, none checked that they still agreed, and the repair tools (`reconcile`, `correct-drift`) overwrote only the counter. Reversals were six separate hand-written routines that found the batch by lot text instead of by identity. Closed status, `OriginalQuantity` and the clinic were derived differently in each path.

## How that produced the five reported failures
| Case | Mechanism |
|---|---|
| 1 Bill edit | Edit = close every batch, then re-add. The re-add found the same closed row and did `OriginalQuantity += q`, and reset consumed units back onto the shelf. |
| 2 Failed give | The give committed stock in its own transaction, then later checks could still reject the request. Each retry deducted again. |
| 3 Give with no stock | The counter was clamped at 0 and a `-1` ledger row with no batch was written. A later purchase left counter and batch at N while the ledger said N-k. The "clear backlog" prompt only marked ledger rows. |
| 4 Transfer delete | The source got the full quantity back by lot lookup, and destination rows were zeroed by hand, ignoring units already used there. |
| 5 Sale delete | The reversal added quantity back but nobody reset the closed flag, so FEFO (which skips closed rows) could not see the units. |

None of these is a one-off bug. Each is the same absence: no single writer, no invariant checked at write time.

## What was changed (architecture, not patches)
1. `InventoryTransactionService` is the only code that writes batch quantity, `OriginalQuantity`, `IsClosed`, the `BrandAmount` counter and the ledger (`ApplyDelta`, `ProjectTouched`, `Log`). Two checks enforce it: a source scan in the test suite, and a runtime check in `Context.SaveChanges` that refuses any such write the service did not mark (`Inventory:EnforceSingleWriter`, on in tests).
2. `IsClosed` is derived from `Quantity == 0` in one place. `BrandAmount` is a projection of the batch rows, rebuilt after every operation; any correction it makes is written to the ledger.
3. Every reversal posts the exact opposite of the original ledger row on the same batch and references it (`ReversesTransactionId`, unique). A reversal that would recreate consumed stock is refused.
4. A give with no batch is recorded as a pending dose (`UnbatchedUse`) with no stock effect. The doctor later allocates it to a real batch through a formal claim operation (batch reduced, ledger row written) or dismisses it with a reason.
5. Give, ungive and bulk give run as one database transaction. A refused request changes nothing.
6. Duplicate requests carrying the same `ClientRequestId` post once.
7. The ledger wipe endpoint is retired; `reconcile` and `correct-drift` rebuild the projection from batches and log what they changed; deletes that would cascade over live stock are refused; a read-only audit report exposes every discrepancy class.
