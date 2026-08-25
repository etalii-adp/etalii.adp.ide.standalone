using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// Where a hand-made arrangement lives, and when it wins. The sidecar is ADP's own file beside
/// the `.dsl`, never a comment inside it, and never something a diagram depends on
/// (c4-diagrams Requirements 3.5, 3.6, 8.3, 8.4).
/// </summary>
public class C4LayoutSidecarTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly C4LayoutSidecar _sidecar = new();

    private const string Model = """
        workspace {
            model {
                a = softwareSystem "A" "desc"
                b = softwareSystem "B" "desc"
                a -> b "Calls" "HTTPS"
            }
            views {
                systemLandscape "all" {
                    include *
                }
            }
        }
        """;

    public C4LayoutSidecarTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
        File.WriteAllText(_bodyPath, Model);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private (C4Workspace Workspace, C4View View) Load(string dsl = Model)
    {
        var workspace = C4Parser.Parse(C4Document.Parse(dsl));
        return (workspace, workspace.Views[0]);
    }

    [Fact]
    public void TheSidecar_SitsBesideTheDocument_AndIsNotTheDocument()
    {
        Assert.Equal(IoPath.Combine(_root, "model.layout.json"), C4LayoutSidecar.PathFor(_bodyPath));
    }

    [Fact]
    public void APositionWritten_IsReadBack()
    {
        _sidecar.Write(_bodyPath, "all", "a", new C4LayoutSidecar.Position(120, 340));

        var positions = _sidecar.Read(_bodyPath, "all");

        Assert.Equal(new C4LayoutSidecar.Position(120, 340), positions["a"]);
    }

    [Fact]
    public void PositionsAreKeptPerView_SoArrangingOneDoesNotMoveAnother()
    {
        _sidecar.Write(_bodyPath, "all", "a", new C4LayoutSidecar.Position(10, 10));
        _sidecar.Write(_bodyPath, "other", "a", new C4LayoutSidecar.Position(99, 99));

        Assert.Equal(new C4LayoutSidecar.Position(10, 10), _sidecar.Read(_bodyPath, "all")["a"]);
        Assert.Equal(new C4LayoutSidecar.Position(99, 99), _sidecar.Read(_bodyPath, "other")["a"]);
    }

    [Fact]
    public void NoSidecar_ReadsAsNoPositions_RatherThanThrowing()
    {
        Assert.Empty(_sidecar.Read(_bodyPath, "all"));
    }

    [Fact]
    public void ACorruptSidecar_CostsTheArrangement_NotTheDiagram()
    {
        // Requirement 3.6: an optimisation, never a dependency.
        File.WriteAllText(C4LayoutSidecar.PathFor(_bodyPath), "{ this is not json");

        Assert.Empty(_sidecar.Read(_bodyPath, "all"));
    }

    [Fact]
    public void Clear_ForgetsOneViewsPositions()
    {
        _sidecar.Write(_bodyPath, "all", "a", new C4LayoutSidecar.Position(10, 10));
        _sidecar.Write(_bodyPath, "other", "b", new C4LayoutSidecar.Position(20, 20));

        _sidecar.Clear(_bodyPath, "all");

        Assert.Empty(_sidecar.Read(_bodyPath, "all"));
        Assert.NotEmpty(_sidecar.Read(_bodyPath, "other"));
    }

    // ---- how the layout uses them --------------------------------------------------------

    [Fact]
    public void AnAuthoredPosition_WinsOverTheComputedOne()
    {
        var (workspace, view) = Load();
        var computed = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
        var authored = new Dictionary<string, C4LayoutSidecar.Position> { ["a"] = new(500, 600) };

        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default, authored);

        Assert.Equal(500, layout.Boxes["a"].X);
        Assert.Equal(600, layout.Boxes["a"].Y);
        // ...and the element keeps the size the backend measured for it.
        Assert.Equal(computed.Boxes["a"].Width, layout.Boxes["a"].Width);
    }

    [Fact]
    public void AnElementWithNoAuthoredPosition_KeepsTheComputedOne()
    {
        var (workspace, view) = Load();
        var computed = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);
        var authored = new Dictionary<string, C4LayoutSidecar.Position> { ["a"] = new(500, 600) };

        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default, authored);

        Assert.Equal(computed.Boxes["b"], layout.Boxes["b"]);
    }

    [Fact]
    public void ADeclaredAutoLayout_BeatsTheArrangement_AndSaysSo()
    {
        // Requirement 8.4: the document asked for a computed layout explicitly, so it wins -
        // but the user is told rather than watching their arrangement vanish.
        var dsl = Model.Replace("include *", "include *\n            autoLayout lr", StringComparison.Ordinal);
        var (workspace, view) = Load(dsl);
        var authored = new Dictionary<string, C4LayoutSidecar.Position> { ["a"] = new(500, 600) };

        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default, authored);

        Assert.NotEqual(500, layout.Boxes["a"].X);
        Assert.True(layout.AuthoredPositionsIgnored);
    }

    [Fact]
    public void WithNoArrangementAtAll_NothingIsReportedAsIgnored()
    {
        var dsl = Model.Replace("include *", "include *\n            autoLayout lr", StringComparison.Ordinal);
        var (workspace, view) = Load(dsl);

        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default);

        Assert.False(layout.AuthoredPositionsIgnored);
    }

    [Fact]
    public void AnAuthoredPositionForSomethingNotOnTheView_IsIgnoredQuietly()
    {
        var (workspace, view) = Load();
        var authored = new Dictionary<string, C4LayoutSidecar.Position> { ["ghost"] = new(500, 600) };

        var layout = C4LayoutEngine.Compute(workspace, view, C4Metrics.Default, authored);

        Assert.DoesNotContain("ghost", layout.Boxes.Keys);
        Assert.False(layout.AuthoredPositionsIgnored);
    }
}
