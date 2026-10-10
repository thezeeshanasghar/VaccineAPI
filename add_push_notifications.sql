-- Push notifications (FCM). Run manually on prod BEFORE deploying the API.
-- Table names MUST be all lowercase (MySQL on Linux is case-sensitive).

CREATE TABLE IF NOT EXISTS `devicetokens` (
  `Id` BIGINT NOT NULL AUTO_INCREMENT,
  `RecipientType` VARCHAR(16) NOT NULL,
  `RecipientId` BIGINT NOT NULL,
  `Token` VARCHAR(512) NOT NULL,
  `Platform` VARCHAR(16) NOT NULL,
  `AppFlavor` VARCHAR(64) NOT NULL,
  `CreatedAt` DATETIME NOT NULL,
  `LastSeenAt` DATETIME NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UX_devicetokens_Token` (`Token`(255)),
  KEY `IX_devicetokens_Recipient` (`RecipientType`,`RecipientId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `pushlog` (
  `Id` BIGINT NOT NULL AUTO_INCREMENT,
  `DedupKey` VARCHAR(190) NOT NULL,
  `SentAt` DATETIME NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UX_pushlog_DedupKey` (`DedupKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
