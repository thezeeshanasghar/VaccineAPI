-- Backfill IsCompleted for PA assignments the doctor already cash-confirmed
-- Run manually on prod (no EF migrations in this project).
--
-- Root cause: ConfirmInvoice (ScheduleController.cs) stamps IsCashConfirmedByDoctor +
-- CashConfirmedAt on the linked PAAssignment when the doctor confirms cash received, but
-- never touched IsCompleted. GetByPA's "Pending" query filters on !IsCompleted only, so a
-- PA's assignment stayed visible in their own Pending/New list forever, even after the
-- doctor had already confirmed the cash weeks or months earlier.
--
-- Confirmed live 2026-08-26: 196 assignments across every PA at this clinic
-- (Amir Khan, Atif Zubair, Sapna, Nouman, Masood) — IsCashConfirmedByDoctor = 1 but
-- IsCompleted = 0, oldest dated 2026-07-19. ConfirmInvoice is now fixed to set
-- IsCompleted going forward; this is the one-time catch-up for everything confirmed
-- before that fix shipped.
UPDATE paassignments
SET IsCompleted = 1,
    CompletedAt = CashConfirmedAt
WHERE IsCashConfirmedByDoctor = 1
  AND IsCompleted = 0;

-- Verify: should return 0 after running the UPDATE above.
-- SELECT COUNT(*) FROM paassignments WHERE IsCashConfirmedByDoctor = 1 AND IsCompleted = 0;
