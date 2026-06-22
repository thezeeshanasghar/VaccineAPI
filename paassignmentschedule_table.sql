-- =====================================================
-- PAAssignmentSchedule — explicit join table between
-- PAAssignment and Schedule.
-- =====================================================
-- Single source of truth for "which Schedule rows does
-- this assignment cover" — replaces the ChildId/date/
-- PaymentCollectorPaId inference that caused two repeated
-- bugs: (1) PaymentCollectorPaId staying NULL when a
-- doctor gave a dose with no PA present, (2) the backfill
-- fix for (1) over-broadly stamping a new PA's ID onto any
-- unpaid dose for the child, including unrelated old
-- visits. The link is now created once, explicitly, at the
-- moment it's known (assignment-create time, or PA-give
-- time for extras) — nothing left to infer or get wrong.
-- =====================================================

CREATE TABLE `paassignmentschedules` (
  `Id`           BIGINT   NOT NULL AUTO_INCREMENT,
  `AssignmentId` BIGINT   NOT NULL,
  `ScheduleId`   BIGINT   NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE INDEX `UX_paassignmentschedules_Assignment_Schedule` (`AssignmentId`, `ScheduleId`),
  INDEX `IX_paassignmentschedules_Assignment` (`AssignmentId`),
  INDEX `IX_paassignmentschedules_Schedule`   (`ScheduleId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Rollback: DROP TABLE `paassignmentschedules`;

-- =====================================================
-- One-time backfill for in-flight assignments.
-- =====================================================
-- Any PAAssignment that's currently active (not completed,
-- not cancelled) was created before this table existed, so
-- it has zero rows here. This reconstructs what Create()
-- would have pinned had the assignment been created under
-- the new system: every Schedule row for the same child
-- whose due-Date falls on the same PKT calendar day as the
-- assignment's AssignedAt — the same ChildId+Date.Date
-- grouping UpdateBulkInjection already uses to find one
-- "bulk group" of doses, excluding skipped rows.
--
-- An assignment whose AssignedAt date doesn't match any
-- Schedule.Date for that child (e.g. an ad-hoc visit) will
-- simply get zero backfilled rows here — same as a fresh
-- assign click on an already-empty group, which is an
-- accepted outcome going forward (extras fold in via
-- LinkScheduleToAssignment as the PA actually gives doses).
-- =====================================================

INSERT INTO `paassignmentschedules` (`AssignmentId`, `ScheduleId`)
SELECT pa.`Id`, s.`Id`
FROM `paassignments` pa
JOIN `schedules` s
  ON s.`ChildId` = pa.`ChildId`
 AND DATE(s.`Date`) = DATE(CONVERT_TZ(pa.`AssignedAt`, '+00:00', '+05:00'))
 AND (s.`IsSkip` IS NULL OR s.`IsSkip` = 0)
WHERE pa.`IsCompleted` = 0
  AND pa.`IsCancelled` = 0
  AND NOT EXISTS (
    SELECT 1 FROM `paassignmentschedules` pas
    WHERE pas.`AssignmentId` = pa.`Id` AND pas.`ScheduleId` = s.`Id`
  );

-- =====================================================
-- Second backfill pass: already-given doses linked via an
-- invoice (the doctor-gave-it-then-assigned-a-PA ordering).
-- =====================================================
-- The pass above only catches doses still due/undone on the
-- assignment's own day — it does not catch a dose the doctor
-- already gave before the PA was assigned, since that dose
-- has no "undone group" to be found in. Those doses are
-- instead linked via the active assignment's InvoiceSubmissionId
-- FK (set by Create()'s orphan-invoice block or by
-- SyncInvoicePaToActiveAssignment at download time). This pass
-- mirrors that same app-code logic: for every active assignment
-- with a linked invoice, pin every Schedule whose GivenDate
-- matches that invoice's InvoiceDate. Since this clinic only
-- ever generates one combined invoice per visit (doses are
-- grouped together, never split across multiple same-day
-- invoices), this match is exact — no cross-visit ambiguity.
-- =====================================================

INSERT INTO `paassignmentschedules` (`AssignmentId`, `ScheduleId`)
SELECT pa.`Id`, s.`Id`
FROM `paassignments` pa
JOIN `invoicesubmissions` inv ON inv.`Id` = pa.`InvoiceSubmissionId`
JOIN `schedules` s
  ON s.`ChildId` = pa.`ChildId`
 AND s.`IsDone` = 1
 AND s.`GivenDate` IS NOT NULL
 AND DATE(s.`GivenDate`) = DATE(inv.`InvoiceDate`)
WHERE pa.`IsCompleted` = 0
  AND pa.`IsCancelled` = 0
  AND pa.`InvoiceSubmissionId` IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM `paassignmentschedules` pas
    WHERE pas.`AssignmentId` = pa.`Id` AND pas.`ScheduleId` = s.`Id`
  );

-- =====================================================
-- Verification — run before/after the backfill to sanity-
-- check row counts per active assignment.
-- =====================================================
-- SELECT pa.Id AS AssignmentId, pa.ChildId, pa.PersonalAssistantId,
--        COUNT(pas.Id) AS LinkedSchedules
-- FROM paassignments pa
-- LEFT JOIN paassignmentschedules pas ON pas.AssignmentId = pa.Id
-- WHERE pa.IsCompleted = 0 AND pa.IsCancelled = 0
-- GROUP BY pa.Id, pa.ChildId, pa.PersonalAssistantId
-- ORDER BY pa.AssignedAt DESC;
