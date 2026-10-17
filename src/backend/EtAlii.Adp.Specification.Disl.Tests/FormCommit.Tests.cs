using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// A form item at commit (DISL 0.3 §7.5): an inspector row's typed value, refused or made into a transaction, and a
/// params form's dialog and the refusal its value meets; and a label's inline edit (DISL 0.3 §6.12): the text its
/// editor opens on, and its <c>parse</c>'s <c>accepts</c>, <c>refusal</c> and <c>write</c>.
/// </summary>
public class FormCommitTests
{
    private static readonly DislSpecification Specification = Specifications.Loaded(Specifications.With("""
        "disl": "0.3",
        "metamodel": { "types": { "Gauge": { "attributes": {
          "level": { "type": "number" },
          "mode": { "type": "string" },
          "note": { "type": "string" },
          "code": { "type": "string" } } } } },
        "notation": { "nodes": { "Gauge": { "shape": "rect", "labels": [
          { "id": "caption", "text": { "cel": "self.note" }, "editable": "inline", "editText": { "cel": "self.note + '?'" },
            "parse": { "accepts": "value != '' && !value.contains('#')", "write": [ { "set": { "note": "value.trim()" } } ],
              "refusal": { "cel": "'\\'' + value + '\\' is no caption.'" } } },
          { "id": "badge", "text": { "cel": "self.code" }, "editable": "inline",
            "parse": { "write": [ { "set": { "code": "value" } } ] } } ] } } },
        "forms": {
          "gauge": {
            "for": "Gauge",
            "items": [
              { "kind": "section", "label": "Reading", "items": [
                { "attribute": "level", "label": "Level", "widget": "text",
                  "parse": {
                    "accepts": "value.trim() == '' || value.trim().matches('^[0-9]+(\\\\.[0-9]+)?$')",
                    "write": [
                      { "unset": ["level"], "when": "value.trim() == ''" },
                      { "set": { "level": "double(value.trim())" }, "when": "value.trim() != ''" } ],
                    "refusal": { "cel": "'\\'' + value + '\\' is not a level.'" } } },
                { "attribute": "mode", "label": "Mode", "widget": "select",
                  "options": [ { "value": "f", "label": "Fast" }, { "value": "s", "label": "Slow" } ],
                  "validate": [ { "timing": "commit", "rule": "value in ['f', 's']", "message": { "cel": "'\\'' + value + '\\' is not a mode.'" } } ] }
              ] },
              { "attribute": "note", "label": "Note", "widget": "text" },
              { "attribute": "code", "label": "Code", "widget": "text", "readOnlyReasons": ["The code is fixed."] },
              { "kind": "computed", "label": "Summary", "widget": "text", "value": { "cel": "self.note" } }
            ]
          },
          "pick": {
            "for": "Gauge",
            "label": "Pick a gauge",
            "icon": "mdi-target",
            "submitLabel": "Go",
            "items": [
              { "attribute": "target", "label": "Target", "widget": "text", "initial": "'G' + string(diagram.nodesOfType('Gauge').size() + 1)",
                "validate": [ { "timing": "commit", "rule": "diagram.nodesOfType('Gauge').exists(g, g.note == value)",
                  "message": { "cel": "'\\'' + value + '\\' is no gauge.'" } } ] }
            ]
          }
        }
        """));

    private static readonly DislEnv Editable = new();

    private static DislElement Gauge()
    {
        var diagram = new DislDiagram(Specification);
        return diagram.AddNode("Gauge", "g1", new Dictionary<string, object?>(StringComparer.Ordinal) { ["level"] = 1.0, ["mode"] = "f", ["note"] = "first", ["code"] = "x" });
    }

