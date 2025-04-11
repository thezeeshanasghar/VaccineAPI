# VaccineAPI

## Development Setup

```bash
dotnet clean
dotnet restore
dotnet build
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --environment Development
```

## Database Operations

### Entity Framework Commands
```bash
# Install Entity Framework tools
dotnet tool install --global dotnet-ef --version 3.*
dotnet tool install --global dotnet-ef

# Create and apply migrations
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Running with Watch
```bash
dotnet watch run --launch-profile https
dotnet watch run --environment Development
```

## Database Backup and Retention Strategy

The VaccineAPI implements an automated backup strategy to ensure data safety and disaster recovery capabilities.

### Automated Daily Backups

Backups are automatically created daily at 2:00 AM using a cron job that executes the `db_backup.sh` script:

```bash
# Crontab entry
0 2 * * * /home/ec2-user/VaccineAPI/db_backup.sh >> /home/ec2-user/db_backup.log 2>&1
```

### Backup Process

The backup script performs the following operations:
1. Dumps the MySQL database from the Docker container
2. Compresses the backup with gzip
3. Copies the backup to the EC2 instance's home directory
4. Uploads the backup to AWS S3 bucket (`vaccine-api-daily-backup`)

### Retention Policy

A 14-day (2-week) retention policy is applied to the S3 bucket. This means:
- Daily backups are stored for exactly 2 weeks
- After 14 days, backups are automatically deleted from S3
- This provides a rolling window of the last 2 weeks of database states

### Manual Backup

If needed, you can manually trigger a backup using:

```bash
# Run a manual backup
docker exec vaccineapi-db-1 sh -c "mysqldump -u root -ptest vaccineapi | gzip > /tmp/vaccineapi_backup_$(date +%F).sql.gz"
docker cp vaccineapi-db-1:/tmp/vaccineapi_backup_$(date +%F).sql.gz .
```

### Disaster Recovery

In case of VM/server crash:
1. Provision a new EC2 instance
2. Clone the VaccineAPI repository
3. Restore the latest database backup from S3
4. Deploy the application using Docker Compose

## Database Schema Changes

### SQL Statements for Schema Modifications

```sql
ALTER TABLE clinics
DROP COLUMN OffDays;

ALTER TABLE doctors
DROP COLUMN SignatureImage;

ALTER TABLE childs
DROP COLUMN PreferredSchedule;

ALTER TABLE childs
DROP COLUMN PreferredDayOfReminder;

ALTER TABLE childs
DROP COLUMN PreferredDayOfWeek;

ALTER TABLE doses
DROP COLUMN IsSpecial;

ALTER TABLE doctors
DROP COLUMN IsApproved;

ALTER TABLE `childs` ADD `Agent` LONGTEXT NOT NULL DEFAULT '' AFTER `Guardian`;

CREATE TABLE `cities` (
  `Id` INT NOT NULL AUTO_INCREMENT,
  `Name` VARCHAR(255) NOT NULL,
  PRIMARY KEY (`Id`)
);

CREATE TABLE Invoices (
    Id INT NOT NULL AUTO_INCREMENT,
    InvoiceId VARCHAR(255) NOT NULL,
    Amount DECIMAL(10, 2) NOT NULL,
    ChildId INT NOT NULL,
    DoctorId INT NOT NULL,
    ClinicId INT NOT NULL,
    DoseId INT NOT NULL,
    PRIMARY KEY (Id)
);

CREATE TABLE `__efmigrationshistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL
);

INSERT INTO `__efmigrationshistory` (`MigrationId`, `ProductVersion`) VALUES
('20250127045531_InitialCreate', '7.0.2');

ALTER TABLE `__efmigrationshistory`
  ADD PRIMARY KEY (`MigrationId`);
```
