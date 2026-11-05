using EtAlii.Lark;
using EtAlii.Lark.Parsers;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// Copies of a parser that feeding the parser leaves as they are. <c>EtAlii.Lark</c>'s tree builder extends the children
/// of a left-recursive repetition on the value stack in place, as Lark's does, so a shallow copy shares them with the
/// parser it was made from: a token the parser reduces for before refusing it adds a part to the copy too, and a unit
/// accepted from that copy holds the part twice. Each tree on the copy's value stack is copied once, its positions with it, so no list is shared;
/// the trees below them are never extended again, and a deep copy at every token would cost the size of the unit read so far.
/// </summary>
internal static class ParserSnapshot
{
    /// <summary>A copy of <paramref name="parser"/> that no later feeding of it changes.</summary>
    public static InteractiveParser Of(InteractiveParser parser)
    {
        var copy = parser.Copy(false);
        Detach(copy.ParserState.ValueStack);
        return copy;
    }

    /// <summary>A copy of <paramref name="state"/> that no later feeding of the parser it is the state of changes.</summary>
    public static ParserState Of(ParserState state)
    {
        var copy = state.Copy(false);
        Detach(copy.ValueStack);
        return copy;
    }

    private static void Detach(List<object?> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is Tree tree) values[i] = new Tree(tree.Data, [.. tree.Children], tree.Meta);
        }
    }
}
