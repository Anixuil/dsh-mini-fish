"""Generate the L1 pet-pack canvas guide (1024x1328) used by the art checklist."""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "pets", "_template-l1")
os.makedirs(OUT, exist_ok=True)

W, H = 1024, 1328
img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# checkerboard-ish faint background so transparent canvas is visible
for y in range(0, H, 64):
    for x in range(0, W, 64):
        if ((x // 64) + (y // 64)) % 2 == 0:
            d.rectangle([x, y, x + 63, y + 63], fill=(255, 255, 255, 26))

lines = [
    (0.00, "top"),
    (0.035, "ahoge base"),
    (0.20, "ear base / head middle"),
    (0.30, "eye line"),
    (0.34, "head base (neck pivot)"),
    (0.46, "shoulder line"),
    (0.50, "brooch / chest"),
    (0.62, "waist pivot"),
    (0.72, "tail base"),
    (0.90, "knee"),
    (1.00, "feet / bottom (anchor 0.5,1.0)"),
]
for frac, label in lines:
    y = int(frac * (H - 1))
    color = (255, 80, 160, 160) if frac in (0.62, 0.34, 0.46, 1.0) else (80, 140, 255, 120)
    d.line([(0, y), (W, y)], fill=color, width=2)
    d.text((12, max(4, y - 16)), f"{label}  y={y}", fill=(30, 30, 30, 230))

d.line([(W // 2, 0), (W // 2, H)], fill=(255, 80, 160, 140), width=2)
for x in range(0, W, 128):
    d.line([(x, 0), (x, 24)], fill=(30, 30, 30, 160), width=1)
    d.text((x + 4, 6), str(x), fill=(30, 30, 30, 200))

d.text((20, H - 30), "DSH Pet L1 canvas guide - 1024 x 1328 - guides are reference only, never export them", fill=(30, 30, 30, 220))
img.save(os.path.join(OUT, "canvas-guide.png"))
print("wrote", os.path.join(OUT, "canvas-guide.png"))
