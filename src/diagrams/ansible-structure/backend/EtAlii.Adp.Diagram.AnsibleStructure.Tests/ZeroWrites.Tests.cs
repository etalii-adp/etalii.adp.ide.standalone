using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The claim this whole diagram type turns on: it reads an Ansible project and changes
/// <b>nothing of Ansible's</b> in it (Requirement 1.1, and the spec's first non-functional
/// requirement).
///
/// <para>
/// That claim was once "changes nothing at all", and ansible-refinements narrowed it by exactly
/// one file: a reposition writes the <c>layout:</c> block of the <c>.adp</c> registration, which
/// is ADP's own file sitting in the folder. Playbooks, roles, inventories, variable folders and
/// every other byte Ansible owns remain untouched, and
/// <see cref="AReposition_WritesTheRegistrationAndNothingOfAnsibles"/> is what holds that line
/// now that a write path exists at all.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// The design says Requirement 1.1 is satisfied by construction - there is no code path that
/// opens a file for writing, so no test needs to prove one is never taken. This test exists
/// anyway, for two reasons. A claim this central deserves a guard rather than an argument. And
/// the guard's real job is the future: it fails the day somebody adds a write path, which is
/// exactly when nobody is thinking about this requirement.
/// </para>
/// <para>
/// It exercises the <b>real</b> components rather than mocks of them, and deliberately includes
/// the two refused write attempts: a refusal that still touched the disk is the bug actually
/// worth guarding against, and mocking the refusal away would guard nothing.
/// </para>
/// </remarks>
public class ZeroWritesTests : IDisposable
{
    private readonly string _root;
    private readonly AnsibleProjectStore _store = new();
    private readonly ServiceProvider _provider;
    private readonly IHistoryStackStore _historyStacks;

    public ZeroWritesTests()
    {
        _provider = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddAnsibleStructure().BuildServiceProvider();
        _historyStacks = _provider.GetRequiredService<IHistoryStackStore>();
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine("Fixtures", "infrastructure"), _root);
        File.WriteAllText(IoPath.Combine(_root, "infrastructure.adp"), "ansible/structure\n");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _store.Dispose();
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public async Task EveryReadPath_LeavesTheProjectByteForByteUnchanged()
    {
        // Arrange.
        // Contents AND timestamps: a file rewritten with identical bytes is still a file this
        // module wrote, and would show up in someone's git status as a touched mtime.
        var before = Snapshot();

        var mapper = new AnsibleElementMapper();
        var factory = new AnsibleSessionFactory(_store, mapper, _historyStacks);
        var registration = IoPath.Combine(_root, "infrastructure.adp");

        // Act.
        // Everything this module can be asked to do, in the order a user would do it.
        await using (var session = factory.Open(ShortGuid.NewShortGuid(), _root, registration, registration))
        {
            session.Baseline();
            session.UpdateView(new DiagramViewport(-50, -50, 200, 200));
            session.UpdateView(DiagramViewport.Unbounded);

            var project = _store.GetOrLoad(_root);
            var graph = AnsibleGraph.Derive(project);

            // Select and describe every node and every edge there is.
            var properties = new AnsibleContextPropertyProvider(_store);
            var ids = graph.Nodes.Select(node => node.Id).Concat(graph.Edges.Select(edge => edge.Id)).ToArray();

            // Assert, before walking them, that there are any. The promise this test makes is
            // that reading writes nothing - and a graph that derived nothing reads nothing, so
            // the promise would hold over an empty fixture while exercising no read path at all.
            Assert.True(
                ids.Length > 0,
                "The derived graph has no nodes and no edges, so no read path was exercised and this guard proved nothing about writes.");

            foreach (var id in ids)
            {
                var target = new ContextTarget(ContextScope.DiagramElement, _root, false, default, _root, default, id);
                await properties.DescribeAsync(target, TestContext.Current.CancellationToken);

                // The refused write. Included on purpose: this is the path most likely to grow
                // a file operation by accident one day.
                await properties.SetAsync(target, "ansible.name", "changed", TestContext.Current.CancellationToken);
            }

            // The other refusal.
            await session.MoveElementAsync("role:nginx", "role:common", 0, TestContext.Current.CancellationToken);

            // And the rules, which read the whole tree again through their own reader.
            await new AnsibleValidator().ValidateAsync(
                new DiagramValidationRequest("ansible/structure\n", "infrastructure", _root, registration, registration)
                {
                    SubjectFolder = _root,
                },
                TestContext.Current.CancellationToken);
        }

        // Assert.
        AssertUnchanged(before, Snapshot());
    }

