namespace EtAlii.Adp.Context;

/// <summary>A document's entry on one connection, and the MIME type of the tool that opens it.</summary>
public sealed record ContextEntryLocation(ShortGuid EntryId, string MimeType);
