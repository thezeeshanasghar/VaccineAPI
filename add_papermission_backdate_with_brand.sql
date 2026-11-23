-- PA permission "Back-date with brand" (HISTORICAL_ENTRY_PA_OWNERSHIP_SPEC.md).
-- OFF = the PA can only give a brand dose dated today, and cannot change the date of
-- an already-given dose. Back-dated OHF (no brand) stays allowed.
--
-- Run on STAGING first, then PROD, BEFORE / WITH the VaccineAPI deploy that reads it
-- (EF selects this column from papermissions; without it the permission endpoints 500).
-- Table name is lowercase: papermissions.
--
-- Column default is 0 (OFF): new PAs start restricted. Step 2 grants YES to named PAs only.
-- Do NOT run a blanket UPDATE. Take a backup of papermissions first (step 0).

-- 0. Backup
CREATE TABLE papermissions_bak_20261123 AS SELECT * FROM papermissions;

-- 1. Add the column (every existing PA becomes OFF)
ALTER TABLE papermissions
  ADD COLUMN BackdateWithBrand TINYINT(1) NOT NULL DEFAULT 0;

-- 2. Look at the PAs first, then choose who keeps back-dating with a brand
SELECT p.Id AS PaId, p.Name, pp.BackdateWithBrand
FROM personalassistant p
JOIN papermissions pp ON pp.PaId = p.Id
ORDER BY p.Id;

-- 3. Grant YES to the trusted PAs only. Fill in the ids from step 2, then run:
-- UPDATE papermissions SET BackdateWithBrand = 1 WHERE PaId IN (/* id1, id2, ... */);

-- 4. Verify
-- SELECT PaId, BackdateWithBrand FROM papermissions ORDER BY PaId;

-- Rollback (if needed): ALTER TABLE papermissions DROP COLUMN BackdateWithBrand;
