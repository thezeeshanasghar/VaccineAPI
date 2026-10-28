-- Agent Referral (two-step registration) — staging table for patients referred from VacAgent.
-- Spec: AGENT_REFERRAL_TWO_STEP_SPEC.md (vsc root). Run on staging first, then prod.
-- No EF migrations in this codebase — manual SQL only. Lowercase table name (case-sensitive host).
--
-- A referral is NOT a child: no childs row, no parent user, no schedule exists until clinic staff
-- register the patient through the normal Add Patient form. On registration the referral is
-- marked Converted and linked to the new child via ChildId.
--
-- No FK constraints: agents is MyISAM (cannot be referenced), and clinics' engine is not confirmed
-- here, so ClinicId is a plain indexed column like the other agent tables.
-- Rows are never deleted; Converted / Rejected stay for the agent's status view and audit.

CREATE TABLE agentreferrals (
  Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  AgentId INT NOT NULL,
  ClinicId BIGINT NOT NULL,
  Name VARCHAR(255) NOT NULL,
  FatherName VARCHAR(255) NOT NULL,
  Gender VARCHAR(10) NOT NULL,
  DOB DATE NOT NULL,
  CountryCode VARCHAR(8) NOT NULL,
  MobileNumber VARCHAR(20) NOT NULL,
  City VARCHAR(100) NULL,
  Email VARCHAR(255) NULL,
  Note VARCHAR(200) NULL,
  Status VARCHAR(16) NOT NULL DEFAULT 'New',   -- New | Converted | Rejected
  ChildId BIGINT NULL,                         -- set when Converted
  RejectReason VARCHAR(200) NULL,
  CreatedAt DATETIME NOT NULL,
  ResolvedAt DATETIME NULL,
  ResolvedByRole VARCHAR(16) NULL,
  ResolvedById BIGINT NULL,
  INDEX idx_agentreferrals_agent (AgentId),
  INDEX idx_agentreferrals_clinic_status (ClinicId, Status, CreatedAt),
  INDEX idx_agentreferrals_mobile_dob (MobileNumber, DOB)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
