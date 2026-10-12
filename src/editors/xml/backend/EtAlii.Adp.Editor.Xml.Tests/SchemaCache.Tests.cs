using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>A compiled schema is kept while nothing of it changes, and dropped when unused (xml-editor Performance).</summary>
public sealed class SchemaCacheTests : IDisposable
{
    private readonly ManualTime _time = new();
    private readonly SchemaCache _cache;
    private readonly ExampleProject _project;

    public SchemaCacheTests()
    {
        _cache = new SchemaCache(_time);
        _project = new ExampleProject(new XmlAnalyzer(_cache));
    }

    public void Dispose() => _project.Dispose();

    [Fact]
    public async Task ASecondRequest_WithNothingChanged_DoesNotCompileAgain()
    {
        // Arrange and act.
        await _project.AnalyzeAsync("library.xml");
        await _project.AnalyzeAsync("library-broken.xml");

        // Assert: both documents name library.xsd; it was compiled once.
        Assert.Equal(1, _cache.Compilations);
    }

    [Fact]
    public async Task AChangedSchema_IsCompiledAgain()
    {
        // Arrange.
        await _project.AnalyzeAsync("library.xml");

        // Act.
        _project.Write("library.xsd", _project.Text("library.xsd"));
        await _project.AnalyzeAsync("library.xml");

        // Assert.
        Assert.Equal(2, _cache.Compilations);
    }

    [Fact]
    public async Task ASchemaUnusedForTenMinutes_IsDropped()
    {
        // Arrange.
        await _project.AnalyzeAsync("library.xml");

        // Act.
        _time.Advance(TimeSpan.FromMinutes(9));
        await _project.AnalyzeAsync("library.xml");
        _time.Advance(TimeSpan.FromMinutes(11));
        await _project.AnalyzeAsync("library.xml");

        // Assert: kept across nine minutes, dropped after eleven unused.
        Assert.Equal(2, _cache.Compilations);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
