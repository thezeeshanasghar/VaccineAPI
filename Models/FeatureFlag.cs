using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // One row per switchable behaviour (PA accountability rollout). Created by add_featureflags_and_financialaudit.sql.
    [Table("featureflags")]
    public class FeatureFlag
    {
        [Key, MaxLength(80)]
        public string FlagKey { get; set; } = "";
        public bool IsOn { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        [MaxLength(255)]
        public string? Note { get; set; }
    }
}
