using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
namespace VaccineAPI.ModelDTO
{
    public class InvoiceDTO
    {
        public string InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public long ChildId { get; set; }
        public long DoctorId { get; set; }
        public long ClinicId { get; set; }
        public long DoseId { get; set; }


    }
}