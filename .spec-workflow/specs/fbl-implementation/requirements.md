# Requirements Document

## Introduction

The user's request, verbatim:

> Create a specification for a FBL implementation in the etalii.adp.ide.standalone repo called EtAlii.Adp.Specification.Fbl. Also call a test project.
> Use the existing example FDL definitions and test them on the real test files available in the repository.  Do not alter the already existing code.

Three readings are taken, and each is stated so that it can be overruled rather than discovered:

- **"FDL" is FBL**, the Format Binding Language. There is no FDL in ADP; the example definitions the request means are the eight `.fbl` documents beside the FBL specification.
- **"Also call a test project" is "also create a test project"**, named `EtAlii.Adp.Specification.Fbl.Tests` by the solution's `<Project>.Tests` convention (`docs/solution-structure.md`, *Where tests and fixtures live*).
- **"Do not alter the already existing code" is a hard boundary on the implementation, not a wish.** The library is built beside everything else and wired into nothing. Exactly which existing files may still change, and why each one must, is Requirement 1.4.

**What FBL is, in one paragraph.** FBL 0.1 (Working Draft, `etalii-adp/etalii.adp`, `specifications/fbl/FBL-specification.md`, merged in etalii.adp #43 on 2026-10-01 as `4590c56`) declares how a file another tool owns maps to a tool type's model in both directions: which files a binding claims, how a body is read into elements and relations while tolerating what it cannot read, how every change is written back as **named, minimal splices** that leave every other byte as it was, how undo is exact and refused on drift, and where the view data a user places is kept (an `.adp` registration beside the body, never in it). It serves every kind of tool; today the diagrams use it. Its eight example bindings cover all five format families: `timeline.fbl` (yaml), `databricks-job.fbl` (yaml), `databricks-pipeline.fbl` (json, also yaml), `mindmap.fbl` (xml), `causal-loop-diagram.fbl` (lines), `structurizr.fbl` (blocks), and two bindings whose reader is a persistence plugin, `helm-chart.fbl` (a folder subject) and `w3c-turtle.fbl`.

**What this specification delivers** is a .NET library, `EtAlii.Adp.Specification.Fbl`, that implements FBL 0.1 as a declared host in the sense of FBL §15.1 for all five families, minus the concerns that only a running host has (file watching, sharing one open body between readings, DISL), and a test project that proves it in two ways: against **the conformance fixtures FBL ships**, which say byte for byte what every host must write, and against **the real files already in this repository** (the timeline, causal loop, mind map, C4, Databricks, Helm and RDF fixtures and examples, and every `.adp` registration under `src/`), which say whether the example bindings fit the files ADP actually reads.

**The second proof is the one the user asked for, and it can fail in a way the first cannot.** The fixtures were written together with the bindings; the real files were not. A real file the binding reads differently from the module that owns it today is a finding about the binding or about the file, and Requirement 12 says it is recorded and reported rather than tuned away. **One is already visible from the registrations:** `src/examples/diagrams/databricks/lakehouse/databricks.pipeline.adp` points the `databricks/pipeline` origin at a bundle file (`body: databricks.yml`, `resource: bronze_to_gold`), while `databricks-pipeline.fbl` binds a bare pipeline settings file and declares no resource capture. Whether that is a gap in the binding or a registration the binding never meant to serve is the kind of question this work exists to surface.

## Alignment with Product Vision

The product direction (2026-09-26) is that every tool is a markup definition interpreted by a core in each IDE, with code only where a definition cannot reach. FBL is that direction applied to persistence: before it, every diagram whose model lives in another tool's file needed a hand-written parser and writer in each of the four hosts, and this repository has one per module (`TimelineParser`, `CausalLoopParser`, `C4Parser`, `MindmapDocument`, `JobParser`, `PipelineParser`). **A library that reads and writes those files from the bindings alone is the first time FBL runs anywhere**, and running it against this repository's corpus is the cheapest available evidence that the bindings are right before any host depends on them.

Standalone is one host among four and no longer the reference (2026-09-26). That is why the library depends on no other project of this solution (Requirement 1.3): what it proves about FBL must not be borrowed from this host's own document primitives.

## Requirements

### Requirement 1: Two new projects, and nothing existing altered

**User Story:** As the owner of this repository, I want FBL implemented beside the existing code rather than inside it, so that the experiment cannot break a diagram that works today.

#### Acceptance Criteria

1. The library SHALL be a new project `src/backend/EtAlii.Adp.Specification.Fbl/EtAlii.Adp.Specification.Fbl.csproj` with root namespace `EtAlii.Adp.Specification.Fbl`, and the tests SHALL be a new project `src/backend/EtAlii.Adp.Specification.Fbl.Tests/EtAlii.Adp.Specification.Fbl.Tests.csproj` with root namespace `EtAlii.Adp.Specification.Fbl.Tests`, an xUnit v3 executable shaped like the existing core test projects.
2. Both projects SHALL be listed in `src/backend/EtAlii.Adp.slnx`, so that `dotnet test --solution EtAlii.Adp.slnx` builds and runs them with everything else.
3. The library SHALL reference no project of the solution, and no existing project SHALL reference the library. Its only package dependency SHALL be one the solution already pins in `src/Directory.Packages.props` (YamlDotNet); a new package needs a change to that file, which Requirement 1.4 does not allow, and therefore a card.
4. WHEN the implementation's pull request is compared with `develop` THEN the only existing files it changes SHALL be: `src/backend/EtAlii.Adp.slnx` (the two project entries); `docs/solution-structure.md` and, if a sentence of it becomes false, `docs/architecture.md` (the counts and the core project list that `ArchitecturePages.Tests` recomputes from the solution, which go red without the update); and `.gitattributes` (Requirement 10.3). **Any other existing file in that diff is a defect**, whatever it fixes.
5. The test project MAY reference existing module projects for reading only, through their public API (Requirement 11.6). It SHALL NOT need any change to them, so a parser that is `internal` today (`JobParser`, `PipelineParser`) is not used, and `InternalsVisibleTo` is not added.
6. All four gates (`npm test`, `npm run typecheck`, `dotnet format style --verify-no-changes --severity info`, `dotnet test`) SHALL exit zero on the implementation branch, judged by captured exit codes.

### Requirement 2: Loading an FBL document

**User Story:** As a tool engineer, I want an FBL document checked when it is loaded, so that a binding that cannot work is refused with a reason before any body is read through it.

#### Acceptance Criteria

1. WHEN an FBL document is loaded THEN the library SHALL parse it as UTF-8 JSON, reject a duplicate key anywhere in it, and refuse a document whose `fbl` major version is not 0; a newer minor version SHALL load with a warning (FBL §2.1, §2.7).
2. The library SHALL resolve names as FBL §14.1 step 4 says: rule names unique within a binding, and every rule named in `parent`, `cascade`, `reference.to`, `within` and `files` existing.
3. The library SHALL compile every regular expression of a binding and reject one outside the common subset of FBL §2.5 (backreferences, lookaround, atomic groups, possessive quantifiers, inline flags, Unicode property classes), naming the construct.
4. The library SHALL compile every CEL expression of a binding against the variables FBL §2.4 makes available where it appears, and report one that does not compile, or that uses a CEL construct the library does not evaluate, naming the construct.
5. The library SHALL apply the checks of FBL §14.1 step 6 that need no DISL specification: a `shared` claim has a `marker` or is `registrationOnly`; at most one reading is `bare`; a declared binding has a file `family` and at least one rule; a plugin binding has no rules; every writable attribute has exactly one slot (FBL §3.3).
6. Every problem SHALL be reported with the JSON Pointer of its location, a severity and a message (FBL §14.1), and loading SHALL return every problem found rather than stop at the first.
7. A binding SHALL be retrievable by a binding reference `<document>#<name>` resolved against the referring document's location, or `#<name>` within the same document (FBL §2.3).
8. WHEN each of the eight vendored example documents is loaded THEN it SHALL load with no problem of severity error.

### Requirement 3: Lossless readings of the five families

**User Story:** As a host implementer, I want every byte of a body accounted for, so that a splice can replace exactly the bytes it names and nothing else.

#### Acceptance Criteria

1. The library SHALL read a body as bytes: UTF-8 with or without a byte-order mark, the mark kept and belonging to no node, all offsets UTF-8 byte offsets including it; a body that is not valid UTF-8 SHALL be unreadable (FBL §2.6, §7.5).
2. The library SHALL split lines and line endings as FBL §2.6 defines them (CRLF, LF, lone CR), and report line and column (code points) 1-based.
3. For each of `yaml`, `json`, `xml`, `lines` and `blocks` the library SHALL build the lossless reading FBL §4 defines, in which every byte of the body belongs to exactly one node or to the trivia one node owns, with each entry's own span and line span, each value's span, and leading comments owned as §4.1.1 says.
4. **yaml** SHALL follow FBL §4.3: only the first document bound; indentation by key or `-` column; scalar spans as written (quotes kept, plain scalars without trailing spaces or comment, block scalars from the indicator to the end of the last content line); a flow collection one value; aliases and merges readable and their slots read-only; a syntax error making the body unreadable, and a well-formed but unexpected shape making only unreadable entries.
5. **json** SHALL follow FBL §4.4: member and item spans, separators, string spans including quotes, a comment making the body unreadable, and a duplicate member name reported as `fbl.duplicate-key` with only the first bound.
6. **xml** SHALL follow FBL §4.5: prolog, comments, processing instructions and a document type declaration kept as unbound content; carriage returns kept rather than normalised; element, attribute and text spans; attribute order kept; an entity reference other than the five predefined ones and character references making the containing element unreadable; and `html-paragraphs` text read and written as that section says.
7. **lines** SHALL follow FBL §4.6: blank lines, comments by the binding's `comment`, statements, the first matching rule winning, named groups as values, and groups split into words with quoted words kept whole.
8. **blocks** SHALL follow FBL §4.7: a statement ending in `{` outside double-quoted strings opening a block that the matching `}` line closes, braces in strings not counted, unbalanced braces making the body unreadable, block rules with `within` and `^`, and a block rule's `view` group naming a view.
9. The reading SHALL keep unbound content (unknown keys, attributes, elements, statements, comments, directives) byte for byte, and present entries to CEL as FBL §4.1.4 says.

### Requirement 4: Rules, slots and the model

**User Story:** As a host implementer, I want a body turned into elements, relations and findings exactly as its binding declares, so that every host builds the same model from the same file.

#### Acceptance Criteria

1. The library SHALL select entries with selectors as FBL §4.2 defines them (keys and names, `*`, `**`, `{capture}`, `name[@A='v']`, absolute and relative), and with `line` expressions for `lines` and `blocks`, honouring `within`, `opens` and `caseInsensitive`.
2. WHEN several rules match one entry THEN the first in binding order, elements before relations, whose `when` holds SHALL take it, and an entry SHALL become at most one element or relation (FBL §5.1).
3. The library SHALL read every slot kind of FBL §5.2 (`key` with `child`, `attribute`, `text`, `group` with `word` and `flag`, `parent`, `capture`, `value`) and apply the attribute options that affect reading: `default`, `map`, `override`, `content` and `readOnly`.
4. The library SHALL take an element's id from its `id.from` slot, from the registration's `identities` block for `id.sidecar` (FBL §8.6), and, for a rule without `id`, SHALL address the element by its rule and its place in the body. Computing a DISL-derived id is out of scope (see *Out of scope*), and the place-based address SHALL be marked as not a stored id.
5. The library SHALL build containment from `parent: {rules, slot}` as the nearest enclosing entry matched by one of those rules (FBL §5.5), and relations from their `source` and `target` slots, reporting an end that names nothing as `fbl.dangling-reference` and creating no relation for it (FBL §5.4).
6. The library SHALL check the binding's `header` and report a missing or different mark as `fbl.header-mismatch`, or make the body unreadable when the header is `required` (FBL §5.6).
7. Reading SHALL NOT fail on content: an entry a rule matches but cannot read SHALL be reported as `std.unreadableEntry` at its source location with its bytes kept, a statement no rule matches SHALL be reported as `fbl.unbound-statement` when `unmatched` is `report`, and an unreadable body SHALL open as an empty, read-only model with exactly one `std.unparseable` finding (FBL §7.4, §7.5).
8. Every finding SHALL carry a code, a severity, and a source location with the file, and the line, column and length of the entry's own span (FBL §7.4).

### Requirement 5: Writing by splices

**User Story:** As a user editing a file another tool owns, I want only the bytes my change concerns to be written, so that the other tool and my own hand edits never see collateral damage.

#### Acceptance Criteria

1. The library SHALL plan a model change (add, set, remove or move an element or relation, and a save without change) as an edit made only of the eleven splice operations of FBL §6.1, each splice named by its operation, with offsets referring to the body before the edit, no two splices overlapping, and splices at one offset applied in the order listed (FBL §6.4, §6.5).
2. The library SHALL plan inserts and removals as FBL §6.2 says: every `insert.place` value, `container` and `create` (with `ensure-container`), `keys`, `emit` with optional segments and flag words, `skeleton` and `insert.when`; `cascade`, bottom-up removal, the line span or own span rule, json separators, and `remove-when-empty` (with `remove-container`).
3. The library SHALL write new text by the rules of FBL §6.3, exactly: line endings (the insertion line's, else the dominant one with CRLF winning a tie, else `text.newline`), indentation (previous sibling, next sibling, one step deeper, the step measured from the body), yaml scalar styles and the plain-safe test, json values and separators, numbers (`shortest`, `{decimals}`), times (`keep-precision`), xml attributes and elements, and `lines`/`blocks` emits, preferring `insert-key` over `re-emit-line` whenever both are possible.
4. The library SHALL apply the attribute options that affect writing: `empty`, `absent`, `override` (removed when the attribute is written), `create` for xml children, `style`, `number`, `time`, and `map` (first key in document order, or the value already there when it maps to the same model value).
5. WHEN a referenced value is renamed THEN every reference to it SHALL be rewritten in the same edit, one `rewrite-reference` splice each, word by word or item by item, and a rename to a value another entry of the same rules already has SHALL be refused (FBL §5.7).
6. IF any change in an edit cannot be planned (no `insert`, no `remove`, a read-only binding, rule or slot, `empty: "refuse"`, an `absent` refusal, a duplicate on rename, `insert.when` false) THEN the whole edit SHALL be refused with that reason and nothing written (FBL §6.4, §3.4).
7. A save without an edit SHALL produce no splice and the bytes that were read (FBL §1.4 principle 2).
8. Planning SHALL be deterministic: the same body, binding and change SHALL give the same splices and bytes on every run and platform (FBL §6.5).
9. The library SHALL save a body atomically, through a temporary file in the same folder moved into place, keeping the file's permissions, and SHALL NOT save an unreadable or read-only body (FBL §6.6).

### Requirement 6: History and drift

**User Story:** As a user, I want undo to put back exactly the bytes an edit replaced, and to be refused rather than guess when the file changed underneath it.

#### Acceptance Criteria

1. Each open body SHALL keep one history of edits; an edit SHALL record its splices, the bytes each replaced, and a digest of the whole body after it (FBL §7.1).
2. Undo SHALL apply the inverse splices of the most recent edit and redo its splices again; a new edit SHALL clear the redo stack.
3. A rule or edit with `undo: "snapshot"` SHALL record the whole body before the edit, and its undo SHALL replace the whole body with that snapshot as one splice under the operation of the edit's first splice; the bytes SHALL be the same as an inverse-splice undo would give.
4. WHEN an undo or redo is requested and the body's current bytes differ from those the history expects THEN it SHALL be refused with the reason "The file has changed since this edit, so it cannot be undone." and nothing SHALL be written (FBL §7.2).

### Requirement 7: The registration

**User Story:** As a user who places elements by hand, I want my positions kept in the `.adp` beside the file rather than in it, and the `.adp` itself edited as carefully as the body.

#### Acceptance Criteria

1. The library SHALL read a registration in the line form of FBL §8.1: the origin on line 1 after an optional byte-order mark, headers as `key: value` split at the first `: `, blank lines skipped, the header region ending at the first non-header line, `layout:` and `identities:` blocks whose entry key is everything before the last `: `, and anything after the blocks kept as unbound content.
2. A header that neither FBL (`body`, `view`, `resource`) nor the binding's `registration.headers` declares SHALL be kept and reported as `fbl.unknown-header`.
3. The library SHALL find the body as FBL §8.2 says (the `body` header relative to the registration's folder, else the sibling with the registration's base name and the first existing claimed extension, else the folder for a folder binding), refuse a `body` that resolves outside the workspace root it was given, and open a missing body empty with `fbl.missing-body`.
4. `view` SHALL select a view of a `blocks` body by its block rule's `view` group, ignoring case, and `resource` SHALL bind the capture named by `registration.resource.capture`, the first such entry in document order being used without the header (FBL §8.2, §9.3).
5. The library SHALL write layout changes by splices as FBL §8.3 says: entries in ordinal order of their ids, numbers in `{decimals: 3}` form, the registration's own line ending, `layout:` created after the headers when missing and removed with its last entry, and a moved element's numbers replaced in place.
6. A layout entry whose id is not in the model SHALL be reported as `fbl.stale-view-data`, applied to no element, and removed at the next write of the registration as part of that edit (FBL §8.5).
7. The library SHALL read and write `identities:` as FBL §8.6 says, the second of two elements with one natural key reported as `std.duplicateId` and given an id that is not stored.
8. The library SHALL read `registration.legacyLayout` and `registration.legacyIdentities` sidecars when the registration has no matching block, write them back by json splices while they exist, match view keys ignoring case, and never create either file (FBL §8.7).

### Requirement 8: Routing and templates

**User Story:** As a host implementer, I want to ask which binding a file belongs to, and to create a new body from a template, with one answer in every host.

#### Acceptance Criteria

1. The library SHALL evaluate markers on a body's bytes without reading it through a binding: `{rootKey, value?}`, `{firstLine}` after a byte-order mark, and `{pattern, lines?}` over the first `lines` lines, default 20 (FBL §12.2).
2. Given a file name, its bytes and a set of bindings, the library SHALL return the routing candidates of FBL §12.3: bindings whose `names` or `extensions` (ignoring case) match, excluding `registrationOnly` bindings and including a `shared` binding only when its marker matches; and it SHALL never choose between several candidates on the caller's behalf.
3. The library SHALL report, for a body, the readings a binding offers in the order of FBL §9.4 (those whose `suggest` matches the first 64 KiB first) and which one a bare file opens as.
4. The library SHALL produce a new body from a binding's template as FBL §13 says: `template.byOrigin[<origin>]` before `template.text`, the placeholders `{name}`, `{base}`, `{key}` (with its exact sanitising rule) and `{newid:<rule>}` replaced and nothing else, and SHALL NOT overwrite an existing file.
5. WHEN each vendored binding's template is produced and read back through its own binding THEN it SHALL read with no finding of severity warning or above (FBL §13).

### Requirement 9: Plugin readers and folder subjects

**User Story:** As a host implementer, I want the parts of a plugin-read or folder-read binding that FBL declares to work without the plugin, so that routing, registration and templates behave the same for every binding.

#### Acceptance Criteria

1. The library SHALL define the persistence plugin contract of FBL §11.2 (`read`, `plan`, `template`, `watch`) as data exchanged through an interface, and SHALL do for a plugin-read binding everything FBL §11.3 gives the host that this library covers: applying the plugin's splices, history, drift, saving, the registration, routing and templates.
2. The library SHALL recognise a folder subject by `recognise` (`all`, `any`, `none`) and select its files by the file rules' globs and `ignore`, in ordinal order of relative path, without following symbolic links (FBL §10.1, §10.2).
3. WHEN a body needs a plugin that the caller did not supply THEN the library SHALL open it read-only with DISL's `std.pluginMissing` finding and a reason, rather than fail (FBL §15.1).
4. No persistence plugin SHALL be implemented by this specification (see *Out of scope*).

### Requirement 10: The conformance fixtures

**User Story:** As the owner of the FBL specification, I want this library held to the fixtures every host must pass, so that it writes the bytes any other conforming host would.

#### Acceptance Criteria

1. The test project SHALL vendor, unchanged, the eight example `.fbl` documents, `fixtures/` and `registrations/` from `etalii-adp/etalii.adp` `specifications/fbl/`, with a readme naming the source commit (`4590c56` at the time of writing, or the commit actually copied) and the repository's `LICENSE` copied beside them verbatim as `LICENSE.md`.
2. The vendored files SHALL NOT be edited in this repository; a difference from the source SHALL be a re-vendoring at a newer commit, recorded in the readme.
3. The fixture inputs SHALL be kept from line-ending conversion, by adding to `.gitattributes`, scoped to the vendored folder and by extension, the extensions the global rules there do not already exempt (`.yml`, `.json`, `.cld`, `.adp`), with the reason written beside the lines, as the Databricks fixtures' entries already are.
4. For every vendored fixture, reading its input SHALL give what its `read` lists (elements by `id` and `type`, findings by `rule` and `line`, `unreadable`), and every step SHALL produce exactly its `splices` and its document after the step, or be refused with its reason and write nothing (FBL §15.3).
5. Every vendored registration SHALL parse, and SHALL write back unchanged when nothing is edited.

### Requirement 11: The real files of this repository

**User Story:** As the user who asked for this, I want the example bindings tried on the files ADP actually reads today, so that I learn where the bindings and the files disagree before any host depends on them.

#### Acceptance Criteria

1. The tests SHALL find the real files by enumerating `src/` at run time (excluding `node_modules`, `bin`, `obj` and the test project's own vendored folder), not from a hard-coded list, so a file added later is covered without a change; and each binding's enumeration SHALL assert a minimum count, so that a broken enumeration fails instead of passing on zero files. At the time of writing that is: `.tml` 15, `.cld` 4, `.mm` 5, `.dsl` 16, Databricks job YAML (a file with `task_key`) 4, pipeline JSON 2, `Chart.yaml` 13, `.ttl` and `.nt` 33, `.adp` 181.
2. WHEN each real file is read through the vendored binding that claims it THEN reading SHALL NOT throw, and the body SHALL NOT be unreadable unless the file is listed as expected-unreadable with a reason (a fixture written to be broken, such as the Databricks `broken.yml`).
3. WHEN each readable real file is saved without an edit THEN there SHALL be no splice and the bytes SHALL be identical.
4. WHEN, for each readable real file, the first writable attribute of the first element is set to a new value and the edit undone THEN every byte outside the edit's splices SHALL be unchanged after the edit, and the body SHALL be byte-identical to the original after the undo; and the same SHALL hold for removing the first removable element.
5. WHEN a real file is changed after an edit and before its undo THEN the undo SHALL be refused for drift (Requirement 6.4).
6. For the four bindings whose module has a public parser today (timeline `TimelineParser`, causal loop `CausalLoopParser`, C4 `C4Parser`, mind map `MindmapDocument`), the set of element ids per type that the binding reads from each real file SHALL equal the set the module's own parser reads, mapped from the module's types to the binding's types by a table in the test project; any difference SHALL be a listed divergence (Requirement 12).
7. Every `.adp` registration under `src/` SHALL parse in the line form without throwing and write back unchanged when nothing is edited; for each whose origin a vendored declared binding claims, the body SHALL resolve, open, and have its `view` or `resource` header (if any) select something, and each layout entry SHALL either name an element of the reading or be reported as stale.
8. For the two plugin bindings, every chart folder holding a `Chart.yaml` SHALL be recognised by `helm-chart.fbl`'s `recognise`, and every `.ttl`/`.nt` file SHALL route to `w3c-turtle.fbl`; for each registration whose origin is `w3c/owl`, `w3c/shacl` or `w3c/skos`, that reading's `suggest` SHALL match its body, or the mismatch SHALL be a listed divergence.
9. The C4 examples' `*.layout.json` sidecars SHALL be read as `structurizr.fbl`'s legacy layout (Requirement 7.8) for the registrations beside them, with the positions of the registration's view applied.

### Requirement 12: Divergences are recorded, not hidden

**User Story:** As the owner of the FBL specification, I want every place where an example binding does not fit a real file reported back to me, so that the binding or the specification can be fixed where it is written.

#### Acceptance Criteria

1. A real-file check that fails because the binding and the file disagree SHALL NOT be made to pass by editing a vendored binding, by skipping the file, or by weakening the check. It SHALL be listed in one divergence file in the test project, one entry per binding, file and kind, each with the observed difference and a reason.
2. The tests SHALL fail when a listed divergence no longer occurs, so that the list cannot outlive what it describes.
3. The implementation's delivery report SHALL list every divergence, each shaped as a candidate follow-up for `etalii-adp/etalii.adp` (the binding or section concerned, the evidence, and what would resolve it).

## Non-Functional Requirements

### Code Architecture and Modularity

- **One concern per folder and file.** The library separates loading, the five lossless readers, the expression languages, rule evaluation, planning, history, the registration, routing and templates (the design names the folders).
- **No dependency on a running host.** The library takes bytes, paths and a workspace root and returns models, findings, splices and bytes; it opens no socket, starts no watcher, keeps no global state and writes no file except through the atomic save of Requirement 5.9.
- **The house rules apply as to any backend project:** `src/.editorconfig`, no blocking call in an `async` method (CA1849), `TestContext.Current.CancellationToken` in tests (xUnit1051), no unneeded `using` (IDE0005), CRLF in the working tree.
- **Vocabulary.** Names say *tool* for what serves every kind and *diagram* only where a diagram is meant, as ADP terminology defines them. FBL's own terms (binding, body, reading, registration, splice, edit) are used as FBL defines them.

### Performance

- Reading and saving without change the largest real file in the corpus SHALL take under one second on the CI runner. The whole real-file suite SHALL finish within the time the existing backend suites take today, so it does not become the slowest part of `dotnet test`.

### Security

- Regular expressions SHALL run with a match timeout, an expression that exceeds it being a finding on the statement rather than a hang (FBL §16).
- The library SHALL enforce a configurable limit on body size and on the number of entries built, reporting a body over the limit as unreadable rather than reading part of it (FBL §16).
- Paths SHALL be resolved within the workspace root the caller gives; a registration's `body` or a folder's file outside it, or reached through a symbolic link, SHALL NOT be read or written (FBL §8.2, §10.2, §16).
- Templates SHALL be text, their placeholders replaced literally and never evaluated (FBL §16).

### Reliability

- The library SHALL never write a body it could not read, never write bytes outside an edit's splices, write atomically, and refuse an undo on drift (FBL §16, *Integrity of other tools' files*).
- Every test written for a defect SHALL be seen to fail against that defect before it is trusted (CLAUDE.md). For this specification that means each real-file property of Requirement 11 is shown to fail once against a deliberately broken planner or reader, and the failure recorded.

### Usability

- Every refusal and finding message SHALL be a sentence a user can act on, using the wording FBL gives where it gives one.

## Out of scope

Each item is out because it belongs to a running host, to DISL, or to another specification, not because it is unimportant:

- **Wiring the library into anything**: the backend, a module, the client, or replacing any module's parser or writer. Requirement 1 forbids it, and doing it is a later specification per module.
- **File watching, external changes and folder settling** (FBL §7.3, §10.3): a host's watcher reports changes; the library offers re-reading, which is all a watcher needs.
- **Sharing one open body between several readings** (FBL §9.1, §9.2): a host's registry of open bodies.
- **DISL**: node types and their attributes, derived and ephemeral ids, constraints, `fixed` attributes, `std.ephemeralViewData`, and checking that a binding's `type` names a type of a specification (FBL §14.1 step 4, last clause). The library treats types and attributes as the binding names them.
- **JSON Schema validation of FBL documents, registrations and fixtures** (FBL §14.1 step 3): `etalii-adp/etalii.adp` already validates every vendored file against `fbl.schema.json` in CI, and doing it here needs a package the solution does not pin.
- **Implementing a persistence plugin** (Turtle, Helm or any other).
