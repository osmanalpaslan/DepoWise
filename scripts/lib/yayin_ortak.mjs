// ⭐ M-01 (2026-10-27) — yayın betiklerinin ortak parçaları: süper admin girişi (iki adımlı giriş kodu dahil) + paket imzası.
//
// Ortam değişkenleri:
//   DEPOWISE_ADMIN_USER / DEPOWISE_ADMIN_PASS  — süper admin
//   DEPOWISE_ADMIN_TOTP   — (isteğe bağlı) doğrulama uygulaması gizli anahtarı (Base32); varsa kod kendiliğinden üretilir
//   DEPOWISE_ADMIN_KOD    — (isteğe bağlı) elle verilen 6 haneli kod / kurtarma kodu
//   DEPOWISE_IMZA_ANAHTARI — (isteğe bağlı) gizli imza anahtarı dosyası; varsayılan ~/.claude-yedek/anahtarlar/DepoWise_paket_imza_ozel.pem
// Sırlar ekrana YAZILMAZ.
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";

/** C# PaketImzasi.ImzaMetni ile AYNI metin (sürüm + büyük harf özet). */
export function imzaMetni(surum, sha256Hex) {
  return `alpnex-paket-v1\n${String(surum).trim()}\n${String(sha256Hex).trim().toUpperCase()}`;
}

/** C# PaketImzasi.KurulumAraciEtiketi */
export const KURULUM_ARACI_ETIKETI = "kurulum-araci";

export function imzala(surum, sha256Hex) {
  const yol = process.env.DEPOWISE_IMZA_ANAHTARI
    || path.join(os.homedir(), ".claude-yedek", "anahtarlar", "DepoWise_paket_imza_ozel.pem");
  if (!fs.existsSync(yol)) throw new Error("Gizli imza anahtarı bulunamadı: " + yol);
  const anahtar = crypto.createPrivateKey(fs.readFileSync(yol));
  return crypto.sign("sha256", Buffer.from(imzaMetni(surum, sha256Hex), "utf8"), { key: anahtar, dsaEncoding: "der" }).toString("base64");
}

const B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
function base32Coz(metin) {
  let tampon = 0, bit = 0; const cikti = [];
  for (const c of metin.replace(/[\s=-]/g, "").toUpperCase()) {
    const i = B32.indexOf(c); if (i < 0) throw new Error("Geçersiz Base32");
    tampon = (tampon << 5) | i; bit += 5;
    if (bit >= 8) { cikti.push((tampon >> (bit - 8)) & 0xff); bit -= 8; }
  }
  return Buffer.from(cikti);
}

/** RFC 6238 TOTP (SHA1, 6 hane, 30 sn) — C# Totp.Uret ile aynı. */
export function totp(gizliBase32, an = Date.now()) {
  let adim = BigInt(Math.floor(an / 1000 / 30));
  const sayac = Buffer.alloc(8);
  for (let i = 7; i >= 0; i--) { sayac[i] = Number(adim & 0xffn); adim >>= 8n; }
  const h = crypto.createHmac("sha1", base32Coz(gizliBase32)).update(sayac).digest();
  const o = h[h.length - 1] & 0x0f;
  const ikili = ((h[o] & 0x7f) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
  return String(ikili % 1000000).padStart(6, "0");
}

/** Süper admin girişi; hesapta iki adımlı giriş açıksa kodu ortamdan üretir/alır. Jetonlu giriş yanıtını döndürür. */
export async function superAdminGiris(API) {
  const username = process.env.DEPOWISE_ADMIN_USER, password = process.env.DEPOWISE_ADMIN_PASS;
  if (!username || !password) throw new Error("DEPOWISE_ADMIN_USER / DEPOWISE_ADMIN_PASS ortam degiskeni yok.");
  const gonder = (totpCode) => fetch(`${API}/api/auth/login`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password, totpCode }),
  });
  let r = await gonder(undefined);
  if (r.status === 401 || r.status === 429) {
    const govde = await r.json().catch(() => ({}));
    if (!govde.mfaRequired) throw new Error(`login ${r.status} - ${govde.error ?? ""}`);
    const kod = process.env.DEPOWISE_ADMIN_KOD || (process.env.DEPOWISE_ADMIN_TOTP ? totp(process.env.DEPOWISE_ADMIN_TOTP) : null);
    if (!kod) throw new Error("Hesapta iki adımlı giriş açık: DEPOWISE_ADMIN_TOTP (gizli anahtar) ya da DEPOWISE_ADMIN_KOD (6 haneli kod) gerekli.");
    r = await gonder(kod);
  }
  if (!r.ok) throw new Error(`login ${r.status} - ${await r.text()}`);
  const login = await r.json();
  if (!login.token) throw new Error("token alinamadi");
  if (!login.isSuperAdmin) throw new Error("bu kullanici Super Admin degil; yayin yetkisi yok.");
  return login;
}
