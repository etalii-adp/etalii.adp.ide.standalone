using System.Text.Json;
using System.Text.Json.Serialization;

namespace EtAlii.Adp;

/// <summary>
/// Serializes a <see cref="ShortGuid"/> as its base36 string form (the same
/// representation <see cref="ShortGuid.ToString()"/>/<see cref="ShortGuid.Parse(string, IFormatProvider?)"/>
/// already use), so any JSON document containing one stays as compact/readable as the type's own string form.
/// </summary>
public sealed class ShortGuidJsonConverter : JsonConverter<ShortGuid>
{
    public override ShortGuid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException($"Expected a {nameof(ShortGuid)} string value.");
        return ShortGuid.TryParse(text, provider: null, out var result)
            ? result
            : throw new JsonException($"'{text}' is not a valid {nameof(ShortGuid)}.");
    }

    public override void Write(Utf8JsonWriter writer, ShortGuid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
