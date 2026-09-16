using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VaccineAPI
{
    public class OnlyDateConverter : IsoDateTimeConverter
    {
        public OnlyDateConverter()
        {
            DateTimeFormat = "dd-MM-yyyy";
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }
            base.WriteJson(writer, value, serializer);
        }
    }
}