"""Makes the program's icons from tools/icon-source.png (artwork on a white ground):

    agent-windows/app.ico                                   the computer's programs and the installer, 16 to 256 px
    android/res/drawable-*dpi/ic_launcher_foreground.png    the app's adaptive icon (on a white background)
    docs/images/logo.png                                    the README

The results are kept in the repository, so building needs nothing of this. Needs Pillow:

    python tools/make_icons.py
"""
import struct
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
DENSITIES = {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}


def artwork():
    """The artwork alone, square, on a transparent ground: the white is taken out of every pixel, edges included."""
    pixels = np.asarray(Image.open(ROOT / "tools" / "icon-source.png").convert("RGB")).astype(float)
    alpha = (255 - pixels).max(axis=2) / 255
    colour = np.where(alpha[..., None] > 0, (pixels - 255 * (1 - alpha[..., None])) / np.maximum(alpha[..., None], 1e-6), 0)
    alpha[alpha < 12 / 255] = 0          # the source's not quite white ground
    picture = Image.fromarray(np.dstack([colour.clip(0, 255), alpha * 255]).astype(np.uint8), "RGBA")
    left, top, right, bottom = picture.getbbox()
    side = max(right - left, bottom - top)
    square = Image.new("RGBA", (side, side))
    square.paste(picture.crop((left, top, right, bottom)), ((side - (right - left)) // 2, (side - (bottom - top)) // 2))
    return square


def tile(art, size, share=0.8):
    """The artwork on a white rounded square, as the icon is shown where there is no mask of the system's own."""
    big = size * 4
    picture = Image.new("RGBA", (big, big))
    ImageDraw.Draw(picture).rounded_rectangle((0, 0, big - 1, big - 1), radius=big * 0.22, fill="white")
    inner = round(big * share)
    picture.alpha_composite(art.resize((inner, inner), Image.LANCZOS), ((big - inner) // 2, (big - inner) // 2))
    return picture.resize((size, size), Image.LANCZOS)


def ico(pictures, path):
    """An icon file of 32-bit bitmaps. Pillow writes each size as PNG, which the .NET Framework's Icon cannot read."""
    frames = []
    for picture in pictures:
        side = picture.width
        pixels = np.asarray(picture.convert("RGBA"))[::-1, :, [2, 1, 0, 3]].tobytes()      # bottom-up BGRA
        mask = bytes(((side + 31) // 32) * 4 * side)                                     # transparency is in alpha
        frames.append((side, struct.pack("<IiiHHIIiiII", 40, side, side * 2, 1, 32, 0, len(pixels) + len(mask), 0, 0, 0, 0) + pixels + mask))
    offset = 6 + 16 * len(frames)
    head = struct.pack("<HHH", 0, 1, len(frames))
    for side, data in frames:
        head += struct.pack("<BBBBHHII", side % 256, side % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(head + b"".join(data for _, data in frames))


def main():
    art = artwork()
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    ico([tile(art, s) for s in sizes], ROOT / "agent-windows" / "app.ico")
    tile(art, 256).save(ROOT / "docs" / "images" / "logo.png", optimize=True)
    # Adaptive icons are 108 dp, of which the launcher shows the middle 72 at most and may cut it to a 66 dp circle.
    for name, scale in DENSITIES.items():
        side, inner = round(108 * scale), round(66 * scale)
        picture = Image.new("RGBA", (side, side))
        picture.alpha_composite(art.resize((inner, inner), Image.LANCZOS), ((side - inner) // 2, (side - inner) // 2))
        folder = ROOT / "android" / "res" / ("drawable-" + name)
        folder.mkdir(exist_ok=True)
        picture.save(folder / "ic_launcher_foreground.png", optimize=True)


if __name__ == "__main__":
    main()
