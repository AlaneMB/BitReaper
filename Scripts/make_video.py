#!/usr/bin/env python3
"""Renders the 20-second BitReaper promo video (1080p, 30 fps) from app window screenshots.

Usage: python3 Scripts/make_video.py <frames-dir> <out.mp4> [<out.gif>]

<frames-dir> holds window captures (screencapture -o -l <window>) named:
  home, scan1, scan2, tree, collect1, collect2, collector, largest, sheet, countdown5, countdown4
Brand art comes from Resources/Assets.xcassets. Needs Pillow, NumPy and ffmpeg.
"""
import math
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Resources", "Assets.xcassets")
W, H, FPS = 1920, 1080, 30
FONT = "/System/Library/Fonts/SFNSRounded.ttf"

NIGHT = (11, 8, 36)
DEEP = (5, 3, 15)
MAGENTA = (255, 43, 214)
CYAN = (34, 228, 245)
VIOLET = (108, 59, 255)
TEXT = (244, 240, 255)
LAVENDER = (169, 155, 216)


def asset(name):
    return Image.open(os.path.join(ASSETS, f"{name}.imageset", f"{name}.png")).convert("RGBA")


def font(size, weight="Bold"):
    f = ImageFont.truetype(FONT, size)
    try:
        f.set_variation_by_name(weight)
    except Exception:
        pass
    return f


def ease_out(t):
    return 1 - (1 - t) ** 3


def ease_in_out(t):
    return 0.5 - 0.5 * math.cos(math.pi * min(max(t, 0), 1))


def back_out(t, s=1.6):
    t = min(max(t, 0), 1) - 1
    return t * t * ((s + 1) * t + s) + 1


def clamp01(t):
    return min(max(t, 0.0), 1.0)


# ---------------------------------------------------------------- background

def make_background():
    y = np.linspace(0, 1, H)[:, None, None]
    grad = (np.array(NIGHT) * (1 - y) + np.array(DEEP) * y) * np.ones((H, W, 3))
    img = Image.fromarray(grad.astype(np.uint8), "RGB").convert("RGBA")
    grid = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(grid)
    for x in range(0, W, 32):
        d.line([(x, 0), (x, H)], fill=(108, 59, 255, 22))
    for yy in range(0, H, 32):
        d.line([(0, yy), (W, yy)], fill=(108, 59, 255, 22))
    img.alpha_composite(grid)
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([W - 900, -500, W + 500, 600], fill=(255, 43, 214, 46))
    gd.ellipse([-600, H - 500, 700, H + 500], fill=(34, 228, 245, 30))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(160)))
    return img


BG = None
SUNSET = None


def sunset_strip(width, opacity=1.0):
    s = SUNSET.resize((width, int(SUNSET.height * width / SUNSET.width)), Image.LANCZOS)
    if opacity < 1:
        a = np.asarray(s).copy()
        a[..., 3] = (a[..., 3] * opacity).astype(np.uint8)
        s = Image.fromarray(a, "RGBA")
    return s


# ---------------------------------------------------------------- text

def glow_text(text, size, color=TEXT, glow=MAGENTA, weight="Bold", glow_radius=14, tracking=0):
    f = font(size, weight)
    box = f.getbbox(text)
    w, h = box[2] - box[0] + tracking * len(text) + 80, box[3] - box[1] + 80
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)

    def draw(dd, fill):
        if tracking:
            x = 40 - box[0]
            for ch in text:
                dd.text((x, 40 - box[1]), ch, font=f, fill=fill)
                x += f.getlength(ch) + tracking
        else:
            dd.text((40 - box[0], 40 - box[1]), text, font=f, fill=fill)

    halo = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    draw(ImageDraw.Draw(halo), glow + (230,))
    layer.alpha_composite(halo.filter(ImageFilter.GaussianBlur(glow_radius)))
    draw(d, color + (255,))
    return layer


def with_alpha(img, alpha):
    if alpha >= 1:
        return img
    a = np.asarray(img).copy()
    a[..., 3] = (a[..., 3].astype(np.float32) * clamp01(alpha)).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def paste_center(canvas, layer, cx, cy, alpha=1.0):
    layer = with_alpha(layer, alpha)
    canvas.alpha_composite(layer, (int(cx - layer.width / 2), int(cy - layer.height / 2)))


# ---------------------------------------------------------------- scenes

