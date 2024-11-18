using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace VaccineAPI.Models
{
    public class Invoice
    {
        public long Id { get; set; }
        public string InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public long ChildId { get; set; }
        public long DoctorId { get; set; }

        public long ClinicId { get; set; }

        public long DoseId { get; set; }
    }
}