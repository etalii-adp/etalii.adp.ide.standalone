using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Loading;

/// <summary>
/// FBL 0.14's checks of an insert that adds a member of a mapping, named by its key (sections 6.2 and 14.1, step 6): the
/// rule binds an attribute to the capture its <c>at</c> ends in, and the sentences of that key stand on no other insert.
/// </summary>
public class MemberInsertsTests
{
    [Theory]
    [InlineData("insert-member-without-capture.fbl", "/bindings/lanes/elements/0/insert")]
    [InlineData("insert-refusals-not-member.fbl", "/bindings/lanes/elements/0/insert/refusals/key", "/bindings/lanes/elements/0/insert/refusals/duplicate")]
    public void TheVendoredRefusedBinding_IsRefusedAtItsInsert(string file, params string[] pointers)
    {
        // Act.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "refused", file), out var document);

        // Assert: refused for that reason alone, at those places.
        Assert.Equal(pointers, problems.Where(problem => problem.Severity == LoadSeverity.Error).Select(problem => problem.Pointer));
        Assert.Null(document);
    }

    [Theory]
    [InlineData("fixtures/insert-member-json/lanes.fbl")]
    [InlineData("databricks.fbl")]
    public void AMemberInsertWithItsKeyAttribute_Loads(string file)
    {
        // Act.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, file), out var document);

        // Assert.
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        Assert.NotNull(document);
    }
}
