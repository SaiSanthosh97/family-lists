/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";

export default defineConfig(({ mode }) => ({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      // The Android app ships its files inside the APK, so it needs no offline
      // cache — and a service worker there could keep serving an old version.
      disable: mode === "android",
      registerType: "autoUpdate",
      includeAssets: ["icon.svg"],
      manifest: {
        name: "Family Lists",
        short_name: "Lists",
        description: "Shared monthly grocery lists, to-dos and spending for your household.",
        theme_color: "#15803d",
        background_color: "#f7f7f4",
        display: "standalone",
        start_url: "/",
        icons: [
          { src: "icon-192.png", sizes: "192x192", type: "image/png" },
          { src: "icon-512.png", sizes: "512x512", type: "image/png" },
          { src: "icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
        ],
      },
      workbox: {
        navigateFallback: "/index.html",
        // Supabase API calls must always hit the network.
        navigateFallbackDenylist: [/^\/auth/],
      },
    }),
  ],
  test: {
    include: ["src/**/*.test.ts"],
  },
}));
