-- Assistant live location (Doctor 1 only). Run manually on prod BEFORE deploying the API.
-- Table names MUST be all lowercase (MySQL on Linux is case-sensitive).

ALTER TABLE papermissions
  ADD COLUMN TrackLocation TINYINT(1) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS `pashifts` (
  `Id` BIGINT NOT NULL AUTO_INCREMENT,
  `PaId` BIGINT NOT NULL,
  `DoctorId` BIGINT NOT NULL,
  `StartedAt` DATETIME NOT NULL,
  `EndedAt` DATETIME NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_pashifts_PaId_EndedAt` (`PaId`,`EndedAt`),
  KEY `IX_pashifts_DoctorId` (`DoctorId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `palocations` (
  `Id` BIGINT NOT NULL AUTO_INCREMENT,
  `ShiftId` BIGINT NOT NULL,
  `PaId` BIGINT NOT NULL,
  `DoctorId` BIGINT NOT NULL,
  `Latitude` DOUBLE NOT NULL,
  `Longitude` DOUBLE NOT NULL,
  `Accuracy` DOUBLE NULL,
  `Battery` INT NULL,
  `RecordedAt` DATETIME NOT NULL,
  `ReceivedAt` DATETIME NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_palocations_PaId_RecordedAt` (`PaId`,`RecordedAt`),
  KEY `IX_palocations_DoctorId_RecordedAt` (`DoctorId`,`RecordedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
