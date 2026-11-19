"""Extend the visual kit without modifying the three approved sample exports."""

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from extract_sample_assets import digest, extract


ROOT = Path(__file__).resolve().parents[1]
PUBLIC = ROOT / "frontend" / "siculingo-web" / "public" / "assets"
RECIPES = (
    {"id": "logo", "source": "asset-pack-v1.png", "crop": [470, 872, 748, 953],
     "canvas": [304, 112], "file": "brand\\siculingo-logo.png", "minComponentSize": 6, "edgeKernel": 3},
    {"id": "ficodi-happy", "source": "brand-board.png", "crop": [562, 344, 719, 486],
     "canvas": [176, 160], "file": "mascot\\ficodi-happy.png"},
    {"id": "ficodi-proud", "source": "brand-board.png", "crop": [882, 533, 1031, 683],
     "canvas": [176, 176], "file": "mascot\\ficodi-proud.png"},
    {"id": "ficodi-victory", "source": "brand-board.png", "crop": [972, 768, 1158, 956],
     "canvas": [224, 224], "file": "mascot\\ficodi-victory.png"},
    {"id": "home", "crop": [32, 935, 96, 993], "file": "icons\\home.png"},
    {"id": "book", "crop": [109, 936, 177, 992], "file": "icons\\book.png", "edgeKernel": 3},
    {"id": "practice", "crop": [201, 935, 263, 993], "file": "icons\\practice.png"},
    {"id": "flame", "crop": [457, 934, 511, 992], "file": "icons\\flame.png"},
    {"id": "heart", "crop": [527, 935, 584, 993], "file": "icons\\heart.png"},
    {"id": "friends", "crop": [703, 935, 760, 992], "file": "icons\\friends.png"},
    {"id": "badge-shield", "crop": [866, 938, 933, 1007], "file": "badges\\shield.png",
     "canvas": [96, 96]},
    {"id": "badge-star", "crop": [943, 938, 1001, 1003], "file": "badges\\star.png",
     "canvas": [96, 96]},
    {"id": "badge-check", "crop": [1148, 938, 1204, 998], "file": "badges\\check.png",
     "canvas": [96, 96]},
    {"id": "badge-xp", "crop": [1206, 937, 1270, 1003], "file": "badges\\xp.png",
     "canvas": [96, 96]},
    {"id": "goal", "source": "asset-pack-v1.png", "crop": [1070, 865, 1124, 921],
     "file": "icons\\goal.png", "edgeKernel": 3},
)


def main():
    manifest_path = ROOT / "assets" / "kit-manifest.json"
    previous = {}
    if manifest_path.exists():
        old = json.loads(manifest_path.read_text(encoding="utf-8"))
        previous = {record["id"]: record for record in old["assets"]}
    records = []
    images = []
    for recipe in RECIPES:
        recipe = {"source": "character-design-kit.png", "canvas": [80, 80], **recipe}
        source = ROOT / "assets" / "source" / recipe["source"]
        with Image.open(source) as original:
            crop = original.crop(recipe["crop"])
        image, details = extract(crop, recipe)
        destination = PUBLIC / recipe["file"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        image.save(destination, optimize=True)
        alpha = np.asarray(image)[:, :, 3]
        if alpha.min() != 0 or alpha.max() != 255:
            raise ValueError(f"{recipe['id']}: missing transparency or opaque foreground")
        record = {
            "id": recipe["id"],
            "status": "awaiting-visual-approval",
            "source": recipe["source"],
            "sourceSha256": digest(source),
            "cropLTRBExclusive": recipe["crop"],
            "file": str(destination.relative_to(ROOT)),
            "canvasPixels": list(image.size),
            "suggestedCssPixelsAt2x": [value // 2 for value in image.size],
            "sha256": digest(destination),
            "processingOptions": {
                key: value for key, value in recipe.items()
                if key in ("minComponentSize", "edgeKernel")
            },
            **details,
        }
        old = previous.get(recipe["id"], {})
        if old.get("status") == "approved" and old.get("sha256") == record["sha256"]:
            record.update(status="approved", approvedOn=old["approvedOn"])
        records.append(record)
        images.append((recipe["id"], image))
        print(f"{recipe['id']}: {details['subjectPixels']} native pixels")

    fonts = []
    for name in ("nunito", "inter"):
        path = PUBLIC / "fonts" / f"{name}-variable.ttf"
        face = ImageFont.truetype(str(path), 20)
        license_path = PUBLIC / "fonts" / f"{name}-OFL.txt"
        license_text = license_path.read_text(encoding="utf-8")
        if "SIL OPEN FONT LICENSE" not in license_text:
            raise ValueError(f"Unrecognized font license: {name}")
        fonts.append({
            "family": face.getname()[0],
            "file": str(path.relative_to(ROOT)),
            "licenseFile": str(license_path.relative_to(ROOT)),
            "source": f"https://github.com/google/fonts/tree/main/ofl/{name}",
            "sha256": digest(path),
        })

    manifest_path.write_text(json.dumps({
        "schemaVersion": 1,
        "status": "awaiting-visual-approval",
        "approvedSamplesManifest": "assets\\manifest.json",
        "notes": "Native raster exports, no upscaling. New artwork is not yet approved.",
        "assets": records,
        "fonts": fonts,
    }, indent=2) + "\n", encoding="utf-8")

    sheet = Image.new("RGB", (1280, 120 + 240 * ((len(images) + 3) // 4)), "#F7F2FE")
    draw = ImageDraw.Draw(sheet)
    title = ImageFont.truetype("C:\\Windows\\Fonts\\segoeuib.ttf", 28)
    label = ImageFont.truetype("C:\\Windows\\Fonts\\segoeui.ttf", 17)
    draw.text((28, 20), "SICULINGO / ESTENSIONE KIT 01", font=title, fill="#36114F")
    draw.text((28, 65), "Nuovi PNG trasparenti - risoluzione nativa - da approvare", font=label, fill="#624D72")
    for index, (name, image) in enumerate(images):
        x, y = 16 + (index % 4) * 320, 112 + (index // 4) * 240
        for by in range(0, 194, 14):
            for bx in range(0, 304, 14):
                color = "#E8E1F0" if (bx // 14 + by // 14) % 2 else "#FFFFFF"
                draw.rectangle((x + bx, y + by, x + min(bx + 13, 303), y + min(by + 13, 193)), fill=color)
        preview = image.copy()
        preview.thumbnail((288, 180), Image.Resampling.LANCZOS)
        sheet.paste(preview, (x + (304 - preview.width) // 2, y + (194 - preview.height) // 2), preview)
        draw.text((x + 8, y + 199), name, font=label, fill="#36114F")
    sheet.save(ROOT / "assets" / "review" / "visual-kit-review.png")
    print(f"Exported {len(records)} new assets; approved samples unchanged.")


if __name__ == "__main__":
    main()
