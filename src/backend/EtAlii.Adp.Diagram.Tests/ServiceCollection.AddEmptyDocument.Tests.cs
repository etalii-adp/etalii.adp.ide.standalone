using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class ServiceCollectionAddEmptyDocumentTests
{
    private static readonly ToolOrigin Origin = new("etalii", "test");

    [Fact]
    public void ARegisteredEmptyDocument_IsItsText_WhateverTheFileIsCalled()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddEmptyDocument(Origin, "test: 1\r\n");
        using var provider = services.BuildServiceProvider();

        // Act.
        var factory = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>()).Find(Origin);

        // Assert.
        Assert.Equal("test: 1\r\n", factory!.CreateEmptyDocument("first"));
        Assert.Equal("test: 1\r\n", factory.CreateEmptyDocument("second"));
    }

    [Fact]
    public void ARegisteredEmptyDocumentFunction_WritesFromTheFileBaseName()
    {
        // Arrange.
        var services = new ServiceCollection();
        services.AddEmptyDocument(Origin, baseName => $"title: {baseName}\r\n");
        using var provider = services.BuildServiceProvider();

        // Act.
        var factory = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>()).Find(Origin);

        // Assert.
        Assert.Equal("title: first\r\n", factory!.CreateEmptyDocument("first"));
        Assert.Equal("title: second\r\n", factory.CreateEmptyDocument("second"));
        Assert.Throws<ArgumentNullException>(() => factory.CreateEmptyDocument(null!));
    }
}
