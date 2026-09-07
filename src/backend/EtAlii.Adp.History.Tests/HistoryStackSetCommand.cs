using EtAlii.Adp.Common;
namespace EtAlii.Adp.History.Tests;

/// <summary>A command that "sets" a value; its inverse sets the previous one back.</summary>
internal sealed record HistoryStackSetCommand(string Value) : ICommand;
