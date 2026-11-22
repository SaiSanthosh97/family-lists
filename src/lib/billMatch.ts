// Name normalising and fuzzy matching between bill lines and list items.
// Claude already suggests matches when it reads a bill; this ranks the choices
// in the review screen and catches lines it left unmatched.

const SIZE = /\b\d+(?:[.,]\d+)?\s*(?:kg|g|gm|gms|mg|l|ltr|lt|ml|cl|oz|lb|lbs|pcs|pc|pk|x)\b/g;
const STOP = new Set(["the", "a", "an", "of", "and", "with", "fresh", "pack", "organic", "pkt"]);

function singular(word: string): string {
  if (word.length > 4 && word.endsWith("ies")) return word.slice(0, -3) + "y";
  if (word.length > 4 && word.endsWith("oes")) return word.slice(0, -2);
  if (word.length > 3 && word.endsWith("s") && !word.endsWith("ss")) return word.slice(0, -1);
  return word;
}

export function tokens(name: string): string[] {
  return name
    .toLowerCase()
    .replace(SIZE, " ")
    .replace(/[^\p{L}\p{N}\s]/gu, " ")
    .split(/\s+/)
    .filter((w) => w && !STOP.has(w) && !/^\d+$/.test(w))
    .map(singular);
}

export function normalizeName(name: string): string {
  return tokens(name).sort().join(" ");
}

/** 0..1: how likely two product names describe the same thing. */
export function similarity(a: string, b: string): number {
  const ta = new Set(tokens(a));
  const tb = new Set(tokens(b));
  if (!ta.size || !tb.size) return 0;
  let shared = 0;
  for (const t of ta) if (tb.has(t)) shared++;
  if (shared === 0) {
    for (const x of ta) {
      for (const y of tb) {
        if (x.length >= 4 && y.length >= 4 && (x.startsWith(y) || y.startsWith(x))) return 0.5;
      }
    }
    return 0;
  }
  const smaller = Math.min(ta.size, tb.size);
  const union = new Set([...ta, ...tb]).size;
  // Containment matters more than overlap: "Milk" is contained in "Toned milk".
  return 0.7 * (shared / smaller) + 0.3 * (shared / union);
}

export interface Matchable {
  id: string;
  name: string;
}

/** Best list item for a bill line, or null when nothing is close enough. */
export function suggestMatch<T extends Matchable>(lineName: string, items: T[], threshold = 0.6): T | null {
  let best: T | null = null;
  let bestScore = 0;
  for (const item of items) {
    const s = similarity(lineName, item.name);
    if (s > bestScore) {
      best = item;
      bestScore = s;
    }
  }
  return bestScore >= threshold ? best : null;
}

/** Items ordered by how well they match a bill line (for the picker). */
export function rankMatches<T extends Matchable>(lineName: string, items: T[]): T[] {
  return [...items]
    .map((item) => ({ item, s: similarity(lineName, item.name) }))
    .sort((a, b) => b.s - a.s || a.item.name.localeCompare(b.item.name))
    .map((x) => x.item);
}
