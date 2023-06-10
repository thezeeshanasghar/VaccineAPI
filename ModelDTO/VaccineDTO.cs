namespace VaccineAPI.ModelDTO
{

    public class VaccineDTO
    {
        public long Id { get; set; }
        public string Name { get; set; } 
        public int MinAge { get; set; }
        public int? MaxAge { get; set; }
        
        public int NumOfDoses { get; set; }

        public int NumOfBrands { get; set; }
        public bool isInfinite { get; set; }

    }

}