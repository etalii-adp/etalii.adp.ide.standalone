namespace EtAlii.Adp.History.Tests;

/// <summary>An edit recorded against one document, as a module's restore is.</summary>
internal sealed record HistoryStackBoundCommand(string BodyPath, string Value) : IDocumentBoundCommand;
