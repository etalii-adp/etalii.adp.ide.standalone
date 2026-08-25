using System.Text.Json;
using Xunit;

namespace EtAlii.Adp.Tests;

public class ShortGuidJsonConverterTests
{

    [Fact]
    public void Serialize_WritesTheBase36StringForm()
    {
        var shortGuid = ShortGuid.NewShortGuid();

        var json = JsonSerializer.Serialize(shortGuid);

        Assert.Equal($"\"{shortGuid}\"", json);
    }

    [Fact]
    public void Deserialize_FromItsOwnSerializedForm_RoundTripsExactly()
    {
        var shortGuid = ShortGuid.NewShortGuid();
        var json = JsonSerializer.Serialize(shortGuid);

        var result = JsonSerializer.Deserialize<ShortGuid>(json);

        Assert.Equal(shortGuid, result);
    }

    [Fact]
    public void Deserialize_WithWrongLengthString_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShortGuid>("\"tooshort\""));
    }

    [Fact]
    public void SerializeDeserialize_WhenEmbeddedInAnotherType_RoundTripsExactly()
    {
        var wrapper = new ShortGuidJsonConverterWrapper(ShortGuid.NewShortGuid());

        var json = JsonSerializer.Serialize(wrapper);
        var result = JsonSerializer.Deserialize<ShortGuidJsonConverterWrapper>(json);

        Assert.Equal(wrapper, result);
    }
}
