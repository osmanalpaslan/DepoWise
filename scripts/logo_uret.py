# DepoWise / Alpnex — yeni logodan tüm simge ve logo dosyalarını üretir (2026-10-11, kullanıcı isteği).
# Kullanım: uv run --with pillow python -I scripts/logo_uret.py "<Yeni logo klasörü>" "<çıktı klasörü>"  → çıktıları Assets/wwwroot'a kopyala
#  - Kaynak 1 "masaüstü-setup logo.jpg": lacivert arka planlı işaret + AlpNeX yazısı → uygulama (.exe) ve kurulum simgesi.
#  - Kaynak 2 "Görev çubuğu logo.jpg": işaret, arkada ÇİZİLMİŞ dama deseni (gerçek şeffaflık YOK) → renk (kroma) anahtarıyla
#    dama deseni temizlenir → şeffaf işaret: görev çubuğu/pencere simgesi + uygulama içi logo + web favicon.
import sys, os
from PIL import Image, ImageFilter

kaynak_dir, cikti = sys.argv[1], sys.argv[2]
os.makedirs(cikti, exist_ok=True)
setup = Image.open(os.path.join(kaynak_dir, "masaüstü-setup logo.jpg")).convert("RGB")
gorev = Image.open(os.path.join(kaynak_dir, "Görev çubuğu logo.jpg")).convert("RGB")
W, H = setup.size

def kare(img, cx, cy, kenar):
    k = kenar // 2
    return img.crop((cx - k, cy - k, cx + k, cy + k))

# ── 1) Şeffaf işaret (dama deseni = renksiz gri/siyah; işaret = canlı mavi/camgöbeği) ──
px = gorev.load()
alfa = Image.new("L", gorev.size, 0)
ap = alfa.load()
T0, T1 = 28, 75                         # kroma eşiği: altı tamamen şeffaf, üstü tamamen opak
for y in range(gorev.size[1]):
    for x in range(gorev.size[0]):
        r, g, b = px[x, y]
        kroma = max(r, g, b) - min(r, g, b)
        a = (kroma - T0) / (T1 - T0)
        parlak = max(r, g, b)                 # çok koyu = dama/gölge kalıntısı → şeffaf (40 altı), 40-70 yumuşak geçiş
        a = min(a, (parlak - 40) / 30)
        ap[x, y] = 0 if a <= 0 else (255 if a >= 1 else int(a * 255))
# JPEG gürültüsü: tek tük piksel kalıntıları için hafif açma (min→max)
alfa = alfa.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
# Logo içindeki çok koyu gölge bandı (A kemerinin altı) kaynakta dama siyahıyla AYNI renk → ayırt edilemez; temiz bir
# boşluk olarak kalır (şeritler arası ayrım gibi görünür). Benekli görünümü yaratan yarı saydam gürültü ortanca süzgeçle
# temizlenir; ardından kenar yumuşatma.
alfa = alfa.filter(ImageFilter.MedianFilter(7))
duz = gorev
alfa = alfa.filter(ImageFilter.GaussianBlur(0.6))   # kenar yumuşatma
isaret = duz; isaret.putalpha(alfa)
bbox = alfa.point(lambda v: 255 if v > 40 else 0).getbbox()
print("isaret bbox", bbox)
isaret = isaret.crop(bbox)
# kare tuval + %6 kenar boşluğu
w, h = isaret.size; s = int(max(w, h) * 1.12)
tuval = Image.new("RGBA", (s, s), (0, 0, 0, 0))
tuval.paste(isaret, ((s - w) // 2, (s - h) // 2), isaret)
isaret_kare = tuval
isaret_kare.resize((1024, 1024), Image.LANCZOS).save(os.path.join(cikti, "app-mark-1024.png"))
isaret_kare.resize((512, 512), Image.LANCZOS).save(os.path.join(cikti, "app-mark.png"))
isaret_kare.resize((192, 192), Image.LANCZOS).save(os.path.join(cikti, "favicon.png"))
isaret_kare.save(os.path.join(cikti, "app-gorev.ico"), sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
isaret_kare.save(os.path.join(cikti, "favicon.ico"), sizes=[(16,16),(32,32),(48,48),(64,64)])

# ── 2) Arka planlı simge (uygulama + kurulum): büyükte işaret + yazı, küçükte yalnız işaret ──
sx, sy = W / 2000, H / 1116            # incelemedeki görüntü ölçeğinden asıl piksele
tam = kare(setup, int(1000 * sx), int(560 * sy), int(760 * sx)).convert("RGBA")
yalniz = kare(setup, 1368, 590, 640).convert("RGBA")   # yalnız işaret (bbox 1066-1671 x 386-821); yazı 915+ dışarıda
tam.resize((1024, 1024), Image.LANCZOS).save(os.path.join(cikti, "app-logo-bg-1024.png"))
buyuk = {n: tam.resize((n, n), Image.LANCZOS) for n in (48, 64, 128, 256)}
kucuk = {n: yalniz.resize((n, n), Image.LANCZOS) for n in (16, 20, 24, 32, 40)}
for n, im in {**kucuk, **buyuk}.items(): im.save(os.path.join(cikti, f"_bg_{n}.png"))
# Çok boyutlu ICO: her boyut kendi görüntüsüyle (Pillow append_images ile)
sirali = [kucuk[n] if n in kucuk else buyuk[n] for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
sirali[-1].save(os.path.join(cikti, "app-logo.ico"), format="ICO",
                sizes=[im.size for im in sirali], append_images=sirali[:-1])
print("tamam")
