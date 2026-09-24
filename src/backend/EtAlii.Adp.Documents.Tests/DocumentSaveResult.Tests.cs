using Xunit;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The shared save result: the three outcomes, and the claim that no constructed value is
/// unanswerable.
/// </summary>
/// <remarks>
/// <b>The last test is the one the type's KIND rests on</b>, and it is here because the whole reason
/// this is a sealed record rather than wardley's `readonly record struct` is what an unassigned
/// value does. A struct's `default` carries null strings and flows through code as a usable object;
/// a null reference throws at the first access. That is a contract worth asserting rather than
/// leaving to the language's behaviour being obvious to the next reader.
/// </remarks>
public class DocumentSaveResultTests
{
    [Fact]
    public void Ok_HasNotFailed_AndSaysNothing()
    {
        // Arrange, act.
        var result = DocumentSaveResult.Ok;

        // Assert.
        Assert.False(result.Failed);
        Assert.Equal("", result.Error);
        Assert.Equal("", result.Warning);
    }

    [Fact]
    public void Failure_HasFailed_AndCarriesTheReasonAndNoWarning()
    {
        // Arrange, act.
        var result = DocumentSaveResult.Failure("plan.tml could not be written.");

        // Assert.
        Assert.True(result.Failed);
        Assert.Equal("plan.tml could not be written.", result.Error);
        Assert.Equal("", result.Warning);
    }

    [Fact]
    public void WithWarning_HasNOTFailed_AndCarriesTheWarning()
    {
        // This is the case the two strings exist for, and the one a single string collapsed: the
        // document IS on disk and something beside it is not. A caller that treats a warning as a
        // failure undoes an edit that landed.
        var result = DocumentSaveResult.WithWarning("The identity sidecar was not written.");

        Assert.False(result.Failed);
        Assert.Equal("", result.Error);
        Assert.Equal("The identity sidecar was not written.", result.Warning);
    }

    [Fact]
    public void TwoResultsWithTheSameWords_AreEqual()
    {
        // Record equality is deliberate: a caller may compare results, and reference equality would
        // make two identical failures unequal for a reason that has nothing to do with saving.
        Assert.Equal(DocumentSaveResult.Failure("the same"), DocumentSaveResult.Failure("the same"));
        Assert.NotEqual(DocumentSaveResult.Failure("this"), DocumentSaveResult.Failure("that"));
    }

    [Fact]
    public void NoFactoryAcceptsNull_SoNoCONSTRUCTEDValueIsUnanswerable()
    {
        // Every instance comes from a factory, and neither string can be null in one - so `Failed`
        // is always answerable for any value that exists. Without this, a null slipped through a
        // factory would make `Failed` throw from inside a perfectly ordinary-looking result.
        Assert.Throws<ArgumentNullException>(() => DocumentSaveResult.Failure(null!));
        Assert.Throws<ArgumentNullException>(() => DocumentSaveResult.WithWarning(null!));
    }

    [Fact]
    public void AnUnassignedResult_ThrowsAtTheFirstQuestion_RatherThanReadingAsEitherOutcome()
    {
        // Arrange: what a forgotten assignment or an unstubbed mock leaves behind.
        DocumentSaveResult? unassigned = null;

        // Act, assert: asking whether it failed throws, rather than answering. THIS IS THE
        // JUSTIFICATION FOR THE TYPE BEING A CLASS. Had it stayed wardley's struct, `default` would
        // be a usable value whose `Error` is null - `Error != ""` reads as failed-with-no-message,
        // an interpolated `Error` prints as empty, and the mistake travels. Here it stops at the
        // first question asked of it.
        Assert.Throws<NullReferenceException>(() => unassigned!.Failed);
    }
}
