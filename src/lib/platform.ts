import { Capacitor } from "@capacitor/core";

/**
 * True inside the Android app (APK). There, Google blocks its sign-in page in
 * embedded browsers, and email links open Chrome rather than the app, so
 * sign-in uses the code from the email instead.
 */
export const isNativeApp = Capacitor.isNativePlatform();
