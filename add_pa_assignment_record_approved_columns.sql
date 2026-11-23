-- PA record-only approval (HISTORICAL_ENTRY_PA_OWNERSHIP_SPEC.md, Approve design 2026-11-23).
-- Doctor attests a no-money (no invoice, no brand, no fee, OHF) PA entry. Separate from the
-- cash-confirm columns; the assignment is NOT cancelled. Run on staging, then prod.
ALTER TABLE `paassignments`
  ADD COLUMN `RecordApprovedAt` DATETIME NULL,
  ADD COLUMN `RecordApprovedBy` BIGINT NULL;
