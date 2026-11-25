using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The document id written into a timeline that has none (FBL 17.2, etalii.adp spec 092, T016): on a line of its own
/// after the schema marker, or its value replaced where the key is present; every other byte kept.
/// </summary>
public class DocumentIdWritingTests
{
    [Theory]
    [InlineData("timeline: 1\nelements:\n  - id: a\n    type: milestone\n", "timeline: 1\nid: d0c1d2e3\nelements:\n  - id: a\n    type: milestone\n")]
    [InlineData("timeline: 1\r\nelements: []\r\n", "timeline: 1\r\nid: d0c1d2e3\r\nelements: []\r\n")]
    [InlineData("timeline: 1\nid: old12345\nelements: []\n", "timeline: 1\nid: d0c1d2e3\nelements: []\n")]
    public void ADocumentId_IsWrittenAfterTheSchemaMarker_OrInPlaceOfTheOneThere(string before, string after)
    {
        // Arrange.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes(before), TimelineBody.Binding);

        // Act.
        var result = body.Change(new ModelChange.SetDocumentId("d0c1d2e3"));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal(after, Encoding.UTF8.GetString(body.Bytes));
        Assert.Equal("d0c1d2e3", body.Model.DocumentId);
    }
}
