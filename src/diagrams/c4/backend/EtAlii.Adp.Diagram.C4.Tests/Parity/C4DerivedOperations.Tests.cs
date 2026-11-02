using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The edits the library writes say what the hand-written ones said (c4-disl-fbl task 15, Requirement 5): for every
/// document of the parity corpus, the five value sets on sampled elements and relationships, the four adds at the top
/// of the model and inside sampled parents, and the removal of sampled elements declared on one line give the same
/// refusal, or a body that reads as the same model, through today's handlers and through
/// <see cref="HandWrittenC4Edits"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model, not the bytes, is compared</b>, because Q1 rules that the bytes may differ: the one value is written
/// in its own style and a new element where the binding places it, indented like its sibling. What each edit changed
/// in the text is held to Requirement 5.2 and 5.4 instead: every line the edit did not touch is still there, in order.
/// </para>
/// <para>
/// <b>Three differences are the ones this specification names</b>, and are checked as such rather than skipped:
/// a value the hand-written edit appended inside a comment, which its model then does not hold (finding B19); a
/// refusal the line surgery gave for a line it could not cut, which the library answers with its own; and the
/// relationships the binding's <c>remove.cascade</c> takes with a removed element, which the line surgery left behind
/// naming nothing.
/// </para>
/// </remarks>
public class C4DerivedOperationsTests
{
    private const string Body = "parity.dsl";
    private const int Sample = 3;
    private const string Value = "Edited \"value\"";

    public static TheoryData<string> Documents() => [.. Edited().Select(document => document.Name)];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEdit_ReadsAsTheHandWrittenModel(string name)
    {
        var text = Edited().Single(document => document.Name == name).Text;
        var comparison = new Comparison(text);
        var model = C4Body.Read(text).Workspace;

        foreach (var element in Sampled(model.Elements))
        {
            await comparison.Set($"name of {element.Id}", new SetElementNameCommand(Body, element.Id, Value), (document, read) => HandWrittenC4Edits.SetName(document, read, element.Id, Value), element.Id, "name");
            await comparison.Set($"description of {element.Id}", new SetElementDescriptionCommand(Body, element.Id, Value), (document, read) => HandWrittenC4Edits.SetDescription(document, read, element.Id, Value), element.Id, "description");
            await comparison.Set($"technology of {element.Id}", new SetElementTechnologyCommand(Body, element.Id, Value), (document, read) => HandWrittenC4Edits.SetTechnology(document, read, element.Id, Value), element.Id, "technology");
        }

        foreach (var relationship in Sampled(model.Relationships))
        {
            await comparison.Set($"description of {relationship.Id}", new SetRelationshipDescriptionCommand(Body, relationship.Id, Value), (document, read) => HandWrittenC4Edits.SetRelationshipDescription(document, read, relationship.Id, Value), relationship.Id, "description");
            await comparison.Set($"technology of {relationship.Id}", new SetRelationshipTechnologyCommand(Body, relationship.Id, Value), (document, read) => HandWrittenC4Edits.SetRelationshipTechnology(document, read, relationship.Id, Value), relationship.Id, "technology");
        }

        string[] parents = ["", .. Sampled(model.Elements).Select(element => element.Id)];
        foreach (var kind in (C4ElementKind[])[C4ElementKind.Person, C4ElementKind.SoftwareSystem, C4ElementKind.Container, C4ElementKind.Component])
        {
            foreach (var parent in parents)
            {
                await comparison.Add(kind, "New \"thing\"", parent);
            }
        }

        foreach (var element in Sampled(model.Elements.Where(comparison.IsOneLine).ToList()))
        {
            await comparison.Remove(element.Id);
        }

        Assert.True(comparison.Mismatches.Count == 0, $"{name}: {comparison.Mismatches.Count} of {comparison.Compared} differ\n{string.Join("\n", comparison.Mismatches)}");
    }

    private static IEnumerable<T> Sampled<T>(IReadOnlyList<T> entries) =>
        entries.Where((_, index) => index < Sample || index == entries.Count - 1);

    private static IEnumerable<(string Name, string Text)> Edited() =>
        C4Corpus.DocumentsAsync(CancellationToken.None).GetAwaiter().GetResult()
            .Select(document => (document.Relative, Text: Encoding.UTF8.GetString(document.Bytes)))
            .DistinctBy(document => document.Text, StringComparer.Ordinal);

