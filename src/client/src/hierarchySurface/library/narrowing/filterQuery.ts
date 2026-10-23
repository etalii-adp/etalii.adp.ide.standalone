import type { HierarchySurfaceModel, HierarchySurfaceNode } from "../api/hierarchySurfaceModel";
import type { HierarchySurfaceLabelDefinition } from "../definition/hierarchySurfaceDefinition";
import { ancestorsOf, type HierarchyIndex } from "../layout";
import { lowerAscii, valuesOf } from "../labels/listEditing";

/** The word that selects levels by their title. */
export const ITEM_PREFIX = "item:";

/** The word that orders the entries within each level. */
export const SORT_PREFIX = "sort:";

/** One word of a query, as typed, and what it selects. */
export interface QueryWord {
  /** The word as written. */
  text: string;
  /** A label's value, a level's title pattern, a sort key, or a word the notation does not know, which selects nothing. */
  kind: "label" | "item" | "sort" | "unknown";
  /** The label a label word names. */
  label: string;
  /** What follows the prefix. */
  value: string;
}

/**
 * A filter's query in the notation of DESL section 7.7.3, read word by word: `<prefix><name>` for a label, `item:pattern`
 * for the levels whose title matches, and `sort:key`. The longest prefix that fits a word decides what it is, so
 * `category:` is not mistaken for a label introduced by `c`.
 */
export function parseQuery(text: string, labels: readonly HierarchySurfaceLabelDefinition[]): QueryWord[] {
  const prefixes = [...labels].sort((left, right) => right.prefix.length - left.prefix.length);
  return text
    .split(/\s+/)
    .filter((word) => word !== "")
    .map((word): QueryWord => {
      const lower = lowerAscii(word);
      if (lower.startsWith(ITEM_PREFIX) && word.length > ITEM_PREFIX.length) {
        return { text: word, kind: "item", label: "", value: word.slice(ITEM_PREFIX.length) };
      }
      if (lower.startsWith(SORT_PREFIX) && word.length > SORT_PREFIX.length) {
        return { text: word, kind: "sort", label: "", value: lowerAscii(word.slice(SORT_PREFIX.length)) };
      }
      const label = prefixes.find((candidate) => lower.startsWith(lowerAscii(candidate.prefix)) && word.length > candidate.prefix.length);
      return label === undefined
        ? { text: word, kind: "unknown", label: "", value: word }
        : { text: word, kind: "label", label: label.label, value: word.slice(label.prefix.length) };
    });
}

/** The query's text again, its words in their order. */
export function queryText(words: readonly QueryWord[]): string {
  return words.map((word) => word.text).join(" ");
}

/** The sort key of a query: of several `sort:` words the first counts; empty for the file's order. */
export function sortOf(words: readonly QueryWord[]): string {
  return words.find((word) => word.kind === "sort")?.value ?? "";
}

/** The query with its sort set to `key`, in the place of the first `sort:` word, or last; empty takes every `sort:` word out. */
export function withSort(words: readonly QueryWord[], key: string): QueryWord[] {
  if (key === "") {
    return words.filter((word) => word.kind !== "sort");
  }
  const sort: QueryWord = { text: SORT_PREFIX + key, kind: "sort", label: "", value: key };
  const at = words.findIndex((word) => word.kind === "sort");
  return at < 0 ? [...words, sort] : words.flatMap((word, place) => (word.kind !== "sort" ? [word] : place === at ? [sort] : []));
}

/** A title pattern as a test: `*` any run of characters, `?` one, the rest as written, in any case, the whole title. */
function patternOf(pattern: string): RegExp {
  const source = Array.from(pattern, (character) => (character === "*" ? ".*" : character === "?" ? "." : character.replace(/[.+^${}()|[\]\\]/g, "\\$&"))).join("");
  return new RegExp(`^${source}$`, "is");
}

/** What a query leaves of the page, and in which order the entries of a level stand. */
export interface Narrowing {
  /** Whether the query narrows at all; false when it only sorts or is empty. */
  narrows: boolean;
  /** Whether `node` stays on the page. */
  shows: (node: HierarchySurfaceNode) => boolean;
  /** The entries of one level in the query's order: the file's when it has no sort. */
  order: (entries: readonly HierarchySurfaceNode[]) => HierarchySurfaceNode[];
}

