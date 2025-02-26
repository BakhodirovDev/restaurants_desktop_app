using System.Text.Json.Serialization;
using System.Text.Json;
using System.Globalization;

namespace Restaurants.CustomConverter;

public class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private const string DateFormat = "dd.MM.yyyy HH:mm:ss";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string dateString = reader.GetString();
        if (DateTimeOffset.TryParseExact(dateString, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset result))
        {
            return result;
        }
        throw new JsonException($"Invalid DateTimeOffset format: {dateString}");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(DateFormat));
    }
}
