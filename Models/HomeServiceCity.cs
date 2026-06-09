namespace VaccineAPI.Models
{
    public class HomeServiceCity
    {
        public long Id { get; set; }
        public string CityName { get; set; } = "";
        public long DoctorId { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
