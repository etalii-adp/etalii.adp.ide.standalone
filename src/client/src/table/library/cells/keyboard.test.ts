import { describe, expect, it } from "vitest";
import { keyOutcome, tabTarget } from "./keyboard";

const bounds = { rows: 100, columns: 4 };
const at = { row: 10, column: 2 };
const move = (row: number, column: number) => ({ kind: "move", to: { row, column } });

describe("keyOutcome", () => {
  it("moves one cell with an arrow key", () => {
    expect(keyOutcome({ key: "ArrowUp" }, at, bounds)).toEqual(move(9, 2));
    expect(keyOutcome({ key: "ArrowDown" }, at, bounds)).toEqual(move(11, 2));
    expect(keyOutcome({ key: "ArrowLeft" }, at, bounds)).toEqual(move(10, 1));
    expect(keyOutcome({ key: "ArrowRight" }, at, bounds)).toEqual(move(10, 3));
  });

  it("stays inside the table at its edges", () => {
    expect(keyOutcome({ key: "ArrowUp" }, { row: 0, column: 0 }, bounds)).toEqual(move(0, 0));
    expect(keyOutcome({ key: "ArrowLeft" }, { row: 0, column: 0 }, bounds)).toEqual(move(0, 0));
    expect(keyOutcome({ key: "ArrowDown" }, { row: 99, column: 3 }, bounds)).toEqual(move(99, 3));
    expect(keyOutcome({ key: "ArrowRight" }, { row: 99, column: 3 }, bounds)).toEqual(move(99, 3));
  });

  it("goes to an end with the command key, on either platform's", () => {
    expect(keyOutcome({ key: "ArrowUp", ctrlKey: true }, at, bounds)).toEqual(move(0, 2));
    expect(keyOutcome({ key: "ArrowDown", metaKey: true }, at, bounds)).toEqual(move(99, 2));
    expect(keyOutcome({ key: "ArrowLeft", ctrlKey: true }, at, bounds)).toEqual(move(10, 0));
    expect(keyOutcome({ key: "ArrowRight", ctrlKey: true }, at, bounds)).toEqual(move(10, 3));
  });

  it("goes to the ends of the row and of the table with Home and End", () => {
    expect(keyOutcome({ key: "Home" }, at, bounds)).toEqual(move(10, 0));
    expect(keyOutcome({ key: "End" }, at, bounds)).toEqual(move(10, 3));
    expect(keyOutcome({ key: "Home", ctrlKey: true }, at, bounds)).toEqual(move(0, 0));
    expect(keyOutcome({ key: "End", ctrlKey: true }, at, bounds)).toEqual(move(99, 3));
  });

  it("pages by ten rows", () => {
    expect(keyOutcome({ key: "PageDown" }, at, bounds)).toEqual(move(20, 2));
    expect(keyOutcome({ key: "PageUp" }, { row: 4, column: 2 }, bounds)).toEqual(move(0, 2));
  });

  it("moves in reading order with Tab", () => {
    expect(keyOutcome({ key: "Tab" }, at, bounds)).toEqual(move(10, 3));
    expect(keyOutcome({ key: "Tab", shiftKey: true }, at, bounds)).toEqual(move(10, 1));
  });

  it("opens the editor with Enter and with F2", () => {
    expect(keyOutcome({ key: "Enter" }, at, bounds)).toEqual({ kind: "edit" });
    expect(keyOutcome({ key: "F2" }, at, bounds)).toEqual({ kind: "edit" });
  });

  it("asks for a new row with Shift and Enter", () => {
    expect(keyOutcome({ key: "Enter", shiftKey: true }, at, bounds)).toEqual({ kind: "newRow" });
  });

  it("clears with Delete and with Backspace", () => {
    expect(keyOutcome({ key: "Delete" }, at, bounds)).toEqual({ kind: "clear" });
    expect(keyOutcome({ key: "Backspace" }, at, bounds)).toEqual({ kind: "clear" });
  });

  it("starts an edit that replaces the value when a character is typed", () => {
    expect(keyOutcome({ key: "a" }, at, bounds)).toEqual({ kind: "edit", replace: "a" });
    expect(keyOutcome({ key: "7" }, at, bounds)).toEqual({ kind: "edit", replace: "7" });
  });

  it("leaves a shortcut and a key it does not know alone", () => {
    // A command with a letter is the application's - copy, undo - never a character typed.
    expect(keyOutcome({ key: "c", ctrlKey: true }, at, bounds)).toEqual({ kind: "none" });
    expect(keyOutcome({ key: "z", metaKey: true }, at, bounds)).toEqual({ kind: "none" });
    expect(keyOutcome({ key: "ArrowDown", altKey: true }, at, bounds)).toEqual({ kind: "none" });
    expect(keyOutcome({ key: "Shift" }, at, bounds)).toEqual({ kind: "none" });
    expect(keyOutcome({ key: "Escape" }, at, bounds)).toEqual({ kind: "none" });
  });

  it("does nothing in a table with no cells", () => {
    expect(keyOutcome({ key: "ArrowDown" }, { row: 0, column: 0 }, { rows: 0, columns: 4 })).toEqual({ kind: "none" });
    expect(keyOutcome({ key: "Enter" }, { row: 0, column: 0 }, { rows: 5, columns: 0 })).toEqual({ kind: "none" });
  });
});

describe("tabTarget", () => {
  it("wraps to the next line after a line's last cell, and back", () => {
    expect(tabTarget({ row: 10, column: 3 }, bounds, false)).toEqual({ row: 11, column: 0 });
    expect(tabTarget({ row: 10, column: 0 }, bounds, true)).toEqual({ row: 9, column: 3 });
  });

  it("stays put at either end of the table, so the key can leave it", () => {
    expect(tabTarget({ row: 99, column: 3 }, bounds, false)).toEqual({ row: 99, column: 3 });
    expect(tabTarget({ row: 0, column: 0 }, bounds, true)).toEqual({ row: 0, column: 0 });
  });
});
