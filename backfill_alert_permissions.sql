-- Backfill alerts/messaging PA permissions — run manually on staging/prod
-- (no EF migrations in this project).
--
-- ViewAlerts/SendBulkEmail/OpenWhatsApp/DownloadAlertCsv default to false on the
-- PaPermission table. GetByPaId already covers PAs with NO saved permission row
-- (returns a blank object with these true). This backfill covers PAs who DO have
-- a saved row already, from before this defaulting existed — without it, those
-- PAs still have the Email/WhatsApp alert buttons hidden (*ngIf-gated in VacDoc's
-- vaccine-alert page) even after the code fix.
UPDATE papermissions
SET ViewAlerts = 1,
    SendBulkEmail = 1,
    OpenWhatsApp = 1,
    DownloadAlertCsv = 1
WHERE ViewAlerts = 0
  AND SendBulkEmail = 0
  AND OpenWhatsApp = 0
  AND DownloadAlertCsv = 0;
