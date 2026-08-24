namespace VaccineAPI.Models
{
    public class ManagerAccess
    {
        public long Id { get; set; }
        public long ManagerId { get; set; }
        public long ClinicId { get; set; }
        public Manager Manager { get; set; } = null!;
        public Clinic Clinic { get; set; } = null!;
    }
}
