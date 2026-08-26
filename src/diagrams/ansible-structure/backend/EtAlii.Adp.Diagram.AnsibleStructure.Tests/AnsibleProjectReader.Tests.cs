using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The folder becoming a model. Read against the committed fixture trees, because the point of
/// building them was that the reader be tested against real Ansible rather than against
/// whatever shape the reader happens to produce.
/// </summary>
public class AnsibleProjectReaderTests
{
    private static readonly AnsibleProjectReader Reader = new();

    private static AnsibleProject Read(string fixture) => Reader.Read(IoPath.Combine("Fixtures", fixture));

    // ---- what it recognises --------------------------------------------------------------

    [Fact]
    public void TheBestPracticeTree_ReadsEveryNodeKind()
    {
        // Act.
        var project = Read("infrastructure");

        // Assert.
        Assert.Equal(["dbservers.yml", "site.yml", "webservers.yml"], project.Playbooks.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["common", "nginx", "postgres"], project.Roles.Select(r => r.Name));
        Assert.Equal(["production", "staging"], project.Inventories.Select(i => i.Name));
        Assert.Empty(project.Failures);
    }

    [Fact]
    public void APlay_CarriesItsNameHostsAndRoles()
    {
        // Act.
        var play = Assert.Single(Read("infrastructure").Playbooks.Single(p => p.Name == "webservers.yml").Plays);

        // Assert.
        Assert.Equal("Configure the web tier", play.Name);
        Assert.Equal("web", play.Hosts);
        Assert.Equal(["common", "nginx"], play.Directives.Where(d => d.Kind == AnsibleDirectiveKind.Roles).Select(d => d.Target));
    }

    [Fact]
    public void ARolesEntryInMappingForm_IsReadAndKeepsItsCondition()
    {
        // Act.
        // dbservers.yml writes `- role: postgres` with a `when:` - the other shape Ansible
        // accepts, and the one a reader that only understood bare strings would drop.
        var play = Assert.Single(Read("infrastructure").Playbooks.Single(p => p.Name == "dbservers.yml").Plays);

        // Assert.
        var postgres = Assert.Single(play.Directives, d => d.Target == "postgres");
        Assert.Equal("postgres_enabled | default(true)", postgres.Condition);
    }

    [Fact]
    public void ImportPlaybook_IsAPlaybooksSibling_NotAPlaysChild()
    {
        // Act.
        // Ansible puts `- import_playbook:` in the file's top-level list, beside the plays.
        var site = Read("infrastructure").Playbooks.Single(p => p.Name == "site.yml");

        // Assert.
        Assert.Empty(site.Plays);
        Assert.Equal(["webservers.yml", "dbservers.yml"], site.Imports.Select(i => i.Target));
        Assert.All(site.Imports, i => Assert.Equal(AnsibleDirectiveKind.ImportPlaybook, i.Kind));
    }

    [Fact]
    public void ARolesTaskIncludes_AreReadWithTheirConditionAndLine()
    {
        // Act.
        var nginx = Read("infrastructure").Roles.Single(r => r.Name == "nginx");

        // Assert.
        var include = Assert.Single(nginx.TaskIncludes);
        Assert.Equal(AnsibleDirectiveKind.IncludeTasks, include.Kind);
        Assert.Equal("tls.yml", include.Target);
        Assert.Equal("nginx_tls_enabled | default(false)", include.Condition);
        Assert.Equal("roles/nginx/tasks/main.yml", include.DeclaredIn);
        Assert.True(include.Line > 0);
    }

    [Fact]
    public void AFullyQualifiedDirectiveName_IsRecognised()
    {
        // Arrange, act and assert.
        // The fixture writes ansible.builtin.include_tasks. A reader that only understood the
        // short form would be reading half of a real repository.
        var nginx = Read("infrastructure").Roles.Single(r => r.Name == "nginx");
        Assert.Single(nginx.TaskIncludes);
    }