class Shot:
    """An app screenshot with a neon glow, a caption and a slow push-in."""

    def __init__(self, frames, kicker, caption, focus=(0.5, 0.5), zoom=(1.0, 1.06), sub=None):
        self.frames = frames          # list of (image, start_fraction)
        self.focus = focus
        self.zoom = zoom
        self.kicker = glow_text(kicker, 26, color=CYAN, glow=CYAN, glow_radius=8, tracking=6)
        self.caption = glow_text(caption, 54)
        self.sub = glow_text(sub, 28, color=LAVENDER, glow=VIOLET, weight="Medium", glow_radius=6) if sub else None

    def render(self, t):
        canvas = BG.copy()
        # pick the screenshot(s) for this moment, with a short crossfade between them
        img = self.frames[0][0]
        for i, (frame, start) in enumerate(self.frames):
            if t >= start:
                img = frame
                if i > 0 and t < start + 0.06:
                    img = Image.blend(self.frames[i - 1][0], frame, (t - start) / 0.06)
        scale = 1.12 * (self.zoom[0] + (self.zoom[1] - self.zoom[0]) * ease_in_out(t))
        w, h = int(img.width * scale), int(img.height * scale)
        shot = img.resize((w, h), Image.LANCZOS)
        # window box is 1364x880 at scale 1; centre it, shifting toward the focus as we zoom
        base_w, base_h = int(img.width * 1.12), int(img.height * 1.12)
        cx, cy = W / 2, 40 + base_h / 2
        x = cx - base_w / 2 - (w - base_w) * self.focus[0]
        y = cy - base_h / 2 - (h - base_h) * self.focus[1]
        # clip the zoomed screenshot to the base window box so it reads as a camera push
        box = (int(cx - base_w / 2), int(cy - base_h / 2), int(cx + base_w / 2), int(cy + base_h / 2))
        glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(glow).rounded_rectangle([box[0] - 6, box[1] - 6, box[2] + 6, box[3] + 6], 26, fill=MAGENTA + (150,))
        canvas.alpha_composite(glow.filter(ImageFilter.GaussianBlur(28)))
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        layer.alpha_composite(shot, (int(x), int(y)))
        mask = Image.new("L", (W, H), 0)
        ImageDraw.Draw(mask).rounded_rectangle(box, 22, fill=255)
        canvas.paste(layer, (0, 0), Image.fromarray(np.minimum(np.asarray(mask), np.asarray(layer)[..., 3]), "L"))
        ImageDraw.Draw(canvas).rounded_rectangle(box, 22, outline=(108, 59, 255, 200), width=2)

        # caption band
        a = ease_out(clamp01(t / 0.18))
        rise = (1 - a) * 24
        paste_center(canvas, self.kicker, W / 2, 944 + rise, a)
        paste_center(canvas, self.caption, W / 2, 992 + rise, a)
        if self.sub:
            paste_center(canvas, self.sub, W / 2, 1040 + rise, a * 0.95)
        return canvas


