using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class ServiceCollectionAddDiagramSessionTests
{
    private static readonly ToolOrigin Origin = new("etalii", "test");

    [Fact]
    public void ARegisteredSession_IsOpenedThroughItsDelegate_WithTheProviderAndWhatItIsOpenedOn()
    {
        // Arrange.
        var services = new ServiceCollection();
        var registered = new object();
        services.AddSingleton(registered);
        DiagramSessionOpening? opened = null;
        IServiceProvider? given = null;
        services.AddDiagramSession(Origin, (provider, opening) =>
        {
            given = provider;
            opened = opening;
            return null!;
        });
        using var provider = services.BuildServiceProvider();
        var watch = ShortGuid.NewShortGuid();

        // Act.
        var factory = new DiagramSessionFactories(provider.GetServices<IDiagramSessionFactory>()).Find(Origin);
        factory!.Open(watch, "root", "root/body.test", "root/body.adp");

        // Assert.
        Assert.Equal(new DiagramSessionOpening(watch, "root", "root/body.test", "root/body.adp"), opened);
        Assert.Same(registered, given!.GetRequiredService<object>());
    }

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
