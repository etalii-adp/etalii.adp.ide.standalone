import { fileURLToPath, URL } from "node:url";
import { defineConfig, searchForWorkspaceRoot } from "vite";
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
      // `src/shell/panels/toolPanelRegistration.ts` for what that surface is meant to cover.
      "@client": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  // Must match appsettings.developer.json's Client:DevServerUrl; strictPort so a
  // port conflict fails loudly instead of silently drifting (and breaking the proxy).
  server: {
    port: 5174,
    strictPort: true,
    fs: {
      // The npm workspace root (`src/`), which covers all three places the dev server has to
      // read from: this project, the diagram modules beside it, and `src/node_modules`, where
      // the workspace hoists dependencies shared between them - `@mdi/font` among them.
      //
      // This is what Vite allows by default when `fs.allow` is not set. Setting it replaces
      // that default rather than adding to it, so listing only this project and the diagram
      // modules silently dropped the hoisted node_modules and every icon 403'd.
      allow: [searchForWorkspaceRoot(fileURLToPath(new URL(".", import.meta.url)))],
    },
  },
});
