"""Repair the v2 layer-split artefact behind the pale stepped marks in the bangs.

Symptom
-------
Thin pale rectangles / stair-stepped marks over the bangs, most obvious while the
idle sway is running, around the eyebrow / forehead area.

Root cause
----------
The approved master (`whale-maid-master-locked-v2.zip`) is a flat composite.  The
split into parts gave a few *upper* layers pixels that actually belong to the
layers beneath them:

* `hair_front.png` contains the skin wedges that are visible between the bang
  strands, as **opaque** pixels.
* `headdress.png` contains a small block of the same skin.

At rest those baked pixels happen to sit exactly on top of the matching skin in
the layer below, so the seam is invisible.  The hair / headdress layers however
sway (spring), so the baked skin travels with them and paints a pale, hard-edged
patch over the dark hair -> the artefact.

Fix
---
1. Strip the baked skin pixels from the hair / accessory layers (the master is
   the ground truth: if the master shows skin there, the pixel belongs to the
   face, not to the hair).
2. Restore the approved master colour into the topmost layer that still covers
   each stripped pixel, so the resting pose keeps matching the approved master
   while the hair layer becomes pure hair.

Run:  python tools/fix-face-split.py
Backups: art-source/whale-maid-l1/face-split-fix/parts-before/
"""
import os
import shutil

import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACK = os.path.join(ROOT, "pets", "whale-maid-l1")
PARTS = os.path.join(PACK, "parts")
MASTER = os.path.join(
    ROOT, "art-source", "whale-maid-l1", "behavior-supplement-v2",
    "final", "previews", "master.png")
BACKUP = os.path.join(ROOT, "art-source", "whale-maid-l1", "face-split-fix", "parts-before")

# Draw order used by pet.json (bottom -> top); value = default image file stem.
DRAW_ORDER = [
    ("tail", "tail"),
    ("hair_back", "hair_back"),
    ("arm_l", "arm_l"),
    ("arm_r", "arm_r"),
    ("body", "body"),
    ("skirt", "skirt"),
    ("apron", "apron"),
    ("ear_l", "ear_l"),
    ("ear_r", "ear_r"),
    ("head", "head"),
    ("brow", "brow"),
    ("blush", "blush"),
    ("mouth", "mouth_smile"),
    ("hair_side_l", "hair_side_l"),
    ("hair_side_r", "hair_side_r"),
    ("hair_front", "hair_front"),
    ("ahoge", "ahoge"),
    ("headdress", "headdress"),
    ("bow_r", "bow_r"),
]
# Layers that must never contain skin pixels (hair / accessories on top of the face).
STRIP_SKIN_FROM = ["hair_front", "headdress"]


def skin_like(arr, b_min=150):
    r, g, b, a = arr[..., 0], arr[..., 1], arr[..., 2], arr[..., 3]
    return ((a > 8) & (r > 195) & ((r - b) > 10) & ((r - g) < 70) & (b > b_min))


def close_holes(mask, radius):
    """Binary closing: fills small transparent holes inside a layer silhouette."""
    if radius <= 0:
        return mask
    m = Image.fromarray((mask * 255).astype(np.uint8))
    m = m.filter(ImageFilter.MaxFilter(radius * 2 + 1)).filter(ImageFilter.MinFilter(radius * 2 + 1))
    return np.asarray(m) > 127


def load(stem):
    return np.asarray(
        Image.open(os.path.join(PARTS, stem + ".png")).convert("RGBA")).astype(np.float32)


def save(arr, stem):
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA").save(
        os.path.join(PARTS, stem + ".png"))


def main():
    if not os.path.exists(MASTER):
        raise SystemExit("missing approved master: " + MASTER)
    master = np.asarray(Image.open(MASTER).convert("RGBA")).astype(np.float32)
    master_skin = skin_like(master)

    os.makedirs(BACKUP, exist_ok=True)
    for _, stem in DRAW_ORDER:
        src = os.path.join(PARTS, stem + ".png")
        dst = os.path.join(BACKUP, stem + ".png")
        if os.path.exists(src) and not os.path.exists(dst):
            shutil.copy2(src, dst)

    layers = {stem: load(stem) for _, stem in DRAW_ORDER}
    for target_layer in STRIP_SKIN_FROM:
        stem = dict(DRAW_ORDER)[target_layer]
        arr = layers[stem]
        alpha = arr[..., 3]
        remove = (alpha > 8) & master_skin
        count = int(remove.sum())
        if count:
            index = [t[0] for t in DRAW_ORDER].index(target_layer)
            for lower, lower_stem in reversed(DRAW_ORDER[:index]):
                lower_alpha = layers[lower_stem][..., 3]
                hit = remove & (lower_alpha > 200)
                if hit.any():
                    layers[lower_stem][hit, :3] = master[..., :3][hit]
                    remove = remove & ~hit
                if not remove.any():
                    break
            arr[(alpha > 8) & master_skin, 3] = 0.0
        print("strip %-12s %6d px (uncovered: %d)" % (target_layer, count, int(remove.sum())))

    # The bangs slide over the forehead (spring sway), and the split left the skin
    # of the face underneath them, so every sway reveals pale skin slivers where the
    # artwork shows hair.  Give the face layer the approved master's own bangs as a
    # static backdrop: whatever the hair layer uncovers then looks like the original
    # artwork (hair, or the skin wedge) instead of a hard-edged pale strip.
    head = layers["head"]
    hair_front = layers["hair_front"]
    area = close_holes(hair_front[..., 3] > 128, 4) & close_holes(head[..., 3] > 128, 16)
    head[area, :3] = master[..., :3][area]
    head[area, 3] = 255.0
    print("backdrop %-9s %6d px (bangs area made solid)" % ("head", int(area.sum())))

    for _, stem in DRAW_ORDER:
        save(layers[stem], stem)
    print("done; backups in", BACKUP)


if __name__ == "__main__":
    main()
