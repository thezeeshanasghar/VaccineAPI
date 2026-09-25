namespace VaccineAPI.ModelDTO
{

    public class VaccineDTO
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public int MinAge { get; set; }
        public int? MaxAge { get; set; }

        public int NumOfDoses { get; set; }

        public int NumOfBrands { get; set; }
        public bool isInfinite { get; set; }
        public int Validity { get; set; }
        public int? Type { get; set; }

        // See Vaccine.ContainsDTaP — generic "this vaccine contains a DTaP/DPT component"
        // marker, admin/doctor-toggled. Drives the combo-coverage grey-out check.
        public bool ContainsDTaP { get; set; }
    }

}