/**
 * Where a schema reference points: the rules of json-editor Requirement 5.2, 5.3 and 5.9, once
 * for every editor (Requirement 9.5). A pure function with no I/O that knows no format: it is
 * given a reference as a document holds it - a `$schema`, a `$ref`, an `xsi:schemaLocation` - and
 * answers which file of the project, which bundled schema, or why there is none.
 *
 * Held to `src/fixtures/schema-reference-cases.json`, the list of cases the XML editor's C# twin
 * is held to as well, so the two cannot drift.
 *
 * <b>The raw reference is decided before any URL arithmetic.</b> `new URL("../../x", base)` clamps
 * at the root and answers a file - a different file from the one the document names. Here a
 * `..` that would step above the project root is refused with a reason, never clamped.
 */
export type SchemaTarget =
  | { kind: "project"; path: string[] }
  | { kind: "bundled"; id: string }
  | { kind: "none"; reason: string };

/** A URI scheme, as RFC 3986 spells one: a letter, then letters, digits, `+`, `-` or `.`, then `:`. */
const scheme = /^[A-Za-z][A-Za-z0-9+.-]*:/;

/**
 * @param reference The reference as the document holds it.
 * @param documentPath The document's path in the project, its file name last.
 * @param bundled The addresses ADP ships a schema for.
 */
export function resolveSchemaReference(reference: string, documentPath: readonly string[], bundled: ReadonlySet<string>): SchemaTarget {
  const trimmed = reference.trim();
  if (trimmed === "") {
    return { kind: "none", reason: "is empty" };
  }

  // A fragment names a part of a file, not another file; an empty one is the same address.
  const hash = trimmed.indexOf("#");
  const address = hash === -1 ? trimmed : trimmed.slice(0, hash);

  if (bundled.has(address)) {
    return { kind: "bundled", id: address };
  }

  if (scheme.test(address)) {
    return /^https?:/i.test(address)
      ? { kind: "none", reason: "is a web address; ADP reads schemas from the project" }
      : { kind: "none", reason: "is not a file of the project" };
  }

  if (address.startsWith("/")) {
    return { kind: "none", reason: "is an absolute path; ADP reads a schema relative to the document" };
  }

  const segments = address.split("/");
  const last = segments[segments.length - 1];
  if (last === "" || last === "." || last === "..") {
    return { kind: "none", reason: "names a folder, not a file" };
  }

  const resolved = documentPath.slice(0, -1);
  for (const segment of segments) {
    if (segment === "" || segment === ".") {
      continue;
    }
    if (segment === "..") {
      if (resolved.length === 0) {
        return { kind: "none", reason: "leaves the project" };
      }
      resolved.pop();
      continue;
    }
    resolved.push(segment);
  }

  return { kind: "project", path: resolved };
}
