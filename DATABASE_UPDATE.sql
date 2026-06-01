-- =====================================================
-- SQL Script to Add IsOnline Column to PaAccess Table
-- =====================================================
-- This script adds the IsOnline column to separate
-- online status between Doctors and Personal Assistants
-- =====================================================

-- Step 1: Add IsOnline column to PaAccess table
-- Default value is false (0) for existing records
ALTER TABLE `paaccess`
ADD COLUMN `IsOnline` tinyint(1) NOT NULL DEFAULT 0
AFTER `ClinicId`;

-- Step 2: Set the first clinic for each PA as online
-- This matches the backend logic where the first clinic is automatically set to online
-- Using JOIN to avoid MySQL error #1093 (can't update table used in FROM clause)
UPDATE `paaccess` pa1
INNER JOIN (
    SELECT PersonalAssistantId, MIN(Id) as FirstId
    FROM `paaccess`
    GROUP BY PersonalAssistantId
) pa2 ON pa1.PersonalAssistantId = pa2.PersonalAssistantId AND pa1.Id = pa2.FirstId
SET pa1.IsOnline = 1;

-- =====================================================
-- Verification Queries (Optional - Run to verify)
-- =====================================================

-- Check the table structure
-- DESCRIBE `paaccess`;

-- Check how many records have IsOnline = 1 (should be one per PA)
-- SELECT PersonalAssistantId, COUNT(*) as OnlineClinics
-- FROM `paaccess`
-- WHERE IsOnline = 1
-- GROUP BY PersonalAssistantId;

-- Check all PaAccess records with their IsOnline status
-- SELECT Id, PersonalAssistantId, ClinicId, IsOnline
-- FROM `paaccess`
-- ORDER BY PersonalAssistantId, Id;

-- =====================================================
-- Rollback Script (If needed)
-- =====================================================
-- To rollback this change, run:
-- ALTER TABLE `paaccess` DROP COLUMN `IsOnline`;

-- =====================================================
-- Add AllowFinancial Column to Doctors Table
-- =====================================================
ALTER TABLE `doctors`
ADD COLUMN `AllowFinancial` tinyint(1) NOT NULL DEFAULT 0;

-- Rollback: ALTER TABLE `doctors` DROP COLUMN `AllowFinancial`;

-- =====================================================
-- Add DoneAt Column to Schedules Table
-- =====================================================
-- Tracks the exact UTC timestamp when a vaccine was marked as given.
-- Used so PA users can only unfill vaccines they administered today.
-- =====================================================
ALTER TABLE `schedules`
ADD COLUMN `DoneAt` datetime NULL;

-- Rollback: ALTER TABLE `schedules` DROP COLUMN `DoneAt`;

-- =====================================================
-- Add IsPAApprove Column to Schedules Table
-- =====================================================
ALTER TABLE `schedules`
ADD COLUMN `IsPAApprove` tinyint(1) NOT NULL DEFAULT 0;

-- Rollback: ALTER TABLE `schedules` DROP COLUMN `IsPAApprove`;

