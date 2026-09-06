namespace EtAlii.Adp.Common;

/// <summary>The file was created, at this absolute path.</summary>
public sealed record AdpFileCreated(string FullPath) : AdpFileWriteResult;
