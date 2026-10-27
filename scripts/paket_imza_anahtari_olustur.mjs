// ⭐ M-01 (2026-10-27) — GÜNCELLEME PAKETİ İMZA ANAHTARI ÜRETİMİ (bir kez çalıştırılır).
//
// ECDSA P-256 anahtar çifti üretir:
//   • GİZLİ anahtar → ~/.claude-yedek/anahtarlar/DepoWise_paket_imza_ozel.pem (özel yedek deposu; ekrana YAZILMAZ).
//     Varsa ÜZERİNE YAZILMAZ (yanlışlıkla çalıştırmak tüm masaüstlerinin güncellemesini kilitlerdi).
//   • AÇIK anahtar (SPKI, base64) → ekrana yazılır; src/DepoWise.Application/Update/PaketImzasi.cs içindeki
//     Anahtarlar listesine eklenir. Anahtar değişimi: yeni açık anahtar listeye EKLENİR (eskisi bir sürüm daha kalır).
//
// Kullanım: node scripts/paket_imza_anahtari_olustur.mjs [hedefDosya]
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";

const hedef = process.argv[2] || path.join(os.homedir(), ".claude-yedek", "anahtarlar", "DepoWise_paket_imza_ozel.pem");
if (fs.existsSync(hedef)) {
  console.error("HATA: anahtar zaten var, üzerine yazılmaz: " + hedef);
  process.exit(1);
}
const { privateKey, publicKey } = crypto.generateKeyPairSync("ec", { namedCurve: "P-256" });
fs.mkdirSync(path.dirname(hedef), { recursive: true });
fs.writeFileSync(hedef, privateKey.export({ type: "pkcs8", format: "pem" }), { mode: 0o600 });
const acik = publicKey.export({ type: "spki", format: "der" }).toString("base64");
console.log("Gizli anahtar yazıldı: " + hedef);
console.log("AÇIK ANAHTAR (PaketImzasi.Anahtarlar'a ekle):");
console.log(acik);
