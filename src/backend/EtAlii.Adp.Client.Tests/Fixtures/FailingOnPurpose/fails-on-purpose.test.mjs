// Every test here FAILS, on purpose. ClientTestRun.FailureMessage.Tests.cs runs this file and
// asserts that each failure's own message reaches the .NET output.
import { expect, test } from "vitest";

// vitest's timeout error is the one that lost its message: reported as STACK_TRACE_ERROR alone.
// The promise never settles, so only the timeout can end the test, however loaded the machine is.
test("times out on purpose", async () => {
  await new Promise(() => {});
}, 50);

// The ordinary case, kept as a control: an assertion's message was never lost, and must not be now.
test("fails an assertion on purpose", () => {
  expect(1).toBe(2);
});
