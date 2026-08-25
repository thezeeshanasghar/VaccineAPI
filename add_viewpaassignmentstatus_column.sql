-- View Own Assignment Status — new PA permission
-- Run manually on staging/prod (no EF migrations in this project).
-- Lets a PA see the "PA Assignments" card + their own list on the dashboard.
-- Previously the frontend read this field from PaPermission even though it
-- didn't exist there (only on ManagerPermission), so the card was always hidden.

ALTER TABLE papermissions
  ADD COLUMN ViewPaAssignmentStatus TINYINT(1) NOT NULL DEFAULT 0;
