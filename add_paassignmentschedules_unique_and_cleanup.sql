-- 2026-10-19  PA multi-assignment: pin-table hygiene (run AFTER the API change is deployed; manual, staging first).
-- 1) Back up the dangling pins, 2) delete them, 3) add the unique (AssignmentId, ScheduleId) index
--    (race-proofs LinkScheduleToAssignment). 10-19 SELECTs: 445 pins point to a missing assignment,
--    34 to a missing schedule, 0 exact duplicate pairs. All-lowercase names (case-sensitive host).

-- Preview (expect ~445 + 34 rows):
SELECT 'missing assignment' AS why, COUNT(*) AS n FROM paassignmentschedules p
  LEFT JOIN paassignments a ON a.Id = p.AssignmentId WHERE a.Id IS NULL
UNION ALL
SELECT 'missing schedule', COUNT(*) FROM paassignmentschedules p
  LEFT JOIN schedules s ON s.Id = p.ScheduleId WHERE s.Id IS NULL;

CREATE TABLE paassignmentschedules_dangling_backup_20261019 AS
SELECT p.* FROM paassignmentschedules p
LEFT JOIN paassignments a ON a.Id = p.AssignmentId
LEFT JOIN schedules s ON s.Id = p.ScheduleId
WHERE a.Id IS NULL OR s.Id IS NULL;

DELETE p FROM paassignmentschedules p
JOIN paassignmentschedules_dangling_backup_20261019 b ON b.Id = p.Id;

-- Must return 0 rows before creating the index:
SELECT AssignmentId, ScheduleId, COUNT(*) c FROM paassignmentschedules GROUP BY AssignmentId, ScheduleId HAVING c > 1;

CREATE UNIQUE INDEX ux_paassignmentschedules_assignment_schedule ON paassignmentschedules (AssignmentId, ScheduleId);

-- Rollback: DROP INDEX ux_paassignmentschedules_assignment_schedule ON paassignmentschedules;
--           INSERT INTO paassignmentschedules SELECT * FROM paassignmentschedules_dangling_backup_20261019;
