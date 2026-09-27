using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// The energy-breakthroughs, llms-and-agents, coal-technologies, electric-vehicles, internet-evolution and
/// warfare-in-ukraine examples, held to what their readmes claim - every claim
/// asserted from the parsed model rather than by eye, as <see cref="GhgDigitalExampleTests"/> does for
/// digital-trends.
/// </summary>
public class GhgEnergyAndAgentsExamplesTests
{
    public static TheoryData<string, int, int> Sizes => new()
    {
        { "energy-breakthroughs", 33, 46 },
        { "llms-and-agents", 35, 56 },
        { "coal-technologies", 34, 45 },
        { "electric-vehicles", 28, 40 },
        { "internet-evolution", 34, 46 },
        { "warfare-in-ukraine", 29, 42 },
    };

    public static TheoryData<string> Examples => ["energy-breakthroughs", "llms-and-agents", "coal-technologies", "electric-vehicles", "internet-evolution", "warfare-in-ukraine"];

    public static TheoryData<string, int> EveryPhaseCount
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var name in new[] { "energy-breakthroughs", "llms-and-agents", "coal-technologies", "electric-vehicles", "internet-evolution", "warfare-in-ukraine" })
            {
                for (var phases = 1; phases <= GhgPhases.Count; phases++)
                {
                    data.Add(name, phases);
                }
            }

            return data;
        }
    }

    private static LineDocument Document(string name) => LineDocument.Parse(File.ReadAllText(GhgModuleFiles.ExampleNamed(name)));

    private static GhgModel Model(string name) => GhgParser.Parse(Document(name));

    [Theory]
    [MemberData(nameof(Sizes))]
    public void TheExample_ReadsWithoutAProblem_AndHasTheSizeItsReadmeSays(string name, int trends, int influences)
    {
        var model = Model(name);

        Assert.Equal(GhgModel.CurrentVersion, model.Version);
        Assert.Empty(model.Problems);
        Assert.Equal(trends, model.Trends.Count);
        Assert.Equal(influences, model.Influences.Count);
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void TheValidator_ReportsNothingForTheExample(string name)
    {
        var breaches = GhgValidator.Validate(Document(name));

        Assert.True(
            breaches.Count == 0,
            "The example breaks the rules it exists to demonstrate: "
            + string.Join("; ", breaches.Select(breach => $"{breach.RuleId} at line {breach.Line + 1}: {breach.Message}")));
    }

    [Theory]
    [MemberData(nameof(EveryPhaseCount))]
    public void EveryPhaseCount_Occurs(string name, int phases)
    {
        Assert.Contains(Model(name).Trends, trend => trend.Phases == phases);
    }

    /// <summary>Both readmes say no influence is hidden: each attaches to a phase both its trends show.</summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void NoInfluence_IsHiddenByAPhaseCount(string name)
    {
        var model = Model(name);
        var trends = model.Trends.ToDictionary(trend => trend.Id);

        Assert.All(model.Influences, influence =>
        {
            Assert.True(influence.FromEnd.PhaseIndex < trends[influence.From].VisiblePhases, $"{influence.Id} leaves a phase {influence.From} does not show.");
            Assert.True(influence.ToEnd.PhaseIndex < trends[influence.To].VisiblePhases, $"{influence.Id} lands on a phase {influence.To} does not show.");
        });
    }

    /// <summary>Both readmes say no two trends in a row overlap, and an empty row separates every two rows of trends.</summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public void RowsHoldNoOverlap_AndAnEmptyRowSeparatesThem(string name)
    {
        var trends = Model(name).Trends;
        foreach (var row in trends.GroupBy(trend => trend.Row))
        {
            var ordered = row.OrderBy(trend => trend.Start).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                Assert.True(ordered[index].Start >= ordered[index - 1].Stop, $"{ordered[index - 1].Id} and {ordered[index].Id} overlap on row {row.Key}.");
            }
        }

        var rows = trends.Select(trend => trend.Row).Distinct().Order().ToList();
        Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.True(pair.Second - pair.First >= 2, $"Rows {pair.First} and {pair.Second} both hold trends with no empty row between them."));
    }

    [Theory]
    [InlineData("energy-breakthroughs", "fusion")]
    [InlineData("energy-breakthroughs", "nuclear or fusion")]
    [InlineData("energy-breakthroughs", "upcoming or conceptual")]
    [InlineData("llms-and-agents", "agents")]
    [InlineData("llms-and-agents", "agents and coding")]
    [InlineData("llms-and-agents", "upcoming or conceptual")]
    [InlineData("coal-technologies", "power")]
    [InlineData("coal-technologies", "gasification or liquids")]
    [InlineData("coal-technologies", "upcoming or conceptual")]
    [InlineData("electric-vehicles", "batteries")]
    [InlineData("electric-vehicles", "vehicles or charging")]
    [InlineData("electric-vehicles", "upcoming or conceptual")]
    [InlineData("internet-evolution", "networking")]
    [InlineData("internet-evolution", "web or community")]
    [InlineData("internet-evolution", "upcoming or conceptual")]
    [InlineData("warfare-in-ukraine", "drones")]
    [InlineData("warfare-in-ukraine", "drones and ew")]
    [InlineData("warfare-in-ukraine", "upcoming or conceptual")]
    public void EachFilterTheReadmeNames_MatchesSomeTrends_AndHidesSome(string name, string filter)
    {
        Func<IReadOnlyList<string>, bool> matches = filter switch
        {
            "fusion" => tags => tags.Contains("fusion"),
            "agents" => tags => tags.Contains("agents"),
            "power" => tags => tags.Contains("power"),
            "batteries" => tags => tags.Contains("batteries"),
            "networking" => tags => tags.Contains("networking"),
            "drones" => tags => tags.Contains("drones"),
            "vehicles or charging" => tags => tags.Contains("vehicles") || tags.Contains("charging"),
            "web or community" => tags => tags.Contains("web") || tags.Contains("community"),
            "drones and ew" => tags => tags.Contains("drones") && tags.Contains("ew"),
            "gasification or liquids" => tags => tags.Contains("gasification") || tags.Contains("liquids"),
            "nuclear or fusion" => tags => tags.Contains("nuclear") || tags.Contains("fusion"),
            "agents and coding" => tags => tags.Contains("agents") && tags.Contains("coding"),
            "upcoming or conceptual" => tags => tags.Contains("upcoming") || tags.Contains("conceptual"),
            _ => throw new ArgumentOutOfRangeException(nameof(filter)),
        };

        var trends = Model(name).Trends;

        Assert.Contains(trends, trend => matches(trend.Tags));
        Assert.Contains(trends, trend => !matches(trend.Tags));
    }

    [Theory]
    [InlineData("energy-breakthroughs", "upcoming", new[] { "fusion-power-plants" })]
    [InlineData("energy-breakthroughs", "conceptual", new[] { "lunar-helium-3", "room-temperature-superconductors", "space-based-solar-power", "superhot-rock-geothermal" })]
    [InlineData("llms-and-agents", "upcoming", new[] { "agent-to-agent-protocols", "human-agent-teams" })]
    [InlineData("llms-and-agents", "conceptual", new[] { "agent-economies", "autonomous-research-agents", "continual-learning", "recursive-self-improvement", "verified-code-generation" })]
    [InlineData("coal-technologies", "upcoming", new[] { "coal-plant-repowering", "coal-to-hydrogen" })]
    [InlineData("coal-technologies", "conceptual", new[] { "coal-to-carbon-materials", "critical-minerals-from-coal-ash", "direct-carbon-fuel-cells" })]
    [InlineData("electric-vehicles", "upcoming", new[] { "electric-air-taxis", "robotaxis" })]
    [InlineData("electric-vehicles", "conceptual", new[] { "lithium-air-batteries", "structural-batteries", "wireless-charging-roads" })]
    [InlineData("internet-evolution", "upcoming", new[] { "6g", "agentic-web" })]
    [InlineData("internet-evolution", "conceptual", new[] { "interplanetary-internet", "quantum-internet" })]
    [InlineData("warfare-in-ukraine", "upcoming", new[] { "drone-walls", "ground-robots", "laser-air-defence" })]
    [InlineData("warfare-in-ukraine", "conceptual", new[] { "autonomous-drone-swarms", "human-machine-teams" })]
    public void TheUpcomingAndConceptualIdeas_AreTheOnesTheReadmeNames(string name, string tag, string[] expected)
    {
        var tagged = Model(name).Trends.Where(trend => trend.Tags.Contains(tag)).Select(trend => trend.Id).Order();

        Assert.Equal(expected, tagged);
    }

    [Theory]
    [InlineData("energy-breakthroughs", new[] { "cold-fusion", "fission-power", "inertial-confinement-fusion", "molten-salt-reactors", "room-temperature-superconductors", "small-modular-reactors", "solar-photovoltaics", "stellarator", "tokamak" })]
    [InlineData("llms-and-agents", new[] { "autonomous-agents", "chat-assistants", "large-language-models", "multi-agent-systems", "prompt-engineering", "scaling-laws", "tool-use", "vibe-coding" })]
    [InlineData("coal-technologies", new[] { "carbon-capture-and-storage", "steam-locomotive" })]
    [InlineData("electric-vehicles", new[] { "battery-electric-cars", "battery-swapping", "electric-air-taxis", "self-driving" })]
    [InlineData("internet-evolution", new[] { "dot-com-bubble", "web3" })]
    [InlineData("warfare-in-ukraine", new[] { "anti-tank-missiles", "fpv-drones", "rocket-artillery", "satellite-communications", "strike-drones" })]
    public void TheDraggedBoundaries_AreOnTheTrendsTheReadmeNames(string name, string[] expected)
    {
        var dragged = Model(name).Trends.Where(trend => trend.DraggedEnds.Any(end => end is not null)).Select(trend => trend.Id).Order();

        Assert.Equal(expected, dragged);
    }

    /// <summary>The llms-and-agents readme names one pair that influenced each other in both directions.</summary>
    [Fact]
    public void AutonomousAgentsAndToolUse_InfluenceEachOtherBothWays()
    {
        var influences = Model("llms-and-agents").Influences;

        Assert.Contains(influences, influence => influence is { From: "autonomous-agents", To: "tool-use" });
        Assert.Contains(influences, influence => influence is { From: "tool-use", To: "autonomous-agents" });
    }
}
