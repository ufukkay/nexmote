import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../src/NexMote.Api/wwwroot",
    emptyOutDir: true
  },
  server: {
    host: "0.0.0.0",
    port: 5173,
    proxy: {
      "/api": "http://127.0.0.1:5080",
      "/health": "http://127.0.0.1:5080",
      "/downloads": "http://127.0.0.1:5080",
      "/hubs": {
        target: "http://127.0.0.1:5080",
        ws: true
      }
    }
  }
});
