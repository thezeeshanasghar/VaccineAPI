-- Assistant location hardening. Run manually on prod BEFORE deploying (backup first). NOT executed by Claude.
-- Table names MUST be all lowercase (MySQL on Linux is case-sensitive). Safe to re-run: every step is guarded.

-- 1) See duplicates (same PA, same fix time). Keep the lowest Id of each.
SELECT PaId, RecordedAt, COUNT(*) AS n FROM palocations GROUP BY PaId, RecordedAt HAVING COUNT(*) > 1;

-- 2) Remove duplicates.
DELETE l1 FROM palocations l1
JOIN palocations l2 ON l1.PaId = l2.PaId AND l1.RecordedAt = l2.RecordedAt AND l1.Id > l2.Id;

-- 3) Unique fix per PA per second (the API dedups on this too). Skipped if the index already exists.
SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'palocations' AND index_name = 'UX_palocations_PaId_RecordedAt') = 0,
  'ALTER TABLE palocations ADD UNIQUE INDEX UX_palocations_PaId_RecordedAt (PaId, RecordedAt)',
  'SELECT ''step 3: index already exists'' AS info');
PREPARE st FROM @sql; EXECUTE st; DEALLOCATE PREPARE st;

-- 4) OPTIONAL: at most one open shift per PA.
-- 4a) Close every open shift that has a newer open shift for the same PA (newest = latest StartedAt, ties broken by Id).
--     EndedAt = that shift's last fix, or its StartedAt if it has none.
UPDATE pashifts s
JOIN pashifts n ON n.PaId = s.PaId AND n.EndedAt IS NULL
               AND (n.StartedAt > s.StartedAt OR (n.StartedAt = s.StartedAt AND n.Id > s.Id))
SET s.EndedAt = COALESCE((SELECT MAX(p.RecordedAt) FROM palocations p WHERE p.ShiftId = s.Id), s.StartedAt)
WHERE s.EndedAt IS NULL;

-- 4b) Generated column + unique index (NULLs never collide, so closed shifts are unconstrained).
--     EF does not map this column; inserts are unaffected. Requires MySQL 5.7+. Skipped if already present.
SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.columns
    WHERE table_schema = DATABASE() AND table_name = 'pashifts' AND column_name = 'OpenFlag') = 0,
  'ALTER TABLE pashifts ADD COLUMN OpenFlag TINYINT GENERATED ALWAYS AS (IF(EndedAt IS NULL, 1, NULL)) VIRTUAL',
  'SELECT ''step 4b: column already exists'' AS info');
PREPARE st FROM @sql; EXECUTE st; DEALLOCATE PREPARE st;

SET @sql = IF(
  (SELECT COUNT(*) FROM information_schema.statistics
    WHERE table_schema = DATABASE() AND table_name = 'pashifts' AND index_name = 'UX_pashifts_PaId_Open') = 0,
  'ALTER TABLE pashifts ADD UNIQUE INDEX UX_pashifts_PaId_Open (PaId, OpenFlag)',
  'SELECT ''step 4b: index already exists'' AS info');
PREPARE st FROM @sql; EXECUTE st; DEALLOCATE PREPARE st;
