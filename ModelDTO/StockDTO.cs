using System.ComponentModel.DataAnnotations;

namespace VaccineAPI.ModelDTO
{
 public class StockDTO
{
    public int Id { get; set; }
    public long BrandId { get; set; }
    public string BrandName { get; set; }
    public int Quantity { get; set; }
    public decimal StockAmount { get; set; } 
    public int BillId { get; set; }
    public string BillNo { get; set; }
    public string Supplier { get; set; }
    public DateTime BillDate { get; set; }
    public bool IsPaid { get; set; }
    public DateTime PaidDate { get; set; }
    public long DoctorId { get; set; }
    public string VaccineName { get; set; }
    // public DateTime Date { get; set; } 
}
}