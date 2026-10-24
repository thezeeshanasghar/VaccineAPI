-- Custom (Customize / "special") quick select: its own table + new PA permission.
-- Fully independent of Travel quick select: touches no existing table except adding one
-- column to papermissions. Run manually on staging then prod (no EF migrations).
-- Table name must stay all-lowercase.

CREATE TABLE IF NOT EXISTS doctorcustomquick (
  Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  DoctorId BIGINT NOT NULL,
  DoseId BIGINT NOT NULL,
  SortOrder INT NOT NULL DEFAULT 0,
  UNIQUE KEY uq_doctorcustomquick_doctor_dose (DoctorId, DoseId),
  KEY ix_doctorcustomquick_doctor (DoctorId)
);

ALTER TABLE papermissions
  ADD COLUMN UseCustomQuickSelect TINYINT(1) NOT NULL DEFAULT 0;
