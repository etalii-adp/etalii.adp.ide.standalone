import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// ASP.NET Core proxies frontend requests to this dev server during the F5
// workflow; production instead serves this project's `vite build` output
// directly (see tech.md's Development tools section).
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // Diagram modules live outside this project, under `src/diagrams/<type>/client/`, so a
      // relative import back into the shell would be a stack of `../`s that changes whenever
      // a file moves. `@client` is the shell surface a module may reach into - see
      // `src/shell/panels/diagramCanvas.ts` for what that surface is meant to cover.
      "@client": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  // Must match appsettings.developer.json's Client:DevServerUrl; strictPort so a
  // port conflict fails loudly instead of silently drifting (and breaking the proxy).
  server: {
    port: 5174,
    strictPort: true,
    fs: {
      // The dev server has to be allowed to read the diagram modules, which sit beside this
      // project rather than inside it.
      allow: [fileURLToPath(new URL(".", import.meta.url)), fileURLToPath(new URL("../diagrams", import.meta.url))],
    },
  },
});
