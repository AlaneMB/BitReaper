#!/usr/bin/env python3
"""Cuts the backgrounds out of the source art in Art/source and writes app assets.

- Icon: used as-is when it already has transparency; otherwise the black surround is removed ("unblended" from black so the neon glow stays soft) and the
  artwork is placed on the macOS icon grid, then exported at every AppIcon size.
- Other art sits on white: the white background (and the light halo connected to it) is unblended from
  white, so glows become translucent neon instead of a pale fringe on dark backgrounds.
"""
import json
import os
from collections import deque

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "source")
ASSETS = os.path.join(ROOT, "Resources", "Assets.xcassets")


def border_flood(mask: np.ndarray) -> np.ndarray:
    """Pixels where `mask` is true that are connected to the image border."""
    h, w = mask.shape
    seen = np.zeros_like(mask, dtype=bool)
    queue = deque()
    for x in range(w):
        for y in (0, h - 1):
            if mask[y, x] and not seen[y, x]:
                seen[y, x] = True
                queue.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if mask[y, x] and not seen[y, x]:
                seen[y, x] = True
                queue.append((y, x))
    while queue:
        y, x = queue.popleft()
        for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
            if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True
                queue.append((ny, nx))
    return seen


def cut_black(img: Image.Image, threshold=0.42) -> Image.Image:
    rgb = np.asarray(img.convert("RGB")).astype(np.float32) / 255
    peak = rgb.max(axis=2)
    region = border_flood(peak < threshold)
    alpha = np.ones(peak.shape, np.float32)
    alpha[region] = np.clip(peak[region] / threshold, 0, 1) ** 1.6
    color = rgb.copy()
    safe = np.maximum(peak, 1e-4)[..., None]
    color[region] = np.clip(rgb[region] / safe[region], 0, 1)
    return to_image(color, alpha)


def cut_white(img: Image.Image, threshold=0.30) -> Image.Image:
    rgb = np.asarray(img.convert("RGB")).astype(np.float32) / 255
    low = rgb.min(axis=2)
    region = border_flood(low > threshold)
    alpha = np.ones(low.shape, np.float32)
    a = np.clip(1 - low, 0, 1)
    # Fade alpha to 0 for the brightest (near white) pixels, keep it smooth up to the threshold edge.
    alpha[region] = a[region] / (1 - threshold) if threshold < 1 else a[region]
    alpha = np.clip(alpha, 0, 1)
    color = rgb.copy()
    safe = np.maximum(a, 1e-4)[..., None]
    unblended = np.clip((rgb - (1 - a[..., None])) / safe, 0, 1)
    color[region] = unblended[region]
    return to_image(color, alpha)


def load_art(name: str) -> Image.Image:
    """Most art already ships with transparency; only opaque art gets its white background cut."""
    img = Image.open(os.path.join(SRC, name))
    if img.mode == "RGBA" and (np.asarray(img)[..., 3] < 10).mean() > 0.05:
        return img.convert("RGBA")
    return cut_white(img)


def drop_subtitle(logo: Image.Image, cut_row=430, fade=8) -> Image.Image:
    """Removes the "DISK SPACE ANALYZER" line under the wordmark, fading the last rows so the cut is soft."""
    logo = logo.crop((0, 0, logo.width, cut_row))
    px = np.asarray(logo).astype(np.float32)
    ramp = np.linspace(1, 0, fade)[:, None]
    px[-fade:, :, 3] *= ramp
    return Image.fromarray(px.astype(np.uint8), "RGBA")


def to_image(color, alpha) -> Image.Image:
    out = np.dstack([color, alpha])
    return Image.fromarray((out * 255 + 0.5).astype(np.uint8), "RGBA")


def trim(img: Image.Image, pad=8) -> Image.Image:
    alpha = np.asarray(img)[..., 3]
    ys, xs = np.where(alpha > 10)
    box = (max(xs.min() - pad, 0), max(ys.min() - pad, 0),
           min(xs.max() + pad + 1, img.width), min(ys.max() + pad + 1, img.height))
    return img.crop(box)


def fit(img: Image.Image, max_w: int, max_h: int) -> Image.Image:
    scale = min(max_w / img.width, max_h / img.height, 1)
    if scale < 1:
        img = img.resize((round(img.width * scale), round(img.height * scale)), Image.LANCZOS)
    return img


def write_imageset(name: str, img: Image.Image):
    folder = os.path.join(ASSETS, f"{name}.imageset")
    os.makedirs(folder, exist_ok=True)
    img.save(os.path.join(folder, f"{name}.png"), optimize=True)
    contents = {"images": [{"idiom": "universal", "filename": f"{name}.png"}],
                "info": {"author": "xcode", "version": 1}}
    with open(os.path.join(folder, "Contents.json"), "w") as f:
        json.dump(contents, f, indent=2)


def write_app_icon(art: Image.Image):
    folder = os.path.join(ASSETS, "AppIcon.appiconset")
    for old in os.listdir(folder):
        os.remove(os.path.join(folder, old))
    # macOS icon grid: 824 pt artwork centred on a 1024 pt canvas.
    canvas = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    body = art.resize((860, 860), Image.LANCZOS)
    canvas.alpha_composite(body, ((1024 - 860) // 2, (1024 - 860) // 2 + 6))
    images = []
    for base in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            px = base * scale
            name = f"icon_{base}x{base}{'@2x' if scale == 2 else ''}.png"
            canvas.resize((px, px), Image.LANCZOS).save(os.path.join(folder, name), optimize=True)
            images.append({"idiom": "mac", "size": f"{base}x{base}", "scale": f"{scale}x", "filename": name})
    with open(os.path.join(folder, "Contents.json"), "w") as f:
        json.dump({"images": images, "info": {"author": "xcode", "version": 1}}, f, indent=2)


def main():
    icon = Image.open(os.path.join(SRC, "icon.webp"))
    has_alpha = icon.mode == "RGBA" and (np.asarray(icon)[..., 3] < 10).mean() > 0.05
    icon = trim(icon.convert("RGBA") if has_alpha else cut_black(icon), pad=0)
    side = max(icon.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.alpha_composite(icon, ((side - icon.width) // 2, (side - icon.height) // 2))
    write_app_icon(square)

    write_imageset("Logo", fit(trim(drop_subtitle(load_art("logo.webp"))), 1400, 400))
    write_imageset("SunsetBanner", fit(trim(load_art("sunset-banner.webp")), 1600, 600))
    write_imageset("SunsetMountains", fit(trim(load_art("sunset-mountains.webp")), 900, 900))
    # The cute reaper, one pose per job.
    write_imageset("ReaperStanding", fit(trim(load_art("reaper-standing.webp")), 640, 640))
    write_imageset("ReaperPeek", fit(trim(load_art("reaper-peek.webp"), pad=0), 512, 512))
    write_imageset("ReaperFolder", fit(trim(load_art("reaper-folder.webp")), 512, 512))
    write_imageset("ReaperDisk", fit(trim(load_art("reaper-disk.webp")), 640, 640))
    print("art written")


if __name__ == "__main__":
    main()
