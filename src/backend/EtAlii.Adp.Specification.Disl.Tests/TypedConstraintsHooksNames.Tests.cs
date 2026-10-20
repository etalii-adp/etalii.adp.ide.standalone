using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The constraints, the behavior besides its operations, the notation the derivation reads and the viewpoints are typed
/// at load (FR-079): a position read as an expression where the loader compiled none is an error at load, and typing them
/// moves no other outcome: what the runtime threw on when it read the JSON as it ran, it still throws, at the same point.
/// </summary>
public class TypedConstraintsHooksNamesTests
{
    private const string Metamodel = """
        "metamodel": {
          "types": { "Box": { "attributes": { "name": { "type": "string" } } } },
          "relations": { "Link": { "source": "Box", "target": "Box" } }
        }
        """;

    private static DislLoadResult Load(string parts) => DislLoader.Load(Specifications.With(Metamodel + "," + parts));

    [Fact]
    public void AnExpressionRead_WhereTheLoaderCompiledNone_IsAnErrorAtLoad()
    {
        // A reason whose id is doc is not walked (§2.4), so no program is compiled for its when; the runtime used to
        // compile one silently when the reason was first named.
        var missed = Load("""
            "behavior": { "reasons": { "doc": { "message": "No.", "when": "true" } } }
            """);
        var walked = Load("""
            "behavior": { "reasons": { "busy": { "message": "No.", "when": "true" } } }
            """);

        var error = Assert.Single(missed.Errors);
        Assert.Equal("/behavior/reasons/doc/when", error.Pointer);
        Assert.Contains("the loader compiles none at this position", error.Message);
        Assert.Null(missed.Specification);
        Assert.Empty(walked.Errors);
    }

    [Fact]
    public void ALabelsAccepts_AndAComputedLegendsKeyAndOrder_AreCompiledAtLoad()
    {
        var result = Load("""
            "notation": {
              "nodes": { "Box": { "labels": [ { "id": "name", "text": { "attribute": "name" }, "parse": { "accepts": "value != ''", "write": [ { "set": { "name": "value" } } ] } } ] } },
              "canvas": { "legend": { "computed": { "key": "self.name", "order": "self.name" } } }
            }
            """);

        Assert.Empty(result.Errors);
        var specification = result.Specification!;
        Assert.NotNull(specification.ExpressionAt("/notation/nodes/Box/labels/0/parse/accepts"));
        Assert.NotNull(specification.ExpressionAt("/notation/canvas/legend/computed/key"));
        Assert.NotNull(specification.ExpressionAt("/notation/canvas/legend/computed/order"));
    }

    private const string NotAnObject = "The requested operation requires an element of type 'Object', but the target element has type 'Number'.";
    private const string NotANumber = "The requested operation requires an element of type 'Number', but the target element has type 'String'.";

