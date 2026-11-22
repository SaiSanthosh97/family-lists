// A "period" is one calendar month, written "YYYY-MM".

export function periodOf(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}`;
}

export function shiftPeriod(period: string, months: number): string {
  const [y, m] = period.split("-").map(Number);
  return periodOf(new Date(y, m - 1 + months, 1));
}

export function periodLabel(period: string, style: "long" | "short" = "long"): string {
  const [y, m] = period.split("-").map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString(undefined, {
    month: style === "long" ? "long" : "short",
    // Always the full year: "Nov 26" reads like a date (26 November).
    year: "numeric",
  });
}

export function monthName(period: string): string {
  const [y, m] = period.split("-").map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString(undefined, { month: "long" });
}

/** Today's date. In development, `?today=2026-12-01` pretends it is another day. */
export function appToday(): Date {
  if (import.meta.env?.DEV && typeof window !== "undefined") {
    const override = new URLSearchParams(window.location.search).get("today");
    if (override && /^\d{4}-\d{2}-\d{2}$/.test(override)) safeSet("today-override", override);
    const saved = override ?? safeGet("today-override");
    if (saved && /^\d{4}-\d{2}-\d{2}$/.test(saved)) {
      const [y, m, d] = saved.split("-").map(Number);
      return new Date(y, m - 1, d);
    }
  }
  return new Date();
}

export function currentPeriod(): string {
  return periodOf(appToday());
}

function safeGet(key: string): string | null {
  try {
    return sessionStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeSet(key: string, value: string) {
  try {
    sessionStorage.setItem(key, value);
  } catch {
    /* storage unavailable */
  }
}
