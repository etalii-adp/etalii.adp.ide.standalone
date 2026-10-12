import { describe, expect, it } from "vitest";
import disText from "../definition/agent-behavior-modelling.dis?raw";
import { actionForKey } from "@client/canvas/library/definition/actions";
import { compileNotation } from "@client/canvas/library/disl/compileNotation";
import { parseDisl } from "@client/canvas/library/disl/disTypes";
import { ABM_BINDINGS } from "./abmBindings";
import { AbmActions } from "./abmIds";

/**
 * Move earlier and Move later answer to Alt+Up and Alt+Down as the canvas hears them. The definition
 * writes `Alt+Up`; a browser reports that press as the key `ArrowUp` with `altKey` set, so a shortcut
 * kept whole as the key `"Alt+Up"` matched no keypress at all (spec 025, T032/T033).
 */
describe("the behavior model's move shortcuts", () => {
  const definition = compileNotation(parseDisl(disText), ABM_BINDINGS);
  const lookup = {
    actions: definition.actions,
    targetKind: "element" as const,
    targetId: "1.2",
    typeId: "action",
    source: { element: { id: "1.2", type: "action", x: 0, y: 0 } },
  };
  const press = (key: string, altKey: boolean) => ({ key, ctrlKey: false, shiftKey: false, altKey, metaKey: false });

  it("dispatches Move earlier for Alt+ArrowUp", () => {
    expect(actionForKey(lookup, press("ArrowUp", true))?.actionId).toBe(AbmActions.moveEarlier);
  });

  it("dispatches Move later for Alt+ArrowDown", () => {
    expect(actionForKey(lookup, press("ArrowDown", true))?.actionId).toBe(AbmActions.moveLater);
  });

  it("dispatches nothing for a bare arrow key", () => {
    expect(actionForKey(lookup, press("ArrowUp", false))).toBeNull();
    expect(actionForKey(lookup, press("ArrowDown", false))).toBeNull();
  });
});
