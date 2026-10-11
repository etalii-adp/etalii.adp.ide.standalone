using EtAlii.Adp.Designer;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Which files are a designer's (knowledge-designer Requirements 2.1 and 10.2): a registration
/// naming the designer type, and the body that registration names - and nothing else, since a
/// designer stores its documents in formats a project is full of.
/// </summary>
public class DesignerFileRouterTests : IDisposable
{
    private static readonly DesignerDefinition Sheet = new(
        "fixture/sheet",
        "Fixture sheet",
        Formats: [new DesignerFormat("YAML", ".yaml"), new DesignerFormat("JSON", ".json")]);

    private readonly string _root;
    private readonly DesignerFileRouter _router = new(new DesignerDefinitionCatalog { All = [Sheet] });

    public DesignerFileRouterTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => TestFolder.TryDelete(_root);

    private string Write(string name, string content)
    {
        // Normalised, so a name written with a forward slash compares equal to what the router resolves.
        var path = IoPath.GetFullPath(IoPath.Combine(_root, name));
        Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ARegistrationNamingTheDesigner_RoutesToIt_WithTheBodyOfItsOwnName()
    {
        // Arrange.
        var adp = Write("cities.adp", "fixture/sheet\n");
        var body = Write("cities.json", "{}");

        // Act.
        var routed = Assert.IsType<DesignerRouted>(_router.Route(adp, _root));

        // Assert.
        Assert.Same(Sheet, routed.Definition);
        Assert.Equal(adp, routed.RegistrationPath);
        Assert.Equal(body, routed.BodyPath);
    }

    [Fact]
    public void WhenBodiesInTwoFormatsExist_TheFirstDeclaredFormatIsTheBody()
    {
        // Arrange: the definition declares YAML before JSON.
        var adp = Write("cities.adp", "fixture/sheet\n");
        Write("cities.json", "{}");
        var yaml = Write("cities.yaml", "a: 1\n");

        // Act.
        var routed = Assert.IsType<DesignerRouted>(_router.Route(adp, _root));

        // Assert.
        Assert.Equal(yaml, routed.BodyPath);
    }

    [Fact]
    public void TheBodyBesideItsRegistration_RoutesToTheSameDocument()
    {
        // Arrange.
        var adp = Write("cities.adp", "fixture/sheet\n");
        var body = Write("cities.yaml", "a: 1\n");

        // Act.
        var routed = Assert.IsType<DesignerRouted>(_router.Route(body, _root));

        // Assert.
        Assert.Equal(adp, routed.RegistrationPath);
        Assert.Equal(body, routed.BodyPath);
    }

    [Fact]
    public void ABodyWithoutARegistration_IsNotADesigners_WhateverItsExtension()
    {
        // Arrange: the same extension the designer writes, and nothing naming it.
        var body = Write("values.yaml", "a: 1\n");

        // Act and assert.
        Assert.IsType<NotADesigner>(_router.Route(body, _root));
    }

    [Fact]
    public void AFileBesideARegistrationThatNamesAnotherBody_IsNotADesigners()
    {
        // Arrange: cities.adp resolves to cities.yaml, so cities.json beside it is just a file.
        Write("cities.adp", "fixture/sheet\n");
        Write("cities.yaml", "a: 1\n");
        var other = Write("cities.json", "{}");

        // Act and assert.
        Assert.IsType<NotADesigner>(_router.Route(other, _root));
    }

    [Fact]
    public void ARegistrationNamingAnotherType_IsNotADesigners()
    {
        // Arrange: a diagram's registration, or a module that is not deployed.
        var adp = Write("plan.adp", "freeplane/mindmap\n");

        // Act and assert: the diagram router answers for it, including "unknown type".
        Assert.IsType<NotADesigner>(_router.Route(adp, _root));
    }

    [Fact]
    public void ABodyHeader_IsFollowedInsideTheProject()
    {
        // Arrange.
        var adp = Write("views/cities.adp", "fixture/sheet\nbody: ../data/all.json\n");
        var body = Write("data/all.json", "{}");

        // Act.
        var routed = Assert.IsType<DesignerRouted>(_router.Route(adp, _root));

        // Assert.
        Assert.Equal(body, routed.BodyPath);
    }

    [Fact]
    public void ABodyHeaderLeavingTheProject_IsRefused_NotFollowed()
    {
        // Arrange.
        var adp = Write("cities.adp", "fixture/sheet\nbody: ../../outside.json\n");

        // Act.
        var unreadable = Assert.IsType<DesignerUnreadable>(_router.Route(adp, _root));

        // Assert.
        Assert.Equal(adp, unreadable.Path);
        Assert.Contains("outside the project", unreadable.Reason);
    }

    [Fact]
    public void ARegistrationWhoseBodyIsMissing_StillRoutes_WithNoBody()
    {
        // Arrange.
        var adp = Write("cities.adp", "fixture/sheet\n");

        // Act.
        var routed = Assert.IsType<DesignerRouted>(_router.Route(adp, _root));

        // Assert: the type is known, and the missing file is the caller's to report.
        Assert.Null(routed.BodyPath);
    }

    [Fact]
    public void WithNoDesignerDeployed_NothingIsADesigners_AndNoFileIsRead()
    {
        // Arrange: a path that does not exist - reading it would throw or answer wrongly.
        var router = new DesignerFileRouter(new DesignerDefinitionCatalog { All = [] });

        // Act and assert.
        Assert.IsType<NotADesigner>(router.Route(IoPath.Combine(_root, "absent.adp"), _root));
    }
}
