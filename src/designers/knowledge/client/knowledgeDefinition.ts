import type { TableComparison, TableDefinition } from "@client/table/library/definition/tableDefinition";

/**
 * The Knowledge designer's table, as declarations: its nine property types - how each is named,
 * drawn, edited, compared, sorted and grouped - and what a property's menu offers. The icons and
 * names are those of `knowledge.des`'s `ValueType`, and each type's comparisons carry the names
 * its `surface.valueTypes` gives them - the names a filter is stored under. Nothing here renders:
 * the table library draws from this.
 */

const is: TableComparison = { id: "is", label: "is" };
const isNot: TableComparison = { id: "is-not", label: "is not" };

const TEXT: readonly TableComparison[] = [
  is,
  isNot,
  { id: "contains", label: "contains" },
  { id: "does-not-contain", label: "does not contain" },
  { id: "starts-with", label: "starts with" },
  { id: "ends-with", label: "ends with" },
];

const NUMBER: readonly TableComparison[] = [
  { id: "equals", label: "equals" },
  { id: "does-not-equal", label: "does not equal" },
  { id: "greater-than", label: "is greater than" },
  { id: "less-than", label: "is less than" },
  { id: "at-least", label: "is at least" },
  { id: "at-most", label: "is at most" },
];

const CHECKBOX: readonly TableComparison[] = [
  { id: "is-checked", label: "is checked", takesValue: false },
  { id: "is-not-checked", label: "is not checked", takesValue: false },
];

const MOMENT: readonly TableComparison[] = [
  is,
  { id: "is-before", label: "is before" },
  { id: "is-after", label: "is after" },
  { id: "is-on-or-before", label: "is on or before" },
  { id: "is-on-or-after", label: "is on or after" },
];

const SEVERAL: readonly TableComparison[] = [
  { id: "contains", label: "contains" },
  { id: "does-not-contain", label: "does not contain" },
];

export const knowledgeTable: TableDefinition = {
  kinds: {
    text: { icon: "mdi-format-text", label: "Text", editor: "text", comparisons: TEXT },
    number: { icon: "mdi-pound", label: "Number", editor: "number", comparisons: NUMBER },
    checkbox: { icon: "mdi-checkbox-marked-outline", label: "Checkbox", editor: "checkbox", comparisons: CHECKBOX, groupable: true },
    date: { icon: "mdi-calendar", label: "Date", editor: "date", comparisons: MOMENT },
    dateTime: { icon: "mdi-calendar-clock", label: "Date and time", editor: "datetime", comparisons: MOMENT },
    time: { icon: "mdi-clock-outline", label: "Time", editor: "time", comparisons: MOMENT },
    selection: { icon: "mdi-chevron-down-circle-outline", label: "Selection", editor: "option", comparisons: [is, isNot], groupable: true },
    multipleSelection: { icon: "mdi-format-list-bulleted", label: "Multiple selection", editor: "options", comparisons: SEVERAL, groupable: true },
    // A relation is made by choosing its target file, which a menu entry cannot ask: it is not
    // offered where a type is picked from a list, and is added through its own flow.
    relation: { icon: "mdi-arrow-top-right", label: "Relation", editor: "rows", comparisons: SEVERAL, groupable: true, addable: false },
  },
  columnActions: ["rename", "changeType", "filter", "sortAscending", "sortDescending", "group", "hide", "wrap", "insertLeft", "insertRight", "duplicate", "delete"],
  defaultKind: "text",
};
