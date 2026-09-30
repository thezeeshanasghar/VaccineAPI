# Stock Integrity Contract

Authoritative record: the ledger (`inventorytransactions`, append-only). Batch state: `Stock.Quantity`, kept equal to the sum of that batch's ledger rows. `BrandAmount.Quantity` is a projection: the sum of the batch quantities of that brand at that clinic.

Rules that hold for every transaction type:
- A runtime check at `SaveChanges` refuses (or, until enabled in production, logs) any change to a batch's quantity, purchased quantity, closed flag or clinic, to the `BrandAmount` counter, or to a ledger row that the inventory service did not itself mark.
- `OriginalQuantity` is never lower than the units on the batch and never negative.
- The only writer is `InventoryTransactionService`.
- All checks run before anything is changed; a refused or failed request leaves stock, counter and ledger exactly as they were.
- Each stock movement writes exactly one ledger row on the specific batch it touched.
- A batch never goes below zero. `IsClosed` is true exactly when `Quantity == 0`.
- A reversal posts the opposite of the original row on the same batch, references it, and can happen once.
- When downstream use exists, the reversal is refused with an explicit message. It never recreates units that were consumed.

| Transaction | Authoritative stock (batch) | Ledger | BrandAmount | On failure | On reversal | If downstream transactions already exist |
|---|---|---|---|---|---|---|
| Purchase (bill create) | New batch, `+q`, `OriginalQuantity = q`, clinic set | `Purchase +q` on the batch | Recomputed from batches | Nothing saved | Bill reverse removes the batch and posts `BillReverse` referencing the purchase row | **Refused** if any unit of the bill was used or moved: the message says to use "Split consumed" first, then reverse the remainder |
| Bill edit | Price only: cost updated, no movement. Quantity: one signed movement on the same batch, `OriginalQuantity` set to the new quantity. Lot/expiry: label change. | `BillEdit` row (delta 0 for cost or label changes) | Recomputed | Nothing saved | n/a | Lowering below the consumed units is refused; removing or re-branding a line with consumed units is refused |
| Adjust increase | New batch `+q` (never merged into a bill's batch) | `AdjustIncrease +q` | Recomputed | Nothing saved | Delete posts `AdjustReverse -q` on that batch | Refused if any of those units were used |
| Adjust loss | Draws from the lot's batches, earliest expiry first | One `AdjustLoss` row per batch drawn | Recomputed | Nothing saved | Delete restores each batch | Always possible (returns units) |
| Opening balance | New batch `+q`, clinic set. Needs a stock-adjust permission; repeats with the same `ClientRequestId` post once | `OpeningBalance +q` | Recomputed | Nothing saved | n/a | n/a |
| Write-off (wastage / expired) | One specific batch `-q` | `Wastage` or `Expiry` `-q` on that batch | Recomputed | Nothing saved | Deleting the adjustment restores the same batch | Always possible (returns units) |
| Transfer out | Source batch `-q` | `TransferOut -q` | Recomputed | Nothing saved | Delete restores the same source batch | See transfer in |
| Transfer in | New destination batch `+q` | `TransferIn +q` | Recomputed | Nothing saved | Delete removes the destination batch | Delete is refused if any of the received units were used or moved; the source never receives units that no longer exist |
| Direct sale | Draws from the lot's batches, earliest expiry first, one sale row per batch | One `DirectSale` row per batch | Recomputed | Nothing saved | Delete restores each batch it came from; the batch is usable again | Always possible (returns units) |
| Give (single or bulk) | FEFO batch `-1` | `Administer -1` on that batch, dated at the give date | Recomputed | The whole request is rolled back | Ungive posts `+1` on the same batch and references the give | n/a |
| Give with no batch | No change | No row | No change | n/a | Ungive voids the pending dose | The dose stays pending until claimed or dismissed |
| Claim a pending dose | Batch `-1` per dose | `Administer -1` per dose on the chosen batch | Recomputed | All doses or none | Ungive of the dose restores the batch | Refused if the batch holds fewer units than doses selected |
| Dismiss a pending dose (doctor only) | No change | Zero-delta note with the reason | No change | n/a | n/a | n/a |
| Reconcile / correct-drift | No change | Zero-delta note `BA_REPROJECTED` per counter changed | Rebuilt from batches | Nothing saved | n/a | n/a |
