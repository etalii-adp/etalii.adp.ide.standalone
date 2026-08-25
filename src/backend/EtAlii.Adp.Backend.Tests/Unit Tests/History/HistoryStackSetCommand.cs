using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>A command that "sets" a value; its inverse sets the previous one back.</summary>
internal sealed record HistoryStackSetCommand(string Value) : ICommand;
