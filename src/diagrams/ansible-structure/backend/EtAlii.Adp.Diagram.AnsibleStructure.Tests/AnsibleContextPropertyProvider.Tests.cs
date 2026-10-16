using EtAlii.Adp.Context;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// Property-grid Requirement 4 exercised at 100%: everything shown, nothing editable, every
/// reason true.
/// </summary>
public class AnsibleContextPropertyProviderTests : IDisposable
{
    private readonly string _root;
    private readonly AnsibleFolders _folders = new();
    private readonly AnsibleContextPropertyProvider _provider;

    public AnsibleContextPropertyProviderTests()
    {
        _root = IoPath.GetFullPath(IoPath.Combine("Fixtures", "infrastructure"));
        _provider = new AnsibleContextPropertyProvider(_folders);
    }

    public void Dispose() => _folders.Dispose();

    /// <summary>Describes an element as selected in the registration of <paramref name="folder"/>, the fixture's by default.</summary>
    private async Task<IReadOnlyList<ContextPropertyDefinition>> Describe(string elementId, string? folder = null) =>
        await _provider.DescribeAsync(
            ContextTarget.ForElement(Registration(folder ?? _root), elementId, _root),
            TestContext.Current.CancellationToken);

    /// <summary>Where a folder's registration sits: a selection's target, from which the provider takes the folder.</summary>
    private static string Registration(string folder) => IoPath.Combine(folder, "infrastructure.adp");

    // ---- the headline: nothing is editable, and every reason names a real file ---------------

    [Theory]
    [InlineData("playbook:site.yml")]
    [InlineData("playbook:webservers.yml")]
    [InlineData("role:nginx")]
    [InlineData("role:common")]
    [InlineData("taskfile:roles/nginx/tasks/tls.yml")]
    [InlineData("inventory:inventories/production")]
    [InlineData("vars:inventories/production/group_vars")]
    public async Task EveryPropertyOfEveryNodeKind_IsReadOnlyWithAReasonNamingAFileThatExists(string elementId)
    {
        // Act.
        var properties = await Describe(elementId);

        // Assert.
        Assert.NotEmpty(properties);
        Assert.All(properties, property =>
        {
            // Not merely non-null: PropertyRow and ContextPropertyDefinition both test LENGTH,
            // so a whitespace-only reason would render - and behave - as editable.
            Assert.False(property.IsEditable, $"{property.Id} is editable.");
            Assert.NotEqual("", property.ReadOnlyReason.Trim());
            Assert.StartsWith("Defined in ", property.ReadOnlyReason, StringComparison.Ordinal);

            // The reason names a real file or folder. A reason pointing nowhere would send a
            // reader to look for something that is not there.
            var named = property.ReadOnlyReason["Defined in ".Length..].Split(';')[0];
            var path = IoPath.Combine(_root, named);
            Assert.True(File.Exists(path) || Directory.Exists(path), $"{property.Id} names '{named}', which does not exist.");
        });
    }

