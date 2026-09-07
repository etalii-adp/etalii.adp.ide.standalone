using EtAlii.Adp.Common;
namespace EtAlii.Adp.History.Tests;

internal sealed record CommandDispatcherGreetCommand(string Name) : ICommand;
