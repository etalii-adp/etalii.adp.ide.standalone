using System.Diagnostics;
using EtAlii.Adp.Backend;
using EtAlii.Adp.Diagram;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

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
/// </remarks>
public class C4InteropTests : IDisposable
{
    private readonly string _root;

    public C4InteropTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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

    private static void SkipWithoutTheCli() => Assert.SkipUnless(
        CliLibrary is not null && Directory.Exists(CliLibrary),
        "Set ADP_STRUCTURIZR_CLI to the Structurizr CLI's lib folder to check ADP's output against Structurizr's own parser.");

    private ServiceProvider Services() => new ServiceCollection().AddCommands().AddC4().BuildServiceProvider();

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
        using var services = Services();
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
}
