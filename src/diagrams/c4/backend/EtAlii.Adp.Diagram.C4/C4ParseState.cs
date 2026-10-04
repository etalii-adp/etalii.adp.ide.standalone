
namespace EtAlii.Adp.Diagram.C4;

/// <summary>The parser's running state: the scope stack, the element chain, and what has been read.</summary>
internal sealed class C4ParseState
{
    private readonly Stack<(C4ParseScope Scope, string? ElementId)> _stack = new();
    private int _generated;

    public C4ParseState() => _stack.Push((C4ParseScope.Root, null));

    public string WorkspaceName { get; set; } = "";

    public string? Environment { get; set; }

    public List<C4Element> Elements { get; } = [];

    public List<C4Relationship> Relationships { get; } = [];

    public List<C4View> Views { get; } = [];

    public List<C4ElementStyle> Styles { get; } = [];

    public List<string> Includes { get; } = [];

    public int? CurrentViewIndex { get; set; }

    public C4ParseScope Current => _stack.Peek().Scope;

    /// <summary>The innermost element block, which is what a nested declaration belongs to.</summary>
    public string? CurrentElementId => _stack.FirstOrDefault(frame => frame.ElementId is not null).ElementId;

    /// <summary>Adds tags to an element already read, for a `tags` line inside its block.</summary>
    public void AddTags(string id, IEnumerable<string> tags)
    {
        var at = Elements.FindIndex(element => string.Equals(element.Id, id, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            return;
        }

        Elements[at] = Elements[at] with { Tags = [.. Elements[at].Tags, .. tags] };
    }

    public void Push(C4ParseScope scope) => _stack.Push((scope, null));

    public void PushElement(string id) => _stack.Push((C4ParseScope.Element, id));

    public void Pop()
    {
        if (_stack.Count > 1)
        {
            (C4ParseScope scope, _) = _stack.Pop();
            if (scope is C4ParseScope.View)
            {
                CurrentViewIndex = null;
            }
        }
    }

    /// <summary>
    /// An id for an element the document declared without one. The DSL allows that; the
    /// wire does not, because the canvas needs something stable to select and edit.
    /// </summary>
    public string GenerateId(C4ElementKind kind, string name)
    {
        _generated++;
        var slug = new string(name.Where(char.IsLetterOrDigit).ToArray());
        return $"{kind.ToString().ToLowerInvariant()}_{(slug.Length > 0 ? slug : "unnamed")}_{_generated}";
    }
}
