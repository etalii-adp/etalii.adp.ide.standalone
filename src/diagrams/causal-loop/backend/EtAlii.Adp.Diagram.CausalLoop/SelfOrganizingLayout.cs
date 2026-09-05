namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Meyer's self-organizing graphs — competitive learning from Kohonen's self-organizing map,
/// applied to layout — with every source of randomness replaced by a function of the document
/// and the iteration index (causal-loop-diagram Requirement 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>The neighbourhood is measured in graph distance, not in layout distance.</b> That single
/// choice is what makes this a graph layout rather than a clustering of points: when a stimulus
/// pulls one variable, the variables pulled along with it are the ones the document links it to,
/// so the arrangement that emerges is the shape of the causal structure rather than the shape of
/// wherever the points happened to start.
/// </para>
/// <para>
/// <b>This repository has ruled against stochastic layout on three grounds, and the ruling is not
/// waived here.</b> The published method is stochastic in three places, and each is answered:
/// </para>
/// <list type="table">
/// <item>
/// <term>Random initial positions</term>
/// <description>A phyllotaxis spiral indexed by the variable's position in the document's own
/// order — a pure function of the document, and a spread that starts nothing on top of anything
/// else.</description>
/// </item>
/// <item>
/// <term>A random stimulus sequence</term>
/// <description>A Halton sequence over the layout rectangle, computed from the iteration index
/// alone. Low-discrepancy rather than pseudo-random: it covers the rectangle more evenly than
/// samples drawn at random would, which is the property the method actually wanted from
/// randomness.</description>
/// </item>
/// <item>
/// <term>Arbitrary tie-breaking for the best-matching unit</term>
/// <description>The document order, lowest index first.</description>
/// </item>
/// </list>
/// <para>
/// Plus a fixed iteration count and a schedule that reads only the iteration index. There is no
/// wall-clock anywhere in here, no random source, and no settling animation whose stopping point
/// depends on how fast the machine ran — which is what makes Requirement 6.3 achievable: the same
/// document laid out in two different processes gives identical positions, not merely similar
/// ones.
/// </para>
/// <para>
/// <b>What this pass does not do is guarantee non-overlap</b>, because a converged
/// self-organizing map does not. It decides the <i>arrangement</i> — related variables near,
/// unrelated apart. Separating boxes that still intersect is a second, bounded pass, and where
/// that pass cannot succeed the action is refused with the size at which it failed rather than
/// delivering a hairball quietly (Requirement 6.5 and 6.7).
/// </para>
/// </remarks>
public static class SelfOrganizingLayout
{
    /// <summary>
    /// The drawn-element budget this layout runs behind, so the "unbounded at scale" ground
    /// cannot arise (Requirement 6.4). The family constant, named here rather than reached for
    /// across a module boundary.
    /// </summary>
    public const int DefaultBudget = 1000;

    /// <summary>
    /// How many stimuli are presented. Fixed — not a convergence test, which would make the
    /// stopping point depend on the arithmetic's own history rather than on the document.
    /// </summary>
    public const int Iterations = 2000;

    /// <summary>The learning rate at the first iteration and at the last.</summary>
    private const double InitialRate = 0.9;
    private const double FinalRate = 0.01;

    /// <summary>
    /// The neighbourhood radius, in graph hops, at the first iteration and at the last. It ends
    /// below one hop so that late stimuli move the winner and effectively nothing else, which is
    /// what turns a coarse arrangement into a settled one.
    /// </summary>
    private const double InitialRadius = 3.0;
    private const double FinalRadius = 0.35;

    /// <summary>
    /// A hair more than the arithmetic needs, so that two boxes pushed to exactly touching are
    /// not read as overlapping again by the next round's floating-point comparison.
    /// </summary>
    private const double Nudge = 0.5;

    /// <summary>
    /// How many rounds the separation pass gets. Bounded rather than "until no overlaps remain":
    /// an unbounded loop on a document it cannot satisfy would spin instead of refusing, and
    /// Requirement 6.7 wants the refusal.
    /// </summary>
    public const int SeparationRounds = 200;

    /// <summary>
    /// The golden angle, in radians. Successive multiples of it never repeat a direction, which
    /// is what makes a phyllotaxis spiral spread points evenly without a random source.
    /// </summary>
    private const double GoldenAngle = 2.39996322972865332;

