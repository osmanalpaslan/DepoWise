// Sürüm/güncelleme mantığı — .NET SemVer/UpdateService/ReleaseService ile aynı kurallar.

export interface SemVer {
  major: number;
  minor: number;
  patch: number;
}

export function parseSemVer(s: string | null | undefined): SemVer | null {
  if (!s) return null;
  const parts = s.trim().replace(/^v/i, "").split(".");
  if (parts.length !== 3) return null;
  const a = Number(parts[0]);
  const b = Number(parts[1]);
  const c = Number(parts[2]);
  if (![a, b, c].every((n) => Number.isInteger(n) && n >= 0)) return null;
  return { major: a, minor: b, patch: c };
}

export function compareSemVer(a: SemVer, b: SemVer): number {
  if (a.major !== b.major) return Math.sign(a.major - b.major);
  if (a.minor !== b.minor) return Math.sign(a.minor - b.minor);
  return Math.sign(a.patch - b.patch);
}

export interface UpdatePackage {
  version: string;
  checksumSha256: string;
  minSupportedVersion: string;
  signed: boolean;
}

export interface UpdateCheckResult {
  updateAvailable: boolean;
  currentVersion: string;
  latestVersion: string | null;
  belowMinSupported: boolean;
  signedWarning: boolean;
}

export function checkUpdate(current: string, latest: UpdatePackage | null): UpdateCheckResult {
  const cv = parseSemVer(current);
  const lv = latest ? parseSemVer(latest.version) : null;
  if (!latest || !lv || !cv) {
    return { updateAvailable: false, currentVersion: current, latestVersion: latest?.version ?? null, belowMinSupported: false, signedWarning: false };
  }
  const minV = parseSemVer(latest.minSupportedVersion);
  return {
    updateAvailable: compareSemVer(lv, cv) > 0,
    currentVersion: current,
    latestVersion: latest.version,
    belowMinSupported: minV ? compareSemVer(cv, minV) < 0 : false,
    signedWarning: !latest.signed,
  };
}

// Geçerli SHA-256 checksum biçimi (64 hex). Bozuk paket kurulmaz → indirme sonrası içerik hash'i bununla doğrulanır.
export const isValidChecksum = (sha: string | null | undefined): boolean =>
  !!sha && sha.length === 64 && /^[0-9a-fA-F]+$/.test(sha);

export const verifyChecksum = (actualSha: string, expectedSha: string): boolean =>
  actualSha.toLowerCase() === expectedSha.toLowerCase();
