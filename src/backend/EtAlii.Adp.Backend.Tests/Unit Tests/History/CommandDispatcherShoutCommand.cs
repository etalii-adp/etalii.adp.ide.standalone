using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

internal sealed record CommandDispatcherShoutCommand(string Name) : ICommand;