    /// <summary>One document's edits, run both ways, and what differed.</summary>
    private sealed class Comparison(string text)
    {
        public List<string> Mismatches { get; } = [];

        public int Compared { get; private set; }

        private readonly string[] _lines = LineDocument.Parse(text).Lines.Select(line => line.Text).ToArray();

        /// <summary>Whether <paramref name="element"/> is declared on a line of its own, which is all the hand-written removal removes.</summary>
        public bool IsOneLine(C4Element element) =>
            element.Line >= 1 && element.Line <= _lines.Length && !_lines[element.Line - 1].Split("//")[0].Split('#')[0].TrimEnd().EndsWith('{');

        /// <summary>Whether the line <paramref name="identifier"/> is declared on ends in a comment.</summary>
        private bool HasComment(string identifier) =>
            C4Body.Read(text).Workspace.Find(identifier) is { } element && new C4Line(_lines[element.Line - 1], element.Line).Code != _lines[element.Line - 1].Trim();

        public async Task Set<TCommand>(string edit, TCommand command, Func<LineDocument, C4Workspace, EditOutcome> handWritten, string target, string attribute)
            where TCommand : IDocumentEditCommand
        {
            (string? refusal, string? written) = await RunAsync(command);
            (string? expectedRefusal, string? expected) = HandWritten(handWritten);
            Compared++;
            if (Refusals(edit, expectedRefusal, refusal)) return;

            var after = C4Body.Read(written!).Workspace;
            if (!Holds(after, target, attribute))
            {
                Mismatches.Add($"{edit}: the library's body does not read the value as it was set");
                return;
            }

            // B19: the line surgery appended the value inside the comment ending the line, so its model kept the old one.
            var handWrittenModel = C4Body.Read(expected!).Workspace;
            var b19 = !Holds(handWrittenModel, target, attribute);
            Compare(edit, b19 ? Model(WithValueSet(C4Body.Read(text).Workspace, target, attribute)) : Model(handWrittenModel), Model(after));
            Untouched(edit, written!, 1);
        }

        public async Task Add(C4ElementKind kind, string name, string parent)
        {
            var edit = $"add {kind} in '{parent}'";
            (string? refusal, string? written) = await RunAsync(new AddC4ElementCommand(Body, kind, name, parent));
            // A person or a software system dropped on another element goes at the top of the model, as the definition's
            // add says; the hand-written add put it inside the element, which only a command naming a parent asked for,
            // since the drops and the menus name none for these two (C4ContextActionProvider).
            var topLevel = kind is C4ElementKind.Person or C4ElementKind.SoftwareSystem;
            (string? expectedRefusal, string? expected) = HandWritten((document, read) => HandWrittenC4Edits.Add(document, read, kind, name, topLevel && read.Find(parent) is not null ? "" : parent, ""));
            Compared++;
            // The line surgery looked for a line reading `model {` and found none where a block comment stands beside
            // it; the library reads the model block, and adds at its end.
            if (expectedRefusal == "This document has no model block to add an element to." && refusal is null && topLevel)
            {
                var before = C4Body.Read(text).Workspace;
                var read = C4Body.Read(written!).Workspace;
                if (read.Elements.Count(element => element.Name == name && element.ParentId is null) != 1 || read.Elements.Count != before.Elements.Count + 1)
                {
                    Mismatches.Add($"{edit}: the library's add does not read as one new element at the top of the model");
                }

                return;
            }

            if (Refusals(edit, expectedRefusal, refusal)) return;

            // B19 again: the brace a parent without a block gained was appended after the comment ending its line, so the
            // hand-written child landed outside it, and what followed inside the stray block. The library writes it
            // before the comment, so its model is the one before the edit with the new element inside the parent.
            var library = C4Body.Read(written!).Workspace;
            if (!topLevel && parent.Length > 0 && HasComment(parent) && library.Elements.SingleOrDefault(element => element.Name == name) is { } child && child.ParentId == parent)
            {
                var before = C4Body.Read(text).Workspace;
                Compare(edit, Sorted(Model(before with { Elements = [.. before.Elements, child] })), Sorted(Model(library)));
                Untouched(edit, written!, 1);
                return;
            }

            // A parent without a block gains one, which changes the parent's own line.
            Compare(edit, Model(C4Body.Read(expected!).Workspace), Model(library));
            Untouched(edit, written!, 1);
        }

