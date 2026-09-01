using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Diagrams;

using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The claim this whole diagram type turns on: it reads an Ansible project and changes
/// <b>nothing</b> in it (Requirement 1.1, and the spec's first non-functional requirement).
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

    public ZeroWritesTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        CopyTree(IoPath.Combine("Fixtures", "infrastructure"), _root);
        File.WriteAllText(IoPath.Combine(_root, "infrastructure.adp"), "ansible/structure\n");
    }

    public void Dispose()
    {
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
        var factory = new AnsibleSessionFactory(_store, mapper);
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
            foreach (var id in graph.Nodes.Select(node => node.Id).Concat(graph.Edges.Select(edge => edge.Id)))
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
        var factory = new AnsibleSessionFactory(_store, new AnsibleElementMapper());
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
        // Move is deliberately not in the list above: MoveElementAsync is core's seam, which
        // this module must implement and does by refusing. The first test in this class proves
        // that refusal touches nothing on disk, which is the guarantee that actually matters.
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
