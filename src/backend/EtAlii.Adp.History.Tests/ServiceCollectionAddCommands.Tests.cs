using EtAlii.Adp.Files;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.History.Tests;

public class ServiceCollectionAddCommandsTests
{
    [Fact]
    public async Task AddCommands_RegistersOneFileWriter_ForEverythingThatWrites()
    {
        // Arrange. One writer for the process: a second one would keep turns of its own, and two
        // saves of one path, one through each, would race (code-structure FR-068).
        await using var provider = new ServiceCollection().AddCommands().BuildServiceProvider();

        // Act.
        var first = provider.GetRequiredService<IFileWriter>();
        var second = provider.GetRequiredService<IFileWriter>();

        // Assert.
        Assert.IsType<FileWriter>(first);
        Assert.Same(first, second);
    }
}
