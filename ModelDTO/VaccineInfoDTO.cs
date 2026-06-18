namespace VaccineAPI.ModelDTO
{

    public class VaccineInfoDTO
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string ProtectsAgainstHtml { get; set; } = "";
        public string WhoShouldGetItHtml { get; set; } = "";
        public string ScheduleHtml { get; set; } = "";
        public string WhyVaccinateHtml { get; set; } = "";
        public string ImportantNoteHtml { get; set; } = "";
    }

}
