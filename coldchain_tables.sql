-- Cold Chain Temperature Monitoring — new columns + tables
-- Run manually on staging/prod (no EF migrations in this project).
-- Adds:
--   1. AllowColdChain on doctors (platform-admin controlled feature flag)
--   2. ColdChainEntry on papermissions (PA permission to log fridge readings)
--   3. refrigerators, temperaturereadings, coldchainapprovallogs tables

ALTER TABLE doctors
  ADD COLUMN AllowColdChain TINYINT(1) NOT NULL DEFAULT 0;

ALTER TABLE papermissions
  ADD COLUMN ColdChainEntry TINYINT(1) NOT NULL DEFAULT 0;

CREATE TABLE refrigerators (
  Id             BIGINT NOT NULL AUTO_INCREMENT,
  DoctorId       BIGINT NOT NULL,
  ClinicId       BIGINT NOT NULL,
  Name           VARCHAR(255) NOT NULL,
  SerialNumber   VARCHAR(255) NOT NULL,
  Type           VARCHAR(50) NOT NULL DEFAULT 'Refrigerator',
  MinTemp        DECIMAL(6,2) NOT NULL,
  MaxTemp        DECIMAL(6,2) NOT NULL,
  Location       VARCHAR(255) NULL,
  IsActive       TINYINT(1) NOT NULL DEFAULT 1,
  CreatedAt      DATETIME(6) NOT NULL,
  PRIMARY KEY (Id),
  INDEX IX_refrigerators_ClinicId (ClinicId),
  INDEX IX_refrigerators_DoctorId (DoctorId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE temperaturereadings (
  Id               BIGINT NOT NULL AUTO_INCREMENT,
  RefrigeratorId   BIGINT NOT NULL,
  DoctorId         BIGINT NOT NULL,
  ClinicId         BIGINT NOT NULL,
  Temperature      DECIMAL(6,2) NOT NULL,
  RecordedDate     DATE NOT NULL,
  RecordedTime     VARCHAR(10) NOT NULL,
  RecordedByPaId   BIGINT NULL,
  RecordedByName   VARCHAR(255) NOT NULL DEFAULT '',
  Notes            VARCHAR(1000) NULL,
  IsInRange        TINYINT(1) NOT NULL DEFAULT 0,
  CreatedAt        DATETIME(6) NOT NULL,
  PRIMARY KEY (Id),
  INDEX IX_temperaturereadings_RefrigeratorId (RefrigeratorId),
  INDEX IX_temperaturereadings_ClinicId (ClinicId),
  INDEX IX_temperaturereadings_RecordedDate (RecordedDate),
  INDEX IX_temperaturereadings_ClinicId_RecordedDate (ClinicId, RecordedDate)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE coldchainapprovallogs (
  Id               BIGINT NOT NULL AUTO_INCREMENT,
  DoctorId         BIGINT NOT NULL,
  ClinicId         BIGINT NOT NULL,
  WeekStartDate    DATETIME(6) NOT NULL,
  WeekEndDate      DATETIME(6) NOT NULL,
  TotalReadings    INT NOT NULL DEFAULT 0,
  InRangeCount     INT NOT NULL DEFAULT 0,
  OutOfRangeCount  INT NOT NULL DEFAULT 0,
  RequiredChecks   INT NOT NULL DEFAULT 0,
  MissedChecks     INT NOT NULL DEFAULT 0,
  Status           VARCHAR(20) NOT NULL DEFAULT 'pending',
  DoctorComments   VARCHAR(2000) NULL,
  ApprovalDate     DATETIME(6) NULL,
  CreatedAt        DATETIME(6) NOT NULL,
  UpdatedAt        DATETIME(6) NULL,
  PRIMARY KEY (Id),
  INDEX IX_coldchainapprovallogs_ClinicId (ClinicId),
  INDEX IX_coldchainapprovallogs_DoctorId_WeekStartDate (DoctorId, WeekStartDate),
  INDEX IX_coldchainapprovallogs_ClinicId_WeekStartDate (ClinicId, WeekStartDate)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
