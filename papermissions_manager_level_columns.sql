-- Manager-Level Permissions — new columns on papermissions
-- Run manually on staging/prod (no EF migrations in this project).
-- Adds the 6 manager-tier flags used by the new "Manager-Level Permissions"
-- section on the PA Permissions page (VacDoc).

ALTER TABLE papermissions
  ADD COLUMN ViewPaAssignmentStatus       TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN ReassignPaTask               TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN ViewFeedbackResponseTracker  TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN SendFeedbackEmail            TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN SendFeedbackWhatsApp         TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN ManagePaClinicAssignments    TINYINT(1) NOT NULL DEFAULT 0;
