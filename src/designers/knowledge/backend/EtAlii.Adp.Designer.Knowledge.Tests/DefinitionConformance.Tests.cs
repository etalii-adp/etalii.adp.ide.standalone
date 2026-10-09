using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>
/// The module against the designer's specification (knowledge-designer Requirement 10.5): its
/// value types, the key each is stored under, the comparisons of each and what a view stores are
/// those of <c>definition/knowledge.des</c>, in both directions.
/// </summary>
/// <remarks>
/// <b>Both directions, each named.</b> A difference is reported as what the module is
/// <i>missing</i> - the specification has it and the module does not - or as what is <i>unknown
/// to the definition</i> - the module has it and the specification does not - so the failure says
/// which of the two has to change. The specification is read here from the file itself, not
/// through the module's own loader: a guard that asked the module what the specification says
/// would compare the module with itself.
/// </remarks>
public sealed class DefinitionConformanceTests
{
    private static JsonElement Definition()
    {
        var text = File.ReadAllText(IoPath.Combine(KnowledgeFiles.DefinitionFolder, "knowledge.des"));
        using var document = JsonDocument.Parse(text.TrimStart('﻿'));
        return document.RootElement.Clone();
    }

    /// <summary>What one side has and the other does not, as the two sentences a failure is made of.</summary>
    private static List<string> Differences(string what, IEnumerable<string> definition, IEnumerable<string> module)
    {
        (HashSet<string> defined, HashSet<string> implemented) = (definition.ToHashSet(StringComparer.Ordinal), module.ToHashSet(StringComparer.Ordinal));
        return
        [
            .. defined.Except(implemented).Order(StringComparer.Ordinal).Select(name => $"{what} '{name}' is missing from the module."),
            .. implemented.Except(defined).Order(StringComparer.Ordinal).Select(name => $"{what} '{name}' is unknown to the definition."),
        ];
    }

    [Fact]
    public void TheModulesValueTypes_AreTheDefinitions()
    {
        // Arrange.
        var defined = Definition().GetProperty("metamodel").GetProperty("enums").GetProperty("ValueType").GetProperty("values").EnumerateObject().Select(type => type.Name).ToList();

        // Assert: the definition was read at all, and the two lists are one.
        Assert.Equal(9, defined.Count);
        Assert.Empty(Differences("The value type", defined, KnowledgeVocabulary.ValueTypes));

        // And each is shown: a type the surface does not describe has no key, no editor and no comparison.
        var shown = Definition().GetProperty("surface").GetProperty("valueTypes").EnumerateObject().Select(type => type.Name);
        Assert.Empty(Differences("The shown value type", shown, KnowledgeVocabulary.ValueTypes));
    }

    [Fact]
    public void EachValueType_IsStoredUnderTheKeyTheDefinitionGivesIt()
    {
        // Arrange.
        var surface = Definition().GetProperty("surface").GetProperty("valueTypes");

        // Assert.
        var differing = surface.EnumerateObject()
            .Where(type => type.Value.GetProperty("key").GetString() != KnowledgeVocabulary.StoredKey(type.Name))
            .Select(type => $"{type.Name}: the definition stores it under '{type.Value.GetProperty("key").GetString()}', the module under '{KnowledgeVocabulary.StoredKey(type.Name)}'.");
        Assert.Empty(differing);

        // The keys of a cell that holds one value are the cell's value attributes in the metamodel.
        var attributes = Definition().GetProperty("metamodel").GetProperty("types").GetProperty("Cell").GetProperty("attributes").EnumerateObject().Select(attribute => attribute.Name).Where(name => name != "property");
        Assert.Empty(Differences("The cell's value key", attributes, KnowledgeVocabulary.ValueKeys));
    }

    [Fact]
    public void EachValueTypesComparisons_AreTheDefinitions_InItsOrder()
    {
        // Arrange.
        var surface = Definition().GetProperty("surface").GetProperty("valueTypes");

        // Assert.
        List<string> differing = [];
        foreach (var type in surface.EnumerateObject())
        {
            var defined = type.Value.GetProperty("comparisons").EnumerateArray().Select(comparison => comparison.GetString()!).ToList();
            var implemented = KnowledgeVocabulary.Comparisons.GetValueOrDefault(type.Name) ?? [];
            differing.AddRange(Differences($"For {type.Name}, the comparison", defined, implemented));
            if (differing.Count == 0 && !defined.SequenceEqual(implemented))
            {
                differing.Add($"For {type.Name}, the comparisons are in another order: the first is what a new condition starts as.");
            }
        }

        Assert.Empty(differing);
        Assert.Empty(Differences("The type with comparisons", surface.EnumerateObject().Select(type => type.Name), KnowledgeVocabulary.Comparisons.Keys));
    }

    [Fact]
    public void WhatAViewStores_IsWhatTheDefinitionSaysItDoes()
    {
        // Arrange.
        var settings = Definition().GetProperty("surface").GetProperty("views").GetProperty("settings").EnumerateObject().Select(setting => setting.Value.GetString()!).ToList();

        // Assert.
        Assert.NotEmpty(settings);
        Assert.Empty(Differences("The view setting", settings, KnowledgeVocabulary.ViewSettings));
    }

    [Fact]
    public void AnOptionsColours_AreTheDefinitions()
    {
        // Arrange.
        var colours = Definition().GetProperty("metamodel").GetProperty("enums").GetProperty("Colour").GetProperty("values").EnumerateObject().Select(colour => colour.Name).ToList();

        // Assert.
        Assert.Equal(10, colours.Count);
        Assert.Empty(Differences("The colour", colours, KnowledgeVocabulary.Colours));
    }

    [Fact]
    public void ADifference_IsNamedInItsDirection()
    {
        // The guard's own control: what a type removed from the module, and one added only to it, are reported as.
        Assert.Equal(
            ["The value type 'time' is missing from the module.", "The value type 'person' is unknown to the definition."],
            Differences("The value type", ["text", "time"], ["text", "person"]));
        Assert.Empty(Differences("The value type", ["text", "time"], ["time", "text"]));
    }
}
