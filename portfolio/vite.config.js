import { defineConfig } from "vite";

const normalizeTrailingSlash = (value) => `${value.replace(/\/+$/, "")}/`;
const basePath = normalizeTrailingSlash(process.env.VITE_BASE_PATH || "/f1/");
const siteUrl = normalizeTrailingSlash(
  process.env.VITE_SITE_URL || "https://yundonggeurami.github.io/f1/",
);

export default defineConfig({
  base: basePath,
  plugins: [
    {
      name: "mlgp-html-environment",
      transformIndexHtml(html) {
        return html.replaceAll("__SITE_URL__", siteUrl);
      },
    },
  ],
  build: {
    outDir: "dist",
    emptyOutDir: true,
    sourcemap: false,
  },
  server: {
    host: "127.0.0.1",
    port: 4173,
  },
  preview: {
    host: "127.0.0.1",
    port: 4173,
  },
});
