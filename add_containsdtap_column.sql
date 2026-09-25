-- Add ContainsDTaP flag to vaccine table — generic "this vaccine contains a DTaP/DPT
-- component" marker (standalone DTaP or any combo product with DTaP/DPT bundled in).
-- Drives the combo-coverage grey-out check on the Add Dose screen and the give-time
-- predecessor check in ScheduleController. Doctor/admin sets it per vaccine after
-- deploy — no backfill needed.
-- Run manually on staging/prod (no EF migrations in this project).

ALTER TABLE vaccines
  ADD COLUMN ContainsDTaP TINYINT(1) NOT NULL DEFAULT 0;
