import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";

/** Where a label is, in the canvas's own units, and what it currently reads. */
export interface LabelPlacement {
  x: number;
  y: number;
  width: number;
  height: number;
  /** The text the editor replaces, so it can open with what is on screen. */
  text: string;
}

/** A canvas's answer for one element: where its label is, or null if it cannot place it. */
export type LabelPlacementResolver = (elementId: string) => LabelPlacement | null;

/**
 * Registration and lookup are two contexts on purpose, and collapsing them into one is the
 * mistake to avoid: a single value has to change identity when a canvas mounts, so that readers
 * re-render - and a registering canvas whose effect depends on that same value then unregisters
 * and re-registers on every change, bumping the version again. That is an infinite loop, and it
 * was written once here before being split.
 */
interface InlineLabelPlacementRegistration {
  /** Adds a canvas's resolver and returns its withdrawal. */
  register: (resolve: LabelPlacementResolver) => () => void;
  /** Wakes the readers because a registered canvas's answers may have changed. */
  touch: () => void;
}

const RegisterContext = createContext<InlineLabelPlacementRegistration | undefined>(undefined);
interface InlineLabelPlacementLookup {
  placementFor: LabelPlacementResolver;
  /**
   * Whether any canvas is registered at all. A registry with nothing in it means "no canvas is
   * mounted", which is not the same as "that element is gone" - and a reader that confuses the
   * two abandons an edit because a panel was between mounts.
   */
  anyCanvasRegistered: boolean;
}

const PlacementContext = createContext<InlineLabelPlacementLookup | undefined>(undefined);

/**
 * Connects the shell's prompt host to whichever diagram canvases are mounted, exactly as
 * `DiagramToolboxContext` connects the Toolbox panel to them.
 *
 * The point is that the *same* function answers for both readers. The host asks "can anyone
 * place this?" to decide whether to stand down, and the canvas asks "can I place this?" to
 * decide whether to draw an editor. Two consumers each deciding for themselves would be two
 * chances to disagree, and a disagreement shows as a dialog on top of an editor, or as a click
 * that does nothing at all.
 *
 * Registration is per canvas rather than per prompt, which is what makes the answer available in
 * the first render that sees a prompt: a canvas is mounted long before anyone can press F2 on
 * something in it, so by the time a prompt exists the registry has already answered and no
 * dialog flashes before the editor appears.
 */
export function InlineLabelPlacementProvider({ children }: { children: ReactNode }) {
  const resolvers = useRef(new Set<LabelPlacementResolver>());
  const [version, setVersion] = useState(0);

  // Stable for the provider's whole life, so a registering canvas's effect runs once.
  const registration = useMemo<InlineLabelPlacementRegistration>(
    () => ({
      register: (resolve) => {
        resolvers.current.add(resolve);
        setVersion((previous) => previous + 1);
        return () => {
          resolvers.current.delete(resolve);
          setVersion((previous) => previous + 1);
        };
      },
      touch: () => setVersion((previous) => previous + 1),
    }),
    [],
  );

  // Rebuilt per version, so a reader holding it re-reads the set after a canvas mounts,
  // unmounts, or reports that its answers changed; the set itself is stable, and this identity
  // is what wakes the readers.
  const lookup = useMemo<InlineLabelPlacementLookup>(
    () => ({
      placementFor: (elementId) => {
        for (const resolve of resolvers.current) {
          const placement = resolve(elementId);
          if (placement !== null) {
            return placement;
          }
        }
        return null;
      },
      anyCanvasRegistered: resolvers.current.size > 0,
    }),
    [version],
  );

  return (
    <RegisterContext.Provider value={registration}>
      <PlacementContext.Provider value={lookup}>{children}</PlacementContext.Provider>
    </RegisterContext.Provider>
  );
}

/**
 * Registers a mounted canvas's placement resolver, and withdraws it on unmount.
 *
 * Memoize the resolver on **what it reads** - a canvas's model, typically. The registration
 * itself is made once and never replaced: what goes into the set is a stable indirection to
 * whatever the canvas's current resolver is, and a changed resolver only *wakes* the readers.
 *
 * That indirection is not a refinement, it is the fix for a real defect, found by running the app
 * rather than by any test. Registering the resolver directly meant a changed model ran the
 * effect's cleanup and then its body: unregister, then register. Between those two the set is
 * EMPTY, and a reader asking in that instant is told the element cannot be placed - which the
 * shell reads as "the thing being edited has gone" and cancels the interaction. Every mindmap
 * rename died that way, immediately and silently, while every unit test passed: a stubbed canvas
 * registers once and never re-registers, so the window does not exist in a test.
 */
export function useRegisterInlineLabelPlacement(resolve: LabelPlacementResolver): void {
  const registration = useContext(RegisterContext);
  const latest = useRef(resolve);
  latest.current = resolve;

  useEffect(() => {
    if (registration === undefined) {
      return;
    }
    return registration.register((elementId) => latest.current(elementId));
  }, [registration]);

  useEffect(() => {
    // A changed resolver means this canvas's answers may have changed - so wake the readers,
    // without the registry ever passing through empty on the way.
    registration?.touch();
  }, [registration, resolve]);
}

/**
 * Asks every mounted canvas whether it can place an element's label. Outside a provider nothing
 * can be placed, which is the right answer rather than an error: a surface with no canvas under
 * it - the explorer, a ribbon - simply keeps its dialog.
 */
export function useInlineLabelPlacement(): InlineLabelPlacementLookup {
  return useContext(PlacementContext) ?? NO_CANVASES;
}

const NO_CANVASES: InlineLabelPlacementLookup = { placementFor: () => null, anyCanvasRegistered: false };
