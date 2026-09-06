using EtAlii.Adp.Common;
namespace EtAlii.Adp.Backend.Tests;

internal sealed record CommandDispatcherShoutCommand(string Name) : ICommand;
