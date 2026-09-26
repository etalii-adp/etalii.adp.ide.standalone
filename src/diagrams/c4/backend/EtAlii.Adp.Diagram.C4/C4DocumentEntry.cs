namespace EtAlii.Adp.Diagram.C4;

/// <summary>A loaded C4 document and the workspace parsed from it.</summary>
/// <param name="Unreadable">
/// The body was there and could not be read, so this entry is an empty document STANDING IN for
/// content nobody could read - never one to write back over the file. A missing body is a new
/// document instead, and is not marked.
/// </param>
internal sealed record C4DocumentEntry(C4Document Document, C4Workspace Workspace, bool Unreadable = false);
