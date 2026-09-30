using System;
using System.ComponentModel.DataAnnotations;

namespace VaccineAPI.Models
{
    // One row per accepted create request that carried a ClientRequestId. A retry with the same
    // (DoctorId, ClientRequestId) is answered from ResponseJson and changes nothing. The unique
    // index makes two concurrent submissions of the same request race on the insert: exactly one
    // transaction can commit.
    public class IdempotencyKey
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        [MaxLength(100)]
        public string ClientRequestId { get; set; } = "";
        [MaxLength(60)]
        public string Endpoint { get; set; } = "";
        public string ResponseJson { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
