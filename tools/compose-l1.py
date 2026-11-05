"""Composite the refined L1 layer pack in its declared order and write previews."""
import json
import os
import sys

from PIL import Image

pack = sys.argv[1] if len(sys.argv) > 1 else r"pets/whale-maid-l1/art-source/extracted"
outdir = sys.argv[2] if len(sys.argv) > 2 else r"docs/images"
os.makedirs(outdir, exist_ok=True)
parts = os.path.join(pack, "parts")

order = json.load(open(os.path.join(pack, "layer-order.json"), encoding="utf-8"))
variants = {"EYES": "eyes_open", "MOUTH": "mouth_smile"}
W, H = 1024, 1328

board = Image.new("RGBA", (W, H), (0, 0, 0, 0))
for name in order:
    if name in variants:
        name = variants[name]
    path = os.path.join(parts, name + ".png")
    if not os.path.exists(path):
        print("missing", name)
        continue
    layer = Image.open(path).convert("RGBA")
    if layer.size != (W, H):
        print("size mismatch", name, layer.size)
    board.alpha_composite(layer)

checker = Image.new("RGBA", (W, H))
for y in range(0, H, 32):
    for x in range(0, W, 32):
        c = (238, 238, 242, 255) if ((x // 32) + (y // 32)) % 2 == 0 else (200, 206, 216, 255)
        checker.paste(c, (x, y, min(x + 32, W), min(y + 32, H)))
checker.alpha_composite(board)
checker.save(os.path.join(outdir, "l1-preview.png"))

# expression sheet
for label, eye, mouth in [
    ("open", "eyes_open", "mouth_smile"),
    ("half", "eyes_half", "mouth_smile"),
    ("closed", "eyes_closed", "mouth_smile"),
    ("talk", "eyes_open", "mouth_open"),
    ("surprised", "eyes_open", "mouth_surprised"),
]:
    frame = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for name in order:
        target = variants.get(name, name)
        if name == "EYES":
            target = eye
        elif name == "MOUTH":
            target = mouth
        frame.alpha_composite(Image.open(os.path.join(parts, target + ".png")).convert("RGBA"))
    frame.save(os.path.join(outdir, f"l1-{label}.png"))
    if label == "talk":
        frame.save(os.path.join(outdir, "l1-talk-full.png"))

print("wrote", outdir)