    /// <summary>
    /// Arranges the model's variables, or reports that it declined to try.
    /// </summary>
    /// <remarks>
    /// Declines above the budget rather than running slowly and delivering something unreadable:
    /// the memory ground is answered by not starting, and the caller is told the size so the
    /// refusal names a fact about the document rather than an internal limit.
    /// </remarks>
    /// <param name="model">The document to arrange.</param>
    /// <param name="metrics">How wide and tall a variable is drawn.</param>
    /// <param name="budget">The drawn-element budget above which the layout declines to start.</param>
    /// <param name="rounds">
    /// How many rounds the separation pass gets. A parameter so that the refusal path is
    /// reachable from a test rather than only from a document nobody has yet written: a refusal
    /// nothing can trigger is a refusal nobody has checked.
    /// </param>
    public static SelfOrganizingResult Compute(
        CausalLoopModel model,
        CausalLoopMetrics? metrics = null,
        int budget = DefaultBudget,
        int rounds = SeparationRounds)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentOutOfRangeException.ThrowIfLessThan(budget, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
        metrics ??= CausalLoopMetrics.Default;

        var variables = model.Variables;
        if (variables.Count > budget)
        {
            return SelfOrganizingResult.OverBudget(variables.Count, budget);
        }

        if (variables.Count == 0)
        {
            return SelfOrganizingResult.Arranged(new Dictionary<string, CausalLoopBox>(StringComparer.Ordinal));
        }

        var widths = new double[variables.Count];
        for (var index = 0; index < variables.Count; index++)
        {
            widths[index] = metrics.WidthOf(variables[index].Display);
        }

        var height = metrics.Height;
        var positions = InitialPositions(variables.Count, widths, height, metrics.Separation);

        // A single variable has nothing to organize itself against, and the spiral has already
        // put it at the origin. Presenting stimuli to it would only drag it toward whichever
        // corner the sequence favoured.
        if (variables.Count > 1)
        {
            Organize(model, variables, positions, widths, height, metrics.Separation);
        }

        // Phase two. A converged self-organizing map does not guarantee non-overlap and
        // Requirement 6.5 does, so the arrangement is separated rather than trusted.
        var remaining = Separate(positions, widths, height, metrics.Separation, rounds);
        if (remaining > 0)
        {
            return SelfOrganizingResult.CouldNotSeparate(variables.Count, remaining);
        }

        var boxes = new Dictionary<string, CausalLoopBox>(variables.Count, StringComparer.Ordinal);
        for (var index = 0; index < variables.Count; index++)
        {
            boxes[variables[index].Id] = new CausalLoopBox(
                positions[index].X - (widths[index] / 2),
                positions[index].Y - (height / 2),
                widths[index],
                height);
        }

        return SelfOrganizingResult.Arranged(boxes);
    }

    /// <summary>
    /// Pushes overlapping boxes apart in stable order, and reports how many pairs still overlap
    /// when the rounds are spent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The extent ratio is not the guard here, and this is the code that says so.</b> A
    /// hairball is roughly square: it scores near 1:1 and would pass a ratio bound comfortably
    /// while being exactly the unreadable arrangement the ruling names. What bites is whether any
    /// two boxes intersect, so that is what is measured and that is what refuses.
    /// </para>
    /// <para>
    /// Deterministic like the pass before it: pairs are visited in document order, each round
    /// reads the positions the previous round left, and the displacement is a function of the
    /// overlap alone. Nothing here consults a random source or a clock, so the arrangement stays
    /// reproducible across processes and the two-process test still covers this half.
    /// </para>
    /// </remarks>
    internal static int Separate(
        (double X, double Y)[] positions,
        double[] widths,
        double height,
        double separation,
        int rounds = SeparationRounds)
    {
        var overlaps = 0;

        for (var round = 0; round < rounds; round++)
        {
            overlaps = 0;

            for (var first = 0; first < positions.Length; first++)
            {
                for (var second = first + 1; second < positions.Length; second++)
                {
                    // The half-extents two boxes need between their centres to clear each other,
                    // plus the separation this module keeps between neighbours.
                    var neededX = ((widths[first] + widths[second]) / 2) + separation;
                    var neededY = height + separation;

                    var dx = positions[second].X - positions[first].X;
                    var dy = positions[second].Y - positions[first].Y;

                    var overlapX = neededX - Math.Abs(dx);
                    var overlapY = neededY - Math.Abs(dy);
                    if (overlapX <= 0 || overlapY <= 0)
                    {
                        continue;
                    }

                    overlaps++;

                    // Push along the axis that needs the least movement: separating two boxes
                    // that are nearly side by side vertically would undo the arrangement the
                    // first pass worked out.
                    if (overlapX / neededX < overlapY / neededY)
                    {
                        var push = (overlapX / 2) + Nudge;
                        var direction = dx < 0 ? -1 : 1;
                        positions[first] = (positions[first].X - (push * direction), positions[first].Y);
                        positions[second] = (positions[second].X + (push * direction), positions[second].Y);
                    }
                    else
                    {
                        var push = (overlapY / 2) + Nudge;
                        var direction = dy < 0 ? -1 : 1;
                        positions[first] = (positions[first].X, positions[first].Y - (push * direction));
                        positions[second] = (positions[second].X, positions[second].Y + (push * direction));
                    }
                }
            }

            if (overlaps == 0)
            {
                return 0;
            }
        }

        return overlaps;
    }

