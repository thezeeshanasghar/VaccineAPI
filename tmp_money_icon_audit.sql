-- Diagnostic query: find Schedule rows likely mis-stamped by the over-broad
-- backfill introduced in commit ef0f33b (PAAssignmentController.cs Create/Reassign),
-- which credited a new PA as PaymentCollectorPaId for ANY unpaid given dose on the
-- child, regardless of how old/unrelated that dose was to the new assignment.
--
-- Heuristic: a dose's PaymentCollectorPaId currently points to a PA whose assignment
-- for this child was created AFTER the dose's own GivenDate. A dose given before the
-- assignment that "claims" it even existed is a strong signal it was swept in by the
-- backfill rather than genuinely belonging to that PA's visit.
--
-- This is read-only — inspect the output before deciding how to clean up.

SELECT
    s.Id                  AS ScheduleId,
    s.ChildId,
    s.DoseId,
    s.GivenDate,
    s.GivenByPaId,
    s.PaymentCollectorPaId,
    s.IsPaymentCollected,
    s.Amount,
    pa.Id                 AS ClaimingAssignmentId,
    pa.AssignedAt         AS ClaimingAssignmentAssignedAt,
    pa.PersonalAssistantId AS ClaimingPaId
FROM Schedules s
JOIN PAAssignments pa
    ON pa.ChildId = s.ChildId
   AND pa.PersonalAssistantId = s.PaymentCollectorPaId
   AND pa.IsCancelled = 0
WHERE s.IsDone = 1
  AND s.IsPaymentCollected = 0
  AND s.PaymentCollectorPaId IS NOT NULL
  AND s.GivenDate IS NOT NULL
  -- dose was given strictly before the assignment that currently "owns" it was created
  AND s.GivenDate < DATE(pa.AssignedAt)
  -- exclude doses the claiming PA actually gave themselves (genuinely theirs)
  AND (s.GivenByPaId IS NULL OR s.GivenByPaId <> s.PaymentCollectorPaId)
ORDER BY s.ChildId, s.GivenDate;
