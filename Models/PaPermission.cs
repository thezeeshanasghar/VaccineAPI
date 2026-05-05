using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class PaPermission
    {
        public long Id { get; set; }
        public long PaId { get; set; }
        [ForeignKey("PaId")]
        public PersonalAssistant PersonalAssistant { get; set; } = null!;

        // Patient Management
        public bool SearchPatient { get; set; }
        public bool AddPatient { get; set; }
        public bool EditPatient { get; set; }
        public bool DeletePatient { get; set; }
        public bool ActivatePatient { get; set; }
        public bool DeactivatePatient { get; set; }
        public bool DownloadPidPdf { get; set; }
        public bool CallPatient { get; set; }
        public bool SendSmsPatient { get; set; }
        public bool ViewUnapproved { get; set; }
        public bool ApproveUnapproved { get; set; }

        // Vaccination
        public bool ViewSchedule { get; set; }
        public bool GiveVaccine { get; set; }
        public bool UngiveVaccine { get; set; }
        public bool SkipVaccine { get; set; }
        public bool UnskipVaccine { get; set; }
        public bool RescheduleVaccine { get; set; }
        public bool BulkGiveVaccines { get; set; }
        public bool BulkUngiveVaccines { get; set; }
        public bool BulkReschedule { get; set; }
        public bool FillVaccineDetails { get; set; }
        public bool AddVaccineParams { get; set; }
        public bool ApproveVaccineRecord { get; set; }
        public bool PrintSchedulePdf { get; set; }
        public bool AddSpecialDoses { get; set; }
        public bool EditVaccineSchedule { get; set; }

        // Follow-Up
        public bool ViewFollowUps { get; set; }
        public bool AddFollowUp { get; set; }
        public bool DeleteFollowUp { get; set; }
        public bool DownloadFollowUpPdf { get; set; }

        // Immunization Cards
        public bool DownloadImmunizationCards { get; set; }

        // Growth
        public bool ViewGrowth { get; set; }
        public bool AddGrowthRecord { get; set; }

        // Alerts & Bulk Messaging
        public bool ViewAlerts { get; set; }
        public bool SendBulkEmail { get; set; }
        public bool OpenWhatsApp { get; set; }
        public bool DownloadAlertCsv { get; set; }
        public bool RetryMessage { get; set; }

        // Invoice
        public bool ManageInvoice { get; set; }

        // Clinics
        public bool AddClinic { get; set; }
        public bool EditClinic { get; set; }
        public bool DeleteClinic { get; set; }
        public bool SetClinicOnline { get; set; }

        // Vaccine Schedule Templates
        public bool AddScheduleTemplate { get; set; }
        public bool EditScheduleTemplate { get; set; }
        public bool SetVacationDates { get; set; }
        public bool ApplySessionTimings { get; set; }

        // Stock Management
        public bool ViewStock { get; set; }
        public bool UpdateSalePrice { get; set; }
        public bool AddBrand { get; set; }
        public bool EditBrand { get; set; }
        public bool AddPurchaseBill { get; set; }
        public bool EditPurchaseBill { get; set; }
        public bool DeletePurchaseBill { get; set; }
        public bool ApprovePurchaseBill { get; set; }
        public bool AdjustStock { get; set; }
        public bool ViewAdjustHistory { get; set; }
        public bool TransferStock { get; set; }
        public bool ViewTransferHistory { get; set; }
        public bool AddDirectSale { get; set; }
        public bool ViewDirectSaleHistory { get; set; }
        public bool DownloadStockReport { get; set; }

        // Analytics
        public bool ViewAnalytics { get; set; }
    }
}
