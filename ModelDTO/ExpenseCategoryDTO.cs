namespace VaccineAPI.ModelDTO
{
    public class ExpenseCategoryDTO
    {
        public long Id { get; set; }
        public long DoctorId { get; set; }
        public string Name { get; set; } = "";
        public bool IsActive { get; set; } = true;
    }
}
