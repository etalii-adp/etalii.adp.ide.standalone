using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents.Wire;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// What every diagram module's element resolver must do, written once (code-structure FR-035,
/// added by the M1 review as T273): an element resolves to what the canvas was sent, a drop and a
/// connect gesture resolve to what they propose, an unknown id, a wrong client path, a selection
/// with no file above it and a file of another type are refused, and a tracked element follows
/// a rename until it is let go. A module's test class derives from this and supplies its resolver,
/// an example and one element of it.
/// </summary>
/// <remarks>
/// <b>Seen to fail with each of the base's checks removed</b>: the parent-scope check, the route
/// and origin check, the client-path check and the payload from the module's mapping, in
/// <see cref="DiagramSnapshotSourceResolver{TSnapshot}"/>; and with <c>Track</c> handing back a
/// subscription that is never let go.
/// </remarks>
public abstract class DiagramElementSourceResolverContract : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.ResolverContract");

    /// <summary>A module example with at least one named element, copied to a scratch folder for each test.</summary>
    protected abstract string Example { get; }

    /// <summary>The resolver under test, over a store of its own; asked for once per test.</summary>
    protected abstract IContextSourceResolver CreateResolver();

    /// <summary>Tells the resolver's store that the body at <paramref name="body"/> changed on disk.</summary>
    protected abstract Task ReloadAsync(string body, CancellationToken cancellationToken);

    /// <summary>An element of <see cref="Example"/> that has a name.</summary>
    protected abstract string ElementId { get; }

    /// <summary>What a selection of <see cref="ElementId"/> shows.</summary>
    protected abstract IReadOnlyList<string> ElementPath { get; }

    /// <summary>The type the canvas was sent <see cref="ElementId"/> as.</summary>
    protected abstract string ElementType { get; }

    /// <summary>A connect gesture between two elements of <see cref="Example"/>, as <c>rel:from-&gt;to</c>.</summary>
    protected abstract string Gesture { get; }

    /// <summary>The module's own words: the refusal for another type and a gone element, and the names of a proposed element and relation.</summary>
    protected abstract DiagramElementTexts Texts { get; }

    /// <summary>The copy of <see cref="Example"/> the test works on, made on first use.</summary>
    private string Body => field ??= CopyExample();

    /// <summary>The resolver under test, made on first use.</summary>
    private IContextSourceResolver Resolver => field ??= CreateResolver();

    /// <summary>
    /// Renames <see cref="ElementId"/> in <paramref name="body"/> to <paramref name="name"/> and returns what a
    /// selection of it shows afterwards: the <c>name:</c> under the element's <c>- id:</c> line is
    /// rewritten, which fits every YAML document so far. A module of another shape overrides it.
    /// </summary>
    protected virtual async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var pattern = "(- id: " + Regex.Escape(ElementId) + "\\r?\\n(?:[ ]+.*\\r?\\n)*?[ ]+name: )[^\\r\\n]*";
        var renamed = Regex.Replace(text, pattern, match => match.Groups[1].Value + name, RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        return [name];
    }

    /// <summary>
    /// The file a selection is made inside: the body itself, or, for a type that shares its extension
    /// with others and so is routed only through its registration, the registration beside it.
    /// </summary>
    protected virtual string SelectedIn(string body) => body;

    private string CopyExample()
    {
        var body = _folder.Combine(IoPath.GetFileName(Example));
        File.Copy(Example, body);
        return body;
    }

    public void Dispose()
    {
        _folder.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnElement_ResolvesToWhatItShows_WithThePayloadTheCanvasWasSent()
    {
        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(ElementId)).Level;

        // Assert.
        Assert.Equal(ElementPath, level.RelativePath);
        Assert.Equal(ContextScope.DiagramElement, level.Scope);
        Assert.Equal((ElementId, Body), (level.Target.ElementId, level.Target.ResolvedFullPath));
        Assert.Equal(ElementType, level.Detail.Element.ElementType);
        Assert.NotNull(level.Detail.Element.Payload);
    }

    [Fact]
    public async Task APlacementAndAGesture_ResolveToWhatTheyPropose()
    {
        // Act.
        var placement = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("new:300,700")).Level;
        var gesture = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(Gesture)).Level;

        // Assert.
        Assert.Equal([Texts.NewElement], placement.RelativePath);
        Assert.Equal(("new:300,700", Body), (placement.Target.ElementId, placement.Target.ResolvedFullPath));
        Assert.Equal([Texts.NewRelation], gesture.RelativePath);
        Assert.Equal((Gesture, Body), (gesture.Target.ElementId, gesture.Target.ResolvedFullPath));
    }

    [Fact]
    public async Task AnUnknownId_IsRefusedAsGone()
    {
        // Act.
        var rejected = Assert.IsType<RejectedContextLevel>(await ResolveAsync("nothing-here"));

        // Assert.
        Assert.Equal(Texts.Gone, rejected.Reason);
    }

    [Fact]
    public async Task AClientPathThatDoesNotMatch_IsRefused()
    {
        // Act: the client believes it selected something else.
        var resolution = await ResolveAsync(ElementId, [.. ElementPath.Select(segment => segment + " (elsewhere)")]);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task AnElementWithNoDiagramAboveIt_IsRefused()
    {
        // Act.
        var resolution = await Resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _folder.Path, Element(ElementId), [], null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task AnElementUnderAnotherElement_IsRefused()
    {
        // Act: the level above is not a file.
        var resolution = await ResolveAsync(ElementId, scope: ContextScope.DiagramElement);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task AnElementInAFileOfAnotherType_IsRefused()
    {
        // Arrange.
        var other = _folder.Combine("notes.txt");
        await File.WriteAllTextAsync(other, "Not a diagram.", TestContext.Current.CancellationToken);

        // Act.
        var rejected = Assert.IsType<RejectedContextLevel>(await ResolveAsync(ElementId, file: other));

        // Assert.
        Assert.Equal(Texts.NotThisType, rejected.Reason);
    }

    [Fact]
    public async Task ATrackedElement_FollowsARename_AndIsLetGo()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(ElementId)).Level;
        var seen = new List<IReadOnlyList<string>?>();
        var tracking = Resolver.Track(level.Target.WatchId, _folder.Path, level, seen.Add);

        // Act: renamed while tracked, then renamed again after it was let go.
        var renamed = await RenameAsync(Body, "Renamed once", cancellationToken);
        await ReloadAsync(Body, cancellationToken);
        tracking.Dispose();
        await RenameAsync(Body, "Renamed twice", cancellationToken);
        await ReloadAsync(Body, cancellationToken);

        // Assert.
        Assert.Equal([renamed], seen);
    }

    /// <summary>Resolves <paramref name="elementId"/> as selected inside <paramref name="file"/>, <see cref="SelectedIn"/> the body by default, at <paramref name="scope"/>.</summary>
    protected Task<ContextLevelResolution> ResolveAsync(
        string elementId, IReadOnlyList<string>? clientPath = null, string? file = null, ContextScope scope = ContextScope.Hierarchy)
    {
        file ??= SelectedIn(Body);
        var parent = new ContextResolvedLevel(
            new ContextSource(),
            [IoPath.GetFileName(file)],
            scope,
            new ContextTarget(ContextScope.Hierarchy, file, IsContainer: false, SourceId: default, _folder.Path, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

        return Resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _folder.Path, Element(elementId), clientPath ?? [], parent, TestContext.Current.CancellationToken).AsTask();
    }

    private static ContextSource Element(string elementId) => new() { ElementId = new ElementId { Value = elementId } };
}
