-- PA accountability R11: financialaudit becomes insert-only at the database, and the identity.bind flag row is added.
-- Prerequisite: add_featureflags_and_financialaudit.sql already run. Back up first.
-- Run the three statements below in this order. Nothing here turns a behaviour on: both flags stay OFF until you UPDATE them.

INSERT IGNORE INTO featureflags (FlagKey, IsOn, Note) VALUES
  ('identity.bind', 0, 'R11: PA/Manager token id overrides a missing or different PaId/ManagerId in give/ungive requests');

-- If your MySQL has binary logging on and the account is not SUPER, creating triggers can fail with
-- "You do not have the SUPER privilege and binary logging is enabled". Fix: run as root, or
-- SET GLOBAL log_bin_trust_function_creators = 1; then retry.
DELIMITER $$

CREATE TRIGGER financialaudit_no_update BEFORE UPDATE ON financialaudit FOR EACH ROW
BEGIN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'financialaudit is append-only: UPDATE is not allowed';
END$$

CREATE TRIGGER financialaudit_no_delete BEFORE DELETE ON financialaudit FOR EACH ROW
BEGIN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'financialaudit is append-only: DELETE is not allowed';
END$$

DELIMITER ;

-- Check:
--   SHOW TRIGGERS LIKE 'financialaudit';
--   UPDATE financialaudit SET Action='x' WHERE Id=0;   -- must fail only if a row exists; to test, INSERT a probe row first
-- Limits to know: TRUNCATE and DROP TABLE do not fire triggers. Keep the API's DB user from having DROP/TRUNCATE on this table.
-- Rollback (only if the triggers cause trouble):
--   DROP TRIGGER financialaudit_no_update; DROP TRIGGER financialaudit_no_delete;
-- Then, when ready (separate step, yours): UPDATE featureflags SET IsOn=1 WHERE FlagKey IN ('identity.stamp','identity.bind');
