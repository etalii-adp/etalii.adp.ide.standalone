using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Routing;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>
/// Every <c>.adp</c> registration under <c>src/</c> (Requirement 11.7, 11.8 and 11.9): parsed in the
/// line form and written back unchanged; for a declared binding's origin, its body found and opened,
/// its <c>view</c> or <c>resource</c> selecting something, and its layout naming elements of the
/// reading or reported stale; for the W3C readings, the reading's <c>suggest</c> matching the body;
/// for the C4 types, the <c>*.layout.json</c> sidecar read as the binding's legacy layout.
/// </summary>
public class RegistrationsTests
{
    public static TheoryData<string> Registrations()
    {
        var data = new TheoryData<string>();
        foreach (var file in All()) data.Add(file);
        return data;
    }

    [Fact]
    public void TheEnumerationFindsTheRegistrations()
    {
        // Act.
        var count = All().Count;

        // Assert.
        Assert.True(count >= RealFileCorpus.MinimumRegistrations, $"{count} registrations were found under src/; at least {RealFileCorpus.MinimumRegistrations} were there when this suite was written.");
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void TheRegistrationParsesAndSavesUnchanged(string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(RealFileCorpus.FullPath(file));

        // Act.
        var registration = OpenRegistration.Open(bytes);
        var result = registration.Change(new ModelChange.Save());

        // Assert.
        Assert.False(string.IsNullOrWhiteSpace(registration.Document.Origin), $"{file}: no origin on line 1.");
        Assert.Empty(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
        Assert.Equal(bytes, registration.Bytes);
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void ADeclaredBindingsRegistrationOpensItsBody(string file)
    {
        // Arrange.
        var registration = RegistrationDocument.Read(File.ReadAllBytes(RealFileCorpus.FullPath(file)));
        if (Declared(registration.Origin) is not { } binding) return;

        // Act.
        var location = BodyLocator.Locate(RealFileCorpus.FullPath(file), registration, binding, Repository.Root);

        // Assert: the body resolves and exists.
        Assert.True(location.Refusal is null, $"{file}: {location.Refusal}");
        Divergences.Check("registration-body", binding.Name, file, location.Exists ? null : $"no body at {Path.GetRelativePath(Repository.Root, location.Path!).Replace('\\', '/')}");
        if (!location.Exists) return;
        var bodyFile = Path.GetRelativePath(Repository.Root, location.Path!).Replace('\\', '/');
        var headers = registration.Headers.ToDictionary(h => h.Key, h => h.Value, StringComparer.Ordinal);
        var body = OpenBody.Open(File.ReadAllBytes(location.Path!), binding, RealFileCorpus.Options(binding, bodyFile, headers));
        var model = body.Model;
        if (model.Unreadable) return;

        // Assert: the view and the resource select something.
        if (registration.View is { } view) Divergences.Check("registration-view", binding.Name, file, model.SelectView(view) is null ? $"no view '{view}' among {string.Join(", ", model.Views.Select(v => v.Name))}" : null);
        if (registration.Resource is { } resource)
        {
            Divergences.Check("registration-resource", binding.Name, file, model.Resources.Contains(resource) ? null : $"no resource '{resource}' among [{string.Join(", ", model.Resources)}]");
        }

        // Assert: every layout entry names an element of the reading, or is reported stale.
        var ids = model.Elements.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var stale = registration.StaleEntries(ids, file);
        var unknown = (registration.Layout?.Entries ?? []).Select(e => e.Key).Where(id => !ids.Contains(id)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(unknown.Count, stale.Count);
        Assert.All(stale, f => Assert.Equal(FindingCodes.StaleViewData, f.Code));
        Divergences.Check("registration-layout", binding.Name, file, unknown.Count == 0 ? null : $"{unknown.Count} layout entries name no element: {string.Join(", ", unknown.Take(5))}{(unknown.Count > 5 ? ", …" : "")}");
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void W3CReadingsSuggestMatchesItsBody(string file)
    {
        // Arrange.
        var registration = RegistrationDocument.Read(File.ReadAllBytes(RealFileCorpus.FullPath(file)));
        if (registration.Origin is not ("w3c/owl" or "w3c/shacl" or "w3c/skos")) return;
        var binding = RealFileCorpus.Binding("w3c-turtle.fbl", "turtle");

        // Act.
        var location = BodyLocator.Locate(RealFileCorpus.FullPath(file), registration, binding, Repository.Root);
        Assert.True(location.Refusal is null && location.Exists, $"{file}: its body was not found ({location.Refusal ?? location.Path}).");
        var suggests = Router.SuggestsReading(binding, registration.Origin, File.ReadAllBytes(location.Path!));

        // Assert.
        Divergences.Check("reading-suggest", binding.Name, file, suggests ? null : $"the {registration.Origin} reading's suggest does not match {Path.GetFileName(location.Path)}");
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void C4RegistrationReadsItsLegacyLayout(string file)
    {
        // Arrange.
        var registration = RegistrationDocument.Read(File.ReadAllBytes(RealFileCorpus.FullPath(file)));
        if (!registration.Origin.StartsWith("c4/", StringComparison.Ordinal) || Declared(registration.Origin) is not { } binding) return;
        var location = BodyLocator.Locate(RealFileCorpus.FullPath(file), registration, binding, Repository.Root);
        if (!location.Exists) return;
        var sidecarPath = LegacySidecar.PathFor(binding.Registration.LegacyLayout!, location.Path!);
        if (registration.Layout is not null || !File.Exists(sidecarPath)) return;

        // Act.
        var sidecar = LegacySidecar.Open(File.ReadAllBytes(sidecarPath));
        var positions = sidecar.Positions(registration.View);
        var bodyFile = Path.GetRelativePath(Repository.Root, location.Path!).Replace('\\', '/');
        var model = OpenBody.Open(File.ReadAllBytes(location.Path!), binding, RealFileCorpus.Options(binding, bodyFile)).Model;

        // Assert: the sidecar reads, and every position of the view names an element of the reading.
        Assert.False(sidecar.IsUnreadable, $"{sidecarPath} could not be read.");
        var unknown = positions.Keys.Where(id => model.Find(id) is null).Order(StringComparer.Ordinal).ToList();
        Assert.True(unknown.Count == 0, $"{file}: the legacy layout places {string.Join(", ", unknown)}, which the reading does not have.");
    }

    [Fact]
    public void TheC4LegacyLayoutsArePositionedThroughTheirRegistrations()
    {
        // Arrange.
        var binding = RealFileCorpus.Binding("structurizr.fbl", "workspace");
        var placed = 0;

        // Act.
        foreach (var file in All())
        {
            var registration = RegistrationDocument.Read(File.ReadAllBytes(RealFileCorpus.FullPath(file)));
            if (!registration.Origin.StartsWith("c4/", StringComparison.Ordinal) || registration.Layout is not null) continue;
            var location = BodyLocator.Locate(RealFileCorpus.FullPath(file), registration, binding, Repository.Root);
            if (!location.Exists) continue;
            var sidecarPath = LegacySidecar.PathFor(binding.Registration.LegacyLayout!, location.Path!);
            if (File.Exists(sidecarPath)) placed += LegacySidecar.Open(File.ReadAllBytes(sidecarPath)).Positions(registration.View).Count;
        }

        // Assert: the enumeration found positions to apply at all, so the theory above is not vacuous.
        Assert.True(placed > 0, "No C4 registration had a legacy layout with positions for its view.");
    }

    [Fact]
    public void EveryChartFolderIsRecognisedAndEveryTurtleFileRoutesToTheTurtleBinding()
    {
        // Arrange.
        var chart = RealFileCorpus.Binding("helm-chart.fbl", "chart");
        var turtle = RealFileCorpus.Binding("w3c-turtle.fbl", "turtle");
        var bindings = RealFileCorpus.AllBindings().Select(b => b.Binding).ToList();
        var charts = RealFileCorpus.All.Where(f => Path.GetFileName(f) == "Chart.yaml").Select(f => Path.GetDirectoryName(RealFileCorpus.FullPath(f))!).ToList();
        var rdf = RealFileCorpus.All.Where(f => f.EndsWith(".ttl", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".nt", StringComparison.OrdinalIgnoreCase)).ToList();

        // Act and assert.
        Assert.True(charts.Count >= RealFileCorpus.MinimumCharts, $"{charts.Count} chart folders were found; at least {RealFileCorpus.MinimumCharts} were there when this suite was written.");
        Assert.All(charts, folder => Assert.True(FolderSubject.Recognise(chart, folder), $"{folder} is not recognised as a chart."));
        Assert.All(charts, folder => Assert.Contains(FolderSubject.Files(chart, folder), f => f.RelativePath == "Chart.yaml"));
        Assert.True(rdf.Count >= RealFileCorpus.MinimumTurtle, $"{rdf.Count} Turtle and N-Triples files were found; at least {RealFileCorpus.MinimumTurtle} were there when this suite was written.");
        Assert.All(rdf, file =>
        {
            var candidates = Router.Candidates(file, File.ReadAllBytes(RealFileCorpus.FullPath(file)), bindings);
            Assert.Equal([turtle], candidates);
        });
    }

    private static List<string> All() => RealFileCorpus.All.Where(f => f.EndsWith(".adp", StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The vendored declared file binding whose <c>claims.origins</c> holds <paramref name="origin"/>, if any.</summary>
    private static FblBinding? Declared(string origin) =>
        RealFileCorpus.AllBindings().Select(b => b.Binding).FirstOrDefault(b => b.Plugin is null && !b.Body.IsFolder && b.Claims.Origins.Contains(origin));
}
