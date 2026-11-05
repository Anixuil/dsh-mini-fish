"""Art-QA for the refined L1 pack: sizes, fringes, and rig-motion hole tests."""
import json
import os
import sys

from PIL import Image, ImageDraw

pack = sys.argv[1] if len(sys.argv) > 1 else r"pets/whale-maid-l1/art-source/extracted"
outdir = sys.argv[2] if len(sys.argv) > 2 else r"docs/images"
parts = os.path.join(pack, "parts")
os.makedirs(outdir, exist_ok=True)

W, H = 1024, 1328
order = json.load(open(os.path.join(pack, "layer-order.json"), encoding="utf-8"))

pivots = {
    "tail": (655, 956),
    "ear_l": (369, 292),
    "ear_r": (655, 292),
    "hair_back": (512, 452),
    "hair_front": (512, 452),
    "hair_side_l": (410, 398),
    "hair_side_r": (614, 398),
    "ahoge": (512, 46),
    "arm_l": (348, 624),
    "arm_r": (676, 624),
    "head": (512, 452),
    "skirt": (512, 823),
    "apron": (512, 823),
    "brow": (512, 452),
    "eyes_open": (512, 452),
    "blush": (512, 452),
    "mouth_smile": (512, 452),
}
movable = {
    "tail": -12,
    "ear_l": 14,
    "ear_r": -14,
    "hair_side_l": 8,
    "hair_side_r": -8,
    "hair_front": 4,
    "arm_l": 20,
    "arm_r": -20,
    "head": 3,
    "skirt": 4,
    "apron": 5,
}

def load(name):
    return Image.open(os.path.join(parts, name + ".png")).convert("RGBA")

# 1) integrity
print("== integrity ==")
for name in sorted(os.listdir(parts)):
    if not name.endswith(".png"):
        continue
    img = Image.open(os.path.join(parts, name))
    if img.size != (W, H) or img.mode != "RGBA":
        print("!!", name, img.size, img.mode)

# 2) dark background fringe check
print("== fringe check ==")
base = Image.new("RGBA", (W, H), (40, 30, 30, 255))
for name in order:
    if name in ("EYES", "MOUTH"):
        name = "eyes_open" if name == "EYES" else "mouth_smile"
    base.alpha_composite(load(name))
base.save(os.path.join(outdir, "l1-fringe-dark.png"))

# 3) motion hole test: rotate individual layers around pivot and composite
frames = []
for name, deg in movable.items():
    frame = Image.new("RGBA", (W, H), (250, 250, 252, 255))
    for n in order:
        if n in ("EYES", "MOUTH"):
            n = "eyes_open" if n == "EYES" else "mouth_smile"
        layer = load(n)
        if n == name:
            pivot = pivots.get(name, (512, 700))
            rotated = layer.rotate(deg, resample=Image.BICUBIC, center=pivot)
            frame.alpha_composite(rotated)
        else:
            frame.alpha_composite(layer)
    # red box marks the moving part's pivot for review
    d = ImageDraw.Draw(frame)
    px, py = pivots.get(name, (512, 700))
    d.ellipse([px - 8, py - 8, px + 8, py + 8], outline=(255, 0, 0, 255), width=3)
    d.text((20, 20), f"{name} {deg:+d} deg", fill=(20, 20, 20, 255))
    frames.append(frame)

cols = 3
cell_w, cell_h = 512, 664
sheet = Image.new("RGBA", (cols * cell_w, ((len(frames) + cols - 1) // cols) * cell_h), (255, 255, 255, 255))
for i, frame in enumerate(frames):
    thumb = frame.resize((cell_w, cell_h), Image.LANCZOS)
    sheet.paste(thumb, ((i % cols) * cell_w, (i // cols) * cell_h))
sheet.save(os.path.join(outdir, "l1-motion-test.png"))
print("wrote", outdir)
