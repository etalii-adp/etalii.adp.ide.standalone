using EtAlii.Adp.Common;
namespace EtAlii.Adp.History.Tests;

internal sealed record CommandDispatcherShoutCommand(string Name) : ICommand;
