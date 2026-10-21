using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// The registry of value types (FR-087): every type the designer's specification has is found by
/// its name as the one strategy for it, a name it does not have is a type of its own that behaves
/// as one the module does not know, and the key each type says its values are under is the key a
/// cell of it has when a shipped example is read.
/// </summary>
/// <remarks>
/// The names are read from <c>definition/knowledge.des</c> itself, not from
/// <see cref="KnowledgeVocabulary"/>: the vocabulary is read from the registry, and a guard that
/// compared the two would compare the registry with itself.
/// </remarks>
public sealed class KnowledgeValueTypesTests
{
    private static List<string> DefinedNames()
    {
        var text = File.ReadAllText(IoPath.Combine(KnowledgeFiles.DefinitionFolder, "knowledge.des"));
        using var document = JsonDocument.Parse(text.TrimStart('﻿'));
        return [.. document.RootElement.GetProperty("metamodel").GetProperty("enums").GetProperty("ValueType").GetProperty("values").EnumerateObject().Select(type => type.Name)];
    }

    [Fact]
    public void EachDefinedValueType_IsFoundByItsName_AsTheRegistrysOneStrategyForIt()
    {
        // Arrange.
        var defined = DefinedNames();

        // Assert: the specification was read at all, and the registry has each of its types once and no other.
        Assert.Equal(9, defined.Count);
        Assert.Equal(defined.Order(StringComparer.Ordinal), KnowledgeValueTypes.All.Select(type => type.Name).Order(StringComparer.Ordinal));
        Assert.All(defined, name =>
        {
            var type = KnowledgeValueTypes.Of(name);
            Assert.True(type.IsKnown, $"'{name}' is not known to the registry.");
            Assert.Equal(name, type.Name);
            Assert.Same(type, KnowledgeValueTypes.Of(name));
            Assert.Contains(type, KnowledgeValueTypes.All);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("colour")]
    [InlineData("Text")]
    [InlineData("options")]
    public void AName_TheSpecificationDoesNotHave_IsATypeOfItsOwn_ThatCanOnlyBeKept(string name)
    {
        // Act.
        var type = KnowledgeValueTypes.Of(name);

        // Assert: not one of the registry's, stored under its own name, compared only as empty or not, and never written.
        Assert.False(type.IsKnown);
        Assert.DoesNotContain(type, KnowledgeValueTypes.All);
        Assert.Equal(name, type.Name);
        Assert.Equal(name, type.StoredKey);
        Assert.Equal(name, type.CellKey);
        Assert.Empty(type.Comparisons);
        Assert.True(type.Compares("is-empty"));
        Assert.False(type.Compares("is"));
        Assert.Equal(KnowledgeValueType.CannotBeSet, type.Write("value", out _));
    }

    [Fact]
    public void EachTypesCellKey_IsTheKeyACellOfItHas_WhenAnExampleIsRead()
    {
        // Arrange: the shipped example, which has a property of every type.
        var table = KnowledgeBody.Read(KnowledgeFiles.ExampleBytes("cities.yaml"), "cities.yaml").Table;
        var properties = table.Properties.ToDictionary(property => property.Id, StringComparer.Ordinal);

        // Act.
        var cells = table.Rows.SelectMany(row => row.Cells).Where(cell => cell.Values.Count > 0).Select(cell => (properties[cell.PropertyId].Type, cell.Key)).ToList();

        // Assert: every type is met, and each cell is under its type's key.
        Assert.Equal(KnowledgeValueTypes.All.Select(type => type.Name).Order(StringComparer.Ordinal), cells.Select(cell => cell.Type.Name).Distinct().Order(StringComparer.Ordinal));
        Assert.All(cells, cell => Assert.Equal(cell.Type.CellKey, cell.Key));
    }
}
