import { useEffect, useRef } from "react";
import { VIEW_REPORT_DEBOUNCE_MS, type ViewBox, type Viewport } from "./viewReport";

/**
 * Reports the canvas's view once it settles, and again on every later change.
 *
 * **The report observes the view; it is not wired to the gesture that moved it.** A pan, a
 * scrollbar thumb, a wheel zoom and a programmatic reveal all reach this the same way - they
 * change the canvas's view state, and this fires. That is what makes the loop hold for ways of
 * moving a view that nobody has thought of yet, and it is why centralizing the pan gesture
 * elsewhere needs no change here (view-delta-adoption Requirement 6.4).
 *
 * The ordering the three viewport specifications share, stated where it is implemented: the
 * canvas's own view state is the single source of truth; state is the module's, gestures write
 * it, scrollbars write it and read it back to place the thumb, and reporting observes it.
 */
export interface ViewReportOptions {
  /**
   * The canvas's current view. Only its four numbers matter: the effect is keyed on their
   * values, so a canvas that rebuilds its view object every render still reports once per
   * actual change.
   */
  view: ViewBox;
  /** The module's report function, from {@link viewReportOf}. */
  report: (viewport: Viewport) => void;
  /**
   * The viewport to report, in the module's own units. Called when the debounce fires rather
   * than during render, so it reads current refs - which is how a canvas reaches its live view
   * and its measured surface. The shared code converts nothing (Requirement 3.4).
   */
  convert: () => Viewport;
  /**
   * False while the diagram is loading or has failed. A report before the first delta describes
   * a view of nothing, and a report after a permanent failure is a call to a connection that
   * has just been told its path is gone.
   */
  ready: boolean;
}

export function useViewReport({ view, report, convert, ready }: ViewReportOptions): void {
  // The refs keep the report and the conversion out of the effect's dependencies - it is the
  // box that matters, not the functions' identities. Without this a `report` recreated per
  // render re-fires the effect every render and the debounce protects nothing.
  const reportRef = useRef(report);
  reportRef.current = report;
  const convertRef = useRef(convert);
  convertRef.current = convert;

  const viewKey = `${view.x},${view.y},${view.w},${view.h}`;
  useEffect(() => {
    if (!ready) {
      return;
    }

    const timer = setTimeout(() => reportRef.current(convertRef.current()), VIEW_REPORT_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [viewKey, ready]);
}
