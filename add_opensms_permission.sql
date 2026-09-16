-- Add OpenSms PA permission column — run manually on staging/prod
-- (no EF migrations in this project).
--
-- New column defaults to 0/false at the DB level. GetByPaId already covers PAs
-- with NO saved permission row (returns a blank object with OpenSms = true,
-- matching OpenWhatsApp). This backfill covers PAs who already have a saved
-- row, so existing PAs don't lose the new SMS alert button just because their
-- row predates this column — same reasoning as add_opensms already mirrors for
-- OpenWhatsApp in backfill_alert_permissions.sql.

ALTER TABLE papermissions
ADD OpenSms BIT NOT NULL DEFAULT 0;

UPDATE papermissions
SET OpenSms = 1
WHERE ViewAlerts = 1
  AND OpenWhatsApp = 1;
