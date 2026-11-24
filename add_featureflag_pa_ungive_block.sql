-- S4 (UNGIVE_GUARDS_SPEC.md section 5): a PA or manager can never ungive a dose that has an invoice link or a
-- recorded payment. Seeded OFF. The API treats a missing row as OFF, so this file is optional before deploy.
-- Run on prod (phpMyAdmin, lowercase table name). To turn the block ON, after checking it in the app:
--   UPDATE featureflags SET IsOn = 1 WHERE FlagKey = 'pa.ungive.block';
-- To turn it OFF again:
--   UPDATE featureflags SET IsOn = 0 WHERE FlagKey = 'pa.ungive.block';
INSERT IGNORE INTO featureflags (FlagKey, IsOn, Note) VALUES
  ('pa.ungive.block', 0, 'S4: PA/manager cannot ungive once an invoice or payment exists');
