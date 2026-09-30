-- =====================================================================================================
-- STOCK INTEGRITY REFACTOR — PHASE A: additive schema.   MySQL 8, all table names lowercase.
--
-- RUN THIS BEFORE DEPLOYING THE NEW API BUILD. VaccineAPI deploys to production on every push to
-- `staging`; the new build reads and writes the columns/tables below and will fail every give until
-- they exist. The OLD build keeps working on this schema (everything here is additive), so running
-- it first is always safe.
--
-- 0) BACKUP FIRST (example):
--      mysqldump --single-transaction vaccineapi inventorytransactions stocks brandamounts bills > stock_before_phase_a.sql
-- =====================================================================================================

-- 1) Ledger: a reversal row points at the movement it undoes; a movement can be reversed at most once.
--    (Written so the whole file can be re-run safely after a partial failure.)
SET @has_col := (SELECT COUNT(*) FROM information_schema.COLUMNS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'inventorytransactions' AND COLUMN_NAME = 'ReversesTransactionId');
SET @sql := IF(@has_col = 0, 'ALTER TABLE inventorytransactions ADD COLUMN ReversesTransactionId BIGINT NULL', 'SELECT ''ReversesTransactionId already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @has_idx := (SELECT COUNT(*) FROM information_schema.STATISTICS
                  WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'inventorytransactions' AND INDEX_NAME = 'ux_inventorytransactions_reverses');
SET @sql := IF(@has_idx = 0, 'CREATE UNIQUE INDEX ux_inventorytransactions_reverses ON inventorytransactions (ReversesTransactionId)', 'SELECT ''index already exists''');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 2) Pending (unbatched) doses: given while no batch existed; NO stock effect until claimed.
CREATE TABLE IF NOT EXISTS unbatcheduses (
    Id                BIGINT       NOT NULL AUTO_INCREMENT,
    ScheduleId        BIGINT       NOT NULL,
    ActiveScheduleKey BIGINT       NULL,
    DoctorId          BIGINT       NOT NULL,
    ClinicId          BIGINT       NOT NULL,
    BrandId           BIGINT       NOT NULL,
    GivenDate         DATETIME(6)  NOT NULL,
    GivenByPaId       BIGINT       NULL,
    DecisionReason    LONGTEXT     NULL,
    Status            TINYINT UNSIGNED NOT NULL DEFAULT 0,
    ClaimStockId      INT          NULL,
    DismissReason     LONGTEXT     NULL,
    CreatedAt         DATETIME(6)  NOT NULL,
    ResolvedAt        DATETIME(6)  NULL,
    RowVersion        INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (Id),
    UNIQUE KEY ux_unbatcheduses_activekey (ActiveScheduleKey),
    KEY ix_unbatcheduses_lookup (DoctorId, ClinicId, BrandId, Status)
) ENGINE=InnoDB;

-- 3) Duplicate-request protection for Bill / StockTransfer / DirectSale / AdjustStock creates.
CREATE TABLE IF NOT EXISTS idempotencykeys (
    Id              BIGINT       NOT NULL AUTO_INCREMENT,
    DoctorId        BIGINT       NOT NULL,
    ClientRequestId VARCHAR(100) NOT NULL,
    Endpoint        VARCHAR(60)  NOT NULL,
    ResponseJson    LONGTEXT     NOT NULL,
    CreatedAt       DATETIME(6)  NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY ux_idempotencykeys_request (DoctorId, Endpoint, ClientRequestId)
) ENGINE=InnoDB;

-- 4) Batches created by purchases/transfers used to leave Stock.ClinicId empty (the clinic was inferred
--    from the bill). The new code always sets it. Backfilling it from the bill is exact and reversible.
--    (Review the row count first:  SELECT COUNT(*) FROM stocks WHERE ClinicId IS NULL AND BillId IS NOT NULL;)
UPDATE stocks s JOIN bills b ON b.Id = s.BillId
   SET s.ClinicId = b.ClinicId
 WHERE s.ClinicId IS NULL;

-- 5) One BrandAmount row per (brand, doctor, clinic). This MUST return zero rows before you create the index.
SELECT BrandId, DoctorId, ClinicId, COUNT(*) AS copies FROM brandamounts GROUP BY BrandId, DoctorId, ClinicId HAVING COUNT(*) > 1;
--   If it returns rows, merge those duplicates by hand first (keep one row per key, its Quantity is rebuilt by
--   POST /api/stock/reconcile afterwards). Then create the index (the new build assumes it exists; without it a
--   concurrent double-create could leave two counters for one brand and clinic):
--   CREATE UNIQUE INDEX ux_brandamounts_brand_doctor_clinic ON brandamounts (BrandId, DoctorId, ClinicId);

-- =====================================================================================================
-- NOT IN THIS FILE (by design): any correction of existing quantities. Existing drift is REPORTED by
-- GET /api/InventoryAudit and by docs/stock/reconciliation_report.sql, and is corrected only through
-- explicit, reviewed, per-clinic operations after a physical count. Nothing here rewrites stock.
-- =====================================================================================================