        public async Task Remove(string identifier)
        {
            var edit = $"remove {identifier}";
            (string? refusal, string? written) = await RunAsync(new RemoveC4ElementCommand(Body, identifier));
            (string? expectedRefusal, string? expected) = HandWritten((document, read) => HandWrittenC4Edits.Remove(document, read, identifier, ""));
            Compared++;
            if (Refusals(edit, expectedRefusal, refusal)) return;

            // The binding's remove.cascade takes the relationships of the element with it.
            Compare(edit, Model(C4Body.Read(expected!).Workspace, identifier), Model(C4Body.Read(written!).Workspace, identifier));
        }

        /// <summary>Whether the two refused, recording a difference; true when there is nothing more to compare.</summary>
        private bool Refusals(string edit, string? expected, string? actual)
        {
            if (expected is null && actual is null) return false;

            // The line surgery's own refusals, for a line it could not cut, have no counterpart: the library either
            // writes the value or says why not. And where the surgery wrote a value into an argument that is not the
            // attribute's, as a description into an instance's deployment groups, the command now refuses it in C4's words
            // (the instance-name defect of task 14, found again for the description).
            var surgery = expected is not null && expected.StartsWith("Line ", StringComparison.Ordinal);
            var notHeld = expected is null && actual is not null && actual.EndsWith(" has no description in C4; it stands for what it is an instance of.", StringComparison.Ordinal);
            if (!string.Equals(expected, actual, StringComparison.Ordinal) && !surgery && !notHeld)
            {
                Mismatches.Add($"{edit}\n  hand-written {expected ?? "applied"}\n  library      {actual ?? "applied"}");
            }

            return true;
        }

        private void Compare(string edit, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Mismatches.Add($"{edit}\n{Difference(expected, actual)}");
            }
        }

        /// <summary>Every line of the document the edit did not change is still there, in order, with at most <paramref name="changed"/> of them changed.</summary>
        private void Untouched(string edit, string written, int changed)
        {
            var after = LineDocument.Parse(written).Lines.Select(line => line.Text).ToList();
            var kept = 0;
            var at = 0;
            foreach (var line in _lines)
            {
                var found = after.IndexOf(line, at);
                if (found < 0) continue;
                kept++;
                at = found + 1;
            }

            if (kept < _lines.Length - changed)
            {
                Mismatches.Add($"{edit}: {_lines.Length - kept} lines it did not need changed");
            }
        }

        private async Task<(string? Refusal, string? Written)> RunAsync<TCommand>(TCommand command)
            where TCommand : IDocumentEditCommand
        {
            var store = new Store(text);
            var services = new Dictionary<Type, Func<IC4DocumentStore, object>>
            {
                [typeof(SetElementNameCommand)] = documents => new SetElementNameCommandHandler(documents),
                [typeof(SetElementDescriptionCommand)] = documents => new SetElementDescriptionCommandHandler(documents),
                [typeof(SetElementTechnologyCommand)] = documents => new SetElementTechnologyCommandHandler(documents),
                [typeof(SetRelationshipDescriptionCommand)] = documents => new SetRelationshipDescriptionCommandHandler(documents),
                [typeof(SetRelationshipTechnologyCommand)] = documents => new SetRelationshipTechnologyCommandHandler(documents),
                [typeof(AddC4ElementCommand)] = documents => new AddC4ElementCommandHandler(documents),
                [typeof(RemoveC4ElementCommand)] = documents => new RemoveC4ElementCommandHandler(documents),
            };
            var handler = (ICommandHandler<TCommand>)services[typeof(TCommand)](store);
            var result = await handler.ExecuteAsync(command, TestContext.Current.CancellationToken);
            return result.IsSuccess ? (null, store.Saved ?? text) : (result.Error, null);
        }

