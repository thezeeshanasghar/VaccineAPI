using System.ComponentModel.DataAnnotations;

namespace VaccineAPI.ModelDTO
{
 public class StockDTO
{
    public int Id { get; set; }
    
    // [Required]
    public long BrandId { get; set; }
    
    public string BrandName { get; set; }
    
    // [Required]
    // [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
    
   public decimal StockAmount { get; set; }  // Ensure this type matches the input

    
    // [Required]
    public int BillId { get; set; }
    
    public string BillNo { get; set; }
    public string Supplier { get; set; }
    
    public DateTime Date { get; set; }
    
    public bool IsPaid { get; set; }

    public long DoctorId { get; set; }
    public string VaccineName { get; set; }
}
}