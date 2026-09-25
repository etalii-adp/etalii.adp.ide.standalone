// A suite that exists to FAIL, driven only by ClientTestRun.FailureMessage.Tests.cs.
//
// Its own config, named explicitly by that test, so that vitest never walks up looking for another
// one, and nothing here is picked up by the client's own run: that run includes only src/client and
// the diagram modules. The cache goes to the temp folder, so a run leaves nothing in this tree.
import { tmpdir } from "node:os";
import { join } from "node:path";

export default {
  cacheDir: join(tmpdir(), "adp-client-failing-on-purpose-vite"),
  test: {
    environment: "node",
    include: ["*.test.mjs"],
  },
};
