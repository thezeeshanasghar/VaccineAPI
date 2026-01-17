-- =====================================================
-- Alternative SQL Script - Check Table Name First
-- =====================================================
-- If the above script fails, the table might be named differently
-- Run this query first to find the correct table name:
-- =====================================================

-- Find the PaAccess table name (run this first)
-- SHOW TABLES LIKE '%access%';
-- or
-- SHOW TABLES LIKE '%pa%';

-- =====================================================
-- Common Table Name Variations:
-- =====================================================

-- Option 1: If table is named 'paaccess' (lowercase, no underscore)
ALTER TABLE `paaccess`
ADD COLUMN `IsOnline` tinyint(1) NOT NULL DEFAULT 0
AFTER `ClinicId`;

-- Option 2: If table is named 'PaAccess' (PascalCase)
-- ALTER TABLE `PaAccess`
-- ADD COLUMN `IsOnline` tinyint(1) NOT NULL DEFAULT 0
-- AFTER `ClinicId`;

-- Option 3: If table is named 'pa_access' (snake_case)
-- ALTER TABLE `pa_access`
-- ADD COLUMN `IsOnline` tinyint(1) NOT NULL DEFAULT 0
-- AFTER `ClinicId`;

-- Option 4: If table is named 'paaccesses' (plural)
-- ALTER TABLE `paaccesses`
-- ADD COLUMN `IsOnline` tinyint(1) NOT NULL DEFAULT 0
-- AFTER `ClinicId`;

-- =====================================================
-- After adding the column, set first clinic as online for each PA
-- Replace 'paaccess' with your actual table name
-- =====================================================

UPDATE `paaccess` pa1
SET `IsOnline` = 1
WHERE pa1.Id = (
    SELECT MIN(pa2.Id)
    FROM `paaccess` pa2
    WHERE pa2.PersonalAssistantId = pa1.PersonalAssistantId
);

-- =====================================================
-- MySQL 8.0+ Alternative (if subquery fails)
-- =====================================================
-- If the above UPDATE fails due to MySQL version, use this:

-- UPDATE `paaccess` pa1
-- INNER JOIN (
--     SELECT PersonalAssistantId, MIN(Id) as FirstId
--     FROM `paaccess`
--     GROUP BY PersonalAssistantId
-- ) pa2 ON pa1.PersonalAssistantId = pa2.PersonalAssistantId AND pa1.Id = pa2.FirstId
-- SET pa1.IsOnline = 1;
