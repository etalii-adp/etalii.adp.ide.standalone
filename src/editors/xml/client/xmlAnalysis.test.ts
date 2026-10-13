import { describe, expect, it, vi } from "vitest";
import { create, type MessageInitShape } from "@bufbuild/protobuf";
import { AnalyzeTextResponseSchema, CompleteTextResponseSchema } from "@client/generated/editors_pb";
import { ProblemSeverity } from "@client/generated/problems_pb";
import { checkingUnavailable, createXmlAnalysis, type XmlAnalysisCalls } from "./xmlAnalysis";

/**
 * The XML editor's analysis over fake calls (xml-editor task 9): what is asked, what an answer
 * becomes, and what happens to an answer that was overtaken or never came.
 */

function analysisOver(calls: Partial<XmlAnalysisCalls>) {
  const listener = { onStatus: vi.fn<(status: string) => void>(), onDependsOn: vi.fn<(paths: readonly (readonly string[])[]) => void>() };
  const analysis = createXmlAnalysis(
    {
      analyze: calls.analyze ?? (() => Promise.resolve(create(AnalyzeTextResponseSchema))),
      complete: calls.complete ?? (() => Promise.resolve(create(CompleteTextResponseSchema))),
    },
    listener,
  );
  return { analysis, listener };
}

const answered = (fields: MessageInitShape<typeof AnalyzeTextResponseSchema>) => create(AnalyzeTextResponseSchema, fields);
const offered = (fields: MessageInitShape<typeof CompleteTextResponseSchema>) => create(CompleteTextResponseSchema, fields);

describe("findings", () => {
  it("turns the backend's findings into the shared shape, with the status and the files read", async () => {
    // Arrange.
    const { analysis, listener } = analysisOver({
      analyze: () =>
        Promise.resolve(
          answered({
            findings: [
              { from: 1, to: 4, severity: ProblemSeverity.ERROR, message: "Bad.", source: "XML" },
              { from: 5, to: 6, severity: ProblemSeverity.WARNING, message: "Odd.", source: "Schema" },
              { from: 7, to: 8, severity: ProblemSeverity.INFO, message: "Noted.", source: "Schema" },
            ],
            status: "Schema: library.xsd",
            dependsOn: [{ segments: ["schemas", "library.xsd"] }],
          }),
        ),
    });

    // Act.
    const findings = await analysis.findings("<a/>");

    // Assert.
    expect(findings.map((finding) => [finding.from, finding.to, finding.severity, finding.message, finding.source])).toEqual([
      [1, 4, "error", "Bad.", "XML"],
      [5, 6, "warning", "Odd.", "Schema"],
      [7, 8, "info", "Noted.", "Schema"],
    ]);
    expect(analysis.status?.("<a/>")).toBe("Schema: library.xsd");
    expect(listener.onDependsOn).toHaveBeenCalledWith([["schemas", "library.xsd"]]);
  });

  it("abandons an older request still in flight when a newer one starts (Requirement 9.4)", async () => {
    // Arrange: the first answer never comes until its signal says otherwise.
    const signals: AbortSignal[] = [];
    const { analysis } = analysisOver({
      analyze: (_text, signal) => {
        signals.push(signal);
        return signals.length === 1
          ? new Promise((_resolve, reject) => signal.addEventListener("abort", () => reject(new DOMException("aborted", "AbortError"))))
          : Promise.resolve(answered({ status: "No schema" }));
      },
    });

    // Act.
    const older = analysis.findings("<a>");
    const newer = await analysis.findings("<a/>");

    // Assert: the older one was aborted and rejects, so the shared runner keeps what it shows;
    // only the newer one answers.
    expect(signals[0].aborted).toBe(true);
    await expect(older).rejects.toThrow();
    expect(newer).toEqual([]);
    expect(analysis.status?.("<a/>")).toBe("No schema");
  });

  it("says once that checking is unavailable when the backend cannot be reached (Requirement 9.5)", async () => {
    // Arrange.
    const { analysis, listener } = analysisOver({ analyze: () => Promise.reject(new Error("unreachable")) });

    // Act.
    const first = await analysis.findings("<a/>");
    const second = await analysis.findings("<a/> ");

    // Assert.
    expect(first).toEqual([]);
    expect(second).toEqual([]);
    expect(analysis.status?.("<a/> ")).toBe(checkingUnavailable);
    expect(listener.onStatus.mock.calls.filter(([status]) => status === checkingUnavailable)).toHaveLength(1);
  });

  it("shows the service's sentence in place of findings when it refuses the text", async () => {
    // Arrange.
    const { analysis } = analysisOver({ analyze: () => Promise.resolve(answered({ error: "Too large to check." })) });

    // Act.
    const findings = await analysis.findings("<a/>");

    // Assert.
    expect(findings).toEqual([]);
    expect(analysis.status?.("<a/>")).toBe("Too large to check.");
  });
});

describe("completions and hover", () => {
  it("asks at the offset and hands the items on in the shared shape", async () => {
    // Arrange.
    const complete = vi.fn<XmlAnalysisCalls["complete"]>(() =>
      Promise.resolve(
        offered({
          items: [{ label: "book", detail: "", info: "One book.", from: 3, to: 3, insert: 'book isbn="${1}">${2}</book>', boost: 0 }],
        }),
      ),
    );
    const { analysis } = analysisOver({ complete });

    // Act.
    const list = await analysis.completions?.("<a><", 4, false);

    // Assert.
    expect(complete).toHaveBeenCalledWith("<a><", 4, false, expect.any(AbortSignal));
    expect(list?.items).toEqual([{ label: "book", detail: undefined, info: "One book.", from: 3, to: 3, insert: 'book isbn="${1}">${2}</book>', boost: undefined }]);
  });

  it("describes a name already written by the item the schema offers at it", async () => {
    // Arrange.
    const { analysis } = analysisOver({
      complete: () => Promise.resolve(offered({ items: [{ label: "book", detail: "", info: "One book.", from: 1, to: 5, insert: "book", boost: 0 }] })),
    });

    // Act.
    const hover = await analysis.hover?.("<book/>", 3);

    // Assert.
    expect(hover).toEqual({ from: 1, to: 5, text: "One book." });
  });

  it("offers nothing when the call fails", async () => {
    // Arrange.
    const { analysis } = analysisOver({ complete: () => Promise.reject(new Error("unreachable")) });

    // Act and assert.
    await expect(analysis.completions?.("<", 1, true)).resolves.toBeNull();
  });
});
