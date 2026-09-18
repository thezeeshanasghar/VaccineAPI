-- Clinic timings / off-days feature removal
-- Run after deploying the code changes that drop ClinicTiming everywhere
-- (Models/ClinicTiming.cs, ModelDTO/ClinicTimingDTO.cs, Context.cs DbSet,
-- AutoMapperProfile, ClinicController/DoctorController/PersonalAssistantController/
-- ManagerController, VacDoc add/edit clinic pages).
--
-- Table name and columns confirmed from Migrations/20250307103214_InitialCreate.cs.
-- lower_case_table_names=0 on prod (see feedback_mysql_case_sensitive_tables memory) —
-- table name must be exactly "clinictimings", all lowercase, as created.

-- 1) Back up before deleting, in case any historical timing data is ever needed for reference.
CREATE TABLE clinictimings_backup_20260918 AS SELECT * FROM clinictimings;

-- 2) Wipe all existing clinic timing / off-day rows for every doctor.
DELETE FROM clinictimings;

-- 3) Drop the table entirely now that the feature and its FK are gone from the app.
--    Foreign key FK_clinictimings_clinics_ClinicId is dropped automatically with the table.
DROP TABLE clinictimings;

-- If you'd rather keep the table around (e.g. to stage a rollback) instead of dropping it,
-- stop after step 2 and skip the DROP TABLE — the app no longer reads or writes this table
-- either way once the code changes are deployed.
