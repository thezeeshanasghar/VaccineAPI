using System;

namespace VaccineAPI.Models
{
    public class ExpenseCategory
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Doctor Doctor { get; set; } = null!;
    }
}
