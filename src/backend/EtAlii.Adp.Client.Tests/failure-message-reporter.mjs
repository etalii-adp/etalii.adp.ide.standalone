// vitest's own JSON reporter, with one thing put back: the failure's MESSAGE.
//
// The JSON reporter writes each failure as `error.stack || error.message`, trusting that a stack
// begins with its message, as a thrown Error's does. vitest's own timeout error does not. In
// @vitest/runner 4.1.11 `makeTimeoutError` builds "Test timed out in <n>ms." and then takes its
// stack from an Error captured when the test was DECLARED, with a `replace` whose arguments are the
// wrong way round, so the text it was meant to swap in never lands. What survives is
// `Error: STACK_TRACE_ERROR` pointing at the test's declaration: that is what a gate log showed on
// 2026-09-25, for a test that had in fact timed out, with nothing in it to say so.
//
// So before the report is written, any error whose stack does not carry its message gets the
// message put in front. The stack is kept after it, because it still names the test's line. An
// error whose stack already carries its message is left exactly as it was.
//
// WHEN THIS CAN GO: the bug is in @vitest/runner 4.1.11 (vitest 4.1.11), dist/chunk-artifact.js,
// `error.stack = stackTraceError.stack.replace(error.message, stackTraceError.message)`. The same
// file does it the right way round about 250 lines later. After upgrading vitest, look at that line:
// once it reads `.replace(stackTraceError.message, error.message)`, this reporter is a no-op and
// ClientTestRun can go back to `--reporter=json`. ClientTestRun.FailureMessage.Tests.cs stays either
// way, and says whether the upgrade really fixed it.
import { JsonReporter } from "vitest/reporters";

export default class FailureMessageReporter extends JsonReporter {
  async onTestRunEnd(testModules, ...rest) {
    for (const testModule of testModules) {
      restoreMessages(testModule.task);
    }

    return super.onTestRunEnd(testModules, ...rest);
  }
}

function restoreMessages(task) {
  for (const error of task.result?.errors ?? []) {
    if (error.message && error.stack && !error.stack.includes(error.message)) {
      error.stack = `${error.name ?? "Error"}: ${error.message}\n${error.stack}`;
    }
  }

  for (const child of task.tasks ?? []) {
    restoreMessages(child);
  }
}
