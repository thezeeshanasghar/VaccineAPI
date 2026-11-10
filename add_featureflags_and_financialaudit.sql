-- PA accountability Phase 0/2: two NEW tables only. Nothing existing is altered, no data is touched.
-- Safe to run before the API that uses them; the API ignores them until its flags are on.
-- Rollback: DROP TABLE financialaudit; DROP TABLE featureflags;   (both empty/inert until written to)
-- Run on prod after taking your usual backup. Lowercase names on purpose (case-sensitive host).

CREATE TABLE IF NOT EXISTS featureflags (
  FlagKey     VARCHAR(80)  NOT NULL,
  IsOn        TINYINT(1)   NOT NULL DEFAULT 0,
  UpdatedAt   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  Note        VARCHAR(255) NULL,
  PRIMARY KEY (FlagKey)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO featureflags (FlagKey, IsOn, Note) VALUES
  ('identity.stamp',      0, 'Phase 2: write token-sourced actor rows into financialaudit'),
  ('integrity.tx',        0, 'Phase 4: transactions + defer push until commit'),
  ('reversal.voidkeep',   0, 'Phase 4: void-and-keep instead of hard delete'),
  ('read.ownerbucket',    0, 'Phase 5: PA / Doctor-direct / Unknown buckets on Cash tab'),
  ('fin.payments',        0, 'Phase 6: invoicepayments + handover items'),
  ('reversal.v2',         0, 'Phase 7: correction vs refusal vs re-give');

-- Append-only record of WHO did WHAT, with the actor taken from the validated token, not the request body.
CREATE TABLE IF NOT EXISTS financialaudit (
  Id            BIGINT       NOT NULL AUTO_INCREMENT,
  At            DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),   -- server UTC
  Action        VARCHAR(40)  NOT NULL,        -- Give, Ungive, InvoiceCreate, InvoicePrint, PaymentRecord, Confirm, Void, Handover, OwnerChange, Refusal ...
  DoctorId      BIGINT       NULL,
  ClinicId      BIGINT       NULL,
  ChildId       BIGINT       NULL,
  ScheduleId    BIGINT       NULL,
  InvoiceSubmissionId BIGINT NULL,
  InvoiceNumber VARCHAR(40)  NULL,
  AssignmentId  BIGINT       NULL,
  Amount        DECIMAL(12,2) NULL,
  ActorUserId   BIGINT       NULL,            -- from the token
  ActorRole     VARCHAR(20)  NULL,            -- DOCTOR / PA / MANAGER / SUPERADMIN / PARENT / AGENT / SYSTEM
  ActorSource   VARCHAR(10)  NOT NULL,        -- 'token' | 'body' | 'none'  (never silently "DOCTOR")
  ClaimedPaId   BIGINT       NULL,            -- what the request body said, kept to compare with the token
  Endpoint      VARCHAR(120) NULL,
  ClientVersion VARCHAR(30)  NULL,
  Detail        JSON         NULL,            -- before/after
  PRIMARY KEY (Id),
  KEY ix_fa_child (ChildId, At),
  KEY ix_fa_invoice (InvoiceSubmissionId),
  KEY ix_fa_actor (ActorUserId, At),
  KEY ix_fa_doctor_at (DoctorId, At)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Check afterwards:
-- SELECT FlagKey, IsOn FROM featureflags;  SHOW CREATE TABLE financialaudit;
