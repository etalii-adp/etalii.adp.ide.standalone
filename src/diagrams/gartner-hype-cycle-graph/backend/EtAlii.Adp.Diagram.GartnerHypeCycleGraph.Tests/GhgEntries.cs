using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>An entry made from a text, as the store makes one, for tests that need one without a file.</summary>
internal static class GhgEntries
{
    public static DocumentEntry<GhgBody, GhgModel> Read(string text)
    {
        var body = GhgBody.Parse(text);
        return new DocumentEntry<GhgBody, GhgModel>(body, GhgParser.Parse(body));
    }
}
