namespace VaccineAPI.ModelDTO
{
    public class SupplierDTO
    {
        public long? Id { get; set; }
        public long DoctorId { get; set; }
        public string Name { get; set; } = "";
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? BankAccount { get; set; }
        public decimal? OpeningBalance { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
