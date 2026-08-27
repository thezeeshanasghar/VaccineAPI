-- Manager: Give Vaccine, Invoice Edit, PA-Assignment-Create — new columns
-- Run manually on staging/prod (no EF migrations in this project).
-- Companion to manager_tables.sql / papermissions_manager_level_columns.sql.
--
-- Adds:
--   1. managerpermissions: 3 new flags (AssignPaToPatient, CanGiveVaccine, CanEditInvoice)
--   2. schedules: GivenByManagerId — attribution ONLY, never a cash-math join key.
--      PaCashHandoverController.ComputeCashInHand/BatchCashInHand must keep joining
--      on GivenByPaId/PaymentCollectorPaId only — GivenByManagerId and PersonalAssistant.Id
--      are separate auto-increment spaces that can collide, so it must never be used
--      as a PA-lookup key anywhere.
--   3. invoicesubmissions: EditedByManagerId — attribution for the 1-edit-cap path
--   4. invoiceamendments: ManagerId (nullable) + PaId made nullable, since a
--      Manager-driven edit has no PA acting on it
--   5. paactivitylogs: ManagerId (nullable) + PaId made nullable, same reasoning —
--      a Manager-initiated ungive-after-payment logs ManagerId instead of PaId
--   6. paassignments: CreatedByManagerId — set only when a Manager (not the doctor)
--      created the assignment, so the reconciliation "AwaitingInvoice" row can show
--      "Manager/(PA Name)" instead of assuming "Doctor/(PA Name)"

ALTER TABLE `managerpermissions`
  ADD COLUMN `AssignPaToPatient` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN `CanGiveVaccine`    TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN `CanEditInvoice`    TINYINT(1) NOT NULL DEFAULT 0;

ALTER TABLE `schedules`
  ADD COLUMN `GivenByManagerId` BIGINT NULL,
  ADD INDEX `IX_schedules_GivenByManagerId` (`GivenByManagerId`);

ALTER TABLE `invoicesubmissions`
  ADD COLUMN `EditedByManagerId` BIGINT NULL;

ALTER TABLE `invoiceamendments`
  ADD COLUMN `ManagerId` BIGINT NULL,
  MODIFY COLUMN `PaId` BIGINT NULL;

ALTER TABLE `paactivitylogs`
  ADD COLUMN `ManagerId` BIGINT NULL,
  MODIFY COLUMN `PaId` BIGINT NULL;

ALTER TABLE `paassignments`
  ADD COLUMN `CreatedByManagerId` BIGINT NULL;

-- Rollback:
-- ALTER TABLE `managerpermissions` DROP COLUMN `AssignPaToPatient`, DROP COLUMN `CanGiveVaccine`, DROP COLUMN `CanEditInvoice`;
-- ALTER TABLE `schedules` DROP INDEX `IX_schedules_GivenByManagerId`, DROP COLUMN `GivenByManagerId`;
-- ALTER TABLE `invoicesubmissions` DROP COLUMN `EditedByManagerId`;
-- ALTER TABLE `invoiceamendments` DROP COLUMN `ManagerId`, MODIFY COLUMN `PaId` BIGINT NOT NULL;
-- ALTER TABLE `paactivitylogs` DROP COLUMN `ManagerId`, MODIFY COLUMN `PaId` BIGINT NOT NULL;
-- ALTER TABLE `paassignments` DROP COLUMN `CreatedByManagerId`;
