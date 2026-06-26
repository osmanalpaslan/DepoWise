// Dosya/fotoğraf doğrulama — .NET FileValidation ile aynı kurallar.
// Boyut ≤7MB, izinli MIME, magic-byte eşleşmesi (sahte içerik reddi), güvenli dosya adı.

export const MAX_BYTES = 7 * 1024 * 1024;
const ALLOWED_MIMES = new Set(["image/jpeg", "image/png"]);

export interface FileValidationResult {
  ok: boolean;
  error?: string;
  detectedMime?: string;
  detectedExt?: string;
}

export function detectImage(bytes: Uint8Array): { mime: string; ext: string } | null {
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) {
    return { mime: "image/jpeg", ext: "jpg" };
  }
  if (
    bytes.length >= 8 &&
    bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47 &&
    bytes[4] === 0x0d && bytes[5] === 0x0a && bytes[6] === 0x1a && bytes[7] === 0x0a
  ) {
    return { mime: "image/png", ext: "png" };
  }
  return null;
}

export function validateImage(
  fileName: string | null | undefined,
  declaredMime: string | null | undefined,
  bytes: Uint8Array,
): FileValidationResult {
  if (!bytes || bytes.length === 0) return { ok: false, error: "Boş dosya." };
  if (bytes.length > MAX_BYTES) return { ok: false, error: "Dosya 7 MB sınırını aşıyor." };
  const detected = detectImage(bytes);
  if (!detected) return { ok: false, error: "Geçersiz veya sahte görsel (magic-byte eşleşmedi)." };
  if (declaredMime && !ALLOWED_MIMES.has(declaredMime)) return { ok: false, error: `İzin verilmeyen MIME: ${declaredMime}` };
  if (declaredMime && declaredMime.toLowerCase() !== detected.mime) {
    return { ok: false, error: "Bildirilen MIME içerikle uyuşmuyor." };
  }
  return { ok: true, detectedMime: detected.mime, detectedExt: detected.ext };
}

export function safeFileName(original: string | null | undefined, detectedExt: string): string {
  const dot = (original ?? "dosya").lastIndexOf(".");
  const base = dot > 0 ? (original ?? "").slice(0, dot) : (original ?? "dosya");
  let clean = base.replace(/[^a-zA-Z0-9_-]/g, "");
  if (!clean) clean = "dosya";
  if (clean.length > 64) clean = clean.slice(0, 64);
  return `${clean}.${detectedExt}`;
}
