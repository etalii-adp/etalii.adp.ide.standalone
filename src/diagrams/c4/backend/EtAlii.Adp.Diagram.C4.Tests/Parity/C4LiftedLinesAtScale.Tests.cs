using System.Text;
using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// A view with many lifted relationships keeps its lines. Merging lifted lines into one line per pair costs
/// more than linearly (400 relationships take about 0.9 million steps, 800 about 3.4 million), so DISL's
/// default <c>language.limits.celCost</c> of one million dropped every lifted line of a view with about 400
/// or more; the C4 definitions raise it to ten million (ruled in chat on 2026-10-19).
/// </summary>
public class C4LiftedLinesAtScaleTests
{
    /// <summary>The number of systems the generated workspace declares, each holding one container.</summary>
    private const int Systems = 25;

    /// <summary>The number of relationships between containers of different systems, each landing on a pair of its own.</summary>
    private const int Relationships = 500;

    [Fact]
    public void AViewWithFiveHundredLiftedRelationships_DerivesEveryLine()
    {
        // Arrange.
        var workspace = C4Parser.Parse(LineDocument.Parse(Dsl()));
        var view = workspace.FindView("Landscape")!;
        const string file = "lifted.dsl";

        // Act.
        var failures = C4Disl.Of(workspace, view, C4Definitions.For(view.Kind).Specification, file).Findings
            .Where(finding => string.Equals(finding.Code, "std.derivedFailed", StringComparison.Ordinal))
            .Select(finding => $"{finding.Code} {finding.Message}")
            .ToList();
        var lines = C4Definitions.Drawing(workspace, file, view).Lines;

        // Assert.
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
        Assert.Equal(Relationships, lines.Count);
    }

    /// <summary>A landscape view over <see cref="Systems"/> systems, with <see cref="Relationships"/> relationships between their containers.</summary>
    private static string Dsl()
    {
        var text = new StringBuilder();
        text.Append("workspace \"Lifted\" {\r\n    model {\r\n");
        for (var system = 0; system < Systems; system++)
        {
            text.Append($"        s{system} = softwareSystem \"System {system}\" {{\r\n            c{system} = container \"Container {system}\"\r\n        }}\r\n");
        }

        var written = 0;
        for (var source = 0; source < Systems && written < Relationships; source++)
        {
            for (var target = 0; target < Systems && written < Relationships; target++)
            {
                if (source != target)
                {
                    text.Append($"        c{source} -> c{target} \"Uses\"\r\n");
                    written++;
                }
            }
        }

        text.Append("    }\r\n    views {\r\n        systemLandscape \"Landscape\" {\r\n            include *\r\n        }\r\n    }\r\n}\r\n");
        return text.ToString();
    }
}
