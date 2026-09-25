using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>
/// The field-service example (Requirement 11), held to what its readme and the requirement claim.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are not fixtures.</b> The fixtures beside this file each carry one construct or one
/// breach; the example is a document a reader opens, and what is tested here is only that the
/// claims made about it stay true - that it breaks no rule, that every type and relation is in it,
/// and that the navigation round trip the cycle ruling exists to allow is really there.
/// </para>
/// <para>
/// <b>Every claim is asserted from the parsed model rather than by eye</b>, and the type and
/// relation cases walk <see cref="FdgElementTypes.All"/> and <see cref="FdgRelations.All"/>
/// rather than a list written here, so a sixth type or relation joins these cases the moment it
/// is declared - and fails them until the example carries it.
/// </para>
/// <para>
/// <b>A missing example FAILS rather than skips</b>: see <see cref="FieldServiceExample"/>, which
/// every test reading the example shares.
/// </para>
/// </remarks>
public class FdgExampleTests
{
    private static LineDocument Document() => LineDocument.Parse(File.ReadAllText(FieldServiceExample.Path));

    private static FdgModel Model() => FdgParser.Parse(Document());

    public static TheoryData<string> EveryElementType => [.. FdgElementTypes.All];

    public static TheoryData<string> EveryRelation => [.. FdgRelations.All.Select(relation => relation.Id)];

    /// <summary>The example reads cleanly: no entry is passed over.</summary>
    [Fact]
    public void TheExample_ReadsWithoutAProblem()
    {
        var model = Model();

        Assert.Equal(1, model.Version);
        Assert.NotEmpty(model.Elements);
        Assert.Empty(model.Problems);
    }

    /// <summary>Requirement 11.4: the validator reports nothing for the example.</summary>
    [Fact]
    public void TheValidator_ReportsNothingForTheExample()
    {
        var breaches = FdgValidator.Validate(Document());

        Assert.True(
            breaches.Count == 0,
            "The example breaks the rules it exists to demonstrate: "
            + string.Join("; ", breaches.Select(breach => $"{breach.RuleId} at line {breach.Line + 1}: {breach.Message}")));
    }

    /// <summary>Requirement 11.2: every element type appears at least once.</summary>
    [Theory]
    [MemberData(nameof(EveryElementType))]
    public void EveryElementType_AppearsAtLeastOnce(string type)
    {
        Assert.Contains(Model().Elements, element => element.Type == type);
    }

    /// <summary>Requirement 11.2: every allowed relation appears at least once.</summary>
    [Theory]
    [MemberData(nameof(EveryRelation))]
    public void EveryRelation_AppearsAtLeastOnce(string relation)
    {
        Assert.Contains(Model().Connections, connection => connection.Type == relation);
    }

    /// <summary>
    /// Requirement 11.2 names two cases inside the relations: Function to Function, and nested
    /// Data Elements. Each relation appearing is not enough to show either, since both can
    /// appear with another source.
    /// </summary>
    [Fact]
    public void AFunctionOwnsAFunction_AndADataElementOwnsADataElement()
    {
        var model = Model();
        var typeOf = model.Elements.ToDictionary(element => element.Id, element => element.Type, StringComparer.Ordinal);

        Assert.Contains(model.Connections, connection =>
            connection.Type == FdgConnectionTypes.OwnsFunction
            && typeOf[connection.From] == FdgElementTypes.Function
            && typeOf[connection.To] == FdgElementTypes.Function);
        Assert.Contains(model.Connections, connection =>
            connection.Type == FdgConnectionTypes.OwnsData
            && typeOf[connection.From] == FdgElementTypes.DataElement
            && typeOf[connection.To] == FdgElementTypes.DataElement);
    }

    /// <summary>
    /// Requirement 11.2: the round trip of navigation - task list, open task, task detail, back,
    /// task list - is there edge by edge, and it is NOT an ownership cycle.
    /// </summary>
    /// <remarks>
    /// The presence half comes first, and is what makes the absence half evidence: an empty
    /// cycle list over a document with no loop in it proves nothing about the Shows exemption.
    /// </remarks>
    [Fact]
    public void TheNavigationRoundTrip_IsThere_AndIsNotAnOwnershipCycle()
    {
        var model = Model();
        (string From, string Type, string To)[] roundTrip =
        [
            ("task-list", FdgConnectionTypes.UiChild, "task-row"),
            ("task-row", FdgConnectionTypes.OwnsAction, "open-task"),
            ("open-task", FdgConnectionTypes.Shows, "task-detail"),
            ("task-detail", FdgConnectionTypes.OwnsAction, "back-to-list"),
            ("back-to-list", FdgConnectionTypes.Shows, "task-list"),
        ];

        Assert.All(roundTrip, edge => Assert.Contains(model.Connections, connection =>
            connection.From == edge.From && connection.Type == edge.Type && connection.To == edge.To));
        Assert.Equal(roundTrip[0].From, roundTrip[^1].To);

        Assert.Empty(FdgOwnership.CyclesIn(model));
    }

    /// <summary>
    /// Requirement 11.2 and the design: named connections, Descriptions on elements and on
    /// connections, and two Comments.
    /// </summary>
    [Fact]
    public void TheExample_CarriesNamesDescriptionsAndComments()
    {
        var model = Model();

        Assert.Contains(model.Connections, connection => !string.IsNullOrEmpty(connection.Name));
        Assert.Contains(model.Elements, element => !string.IsNullOrEmpty(element.Description));
        Assert.Contains(model.Connections, connection => !string.IsNullOrEmpty(connection.Description));
        Assert.Equal(2, model.Elements.Count(element => element.Type == FdgElementTypes.Comment));
    }
}
