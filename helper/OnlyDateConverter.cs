using System;
using System.Globalization;
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

        // Day fields are written as dd-MM-yyyy but clients may send the canonical yyyy-MM-dd too.
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var text = (reader.Value as string ?? "").Trim();
                if (text.Length == 10 && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                {
                    return day;
                }
            }
            // A Day never keeps a time: an instant ("...Z") becomes its PKT day, a bare time is dropped.
            var parsed = base.ReadJson(reader, objectType, existingValue, serializer);
            return parsed is DateTime dt ? Pkt.DayOf(dt) : parsed;
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