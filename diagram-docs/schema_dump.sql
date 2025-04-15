-- MySQL dump 10.13  Distrib 8.0.41, for Linux (x86_64)
--
-- Host: localhost    Database: vaccineapi
-- ------------------------------------------------------
-- Server version	8.0.41

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Table structure for table `__EFMigrationsHistory`
--

DROP TABLE IF EXISTS `__EFMigrationsHistory`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `__EFMigrationsHistory` (
  `MigrationId` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ProductVersion` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `__efmigrationshistory`
--

DROP TABLE IF EXISTS `__efmigrationshistory`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `__efmigrationshistory` (
  `MigrationId` varchar(150) COLLATE utf8mb4_general_ci NOT NULL,
  `ProductVersion` varchar(32) COLLATE utf8mb4_general_ci NOT NULL,
  PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `adjuststocks`
--

DROP TABLE IF EXISTS `adjuststocks`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `adjuststocks` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `BrandId` bigint NOT NULL,
  `Adjustment` int NOT NULL,
  `Reason` varchar(200) COLLATE utf8mb4_general_ci NOT NULL,
  `Date` datetime NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `BrandId` (`BrandId`),
  CONSTRAINT `adjuststocks_ibfk_1` FOREIGN KEY (`BrandId`) REFERENCES `brands` (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=40 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `agents`
--

DROP TABLE IF EXISTS `agents`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `agents` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` longtext COLLATE utf8mb4_general_ci NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `bills`
--

DROP TABLE IF EXISTS `bills`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `bills` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `BillNo` varchar(100) COLLATE utf8mb4_general_ci NOT NULL,
  `Supplier` varchar(100) COLLATE utf8mb4_general_ci NOT NULL,
  `Date` date NOT NULL,
  `IsPaid` tinyint NOT NULL,
  `DoctorId` bigint NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `BillNo` (`BillNo`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `brandamounts`
--

DROP TABLE IF EXISTS `brandamounts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `brandamounts` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Amount` int NOT NULL,
  `BrandId` bigint NOT NULL,
  `DoctorId` bigint NOT NULL,
  `Count` int NOT NULL,
  `PurchasedAmt` int NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_BrandAmounts_BrandId` (`BrandId`),
  KEY `IX_BrandAmounts_DoctorId` (`DoctorId`),
  CONSTRAINT `FK_BrandAmounts_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `brands` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_BrandAmounts_Doctors_DoctorId` FOREIGN KEY (`DoctorId`) REFERENCES `doctors` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=4455 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `brands`
--

DROP TABLE IF EXISTS `brands`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `brands` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Name` text COLLATE utf8mb4_general_ci,
  `VaccineId` bigint NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Brands_VaccineId` (`VaccineId`),
  CONSTRAINT `FK_Brands_Vaccines_VaccineId` FOREIGN KEY (`VaccineId`) REFERENCES `vaccines` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=187 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `childs`
--

DROP TABLE IF EXISTS `childs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `childs` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Name` text COLLATE utf8mb4_general_ci,
  `FatherName` text COLLATE utf8mb4_general_ci,
  `Email` text COLLATE utf8mb4_general_ci,
  `DOB` datetime NOT NULL,
  `Gender` text COLLATE utf8mb4_general_ci,
  `Type` text COLLATE utf8mb4_general_ci,
  `City` varchar(255) COLLATE utf8mb4_general_ci NOT NULL DEFAULT '',
  `IsEPIDone` smallint DEFAULT NULL,
  `IsVerified` smallint DEFAULT NULL,
  `IsInactive` tinyint(1) DEFAULT NULL,
  `ClinicId` bigint NOT NULL,
  `UserId` bigint NOT NULL,
  `DoctorId` bigint DEFAULT NULL,
  `CNIC` varchar(255) COLLATE utf8mb4_general_ci NOT NULL DEFAULT '',
  `Guardian` varchar(255) COLLATE utf8mb4_general_ci NOT NULL DEFAULT '''Father''',
  `Agent` varchar(255) COLLATE utf8mb4_general_ci NOT NULL DEFAULT '',
  PRIMARY KEY (`Id`),
  KEY `IX_Childs_ClinicId` (`ClinicId`),
  KEY `IX_Childs_DoctorId` (`DoctorId`),
  KEY `IX_Childs_UserId` (`UserId`),
  CONSTRAINT `FK_Childs_Clinics_ClinicId` FOREIGN KEY (`ClinicId`) REFERENCES `clinics` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_Childs_Doctors_DoctorId` FOREIGN KEY (`DoctorId`) REFERENCES `doctors` (`Id`),
  CONSTRAINT `FK_Childs_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=12593 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `cities`
--

DROP TABLE IF EXISTS `cities`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `cities` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Name` varchar(255) COLLATE utf8mb4_general_ci NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=301 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `clinics`
--

DROP TABLE IF EXISTS `clinics`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `clinics` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Name` text COLLATE utf8mb4_general_ci,
  `ConsultationFee` int NOT NULL,
  `Lat` double NOT NULL,
  `Long` double NOT NULL,
  `PhoneNumber` text COLLATE utf8mb4_general_ci,
  `IsOnline` smallint NOT NULL,
  `Address` text COLLATE utf8mb4_general_ci,
  `MonogramImage` text COLLATE utf8mb4_general_ci,
  `DoctorId` bigint NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Clinics_DoctorId` (`DoctorId`),
  CONSTRAINT `FK_Clinics_Doctors_DoctorId` FOREIGN KEY (`DoctorId`) REFERENCES `doctors` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=83 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `clinictimings`
--

DROP TABLE IF EXISTS `clinictimings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `clinictimings` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Day` text COLLATE utf8mb4_general_ci,
  `StartTime` time NOT NULL,
  `EndTime` time NOT NULL,
  `Session` text COLLATE utf8mb4_general_ci,
  `IsOpen` smallint NOT NULL,
  `ClinicId` bigint NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_ClinicTimings_ClinicId` (`ClinicId`),
  CONSTRAINT `FK_ClinicTimings_Clinics_ClinicId` FOREIGN KEY (`ClinicId`) REFERENCES `clinics` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=539 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `doctors`
--

DROP TABLE IF EXISTS `doctors`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `doctors` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `FirstName` text COLLATE utf8mb4_general_ci,
  `LastName` text COLLATE utf8mb4_general_ci,
  `Email` text COLLATE utf8mb4_general_ci,
  `PMDC` text COLLATE utf8mb4_general_ci,
  `ShowPhone` smallint NOT NULL,
  `ShowMobile` smallint NOT NULL,
  `PhoneNo` text COLLATE utf8mb4_general_ci,
  `ValidUpto` datetime DEFAULT NULL,
  `InvoiceNumber` int DEFAULT NULL,
  `ProfileImage` text COLLATE utf8mb4_general_ci,
  `DisplayName` text COLLATE utf8mb4_general_ci,
  `AllowInvoice` smallint NOT NULL,
  `AllowChart` smallint NOT NULL,
  `AllowFollowUp` smallint NOT NULL,
  `AllowInventory` smallint NOT NULL,
  `SMSLimit` int NOT NULL,
  `DoctorType` varchar(255) COLLATE utf8mb4_general_ci DEFAULT NULL,
  `Qualification` varchar(255) COLLATE utf8mb4_general_ci DEFAULT '',
  `AdditionalInfo` varchar(255) COLLATE utf8mb4_general_ci DEFAULT '',
  `UserId` bigint NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Doctors_UserId` (`UserId`),
  CONSTRAINT `FK_Doctors_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=1083 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `doctorschedules`
--

DROP TABLE IF EXISTS `doctorschedules`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `doctorschedules` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `DoseId` bigint NOT NULL,
  `DoctorId` bigint NOT NULL,
  `GapInDays` int NOT NULL,
  `IsActive` smallint DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_DoctorSchedules_DoctorId` (`DoctorId`),
  KEY `IX_DoctorSchedules_DoseId` (`DoseId`),
  CONSTRAINT `FK_DoctorSchedules_Doctors_DoctorId` FOREIGN KEY (`DoctorId`) REFERENCES `doctors` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_DoctorSchedules_Doses_DoseId` FOREIGN KEY (`DoseId`) REFERENCES `doses` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=3078 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `doses`
--

DROP TABLE IF EXISTS `doses`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `doses` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Name` text COLLATE utf8mb4_general_ci,
  `MinAge` int NOT NULL,
  `MaxAge` int DEFAULT NULL,
  `MinGap` int DEFAULT NULL,
  `DoseOrder` int DEFAULT NULL,
  `VaccineId` bigint NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Doses_VaccineId` (`VaccineId`),
  CONSTRAINT `FK_Doses_Vaccines_VaccineId` FOREIGN KEY (`VaccineId`) REFERENCES `vaccines` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=138 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `followups`
--

DROP TABLE IF EXISTS `followups`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `followups` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Disease` text COLLATE utf8mb4_general_ci,
  `NextVisitDate` datetime DEFAULT NULL,
  `CurrentVisitDate` datetime DEFAULT NULL,
  `Weight` float DEFAULT NULL,
  `Height` float DEFAULT NULL,
  `OFC` float DEFAULT NULL,
  `BloodPressure` float DEFAULT NULL,
  `BloodSugar` float DEFAULT NULL,
  `ChildId` bigint NOT NULL,
  `DoctorId` bigint NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_FollowUps_ChildId` (`ChildId`),
  KEY `IX_FollowUps_DoctorId` (`DoctorId`),
  CONSTRAINT `FK_FollowUps_Childs_ChildId` FOREIGN KEY (`ChildId`) REFERENCES `childs` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_FollowUps_Doctors_DoctorId` FOREIGN KEY (`DoctorId`) REFERENCES `doctors` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=2959 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `invoices`
--

DROP TABLE IF EXISTS `invoices`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `invoices` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `InvoiceId` varchar(255) NOT NULL,
  `Amount` decimal(10,2) NOT NULL,
  `ChildId` int NOT NULL,
  `DoctorId` int NOT NULL,
  `ClinicId` int NOT NULL,
  `DoseId` int NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=2212 DEFAULT CHARSET=utf8mb3;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `messages`
--

DROP TABLE IF EXISTS `messages`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `messages` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `MobileNumber` text COLLATE utf8mb4_general_ci,
  `SMS` text COLLATE utf8mb4_general_ci,
  `ApiResponse` text COLLATE utf8mb4_general_ci,
  `Created` datetime NOT NULL,
  `UserId` bigint DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Messages_UserId` (`UserId`),
  CONSTRAINT `FK_Messages_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=87 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `normalranges`
--

DROP TABLE IF EXISTS `normalranges`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `normalranges` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Age` int NOT NULL,
  `Gender` int NOT NULL,
  `WeightMin` float NOT NULL,
  `WeightMax` float NOT NULL,
  `HeightMin` float NOT NULL,
  `HeightMax` float NOT NULL,
  `OfcMin` float NOT NULL,
  `OfcMax` float NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=485 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `schedules`
--

DROP TABLE IF EXISTS `schedules`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `schedules` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Date` datetime NOT NULL,
  `Weight` float DEFAULT NULL,
  `Height` float DEFAULT NULL,
  `Circle` float DEFAULT NULL,
  `IsDone` smallint NOT NULL,
  `IsSkip` smallint DEFAULT NULL,
  `IsDisease` smallint DEFAULT NULL,
  `Due2EPI` smallint NOT NULL,
  `DiseaseYear` text COLLATE utf8mb4_general_ci,
  `GivenDate` datetime DEFAULT NULL,
  `BrandId` bigint DEFAULT NULL,
  `Amount` int DEFAULT NULL,
  `ChildId` bigint NOT NULL,
  `DoseId` bigint NOT NULL,
  `Manufacturer` varchar(20) COLLATE utf8mb4_general_ci NOT NULL,
  `Lot` varchar(25) COLLATE utf8mb4_general_ci NOT NULL,
  `Expiry` datetime DEFAULT NULL,
  `Validity` datetime DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Schedules_BrandId` (`BrandId`),
  KEY `IX_Schedules_ChildId` (`ChildId`),
  KEY `IX_Schedules_DoseId` (`DoseId`),
  CONSTRAINT `FK_Schedules_Brands_BrandId` FOREIGN KEY (`BrandId`) REFERENCES `brands` (`Id`),
  CONSTRAINT `FK_Schedules_Childs_ChildId` FOREIGN KEY (`ChildId`) REFERENCES `childs` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_Schedules_Doses_DoseId` FOREIGN KEY (`DoseId`) REFERENCES `doses` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=357774 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `stocks`
--

DROP TABLE IF EXISTS `stocks`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `stocks` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `BrandId` bigint NOT NULL,
  `Quantity` int NOT NULL,
  `StockAmount` decimal(10,0) NOT NULL,
  `BillId` int NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `BillId` (`BillId`),
  KEY `BrandId` (`BrandId`),
  CONSTRAINT `stocks_ibfk_1` FOREIGN KEY (`BrandId`) REFERENCES `brands` (`Id`),
  CONSTRAINT `stocks_ibfk_2` FOREIGN KEY (`BillId`) REFERENCES `bills` (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=47 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `users`
--

DROP TABLE IF EXISTS `users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `users` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `MobileNumber` text COLLATE utf8mb4_general_ci,
  `Password` text COLLATE utf8mb4_general_ci,
  `UserType` text COLLATE utf8mb4_general_ci,
  `CountryCode` text COLLATE utf8mb4_general_ci,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=9533 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `vaccines`
--

DROP TABLE IF EXISTS `vaccines`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `vaccines` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `Name` text COLLATE utf8mb4_general_ci,
  `MinAge` int NOT NULL,
  `MaxAge` int DEFAULT NULL,
  `isInfinite` smallint DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=84 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2025-04-13  3:34:28
