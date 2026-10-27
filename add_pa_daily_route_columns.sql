-- PA Daily Route, build step 1 (spec v3). No behaviour change: all columns nullable or defaulted.
-- RUN ON STAGING, THEN PROD, BEFORE DEPLOYING THE API. API before SQL makes every paassignments query fail.
-- Never drop these columns on rollback; just hide the menu.

ALTER TABLE `paassignments`
  ADD COLUMN `IsScheduledVisit` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN `VisitOrder` INT NULL,
  ADD COLUMN `Area` VARCHAR(80) NULL,
  ADD COLUMN `WindowStart` TIME NULL,
  ADD COLUMN `WindowMinutes` SMALLINT NOT NULL DEFAULT 60,
  ADD COLUMN `VisitCompletedAt` DATETIME NULL;

CREATE INDEX `IX_paassignments_DoctorId_TargetDate` ON `paassignments` (`DoctorId`, `TargetDate`);

-- Verify (expect 6 rows, then existing rows all IsScheduledVisit = 0):
-- SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_NAME='paassignments' AND COLUMN_NAME IN ('IsScheduledVisit','VisitOrder','Area','WindowStart','WindowMinutes','VisitCompletedAt');
-- SELECT COUNT(*) FROM paassignments WHERE IsScheduledVisit <> 0;
