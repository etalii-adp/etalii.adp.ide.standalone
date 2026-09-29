import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { CanvasFrame } from "@client/canvas/library/surface/CanvasFrame";
import { ContextConnectionProvider } from "@client/shell/context/ContextConnectionProvider";
import { DiagramViewProvider } from "@client/shell/panels/DiagramViewContext";
import { DiagramToolboxProvider } from "@client/shell/panels/DiagramToolboxContext";
import { InlineLabelPlacementProvider } from "@client/shell/panels/InlineLabelPlacementContext";
import { CausalLoopCanvas } from "./CausalLoopCanvas";

/**
 * A refusal travels the whole chain to the library's one line, with nothing between faked but the
 * wire (client-centralization Requirement 2).
 *
 * Every module test mocks `useContextConnection`, so none of them can see this: the module's call
 * goes through the REAL context connection, whose gesture calls report to the canvas they are made
 * in, and the REAL frame the shell puts around the canvas draws the line. The module holds no
 * rejection state and draws nothing. If any hop is cut - the module stops calling, the connection
 * stops reporting, the frame stops drawing - the sentence never appears.
 */

const REFUSAL = "A variable cannot be added while the diagram is read-only.";

const parked = { [Symbol.asyncIterator]: () => ({ next: () => new Promise<never>(() => {}) }) };

vi.mock("@connectrpc/connect", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@connectrpc/connect")>();
  return {
    ...actual,
    createClient: () =>
      new Proxy(
        {},
        {
          get: (_target, property) => {
            if (property === "open" || property === "watch") {
              return () => parked;
            }
            if (property === "executeAction") {
              return () => Promise.resolve({ accepted: false, error: REFUSAL });
            }
            return () => Promise.resolve({ accepted: true, error: "", items: [] });
          },
        },
      ),
  };
});

vi.mock("@client/auth/AuthContext", () => {
  const transport = {};
  return { useAuth: () => ({ transport }) };
});

describe("a module's own call, refused, on the library's line", () => {
  it("shows a refused toolbox drop once, on the frame's line, through the real connection", async () => {
    // Arrange: the shell's providers and frame, as a workspace tab has them.
    const { container } = render(
      <ContextConnectionProvider projectId={new Uint8Array(16)}>
        <DiagramViewProvider>
          <DiagramToolboxProvider>
            <InlineLabelPlacementProvider>
              <CanvasFrame>
                <CausalLoopCanvas projectId={new Uint8Array(16)} entryId={new Uint8Array(16)} path={["feedback.adp"]} />
              </CanvasFrame>
            </InlineLabelPlacementProvider>
          </DiagramToolboxProvider>
        </DiagramViewProvider>
      </ContextConnectionProvider>,
    );
    const surface = container.querySelector("svg.library-canvas-surface")!;
    const data = new Map<string, string>([["application/x-adp-toolbox-item", "causal-loop.add-variable"]]);
    const dataTransfer = { getData: (type: string) => data.get(type) ?? "", dropEffect: "", types: [...data.keys()] };

    // Act: the module answers the drop with executeAction; the backend refuses.
    fireEvent.dragOver(surface, { dataTransfer });
    fireEvent.drop(surface, { dataTransfer, clientX: 0, clientY: 0 });

    // Assert.
    const line = await screen.findByText(REFUSAL);
    expect(line.getAttribute("data-canvas-surface")).toBe("refusal");
    expect(screen.getAllByText(REFUSAL)).toHaveLength(1);
  });
});
