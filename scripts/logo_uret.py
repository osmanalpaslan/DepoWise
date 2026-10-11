# DepoWise / Alpnex — yeni logodan tüm simge ve logo dosyalarını üretir (2026-10-11, kullanıcı isteği).
# Kullanım: uv run --with pillow --with numpy python -I scripts/logo_uret.py "<Yeni logo klasörü>" "<çıktı klasörü>"
#  - Kaynak 1 "masaüstü-setup logo.jpg": lacivert arka planlı işaret + AlpNeX yazısı → kurulum dosyası + masaüstü KISAYOLU.
#  - Kaynak 2 "Görev çubuğu logo.jpg": işaret, arkada ÇİZİLMİŞ dama deseni (gerçek şeffaflık YOK) → kroma + parlaklık
#    anahtarıyla dama temizlenir → ŞEFFAF işaret: exe/görev çubuğu/pencere simgesi, uygulama içi logo, web favicon.
# Kalite (2026-10-11 kullanıcı: "uygulama içinde logolar keskin ve pürüzlü"): (1) yarı saydam kenar piksellerinin rengi
# dama kalıntısıydı (koyu) → en yakın opak logo rengiyle değiştirilir (renk sızdırma); (2) tüm küçültmeler
# ÖN-ÇARPILMIŞ alfa (RGBa) ile yapılır → koyu hale/tırtık oluşmaz; (3) her kullanım boyutu için ayrı dosya.
import sys, os
import numpy as np
from PIL import Image, ImageFilter

kaynak_dir, cikti = sys.argv[1], sys.argv[2]
os.makedirs(cikti, exist_ok=True)
setup = Image.open(os.path.join(kaynak_dir, "masaüstü-setup logo.jpg")).convert("RGB")
gorev = Image.open(os.path.join(kaynak_dir, "Görev çubuğu logo.jpg")).convert("RGB")

def kucult(im, n):
    """Ön-çarpılmış alfa ile yüksek kaliteli küçültme (şeffaf kenarlarda koyu hale olmaz)."""
    return im.convert("RGBa").resize((n, n), Image.LANCZOS).convert("RGBA")

def kare(img, cx, cy, kenar):
    k = kenar // 2
    return img.crop((cx - k, cy - k, cx + k, cy + k))

# ── 1) Şeffaf işaret ──
a = np.asarray(gorev).astype(np.float32)
kroma = a.max(axis=2) - a.min(axis=2)
parlak = a.max(axis=2)
alfa = np.clip((kroma - 28) / (75 - 28), 0, 1)
alfa = np.minimum(alfa, np.clip((parlak - 40) / 30, 0, 1))          # çok koyu = dama/gölge kalıntısı
alfa_im = Image.fromarray((alfa * 255).astype(np.uint8), "L")
alfa_im = alfa_im.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
alfa_im = alfa_im.filter(ImageFilter.MedianFilter(7)).filter(ImageFilter.GaussianBlur(0.8))
alfa = np.asarray(alfa_im).astype(np.float32) / 255.0

# Renk sızdırma: yalnız tam opak (>0.9) piksellerin rengi, bulanıklaştırılıp çevreye yayılır; yarı saydam kenarlar bu
# YAYILMIŞ logo rengini alır → kenarda dama/siyah kalıntısı kalmaz.
opak = (alfa > 0.9).astype(np.float32)
renk = np.zeros_like(a)
for _ in range(1):
    pay = Image.fromarray(np.uint8(np.clip(a * opak[..., None], 0, 255)), "RGB")
    pay_bl = np.asarray(pay.filter(ImageFilter.GaussianBlur(6))).astype(np.float32)
    pd_bl = np.asarray(Image.fromarray(np.uint8(opak * 255), "L").filter(ImageFilter.GaussianBlur(6))).astype(np.float32) / 255.0
    yayilmis = pay_bl / np.maximum(pd_bl[..., None], 1e-3)
sonuc = np.where((alfa > 0.9)[..., None], a, yayilmis)
rgba = np.dstack([np.clip(sonuc, 0, 255), alfa * 255]).astype(np.uint8)
isaret = Image.fromarray(rgba, "RGBA")
bbox = Image.fromarray(np.uint8((alfa > 0.15) * 255), "L").getbbox()
print("isaret bbox", bbox)
isaret = isaret.crop(bbox)
w, h = isaret.size; s = int(max(w, h) * 1.08)
tuval = Image.new("RGBA", (s, s), (0, 0, 0, 0))
tuval.paste(isaret, ((s - w) // 2, (s - h) // 2))
for n, ad in [(1024, "app-mark-1024.png"), (512, "app-mark.png"), (256, "logo-256.png"), (128, "app-mark-128.png"),
              (64, "app-mark-64.png"), (192, "favicon.png")]:
    kucult(tuval, n).save(os.path.join(cikti, ad), optimize=True)
# Çok boyutlu ICO: her boyut ayrı, ön-çarpılmış küçültmeyle
def ico(im_kaynak, boyutlar, ad):
    kareler = [kucult(im_kaynak, n) for n in boyutlar]
    kareler[-1].save(os.path.join(cikti, ad), format="ICO", sizes=[k.size for k in kareler], append_images=kareler[:-1])
ico(tuval, (16, 20, 24, 32, 40, 48, 64, 128, 256), "app-gorev.ico")
ico(tuval, (16, 32, 48, 64), "favicon.ico")

# ── 2) Arka planlı simge (kurulum + masaüstü kısayolu): büyükte işaret + yazı, küçükte yalnız işaret ──
W, H = setup.size
sx, sy = W / 2000, H / 1116
tam = kare(setup, int(1000 * sx), int(560 * sy), int(760 * sx)).convert("RGBA")
yalniz = kare(setup, 1368, 590, 640).convert("RGBA")
kucult(tam, 1024).save(os.path.join(cikti, "app-logo-bg-1024.png"))
buyuk = {n: kucult(tam, n) for n in (48, 64, 128, 256)}
kucuk = {n: kucult(yalniz, n) for n in (16, 20, 24, 32, 40)}
for n, im in {**kucuk, **buyuk}.items(): im.save(os.path.join(cikti, f"_bg_{n}.png"))
sirali = [kucuk[n] if n in kucuk else buyuk[n] for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
sirali[-1].save(os.path.join(cikti, "app-logo.ico"), format="ICO", sizes=[im.size for im in sirali], append_images=sirali[:-1])

# Kenar kalitesi ölçümü: yarı saydam kenarlarda koyu piksel oranı (eskisi %43'tü)
m = np.asarray(Image.open(os.path.join(cikti, "logo-256.png")).convert("RGBA")).astype(np.int32)
yari = (m[..., 3] > 20) & (m[..., 3] < 200)
koyu = yari & (m[..., :3].max(axis=2) < 60)
print(f"kenar: {yari.sum()} yarı saydam piksel, koyu oran %{100 * koyu.sum() / max(1, yari.sum()):.1f}")
print("tamam")
