using EtAlii.Adp.Common;
namespace EtAlii.Adp.Documents;

/// <summary>The name is already in use; the user picks another one, and nothing is renamed for them.</summary>
public sealed record AdpFileNameTaken : AdpFileWriteResult;
