using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>An entry made from a text, as the store makes one, for tests that need one without a file.</summary>
internal static class AadEntries
{
    public static DocumentEntry<AadBody, AadModel> Read(string text)
    {
        var body = AadBody.Parse(text);
        return new DocumentEntry<AadBody, AadModel>(body, AadModel.Read(body), body.Unreadable);
    }
}
