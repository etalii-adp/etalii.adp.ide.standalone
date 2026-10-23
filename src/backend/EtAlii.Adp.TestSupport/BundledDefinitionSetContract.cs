using System.Text.Json.Nodes;
using Xunit;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// <see cref="BundledDefinitionContract{TModule}"/> for a module whose definitions repeat parts rather than import them: the
/// parts are equal in every definition, compared as parsed data, so a change made to one and not the others is caught here
/// rather than as one reading that behaves apart.
/// </summary>
public abstract class BundledDefinitionSetContract<TModule> : BundledDefinitionContract<TModule>
    where TModule : IBundledDefinitions
{
    /// <summary>The parts every definition repeats, each named as a failure names it and read off a parsed definition.</summary>
    protected abstract IReadOnlyList<(string Part, Func<JsonNode, JsonNode?> Of)> RepeatedParts { get; }

    /// <summary>The repeated parts are equal in every definition.</summary>
    [Fact]
    public async Task TheRepeatedParts_AreEqualInEveryDefinition()
    {
        var parsed = new List<(string File, JsonNode Definition)>();
        foreach (var definition in TModule.Definitions)
        {
            parsed.Add((definition.Name + ".dis", JsonNode.Parse(await BundledFileAsync(definition.Name + ".dis"))!));
        }

        // The sentences in full, because a collection's failure message cuts them short of the second file.
        var differences = Differences(parsed);

        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    /// <summary>
    /// The comparison names the part and the two files when one definition's part differs, so
    /// <see cref="TheRepeatedParts_AreEqualInEveryDefinition"/> cannot pass by comparing nothing.
    /// </summary>
    [Fact]
    public async Task TheRepeatedPartComparison_NamesThePartAndTheTwoFiles()
    {
        var first = TModule.Definitions[0].Name + ".dis";
        var definition = JsonNode.Parse(await BundledFileAsync(first))!;
        var changed = definition.DeepClone();
        RepeatedParts[0].Of(changed)!.AsObject()["x-changed"] = true;

        var differences = Differences([(first, definition), ("changed.dis", changed)]);

        Assert.Equal([$"The {RepeatedParts[0].Part} of changed.dis differs from that of {first}."], differences);
    }

    /// <summary>Every repeated part of each definition after the first that is not equal to the first's, as a sentence naming the part and the two files.</summary>
    private List<string> Differences(IReadOnlyList<(string File, JsonNode Definition)> definitions)
    {
        (string firstFile, JsonNode first) = definitions[0];
        return
        [
            .. from definition in definitions.Skip(1)
               from part in RepeatedParts
               where !JsonNode.DeepEquals(part.Of(first), part.Of(definition.Definition))
               select $"The {part.Part} of {definition.File} differs from that of {firstFile}.",
        ];
    }
}
