using System.Text.RegularExpressions;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests.Parity;

/// <summary>
/// The document id the module's first edit of a graph without one writes after its schema marker (FBL 17.4, etalii.adp spec
/// 092): asserted written, and taken out, so a guard over the edit's own bytes compares what the edit did.
/// </summary>
internal static partial class DependencyGraphWrittenDocumentId
{
    /// <summary><paramref name="text"/>, which must hold the id the first edit wrote, without it.</summary>
    public static string Without(string text)
    {
        Assert.Matches(WrittenId(), text);
        return WrittenId().Replace(text, "$1", 1);
    }

    [GeneratedRegex(@"^(dependencies: 1\r?\n)id: [0-9a-z]{8}\r?\n")]
    private static partial Regex WrittenId();
}
