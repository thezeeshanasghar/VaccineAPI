-- PA_CANCEL_REDESIGN_SPEC.md §2 (2026-11-26).
-- Run on prod BEFORE deploying the API that reads these columns (EF selects every mapped column,
-- so the API would fail on paassignments queries if they are missing).
-- Nullable, no default, no backfill: existing rows keep NULL and never show in the new panel.

-- 1. Backup (standard pattern; structure + rows).
CREATE TABLE paassignments_backup_cancelledby_20261126 AS SELECT * FROM paassignments;
SELECT (SELECT COUNT(*) FROM paassignments) AS live_rows,
       (SELECT COUNT(*) FROM paassignments_backup_cancelledby_20261126) AS backup_rows;

-- 2. Add the columns.
ALTER TABLE paassignments
  ADD COLUMN CancelledBy VARCHAR(16) NULL,       -- 'PA' | 'DOCTOR' | 'MANAGER' | 'REASSIGN' | 'TAKEBACK'
  ADD COLUMN FollowUpClosedAt DATETIME(6) NULL;  -- doctor tapped "No visit needed"

-- 3. Verify.
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE FROM information_schema.COLUMNS
 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'paassignments'
   AND COLUMN_NAME IN ('CancelledBy', 'FollowUpClosedAt');
