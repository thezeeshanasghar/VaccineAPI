using Newtonsoft.Json.Converters;

namespace VaccineAPI
{
    public class OnlyDateConverter : IsoDateTimeConverter
    {
        public OnlyDateConverter()
        {
            DateTimeFormat = "dd-MM-yyyy";
        }
    }
}