    /// <summary>
    /// Where each variable starts: a phyllotaxis spiral in the document's own order.
    /// </summary>
    /// <remarks>
    /// The published method starts from random positions. A spiral answers the same need — no two
    /// variables on top of each other, no direction favoured — while being a pure function of the
    /// document, and it starts the arrangement from an order the author can actually see, which a
    /// random scatter never does.
    /// </remarks>
    internal static (double X, double Y)[] InitialPositions(
        int count, double[] widths, double height, double separation)
    {
        // Scaled so the spiral's own spacing already accounts for how big the boxes are: a
        // diagram of long names starts wider than a diagram of short ones.
        var spacing = ((widths.Length == 0 ? 0 : widths.Max()) + height + (separation * 2)) / 2;
        var positions = new (double X, double Y)[count];

        for (var index = 0; index < count; index++)
        {
            var radius = spacing * Math.Sqrt(index);
            var angle = index * GoldenAngle;
            positions[index] = (radius * Math.Cos(angle), radius * Math.Sin(angle));
        }

        return positions;
    }

    /// <summary>Presents the stimulus sequence and moves the variables toward it.</summary>
    private static void Organize(
        CausalLoopModel model,
        IReadOnlyList<CausalLoopVariable> variables,
        (double X, double Y)[] positions,
        double[] widths,
        double height,
        double separation)
    {
        var hops = GraphDistances(model, variables);

        // The rectangle stimuli are drawn from, sized from the spiral the variables start on so
        // that the sequence covers where they actually are.
        var (extentMinimumX, extentMinimumY, extentMaximumX, extentMaximumY) = Extent(positions);
        var padding = ((widths.Max() + height) / 2) + separation;
        var minimumX = extentMinimumX - padding;
        var minimumY = extentMinimumY - padding;
        var spanX = Math.Max(extentMaximumX - extentMinimumX + (padding * 2), 1);
        var spanY = Math.Max(extentMaximumY - extentMinimumY + (padding * 2), 1);

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var progress = (double)iteration / Iterations;
            var rate = InitialRate * Math.Pow(FinalRate / InitialRate, progress);
            var radius = InitialRadius * Math.Pow(FinalRadius / InitialRadius, progress);
            var falloff = 2 * radius * radius;

            // The stimulus: the iteration's point in a two-dimensional Halton sequence, mapped
            // onto the rectangle. A function of the index and nothing else, so iteration 900 is
            // the same point in every process that ever runs this.
            var stimulusX = minimumX + (Halton(iteration + 1, 2) * spanX);
            var stimulusY = minimumY + (Halton(iteration + 1, 3) * spanY);

            var winner = BestMatchingUnit(positions, stimulusX, stimulusY);

            for (var index = 0; index < positions.Length; index++)
            {
                var distance = hops[winner][index];
                if (distance < 0)
                {
                    // Not connected to the winner at all. The published method would pull it in
                    // anyway at whatever the layout distance implied; here it is left alone,
                    // because a variable in another component has no causal relationship to the
                    // one being pulled and should not be dragged toward it.
                    continue;
                }

                var influence = rate * Math.Exp(-(distance * distance) / falloff);
                positions[index] = (
                    positions[index].X + (influence * (stimulusX - positions[index].X)),
                    positions[index].Y + (influence * (stimulusY - positions[index].Y)));
            }
        }
    }

    /// <summary>
    /// The variable nearest the stimulus, ties broken by document order.
    /// </summary>
    /// <remarks>
    /// The tie-break is the third source of randomness in the published method and the easiest
    /// one to leave in by accident: two variables at the same distance would otherwise be
    /// separated by whatever order the collection happened to enumerate in. Strictly-less-than
    /// keeps the earlier index, which is the document's own order.
    /// </remarks>
    private static int BestMatchingUnit((double X, double Y)[] positions, double x, double y)
    {
        var winner = 0;
        var best = double.MaxValue;

        for (var index = 0; index < positions.Length; index++)
        {
            var dx = positions[index].X - x;
            var dy = positions[index].Y - y;
            var distance = (dx * dx) + (dy * dy);

            if (distance < best)
            {
                best = distance;
                winner = index;
            }
        }

        return winner;
    }

    /// <summary>
    /// Hop counts between every pair of variables, over the links read as undirected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Undirected on purpose. Causality has a direction and the drawing shows it, but two
    /// variables joined by an arrow belong near each other regardless of which way it points —
    /// and a causal loop diagram read directionally would leave a variable that only ever causes
    /// and never responds at graph distance infinity from everything downstream of it.
    /// </para>
    /// <para>
    /// <c>-1</c> means unreachable, which is a different thing from far away and is treated as
    /// one: a variable in another component is not pulled at all.
    /// </para>
    /// </remarks>
    internal static int[][] GraphDistances(CausalLoopModel model, IReadOnlyList<CausalLoopVariable> variables)
    {
        var index = new Dictionary<string, int>(variables.Count, StringComparer.Ordinal);
        for (var i = 0; i < variables.Count; i++)
        {
            index[variables[i].Id] = i;
        }

        var adjacency = new List<int>[variables.Count];
        for (var i = 0; i < variables.Count; i++)
        {
            adjacency[i] = [];
        }

        foreach (var link in model.Links)
        {
            // A link to something the document never declared is a finding the validator makes;
            // here it simply joins nothing.
            if (!index.TryGetValue(link.From, out var from) || !index.TryGetValue(link.To, out var to) || from == to)
            {
                continue;
            }

            adjacency[from].Add(to);
            adjacency[to].Add(from);
        }

        var distances = new int[variables.Count][];
        for (var source = 0; source < variables.Count; source++)
        {
            distances[source] = BreadthFirst(adjacency, source);
        }

        return distances;
    }

    private static int[] BreadthFirst(List<int>[] adjacency, int source)
    {
        var distances = new int[adjacency.Length];
        Array.Fill(distances, -1);
        distances[source] = 0;

        var queue = new Queue<int>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbour in adjacency[current])
            {
                if (distances[neighbour] < 0)
                {
                    distances[neighbour] = distances[current] + 1;
                    queue.Enqueue(neighbour);
                }
            }
        }

        return distances;
    }

    /// <summary>
    /// The <paramref name="index"/>th value of the radical-inverse (Halton) sequence in
    /// <paramref name="radix"/>, which lies in <c>[0, 1)</c>.
    /// </summary>
    /// <remarks>
    /// This is what replaces the random stimulus source. It is not an approximation of
    /// randomness: a low-discrepancy sequence fills the interval more evenly than random samples
    /// do, so the stimuli cover the layout rectangle better than the method's own randomness
    /// would have — and every value is a function of its index, which is the whole point.
    /// </remarks>
    internal static double Halton(int index, int radix)
    {
        var result = 0.0;
        var fraction = 1.0 / radix;

        while (index > 0)
        {
            result += fraction * (index % radix);
            index /= radix;
            fraction /= radix;
        }

        return result;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Extent((double X, double Y)[] positions)
    {
        var minimumX = double.MaxValue;
        var minimumY = double.MaxValue;
        var maximumX = double.MinValue;
        var maximumY = double.MinValue;

        foreach (var (x, y) in positions)
        {
            minimumX = Math.Min(minimumX, x);
            minimumY = Math.Min(minimumY, y);
            maximumX = Math.Max(maximumX, x);
            maximumY = Math.Max(maximumY, y);
        }

        return (minimumX, minimumY, maximumX, maximumY);
    }
}
