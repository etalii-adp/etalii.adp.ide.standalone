import { fileURLToPath, URL } from "node:url";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  resolve: {
    // Kept in step with vite.config.ts: a module's tests import through the same alias its
    // sources do, so a broken alias fails here rather than only in a browser.
    alias: {
      "@client": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    environment: "jsdom",
    passWithNoTests: true,
    // The diagram and designer modules' own tests live with the modules, outside this project.
    include: [
      "src/**/*.{test,spec}.{ts,tsx}",
      "../diagrams/*/client/**/*.{test,spec}.{ts,tsx}",
      "../designers/*/client/**/*.{test,spec}.{ts,tsx}",
    ],
    setupFiles: ["./src/test-setup.ts"],
  },
});
