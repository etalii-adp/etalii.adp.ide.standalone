using System.Diagnostics;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The claim that picked this format: a `.dsl` ADP wrote is a `.dsl` the C4 ecosystem can read.
/// Checked by handing ADP's own output to Structurizr's own parser rather than to ADP's.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in this project tests ADP against ADP. That is circular exactly where it
/// matters least and most: a parser and a writer that agree with each other can still both be
/// wrong about the format. The Structurizr CLI's <c>validate</c> runs the <c>structurizr-dsl</c>
/// library - the same code Structurizr Lite and the Structurizr web tooling parse with - so it
/// is the authority, and ADP is what is on trial.
/// </para>
/// <para>
/// The CLI is a Java program and is not vendored here, so these checks skip unless the
/// environment provides one. To run them, set <c>ADP_STRUCTURIZR_CLI</c> to the folder holding
/// the CLI's jars (its <c>lib</c> directory) and have <c>java</c> on the PATH:
/// </para>
/// <code>
/// $env:ADP_STRUCTURIZR_CLI = "C:\tools\structurizr-cli\lib"
/// dotnet test --solution EtAlii.Adp.slnx
/// </code>
/// <para>
/// Skipping rather than failing when it is absent is deliberate: a developer without a JDK
/// should not be blocked, and a skipped test reports itself, so the check cannot quietly
/// disappear the way a commented-out one would.
/// </para>
/// <para>
/// <b>Where the CLI comes from.</b> Two routes, both reachable - an earlier session concluded
/// the <c>structurizr</c> GitHub organisation was filtered by its sandbox, and it was not; the
/// repositories it guessed at simply do not exist under those names, and a 404 was read as a
/// filter. Recording this so nobody retraces it:
/// </para>
/// <list type="bullet">
///   <item><description>
///     GitHub releases: <c>github.com/structurizr/cli/releases</c>, asset
///     <c>structurizr-cli.zip</c>. The verdicts committed here were produced by <c>2025.11.09</c>.
///   </description></item>
///   <item><description>
///     Maven Central: <c>com.structurizr:structurizr-cli</c>, at
///     <c>repo1.maven.org/maven2/com/structurizr/structurizr-cli/</c>.
///   </description></item>
/// </list>
/// <para>
/// <b>The ceiling on gated skips.</b> Everything in this class needs a CLI, and that is the
/// whole of what may. The interoperability guarantee itself is checked by
/// <c>C4ReconciliationTests</c> against committed verdicts, with no JDK anywhere, and adding a
/// gated test that is the only cover for some behaviour would move that behaviour back out of
/// the everyday run. What lives here is the CLI-only half: that Structurizr accepts what ADP
/// writes, and that the committed verdicts are still what Structurizr says.
/// </para>
/// </remarks>
public class C4InteropTests : IDisposable
{
    private readonly string _root;

