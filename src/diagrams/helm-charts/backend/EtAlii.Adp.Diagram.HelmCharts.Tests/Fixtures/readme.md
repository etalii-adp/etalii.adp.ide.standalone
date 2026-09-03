# Fixtures

Three chart trees the module tests read from disk exactly as the backend will:

* `well-formed/` - a v2 chart exercising the whole inventory: two dependencies (one
  aliased with a condition, one plain), the redis one vendored unpacked, a values stack
  with an override layer, schema, lock, crds, every template role, and a foreign file the
  reader must ignore.
* `broken/` - Chart.yaml parses but has no version; values.yaml does not parse; a
  template's kind is templated and therefore unknown by design.
* `unconventional/` - apiVersion v1 (legacy: requirements.yaml/requirements.lock), a
  not-SemVer version, `type: library`, a sealed `.tgz`, and a vendored chart with deeper
  nesting that is summarized by count, never recursed.

`.gitattributes` here says `* -text`: these bytes are test subjects (the zero-writes proof
compares them against themselves), so git must never rewrite their line endings - the same
reasoning the root `.gitattributes` records for the sibling modules' fixtures.
