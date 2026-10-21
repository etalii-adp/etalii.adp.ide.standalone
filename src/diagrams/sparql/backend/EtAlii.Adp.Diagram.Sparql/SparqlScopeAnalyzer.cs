namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The pass that runs once the grammar has read a query: it gives every group scope its stable
/// path, and indexes every variable - where patterns use it, whether it is projected, the
/// expression that defines it and the scopes it appears in. <c>SELECT *</c> is resolved here, to
/// every variable in the order the query first mentions it, because only the finished tree knows
/// them all.
/// </summary>
internal sealed class SparqlScopeAnalyzer
{
    private readonly Dictionary<string, UsageBuilder> _usages = new(StringComparer.Ordinal);
    private int _seenCounter;

    private SparqlScopeAnalyzer()
    {
    }

    /// <summary><paramref name="model"/> with its scope paths assigned, its projection resolved and its variables indexed.</summary>
    public static SparqlQueryModel Finish(SparqlQueryModel model) => new SparqlScopeAnalyzer().Analyze(model);

    private SparqlQueryModel Analyze(SparqlQueryModel model)
    {
        model.Where.Path = "where";
        AssignPaths(model.Where);
        if (model.Template is not null)
        {
            model.Template.Path = "template";
            AssignPaths(model.Template);
        }

        IndexScope(model.Where);
        if (model.Template is not null)
        {
            IndexScope(model.Template);
        }

        var projection = model.Projection;
        if (model is { ProjectsAll: true, Form: SparqlQueryForm.Select })
        {
            projection = _usages.Keys.OrderBy(name => _usages[name].FirstSeen).Select(name => new SparqlProjectionItem(name, "")).ToList();
        }

        foreach (var item in projection)
        {
            var builder = GetOrAdd(item.Name);
            builder.Projected = true;
            if (item.ExpressionText.Length > 0)
            {
                builder.DefiningExpression = item.ExpressionText;
            }
        }

        return model with
        {
            Projection = projection,
            Variables = _usages.ToDictionary(
                pair => pair.Key,
                pair => new SparqlVariableUsage(
                    pair.Key,
                    pair.Value.PatternOccurrences,
                    pair.Value.Projected,
                    pair.Value.DefiningExpression,
                    pair.Value.ScopePaths)),
        };
    }

    private static void AssignPaths(GroupScope scope)
    {
        foreach (var child in scope.Children)
        {
            child.Path = $"{scope.Path}/{child.Kind.ToString().ToLowerInvariant()}.{child.Ordinal}";
            AssignPaths(child);
        }
    }

    private sealed class UsageBuilder
    {
        public int PatternOccurrences;
        public bool Projected;
        public string DefiningExpression = "";
        public List<string> ScopePaths { get; } = [];
        public int FirstSeen;
    }

    private UsageBuilder GetOrAdd(string name)
    {
        if (!_usages.TryGetValue(name, out var builder))
        {
            builder = new UsageBuilder { FirstSeen = _seenCounter++ };
            _usages[name] = builder;
        }

        return builder;
    }

    private void IndexScope(GroupScope scope)
    {
        foreach (var pattern in scope.Patterns)
        {
            foreach (var term in (SparqlTerm[])[pattern.Subject, pattern.Predicate, pattern.Object])
            {
                if (term is VariableTerm variable)
                {
                    Touch(scope, variable.Name, patternPosition: true);
                }
            }
        }

        foreach (var constraint in scope.Constraints)
        {
            foreach (var name in constraint.ReferencedVariables)
            {
                Touch(scope, name, patternPosition: false);
            }

            if (constraint.DefinedVariable.Length > 0)
            {
                var builder = GetOrAdd(constraint.DefinedVariable);
                builder.DefiningExpression = constraint.Text;
                Touch(scope, constraint.DefinedVariable, patternPosition: false);
            }
        }

        foreach (var subSelect in scope.SubSelects)
        {
            foreach (var name in subSelect.ProjectedNames)
            {
                Touch(scope, name, patternPosition: false);
            }
        }

        foreach (var child in scope.Children)
        {
            IndexScope(child);
        }
    }

    /// <summary>Records that <paramref name="name"/> appears in <paramref name="scope"/>, and counts it when a pattern uses it.</summary>
    private void Touch(GroupScope scope, string name, bool patternPosition)
    {
        var builder = GetOrAdd(name);
        if (patternPosition)
        {
            builder.PatternOccurrences++;
        }

        if (!builder.ScopePaths.Contains(scope.Path))
        {
            builder.ScopePaths.Add(scope.Path);
        }
    }
}
