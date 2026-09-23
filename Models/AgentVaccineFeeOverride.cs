using System.ComponentModel.DataAnnotations.Schema;

namespace VaccineAPI.Models
{
    // Per-agent, per-vaccine referral fee that overrides Agent.ReferralFeePerClient when a
    // patient's first-eligible dose (see AgentController.GetAgentReport) was that vaccine.
    // Sparse by design: only the handful of vaccines that pay differently need a row here —
    // everything else falls back to the agent's flat ReferralFeePerClient.
    [Table("agentvaccinefeeoverrides")]
    public class AgentVaccineFeeOverride
    {
        public long Id { get; set; }
        public int AgentId { get; set; }
        public virtual Agent Agent { get; set; } = null!;
        public long VaccineId { get; set; }
        public virtual Vaccine Vaccine { get; set; } = null!;
        public decimal Fee { get; set; }
    }
}
