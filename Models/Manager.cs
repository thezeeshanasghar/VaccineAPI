namespace VaccineAPI.Models
{
    public class Manager
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsVerified { get; set; }
        public bool IsActive { get; set; } = true;
        public string ProfileImage { get; set; } = "Resources/Images/avatar.png";
        public long DoctorId { get; set; }
        public long UserId { get; set; }
        public User User { get; set; } = null!;
        public Doctor Doctor { get; set; } = null!;
    }
}
