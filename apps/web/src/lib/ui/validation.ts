// GG/AA/YYYY tarih + numerik doğrulama — .NET DateInput/NumericInput ile aynı kurallar.

export interface ValidationResult {
  ok: boolean;
  error?: string;
}
const ok: ValidationResult = { ok: true };
const fail = (error: string): ValidationResult => ({ ok: false, error });

// Yalnız maske değil GERÇEK takvim (31/02, 13. ay, artık-yıl-dışı 29/02 reddedilir).
export function parseDate(text: string | null | undefined): Date | null {
  if (!text) return null;
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(text.trim());
  if (!m) return null;
  const day = Number(m[1]);
  const month = Number(m[2]);
  const year = Number(m[3]);
  if (year < 1900 || year > 2200 || month < 1 || month > 12 || day < 1) return null;
  const d = new Date(Date.UTC(year, month - 1, day));
  // Geri doğrulama: bileşenler korunmadıysa geçersiz (örn. 31/02 → 03/03'e taşar)
  if (d.getUTCFullYear() !== year || d.getUTCMonth() !== month - 1 || d.getUTCDate() !== day) {
    return null;
  }
  return d;
}

export function validateDate(text: string | null | undefined): ValidationResult {
  return parseDate(text) ? ok : fail("Geçersiz tarih (GG/AA/YYYY, gerçek takvim).");
}

export function validateNumeric(
  value: number | null | undefined,
  opts: { min?: number; max?: number; allowNegative?: boolean } = {},
): ValidationResult {
  if (value === null || value === undefined || Number.isNaN(value)) return fail("Değer zorunlu.");
  if (!opts.allowNegative && value < 0) return fail("Negatif değer kabul edilmez.");
  if (opts.min !== undefined && value < opts.min) return fail(`En küçük değer ${opts.min}.`);
  if (opts.max !== undefined && value > opts.max) return fail(`En büyük değer ${opts.max}.`);
  return ok;
}
