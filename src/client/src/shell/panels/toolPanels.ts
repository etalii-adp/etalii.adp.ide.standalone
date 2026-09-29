import type { ToolPanelRegistration, ToolClientModule } from "./toolPanelRegistration";

/**
 * Every tool module's client registrations - diagrams, designers and editors - discovered rather
 * than listed.
 *
 * `import.meta.glob` is the client's answer to the backend's assembly scan: Vite resolves the
 * pattern at build time, so a module that ships a `client/register.ts` is picked up with no
 * edit to the shell, and one that is deleted takes its registrations with it.
 */
const modules = import.meta.glob<ToolClientModule>(
  // Three families, one registry: an editor module's text panel registers exactly as a diagram
  // module's canvas does, keyed by the "editor/<id>" mime the backend resolves for it. The
  // designer family has no module yet (src/designers/README.md); its glob matches nothing until
  // the first one arrives, and needs no edit here when it does.
  [
    "../../../../diagrams/*/client/register.ts",
    "../../../../designers/*/client/register.ts",
    "../../../../editors/*/client/register.ts",
  ],
  { eager: true },
);

/**
 * In path order, so two modules whose predicates overlap resolve the same way on every build.
 * Glob order is otherwise the file system's, which is not a promise anyone should rely on.
 */
export const toolPanels: ToolPanelRegistration[] = Object.keys(modules)
  .sort()
  .flatMap((path) => modules[path].registrations ?? []);

/** The registration that claims this MIME type, or undefined when nothing does. */
export function panelFor(mimeType: string): ToolPanelRegistration | undefined {
  return toolPanels.find((registration) => registration.matches(mimeType));
}
