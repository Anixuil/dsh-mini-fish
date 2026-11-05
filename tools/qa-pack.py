"""Generic DSH Pet Format pack QA: compose, fringe check, pivot-based motion test.

Usage: python tools/qa-pack.py <pack-dir> <out-dir>
"""
import json
import os
import sys

from PIL import Image, ImageDraw

pack = sys.argv[1]
outdir = sys.argv[2]
os.makedirs(outdir, exist_ok=True)

manifest = json.load(open(os.path.join(pack, "pet.json"), encoding="utf-8"))
W = int(manifest["canvas"]["width"])
H = int(manifest["canvas"]["height"])
layers = manifest["layers"]
by_id = {l["id"]: l for l in layers}


def image_of(layer, variant=None):
    rel = layer.get("image")
    if variant and layer.get("variants", {}).get(variant):
        rel = layer["variants"][variant]
    return Image.open(os.path.join(pack, rel)).convert("RGBA")


def pivot_px(layer):
    px, py = layer.get("pivot", [0.5, 0.5])
    return px * W, py * H


def compose(base="open", mouth="smile"):
    frame = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for layer in sorted(layers, key=lambda l: l.get("z", 0)):
        lid = layer["id"]
        if lid.startswith("eyes"):
            frame.alpha_composite(image_of(layer, base))
        elif lid.startswith("mouth"):
            frame.alpha_composite(image_of(layer, mouth))
        else:
            frame.alpha_composite(image_of(layer))
    return frame


# 1) integrity
print("== integrity ==")
for layer in layers:
    for rel in [layer.get("image")] + list(layer.get("variants", {}).values()):
        if not rel:
            continue
        path = os.path.join(pack, rel)
        img = Image.open(path)
        if img.size != (W, H) or img.mode != "RGBA":
            print("!!", rel, img.size, img.mode)

# 2) previews
print("== previews ==")
checker = Image.new("RGBA", (W, H))
for y in range(0, H, 32):
    for x in range(0, W, 32):
        c = (238, 238, 242, 255) if ((x // 32) + (y // 32)) % 2 == 0 else (200, 206, 216, 255)
        checker.paste(c, (x, y, min(x + 32, W), min(y + 32, H)))
checker.alpha_composite(compose())
checker.save(os.path.join(outdir, "preview.png"))

# expression sheet
states = [("open", "smile"), ("half", "smile"), ("closed", "smile"), ("open", "open"), ("open", "surprised")]
frames = []
for base, mouth in states:
    frames.append(compose(base, mouth))
cols = 5
cell_w, cell_h = W // 2, H // 2
sheet = Image.new("RGBA", (cols * cell_w, cell_h), (250, 250, 252, 255))
for i, frame in enumerate(frames):
    sheet.paste(frame.resize((cell_w, cell_h), Image.LANCZOS), (i * cell_w, 0))
sheet.save(os.path.join(outdir, "expressions.png"))

# dark fringe
dark = Image.new("RGBA", (W, H), (40, 30, 30, 255))
dark.alpha_composite(compose())
dark.save(os.path.join(outdir, "fringe-dark.png"))

# 3) motion extremes around declared pivots
print("== motion ==")
motions = [
    ("tail", 14), ("ear_l", 16), ("ear_r", -16),
    ("hair_side_l", 12), ("hair_side_r", -12), ("hair_front", 6),
    ("arm_l", 22), ("arm_r", -22), ("head", 4), ("skirt", 6), ("apron", 7),
    ("ahoge", 14), ("bow_r", 10),
]
cells = []
for lid, deg in motions:
    if lid not in by_id:
        continue
    frame = Image.new("RGBA", (W, H), (250, 250, 252, 255))
    for layer in sorted(layers, key=lambda l: l.get("z", 0)):
        img = compose_single = image_of(layer, "open" if layer["id"].startswith("eyes") else "smile" if layer["id"].startswith("mouth") else None)
        if layer["id"] == lid:
            px, py = pivot_px(layer)
            img = img.rotate(deg, resample=Image.BICUBIC, center=(px, py))
        frame.alpha_composite(img)
    d = ImageDraw.Draw(frame)
    px, py = pivot_px(by_id[lid])
    d.ellipse([px - 8, py - 8, px + 8, py + 8], outline=(255, 0, 0, 255), width=3)
    d.text((20, 20), f"{lid} {deg:+d} deg", fill=(20, 20, 20, 255))
    cells.append(frame)

cols = 4
cell_w, cell_h = W // 2, H // 2
rows = (len(cells) + cols - 1) // cols
sheet = Image.new("RGBA", (cols * cell_w, rows * cell_h), (255, 255, 255, 255))
for i, frame in enumerate(cells):
    sheet.paste(frame.resize((cell_w, cell_h), Image.LANCZOS), ((i % cols) * cell_w, (i // cols) * cell_h))
sheet.save(os.path.join(outdir, "motion-test.png"))
print("wrote", outdir)
