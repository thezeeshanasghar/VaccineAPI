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
