import { describe, expect, it } from "vitest";
import { buildCarryOver, type CarrySource } from "./rollover";
import { normalizeName, rankMatches, similarity, suggestMatch } from "./billMatch";
import { billDifference, monthSpend, sumMoney } from "./money";
import { periodOf, shiftPeriod } from "./period";

const src = (name: string, over: Partial<CarrySource> = {}): CarrySource => ({
  name,
  qty: 1,
  unit: null,
  notes: null,
  included: true,
  position: 0,
  ...over,
});

describe("buildCarryOver", () => {
  it("copies last month's items, keeps quantities, clears prices and ticks", () => {
    const out = buildCarryOver([src("Rice", { qty: 5, unit: "kg", position: 2 }), src("Milk", { position: 1 })]);
    expect(out.map((i) => i.name)).toEqual(["Milk", "Rice"]);
    expect(out[1]).toMatchObject({ qty: 5, unit: "kg", included: true, bought: false, price: null, source: "carried" });
    expect(out.map((i) => i.position)).toEqual([1, 2]);
  });

  it("carries items that were added from the bill", () => {
    const out = buildCarryOver([src("Milk", { position: 1 }), src("Bananas", { position: 9 })]);
    expect(out.map((i) => i.name)).toContain("Bananas");
  });

  it("keeps skipped items but flags them and lists them last", () => {
    const out = buildCarryOver([src("Olive oil", { included: false, position: 1 }), src("Eggs", { position: 2 })]);
    expect(out.map((i) => [i.name, i.skipped_last])).toEqual([
      ["Eggs", false],
      ["Olive oil", true],
    ]);
    expect(out.every((i) => i.included)).toBe(true);
  });

  it("collapses duplicates (plural / case / size differences)", () => {
    const out = buildCarryOver([
      src("Tomatoes", { included: false, position: 1 }),
      src("tomato", { position: 2 }),
      src("Milk 1L", { position: 3 }),
      src("milk", { position: 4 }),
    ]);
    expect(out.map((i) => i.name)).toEqual(["Tomatoes", "Milk 1L"]);
    expect(out[0].skipped_last).toBe(false); // needed by one of the duplicates
  });

  it("ignores blank names and starts empty when there is no history", () => {
    expect(buildCarryOver([])).toEqual([]);
    expect(buildCarryOver([src("   ")])).toEqual([]);
  });
});

describe("bill matching", () => {
  const items = [
    { id: "1", name: "Milk" },
    { id: "2", name: "Bananas" },
    { id: "3", name: "Basmati rice" },
    { id: "4", name: "Dish soap" },
  ];

  it("normalises sizes, plurals and punctuation", () => {
    expect(normalizeName("Bananas 1kg")).toBe("banana");
    expect(normalizeName("Toned-Milk 500ml")).toBe("milk toned");
  });

  it("matches cleaned-up bill names to list items", () => {
    expect(suggestMatch("Toned milk 1L", items)?.id).toBe("1");
    expect(suggestMatch("Banana", items)?.id).toBe("2");
    expect(suggestMatch("India Gate basmati rice 5kg", items)?.id).toBe("3");
  });

  it("does not force a match for unrelated products", () => {
    expect(suggestMatch("Chocolate cookies", items)).toBeNull();
    expect(similarity("Rice", "Dish soap")).toBe(0);
  });

  it("ranks the best candidates first", () => {
    expect(rankMatches("Liquid dish soap", items)[0].id).toBe("4");
  });
});

describe("money", () => {
  it("sums without floating point drift", () => {
    expect(sumMoney([0.1, 0.2, "0.3", null])).toBe(0.6);
  });

  it("prefers confirmed bills, then a manual total, then item prices", () => {
    expect(
      monthSpend({ bills: [{ status: "confirmed", total: "40.10" }, { status: "parsed", total: 99 }], manualTotal: 5, itemPrices: [1] }),
    ).toEqual({ total: 40.1, source: "bills" });
    expect(monthSpend({ bills: [], manualTotal: "120", itemPrices: [1] })).toEqual({ total: 120, source: "manual" });
    expect(monthSpend({ bills: [], manualTotal: null, itemPrices: [1.5, null, "2"] })).toEqual({ total: 3.5, source: "items" });
    expect(monthSpend({ bills: [], manualTotal: null, itemPrices: [] })).toEqual({ total: null, source: "none" });
  });

  it("reports how far the bill lines are from the printed total", () => {
    const lines = [
      { line_total: 3.5, action: "match" },
      { line_total: 1.2, action: "add" },
    ];
    expect(billDifference(lines, { total: 4.2, discount: 0.5 })).toBe(0);
    expect(billDifference(lines, { total: 5, discount: null })).toBe(0.3);
    expect(billDifference(lines, { total: null, discount: null })).toBeNull();
  });
});

describe("periods", () => {
  it("formats and shifts months across year ends", () => {
    expect(periodOf(new Date(2026, 10, 22))).toBe("2026-11");
    expect(shiftPeriod("2026-12", 1)).toBe("2027-01");
    expect(shiftPeriod("2026-01", -1)).toBe("2025-12");
  });
});
