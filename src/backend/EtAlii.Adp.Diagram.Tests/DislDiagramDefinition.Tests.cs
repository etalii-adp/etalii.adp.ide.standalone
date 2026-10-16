using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The mappings every module with a bundled definition shares: a menu entry, a row and a finding's
/// severity as the host sends them. The derivations themselves are the DISL runtime's, and each
/// module's tests read them from its own definition.
/// </summary>
public class DislDiagramDefinitionTests
{
    /// <summary>
    /// Never loaded: a definition is read only when something asks for it, and a menu entry with no
    /// standing operation needs none.
    /// </summary>
    private readonly DislDiagramDefinition _unloaded = new(typeof(DislDiagramDefinitionTests).Assembly, "absent.dis", "x-absent");

    [Fact]
    public void AShortcut_IsSentAsABrowserSpellsTheKey_WithItsModifiers()
    {
        // Arrange.
        var entry = Entry(shortcut: DerivedShortcut.Parse("Alt+Up"));

        // Act.
        var action = _unloaded.Action(entry);

        // Assert: a keypress of Alt and the up arrow is what the client sends, and it has to match.
        Assert.NotNull(action.Shortcut);
        Assert.True(action.Shortcut.Matches(new ContextShortcutDefinition("ArrowUp", Alt: true)), $"Sent as {action.Shortcut}.");
    }

    [Fact]
    public void AnEntryWithoutAShortcut_IsSentWithoutOne()
    {
        // Act.
        var action = _unloaded.Action(Entry(shortcut: null));

        // Assert.
        Assert.Null(action.Shortcut);
    }

    [Fact]
    public void AnUnavailableEntry_CarriesItsReason_AndAnAvailableOneNone()
    {
        // Act.
        var unavailable = _unloaded.Action(Entry(available: false, reason: "Nothing to arrange."));
        var available = _unloaded.Action(Entry(available: true, reason: ""));

        // Assert.
        Assert.Equal(("Nothing to arrange.", false), (unavailable.UnavailableReason, unavailable.Available));
        Assert.Equal(("", true), (available.UnavailableReason, available.Available));
    }

    [Theory]
    [InlineData(null, false, ContextPropertyEditor.Line)]
    [InlineData("text", false, ContextPropertyEditor.Line)]
    [InlineData("number", false, ContextPropertyEditor.Line)]
    [InlineData("textarea", false, ContextPropertyEditor.Text)]
    [InlineData("select", false, ContextPropertyEditor.Choice)]
    [InlineData("switch", false, ContextPropertyEditor.Toggle)]
    [InlineData("tags", false, ContextPropertyEditor.Tags)]
    [InlineData("slider", false, ContextPropertyEditor.Slider)]
    [InlineData("textarea", true, ContextPropertyEditor.Choice)]
    public void ARow_IsDrawnWithTheEditorOfItsWidget(string? widget, bool retypes, ContextPropertyEditor expected)
    {
        // Act.
        var row = DislDiagramDefinition.Row(new DerivedRow("p", "Label", "value", widget, "", "General", ["a", "b"], retypes));

        // Assert.
        Assert.Equal(expected, row.Editor);
    }

    [Theory]
    [InlineData("select", true)]
    [InlineData("tags", true)]
    [InlineData("slider", true)]
    [InlineData("textarea", false)]
    [InlineData("switch", false)]
    public void OnlyAChoiceTagsAndASlider_CarryCandidates(string widget, bool carries)
    {
        // Act.
        var row = DislDiagramDefinition.Row(new DerivedRow("p", "Label", "value", widget, "", "General", ["a", "b"]));

        // Assert.
        Assert.Equal(carries ? ["a", "b"] : null, row.Candidates);
    }

    [Theory]
    [InlineData("error", DiagramProblemSeverity.Error)]
    [InlineData("warning", DiagramProblemSeverity.Warning)]
    [InlineData("info", DiagramProblemSeverity.Info)]
    [InlineData("hint", DiagramProblemSeverity.Info)]
    public void AFinding_IsCountedAtItsSeverity(string severity, DiagramProblemSeverity expected)
    {
        // Act.
        var counted = DislDiagramDefinition.SeverityOf(new DislFinding("x.code", "rule", severity, "Message.", [], Line: 1));

        // Assert.
        Assert.Equal(expected, counted);
    }

    private static DerivedMenuEntry Entry(DerivedShortcut? shortcut = null, bool available = true, string reason = "") =>
        new("arrange", "operation", "arrange", "Arrange", "mdi-auto-fix", shortcut, available, reason, new Dictionary<string, object?>());
}
