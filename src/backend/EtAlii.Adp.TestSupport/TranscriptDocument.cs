namespace EtAlii.Adp.TestSupport;

/// <summary>One document of a parity transcript's corpus.</summary>
/// <param name="Name">
/// What the transcript records it as: a path relative to <c>src/</c> with forward slashes for a
/// document on disk, the corpus's own name for one written inline.
/// </param>
/// <param name="Read">Its bytes, as the module's code will be handed them.</param>
public sealed record TranscriptDocument(string Name, Func<CancellationToken, Task<byte[]>> Read);
