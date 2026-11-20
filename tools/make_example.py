"""Regenerate the bundled north-up calibration clip with Pillow and NumPy."""
import io
import math
import struct
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"
WIDTH, HEIGHT, FPS = 640, 360, 12


def element(tag, data):
    code = bytes.fromhex(tag)
    length = len(data)
    size = next(n for n in range(1, 9) if length < (1 << (7 * n)) - 1)
    return code + (length | (1 << (7 * size))).to_bytes(size, "big") + data


def integer(tag, value):
    return element(tag, value.to_bytes(max(1, (value.bit_length() + 7) // 8), "big"))


def world_map():
    rng = np.random.default_rng(81)
    y, x = np.mgrid[0:400, 0:440]
    h = 0.48 * np.sin(x / 27 + np.sin(y / 23)) + 0.32 * np.cos(y / 15 - x / 47)
    h += 0.17 * np.sin(x / 9 + y / 17) + 0.12 * np.cos(y / 5 - x / 12)
    palette = np.array([[41, 93, 103], [63, 119, 123], [138, 162, 122], [153, 169, 121],
                        [124, 153, 102], [115, 145, 102], [172, 177, 124], [188, 182, 138]])
    colors = palette[np.clip(((h + 1.13) * 3.6).astype(int), 0, len(palette) - 1)]
    contours = np.minimum(np.mod(h * 6, 1), 1 - np.mod(h * 6, 1)) < .073
    colors[contours] = colors[contours] * .76
    colors += rng.integers(-3, 4, (400, 440, 1))
    terrain = Image.fromarray(colors.clip(0, 255).astype("uint8"))
    draw = ImageDraw.Draw(terrain)
    for _ in range(210):
        a, b = int(rng.integers(4, 434)), int(rng.integers(4, 394))
        draw.ellipse((a, b, a + 2, b + 2), fill=(100, 119, 83))
    return terrain


def route(t):
    # Coordinates increase east and south in the bitmap.
    east = min(t, 3.5) * 17
    north = max(0, min(t - 3.5, 3.5)) * 17
    diagonal = max(0, min(t - 9, 3.5)) * 11
    label = "向东" if t < 3.5 else "向北" if t < 7 else "静止" if t < 9 or t >= 12.5 else "向西南"
    return round(180 + east - diagonal), round(215 - north + diagonal), label


def make_clip():
    terrain = world_map()
    font_file = Path("C:/Windows/Fonts/msyh.ttc")
    font = lambda size: ImageFont.truetype(str(font_file), size)
    title, label_font, small = font(26), font(22), font(14)
    base = Image.new("RGB", (WIDTH, HEIGHT), "#18392f")
    draw = ImageDraw.Draw(base)
    draw.text((167, 31), "小地图方向校准", font=title, fill="#f2f7e9")
    draw.text((168, 77), "固定朝北  ·  路线回放", font=small, fill="#b2cec1")
    draw.rounded_rectangle((27, 151, 613, 329), radius=12, fill="#214637", outline="#43705a")
    tx, ty = 86, 60
    to_screen = lambda x, y: (tx + x, ty + y)
    stops = [route(t)[:2] for t in (0, 3.5, 7, 12.5)]
    draw.line([to_screen(*p) for p in stops], fill="#8bb59b", width=3)
    for x, y in stops:
        px, py = to_screen(x, y)
        draw.ellipse((px - 4, py - 4, px + 4, py + 4), fill="#f1dfad")
    draw.text((53, 173), "示例路线", font=small, fill="#c1d7c8")
    draw.text((442, 180), "预期方向", font=small, fill="#c1d7c8")
    mask = Image.new("L", (100, 100))
    ImageDraw.Draw(mask).ellipse((0, 0, 99, 99), fill=255)
    frames = []
    for i in range(14 * FPS):
        x, y, label = route(i / FPS)
        frame = base.copy()
        draw = ImageDraw.Draw(frame)
        draw.ellipse((26, 15, 130, 119), fill="#e6e3c6")
        frame.paste(terrain.crop((x - 50, y - 50, x + 50, y + 50)), (28, 17), mask)
        draw.polygon([(78, 60), (73, 72), (78, 69), (83, 72)], fill="#e1ffff", outline="#245858")
        draw.text((72, 0), "N", font=small, fill="white")
        px, py = to_screen(x, y)
        draw.ellipse((px - 6, py - 6, px + 6, py + 6), fill="#84ebbc", outline="white", width=2)
        draw.text((442, 213), label, font=label_font, fill="#a5f2cf")
        webp = io.BytesIO()
        frame.save(webp, "WEBP", quality=72, method=3)
        raw = webp.getvalue()
        if raw[12:16] != b"VP8 ":
            raise ValueError("Expected an intra-coded VP8 frame")
        frames.append(raw[20:20 + int.from_bytes(raw[16:20], "little")])
    header = element("1a45dfa3", integer("4286", 1) + integer("42f7", 1) + integer("42f2", 4)
                     + integer("42f3", 8) + element("4282", b"webm") + integer("4287", 2) + integer("4285", 2))
    info = element("1549a966", integer("2ad7b1", 1_000_000) + element("4d80", b"TeyvatGuide")
                   + element("5741", b"TeyvatGuide") + element("4489", struct.pack(">d", len(frames) * 1000 / FPS)))
    track = element("1654ae6b", element("ae", integer("d7", 1) + integer("73c5", 1) + integer("83", 1)
                    + element("86", b"V_VP8") + integer("23e383", round(1e9 / FPS))
                    + element("e0", integer("b0", WIDTH) + integer("ba", HEIGHT))))
    segment = bytearray(info + track)
    cues = bytearray()
    for first in range(0, len(frames), 2 * FPS):
        start = round(first * 1000 / FPS)
        blocks = bytearray(integer("e7", start))
        cues += element("bb", integer("b3", start) + element("b7", integer("f7", 1) + integer("f1", len(segment))))
        for j, vp8 in enumerate(frames[first:first + 2 * FPS], first):
            blocks += element("a3", b"\x81" + struct.pack(">hB", round(j * 1000 / FPS) - start, 0x80) + vp8)
        segment += element("1f43b675", blocks)
    segment += element("1c53bb6b", cues)
    return header + element("18538067", segment)


if __name__ == "__main__":
    ASSETS.mkdir(exist_ok=True)
    (ASSETS / "example.webm").write_bytes(make_clip())
    icon = Image.new("RGBA", (256, 256))
    d = ImageDraw.Draw(icon)
    d.rounded_rectangle((7, 7, 249, 249), 52, fill="#165d49")
    d.polygon([(175, 38), (163, 211), (120, 159), (64, 158)], fill="#eaf2d9")
    icon.save(ASSETS / "guide.ico", sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
