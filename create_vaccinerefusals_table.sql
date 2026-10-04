-- Patient-refused-at-home requests (PA -> doctor approval). Table name must be all lowercase.
CREATE TABLE IF NOT EXISTS vaccinerefusals (
    Id BIGINT NOT NULL AUTO_INCREMENT,
    AssignmentId BIGINT NOT NULL,
    ChildId BIGINT NOT NULL,
    DoctorId BIGINT NOT NULL,
    PaId BIGINT NOT NULL,
    Reason VARCHAR(500) NULL,
    Status VARCHAR(20) NOT NULL DEFAULT 'Pending',
    RequestedAt DATETIME NOT NULL,
    ResolvedAt DATETIME NULL,
    RejectionNote VARCHAR(500) NULL,
    PRIMARY KEY (Id),
    INDEX ix_vaccinerefusals_assignment (AssignmentId),
    INDEX ix_vaccinerefusals_child (ChildId),
    INDEX ix_vaccinerefusals_doctor_status (DoctorId, Status)
);
