"""Draw the app mark: an inventory slot (chamfered square, the overlay's panel colours) holding a
sound waveform whose tallest bar is the 大红 red, flanked by gold and pale bars — "hear the red".
Every icon size is drawn from the shapes at that size (4x supersampled), not scaled from one
bitmap; at 24 px and below the waveform keeps three heavier bars so it stays crisp in the tray.

Writes src/Listener.App/Assets/listener-mark.png (1254 px), src/Listener.App/Assets/listener.ico
(16–256 px) and docs/design/brand/listener-mark-preview.png.
Usage: python scripts/draw-logo.py
"""
from __future__ import annotations

import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "src/Listener.App/Assets"
PANEL, EDGE = (27, 28, 28, 255), (201, 214, 211, 255)      # Theme.Panel, Theme.Accent
PALE, GOLD, RED = (201, 214, 211, 255), (231, 199, 128, 255), (240, 100, 91, 255)  # Accent, Gold, Hot


def chamfered(x0, y0, x1, y1, cut):
    """A square with the top-left and bottom-right corners cut, like the game's panels."""
    return [(x0 + cut, y0), (x1, y0), (x1, y1 - cut), (x1 - cut, y1), (x0, y1), (x0, y0 + cut)]


def bar(draw, cx, half_width, top, bottom, colour):
    cut = half_width * .55
    draw.polygon([(cx - half_width, top + cut), (cx - half_width + cut, top), (cx + half_width - cut, top), (cx + half_width, top + cut),
                  (cx + half_width, bottom - cut), (cx + half_width - cut, bottom), (cx - half_width + cut, bottom), (cx - half_width, bottom - cut)],
                 fill=colour)


def render(size: int) -> Image.Image:
    scale = 4
    s = size * scale
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    small = size <= 24
    margin = s * (.04 if small else .06)
    cut = s * .2
    border = max(scale * 1.0, s * (.07 if small else .045))
    d.polygon(chamfered(margin, margin, s - margin, s - margin, cut), fill=EDGE)
    inner = margin + border
    d.polygon(chamfered(inner, inner, s - inner, s - inner, cut - border * .42), fill=PANEL)
    middle, base = s / 2, s * .5
    if small:
        bars = [(-.22, .24, PALE if size <= 16 else GOLD), (0, .34, RED), (.22, .24, PALE if size <= 16 else GOLD)]
        half = s * .075
    else:
        bars = [(-.28, .12, PALE), (-.14, .22, GOLD), (0, .32, RED), (.14, .22, GOLD), (.28, .12, PALE)]
        half = s * .045
    for offset, height, colour in bars:
        bar(d, middle + offset * s, half, base - height * s, base + height * s, colour)
    return im.resize((size, size), Image.LANCZOS)


def write_ico(path: Path, sizes=(16, 20, 24, 32, 40, 48, 64, 128, 256)):
    frames = []
    for size in sizes:
        buffer = io.BytesIO(); render(size).save(buffer, "PNG"); frames.append(buffer.getvalue())
    header = struct.pack("<HHH", 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries = b""
    for size, data in zip(sizes, frames):
        entries += struct.pack("<BBBBHHII", 0 if size == 256 else size, 0 if size == 256 else size, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(header + entries + b"".join(frames))


def preview(path: Path):
    font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 18)
    sheet = Image.new("RGB", (1180, 400), (17, 18, 18))
    d = ImageDraw.Draw(sheet)
    sheet.paste(render(256), (24, 40), render(256))
    x = 310
    for size in (128, 64, 48, 32, 24, 20, 16):
        for row, bg in enumerate(((17, 18, 18), (238, 238, 238))):
            tile = Image.new("RGB", (size + 16, size + 16), bg); tile.paste(render(size), (8, 8), render(size))
            sheet.paste(tile, (x, 40 + row * 170))
        d.text((x, 16), f"{size}px", font=font, fill=(200, 200, 200))
        x += size + 40
    d.text((24, 365), "新图标：背包物品格 + 声波，正中最高为大红色（上排深色、下排浅色任务栏，各尺寸单独绘制）", font=font, fill=(200, 200, 200))
    sheet.save(path)


if __name__ == "__main__":
    render(1254).save(ASSETS / "listener-mark.png", optimize=True)
    write_ico(ASSETS / "listener.ico")
    (ROOT / "docs/design/brand").mkdir(parents=True, exist_ok=True)
    preview(ROOT / "docs/design/brand/listener-mark-preview.png")
    print("wrote", ASSETS / "listener-mark.png", ASSETS / "listener.ico")
