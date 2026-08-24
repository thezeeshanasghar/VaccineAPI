-- Manager role — new tables
-- Run manually on staging/prod (no EF migrations in this project).
-- Manager is a real third role (mirrors PersonalAssistant/PaAccess/PaPermission),
-- not a flag on PaPermission. Adds:
--   1. manager            — the account itself, mirrors personalassistant
--   2. manageraccess      — clinic grants, mirrors paaccess (no IsOnline — Manager
--                           has no "currently online clinic" concept)
--   3. managerpermissions — the 6 oversight flags moved OUT of papermissions

CREATE TABLE manager (
  Id            BIGINT NOT NULL AUTO_INCREMENT,
  Name          VARCHAR(255) NOT NULL DEFAULT '',
  Email         VARCHAR(255) NOT NULL DEFAULT '',
  IsVerified    TINYINT(1) NOT NULL DEFAULT 0,
  IsActive      TINYINT(1) NOT NULL DEFAULT 1,
  ProfileImage  VARCHAR(255) NOT NULL DEFAULT 'Resources/Images/avatar.png',
  DoctorId      BIGINT NOT NULL,
  UserId        BIGINT NOT NULL,
  PRIMARY KEY (Id),
  INDEX IX_manager_DoctorId (DoctorId),
  INDEX IX_manager_UserId (UserId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE manageraccess (
  Id         BIGINT NOT NULL AUTO_INCREMENT,
  ManagerId  BIGINT NOT NULL,
  ClinicId   BIGINT NOT NULL,
  PRIMARY KEY (Id),
  INDEX IX_manageraccess_ManagerId (ManagerId),
  INDEX IX_manageraccess_ClinicId (ClinicId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE managerpermissions (
  Id                          BIGINT NOT NULL AUTO_INCREMENT,
  ManagerId                   BIGINT NOT NULL,
  ViewPaAssignmentStatus      TINYINT(1) NOT NULL DEFAULT 0,
  ReassignPaTask              TINYINT(1) NOT NULL DEFAULT 0,
  ViewFeedbackResponseTracker TINYINT(1) NOT NULL DEFAULT 0,
  SendFeedbackEmail           TINYINT(1) NOT NULL DEFAULT 0,
  SendFeedbackWhatsApp        TINYINT(1) NOT NULL DEFAULT 0,
  ManagePaClinicAssignments   TINYINT(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (Id),
  INDEX IX_managerpermissions_ManagerId (ManagerId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- NOTE: this does NOT drop the 6 columns papermissions_manager_level_columns.sql added to
-- papermissions (ViewPaAssignmentStatus etc.) — if that SQL was already run on this
-- environment, those columns are now orphaned (PaPermission.cs no longer has these fields,
-- so nothing reads/writes them) but harmless to leave in place. Only drop them if you
-- confirm that SQL was actually run here; ask before doing so, don't guess:
--
-- ALTER TABLE papermissions
--   DROP COLUMN ViewPaAssignmentStatus,
--   DROP COLUMN ReassignPaTask,
--   DROP COLUMN ViewFeedbackResponseTracker,
--   DROP COLUMN SendFeedbackEmail,
--   DROP COLUMN SendFeedbackWhatsApp,
--   DROP COLUMN ManagePaClinicAssignments;
