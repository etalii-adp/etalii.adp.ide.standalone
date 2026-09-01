import type { DiagramCanvasRegistration, DiagramClientModule } from "./diagramCanvas";

/**
 * Every diagram module's client registrations, discovered rather than listed.
 *
 * `import.meta.glob` is the client's answer to the backend's assembly scan: Vite resolves the
 * pattern at build time, so a module that ships a `client/register.ts` is picked up with no
 * edit to the shell, and one that is deleted takes its registrations with it.
 */
const modules = import.meta.glob<DiagramClientModule>(
  // Two families, one registry: an editor module's canvas registers exactly as a diagram
  // module's does, keyed by the "editor/<id>" mime the backend resolves for it.
  ["../../../../diagrams/*/client/register.ts", "../../../../editors/*/client/register.ts"],
  { eager: true },
);

/**
 * In path order, so two modules whose predicates overlap resolve the same way on every build.
 * Glob order is otherwise the file system's, which is not a promise anyone should rely on.
 */
export const diagramCanvases: DiagramCanvasRegistration[] = Object.keys(modules)
  .sort()
  .flatMap((path) => modules[path].registrations ?? []);

/** The registration that claims this MIME type, or undefined when nothing does. */
export function canvasFor(mimeType: string): DiagramCanvasRegistration | undefined {
  return diagramCanvases.find((registration) => registration.matches(mimeType));
}