    [Fact]
    public async Task EveryPropertyId_IsModulePrefixed()
    {
        // Act.
        var properties = await Describe("role:nginx");

        // Assert.
        Assert.All(properties, property => Assert.StartsWith("ansible.", property.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetAsync_RefusesWhateverItIsGiven()
    {
        // Act.
        // Unreachable through the grid - the resolver stops a write to a read-only property
        // first - and written to refuse anyway, so it stays safe if that ever changes.
        var result = await _provider.SetAsync(
            ContextTarget.ForElement(Registration(_root), "role:nginx", _root),
            "ansible.name", "something-else", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(result.IsSuccess);
        Assert.Contains("changes nothing", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ---- the rows each kind carries ------------------------------------------------------------

    [Fact]
    public async Task ARole_ShowsIdentityContentsAndRelationships()
    {
        // Act.
        var properties = await Describe("role:nginx");

        // Assert.
        Assert.Equal(["Identity", "Contents", "Relationships"], properties.Select(p => p.Group).Distinct());
        Assert.Equal("nginx", properties.Row("ansible.name").Value);
        Assert.Equal("2", properties.Row("ansible.tasks").Value);
        Assert.Equal("1", properties.Row("ansible.handlers").Value);
        Assert.Equal("common", properties.Row("ansible.dependencies").Value);
        Assert.Contains("webservers.yml", properties.Row("ansible.used-by").Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARolesDependenciesRow_NamesTheMetaFileItComesFrom()
    {
        // Act.
        var properties = await Describe("role:nginx");

        // Assert.
        // Not the role folder: the value lives in meta/main.yml and the reason has to say so,
        // or the reader opens the wrong thing.
        var dependencies = properties.Single(p => p.Id == "ansible.dependencies");
        Assert.Contains("roles/nginx/meta/main.yml", dependencies.ReadOnlyReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APlaybook_ShowsIdentityRunsAndTargets()
    {
        // Act.
        var properties = await Describe("playbook:webservers.yml");

        // Assert.
        Assert.Equal(["Identity", "Runs", "Targets"], properties.Select(p => p.Group).Distinct());
        Assert.Equal("common, nginx", properties.Row("ansible.roles").Value);
        Assert.Equal("web", properties.Row("ansible.hosts").Value);
        Assert.Contains("production", properties.Row("ansible.inventories").Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEntryPlaybook_ShowsWhatItImports()
    {
        // Act.
        var properties = await Describe("playbook:site.yml");

        // Assert.
        Assert.Equal("webservers.yml, dbservers.yml", properties.Row("ansible.imports").Value);
    }

    [Fact]
    public async Task AnInventory_ShowsItsGroupsAndVariableFolders()
    {
        // Act.
        var properties = await Describe("inventory:inventories/production");

        // Assert.
        Assert.Equal(["Identity", "Groups", "Variables"], properties.Select(p => p.Group).Distinct());
        Assert.Equal("2 hosts", properties.Row("ansible.group.web").Value);
        Assert.Equal("1 host", properties.Row("ansible.group.db").Value);
        Assert.Equal("2 files", properties.Row("ansible.vars.group_vars").Value);
    }

    [Fact]
    public async Task AnEdge_SaysWhichFileAndDirectiveDeclaredIt()
    {
        // Arrange.
        var edge = AnsibleGraph.Derive(new AnsibleProjectReader().Read(_root)).Edges.Single(e => e.Kind == AnsibleEdgeKind.IncludesTasks);

        // Act.
        var properties = await Describe(edge.Id);

        // Assert.
        // The answer to "why is this here" (Requirement 10.6).
        Assert.Equal(["Declaration"], properties.Select(p => p.Group).Distinct());
        Assert.StartsWith("roles/nginx/tasks/main.yml:", properties.Row("ansible.declared-in").Value, StringComparison.Ordinal);
        Assert.Equal("include_tasks", properties.Row("ansible.directive").Value);
        Assert.Equal("tls.yml", properties.Row("ansible.target").Value);
        Assert.Equal("nginx_tls_enabled | default(false)", properties.Row("ansible.condition").Value);
    }

    // ---- absent is absent, not empty ---------------------------------------------------------------

    [Fact]
    public async Task APropertyAbsentFromTheFiles_IsNotContributedAtAll()
    {
        // Act.
        // common has no meta, no templates and no handlers; the grid shows what a role has
        // rather than a checklist of what it lacks (Requirement 10.7).
        var properties = await Describe("role:common");

        // Assert.
        Assert.DoesNotContain(properties, p => p.Id == "ansible.meta");
        Assert.DoesNotContain(properties, p => p.Id == "ansible.templates");
        Assert.DoesNotContain(properties, p => p.Id == "ansible.handlers");
        Assert.DoesNotContain(properties, p => p.Id == "ansible.dependencies");
        // What it does have is there.
        Assert.Equal("1", properties.Row("ansible.tasks").Value);
        Assert.Equal("1", properties.Row("ansible.defaults").Value);
    }

    [Fact]
    public async Task AnEdgeWithNoCondition_HasNoConditionRow()
    {
        // Arrange.
        var edge = AnsibleGraph.Derive(new AnsibleProjectReader().Read(_root)).Edges
            .First(e => e.Kind == AnsibleEdgeKind.ImportsPlaybook);

        // Act.
        var properties = await Describe(edge.Id);

        // Assert.
        Assert.DoesNotContain(properties, p => p.Id == "ansible.condition");
    }

    [Fact]
    public async Task AHollowRole_SaysSo()
    {
        // Arrange.
        var broken = IoPath.GetFullPath(IoPath.Combine("Fixtures", "broken"));

        // Act.
        var properties = await Describe("role:hollow", broken);

        // Assert.
        Assert.Contains("nothing to run", properties.Row("ansible.hollow").Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnresolvedEdge_SaysWhatBecameOfItsTarget()
    {
        // Arrange.
        var broken = IoPath.GetFullPath(IoPath.Combine("Fixtures", "broken"));
        var edges = AnsibleGraph.Derive(new AnsibleProjectReader().Read(broken)).Edges;

        // Act.
        var missing = await Describe(edges.Single(e => e.Directive.Target == "absent-role").Id, broken);
        var expression = await Describe(edges.Single(e => e.Directive.IsExpression).Id, broken);

        // Assert.
        Assert.Equal("nothing in this folder", missing.Row("ansible.resolution").Value);
        Assert.Contains("not knowable", expression.Row("ansible.resolution").Value, StringComparison.Ordinal);
    }

    // ---- targets that no longer resolve ---------------------------------------------------------------

    [Fact]
    public async Task AnElementThatIsGone_DescribesNothing()
    {
        // Act.
        var properties = await Describe("role:never-existed");

        // Assert.
        Assert.Empty(properties);
    }

    [Fact]
    public async Task AFolderThatIsGone_DescribesNothing()
    {
        // Act.
        var properties = await Describe("role:nginx", IoPath.Combine(_root, "no-such-folder"));

        // Assert.
        Assert.Empty(properties);
    }
}
