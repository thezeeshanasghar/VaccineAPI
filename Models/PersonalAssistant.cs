namespace VaccineAPI.Models
{
    public class PersonalAssistant
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; } = "";
        public bool AllowStock { get; set; }
        public bool AllowAlert { get; set; }
        public bool AllowClinic { get; set; }
        public bool AllowSchedule { get; set; }
        public bool AllowVacation { get; set; }
        public bool AllowAnalytics { get; set; }
        public bool AllowChild { get; set; }
        public bool IsVerified { get; set; }
        public long DoctorId { get; set; } 
        public long UserId { get; set; }
        public User User { get; set; }
        public Doctor Doctor { get; set; }
    }
}