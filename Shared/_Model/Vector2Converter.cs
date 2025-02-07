using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EtAlii.Adp;

public class Vector2Converter : JsonConverter<Vector2>
{
    public override Vector2 Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        var x = 0f;
        var y = 0f;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndObject:
                    return new Vector2(x, y);
                case JsonTokenType.PropertyName:
                {
                    var propertyName = reader.GetString();
                    reader.Read();

                    switch (propertyName)
                    {
                        case "X":
                            x = reader.GetSingle();
                            break;
                        case "Y":
                            y = reader.GetSingle();
                            break;
                    }

                    break;
                }
            }
        }

        throw new JsonException();
    }

    public override void Write(
        Utf8JsonWriter writer,
        Vector2 v,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("X", v.X);
        writer.WriteNumber("Y", v.Y);
        writer.WriteEndObject();
    }
}