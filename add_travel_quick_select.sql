-- Travel quick select: per-doctor list of up to 4 doses + new PA permission.
-- Run manually on staging then prod (no EF migrations). Table name must stay all-lowercase.

CREATE TABLE IF NOT EXISTS doctortravelquick (
  Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  DoctorId BIGINT NOT NULL,
  DoseId BIGINT NOT NULL,
  SortOrder INT NOT NULL DEFAULT 0,
  UNIQUE KEY uq_doctortravelquick_doctor_dose (DoctorId, DoseId),
  KEY ix_doctortravelquick_doctor (DoctorId)
);

ALTER TABLE papermissions
  ADD COLUMN UseTravelQuickSelect TINYINT(1) NOT NULL DEFAULT 0;
