using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    [Table("managerpermissions")]
    public class ManagerPermission
    {
        public long Id { get; set; }
        public long ManagerId { get; set; }
        [ForeignKey("ManagerId")]
        public Manager Manager { get; set; } = null!;

        // PA Oversight
        public bool ViewPaAssignmentStatus { get; set; }
        public bool ReassignPaTask { get; set; }
        public bool ViewFeedbackResponseTracker { get; set; }
        public bool SendFeedbackEmail { get; set; }
        public bool SendFeedbackWhatsApp { get; set; }
        public bool ManagePaClinicAssignments { get; set; }
    }
}
