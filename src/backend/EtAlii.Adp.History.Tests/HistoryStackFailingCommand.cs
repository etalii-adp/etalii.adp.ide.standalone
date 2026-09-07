namespace EtAlii.Adp.History.Tests;

internal sealed record HistoryStackFailingCommand(string Error) : ICommand;
