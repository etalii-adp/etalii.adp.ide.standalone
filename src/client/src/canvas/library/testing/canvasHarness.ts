import { vi, type Mock } from "vitest";
import { selectedElementIdOf } from "../../selection";
import type { ActionOutcome, ContextConnectionValue, PropertyDescription } from "../../../shell/context/ContextConnectionProvider";
import type { ContextSelection } from "../../../generated/context_pb";

/**
 * The helpers every canvas test used to write for itself (client-centralization Requirement 10).
 *
 * Each was copied file to file - the pointer-event factory into 23 tests, the `pushedIds`
 * adapter into 18, a partial fake of the context connection into 30 - and the copies
 * drifted: a factory here without `cancelable`, a fake there without the member the canvas had
 * just started calling. They are written once here instead, and `noCanvasTestCopiesAHelper`
 * fails on a canvas test that declares its own again.
 *
 * jsdom's missing pointer capture is not here: it is a workaround for the environment rather
 * than a helper a test chooses, so `test-setup.ts` installs it for every test (Requirement 10.1).
 *
 * `renderCanvas` is deliberately not here. Its props differ per module - a model, a definition,
 * a stream to fake - so each module's own copy is that module's harness, not a duplicate
 * (Requirement 10.4).
 */

/**
 * A pointer event jsdom can actually carry. jsdom implements no `PointerEvent`, and
 * `fireEvent.pointerDown` builds a plain `Event` that drops `clientX`/`clientY` and `button`; a
 * `MouseEvent` named for the pointer event keeps them, and React dispatches on the type name.
 *
 * `pointerId` is set on the event when given: a `MouseEvent` has none of its own, and the
 * gesture hook tells one pointer from another by it.
 */
export function pointer(type: string, init: MouseEventInit & { pointerId?: number } = {}): MouseEvent {
  const { pointerId, ...mouse } = init;
  const event = new MouseEvent(type, { bubbles: true, cancelable: true, ...mouse });
  if (pointerId !== undefined) {
    Object.defineProperty(event, "pointerId", { value: pointerId });
  }
  return event;
}

/** The element id one pushed selection names, or `null` for a clear or a selection of no element. */
function pushedIdOf(push: unknown): string | null {
  return push === null ? null : (selectedElementIdOf(push as ContextSelection) ?? null);
}

/**
 * The ids a canvas pushed, oldest first, with `null` for a clear - what
 * `LibrarySelectionHarness.pushedIds` answers. Reads either a list the fake's `select` appends to,
 * or the `select` mock itself.
 */
export function idsPushed(pushes: readonly unknown[] | Mock): (string | null)[] {
  const list: readonly unknown[] = Array.isArray(pushes) ? pushes : (pushes as Mock).mock.calls.map(([push]) => push);
  return list.map(pushedIdOf);
}

/**
 * The context connection with every member faked. The data member is data; every function member
 * is a `vi.fn()`, so a test can assert on any of them and none is ever missing.
 */
type FakeContextConnection = {
  readonly [K in keyof ContextConnectionValue]: ContextConnectionValue[K] extends (...args: infer A) => infer R
    ? Mock<(...args: A) => R>
    : ContextConnectionValue[K];
};

const accepted = (): Promise<ActionOutcome> => Promise.resolve({ accepted: true, error: "" });
const noProperties = (): Promise<PropertyDescription> => Promise.resolve({ properties: [], error: "" });

/** A given function as a mock: kept as it is when it already is one, so its owner's handle still reads it. */
function asMock<F extends (...args: any[]) => any>(given: F | undefined, fallback: F): Mock<F> {
  const implementation = given ?? fallback;
  return vi.isMockFunction(implementation) ? (implementation as unknown as Mock<F>) : vi.fn(implementation);
}

/**
 * <b>The one fake of the context connection</b> (Requirement 10.3). A test passes only the members
 * it drives or reads; everything else answers the way a quiet backend does - a select that goes
 * nowhere, actions accepted, no properties.
 *
 * The drift this closes is a canvas that starts calling a member its test's partial fake omitted:
 * it crashed in that one file and nowhere else. The return type lists every member of
 * `ContextConnectionValue`, so a member added there fails the typecheck here, once, rather than
 * in whichever canvas test happens to call it first.
 *
 * Mount it with `useContextConnection: () => connection` inside the module's own `vi.mock`, with
 * `connection` built once at module level - one stable object, as the real provider memoizes its
 * value.
 */
export function fakeContextConnection(overrides: Partial<ContextConnectionValue> = {}): FakeContextConnection {
  return {
    watchId: overrides.watchId ?? new Uint8Array(16),
    select: asMock(overrides.select, () => {}),
    executeAction: asMock(overrides.executeAction, accepted),
    clearReveal: asMock(overrides.clearReveal, () => {}),
    revealPath: asMock(overrides.revealPath, () => {}),
    executeShortcut: asMock(overrides.executeShortcut, accepted),
    describeProperties: asMock(overrides.describeProperties, noProperties),
    setProperty: asMock(overrides.setProperty, accepted),
  };
}