        private (string? Refusal, string? Written) HandWritten(Func<LineDocument, C4Workspace, EditOutcome> edit)
        {
            var document = LineDocument.Parse(text);
            var outcome = edit(document, C4Body.Read(text).Workspace);
            return outcome.WasApplied ? (null, document.Text) : (outcome.Refusal, null);
        }
    }

    /// <summary>Whether <paramref name="workspace"/> reads <see cref="Value"/> as <paramref name="attribute"/> of the element or relationship <paramref name="target"/> named before the edit.</summary>
    private static bool Holds(C4Workspace workspace, string target, string attribute)
    {
        // An element written without an identifier is named after its name by the host, so a rename names it anew;
        // it is found again by its place in the model.
        if (!target.Contains('@', StringComparison.Ordinal) && workspace.Find(target) is null)
        {
            return workspace.Elements.Any(candidate => (attribute switch { "name" => candidate.Name, "description" => candidate.Description, _ => candidate.Technology }) == Value);
        }

        if (workspace.Find(target) is { } element)
        {
            return (attribute switch { "name" => element.Name, "description" => element.Description, _ => element.Technology }) == Value;
        }

        var at = target[..target.LastIndexOf('@')];
        return workspace.Relationships.Any(relationship => $"{relationship.SourceId}->{relationship.DestinationId}" == at
            && (attribute == "description" ? relationship.Description : relationship.Technology) == Value);
    }

    /// <summary><paramref name="workspace"/> with <see cref="Value"/> as <paramref name="attribute"/> of <paramref name="target"/>, as an edit outside a comment gives it.</summary>
    private static C4Workspace WithValueSet(C4Workspace workspace, string target, string attribute)
    {
        if (workspace.Find(target) is not null)
        {
            return workspace with
            {
                Elements = [.. workspace.Elements.Select(element => !string.Equals(element.Id, target, StringComparison.OrdinalIgnoreCase) ? element : WithValue(element, attribute))],
            };
        }

        return workspace with
        {
            Relationships = [.. workspace.Relationships.Select(relationship => relationship.Id != target ? relationship : WithValue(relationship, attribute))],
        };
    }

    private static C4Element WithValue(C4Element element, string attribute) => attribute switch
    {
        "name" => element with { Name = Value },
        "description" => element with { Description = Value },
        _ => element with { Technology = Value },
    };

    private static C4Relationship WithValue(C4Relationship relationship, string attribute) =>
        attribute == "description" ? relationship with { Description = Value } : relationship with { Technology = Value };

    /// <summary>What a body's model says, without where it says it: each element and relationship, and none touching <paramref name="removed"/>.</summary>
    private static string Model(C4Workspace workspace, string? removed = null) =>
        string.Join("\n", [
            .. workspace.Elements.Select(element => $"{element.Kind} {element.Id} | {element.Name} | {element.Description} | {element.Technology} | {element.ParentId} | {string.Join(",", element.Tags)}"),
            .. workspace.Relationships
                .Where(relationship => removed is null || (!string.Equals(relationship.SourceId, removed, StringComparison.OrdinalIgnoreCase) && !string.Equals(relationship.DestinationId, removed, StringComparison.OrdinalIgnoreCase)))
                .Select(relationship => $"{relationship.SourceId} -> {relationship.DestinationId} | {relationship.Description} | {relationship.Technology}"),
        ]);

    private static string Sorted(string model) => string.Join("\n", model.Split('\n').Order(StringComparer.Ordinal));

    private static string Difference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var index = 0; index < Math.Max(expectedLines.Length, actualLines.Length); index++)
        {
            var left = index < expectedLines.Length ? expectedLines[index] : "(end)";
            var right = index < actualLines.Length ? actualLines[index] : "(end)";
            if (left != right) return $"  first differs at {index + 1}\n  hand-written {left}\n  library      {right}";
        }

        return "  the same lines in another order";
    }

    /// <summary>A store holding one document that never changes; a save records the text it was given.</summary>
    private sealed class Store(string text) : IC4DocumentStore
    {
        /// <summary>The text of the last save, or null.</summary>
        public string? Saved { get; private set; }

        public event EventHandler<DocumentChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public ValueTask<DocumentEntry<LineDocument, C4Workspace>> GetOrLoadAsync(string path, CancellationToken cancellationToken) => ValueTask.FromResult(Get(path));

        public DocumentEntry<LineDocument, C4Workspace> Get(string path) => new(LineDocument.Parse(text), C4Body.Read(text).Workspace);

        public Task<DocumentSaveResult> SaveAsync(string path, LineDocument document, CancellationToken cancellationToken)
        {
            Saved = document.Text;
            return Task.FromResult(DocumentSaveResult.Ok);
        }

        public Task ReloadAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BodyDeletedAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;

        public void Forget(string path)
        {
        }

        public Task TouchAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
