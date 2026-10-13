using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>An entry made from a text, as the store makes one, for tests that need one without a file.</summary>
internal static class AbmEntries
{
    public static DocumentEntry<AbmBody, AbmModel> Read(string text)
    {
        var body = AbmBody.Parse(text);
        return new DocumentEntry<AbmBody, AbmModel>(body, body.Model);
    }
}
