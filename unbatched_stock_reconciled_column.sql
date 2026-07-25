-- Adds the ReconciledByTransactionId column used by the unbatched-stock backlog-clearing
-- prompt (purchase-time). Nullable, no default needed — existing rows stay NULL (outstanding).
-- Safe to run once; re-running is a no-op error only if the column already exists.

ALTER TABLE InventoryTransactions
  ADD COLUMN ReconciledByTransactionId BIGINT NULL;