class Title:
    def __init__(self):
        self.logo = asset("Logo")
        self.reaper = asset("ReaperStanding")
        self.tagline = glow_text("See what's filling your disk. Reap it in one go.", 40, color=LAVENDER, glow=VIOLET, weight="Medium", glow_radius=8)
        self.sunset = sunset_strip(1700, 0.95)

    def render(self, t):
        canvas = BG.copy()
        sy = H - self.sunset.height + 40 + (1 - ease_out(clamp01(t / 0.35))) * 120
        canvas.alpha_composite(with_alpha(self.sunset, ease_out(clamp01(t / 0.3))), ((W - self.sunset.width) // 2, int(sy)))
        k = back_out(clamp01((t - 0.08) / 0.3))
        if k > 0.01:
            lw = int(1000 * max(k, 0.05))
            logo = self.logo.resize((lw, int(self.logo.height * lw / self.logo.width)), Image.LANCZOS)
            paste_center(canvas, logo, W / 2 + 230, 400, clamp01(k))
        r = ease_out(clamp01((t - 0.2) / 0.3))
        rh = 470
        reaper = self.reaper.resize((int(self.reaper.width * rh / self.reaper.height), rh), Image.LANCZOS)
        bob = math.sin(t * 3 * math.pi * 2) * 8
        canvas.alpha_composite(with_alpha(reaper, r), (int(110 - (1 - r) * 300), int(180 + bob)))
        paste_center(canvas, self.tagline, W / 2 + 230, 560, ease_out(clamp01((t - 0.45) / 0.25)))
        return canvas


class EndCard:
    def __init__(self):
        icon = Image.open(os.path.join(ASSETS, "AppIcon.appiconset", "icon_512x512@2x.png")).convert("RGBA")
        self.icon = icon.resize((330, 330), Image.LANCZOS)
        logo = asset("Logo")
        self.logo = logo.resize((820, int(logo.height * 820 / logo.width)), Image.LANCZOS)
        self.line1 = glow_text("Free & open source  ·  MIT", 40, color=TEXT, glow=VIOLET, weight="Semibold", glow_radius=8)
        self.line2 = glow_text("github.com/Pau-mbox/BitReaper", 34, color=CYAN, glow=CYAN, weight="Medium", glow_radius=8)
        self.sunset = sunset_strip(1900, 0.6)

    def render(self, t):
        canvas = BG.copy()
        canvas.alpha_composite(self.sunset, ((W - self.sunset.width) // 2, H - self.sunset.height + 60))
        k = back_out(clamp01(t / 0.35))
        size = int(330 * max(k, 0.05))
        icon = self.icon.resize((size, size), Image.LANCZOS)
        halo = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        ImageDraw.Draw(halo).rounded_rectangle([W / 2 - 170, 120, W / 2 + 170, 460], 70, fill=MAGENTA + (int(110 * clamp01(k)),))
        canvas.alpha_composite(halo.filter(ImageFilter.GaussianBlur(40)))
        paste_center(canvas, icon, W / 2, 290, clamp01(k))
        a = ease_out(clamp01((t - 0.2) / 0.3))
        paste_center(canvas, self.logo, W / 2, 560 + (1 - a) * 20, a)
        b = ease_out(clamp01((t - 0.35) / 0.3))
        paste_center(canvas, self.line1, W / 2, 690 + (1 - b) * 20, b)
        paste_center(canvas, self.line2, W / 2, 750 + (1 - b) * 20, b)
        return canvas


# ---------------------------------------------------------------- timeline

def main():
    global BG, SUNSET
    frames_dir, out_mp4 = sys.argv[1], sys.argv[2]
    out_gif = sys.argv[3] if len(sys.argv) > 3 else None
    BG = make_background()
    SUNSET = asset("SunsetBanner")

    def shot(name):
        return Image.open(os.path.join(frames_dir, f"{name}.png")).convert("RGBA")

    # (scene, start seconds, duration seconds)
    timeline = [
        (Title(), 0.0, 3.0),
        (Shot([(shot("home"), 0)], "01 · PICK A TARGET", "Every disk and folder, one click away",
              focus=(0.5, 0.55)), 3.0, 2.6),
        (Shot([(shot("scan1"), 0), (shot("scan2"), 0.5)], "02 · SCAN", "A million files in seconds",
              sub="Physical sizes, hard links counted once", focus=(0.5, 0.4), zoom=(1.0, 1.08)), 5.6, 2.4),
        (Shot([(shot("tree"), 0)], "03 · EXPLORE", "A fast tree of everything on your disk",
              focus=(0.35, 0.3), zoom=(1.0, 1.10)), 8.0, 2.8),
        (Shot([(shot("collect1"), 0), (shot("collect2"), 0.33), (shot("collector"), 0.66)], "04 · COLLECT",
              "Hit Delete or click + to collect", sub="Nothing is touched until you hit Delete",
              focus=(0.4, 0.75), zoom=(1.0, 1.06)), 10.8, 3.2),
        (Shot([(shot("largest"), 0)], "05 · HUNT", "Largest files, instantly",
              focus=(0.3, 0.3), zoom=(1.0, 1.08)), 14.0, 2.0),
        (Shot([(shot("sheet"), 0), (shot("countdown5"), 0.45), (shot("countdown4"), 0.75)], "06 · REAP",
              "Delete it all at once", sub="Trash or permanent · safety countdown · Put Back",
              focus=(0.5, 0.85), zoom=(1.0, 1.05)), 16.0, 2.2),
        (EndCard(), 18.2, 1.8),
    ]
    fade = 0.35
    total = int(20 * FPS)

    ffmpeg = subprocess.Popen([
        "ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}",
        "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
        "-movflags", "+faststart", out_mp4,
    ], stdin=subprocess.PIPE)

    for i in range(total):
        sec = i / FPS
        active = [(s, start, dur) for s, start, dur in timeline if start - fade <= sec < start + dur]
        frame = None
        for scene, start, dur in active:
            local = clamp01((sec - start) / dur)
            img = scene.render(local).convert("RGB")
            if frame is None:
                frame = img
            else:
                frame = Image.blend(frame, img, ease_in_out((sec - (start - fade)) / fade))
        if frame is None:
            frame = BG.convert("RGB")
        # fade in from / out to black
        k = min(clamp01(sec / 0.4), clamp01((20 - sec) / 0.5))
        if k < 1:
            frame = Image.blend(Image.new("RGB", (W, H), (0, 0, 0)), frame, k)
        ffmpeg.stdin.write(frame.tobytes())
        if i % 60 == 0:
            print(f"frame {i}/{total}", flush=True)
    ffmpeg.stdin.close()
    ffmpeg.wait()

    if out_gif:
        palette = out_gif + ".palette.png"
        filters = "fps=12,scale=800:-1:flags=lanczos"
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", out_mp4, "-vf", f"{filters},palettegen=stats_mode=diff", palette], check=True)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", out_mp4, "-i", palette, "-lavfi",
                        f"{filters}[x];[x][1:v]paletteuse=dither=sierra2_4a:diff_mode=rectangle", out_gif], check=True)
        os.remove(palette)
    print("done")


if __name__ == "__main__":
    main()
