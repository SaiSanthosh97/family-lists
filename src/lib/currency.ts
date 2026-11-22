export const CURRENCIES = [
  "USD", "INR", "EUR", "GBP", "CAD", "AUD", "NZD", "SGD", "AED", "SAR", "QAR",
  "JPY", "CNY", "HKD", "MYR", "ZAR", "CHF", "SEK", "NOK", "DKK", "BRL", "MXN",
];

const BY_REGION: Record<string, string> = {
  US: "USD", IN: "INR", GB: "GBP", CA: "CAD", AU: "AUD", NZ: "NZD", SG: "SGD", AE: "AED",
  SA: "SAR", QA: "QAR", JP: "JPY", CN: "CNY", HK: "HKD", MY: "MYR", ZA: "ZAR", CH: "CHF",
  SE: "SEK", NO: "NOK", DK: "DKK", BR: "BRL", MX: "MXN",
  DE: "EUR", FR: "EUR", ES: "EUR", IT: "EUR", NL: "EUR", IE: "EUR", BE: "EUR", AT: "EUR", PT: "EUR", FI: "EUR",
};

/** Best guess at the user's currency from their browser language, e.g. "en-IN" → INR. */
export function guessCurrency(): string {
  const region = (navigator.language || "").split("-")[1]?.toUpperCase();
  return (region && BY_REGION[region]) || "USD";
}

const LAST_GROUP = "last-group";

export function rememberGroup(id: string) {
  try {
    localStorage.setItem(LAST_GROUP, id);
  } catch {
    /* storage unavailable */
  }
}

export function lastGroup(): string | null {
  try {
    return localStorage.getItem(LAST_GROUP);
  } catch {
    return null;
  }
}
