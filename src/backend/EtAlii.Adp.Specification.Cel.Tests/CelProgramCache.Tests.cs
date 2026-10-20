using System.Collections.Concurrent;
using Xunit;

namespace EtAlii.Adp.Specification.Cel.Tests;

/// <summary>The one cache of compiled programs: one compilation per key, whoever asks and however many at once.</summary>
public class CelProgramCacheTests
{
    private static readonly string[] Expressions = ["1 + 1", "'a' + 'b'", "[1, 2].size()", "x * 2", "1 +"];

    [Fact]
    public void ManyThreadsAskingAtOnce_CompileEachKeyOnce_AndAreAllGivenItsOneProgram()
    {
        // Arrange: a compiler slow enough that the threads overlap inside it.
        var compilations = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var cache = new CelProgramCache<string>(expression =>
        {
            compilations.AddOrUpdate(expression, 1, (_, count) => count + 1);
            Thread.Sleep(20);
            return CelEnvironment.Standard().DeclareVariable("x").Compile(expression);
        });
        const int threadCount = 16;
        var given = new ConcurrentBag<(string Expression, CelProgram? Program, string? Problem)>();
        var cancellation = TestContext.Current.CancellationToken;
        using var start = new Barrier(threadCount);
        // The barrier is handed to each thread as it starts rather than captured, since it is disposed here.
        var threads = Enumerable.Range(0, threadCount).Select(index => new Thread(barrier =>
        {
            ((Barrier)barrier!).SignalAndWait(cancellation);
            // Each thread asks for the keys in its own order, so threads meet on every key.
            foreach (var expression in Expressions.Skip(index % Expressions.Length).Concat(Expressions.Take(index % Expressions.Length)))
            {
                cache.TryGet(expression, out var program, out var problem);
                given.Add((expression, program, problem));
            }
        })).ToList();

        // Act.
        threads.ForEach(thread => thread.Start(start));
        threads.ForEach(thread => thread.Join());

        // Assert.
        Assert.Equal(Expressions.Order(StringComparer.Ordinal), compilations.Keys.Order(StringComparer.Ordinal));
        Assert.All(compilations, compilation => Assert.Equal(1, compilation.Value));
        foreach (var expression in Expressions)
        {
            var answers = given.Where(answer => answer.Expression == expression).ToList();
            Assert.Equal(threadCount, answers.Count);
            Assert.Single(answers.Select(answer => answer.Program).Distinct(ReferenceEqualityComparer.Instance));
        }
        Assert.All(given.Where(answer => answer.Expression == "1 +"), answer => Assert.Equal("'1 +' ends too early.", answer.Problem));
        Assert.All(given.Where(answer => answer.Expression != "1 +"), answer => Assert.NotNull(answer.Program));
    }

    [Fact]
    public void AnExpressionThatDoesNotCompile_IsCompiledOnce_AndGivesItsReasonEveryTime()
    {
        // Arrange.
        var compilations = 0;
        var cache = new CelProgramCache<string>(expression =>
        {
            compilations++;
            return CelEnvironment.Standard().Compile(expression);
        });

        // Act.
        var first = cache.TryGet("y + 1", out _, out var firstProblem);
        var second = cache.TryGet("y + 1", out _, out var secondProblem);

        // Assert.
        Assert.False(first);
        Assert.False(second);
        Assert.Equal("'y' is not a variable here; available: .", firstProblem);
        Assert.Equal(firstProblem, secondProblem);
        Assert.Equal(1, compilations);
    }
}
