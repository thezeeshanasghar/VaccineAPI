-- Push delivery log: one row per send attempt (or skipped send). Answers "why didn't X get it".
-- Run manually on staging, then prod, BEFORE deploying the API. Table name MUST be lowercase.
-- Rows older than 30 days are pruned by PushReminderService.

CREATE TABLE IF NOT EXISTS `pushattempt` (
  `Id` BIGINT NOT NULL AUTO_INCREMENT,
  `RecipientType` VARCHAR(16) NOT NULL,
  `RecipientId` BIGINT NOT NULL,
  `Type` VARCHAR(64) NOT NULL,
  `Title` VARCHAR(200) NOT NULL,
  `Result` VARCHAR(24) NOT NULL,
  `TokenCount` INT NOT NULL,
  `SentCount` INT NOT NULL,
  `RemovedCount` INT NOT NULL,
  `Detail` VARCHAR(1000) NOT NULL,
  `DedupKey` VARCHAR(190) NULL,
  `CreatedAt` DATETIME NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_pushattempt_Recipient` (`RecipientType`,`RecipientId`,`CreatedAt`),
  KEY `IX_pushattempt_CreatedAt` (`CreatedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
