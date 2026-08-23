import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// ASP.NET Core proxies frontend requests to this dev server during the F5
// workflow; production instead serves this project's `vite build` output
// directly (see tech.md's Development tools section).
export default defineConfig({
  plugins: [react()],
  // Must match appsettings.developer.json's Client:DevServerUrl; strictPort so a
  // port conflict fails loudly instead of silently drifting (and breaking the proxy).
  server: {
    port: 5174,
    strictPort: true,
  },
});
