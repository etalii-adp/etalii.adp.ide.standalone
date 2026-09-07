using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Common.Wire.Path;

namespace EtAlii.Adp.Problems;

/// <summary>
/// The one thing that turns a problem-set change into a push: it subscribes once to the
/// problem store, and on each change reads the project's set and sends it to that project's
/// connections (Requirements 1.1, 1.2, 6.4) - the only class that knows both stores, so the
/// selection store never learns where problems come from and the problem store never learns
/// about connections. <see cref="HistoryActionsBroadcaster"/> is the precedent.
/// </summary>
public sealed class ProblemBroadcaster : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<ProblemBroadcaster>();

    private readonly IProblemStore _problems;
    private readonly IContextSelectionStore _selectionStore;
    private bool _disposed;

    public ProblemBroadcaster(IProblemStore problems, IContextSelectionStore selectionStore)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(selectionStore);
        _problems = problems;
        _selectionStore = selectionStore;
        _problems.Changed += OnProblemsChanged;
    }

    /// <summary>
    /// The project's current set as the wire carries it - what the service puts on a new
    /// connection's baseline, so the panel is current the moment it connects.
    /// </summary>
    public ProjectProblems CurrentFor(string rootPath) => ToProto(_problems.Get(rootPath));

    public void Dispose()
    {
        _disposed = true;
        _problems.Changed -= OnProblemsChanged;
    }

    private void OnProblemsChanged(string rootPath)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _selectionStore.PushProblems(rootPath, ToProto(_problems.Get(rootPath)));
        }
        catch (Exception exception)
        {
            // A push that failed pushes nothing; the clients keep the list they last had
            // rather than losing it to a transient fault.
            _logger.Warning(exception, "Could not broadcast the problems of {RootPath}", rootPath);
        }
    }

    public static ProjectProblems ToProto(ProjectProblemSet set)
    {
        var proto = new ProjectProblems
        {
            State = set.State switch
            {
                ProjectProblemSetState.Validating => ProblemSetState.Validating,
                ProjectProblemSetState.Validated => ProblemSetState.Validated,
                _ => ProblemSetState.NeverValidated,
            },
            ErrorCount = (uint)set.ErrorCount,
            WarningCount = (uint)set.WarningCount,
            InfoCount = (uint)set.InfoCount,
            TruncatedAt = (uint)set.TruncatedAt,
        };
        proto.Problems.AddRange(set.Problems.Select(ToProto));
        return proto;
    }

    private static Problem ToProto(StoredProblem stored)
    {
        var problem = new Problem
        {
            // A switch rather than a ternary, and one arm per level: the ternary this replaced
            // collapsed everything that was not an Error into a Warning, so a third severity
            // would have reached the client disguised as the second one.
            Severity = stored.Problem.Severity switch
            {
                DiagramProblemSeverity.Error => ProblemSeverity.Error,
                DiagramProblemSeverity.Warning => ProblemSeverity.Warning,
                DiagramProblemSeverity.Info => ProblemSeverity.Info,
                _ => ProblemSeverity.Unspecified,
            },
            Message = stored.Problem.Message,
            RuleId = stored.Problem.RuleId,
            Stale = stored.Stale,
            Path = new Path
            {
                Segments =
                {
                    stored.RelativePath.Split(
                        [IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries),
                },
            },
        };
        switch (stored.Problem.Location)
        {
            case DiagramProblemElementLocation elementId:
                problem.Location = new ProblemLocation { ElementId = new ElementId { Value = elementId.Id } };
                break;
            case DiagramProblemLineLocation line:
                problem.Location = new ProblemLocation { Line = line.Number };
                break;
            case DiagramProblemFileLocation file:
                problem.Location = new ProblemLocation
                {
                    File = new ProblemFileLocation
                    {
                        Path = new Path
                        {
                            Segments =
                            {
                                file.RelativePath.Split(
                                    [IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar],
                                    StringSplitOptions.RemoveEmptyEntries),
                            },
                        },
                        Line = file.Line,
                    },
                };
                break;
        }
        return problem;
    }
}
