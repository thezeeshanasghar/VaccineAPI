-- Backfill SecurityStamp for existing users
-- Run manually on prod (no EF migrations in this project).
--
-- Root cause: SecurityStamp was added to the User model with a C# default of
-- Guid.NewGuid() — but that default only applies to a brand-new object created
-- in memory. It never touched existing rows in the database, which were left
-- with an empty string. 12,382 of 12,841 users (96%) had SecurityStamp = ''.
--
-- This mattered starting 2026-08-25, when PAAssignmentController.VerifyCaller
-- started requiring a non-empty stamp to match on Create/GetByPA/Reassign —
-- any user whose stamp was empty could never pass that check, since their
-- login response faithfully echoed back the empty value and the check
-- explicitly refuses to treat an empty stamp as valid (string.IsNullOrEmpty
-- guard), even when empty happens to equal what's stored. That's correct
-- behavior for the check itself (never treat "blank" as a valid credential)
-- but wrong for data that was never populated in the first place.
--
-- Each row gets a distinct value — never copy one UUID across multiple rows,
-- since SecurityStamp is meant to be a per-user secret.
UPDATE users
SET SecurityStamp = UUID()
WHERE SecurityStamp = '' OR SecurityStamp IS NULL;

-- Verify: should return 0 after running the UPDATE above.
-- SELECT COUNT(*) FROM users WHERE SecurityStamp = '' OR SecurityStamp IS NULL;
