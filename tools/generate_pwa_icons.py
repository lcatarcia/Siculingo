"""Generate installable PWA icons from the approved ficodi-front.png asset without modifying source files."""
from __future__ import annotations

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
SOURCE_MASCOT = ROOT / "frontend" / "siculingo-web" / "public" / "assets" / "mascot" / "ficodi-front.png"
OUTPUT_DIR = ROOT / "frontend" / "siculingo-web" / "public" / "assets" / "pwa"

BRAND_PURPLE = (106, 0, 244, 255)      # #6A00F4
BRAND_CREAM = (255, 248, 240, 255)     # #FFF8F0
BRAND_GOLD = (255, 199, 0, 255)        # #FFC700


def trim_alpha(image: Image.Image) -> Image.Image:
    rgba = image.convert("RGBA")
    bbox = rgba.getbbox()
    return rgba.crop(bbox) if bbox else rgba


def compose_standard_icon(mascot: Image.Image, size: int) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    scale = 4
    hi = size * scale
    bg = Image.new("RGBA", (hi, hi), (0, 0, 0, 0))
    draw = ImageDraw.Draw(bg)
    pad = max(scale * 2, int(hi * 0.03))
    radius = int(hi * 0.22)
    draw.rounded_rectangle((pad, pad, hi - pad - 1, hi - pad - 1), radius=radius, fill=BRAND_PURPLE)

    inner_pad = int(hi * 0.085)
    inner_radius = int(hi * 0.17)
    draw.rounded_rectangle(
        (inner_pad, inner_pad, hi - inner_pad - 1, hi - inner_pad - 1),
        radius=inner_radius,
        fill=BRAND_CREAM,
    )
    canvas = bg.resize((size, size), Image.Resampling.LANCZOS)

    # Fit mascot comfortably inside the cream badge
    max_box = int(size * 0.74)
    ratio = min(max_box / mascot.width, max_box / mascot.height)
    new_w = max(1, round(mascot.width * ratio))
    new_h = max(1, round(mascot.height * ratio))
    resized = mascot.resize((new_w, new_h), Image.Resampling.LANCZOS)
    x = (size - new_w) // 2
    y = (size - new_h) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas


def compose_maskable_icon(mascot: Image.Image, size: int) -> Image.Image:
    # Full-bleed background; keep mascot strictly inside central 72% (< 80% W3C safe zone)
    scale = 2
    hi = size * scale
    bg = Image.new("RGBA", (hi, hi), BRAND_PURPLE)
    draw = ImageDraw.Draw(bg)
    disc_margin = int(hi * 0.12)
    draw.ellipse(
        (disc_margin, disc_margin, hi - disc_margin - 1, hi - disc_margin - 1),
        fill=BRAND_CREAM,
        outline=BRAND_GOLD,
        width=max(4, int(hi * 0.02)),
    )
    canvas = bg.resize((size, size), Image.Resampling.LANCZOS)

    safe_box = int(size * 0.64)
    ratio = min(safe_box / mascot.width, safe_box / mascot.height)
    new_w = max(1, round(mascot.width * ratio))
    new_h = max(1, round(mascot.height * ratio))
    resized = mascot.resize((new_w, new_h), Image.Resampling.LANCZOS)
    x = (size - new_w) // 2
    y = (size - new_h) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas


def compose_apple_touch_icon(mascot: Image.Image, size: int = 180) -> Image.Image:
    # Opaque square for iOS home screen
    canvas = Image.new("RGBA", (size, size), BRAND_CREAM)
    scale = 4
    hi = size * scale
    ring_layer = Image.new("RGBA", (hi, hi), (0, 0, 0, 0))
    draw = ImageDraw.Draw(ring_layer)
    border = int(hi * 0.035)
    draw.rectangle((0, 0, hi - 1, hi - 1), outline=BRAND_PURPLE, width=border)
    canvas.alpha_composite(ring_layer.resize((size, size), Image.Resampling.LANCZOS))

    max_box = int(size * 0.78)
    ratio = min(max_box / mascot.width, max_box / mascot.height)
    new_w = max(1, round(mascot.width * ratio))
    new_h = max(1, round(mascot.height * ratio))
    resized = mascot.resize((new_w, new_h), Image.Resampling.LANCZOS)
    x = (size - new_w) // 2
    y = (size - new_h) // 2
    canvas.alpha_composite(resized, (x, y))
    return canvas.convert("RGB")


def compose_favicon(mascot: Image.Image, size: int = 32) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    max_box = int(size * 0.94)
    ratio = min(max_box / mascot.width, max_box / mascot.height)
    new_w = max(1, round(mascot.width * ratio))
    new_h = max(1, round(mascot.height * ratio))
    resized = mascot.resize((new_w, new_h), Image.Resampling.LANCZOS)
    canvas.alpha_composite(resized, ((size - new_w) // 2, (size - new_h) // 2))
    return canvas


def main() -> None:
    if not SOURCE_MASCOT.exists():
        raise FileNotFoundError(f"Missing approved mascot asset: {SOURCE_MASCOT}")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    mascot = trim_alpha(Image.open(SOURCE_MASCOT))

    compose_standard_icon(mascot, 192).save(OUTPUT_DIR / "icon-192.png", optimize=True)
    compose_standard_icon(mascot, 512).save(OUTPUT_DIR / "icon-512.png", optimize=True)
    compose_maskable_icon(mascot, 512).save(OUTPUT_DIR / "icon-maskable-512.png", optimize=True)
    compose_apple_touch_icon(mascot, 180).save(OUTPUT_DIR / "apple-touch-icon.png", optimize=True)
    compose_favicon(mascot, 32).save(OUTPUT_DIR / "favicon-32.png", optimize=True)
    print(f"Generated 5 PWA icons in {OUTPUT_DIR} from {SOURCE_MASCOT.name} ({mascot.width}x{mascot.height}).")


if __name__ == "__main__":
    main()
