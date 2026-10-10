using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GTMP.Server.MasterApi.Infrastructure;

/// <summary>
/// Writes <see cref="DateTime"/> as "yyyy-MM-dd HH:mm:ss" and reads the same,
/// with ISO 8601 as a tolerated fallback.
/// </summary>
/// <remarks>
/// The launcher pins this exact pattern through a Newtonsoft
/// <c>IsoDateTimeConverter</c> with <c>DateTimeFormat = "yyyy-MM-dd HH:mm:ss"</c>.
/// The default System.Text.Json writer emits ISO 8601 with fractional seconds,
/// which that converter rejects on read.
/// </remarks>
public sealed class MicrosoftDateTimeConverter : JsonConverter<DateTime>
{
    private const string Pattern = "yyyy-MM-dd HH:mm:ss";

    /// <inheritdoc />
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        if (DateTime.TryParseExact(raw, Pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
        {
            return loose;
        }

        throw new JsonException($"'{raw}' is neither '{Pattern}' nor a parseable date.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(Pattern, CultureInfo.InvariantCulture));
    }
}
