using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaPermissionController : ControllerBase
    {
        private readonly Context _db;

        public PaPermissionController(Context db)
        {
            _db = db;
        }

        [HttpGet("{paId:long}")]
        public ActionResult<PaPermission> GetByPaId(long paId)
        {
            var perm = _db.PaPermissions.FirstOrDefault(p => p.PaId == paId);
            if (perm == null)
            {
                // Return a blank permission object so frontend knows none are set yet
                return Ok(new PaPermission { PaId = paId });
            }
            return Ok(perm);
        }

        [HttpPut("{paId:long}")]
        public ActionResult Upsert(long paId, [FromBody] PaPermission incoming)
        {
            var pa = _db.PersonalAssistant.Find(paId);
            if (pa == null)
                return NotFound(new { message = "Personal Assistant not found." });

            var existing = _db.PaPermissions.FirstOrDefault(p => p.PaId == paId);
            if (existing == null)
            {
                incoming.PaId = paId;
                incoming.Id = 0;
                _db.PaPermissions.Add(incoming);
            }
            else
            {
                existing.SearchPatient = incoming.SearchPatient;
                existing.AddPatient = incoming.AddPatient;
                existing.EditPatient = incoming.EditPatient;
                existing.DeletePatient = incoming.DeletePatient;
                existing.ActivatePatient = incoming.ActivatePatient;
                existing.DeactivatePatient = incoming.DeactivatePatient;
                existing.DownloadPidPdf = incoming.DownloadPidPdf;
                existing.CallPatient = incoming.CallPatient;
                existing.SendSmsPatient = incoming.SendSmsPatient;
                existing.ViewUnapproved = incoming.ViewUnapproved;
                existing.ApproveUnapproved = incoming.ApproveUnapproved;

                existing.ViewSchedule = incoming.ViewSchedule;
                existing.GiveVaccine = incoming.GiveVaccine;
                existing.UngiveVaccine = incoming.UngiveVaccine;
                existing.SkipVaccine = incoming.SkipVaccine;
                existing.UnskipVaccine = incoming.UnskipVaccine;
                existing.RescheduleVaccine = incoming.RescheduleVaccine;
                existing.BulkGiveVaccines = incoming.BulkGiveVaccines;
                existing.BulkUngiveVaccines = incoming.BulkUngiveVaccines;
                existing.BulkReschedule = incoming.BulkReschedule;
                existing.FillVaccineDetails = incoming.FillVaccineDetails;
                existing.AddVaccineParams = incoming.AddVaccineParams;
                existing.ApproveVaccineRecord = incoming.ApproveVaccineRecord;
                existing.PrintSchedulePdf = incoming.PrintSchedulePdf;
                existing.AddSpecialDoses = incoming.AddSpecialDoses;
                existing.EditVaccineSchedule = incoming.EditVaccineSchedule;

                existing.ViewFollowUps = incoming.ViewFollowUps;
                existing.AddFollowUp = incoming.AddFollowUp;
                existing.DeleteFollowUp = incoming.DeleteFollowUp;
                existing.DownloadFollowUpPdf = incoming.DownloadFollowUpPdf;

                existing.DownloadImmunizationCards = incoming.DownloadImmunizationCards;

                existing.ViewGrowth = incoming.ViewGrowth;
                existing.AddGrowthRecord = incoming.AddGrowthRecord;

                existing.ViewAlerts = incoming.ViewAlerts;
                existing.SendBulkEmail = incoming.SendBulkEmail;
                existing.OpenWhatsApp = incoming.OpenWhatsApp;
                existing.DownloadAlertCsv = incoming.DownloadAlertCsv;
                existing.RetryMessage = incoming.RetryMessage;

                existing.ManageInvoice = incoming.ManageInvoice;

                existing.AddClinic = incoming.AddClinic;
                existing.EditClinic = incoming.EditClinic;
                existing.DeleteClinic = incoming.DeleteClinic;
                existing.SetClinicOnline = incoming.SetClinicOnline;

                existing.AddScheduleTemplate = incoming.AddScheduleTemplate;
                existing.EditScheduleTemplate = incoming.EditScheduleTemplate;
                existing.SetVacationDates = incoming.SetVacationDates;
                existing.ApplySessionTimings = incoming.ApplySessionTimings;

                existing.ViewStock = incoming.ViewStock;
                existing.UpdateSalePrice = incoming.UpdateSalePrice;
                existing.AddBrand = incoming.AddBrand;
                existing.EditBrand = incoming.EditBrand;
                existing.AddPurchaseBill = incoming.AddPurchaseBill;
                existing.EditPurchaseBill = incoming.EditPurchaseBill;
                existing.DeletePurchaseBill = incoming.DeletePurchaseBill;
                existing.ApprovePurchaseBill = incoming.ApprovePurchaseBill;
                existing.AdjustStock = incoming.AdjustStock;
                existing.ViewAdjustHistory = incoming.ViewAdjustHistory;
                existing.TransferStock = incoming.TransferStock;
                existing.ViewTransferHistory = incoming.ViewTransferHistory;
                existing.AddDirectSale = incoming.AddDirectSale;
                existing.ViewDirectSaleHistory = incoming.ViewDirectSaleHistory;
                existing.DownloadStockReport = incoming.DownloadStockReport;

                existing.ViewAnalytics = incoming.ViewAnalytics;

                existing.DevelopmentalAssessment = incoming.DevelopmentalAssessment;
                existing.ChangePaymentMode = incoming.ChangePaymentMode;

                _db.Entry(existing).State = EntityState.Modified;
            }

            _db.SaveChanges();
            return Ok(new { message = "Permissions saved successfully." });
        }
    }
}
