namespace EtAlii.Adp.Layout;

/// <summary>
/// Pushes boxes that overlap apart, for the layouts whose first pass places boxes by a rule that
/// does not promise to keep them clear of each other (code-structure FR-045).
/// </summary>
public static class OverlapResolver
{
    /// <summary>
    /// Pushes overlapping boxes apart in stable order, and reports how many pairs still overlap
    /// when the rounds are spent.
    /// </summary>
    /// <remarks>
    /// Deterministic: pairs are visited in the order given, each round reads the centres the
    /// previous round left, and a push is a function of the overlap alone. A pair is pushed along
    /// the axis that needs the least movement relative to what it needs, so two boxes nearly side
    /// by side are not moved apart the long way, which would undo the arrangement they came from.
    /// </remarks>
    /// <param name="centres">Each box's centre, moved in place.</param>
    /// <param name="sizes">Each box's width and height, in the order of <paramref name="centres"/>.</param>
    /// <param name="separation">The clear space two boxes keep between them.</param>
    /// <param name="rounds">The most passes over every pair before giving up.</param>
    /// <param name="nudge">What each box of a pair is pushed beyond half the overlap, so a pair just cleared does not touch.</param>
    /// <returns>How many pairs overlapped in the last round; zero when every box is clear.</returns>
    public static int Separate(
        (double X, double Y)[] centres,
        IReadOnlyList<(double Width, double Height)> sizes,
        double separation,
        int rounds,
        double nudge)
    {
        ArgumentNullException.ThrowIfNull(centres);
        ArgumentNullException.ThrowIfNull(sizes);

        var overlaps = 0;
        for (var round = 0; round < rounds; round++)
        {
            overlaps = 0;
            for (var first = 0; first < centres.Length; first++)
            {
                for (var second = first + 1; second < centres.Length; second++)
                {
                    // The half-extents two boxes need between their centres to clear each other,
                    // plus the separation kept between neighbours.
                    var neededX = ((sizes[first].Width + sizes[second].Width) / 2) + separation;
                    var neededY = ((sizes[first].Height + sizes[second].Height) / 2) + separation;

                    var dx = centres[second].X - centres[first].X;
                    var dy = centres[second].Y - centres[first].Y;

                    var overlapX = neededX - Math.Abs(dx);
                    var overlapY = neededY - Math.Abs(dy);
                    if (overlapX <= 0 || overlapY <= 0)
                    {
                        continue;
                    }

                    overlaps++;
                    if (overlapX / neededX < overlapY / neededY)
                    {
                        var push = (overlapX / 2) + nudge;
                        var direction = dx < 0 ? -1 : 1;
                        centres[first] = (centres[first].X - (push * direction), centres[first].Y);
                        centres[second] = (centres[second].X + (push * direction), centres[second].Y);
                    }
                    else
                    {
                        var push = (overlapY / 2) + nudge;
                        var direction = dy < 0 ? -1 : 1;
                        centres[first] = (centres[first].X, centres[first].Y - (push * direction));
                        centres[second] = (centres[second].X, centres[second].Y + (push * direction));
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
}