    /// <summary>
    /// Each specification with a part of the wrong shape, the use that reads that part, and what loading it and then that
    /// use gave when the runtime read the JSON as it ran: System.Text.Json's own exception, or what it read instead.
    /// </summary>
    public static TheoryData<string, string, string> WrongShapes => new()
    {
        { """ "behavior": 5 """, "AfterChange", "throws InvalidOperationException: " + NotAnObject },
        { """ "behavior": 5 """, "Confirmation", "throws InvalidOperationException: " + NotAnObject },
        { """ "behavior": 5 """, "Retype", "throws InvalidOperationException: " + NotAnObject },
        { """ "behavior": { "messages": 5 } """, "ReadOnlyLabel", "throws InvalidOperationException: " + NotAnObject },
        { """ "behavior": { "reasons": 5, "operations": { "op": { "label": "Op", "for": ["Box"], "unavailable": [ { "reason": "r" } ], "actions": [] } } } """, "Refusal", "throws InvalidOperationException: " + NotAnObject },
        { """ "behavior": { "maxHookDepth": "deep", "hooks": [ { "on": "change", "for": "Box", "actions": [] } ] } """, "AfterChange", "throws InvalidOperationException: " + NotANumber },
        { """ "behavior": { "hooks": [ { "on": "change", "for": "Box", "order": "first", "actions": [] } ] } """, "AfterChange", "throws InvalidOperationException: " + NotANumber },
        { """ "behavior": { "deletion": { "Box": 5 } } """, "Confirmation", "throws InvalidOperationException: " + NotAnObject },
        { """ "behavior": { "deletion": { "Box": { "confirm": { "message": "Sure?", "threshold": "two" } } } } """, "Confirmation", "DislConfirmation { Title = Delete, Message = Sure?, ConfirmLabel = Delete, Danger = True, Count =  }" },
        { """ "behavior": { "deletion": { "Box": { "confirm": { "count": "2", "message": "Sure?", "threshold": "two" } } } } """, "Confirmation", "throws InvalidOperationException: " + NotANumber },
        { """ "behavior": { "retype": { "Box": 5 } } """, "Retype", "refused: A Box cannot become a Box." },
        { """ "constraints": { "rules": [ 5 ] } """, "Evaluate", "throws InvalidOperationException: " + NotAnObject },
        { """ "toolbox": { "tools": { "link": { "creates": "Link" } } }, "notation": 5 """, "ConnectToNew", "throws InvalidOperationException: " + NotAnObject },
        { """ "notation": 5 """, "EditText", "no text" },
        { """ "toolbox": { "tools": { "link": { "creates": "Link" } } }, "notation": { "edges": 5 } """, "ConnectToNew", "throws InvalidOperationException: " + NotAnObject },
        { """ "toolbox": { "tools": { "link": { "creates": "Link" } } }, "notation": { "edges": { "Link": 5 } } """, "ConnectToNew", "throws InvalidOperationException: " + NotAnObject },
        { """ "notation": { "canvas": { "header": 5 } } """, "Chrome", "throws InvalidOperationException: " + NotAnObject },
        { """ "notation": { "nodes": { "Box": 5 } } """, "EditText", "throws InvalidOperationException: " + NotAnObject },
        { """ "viewpoints": { "main": 5 } """, "View", "load throws InvalidOperationException: " + NotAnObject },
        { """ "persistence": 5 """, "Load", "load throws InvalidOperationException: " + NotAnObject },
    };

    [Theory]
    [MemberData(nameof(WrongShapes))]
    public void APartOfTheWrongShape_ThrowsWhatItThrew_WhereItThrewIt(string parts, string use, string expected)
    {
        Assert.Equal(expected, Outcome(parts, use));
    }

    private static string Outcome(string parts, string use)
    {
        DislLoadResult result;
        try
        {
            result = Load(parts);
        }
        catch (Exception e)
        {
            return $"load throws {e.GetType().Name}: {e.Message}";
        }
        if (result.Errors.Any()) return "load reports " + string.Join("; ", result.Errors.Select(error => error.ToString()));
        if (use == "Load") return "loads";

        var specification = result.Specification!;
        var diagram = new DislDiagram(specification);
        var box = diagram.AddNode("Box", "b", new Dictionary<string, object?> { ["name"] = "first" });
        try
        {
            return use switch
            {
                "AfterChange" => Describe(HookRunner.AfterChange(specification, box, HookRunner.Snapshot(box), ["name"], DislIds.Fixed())),
                "Confirmation" => DeletionPolicy.Confirmation(specification, box)?.ToString() ?? "no confirmation",
                "Retype" => Describe(RetypePolicy.Change(specification, box, "Box")),
                "ReadOnlyLabel" => Describe(FormCommit.Label(specification, box, "name", "x", new DislEnv(ReadOnly: true))),
                "Refusal" => OperationInterpreter.Refusal(specification, "op", diagram, box) ?? "no refusal",
                "Evaluate" => $"{ConstraintEvaluator.Evaluate(specification, diagram).Count} findings",
                "ConnectToNew" => Describe(OperationInterpreter.ConnectToNew(specification, "Link", diagram, box, "target", new Dictionary<string, object?> { ["x"] = 0.0, ["y"] = 0.0 }, DislIds.Fixed("n"))),
                "EditText" => FormCommit.EditText(specification, box, "name", new DislEnv()) ?? "no text",
                "Chrome" => $"{CanvasChromeDerivation.Derive(diagram, diagram.Nodes, diagram.Env).Header.Count} header rows",
                "View" => $"{diagram.AddView("v", "main").Members.Count} members",
                _ => throw new ArgumentOutOfRangeException(nameof(use), use, null),
            };
        }
        catch (Exception e)
        {
            return $"throws {e.GetType().Name}: {e.Message}";
        }
    }

    private static string Describe(DislTransaction transaction) => transaction.Refusal is { } refusal ? "refused: " + refusal : $"{transaction.Changes.Count} changes";
}
