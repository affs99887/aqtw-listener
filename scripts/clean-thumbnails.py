"""Remove the in-game name tag and corner icon baked into catalogue thumbnails.

The inventory crops carry the game's own name label at the top-left of the item
box and a small cube icon at the bottom-left. The overlay prints the name next to
the picture, so the label only repeats it at an unreadable size. Only light,
low-saturation pixels inside those two corners are replaced (OpenCV inpainting);
the item art and the rarity background are kept.

Requires NumPy, Pillow and opencv-python-headless. Each processed image records
its original SHA-256 in provenance.json, so a second run leaves it untouched.
"""
import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def light_mask(rgb: np.ndarray, alpha: np.ndarray, box) -> np.ndarray:
    """Neutral pixels clearly brighter than the tag's background. Downscaled tags are dim, so the
    threshold follows the background of the item box instead of a fixed level."""
    left, top, right, bottom = box
    lum = rgb @ np.array([.299, .587, .114])
    chroma = rgb.max(axis=2) - rgb.min(axis=2)
    corner = lum[top:top + max(1, (bottom - top) // 3), left:left + max(1, (right - left) // 2)]
    background = float(np.median(corner))
    return (lum >= max(95, background + 55)) & (chroma <= 70) & (alpha > 128)


def runs(row: np.ndarray):
    """(start, length) of each horizontal run of True pixels."""
    padded = np.concatenate([[False], row, [False]]).astype(np.int8)
    edges = np.flatnonzero(np.diff(padded))
    return list(zip(edges[::2], edges[1::2] - edges[::2]))


def glyph_count(name: str) -> float:
    """Advance of a name in square CJK glyph widths; Latin letters and digits are about half."""
    return sum(1.0 if ord(c) > 0x2E80 else .55 for c in name)


def label_band(text: np.ndarray, box, name: str):
    """Rows and columns of the name tag, or None when the top-left holds item art instead."""
    left, top, right, bottom = box
    width, height = right - left, bottom - top
    limit = left + int(width * .9)

    def frame_edge(y):
        return any(length >= width * .8 for _, length in runs(text[y, left:right]))

    window = max(6, int(height * .08))
    start = next((i for i in range(min(height, window + 3))
                  if not frame_edge(top + i) and text[top + i, left:limit].sum() >= 2), None)
    if start is None or start > window:
        return None
    # The glyphs' first rows fix the tag's width; art further right (a camera flash
    # below the tag, say) must not extend the band. A tag is one line of square
    # glyphs, so that width also bounds its height.
    first = text[top + start:top + start + 3, left:limit]
    xs = np.nonzero(first.any(axis=0))[0]
    x_end = left + xs[-1] + 4
    cap = int(round((xs[-1] - xs[0] + 1) / glyph_count(name) * 1.15)) + 2
    # Tag rows cross several glyphs, so they hold several short strokes. Item art
    # that starts right under the tag is one solid run per row; two such rows end
    # the band. So do two empty rows, or one near the expected glyph bottom.
    end, gap, solid, contact = start, 0, 0, False
    for i in range(start + 1, min(height, start + cap + 1)):
        pieces = runs(text[top + i, left:x_end])
        if not pieces or frame_edge(top + i):
            gap += 1
            if gap >= 2 or i - start >= cap * .7:
                break
            continue
        gap = 0
        if len(pieces) == 1 and pieces[0][1] >= 3 and i > start + 2:
            solid += 1
            if solid >= 2:
                contact = True
                break
            continue
        solid = 0
        end = i
    end = min(end, start + cap - 1)
    band_height = end - start + 1
    if band_height < 4 or band_height > height * .3:
        return None
    y0, y1 = top + start, top + end + 1
    xs = np.nonzero(text[y0:y1, left:x_end].any(axis=0))[0]
    if len(xs) == 0 or xs[0] > width * .15 + 2:
        return None
    x0, x1 = left + xs[0], left + xs[-1] + 1
    # The last glyph's right strokes may start below the rows that fixed x_end.
    # Only the upper rows count: art that meets the tag does so from below.
    columns = text[y0:y0 + max(2, int(band_height * .75)), :].any(axis=0)
    step = max(3, int(round(band_height * .25)))
    while x1 < right:
        ahead = np.nonzero(columns[x1:min(right, x1 + step)])[0]
        if len(ahead) == 0:
            break
        x1 += ahead[-1] + 1
    expected = glyph_count(name) * band_height
    if not (.3 * expected <= x1 - x0 <= 1.6 * expected + 6):
        return None
    return y0, y1, x0, x1, band_height, contact


def corner_icon(text: np.ndarray, box, size: int):
    """The small cube icon: a compact light blob about the tag's height, in the bottom-left corner."""
    left, top, right, bottom = box
    reach = int(size * 2.2) + 4
    region = text[max(top, bottom - reach):bottom, left:min(right, left + reach)].astype(np.uint8)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(region, connectivity=8)
    oy, ox = max(top, bottom - reach), left
    best = None
    for index in range(1, count):
        x, y, w, h, area = stats[index]
        near_corner = x <= size * .8 + 3 and (region.shape[0] - (y + h)) <= size * .8 + 3
        if near_corner and .45 * size <= max(w, h) <= 1.7 * size and area >= .2 * w * h:
            if best is None or area > best[1]:
                best = (index, area)
    if best is None:
        return None
    mask = np.zeros(text.shape, bool)
    mask[oy:oy + region.shape[0], ox:ox + region.shape[1]] = labels == best[0]
    return mask


def clean(path: Path, name: str):
    image = Image.open(path).convert("RGBA")
    data = np.asarray(image).astype(np.float64)
    rgb, alpha = data[..., :3], data[..., 3]
    box = Image.fromarray(alpha.astype(np.uint8)).point(lambda a: 255 if a > 8 else 0).getbbox()
    if box is None:
        return None, []
    text = light_mask(rgb, alpha, box)
    band = label_band(text, box, name)
    if band is None:
        return None, []
    y0, y1, x0, x1, height, contact = band
    left, top, right, bottom = box
    # Replace the whole tag, including the dim anti-aliased strokes and the black
    # outline and drop shadow the game draws around its text. When item art starts
    # right under the tag, stop at the last text row instead of eating into it.
    pad = max(2, int(round(height * .3)))
    below = max(1, int(round(height * .2)))
    shadow = max(1, int(round(height * .15)))
    rgb8 = np.asarray(image.convert("RGB")).copy()
    foreign = (rgb8 @ np.array([.299, .587, .114])) > lum_of(rgb8, box) + 35
    contact = contact or foreign[y1:y1 + below, max(left, x0 - pad):x1].any()
    r0 = max(top, y0 - min(pad, max(1, y0 - top - 1)))
    r1 = min(bottom, y1 + (0 if contact else below))
    # The drop shadow sits to the right of the last glyph, but art beside the tag
    # (a camera flash, say) stops the padding.
    c1 = x1
    for column in range(x1, min(right, x1 + pad + shadow)):
        if foreign[r0:r1, column].any():
            break
        c1 = column + 1
    boxes = [(r0, r1, max(left, x0 - pad), c1)]
    found = ["label"]
    icon = corner_icon(text, box, height)
    if icon is not None:
        # On downscaled catalogue images only the icon's lightest face registers, so
        # cover at least the icon's usual footprint in the corner.
        ys, xs = np.nonzero(icon)
        corner = int(round(height * 1.5))
        boxes.append((max(top, min(ys.min() - pad, bottom - corner) - 1), bottom,
                      left, min(right, max(xs.max() + 1 + pad, left + corner) + 1)))
        found.append("icon")
    for rect in boxes:
        patch_from_background(rgb8, text, rect, box, pad)
    if contact:
        # Art starts right under the tag: only darken-to-background the shadow row,
        # leaving the brighter art pixels there untouched.
        strip = (r1, min(bottom, r1 + shadow), max(left, x0 - pad), c1)
        before = rgb8.copy()
        patch_from_background(rgb8, text, strip, box, pad)
        lum = before[strip[0]:strip[1], strip[2]:strip[3]] @ np.array([.299, .587, .114])
        keep = lum >= lum_of(rgb8, box) - 20
        region = rgb8[strip[0]:strip[1], strip[2]:strip[3]]
        region[keep] = before[strip[0]:strip[1], strip[2]:strip[3]][keep]
    out = np.dstack([rgb8, np.asarray(image)[..., 3]])
    return Image.fromarray(out.astype(np.uint8), "RGBA"), found


def lum_of(rgb: np.ndarray, box) -> float:
    left, top, right, bottom = box
    corner = rgb[top:top + max(1, (bottom - top) // 3), left:left + max(1, (right - left) // 2)]
    return float(np.median(corner.reshape(-1, 3) @ np.array([.299, .587, .114])))


def patch_from_background(rgb: np.ndarray, text: np.ndarray, rect, box, pad: int):
    """Inpaint rect using only the rarity background around it. Art that touches the
    rectangle (a camera flash, a ring's diamond) is swapped for the background colour
    in a scratch copy first, so it cannot streak into the patch."""
    r0, r1, c0, c1 = rect
    left, top, right, bottom = box
    ring = pad * 2 + 2
    s0, s1, t0, t1 = max(top, r0 - ring), min(bottom, r1 + ring), max(left, c0 - ring), min(right, c1 + ring)
    scratch = rgb[s0:s1, t0:t1].astype(np.float64)
    inside = np.zeros(scratch.shape[:2], bool)
    inside[r0 - s0:r1 - s0, c0 - t0:c1 - t0] = True
    around = ~inside & ~text[s0:s1, t0:t1]
    if around.sum() < 8:
        around = ~inside
    median = np.median(scratch[around], axis=0)
    art = np.linalg.norm(scratch - median, axis=2) > 48
    scratch[art & ~inside] = median
    mask = inside.astype(np.uint8) * 255
    bgr = cv2.cvtColor(np.clip(scratch, 0, 255).astype(np.uint8), cv2.COLOR_RGB2BGR)
    repaired = cv2.cvtColor(cv2.inpaint(bgr, mask, 3, cv2.INPAINT_TELEA), cv2.COLOR_BGR2RGB)
    rgb[r0:r1, c0:c1] = repaired[r0 - s0:r1 - s0, c0 - t0:c1 - t0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library", type=Path, help="library folder holding library.json and provenance.json")
    parser.add_argument("--preview", type=Path, help="write cleaned copies here instead of replacing the library images")
    args = parser.parse_args()
    library = json.loads((args.library / "library.json").read_text(encoding="utf-8"))
    provenance_path = args.library / "provenance.json"
    provenance = json.loads(provenance_path.read_text(encoding="utf-8"))
    entries = {entry["thumbnail"]: entry for entry in provenance["entries"]}
    report = []
    for item in library["items"]:
        relative = item.get("thumbnail")
        if not relative:
            continue
        path = args.library / relative
        entry = entries.get(relative)
        if entry is not None and entry.get("originalThumbnailSha256"):
            report.append((item["name"], "already cleaned"))
            continue
        cleaned, found = clean(path, item["name"])
        if cleaned is None:
            report.append((item["name"], "no tag found"))
            continue
        target = (args.preview / relative) if args.preview else path
        target.parent.mkdir(parents=True, exist_ok=True)
        before = sha(path)
        cleaned.save(target, optimize=True)
        if not args.preview and entry is not None:
            entry["originalThumbnailSha256"] = entry.get("thumbnailSha256", before)
            entry["thumbnailSha256"] = sha(target)
            entry["thumbnailEdit"] = "in-game " + " and ".join(found) + " removed by inpainting (scripts/clean-thumbnails.py)"
        report.append((item["name"], "+".join(found)))
    if not args.preview:
        provenance_path.write_text(json.dumps(provenance, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    for name, state in report:
        print(f"{name}\t{state}")
    print(f"cleaned {sum(1 for _, s in report if s not in ('no tag found', 'already cleaned'))} of {len(report)}")


if __name__ == "__main__":
    main()
