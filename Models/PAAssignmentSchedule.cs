namespace VaccineAPI.Models
{
public class PAAssignmentSchedule
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public long ScheduleId { get; set; }

    public virtual PAAssignment Assignment { get; set; } = null!;
    public virtual Schedule Schedule { get; set; } = null!;
}
}
