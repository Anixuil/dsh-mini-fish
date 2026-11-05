"""Build the reference pet pack art from the provided character sheet.

Input : assets/ref-three-view.webp  (front / side / back chibi views, white background)
Output: pets/whale-maid/parts/body.png   (transparent, trimmed front view)
        pets/whale-maid/preview.png      (square preview for the picker)
"""
import os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "assets", "ref-three-view.webp")
OUT_DIR = os.path.join(ROOT, "pets", "whale-maid")
PARTS = os.path.join(OUT_DIR, "parts")
os.makedirs(PARTS, exist_ok=True)


def near_white(rgb, tol=238):
    return (rgb[:, :, 0] >= tol) & (rgb[:, :, 1] >= tol) & (rgb[:, :, 2] >= tol)


def background_mask(white):
    """Flood fill from the borders through near-white pixels only."""
    h, w = white.shape
    bg = np.zeros((h, w), dtype=bool)
    bg[0, :] = white[0, :]
    bg[-1, :] = white[-1, :]
    bg[:, 0] = white[:, 0]
    bg[:, -1] = white[:, -1]
    while True:
        grown = bg.copy()
        grown[1:, :] |= bg[:-1, :]
        grown[:-1, :] |= bg[1:, :]
        grown[:, 1:] |= bg[:, :-1]
        grown[:, :-1] |= bg[:, 1:]
        grown &= white
        if (grown == bg).all():
            return bg
        bg = grown


def content_columns(rgb):
    white = near_white(rgb)
    ink = ~white
    cols = ink.sum(axis=0)
    # a column belongs to a view when it has a meaningful amount of ink
    active = cols > 8
    runs, start = [], None
    for x, a in enumerate(active):
        if a and start is None:
            start = x
        elif not a and start is not None:
            if x - start > 30:
                runs.append((start, x - 1))
            start = None
    if start is not None and len(active) - start > 30:
        runs.append((start, len(active) - 1))
    return runs


def content_rows(alpha):
    """Vertical runs of alpha, so a printed caption below the character is excluded."""
    active = (alpha > 24).sum(axis=1) > 2
    runs, start = [], None
    for y, a in enumerate(active):
        if a and start is None:
            start = y
        elif not a and start is not None:
            runs.append((start, y - 1))
            start = None
    if start is not None:
        runs.append((start, len(active) - 1))
    return runs


def trim_alpha(img):
    a = np.array(img)
    alpha = a[:, :, 3]
    runs = content_rows(alpha)
    if not runs:
        raise SystemExit("nothing left after background removal")
    # the character is the tallest vertical run; captions and rules are short bands
    y0r, y1r = max(runs, key=lambda r: r[1] - r[0])
    band = alpha[y0r : y1r + 1, :]
    ys, xs = np.where(band > 8)
    x0, x1 = max(xs.min() - 4, 0), min(xs.max() + 5, a.shape[1])
    y0, y1 = max(y0r + ys.min() - 4, 0), min(y0r + ys.max() + 5, a.shape[0])
    return img.crop((x0, y0, x1, y1))


def main():
    src = Image.open(SRC).convert("RGB")
    rgb = np.array(src)
    runs = content_columns(rgb)
    print("view column runs:", runs)
    if not runs:
        raise SystemExit("no view detected")
    x0, x1 = runs[0]
    pad = 6
    view = src.crop((max(x0 - pad, 0), 0, min(x1 + pad, src.width), src.height))

    rgb = np.array(view)
    white = near_white(rgb)
    bg = background_mask(white)
    alpha = np.where(bg, 0, 255).astype(np.uint8)
    # thin, soft edge: blur alpha once so resampling does not look cut out
    a_img = Image.fromarray(alpha, "L").filter(ImageFilter.GaussianBlur(0.6))
    out = view.convert("RGBA")
    out.putalpha(a_img)
    out = trim_alpha(out)
    out.save(os.path.join(PARTS, "body.png"), optimize=True)
    print("body.png:", out.size)

    # preview: square, transparent, fitted
    side = 512
    scale = min(side / out.width, side / out.height)
    pw, ph = int(out.width * scale), int(out.height * scale)
    body = out.resize((pw, ph), Image.LANCZOS)
    preview = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    preview.paste(body, ((side - pw) // 2, side - ph - 8), body)
    preview.save(os.path.join(OUT_DIR, "preview.png"), optimize=True)
    print("preview.png:", preview.size)


if __name__ == "__main__":
    main()
