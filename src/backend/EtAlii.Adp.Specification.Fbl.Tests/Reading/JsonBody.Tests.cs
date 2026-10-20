using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Json;
using EtAlii.Adp.Specification.Fbl.Text;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Reading;

/// <summary>
/// The json family reads a body on .NET's JSON reader (FBL §4.4; spec 025 T219, behaviour change B-29):
/// what RFC 8259 refuses is unreadable, the reader's own sentence is the reason, at the reader's offset.
/// </summary>
public class JsonBodyTests
{
    private static readonly FblBinding _binding = Inline();

    [Theory]
    [InlineData("""{ "a": 01 }""")]
    [InlineData("""{ "a": 1. }""")]
    [InlineData("""{ "a": .5 }""")]
    [InlineData("""{ "a": -.5 }""")]
    [InlineData("""{ "a": nan }""")]
    [InlineData("""{ "a": infinity }""")]
    public void ANumberRfc8259RefusesIsUnreadable(string body)
    {
        // Act.
        var family = Parse(body);

        // Assert: these were read as numbers before the json family read on the platform's reader.
        Assert.NotNull(family.Unreadable);
    }

    [Fact]
    public void ABodyNestedDeeperThanTheReaderGoesIsUnreadable()
    {
        // Arrange: deeper than the 1,024 levels the json family reads, and shallow enough for any stack.
        var body = new string('[', 2000) + new string(']', 2000);

        // Act.
        var family = Parse(body);

        // Assert.
        Assert.NotNull(family.Unreadable);
    }

    [Fact]
    public void TheReasonIsTheReadersSentenceAtItsOffset()
    {
        // Act.
        var family = Parse("{\n  \"a\": 1\n}}");

        // Assert: the sentence leaves out the line and position the reader appends; the offset says where.
        Assert.Equal((12, "'}' is invalid after a single JSON value. Expected end of data."), family.Unreadable);
    }

    [Fact]
    public void TheOffsetCountsTheByteOrderMark()
    {
        // Act.
        var family = Parse("﻿{ \"a\": 1 ]");

        // Assert.
        Assert.Equal(12, family.Unreadable?.Offset);
    }

    [Fact]
    public void AnEscapedSurrogateWithoutItsPairIsRead()
    {
        // Act: RFC 8259 §8.2 lets a string hold one; the platform's reader will not decode it.
        var family = Parse("""{ "a": "\ud800" }""");

        // Assert.
        Assert.Null(family.Unreadable);
        Assert.Equal("\ud800", family.Root!.Value.Entries[0].Value.Text);
    }

    private static JsonFamily Parse(string body)
    {
        var family = new JsonFamily(new BodyText(Encoding.UTF8.GetBytes(body)), _binding, new FblOptions { FileName = "plan.t" });
        family.Parse();
        return family;
    }

    private static FblBinding Inline()
    {
        var json = """{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "json" }, "reader": "declared", "elements": [{ "name": "item", "type": "Item", "at": "/items/*" }] } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }
}