/**
 * The page a query leaves (FR-019, FR-020; DESL section 7.7.3). Words of one kind are alternatives and kinds combine:
 * an entry stays when, for every label its words name, it carries one of them, and it stands in a level an `item:` word
 * selects, or below one, or no `item:` word is given. A level stays when it holds an entry that stays, or, with no word
 * on entries, when it is selected or below one that is; and every level above what stays stays with it. The sort orders
 * the entries within each level: by number, by title, or by the place of their value in a label's list, those without
 * one last; the levels keep the file's order. Nothing here is sent: narrowing and sorting write nothing (FR-049).
 */
export function narrowingOf(model: HierarchySurfaceModel, index: HierarchyIndex, words: readonly QueryWord[]): Narrowing {
  const byLabel = new Map<string, Set<string>>();
  for (const word of words.filter((candidate) => candidate.kind === "label")) {
    byLabel.set(word.label, (byLabel.get(word.label) ?? new Set()).add(lowerAscii(word.value)));
  }
  const patterns = words.filter((word) => word.kind === "item").map((word) => patternOf(word.value));
  const narrows = byLabel.size > 0 || patterns.length > 0;
  const shown = new Set<string>();
  if (narrows) {
    const selected = (node: HierarchySurfaceNode) => node.role === "level" && patterns.some((pattern) => pattern.test(node.title));
    const inScope = (node: HierarchySurfaceNode) => patterns.length === 0 || selected(node) || ancestorsOf(index, node.id).some(selected);
    const keep = (node: HierarchySurfaceNode) => {
      shown.add(node.id);
      for (const level of ancestorsOf(index, node.id)) {
        shown.add(level.id);
      }
    };
    for (const node of model.nodes) {
      if (!inScope(node)) {
        continue;
      }
      if (node.role === "entry" && [...byLabel].every(([label, names]) => valuesOf(node, label).some((value) => names.has(lowerAscii(value))))) {
        keep(node);
      } else if (node.role === "level" && byLabel.size === 0) {
        keep(node);
      }
    }
  }

  const key = sortOf(words);
  const lists = new Map(model.optionLists.map((list) => [list.label, list.options.map((option) => lowerAscii(option.name))]));
  const rank = (node: HierarchySurfaceNode): number => {
    const value = valuesOf(node, key)[0];
    const place = value === undefined ? -1 : (lists.get(key)?.indexOf(lowerAscii(value)) ?? -1);
    // A value the list does not hold stands after those it does, and no value after both.
    return value === undefined ? Number.MAX_SAFE_INTEGER : place < 0 ? Number.MAX_SAFE_INTEGER - 1 : place;
  };
  const compare = (left: HierarchySurfaceNode, right: HierarchySurfaceNode): number => {
    switch (key) {
      case "number":
        return left.number.localeCompare(right.number, undefined, { numeric: true });
      case "title":
        return left.title.localeCompare(right.title, undefined, { sensitivity: "base" });
      default:
        return lists.has(key) ? rank(left) - rank(right) : 0;
    }
  };

  return {
    narrows,
    shows: (node) => !narrows || shown.has(node.id),
    // Array.prototype.sort is stable, so equal entries keep the file's order.
    order: (entries) => (key === "" ? [...entries] : [...entries].sort(compare)),
  };
}

/**
 * The query without the words that hide `nodeId`, as far as they hide it (FR-018, FR-021): the `item:` words when the
 * node stands outside every level they select, and the words of each label the node does not carry, or every label's
 * words for a level, which they hide by holding no entry that stays. The sort is kept.
 */
export function unhiding(model: HierarchySurfaceModel, index: HierarchyIndex, words: readonly QueryWord[], nodeId: string): QueryWord[] {
  const node = index.byId.get(nodeId);
  if (node === undefined || narrowingOf(model, index, words).shows(node)) {
    return [...words];
  }
  const patterns = words.filter((word) => word.kind === "item").map((word) => patternOf(word.value));
  const outside = patterns.length > 0 && ![node, ...ancestorsOf(index, nodeId)].some((level) => level.role === "level" && patterns.some((pattern) => pattern.test(level.title)));
  return words.filter((word) => {
    if (word.kind === "item") {
      return !outside;
    }
    if (word.kind !== "label") {
      return true;
    }
    if (node.role === "level") {
      return false;
    }
    const names = new Set(words.filter((other) => other.kind === "label" && other.label === word.label).map((other) => lowerAscii(other.value)));
    return valuesOf(node, word.label).some((value) => names.has(lowerAscii(value)));
  });
}