    [Fact]
    public void MetaDependencies_AreReadInBothShapes()
    {
        // Act.
        var project = Read("infrastructure");

        // Assert.
        // infrastructure writes `- role: common`; broken writes the same shape for `base`.
        Assert.Equal(["common"], project.Roles.Single(r => r.Name == "nginx").Dependencies.Select(d => d.Target));
        Assert.Equal(["common"], project.Roles.Single(r => r.Name == "postgres").Dependencies.Select(d => d.Target));
        Assert.Empty(project.Roles.Single(r => r.Name == "common").Dependencies);
    }

    [Fact]
    public void ARolesContents_AreSummarised()
    {
        // Act.
        var nginx = Read("infrastructure").Roles.Single(r => r.Name == "nginx");

        // Assert.
        Assert.Equal(2, nginx.Contents.TaskFiles);
        Assert.Equal(1, nginx.Contents.Handlers);
        Assert.Equal(1, nginx.Contents.Templates);
        Assert.True(nginx.Contents.HasMeta);
        Assert.False(nginx.Contents.IsHollow);
        Assert.Equal(0, nginx.Contents.Defaults);
    }

    [Fact]
    public void AnInventory_CarriesItsGroupsAndVariableFolders()
    {
        // Act.
        var production = Read("infrastructure").Inventories.Single(i => i.Name == "production");

        // Assert.
        Assert.Equal(["web", "db"], production.Groups.Select(g => g.Name));
        Assert.Equal(2, production.Groups.Single(g => g.Name == "web").HostCount);
        Assert.Equal(["group_vars", "host_vars"], production.VariableFolders.Select(v => v.Name));
        Assert.Equal(2, production.VariableFolders.Single(v => v.Name == "group_vars").FileCount);
    }

    [Fact]
    public void TheAnnotations_AreRecordedRatherThanDrawn()
    {
        // Act.
        var project = Read("infrastructure");

        // Assert.
        Assert.Contains("ansible.cfg", project.Annotations);
        Assert.Contains("collections/requirements.yml", project.Annotations);
    }

    // ---- what it ignores, and how it degrades ---------------------------------------------

    [Fact]
    public void ContentThatIsNotAnsible_IsIgnoredWithoutComplaint()
    {
        // Act.
        var project = Read("infrastructure");

        // Assert.
        // A Makefile, a docs folder and a shell script sit in that tree (Requirement 1.3).
        Assert.Empty(project.Failures);
        Assert.DoesNotContain(project.Annotations, a => a.Contains("Makefile", StringComparison.Ordinal));
    }

    [Fact]
    public void AYamlFileThatIsAMappingAtTheTop_IsNotAPlaybook()
    {
        // Act.
        // docs/topology.yml is data a human keeps for reference. The "list of mappings" shape
        // test is the whole of what excludes it - no name list, no exception to maintain.
        var project = Read("infrastructure");

        // Assert.
        Assert.DoesNotContain(project.Playbooks, p => p.Name == "topology.yml");
    }

    [Fact]
    public void AnUnparsableFile_DegradesOnlyItself()
    {
        // Act.
        var project = Read("broken");

        // Assert.
        var failure = Assert.Single(project.Failures);
        Assert.Equal("unparsable.yml", failure.RelativePath);
        // The rest of the tree still read.
        Assert.NotEmpty(project.Playbooks);
        Assert.NotEmpty(project.Roles);
    }

    [Fact]
    public void AHollowRole_IsStillARole_AndVisiblyHollow()
    {
        // Act.
        var hollow = Read("broken").Roles.Single(r => r.Name == "hollow");

        // Assert.
        // The README in that folder is not canonical content, so the role is still hollow.
        Assert.True(hollow.Contents.IsHollow);
        Assert.Empty(hollow.TaskFiles);
    }

    [Fact]
    public void AnExpressionTarget_IsMarkedAsOne_RatherThanTakenLiterally()
    {
        // Act.
        var deploy = Read("broken").Playbooks.Single(p => p.Name == "deploy.yml");

        // Assert.
        var expression = Assert.Single(deploy.Plays.SelectMany(p => p.Directives), d => d.IsExpression);
        Assert.Equal("{{ role_name }}", expression.Target);
    }

