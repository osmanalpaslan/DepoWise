// Malzeme içe aktarım doğrulaması — .NET MaterialImportService ile aynı kurallar.
// Örnek başlık + ön kontrol + satır bazlı hata + dry-run (yazmadan doğrula).

export const SAMPLE_HEADERS = ["Kod", "Ad", "Tür", "Min Stok", "Birim Fiyat", "Para Birimi"] as const;
const SUPPORTED_CURRENCIES = new Set(["TRY", "USD", "EUR"]);
export const MAX_REPORTED_ERRORS = 15;

export interface ImportRow {
  rowNumber: number;
  values: Record<string, string | null | undefined>;
}
export interface ImportRowError {
  rowNumber: number;
  message: string;
}
export interface ImportResult {
  dryRun: boolean;
  total: number;
  valid: number;
  failed: number;
  errors: ImportRowError[];
}

export function validateRow(row: ImportRow): string | null {
  const code = row.values["Kod"]?.trim();
  const name = row.values["Ad"]?.trim();
  if (!code) return "Kod zorunlu.";
  if (!name) return "Ad zorunlu.";
  const price = row.values["Birim Fiyat"];
  if (price && price.trim() !== "" && !Number.isFinite(Number(price))) return "Birim Fiyat sayısal olmalı.";
  const cur = row.values["Para Birimi"]?.trim();
  if (cur && !SUPPORTED_CURRENCIES.has(cur)) return `Desteklenmeyen para birimi: ${cur}`;
  return null;
}

// Dry-run: hiçbir şey yazmaz, yalnız doğrular.
export function dryRun(rows: ImportRow[]): ImportResult {
  const errors: ImportRowError[] = [];
  let valid = 0;
  for (const row of rows) {
    const err = validateRow(row);
    if (err === null) valid++;
    else if (errors.length < MAX_REPORTED_ERRORS) errors.push({ rowNumber: row.rowNumber, message: err });
  }
  return { dryRun: true, total: rows.length, valid, failed: rows.length - valid, errors };
}
