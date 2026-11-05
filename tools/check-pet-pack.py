"""Validate a DSH Pet Format L1 pack and render a checkerboard composite preview.

Usage:
  python tools/check-pet-pack.py pets/whale-maid-l1
Outputs a per-layer report and <pack>/_preview-composite.png (never edits the pack).
"""
import json
import os
import sys

from PIL import Image

REQUIRED_FLAT = ["body.png"]
# A pack may ship a single-piece layer or the equivalent v2 sub-chain (e.g. the tail
# can be tail_base/mid/tip instead of tail.png), so accept either name.
CORE_LAYERS = [
    ("hair back", ["hair_back.png", "hair_back_upper.png"]),
    ("tail", ["tail.png", "tail_base.png"]),
    ("body", ["body.png"]),
    ("skirt", ["skirt.png", "skirt_front.png"]),
    ("left arm", ["arm_l.png", "upper_arm_l.png"]),
    ("right arm", ["arm_r.png", "upper_arm_r.png"]),
    ("head", ["head.png"]),
    ("mouth", ["mouth_smile.png"]),
    ("hair front", ["hair_front.png"]),
]
OPTIONAL_LAYERS = [
    "apron.png", "brooch.png", "ear_l.png", "ear_r.png",
    "brow.png", "blush.png", "eyes_half.png", "eyes_closed.png",
    "mouth_open.png", "mouth_surprised.png",
    "hair_side_l.png", "hair_side_r.png", "ahoge.png",
    "headdress.png", "bow_l.png", "bow_r.png",
    "shadow.png",
]


def load_manifest(pack):
    path = os.path.join(pack, "pet.json")
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def main():
    if len(sys.argv) < 2:
        print("usage: check-pet-pack.py <pack-dir>")
        return 2
    pack = os.path.abspath(sys.argv[1])
    manifest = load_manifest(pack)
    canvas = manifest.get("canvas", {})
    cw, ch = int(canvas.get("width", 0)), int(canvas.get("height", 0))
    print(f"pack: {manifest.get('id')}  spec={manifest.get('spec')}  canvas={cw}x{ch}")

    layers = manifest.get("layers") or []
    if not layers:
        print("!! no layers in pet.json")
        return 1

    problems = []
    images = []
    for layer in layers:
        rel = layer.get("image", "")
        path = os.path.join(pack, rel)
        if not os.path.isfile(path):
            problems.append(f"missing layer file: {rel}")
            continue
        img = Image.open(path).convert("RGBA")
        if (img.width, img.height) != (cw, ch):
            problems.append(f"{rel}: {img.width}x{img.height} != canvas {cw}x{ch}")
        bbox = img.getchannel("A").getbbox()
        if bbox is None:
            problems.append(f"{rel}: fully transparent")
            continue
        covered = (bbox[2] - bbox[0]) / cw * (bbox[3] - bbox[1]) / ch
        flags = []
        if layer.get("hidden"): flags.append("hidden")
        if layer.get("retired"): flags.append("retired")
        flag_text = ("  [" + ",".join(flags) + "]") if flags else ""
        print(f"  {rel:<26} bbox={bbox}  alpha>0 area={covered*100:.1f}%{flag_text}")
        if not layer.get("hidden") and not layer.get("retired"):
            images.append((layer.get("z", 0), img))

    # composite over a checkerboard
    if images:
        board = Image.new("RGBA", (cw, ch))
        for y in range(0, ch, 32):
            for x in range(0, cw, 32):
                color = (240, 240, 240, 255) if ((x // 32) + (y // 32)) % 2 == 0 else (205, 210, 220, 255)
                board.paste(color, (x, y, min(x + 32, cw), min(y + 32, ch)))
        for _, img in sorted(images, key=lambda item: item[0]):
            board.alpha_composite(img)
        out = os.path.join(pack, "_preview-composite.png")
        board.save(out)
        print(f"composite preview: {out}")

    if manifest.get("conformance") == "L1":
        for label, alternatives in CORE_LAYERS:
            if not any(any(l.get("image", "").endswith(alt) for alt in alternatives) for l in layers):
                problems.append(f"recommended core layer missing (L1): {label} ({' or '.join(alternatives)})")
        if not manifest.get("eyesRig") and not any(l.get("image", "").endswith("eyes_open.png") for l in layers):
            problems.append("recommended eye rig missing: add eyesRig or eyes_open.png")

    # parent-child chains must point at layers that exist
    ids = {l.get("id") for l in layers}
    for layer in layers:
        parent = layer.get("parent")
        if parent and parent not in ids:
            problems.append(f"{layer.get('id')}: unknown parent {parent}")

    # whole-body poses (sit / peek) must exist and use the canvas size
    for pose_id, pose in (manifest.get("poses") or {}).items():
        rel = pose.get("image", "")
        path = os.path.join(pack, rel)
        if not os.path.isfile(path):
            problems.append(f"pose {pose_id}: missing file {rel}")
            continue
        img = Image.open(path).convert("RGBA")
        if (img.width, img.height) != (cw, ch):
            problems.append(f"pose {pose_id}: {img.width}x{img.height} != canvas {cw}x{ch}")
        if img.getchannel("A").getbbox() is None:
            problems.append(f"pose {pose_id}: fully transparent")
        print(f"  pose {pose_id:<21} ledgeY={pose.get('ledgeY')}  file={rel}")

    if problems:
        print("\n".join("!! " + p for p in problems))
        return 1
    print("ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())