    [Fact]
    public void AParsedValue_IsWrittenByTheParsesWrite()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "level", " 2.5 ", Editable);

        // Assert.
        Assert.Null(transaction.Refusal);
        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.Equal(("g1", 2.5), (set.ElementId, set.Attributes["level"]));
    }

    [Fact]
    public void AnEmptyParsedValue_IsUnsetByTheParsesWrite()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "level", "", Editable);

        // Assert.
        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.True(set.Attributes.ContainsKey("level"));
        Assert.Null(set.Attributes["level"]);
    }

    [Fact]
    public void AValueTheParseDoesNotAccept_IsRefusedWithItsRefusal()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "level", "high", Editable);

        // Assert.
        Assert.Equal("'high' is not a level.", transaction.Refusal);
    }

    [Fact]
    public void ASelectsOption_ChosenByItsLabel_IsWrittenAsItsValue()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "mode", "Slow", Editable);

        // Assert.
        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.Equal("s", set.Attributes["mode"]);
    }

    [Fact]
    public void AValueTheValidationRefuses_IsRefusedWithItsMessage()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "mode", "Turbo", Editable);

        // Assert.
        Assert.Equal("'Turbo' is not a mode.", transaction.Refusal);
    }

    [Fact]
    public void APlainField_SetsItsAttribute()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "note", "second", Editable);

        // Assert.
        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.Equal("second", set.Attributes["note"]);
    }

    [Fact]
    public void ARowWithAReadOnlyReason_IsRefusedWithIt()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "code", "y", Editable);

        // Assert.
        Assert.Equal("The code is fixed.", transaction.Refusal);
    }

    [Fact]
    public void AComputedItemWithoutAParse_IsRefused()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "Summary", "anything", Editable);

        // Assert.
        Assert.NotNull(transaction.Refusal);
        Assert.Empty(transaction.Changes);
    }

    [Fact]
    public void ARowOfAReadOnlyDiagram_IsRefusedWithTheStandardSentence()
    {
        // Act.
        var transaction = FormCommit.Row(Specification, Gauge(), "note", "second", new DislEnv(ReadOnly: true));

        // Assert.
        Assert.Equal("This diagram is read-only.", transaction.Refusal);
    }

    [Fact]
    public void AParamsForm_GivesTheDialogAHostShows()
    {
        // Act.
        var request = FormCommit.Request(Specification, "pick", Gauge(), Editable);

        // Assert.
        Assert.NotNull(request);
        Assert.Equal(("Pick a gauge", "mdi-target", "Go"), (request.Label, request.Icon, request.SubmitLabel));
        var field = Assert.Single(request.Fields);
        Assert.Equal(("target", "Target", "text", (object?)"G2"), (field.Attribute, field.Label, field.Widget, field.Initial));
    }

    [Fact]
    public void AParamsFormsValue_IsRefusedByItsValidation_AndAcceptedWhenItHolds()
    {
        // Act.
        var refused = FormCommit.Refusal(Specification, "pick", "target", "nope", Gauge(), Editable);
        var accepted = FormCommit.Refusal(Specification, "pick", "target", "first", Gauge(), Editable);

        // Assert.
        Assert.Equal("'nope' is no gauge.", refused);
        Assert.Null(accepted);
    }

    [Fact]
    public void AParamsForm_WithoutAnElement_ReadsTheDiagramGiven()
    {
        // Arrange.
        var diagram = Gauge().Diagram;

        // Act.
        var request = FormCommit.Request(Specification, "pick", null, Editable, diagram);
        var refused = FormCommit.Refusal(Specification, "pick", "target", "nope", null, Editable, diagram);

        // Assert.
        Assert.Equal("G2", Assert.Single(request!.Fields).Initial);
        Assert.Equal("'nope' is no gauge.", refused);
    }

    [Fact]
    public void ALabelsEdit_ThatItsParseDoesNotAccept_IsRefusedWithItsRefusal()
    {
        // Act.
        var refused = FormCommit.Label(Specification, Gauge(), "caption", "a # b", Editable);

        // Assert.
        Assert.Equal("'a # b' is no caption.", refused.Refusal);
        Assert.Empty(refused.Changes);
    }

    [Fact]
    public void ALabelsEdit_ThatItsParseAccepts_IsWrittenByItsWrite()
    {
        // Act.
        var transaction = FormCommit.Label(Specification, Gauge(), "caption", " second ", Editable);

        // Assert.
        Assert.Null(transaction.Refusal);
        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.Equal(("g1", "second"), (set.ElementId, set.Attributes["note"]));
    }

    [Fact]
    public void ALabelsParse_WithoutAccepts_TakesEveryText()
    {
        // Act.
        var transaction = FormCommit.Label(Specification, Gauge(), "badge", "", Editable);

        // Assert.
        var set = Assert.IsType<DislChange.Set>(Assert.Single(transaction.Changes));
        Assert.Equal("", set.Attributes["code"]);
    }

    [Fact]
    public void ALabelsEditor_OpensOnItsEditText()
    {
        // Act.
        var text = FormCommit.EditText(Specification, Gauge(), "caption", Editable);

        // Assert.
        Assert.Equal("first?", text);
    }
}
