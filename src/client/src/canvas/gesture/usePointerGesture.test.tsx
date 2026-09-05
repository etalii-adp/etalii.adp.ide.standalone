import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { GESTURE_MOVEMENT_THRESHOLD_PX, usePointerGesture } from "./usePointerGesture";

/**
 * A pointer event jsdom can actually carry: jsdom implements no PointerEvent, and
 * `fireEvent.pointerDown` builds a bare Event whose `button` is undefined - which the
 * arbiter reads as not-the-primary-button, correctly. A MouseEvent typed "pointerdown"
 * bubbles the same way and carries the button, which is what a real browser delivers.
 * The idiom is AnsibleCanvas.test.tsx's, for the same reason.
 */
function pointer(type: string, init: MouseEventInit & { pointerId?: number }) {
  const event = new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
  if (init.pointerId !== undefined) {
    Object.defineProperty(event, "pointerId", { value: init.pointerId });
  }
  return event;
}

// jsdom implements no pointer capture on SVG elements; the arbiter uses it so a release
// outside the surface still ends the gesture. Stubbed rather than feature-detected in the
// module, so the production path stays the one that ships.
beforeEach(() => {
  SVGElement.prototype.setPointerCapture ??= () => {};
  SVGElement.prototype.releasePointerCapture ??= () => {};
});

type Target = { kind: "element" | "relation" | "background"; id: string };

const onPress = vi.fn();
const onDragMove = vi.fn();
const onDragEnd = vi.fn();
const onDragAbandon = vi.fn();

/** Two pressable targets and the surface, wired the way a canvas would wire them. */
function Harness() {
  const gesture = usePointerGesture<Target>({ onPress, onDragMove, onDragEnd, onDragAbandon });
  return (
    <svg data-testid="surface" {...gesture.background({ kind: "background", id: "surface" })}>
      <rect data-testid="a" {...gesture.press({ kind: "element", id: "a" })} />
      <rect data-testid="b" {...gesture.press({ kind: "element", id: "b" })} />
      <circle data-testid="r" {...gesture.press({ kind: "relation", id: "r" })} />
      <text data-testid="decoration">not a target</text>
    </svg>
  );
}

function renderHarness() {
  onPress.mockClear();
  onDragMove.mockClear();
  onDragEnd.mockClear();
  onDragAbandon.mockClear();
  render(<Harness />);
  return {
    a: screen.getByTestId("a"),
    b: screen.getByTestId("b"),
    r: screen.getByTestId("r"),
    surface: screen.getByTestId("surface"),
    decoration: screen.getByTestId("decoration"),
  };
}

