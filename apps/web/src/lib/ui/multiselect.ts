// Aranabilir çoklu seçim — .NET MultiSelectState ile fonksiyonel eşit.
// Kurallar: arama seçimi kaybetmez; "tümünü seç" yalnız filtreyi ekler; Türkçe duyarsız.

const trLower = (s: string): string => s.toLocaleLowerCase("tr");

export class MultiSelectState<T> {
  private readonly all: T[];
  private readonly textOf: (item: T) => string;
  private readonly selected = new Set<T>();
  query = "";

  constructor(all: T[], textOf: (item: T) => string, initialSelected?: Iterable<T>) {
    this.all = [...all];
    this.textOf = textOf;
    if (initialSelected) for (const s of initialSelected) this.selected.add(s);
  }

  search(query: string | null | undefined): void {
    this.query = (query ?? "").trim();
  }

  filtered(): T[] {
    if (this.query.length === 0) return this.all;
    const q = trLower(this.query);
    return this.all.filter((x) => trLower(this.textOf(x)).includes(q));
  }

  isSelected(item: T): boolean {
    return this.selected.has(item);
  }
  get selectedCount(): number {
    return this.selected.size;
  }

  toggle(item: T, on: boolean): void {
    if (on) this.selected.add(item);
    else this.selected.delete(item);
  }

  selectAllFiltered(): void {
    for (const x of this.filtered()) this.selected.add(x);
  }

  clearFiltered(): void {
    for (const x of this.filtered()) this.selected.delete(x);
  }
}
