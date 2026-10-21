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

    /// <summary>
    /// Pushes each mover off every box it overlaps, along the one axis given for it and by the whole
    /// of the overlap plus the separation, leaving the box it was pushed off where it is - the rule of
    /// a layout whose movers each came from one direction and must not walk back along the other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C4's rule, kept as it was when it moved here (code-structure T239), beside <see cref="Separate"/>
    /// rather than in place of it: each layout passes the rule it already has, so joining them moves
    /// no picture (behaviour change B-03's precedent). A mover goes the shorter way off its blocker;
    /// where both ways are equally short the pair is symmetric, and answering it the same way for both
    /// is how two boxes start moving in lockstep, so the one with the lower <paramref name="tieRank"/>
    /// goes the lower way and the other the higher.
    /// </para>
    /// <para>
    /// Deterministic: movers are pushed in the order given, each against every box in index order,
    /// each push reads the boxes as the pushes before it left them, and a new coordinate is rounded to
    /// <paramref name="decimals"/> places.
    /// </para>
    /// </remarks>
    /// <param name="boxes">The boxes, moved in place.</param>
    /// <param name="movers">Which boxes move, in the order they are pushed, and whether each moves along X (true) or along Y.</param>
    /// <param name="tieRank">Each box's rank for breaking a tie, distinct per box.</param>
    /// <param name="separation">The clear space a pushed box keeps from the box it was pushed off.</param>
    /// <param name="decimals">The places a new coordinate is rounded to.</param>
    /// <returns>Whether a round moved nothing before the rounds - one per box - were spent.</returns>
    public static bool PushOffAlongAxes(
        Rect[] boxes,
        IReadOnlyList<(int Index, bool AlongX)> movers,
        IReadOnlyList<int> tieRank,
        double separation,
        int decimals)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(movers);
        ArgumentNullException.ThrowIfNull(tieRank);

        foreach (var _ in boxes)
        {
            var moved = false;
            foreach ((int index, bool alongX) in movers)
            {
                for (var other = 0; other < boxes.Length; other++)
                {
                    if (other == index || !boxes[index].Overlaps(boxes[other]))
                    {
                        continue;
                    }

                    var box = boxes[index];
                    var blocker = boxes[other];
                    var goesFirst = tieRank[index] < tieRank[other];
                    if (alongX)
                    {
                        var toLeft = box.Right - blocker.X + separation;
                        var toRight = blocker.Right - box.X + separation;
                        boxes[index] = toLeft < toRight || (Math.Abs(toLeft - toRight) < Tolerance && goesFirst)
                            ? box with { X = Math.Round(box.X - toLeft, decimals) }
                            : box with { X = Math.Round(box.X + toRight, decimals) };
                    }
                    else
                    {
                        var up = box.Bottom - blocker.Y + separation;
                        var down = blocker.Bottom - box.Y + separation;
                        boxes[index] = up < down || (Math.Abs(up - down) < Tolerance && goesFirst)
                            ? box with { Y = Math.Round(box.Y - up, decimals) }
                            : box with { Y = Math.Round(box.Y + down, decimals) };
                    }

                    moved = true;
                }
            }

            if (!moved)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How close two distances are to count as one: C4's tolerance, as it was.</summary>
    private const double Tolerance = 0.000001f;
}