    [Fact]
    public async Task AReposition_WritesTheRegistrationAndNothingOfAnsibles()
    {
        // Arrange: the one write path this module has (Requirement 2.3). The guard is not that
        // nothing is written - something is, deliberately - but that the blast radius is exactly
        // one file, and it is ADP's own.
        var registration = IoPath.Combine(_root, "infrastructure.adp");
        var before = Snapshot();

        var factory = new AnsibleSessionFactory(_store, new AnsibleElementMapper(), _historyStacks);
        await using var session = factory.Open(ShortGuid.NewShortGuid(), _root, registration, registration);
        session.Baseline();

        // Act.
        var answer = await session.MoveElementToAsync("role:common", 321, 123, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(string.Empty, answer);

        var after = Snapshot();
        Assert.Equal(before.Count, after.Count);

        var changed = before.Keys
            .Where(path => !before[path].Bytes.SequenceEqual(after[path].Bytes))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([registration], changed);

        // Contents and timestamps both, for every file Ansible owns: a file rewritten with
        // identical bytes is still a file this module wrote.
        var untouched = before
            .Where(pair => !string.Equals(pair.Key, registration, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // The filter, not the snapshot, is what can empty here: a project holding nothing but
        // the registration would leave this walk with no file to compare, and the promise that
        // the module touched only the registration would be asserted about the registration
        // alone.
        Assert.True(
            untouched.Length > 0,
            "Every file in the snapshot is the registration, so this guard compared no other file and cannot show the module left them alone.");

        foreach (var (path, expected) in untouched)
        {
            var (actualBytes, actualWritten) = after[path];
            Assert.True(expected.Bytes.SequenceEqual(actualBytes), $"The module rewrote the contents of {path}.");
            Assert.True(
                expected.Written == actualWritten,
                $"The module touched {path}: written {expected.Written:O} before, {actualWritten:O} after.");
        }
    }

    /// <summary>
    /// Compares two snapshots and says exactly which file broke the promise. A bare
    /// <c>Assert.Equal</c> on the dictionaries reports "Dictionaries differ" and elides the
    /// entries, which tells whoever hits this in two years' time nothing at all - and this is
    /// the one test in the module whose failure message has to be immediately actionable.
    /// </summary>
    private static void AssertUnchanged(
        Dictionary<string, (byte[] Bytes, DateTime Written)> before,
        Dictionary<string, (byte[] Bytes, DateTime Written)> after)
    {
        // Assert, first, that there was a project to leave alone. Every check below is either a
        // `== 0` comparison or sits inside the loop, and an empty snapshot satisfies all three -
        // so a fixture that failed to copy would let this helper certify that the module wrote
        // nothing, having watched nothing.
        Assert.True(
            before.Count > 0,
            "The before-snapshot holds no files, so this helper compared nothing and proved nothing about writes.");

        var appeared = after.Keys.Except(before.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var vanished = before.Keys.Except(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        Assert.True(appeared.Length == 0, $"The module created: {string.Join(", ", appeared)}");
        Assert.True(vanished.Length == 0, $"The module removed: {string.Join(", ", vanished)}");

        foreach (var (path, expected) in before.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var (actualBytes, actualWritten) = after[path];
            Assert.True(expected.Bytes.SequenceEqual(actualBytes), $"The module rewrote the contents of {path}.");
            Assert.True(
                expected.Written == actualWritten,
                $"The module touched {path}: written {expected.Written:O} before, {actualWritten:O} after.");
        }
    }

    [Fact]
    public void ReadingAFolder_CreatesNoFileOfItsOwn()
    {
        // Arrange.
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        // Act.
        // No cache file, no index, no sidecar. Some tools leave one; this one may not - the
        // .adp is the only file ADP contributes to the folder, and it was already there.
        _ = AnsibleRuleSet.Judge(new AnsibleProjectReader().Read(_root));

        // Assert.
        Assert.Equal(before, Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReadingDoesNotLockTheProject_SoAnEditorCanStillSaveOverIt()
    {
        // Arrange.
        // The only editor this diagram type ever expects a user to have open is a text editor on
        // these very files. Holding an exclusive handle would fight it.
        var registration = IoPath.Combine(_root, "infrastructure.adp");
        var factory = new AnsibleSessionFactory(_store, new AnsibleElementMapper(), _historyStacks);
        await using var session = factory.Open(ShortGuid.NewShortGuid(), _root, registration, registration);
        session.Baseline();

        // Act and assert.
        var playbook = IoPath.Combine(_root, "webservers.yml");
        var exception = Record.Exception(() => File.AppendAllText(playbook, "\n# edited while open\n"));
        Assert.Null(exception);
    }

    [Fact]
    public void TheModulesPublicSurface_NamesNoVerbThatWrites()
    {
        // Arrange.
        // Aimed at the future rather than the present: this module's public surface should never
        // grow a verb that writes. If one appears, this names it rather than leaving a reviewer
        // to notice. Crude, and the right kind of crude for a claim that has to survive people.
        var writeVerbs = new[] { "Save", "Write", "Delete", "Create", "Rename", "Update" };

        // Act.
        var named = typeof(AnsibleProjectStore).Assembly.GetExportedTypes()
            // The generated wire types are excluded: protobuf gives every message a WriteTo,
            // which serializes to a stream and has nothing to do with a file. Their namespace
            // is what tells them apart, and it exists precisely because they are generated.
            .Where(type => !(type.Namespace ?? "").EndsWith(".Wire", StringComparison.Ordinal))
            .SelectMany(type => type
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Select(method => $"{type.Name}.{method.Name}"))
            .Where(name => writeVerbs.Any(verb => name.Contains(verb, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert.
        // Move is deliberately not in the list above: MoveElementAsync and MoveElementToAsync
        // are core's seams, which this module must implement. The first refuses; the second
        // writes the registration's layout block and nothing else, which the reposition test
        // above pins. Neither is a verb that writes an Ansible file, which is what this list
        // is watching for.
        Assert.Empty(named);
    }

    private Dictionary<string, (byte[] Bytes, DateTime Written)> Snapshot() =>
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)));

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(IoPath.Combine(destination, IoPath.GetRelativePath(source, folder)));
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, IoPath.Combine(destination, IoPath.GetRelativePath(source, file)));
        }
    }
}
