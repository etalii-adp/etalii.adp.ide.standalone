using EtAlii.Adp.Backend.Diagrams;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// Turns a causal loop model, its boxes and its computed loop polarities into the core element
/// vocabulary - without extending that vocabulary by a field.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three element kinds, and only one of them has a position.</b> A variable is placed by the
/// layout. A link is drawn between its endpoints. A loop label is drawn at the centroid of the
/// variables it runs through. The two without positions of their own are therefore filtered
/// <i>structurally</i> - a link when both of its endpoints survived, a loop label when its
/// members did - and never by testing coordinates they do not carry (Requirement 7.3).
/// </para>
/// <para>
/// <b>An unplaced variable is not drawn, and is not drawn at the origin either.</b> The layout
/// returns a dictionary and this asks it with <c>TryGetValue</c>; a variable it has no box for
/// is left out of the drawn set deliberately, which is the choice Requirement 7.2 asks to be
/// visible in code rather than emergent from <c>default</c>.
/// </para>
/// </remarks>
public sealed class CausalLoopElementMapper
{
    /// <summary>The mime-style element kinds this type puts on the wire.</summary>
    public const string VariableType = "systems/causal-loop+variable";

    /// <summary>A causal link, drawn between two variables.</summary>
    public const string LinkType = "systems/causal-loop+link";

    /// <summary>A loop label, drawn among the variables it runs through.</summary>
    public const string LoopType = "systems/causal-loop+loop";

    private static readonly string _variableTypeUrl =
        $"type.googleapis.com/{Wire.CausalLoopVariablePayload.Descriptor.FullName}";

    private static readonly string _linkTypeUrl =
        $"type.googleapis.com/{Wire.CausalLoopLinkPayload.Descriptor.FullName}";

    private static readonly string _loopTypeUrl =
        $"type.googleapis.com/{Wire.CausalLoopLoopPayload.Descriptor.FullName}";

    /// <summary>The element id a variable is addressed by.</summary>
    public static string ElementIdOf(CausalLoopVariable variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        return $"variable:{variable.Id}";
    }

    /// <summary>Everything the viewport admits, mapped to core elements.</summary>
    public IReadOnlyList<DiagramElement> Visible(
        CausalLoopModel model,
        IReadOnlyDictionary<string, CausalLoopBox> boxes,
        DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(boxes);

        var elements = new List<DiagramElement>();

        // A variable is drawn when the layout placed it and the viewport touches it. Asked with
        // TryGetValue rather than GetValueOrDefault, so "not placed" cannot become "at (0, 0)".
        var drawn = new Dictionary<string, CausalLoopBox>(StringComparer.Ordinal);
        foreach (var variable in model.Variables)
        {
            if (!boxes.TryGetValue(variable.Id, out var box) || !Intersects(box, viewport))
            {
                continue;
            }

            drawn[variable.Id] = box;
            elements.Add(VariableElement(variable, box));
        }

        // A link rides on its endpoints: both must be drawn, or there is nothing to draw it
        // between. It has no position to test, and asking one of it would be inventing one.
        foreach (var link in model.Links.Where(link => drawn.ContainsKey(link.From) && drawn.ContainsKey(link.To)))
        {
            elements.Add(LinkElement(link, drawn[link.From], drawn[link.To]));
        }

        // A loop label rides on its members, and is placed at their centroid. Every member must
        // be drawn: a label placed among a subset would sit somewhere the loop is not.
        foreach (var loop in model.Loops.Where(loop => loop.Variables.Count > 0 && loop.Variables.All(drawn.ContainsKey)))
        {
            elements.Add(LoopElement(model, loop, [.. loop.Variables.Select(id => drawn[id])]));
        }

        return elements;
    }

    /// <summary>Everything the model states, with no viewport applied.</summary>
    /// <remarks>
    /// The unbounded case of <see cref="Visible"/>, delegated to it rather than written twice.
    /// An infinite viewport touches every box, so every placed variable is drawn and with it
    /// every link and loop whose members are. Keeping the two as one body is what stops them
    /// drifting once only the filtered one is exercised.
    /// </remarks>
    public IReadOnlyList<DiagramElement> Elements(
        CausalLoopModel model, IReadOnlyDictionary<string, CausalLoopBox> boxes) =>
        Visible(model, boxes, DiagramViewport.Unbounded);

    private static DiagramElement VariableElement(CausalLoopVariable variable, CausalLoopBox box)
    {
        var payload = new Wire.CausalLoopVariablePayload
        {
            Display = variable.Display,
            Id = variable.Id,
            Width = box.Width,
            Height = box.Height,
        };

        return new DiagramElement(
            ElementIdOf(variable), box.CenterX, box.CenterY, VariableType, _variableTypeUrl, payload.ToByteArray());
    }

    private static DiagramElement LinkElement(CausalLoopLink link, CausalLoopBox from, CausalLoopBox to)
    {
        var payload = new Wire.CausalLoopLinkPayload
        {
            FromElementId = $"variable:{link.From}",
            ToElementId = $"variable:{link.To}",
            Polarity = link.Polarity switch
            {
                CausalLoopPolarity.Positive => Wire.CausalLoopPolarityProto.Positive,
                CausalLoopPolarity.Negative => Wire.CausalLoopPolarityProto.Negative,
                _ => Wire.CausalLoopPolarityProto.Unstated,
            },
            Delayed = link.Delayed,
            Weight = link.Weight ?? 0,
            HasWeight = link.Weight.HasValue,
            Label = link.Label,
        };

        // Anchored at the midpoint of the two boxes. That is where the polarity mark and any
        // delay strokes are drawn; the line itself is computed by the canvas from the endpoints.
        return new DiagramElement(
            link.Id,
            (from.CenterX + to.CenterX) / 2,
            (from.CenterY + to.CenterY) / 2,
            LinkType,
            _linkTypeUrl,
            payload.ToByteArray());
    }

    private static DiagramElement LoopElement(
        CausalLoopModel model, CausalLoopLoop loop, IReadOnlyList<CausalLoopBox> members)
    {
        var computed = LoopPolarity.Of(model, loop.Variables);
        var payload = new Wire.CausalLoopLoopPayload
        {
            Identifier = loop.Identifier,
            Name = loop.Name,
            Computed = computed switch
            {
                LoopPolarityResult.Reinforcing => Wire.LoopPolarityProto.Reinforcing,
                LoopPolarityResult.Balancing => Wire.LoopPolarityProto.Balancing,
                _ => Wire.LoopPolarityProto.Undecidable,
            },

            // The one fact this diagram type knows that a drawing tool does not. Undecidable is
            // not a disagreement: nothing is claimed to be wrong with the label.
            Disagrees = computed != LoopPolarityResult.Undecidable
                && loop.ClaimsReinforcing is { } claimed
                && claimed != (computed == LoopPolarityResult.Reinforcing),
        };
        payload.MemberElementIds.AddRange(loop.Variables.Select(id => $"variable:{id}"));

        return new DiagramElement(
            loop.Id,
            members.Average(box => box.CenterX),
            members.Average(box => box.CenterY),
            LoopType,
            _loopTypeUrl,
            payload.ToByteArray());
    }

    private static bool Intersects(CausalLoopBox box, DiagramViewport viewport) =>
        box.Right >= viewport.MinX && box.X <= viewport.MaxX
        && box.Bottom >= viewport.MinY && box.Y <= viewport.MaxY;
}
