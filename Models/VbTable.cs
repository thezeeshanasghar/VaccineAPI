namespace VaccineAPI.Models
{
public class VbTable
{
    public long Id { get; set; }
    public long BrandId { get; set; }
    public long VaccineId { get; set; }

    public virtual Brand Brand { get; set; }
    public virtual Vaccine Vaccine { get; set; }
}
}