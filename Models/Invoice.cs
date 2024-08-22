
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    public class Invoice
    {
        public long Id { get; set; } // Primary key
        public string InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public long ChildId { get; set; } // Foreign key to the Child table
    }
}