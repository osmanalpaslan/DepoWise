import { pbkdf2, randomBytes, timingSafeEqual } from "node:crypto";

// PBKDF2-HMAC-SHA256 — .NET PasswordHasher ile AYNI biçim:
// pbkdf2$sha256$<iter>$<saltB64>$<hashB64>. Fonksiyonel parite.
const ITERATIONS = 100_000;
const SALT_SIZE = 16;
const HASH_SIZE = 32;
const DIGEST = "sha256";

function derive(password: string, salt: Buffer, iter: number, len: number): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    pbkdf2(password, salt, iter, len, DIGEST, (err, key) => (err ? reject(err) : resolve(key)));
  });
}

export async function hashPassword(password: string): Promise<string> {
  if (!password) throw new Error("Parola boş olamaz.");
  const salt = randomBytes(SALT_SIZE);
  const hash = await derive(password, salt, ITERATIONS, HASH_SIZE);
  return `pbkdf2$${DIGEST}$${ITERATIONS}$${salt.toString("base64")}$${hash.toString("base64")}`;
}

export async function verifyPassword(password: string, encoded: string): Promise<boolean> {
  if (!password || !encoded) return false;
  const parts = encoded.split("$");
  if (parts.length !== 5 || parts[0] !== "pbkdf2" || parts[1] !== "sha256") return false;
  const iter = Number(parts[2]);
  if (!Number.isInteger(iter) || iter < 1) return false;
  let salt: Buffer;
  let expected: Buffer;
  try {
    salt = Buffer.from(parts[3]!, "base64");
    expected = Buffer.from(parts[4]!, "base64");
  } catch {
    return false;
  }
  const actual = await derive(password, salt, iter, expected.length);
  return actual.length === expected.length && timingSafeEqual(actual, expected);
}