describe("usePointerGesture", () => {
  it("an unmoved release is a click: onPress, and only onPress", () => {
    const { a } = renderHarness();

    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(a, pointer("pointerup", { clientX: 10, clientY: 10 }));

    expect(onPress).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "a" });
    expect(onDragMove).not.toHaveBeenCalled();
    expect(onDragEnd).not.toHaveBeenCalled();
    expect(onDragAbandon).not.toHaveBeenCalled();
  });

  it("a moved release is a drag: onDragMove then onDragEnd, and never onPress", () => {
    const { a } = renderHarness();

    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(a, pointer("pointermove", { clientX: 30, clientY: 10 }));
    fireEvent(a, pointer("pointerup", { clientX: 30, clientY: 10 }));

    expect(onDragMove).toHaveBeenCalledWith({ kind: "element", id: "a" }, 20, 0);
    expect(onDragEnd).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "a" }, 20, 0);
    expect(onPress).not.toHaveBeenCalled();
  });

  it("movement of exactly the threshold is still a click; one pixel past it is a drag", () => {
    const { a, b } = renderHarness();

    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(a, pointer("pointermove", { clientX: GESTURE_MOVEMENT_THRESHOLD_PX, clientY: 0 }));
    fireEvent(a, pointer("pointerup", { clientX: GESTURE_MOVEMENT_THRESHOLD_PX, clientY: 0 }));
    expect(onPress).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "a" });
    expect(onDragEnd).not.toHaveBeenCalled();

    fireEvent(b, pointer("pointerdown", { button: 0, clientX: 0, clientY: 0 }));
    fireEvent(b, pointer("pointermove", { clientX: GESTURE_MOVEMENT_THRESHOLD_PX + 1, clientY: 0 }));
    fireEvent(b, pointer("pointerup", { clientX: GESTURE_MOVEMENT_THRESHOLD_PX + 1, clientY: 0 }));
    expect(onDragEnd).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "b" }, GESTURE_MOVEMENT_THRESHOLD_PX + 1, 0);
  });

  it("captures the pointer on the pressed element, so an off-surface release still ends the gesture", () => {
    const { a } = renderHarness();
    const capture = vi.spyOn(a as unknown as SVGElement, "setPointerCapture");

    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10, pointerId: 7 }));
    expect(capture).toHaveBeenCalledWith(7);

    // Under capture the browser delivers these to the captured element wherever the pointer
    // is - outside the surface included - which is exactly what jsdom is imitating here.
    fireEvent(a, pointer("pointermove", { clientX: 500, clientY: 500, pointerId: 7 }));
    fireEvent(a, pointer("pointerup", { clientX: 500, clientY: 500, pointerId: 7 }));

    expect(onDragEnd).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "a" }, 490, 490);
  });

  it("losing the capture abandons the gesture instead of leaking it", () => {
    const { a, b } = renderHarness();

    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(a, pointer("pointermove", { clientX: 40, clientY: 10 }));
    fireEvent(a, pointer("lostpointercapture", {}));

    expect(onDragAbandon).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "a" });
    expect(onDragEnd).not.toHaveBeenCalled();

    // And nothing lingers: the next press is judged entirely on its own.
    fireEvent(b, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(b, pointer("pointerup", { clientX: 10, clientY: 10 }));
    expect(onPress).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "b" });
  });

  it("the implicit capture release after a pointerup abandons nothing", () => {
    const { a } = renderHarness();

    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(a, pointer("pointerup", { clientX: 10, clientY: 10 }));
    // Browsers release the capture implicitly after pointerup and fire this; it must not
    // turn a completed click into an abandonment.
    fireEvent(a, pointer("lostpointercapture", {}));

    expect(onPress).toHaveBeenCalledTimes(1);
    expect(onDragAbandon).not.toHaveBeenCalled();
  });

  it("a second pointer down while a gesture is active starts nothing", () => {
    const { a, b } = renderHarness();

    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10, pointerId: 1 }));
    fireEvent(b, pointer("pointerdown", { button: 0, clientX: 50, clientY: 50, pointerId: 2 }));
    fireEvent(a, pointer("pointerup", { clientX: 10, clientY: 10, pointerId: 1 }));

    expect(onPress).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "a" });
  });

  it("a non-primary button starts nothing - the menu's gesture, not ours", () => {
    const { a } = renderHarness();

    fireEvent(a, pointer("pointerdown", { button: 2, clientX: 10, clientY: 10 }));
    fireEvent(a, pointer("pointerup", { clientX: 10, clientY: 10 }));

    expect(onPress).not.toHaveBeenCalled();
  });

  it("abandon() resolves the active gesture - the module's Escape handler", () => {
    const { a } = renderHarness();
    let abandonNow: () => void = () => {};

    function EscapeHarness() {
      const gesture = usePointerGesture<Target>({ onPress, onDragMove, onDragEnd, onDragAbandon });
      abandonNow = gesture.abandon;
      return <svg><rect data-testid="esc-a" {...gesture.press({ kind: "element", id: "esc-a" })} /></svg>;
    }
    render(<EscapeHarness />);
    const target = screen.getByTestId("esc-a");

    fireEvent(target, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(target, pointer("pointermove", { clientX: 40, clientY: 10 }));
    abandonNow();

    expect(onDragAbandon).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "esc-a" });
    expect(onDragEnd).not.toHaveBeenCalled();
    void a;
  });

  it("a background press begins only on the surface itself, never from a bubbling child", () => {
    const { surface, decoration } = renderHarness();

    // A press on a decoration that is not a target bubbles to the surface and must not
    // become a background gesture.
    fireEvent(decoration, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(decoration, pointer("pointerup", { clientX: 10, clientY: 10 }));
    expect(onPress).not.toHaveBeenCalled();

    fireEvent(surface, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(surface, pointer("pointerup", { clientX: 10, clientY: 10 }));
    expect(onPress).toHaveBeenCalledExactlyOnceWith({ kind: "background", id: "surface" });
  });

  it("a press on a target never reaches the background: one gesture, owned by the innermost", () => {
    const { r } = renderHarness();

    fireEvent(r, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(r, pointer("pointerup", { clientX: 10, clientY: 10 }));

    expect(onPress).toHaveBeenCalledExactlyOnceWith({ kind: "relation", id: "r" });
  });

  it("the anti-latch property: a completed drag leaves the very next click untouched", () => {
    const { a, b } = renderHarness();

    // A drag of A, released wherever it happens to end...
    fireEvent(a, pointer("pointerdown", { button: 0, clientX: 10, clientY: 10 }));
    fireEvent(a, pointer("pointermove", { clientX: 60, clientY: 60 }));
    fireEvent(a, pointer("pointerup", { clientX: 60, clientY: 60 }));
    // ...even one whose trailing click the browser sends somewhere unexpected:
    fireEvent(b, new MouseEvent("click", { bubbles: true }));

    // ...and the next press on B is a plain click that selects B. This is the defect the
    // module exists for; nothing here can swallow it because nothing here listens to click.
    fireEvent(b, pointer("pointerdown", { button: 0, clientX: 60, clientY: 60 }));
    fireEvent(b, pointer("pointerup", { clientX: 60, clientY: 60 }));

    expect(onPress).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "b" });
    expect(onDragEnd).toHaveBeenCalledExactlyOnceWith({ kind: "element", id: "a" }, 50, 50);
  });
});
