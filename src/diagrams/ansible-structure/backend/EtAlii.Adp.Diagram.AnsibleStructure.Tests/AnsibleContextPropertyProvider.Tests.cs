using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
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
    private readonly AnsibleProjectStore _store = new();
    private readonly AnsibleContextPropertyProvider _provider;

    public AnsibleContextPropertyProviderTests()
    {
        _root = IoPath.GetFullPath(IoPath.Combine("Fixtures", "infrastructure"));
        _provider = new AnsibleContextPropertyProvider(_store);
    }

    public void Dispose() => _store.Dispose();

    private async Task<IReadOnlyList<ContextPropertyDefinition>> Describe(string elementId, string? folder = null) =>
        await _provider.DescribeAsync(
            new ContextTarget(ContextScope.DiagramElement, folder ?? _root, IsContainer: false, SourceId: default, _root, default, elementId),
            TestContext.Current.CancellationToken);

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
            new ContextTarget(ContextScope.DiagramElement, _root, false, default, _root, default, "role:nginx"),
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
        Assert.Equal("nginx", Value(properties, "ansible.name"));
        Assert.Equal("2", Value(properties, "ansible.tasks"));
        Assert.Equal("1", Value(properties, "ansible.handlers"));
        Assert.Equal("common", Value(properties, "ansible.dependencies"));
        Assert.Contains("webservers.yml", Value(properties, "ansible.used-by"), StringComparison.Ordinal);
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
        Assert.Equal("common, nginx", Value(properties, "ansible.roles"));
        Assert.Equal("web", Value(properties, "ansible.hosts"));
        Assert.Contains("production", Value(properties, "ansible.inventories"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEntryPlaybook_ShowsWhatItImports()
    {
        // Act.
        var properties = await Describe("playbook:site.yml");

        // Assert.
        Assert.Equal("webservers.yml, dbservers.yml", Value(properties, "ansible.imports"));
    }

    [Fact]
    public async Task AnInventory_ShowsItsGroupsAndVariableFolders()
    {
        // Act.
        var properties = await Describe("inventory:inventories/production");

        // Assert.
        Assert.Equal(["Identity", "Groups", "Variables"], properties.Select(p => p.Group).Distinct());
        Assert.Equal("2 hosts", Value(properties, "ansible.group.web"));
        Assert.Equal("1 host", Value(properties, "ansible.group.db"));
        Assert.Equal("2 files", Value(properties, "ansible.vars.group_vars"));
    }

    [Fact]
    public async Task AnEdge_SaysWhichFileAndDirectiveDeclaredIt()
    {
        // Arrange.
        var edge = AnsibleGraph.Derive(_store.GetOrLoad(_root)).Edges.Single(e => e.Kind == AnsibleEdgeKind.IncludesTasks);

        // Act.
        var properties = await Describe(edge.Id);

        // Assert.
        // The answer to "why is this here" (Requirement 10.6).
        Assert.Equal(["Declaration"], properties.Select(p => p.Group).Distinct());
        Assert.StartsWith("roles/nginx/tasks/main.yml:", Value(properties, "ansible.declared-in"), StringComparison.Ordinal);
        Assert.Equal("include_tasks", Value(properties, "ansible.directive"));
        Assert.Equal("tls.yml", Value(properties, "ansible.target"));
        Assert.Equal("nginx_tls_enabled | default(false)", Value(properties, "ansible.condition"));
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
        Assert.Equal("1", Value(properties, "ansible.tasks"));
        Assert.Equal("1", Value(properties, "ansible.defaults"));
    }

    [Fact]
    public async Task AnEdgeWithNoCondition_HasNoConditionRow()
    {
        // Arrange.
        var edge = AnsibleGraph.Derive(_store.GetOrLoad(_root)).Edges
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
        Assert.Contains("nothing to run", Value(properties, "ansible.hollow"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnresolvedEdge_SaysWhatBecameOfItsTarget()
    {
        // Arrange.
        var broken = IoPath.GetFullPath(IoPath.Combine("Fixtures", "broken"));
        var edges = AnsibleGraph.Derive(_store.GetOrLoad(broken)).Edges;

        // Act.
        var missing = await Describe(edges.Single(e => e.Directive.Target == "absent-role").Id, broken);
        var expression = await Describe(edges.Single(e => e.Directive.IsExpression).Id, broken);

        // Assert.
        Assert.Equal("nothing in this folder", Value(missing, "ansible.resolution"));
        Assert.Contains("not knowable", Value(expression, "ansible.resolution"), StringComparison.Ordinal);
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

    private static string Value(IReadOnlyList<ContextPropertyDefinition> properties, string id) =>
        properties.Single(property => property.Id == id).Value;
}
