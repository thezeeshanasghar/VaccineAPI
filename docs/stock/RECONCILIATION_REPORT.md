# Reconciliation report

## What exists
- `InventoryAuditService` / `GET /api/InventoryAudit?doctorId=&clinicId=`: read-only report with, per clinic / brand / batch, the stored stock, the ledger-derived stock, the `BrandAmount` value and the differences, plus these finding classes: `NEGATIVE_BATCH`, `NEGATIVE_BRANDAMOUNT`, `ORPHAN_BATCH`, `CLOSED_WITH_STOCK`, `OPEN_ZERO`, `STOCK_WITHOUT_LEDGER`, `LEDGER_WITHOUT_STOCK`, `LEGACY_UNBATCHED_LEDGER`, `BATCH_LEDGER_MISMATCH`, `BRAND_COUNTER_MISMATCH`, `BRAND_LEDGER_MISMATCH`, `ORIGINAL_QTY_MISMATCH`, `REVERSAL_ORPHAN`, `REVERSAL_QUANTITY_MISMATCH`, `TRANSFER_UNPAIRED`, `MISSING_BRANDAMOUNT`, `PENDING_DOSES`.
- `docs/stock/reconciliation_report.sql`: the same checks as plain SELECTs, runnable on production today, before the new build is deployed.
- `POST /api/stock/reconcile` and `POST /api/InventoryBackfill/correct-drift` rebuild `BrandAmount` from the batches. They change nothing else and write a `BA_REPROJECTED` note to the ledger for every counter they change. The old "rebuild the ledger" endpoint is retired.

## What was NOT done
The production database was not read from this environment (no MySQL client or Docker here, and no read access was requested). This report therefore contains no production numbers. To produce them, run `docs/stock/reconciliation_report.sql` against production (read-only) or call `GET /api/InventoryAudit` after deployment, and review the differences per clinic.

## How existing drift will be handled
- Nothing is corrected automatically.
- `LEGACY_UNBATCHED_LEDGER` rows (old gives recorded with no batch) and drift on legacy batches are reported. They should be corrected only after a physical count per clinic, as explicit ledger adjustments, not by rewriting quantities.
- A counter that disagrees with its batches is rebuilt by `reconcile`, and the change is logged.
- Legacy claimed-by-adjustment doses (old `ReconciledByTransactionId` marks) never deducted stock; the audit shows the resulting surplus so the doctor can decide.
