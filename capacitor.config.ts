import type { CapacitorConfig } from "@capacitor/cli";

// Wraps the built web app (dist/) into the Android app.
const config: CapacitorConfig = {
  appId: "com.familylists.app",
  appName: "Family Lists",
  webDir: "dist",
  android: {
    // Matches the app's background so there's no white flash on start.
    backgroundColor: "#f7f7f4",
  },
};

export default config;
