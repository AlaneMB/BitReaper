"""Builds Windows/BitReaper/Assets/BitReaper.ico from the macOS app icon PNGs (no dependencies).

Every image is stored as PNG inside the .ico, which Windows Vista and later read natively.

    python Windows/Scripts/make_icon.py
"""
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ICONSET = ROOT / "Resources" / "Assets.xcassets" / "AppIcon.appiconset"
OUTPUT = ROOT / "Windows" / "BitReaper" / "Assets" / "BitReaper.ico"

# (pixel size, source file)
SOURCES = [
    (16, "icon_16x16.png"),
    (32, "icon_32x32.png"),
    (64, "icon_32x32@2x.png"),
    (128, "icon_128x128.png"),
    (256, "icon_256x256.png"),
]


def png_size(data: bytes) -> tuple[int, int]:
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")
    return struct.unpack(">II", data[16:24])


def main() -> None:
    images = []
    for size, name in SOURCES:
        data = (ICONSET / name).read_bytes()
        width, height = png_size(data)
        if (width, height) != (size, size):
            raise ValueError(f"{name} is {width}x{height}, expected {size}x{size}")
        images.append((size, data))

    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries = b""
    for size, data in images:
        dimension = 0 if size >= 256 else size  # 0 means 256
        entries += struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(data), offset)
        offset += len(data)

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_bytes(header + entries + b"".join(data for _, data in images))
    print(f"Wrote {OUTPUT.relative_to(ROOT)} ({OUTPUT.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
