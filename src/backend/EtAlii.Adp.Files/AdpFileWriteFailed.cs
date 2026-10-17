namespace EtAlii.Adp.Files;

/// <summary>The file could not be written, with a reason meant for the user.</summary>
public sealed record AdpFileWriteFailed(string Message) : AdpFileWriteResult;
