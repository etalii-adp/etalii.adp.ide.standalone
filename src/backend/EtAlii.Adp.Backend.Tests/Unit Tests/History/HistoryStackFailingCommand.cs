namespace EtAlii.Adp.Backend.Tests;

internal sealed record HistoryStackFailingCommand(string Error) : ICommand;
