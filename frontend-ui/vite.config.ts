import { defineConfig } from "vite";

export default defineConfig({
  server: {
    port: Number(process.env.PORT) || 5173,
    host: true,
    proxy: {
      "/api": {
        target: process.env.DUCKDB_API_URL || "http://localhost:8002",
        changeOrigin: true
      }
    }
  }
});