-- =====================================================
-- Create expenses Table (Financial Module — Phase 1)
-- =====================================================
CREATE TABLE `expenses` (
  `Id`               BIGINT        NOT NULL AUTO_INCREMENT,
  `DoctorId`         BIGINT        NOT NULL,
  `ClinicId`         BIGINT        NULL,
  `IsShared`         TINYINT(1)    NOT NULL DEFAULT 0,
  `ExpenseDate`      DATE          NOT NULL,
  `Amount`           DECIMAL(12,2) NOT NULL,
  `Description`      VARCHAR(500)  NOT NULL,
  `Category`         VARCHAR(50)   NOT NULL,
  `ExpenseType`      VARCHAR(20)   NOT NULL DEFAULT 'Recurring',
  `PaymentMode`      VARCHAR(30)   NOT NULL DEFAULT 'Cash',
  `Notes`            TEXT          NULL,
  `AssetName`        VARCHAR(200)  NULL,
  `ExpectedLifeYrs`  DECIMAL(4,1)  NULL,
  `WarrantyExpiry`   DATE          NULL,
  `ReceiptImagePath` VARCHAR(500)  NULL,
  `WarrantyImagePath` VARCHAR(500) NULL,
  `CreatedAt`        DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`Id`),
  INDEX `IX_expenses_DoctorId` (`DoctorId`),
  INDEX `IX_expenses_Date`     (`ExpenseDate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Rollback: DROP TABLE `expenses`;

-- PA Assignments
CREATE TABLE `paassignment` (
  `Id`                  BIGINT        NOT NULL AUTO_INCREMENT,
  `DoctorId`            BIGINT        NOT NULL,
  `ClinicId`            BIGINT        NULL,
  `PersonalAssistantId` BIGINT        NOT NULL,
  `ChildId`             BIGINT        NOT NULL,
  `AssignedAt`          DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `IsCompleted`         TINYINT(1)    NOT NULL DEFAULT 0,
  `CompletedAt`         DATETIME      NULL,
  `Notes`               VARCHAR(500)  NULL,
  PRIMARY KEY (`Id`),
  INDEX `IX_paassignment_PA`    (`PersonalAssistantId`),
  INDEX `IX_paassignment_Child` (`ChildId`),
  INDEX `IX_paassignment_Doctor`(`DoctorId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Rollback: DROP TABLE `paassignment`;

-- =====================================================
-- Add Cancel + Reassign columns to paassignment
-- =====================================================
ALTER TABLE `paassignment`
  ADD COLUMN `IsCancelled`                tinyint(1)   NOT NULL DEFAULT 0,
  ADD COLUMN `CancelledAt`                datetime     NULL,
  ADD COLUMN `CancelReason`               varchar(500) NULL,
  ADD COLUMN `ReassignedFromAssignmentId` bigint       NULL;

-- Rollback:
-- ALTER TABLE `paassignment`
--   DROP COLUMN `IsCancelled`,
--   DROP COLUMN `CancelledAt`,
--   DROP COLUMN `CancelReason`,
--   DROP COLUMN `ReassignedFromAssignmentId`;

-- =====================================================
-- Add Payment Verification Audit Trail to Schedules
-- Tracks who verified a payment and when
-- =====================================================
ALTER TABLE `schedules`
  ADD COLUMN `PaymentApprovedAt`          DATETIME NULL,
  ADD COLUMN `PaymentApprovedByDoctorId`  BIGINT   NULL;

-- Rollback:
-- ALTER TABLE `schedules` DROP COLUMN `PaymentApprovedAt`;
-- ALTER TABLE `schedules` DROP COLUMN `PaymentApprovedByDoctorId`;

-- =====================================================
-- Add Payment Collection Columns to Schedules Table
-- PaymentMode, OnlineService, IsPaymentCollected,
-- IsPaymentApproved, PaymentCollectorPaId,
-- GivenByPaId, SkippedByPaId, SkippedAt,
-- and audit counters GiveCount/UngiveCount/SkipCount/UnskipCount
-- =====================================================
ALTER TABLE `schedules`
  ADD COLUMN `PaymentMode`           VARCHAR(30)  NOT NULL DEFAULT 'Cash',
  ADD COLUMN `OnlineService`         VARCHAR(100) NULL,
  ADD COLUMN `IsPaymentApproved`     TINYINT(1)   NOT NULL DEFAULT 0,
  ADD COLUMN `IsPaymentCollected`    TINYINT(1)   NOT NULL DEFAULT 0,
  ADD COLUMN `PaymentCollectorPaId`  BIGINT       NULL,
  ADD COLUMN `GivenByPaId`           BIGINT       NULL,
  ADD COLUMN `SkippedByPaId`         BIGINT       NULL,
  ADD COLUMN `SkippedAt`             DATETIME     NULL,
  ADD COLUMN `GiveCount`             INT          NOT NULL DEFAULT 0,
  ADD COLUMN `UngiveCount`           INT          NOT NULL DEFAULT 0,
  ADD COLUMN `SkipCount`             INT          NOT NULL DEFAULT 0,
  ADD COLUMN `UnskipCount`           INT          NOT NULL DEFAULT 0;

-- Rollback:
-- ALTER TABLE `schedules`
--   DROP COLUMN `PaymentMode`,
--   DROP COLUMN `OnlineService`,
--   DROP COLUMN `IsPaymentApproved`,
--   DROP COLUMN `IsPaymentCollected`,
--   DROP COLUMN `PaymentCollectorPaId`,
--   DROP COLUMN `GivenByPaId`,
--   DROP COLUMN `SkippedByPaId`,
--   DROP COLUMN `SkippedAt`,
--   DROP COLUMN `GiveCount`,
--   DROP COLUMN `UngiveCount`,
--   DROP COLUMN `SkipCount`,
--   DROP COLUMN `UnskipCount`;

-- Store the exact invoice total (vaccines + consultation fee) so the payment popup
-- always shows the same number the PDF shows.
ALTER TABLE `invoicesubmissions`
  ADD COLUMN `TotalAmount` decimal(18,2) NOT NULL DEFAULT 0;

-- PA Assignment cancel/reassign support
ALTER TABLE `paassignment`
  ADD COLUMN `IsCancelled`                tinyint(1)   NOT NULL DEFAULT 0,
  ADD COLUMN `CancelledAt`                datetime     NULL,
  ADD COLUMN `CancelReason`               varchar(500) NULL,
  ADD COLUMN `ReassignedFromAssignmentId` bigint       NULL;

-- PA Assignment Flow A: auto-created when PA gives vaccine
ALTER TABLE `paassignment`
  ADD COLUMN `IsAutoCreated` tinyint(1) NOT NULL DEFAULT 0;

-- =====================================================
-- Refactor Stock Permissions in papermissions table
-- Replace 15 granular flags with 7 card-level flags
-- =====================================================
ALTER TABLE `papermissions`
  DROP COLUMN `ViewStock`,
  DROP COLUMN `UpdateSalePrice`,
  DROP COLUMN `AddBrand`,
  DROP COLUMN `EditBrand`,
  DROP COLUMN `AddPurchaseBill`,
  DROP COLUMN `EditPurchaseBill`,
  DROP COLUMN `DeletePurchaseBill`,
  DROP COLUMN `ApprovePurchaseBill`,
  DROP COLUMN `AdjustStock`,
  DROP COLUMN `ViewAdjustHistory`,
  DROP COLUMN `TransferStock`,
  DROP COLUMN `ViewTransferHistory`,
  DROP COLUMN `AddDirectSale`,
  DROP COLUMN `ViewDirectSaleHistory`,
  DROP COLUMN `DownloadStockReport`,
  ADD COLUMN `StockSuppliers`     tinyint(1) NOT NULL DEFAULT 0,
  ADD COLUMN `StockPurchaseBills` tinyint(1) NOT NULL DEFAULT 0,
  ADD COLUMN `StockOverview`      tinyint(1) NOT NULL DEFAULT 0,
  ADD COLUMN `StockAdjust`        tinyint(1) NOT NULL DEFAULT 0,
  ADD COLUMN `StockTransfer`      tinyint(1) NOT NULL DEFAULT 0,
  ADD COLUMN `StockDirectSale`    tinyint(1) NOT NULL DEFAULT 0,
  ADD COLUMN `StockReports`       tinyint(1) NOT NULL DEFAULT 0;

-- Rollback:
-- ALTER TABLE `papermissions`
--   DROP COLUMN `StockSuppliers`,
--   DROP COLUMN `StockPurchaseBills`,
--   DROP COLUMN `StockOverview`,
--   DROP COLUMN `StockAdjust`,
--   DROP COLUMN `StockTransfer`,
--   DROP COLUMN `StockDirectSale`,
--   DROP COLUMN `StockReports`,
--   ADD COLUMN `ViewStock`            tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `UpdateSalePrice`      tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `AddBrand`             tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `EditBrand`            tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `AddPurchaseBill`      tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `EditPurchaseBill`     tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `DeletePurchaseBill`   tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `ApprovePurchaseBill`  tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `AdjustStock`          tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `ViewAdjustHistory`    tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `TransferStock`        tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `ViewTransferHistory`  tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `AddDirectSale`        tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `ViewDirectSaleHistory` tinyint(1) NOT NULL DEFAULT 0,
--   ADD COLUMN `DownloadStockReport`  tinyint(1) NOT NULL DEFAULT 0;