    [Fact]
    public void AnUnrecognisedLayout_ReadsAsNothingButItsAnnotation()
    {
        // Act.
        // Valid Ansible that keeps nothing where the recommended layout puts it. Undrawn is
        // the right answer; a complaint would not be (Requirement 9.4).
        var project = Read("unconventional");

        // Assert.
        Assert.True(project.IsEmpty);
        Assert.Empty(project.Failures);
        Assert.Equal(["ansible.cfg"], project.Annotations);
    }

    [Fact]
    public void AFolderThatIsNotThere_ReadsAsEmpty_RatherThanThrowing()
    {
        // Act.
        var project = Reader.Read(IoPath.Combine("Fixtures", "no-such-folder"));

        // Assert.
        Assert.True(project.IsEmpty);
        Assert.Empty(project.Failures);
    }

    // ---- determinism -----------------------------------------------------------------------

    [Fact]
    public void TheSameTree_ReadsToTheSameModelTwice()
    {
        // Act.
        var first = Read("infrastructure");
        var second = Read("infrastructure");

        // Assert.
        // Compared as a rendering rather than with Assert.Equal: these are records, but their
        // members are lists, and a record's generated equality compares a list by reference.
        // Two reads are never Equal however identical their contents, so asserting on the
        // records themselves would fail for a reason that has nothing to do with determinism.
        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public void TheOrderOfEveryCollection_IsOrdinalRatherThanTheFilesystems()
    {
        // Act.
        var project = Read("infrastructure");

        // Assert.
        Assert.Equal(project.Roles.Select(r => r.Name).Order(StringComparer.Ordinal), project.Roles.Select(r => r.Name));
        Assert.Equal(project.Inventories.Select(i => i.Name).Order(StringComparer.Ordinal), project.Inventories.Select(i => i.Name));
        var nginx = project.Roles.Single(r => r.Name == "nginx");
        Assert.Equal(nginx.TaskFiles.Select(t => t.Name).Order(StringComparer.Ordinal), nginx.TaskFiles.Select(t => t.Name));
    }

    /// <summary>
    /// The whole model as one ordered string - every node, every directive, every count, in the
    /// order the reader produced them. What determinism actually means here: not that two reads
    /// return equal objects, but that they describe the same tree the same way.
    /// </summary>
    private static string Describe(AnsibleProject project)
    {
        var lines = new List<string>();

        foreach (var playbook in project.Playbooks)
        {
            lines.Add($"playbook {playbook.RelativePath}");
            lines.AddRange(playbook.Imports.Select(Describe));
            foreach (var play in playbook.Plays)
            {
                lines.Add($"  play {play.Index} '{play.Name}' hosts={play.Hosts} line={play.Line}");
                lines.AddRange(play.Directives.Select(Describe));
            }
        }

        foreach (var role in project.Roles)
        {
            lines.Add($"role {role.Name} at {role.RelativePath} contents={role.Contents} hollow={role.Contents.IsHollow}");
            lines.AddRange(role.TaskFiles.Select(file => $"  task {file.RelativePath}"));
            lines.AddRange(role.Dependencies.Select(Describe));
            lines.AddRange(role.TaskIncludes.Select(Describe));
        }

        foreach (var inventory in project.Inventories)
        {
            lines.Add($"inventory {inventory.Name} at {inventory.RelativePath}");
            lines.AddRange(inventory.Groups.Select(group => $"  group {group.Name} hosts={group.HostCount}"));
            lines.AddRange(inventory.VariableFolders.Select(folder => $"  vars {folder.RelativePath} files={folder.FileCount}"));
        }

        lines.AddRange(project.Annotations.Select(annotation => $"annotation {annotation}"));
        lines.AddRange(project.Failures.Select(failure => $"failure {failure.RelativePath}:{failure.Line}"));

        return string.Join("\n", lines);
    }

    private static string Describe(AnsibleDirective directive) =>
        $"    {directive.Kind} -> '{directive.Target}' when='{directive.Condition}' " +
        $"in {directive.DeclaredIn}:{directive.Line} dynamic={directive.IsDynamic} expression={directive.IsExpression}";
}