    public C4InteropTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        TestFolder.TryDelete(_root);
    }

    private static string? CliLibrary => Environment.GetEnvironmentVariable("ADP_STRUCTURIZR_CLI");

    /// <summary>
    /// Structurizr's own verdict on a document. Empty means valid; the CLI prints nothing at
    /// all when it is happy, and the parse error when it is not.
    /// </summary>
    private static string Verdict(string path)
    {
        var process = Process.Start(new ProcessStartInfo("java")
        {
            ArgumentList =
            {
                "-cp", IoPath.Combine(CliLibrary!, "*"),
                "com.structurizr.cli.StructurizrCliApplication",
                "validate", "-workspace", path,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? "" : output.Trim();
    }

    /// <summary>How to make these run, said in the skip message rather than in a comment nobody reads.</summary>
    private const string HowToRun =
        "Set ADP_STRUCTURIZR_CLI to the Structurizr CLI's lib folder and put java on the PATH, then run " +
        "`dotnet test --solution EtAlii.Adp.slnx` from src/backend. The CLI is at " +
        "github.com/structurizr/cli/releases, or com.structurizr:structurizr-cli on Maven Central.";

    private static void SkipWithoutTheCli() => Assert.SkipUnless(
        CliLibrary is not null && Directory.Exists(CliLibrary),
        "Skipped: no Structurizr CLI. This check hands ADP's output to Structurizr's own parser; " +
        "the everyday run checks the same guarantee against committed verdicts instead. " + HowToRun);

    private ServiceProvider Services() => new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();

    public static TheoryData<string> EveryWorkingType() =>
    [
        "c4/system-landscape",
        "c4/context",
        "c4/container",
        "c4/component",
        "c4/dynamic",
        "c4/deployment",
    ];

    [Theory]
    [MemberData(nameof(EveryWorkingType))]
    public void ADocumentAdpCreates_IsValidStructurizrDsl(string mimeType)
    {
        SkipWithoutTheCli();
        using var services = Services();
        var factory = services.GetServices<IDiagramDocumentFactory>().Single(candidate => candidate.Origin.Key == mimeType);

        var path = IoPath.Combine(_root, $"{mimeType.Replace('/', '-')}.dsl");
        File.WriteAllText(path, factory.CreateEmptyDocument("Acme Banking"));

        Assert.Equal("", Verdict(path));
    }

    [Fact]
    public async Task ADocumentAdpCreatedAndThenEdited_IsStillValidStructurizrDsl()
    {
        // The one that matters. A template is written once and can be got right by inspection;
        // what interoperability actually rests on is that every *edit* leaves the document
        // something Structurizr still reads - and ADP's edits are line surgery on someone
        // else's format.
        SkipWithoutTheCli();
        await using var services = Services();
        var factory = services.GetServices<IDiagramDocumentFactory>().Single(candidate => candidate.Origin.Key == "c4/container");
        var history = services.GetRequiredService<IHistoryStackStore>().Get(_root);

        var path = IoPath.Combine(_root, "acme.dsl");
        await File.WriteAllTextAsync(path, factory.CreateEmptyDocument("Acme Banking"), TestContext.Current.CancellationToken);

        var edits = new ICommand[]
        {
            new SetElementNameCommand(path, "Acme_Banking", "Acme Retail Banking"),
            new SetElementDescriptionCommand(path, "Acme_Banking", "Everything a customer does with their money."),
            new AddC4ViewCommand(
                path,
                IoPath.Combine(_root, "acme-context.adp"),
                "c4/context",
                C4ViewKind.SystemContext,
                "context",
                "acme.dsl"),
        };

        foreach (var edit in edits)
        {
            var result = await history.ExecuteAsync(edit, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess, result.Error);
        }

        Assert.Equal("", Verdict(path));

        // ...and undoing them all leaves something valid too, since an inverse is an edit like
        // any other.
        await history.UndoAsync(TestContext.Current.CancellationToken);
        await history.UndoAsync(TestContext.Current.CancellationToken);
        await history.UndoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("", Verdict(path));
    }

    [Theory]
    [MemberData(nameof(C4DocumentTests.Corpus), MemberType = typeof(C4DocumentTests))]
    public void EveryFixture_IsValidStructurizrDsl(string name)
    {
        // The corpus is only worth what its documents are: a fixture that is not valid DSL
        // pins ADP's misreading in place instead of catching it. `comments-everywhere.dsl` was
        // exactly that until this ran - it trailed comments after declarations, which the real
        // parser rejects as "Too many tokens".
        SkipWithoutTheCli();

        Assert.Equal("", Verdict(IoPath.Combine("Fixtures", name)));
    }

    /// <summary>
    /// Every committed verdict against what Structurizr says today.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes the baselines worth having. Moving the authority into a committed
    /// file buys a reconciliation that needs no JDK, and costs the possibility that the file
    /// has drifted from what the tool actually says. Running this makes a stale baseline
    /// detectable rather than merely possible, which is the whole difference between a
    /// baseline and a fiction.
    /// </para>
    /// <para>
    /// A stale baseline fails <em>here</em> and not in the everyday run. They are different
    /// faults with different owners: the reconciliation failing means ADP and Structurizr
    /// disagree about a model, and this failing means the recorded answer is out of date.
    /// Making the everyday run carry both would tell a developer with no JDK to go and fix
    /// something they cannot reproduce.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(C4DocumentTests.Corpus), MemberType = typeof(C4DocumentTests))]
    public void EveryVerdict_IsStillWhatStructurizrSays(string name)
    {
        SkipWithoutTheCli();

        // Arrange.
        var committed = C4Verdict.Read(name);

        // Act.
        // Rule ids rather than whole lines: Structurizr is free to reword a message between
        // versions, and a baseline that fails because a sentence improved is a baseline people
        // learn to regenerate without reading.
        var today = Inspect(IoPath.Combine("Fixtures", name));

        // Assert.
        Assert.Equal(
            committed.Findings.Select(finding => finding.RuleId).Order(StringComparer.Ordinal),
            today.Select(finding => finding.RuleId).Order(StringComparer.Ordinal));
    }

    /// <summary>What <c>inspect</c> reports for <paramref name="path"/> right now.</summary>
    /// <remarks>
    /// <c>inspect</c> exits with the number of findings rather than with zero, so its exit code
    /// says how much it found and never whether it worked. Only the output is read.
    /// </remarks>
    private static IReadOnlyList<C4VerdictFinding> Inspect(string path)
    {
        var process = Process.Start(new ProcessStartInfo("java")
        {
            ArgumentList =
            {
                "-cp", IoPath.Combine(CliLibrary!, "*"),
                "com.structurizr.cli.StructurizrCliApplication",
                "inspect", "-workspace", path,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return output
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains('|', StringComparison.Ordinal))
            .Select(line => line.Split('|', 3))
            .Select(parts => new C4VerdictFinding(parts[0].Trim(), parts[1].Trim(), parts[2].Trim()))
            .ToArray();
    }

    /// <summary>
    /// Every fixture exported to a diagram format, against the exports committed beside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Validating parses and inspecting judges; neither draws anything. This is the third
    /// question, and the one a reader of the diagram actually cares about: does another tool
    /// turn what ADP wrote into a picture, and is the picture the model? Mermaid is the format
    /// because its output is text with the element names in it, so a diff is legible and an
    /// empty diagram is visibly empty.
    /// </para>
    /// <para>
    /// Exporting is not the assertion. A view that renders empty exports perfectly happily, and
    /// ADP shipped exactly that once - a `c4/deployment` template whose `deploymentEnvironment`
    /// had no nodes in it, so the view bound to nothing and drew nothing while every command
    /// exited zero. <c>C4ExportTests</c> is what checks the elements are there, and it does it
    /// without a JDK.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(C4DocumentTests.Corpus), MemberType = typeof(C4DocumentTests))]
    public void EveryExport_IsStillWhatStructurizrDraws(string name)
    {
        SkipWithoutTheCli();

        // Arrange.
        var fixture = IoPath.GetFileNameWithoutExtension(name);
        var committed = C4Export.Read(fixture);
        var directory = IoPath.Combine(_root, "export", fixture);
        Directory.CreateDirectory(directory);

        // Act.
        Export(IoPath.Combine("Fixtures", name), directory);
        var today = Directory.GetFiles(directory, "*.mmd")
            .ToDictionary(v => IoPath.GetFileName(v), File.ReadAllText, StringComparer.Ordinal);

        // Assert, step by step.
        // The names first, so a view that stopped being exported reads as the missing view it
        // is rather than as a puzzling content mismatch.
        Assert.Equal(committed.Keys.Order(StringComparer.Ordinal), today.Keys.Order(StringComparer.Ordinal));
        foreach ((string diagram, string content) in today)
        {
            Assert.Equal(Normalise(committed[diagram]), Normalise(content));
        }
    }

    /// <summary>Runs the CLI's <c>export</c> into <paramref name="directory"/>.</summary>
    private static void Export(string workspace, string directory)
    {
        var process = Process.Start(new ProcessStartInfo("java")
        {
            ArgumentList =
            {
                "-cp", IoPath.Combine(CliLibrary!, "*"),
                "com.structurizr.cli.StructurizrCliApplication",
                "export", "-workspace", workspace, "-format", "mermaid", "-output", directory,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"Exporting '{workspace}' failed: {output.Trim()}");
    }

    /// <summary>Line endings only, since those are the platform's rather than Structurizr's.</summary>
    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
