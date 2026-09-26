using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldcrossInfoDisplay;

internal sealed class FlexibleInt32JsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number) &&
            number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue)
            return decimal.ToInt32(number);

        if (reader.TokenType == JsonTokenType.String &&
            decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) &&
            text == decimal.Truncate(text) && text is >= int.MinValue and <= int.MaxValue)
            return decimal.ToInt32(text);

        throw new JsonException("Expected an integral 32-bit number.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class FlexibleInt64JsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number) &&
            number == decimal.Truncate(number) && number is >= long.MinValue and <= long.MaxValue)
            return decimal.ToInt64(number);

        if (reader.TokenType == JsonTokenType.String &&
            decimal.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) &&
            text == decimal.Truncate(text) && text is >= long.MinValue and <= long.MaxValue)
            return decimal.ToInt64(text);

        throw new JsonException("Expected an integral 64-bit number.");
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class FlexibleWorldcrossEventKindConverter : JsonConverter<WorldcrossEventKind>
{
    public override WorldcrossEventKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (Enum.TryParse<WorldcrossEventKind>(text, ignoreCase: true, out var named) &&
                Enum.IsDefined(named))
                return named;

            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                return FromNumeric(numeric);

            return WorldcrossEventKind.Unknown;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var value))
            return FromNumeric(value);

        throw new JsonException("Expected a relay event kind name or number.");
    }

    public override void Write(Utf8JsonWriter writer, WorldcrossEventKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());

    private static WorldcrossEventKind FromNumeric(decimal value) =>
        value == decimal.Truncate(value) && value is >= int.MinValue and <= int.MaxValue &&
        Enum.IsDefined(typeof(WorldcrossEventKind), decimal.ToInt32(value))
            ? (WorldcrossEventKind)decimal.ToInt32(value)
            : WorldcrossEventKind.Unknown;
}
