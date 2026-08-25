-- Add Vaccine to Patient Record — new PA permission
-- Run manually on staging/prod (no EF migrations in this project).
-- Previously this action was gated by AddSpecialDoses; now it has its own flag.

ALTER TABLE papermissions
  ADD COLUMN AddVaccineToPatientRecord TINYINT(1) NOT NULL DEFAULT 0;
