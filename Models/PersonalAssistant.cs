namespace VaccineAPI.Models
{
public class PersonalAssistant
{
    public long Id { get; set; }
    public string Name { get; set; }
    public long DoctorId { get; set; } // Foreign key to Doctor
    public long UserId { get; set; } 
    public User User { get; set; }
    public Doctor Doctor { get; set; }
}
}