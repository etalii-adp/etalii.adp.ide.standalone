using System.Text.Json;
using Xunit;

namespace EtAlii.Adp.Tests;

public class ShortGuidJsonConverterTests
{

    [Fact]
    public void Serialize_WritesTheBase36StringForm()
    {
        // Arrange.
        var shortGuid = ShortGuid.NewShortGuid();

        // Act.
        var json = JsonSerializer.Serialize(shortGuid);

        // Assert.
        Assert.Equal($"\"{shortGuid}\"", json);
    }

    [Fact]
    public void Deserialize_FromItsOwnSerializedForm_RoundTripsExactly()
    {
        // Arrange.
        var shortGuid = ShortGuid.NewShortGuid();
        var json = JsonSerializer.Serialize(shortGuid);

        // Act.
        var result = JsonSerializer.Deserialize<ShortGuid>(json);

        // Assert.
        Assert.Equal(shortGuid, result);
    }

    [Fact]
    public void Deserialize_WithWrongLengthString_ThrowsJsonException()
    {
        // Arrange, act and assert.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ShortGuid>("\"tooshort\""));
    }

    [Fact]
    public void SerializeDeserialize_WhenEmbeddedInAnotherType_RoundTripsExactly()
    {
        // Arrange.
        var wrapper = new ShortGuidJsonConverterWrapper(ShortGuid.NewShortGuid());

        // Act.
        var json = JsonSerializer.Serialize(wrapper);
        var result = JsonSerializer.Deserialize<ShortGuidJsonConverterWrapper>(json);

        // Assert.
        Assert.Equal(wrapper, result);
    }
}
