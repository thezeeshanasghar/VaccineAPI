using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class RefrigeratorCreateDTO
    {
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public string Name { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Type { get; set; } = "Refrigerator";
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public string? Location { get; set; }
    }

    public class RefrigeratorResponseDTO
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public string Name { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Type { get; set; } = "";
        public decimal MinTemp { get; set; }
        public decimal MaxTemp { get; set; }
        public string? Location { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class TemperatureReadingCreateDTO
    {
        public long RefrigeratorId { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public decimal Temperature { get; set; }
        public DateTime RecordedDate { get; set; }
        public string RecordedTime { get; set; } = "";
        public long? RecordedByPaId { get; set; }
        public string RecordedByName { get; set; } = "";
        public string? Notes { get; set; }
    }

    public class TemperatureReadingResponseDTO
    {
        public long Id { get; set; }
        public long RefrigeratorId { get; set; }
        public string? RefrigeratorName { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public decimal Temperature { get; set; }
        public DateTime RecordedDate { get; set; }
        public string RecordedTime { get; set; } = "";
        public long? RecordedByPaId { get; set; }
        public string RecordedByName { get; set; } = "";
        public string? Notes { get; set; }
        public bool IsInRange { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ColdChainApprovalSubmitDTO
    {
        public string Status { get; set; } = ""; // approved / flagged / rejected
        public string? Comments { get; set; }
    }

    public class ColdChainApprovalResponseDTO
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public DateTime WeekStartDate { get; set; }
        public DateTime WeekEndDate { get; set; }
        public int TotalReadings { get; set; }
        public int InRangeCount { get; set; }
        public int OutOfRangeCount { get; set; }
        public int RequiredChecks { get; set; }
        public int MissedChecks { get; set; }
        public string Status { get; set; } = "";
        public string? DoctorComments { get; set; }
        public DateTime? ApprovalDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Drill-down extras (populated only by the clinic drill-down endpoint)
        public List<ColdChainFridgeBreakdownDTO>? FridgeBreakdown { get; set; }
        public List<TemperatureReadingResponseDTO>? ExcursionReadings { get; set; }
        public List<RefrigeratorResponseDTO>? FridgesWithNoReadings { get; set; }
    }

    // All-clinics rollup row
    public class ColdChainClinicRollupDTO
    {
        public long ClinicId { get; set; }
        public string ClinicName { get; set; } = "";
        public decimal CompliancePercent { get; set; }
        public int MissedChecks { get; set; }
        public int ExcursionCount { get; set; }
        public string Status { get; set; } = "pending";
        public int FridgeCount { get; set; }
    }

    // Per-fridge breakdown row for the single-clinic drill-down
    public class ColdChainFridgeBreakdownDTO
    {
        public long RefrigeratorId { get; set; }
        public string RefrigeratorName { get; set; } = "";
        public int InRangeCount { get; set; }
        public int TotalReadings { get; set; }
        public int RequiredChecks { get; set; }
        public int MissedChecks { get; set; }
        public decimal CompliancePercent { get; set; }
    }

    // PA entry-screen requirement status row
    public class ColdChainRequirementStatusDTO
    {
        public long RefrigeratorId { get; set; }
        public string RefrigeratorName { get; set; } = "";
        public int ReadingsToday { get; set; }
        public bool RequirementMet { get; set; }
        public string? LastReadingTime { get; set; }
    }
}
