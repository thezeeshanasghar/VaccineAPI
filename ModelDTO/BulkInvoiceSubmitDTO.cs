using System;
using System.Collections.Generic;

namespace VaccineAPI.ModelDTO
{
    public class BulkInvoiceSubmitDTO
    {
        public List<ScheduleAmountItem> Schedules { get; set; } = new();
        public long ChildId { get; set; }
        public long DoctorId { get; set; }
        public long? PaId { get; set; }
        public long? ClinicId { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal ConsultationFee { get; set; }
    }

    public class ScheduleAmountItem
    {
        public long Id { get; set; }
        public decimal Amount { get; set; }
    }
}
