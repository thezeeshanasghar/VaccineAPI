-- 2026-10-19  Cleanup of stale PA pins on OPEN assignments (orphan-invoice sweep bug).
-- See memory project_pa_assignment_stale_pin_orphan_invoice_2026_10_19.
-- Scope (derived from a read-only dry run, NOT a blanket update):
--   open assignment (not cancelled, not cash-confirmed)
--   pinned dose IsDone=1, GivenByPaId IS NULL, given > 1 day before AssignedAt
--   AND its invoice is already IsConfirmedByDoctor=1 (settled) or dose older than 7 days
--   EXEMPT: IsPaymentCollected=1 on an UNCONFIRMED invoice (real cash in transit: assignments 396, 1935)
-- Actions: (1) delete the stale pin row, (2) clear PaymentCollectorPaId only where it equals the
-- assigned PA, dose unpaid and its invoice confirmed. Invoice PaId/label are left untouched
-- (the prior label was not recorded; the invoices are confirmed history).
-- Tables are all-lowercase (case-sensitive host). Rollback = restore from the two backup tables.

CREATE TABLE paassignmentschedules_stalepin_backup_20261019 AS
SELECT pas.* FROM paassignmentschedules pas
JOIN paassignments pa ON pa.Id = pas.AssignmentId
JOIN schedules s ON s.Id = pas.ScheduleId
LEFT JOIN invoicesubmissions i ON i.Id = s.InvoiceSubmissionId
WHERE pa.IsCancelled = 0 AND pa.IsCashConfirmedByDoctor = 0
  AND s.IsDone = 1 AND s.GivenByPaId IS NULL
  AND COALESCE(s.DoneAt, s.GivenDate) < DATE_SUB(pa.AssignedAt, INTERVAL 1 DAY)
  AND (i.IsConfirmedByDoctor = 1 OR COALESCE(s.DoneAt, s.GivenDate) < DATE_SUB(pa.AssignedAt, INTERVAL 7 DAY))
  AND NOT (s.IsPaymentCollected = 1 AND COALESCE(i.IsConfirmedByDoctor, 0) = 0);

CREATE TABLE schedules_stalepin_stamp_backup_20261019 AS
SELECT s.* FROM schedules s
WHERE s.Id IN (SELECT ScheduleId FROM paassignmentschedules_stalepin_backup_20261019);

-- Clear the collector stamp (before deleting pins, while the join still resolves).
UPDATE schedules s
JOIN paassignmentschedules_stalepin_backup_20261019 b ON b.ScheduleId = s.Id
JOIN paassignments pa ON pa.Id = b.AssignmentId
JOIN invoicesubmissions i ON i.Id = s.InvoiceSubmissionId
SET s.PaymentCollectorPaId = NULL
WHERE s.PaymentCollectorPaId = pa.PersonalAssistantId
  AND s.IsPaymentCollected = 0
  AND i.IsConfirmedByDoctor = 1;

DELETE pas FROM paassignmentschedules pas
JOIN paassignmentschedules_stalepin_backup_20261019 b ON b.Id = pas.Id;
