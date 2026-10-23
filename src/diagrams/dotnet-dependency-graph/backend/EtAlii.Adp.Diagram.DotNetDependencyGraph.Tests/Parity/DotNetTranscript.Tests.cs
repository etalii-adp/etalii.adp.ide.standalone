namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests.Parity;

/// <summary>
/// The module reproduces the frozen transcript byte for byte: the oracle the switch-over to the DISL
/// definition is held to, frozen from the hand-written code before any of it was derived.
/// </summary>
/// <remarks>
/// A difference is either a regression or an intended change. Only the second is fixed by
/// regenerating - <c>ADP_WRITE_PARITY_TRANSCRIPTS=1</c> rewrites the file before comparing - and the
/// diff of the checked-in file is then the review of that change.
/// </remarks>
public sealed class DotNetTranscriptTests : TranscriptTests<DotNetTranscript>;
