using EtAlii.Adp.Common;
namespace EtAlii.Adp.Backend.Tests;

internal sealed record CommandDispatcherGreetCommand(string Name) : ICommand;
