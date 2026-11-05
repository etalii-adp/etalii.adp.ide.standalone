namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The document as bytes, which is where Requirement 2.1 either holds or does not: the timeline corpus held to
/// <see cref="LineDocumentContract{TCorpus}"/>.
/// </summary>
public sealed class TimelineDocumentTests : LineDocumentContract<TimelineDocumentTests>, ILineDocumentCorpus
{
    public static string Extension => ".tml";

    public static int MinimumFixtures => 11;
}
