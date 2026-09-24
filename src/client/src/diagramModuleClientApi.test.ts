import { describe, expect, it } from "vitest";
import { computeModuleClientApiSurface, SET_B_ROOTS } from "./diagramModuleClientApi.surface";

/**
 * The canaries for the computed module-facing surface (Requirement 6.4).
 *
 * The readme this feeds is checked against two sets that are recomputed on every run, so the
 * failure mode to fear is not a wrong answer but an EMPTY one: a moved folder, a renamed alias
 * or a parse that silently reads nothing would leave the sets small and every later check would
 * pass by having nothing to check. These assert the computation is alive before anything trusts
 * its output - a floor, two named members that must be found, and that every surface file was
 * actually read.
 *
 * The floor is set below the measured size rather than at it, so an ordinary edit does not fail
 * it while an emptied computation does. The named members are the better half of the test: a
 * floor can be met by a set full of the wrong names, and `DiagramDefinition` reaching Set B is
 * the walk working rather than the count being large.
 */
describe("the module-facing client API surface, computed", () => {
  const surface = computeModuleClientApiSurface();
  const everyName = new Set([...surface.setA.keys(), ...surface.setB.keys()]);

  it("finds a surface at all, above a floor set below the measured size", () => {
    // Assert. 90 is just under the size measured while the design was written; the message
    // carries both sets' counts because a failure here is nearly always one of them collapsing
    // to zero rather than both shrinking together.
    expect(
      everyName.size,
      `Set A ∪ Set B holds ${everyName.size} names (Set A ${surface.setA.size}, Set B ${surface.setB.size}), ` +
        `computed from ${surface.surfaceFiles.length} surface files and ${surface.moduleFiles.length} module files. ` +
        "Below the floor means the computation found little or nothing - check the @client alias and the module client folders " +
        "before assuming the API shrank.",
    ).toBeGreaterThanOrEqual(90);
  });

  it("reaches the four roots' own names in Set B, the walk rather than the count", () => {
    // Assert. Every root is declared by a surface file, so every root must come back;
    // `DiagramDefinition` is the one named in the requirement.
    expect(surface.setB.has("DiagramDefinition")).toBe(true);
    for (const root of SET_B_ROOTS) {
      expect([...surface.setB.keys()], `'${root.name}' is a Set B root and must be reached by the walk`).toContain(root.name);
    }
  });

  it("sees what a module imports from outside the library, in Set A", () => {
    // Assert. `useViewReport` lives outside the library and reaches the surface only by being
    // imported - so finding it proves Set A's import parse AND that a non-library file can
    // enter the surface, which is the half a library-only walk would miss.
    expect(
      [...surface.setA.keys()],
      `Set A holds ${surface.setA.size} names from ${surface.moduleFiles.length} module client files`,
    ).toContain("useViewReport");
  });

  it("read every surface file it names, and found exports in them", () => {
    // Assert. computeModuleClientApiSurface throws on an unreadable file, so reaching here means
    // each was read; what this adds is that reading them produced declarations, which is the
    // difference between a file that parsed and a file that parsed to nothing.
    expect(surface.surfaceFiles.length).toBeGreaterThanOrEqual(14);
    expect(surface.exportsBySurfaceFile.size).toBeGreaterThanOrEqual(90);
    for (const root of SET_B_ROOTS) {
      expect(surface.exportsBySurfaceFile.has(root.name), `no surface file exports the root '${root.name}'`).toBe(true);
    }
  });
});
