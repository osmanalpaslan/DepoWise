// Para — .NET Money ile aynı kurallar. Varsayılan TRY; USD/EUR desteklenir. Float ile değil
// string/decimal-temsille taşınır (sınırda doğrulama). İşlem anı kur snapshot ayrı saklanır.
export const BASE_CURRENCY = "TRY";
const ALLOWED = new Set(["TRY", "USD", "EUR"]);

export const isSupportedCurrency = (c: string | null | undefined): boolean =>
  !!c && ALLOWED.has(c);

// Invariant ondalık metin (nokta ayraç). Geçersizse 0.
export function parseAmount(text: string | null | undefined): number {
  if (text === null || text === undefined || text.trim() === "") return 0;
  const v = Number(text);
  return Number.isFinite(v) ? v : 0;
}

export const serializeAmount = (value: number): string => String(value);
