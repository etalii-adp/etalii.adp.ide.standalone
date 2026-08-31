# Requirements Document

## Introduction

This spec adds `ShortGuid`, a reusable value type that represents a `System.Guid` as a compact, fixed-length base36 string instead of the standard 32-hex-digit representation. It is intended for contexts where GUID-based identifiers need to appear in a shorter, still-unambiguous form (e.g. in the UI, file names, or logs). This is the first reusable helper placed in the `EtAlii.Adp` library project described in [structure.md](../../../steering/structure.md); that project (and its `EtAlii.Adp.Tests` counterpart) does not exist on disk yet and is created as part of this spec.

## Alignment with Product Vision

- [structure.md](../../../steering/structure.md)'s **`EtAlii.Adp` (Library)** definition: "Contains all reusable helper classes, mechanisms and extension methods" — `ShortGuid` is exactly this kind of helper, and gives the project its first content.
- [product.md](../../../steering/product.md)'s **"Don't reinvent, integrate"**: `ShortGuid` wraps `System.Guid` rather than introducing a competing identifier concept — it is purely an alternate, round-trippable string representation of the same value.
- [tech.md](../../../steering/tech.md)'s **testing guidance**: tests are plain unit tests runnable locally with no external dependencies, consistent with the "F5 experience" requirement.

## Requirements

### Requirement 1 — Compact, round-trippable string representation

**User Story:** As a developer, I want a `ShortGuid` type that represents a `Guid` as a base36 string, so that I can display, store, or transmit GUID-based identifiers in a more compact form than the standard 32-hex-digit representation, without losing any information.

#### Acceptance Criteria

1. WHEN a `ShortGuid` is created from a `Guid` THEN the system SHALL be able to produce a base36-encoded string representation (using digits `0-9` and letters `a-z`).
2. WHEN a `ShortGuid`'s string representation is parsed back THEN the system SHALL reconstruct the exact original `Guid` value (full round-trip, no precision loss).
3. WHEN encoding a `Guid` THEN the resulting string SHALL always be the same fixed length, zero-padded as needed, so every `Guid` produces a predictably-sized identifier and lexicographic string ordering is well-defined.
4. IF `Guid.Empty` is encoded THEN the system SHALL represent it as the fixed-length string of all `'0'` characters, not an empty string.

### Requirement 2 — An interface that feels like a natural counterpart to `Guid`

**User Story:** As a developer already familiar with `System.Guid`, I want `ShortGuid`'s API to mirror `Guid`'s own conventions, so that I can adopt it without learning a new set of idioms.

#### Acceptance Criteria

1. `ShortGuid` SHALL be an immutable `readonly struct`, consistent with `Guid`'s own design, and SHALL store only the wrapped `Guid` value — it SHALL NOT cache its string representation as extra struct state.
2. `ShortGuid` SHALL implement `IEquatable<ShortGuid>` and `IComparable<ShortGuid>`, and SHALL override `Equals`, `GetHashCode`, and `ToString()`, consistent with `Guid`'s own contract.
3. `ShortGuid` SHALL provide conversions to/from `Guid` (a constructor accepting a `Guid`, an explicit or implicit conversion operator, and a `Guid` property) so callers can move between the two representations without ceremony.
4. `ShortGuid` SHALL provide static members mirroring `Guid`'s: `NewShortGuid()` (equivalent to `Guid.NewGuid()`), `Empty` (equivalent to `Guid.Empty`), `Parse`, and `TryParse`.
5. `ShortGuid` SHALL implement `IParsable<ShortGuid>` and `ISpanParsable<ShortGuid>`, consistent with the parsing pattern `Guid` itself follows on .NET 8.
6. WHEN two `ShortGuid` values are compared (via `CompareTo`, `<`, `>`, or sorting) THEN the resulting order SHALL be identical to the lexicographic order of their base36 string representations, so sorting a collection of `ShortGuid` values is equivalent to sorting their displayed strings.

### Requirement 3 — Strict parsing and validation

**User Story:** As a developer, I want `ShortGuid.Parse`/`TryParse` to validate input strictly, so that a malformed identifier is caught immediately rather than silently producing the wrong `Guid`.

#### Acceptance Criteria

1. WHEN parsing a string of the correct fixed length containing only valid base36 characters (case-insensitive) THEN parsing SHALL succeed and produce the correct `Guid`.
2. WHEN parsing a string of any other length THEN `Parse` SHALL throw `FormatException` and `TryParse` SHALL return `false` (with `default` as the out value).
3. WHEN parsing a string containing a character outside `0-9`, `a-z`, `A-Z` THEN `Parse` SHALL throw `FormatException` and `TryParse` SHALL return `false`.
4. WHEN parsing a string whose numeric value would exceed the maximum value representable by a `Guid` (128 bits) THEN `Parse` SHALL throw `FormatException` and `TryParse` SHALL return `false`, rather than silently overflowing or wrapping.
5. Parsing SHALL be case-insensitive, and `ToString()` SHALL always emit a single, consistent case, so that values parsed from differently-cased input are equal after normalization.

### Requirement 4 — Allocation-conscious conversions

**User Story:** As a developer working with identifier-heavy data (e.g. many linked diagram elements), I want `ShortGuid` conversions to avoid unnecessary allocations, so that identifier handling doesn't become a performance bottleneck.

#### Acceptance Criteria

1. Encoding a `Guid` to its base36 string SHALL use fixed-width 128-bit integer arithmetic (e.g. `UInt128`) rather than arbitrary-precision arithmetic (e.g. `System.Numerics.BigInteger`) or intermediate collections.
2. `ShortGuid` SHALL support formatting directly into a caller-supplied buffer (a `TryFormat(Span<char>, ...)` implementation of `ISpanFormattable`), in addition to the convenience `ToString()`.
3. Parsing SHALL accept a `ReadOnlySpan<char>` (via `ISpanParsable<ShortGuid>`) without first requiring the caller to materialize a `string`.

## Non-Functional Requirements

### Code Architecture and Modularity

- **Single Responsibility**: `ShortGuid` and its base36 encode/decode logic are the only content this spec adds; no unrelated helpers are introduced under the same change.
- **Project placement**: the type SHALL live in the `EtAlii.Adp` class library project (created by this spec if absent, per [structure.md](../../../steering/structure.md)), not in `EtAlii.Adp.Backend` or any other project.
- **No forced wiring**: this spec SHALL NOT require any existing project to start depending on `EtAlii.Adp` or to adopt `ShortGuid` — it only establishes the type and its home project for future use.

### Performance

- Encoding and decoding SHALL run in constant time relative to the fixed 128-bit input size (no allocation growth with input "size", since the input is always exactly one `Guid`).
- Per Requirement 4, encode/decode SHALL avoid heap allocation on the hot path where a caller supplies its own buffer or span.

### Reliability

- Every `Guid` value, including `Guid.Empty` and a `Guid` whose bytes are all `0xFF`, SHALL round-trip through `ShortGuid` encoding and decoding without loss.

### Usability

- `ShortGuid` SHALL be usable as a drop-in, self-contained value type: constructing one from a `Guid` and reading `.ToString()` SHALL be sufficient for the common case, with no required configuration.

### Testing

- `EtAlii.Adp.Tests` (created by this spec) SHALL contain unit tests covering: round-trip encode/decode for representative and edge-case `Guid` values, fixed-length/padding behavior, case-insensitive parsing, all `Parse`/`TryParse` failure modes from Requirement 3, equality/comparison/ordering semantics from Requirement 2.6, and the conversions from Requirement 2.3/2.4.
